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

**Tag-only updates** (no credits): the TTS panel's *Load From Generation* /
*Update Tags Only* buttons (cloned from Generate at runtime in
`TtsPanelController.BuildTagEditRow`) re-time an edited script's tags against
the active generation's existing voice — `TagOnlyRetimer` (pure) +
`TagOnlyUpdate` (folder IO). Only `_timed.txt` and `script.txt` are rewritten;
the old ones go to `tag_edits/<time>/`. Narration, `## SECTION` headings,
`{Transition:…}` tags and voiced `[stage directions]` are locked. Times come
from `chunks/` + `render_manifest.json` (exact) or `word_timestamps/`.

**Saved copies**: after a real Generate, the TTS panel copies the whole
generation folder to its *Save copies to* folder (PlayerPref
`AutoAvatarGen.GenerationSaveFolder`, default `Documents/AutoAvatarGen Saves`,
empty = off) as `<yyyy-MM-dd_HH-mm> <Video name>` (`GenerationSave`), then
makes the **copy** the selected generation so tag edits and recordings stay
with it. The output library lists the save folder too. The two extra panel rows
are cloned from the output folder row at runtime (`BuildSaveRows`), which grows
the panel by two rows instead of touching the scene.

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
parser AND both TTS marker processors AND the guide, or it breaks silently —
and if `StampMarker` gives it a `D=` slot, `TagOnlyRetimer.UnstampDuration`
must turn that back into the authored form.

Worked example — `{Pause:N}`: `KnownMarkers` + an explicit `StampMarker`
branch (C#) and `_ALL_MARKERS` + `_stamp_marker` (Python) stamp it
`{Pause:0.8,T=…}`; no `D=` slot, so `Unstamp` needs no case; it is consumed
in `SegmentSequencer` (via the pure `Tts/PauseInserter`) before any runtime
parser and stripped again in `ScriptFileReader.Dispatch` as a safety net;
the guide covers it in §3/§4/§5/§7; the `Pause_*` self-tests cover it
offline. The `--auto-script` result JSON reports each one in `pauses[]`.

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
- **`{Pause:N}` is laid into audio that already exists — never requested.**
  Per section, `PauseInserter` finds the gap in the decoded samples (the tag's
  `T=` only says where to look: on the 2026-11-01 Binance take v3's synthesis
  alignment was 1–2 s off mid-section while `rescale` stayed off), widens it to
  *at least* N (never N on top of a natural gap), pins the tags in that gap to
  its edges (written above → fire as it opens; below → as it closes — own-line
  tags otherwise all land on the next word's onset) and moves later `T=` along
  inside `ShiftTimestamps`. It never splices into sound: no silence near the
  tag → nothing inserted + a severe `[Pause]` log line. Section-edge pauses
  widen the gap between sections through `PauseInserter.SizeBoundary`, which is
  the old sizing exactly when there are none. Don't make it splice at a raw
  alignment time.
- **Loudness is matched per chunk, never per section** — a two-pass `loudnorm`
  in *linear* mode, so it is one uniform gain. Normalising sections individually
  (or letting loudnorm fall back to dynamic mode) flattens the quiet beats the
  script writes deliberately.
- **The take's −14 LUFS gain is a `ListenerGain` filter on the AudioListener's
  object, and it must sit BEFORE Evereal's `AudioRecorder` there** (both are
  `OnAudioFilterRead` filters; order = component order). Evereal appends its
  recorder at `StartCapture`, so `TakeLoudness` adds the filter before the take;
  `ListenerGain` checks the order once the recorder exists and logs an error
  (`gainInFile: false`) if it isn't. AudioSource.volume can't boost and there is
  no AudioMixer — don't move the gain onto the clips' samples. While *Sound
  effects* is on the same filter (added even at gain 1) bends samples past
  −1 dBFS softly toward 0 dBFS: the gain keeps voice + bed under it, so it only
  ever touches an effect landing on a loud syllable (`sfxCeilingSamples`).
- **The idle camera drift (`Motion/IdleDrift`) borrows the camera between
  events.** It eases back to the exact rest framing before every marker
  `MediaPresentationSystem.NextCameraStop` lists (zooms, media, black,
  transitions, cards), and `ApplyZoom` calls `IdleDrift.Settle` before it reads
  the camera, so zoom steps keep their size. A new tag that moves the camera or
  lands on the frame must be added to `NextCameraStop`; a zoom that ever finds
  the drift not home is counted in the result JSON's `idleDrift.snaps`.
- **Captions take their times through SegmentSequencer, never on their own.**
  `word_timestamps/<slug>_words.json` only lends times to the script's own
  words (`Captions/CaptionBuilder`); each section's times go through the exact
  mapping its `T=` stamps get in `BuildCombined` (measured-span rescale, pause
  snap, `{Pause}` silence, section offset). Change one, change both — and keep
  the plausibility verdict: v3's alignment collapses words to a few ms
  ("money under my mattress." in 25 ms), so a phrase only highlights word by
  word when every word passes, else it shows whole or on the `T=` span. The
  verdict reads each word's start AFTER the pause snap (v3 starts the word
  after a breath inside the measured pause; judging the raw start sent 23 of
  48 phrases of the 2026-11-09 test to the guess), and a `T=`-span guess is
  kept between its script neighbours — it never cuts a timed phrase short.
- Section files are **mp3** because `ScriptFileReader`/`SegmentSequencer` load
  them; both now pick `AudioType` from the extension, so the no-ffmpeg WAV
  fallback also plays. `MugsTech ▸ TTS ▸ Run Pipeline Self-Tests` covers the
  splitter, the offsets and the overlap offline (no key, no credits).
