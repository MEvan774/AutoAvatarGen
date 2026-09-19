#!/usr/bin/env python3
"""
organize_media.py
=================

Sort the media root's unsorted files into their proper subfolders, creating
any folder that is missing:

    BRoll/   Images/   CEO/   TwitterPosts/   Flags/   Logos/

The app (MediaPresentationSystem.cs) looks a media name up in EVERY one of
these folders, so moving a file never changes a script tag. This is pure
housekeeping so each folder means what its name says.

WHAT COUNTS AS UNSORTED (the "inbox")
    - files sitting directly in the root
    - files in Images/
    Files already in BRoll/, CEO/, TwitterPosts/, Flags/ and Logos/ are
    trusted and left alone, with one exception: a file of the wrong KIND
    (a video in an image folder, an image in BRoll/) is moved by kind.

HOW AN INBOX FILE IS CLASSIFIED (first rule that matches wins)
    1. MANUAL below                                   -> whatever it says
    2. video extension (.mp4 .mov .webm .avi)         -> BRoll/
    3. name starts with x_post / xpost / tweet / twitter -> TwitterPosts/
    4. ISO 3166 country code (us, de, gb, gb-sct ...) -> Flags/
    5. contains a place word (headquarters, office, building, campus ...)
                                                      -> Images/
    6. looks like a person's name:
         first-last, first_last, First_Last, first-middle-last,
         FirstLast (two capitalised words), or any of those after a
         leading numeric id (104900879-Andrew_Bailey)  -> CEO/
    7. anything else                                  -> Images/

    A wrongly sorted file is fixed by adding its bare name (no extension,
    any case) to MANUAL, or simply by dragging it to the right folder — the
    sorted folders are never touched again.

USAGE
    python organize_media.py                 # show the plan, ask, then move
    python organize_media.py --dry-run       # show the plan only, never move
    python organize_media.py --apply         # move without asking
    python organize_media.py "D:/other/root" --apply

    With no root argument the script sorts the folder it sits in, so you can
    drop it into the media root and double-click it.

    After moving it re-runs generate_media_library.py (looked for next to
    this script, then in the root) so MEDIA_LIBRARY.md matches the new
    layout. --no-library skips that.

Nothing is ever overwritten: when the destination already has a file with
that name, the source stays where it is and is listed under "skipped".
"""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
from pathlib import Path

# --- Layout: kept in sync with MediaPresentationSystem.cs --------------------
FOLDERS = ["BRoll", "Images", "CEO", "TwitterPosts", "Flags", "Logos"]
INBOX = {"", "Images"}                     # "" = the root itself
IMAGE_EXTS = {".png", ".jpg", ".jpeg"}
VIDEO_EXTS = {".mp4", ".mov", ".webm", ".avi"}

LIBRARY_SCRIPT = "generate_media_library.py"
OUTPUT_NAME = "MEDIA_LIBRARY.md"

# --- Rule 1: hand overrides. Bare file name (no extension), any case. -------
MANUAL = {
    "michealburry": "CEO",
    "raydalio": "CEO",
    "bill-gates-microsoft-corporation-operating-system-press-2001": "CEO",
}

# --- Rule 3: tweet / social-post screenshots ---------------------------------
TWEET_PREFIXES = ("x_post", "x-post", "xpost", "tweet", "twitter")

# --- Rule 4: ISO 3166-1 alpha-2 codes (+ eu / un / xk, which flagcdn ships) --
ISO_CODES = set("""
ad ae af ag ai al am ao aq ar as at au aw ax az ba bb bd be bf bg bh bi bj bl
bm bn bo bq br bs bt bv bw by bz ca cc cd cf cg ch ci ck cl cm cn co cr cu cv
cw cx cy cz de dj dk dm do dz ec ee eg eh er es et fi fj fk fm fo fr ga gb gd
ge gf gg gh gi gl gm gn gp gq gr gs gt gu gw gy hk hm hn hr ht hu id ie il im
in io iq ir is it je jm jo jp ke kg kh ki km kn kp kr kw ky kz la lb lc li lk
lr ls lt lu lv ly ma mc md me mf mg mh mk ml mm mn mo mp mq mr ms mt mu mv mw
mx my mz na nc ne nf ng ni nl no np nr nu nz om pa pe pf pg ph pk pl pm pn pr
ps pt pw py qa re ro rs ru rw sa sb sc sd se sg sh si sj sk sl sm sn so sr ss
st sv sx sy sz tc td tf tg th tj tk tl tm tn to tr tt tv tw tz ua ug um us uy
uz va vc ve vg vi vn vu wf ws ye yt za zm zw
eu un xk
""".split())
FLAG_RE = re.compile(r"^([a-z]{2})(?:-[a-z]{2,3})?$")   # us, gb-sct, us-ca

