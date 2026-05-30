using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MugsTech.Tts
{
    /// <summary>
    /// Drives the (scene-authored) TTS generator panel: script textarea,
    /// output folder picker, API-key edit, dry-test + generate, progress bar,
    /// status text, back button. All UI elements are wired via Inspector
    /// references — the panel hierarchy is created once by
    /// <c>Tools &gt; AutoAvatarGen &gt; Add TTS Panel</c> and from then on
    /// lives in the .unity file so it can be restyled in the Scene view.
    ///
    /// <see cref="Show"/> / <see cref="Close"/> just toggle the root active
    /// state. The TTS job runs on <see cref="CoroutineHost"/> so the job
    /// survives even if the panel root is disabled mid-run.
    /// </summary>
    public class TtsPanelController : MonoBehaviour
    {
        // Persistence keys
        public const string ScriptPrefKey       = "AutoAvatarGen.TtsScriptDraft";
        public const string OutputFolderPrefKey = MainMenuController.PythonOutputFolderPrefKey;

        // Records the just-generated subfolder so the recording scene
        // (ScriptFileReader) renders that exact generation. Keep in sync with
        // OutputLibraryController + ScriptFileReader.
        public const string SelectedGenerationPrefKey = "AutoAvatarGen.SelectedGeneration";

        [Header("Root")]
        [Tooltip("The panel root that gets shown/hidden. Usually this GameObject itself.")]
        [SerializeField] GameObject panelRoot;

        [Header("Inputs")]
        [SerializeField] TMP_InputField scriptInput;
        [SerializeField] TMP_InputField outputInput;
        [SerializeField] Button         outputBrowseButton;

        [Header("API Key Row")]
        [SerializeField] TMP_Text        apiKeyDisplay;
        [SerializeField] Button          apiKeyEditButton;
        [SerializeField] TtsApiKeyPopup  apiKeyPopup;

        [Header("Actions")]
        [SerializeField] Button dryTestButton;
        [SerializeField] Button generateButton;
        [SerializeField] Button backButton;

        [Header("Progress")]
        [Tooltip("The fixed-width container the fill is sized against. Pivot left-edge.")]
        [SerializeField] RectTransform progressTrack;
        [Tooltip("Image whose width is set to 0..track.width as progress goes 0..1.")]
        [SerializeField] RectTransform progressFill;

        [Tooltip("How fast the displayed bar catches up to the target value. " +
                 "Higher = snappier. Used as a per-second exponential rate.")]
        [Range(1f, 20f)] [SerializeField] float progressLerpSpeed = 6f;

        [Tooltip("If real progress reports stop arriving for this long, the bar " +
                 "slowly creeps forward so the user sees the job is alive. " +
                 "Set to 0 to disable the creep entirely.")]
        [SerializeField] float idleCreepStartSeconds = 0.4f;

        [Tooltip("Fraction-per-second the idle creep advances when active.")]
        [SerializeField] float idleCreepRate = 0.07f;

        [Tooltip("Idle creep stops at this value so we don't pretend the job " +
                 "is finished. Real progress reports can push past it.")]
        [Range(0.5f, 0.99f)] [SerializeField] float idleCreepMax = 0.92f;

        [Header("Status")]
        [SerializeField] TMP_Text statusText;

        Action onClosed;
        bool   busy;

        // Two-value animation model: the job feeds `targetProgress` (the real
        // value we want to reach), Update() smoothly walks `displayedProgress`
        // toward it. Decoupling these two means the bar always animates even
        // when ElevenLabs reports 0 → 1 with nothing in between (which is
        // common — UnityWebRequest progress is unreliable for short POSTs).
        float targetProgress;
        float displayedProgress;
        float lastTargetSetTime;   // unscaledTime of the last SetProgress call
        bool  jobRunning;          // gates the idle creep so it only fires during real work

        void Awake()
        {
            // If panelRoot isn't explicitly wired, treat this GameObject as
            // the root. Common case when the materializer puts the controller
            // on the same GO as the panel.
            if (panelRoot == null) panelRoot = gameObject;

            if (outputBrowseButton != null)
                outputBrowseButton.onClick.AddListener(OnBrowseOutputClicked);
            if (apiKeyEditButton != null)
                apiKeyEditButton.onClick.AddListener(OnEditKeyClicked);
            if (dryTestButton != null)
                dryTestButton.onClick.AddListener(OnDryTestClicked);
            if (generateButton != null)
                generateButton.onClick.AddListener(OnGenerateClicked);
            if (backButton != null)
                backButton.onClick.AddListener(Close);

            // Auto-save script + output folder fields as the user types.
            if (scriptInput != null)
                scriptInput.onValueChanged.AddListener(v =>
                    PlayerPrefs.SetString(ScriptPrefKey, v));
            if (outputInput != null)
                outputInput.onValueChanged.AddListener(OnOutputFolderChanged);

            // Hide on load — panel only shows when Show() is called.
            panelRoot.SetActive(false);
        }

        // ---- show / hide -------------------------------------------------

        public void Show(Action closedCallback = null)
        {
            this.onClosed = closedCallback;

            if (scriptInput != null)
                scriptInput.text = PlayerPrefs.GetString(ScriptPrefKey, "");
            if (outputInput != null)
                outputInput.text = PlayerPrefs.GetString(OutputFolderPrefKey,
                    MainMenuController.DefaultPythonOutputFolder);

            RefreshApiKeyDisplay();
            SetStatus("Ready.", neutral: true);
            SetProgress(0f);

            panelRoot.SetActive(true);
            panelRoot.transform.SetAsLastSibling();
        }

        public void Close()
        {
            panelRoot.SetActive(false);
            onClosed?.Invoke();
            onClosed = null;
        }

        // ---- button handlers --------------------------------------------

        void OnOutputFolderChanged(string value)
        {
            string trimmed = string.IsNullOrWhiteSpace(value)
                ? MainMenuController.DefaultPythonOutputFolder
                : value.Trim();
            PlayerPrefs.SetString(OutputFolderPrefKey, trimmed);
            PlayerPrefs.Save();
            if (outputInput != null && outputInput.text != trimmed)
                outputInput.text = trimmed;
        }

        void OnBrowseOutputClicked()
        {
            string current = outputInput != null ? outputInput.text : "";
            string startDir = ResolveStartDir(current);
            string picked = PickFolder("Pick TTS output folder", startDir);
            if (string.IsNullOrEmpty(picked)) return;
            if (outputInput != null)
            {
                outputInput.text = picked;
                outputInput.onValueChanged?.Invoke(picked);
            }
        }

        void OnEditKeyClicked()
        {
            if (apiKeyPopup == null)
            {
                Debug.LogWarning("[TtsPanel] No TtsApiKeyPopup wired in the inspector. " +
                                 "Re-run Tools > AutoAvatarGen > Add TTS Panel to bake it in.");
                return;
            }
            apiKeyPopup.Show(_ => RefreshApiKeyDisplay());
        }

        void OnDryTestClicked()   => StartJob(dryRun: true);
        void OnGenerateClicked()  => StartJob(dryRun: false);

        void StartJob(bool dryRun)
        {
            if (busy)
            {
                SetStatus("Already running — wait for the current run to finish.", error: true);
                return;
            }

            string script    = scriptInput  != null ? scriptInput.text  : "";
            string outFolder = outputInput  != null ? outputInput.text  : "";
            string apiKey    = TtsApiKeyPopup.LoadKey();

            if (string.IsNullOrWhiteSpace(script))
            {
                SetStatus("Script is empty — paste something before running.", error: true);
                return;
            }
            if (!dryRun && string.IsNullOrWhiteSpace(apiKey))
            {
                SetStatus("No API key — click Edit Key… first.", error: true);
                return;
            }

            busy       = true;
            jobRunning = true;          // arms the idle creep in Update()
            SetButtonsInteractable(false);
            SetProgress(0f);
            SetStatus(dryRun ? "Dry-running…" : "Generating…", neutral: true);

            // Each real generation lands in its own timestamped subfolder under
            // the chosen library root, so nothing is overwritten. Dry runs write
            // nothing, so they don't need (or create) a subfolder.
            string genFolder = outFolder;
            if (!dryRun)
            {
                string rootAbs    = TtsGenerationJob.ResolveOutputFolder(outFolder);
                string baseFolder = Path.Combine(rootAbs,
                    DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                genFolder = baseFolder;
                for (int n = 1; Directory.Exists(genFolder); n++)   // avoid same-second clashes
                    genFolder = $"{baseFolder}_{n}";
            }

            var cfg = new TtsGenerationJob.Config {
                ApiKey       = apiKey,
                OutputFolder = genFolder,
                ScriptText   = script,
                DryRun       = dryRun,
            };

            var job = new TtsGenerationJob(cfg,
                progress: p => SetProgress(p),
                status:   s => SetStatus(s, neutral: true),
                complete: r =>
                {
                    busy       = false;
                    jobRunning = false; // disarms idle creep; bar lerps to wherever target lands
                    SetButtonsInteractable(true);
                    if (r.Success)
                    {
                        SetProgress(1f);   // target → 100%, Update() animates the rest of the way
                        SetStatus(r.WasDryRun
                            ? $"Dry run OK — {r.SegmentsProcessed}/{r.SegmentsTotal} segment(s) parsed."
                            : $"Done — {r.SegmentsProcessed}/{r.SegmentsTotal} segment(s) saved.",
                            success: true);
                        if (!string.IsNullOrEmpty(r.ManifestPath))
                            Debug.Log($"[Tts] Manifest written to: {r.ManifestPath}");

                        // Make this generation the active render input and
                        // refresh the library dropdown so it shows immediately.
                        if (!r.WasDryRun)
                        {
                            PlayerPrefs.SetString(SelectedGenerationPrefKey, genFolder);
                            PlayerPrefs.Save();
                        }
                    }
                    else
                    {
                        SetStatus("Failed: " + (r.ErrorMessage ?? "unknown error"), error: true);
                    }
                });

            CoroutineHost.Instance.StartCoroutine(job.Run());
        }

        // ---- ui helpers --------------------------------------------------

        void RefreshApiKeyDisplay()
        {
            if (apiKeyDisplay == null) return;
            string key = TtsApiKeyPopup.LoadKey();
            if (string.IsNullOrEmpty(key))
            {
                apiKeyDisplay.text  = "no key saved";
                apiKeyDisplay.color = new Color(0.95f, 0.55f, 0.40f);
            }
            else
            {
                string masked = key.Length <= 4
                    ? new string('•', key.Length)
                    : new string('•', key.Length - 4) + key.Substring(key.Length - 4);
                apiKeyDisplay.text  = $"{masked}   ({key.Length} chars)";
                apiKeyDisplay.color = new Color(0.55f, 0.85f, 0.60f);
            }
        }

        void SetStatus(string msg, bool error = false, bool success = false, bool neutral = false)
        {
            if (statusText == null) return;
            statusText.text = msg;
            if (error)        statusText.color = new Color(0.95f, 0.40f, 0.40f);
            else if (success) statusText.color = new Color(0.55f, 0.85f, 0.60f);
            else              statusText.color = new Color(0.65f, 0.70f, 0.78f);
        }

        // External setter — receives real progress reports from the TTS job.
        // Backward seeks (value < current) hard-reset the displayed bar so a
        // new run starts from 0 immediately rather than waiting for the
        // animation to slide back down.
        void SetProgress(float value)
        {
            value = Mathf.Clamp01(value);
            if (value < targetProgress)
            {
                targetProgress    = value;
                displayedProgress = value;   // snap on reset, no animation backward
                ApplyProgressToBar();
            }
            else if (value > targetProgress)
            {
                targetProgress = value;
            }
            // Stamp the time even when value == target so the idle creep
            // counts "no new info for X seconds" not "no change for X
            // seconds" — a steady 0 from the API still resets the clock.
            lastTargetSetTime = Time.unscaledTime;
        }

        void Update()
        {
            // Idle creep — only while a job is actively running. Without
            // this, ElevenLabs's habit of reporting 0 → 1 with nothing
            // between leaves the bar stuck at 0% for the whole API round-trip.
            if (jobRunning && idleCreepStartSeconds > 0f &&
                targetProgress < idleCreepMax &&
                Time.unscaledTime - lastTargetSetTime > idleCreepStartSeconds)
            {
                targetProgress = Mathf.Min(idleCreepMax,
                    targetProgress + idleCreepRate * Time.unscaledDeltaTime);
            }

            // Smooth exponential approach. The 1 - exp(-k*dt) form gives a
            // framerate-independent lerp speed — at k=6 the displayed value
            // covers ~95% of the gap in 0.5s regardless of FPS.
            if (!Mathf.Approximately(displayedProgress, targetProgress))
            {
                float t = 1f - Mathf.Exp(-progressLerpSpeed * Time.unscaledDeltaTime);
                displayedProgress = Mathf.Lerp(displayedProgress, targetProgress, t);
                ApplyProgressToBar();
            }
        }

        // Writes displayedProgress to the fill's width. Falls back to a
        // forced canvas update on the first call if the layout hasn't run
        // yet — without this the initial rect.width is 0 and the fill stays
        // invisible.
        void ApplyProgressToBar()
        {
            if (progressFill == null || progressTrack == null) return;
            float width = progressTrack.rect.width;
            if (width <= 0f)
            {
                Canvas.ForceUpdateCanvases();
                width = progressTrack.rect.width;
            }
            if (width <= 0f) return;  // give up — track has no size yet
            var sd = progressFill.sizeDelta;
            progressFill.sizeDelta = new Vector2(width * displayedProgress, sd.y);
        }

        void SetButtonsInteractable(bool on)
        {
            if (dryTestButton  != null) dryTestButton.interactable  = on;
            if (generateButton != null) generateButton.interactable = on;
            if (backButton     != null) backButton.interactable     = on;
        }

        // ---- folder picker ----------------------------------------------

        static string ResolveStartDir(string current)
        {
            if (string.IsNullOrWhiteSpace(current)) return Application.dataPath;
            if (Path.IsPathRooted(current))         return current;
            return Path.Combine(Application.dataPath, current);
        }

        static string PickFolder(string title, string startDir)
        {
#if STANDALONE_FILE_BROWSER
            var picked = SFB.StandaloneFileBrowser.OpenFolderPanel(title, startDir, false);
            return (picked != null && picked.Length > 0) ? picked[0] : "";
#elif UNITY_EDITOR
            return UnityEditor.EditorUtility.OpenFolderPanel(title, startDir, "");
#else
            return "";
#endif
        }
    }
}
