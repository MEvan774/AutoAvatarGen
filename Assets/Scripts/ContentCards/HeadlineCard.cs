using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays a bold headline with source attribution.
/// Tag: {Headline:"headline text","source name",duration}
/// Self-building: constructs its own UI hierarchy in Awake.
///
/// Typography (see <see cref="CardTypography"/>): the headline (bold,
/// left-aligned) auto-fits 56–90 px with the source row (optional logo + name)
/// beneath it, as one vertically centred group. No line cap — the headline
/// renders in full.
/// </summary>
public class HeadlineCard : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.Headline;

    private TextMeshProUGUI headlineText;
    private TextMeshProUGUI sourceText;
    private Image sourceLogo;

    private Vector2 laidOutSize = new Vector2(-1f, -1f);

    /// <summary>The last layout (sizes, warnings) — read by the self-tests.</summary>
    public CardTypography.Report Layout { get; private set; }

    protected override void BuildUI()
    {
        ContentCardUIBuilder.CreateBackground(rectTransform);

        headlineText = ContentCardUIBuilder.CreateText(
            rectTransform, "HeadlineText",
            ContentCardUIBuilder.TextPrimary,
            CardTypography.HeadlineMaxPx, TextAlignmentOptions.TopLeft);
        CardTypography.PrepareText(headlineText, CardTypography.Weight.Bold, TextAlignmentOptions.TopLeft);

        // Source logo (hidden unless the source name maps to one)
        GameObject logoGO = new GameObject("SourceLogo", typeof(RectTransform));
        logoGO.transform.SetParent(rectTransform, false);
        sourceLogo = logoGO.AddComponent<Image>();
        sourceLogo.preserveAspect = true;
        sourceLogo.raycastTarget = false;
        logoGO.SetActive(false);

        sourceText = ContentCardUIBuilder.CreateText(
            rectTransform, "SourceText",
            ContentCardUIBuilder.TextTertiary,
            CardTypography.HeadlineSourcePx, TextAlignmentOptions.MidlineLeft);
        CardTypography.PrepareText(sourceText, CardTypography.Weight.Regular, TextAlignmentOptions.TopLeft);
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        headlineText.text = data.primaryText ?? string.Empty;
        sourceText.text = data.secondaryText ?? string.Empty;

        Sprite logo = assets != null && !string.IsNullOrEmpty(data.secondaryText)
            ? assets.GetLogo(data.secondaryText) : null;
        sourceLogo.sprite = logo;
        sourceLogo.gameObject.SetActive(logo != null);

        LayoutCard(log: true);
    }

    void LateUpdate()
    {
        // Only when the card's size changed after Initialize — the slide moves
        // the card, it never resizes it.
        if (CardTypography.CardSize(rectTransform) != laidOutSize)
            LayoutCard(log: false);
    }

    private void LayoutCard(bool log)
    {
        Vector2 size = CardTypography.CardSize(rectTransform);
        laidOutSize = size;
        float k = CardTypography.ScaleFor(size);
        float pad = CardTypography.PaddingFor(size);

        bool hasLogo = sourceLogo.gameObject.activeSelf;
        float logoSide = CardTypography.HeadlineSourcePx * CardTypography.HeadlineLogoToSource * k;
        float logoGap = 12f * k;

        var report = new CardTypography.Report { Card = CardTypography.CardLabel("Headline", headlineText.text) };
        var body = new CardTypography.Row { Name = "headline", Text = headlineText };
        var sourceRow = new CardTypography.Row
        {
            Name = "source", Text = sourceText, SizePx = CardTypography.HeadlineSourcePx,
            FloorPx = CardTypography.HeadlineSourceFloorPx, GapAbovePx = CardTypography.BodyToAttributionGapPx,
            Indent = hasLogo ? logoSide + logoGap : 0f,
            MinHeight = hasLogo ? logoSide : 0f,
        };
        var rows = new List<CardTypography.Row> { sourceRow };

        CardTypography.LayoutBodyGroup(rectTransform, report, body,
            CardTypography.HeadlineMinPx, CardTypography.HeadlineMaxPx, CardTypography.HeadlineWarnFloorPx, rows);

        if (hasLogo)
        {
            // Logo square at the start of the source row, centred on the row.
            RectTransform lrt = sourceLogo.rectTransform;
            RectTransform srt = sourceText.rectTransform;
            float rowTop = -srt.offsetMax.y - (sourceRow.Height - sourceRow.TextHeight) * 0.5f;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 1f);
            lrt.sizeDelta = new Vector2(logoSide, logoSide);
            lrt.anchoredPosition = new Vector2(pad, -(rowTop + (sourceRow.Height - logoSide) * 0.5f));
        }

        Layout = report;
        if (log) report.LogSummary();
    }
}