# --- Rule 5: a photo of a place, not a person ---------------------------------
PLACE_WORDS = (
    "headquarter", "office", "building", "campus", "store", "factory", "plant",
    "datacenter", "data-center", "data_center", "skyline", "tower", "warehouse",
    "gigafactory", "facility", "lab", "street", "city", "aerial", "exterior",
    "interior", "logo", "icon", "chart", "graph", "screenshot", "article",
    "headline", "banner", "thumbnail", "map", "cover", "photo", "stock",
)
PLACE_TOKENS = {"hq"}                       # too short for a substring match

# --- Rule 6: person-name shapes ---------------------------------------------
LEADING_ID_RE = re.compile(r"^\d+[-_ ]+")             # 104900879-Andrew_Bailey
WORD_RE = re.compile(r"^[A-Za-z]{2,}$")
CAMEL_TWO_RE = re.compile(r"^[A-Z][a-z]+[A-Z][a-z]+$")  # MichealBurry


def kind_of(path: Path):
    ext = path.suffix.lower()
    if ext in IMAGE_EXTS:
        return "image"
    if ext in VIDEO_EXTS:
        return "video"
    return None


def tokens(stem: str):
    return [t for t in re.split(r"[-_ ]+", stem) if t]


def looks_like_person(stem: str) -> bool:
    stem = LEADING_ID_RE.sub("", stem)
    if CAMEL_TWO_RE.match(stem):
        return True
    parts = tokens(stem)
    if not 2 <= len(parts) <= 3:
        return False
    return all(WORD_RE.match(p) for p in parts)


def classify(path: Path) -> tuple[str, str]:
    """Return (folder, reason) for an inbox file."""
    stem = path.stem
    low = stem.lower()

    if low in MANUAL:
        return MANUAL[low], "manual override"
    if kind_of(path) == "video":
        return "BRoll", "video file"
    if low.startswith(TWEET_PREFIXES):
        return "TwitterPosts", "tweet screenshot prefix"
    m = FLAG_RE.match(low)
    if m and m.group(1) in ISO_CODES:
        return "Flags", "ISO country code"
    if any(w in low for w in PLACE_WORDS) or PLACE_TOKENS & set(tokens(low)):
        return "Images", "place / object keyword"
    if looks_like_person(stem):
        return "CEO", "looks like a person's name"
    return "Images", "no rule matched (general image)"


def existing_dir(root: Path, name: str):
    """The on-disk folder for `name`, matched case-insensitively (logos == Logos)."""
    direct = root / name
    if direct.is_dir():
        return direct
    for child in root.iterdir():
        if child.is_dir() and child.name.casefold() == name.casefold():
            return child
    return None


def ensure_folders(root: Path):
    """Create every missing media folder; return {canonical name: Path}."""
    dirs = {}
    created = []
    for name in FOLDERS:
        d = existing_dir(root, name)
        if d is None:
            d = root / name
            d.mkdir()
            created.append(name)
        dirs[name] = d
    return dirs, created


def is_media_file(p: Path) -> bool:
    return (p.is_file() and not p.name.startswith(".") and not p.name.endswith("~")
            and p.suffix.lower() != ".meta" and kind_of(p) is not None)


def build_plan(root: Path, dirs: dict):
    """
    Return (moves, stays) where moves = [(src, dest_dir, reason)] and
    stays = [(src, reason)] for inbox files that already sit in the right place.
    """
    moves, stays = [], []   # moves: (src, canonical folder name, reason)

    # Inbox: the root itself and Images/.
    inbox_dirs = [(root, "")] + [(dirs[n], n) for n in FOLDERS if n in INBOX]
    for folder, name in inbox_dirs:
        for p in sorted(folder.iterdir(), key=lambda x: x.name.casefold()):
            if not is_media_file(p):
                continue
            target, reason = classify(p)
            if target == name:
                stays.append((p, reason))
            else:
                moves.append((p, target, reason))

    # Sorted folders: only fix the wrong KIND.
    for name in FOLDERS:
        if name in INBOX:
            continue
        for p in sorted(dirs[name].iterdir(), key=lambda x: x.name.casefold()):
            if not is_media_file(p):
                continue
            k = kind_of(p)
            if name == "BRoll" and k == "image":
                target, reason = classify(p)
                if target == "BRoll":
                    target, reason = "Images", "image file in BRoll/"
                moves.append((p, target, reason))
            elif name != "BRoll" and k == "video":
                moves.append((p, "BRoll", "video file in an image folder"))

    return moves, stays


