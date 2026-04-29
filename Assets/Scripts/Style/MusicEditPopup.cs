using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using MugsTech.Background;

namespace MugsTech.Style
{
    /// <summary>
    /// Modal popup for editing the background-music playlist + volume on a
    /// VisualsSave. Self-builds its UI on first Show. Track rows are rebuilt
    /// dynamically so the list grows / shrinks as the user adds and removes
    /// files.
    ///
    /// Mutates the passed-in BackgroundMusicData directly and fires onChanged
    /// after every edit so the visuals menu can persist live.
    /// </summary>
    public class MusicEditPopup : MonoBehaviour
    {
        public static MusicEditPopup GetOrCreate(Transform parent)
        {
            var found = parent.GetComponentInChildren<MusicEditPopup>(includeInactive: true);
            if (found != null) return found;
            var go = new GameObject("MusicEditPopup", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<MusicEditPopup>();
        }

        BackgroundMusicData data;
        Action              onChanged;
        bool                built;

        Slider        volumeSlider;
        Text          volumeLabel;
        RectTransform listContainer;
        Text          emptyHint;

        public void Show(BackgroundMusicData target, Action onChange)
        {
            this.data      = target ?? new BackgroundMusicData();
            this.onChanged = onChange;
            if (!built) BuildUI();
            volumeSlider.SetValueWithoutNotify(Mathf.Clamp01(data.volume));
            UpdateVolumeLabel();
            RebuildTrackList();
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
        }

        void Close()
        {
            gameObject.SetActive(false);
            onChanged = null;
            data      = null;
        }

        // -------------------------------------------------------------------
        // UI construction (panel + static rows + dynamic list container)
        // -------------------------------------------------------------------

        const float k_PanelWidth   = 820f;
        const float k_PanelHeight  = 720f;
        const float k_RowHeight    = 50f;

        Transform panelTf;

        void BuildUI()
        {
            built = true;

            var selfRT = (RectTransform)transform;
            selfRT.anchorMin = Vector2.zero;
            selfRT.anchorMax = Vector2.one;
            selfRT.offsetMin = selfRT.offsetMax = Vector2.zero;

            var backdrop = NewChild("Backdrop", transform, stretch: true);
            backdrop.AddComponent<Image>().color = new Color(0, 0, 0, 0.55f);

            var panel = NewChild("Panel", transform, stretch: false);
            panelTf = panel.transform;
            var prt = (RectTransform)panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(k_PanelWidth, k_PanelHeight);
            panel.AddComponent<Image>().color = new Color(0.10f, 0.12f, 0.16f, 0.98f);

            float y = k_PanelHeight * 0.5f - 50f;
            BuildTitle("Background Music", ref y);
            y -= 6f;
            BuildVolumeRow(ref y);
            y -= 14f;
            BuildAddButton(ref y);
            y -= 10f;
            BuildListContainer(ref y);

            BuildCloseButton();
        }

        void BuildTitle(string text, ref float y)
        {
            var go = NewRow("Title", ref y, 56f);
            var t = go.AddComponent<Text>();
            t.text       = text;
            t.font       = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize   = 32;
            t.fontStyle  = FontStyle.Bold;
            t.alignment  = TextAnchor.MiddleCenter;
            t.color      = new Color(0.95f, 0.97f, 1f, 1f);
        }

        void BuildVolumeRow(ref float y)
        {
            var rowGO = NewRow("Volume", ref y, k_RowHeight);

            var labelGO = NewChild("Label", rowGO.transform, stretch: false);
            var lRT = (RectTransform)labelGO.transform;
            lRT.anchorMin = lRT.anchorMax = lRT.pivot = new Vector2(0.5f, 0.5f);
            lRT.anchoredPosition = new Vector2(-k_PanelWidth * 0.5f + 130f, 0f);
            lRT.sizeDelta        = new Vector2(220f, k_RowHeight);
            volumeLabel = labelGO.AddComponent<Text>();
            volumeLabel.font       = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            volumeLabel.fontSize   = 22;
            volumeLabel.alignment  = TextAnchor.MiddleLeft;
            volumeLabel.color      = new Color(0.85f, 0.88f, 0.93f, 1f);

            // Slider track
            var trackGO = NewChild("Slider", rowGO.transform, stretch: false);
            var trt = (RectTransform)trackGO.transform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(80f, 0f);
            trt.sizeDelta        = new Vector2(440f, 28f);
            var trackImg = trackGO.AddComponent<Image>();
            trackImg.color = new Color(0.18f, 0.20f, 0.24f, 1f);

            volumeSlider = trackGO.AddComponent<Slider>();
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;
            volumeSlider.value    = Mathf.Clamp01(data != null ? data.volume : BackgroundMusicPlayer.DefaultVolume);
            volumeSlider.targetGraphic = trackImg;
            volumeSlider.direction = Slider.Direction.LeftToRight;

            var fillArea = NewChild("Fill Area", trackGO.transform, stretch: false);
            var faRT = (RectTransform)fillArea.transform;
            faRT.anchorMin = new Vector2(0, 0.25f);
            faRT.anchorMax = new Vector2(1, 0.75f);
            faRT.offsetMin = new Vector2(8, 0);
            faRT.offsetMax = new Vector2(-8, 0);
            var fill = NewChild("Fill", fillArea.transform, stretch: true);
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.55f, 0.78f, 1f, 1f);
            volumeSlider.fillRect = (RectTransform)fill.transform;

            var handleArea = NewChild("Handle Slide Area", trackGO.transform, stretch: false);
            var haRT = (RectTransform)handleArea.transform;
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(8, 0);
            haRT.offsetMax = new Vector2(-8, 0);
            var handle = NewChild("Handle", handleArea.transform, stretch: false);
            var handleRT = (RectTransform)handle.transform;
            handleRT.sizeDelta = new Vector2(18f, 32f);
            handle.AddComponent<Image>().color = Color.white;
            volumeSlider.handleRect = handleRT;

            volumeSlider.onValueChanged.AddListener(v =>
            {
                if (data == null) return;
                data.volume = Mathf.Clamp01(v);
                UpdateVolumeLabel();
                onChanged?.Invoke();
            });
        }

