using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using MugsTech.Style;
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

        // Tag-only edits. Built at runtime from the Generate button (see
        // BuildTagEditRow) so the hand-tweaked panel in MainMenu.unity is
        // never re-baked.
        Button loadGenerationButton;
        Button updateTagsButton;
        float  loadConfirmUntil;   // unscaledTime; a second Load click before this replaces the draft

        // Saved copies (GenerationSave): "Save copies to" + "Video name" rows,
        // cloned from the output folder row at runtime (see BuildSaveRows).
        TMP_InputField saveFolderInput;
        Button         saveFolderBrowseButton;
        TMP_InputField videoNameInput;

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

            BuildSaveRows();     // first: it moves the status line the tag row is placed under
            BuildTagEditRow();

            // Auto-save script + output folder fields as the user types.
            if (scriptInput != null)
                scriptInput.onValueChanged.AddListener(v =>
                    PlayerPrefs.SetString(ScriptPrefKey, v));
            if (outputInput != null)
                outputInput.onValueChanged.AddListener(OnOutputFolderChanged);
            if (saveFolderInput != null)
                saveFolderInput.onValueChanged.AddListener(v =>
                    PlayerPrefs.SetString(GenerationSave.FolderPrefKey, v.Trim()));
            if (saveFolderBrowseButton != null)
                saveFolderBrowseButton.onClick.AddListener(OnBrowseSaveFolderClicked);
            if (videoNameInput != null)
                videoNameInput.onValueChanged.AddListener(v =>
                    PlayerPrefs.SetString(GenerationSave.VideoNamePrefKey, v));

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
            if (saveFolderInput != null)
                saveFolderInput.text = GenerationSave.ConfiguredFolder;
            if (videoNameInput != null)
                videoNameInput.text = PlayerPrefs.GetString(GenerationSave.VideoNamePrefKey, "");

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

        void OnBrowseSaveFolderClicked()
        {
            // The default folder doesn't exist until the first copy, so start
            // the picker at its nearest existing parent.
            string start = saveFolderInput != null ? saveFolderInput.text.Trim() : "";
            while (!string.IsNullOrEmpty(start) && !Directory.Exists(start))
                start = Path.GetDirectoryName(start);
            string picked = PickFolder("Pick a folder to keep copies of generations in", start ?? "");
            if (string.IsNullOrEmpty(picked) || saveFolderInput == null) return;
            saveFolderInput.text = picked;   // onValueChanged saves it
            PlayerPrefs.Save();
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

        // ---- tag-only edits ---------------------------------------------
        //
        // Load fills the script box with the active generation's script; Update
        // writes the box's tags back into that generation, re-timed against the
        // voice it already has (TagOnlyUpdate). No ElevenLabs call either way.

        void OnLoadFromGenerationClicked()
        {
            if (busy) return;

            string folder = TagOnlyUpdate.ResolveActiveGeneration();
            if (folder == null)
            {
                SetStatus("No generation found — generate one or pick one in the output library.", error: true);
                return;
            }
            if (!TagOnlyUpdate.TryLoadScript(folder, out string script, out string error))
            {
                SetStatus(error, error: true);
                return;
            }

            // The script box may be the only copy of an unsaved draft, so
            // replacing different text takes a second click.
            string current = scriptInput != null ? scriptInput.text : "";
            bool replacesDraft = !string.IsNullOrWhiteSpace(current) &&
                TtsScriptProcessor.NormaliseLineEndings(current).Trim() != script.Trim();
            if (replacesDraft && Time.unscaledTime > loadConfirmUntil)
            {
                loadConfirmUntil = Time.unscaledTime + 5f;
                SetStatus($"Click Load From Generation again to replace the script box with " +
                          $"{Path.GetFileName(folder)}'s script.", warning: true);
                return;
            }
            loadConfirmUntil = 0f;

            if (scriptInput != null) scriptInput.text = script;   // onValueChanged saves the draft
            SetStatus($"Loaded {Path.GetFileName(folder)} — move, edit or remove tags, then click " +
                      "Update Tags Only.", success: true);
        }

        void OnUpdateTagsClicked()
        {
            if (busy)
            {
                SetStatus("Already running — wait for the current run to finish.", error: true);
                return;
            }

            string folder = TagOnlyUpdate.ResolveActiveGeneration();
            if (folder == null)
            {
                SetStatus("No generation found — generate one or pick one in the output library.", error: true);
                return;
            }

            var outcome = TagOnlyUpdate.Apply(folder, scriptInput != null ? scriptInput.text : "");
            foreach (string w in outcome.Warnings) Debug.LogWarning("[TagOnly] " + w);

            if (!outcome.Success)
                SetStatus(outcome.Message, error: true);
            else if (outcome.Warnings.Count > 0)
                SetStatus(outcome.Message + "\nWarning: " + string.Join(" ", outcome.Warnings), warning: true);
            else
                SetStatus(outcome.Message, success: true);
        }

        // A second action row under the status line: Load on the left and
        // Update on the right, mirroring Dry Test / Generate above. The buttons
        // are clones of Generate, so they pick up its look and the menu theme.
        void BuildTagEditRow()
        {
            if (generateButton == null || statusText == null || updateTagsButton != null) return;

            RectTransform status = statusText.rectTransform;
            var row = new GameObject("TagEditRow", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(status.parent, false);
            row.anchorMin = row.anchorMax = status.anchorMin;
            row.pivot     = new Vector2(0.5f, 0.5f);
            row.sizeDelta = new Vector2(status.rect.width, 50f);
            row.anchoredPosition = new Vector2(status.anchoredPosition.x,
                status.anchoredPosition.y - status.rect.height * status.pivot.y - 8f - 25f);

            RectTransform leftTemplate = dryTestButton != null
                ? (RectTransform)dryTestButton.transform
                : (RectTransform)generateButton.transform;
            loadGenerationButton = CloneGenerateButton(row, "LoadFromGenerationButton",
                "Load From Generation", leftTemplate);
            updateTagsButton = CloneGenerateButton(row, "UpdateTagsOnlyButton",
                "Update Tags Only", (RectTransform)generateButton.transform);

            loadGenerationButton.onClick.AddListener(OnLoadFromGenerationClicked);
            updateTagsButton.onClick.AddListener(OnUpdateTagsClicked);

            UITheme.Apply(row.gameObject);
        }

        Button CloneGenerateButton(RectTransform row, string objectName, string label, RectTransform placeLike)
        {
            var go = Instantiate(generateButton.gameObject, row, false);
            go.name = objectName;

            var btn = go.GetComponent<Button>();
            btn.onClick = new Button.ButtonClickedEvent();   // nothing copied from Generate
            btn.interactable = true;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = placeLike.anchorMin;
            rt.anchorMax = placeLike.anchorMax;
            rt.pivot     = placeLike.pivot;
            rt.anchoredPosition = new Vector2(placeLike.anchoredPosition.x, 0f);
            rt.sizeDelta = new Vector2(260f, ((RectTransform)generateButton.transform).sizeDelta.y);

            var text = go.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
                text.enableAutoSizing = true;
                text.fontSizeMax = text.fontSize;
                text.fontSizeMin = 14f;
            }
            return btn;
        }

        // ---- saved copies -----------------------------------------------
        //
        // Two rows above the action row — "Save copies to" (folder + Browse)
        // and "Video name" right above Generate — cloned from the output
        // folder row so they match the hand-tweaked panel. The panel grows by
        // the two rows: what sits above the action row moves up, the action
        // row and everything below it move down, so all spacing is kept.

        void BuildSaveRows()
        {
            if (outputInput == null || generateButton == null || saveFolderInput != null) return;
            var outputRow = outputInput.transform.parent as RectTransform;
            var actionRow = generateButton.transform.parent as RectTransform;
            var panel     = actionRow != null ? actionRow.parent as RectTransform : null;
            if (outputRow == null || panel == null || outputRow.parent != panel) return;

            float rowHeight = outputRow.sizeDelta.y;
            float step      = rowHeight + 8f;
            float split     = actionRow.anchoredPosition.y + actionRow.sizeDelta.y * (1f - actionRow.pivot.y);
            MakeRoom(panel, split, 2f * step);

            // Stacked up from the action row's new top edge, 8 px apart.
            float nameY = split - step + 8f + rowHeight * 0.5f;
            saveFolderInput = CloneOutputRow(outputRow, "SaveFolderRow", "Save copies to",
                "Folder for named copies — empty turns saving off", nameY + step,
                keepBrowse: true, out saveFolderBrowseButton);
            videoNameInput = CloneOutputRow(outputRow, "VideoNameRow", "Video name",
                "Names the saved copy — empty uses the first chapter title", nameY,
                keepBrowse: false, out _);
            if (videoNameInput != null) videoNameInput.characterLimit = 60;
        }

        // Grows 'panel' by 'added' and opens a gap of that height at splitY.
        // Only centre-anchored items are moved: top/bottom-anchored ones already
        // follow the panel's edges. Siblings laid over the panel (the debug
        // toggle by the action row) share its centre and follow the same rule.
        static void MakeRoom(RectTransform panel, float splitY, float added)
        {
            panel.sizeDelta += new Vector2(0f, added);
            foreach (Transform t in panel)
                if (t is RectTransform child) Shift(child, 0f, splitY, added);

            if (panel.parent == null) return;
            foreach (Transform t in panel.parent)
                if (t is RectTransform sibling && sibling != panel && sibling.anchorMin == panel.anchorMin)
                    Shift(sibling, panel.anchoredPosition.y, splitY, added);
        }

        static void Shift(RectTransform rt, float originY, float splitY, float added)
        {
            if (rt.anchorMin != rt.anchorMax || !Mathf.Approximately(rt.anchorMin.y, 0.5f)) return;
            float y = rt.anchoredPosition.y - originY;
            rt.anchoredPosition += new Vector2(0f, y > splitY ? added * 0.5f : -added * 0.5f);
        }

        TMP_InputField CloneOutputRow(RectTransform template, string objectName, string label,
                                      string placeholder, float y, bool keepBrowse, out Button browse)
        {
            var row = (RectTransform)Instantiate(template.gameObject, template.parent, false).transform;
            row.name = objectName;
            row.anchoredPosition = new Vector2(template.anchoredPosition.x, y);

            var input = row.GetComponentInChildren<TMP_InputField>(true);
            browse    = row.GetComponentInChildren<Button>(true);
            if (input == null) { Destroy(row.gameObject); browse = null; return null; }

            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
                if (!text.transform.IsChildOf(input.transform) &&
                    (browse == null || !text.transform.IsChildOf(browse.transform)))
                {
                    text.text = label;
                    break;
                }

            // Nothing carried over from the output folder field.
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onEndEdit      = new TMP_InputField.SubmitEvent();
            input.onSubmit       = new TMP_InputField.SubmitEvent();
            input.text           = "";
            if (input.placeholder is TMP_Text hint) hint.text = placeholder;

            // The input runs from where it starts to the Browse button (or, with
            // the button dropped, to where the button ended).
            var field = (RectTransform)input.transform;
            float left  = field.localPosition.x + field.rect.xMin;
            float right = row.rect.xMax - 20f;
            if (browse != null)
            {
                var b = (RectTransform)browse.transform;
                right = keepBrowse ? b.localPosition.x + b.rect.xMin - 12f : b.localPosition.x + b.rect.xMax;
            }
            field.anchorMin = field.anchorMax = new Vector2(0.5f, 0.5f);
            field.pivot            = new Vector2(0f, 0.5f);
            field.anchoredPosition = new Vector2(left, field.anchoredPosition.y);
            field.sizeDelta        = new Vector2(Mathf.Max(120f, right - left), field.sizeDelta.y);

            if (browse != null)
            {
                if (keepBrowse) browse.onClick = new Button.ButtonClickedEvent();
                else { Destroy(browse.gameObject); browse = null; }
            }

            UITheme.Apply(row.gameObject);
            return input;
        }

        // Copies the finished generation into the save folder off the main
        // thread (the raw takes run to tens of MB), then makes the copy the
        // selected generation, so tag edits and recordings land in the named
        // folder the user comes back to; the original stays in the output
        // folder. The panel stays busy until then — a tag edit mid-copy would
        // race it.
        IEnumerator SaveCopy(string genFolder, string saveRoot, string folderName,
                             string doneMessage, bool doneIsError)
        {
            SetStatus(doneMessage + $"\nSaving a copy as \"{folderName}\"…",
                      error: doneIsError, neutral: !doneIsError);

            var copy = Task.Run(() => GenerationSave.Copy(genFolder, saveRoot, folderName));
            while (!copy.IsCompleted) yield return null;

            busy = false;
            SetButtonsInteractable(true);

            if (copy.IsFaulted)
            {
                string why = copy.Exception?.GetBaseException().Message ?? "unknown error";
                Debug.LogWarning($"[Tts] Could not save a copy to {saveRoot}: {why}");
                SetStatus(doneMessage + $"\nCouldn't save a copy to {saveRoot}: {why}",
                          error: doneIsError, warning: !doneIsError);
                yield break;
            }

            string saved = copy.Result;
            PlayerPrefs.SetString(SelectedGenerationPrefKey, saved);
            PlayerPrefs.Save();
            Debug.Log($"[Tts] Saved a copy of {genFolder} to {saved}");
            SetStatus(doneMessage + $"\nCopy saved: {Path.GetFileName(saveRoot.TrimEnd('/', '\\'))}/{Path.GetFileName(saved)} " +
                      "(now the selected generation).", error: doneIsError, success: !doneIsError);
        }

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
            string videoName = videoNameInput != null ? videoNameInput.text : "";
            DateTime started = DateTime.Now;
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
                    started.ToString("yyyy-MM-dd_HH-mm-ss"));
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
                    jobRunning = false; // disarms idle creep; bar lerps to wherever target lands

                    // A real generation is copied to the save folder before the
                    // panel frees up (SaveCopy); anything else frees it now.
                    string saveRoot = r.Success && !r.WasDryRun ? GenerationSave.ResolvedFolder : null;
                    if (saveRoot == null)
                    {
                        busy = false;
                        SetButtonsInteractable(true);
                    }

                    if (r.Success)
                    {
                        SetProgress(1f);   // target → 100%, Update() animates the rest of the way

                        // A segment that stalled through every retry produced audio
                        // whose markers don't match it. The files are written, but
                        // this must not read as a clean run — recording it would
                        // waste a take.
                        bool stalled = r.StalledSegments != null && r.StalledSegments.Count > 0;
                        if (stalled)
                            SetStatus($"Saved, but {string.Join(", ", r.StalledSegments)} " +
                                      "stalled — cards will drift. Run again before recording.",
                                      error: true);
                        else
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
                            if (saveRoot != null)
                                CoroutineHost.Instance.StartCoroutine(SaveCopy(genFolder, saveRoot,
                                    GenerationSave.FolderName(started, videoName, script),
                                    statusText != null ? statusText.text : "", stalled));
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

        void SetStatus(string msg, bool error = false, bool success = false, bool neutral = false,
                       bool warning = false)
        {
            if (statusText == null) return;
            statusText.text = msg;
            if (error)        statusText.color = new Color(0.95f, 0.40f, 0.40f);
            else if (success) statusText.color = new Color(0.55f, 0.85f, 0.60f);
            else if (warning) statusText.color = new Color(0.95f, 0.75f, 0.35f);
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
            if (loadGenerationButton != null) loadGenerationButton.interactable = on;
            if (updateTagsButton     != null) updateTagsButton.interactable     = on;
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
