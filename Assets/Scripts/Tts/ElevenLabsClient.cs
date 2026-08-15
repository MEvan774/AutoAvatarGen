using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MugsTech.Tts
{
    /// <summary>
    /// Thin coroutine wrapper around ElevenLabs's <c>with-timestamps</c> TTS
    /// endpoint. One method, one POST, one JSON parse — no global state.
    ///
    /// Used by <see cref="TtsGenerationJob"/> to render a single segment.
    /// The orchestrator computes overall progress by combining each segment's
    /// 0–1 upload+download progress with the segments-completed count.
    /// </summary>
    public static class ElevenLabsClient
    {
        public const string DefaultVoiceId = "3jR9BuQAOPMWUjWpi0ll";
        public const string DefaultModelId = "eleven_v3";

        /// <summary>
        /// Voice settings sent in the request body. Defaults mirror the
        /// Python script's VOICE_CONFIG.
        /// </summary>
        [Serializable]
        public class VoiceSettings
        {
            // eleven_v3 takes three discrete stability values:
            // 0.0 = Creative, 0.5 = Natural, 1.0 = Robust.
            public float stability         = 0.5f;
            public float similarity_boost  = 0.80f;
            public float style             = 0.35f;
            public bool  use_speaker_boost = true;
        }

        /// <summary>One TTS render — bytes for the mp3 plus the word alignment.</summary>
        public class TtsResult
        {
            public byte[] AudioBytes;
            public List<TtsScriptProcessor.WordTimestamp> WordTimestamps;
        }

        /// <summary>
        /// Coroutine: POST to the TTS endpoint, then decode the response.
        /// Invokes exactly one of <paramref name="onSuccess"/> /
        /// <paramref name="onError"/>. <paramref name="onProgress"/> fires
        /// repeatedly with a value in [0,1] while the request is in flight.
        /// </summary>
        public static IEnumerator GenerateTts(
            string text,
            string voiceId,
            string modelId,
            VoiceSettings settings,
            string apiKey,
            Action<float>  onProgress,
            Action<TtsResult> onSuccess,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onError?.Invoke("No ElevenLabs API key set. Open the API Key popup first.");
                yield break;
            }
            if (string.IsNullOrEmpty(text))
            {
                onError?.Invoke("Empty text — nothing to send to ElevenLabs.");
                yield break;
            }

            string url = $"https://api.elevenlabs.io/v1/text-to-speech/{voiceId}/with-timestamps";
            string body = BuildRequestJson(text, modelId, settings);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler   = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("xi-api-key",   apiKey);
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept",       "application/json");
                request.timeout = 120;

                var op = request.SendWebRequest();
                while (!op.isDone)
                {
                    // Upload runs first, then download. UnityWebRequest reports
                    // each independently — collapse to a single 0–1 by averaging.
                    float p = 0.5f * Mathf.Clamp01(request.uploadProgress)
                            + 0.5f * Mathf.Clamp01(request.downloadProgress);
                    onProgress?.Invoke(p);
                    yield return null;
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string detail = request.downloadHandler != null
                        ? request.downloadHandler.text : "";
                    onError?.Invoke($"ElevenLabs API error {request.responseCode}: {request.error}\n{Truncate(detail, 400)}");
                    yield break;
                }

                onProgress?.Invoke(1f);

                TtsResult result;
                try
                {
                    result = ParseResponse(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse ElevenLabs response: {e.Message}");
                    yield break;
                }
                onSuccess?.Invoke(result);
            }
        }

        /// <summary>
        /// Coroutine: POST the rendered audio + its transcript to
        /// <c>/v1/forced-alignment</c> and return word timings measured on the
        /// ACTUAL audio. eleven_v3's synthesis alignment routinely drifts off
        /// its own render (±1s mid-segment, up to ~2s at the tail on real
        /// generations), while forced alignment listens to the file it is
        /// given — so these timings are the ones markers should be mapped with.
        /// Requires the API key to carry the <c>forced_alignment</c> permission;
        /// without it the API answers 401 missing_permissions and the caller
        /// falls back to the synthesis alignment.
        /// </summary>
        public static IEnumerator GetForcedAlignment(
            byte[] audioBytes,
            string text,
            string apiKey,
            Action<List<TtsScriptProcessor.WordTimestamp>> onSuccess,
            Action<string> onError)
        {
            if (audioBytes == null || audioBytes.Length == 0 || string.IsNullOrEmpty(text))
            {
                onError?.Invoke("Nothing to align (no audio or empty text).");
                yield break;
            }

            var form = new List<IMultipartFormSection> {
                new MultipartFormFileSection("file", audioBytes, "segment.mp3", "audio/mpeg"),
                new MultipartFormDataSection("text", text),
            };

            using (var request = UnityWebRequest.Post(
                "https://api.elevenlabs.io/v1/forced-alignment", form))
            {
                request.SetRequestHeader("xi-api-key", apiKey);
                request.timeout = 120;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string detail = request.downloadHandler != null
                        ? request.downloadHandler.text : "";
                    onError?.Invoke($"Forced alignment error {request.responseCode}: " +
                                    $"{request.error}\n{Truncate(detail, 400)}");
                    yield break;
                }

                List<TtsScriptProcessor.WordTimestamp> words;
                try
                {
                    words = ParseForcedAlignment(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse forced-alignment response: {e.Message}");
                    yield break;
                }

                if (words.Count == 0)
                {
                    onError?.Invoke("Forced alignment returned no words.");
                    yield break;
                }
                onSuccess?.Invoke(words);
            }
        }

        // ---- request / response shapes -----------------------------------

        [Serializable]
        private class RequestPayload
        {
            public string text;
            public string model_id;
            public VoiceSettings voice_settings;
        }

        // /v1/forced-alignment response: { characters:[...], words:[{text,start,
        // end,loss}], loss } — only the words are consumed; JsonUtility skips
        // the unknown fields.
        [Serializable]
        private class ForcedAlignmentWord
        {
            public string text;
            public float  start;
            public float  end;
        }

        [Serializable]
        private class ForcedAlignmentResponse
        {
            public ForcedAlignmentWord[] words;
        }

        private static List<TtsScriptProcessor.WordTimestamp> ParseForcedAlignment(string json)
        {
            var resp = JsonUtility.FromJson<ForcedAlignmentResponse>(json);
            var words = new List<TtsScriptProcessor.WordTimestamp>();
            if (resp?.words == null) return words;

            foreach (var w in resp.words)
            {
                if (w == null || string.IsNullOrWhiteSpace(w.text)) continue;
                words.Add(new TtsScriptProcessor.WordTimestamp {
                    Word  = w.text.Trim(),
                    Start = w.start,
                    End   = w.end,
                });
            }
            return words;
        }

        // JsonUtility doesn't like top-level arrays-of-floats, but it's fine
        // when they're fields on a wrapper object — which is exactly the shape
        // ElevenLabs returns.
        [Serializable]
        private class ResponsePayload
        {
            public string audio_base64;
            public Alignment alignment;
        }

        [Serializable]
        private class Alignment
        {
            public string[] characters;
            public float[]  character_start_times_seconds;
            public float[]  character_end_times_seconds;
        }

        private static string BuildRequestJson(string text, string modelId, VoiceSettings settings)
        {
            var payload = new RequestPayload {
                text           = text,
                model_id       = modelId,
                voice_settings = settings ?? new VoiceSettings(),
            };
            return JsonUtility.ToJson(payload);
        }

        private static TtsResult ParseResponse(string json)
        {
            var resp = JsonUtility.FromJson<ResponsePayload>(json);
            if (resp == null || string.IsNullOrEmpty(resp.audio_base64))
                throw new Exception("Response missing audio_base64");

            byte[] audio = Convert.FromBase64String(resp.audio_base64);
            var alignment = resp.alignment ?? new Alignment();
            var words = TtsScriptProcessor.GroupCharsIntoWords(
                alignment.characters ?? Array.Empty<string>(),
                alignment.character_start_times_seconds ?? Array.Empty<float>(),
                alignment.character_end_times_seconds   ?? Array.Empty<float>());

            return new TtsResult { AudioBytes = audio, WordTimestamps = words };
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }
    }
}