        void UpdateVolumeLabel()
        {
            if (volumeLabel == null) return;
            float v = data != null ? data.volume : BackgroundMusicPlayer.DefaultVolume;
            volumeLabel.text = $"Volume:  {Mathf.RoundToInt(v * 100f)}%";
        }

        void BuildAddButton(ref float y)
        {
            var rowGO = NewRow("AddRow", ref y, k_RowHeight);

            var btnGO = NewChild("AddButton", rowGO.transform, stretch: false);
            var rt = (RectTransform)btnGO.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 0f);
            rt.sizeDelta        = new Vector2(360f, 44f);
            var img = btnGO.AddComponent<Image>();
            img.color = new Color(0.20f, 0.55f, 0.32f, 1f);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;
            var labelGO = NewChild("Label", btnGO.transform, stretch: true);
            var t = labelGO.AddComponent<Text>();
            t.text       = "+ Add Music File…";
            t.font       = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize   = 22;
            t.fontStyle  = FontStyle.Bold;
            t.alignment  = TextAnchor.MiddleCenter;
            t.color      = Color.white;

            btn.onClick.AddListener(OnAddClicked);
        }

        void BuildListContainer(ref float y)
        {
            // Reserve a region for dynamic track rows. The rows are children
            // of `listContainer`, repositioned by RebuildTrackList. Total
            // height is fixed; if more tracks are added than fit, the bottom
            // ones will visually overflow (kept simple — the README expects
            // only 2–3 tracks per video).
            const float listHeight = 360f;
            var rowGO = NewRow("List", ref y, listHeight);
            listContainer = (RectTransform)rowGO.transform;

            var emptyGO = NewChild("Empty", listContainer, stretch: false);
            var ert = (RectTransform)emptyGO.transform;
            ert.anchorMin = ert.anchorMax = ert.pivot = new Vector2(0.5f, 1f);
            ert.anchoredPosition = new Vector2(0f, -10f);
            ert.sizeDelta        = new Vector2(700f, 50f);
            emptyHint = emptyGO.AddComponent<Text>();
            emptyHint.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            emptyHint.fontSize  = 20;
            emptyHint.fontStyle = FontStyle.Italic;
            emptyHint.alignment = TextAnchor.MiddleCenter;
            emptyHint.color     = new Color(0.55f, 0.58f, 0.64f, 1f);
            emptyHint.text      = "No tracks yet. Click + Add Music File above.";
        }

        void BuildCloseButton()
        {
            var btnGO = NewChild("CloseButton", panelTf, stretch: false);
            var rt = (RectTransform)btnGO.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -k_PanelHeight * 0.5f + 50f);
            rt.sizeDelta        = new Vector2(220f, 60f);
            var img = btnGO.AddComponent<Image>();
            img.color = new Color(0.30f, 0.32f, 0.36f, 1f);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(Close);

