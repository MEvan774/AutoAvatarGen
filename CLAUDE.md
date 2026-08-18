# AutoAvatarGen

Unity 6000.3.6f1 (Windows) app that turns a tagged text script into a finished
avatar video: ElevenLabs TTS → timed markers → an avatar "performs" the script
in a recording scene while Evereal VideoCapture records → muxed mp4.

## Pipeline (two phases, both in-app)

1. **TTS** — `TtsGenerationJob` (Assets/Scripts/Tts) splits the script on
   `## SECTION` headings, then renders the voice as **two long takes** and
   slices them back into one file per section (`ChunkedTtsGenerationJob`).
   Output is unchanged: mp3 + `_timed.txt` + `manifest.json` (+ a
   `render_manifest.json` debug trail and the raw takes under `chunks/`) in a
   timestamped *generation folder* under the output library root (PlayerPref
   `AutoAvatarGen.PythonOutputFolder`). The chosen generation is stored in
   PlayerPref `AutoAvatarGen.SelectedGeneration`. Entry points: the in-app TTS
   panel, `--auto-script`, and **MugsTech ▸ TTS ▸ Chunked Pipeline** in the
   editor. `ffmpeg` on PATH (or `AutoAvatarGen.FfmpegPath`) is used for
   loudness and mp3 encoding; without it the pipeline still runs and writes WAV.
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
- **eleven_v3's synthesis alignment lies** — measured ±1.3s mid-segment and 2s
  short at a tail, which cut real speech ahead of transitions. Timing is
  defended in three layers: a forced-alignment pass in `TtsGenerationJob`
  (needs the API key's `forced_alignment` permission or it falls back), a
  tail-mismatch re-render check, and `SegmentSequencer.correctTimingFromAudio`
  (measures real speech spans/pauses from the decoded clips). Don't remove any
  of them.
- **The voice is generated in 2 long takes, not one per section** — v3 re-rolls
  its vocal character on every generation, so a request per section produced an
  audible "different person" jolt at each seam, and request stitching
  (`previous_request_ids`) isn't available for v3. `TtsChunkAssembler` joins
  sections into chunks and records each section's exact character range;
  `AudioSlicer` cuts the take back apart on those offsets. Never re-find a
  section by searching the chunk text — carry the offsets. Chunk B opens with
  the tail of chunk A (overlap-and-trim) so the model reads into it from the
  same context; that audio is generated, then discarded.
- **The character alignment now decides where audio is CUT**, not just when
  markers fire — a drifted alignment would sever a word. `AudioSlicer.FindBoundary`
  treats the alignment as a proposal and the audio as the authority: if the
  proposed gap holds no silence it hunts for the nearest real pause. Keep that
  fallback.
- **Loudness is matched per chunk, never per section** — a two-pass `loudnorm`
  in *linear* mode, so it is one uniform gain. Normalising sections individually
  (or letting loudnorm fall back to dynamic mode) flattens the quiet beats the
  script writes deliberately.
- Section files are **mp3** because `ScriptFileReader`/`SegmentSequencer` load
  them; both now pick `AudioType` from the extension, so the no-ffmpeg WAV
  fallback also plays. `MugsTech ▸ TTS ▸ Run Pipeline Self-Tests` covers the
  splitter, the offsets and the overlap offline (no key, no credits).
