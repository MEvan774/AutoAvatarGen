"""
AutoAvatarGen — ElevenLabs TTS Pre-Processor  (v2 — Segmented)
===============================================================
Takes a Script.txt with ## SECTION NAME headings, splits it into segments,
sends each segment to ElevenLabs separately, and outputs per-segment files.

When the script has 2+ segments, outputs get a numeric prefix (01_, 02_, …)
so Unity can sort them into the correct playback order and stitch them
back-to-back.  A manifest.json is always emitted so Unity knows each
segment's leading/trailing silence (from word-level ElevenLabs timing)
and can trim it to produce seamless inter-segment pacing.

  output/
    manifest.json             ← order + speech_start/end per segment
    01_COLD_OPEN.mp3          ← audio per segment (prefix only if 2+ segments)
    01_COLD_OPEN_timed.txt    ← script with T= timestamps baked in
    02_SETUP.mp3
    02_SETUP_timed.txt
    ...
    word_timestamps/
      01_COLD_OPEN_words.json ← debug: word-level alignment data

What gets STRIPPED before TTS (never read aloud):
  - ## SECTION HEADERS
  - {Emotion}        e.g. {Concerned} {Excited} {Neutral} {Serious} {Sad}
  - {Position:...}   e.g. {Position:Left,Cut}
  - {Zoom:...}       e.g. {Zoom:In}
  - {Black:...}      e.g. {Black:3}  (fullscreen black panel, duration in seconds)
  - {Image:...}      e.g. {Image:file,5}
  - {Video:...}      e.g. {Video:clip,0}
  - {Headline:...}   content cards
  - {Excerpt:...}
  - {Quote:...}
  - {Stat:...}
  - {Logo:...}
  - {BRoll:...}
  - {BigMedia:...}  e.g. {BigMedia:logo_name,4}  (large centered image over character)
  - [stage directions]  e.g. [pause] [deadpan] [sips coffee]

Usage:
  python elevenlabs_tts_processor.py
  python elevenlabs_tts_processor.py --script MyScript.txt
  python elevenlabs_tts_processor.py --script MyScript.txt --out_dir ./output
  python elevenlabs_tts_processor.py --script MyScript.txt --dry_run
  python elevenlabs_tts_processor.py --script MyScript.txt --segment "COLD OPEN"
"""

import re
import json
import sys
import os
import base64
import argparse
import requests

# ==============================================================================
#  CONFIGURE YOUR VOICE HERE — edit once, never touch again
# ==============================================================================

ELEVENLABS_API_KEY = "sk_d5b1984b2dcfcc8ca651004f5a6d39e471a4db30f552806a"

VOICE_CONFIG = {
    "voice_id":          "rPMkKgdwgIwqv4fXgR6N",    # from elevenlabs.io/app/voice-lab
    "model_id":          "eleven_multilingual_v2", # or "eleven_turbo_v2_5" for speed
    "stability":         0.45,   # 0.0-1.0  | lower = more expressive
    "similarity_boost":  0.80,   # 0.0-1.0  | higher = truer to cloned voice
    "style":             0.35,   # 0.0-1.0  | 0 = neutral, 1 = very dramatic
    "use_speaker_boost": True,   # enhances voice clarity
}

# ==============================================================================
#  MARKER PATTERNS — everything that must be stripped before sending to TTS
# ==============================================================================

_ALL_MARKERS = re.compile(
    r'\{(?:Excited|Serious|Concerned|Neutral|Sad)\}'        # emotion states
    r'|\{Position:\w+(?:,\w+)?\}'                           # position markers
    r'|\{Zoom:\w+\}'                                        # zoom markers
    r'|\{Black:\d+(?:\.\d+)?\}'                             # black panel markers
    r'|\{(?:Image|Video):[^}]+\}'                           # media markers
    r'|\{Headline:"[^"]*","[^"]*",\d+(?:\.\d+)?\}'          # headline cards
    r'|\{Excerpt:"[^"]*","[^"]*","[^"]*",\d+(?:\.\d+)?\}'   # excerpt cards
    r'|\{Quote:"[^"]*","[^"]*","[^"]*",\d+(?:\.\d+)?\}'     # quote cards
    r'|\{Stat:"[^"]*","[^"]*","[^"]*",\d+(?:\.\d+)?\}'      # stat cards
    r'|\{Logo:[^,}]+,\d+(?:\.\d+)?\}'                       # logo cards
    r'|\{BRoll:[^,}]+,\d+(?:\.\d+)?\}'                      # broll cards
    r'|\{BigMedia:[^,}]+,\d+(?:\.\d+)?\}'                   # big-media feature cards
    r'|\[[\w\s]+\]'                                         # [stage directions]
)

