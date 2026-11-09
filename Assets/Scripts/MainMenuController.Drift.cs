using TMPro;
using UnityEngine.UI;

/// <summary>
/// "Idle camera drift  &lt;  On  &gt;" in the Look card — the same cycle-row
/// pattern as Show chapter titles, built at runtime (no scene edit). Saves to
/// PlayerPrefs <see cref="MugsTech.Motion.IdleDrift.PrefKey"/> (default On);
/// the recording scene reads it when the take starts. Off: no drift object
/// at all, so the camera only moves for the script's own zoom tags.
/// </summary>
public partial class MainMenuController
{
    Button   idleDriftPrevButton;
    TMP_Text idleDriftLabel;
    Button   idleDriftNextButton;

    void EnsureIdleDriftControls()
    {
        if (idleDriftLabel != null) return;
        BuildCycleControls("IdleDrift", out idleDriftPrevButton, out idleDriftLabel, out idleDriftNextButton);
    }

    void WireIdleDriftRow()
    {
        if (idleDriftPrevButton != null) idleDriftPrevButton.onClick.AddListener(ToggleIdleDrift);
        if (idleDriftNextButton != null) idleDriftNextButton.onClick.AddListener(ToggleIdleDrift);
        UpdateIdleDriftLabel();
    }

    void ToggleIdleDrift()
    {
        MugsTech.Motion.IdleDrift.Enabled = !MugsTech.Motion.IdleDrift.Enabled;
        UpdateIdleDriftLabel();
    }

    void UpdateIdleDriftLabel()
    {
        if (idleDriftLabel != null) idleDriftLabel.text = MugsTech.Motion.IdleDrift.Enabled ? "On" : "Off";
    }
}