def rel(root: Path, p: Path) -> str:
    try:
        return p.relative_to(root).as_posix()
    except ValueError:
        return str(p)


def print_plan(root: Path, moves, stays, created):
    if created:
        print("Created missing folder(s): " + ", ".join(created))
    if not moves:
        print("Nothing to move - every file is already in its folder.")
    else:
        by_dest = {}
        for src, target, reason in moves:
            by_dest.setdefault(target, []).append((src, reason))
        print("Planned moves ({} file(s)):".format(len(moves)))
        for target in FOLDERS:
            items = by_dest.get(target)
            if not items:
                continue
            print("  -> {}/  ({} file(s))".format(target, len(items)))
            for src, reason in items:
                print("       {:<52} [{}]".format(rel(root, src), reason))
    review = [(p, r) for p, r in stays if r.startswith("no rule")]
    if review:
        print("Left in Images/ because no rule matched ({}); drag or add to MANUAL if wrong:".format(len(review)))
        for p, _ in review:
            print("       " + rel(root, p))


def apply_moves(root: Path, moves, dirs: dict):
    moved, skipped = [], []
    for src, target, reason in moves:
        dest = dirs[target] / src.name
        if dest.exists():
            skipped.append((src, dest))
            continue
        shutil.move(str(src), str(dest))
        moved.append((src, dest))
    return moved, skipped


def regenerate_library(root: Path):
    here = Path(__file__).resolve().parent
    for candidate in (here / LIBRARY_SCRIPT, root / LIBRARY_SCRIPT):
        if candidate.is_file():
            print("Regenerating {} with {} ...".format(OUTPUT_NAME, candidate))
            subprocess.run([sys.executable, str(candidate), str(root)],
                           stdin=subprocess.DEVNULL, check=False)
            return True
    print("({} not found next to this script or in the root - {} not refreshed.)"
          .format(LIBRARY_SCRIPT, OUTPUT_NAME))
    return False


def main() -> int:
    ap = argparse.ArgumentParser(description="Sort the media root into its subfolders.")
    ap.add_argument("root", nargs="?", help="media root (default: the folder this script is in)")
    ap.add_argument("--dry-run", action="store_true", help="show the plan, move nothing")
    ap.add_argument("--apply", "--yes", "-y", dest="apply", action="store_true",
                    help="move without asking")
    ap.add_argument("--no-library", action="store_true",
                    help="do not re-run generate_media_library.py afterwards")
    args = ap.parse_args()

    root = Path(args.root).resolve() if args.root else Path(__file__).resolve().parent
    if not root.is_dir():
        print("ERROR: not a folder: {}".format(root))
        return 1

    print("Media root: {}".format(root))
    dirs, created = ensure_folders(root)
    moves, stays = build_plan(root, dirs)
    print_plan(root, moves, stays, created)

    if not moves:
        return 0
    if args.dry_run:
        print("\nDRY RUN - nothing was moved. Re-run without --dry-run to move.")
        return 0

    if not args.apply:
        try:
            answer = input("\nMove these {} file(s)? [y/N] ".format(len(moves)))
        except EOFError:
            answer = ""
        if answer.strip().lower() not in ("y", "yes"):
            print("Nothing moved.")
            return 0

    moved, skipped = apply_moves(root, moves, dirs)
    print("\nMoved {} file(s).".format(len(moved)))
    if skipped:
        print("Skipped {} file(s) because the destination already has that name:".format(len(skipped)))
        for src, dest in skipped:
            print("       {}  ->  {}  (exists)".format(rel(root, src), rel(root, dest)))

    if moved and not args.no_library:
        regenerate_library(root)
    return 0


if __name__ == "__main__":
    code = main()
    # Friendly pause so a double-click on Windows doesn't flash the window shut.
    if sys.platform == "win32" and sys.stdin.isatty():
        try:
            input("\nDone. Press Enter to close...")
        except EOFError:
            pass
    sys.exit(code)
