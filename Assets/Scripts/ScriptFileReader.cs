using System.IO;
using UnityEditor;
using UnityEngine;
using static UnityEditor.Progress;
using static UnityEngine.UIElements.UxmlAttributeDescription;

public class ScriptFileReader : MonoBehaviour
{
    [Header("References")]
    public HybridAvatarSystem avatarSystem;
    public MediaPresentationSystem mediaPresentationSystem;
    public AudioClip correspondingAudio;

    [Header("File Settings")]
    public string scriptFileName = "tech_news_script.txt"; // Name of your text file

    [Header("Or Use Resources Folder")]
    public TextAsset scriptTextAsset; // Drag text file here from Resources folder

    [Header("Processing Mode")]
    public bool useMediaSystem = true; // Toggle between media system and avatar-only

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

        // Choose which system to use
        if (useMediaSystem && mediaPresentationSystem != null)
        {
            // Use media system (handles both emotions AND media)
            mediaPresentationSystem.ProcessScriptWithMedia(script, audio);
            Debug.Log("Processing with Media Presentation System");
        }
        else if (avatarSystem != null)
        {
            // Use avatar system only (emotions only, no media)
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
        // Try TextAsset first (from Resources folder)
        if (scriptTextAsset != null)
        {
            Debug.Log("Loading script from TextAsset");
            return scriptTextAsset.text;
        }

        // Otherwise try loading from file
        string filePath = Path.Combine(Application.dataPath, "Scripts", scriptFileName);

        if (File.Exists(filePath))
        {
            Debug.Log($"Loading script from file: {filePath}");
            return File.ReadAllText(filePath);
        }

        // Try StreamingAssets as fallback
        string streamingPath = Path.Combine(Application.streamingAssetsPath, scriptFileName);
        if (File.Exists(streamingPath))
        {
            Debug.Log($"Loading script from StreamingAssets: {streamingPath}");
            return File.ReadAllText(streamingPath);
        }

        Debug.LogError($"Script file not found! Tried:\n- TextAsset\n- {filePath}\n- {streamingPath}");
        return null;
    }

    // Legacy methods for backward compatibility
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

    // Manual trigger with custom path
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
/*
```

---

## **Key Changes:**

1. ✅ Added `LoadScript()` method that tries multiple sources
2. ✅ Added `ProcessScript()` method that handles both systems
3. ✅ Added `useMediaSystem` toggle to switch between modes
4. ✅ Kept your original methods for backward compatibility

---

## **How to Use:**

### **Option 1: Resources Folder (Easiest)**

1. Create folder: `Assets / Resources /`
2.Put your script there: `my_script.txt`
3.In Inspector:
-**Script Text Asset:**Drag the.txt file here
   - **Corresponding Audio:**Drag your AudioClip
   - **Use Media System:** ✓ (checked if using images/ videos)

### **Option 2: File Path**

1.Create folder: `Assets / Scripts /`
2.Put your script there: `tech_news_script.txt`
3.In Inspector:
-**Script File Name:** `tech_news_script.txt`
   -**Corresponding Audio: **Drag AudioClip
   - Leave * *Script Text Asset** empty

---

## **Inspector Setup:**
```
ScriptFileReader
├── Avatar System: [Drag HybridAvatarSystem]
├── Media Presentation System: [Drag MediaPresentationSystem]
├── Corresponding Audio: [Drag AudioClip]
├── Script File Name: "tech_news_script.txt"
├── Script Text Asset: [Drag.txt from Resources] (optional)
└── Use Media System: ☑ (check to use images/videos)
```

---

## **Example Script Format:**

**Without Media (emotions only):**
```
{ Neutral}
Hello everyone!
{Excited} This is exciting news!
{ Serious}
But we need to be serious about this.
```

**With Media (emotions + images/videos):**
```
{ Neutral}
Hello everyone, welcome to Tech News!
{Excited} Breaking news in AI today!
{Image:ai_headline,3}
{ Serious}
As you can see from this headline, it's significant.
{Video:interview,8}
{ Neutral}
That's all for today!
*/