using UnityEngine;
using Evereal.VideoCapture;

public class CrossPlatformRecorder : MonoBehaviour
{
    [Header("References")]
    public VideoCapture videoCaptureComponent; // Drag the VideoCapture prefab here
    public AudioSource voiceAudio;
    public Camera targetCamera;

    [Header("Settings")]
    public bool useGreenScreen = true;

    void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        // Set camera background
        if (targetCamera != null)
        {
            targetCamera.clearFlags = CameraClearFlags.SolidColor;

            if (useGreenScreen)
            {
                targetCamera.backgroundColor = new Color(0, 1, 0, 1); // Green
                Debug.Log("Camera set to GREEN SCREEN");
            }
            else
            {
                targetCamera.backgroundColor = new Color(0, 0, 0, 0); // Transparent
                Debug.Log("Camera set to TRANSPARENT");
            }
        }

        // Configure the VideoCapture component
        if (videoCaptureComponent != null)
        {
            videoCaptureComponent.regularCamera = targetCamera;
            videoCaptureComponent.captureAudio = false; // We don't need audio in the video
            Debug.Log("VideoCapture configured");
        }
        else
        {
            Debug.LogError("VideoCapture component not assigned! Drag the prefab to the inspector.");
        }
    }

    public void StartRecordingWithAudio()
    {
        if (voiceAudio == null)
        {
            Debug.LogError("No AudioSource assigned!");
            return;
        }

        if (videoCaptureComponent == null)
        {
            Debug.LogError("VideoCapture component not assigned!");
            return;
        }

        Debug.Log("=== STARTING RECORDING ===");

        // Start video recording
        videoCaptureComponent.StartCapture();

        // Start audio playback
        voiceAudio.Play();

        // Monitor audio and stop when done
        StartCoroutine(StopWhenAudioEnds());
    }

    System.Collections.IEnumerator StopWhenAudioEnds()
    {
        yield return new WaitForSeconds(0.1f);

        // Wait for audio to finish
        while (voiceAudio != null && voiceAudio.isPlaying)
        {
            yield return null;
        }

        yield return new WaitForSeconds(0.5f);

        // Stop recording
        if (videoCaptureComponent != null && videoCaptureComponent.status == CaptureStatus.STARTED)
        {
            videoCaptureComponent.StopCapture();
            Debug.Log("Recording stopped");
        }
    }

    // Optional: Listen for completion event
    void OnEnable()
    {
        if (videoCaptureComponent != null)
        {
            videoCaptureComponent.OnComplete += OnVideoComplete;
        }
    }

    void OnDisable()
    {
        if (videoCaptureComponent != null)
        {
            videoCaptureComponent.OnComplete -= OnVideoComplete;
        }
    }

    void OnVideoComplete(object sender, CaptureCompleteEventArgs args)
    {
        Debug.Log($"✓✓✓ VIDEO SAVED: {args.SavePath}");

        if (useGreenScreen)
        {
            Debug.Log("✓✓✓ GREEN SCREEN - Use Chroma Key!");
        }
    }
}