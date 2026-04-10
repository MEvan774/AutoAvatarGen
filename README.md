# Animated Tech Commentary — Unity Production System

## Project Overview

This is a Unity-based automated production system for a high-frequency YouTube channel focused on casual tech news and trend commentary. The system renders stylized animated commentary videos with AI-driven character movement, camera motion, scene transitions, and content zone management.

**Format:** 8–12 minute videos with an animated character presenting tech news, commentary, and reactions alongside dynamic content cards (headlines, screenshots, social posts, data visualizations, quotes).

**Target audience context:** 70–90% mobile viewers, many consuming semi-passively (background listening). Every design decision prioritizes mobile readability and audio-first comprehension.

## Design Reference

See `Youtube_video_template` for the full production blueprint covering layout specs, pacing models, motion design, audio strategy, content formulas, and transition catalogs.

## Architecture

### Core Systems to Build

1. **Scene/Layout Manager** — Manages 23 layout states (9 Left, 9 Right, 5 Center) and transitions between them
2. **Character Controller** — State machine with Idle → Micro Reaction → Emphasis → Major Shift states
3. **Camera Controller** — Automated push-in, pull-back, lateral pan, and static holds with ease-in-out curves
4. **Content Zone Manager** — Displays/rotates content cards (headlines, screenshots, social posts, quote cards, data viz, key phrase cards, comparison layouts)
5. **Background System** — Animated looping backgrounds with 5 mood variants controlled via shader parameters
6. **Transition System** — 11 transition types (3 cuts + 8 smooth transitions)
7. **Audio Sync System** — Reads script markers and audio amplitude to trigger character animations and scene changes
8. **Script/Timeline Driver** — Consumes a per-video script file with timestamped markers for all events

### Layout System

**Screen zones (16:9 at 1080p):**
- Character zone: left or right 25–30% of screen
- Content zone: opposite 55–65% of screen
- Breathing space: 5–10% gutter between character and content
- Top safe zone: top 10% (reserved for mobile UI)
- Bottom safe zone: bottom 15% (reserved for YouTube controls)
- Branding strip: optional, max 5% height

**Character sizing:** 20–28% of screen width. Head in upper 40% of frame. Always faces toward content zone; faces camera when centered.

**Text sizing at 1080p:**
- Primary headlines: min 48px, bold sans-serif
- Secondary text: 32–40px
- Body/excerpt: 24–30px, max 2–3 lines on screen
- Source/attribution: 18–22px

### Layout State IDs

Layout states follow this naming convention:
- `L-01` through `L-09`: Character left + various content zone states
- `R-01` through `R-09`: Character right (mirrors of L)
- `C-01` through `C-05`: Character centered

Key states:
- `L-01`/`R-01`: Character + headline card
- `L-02`/`R-02`: Character + excerpt/article detail
- `L-03`/`R-03`: Character + social media post
- `L-05`/`R-05`: Character + contextual image
- `L-06`/`R-06`: Character + key phrase text card
- `L-07`/`R-07`: Character + comparison layout
- `L-09`/`R-09`: Character + video/clip embed
- `C-01`: Center, facing camera (cold open/hook)
- `C-02`: Center, reaction
- `C-03`: Center, branded closing
- `C-04`: Center, breather
- `C-05`: Center + branded card

### Character State Machine

| State | Description | Trigger | Duration |
|-------|------------|---------|----------|
| Idle | Breathing, micro-sway, blink | Default/baseline | Always running |
| Micro Reaction | Head tilt, eyebrow raise, lean | Script markers or audio spikes, every 8–15s | 1–2s, return to Idle |
| Emphasis | Deliberate gesture, head nod, lean forward | Emphasis markers, every 20–45s, max 2 in a row | 2–4s, return to Idle |
| Major Shift | Full reposition, enter/exit frame, pose change | Scene transition markers, every 45–90s | Varies, transitions to new Idle |

**Trigger sources:**
- Script markers (pre-placed timestamps)
- Audio amplitude analysis (real-time)
- Timer-based fallback: auto-fire random micro reaction if no trigger for 12+ seconds

### Transition System

**Cut types:**
- `CUT-01`: Hard cut (instant, 0 frames) — 60% of all transitions
- `CUT-02`: Jump cut (content swaps, character stays) — 10–15%
- `CUT-03`: L-cut/J-cut (audio leads visual by 0.5–1.5s) — 10–15%

