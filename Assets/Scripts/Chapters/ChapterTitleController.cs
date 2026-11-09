using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Draws the on-screen chapter titles in a take (rules: <see cref="ChapterTitles"/>).
///
/// Created by MediaPresentationSystem once the script is parsed — only when
/// the main menu's "Show chapter titles" is on and the script has a second
/// {Timestamp}, so with the switch off nothing is built and the take renders
/// exactly as before. It never changes what {Timestamp} records.
///
/// Per chapter after the first: due 0.2 s after the section's opening
/// {Transition} has fully revealed (MediaPresentationSystem reports the
/// reveal), or on the Timestamp's word when the section has no transition.
/// Placed in the band above the cards on the side opposite Mugs (centred
/// when he is at Center), at the first spot clear of Mugs, any card or held
/// image; none free → not shown. Up for 2.5 s; a {Black}, an extreme
/// close-up, a new transition or a fullscreen card appearing hides it at
/// once, and it isn't shown again. A fullscreen card already up when the
/// title is due (a section that opens on a ,bigCenter headline) is the
/// backdrop it's drawn over — still clear of that card's own text / image.
///
/// Sort order 31500: above side cards (30000) and fullscreen cards (31000),
/// below transitions and {Black} (32000).
/// </summary>
public class ChapterTitleController : MonoBehaviour
{
    public const int SortOrder = 31500;

    class Chapter
    {
        public int Index;
        public string Label;
        public float Time;
        public TransitionMarkerData Opening; // null: shows on the Timestamp's word
        public float RevealedAt = float.NaN;
        public bool Done;
    }

    MediaPresentationSystem mps;
    AudioSource voice;
    readonly List<Chapter> chapters = new List<Chapter>();
    int timestampCount;

    // UI
    RectTransform overlay;
    RectTransform title;
    CanvasGroup titleGroup;
    TextMeshProUGUI label;
    RectTransform pipRow;
    readonly List<Image> pips = new List<Image>();
    static Sprite s_PipSprite;

    // The title up now
    Chapter current;
    float shownAtClock;
    float dueAtAudio;
    float clock;
    Vector2 restPosition;
    bool transitionStartedWhileUp;
    ContentCard featureAtShow;
    ContentCard lastSeenCard;
    float lastCardSeenAtClock;
    float pxToNormX = 1f / 1920f, pxToNormY = 1f / 1080f;

    // =====================================================================
    // Creation
    // =====================================================================

    public static ChapterTitleController Create(MediaPresentationSystem mps, AudioSource voice,
        List<TimestampMarkerData> stamps, List<TransitionMarkerData> transitions)
    {
        if (stamps == null || ChapterTitles.TitleCount(stamps.Count) == 0) return null;
        if (!ChapterTitles.Enabled)
        {
            Debug.Log("[Chapter] chapter titles are off (main menu) — none shown.");
            return null;
        }
        if (mps == null || mps.mediaCanvas == null)
        {
            Debug.LogWarning("[Chapter] no media canvas — chapter titles can't be drawn.");
            return null;
        }

        var go = new GameObject("ChapterTitleController");
        go.transform.SetParent(mps.transform, false);
        var c = go.AddComponent<ChapterTitleController>();
        c.mps = mps;
        c.voice = voice;
        c.timestampCount = stamps.Count;

        var times = new List<float>();
        if (transitions != null) foreach (var t in transitions) times.Add(t.triggerTime);

        for (int i = 0; i < stamps.Count; i++)
        {
            var ch = new Chapter { Index = i, Label = stamps[i].label, Time = stamps[i].triggerTime };
            if (!ChapterTitles.ShowsTitle(i))
            {
                ch.Done = true;
                Debug.Log($"[Chapter] \"{ch.Label}\" 1/{stamps.Count} — the cold open, no title.");
            }
            else
            {
                int ti = ChapterTitles.OpeningTransition(ch.Time, times);
                if (ti >= 0) ch.Opening = transitions[ti];
            }
            c.chapters.Add(ch);
        }

        c.BuildUI(mps.mediaCanvas);
        return c;
    }

