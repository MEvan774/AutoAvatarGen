---
name: generate-video
description: Generate a finished avatar video (mp4 + YouTube chapters) from a script file using the AutoAvatarGen build. Use when asked to produce, render, or generate a video from a script or topic, end to end.
---

# Generate a video with the AutoAvatarGen build

One command runs the whole pipeline: script → ElevenLabs TTS → recorded avatar take → muxed mp4.

```
python Automation/generate_video.py <path\to\script.txt>
```

## Before launching

1. **Script format**: the script needs `## SECTION NAME` headings (each becomes a TTS segment) and may use inline tags (`{Timestamp:"..."}`, emotion/media tags, etc.). When writing a script, read `Assets/Python/script/SCRIPT_TAG_GUIDE.md` first — tags not in the guide break silently.
2. **Player exe**: the wrapper needs the built player. It resolves `--exe` → `AUTOAVATARGEN_EXE` env var → `Automation/automation_config.json` (`exePath`). If none is set, ask the user where their build is and store it in the config file once.
3. **Costs and side effects**: every run spends ElevenLabs credits, opens the app window on the user's machine, and occupies it for roughly the video's real-time length. Confirm with the user before launching runs they didn't explicitly ask for.

## Launching

- Always run it in the background (`run_in_background`) — a run usually takes longer than the 10-minute foreground command cap. Do not poll in a sleep loop; you'll be notified when it exits.
- One run at a time. Never start a second run while one is active (the app refuses and the run fails). Ask the user to close a manually-opened app instance first if one is running.
- The wrapper prints phase lines (`[tts]`, `[recording]`, `[finalizing]`) and a heartbeat, so partial output tells you where it is.
- Do not kill the process during `[finalizing]` — that's the mux writing the mp4. Hang protection already exists (in-app watchdog + wrapper deadline). For long scripts pass `--timeout-min` (default 90).

## Reading the outcome

On success (exit 0) the last lines contain:

```
Chapters (YouTube format):
  0:00 Intro
  1:23 ...
RESULT_JSON: Automation/runs/<timestamp>/result.json
VIDEO: <absolute path to the mp4>
```

Give the user the `VIDEO:` path and the chapter list (it's ready to paste into a YouTube description). `result.json` has machine-readable details (segments, sizes, timings, `timestampsPath`).

On failure (exit 1) the wrapper prints the failing stage, the error, and the last 40 lines of the player log (full log: `Automation/runs/<timestamp>/player.log`). Common cases:

- `No ElevenLabs API key` — the user must open the app once and save the key (TTS panel → Edit Key…), or set `ELEVENLABS_API_KEY`. Note: the built player has its own PlayerPrefs, separate from the Unity editor's — a key (or output folder) saved while testing in the editor does NOT exist for the build. All saved settings must have been made in the build itself.
- `segments stalled (markers won't line up)` — ElevenLabs alignment glitch; simply re-run, it's non-deterministic. Audio for the failed attempt was still billed.
- `Another instance of the app is already running` — ask the user to close the app, then retry.
- Timeout — re-run with a higher `--timeout-min`.

## What NOT to touch

- Voice, visual style, avatar emotions and output folders all come from the app's saved settings — a run behaves exactly as if the user pressed Generate + Start in the UI. Don't try to change those from the command line; there are no flags for them by design.
- Recordings land where the app is configured to save them (the `VIDEO:` path is authoritative — don't guess folders).
