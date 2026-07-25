#!/usr/bin/env python3
"""
generate_media_library.py
=========================

Inventory the media folders that sit next to this script and write a tag-ready
``MEDIA_LIBRARY.md`` for the AutoAvatarGen script-writing AI.

WHAT IT DOES
    - Looks at every subfolder sitting next to this script.
    - Collects the image / video files Unity can actually load.
    - Writes ``MEDIA_LIBRARY.md`` right beside itself, listing each file as the
      exact NAME you type in a media tag, which tags accept it, and a
      copy-paste example.

WHERE TO PUT IT
    Drop this script into your external media ROOT -- the folder you point the
    app's "Media folder" setting at, i.e. the one that contains the
    ``Images/``, ``Logos/`` and ``BRoll/`` subfolders. Then run it:

        python generate_media_library.py

    (Optional: pass a folder to scan instead of the script's own location:
        python generate_media_library.py "D:/path/to/media root")

WHY
    The script tags ({Image:name}, {Video:name}, {BigImage:name}, ...) reference
    media by bare file name. Hand MEDIA_LIBRARY.md to the model together with
    SCRIPT_TAG_GUIDE.md so it uses real, existing names instead of placeholders
    like ArticleTemp / VideoTemp.

This mirrors how MediaPresentationSystem.cs resolves names on disk:
    Images/  and  Logos/   ->  {Image:} / {BigImage:}     (.png .jpg .jpeg)
    BRoll/                  ->  {Video:}                   (.mp4 .mov .webm .avi)
Resolution is per-folder and NON-recursive, exactly like the C# side.
"""

from __future__ import annotations

import sys
from datetime import datetime
from pathlib import Path

# --- Config: kept in sync with MediaPresentationSystem.cs --------------------
# ImageExtensions / VideoExtensions and the {Image:} "Images then Logos" search
# order. If you change these in the C#, change them here too.
IMAGE_EXTS = {".png", ".jpg", ".jpeg"}
VIDEO_EXTS = {".mp4", ".mov", ".webm", ".avi"}
IMAGE_EXT_ORDER = [".png", ".jpg", ".jpeg"]      # order Unity tries within a folder
VIDEO_EXT_ORDER = [".mp4", ".mov", ".webm", ".avi"]

OUTPUT_NAME = "MEDIA_LIBRARY.md"

# The subfolders Unity resolves media names against, in {Image:} search order.
# rank = tie-break priority when the same bare name exists in more than one
# image folder (Images is searched before Logos).
KNOWN_FOLDERS = [
    {
        "name": "Images",
        "kind": "image",
        "rank": 0,
        "blurb": "General images / screenshots. Searched FIRST by `{Image:}`.",
        "works_with": "`{Image:name}` (shows in the media area, default 3s) · "
                      "`{BigImage:name}` (fullscreen article screenshot covering the "
                      "left ¾ — stand the presenter on the right with "
                      "`{Position:Right}`)",
        "note": None,
    },
    {
        "name": "Logos",
        "kind": "image",
        "rank": 1,
        "blurb": "Company logos. Searched by `{Image:}` AFTER Images.",
        "works_with": "`{Image:name}` · `{BigImage:name}` (same disk lookup as "
                      "Images)",
        "note": "The `{Logo:name}` and `{BigMedia:name}` cards resolve names through "
                "the Unity **ContentCardAssets** asset, NOT this folder — the "
                "company name there may differ from the file name. Verify those names "
                "in Unity before using `{Logo:}` / `{BigMedia:}`.",
    },
    {
        "name": "BRoll",
        "kind": "video",
        "rank": 0,
        "blurb": "Video b-roll clips. Played by `{Video:}` (narration pauses while it plays).",
        "works_with": "`{Video:name}` (omit the duration to play the whole clip, or "
                      "add `,seconds` to cap it)",
        "note": "The `{BRoll:description}` side card resolves through the Unity "
                "**ContentCardAssets** asset, NOT this folder. Verify it in Unity.",
    },
]

# Characters that break a {...} tag if they appear in a file name.
BAD_NAME_CHARS = [",", "{", "}", '"', "|", "`"]


def classify(path: Path):
    """Return 'image', 'video', or None for a file based on its extension."""
    ext = path.suffix.lower()
    if ext in IMAGE_EXTS:
        return "image"
    if ext in VIDEO_EXTS:
        return "video"
    return None


