using UnityEngine;
using System.Collections;
using System.IO;

public class LinuxTransparentRecorder : MonoBehaviour
{
    [Header("Recording Settings")]
    public string outputFilePrefix = "avatar_video";
    public int recordWidth = 1920;
    public int recordHeight = 1080;
    public int frameRate = 60;

    [Header("References")]
    public AudioSource voiceAudio;
    public Camera targetCamera;

    private bool isRecording = false;
    private int recordingCounter = 0;
    private string currentOutputName;
    private string framesFolder;
    private int frameCounter = 0;

    void Start()
    {
        if (targetCamera == null) 
            targetCamera = Camera.main;
            
        if (targetCamera != null)
        {
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = new Color(0, 1, 0, 1); // Green screen
            Debug.Log("Camera set to GREEN SCREEN");
        }
        else
        {
            Debug.LogError("No camera assigned!");
        }
    }

    public void StartRecordingWithAudio()
    {
        if (voiceAudio == null)
        {
            Debug.LogError("No AudioSource assigned!");
            return;
        }

        if (targetCamera == null)
        {
            Debug.LogError("No Camera assigned!");
            return;
        }

        // Generate unique filename
        currentOutputName = $"{outputFilePrefix}_{recordingCounter:D3}_{System.DateTime.Now:yyyyMMdd_HHmmss}";
        recordingCounter++;
        
        // Setup paths
        string recordingsFolder = Path.Combine(Application.dataPath, "..", "Recordings");
        framesFolder = Path.Combine(recordingsFolder, currentOutputName + "_frames");
        
        // Create folder
        if (Directory.Exists(framesFolder))
        {
            Directory.Delete(framesFolder, true);
        }
        Directory.CreateDirectory(framesFolder);
        
        frameCounter = 0;
        
        Debug.Log($"=== NEW RECORDING: {currentOutputName} ===");
        Debug.Log($"Frames will be saved to: {framesFolder}");
        
        StartCoroutine(CaptureFrames());
    }

    IEnumerator CaptureFrames()
    {
        isRecording = true;
        
        // Create RenderTexture
        RenderTexture renderTexture = new RenderTexture(recordWidth, recordHeight, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousRT = targetCamera.targetTexture;
        targetCamera.targetTexture = renderTexture;
        
        Texture2D screenshot = new Texture2D(recordWidth, recordHeight, TextureFormat.RGB24, false);
        
        float targetFrameTime = 1f / frameRate;
        float timer = 0f;

        Debug.Log("Started capturing frames...");

        // Wait a moment for everything to be ready
        yield return new WaitForEndOfFrame();

        while (voiceAudio.isPlaying)
        {
            timer += Time.deltaTime;
            
            // Capture at target framerate
            if (timer >= targetFrameTime)
            {
                timer -= targetFrameTime;
                
                // Force camera to render to the RenderTexture
                targetCamera.Render();
                
                // Read the RenderTexture
                RenderTexture.active = renderTexture;
                screenshot.ReadPixels(new Rect(0, 0, recordWidth, recordHeight), 0, 0, false);
                screenshot.Apply();
                RenderTexture.active = null;
                
                // Save as PNG
                byte[] bytes = screenshot.EncodeToPNG();
                string framePath = Path.Combine(framesFolder, $"frame_{frameCounter:D6}.png");
                
                try
                {
                    File.WriteAllBytes(framePath, bytes);
                    
                    // Debug every 60 frames
                    if (frameCounter % 60 == 0)
                    {
                        Debug.Log($"Captured frame {frameCounter}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to save frame {frameCounter}: {e.Message}");
                }
                
                frameCounter++;
            }
            
            yield return null;
        }

        // Cleanup
        targetCamera.targetTexture = previousRT;
        RenderTexture.active = null;
        Destroy(renderTexture);
        Destroy(screenshot);

        isRecording = false;
        
        Debug.Log($"=== RECORDING COMPLETE ===");
        Debug.Log($"Total frames captured: {frameCounter}");

        // Verify frames were created
        yield return new WaitForSeconds(0.5f);
        
        string[] frames = Directory.GetFiles(framesFolder, "*.png");
        Debug.Log($"Frames found on disk: {frames.Length}");
        
        if (frames.Length > 0)
        {
            FileInfo firstFrame = new FileInfo(frames[0]);
            Debug.Log($"First frame: {frames[0]}");
            Debug.Log($"First frame size: {firstFrame.Length / 1024}KB");
            
            // Convert to MP4
            CombineWithFFmpeg();
        }
        else
        {
            Debug.LogError("No frames were saved! Check folder permissions.");
            Debug.LogError($"Expected folder: {framesFolder}");
        }
    }

    void CombineWithFFmpeg()
    {
        string outputVideo = Path.Combine(Application.dataPath, "..", "Recordings", currentOutputName + ".mp4");

        string[] frames = Directory.GetFiles(framesFolder, "*.png");
        
        if (frames.Length == 0)
        {
            Debug.LogError("No frames to encode!");
            return;
        }

        Debug.Log($"Encoding {frames.Length} frames to MP4...");

        // Use exact frame pattern
        string framePattern = Path.Combine(framesFolder, "frame_%06d.png");
        
        string ffmpegCommand = $"ffmpeg -y -framerate {frameRate} " +
                              $"-i '{framePattern}' " +
                              $"-c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p " +
                              $"'{outputVideo}'";

        Debug.Log($"FFmpeg command: {ffmpegCommand}");

        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{ffmpegCommand}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = System.Diagnostics.Process.Start(processInfo);
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                if (File.Exists(outputVideo))
                {
                    FileInfo videoFile = new FileInfo(outputVideo);
                    Debug.Log($"✓✓✓ SUCCESS! Video created: {outputVideo}");
                    Debug.Log($"✓✓✓ Video size: {videoFile.Length / 1024 / 1024}MB");
                    Debug.Log("Import into Kdenlive and use Chroma Key to remove green!");
                }
                else
                {
                    Debug.LogError("FFmpeg reported success but video file doesn't exist!");
                }

                // Cleanup frames
                try
                {
                    Directory.Delete(framesFolder, true);
                    Debug.Log("Cleaned up frame files");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Could not delete frames: {e.Message}");
                }
            }
            else
            {
                Debug.LogError($"FFmpeg failed with exit code: {process.ExitCode}");
                Debug.LogError($"FFmpeg stderr: {stderr}");
                Debug.Log($"FFmpeg stdout: {stdout}");
                Debug.LogWarning($"Frames preserved at: {framesFolder}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error running FFmpeg: {e.Message}");
        }
    }
}