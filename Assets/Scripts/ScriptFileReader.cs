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

    [Tooltip("Optional — assigned automatically when a manifest.json is found. " +
             "Stitches multiple numbered segments into one seamless clip so " +
             "reactions fire on each file's own timing.")]
    public SegmentSequencer segmentSequencer;

    [Header("File Settings")]
    public string scriptFileName = "tech_news_script.txt";

    [Header("Or Use Resources Folder")]
    public TextAsset scriptTextAsset;

    [Header("Processing Mode")]
    public bool useMediaSystem = true;

    [Header("Auto-Load From ElevenLabs Output")]
    [Tooltip("When true, auto-discovers the ElevenLabs pre-processor output in " +
             "the folder specified by 'Python Output Folder'. If a manifest.json " +
             "is present, all numbered segments are loaded and stitched into one " +
             "seamless clip. Otherwise a single '<SLUG>_timed.txt' + '<SLUG>.mp3' " +
             "pair is loaded. The discovered slug is used as the recorded video's title.")]
    public bool autoLoadFromPythonOutput = true;

    [Tooltip("Folder containing the ElevenLabs pre-processor output. May be an " +
             "absolute path (e.g. 'D:/MyOutputs/elevenlabs') or a path relative " +
             "to Unity's Assets/ folder (e.g. 'Python/output').")]
    public string pythonOutputFolder = "Python/output";

    [Tooltip("Leave empty to load the first '*_timed.txt' found (alphabetically). Otherwise specify " +
             "a slug like 'COLD_OPEN' to load 'COLD_OPEN_timed.txt' + 'COLD_OPEN.mp3'. " +
             "When set, forces the single-pair path and bypasses manifest stitching.")]
    public string segmentSlugOverride = "";

    // Resolved slug of the segment that was actually loaded — surfaced so the
    // recorder can stamp it into the output filename.
    public string LoadedSegmentSlug { get; private set; }

    // Kept in sync with MainMenuController.PythonOutputFolderPrefKey. If you
    // rename one, rename the other.
    const string PythonOutputFolderPrefKey = "AutoAvatarGen.PythonOutputFolder";

    void Start()
    {
        // Honor any override saved from the main menu's "Python output folder"
        // input. Scenes don't share MonoBehaviour state directly, so we pass
        // the value via PlayerPrefs.
        string overrideFolder = PlayerPrefs.GetString(PythonOutputFolderPrefKey, "");
        if (!string.IsNullOrWhiteSpace(overrideFolder))
            pythonOutputFolder = overrideFolder;

        if (autoLoadFromPythonOutput)
        {
            string folder = ResolveOutputFolder(pythonOutputFolder);

            // Manifest-driven multi-segment path takes precedence unless the
            // user explicitly overrides the slug (which targets a single pair).
            if (string.IsNullOrWhiteSpace(segmentSlugOverride) &&
                File.Exists(Path.Combine(folder, "manifest.json")))
            {
                StartCoroutine(AutoLoadFromManifest(folder));
                return;
            }

            if (TryResolvePythonPair(out string scriptPath, out string audioPath, out string slug))
            {
                StartCoroutine(AutoLoadAndProcess(scriptPath, audioPath, slug));
                return;
            }
        }

        ProcessScript();
    }

    // Absolute paths are used as-is; anything else is treated as relative to
    // Application.dataPath (the Assets/ folder) for backwards compatibility
    // with the old "Python/output" default.
    static string ResolveOutputFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return Application.dataPath;
        return Path.IsPathRooted(folder)
            ? folder
            : Path.Combine(Application.dataPath, folder);
    }

    // -----------------------------------------------------------------------
    // Manifest path — stitches multiple numbered segments via SegmentSequencer
    // -----------------------------------------------------------------------

    IEnumerator AutoLoadFromManifest(string folder)
    {
        if (segmentSequencer == null)
            segmentSequencer = gameObject.AddComponent<SegmentSequencer>();

        yield return segmentSequencer.LoadAndBuild(folder);

        if (segmentSequencer.combinedClip == null ||
            string.IsNullOrEmpty(segmentSequencer.combinedScript))
        {
            Debug.LogWarning("[ScriptFileReader] Manifest load failed — falling back to single-pair discovery.");
            if (TryResolvePythonPair(out string scriptPath, out string audioPath, out string slug))
                yield return AutoLoadAndProcess(scriptPath, audioPath, slug);
            else
                ProcessScript();
            yield break;
        }

        LoadedSegmentSlug = segmentSequencer.titleSlug;
        correspondingAudio = segmentSequencer.combinedClip;
        ApplyVideoTitle(segmentSequencer.titleSlug);

        int count = segmentSequencer.orderedSegments != null
            ? segmentSequencer.orderedSegments.Count
            : 0;
        Debug.Log($"[ScriptFileReader] Auto-loaded {count} stitched segment(s) as '{segmentSequencer.titleSlug}'");

        Dispatch(segmentSequencer.combinedScript, segmentSequencer.combinedClip);
    }

    // -----------------------------------------------------------------------
    // Auto-load path — reads <SLUG>_timed.txt + <SLUG>.mp3 from Python output
    // -----------------------------------------------------------------------

    bool TryResolvePythonPair(out string scriptPath, out string audioPath, out string slug)
    {
        scriptPath = audioPath = slug = null;

        string folder = ResolveOutputFolder(pythonOutputFolder);
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
