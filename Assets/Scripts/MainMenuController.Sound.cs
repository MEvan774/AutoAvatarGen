using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MugsTech.Background;
using MugsTech.Style;

/// <summary>
/// The "3   Sound" card (left column, under Generation): how the take sounds, saved to PlayerPrefs as soon as
/// it changes (the recording scene reads them when the take starts).
///   • Music volume  &lt; 18 % &gt;   — the folder-mode bed's constant duck,
///     5–40 % in 2 % steps (BackgroundMusicPlayer.MusicVolume, default 18 %).
///   • Normalise loudness  &lt; On &gt; — one gain lands the take on −14 LUFS,
///     true peak under −1 dBTP (TakeLoudness). Off = the take plays at its
///     own level, exactly as before.
///   • Sound effects  &lt; 60 % &gt;  — the synthesised ticks and thuds under
///     the picture changes (MugsTech.Sound.SoundLayer), Off or 10–100 % in
///     10 % steps. Off = no effect layer at all, exactly as before.
/// Built at runtime, like the other rows — no scene edit.
/// </summary>
public partial class MainMenuController
{
    const float MusicVolumeStep = 0.02f;

    Button   musicVolumePrevButton;
    TMP_Text musicVolumeLabel;
    Button   musicVolumeNextButton;

    Button   normaliseLoudnessPrevButton;
    TMP_Text normaliseLoudnessLabel;
    Button   normaliseLoudnessNextButton;

    const float SfxVolumeStep = 0.1f;

    Button   sfxVolumePrevButton;
    TMP_Text sfxVolumeLabel;
    Button   sfxVolumeNextButton;

    void EnsureSoundControls()
    {
        if (musicVolumeLabel == null)
            BuildCycleControls("MusicVolume", out musicVolumePrevButton, out musicVolumeLabel, out musicVolumeNextButton);
        if (normaliseLoudnessLabel == null)
            BuildCycleControls("NormaliseLoudness", out normaliseLoudnessPrevButton, out normaliseLoudnessLabel, out normaliseLoudnessNextButton);
        if (sfxVolumeLabel == null)
            BuildCycleControls("SfxVolume", out sfxVolumePrevButton, out sfxVolumeLabel, out sfxVolumeNextButton);
    }

    void WireSoundRows()
    {
        if (musicVolumePrevButton != null) musicVolumePrevButton.onClick.AddListener(() => StepMusicVolume(-1));
        if (musicVolumeNextButton != null) musicVolumeNextButton.onClick.AddListener(() => StepMusicVolume(+1));
        if (normaliseLoudnessPrevButton != null) normaliseLoudnessPrevButton.onClick.AddListener(ToggleNormaliseLoudness);
        if (normaliseLoudnessNextButton != null) normaliseLoudnessNextButton.onClick.AddListener(ToggleNormaliseLoudness);
        if (sfxVolumePrevButton != null) sfxVolumePrevButton.onClick.AddListener(() => StepSfxVolume(-1));
        if (sfxVolumeNextButton != null) sfxVolumeNextButton.onClick.AddListener(() => StepSfxVolume(+1));
        UpdateSoundLabels();
    }

    void StepMusicVolume(int dir)
    {
        float v = Mathf.Round(BackgroundMusicPlayer.MusicVolume / MusicVolumeStep) * MusicVolumeStep + dir * MusicVolumeStep;
        BackgroundMusicPlayer.MusicVolume = v; // clamps to 5–40 %
        UpdateSoundLabels();
    }

    void StepSfxVolume(int dir)
    {
        float v = Mathf.Round(MugsTech.Sound.SoundLayer.Volume / SfxVolumeStep) * SfxVolumeStep + dir * SfxVolumeStep;
        MugsTech.Sound.SoundLayer.Volume = Mathf.Clamp01(v); // 0 = Off
        UpdateSoundLabels();
    }

    void ToggleNormaliseLoudness()
    {
        TakeLoudness.NormaliseEnabled = !TakeLoudness.NormaliseEnabled;
        UpdateSoundLabels();
    }

    void UpdateSoundLabels()
    {
        if (musicVolumeLabel != null)
            musicVolumeLabel.text = $"{Mathf.RoundToInt(BackgroundMusicPlayer.MusicVolume * 100f)} %";
        if (normaliseLoudnessLabel != null)
            normaliseLoudnessLabel.text = TakeLoudness.NormaliseEnabled ? "On" : "Off";
        if (sfxVolumeLabel != null)
            sfxVolumeLabel.text = MugsTech.Sound.SoundLayer.Enabled
                ? $"{Mathf.RoundToInt(MugsTech.Sound.SoundLayer.Volume * 100f)} %"
                : "Off";
    }

    void BuildSoundCard(Transform column)
    {
        if (musicVolumeLabel == null && normaliseLoudnessLabel == null && sfxVolumeLabel == null) return;
        var card = UITheme.MakeCard(column, "SoundCard");
        UITheme.MakeText(card, "Heading", "3   Sound", UITheme.TextRole.Heading);
        Hint(card, "Music volume applies to the Music folder's tracks. Normalise loudness brings every take to −14 LUFS. " +
                   "Sound effects puts a soft tick under cards and BigText lines (Off = none).");
        LookRow(card, "Music volume",       musicVolumePrevButton,       musicVolumeLabel,       musicVolumeNextButton);
        LookRow(card, "Normalise loudness", normaliseLoudnessPrevButton, normaliseLoudnessLabel, normaliseLoudnessNextButton);
        LookRow(card, "Sound effects",      sfxVolumePrevButton,         sfxVolumeLabel,         sfxVolumeNextButton);
    }
}