# Section header line:  ## COLD OPEN  /  ## SETUP  etc.
_SECTION_HEADER = re.compile(r'^##\s+(.+)$', re.MULTILINE)


# ==============================================================================
#  STEP 0 — Split script into named segments on ## headings
# ==============================================================================

def split_into_segments(raw_script: str) -> list:
    """
    Splits raw_script at every ## HEADING line.
    Returns list of dicts: { 'name', 'slug', 'raw' }
    Text before the first heading is discarded (usually empty).
    """
    segments = []
    headers  = list(_SECTION_HEADER.finditer(raw_script))

    if not headers:
        # No headings — treat whole script as one segment
        segments.append({'name': 'FULL', 'slug': 'FULL', 'raw': raw_script.strip()})
        return segments

    for i, header in enumerate(headers):
        name          = header.group(1).strip()
        slug          = _make_slug(name)
        content_start = header.end()
        content_end   = headers[i + 1].start() if i + 1 < len(headers) else len(raw_script)
        raw_content   = raw_script[content_start:content_end].strip()
        segments.append({'name': name, 'slug': slug, 'raw': raw_content})

    return segments


def _make_slug(name: str) -> str:
    """'COLD OPEN' -> 'COLD_OPEN',  'Story 1 - Lead' -> 'STORY_1_LEAD'"""
    slug = name.upper()
    slug = re.sub(r'[^\w\s]', '', slug)
    slug = re.sub(r'\s+', '_', slug.strip())
    return slug


# ==============================================================================
#  STEP 1 — Extract markers from a single segment
# ==============================================================================

def extract_markers(raw_segment: str) -> tuple:
    """
    Finds every Unity marker in the raw segment and records:
      'marker'      : original marker text  e.g. {Position:Left,Cut}
      'char_index'  : index in RAW segment string
      'clean_index' : index in CLEAN (no-marker) string
    Returns (markers_list, clean_text_for_tts)
    """
    markers = []
    for match in _ALL_MARKERS.finditer(raw_segment):
        text_before  = raw_segment[:match.start()]
        clean_before = _ALL_MARKERS.sub('', text_before)
        markers.append({
            'marker':      match.group(),
            'char_index':  match.start(),
            'clean_index': len(clean_before),
        })

    clean = _ALL_MARKERS.sub('', raw_segment)
    clean = re.sub(r'  +', ' ', clean).strip()
    return markers, clean


# ==============================================================================
#  STEP 2 — Call ElevenLabs with-timestamps endpoint
# ==============================================================================

def call_elevenlabs(clean_text: str, config: dict, api_key: str) -> tuple:
    """
    Returns (mp3_bytes, word_timestamps)
    word_timestamps: [{'word': str, 'start': float, 'end': float}, ...]
    """
    url = (f"https://api.elevenlabs.io/v1/text-to-speech"
           f"/{config['voice_id']}/with-timestamps")

    headers = {
        "xi-api-key":   api_key,
        "Content-Type": "application/json",
        "Accept":       "application/json",
    }

    payload = {
        "text":     clean_text,
        "model_id": config["model_id"],
        "voice_settings": {
            "stability":         config["stability"],
            "similarity_boost":  config["similarity_boost"],
            "style":             config["style"],
            "use_speaker_boost": config["use_speaker_boost"],
        },
    }

    print("    -> Sending to ElevenLabs...")
    response = requests.post(url, headers=headers, json=payload)

    if response.status_code != 200:
        print(f"\n  [ERROR] ElevenLabs API returned {response.status_code}")
        try:
            err = response.json()
            msg = err.get('detail', {}).get('message', response.text)
            print(f"          {msg}")
        except Exception:
            print(f"          {response.text[:300]}")
        sys.exit(1)

    data        = response.json()
    audio_bytes = base64.b64decode(data["audio_base64"])

    alignment  = data.get("alignment", {})
    chars      = alignment.get("characters", [])
    char_start = alignment.get("character_start_times_seconds", [])
    char_end   = alignment.get("character_end_times_seconds", [])

    word_timestamps = _group_chars_into_words(chars, char_start, char_end)
    print(f"    OK {len(audio_bytes)//1024} KB audio  |  {len(word_timestamps)} words timestamped")
    return audio_bytes, word_timestamps


