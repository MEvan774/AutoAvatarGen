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

        // Raw 16-bit PCM: nothing to decode, trivially sliceable, lossless
        // through the cut. Plan-gated — 44.1kHz PCM/WAV needs Pro tier or
        // above — so every request that asks for it must be able to fall back.
        public const string OutputFormatPcm44100 = "pcm_44100";
        public const string OutputFormatMp3      = "mp3_44100_128";

        // eleven_v3 takes three discrete stability values.
        public const float StabilityCreative = 0.0f;
        public const float StabilityNatural  = 0.5f;
        public const float StabilityRobust   = 1.0f;

        /// <summary>Nearest of the three values v3 accepts.</summary>
        public static float SnapStability(float v)
        {
            if (v < 0.25f) return StabilityCreative;
            return v < 0.75f ? StabilityNatural : StabilityRobust;
        }

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

            public VoiceSettings Clone() => (VoiceSettings)MemberwiseClone();
        }

        /// <summary>
        /// Everything one render needs. Used by the chunked pipeline, which
        /// sends the SAME seed on every chunk of a video so the takes sample
        /// from the same place — the other half (with the overlap prefix) of
        /// keeping one voice across a seam.
        /// </summary>
        public class TtsRequest
        {
            public string        Text;
            public string        VoiceId  = DefaultVoiceId;
            public string        ModelId  = DefaultModelId;
            public VoiceSettings Settings = new VoiceSettings();

            /// <summary>0–4294967295 per the API; kept inside int range here.
            /// Null omits it entirely (the old, unseeded behaviour).</summary>
            public int?   Seed;

            /// <summary>Null = the API default (mp3_44100_128).</summary>
            public string OutputFormat;

            /// <summary>Retry once as mp3 when the account's plan refuses the
            /// requested format.</summary>
            public bool   AllowOutputFormatFallback = true;
        }

        /// <summary>One TTS render — the audio bytes plus its alignment.</summary>
        public class TtsResult
        {
            public byte[] AudioBytes;
            public List<TtsScriptProcessor.WordTimestamp> WordTimestamps;

            /// <summary>Character-level alignment over the text that was sent.
            /// Null when the response carried none.</summary>
            public TtsAlignment Alignment;

            /// <summary>The format the audio actually came back in — not
            /// necessarily the one asked for (see AllowOutputFormatFallback).</summary>
            public string OutputFormat;

            /// <summary>ElevenLabs' request id, recorded in the render manifest
            /// so a bad take can be traced back.</summary>
            public string RequestId;
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
            return GenerateTts(
                new TtsRequest {
                    Text     = text,
                    VoiceId  = voiceId,
                    ModelId  = modelId,
                    Settings = settings,
                },
                apiKey, onProgress, onSuccess, onError);
        }

        /// <summary>
        /// As above, with seed / output-format control. When the requested
        /// output format is refused because of the account's plan (44.1kHz PCM
        /// needs Pro tier or above), the call retries once as mp3 rather than
        /// failing the run — the pipeline decodes either one.
        /// </summary>
        public static IEnumerator GenerateTts(
            TtsRequest req,
            string apiKey,
            Action<float>  onProgress,
            Action<TtsResult> onSuccess,
            Action<string> onError)
        {
            if (req == null || string.IsNullOrEmpty(req.Text))
            {
                onError?.Invoke("Empty text — nothing to send to ElevenLabs.");
                yield break;
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onError?.Invoke("No ElevenLabs API key set. Open the API Key popup first.");
                yield break;
            }

            string format = string.IsNullOrWhiteSpace(req.OutputFormat)
                ? OutputFormatMp3 : req.OutputFormat.Trim();
            byte[] bodyBytes = Encoding.UTF8.GetBytes(
                BuildRequestJson(req.Text, req.ModelId, req.Settings, req.Seed));
            bool fallbackUsed = false;

            while (true)
            {
                string url = $"https://api.elevenlabs.io/v1/text-to-speech/{req.VoiceId}" +
                             $"/with-timestamps?output_format={format}";

                string  failure    = null;   // set instead of yielding out of the using block
                bool    retryAsMp3 = false;
                TtsResult result   = null;

                using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
                {
                    request.uploadHandler   = new UploadHandlerRaw(bodyBytes);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("xi-api-key",   apiKey);
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Accept",       "application/json");
                    // A full chunk is minutes of audio, not one section — the
                    // old 120s ceiling would time out a healthy render.
                    request.timeout = 600;

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

                        if (!fallbackUsed && req.AllowOutputFormatFallback &&
                            format != OutputFormatMp3 &&
                            LooksLikeFormatRejection(request.responseCode, detail))
                        {
                            retryAsMp3 = true;
                            Debug.LogWarning(
                                $"[ElevenLabs] '{format}' was refused ({request.responseCode}) — " +
                                $"this usually means the plan doesn't include it (44.1kHz PCM/WAV " +
                                $"needs Pro tier or above). Retrying as {OutputFormatMp3}.\n" +
                                Truncate(detail, 300));
                        }
                        else
                        {
                            failure = $"ElevenLabs API error {request.responseCode}: " +
                                      $"{request.error}\n{Truncate(detail, 400)}";
                        }
                    }
                    else
                    {
                        onProgress?.Invoke(1f);
                        try
                        {
                            result = ParseResponse(request.downloadHandler.text);
                            result.OutputFormat = format;
                            result.RequestId    = request.GetResponseHeader("request-id");
                        }
                        catch (Exception e)
                        {
                            failure = $"Failed to parse ElevenLabs response: {e.Message}";
                        }
                    }
                }

                if (retryAsMp3)
                {
                    fallbackUsed = true;
                    format = OutputFormatMp3;
                    continue;
                }
                if (failure != null) { onError?.Invoke(failure); yield break; }

                onSuccess?.Invoke(result);
                yield break;
            }
        }

        // A refused output_format answers 4xx with a body naming the format or
        // the plan. Anything else (bad key, bad voice, server error) is a real
        // failure and must surface, not be papered over with a silent retry.
        static bool LooksLikeFormatRejection(long code, string body)
        {
            if (code < 400 || code >= 500) return false;
            if (string.IsNullOrEmpty(body)) return false;
            string b = body.ToLowerInvariant();
            return b.Contains("output_format")
                || b.Contains("output format")
                || b.Contains("tier")
                || b.Contains("subscription")
                || b.Contains("upgrade");
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
            Action<string> onError,
            string fileName = "segment.mp3",
            string mimeType = "audio/mpeg")
        {
            if (audioBytes == null || audioBytes.Length == 0 || string.IsNullOrEmpty(text))
            {
                onError?.Invoke("Nothing to align (no audio or empty text).");
                yield break;
            }

            var form = new List<IMultipartFormSection> {
                new MultipartFormFileSection("file", audioBytes, fileName, mimeType),
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

        // JsonUtility always emits every field, so "no seed" needs a payload
        // shape without one rather than a nullable. (Inherited fields are
        // serialised, so this stays in lockstep with RequestPayload.)
        [Serializable]
        private class SeededRequestPayload : RequestPayload
        {
            public int seed;
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
        // ElevenLabs returns. `alignment` indexes the text that was SENT;
        // `normalized_alignment` indexes ElevenLabs' normalised rewrite of it,
        // which is why only the former is read here — every character offset
        // the splitter holds is measured against the sent text.
        [Serializable]
        private class ResponsePayload
        {
            public string       audio_base64;
            public TtsAlignment alignment;
        }

        private static string BuildRequestJson(
            string text, string modelId, VoiceSettings settings, int? seed)
        {
            settings = settings ?? new VoiceSettings();
            if (!seed.HasValue)
            {
                return JsonUtility.ToJson(new RequestPayload {
                    text           = text,
                    model_id       = modelId,
                    voice_settings = settings,
                });
            }

            return JsonUtility.ToJson(new SeededRequestPayload {
                text           = text,
                model_id       = modelId,
                voice_settings = settings,
                seed           = Math.Max(0, seed.Value),
            });
        }

        private static TtsResult ParseResponse(string json)
        {
            var resp = JsonUtility.FromJson<ResponsePayload>(json);
            if (resp == null || string.IsNullOrEmpty(resp.audio_base64))
                throw new Exception("Response missing audio_base64");

            byte[] audio = Convert.FromBase64String(resp.audio_base64);
            var alignment = resp.alignment;

            return new TtsResult {
                AudioBytes     = audio,
                Alignment      = alignment != null && alignment.IsUsable ? alignment : null,
                WordTimestamps = alignment != null
                    ? alignment.ToWords()
                    : new List<TtsScriptProcessor.WordTimestamp>(),
            };
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }
    }
}
