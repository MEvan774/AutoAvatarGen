using UnityEngine;
using System.IO;

public class ScriptFileReader : MonoBehaviour 
{
    [Header("References")]
    public HybridAvatarSystem avatarSystem;
    public AudioClip correspondingAudio;
    
    [Header("File Settings")]
    public string scriptFileName = "tech_news_script.txt"; // Name of your text file
    
    [Header("Or Use Resources Folder")]
    public TextAsset scriptTextAsset; // Drag text file here from Resources folder
    
    void Start() 
    {
        // Option A: Read from Resources folder (easier)
        if (scriptTextAsset != null) 
        {
            ReadScriptFromTextAsset();
        }
        // Option B: Read from file path
        else 
        {
            ReadScriptFromFile();
        }
    }
    
    // Option A: Read from TextAsset (drag .txt file into inspector)
    void ReadScriptFromTextAsset() 
    {
        string scriptContent = scriptTextAsset.text;
        Debug.Log("Script loaded from TextAsset: " + scriptContent);
        
        avatarSystem.ProcessWithExistingAudio(scriptContent, correspondingAudio);
    }
    
    // Option B: Read from file path
    void ReadScriptFromFile() 
    {
        // Path to your script file (in project folder)
        string filePath = Path.Combine(Application.dataPath, "Scripts", scriptFileName);
        
        // Alternative: Read from StreamingAssets folder
        // string filePath = Path.Combine(Application.streamingAssetsPath, scriptFileName);
        
        if (File.Exists(filePath)) 
        {
            string scriptContent = File.ReadAllText(filePath);
            Debug.Log("Script loaded from file: " + scriptContent);
            
            avatarSystem.ProcessWithExistingAudio(scriptContent, correspondingAudio);
        }
        else 
        {
            Debug.LogError($"Script file not found at: {filePath}");
        }
    }
    
    // Call this from button or other script if you want manual trigger
    public void LoadAndProcess(string customFilePath) 
    {
        if (File.Exists(customFilePath)) 
        {
            string scriptContent = File.ReadAllText(customFilePath);
            avatarSystem.ProcessWithExistingAudio(scriptContent, correspondingAudio);
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

**How to Use - Two Methods:**

**Method 1: Resources Folder (Easiest)**

1. Create folder: `Assets/Resources/`
2. Put your text file there: `my_script.txt`
3. Text file contains:
```
Breaking news {Excited} in the AI world today! 
NVIDIA just announced {Serious} a revolutionary chip. 
{Concerned} However, supply chain issues remain.
*/