def _group_chars_into_words(chars, starts, ends) -> list:
    words, current_word, current_start = [], [], None

    for i, ch in enumerate(chars):
        if ch in (' ', '\n', '\t'):
            if current_word:
                words.append({
                    'word':  ''.join(current_word),
                    'start': current_start,
                    'end':   ends[i - 1] if i > 0 else 0.0,
                })
                current_word, current_start = [], None
        else:
            if current_start is None:
                current_start = starts[i]
            current_word.append(ch)

    if current_word:
        words.append({
            'word':  ''.join(current_word),
            'start': current_start,
            'end':   ends[-1] if ends else 0.0,
        })
    return words


# ==============================================================================
#  STEP 3 — Map each marker's clean_index to an exact timestamp
# ==============================================================================

def map_markers_to_timestamps(markers: list, word_timestamps: list, clean_script: str) -> list:
    char_times     = _build_char_time_map(clean_script, word_timestamps)
    total_duration = word_timestamps[-1]['end'] if word_timestamps else 0.0
    timed = []

    for m in markers:
        idx = min(m['clean_index'], len(char_times) - 1)
        t   = None
        for probe in range(idx, len(char_times)):
            if char_times[probe] is not None:
                t = char_times[probe]
                break
        timed.append({**m, 'trigger_time': round(t if t is not None else total_duration, 3)})

    return timed


def _build_char_time_map(clean_script: str, word_timestamps: list) -> list:
    char_times = [None] * len(clean_script)
    script_pos = 0

    for w in word_timestamps:
        word_text = w['word']
        idx = clean_script.find(word_text, script_pos)
        if idx == -1:
            stripped = re.sub(r'[^\w]', '', word_text)
            for p in range(script_pos, len(clean_script)):
                window = re.sub(r'[^\w]', '', clean_script[p:p + len(word_text) + 2])
                if window.startswith(stripped):
                    idx = p
                    break
        if idx != -1:
            char_times[idx] = w['start']
            script_pos = idx + 1

    return char_times


# ==============================================================================
#  STEP 4 — Rebuild segment script with T= timestamps baked in
# ==============================================================================

def rebuild_timed_script(raw_segment: str, timed_markers: list) -> str:
    """Injects T= into every marker, working backwards so positions stay valid."""
    sorted_markers = sorted(timed_markers, key=lambda m: m['char_index'], reverse=True)
    script = raw_segment

    for m in sorted_markers:
        original    = m['marker']
        replacement = _stamp_marker(original, m['trigger_time'])
        script = (script[:m['char_index']]
                  + replacement
                  + script[m['char_index'] + len(original):])
    return script


def _stamp_marker(marker: str, t: float) -> str:
    ts    = f"T={t:.3f}"
    inner = marker[1:-1]

    # [stage direction]
    if marker.startswith('['):
        return f"[{inner},{ts}]"

    # {Emotion}
    if re.match(r'^(Excited|Serious|Concerned|Neutral|Sad)$', inner):
        return f"{{{inner},{ts}}}"

    # {Zoom:In}
    if inner.startswith('Zoom:'):
        return f"{{{inner},{ts}}}"

    # {Position:Left,Cut}
    if inner.startswith('Position:'):
        return f"{{{inner},{ts}}}"

    # {Black:3}  →  {Black:D=3,T=X.XXX}
    m_black = re.match(r'^Black:(\d+(?:\.\d+)?)$', inner)
    if m_black:
        return f"{{Black:D={m_black.group(1)},{ts}}}"

    # {Image:file,5}  /  {Video:file,0}
    m_media = re.match(r'^(Image|Video):([^,}]+)(?:,(\d+(?:\.\d+)?))?$', inner)
    if m_media:
        dur = m_media.group(3) or '0'
        return f"{{{m_media.group(1)}:{m_media.group(2)},{ts},D={dur}}}"

    # {Logo:name,5}  /  {BRoll:name,4}  /  {BigMedia:name,5}
    m_lb = re.match(r'^(Logo|BRoll|BigMedia):([^,}]+),(\d+(?:\.\d+)?)$', inner)
    if m_lb:
        return f"{{{m_lb.group(1)}:{m_lb.group(2)},{ts},D={m_lb.group(3)}}}"

    # Content cards: Headline / Excerpt / Quote / Stat
    m_card = re.match(r'^(Headline|Excerpt|Quote|Stat):(.*),(\d+(?:\.\d+)?)$',
                      inner, re.DOTALL)
    if m_card:
        return f"{{{m_card.group(1)}:{m_card.group(2)},{ts},D={m_card.group(3)}}}"

    # Fallback
    return f"{{{inner},{ts}}}"


