#!/usr/bin/env python3
"""Generate a finished AutoAvatarGen video from a script file, end to end.

Launches the built Unity player in one-shot automation mode (--auto-script),
waits for it to render the TTS, record the take and mux the mp4, then prints
where the video landed plus the captured {Timestamp} chapters.

Stdlib only — no pip installs needed.

Usage:
    python Automation/generate_video.py path/to/script.txt
    python Automation/generate_video.py script.txt --exe "C:/Builds/AutoAvatarGen.exe"
    python Automation/generate_video.py script.txt --timeout-min 120

The player executable is resolved in this order:
    1. --exe argument
    2. AUTOAVATARGEN_EXE environment variable
    3. "exePath" in Automation/automation_config.json (next to this script)

Exit code 0 on success (last lines include ``VIDEO: <path>``), 1 on any
failure (reason + player log path are printed).

Notes:
    * The app window opens and must stay running for roughly the video's
      length — recording happens in real time on the GPU.
    * Each run calls ElevenLabs and spends TTS credits.
    * Only one run at a time; the app refuses to start twice.
"""

import argparse
import json
import os
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent
CONFIG_PATH = HERE / "automation_config.json"
RUNS_DIR = HERE / "runs"

# Grace periods around the player's own --auto-timeout-sec watchdog.
WRAPPER_TIMEOUT_BUFFER_SEC = 300   # wrapper kills the app this long after its deadline
EXIT_AFTER_RESULT_GRACE_SEC = 120  # app should quit soon after writing result.json
STARTUP_GRACE_SEC = 180            # automation writes its first progress within seconds;
                                   # nothing by now = the build predates automation support

POLL_SEC = 1.0
HEARTBEAT_SEC = 60


def log(msg):
    print(msg, flush=True)


def fail(msg, player_log=None):
    log(f"ERROR: {msg}")
    if player_log and Path(player_log).exists():
        log(f"PLAYER_LOG: {player_log}")
        tail = tail_lines(player_log, 40)
        if tail:
            log("---- player.log (last 40 lines) ----")
            for line in tail:
                log("  " + line.rstrip())
            log("------------------------------------")
    sys.exit(1)


def tail_lines(path, n):
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            return f.readlines()[-n:]
    except OSError:
        return []


def resolve_exe(cli_exe):
    candidates = []
    if cli_exe:
        candidates.append(("--exe argument", cli_exe))
    env = os.environ.get("AUTOAVATARGEN_EXE", "").strip()
    if env:
        candidates.append(("AUTOAVATARGEN_EXE env var", env))
    if CONFIG_PATH.exists():
        try:
            cfg = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
            cfg_exe = (cfg.get("exePath") or "").strip()
            if cfg_exe:
                candidates.append((str(CONFIG_PATH), cfg_exe))
        except (OSError, json.JSONDecodeError) as e:
            log(f"WARNING: could not read {CONFIG_PATH}: {e}")

    for source, exe in candidates:
        p = Path(exe).expanduser()
        if p.is_file():
            return p
        fail(f"Player exe from {source} does not exist: {p}")

    fail(
        "No player exe configured. Build the Unity project (File > Build), then either:\n"
        f"  * put its absolute path in {CONFIG_PATH} as {{\"exePath\": \"C:/.../AutoAvatarGen.exe\"}},\n"
        "  * or set the AUTOAVATARGEN_EXE environment variable,\n"
        "  * or pass --exe <path>."
    )


def format_ts(seconds):
    seconds = max(0, int(round(seconds)))
    h, rem = divmod(seconds, 3600)
    m, s = divmod(rem, 60)
    return f"{h}:{m:02d}:{s:02d}" if h else f"{m}:{s:02d}"


def kill_tree(pid):
    if os.name == "nt":
        subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"],
                       capture_output=True, check=False)
    else:
        subprocess.run(["kill", "-9", str(pid)], capture_output=True, check=False)


