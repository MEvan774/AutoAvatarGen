using TMPro;
using UnityEngine.UI;

/// <summary>
/// "Show chapter titles  <  On  >" in the Look card — the same cycle-row
/// pattern as Card animation, built at runtime (no scene edit). Saves to
/// PlayerPrefs <see cref="ChapterTitles.ShowPrefKey"/> (default On); the
/// recording scene reads it when the script is parsed. Off renders a take
/// exactly as before the titles existed.
/// </summary>
public partial class MainMenuController
{
    Button   chapterTitlesPrevButton;
    TMP_Text chapterTitlesLabel;
    Button   chapterTitlesNextButton;

    void EnsureChapterTitleControls()
    {
        if (chapterTitlesLabel != null) return;
        BuildCycleControls("ChapterTitles", out chapterTitlesPrevButton, out chapterTitlesLabel, out chapterTitlesNextButton);
    }

    void WireChapterTitleRow()
    {
        if (chapterTitlesPrevButton != null) chapterTitlesPrevButton.onClick.AddListener(ToggleChapterTitles);
        if (chapterTitlesNextButton != null) chapterTitlesNextButton.onClick.AddListener(ToggleChapterTitles);
        UpdateChapterTitlesLabel();
    }

    void ToggleChapterTitles()
    {
        ChapterTitles.Enabled = !ChapterTitles.Enabled;
        UpdateChapterTitlesLabel();
    }

    void UpdateChapterTitlesLabel()
    {
        if (chapterTitlesLabel != null) chapterTitlesLabel.text = ChapterTitles.Enabled ? "On" : "Off";
    }
}