# ==============================================================================
#  PROCESS ONE SEGMENT
# ==============================================================================

def process_segment(segment: dict, out_dir: str, api_key: str,
                    config: dict, dry_run: bool = False) -> dict:
    slug = segment['slug']
    name = segment['name']
    raw  = segment['raw']

    print(f"\n  [{slug}]  ## {name}")
    print(f"  {'─' * 55}")

    # Step 1
    markers, clean = extract_markers(raw)
    print(f"    Markers : {len(markers)}   Clean chars : {len(clean)}")

    if dry_run:
        print(f"    {'IDX':<4} {'CLEAN POS':>9}  MARKER")
        for i, m in enumerate(markers):
            print(f"    {i:<4} {m['clean_index']:>9}  {m['marker'][:60]}")
        return {'slug': slug, 'name': name, 'dry_run': True}

    # Step 2
    audio_bytes, word_ts = call_elevenlabs(clean, config, api_key)

    # Step 3
    timed_markers = map_markers_to_timestamps(markers, word_ts, clean)

    # Step 4
    timed_script = rebuild_timed_script(raw, timed_markers)

    # Write outputs
    words_dir   = os.path.join(out_dir, "word_timestamps")
    os.makedirs(words_dir, exist_ok=True)

    audio_path  = os.path.join(out_dir,  f"{slug}.mp3")
    script_path = os.path.join(out_dir,  f"{slug}_timed.txt")
    words_path  = os.path.join(words_dir, f"{slug}_words.json")

    with open(audio_path,  'wb')                  as f: f.write(audio_bytes)
    with open(script_path, 'w', encoding='utf-8') as f: f.write(timed_script)
    with open(words_path,  'w', encoding='utf-8') as f: json.dump(word_ts, f, indent=2)

    duration     = word_ts[-1]['end']   if word_ts else 0.0
    speech_start = word_ts[0]['start']  if word_ts else 0.0
    speech_end   = word_ts[-1]['end']   if word_ts else 0.0

    print(f"\n    Timestamp map:")
    print(f"    {'MARKER':<52} {'TIME':>8}")
    print(f"    {'─'*52} {'─'*8}")
    for m in timed_markers:
        print(f"    {m['marker'][:52]:<52} {m['trigger_time']:>7.3f}s")

    print(f"\n    -> {slug}.mp3          ({duration:.1f}s, speech {speech_start:.2f}s–{speech_end:.2f}s)")
    print(f"    -> {slug}_timed.txt")

    return {
        'slug':         slug,
        'name':         name,
        'duration':     duration,
        'speech_start': speech_start,
        'speech_end':   speech_end,
        'audio':        audio_path,
        'script':       script_path,
        'n_markers':    len(timed_markers),
    }


# ==============================================================================
#  MAIN
# ==============================================================================

