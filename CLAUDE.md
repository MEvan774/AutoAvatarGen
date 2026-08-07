# AutoAvatarGen

Unity 6000.3.6f1 (Windows) app that turns a tagged text script into a finished
avatar video: ElevenLabs TTS → timed markers → an avatar "performs" the script
in a recording scene while Evereal VideoCapture records → muxed mp4.

## Pipeline (two phases, both in-app)

1. **TTS** — `TtsGenerationJob` (Assets/Scripts/Tts) splits the script on
   `## SECTION` headings, renders each via ElevenLabs, and writes
   mp3 + `_timed.txt` + `manifest.json` into a timestamped *generation folder*
   under the output library root (PlayerPref `AutoAvatarGen.PythonOutputFolder`).
   The chosen generation is stored in PlayerPref `AutoAvatarGen.SelectedGeneration`.
2. **Recording** — `RecordingSession.Begin()` swaps to `SampleScene`;
   `ScriptFileReader` auto-loads the selected generation and dispatches to
   `MediaPresentationSystem`/`HybridAvatarSystem`, which starts
   `CrossPlatformRecorder` (Evereal). Terminal outcome lands in the static
   `RecordingSession.LastResult` (Saved + path, or Failed + error).

## Headless-style automation (Claude Cowork)

`AutoAvatarGen.exe --auto-script <file>` runs the full pipeline unattended and
quits (driver: `Assets/Scripts/Automation/AutomationRunner.cs`). Use the
wrapper — see the `generate-video` skill (.claude/skills/generate-video):

```
python Automation/generate_video.py path\to\script.txt
```

Real GPU rendering is required, so the window opens and a run takes about the
video's length; there is no true `-batchmode` mode. Each run spends ElevenLabs
credits. One run at a time.

## Script format

Tags (`{Timestamp:"..."}`, emotions, media, …) are documented in
`Assets/Python/script/SCRIPT_TAG_GUIDE.md`. A new tag must be handled in the
parser AND both TTS marker processors AND the guide, or it breaks silently.

## Hard-won constraints (do not undo)

- **MainMenu scene is hand-tweaked** — never re-bake/regenerate the menu
  hierarchies from editor tools; edit the scene/controllers directly.
  `UITheme.cs` restyles buttons at runtime.
- **The Evereal capture rig must survive the scene swap** back to the menu —
  destroying/deactivating it at hand-off silently produces an audio-less mp4
  plus an orphan wav. `RecordingSession` owns this dance; don't "simplify" it.
- **uNvEncoder is D3D11-only** — `CrossPlatformRecorder.ConfigureVideoCapture`
  guards against the NVIDIA encoder under D3D12 (access violation). Don't
  bypass the guard.
- Recorder configuration happens in `StartRecordingWithAudio`, not `Awake`
  (the component's Awake never fires in the recording flow).