    void BuildUI(Canvas host)
    {
        var container = new GameObject("ChapterTitle_Container", typeof(RectTransform), typeof(Canvas));
        container.transform.SetParent(host.transform, false);
        overlay = container.GetComponent<RectTransform>();
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        Canvas sub = container.GetComponent<Canvas>();
        sub.overrideSorting = true;
        sub.sortingOrder = SortOrder;

        BuildTitle(overlay);
        title.gameObject.SetActive(false);
    }

    /// <summary>The title block (label + pip row) under <paramref name="parent"/>.
    /// Public for the self-tests, which build it without a take.</summary>
    public void BuildTitle(RectTransform parent)
    {
        var go = new GameObject("ChapterTitle", typeof(RectTransform), typeof(CanvasGroup));
        go.transform.SetParent(parent, false);
        title = go.GetComponent<RectTransform>();
        title.anchorMin = title.anchorMax = new Vector2(0.5f, 0.5f);
        titleGroup = go.GetComponent<CanvasGroup>();
        titleGroup.alpha = 0f;
        titleGroup.blocksRaycasts = false;

        label = ContentCardUIBuilder.CreateText(title, "Label", Color.white, ChapterTitles.LabelPx, TextAlignmentOptions.TopLeft);
        CardTypography.ApplyWeight(label, CardTypography.Weight.Bold);
        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.characterSpacing = ChapterTitles.LabelLetterSpacing;
        label.color = Color.white;

        // Soft dark shadow (TMP underlay) so white reads on any backdrop.
        Material m = label.fontMaterial;
        m.EnableKeyword("UNDERLAY_ON");
        m.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.55f));
        m.SetFloat("_UnderlayOffsetX", 0f);
        m.SetFloat("_UnderlayOffsetY", -0.6f);
        m.SetFloat("_UnderlayDilate", 0.15f);
        m.SetFloat("_UnderlaySoftness", 0.6f);
        label.UpdateMeshPadding();

        pipRow = ContentCardUIBuilder.CreateChild(title, "Pips");
    }

    // =====================================================================
    // Events from MediaPresentationSystem
    // =====================================================================

    /// <summary>A {Transition} is starting its cover.</summary>
    public void OnTransitionStarted(TransitionMarkerData m)
    {
        if (current != null) transitionStartedWhileUp = true;
    }

    /// <summary>A {Transition}'s cover has fully opened again.</summary>
    public void OnTransitionRevealed(TransitionMarkerData m)
    {
        float now = voice != null ? voice.time : 0f;
        foreach (Chapter ch in chapters)
            if (!ch.Done && ch.Opening == m && float.IsNaN(ch.RevealedAt))
                ch.RevealedAt = now;
    }

    // =====================================================================
    // Per frame
    // =====================================================================

    void Update()
    {
        clock += Time.deltaTime;
        TrackActiveCard();
        if (voice == null) return;
        float now = voice.time;

        if (voice.isPlaying)
        {
            foreach (Chapter ch in chapters)
            {
                if (ch.Done) continue;
                if (ch.Opening != null)
                {
                    if (!float.IsNaN(ch.RevealedAt))
                    {
                        if (now >= ch.RevealedAt + ChapterTitles.AfterRevealDelay) Show(ch, now);
                    }
                    else if (now >= ch.Time + ChapterTitles.RevealFallbackSeconds)
                    {
                        Debug.Log($"[Chapter] \"{ch.Label}\": its {{Transition}} never reported a reveal " +
                                  "(another one was still running) — showing on its own.");
                        Show(ch, now);
                    }
                }
                else if (now >= ch.Time)
                {
                    Show(ch, now);
                }
                break; // chapters fire in order
            }
        }

        if (current != null) Tick(now);
    }

    void Tick(float now)
    {
        float age = clock - shownAtClock;

        string reason = ChapterTitles.InterruptReason(ReadInterrupts(now));
        if (reason == null)
        {
            Rect block = BlockRectNow();
            List<Rect> obstacles = Obstacles(out List<string> names, ignoreEnteringFeature: true);
            int hit = ChapterTitles.FirstOverlap(block, obstacles);
            if (hit >= 0) reason = $"it would now overlap {names[hit]} (title {Fmt(block)}; {names[hit]} {Fmt(obstacles[hit])})";
        }
        if (reason != null)
        {
            Debug.Log($"[Chapter] t={now:F2}s \"{current.Label}\" hidden early — {reason}.");
            HideNow();
            return;
        }

        titleGroup.alpha = ChapterTitles.AlphaAt(age);
        title.anchoredPosition = restPosition + new Vector2(0f, ChapterTitles.SlideAt(age) * title.localScale.y);
        if (age >= ChapterTitles.TotalSeconds) HideNow();
    }

    ChapterTitles.InterruptState ReadInterrupts(float now)
    {
        var s = new ChapterTitles.InterruptState
        {
            TransitionStarted = transitionStartedWhileUp,
            BlackShowing = mps.blackPanelController != null && mps.blackPanelController.IsShowing,
            CloseUp = mps.IsCloseUpHeld || mps.IsCloseUpCutImminent,
        };

        ContentZoneController cz = mps.contentZoneController;
        ContentCard feature = cz != null && cz.ActiveCardIsFeature ? cz.ActiveCard : null;
        if (feature != null && feature != featureAtShow)
        {
            if (ChapterTitles.IsArrival(false, now - dueAtAudio))
            {
                s.FeatureCardArrived = true;
                s.FeatureCardName = feature.GetType().Name.Replace("Card", "");
            }
            else featureAtShow = feature; // landed with the title: its backdrop
        }
        return s;
    }

    void HideNow()
    {
        if (current != null) current.Done = true;
        current = null;
        titleGroup.alpha = 0f;
        title.gameObject.SetActive(false);
    }

    void TrackActiveCard()
    {
        ContentZoneController cz = mps.contentZoneController;
        ContentCard c = cz != null ? cz.ActiveCard : null;
        if (c != lastSeenCard)
        {
            lastSeenCard = c;
            lastCardSeenAtClock = clock;
        }
    }

    // =====================================================================
    // Show
    // =====================================================================

    void Show(Chapter ch, float now)
    {
        if (current != null) HideNow(); // a new chapter replaces the old one
        ch.Done = true;

        Vector2 frame = OverlaySize();
        float k = frame.y / 1080f;
        pxToNormX = 1f / frame.x;
        pxToNormY = 1f / frame.y;

        title.gameObject.SetActive(true);
        Vector2 blockPx = Compose(ch, k, out bool shrunk, out bool truncated, out string shownText);
        float wN = blockPx.x * pxToNormX, hN = blockPx.y * pxToNormY;

        ChapterTitles.Side preferred = ChapterTitles.OppositeOf(mps.CurrentPosition);
        List<Rect> obstacles = Obstacles(out List<string> names, ignoreEnteringFeature: true);
        ChapterTitles.Side? side = ChapterTitles.ChooseSpot(preferred, wN, hN, obstacles);

        string part = $"{ch.Index + 1}/{timestampCount}";
        if (side == null)
        {
            Rect want = ChapterTitles.BlockRect(preferred, wN, hN);
            int hit = ChapterTitles.FirstOverlap(want, obstacles);
            Debug.Log($"[Chapter] t={now:F2}s \"{ch.Label}\" {part} not shown — no free spot in the top band" +
                      (hit >= 0 ? $" ({names[hit]} is in the way: title {Fmt(want)}; {names[hit]} {Fmt(obstacles[hit])})" : "") + ".");
            title.gameObject.SetActive(false);
            return;
        }

        Place(side.Value, wN, hN, blockPx);
        current = ch;
        shownAtClock = clock;
        dueAtAudio = now;
        transitionStartedWhileUp = false;
        ContentZoneController cz = mps.contentZoneController;
        featureAtShow = cz != null && cz.ActiveCardIsFeature ? cz.ActiveCard : null;
        titleGroup.alpha = 0f;

        Debug.Log($"[Chapter] t={now:F2}s \"{ch.Label}\" {part} shown (side={side.Value})" +
                  (side.Value != preferred ? $" — {preferred} was blocked" : "") +
                  (featureAtShow != null ? $", over the {featureAtShow.GetType().Name.Replace("Card", "")} card" : "") + ".");
        if (truncated)
            Debug.LogWarning($"[Chapter] \"{ch.Label}\" is too long for the band — shrunk to {label.fontSize / k:F0} px " +
                             $"and cut to \"{shownText}\".");
        else if (shrunk)
            Debug.Log($"[Chapter] \"{ch.Label}\" shrunk to {label.fontSize / k:F0} px to fit the band.");
    }

    /// <summary>
    /// Sets the label (fitted) and pips for a chapter; returns the block's size
    /// in canvas px. Public for the self-tests.
    /// </summary>
    public Vector2 Compose(int chapterIndex, int count, string rawLabel, float k,
        out bool shrunk, out bool truncated, out string shownText)
    {
        timestampCount = count;
        return Compose(new Chapter { Index = chapterIndex, Label = rawLabel }, k, out shrunk, out truncated, out shownText);
    }

    Vector2 Compose(Chapter ch, float k, out bool shrunk, out bool truncated, out string shownText)
    {
        string text = ChapterTitles.DisplayLabel(ch.Label);
        float maxW = OverlaySize().x * ChapterTitles.MaxLabelWidth;
        ChapterTitles.LabelFit fit = ChapterTitles.FitLabel(text, (t, s) =>
        {
            label.text = t;
            return CardTypography.MeasureLineWidth(label, s);
        }, maxW, ChapterTitles.LabelPx * k, ChapterTitles.LabelMinPx * k);

        label.text = fit.Text;
        label.fontSize = fit.Size;
        shrunk = fit.Shrunk;
        truncated = fit.Truncated;
        shownText = fit.Text;

        float labelW = CardTypography.MeasureLineWidth(label, fit.Size);
        float cap = CardTypography.CapHeight(label.font, fit.Size);

        // Pips
        bool[] states = ChapterTitles.Pips(timestampCount, ch.Index);
        while (pips.Count < states.Length)
        {
            Image p = ContentCardUIBuilder.CreateImage(pipRow, "Pip", Color.white);
            p.sprite = PipSprite();
            var sh = p.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.45f);
            sh.effectDistance = new Vector2(0f, -2f * k);
            pips.Add(p);
        }
        float d = ChapterTitles.PipDiameterPx * k, gap = ChapterTitles.PipGapPx * k;
        float pipsW = states.Length * d + Mathf.Max(0, states.Length - 1) * gap;
        for (int i = 0; i < pips.Count; i++)
        {
            bool on = i < states.Length;
            pips[i].gameObject.SetActive(on);
            if (!on) continue;
            pips[i].color = new Color(1f, 1f, 1f, states[i] ? 1f : ChapterTitles.PipEmptyAlpha);
        }

        float w = Mathf.Max(labelW, pipsW);
        float labelBoxH = label.GetPreferredValues(100000f, 100000f).y;
        float capTopInBox = CapTopOffset(label, fit.Size); // air above the capitals in TMP's line box
        float h = cap + ChapterTitles.LabelToPipsGapPx * k + d;

        // Layout inside the block (block top = top of the capitals).
        title.sizeDelta = new Vector2(w, h);
        RectTransform lrt = label.rectTransform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f);
        lrt.pivot = new Vector2(0f, 1f);
        lrt.sizeDelta = new Vector2(w, labelBoxH);
        lrt.anchoredPosition = new Vector2(0f, capTopInBox);

        pipRow.anchorMin = pipRow.anchorMax = new Vector2(0f, 1f);
        pipRow.pivot = new Vector2(0f, 1f);
        pipRow.sizeDelta = new Vector2(w, d);
        pipRow.anchoredPosition = new Vector2(0f, -(cap + ChapterTitles.LabelToPipsGapPx * k));
        for (int i = 0; i < states.Length; i++)
        {
            RectTransform prt = pips[i].rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot = new Vector2(0f, 0.5f);
            prt.sizeDelta = new Vector2(d, d);
            prt.anchoredPosition = new Vector2(i * (d + gap), 0f);
        }
        return new Vector2(w, h);
    }

    // Distance from the top of TMP's line box down to the capitals' top.
    static float CapTopOffset(TMP_Text t, float size)
    {
        var fi = t.font.faceInfo;
        float scale = size / Mathf.Max(1f, fi.pointSize) * Mathf.Max(0.0001f, fi.scale);
        return (fi.ascentLine - fi.capLine) * scale;
    }

    // Aligns label and pips to the side and pins the block's top-left (Left),
    // top-centre (Center) or top-right (Right) to its spot.
    void Place(ChapterTitles.Side side, float wN, float hN, Vector2 blockPx)
    {
        TextAlignmentOptions align = side == ChapterTitles.Side.Left ? TextAlignmentOptions.TopLeft
                                   : side == ChapterTitles.Side.Right ? TextAlignmentOptions.TopRight
                                   : TextAlignmentOptions.Top;
        label.alignment = align;

        float d = pips.Count > 0 ? pips[0].rectTransform.sizeDelta.x : 0f;
        int n = 0;
        foreach (Image p in pips) if (p.gameObject.activeSelf) n++;
        float pipsW = n > 0 ? pips[n - 1].rectTransform.anchoredPosition.x + d : 0f;
        float shift = side == ChapterTitles.Side.Left ? 0f
                    : side == ChapterTitles.Side.Right ? blockPx.x - pipsW
                    : (blockPx.x - pipsW) * 0.5f;
        pipRow.anchoredPosition = new Vector2(shift, pipRow.anchoredPosition.y);

        Rect r = ChapterTitles.BlockRect(side, wN, hN);
        bool mirrored = IsMirrored();
        title.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
        title.pivot = new Vector2(0f, 1f);
        // Pivot = the block's top-left; with the flip, it still grows rightwards on screen.
        restPosition = NormToOverlayLocal(new Vector2(r.xMin, r.yMin));
        title.anchoredPosition = restPosition;
    }

    // =====================================================================
    // Geometry: canvas ↔ frame-normalised
    // =====================================================================

    Vector2 OverlaySize()
    {
        RectTransform rt = overlay != null ? overlay : (title != null ? title.parent as RectTransform : null);
        if (rt == null) return new Vector2(1920f, 1080f);
        Rect r = rt.rect;
        return (r.width > 1f && r.height > 1f) ? r.size : new Vector2(1920f, 1080f);
    }

    Camera Cam()
    {
        if (mps == null) return null;
        if (mps.mediaCanvas != null && mps.mediaCanvas.worldCamera != null) return mps.mediaCanvas.worldCamera;
        return mps.mainCamera != null ? mps.mainCamera : Camera.main;
    }

    // World point → frame-normalised (x right, y down), as the recording camera sees it.
    bool WorldToNorm(Vector3 world, out Vector2 norm)
    {
        Camera cam = Cam();
        if (cam != null)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            Rect pr = cam.pixelRect;
            norm = new Vector2((s.x - pr.x) / pr.width, 1f - (s.y - pr.y) / pr.height);
            return true;
        }
        RectTransform rt = title.parent as RectTransform;
        Vector3 l = rt.InverseTransformPoint(world);
        Rect r = rt.rect;
        norm = new Vector2((l.x - r.xMin) / r.width, 1f - (l.y - r.yMin) / r.height);
        return true;
    }

    // Frame-normalised point → the title parent's local space (for anchoredPosition with centre anchors).
    Vector2 NormToOverlayLocal(Vector2 norm)
    {
        RectTransform rt = title.parent as RectTransform;
        Rect r = rt.rect;
        Camera cam = Cam();
        if (cam != null)
        {
            Rect pr = cam.pixelRect;
            Vector2 screen = new Vector2(pr.x + norm.x * pr.width, pr.y + (1f - norm.y) * pr.height);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, cam, out Vector2 local))
                return local - r.center; // anchors at the centre
        }
        return new Vector2(r.xMin + norm.x * r.width, r.yMin + (1f - norm.y) * r.height) - r.center;
    }

    // The recorded canvas can be horizontally mirrored (see ContentCard):
    // when its +x runs right-to-left on screen, the title flips its own X
    // scale so the text reads correctly and grows rightwards on screen.
    bool IsMirrored()
    {
        RectTransform rt = title.parent as RectTransform;
        if (Cam() == null || rt == null) return false;
        WorldToNorm(rt.TransformPoint(new Vector3(rt.rect.xMin, 0f, 0f)), out Vector2 a);
        WorldToNorm(rt.TransformPoint(new Vector3(rt.rect.xMax, 0f, 0f)), out Vector2 b);
        return b.x < a.x;
    }

    Rect RectToNorm(Vector3[] worldCorners)
    {
        float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
        foreach (Vector3 w in worldCorners)
        {
            WorldToNorm(w, out Vector2 n);
            xMin = Mathf.Min(xMin, n.x); xMax = Mathf.Max(xMax, n.x);
            yMin = Mathf.Min(yMin, n.y); yMax = Mathf.Max(yMax, n.y);
        }
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    Rect BlockRectNow()
    {
        var corners = new Vector3[4];
        title.GetWorldCorners(corners);
        // The block's rest rect, not mid-slide.
        Rect r = RectToNorm(corners);
        r.y += ChapterTitles.SlideAt(clock - shownAtClock) * pxToNormY;
        return r;
    }

    /// <summary>
    /// Everything the title must stay clear of, frame-normalised: Mugs, the
    /// active side card, a held {Image}/{Video}, and a fullscreen card's own
    /// text / images (not its full-screen panel or dim — that is the backdrop).
    /// A fullscreen card still in its first 0.8 s is skipped: its headline
    /// slides in through the band on its way to the centre.
    /// </summary>
    List<Rect> Obstacles(out List<string> names, bool ignoreEnteringFeature)
    {
        var list = new List<Rect>();
        names = new List<string>();
        var corners = new Vector3[4];

        // Mugs: his sprite's drawn outline. NOT renderer.bounds — that is the
        // whole texture rectangle, and the emotion sprites carry ~8 % of
        // transparent canvas above his ears, which put his "top" at ~9 % of the
        // frame (inside the band) while his ears sit at ~19 % (2026-11-08 test
        // take: two centred titles hidden on their first frame). His drop
        // shadow is skipped: it draws a full-rect copy of the same sprite.
        if (mps.avatarParent != null)
            foreach (SpriteRenderer sr in mps.avatarParent.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!sr.enabled || sr.sprite == null || sr.color.a < 0.05f) continue;
                if (sr.gameObject.name == ShadowRendererName) continue;
                if (!SpriteOutlineWorld(sr, corners)) continue;
                list.Add(RectToNorm(corners));
                names.Add("Mugs");
            }

        if (mps.IsShowingMedia && mps.mediaDisplay != null && mps.mediaDisplay.gameObject.activeInHierarchy)
        {
            mps.mediaDisplay.rectTransform.GetWorldCorners(corners);
            list.Add(RectToNorm(corners));
            names.Add("the held image");
        }

        ContentZoneController cz = mps.contentZoneController;
        ContentCard card = cz != null ? cz.ActiveCard : null;
        if (card != null)
        {
            string cardName = "the " + card.GetType().Name.Replace("Card", "").Replace("Display", "") + " card";
            if (!cz.ActiveCardIsFeature)
            {
                ((RectTransform)card.transform).GetWorldCorners(corners);
                list.Add(RectToNorm(corners));
                names.Add(cardName);
            }
            else if (!(ignoreEnteringFeature && clock - lastCardSeenAtClock < 0.8f))
            {
                foreach (Graphic g in card.GetComponentsInChildren<Graphic>())
                {
                    if (!g.enabled || g.color.a < 0.05f) continue;
                    Rect r;
                    if (g is TMP_Text t)
                    {
                        if (string.IsNullOrEmpty(t.text)) continue;
                        Bounds tb = t.textBounds;
                        if (tb.size.x <= 0f) continue;
                        corners[0] = t.rectTransform.TransformPoint(new Vector3(tb.min.x, tb.min.y));
                        corners[1] = t.rectTransform.TransformPoint(new Vector3(tb.min.x, tb.max.y));
                        corners[2] = t.rectTransform.TransformPoint(new Vector3(tb.max.x, tb.max.y));
                        corners[3] = t.rectTransform.TransformPoint(new Vector3(tb.max.x, tb.min.y));
                        r = RectToNorm(corners);
                    }
                    else
                    {
                        g.rectTransform.GetWorldCorners(corners);
                        r = RectToNorm(corners);
                        if (r.width >= 0.9f && r.height >= 0.9f) continue; // full-screen panel / dim
                    }
                    list.Add(r);
                    names.Add(cardName + "'s " + g.name);
                }
            }
        }
        return list;
    }

    /// <summary>PresenterShadow's child renderer (a blurred full-rect copy of Mugs).</summary>
    public const string ShadowRendererName = "PresenterShadowRenderer";

    /// <summary>
    /// The four world corners of a sprite's drawn outline — its tight mesh's
    /// extent, honouring flips — instead of its texture rectangle. False when
    /// the sprite has no geometry.
    /// </summary>
    public static bool SpriteOutlineWorld(SpriteRenderer sr, Vector3[] corners)
    {
        Sprite sp = sr != null ? sr.sprite : null;
        if (sp == null) return false;
        Vector2[] v = sp.vertices;
        if (v == null || v.Length == 0) return false;

        float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
        foreach (Vector2 p in v)
        {
            float x = sr.flipX ? -p.x : p.x, y = sr.flipY ? -p.y : p.y;
            xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x);
            yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
        }
        Transform t = sr.transform;
        corners[0] = t.TransformPoint(new Vector3(xMin, yMin, 0f));
        corners[1] = t.TransformPoint(new Vector3(xMin, yMax, 0f));
        corners[2] = t.TransformPoint(new Vector3(xMax, yMax, 0f));
        corners[3] = t.TransformPoint(new Vector3(xMax, yMin, 0f));
        return true;
    }

    static string Fmt(Rect r) => $"x {r.xMin:F3}–{r.xMax:F3}, y {r.yMin:F3}–{r.yMax:F3}";

    static Sprite PipSprite()
    {
        if (s_PipSprite != null) return s_PipSprite;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        float r = size * 0.5f - 1f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x + 0.5f - size * 0.5f, dy = y + 0.5f - size * 0.5f;
            float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.Apply();
        s_PipSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        s_PipSprite.name = "ChapterPip";
        return s_PipSprite;
    }

    // ---- Self-test access ----------------------------------------------------
    public TMP_Text LabelText => label;
    public RectTransform TitleRect => title;

    /// <summary>True while a chapter title is on screen (captions keep out of its way).</summary>
    public bool IsShowing => title != null && title.gameObject.activeInHierarchy;
    public IReadOnlyList<Image> PipImages => pips;
    public void PlaceForTest(ChapterTitles.Side side, Vector2 blockPx)
    {
        Vector2 frame = OverlaySize();
        Place(side, blockPx.x / frame.x, blockPx.y / frame.y, blockPx);
    }
}
