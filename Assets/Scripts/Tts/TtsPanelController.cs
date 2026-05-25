using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MugsTech.Tts
{
    /// <summary>
    /// Full-screen sub-panel for generating TTS audio from a pasted script.
    /// Self-builds its UI on first <see cref="Show"/>. Mirrors the visual
    /// language of MusicEditPopup / TtsApiKeyPopup: dark panel + TMP text
    /// + tinted buttons.
    ///
    /// Layout:
    ///   ┌──────────────────────────────────────────────────────┐
    ///   │ Generate Audio                              [ Back ] │
    ///   │ ──────────────────────────────────────────────────── │
    ///   │ Script  ┌──────────────────────────────────────────┐ │
    ///   │         │ (paste script here — multi-line)         │ │
    ///   │         └──────────────────────────────────────────┘ │
    ///   │ Output  [ folder path                  ] [ Browse ] │
    ///   │ API Key [ (hidden)                     ] [ Edit…  ] │
    ///   │ [ Dry Test ]            [ Generate ]                │
    ///   │ ──────────────────────────────────────────────────── │
    ///   │  [============= 47% =============      ]            │
    ///   │  Status line                                        │
    ///   └──────────────────────────────────────────────────────┘
    /// </summary>
    public class TtsPanelController : MonoBehaviour
    {
        // Persistence keys
        public const string ScriptPrefKey       = "AutoAvatarGen.TtsScriptDraft";
        public const string OutputFolderPrefKey = MainMenuController.PythonOutputFolderPrefKey;

        public static TtsPanelController GetOrCreate(Transform parent)
        {
            var found = parent.GetComponentInChildren<TtsPanelController>(includeInactive: true);
            if (found != null) return found;
            var go = new GameObject("TtsPanel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<TtsPanelController>();
        }

        Action onClosed;
        bool   built;
        bool   busy;

        TMP_InputField scriptInput;
        TMP_InputField outputInput;
        TMP_Text       apiKeyDisplay;
        TMP_Text       statusText;
        RectTransform  progressFillRT;       // hand-rolled progress bar fill
        RectTransform  progressTrackRT;      // measured to size the fill
        float          progressValue;
        Button         dryTestButton;
        Button         generateButton;
        Button         backButton;

        TtsApiKeyPopup apiKeyPopup;

        // ---- show / hide -------------------------------------------------

        public void Show(Action closedCallback = null)
        {
            this.onClosed = closedCallback;
            if (!built) BuildUI();

            scriptInput.text = PlayerPrefs.GetString(ScriptPrefKey, "");
            outputInput.text = PlayerPrefs.GetString(OutputFolderPrefKey,
                MainMenuController.DefaultPythonOutputFolder);
            RefreshApiKeyDisplay();
            SetStatus("Ready.", neutral: true);
            SetProgress(0f);

            transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }

        void Close()
        {
            gameObject.SetActive(false);
            onClosed?.Invoke();
            onClosed = null;
        }

        // ---- build -------------------------------------------------------

        const float kPanelWidth  = 1100f;
        const float kPanelHeight = 760f;

        void BuildUI()
        {
            built = true;

            // Make our root fill the screen (Canvas comes from the parent).
            var selfRT = (RectTransform)transform;
            selfRT.anchorMin = Vector2.zero;
            selfRT.anchorMax = Vector2.one;
            selfRT.offsetMin = selfRT.offsetMax = Vector2.zero;

            // Backdrop dims the main menu underneath.
            var backdrop = MakeImage(transform, "Backdrop",
                new Color(0, 0, 0, 0.65f), stretch: true);
            // Backdrop blocks clicks to the menu below.

            // Panel
            var panel = MakeImage(transform, "Panel",
                new Color(0.10f, 0.12f, 0.16f, 0.98f), stretch: false);
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(kPanelWidth, kPanelHeight);

            float y = kPanelHeight * 0.5f - 30f;

            BuildHeader(panel.transform, ref y);
            y -= 12f;
            BuildScriptRow(panel.transform, ref y);
            y -= 12f;
            BuildOutputRow(panel.transform, ref y);
            y -= 8f;
            BuildApiKeyRow(panel.transform, ref y);
            y -= 14f;
            BuildActionRow(panel.transform, ref y);
            y -= 12f;
            BuildProgressRow(panel.transform, ref y);
            y -= 4f;
            BuildStatusRow(panel.transform, ref y);

            // Pre-create the popup as a sibling so it overlays this panel.
            apiKeyPopup = TtsApiKeyPopup.GetOrCreate(transform);
            apiKeyPopup.gameObject.SetActive(false);
        }

        // ---- rows --------------------------------------------------------

        void BuildHeader(Transform parent, ref float y)
        {
            var row = MakeRow(parent, "Header", 56f, ref y);

            var titleGO = new GameObject("Title", typeof(RectTransform));
            titleGO.transform.SetParent(row.transform, false);
            var trt = (RectTransform)titleGO.transform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0f, 0.5f);
            trt.anchoredPosition = new Vector2(20f, 0f);
            trt.sizeDelta        = new Vector2(600f, 48f);
            var ttmp = AddTMP(titleGO.transform, "Generate Audio", 28, FontStyles.Bold);
            ttmp.alignment = TextAlignmentOptions.MidlineLeft;
            ttmp.color     = new Color(0.95f, 0.97f, 1f);

            backButton = MakeButton(row.transform, "Back",
                new Color(0.32f, 0.34f, 0.40f),
                anchorX: 1f, offsetX: -90f, width: 140f, height: 44f, Close);
        }

        void BuildScriptRow(Transform parent, ref float y)
        {
            var label = MakeRow(parent, "ScriptLabel", 24f, ref y);
            var ltmp = AddTMP(label.transform, "Script (paste here)", 16, FontStyles.Bold);
            ltmp.alignment = TextAlignmentOptions.MidlineLeft;
            ltmp.color     = new Color(0.75f, 0.80f, 0.86f);

            const float boxHeight = 280f;
            var box = MakeRow(parent, "ScriptBox", boxHeight, ref y);
            var bg = box.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.08f, 1f);

            // The InputField needs its own raycastable Image so clicks land
            // on it (parent's Image won't bubble down to the input).
            var inputGO = new GameObject("Input",
                typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputGO.transform.SetParent(box.transform, false);
            var irt = (RectTransform)inputGO.transform;
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(12, 10); irt.offsetMax = new Vector2(-12, -10);
            inputGO.GetComponent<Image>().color = new Color(0.08f, 0.10f, 0.13f, 1f);

            scriptInput = inputGO.GetComponent<TMP_InputField>();
            // contentType MUST come before lineType — setting contentType
            // resets lineType to whatever the content type prefers (Standard
            // = SingleLine), so multi-line scripts get clobbered if we don't
            // re-apply MultiLineNewline last.
            scriptInput.contentType = TMP_InputField.ContentType.Standard;
            scriptInput.lineType    = TMP_InputField.LineType.MultiLineNewline;
            scriptInput.textComponent = MakeInputText(inputGO.transform, "Text",
                new Color(0.95f, 0.97f, 1f), placeholder: false);
            scriptInput.placeholder = MakeInputText(inputGO.transform, "Placeholder",
                new Color(0.45f, 0.50f, 0.58f), placeholder: true);
            ((TextMeshProUGUI)scriptInput.placeholder).text =
                "## COLD OPEN\n[deadpan] Paste your script here...\n## SETUP\n...";
            scriptInput.onValueChanged.AddListener(v =>
                PlayerPrefs.SetString(ScriptPrefKey, v));
        }

        void BuildOutputRow(Transform parent, ref float y)
        {
            var row = MakeRow(parent, "OutputRow", 56f, ref y);

            BuildLabeledInputRow(row.transform,
                labelText: "Output folder",
                buttonText: "Browse…",
                buttonTint: new Color(0.20f, 0.45f, 0.65f),
                onButton: OnBrowseOutputClicked,
                placeholderText: "Python/output",
                contentType: TMP_InputField.ContentType.Standard,
                onChanged: v =>
                {
                    string trimmed = string.IsNullOrWhiteSpace(v)
                        ? MainMenuController.DefaultPythonOutputFolder
                        : v.Trim();
                    PlayerPrefs.SetString(OutputFolderPrefKey, trimmed);
                    PlayerPrefs.Save();
                    if (outputInput.text != trimmed) outputInput.text = trimmed;
                },
                out outputInput);
        }

        void BuildApiKeyRow(Transform parent, ref float y)
        {
            var row = MakeRow(parent, "ApiKeyRow", 56f, ref y);

            var bg = row.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.08f, 0.10f, 1f);

            // Label
            var lblGO = new GameObject("Label", typeof(RectTransform));
            lblGO.transform.SetParent(row.transform, false);
            var lrt = (RectTransform)lblGO.transform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(20f, 0f);
            lrt.sizeDelta        = new Vector2(180f, 44f);
            var ltmp = AddTMP(lblGO.transform, "API key", 16, FontStyles.Bold);
            ltmp.alignment = TextAlignmentOptions.MidlineLeft;
            ltmp.color     = new Color(0.75f, 0.80f, 0.86f);

            // Status text in the middle
            var statusGO = new GameObject("KeyStatus", typeof(RectTransform));
            statusGO.transform.SetParent(row.transform, false);
            var srt = (RectTransform)statusGO.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 0.5f);
            srt.anchoredPosition = new Vector2(210f, 0f);
            srt.sizeDelta        = new Vector2(640f, 44f);
            apiKeyDisplay = AddTMP(statusGO.transform, "", 16, FontStyles.Italic);
            apiKeyDisplay.alignment = TextAlignmentOptions.MidlineLeft;

            // Edit button on the right
            MakeButton(row.transform, "Edit Key…",
                new Color(0.40f, 0.30f, 0.55f),
                anchorX: 1f, offsetX: -110f, width: 180f, height: 44f, OnEditKeyClicked);
        }

        void BuildActionRow(Transform parent, ref float y)
        {
            var row = MakeRow(parent, "ActionRow", 60f, ref y);

            dryTestButton = MakeButton(row.transform, "Dry Test",
                new Color(0.30f, 0.55f, 0.65f),
                anchorX: 0f, offsetX: 110f, width: 200f, height: 50f, OnDryTestClicked);

            generateButton = MakeButton(row.transform, "Generate",
                new Color(0.25f, 0.65f, 0.40f),
                anchorX: 1f, offsetX: -130f, width: 240f, height: 50f, OnGenerateClicked);
        }

        void BuildProgressRow(Transform parent, ref float y)
        {
            var row = MakeRow(parent, "Progress", 28f, ref y);
            var bg = row.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.08f, 1f);
            progressTrackRT = (RectTransform)row.transform;

            // Hand-rolled bar — Slider needs fill/handle assigned to behave
            // and a handle would just distract here. One Image stretched
            // vertically, anchored left, scaled horizontally via SetProgress.
            var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGO.transform.SetParent(row.transform, false);
            progressFillRT = (RectTransform)fillGO.transform;
            progressFillRT.anchorMin = new Vector2(0f, 0f);
            progressFillRT.anchorMax = new Vector2(0f, 1f);
            progressFillRT.pivot     = new Vector2(0f, 0.5f);
            progressFillRT.anchoredPosition = Vector2.zero;
            progressFillRT.sizeDelta        = new Vector2(0f, 0f);
            fillGO.GetComponent<Image>().color = new Color(0.30f, 0.65f, 0.45f);
        }

        // Sets the bar's fill 0–1. Defers track-width measurement until end of
        // frame the first time, since RectTransform.rect isn't valid before
        // the canvas does its first layout pass.
        void SetProgress(float value)
        {
            progressValue = Mathf.Clamp01(value);
            if (progressFillRT != null && progressTrackRT != null)
            {
                float trackWidth = progressTrackRT.rect.width;
                progressFillRT.sizeDelta = new Vector2(trackWidth * progressValue, 0f);
            }
        }

        void BuildStatusRow(Transform parent, ref float y)
        {
            var go = MakeRow(parent, "Status", 32f, ref y);
            statusText = AddTMP(go.transform, "Ready.", 15, FontStyles.Italic);
            statusText.alignment = TextAlignmentOptions.MidlineLeft;
            statusText.color     = new Color(0.65f, 0.70f, 0.78f);
            // Inset a bit so it lines up with the labels above.
            var rt = (RectTransform)statusText.transform;
            rt.offsetMin = new Vector2(20f, 0f); rt.offsetMax = new Vector2(-20f, 0f);
        }

        // ---- button handlers --------------------------------------------

        void OnBrowseOutputClicked()
        {
            string current = outputInput != null ? outputInput.text : "";
            string startDir = ResolveStartDir(current);
            string picked = PickFolder("Pick TTS output folder", startDir);
            if (string.IsNullOrEmpty(picked)) return;
            outputInput.text = picked;
            outputInput.onEndEdit?.Invoke(picked);
            outputInput.onValueChanged?.Invoke(picked);
        }

        void OnEditKeyClicked()
        {
            apiKeyPopup.Show(_ => RefreshApiKeyDisplay());
        }

        void OnDryTestClicked()
        {
            StartJob(dryRun: true);
        }

        void OnGenerateClicked()
        {
            StartJob(dryRun: false);
        }

        void StartJob(bool dryRun)
        {
            if (busy)
            {
                SetStatus("Already running — wait for the current run to finish.", error: true);
                return;
            }

            string script = scriptInput.text ?? "";
            string outFolder = outputInput.text ?? "";
            string apiKey = TtsApiKeyPopup.LoadKey();

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

            busy = true;
            SetButtonsInteractable(false);
            SetProgress(0f);
            SetStatus(dryRun ? "Dry-running…" : "Generating…", neutral: true);

            var cfg = new TtsGenerationJob.Config {
                ApiKey       = apiKey,
                OutputFolder = outFolder,
                ScriptText   = script,
                DryRun       = dryRun,
            };

            var job = new TtsGenerationJob(cfg,
                progress: p => SetProgress(p),
                status:   s => SetStatus(s, neutral: true),
                complete: r =>
                {
                    busy = false;
                    SetButtonsInteractable(true);
                    if (r.Success)
                    {
                        SetProgress(1f);
                        SetStatus(r.WasDryRun
                            ? $"Dry run OK — {r.SegmentsProcessed}/{r.SegmentsTotal} segment(s) parsed."
                            : $"Done — {r.SegmentsProcessed}/{r.SegmentsTotal} segment(s) saved.",
                            success: true);
                        if (!string.IsNullOrEmpty(r.ManifestPath))
                            Debug.Log($"[Tts] Manifest written to: {r.ManifestPath}");
                    }
                    else
                    {
                        SetStatus("Failed: " + (r.ErrorMessage ?? "unknown error"), error: true);
                    }
                });

            CoroutineHost.Instance.StartCoroutine(job.Run());
        }

        // ---- helpers -----------------------------------------------------

        void RefreshApiKeyDisplay()
        {
            string key = TtsApiKeyPopup.LoadKey();
            if (string.IsNullOrEmpty(key))
            {
                apiKeyDisplay.text  = "no key saved";
                apiKeyDisplay.color = new Color(0.95f, 0.55f, 0.40f);
            }
            else
            {
                // Mask everything but the last 4 chars — enough to confirm
                // identity without showing the secret.
                string masked = key.Length <= 4
                    ? new string('•', key.Length)
                    : new string('•', key.Length - 4) + key.Substring(key.Length - 4);
                apiKeyDisplay.text  = $"{masked}   ({key.Length} chars)";
                apiKeyDisplay.color = new Color(0.55f, 0.85f, 0.60f);
            }
        }

        void SetStatus(string msg, bool error = false, bool success = false, bool neutral = false)
        {
            statusText.text = msg;
            if (error)        statusText.color = new Color(0.95f, 0.40f, 0.40f);
            else if (success) statusText.color = new Color(0.55f, 0.85f, 0.60f);
            else              statusText.color = new Color(0.65f, 0.70f, 0.78f);
        }

        void SetButtonsInteractable(bool on)
        {
            if (dryTestButton  != null) dryTestButton.interactable  = on;
            if (generateButton != null) generateButton.interactable = on;
            if (backButton     != null) backButton.interactable     = on;
        }

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

        // ---- generic UI builders ----------------------------------------

        static GameObject MakeImage(Transform parent, string name, Color color, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            if (stretch)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            go.GetComponent<Image>().color = color;
            return go;
        }

        GameObject MakeRow(Transform parent, string name, float height, ref float y)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(kPanelWidth - 60f, height);
            rt.anchoredPosition = new Vector2(0f, y - height * 0.5f);
            y -= height;
            return go;
        }

        static TextMeshProUGUI AddTMP(Transform parent, string text, int size, FontStyles style)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font     = TMP_Settings.defaultFontAsset;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.text     = text;
            tmp.raycastTarget = false;
            return tmp;
        }

        // For TMP_InputField we need a TMP text child with specific anchor
        // setup so the caret/scroll machinery has a viewport-sized rect.
        static TextMeshProUGUI MakeInputText(Transform parent, string name,
            Color color, bool placeholder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10, 6); rt.offsetMax = new Vector2(-10, -6);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font      = TMP_Settings.defaultFontAsset;
            tmp.fontSize  = placeholder ? 16 : 16;
            tmp.fontStyle = placeholder ? FontStyles.Italic : FontStyles.Normal;
            tmp.color     = color;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            return tmp;
        }

        Button MakeButton(Transform parent, string label, Color tint,
            float anchorX, float offsetX, float width, float height, Action onClick)
        {
            var go = new GameObject(label + "Button",
                typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(anchorX, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(offsetX, 0f);
            go.GetComponent<Image>().color = tint;

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lblGO = new GameObject("Label", typeof(RectTransform));
            lblGO.transform.SetParent(go.transform, false);
            var lrt = (RectTransform)lblGO.transform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var tmp = lblGO.AddComponent<TextMeshProUGUI>();
            tmp.font      = TMP_Settings.defaultFontAsset;
            tmp.fontSize  = 18;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text      = label;
            tmp.raycastTarget = false;
            return btn;
        }

        // Composite row with a left-aligned label, a stretching input field,
        // and a right-aligned action button. Used by the Output folder row;
        // could be reused for any future single-line picker rows.
        void BuildLabeledInputRow(Transform parent,
            string labelText, string buttonText, Color buttonTint,
            Action onButton, string placeholderText,
            TMP_InputField.ContentType contentType,
            Action<string> onChanged,
            out TMP_InputField input)
        {
            var bg = parent.gameObject.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.08f, 0.10f, 1f);

            // Label
            var lblGO = new GameObject("Label", typeof(RectTransform));
            lblGO.transform.SetParent(parent, false);
            var lrt = (RectTransform)lblGO.transform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(20f, 0f);
            lrt.sizeDelta        = new Vector2(180f, 44f);
            var ltmp = AddTMP(lblGO.transform, labelText, 16, FontStyles.Bold);
            ltmp.alignment = TextAlignmentOptions.MidlineLeft;
            ltmp.color     = new Color(0.75f, 0.80f, 0.86f);

            // Input
            var inputGO = new GameObject("Input",
                typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            inputGO.transform.SetParent(parent, false);
            var irt = (RectTransform)inputGO.transform;
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(1f, 0.5f);
            irt.pivot     = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(210f, 0f);
            // Width: total row width minus label-area (210) minus button (170)
            // — kept symmetrical with kPanelWidth so the layout reads cleanly.
            irt.sizeDelta = new Vector2(kPanelWidth - 60f - 210f - 170f, 44f);
            inputGO.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 1f);

            input = inputGO.GetComponent<TMP_InputField>();
            input.lineType    = TMP_InputField.LineType.SingleLine;
            input.contentType = contentType;
            input.textComponent = MakeInputText(inputGO.transform, "Text",
                new Color(0.95f, 0.97f, 1f), placeholder: false);
            input.placeholder   = MakeInputText(inputGO.transform, "Placeholder",
                new Color(0.45f, 0.50f, 0.58f), placeholder: true);
            ((TextMeshProUGUI)input.placeholder).text = placeholderText;
            input.onValueChanged.AddListener(v => onChanged?.Invoke(v));

            // Button
            MakeButton(parent, buttonText, buttonTint,
                anchorX: 1f, offsetX: -90f, width: 160f, height: 44f, onButton);
        }
    }
}