**Smooth transitions:**
- `TRANS-01`: Crossfade/dissolve (0.5–1.0s)
- `TRANS-02`: Camera push-in (2–3s, 100%→110–115%, ease-in-out)
- `TRANS-03`: Camera pull-back (2–3s, 115%→100%, ease-in-out)
- `TRANS-04`: Lateral slide for content cards (0.5–1.0s, ease-out)
- `TRANS-05`: Character slide reposition (0.8–1.5s, ease-in-out)
- `TRANS-06`: Character center pull (0.5–1.0s, content fades out)
- `TRANS-07`: Character side return (0.5–1.0s, content fades in)
- `TRANS-08`: Background mood crossfade (2–4s, lerp shader params)

**Camera rules:** Static 80% of runtime. Max zoom 115–120%. Max pan 5–10% frame width. All moves use ease-in-out. Never combine camera + character movement simultaneously. Every camera move resolves to static within 3s.

### Background System

5 mood variants, switched via shader/particle parameter lerps:

| Variant | Tone | Use |
|---------|------|-----|
| Calm/Neutral | Cool blue/purple, slow drift | Default, exposition |
| Energetic | Warmer, faster particles, brighter | Analysis, arguments |
| Tense/Dramatic | Dark, red/orange accents, heavy motion | Escalation, serious |
| Playful/Light | Bright, varied movement | Humor, light topics |
| Minimal/Focus | Nearly static, very muted | Breathers, dense content |

Loop duration: 30–60s minimum. Motion should be perceptible but not trackable. Always crossfade (TRANS-08), never hard-swap.

### Audio Design Specs

**Voice pacing targets:**
- Conversational (default): 130–160 WPM, 60–70% of video
- Emphatic: 160–180 WPM, 20–25%
- Dramatic pause: 1–2s full stop, 5–10%
- Rapid fire: 180–200 WPM, max 15–20s at a time

**Background music:** Present 70–80% of video at 20–30% of voice level. 2–3 tracks per video. Crossfade on transitions. Drop entirely for dramatic pauses.

**SFX:** 4–8 per minute max. All under 1 second. Sit under vocals. Subtle whoosh on card swaps, pop on headlines, thud on reveals.

**Re-engagement cues:** Every 60–90s include a vocal pattern break. Audible transition cue at each topic change. Verbally number stories for passive listeners.

## Video Structure Template

**Target length:** 8–12 minutes, 3-act structure.

| Segment | Time | Purpose |
|---------|------|---------|
| Cold Open | 0:00–0:30 | Hook with strongest story, no intro |
| Story 1 (Lead) | 0:30–4:00 | Main story, deepest analysis |
| Transition Beat | 4:00–4:15 | Verbal bridge |
| Story 2 (Secondary) | 4:15–7:00 | Solid coverage, slightly faster |
| Transition Beat | 7:00–7:15 | Verbal bridge |
| Story 3 (Quick Hit) | 7:15–9:00 | Headline + quick take |
| Synthesis & Close | 9:00–10:30 | Connect themes, final take, CTA |

**Pacing rules:**
- Minor visual reset every 8–15 seconds
- Major visual reset every 45–90 seconds
- Never hold the same frame for more than 20 seconds
- Max sustained high-energy: 45–60s before cooldown
- Always follow escalation with a breather
- Max 2–3 big animated reactions per video
- Content zone empty for no more than 5–7 seconds

## Key Constraints & Rules

- Character side swaps ONLY on story transitions, never mid-story
- Character centering max 30 seconds before returning to sided layout
- Never combine camera movement with character movement
- 60:40 ratio of hard cuts to smooth transitions
- All text must have semi-opaque background plate or drop shadow
- Medium or bold font weights only (no thin fonts)
- Reserve large reactions for genuinely significant moments (2–3 per video)
- Every character reposition must have a narrative reason
- Content zone priority waterfall: direct evidence → contextual imagery → key phrase card → comparison layout → centered character (last resort)

## Thumbnail & Title

**Thumbnail:** Max 3 elements (character face, one image/logo, 2–4 words text). Test readability at 168×94px.

**Title:** 50–65 characters. Front-load subject. No dates. Curiosity gap, not clickbait.

**First frame must:** match thumbnail colors/pose, show topic immediately, be readable on mobile.