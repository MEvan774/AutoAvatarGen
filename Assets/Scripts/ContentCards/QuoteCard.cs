using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Displays a quote with a large decorative opening mark and attribution.
/// Tag: {Quote:"quote text","person name","role/title",duration}
/// Self-building: constructs its own UI hierarchy in Awake.
///
/// Typography (see <see cref="CardTypography"/>): the quote, left-aligned and
/// upright, auto-fits 56–80 px (warning under 44); the name (bold) and role
/// sit beneath it, and the three form one vertically centred group. One big
/// translucent “ in the accent colour sits behind the quote's top-left.
/// </summary>
public class QuoteCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.Quote;

    private TextMeshProUGUI quoteText;
    private TextMeshProUGUI personName;
    private TextMeshProUGUI roleTitle;
    private TextMeshProUGUI quoteMarkText;
    private RectTransform quoteMark;

    private Vector2 laidOutSize = new Vector2(-1f, -1f);

    /// <summary>The last layout (sizes, warnings) — read by the self-tests.</summary>
    public CardTypography.Report Layout { get; private set; }

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
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        quoteText.text = data.primaryText ?? string.Empty;
        personName.text = data.secondaryText ?? string.Empty;
        roleTitle.text = data.tertiaryText ?? string.Empty;
        LayoutCard(log: true);
    }

    void LateUpdate()
    {
        // Re-lay out only if the card's size changed after Initialize (its rect
        // wasn't resolved yet) — the slide moves the card, it never resizes it.
        if (CardTypography.CardSize(rectTransform) != laidOutSize)
            LayoutCard(log: false);
    }

    private void LayoutCard(bool log)
    {
        Vector2 size = CardTypography.CardSize(rectTransform);
        laidOutSize = size;
        float k = CardTypography.ScaleFor(size);
        float pad = CardTypography.PaddingFor(size);

        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Quote", quoteText.text) };
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
        float top = CardTypography.LayoutBodyGroup(rectTransform, report, body,
            CardTypography.BodyMinPx, CardTypography.BodyMaxPx, CardTypography.BodyWarnFloorPx, rows);

        PlaceQuoteMark(k, pad, top);
        report.Add("mark", quoteMarkText, quoteMarkText.fontSize / k, CardTypography.HardFloorPx);

        Layout = report;
        if (log) report.LogSummary();
    }

    // The mark's ink sits at the inner area's left edge, rising
    // QuoteMarkRise of its own height above the quote's first line — but never
    // above the inner padding.
    private void PlaceQuoteMark(float k, float pad, float bodyTop)
    {
        quoteMarkText.fontSize = CardTypography.QuoteMarkPx * k;
        float boxW = quoteMarkText.fontSize * 1.2f, boxH = quoteMarkText.fontSize * 1.4f;
        CardTypography.PlaceBox(quoteMarkText, pad, pad, boxW, boxH);

        Rect ink = CardTypography.InkInCard(quoteMarkText, rectTransform);
        if (ink.width <= 0f) return;

        float targetTop = Mathf.Max(pad, bodyTop - ink.height * CardTypography.QuoteMarkRise);
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
    }
}