            var labelGO = NewChild("Label", btnGO.transform, stretch: true);
            var t = labelGO.AddComponent<Text>();
            t.text       = "Close";
            t.font       = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize   = 26;
            t.fontStyle  = FontStyle.Bold;
            t.alignment  = TextAnchor.MiddleCenter;
            t.color      = Color.white;
        }

        // -------------------------------------------------------------------
        // Dynamic track list
        // -------------------------------------------------------------------

        void RebuildTrackList()
        {
            if (listContainer == null) return;

            // Tear down all existing rows except the empty hint (kept so we
            // can toggle it via SetActive without losing the reference).
            for (int i = listContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = listContainer.GetChild(i);
                if (emptyHint != null && child == emptyHint.transform) continue;
                Destroy(child.gameObject);
            }

            bool empty = data == null || data.filePaths == null || data.filePaths.Count == 0;
            if (emptyHint != null) emptyHint.gameObject.SetActive(empty);
            if (empty) return;

            float y = -10f;
            for (int i = 0; i < data.filePaths.Count; i++)
            {
                int captured = i;
                var rowGO = NewChild($"Track_{i}", listContainer, stretch: false);
                var rt = (RectTransform)rowGO.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, y);
                rt.sizeDelta        = new Vector2(740f, 44f);
                var img = rowGO.AddComponent<Image>();
                img.color = new Color(0.16f, 0.18f, 0.22f, 1f);

                var labelGO = NewChild("Label", rowGO.transform, stretch: false);
                var lRT = (RectTransform)labelGO.transform;
                lRT.anchorMin = new Vector2(0f, 0f);
                lRT.anchorMax = new Vector2(1f, 1f);
                lRT.offsetMin = new Vector2(16f, 0f);
                lRT.offsetMax = new Vector2(-110f, 0f);
                var t = labelGO.AddComponent<Text>();
                t.font       = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                t.fontSize   = 20;
                t.alignment  = TextAnchor.MiddleLeft;
                t.color      = new Color(0.85f, 0.88f, 0.93f, 1f);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                string fileName = Path.GetFileName(data.filePaths[i]);
                t.text       = $"{i + 1}.  {fileName}";

                var removeGO = NewChild("Remove", rowGO.transform, stretch: false);
                var rrt = (RectTransform)removeGO.transform;
                rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(1f, 0.5f);
                rrt.anchoredPosition = new Vector2(-10f, 0f);
                rrt.sizeDelta        = new Vector2(90f, 36f);
                var rImg = removeGO.AddComponent<Image>();
                rImg.color = new Color(0.45f, 0.20f, 0.20f, 1f);
                var rBtn = removeGO.AddComponent<Button>();
                rBtn.targetGraphic = rImg;
                rBtn.onClick.AddListener(() => OnRemoveClicked(captured));
                var rLabelGO = NewChild("Label", removeGO.transform, stretch: true);
                var rt2 = rLabelGO.AddComponent<Text>();
                rt2.text       = "Remove";
                rt2.font       = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                rt2.fontSize   = 18;
                rt2.fontStyle  = FontStyle.Bold;
                rt2.alignment  = TextAnchor.MiddleCenter;
                rt2.color      = Color.white;

                y -= 50f;
            }
        }

        void OnAddClicked()
        {
            if (data == null) return;
            string picked = TryPickAudioPath();
            if (string.IsNullOrEmpty(picked)) return;
            if (data.filePaths == null) data.filePaths = new System.Collections.Generic.List<string>();
            data.filePaths.Add(picked);
            RebuildTrackList();
            onChanged?.Invoke();
        }

        void OnRemoveClicked(int idx)
        {
            if (data == null || data.filePaths == null) return;
            if (idx < 0 || idx >= data.filePaths.Count) return;
            data.filePaths.RemoveAt(idx);
            RebuildTrackList();
            onChanged?.Invoke();
        }

        static string TryPickAudioPath()
        {
#if STANDALONE_FILE_BROWSER
            var ext = new[]
            {
                new SFB.ExtensionFilter("Audio Files", "mp3", "wav", "ogg", "aif", "aiff"),
                new SFB.ExtensionFilter("All Files",   "*"),
            };
            var picked = SFB.StandaloneFileBrowser.OpenFilePanel("Pick music file", "", ext, false);
            return (picked != null && picked.Length > 0) ? picked[0] : "";
#elif UNITY_EDITOR
            return UnityEditor.EditorUtility.OpenFilePanel("Pick music file", "", "mp3,wav,ogg,aif,aiff");
#else
            return "";
#endif
        }

        // -------------------------------------------------------------------
        // Layout helpers (mirror the BigTextStylePopup pattern)
        // -------------------------------------------------------------------

        GameObject NewRow(string name, ref float y, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(panelTf, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y - height * 0.5f);
            rt.sizeDelta        = new Vector2(k_PanelWidth - 40f, height);
            y -= height + 6f;
            return go;
        }

        static GameObject NewChild(string name, Transform parent, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            if (stretch)
            {
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            return go;
        }
    }
}