def scan_folder(folder: Path, only_kind=None):
    """
    List the loadable media files directly inside `folder` (non-recursive, to
    match Unity). Returns (files, has_nested_dirs) where each file is a dict
    with stem / name / kind / ext.
    """
    files = []
    has_nested = False
    for p in sorted(folder.iterdir(), key=lambda x: x.name.casefold()):
        if p.is_dir():
            has_nested = True
            continue
        if not p.is_file() or p.name.startswith(".") or p.suffix.lower() == ".meta":
            continue
        kind = classify(p)
        if kind is None:
            continue
        if only_kind and kind != only_kind:
            continue
        files.append({"stem": p.stem, "name": p.name, "kind": kind, "ext": p.suffix.lower()})
    return files, has_nested


def cell(text: str) -> str:
    """Escape a value for a Markdown table cell (a literal | breaks the table)."""
    return text.replace("|", "\\|")


def primary_example(name: str, kind: str) -> str:
    """The ready-to-paste example tag shown in each table row."""
    if kind == "image":
        return "{Image:" + name + ",4}"
    return "{Video:" + name + ",6}"


def find_duplicates(entries, ext_order):
    """
    Flag bare names that exist more than once across a resolution scope (e.g.
    the same name in both Images/ and Logos/, or two extensions of one name in
    one folder). Reports which file a bare tag actually resolves to.
    """
    groups = {}
    for e in entries:
        groups.setdefault(e["stem"].casefold(), []).append(e)

    def rank(e):
        try:
            ext_rank = ext_order.index(e["ext"])
        except ValueError:
            ext_rank = len(ext_order)
        return (e["folder_rank"], ext_rank)

    warnings = []
    for group in groups.values():
        if len(group) < 2:
            continue
        ordered = sorted(group, key=rank)
        winner = ordered[0]
        locs = ", ".join("`{}/{}`".format(e["folder"], e["name"]) for e in ordered)
        warnings.append(
            "Duplicate name **`{stem}`** — {locs}. A bare tag resolves to "
            "`{wf}/{wn}`.".format(
                stem=group[0]["stem"], locs=locs,
                wf=winner["folder"], wn=winner["name"]
            )
        )
    return warnings


