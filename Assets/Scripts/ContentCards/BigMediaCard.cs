using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Large centered image/logo card that appears in front of the character.
/// Tag: {BigMedia:name,duration}  or  {BigMedia:name1+name2+...,duration}
///
/// Names joined by '+' produce up to <see cref="MAX_LOGOS"/> logos arranged
/// in a horizontal row. They pop in one after another with a small stagger,
/// like someone counting on their fingers. A single name collapses to a
/// single centered pop.
///
/// Visuals: no panel background — each logo is centered in its slot with
/// preserveAspect. The row's total width widens with count so each entry
/// stays readable. Parent is the fullscreen feature-media zone (see
/// <see cref="ContentZoneController"/>), so the row sits in front of the
/// character.
///
/// Animation: each visible slot scales 0 → 1 with the CSS-derived overshoot
/// curve (peaks ~10% past full size before settling), staggered by
/// <see cref="STAGGER_DELAY"/> so the entrance reads as a count.
///
/// THE LOGO RUN — {BigMedia:name} with NO duration … {BigMedia:End} (the
/// line-by-line mode BigText already has): the first tag opens the grid with
/// one logo, each next one lands on its word, up to eight — one row up to
/// four, a second row beyond that — and the grid re-centres (eased) as it
/// grows. {BigMedia:Strike,name} crosses out a logo already in the grid (two
/// strokes in the Document stamp's red; the logo dims to half) and it stays
/// struck until the run ends. Geometry and timing: <see cref="BigMediaGrid"/>;
/// which tag opens, joins or strikes is ContentZoneController's. A
/// duration-less logo that lands while a TIMED BigMedia is up joins it (the row
/// becomes a grid) and keeps the timed lifetime — the BigText rule.
/// </summary>
public class BigMediaCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.BigMedia;

    // =====================================================================
    // LAYOUT — slot count and horizontal spacing.
    //
    //   MAX_LOGOS              : maximum number of '+'-joined logos.
    //   SLOT_HORIZONTAL_PADDING: pixel inset between sibling slots.
    //   BAND_WIDTHS            : how much horizontal space the row occupies
    //                            for each logo count (1 → 70%, 4 → 92%).
    // =====================================================================
    private const int   MAX_LOGOS               = 4;
    private const float SLOT_HORIZONTAL_PADDING = 32f;

    // =====================================================================
    // ALL TIMING / STAGGER KNOBS LIVE IN THE INSPECTOR
    //
    // Open the ContentZoneController GameObject → CardEntryAnimator
    // component. There are three groups for this card:
    //
    //   • "Per-Card Settings" → BigMedia row
    //         Pop duration override (Slide Duration) & Fade-In Duration
    //         (Direction & Slide Distance Factor don't apply — BigMedia uses
    //          a scale-pop, not a slide.)
    //
    //   • "BigMedia Card" group
    //         Stagger Delay (seconds between consecutive logos popping in)
    //
    //   • "Overshoot Curve" at the top of the component
    //         The shared overshoot curve used by every pop.
    // =====================================================================
    private CardEntryAnimator.BigMediaSettings BigMediaCfg
        => CardEntryAnimator.Instance.bigMedia;

    // Per-count horizontal band of the parent: 1 logo gets 70%, 4 gets 92%.
    // Wider bands for higher counts keep individual logos legible.
    private static readonly float[] BAND_WIDTHS = { 0.70f, 0.80f, 0.88f, 0.92f };

    private readonly List<RectTransform> slotContainers = new List<RectTransform>(MAX_LOGOS);
    // Per-slot CanvasGroups so 'Ease in + fade' can dissolve each logo on its
    // own stagger (the card root stays fully visible during the entrance).
    private readonly List<CanvasGroup> slotGroups = new List<CanvasGroup>(MAX_LOGOS);
    private readonly List<Image> slotImages = new List<Image>(MAX_LOGOS);
    private readonly List<TextMeshProUGUI> slotFallbacks = new List<TextMeshProUGUI>(MAX_LOGOS);

    private int activeSlotCount;

    // ---- The logo run (see the class summary) --------------------------------------
    private const int RUN_SLOTS = BigMediaGrid.MaxLogos;
    private bool runMode;                       // laid out by BigMediaGrid, driven from the card clock
    private ContentCardAssets cardAssets;
    private MediaPresentationSystem logoSource; // the media folder's Logos/ (third lookup)
    private readonly List<Sprite> ownedSprites = new List<Sprite>();
    private readonly string[] slotNames = new string[RUN_SLOTS];
    private readonly float[] landedAt = new float[RUN_SLOTS];
    private readonly bool[] struck = new bool[RUN_SLOTS];
    private readonly float[] struckAt = new float[RUN_SLOTS];
    private readonly RectTransform[][] strokes = new RectTransform[RUN_SLOTS][];
    private readonly Vector2[] restFrom = new Vector2[RUN_SLOTS];
    private readonly Vector2[] restTo = new Vector2[RUN_SLOTS];
    private float clock;
    private float recentredAt = float.NegativeInfinity;
    private Vector2 laidOutFrame = new Vector2(-1f, -1f);

    /// <summary>True for a line-by-line run (or a timed card a duration-less logo joined).</summary>
    public bool IsRun => runMode;
    /// <summary>Logos in the grid now.</summary>
    public int LogoCount => activeSlotCount;
    /// <summary>
    /// How many logos this run will hold — its later tags read ahead from the
    /// timeline by ContentZoneController before Initialize — so the slots are
    /// sized once and only their places change as the run grows.
    /// </summary>
    public int AnticipatedCount { get; set; }
    /// <summary>True once the card is on its way out: a logo arriving now belongs to the next run.</summary>
    public bool IsClosing { get; private set; }

    // ---- Read by the self-tests ---------------------------------------------------------
    public RectTransform Slot(int i) => slotContainers[i];
    public string LogoName(int i) => slotNames[i];
    public bool IsStruck(int i) => struck[i];
    public float LogoAlpha(int i) => slotImages[i].gameObject.activeSelf ? slotImages[i].color.a : slotFallbacks[i].color.a;
    public bool IsText(int i) => slotFallbacks[i].gameObject.activeSelf;
    public float StrokeDrawn(int i, int stroke) =>
        strokes[i] == null ? 0f : strokes[i][stroke].sizeDelta.x / Mathf.Max(1f, StrokeLength(i));
    /// <summary>Slot i's resting centre for the logos now shown, in frame space (origin at the centre, y up).</summary>
    public Vector2 SlotRest(int i) => restTo[i];

    protected override void BuildUI()
    {
        // Pre-build the slots; Initialize() decides how many to activate
        // based on the number of '+'-separated names in the tag. The timed form
        // only ever uses the first MAX_LOGOS; a logo run may fill all eight.
        for (int i = 0; i < RUN_SLOTS; i++)
        {
            GameObject slotGO = new GameObject($"BigMediaSlot_{i}", typeof(RectTransform));
            slotGO.transform.SetParent(rectTransform, false);
            RectTransform slotRT = slotGO.GetComponent<RectTransform>();
            slotRT.pivot = new Vector2(0.5f, 0.5f);

            // Image fills the slot, aspect preserved so logos never stretch.
            GameObject imgGO = new GameObject("Image", typeof(RectTransform));
            imgGO.transform.SetParent(slotRT, false);
            RectTransform imgRT = imgGO.GetComponent<RectTransform>();
            imgRT.anchorMin = Vector2.zero;
            imgRT.anchorMax = Vector2.one;
            imgRT.offsetMin = Vector2.zero;
            imgRT.offsetMax = Vector2.zero;

            Image img = imgGO.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;

            // Fallback text shown when the sprite can't be resolved.
            TextMeshProUGUI fallback = ContentCardUIBuilder.CreateText(
                slotRT, "FallbackText",
                ContentCardUIBuilder.TextPrimary,
                72f, TextAlignmentOptions.Center, FontStyles.Bold);
            ContentCardUIBuilder.SetStretch(fallback.rectTransform, 16f, 16f, 16f, 16f);
            fallback.enableAutoSizing = true;
            fallback.fontSizeMin = 36f;
            fallback.fontSizeMax = 96f;

            slotGO.SetActive(false);

            slotContainers.Add(slotRT);
            slotGroups.Add(EnsureGroup(slotRT));
            slotImages.Add(img);
            slotFallbacks.Add(fallback);
        }
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        cardAssets = assets;

        // {BigMedia:name} with NO duration opens a line-by-line logo run: the
        // grid, starting with this logo (a '+' list lands together).
        if (data.duration <= 0f)
        {
            runMode = true;
            activeSlotCount = 0;
            foreach (string part in (data.primaryText ?? string.Empty).Split('+'))
            {
                if (activeSlotCount >= RUN_SLOTS) break;
                AddRunSlot(part.Trim(), float.NegativeInfinity);
            }
            for (int i = activeSlotCount; i < RUN_SLOTS; i++)
                slotContainers[i].gameObject.SetActive(false);
            LayoutRun(snap: true);
            StepRun();
            return;
        }

        // Multi-logo syntax: names joined by '+' — e.g. "Google+Apple+Meta".
        // A single name (no '+') collapses to a single-slot layout.
        string raw = data.primaryText ?? string.Empty;
        string[] names = raw.Split('+');
        activeSlotCount = Mathf.Clamp(names.Length, 0, MAX_LOGOS);

        for (int i = 0; i < activeSlotCount; i++)
        {
            ApplySlotContent(i, names[i].Trim(), assets);
            slotContainers[i].gameObject.SetActive(true);
        }
        for (int i = activeSlotCount; i < RUN_SLOTS; i++)
            slotContainers[i].gameObject.SetActive(false);

        LayoutSlots(activeSlotCount);
    }

    private void ApplySlotContent(int index, string name, ContentCardAssets assets)
    {
        slotNames[index] = name;
        Sprite sprite = ResolveSprite(name, assets, out string usedPath);
        Image img = slotImages[index];
        TextMeshProUGUI fallback = slotFallbacks[index];

        if (sprite != null)
        {
            Debug.Log($"[BigMedia] slot {index} resolved \"{name}\" via {usedPath}");
            img.sprite = sprite;
            img.gameObject.SetActive(true);
            fallback.gameObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning($"[BigMedia] slot {index} could not resolve \"{name}\". " +
                             (runMode
                                 ? $"Looked in ContentCardAssets.logos, Resources/<folder>/{name} and the media folder's Logos/. "
                                 : $"Looked in ContentCardAssets.logos and Resources/<folder>/{name}. ") +
                             $"Showing fallback text.");
            img.gameObject.SetActive(false);
            fallback.gameObject.SetActive(true);
            string display = name;
            if (display.Length > 0)
                display = char.ToUpper(display[0]) + display.Substring(1);
            fallback.text = display;
        }
    }

    private Sprite ResolveSprite(string name, ContentCardAssets assets, out string usedPath)
    {
        usedPath = null;

        // Tier 1 — ContentCardAssets (logo dictionary, then the SO's own Resources fallback)
        if (assets != null)
        {
            Sprite s = assets.GetBigMedia(name);
            if (s != null) { usedPath = "ContentCardAssets.GetBigMedia"; return s; }
        }

        // Tier 2 — direct Resources lookup, so dropping a file into
        // Assets/Resources/Media/ is enough even without a ContentCardAssets SO.
        //
        // Important: try Texture2D BEFORE Sprite. Resources.Load<Sprite>(path)
        // returns null for PNGs imported with spriteMode = Multiple (the sprite
        // is named "X_0", not "X", so the load-by-asset-name fails). Texture2D
        // loads the underlying image regardless of importer mode, and we wrap
        // it in a Sprite — this is why ArticleTemp.jpeg worked but X.png /
        // Brave.png / Google.png didn't.
        string folder = (assets != null && !string.IsNullOrEmpty(assets.bigMediaResourcesFolder))
            ? assets.bigMediaResourcesFolder
            : "Media";
        string path = $"{folder}/{name}";

        Texture2D tex = Resources.Load<Texture2D>(path);
        if (tex != null)
        {
            usedPath = $"Resources.Load<Texture2D>(\"{path}\") + Sprite.Create";
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        Sprite sprite = Resources.Load<Sprite>(path);
        if (sprite != null) { usedPath = $"Resources.Load<Sprite>(\"{path}\")"; return sprite; }

        // Last resort — Multiple-mode atlases where the sub-sprite is the
        // first/only one in the file.
        Sprite[] all = Resources.LoadAll<Sprite>(path);
        if (all != null && all.Length > 0)
        {
            usedPath = $"Resources.LoadAll<Sprite>(\"{path}\")[0]";
            return all[0];
        }

        // The timed row ({BigMedia:a+b,4}) stops here, as it always did: a name
        // the two tiers above don't know shows as text. Old scripts must not
        // suddenly grow logos.
        if (!runMode) return null;

        // Tier 3, a logo RUN only — the media folder's Logos/ on disk (the
        // 280-odd logo files {Image:} already reads), matched without regard
        // to case.
        if (logoSource == null) logoSource = FindObjectOfType<MediaPresentationSystem>();
        Texture2D logo = logoSource != null ? logoSource.LoadLogoTexture(name) : null;
        if (logo != null)
        {
            usedPath = "the media folder's Logos/";
            Sprite made = Sprite.Create(logo, new Rect(0, 0, logo.width, logo.height), new Vector2(0.5f, 0.5f));
            ownedSprites.Add(made);
            return made;
        }

        return null;
    }

    // Horizontal row, centered within the parent. Total band widens with
    // count so each slot stays readable. Vertical band is fixed at 70% of
    // the parent so logos have a consistent visual weight.
    private void LayoutSlots(int count)
    {
        if (count <= 0) return;

        float totalWidth = BAND_WIDTHS[Mathf.Clamp(count - 1, 0, BAND_WIDTHS.Length - 1)];
        float perWidth = totalWidth / count;
        float xStart = 0.5f - totalWidth * 0.5f;
        const float yMin = 0.15f;
        const float yMax = 0.85f;

        for (int i = 0; i < count; i++)
        {
            RectTransform rt = slotContainers[i];
            rt.anchorMin = new Vector2(xStart + perWidth * i, yMin);
            rt.anchorMax = new Vector2(xStart + perWidth * (i + 1), yMax);
            rt.offsetMin = new Vector2(SLOT_HORIZONTAL_PADDING * 0.5f, 0f);
            rt.offsetMax = new Vector2(-SLOT_HORIZONTAL_PADDING * 0.5f, 0f);
        }
    }

    public override void Show()
    {
        KillCurrentSequence();

        // BigMedia owns its own entry — flatten any preset rotation and force
        // the CanvasGroup fully visible, then pop each slot in turn. Single
        // logo collapses to a single pop (no stagger to wait through).
        rectTransform.localEulerAngles = Vector3.zero;
        canvasGroup.alpha = 1f;

        if (runMode)
        {
            // A run: the opening logo(s) pop on the card clock, like every later one.
            for (int i = 0; i < activeSlotCount; i++)
            {
                slotGroups[i].alpha = 1f;
                landedAt[i] = clock;
            }
            StepRun();
            return;
        }

        Sequence seq = DOTween.Sequence();
        var join = new StaggeredJoin(seq); // each slot at stagger × i from the start — not stacked
        float dur = SlideDuration;
        float stagger = BigMediaCfg.staggerDelay;

        for (int i = 0; i < activeSlotCount; i++)
        {
            RectTransform rt = slotContainers[i];
            rt.localEulerAngles = Vector3.zero;
            rt.localScale = Vector3.zero;

            // Build the per-slot tween fully (ease) BEFORE handing it to the
            // sequence. Sequence.Insert with an AnimationCurve ease has been
            // observed to silently drop the curve in some DOTween builds, so
            // the slot is joined (Join + SetDelay applies the curve reliably)
            // — through StaggeredJoin, which gives each join only the delay
            // since the previous one; a plain SetDelay(stagger * i) per join
            // piles the delays up (logos 3 and 4 landed at 1.05 s / 2.1 s).
            join.At(stagger * i, ApplyEntryEase(rt.DOScale(Vector3.one, dur)));

            // 'Ease in + fade': the logo dissolves in while it grows, on the
            // same stagger. Overshoot: opaque from the first frame, as before.
            CanvasGroup g = slotGroups[i];
            if (UseOvershootEntry)
            {
                g.alpha = 1f;
            }
            else
            {
                g.alpha = 0f;
                join.At(stagger * i, g.DOFade(1f, EntryFadeDuration).SetEase(EntryFadeEase));
            }
        }

        seq.OnComplete(() => StartIdleFloat());
        currentSequence = seq;
    }

    // =====================================================================
    // The logo run
    // =====================================================================

    /// <summary>
    /// Adds the next logo on its word: it pops in and the grid re-centres.
    /// <paramref name="instant"/> lands it at once (its beat passed while the
    /// card waited in the queue). False when the grid already holds eight.
    /// </summary>
    public bool AppendLogo(string name, bool instant = false)
    {
        if (activeSlotCount >= RUN_SLOTS) return false;
        if (!runMode) ConvertTimedRowToRun();

        AddRunSlot((name ?? string.Empty).Trim(), instant ? float.NegativeInfinity : clock);
        LayoutRun(snap: instant);
        StepRun();
        return true;
    }

    /// <summary>
    /// Crosses out the logo called <paramref name="name"/> (matched as it was
    /// added, ignoring case): two strokes, and it dims to half. NotFound when
    /// no logo of the grid has that name; Repeated when it is struck already.
    /// </summary>
    public MarkResult Strike(string name)
    {
        for (int i = 0; i < activeSlotCount; i++)
        {
            if (!BigMediaGrid.SameName(slotNames[i], name)) continue;
            if (struck[i]) return MarkResult.Repeated;
            if (!runMode) ConvertTimedRowToRun();
            struck[i] = true;
            struckAt[i] = clock;
            BuildStrokes(i);
            StepRun();
            return MarkResult.Ok;
        }
        return MarkResult.NotFound;
    }

    public override void Hide(bool fast = false)
    {
        IsClosing = true;
        base.Hide(fast);
    }

    void Update() => Advance(Time.deltaTime);

    /// <summary>One frame of the card clock; public so the self-tests can step the run in edit mode.</summary>
    public void Advance(float dt)
    {
        clock += dt;
        if (runMode) StepRun();
    }

    void LateUpdate()
    {
        if (runMode && FrameSize() != laidOutFrame) { LayoutRun(snap: true); StepRun(); }
    }

    private Vector2 FrameSize()
    {
        Rect r = rectTransform.rect;
        return (r.width > 1f && r.height > 1f) ? r.size : new Vector2(1920f, 1080f);
    }

    private void AddRunSlot(string name, float landed)
    {
        int i = activeSlotCount;
        ApplySlotContent(i, name, cardAssets);
        RectTransform rt = slotContainers[i];
        rt.gameObject.SetActive(true);
        slotGroups[i].alpha = 1f;
        rt.localEulerAngles = Vector3.zero;
        landedAt[i] = landed;
        struck[i] = false;
        restFrom[i] = new Vector2(float.NaN, float.NaN); // no place yet: it lands at its rest
        activeSlotCount++;
    }

    // A timed row a duration-less logo joined: the same slots, now placed by
    // the grid. Each keeps the place it has on screen and eases to its new one.
    private void ConvertTimedRowToRun()
    {
        if (currentSequence != null && currentSequence.IsActive()) currentSequence.Complete(true);
        currentSequence = null;
        StopIdleFloat();

        Vector2 frame = FrameSize();
        for (int i = 0; i < activeSlotCount; i++)
        {
            RectTransform rt = slotContainers[i];
            Rect r = rt.rect;
            Vector3 c = rt.localPosition + new Vector3(r.center.x, r.center.y, 0f);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = r.size;
            rt.anchoredPosition = new Vector2(c.x, c.y);
            rt.localScale = Vector3.one;
            slotGroups[i].alpha = 1f;
            landedAt[i] = float.NegativeInfinity;
            restTo[i] = rt.anchoredPosition;
        }
        runMode = true;
        laidOutFrame = frame;
    }

    // Sizes every slot for the run's final count and sets where each one rests
    // for the logos shown now; they ease there from wherever they are (or snap).
    private void LayoutRun(bool snap)
    {
        Vector2 frame = FrameSize();
        laidOutFrame = frame;
        int sizedFor = BigMediaGrid.SizedFor(activeSlotCount, AnticipatedCount);
        Vector2 size = BigMediaGrid.SlotSize(sizedFor, frame);

        for (int i = 0; i < activeSlotCount; i++)
        {
            RectTransform rt = slotContainers[i];
            bool isNew = float.IsNaN(restFrom[i].x);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            Vector2 to = BigMediaGrid.SlotCentre(i, activeSlotCount, sizedFor, frame);
            restFrom[i] = isNew || snap ? to : rt.anchoredPosition;
            restTo[i] = to;
            if (isNew || snap) rt.anchoredPosition = to;
            if (strokes[i] != null) PlaceStrokes(i);
        }
        recentredAt = snap ? float.NegativeInfinity : clock;
    }

    private void StepRun()
    {
        float g = float.IsNegativeInfinity(recentredAt) ? 1f : BigMediaGrid.RecentreAt(clock - recentredAt);
        for (int i = 0; i < activeSlotCount; i++)
        {
            RectTransform rt = slotContainers[i];
            rt.anchoredPosition = Vector2.LerpUnclamped(restFrom[i], restTo[i], g);
            float age = float.IsNegativeInfinity(landedAt[i]) ? float.PositiveInfinity : clock - landedAt[i];
            float scale = BigMediaGrid.PopScaleAt(age);
            rt.localScale = new Vector3(scale, scale, 1f);

            if (!struck[i]) continue;
            float sAge = clock - struckAt[i];
            float a = BigMediaGrid.StruckAlphaAt(sAge);
            Color ic = slotImages[i].color; ic.a = a; slotImages[i].color = ic;
            Color fc = slotFallbacks[i].color; fc.a = a; slotFallbacks[i].color = fc;
            float len = StrokeLength(i);
            for (int k = 0; k < 2; k++)
            {
                float p = BigMediaGrid.StrikeStrokeAt(sAge, k);
                RectTransform st = strokes[i][k];
                st.gameObject.SetActive(p > 0f);
                st.sizeDelta = new Vector2(len * p, st.sizeDelta.y);
            }
        }
    }

    // The cross is drawn over a square in the slot's middle.
    private float CrossSide(int i)
    {
        Vector2 size = slotContainers[i].sizeDelta;
        return Mathf.Min(size.x, size.y) * 0.78f;
    }

    private float StrokeLength(int i) => CrossSide(i) * 1.41421356f;

    private void BuildStrokes(int i)
    {
        if (strokes[i] != null) return;
        Color red = DocumentTypography.StampColor(DocumentStamp.Allegation);
        strokes[i] = new RectTransform[2];
        for (int k = 0; k < 2; k++)
        {
            Image bar = ContentCardUIBuilder.CreateImage(slotContainers[i], k == 0 ? "StrikeA" : "StrikeB", red);
            bar.sprite = MugsTech.Style.StyleSpriteFactory.GetRoundedRect(6);
            bar.type = Image.Type.Sliced;
            RectTransform st = bar.rectTransform;
            st.anchorMin = st.anchorMax = new Vector2(0.5f, 0.5f);
            st.pivot = new Vector2(0f, 0.5f);          // drawn from its start, like a pen stroke
            strokes[i][k] = st;
            st.gameObject.SetActive(false);
        }
        PlaceStrokes(i);
    }

    private void PlaceStrokes(int i)
    {
        float half = CrossSide(i) * 0.5f;
        float thick = Mathf.Max(8f, FrameSize().y * 0.016f);
        // Stroke A: top-left → bottom-right. Stroke B: top-right → bottom-left.
        strokes[i][0].anchoredPosition = new Vector2(-half, half);
        strokes[i][0].localEulerAngles = new Vector3(0f, 0f, -45f);
        strokes[i][1].anchoredPosition = new Vector2(half, half);
        strokes[i][1].localEulerAngles = new Vector3(0f, 0f, -135f);
        for (int k = 0; k < 2; k++)
            strokes[i][k].sizeDelta = new Vector2(strokes[i][k].sizeDelta.x, thick);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        foreach (Sprite sp in ownedSprites)
            if (sp != null) { if (Application.isPlaying) Destroy(sp); else DestroyImmediate(sp); }
        ownedSprites.Clear();
    }
}