def main():
    parser = argparse.ArgumentParser(
        description="AutoAvatarGen ElevenLabs TTS Pre-Processor — Segmented v2")
    script_dir = os.path.dirname(os.path.abspath(__file__))
    default_script = os.path.join(script_dir, 'script', 'Script.txt')
    parser.add_argument('--script',  default=default_script,
                        help='Path to input Script.txt  (default: <this_dir>/script/Script.txt)')
    parser.add_argument('--out_dir', default='./output',
                        help='Output folder  (default: ./output)')
    parser.add_argument('--dry_run', action='store_true',
                        help='Parse only — no ElevenLabs API call')
    parser.add_argument('--segment', default=None, metavar='NAME',
                        help='Process only one segment, e.g. --segment "COLD OPEN"')
    args = parser.parse_args()

    # Validate config
    if not args.dry_run:
        if ELEVENLABS_API_KEY == "YOUR_API_KEY_HERE":
            print("[ERROR] Set ELEVENLABS_API_KEY at the top of this script.")
            sys.exit(1)
        if VOICE_CONFIG["voice_id"] == "YOUR_VOICE_ID_HERE":
            print("[ERROR] Set voice_id in VOICE_CONFIG at the top of this script.")
            sys.exit(1)

    # Read script
    if not os.path.exists(args.script):
        print(f"[ERROR] Script not found: {args.script}")
        sys.exit(1)

    with open(args.script, 'r', encoding='utf-8') as f:
        raw_script = f.read()

    print(f"\n AutoAvatarGen — ElevenLabs TTS Pre-Processor  (Segmented v2)")
    print(f" ──────────────────────────────────────────────────────────────")
    print(f" Input  : {args.script}")
    print(f" Output : {args.out_dir}/")
    if args.dry_run:
        print(f" Mode   : DRY RUN (no API calls)")

    # Split into segments
    all_segments = split_into_segments(raw_script)

    print(f"\n Segments found: {len(all_segments)}")
    for s in all_segments:
        n = len(_ALL_MARKERS.findall(s['raw']))
        clean_len = len(_ALL_MARKERS.sub('', s['raw']).strip())
        print(f"   ## {s['name']:<30}  slug={s['slug']:<20}  {n} markers  {clean_len} chars")

    # Filter to one segment if --segment flag used
    segments = all_segments
    if args.segment:
        target = _make_slug(args.segment)
        segments = [s for s in all_segments if s['slug'] == target]
        if not segments:
            available = [s['name'] for s in all_segments]
            print(f"\n[ERROR] Segment '{args.segment}' not found.")
            print(f"        Available: {available}")
            sys.exit(1)

    # When processing multiple segments, prefix each slug with a zero-padded
    # order number so Unity can pick them up in the right playback order.
    # Single-segment runs keep the original slug for backwards compatibility.
    if len(segments) > 1:
        width = max(2, len(str(len(segments))))
        for i, s in enumerate(segments):
            s['order'] = i + 1
            s['slug']  = f"{i + 1:0{width}d}_{s['slug']}"
    else:
        segments[0]['order'] = 1

    # Process
    os.makedirs(args.out_dir, exist_ok=True)
    results = []

    print(f"\n{'=' * 60}")
    for seg in segments:
        result = process_segment(
            segment = seg,
            out_dir = args.out_dir,
            api_key = ELEVENLABS_API_KEY,
            config  = VOICE_CONFIG,
            dry_run = args.dry_run,
        )
        result['order'] = seg.get('order', len(results) + 1)
        results.append(result)

    # Write manifest.json — Unity reads this to know the playback order and
    # each segment's leading/trailing silence (for seamless stitching).
    if not args.dry_run:
        manifest = {
            "segments": [
                {
                    "order":        r['order'],
                    "slug":         r['slug'],
                    "name":         r['name'],
                    "audio_file":   f"{r['slug']}.mp3",
                    "script_file":  f"{r['slug']}_timed.txt",
                    "duration":     round(r.get('duration', 0.0),     3),
                    "speech_start": round(r.get('speech_start', 0.0), 3),
                    "speech_end":   round(r.get('speech_end', 0.0),   3),
                }
                for r in results
            ]
        }
        manifest_path = os.path.join(args.out_dir, 'manifest.json')
        with open(manifest_path, 'w', encoding='utf-8') as f:
            json.dump(manifest, f, indent=2)

    # Summary
    print(f"\n{'=' * 60}")
    if args.dry_run:
        print(f"  Dry run complete — {len(results)} segment(s) parsed, no API calls made.")
        print(f"\n  Run without --dry_run to generate audio.")
    else:
        total_dur     = sum(r.get('duration', 0) for r in results)
        total_markers = sum(r.get('n_markers', 0) for r in results)

        print(f"  DONE — {len(results)} segment(s)")
        print(f"  {'─'*64}")
        print(f"  {'#':>2}  {'SEGMENT':<28} {'DURATION':>10}  {'SPEECH':>12}  {'MARKERS':>8}")
        print(f"  {'─'*2}  {'─'*28} {'─'*10}  {'─'*12}  {'─'*8}")
        for r in results:
            sp_rng = f"{r.get('speech_start',0):.2f}–{r.get('speech_end',0):.2f}"
            print(f"  {r.get('order','?'):>2}  {r['name']:<28} {r.get('duration',0):>9.1f}s"
                  f"  {sp_rng:>12}  {r.get('n_markers',0):>8}")
        print(f"  {'─'*2}  {'─'*28} {'─'*10}  {'─'*12}  {'─'*8}")
        print(f"      {'TOTAL':<28} {total_dur:>9.1f}s  {'':>12}  {total_markers:>8}")
        print(f"\n  -> {os.path.join(args.out_dir, 'manifest.json')}")
        print(f"\n  Unity picks up segments automatically from {args.out_dir}/")
        print(f"  (reads manifest.json, stitches audio back-to-back with")
        print(f"   leading/trailing silence trimmed per word timestamps).")
    print()


if __name__ == "__main__":
    main()
