using TMPro;
using UnityEngine.UI;

/// <summary>
/// "Captions in gaps  &lt;  On  &gt;" and "Highlight words  &lt;  On  &gt;" in the
/// Look card — the same cycle-row pattern as Show chapter titles, built at
/// runtime (no scene edit). Saved to PlayerPrefs
/// <see cref="MugsTech.Captions.CaptionOverlay.PrefKey"/> /
/// <see cref="MugsTech.Captions.CaptionOverlay.HighlightPrefKey"/> (both
/// default On); the recording scene reads them when the script is parsed.
/// Captions off: nothing is drawn. Highlight off: each phrase shows plainly in white.
/// </summary>
public partial class MainMenuController
{
    Button   captionsPrevButton;
    TMP_Text captionsLabel;
    Button   captionsNextButton;

    Button   captionHighlightPrevButton;
    TMP_Text captionHighlightLabel;
    Button   captionHighlightNextButton;

    void EnsureCaptionControls()
    {
        if (captionsLabel == null)
            BuildCycleControls("CaptionsInGaps", out captionsPrevButton, out captionsLabel, out captionsNextButton);
        if (captionHighlightLabel == null)
            BuildCycleControls("CaptionHighlight", out captionHighlightPrevButton, out captionHighlightLabel, out captionHighlightNextButton);
    }

    void WireCaptionRows()
    {
        if (captionsPrevButton != null) captionsPrevButton.onClick.AddListener(ToggleCaptions);
        if (captionsNextButton != null) captionsNextButton.onClick.AddListener(ToggleCaptions);
        if (captionHighlightPrevButton != null) captionHighlightPrevButton.onClick.AddListener(ToggleCaptionHighlight);
        if (captionHighlightNextButton != null) captionHighlightNextButton.onClick.AddListener(ToggleCaptionHighlight);
        UpdateCaptionLabels();
    }

    void ToggleCaptions()
    {
        MugsTech.Captions.CaptionOverlay.Enabled = !MugsTech.Captions.CaptionOverlay.Enabled;
        UpdateCaptionLabels();
    }

    void ToggleCaptionHighlight()
    {
        MugsTech.Captions.CaptionOverlay.HighlightEnabled = !MugsTech.Captions.CaptionOverlay.HighlightEnabled;
        UpdateCaptionLabels();
    }

    void UpdateCaptionLabels()
    {
        if (captionsLabel != null)
            captionsLabel.text = MugsTech.Captions.CaptionOverlay.Enabled ? "On" : "Off";
        if (captionHighlightLabel != null)
            captionHighlightLabel.text = MugsTech.Captions.CaptionOverlay.HighlightEnabled ? "On" : "Off";
    }
}
