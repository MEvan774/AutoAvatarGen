using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class HybridAvatarSystem : MonoBehaviour
{
    [Header("Components")]
    public AudioSource voiceAudio;
    public SpriteRenderer avatarRenderer;

    public GameObject pivot;

    [Header("Emotion Sprites")]
    public Sprite neutralSprite;
    public Sprite excitedSprite;
    public Sprite seriousSprite;
    public Sprite sadSprite;
    public Sprite concernedSprite;

    [Header("Audio Settings")]
    [Range(0.01f, 0.1f)]
    public float silenceThreshold = 0.02f;

    [Range(0.1f, 0.5f)]
    public float minWordDuration = 0.15f;

    private float animationDuration = 0.15f;
    private float squashAmount = 1.6f; // How much to squash/stretch

    private Coroutine currentAnimation;

    private Dictionary<string, Sprite> emotionMap;
    private string cleanScript;
    private string[] words;
    private List<MarkerData> markers;
    private int currentWordIndex = 0;

    [Header("Recording")]
    public LinuxTransparentRecorder recorder;
    public bool autoRecord = true;

    void Awake()
    {
        // Setup emotion mapping
        emotionMap = new Dictionary<string, Sprite>()
        {
            {"Neutral", neutralSprite},
            {"Excited", excitedSprite},
            {"Serious", seriousSprite},
            {"Sad", sadSprite},
            {"Concerned", concernedSprite}
        };

        // Start with neutral
        avatarRenderer.sprite = neutralSprite;
    }

    // Call this with your script and pre-generated audio
    public void ProcessWithExistingAudio(string scriptWithMarkers, AudioClip audio)
    {
        (cleanScript, markers) = ParseScriptWithMarkers(scriptWithMarkers);
        words = cleanScript.Split(new char[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

        voiceAudio.clip = audio;

        // Start recording if enabled
        if (autoRecord && recorder != null)
        {
            recorder.StartRecordingWithAudio();
        }

        voiceAudio.Play();
        StartCoroutine(TrackSpeechHybrid());
    }

    IEnumerator TrackSpeechHybrid()
    {
        float[] samples = new float[1024];
        float lastAmplitude = 0f;
        float timeSinceLastWord = 0f;
        int wordsDetectedByAudio = 0;
        int lastTriggeredMarker = -1;

        while (voiceAudio.isPlaying)
        {
            // METHOD 1: Analyze audio amplitude for word detection
            voiceAudio.GetOutputData(samples, 0);
            float currentAmplitude = CalculateRMS(samples);

            timeSinceLastWord += Time.deltaTime;

            // Detect word boundary (silence to speech transition)
            if (currentAmplitude > silenceThreshold &&
                currentAmplitude > lastAmplitude * 1.3f &&
                timeSinceLastWord > minWordDuration)
            {
                wordsDetectedByAudio++;
                timeSinceLastWord = 0f;
            }

            // METHOD 2: Proportional progress as backup/correction
            float audioProgress = voiceAudio.time / voiceAudio.clip.length;
            int expectedWordIndex = Mathf.RoundToInt(audioProgress * words.Length);

            // Hybrid: Use average of both methods, weighted toward proportion
            currentWordIndex = Mathf.RoundToInt(
                (wordsDetectedByAudio * 0.3f) + (expectedWordIndex * 0.7f)
            );

            // Clamp to valid range
            currentWordIndex = Mathf.Clamp(currentWordIndex, 0, words.Length - 1);

            // Check if we should trigger an emotion change
            for (int i = lastTriggeredMarker + 1; i < markers.Count; i++)
            {
                if (currentWordIndex >= markers[i].wordIndex)
                {
                    Debug.Log($"Triggering emotion {markers[i].emotion} at word index {currentWordIndex}");
                    ChangeEmotion(markers[i].emotion);
                    lastTriggeredMarker = i;
                    Debug.Log($"Emotion changed to {markers[i].emotion} at word {currentWordIndex}");
                }
                else
                {
                    break; // Haven't reached this marker yet
                }
            }

            lastAmplitude = currentAmplitude;
            yield return null;
        }

        Debug.Log("Audio finished playing");
        recorder.StopRecording();
    }

    float CalculateRMS(float[] samples)
    {
        float sum = 0f;
        foreach (float sample in samples)
        {
            sum += sample * sample;
        }
        return Mathf.Sqrt(sum / samples.Length);
    }

    void ChangeEmotion(string emotion)
    {
        // Safety checks
        if (emotionMap == null)
        {
            Debug.LogError("emotionMap is null! Make sure Awake() ran before processing.");
            return;
        }

        if (string.IsNullOrEmpty(emotion))
        {
            Debug.LogError("Emotion string is null or empty!");
            return;
        }

        if (emotionMap.ContainsKey(emotion))
        {
            if (avatarRenderer != null)
            {
                // Stop previous animation if running
                if (currentAnimation != null)
                {
                    StopCoroutine(currentAnimation);
                }

                // Start squash & stretch animation
                currentAnimation = StartCoroutine(SquashStretchAnimation(emotionMap[emotion]));

                Debug.Log($"Changed emotion to: {emotion}");
            }
            else
            {
                Debug.LogError("avatarRenderer is null!");
            }
        }
        else
        {
            Debug.LogWarning($"Emotion '{emotion}' not found in emotion map! Available emotions: {string.Join(", ", emotionMap.Keys)}");
        }
    }

    IEnumerator SquashStretchAnimation(Sprite newSprite)
    {
        Transform avatarTransform = pivot.transform;
        Vector3 originalScale = avatarTransform.localScale;

        float elapsed = 0f;
        float phaseDuration = animationDuration / 3f; // Divide into 3 phases

        // Phase 1: Squash down (compress vertically, stretch horizontally)
        while (elapsed < phaseDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / phaseDuration;

            // Ease in for smoother start
            t = t * t; // quadratic ease in

            float scaleY = Mathf.Lerp(1f, 1f / squashAmount, t);
            float scaleX = Mathf.Lerp(1f, squashAmount, t);

            avatarTransform.localScale = new Vector3(
                originalScale.x * scaleX,
                originalScale.y * scaleY,
                originalScale.z
            );

            yield return null;
        }

        // Switch sprite at the peak of squash
        avatarRenderer.sprite = newSprite;

        elapsed = 0f;

        // Phase 2: Stretch up (compress horizontally, stretch vertically - overshoot)
        while (elapsed < phaseDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / phaseDuration;

            // Fast snap for cartoon effect
            t = 1f - (1f - t) * (1f - t); // quadratic ease out

            float scaleY = Mathf.Lerp(1f / squashAmount, squashAmount, t);
            float scaleX = Mathf.Lerp(squashAmount, 1f / squashAmount, t);

            avatarTransform.localScale = new Vector3(
                originalScale.x * scaleX,
                originalScale.y * scaleY,
                originalScale.z
            );

            yield return null;
        }

        elapsed = 0f;

        // Phase 3: Settle back to normal (with slight bounce)
        while (elapsed < phaseDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / phaseDuration;

            // Ease out with slight overshoot
            t = t * t * t; // cubic ease for smooth settle

            float scaleY = Mathf.Lerp(squashAmount, 1f, t);
            float scaleX = Mathf.Lerp(1f / squashAmount, 1f, t);

            avatarTransform.localScale = new Vector3(
                originalScale.x * scaleX,
                originalScale.y * scaleY,
                originalScale.z
            );

            yield return null;
        }

        // Ensure we end at exactly original scale
        avatarTransform.localScale = originalScale;
    }

    (string, List<MarkerData>) ParseScriptWithMarkers(string script)
    {
        List<MarkerData> markerList = new List<MarkerData>();
        string clean = script;

        // Find all {Emotion} markers
        Regex regex = new Regex(@"\{(\w+)\}");
        MatchCollection matches = regex.Matches(script);

        int wordOffset = 0;

        foreach (Match match in matches)
        {
            // Count words before this marker
            string textBeforeMarker = script.Substring(0, match.Index);
            int wordsBeforeMarker = textBeforeMarker.Split(new char[] { ' ' },
                System.StringSplitOptions.RemoveEmptyEntries).Length;

            markerList.Add(new MarkerData
            {
                wordIndex = wordsBeforeMarker - wordOffset,
                emotion = match.Groups[1].Value
            });

            // Remove marker from script
            clean = clean.Replace(match.Value, "");
        }

        return (clean, markerList);
    }
}

[System.Serializable]
public class MarkerData
{
    public int wordIndex;
    public string emotion;
    public bool triggered = false;
}