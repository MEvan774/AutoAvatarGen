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

    // "Side move  <  Glide  >" — how Mugs gets between Left, Right and Center:
    // Glide (the default, and the move every existing video was made with) or
    // Dash. Saved to PlayerPrefs StagePrefs.SideMoveKey; the recording scene
    // reads it when a take starts. Like Type quotes on, it sits in the Sound
    // card because the Look column is full.
    Button   sideMovePrevButton;
    TMP_Text sideMoveLabel;
    Button   sideMoveNextButton;

    void EnsureStageControls()
    {
        if (typeQuotesLabel == null)
            BuildCycleControls("TypeQuotes", out typeQuotesPrevButton, out typeQuotesLabel, out typeQuotesNextButton);
        if (sideMoveLabel == null)
            BuildCycleControls("SideMove", out sideMovePrevButton, out sideMoveLabel, out sideMoveNextButton);
    }

    void WireStageRows()
    {
        if (typeQuotesPrevButton != null) typeQuotesPrevButton.onClick.AddListener(ToggleTypeQuotes);
        if (typeQuotesNextButton != null) typeQuotesNextButton.onClick.AddListener(ToggleTypeQuotes);
        if (sideMovePrevButton != null) sideMovePrevButton.onClick.AddListener(ToggleSideMove);
        if (sideMoveNextButton != null) sideMoveNextButton.onClick.AddListener(ToggleSideMove);
        UpdateStageLabels();
    }

    void ToggleSideMove()
    {
        MugsTech.Stage.StagePrefs.SideMoveIsDash = !MugsTech.Stage.StagePrefs.SideMoveIsDash;
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
        if (sideMoveLabel != null) sideMoveLabel.text = MugsTech.Stage.StagePrefs.SideMoveIsDash ? "Dash" : "Glide";
    }
}
