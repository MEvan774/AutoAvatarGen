using System.IO;
using UnityEditor;
using UnityEngine;

public class ScriptFileReader : MonoBehaviour
{
    [Header("References")]
    public HybridAvatarSystem avatarSystem;
    public MediaPresentationSystem mediaPresentationSystem;
    public AudioClip correspondingAudio;

    [Header("File Settings")]
    public string scriptFileName = "tech_news_script.txt";

    [Header("Or Use Resources Folder")]
    public TextAsset scriptTextAsset;

    [Header("Processing Mode")]
    public bool useMediaSystem = true;

    void Start()
    {
        ProcessScript();
    }

    public void ProcessScript()
    {
        string script = LoadScript();
        AudioClip audio = correspondingAudio;

        if (string.IsNullOrEmpty(script))
        {
            Debug.LogError("No script loaded!");
            return;
        }

        if (audio == null)
        {
            Debug.LogError("No audio assigned!");
            return;
        }

        if (useMediaSystem && mediaPresentationSystem != null)
        {
            mediaPresentationSystem.ProcessScriptWithMedia(script, audio);
            Debug.Log("Processing with Media Presentation System");
        }
        else if (avatarSystem != null)
        {
            avatarSystem.ProcessWithExistingAudio(script, audio);
            Debug.Log("Processing with Avatar System only");
        }
        else
        {
            Debug.LogError("No processing system assigned!");
        }
    }

    string LoadScript()
    {
        if (scriptTextAsset != null)
        {
            Debug.Log("Loading script from TextAsset");
            return scriptTextAsset.text;
        }

        string filePath = Path.Combine(Application.dataPath, "Scripts", scriptFileName);
        if (File.Exists(filePath))
        {
            Debug.Log($"Loading script from file: {filePath}");
            return File.ReadAllText(filePath);
        }

        string streamingPath = Path.Combine(Application.streamingAssetsPath, scriptFileName);
        if (File.Exists(streamingPath))
        {
            Debug.Log($"Loading script from StreamingAssets: {streamingPath}");
            return File.ReadAllText(streamingPath);
        }

        Debug.LogError($"Script file not found!\nTried:\n- TextAsset\n- {filePath}\n- {streamingPath}");
        return null;
    }

    void ReadScriptFromTextAsset()
    {
        if (scriptTextAsset != null)
        {
            string scriptContent = scriptTextAsset.text;
            Debug.Log("Script loaded from TextAsset");

            if (useMediaSystem && mediaPresentationSystem != null)
            {
                mediaPresentationSystem.ProcessScriptWithMedia(scriptContent, correspondingAudio);
            }
            else
            {
                avatarSystem.ProcessWithExistingAudio(scriptContent, correspondingAudio);
            }
        }
        else
        {
            Debug.LogError("No TextAsset assigned!");
        }
    }

    void ReadScriptFromFile()
    {
        string filePath = Path.Combine(Application.dataPath, "Scripts", scriptFileName);

        if (File.Exists(filePath))
        {
            string scriptContent = File.ReadAllText(filePath);
            Debug.Log("Script loaded from file");

            if (useMediaSystem && mediaPresentationSystem != null)
            {
                mediaPresentationSystem.ProcessScriptWithMedia(scriptContent, correspondingAudio);
            }
            else
            {
                avatarSystem.ProcessWithExistingAudio(scriptContent, correspondingAudio);
            }
        }
        else
        {
            Debug.LogError($"Script file not found at: {filePath}");
        }
    }

    public void LoadAndProcess(string customFilePath)
    {
        if (File.Exists(customFilePath))
        {
            string scriptContent = File.ReadAllText(customFilePath);

            if (useMediaSystem && mediaPresentationSystem != null)
            {
                mediaPresentationSystem.ProcessScriptWithMedia(scriptContent, correspondingAudio);
            }
            else
            {
                avatarSystem.ProcessWithExistingAudio(scriptContent, correspondingAudio);
            }
        }
        else
        {
            Debug.LogError($"File not found: {customFilePath}");
        }
    }
}