def build_markdown(root: Path) -> str:
    out = []
    summary_rows = []      # (folder, count_str, tags_str)
    sections = []          # per-folder detail blocks
    image_entries = []     # for cross-folder duplicate detection (Images + Logos)
    video_entries = []     # for BRoll duplicate detection
    warnings = []
    total_files = 0

    known_names = {f["name"].casefold() for f in KNOWN_FOLDERS}

    # --- Known folders, in {Image:} search order ----------------------------
    for cfg in KNOWN_FOLDERS:
        folder = root / cfg["name"]
        tag_label = {
            "Images": "`{Image:}` `{BigImage:}`",
            "Logos":  "`{Image:}` `{BigImage:}`",
            "BRoll":  "`{Video:}`",
        }[cfg["name"]]

        if not folder.is_dir():
            summary_rows.append((cfg["name"] + "/", "_not found_", tag_label))
            continue

        files, has_nested = scan_folder(folder, only_kind=cfg["kind"])
        total_files += len(files)
        summary_rows.append((cfg["name"] + "/", str(len(files)), tag_label))

        # Record entries for duplicate detection.
        for f in files:
            entry = dict(f, folder=cfg["name"], folder_rank=cfg["rank"])
            (image_entries if cfg["kind"] == "image" else video_entries).append(entry)

        # Build the detail section.
        block = []
        block.append("## `{}/` — {} file(s)".format(cfg["name"], len(files)))
        block.append("")
        block.append(cfg["blurb"])
        block.append("")
        block.append("**Works with:** " + cfg["works_with"])
        block.append("")
        if cfg["note"]:
            block.append("> ⚠️ " + cfg["note"])
            block.append("")

        if files:
            block.append("| Name to type | File on disk | Copy-paste example |")
            block.append("|---|---|---|")
            for f in files:
                block.append("| `{name}` | `{file}` | `{ex}` |".format(
                    name=cell(f["stem"]),
                    file=cell(f["name"]),
                    ex=cell(primary_example(f["stem"], f["kind"])),
                ))
                for ch in BAD_NAME_CHARS:
                    if ch in f["stem"]:
                        warnings.append(
                            "File **`{}`** contains `{}`, which breaks `{{...}}` tags. "
                            "Rename it.".format(f["name"], ch)
                        )
                        break
        else:
            block.append("_No usable {} files here yet._".format(cfg["kind"]))
            warnings.append("`{}/` exists but has no usable {} files.".format(
                cfg["name"], cfg["kind"]))

        if has_nested:
            warnings.append(
                "`{}/` has nested subfolders — Unity searches this folder "
                "non-recursively, so files inside them are NOT found. Move them up "
                "one level.".format(cfg["name"]))

        block.append("")
        sections.append("\n".join(block))

    # --- Other subfolders (won't resolve at runtime) ------------------------
    other_blocks = []
    for child in sorted(root.iterdir(), key=lambda x: x.name.casefold()):
        if not child.is_dir() or child.name.casefold() in known_names:
            continue
        files, _ = scan_folder(child)
        if not files:
            continue
        names = ", ".join("`{}`".format(f["stem"]) for f in files)
        other_blocks.append("- **`{}/`** — {} file(s): {}".format(
            child.name, len(files), names))

    # --- Loose media files sitting in the root (also won't resolve) ---------
    loose, _ = scan_folder(root)
    loose = [f for f in loose if f["name"] != OUTPUT_NAME]

    # --- Cross-folder duplicate names ---------------------------------------
    warnings.extend(find_duplicates(image_entries, IMAGE_EXT_ORDER))
    warnings.extend(find_duplicates(video_entries, VIDEO_EXT_ORDER))

    # --- Assemble the document ----------------------------------------------
    stamp = datetime.now().strftime("%Y-%m-%d %H:%M")
    out.append("# Media Library — files available for media tags")
    out.append("")
    out.append("> Auto-generated by `generate_media_library.py` on {}.".format(stamp))
    out.append("> Media root: `{}`".format(root))
    out.append("")
    out.append("**Give this file to the model together with `SCRIPT_TAG_GUIDE.md`** "
               "before asking it to write `Script.txt`.")
    out.append("")
    out.append("Rules for the model:")
    out.append("")
    out.append("- Use **only** the names listed below in media tags. Every name is a "
               "real file on disk — **do not invent names** and do not reuse "
               "placeholder names like `ArticleTemp` / `VideoTemp`.")
    out.append("- Type the name **exactly as shown, without the file extension**, e.g. "
               "`{Image:<name>,4}`. Match the casing.")
    out.append("- If a needed image or clip isn't listed here, it doesn't exist yet — "
               "pick the closest listed name or leave the tag out.")
    out.append("")
    out.append("---")
    out.append("")

    # Summary table
    out.append("## At a glance")
    out.append("")
    out.append("| Folder | Files | Tags |")
    out.append("|---|---|---|")
    for name, count, tags in summary_rows:
        out.append("| `{}` | {} | {} |".format(name, count, tags))
    out.append("")
    out.append("---")
    out.append("")

    out.extend(sections)

    if other_blocks:
        out.append("## ⚠️ Other subfolders found")
        out.append("")
        out.append("These are **not** the configured `Images` / `Logos` / `BRoll` "
                   "folders, so Unity will not find their files at runtime unless you "
                   "rename the folder (or set that subfolder name in the avatar's "
                   "Inspector). Listed for reference only:")
        out.append("")
        out.extend(other_blocks)
        out.append("")

    if loose:
        out.append("## ⚠️ Loose files in the media root")
        out.append("")
        out.append("These media files sit directly in the root, not inside a media "
                   "subfolder, so Unity will not find them. Move them into "
                   "`Images/`, `Logos/` or `BRoll/`:")
        out.append("")
        for f in loose:
            out.append("- `{}`".format(f["name"]))
        out.append("")

    if warnings:
        out.append("## ⚠️ Check these")
        out.append("")
        for w in warnings:
            out.append("- " + w)
        out.append("")

    out.append("---")
    out.append("")
    out.append("_Total resolvable media files: {}._".format(total_files))
    out.append("")
    return "\n".join(out), total_files, summary_rows


def main():
    root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parent
    if not root.is_dir():
        print("ERROR: not a folder: {}".format(root))
        return 1

    markdown, total, summary_rows = build_markdown(root)
    out_path = root / OUTPUT_NAME
    out_path.write_text(markdown, encoding="utf-8")

    # Console summary
    print("Scanned: {}".format(root))
    for name, count, _ in summary_rows:
        print("  {:<10} {}".format(name, count))
    if total == 0:
        print("\nNo resolvable media found. Place this script in your media ROOT —")
        print("the folder that contains the Images/, Logos/ and BRoll/ subfolders.")
    print("\nWrote {} ({} file(s)).".format(out_path, total))
    return 0


if __name__ == "__main__":
    code = main()
    # Friendly pause so a double-click on Windows doesn't flash the window shut.
    if sys.platform == "win32":
        try:
            input("\nDone. Press Enter to close...")
        except EOFError:
            pass
    sys.exit(code)
