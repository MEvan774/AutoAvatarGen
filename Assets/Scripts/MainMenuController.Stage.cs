using TMPro;
using UnityEngine.UI;

/// <summary>
/// "Type quotes on  &lt;  On  &gt;" — the same cycle-row pattern as Show chapter
/// titles, built at runtime (no scene edit). Saved to PlayerPrefs
/// <see cref="MugsTech.Stage.StagePrefs.TypeQuotesKey"/> (default On); the
/// recording scene reads it as each quote shows. On: a quote in the WIDE slot
/// (Mugs off or peeking) types on as it is spoken. Off: it shows whole, like a
/// side quote. The row sits in the Sound card only because the Look column is
/// full — it is a look setting.
/// </summary>
public partial class MainMenuController
{
    Button   typeQuotesPrevButton;
    TMP_Text typeQuotesLabel;
    Button   typeQuotesNextButton;

    void EnsureStageControls()
    {
        if (typeQuotesLabel != null) return;
        BuildCycleControls("TypeQuotes", out typeQuotesPrevButton, out typeQuotesLabel, out typeQuotesNextButton);
    }

    void WireStageRows()
    {
        if (typeQuotesPrevButton != null) typeQuotesPrevButton.onClick.AddListener(ToggleTypeQuotes);
        if (typeQuotesNextButton != null) typeQuotesNextButton.onClick.AddListener(ToggleTypeQuotes);
        UpdateStageLabels();
    }

    void ToggleTypeQuotes()
    {
        MugsTech.Stage.StagePrefs.TypeQuotes = !MugsTech.Stage.StagePrefs.TypeQuotes;
        UpdateStageLabels();
    }

    void UpdateStageLabels()
    {
        if (typeQuotesLabel != null) typeQuotesLabel.text = MugsTech.Stage.StagePrefs.TypeQuotes ? "On" : "Off";
    }
}
