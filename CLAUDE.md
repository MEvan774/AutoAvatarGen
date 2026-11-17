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
   The voice is **eleven_v4** unless the *Voice* row of the TTS panel (or the
   editor window's *Model*) is switched to eleven_v3 — a setting, not a build
   option (`Tts/VoiceModel`; see *Hard-won constraints*).
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
with it. The output library lists the save folder too. The extra panel rows
(these two, and the *Voice* row above them: model button + v4 stability field)
are cloned from the output folder row at runtime (`BuildSaveRows`), which grows
the panel by three rows instead of touching the scene.

## Headless-style automation (Claude Cowork)

`AutoAvatarGen.exe --auto-script <file>` runs the full pipeline unattended and
quits (driver: `Assets/Scripts/Automation/AutomationRunner.cs`). Use the
wrapper — see the `generate-video` skill (.claude/skills/generate-video):

```
python Automation/generate_video.py path\to\script.txt
```

Real GPU rendering is required, so the window opens and a run takes about the
video's length; there is no true `-batchmode` mode. Each run spends ElevenLabs
credits. One run at a time. The result JSON's `voiceModel` / `voiceStability`
are read from the generation's `render_manifest.json` — the voice the take was
rendered with, so a reused v3 generation says v3 whatever the setting is.

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

## The evidence-first stage (Mugs off, peek, wide cards)

`{Position:Off}` / `PeekLeft` / `PeekRight` and the devices built on them
(`,Scroll`, logo runs + `Strike`, `{Name}`, Quote `,Photo=` + type-on,
`{Clipping}`, `{Mark}`, `{Timeline:…,Wide}` + `Span`, `{Pop}`, `{Graph}`) are
documented for authors in the tag guide (§4 *"Mugs off stage"* onward). Where
it lives: `Stage/StageMath` (pure: travel plans, leg durations, wide slot,
`StageMeter`, `EmptyStageWatch`), `Stage/StageMoves` (pure: the pose of each
leg), `MediaPresentationSystem.Stage.cs` (the moves, full-frame media, the
meter), `ContentZoneController` (wide zone, runs, marks, strikes),
`Stage/NameOverlay` + `Stage/PopOverlay` (parsed by the pure `Stage/StageTags`,
before any card or media parser sees the script).

- **A stage move is a pose per frame, on `avatarParent`** (`StageMoves`, built
  like `HopMath`: travel that may pass 1 a little, scale X/Y, tilt;
  `ApplyStagePose` puts it on the transform). Nothing else writes that
  transform's scale or rotation, so the Hop / Grow / Shrink and the idle sway
  on the pivot below it keep running under a move and the shadow follows.
  Three things to keep: his size on the stage is `presenterUniform`, never
  read back from `localScale` (it carries the squash); `StageMoves.HoldEdge`
  moves the transform so the line he stands on — his feet, or the frame edge
  under a peek — stays still (the transform's origin is ~0.4 above his feet);
  a `,Cut` that stops a leg must call `EndStageShape`. A departure ends at
  speed, out of sight; an arrival ends on exactly rest.
- **The moves are calm — the creator's eye, not the brief's numbers.** The
  brief asked for nothing longer than half a second, with anticipation and a
  hard landing; on screen that was "way too hyper" for Mugs (he is relaxed; the
  1 s Glide is his reference). The timings in `StageMath` (pop-up 0.65,
  slide-out 0.60, peek and lean 0.40 in / 0.35 out) and the shapes in
  `StageMoves` (`Arrive`: eases up, ~3 % past his mark, a soft give, no dead
  stop; `Depart`: no wind-up, slow off the mark) were picked from a
  side-by-side preview on 2026-11-17. Show a preview before changing either.
- **A stage move is planned from where he is, never from where a move would
  have ended** (`PlanStageMove`, `BeginStageLeg`). A peek or a lean ↔ a full
  position is ALWAYS out, then in (`StageMath.PlanTravel`): Tier 5 first made
  a peek rise straight into `Center` in one leg and the creator rejected it on
  screen ("looks wrong") — don't bring that back.
  A `{Position:…}` during a move never jumps. Caught while LEAVING
  (`StageMath.IsLeaving` / `PlanWhileLeaving`): back to the place he is leaving,
  still in view (`StageMath.InView`), he comes back from where he is (`ToFull` /
  `ToPeek` / `ToLean`, the only use of those legs besides the redirect); for
  anywhere else the running leg carries on untouched (`finishFirst`:
  `StageTravel` resumes that same `StageLegRun` at `run.T`) and the arrival
  follows it. Caught while arriving: he goes back down out of a pop-up, leaves
  through the nearest edge between marks (`ExitFrom`); the squash and lean he
  had are eased away over 0.10 s (`CarryShape`), not dropped. A Left / Right /
  Center tag during an arrival redirects it (`RedirectStageArrival`, planned by
  `PlanStageRedirect`) — the only stage code `MoveToPosition`'s side path
  reaches, and only while `stageMoving`. A Glide
  cut short by another Glide still starts from the mark, as it always has. The `Tier5_*` self-tests
  (`TtsPipelineSelfTests.Motion.cs`) hold every pose to its limits.
- **The side move has two styles, chosen in the main menu** (*Side move*,
  PlayerPref `AutoAvatarGen.SideMove`, 0/absent = Glide, 1 = Dash; read once
  per take in `BeginStageRun`; no tag keyword, like the Hop). Glide is
  `MoveAvatar`, untouched, and must stay the default: every existing video
  re-renders the same until the creator picks Dash. Dash is `Stage/DashMath`
  (pure) applied by `MediaPresentationSystem.Dash.cs` on `avatarParent`, from
  where he is; its turn closes his width and mirrors the sprite at zero.
  `PresenterLandCap` is the longer of `SideMoveSeconds` and
  `StageMath.LongestArrivalSeconds` (a peek's drop + the pop-up), + 0.25 s:
  1.25 s in both styles. The result JSON carries
  `sideMove` and `presenterHolds{count,totalSeconds,longestSeconds}`
  (`Stage.PresenterHolds`, fed by both "Held … for the presenter to land"
  sites). The menu row sits in the Sound card: the Look column is full.
- **The handover — `{Position:…,Over}`** (`MediaPresentationSystem.Handover.cs`):
  from off or a peek he comes up IN FRONT of what has the frame and it stays
  exactly as it is until its own End. Three mechanisms, each easy to break:
  (1) his sprites go to `StageMath.OverSortingOrder` 31200 through
  `SetPresenterLayer` — every stage sorting change goes through it, never
  straight to `SetPeekSorting` — with `HandoverDim` at 31100 under him;
  (2) `ContentZoneController.BeginHandover` sets the zone's flags for his new
  position WITHOUT `PauseTimeline` / `SetStageWide`, which would hide or move
  the card under him, and `ActsOnHandoverCard` lets that card's End through
  Center's skip; (3) `MoveOnStage` skips `DismissActiveMedia`. It ends when
  `StepHandover` sees the card instance and the full-frame media gone (so he
  keeps his layer through the fade), on a new beat (`HandoverNewBeat`, called
  from the card tracker and the media tracker), on `{Position:Off}` / a plain
  peek (`LeaveHandover`: it carries on) or a transition. He keeps the handover's
  layer until the dim has gone (`handoverTail`) — on the first take an image
  that ended on its usual cut left him UNDER the fading sheet for 9 frames;
  an image under him now fades out behind him (`FadeMediaBehindPresenter`,
  before its texture is destroyed; a clip still cuts). Zooms are ignored
  meanwhile (`StageIgnoresZoom`). Both stampers already took a 2nd word on
  `{Position:}`, so `,Over` needed no stamper change — the runtime regex is
  `PositionTagRegex` (a 2nd word other than Cut / Smooth / Over is still not a
  position tag). The result JSON's `stage{}` has `handovers`, `overSeconds`.
- **The lean — `{Position:LeanLeft}` / `{Position:LeanRight}`** is the peek's
  twin at a side edge (`StageMath.IsSmall` = peek or lean; ask that, not
  `IsPeek`, for "a small appearance"). Its pose (`StageMath.Lean`) is tilted
  **35°**, not the brief's 12°: at 12° his whole front was in the frame and he
  read as standing cut off by the edge — the creator chose 35° from take
  frames on 2026-11-17. It is the only position he RESTS at tilted, so a leg
  carries the tilt it starts and ends at (`StageLegRun.TiltA/TiltB`), every
  way out sets him upright (`UprightRotation`), and `PresenterBody` measures
  his outline in `avatarParent`'s own space (a world-space box of a tilted
  sprite is the wrong size). The sprite's outline is not centred on the
  transform and mirrors with `flipX`: a pose that faces the other way is
  computed from `FacingBody`. Lean legs hold the frame's SIDE edge still
  (`StageMoves.HoldSideEdge`) and reuse the peek's shapes. A lean ↔ a full
  position is out, then in, like a peek (also on its own side). The meter
  counts a lean's seconds as peek seconds and its count apart (`leanCount`).
- **Arrive on the word: an arrival from off stage starts early, and its tag
  is never moved.** `Stage/ArrivalLeads` (pure) plans it from the PARSED
  times — final by then, so the three timing defences, the `{Pause}` pins and
  the same-beat join are untouched; do not move this into `SegmentSequencer`.
  `PositionMarkerData.startTime` is when the travel starts; `triggerTime` is
  still when the STAGE changes: `MoveOnStage(…, ledUntil)` leaves the media
  dismissal, `PauseTimeline` and `SetStageWide` to `ApplyPendingStageChange`
  on the tag's time (run early, a wide card would glide to its side 0.3 s
  before its own End). `StageChangeIsDue` counts that pending change, or
  media written on the same word would open and be dismissed on one frame.
  The tracker starts him only once he is really off and still (a live clamp
  on top of the planned one). `NextCaptionStop` and `NextCameraStop` use
  `startTime`. `HybridAvatarSystem.ApplyArrivalFaces` puts an emotion that
  would fire during the rise on its first frame as a cut. He lands BEFORE the
  wide card he rose in front of starts to close, so he keeps the peek's layer
  until it has (`frontHeld` / `StepFrontHold`) — without that he stood behind
  it for three frames after every landing (found in frames). Not led: leaving,
  `,Cut`, a move while on screen, under a `{Black}`, a `{Transition}` line,
  the first position. `stage{}` reports `ledArrivals`, `shortenedLeads`.
- **Old scripts must not notice.** Everything is reached only through a new
  `{Position:…}` value, a new tag or a new optional keyword; the side layout,
  the timed `{BigMedia:a+b,4}` row, the plain Quote and the vertical Timeline
  are the old code paths (the disk `Logos/` lookup is for logo RUNS only — on
  the timed row it would turn an old script's text fallback into a logo).
  Keep it that way when adding to a card.
- **"Wide" = Off or peeking** (`StageMath.IsWide`). Side cards then open in
  `ContentZone_Cards_Wide` and lay out through `ContentCard.LayoutScale` —
  the full width's scale, stepped down by `FitWide` until the card's own
  report fits the slot's height (the slot is 44 % wider than the side card but
  hardly taller: on the first real take a wide Compare's source fell off the
  card). The side layout never goes through that fit. `{Image}`/`{Video}` fill the frame by `uvRect` (cover, push-in,
  scroll — the slot never moves, so an image can't show its edge). A wide ↔
  side change ends the media like a change of side; Off ↔ peek keeps it.
- **Cards wait for an arrival, never for him leaving** (`StageMath.CardsWaitFor`),
  and `ShowMedia` waits ≤3 frames while `StageChangeIsDue()` — the media
  tracker runs before the position tracker, so media written under a
  `{Position:Off}` would otherwise land in the side slot and be dismissed.
- **A peek is drawn at sorting 30500**: in front of the card zones (30000),
  behind the feature zone (31000). His pop-up uses the same order while he
  rises, so a wide card closing on that beat fades behind him, not over him. `NameOverlay` 30600, `PopOverlay` 31300
  (under captions 31400, Echo 31500, black 32000).
- **His screen time is measured, not inferred**: `StageMeter` steps on voice
  time; `Covered` (black, `,bigCenter` headline, Clipping) is not on screen.
  The take logs `[Stage] Mugs on screen 41.2 % of 312.4 s (off 6 times)` and
  the result JSON carries `mugsOnScreenShare`, `mugsPeekShare` and `stage{}`.
  Off with nothing on screen for >3 s is the one fault it warns about.
- **Runs are sized once, read ahead** (`ContentZoneController.AnticipateRun`):
  a logo grid, a wide timeline and a clipping pile know their whole run when
  they open, so nothing re-flows on a beat (an item lands once — the opener's
  catch-up and the tracker can both reach it). `{Mark}`, `{BigMedia:Strike}`
  and `{…:End}` act on the card that is up and pass a `{Pop}`; a NEW card waits
  behind it, and then so does everything written after that card (order kept).
- **A baked stamp is `TagBeats.StampPattern`, not any `T=digit`.** A `{Graph}`
  label like `ChatGPT=400M` would otherwise be shifted onto the take's clock.
  `SegmentSequencer._TPattern` is that same pattern (the join's indices rely
  on it); it matches the identical 5,746 stamps in the 272 real timed files.
- **A new tag still needs all of**: runtime parser, `KnownMarkers`,
  `_ALL_MARKERS`, both `StampMarker`s, `TagOnlyRetimer.UnstampDuration` when
  it has a `D=` slot, the guide — `Tier4_*` in the self-tests is the template.

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
- **The voice model is a setting, and a take's timing follows its generation,
  never the setting.** `Tts/VoiceModel`: PlayerPref `AutoAvatarGen.TtsModel`
  (absent = `eleven_v4`) and `AutoAvatarGen.TtsStability` (v4 only, 0–1, default
  0.5, never snapped; v3 stays 0.0 through `SnapStability`). A null
  `Config.ModelId` / `ChunkedOptions.Stability` means "the setting", resolved
  once in `TtsGenerationJob.Run()`. The v4 request is hand-written JSON with
  `stability` + `similarity_boost` only (v4 has no Style / Speed, and nothing
  else was tested); the v3 request is still JsonUtility's four fields, byte for
  byte (pinned in `Voice_RequestJsonFollowsTheModel`), and the render manifest
  records only what was sent. Everything that TIMES a take asks
  `VoiceTiming.For(VoiceModel.OfGeneration(folder))` — the `model` in that
  generation's `render_manifest.json`, none = v3 — so a v3 generation is
  re-timed and re-recorded with v3's numbers on any build (a dry-run replay
  re-slices with the cached take's model too). Tag-only updates make no TTS
  call and read no voice setting.
- **eleven_v4 packs a break's pause into the sentence's last word** (the
  2026-11-17 lab takes; three of them are the fixtures in
  `Editor/TestData/VoiceLab~`). The newline and cue characters start at the
  END of the silence, a break is 0.25–0.5 s where v3 left 0.6–0.9 s, and a
  section break gets no more than a line break. Four rules answer that, all in
  `VoiceTiming.V4`, none reached by a v3 generation:
  (1) *the cut* — the alignment's gap is a 0–30 ms sliver hard against the next
  word (on the two v4 fixtures the v3 rule needed its "drifted" rescue at 19 of
  46 breaks and once cut outside silence), so
  `AudioSlicer.Options.CutMidSilence` cuts in the middle of the longest silence
  touching [the section's last word's start, the next section's start]; with
  no such silence the old path runs, fallback included.
  (2) *the pause floor* is 0.20 s (the shortest v4 break measured 0.26 s by the
  engine's −35 dBFS rule; quiet runs inside words stay under 0.20 s).
  (3) *one beat, one frame* — an own-line tag is stamped on the next word's
  onset already, so the snap rarely fires and the join had nothing to start
  from: stacked tags came out more than a frame apart at 12 of 46 breaks.
  `TagBeats.Resolve(…, firstTagSetsBeat)`: the run's first tag sets the beat;
  a snap later in the run still wins and then pulls the EARLIER tags of the
  run onto its onset — the one place a tag moves later, v4 only.
  (4) *captions* — a sentence-final word's end time is the silence's end, so
  `CaptionBuilder.EndWordsAtPauses` ends it where its measured pause begins
  (else a tag that opens a `{Pause}` gap is "due before the words are said"
  and the phrase is skipped).
  Forced alignment has never run on this account (every `render_manifest.json`
  says `synthesis`), so these rules read v4's synthesis alignment directly.
  The `Voice_*` self-tests hold all four on the real takes.
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
  snap, `{Pause}` silence, section offset). Change one, change both (the
  same-beat join below moves tags only, never words — it only makes the T=
  fallback spans start on the real onset) — and keep
  the plausibility verdict: v3's alignment collapses words to a few ms
  ("money under my mattress." in 25 ms), so a phrase only highlights word by
  word when every word passes, else it shows whole or on the `T=` span. The
  verdict reads each word's start AFTER the pause snap (v3 starts the word
  after a breath inside the measured pause; judging the raw start sent 23 of
  48 phrases of the 2026-11-09 test to the guess), and a `T=`-span guess is
  kept between its script neighbours — it never cuts a timed phrase short.
- **Tags written on one beat fire on one frame.** A run = tags with no spoken
  word between them (adjacent lines or one line; `[stage directions]` don't
  break it). Own-line tags are each stamped at their own whitespace character,
  and v3 spreads those across the silence: on the 2026-11-10 Cybertruck take
  `{Shrug,Cut}` got 67.088 and the `{Zoom:ExtremeOut}` below it 67.487, the snap
  put Shrug on the onset (67.36) and the swap fired 4 frames before the cut.
  `SegmentSequencer.ShiftTimestamps` resolves a section's T= together through
  `Tts/TagBeats`: once a tag of a run snaps to a pause's end, later tags of the
  run past that onset are pulled back onto it — never pushed later, at most
  `PauseSnapMaxSeconds`, not past another measured pause, never in a run holding
  a `{Pause}` (PauseInserter's pins win), and nothing joins where the rescale
  end-zone rule stopped the snap. Words aren't tags, so captions don't move.
  `HybridAvatarSystem.ShareExtremeZoomCutFrames` backs it up: an emotion within
  `extremeZoomCutWindow` of an ExtremeIn/Out takes the cut's time.
- **Captions are decided per phrase, never per frame** (`Captions/CaptionRules`,
  pure). A phrase starts only within 0.2 s of its first word, only when
  `MediaPresentationSystem.NextCaptionStop` (NextCameraStop + chapter titles,
  Echo, smooth `{Position}` glides) is past its last word (`SpokenEnd`), after
  0.4 s of bare frame (a card's 0.3 s fade-out + 3 frames); once up it stays to
  its End, and anything unannounced hides it for good. Late = skipped whole,
  never shown halfway — the old 1.2 s per-frame gate popped phrases in mid-line
  and let cards cut them off after 0.1 s. A new thing that clears captions must
  be in `FrameBare` AND `NextCaptionStop`. The result JSON's captions block
  reports `skippedLate`, `skippedAhead`, `interrupted` (should be 0) and
  `shortestShownSeconds`.
- Section files are **mp3** because `ScriptFileReader`/`SegmentSequencer` load
  them; both now pick `AudioType` from the extension, so the no-ffmpeg WAV
  fallback also plays. `MugsTech ▸ TTS ▸ Run Pipeline Self-Tests` covers the
  splitter, the offsets and the overlap offline (no key, no credits).
