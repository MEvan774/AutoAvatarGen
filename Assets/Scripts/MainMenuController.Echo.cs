using TMPro;
using UnityEngine.UI;

/// <summary>
/// "Thumbnail echo  &lt;  On  &gt;" in the Look card — the same cycle-row
/// pattern as Show chapter titles, built at runtime (no scene edit). Saves to
/// PlayerPrefs <see cref="MugsTech.Echo.EchoOverlay.PrefKey"/> (default On);
/// the recording scene reads it when the script is parsed. Off: a script's
/// {Echo:…} is still stripped, but nothing is shown.
/// </summary>
public partial class MainMenuController
{
    Button   thumbnailEchoPrevButton;
    TMP_Text thumbnailEchoLabel;
    Button   thumbnailEchoNextButton;

    void EnsureThumbnailEchoControls()
    {
        if (thumbnailEchoLabel != null) return;
        BuildCycleControls("ThumbnailEcho", out thumbnailEchoPrevButton, out thumbnailEchoLabel, out thumbnailEchoNextButton);
    }

    void WireThumbnailEchoRow()
    {
        if (thumbnailEchoPrevButton != null) thumbnailEchoPrevButton.onClick.AddListener(ToggleThumbnailEcho);
        if (thumbnailEchoNextButton != null) thumbnailEchoNextButton.onClick.AddListener(ToggleThumbnailEcho);
        UpdateThumbnailEchoLabel();
    }

    void ToggleThumbnailEcho()
    {
        MugsTech.Echo.EchoOverlay.Enabled = !MugsTech.Echo.EchoOverlay.Enabled;
        UpdateThumbnailEchoLabel();
    }

    void UpdateThumbnailEchoLabel()
    {
        if (thumbnailEchoLabel != null) thumbnailEchoLabel.text = MugsTech.Echo.EchoOverlay.Enabled ? "On" : "Off";
    }
}
