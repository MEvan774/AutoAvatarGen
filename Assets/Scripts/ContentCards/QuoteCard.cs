using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using MugsTech.Stage;

/// <summary>
/// Displays a quote with a large decorative opening mark and attribution.
/// Tag: {Quote:"quote text","person name","role/title",duration|Start[,Photo=image_name][,Left|,Right]}
/// Self-building: constructs its own UI hierarchy in Awake.
///
/// Typography (see <see cref="CardTypography"/>): the quote, left-aligned and
/// upright, auto-fits 56–80 px (warning under 44); the name (bold) and role
/// sit beneath it, and the three form one vertically centred group. One big
/// translucent “ in the accent colour sits behind the quote's top-left.
///
/// ,Photo=image_name — a round portrait (an image file, looked up the way
/// {Image:} looks one up; in practice the CEO/ folder). The card then lays out
/// portrait + name + role on ONE ROW with the quote beneath, in the side slot
/// and the wide one alike. A name that doesn't resolve: no portrait, a warning,
/// and the usual layout. The portrait is never chosen from the person's name.
///
/// Type-on — the WIDE slot only (Mugs off or peeking), main menu "Type quotes
/// on" (default on): the quote's characters appear in order, so the viewer
/// reads it as it is said. A held quote spreads them evenly between its Start
/// and 0.3 s before its End, at 12–30 characters a second; a timed one types at
/// 16 a second. The portrait, name and role are there from the first frame, and
/// the text box is laid out for the FULL quote from the start, so no line ever
/// re-wraps while it types. A side-slot quote shows whole, as it always has.
/// </summary>
public class QuoteCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.Quote;

    // ---- The portrait row (px of the 1000-px-wide reference card) -----------------
    public const float PortraitPx = 150f;
    public const float PortraitToTextGapPx = 28f;
    public const float PortraitRingAlpha = 1f;

    private TextMeshProUGUI quoteText;
    private TextMeshProUGUI personName;
    private TextMeshProUGUI roleTitle;
    private TextMeshProUGUI quoteMarkText;
    private RectTransform quoteMark;

    private CircleImage portrait;
    private Image portraitRing;
    private Texture2D ownedPhoto;       // decoded for this card only — destroyed with it
    private bool hasPhoto;
    private string photoName;

    private Vector2 laidOutSize = new Vector2(-1f, -1f);

    // ---- Type-on ---------------------------------------------------------------------
    private bool typing;
    private float typeClock;
    private float typeWindow = float.NaN;
    private int typeCharacters;
    private float heldEndTime = float.NaN, shownTriggerTime = float.NaN;
    private bool isHeld;

    /// <summary>The last layout (sizes, warnings) — read by the self-tests.</summary>
    public CardTypography.Report Layout { get; private set; }

    // ---- Read by the self-tests ------------------------------------------------------
    public bool HasPortrait => hasPhoto;
    public CircleImage Portrait => portrait;
    public TextMeshProUGUI QuoteText => quoteText;
    public TextMeshProUGUI NameText => personName;
    public TextMeshProUGUI RoleText => roleTitle;
    /// <summary>True while the quote is still typing on.</summary>
    public bool IsTyping => typing;
    /// <summary>Characters of the quote on screen now (all of them when it isn't typing).</summary>
    public int VisibleCharacters => typing ? Mathf.Min(quoteText.maxVisibleCharacters, typeCharacters) : CharacterCount();
    public int TotalCharacters => typing ? typeCharacters : CharacterCount();
    public float TypeWindow => typeWindow;

    protected override void BuildUI()
    {
        ContentCardUIBuilder.CreateBackground(rectTransform);

        // Decorative opening mark — created first so it renders BEHIND the quote.
        Color markColor = ContentCardUIBuilder.AccentColor;
        markColor.a = CardTypography.QuoteMarkAlpha;
        quoteMarkText = ContentCardUIBuilder.CreateText(
            rectTransform, "QuoteMark", markColor,
            CardTypography.QuoteMarkPx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(quoteMarkText, CardTypography.Weight.Heavy, TextAlignmentOptions.TopLeft);
        quoteMarkText.textWrappingMode = TextWrappingModes.NoWrap;
        quoteMarkText.text = "“";
        quoteMark = quoteMarkText.rectTransform;

        quoteText = ContentCardUIBuilder.CreateText(
            rectTransform, "QuoteText", ContentCardUIBuilder.TextPrimary,
            CardTypography.BodyMaxPx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(quoteText, CardTypography.Weight.Regular, TextAlignmentOptions.TopLeft);

        personName = ContentCardUIBuilder.CreateText(
            rectTransform, "PersonName", ContentCardUIBuilder.TextPrimary,
            CardTypography.NamePx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(personName, CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft);

        roleTitle = ContentCardUIBuilder.CreateText(
            rectTransform, "RoleTitle", ContentCardUIBuilder.TextSecondary,
            CardTypography.RolePx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(roleTitle, CardTypography.Weight.Regular, TextAlignmentOptions.TopLeft);

        // The round portrait and the soft ring over its rim — hidden unless the
        // tag carries a ,Photo= that resolves.
        var pgo = new GameObject("Portrait", typeof(RectTransform));
        pgo.transform.SetParent(rectTransform, false);
        portrait = pgo.AddComponent<CircleImage>();
        portrait.raycastTarget = false;
        RectTransform prt = portrait.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0f, 1f);
        prt.pivot = new Vector2(0f, 1f);

        Color ring = ContentCardUIBuilder.AccentColor;
        ring.a = PortraitRingAlpha;
        portraitRing = ContentCardUIBuilder.CreateImage(prt, "Ring", ring);
        // Its sprite is made when a portrait is first shown (Initialize): a
        // quote without ,Photo= must not pay for a texture it never draws.
        ContentCardUIBuilder.SetStretch(portraitRing.rectTransform, -2f, -2f, -2f, -2f);
        pgo.SetActive(false);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        quoteText.text = data.primaryText ?? string.Empty;
        personName.text = data.secondaryText ?? string.Empty;
        roleTitle.text = data.tertiaryText ?? string.Empty;

        isHeld = data.duration <= 0f;
        heldEndTime = data.endTime;
        shownTriggerTime = data.triggerTime;

        photoName = string.IsNullOrWhiteSpace(data.photoName) ? null : data.photoName.Trim();
        hasPhoto = false;
        if (photoName != null)
        {
            Texture2D tex = null;
            bool owned = false;
            var mps = FindObjectOfType<MediaPresentationSystem>();
            if (mps != null) tex = mps.LoadImageTexture(photoName, out owned);
            if (tex == null) tex = Resources.Load<Texture2D>($"Media/{photoName}");
            if (tex != null)
            {
                portrait.Texture = tex;
                ownedPhoto = owned ? tex : null;
                hasPhoto = true;
            }
            else
            {
                Debug.LogWarning($"[Card] {CardTypography.CardLabel("Quote", quoteText.text)}: the photo \"{photoName}\" wasn't found in the " +
                                 "media folders — shown without a portrait.");
            }
        }
        if (hasPhoto && portraitRing.sprite == null) portraitRing.sprite = ProceduralSprites.Ring();
        portrait.gameObject.SetActive(hasPhoto);

        LayoutCard(log: true);
    }

    void LateUpdate()
    {
        // Re-lay out only if the card's size changed after Initialize (its rect
        // wasn't resolved yet) — the slide moves the card, it never resizes it.
        if (CardTypography.CardSize(rectTransform) != laidOutSize)
            LayoutCard(log: false);
    }

    // The side card lays out exactly as it always has. In the wide slot the
    // scale is stepped down until the card's own report says it fits (FitWide).
    private void LayoutCard(bool log)
    {
        if (!IsWideLayout) { LayoutCardOnce(log); return; }
        FitWide(() => LayoutCardOnce(false), () => Layout);
        if (log) LayoutCardOnce(true);
    }

    private void LayoutCardOnce(bool log)
    {
        Vector2 size = CardTypography.CardSize(rectTransform);
        laidOutSize = size;
        float k = LayoutScale(size);
        float pad = CardTypography.PaddingFor(size);

        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Quote", quoteText.text) };
        float top = hasPhoto ? LayoutWithPortrait(size, k, pad, report) : LayoutPlain(report);

        PlaceQuoteMark(k, pad, top, hasPhoto ? portraitBottom + 4f * k : pad);
        report.Add("mark", quoteMarkText, quoteMarkText.fontSize / k, CardTypography.HardFloorPx);

        Layout = report;
        if (log) report.LogSummary();
    }

    // The layout the card has always had: the quote, then the name, then the
    // role, as one vertically centred group.
    private float LayoutPlain(CardTypography.Report report)
    {
        var body = new CardTypography.Row { Name = "quote", Text = quoteText };
        var rows = new List<CardTypography.Row>
        {
            new CardTypography.Row { Name = "name", Text = personName, SizePx = CardTypography.NamePx,
                                     FloorPx = CardTypography.NameFloorPx, GapAbovePx = CardTypography.BodyToAttributionGapPx },
            new CardTypography.Row { Name = "role", Text = roleTitle, SizePx = CardTypography.RolePx,
                                     FloorPx = CardTypography.RoleFloorPx,
                                     GapAbovePx = personName.text.Length > 0 ? CardTypography.NameToRoleGapPx
                                                                              : CardTypography.BodyToAttributionGapPx },
        };
        return CardTypography.LayoutBodyGroup(rectTransform, report, body,
            CardTypography.BodyMinPx, CardTypography.BodyMaxPx, CardTypography.BodyWarnFloorPx, rows);
    }

    private float portraitBottom;

    // With a portrait: [portrait] [name over role] on one row, the quote
    // beneath, the two as one vertically centred group. Returns the quote's top.
    private float LayoutWithPortrait(Vector2 size, float k, float pad, CardTypography.Report report)
    {
        float innerW = size.x - 2f * pad, innerH = size.y - 2f * pad;
        float d = PortraitPx * k;
        float textX = pad + d + PortraitToTextGapPx * k;
        float textW = innerW - d - PortraitToTextGapPx * k;

        float nameSize = CardTypography.NamePx * k, roleSize = CardTypography.RolePx * k;
        float nameH = personName.text.Length > 0 ? CardTypography.MeasureHeight(personName, nameSize, textW) : 0f;
        float roleH = roleTitle.text.Length > 0 ? CardTypography.MeasureHeight(roleTitle, roleSize, textW) : 0f;
        float between = nameH > 0f && roleH > 0f ? CardTypography.NameToRoleGapPx * k : 0f;
        float textBlock = nameH + between + roleH;
        float rowH = Mathf.Max(d, textBlock);
        float gap = CardTypography.BodyToAttributionGapPx * k;

        string bodyText = quoteText.text;
        TMP_FontAsset bodyFont = quoteText.font;
        CardTypography.BodyFit fit = CardTypography.FitBody(
            s => CardTypography.MeasureHeight(quoteText, s, innerW) + rowH + gap <= innerH + 0.5f &&
                 CardTypography.WidestWordWidth(bodyFont, bodyText, s) <= innerW,
            CardTypography.BodyMinPx * k, CardTypography.BodyMaxPx * k,
            CardTypography.BodyWarnFloorPx * k, CardTypography.HardFloorPx * k);

        float bodyH = CardTypography.MeasureHeight(quoteText, fit.Size, innerW);
        report.Add("quote", quoteText, fit.Size / k, CardTypography.BodyWarnFloorPx);
        if (fit.Overflows)
            report.Warn($"{report.Card}: quote doesn't fit even at {CardTypography.HardFloorPx:F0} px — it overflows the card's padding.");
        else if (fit.BelowWarnFloor)
            report.Warn($"{report.Card}: quote fits only at {fit.Size / k:F0} px — below the {CardTypography.BodyWarnFloorPx:F0} px floor.");

        float groupH = rowH + gap + bodyH;
        float groupTop = pad + Mathf.Max(0f, (innerH - groupH) * 0.5f);
        report.GroupHeight = groupH;
        report.InnerHeight = innerH;

        // The row: the portrait, and the name / role centred against it.
        RectTransform prt = portrait.rectTransform;
        prt.sizeDelta = new Vector2(d, d);
        prt.anchoredPosition = new Vector2(pad, -(groupTop + (rowH - d) * 0.5f));
        portrait.SetVerticesDirty();
        portraitBottom = groupTop + rowH;

        float y = groupTop + (rowH - textBlock) * 0.5f;
        if (nameH > 0f)
        {
            personName.fontSize = nameSize;
            CardTypography.PlaceBox(personName, textX, y, textW, nameH);
            report.Add("name", personName, CardTypography.NamePx, CardTypography.NameFloorPx);
            y += nameH + between;
        }
        if (roleH > 0f)
        {
            roleTitle.fontSize = roleSize;
            CardTypography.PlaceBox(roleTitle, textX, y, textW, roleH);
            report.Add("role", roleTitle, CardTypography.RolePx, CardTypography.RoleFloorPx);
        }

        float bodyTop = groupTop + rowH + gap;
        quoteText.fontSize = fit.Size;
        CardTypography.PlaceRow(quoteText, pad, bodyTop, bodyH);
        return bodyTop;
    }

    // The mark's ink sits at the inner area's left edge, rising
    // QuoteMarkRise of its own height above the quote's first line — but never
    // above the inner padding (or, with a portrait, above the row over it).
    private void PlaceQuoteMark(float k, float pad, float bodyTop, float ceiling)
    {
        quoteMarkText.fontSize = CardTypography.QuoteMarkPx * k;
        float boxW = quoteMarkText.fontSize * 1.2f, boxH = quoteMarkText.fontSize * 1.4f;
        CardTypography.PlaceBox(quoteMarkText, pad, pad, boxW, boxH);

        Rect ink = CardTypography.InkInCard(quoteMarkText, rectTransform);
        if (ink.width <= 0f) return;

        float targetTop = Mathf.Max(ceiling, bodyTop - ink.height * CardTypography.QuoteMarkRise);
        float dx = pad - ink.xMin;
        float dy = targetTop - ink.yMin;
        CardTypography.PlaceBox(quoteMarkText, pad + dx, pad + dy, boxW, boxH);
    }

    public override void Show()
    {
        if (quoteMark != null)
        {
            quoteMark.localScale = Vector3.one * 0.95f;
            quoteMark.DOScale(Vector3.one, FadeInDuration).SetEase(Ease.OutQuad);
        }

        base.Show();

        // Wide slot only: the words arrive as they are spoken.
        if (IsWideLayout && StagePrefs.TypeQuotes)
        {
            float shownAt = MugsTech.Background.TakeLoudness.VoiceTime;
            if (float.IsNaN(shownAt)) shownAt = shownTriggerTime;
            BeginTypeOn(isHeld ? TypeOn.Window(shownAt, heldEndTime) : float.NaN);
        }
    }

    /// <summary>
    /// Starts the type-on: <paramref name="window"/> seconds to spread the
    /// quote over (NaN: 16 characters a second). Public so the self-tests can
    /// start one in edit mode.
    /// </summary>
    public void BeginTypeOn(float window)
    {
        typeCharacters = CharacterCount();
        if (typeCharacters <= 0) return;
        typeWindow = window;
        typeClock = 0f;
        typing = true;
        quoteText.maxVisibleCharacters = 0;
        Debug.Log($"[Card] Quote types on: {typeCharacters} characters at {TypeOn.Rate(typeCharacters, window):F0}/s" +
                  (float.IsNaN(window) ? "" : $" (window {Mathf.Max(0f, window):F1}s)"));
    }

    private int CharacterCount()
    {
        quoteText.ForceMeshUpdate(true, true);
        return quoteText.textInfo.characterCount;
    }

    void Update()
    {
        if (typing) Advance(Time.deltaTime);
    }

    /// <summary>One frame of the type-on; public so the self-tests can step it in edit mode.</summary>
    public void Advance(float dt)
    {
        if (!typing) return;
        typeClock += dt;
        int visible = TypeOn.VisibleAt(typeClock, typeCharacters, typeWindow);
        if (visible >= typeCharacters)
        {
            typing = false;
            quoteText.maxVisibleCharacters = 99999;
        }
        else quoteText.maxVisibleCharacters = visible;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (ownedPhoto != null)
        {
            if (Application.isPlaying) Destroy(ownedPhoto); else DestroyImmediate(ownedPhoto);
            ownedPhoto = null;
        }
    }
}
