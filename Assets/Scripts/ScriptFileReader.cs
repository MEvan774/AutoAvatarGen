using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

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

    [Header("Auto-Load From ElevenLabs Output")]
    [Tooltip("When true, auto-discovers a '<SLUG>_timed.txt' and matching '<SLUG>.mp3' pair " +
             "in 'Assets/<pythonOutputFolder>' and loads both. The discovered slug is used as " +
             "the recorded video's title.")]
    public bool autoLoadFromPythonOutput = true;

    [Tooltip("Folder (relative to Assets/) that contains the ElevenLabs pre-processor output. " +
             "Default matches elevenlabs_tts_processor.py's default --out_dir.")]
    public string pythonOutputFolder = "Python/output";

    [Tooltip("Leave empty to load the first '*_timed.txt' found (alphabetically). Otherwise specify " +
             "a slug like 'COLD_OPEN' to load 'COLD_OPEN_timed.txt' + 'COLD_OPEN.mp3'.")]
    public string segmentSlugOverride = "";

    // Resolved slug of the segment that was actually loaded — surfaced so the
    // recorder can stamp it into the output filename.
    public string LoadedSegmentSlug { get; private set; }

    void Start()
    {
        if (autoLoadFromPythonOutput && TryResolvePythonPair(out string scriptPath, out string audioPath, out string slug))
        {
            StartCoroutine(AutoLoadAndProcess(scriptPath, audioPath, slug));
        }
        else
        {
            ProcessScript();
        }
    }

    // -----------------------------------------------------------------------
    // Auto-load path — reads <SLUG>_timed.txt + <SLUG>.mp3 from Python output
    // -----------------------------------------------------------------------

    bool TryResolvePythonPair(out string scriptPath, out string audioPath, out string slug)
    {
        scriptPath = audioPath = slug = null;

        string folder = Path.Combine(Application.dataPath, pythonOutputFolder);
        if (!Directory.Exists(folder))
        {
            Debug.LogWarning($"[ScriptFileReader] Python output folder not found: {folder}");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(segmentSlugOverride))
        {
            slug = segmentSlugOverride.Trim();
            scriptPath = Path.Combine(folder, slug + "_timed.txt");
            audioPath = Path.Combine(folder, slug + ".mp3");
            if (File.Exists(scriptPath) && File.Exists(audioPath)) return true;

            Debug.LogWarning($"[ScriptFileReader] Segment override '{slug}' not found " +
                             $"(looked for {scriptPath} + {audioPath}).");
            return false;
        }

        string[] timedScripts = Directory.GetFiles(folder, "*_timed.txt");
        System.Array.Sort(timedScripts);
        foreach (string candidate in timedScripts)
        {
            string baseName = Path.GetFileNameWithoutExtension(candidate);
            if (!baseName.EndsWith("_timed")) continue;
            string candidateSlug = baseName.Substring(0, baseName.Length - "_timed".Length);
            string candidateAudio = Path.Combine(folder, candidateSlug + ".mp3");
            if (!File.Exists(candidateAudio)) continue;

            scriptPath = candidate;
            audioPath = candidateAudio;
            slug = candidateSlug;
            return true;
        }

        Debug.LogWarning($"[ScriptFileReader] No '<SLUG>_timed.txt' + '<SLUG>.mp3' pair found in {folder}");
        return false;
    }

    IEnumerator AutoLoadAndProcess(string scriptPath, string audioPath, string slug)
    {
        LoadedSegmentSlug = slug;
        Debug.Log($"[ScriptFileReader] Auto-loading segment '{slug}' from {scriptPath}");

        string scriptContent = File.ReadAllText(scriptPath);

        AudioClip clip = null;
        yield return LoadAudioClip(audioPath, loaded => clip = loaded);

        if (clip == null)
        {
            Debug.LogError($"[ScriptFileReader] Failed to load audio at {audioPath}. Aborting.");
            yield break;
        }

        correspondingAudio = clip;
        ApplyVideoTitle(slug);
        Dispatch(scriptContent, clip);
    }

    IEnumerator LoadAudioClip(string path, System.Action<AudioClip> onLoaded)
    {
        // System.Uri produces the correct 'file:///C:/...' form on Windows —
        // a plain "file://" + path would put the drive letter in the authority.
        string uri = new System.Uri(path).AbsoluteUri;
        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[ScriptFileReader] Audio request failed: {req.error} ({uri})");
                onLoaded(null);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
            clip.name = Path.GetFileNameWithoutExtension(path);
            onLoaded(clip);
        }
    }

    // Set the recorded video's filename prefix to the slug so each recording
    // is named '<SLUG>_<yyyy-MM-dd_HH-mm-ss>.mp4'. The recorder lives on the
    // avatar system; grab it through whichever pipeline is active.
    void ApplyVideoTitle(string slug)
    {
        CrossPlatformRecorder recorder = null;
        if (mediaPresentationSystem != null && mediaPresentationSystem.avatarSystem != null)
            recorder = mediaPresentationSystem.avatarSystem.recorder;
        if (recorder == null && avatarSystem != null)
            recorder = avatarSystem.recorder;

        if (recorder != null)
            recorder.fileNamePrefix = slug;
    }

    // -----------------------------------------------------------------------
    // Dispatch to the configured processing system
    // -----------------------------------------------------------------------

    void Dispatch(string scriptContent, AudioClip audio)
    {
        if (useMediaSystem && mediaPresentationSystem != null)
        {
            mediaPresentationSystem.ProcessScriptWithMedia(scriptContent, audio);
            Debug.Log("Processing with Media Presentation System");
        }
        else if (avatarSystem != null)
        {
            avatarSystem.ProcessWithExistingAudio(scriptContent, audio);
            Debug.Log("Processing with Avatar System only");
        }
        else
        {
            Debug.LogError("No processing system assigned!");
        }
    }

    // -----------------------------------------------------------------------
    // Legacy manual-load path (Inspector TextAsset / file / StreamingAssets)
    // -----------------------------------------------------------------------

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

        Dispatch(script, audio);
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

    public void LoadAndProcess(string customFilePath)
    {
        if (File.Exists(customFilePath))
        {
            string scriptContent = File.ReadAllText(customFilePath);
            Dispatch(scriptContent, correspondingAudio);
        }
        else
        {
            Debug.LogError($"File not found: {customFilePath}");
        }
    }
}