def main():
    try:  # keep non-ASCII chapter labels printable on legacy Windows consoles
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, OSError):
        pass

    ap = argparse.ArgumentParser(description="Run the AutoAvatarGen build end to end on one script file.")
    ap.add_argument("script", help="Path to the script .txt (## SECTION headings + tags)")
    ap.add_argument("--exe", help="Path to the built AutoAvatarGen player exe")
    ap.add_argument("--timeout-min", type=float, default=90.0,
                    help="Overall deadline for the run (default: 90)")
    args = ap.parse_args()

    script = Path(args.script).expanduser().resolve()
    if not script.is_file():
        fail(f"Script file not found: {script}")

    exe = resolve_exe(args.exe)

    run_dir = RUNS_DIR / datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    run_dir.mkdir(parents=True, exist_ok=True)
    result_path = run_dir / "result.json"
    progress_path = Path(str(result_path) + ".progress")
    player_log = run_dir / "player.log"

    timeout_sec = args.timeout_min * 60.0
    cmd = [
        str(exe),
        "--auto-script", str(script),
        "--auto-result", str(result_path),
        "--auto-timeout-sec", str(int(timeout_sec)),
        "-logFile", str(player_log),
    ]

    log(f"Script : {script}")
    log(f"Player : {exe}")
    log(f"Run dir: {run_dir}")
    log("Launching — the app window will open and record in real time. "
        "Leave it running (unfocused is fine).")

    started = time.monotonic()
    deadline = started + timeout_sec + WRAPPER_TIMEOUT_BUFFER_SEC
    proc = subprocess.Popen(cmd, cwd=str(exe.parent))

    last_phase_line = None
    last_heartbeat = started
    result_seen_at = None

    while True:
        time.sleep(POLL_SEC)
        now = time.monotonic()
        alive = proc.poll() is None

        # Relay the player's phase reports as they change.
        if progress_path.exists():
            try:
                prog = json.loads(progress_path.read_text(encoding="utf-8"))
                line = f"[{prog.get('phase', '?')}] {prog.get('detail', '')}"
                if line != last_phase_line:
                    log(f"{format_ts(now - started)}  {line}")
                    last_phase_line = line
                    last_heartbeat = now
            except (OSError, json.JSONDecodeError):
                pass  # mid-write — next poll gets it

        if now - last_heartbeat > HEARTBEAT_SEC:
            log(f"{format_ts(now - started)}  ... still running")
            last_heartbeat = now

        if result_path.exists() and result_seen_at is None:
            result_seen_at = now

        # An app that has shown no automation signs at all is an interactive
        # build from before automation support existed — it would sit at the
        # menu forever. Bail out early instead of burning the full deadline.
        if (last_phase_line is None and result_seen_at is None and alive
                and now - started > STARTUP_GRACE_SEC):
            kill_tree(proc.pid)
            fail("The app started but never reported automation progress. The configured "
                 "exe is probably an older build without automation support — rebuild the "
                 "player from the current project and update the exe path.", player_log)

        if not alive:
            break
        if result_seen_at is not None and now - result_seen_at > EXIT_AFTER_RESULT_GRACE_SEC:
            log("Result written but the app didn't quit — closing it.")
            kill_tree(proc.pid)
            break
        if now > deadline:
            kill_tree(proc.pid)
            fail(f"Run exceeded {args.timeout_min:.0f} min + buffer; the app was closed. "
                 "Raise --timeout-min for long scripts.", player_log)

    if not result_path.exists():
        fail(f"The app exited (code {proc.returncode}) without writing a result file. "
             "It may have crashed during startup.", player_log)

    try:
        result = json.loads(result_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as e:
        fail(f"Result file is unreadable: {e}", player_log)

    log("")
    if not result.get("success"):
        fail(f"Pipeline failed at stage '{result.get('stage')}': {result.get('error')}",
             player_log)

    video = result.get("videoPath") or ""
    size_mb = (result.get("videoSizeBytes") or 0) / (1024 * 1024)
    segs = f"{result.get('segmentsProcessed', 0)}/{result.get('segmentsTotal', 0)}"
    log(f"SUCCESS — {segs} segments, {size_mb:.1f} MB, "
        f"took {format_ts(result.get('elapsedSec', 0))}.")

    timestamps = result.get("timestamps") or []
    if timestamps:
        log("Chapters (YouTube format):")
        for t in timestamps:
            log(f"  {format_ts(t.get('seconds', 0))} {t.get('label', '')}")

    log(f"RESULT_JSON: {result_path}")
    log(f"VIDEO: {video}")
    sys.exit(0)


if __name__ == "__main__":
    main()
