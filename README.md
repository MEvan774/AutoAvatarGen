# AutoAvatarGen — Animated Tech Commentary Unity Production System

> **Project:** AutoAvatarGen
> **Engine:** Unity 6000.3.6f1
> **Render Pipeline:** URP

## What This Is

A Unity-based automated production system for a high-frequency YouTube channel focused on casual tech news and trend commentary. The system renders stylized animated commentary videos with AI-driven character movement, camera motion, scene transitions, and content zone management.

**Format:** 8–12 minute videos with an animated character presenting tech news, commentary, and reactions alongside dynamic content cards (headlines, screenshots, social posts, data visualizations, quotes).

**Target audience:** 70–90% mobile viewers, many consuming semi-passively (background listening). Every design decision must prioritize mobile readability and audio-first comprehension.

**Style reference:** Premium-feeling animated commentary (think structured, brand-consistent visual resets — NOT meme-chaos editing). Editing pacing similar to Rogue Internet Man but cleaner and more systematic. Topic scope similar to Rev Says Desu and Clownfish TV.

---

## Guiding Principles

These are non-negotiable and apply to every system, script, and design decision:

1. **Clarity > Gimmicks.** Every visual choice serves comprehension, not spectacle.
2. **Consistency > Novelty.** A repeatable, recognizable format builds trust and allows faster production.
3. **Audio Must Carry.** The content must be fully understandable even if the screen is not being watched. Visuals enhance but do not replace the spoken word.

---

## Existing Project Structure

The Unity project already contains these key components:

- `Assets/Scripts/ScriptFileReader.cs` — Reads a per-video `Script.txt` file and drives the timeline
- `Assets/Scripts/NewMonoBehaviourScript.cs` — Avatar/character controller (sprite-based: neutral, excited, serious, sad states)
- `Assets/Scripts/MediaPresentationSystem.cs` — Media presentation system handling content zone display, character positioning (Left/Right/Center), zoom markers, and media markers
- `Assets/Scripts/LinuxTransparentRecorder.cs` — Headless recording for Linux rendering
- `Assets/Scenes/SampleScene.unity` — Main scene with Canvas-based UI, floating background shapes, avatar renderer, and pivot system
- Background system uses floating shape prefabs on a dedicated layer

---

## Core Systems — Build Specifications

### 1. Scene/Layout Manager

Manages 23 layout states and transitions between them.

**Screen zones (16:9 at 1080p):**

| Zone | Specification |
|------|--------------|
| Character Zone | Left or right 25–30% of screen. Never overlaps with primary content. |
| Content Zone | Opposite 55–65% of screen. Headlines, images, excerpts, visual aids. |
| Breathing Space | 5–10% vertical gutter between character and content. |
| Top Safe Zone | Top 10% reserved — mobile status bars and YouTube UI overlay this. |
| Bottom Safe Zone | Bottom 15% reserved — YouTube progress bar, captions, controls. |
| Branding Strip | Optional thin bar, max 5% of screen height. |

**Character sizing:** 20–28% of screen width. Head in upper 40% of frame. Always faces toward content zone; faces camera when centered.

**Text sizing at 1080p:**

| Element | Size |
|---------|------|
| Primary headlines | Min 48px, bold sans-serif |
| Secondary text | 32–40px |
| Body/excerpt | 24–30px, max 2–3 lines on screen |
| Source/attribution | 18–22px |

**Text rules:**
- Never more than 3 lines of readable text on screen at once
- Prefer highlighted key phrases over full paragraphs
- All text must have semi-opaque background plate or drop shadow
- Medium or bold font weights only — no thin fonts
- Use progressive reveal: headline first, supporting detail 1–2 seconds later

### 2. Layout State IDs

**Character Left (L-01 through L-09):**

| ID | Content Zone State |
|----|-------------------|
| L-01 | Character left + headline card |
| L-02 | Character left + excerpt/article detail |
| L-03 | Character left + social media post |
| L-04 | Character left + data visualization/stat |
| L-05 | Character left + contextual image |
| L-06 | Character left + key phrase text card |
| L-07 | Character left + comparison layout |
| L-08 | Character left + quote card |
| L-09 | Character left + video/clip embed |

**Character Right (R-01 through R-09):** Mirror of L states.

**Character Center:**

| ID | State |
|----|-------|
| C-01 | Center, facing camera (cold open/hook) |
| C-02 | Center, reaction |
| C-03 | Center, branded closing |
| C-04 | Center, breather |
| C-05 | Center + branded card |

**Total: 23 layout states.**

### 3. Character Controller — State Machine

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

**Vocal tone sync mapping:**

| Vocal Cue | Motion Response |
|-----------|----------------|
| Rising pitch / excitement | Lean forward, eyes widen, gesture speed increases |
| Dropping pitch / serious | Straightens, gestures slow, slight head-down angle |
| Pause / silence | Returns to idle, brief stillness |
| Rhetorical question | Head tilt, eyebrow raise, hold 1–2 seconds |
| Punchline or payoff | Quick sharp gesture, then immediate return to composed idle |
| Listing items | Subtle hand count or tick gestures |

**Character positioning rules:**
- Side swaps ONLY on story transitions, never mid-story
- Centering max 30 seconds before returning to sided layout
- Every reposition must have a narrative reason
- Never reposition just for visual variety
- Always tie repositioning to a natural pause or topic change

### 4. Camera Controller

- Static composition for 80% of runtime
- Push-in (110–115%) on emphasis/reaction scenes, 2–3 seconds, ease-in-out
- Pull-back on breather scenes, 2–3 seconds, ease-in-out
- Lateral pan (5–10% of frame width) on content card swaps, 1.5–2.5 seconds
- Max zoom: 115–120%. Never exceed 120%.
- Max pan: 5–10% of frame width
- All camera transitions use ease-in-out curves. No linear moves.
- **Never combine camera movement with character movement simultaneously.**
- Every camera move must resolve to a clean static composition within 3 seconds.

### 5. Content Zone Manager

Displays and rotates content cards in the content zone. Content types:

| Content Type | Usage |
|-------------|-------|
| Headlines & Article Screenshots | Primary source material. 30–40% of content zone time. |
| Social Media Posts & Replies | Public reaction, controversy, community sentiment. |
| Data Visualizations & Simple Graphics | Stats, growth/decline, comparisons. Keep simplified for mobile. |
| Logos, Product Images & Brand Imagery | When discussing a specific company or product. |
| Contextual Imagery | Stylized/thematic images as visual texture, not evidence. |
| Key Phrase Text Cards | Your own spoken words as styled branded text card. |
| Comparison Layouts | Split content zone into two panels (before/after, A vs B). |
| Quote Cards | Formatted quote with attribution, max 15–20 words. |

**Content zone scheduling rule:** Never empty for more than 5–7 seconds. Priority waterfall:
1. Direct evidence (article, screenshot, social post, data)
2. Contextual imagery (logo, product image, thematic visual)
3. Key phrase text card (pull key sentence from script)
4. Comparison or formatted layout
5. Centered character (last resort, only if under 30 seconds)

**Visual hierarchy:** At any moment, exactly one dominant visual element:

| Scene Type | Dominant Element | Supporting Elements |
|-----------|-----------------|-------------------|
| Headline Intro | The headline text | Character idle, source attribution small |
| Commentary | The character (speaking) | Content card dimmed or static |
| Evidence/Proof | The screenshot or image | Character scaled down or partially off-screen |
| Reaction | Character (animated reaction) | Trigger content shown briefly, then fades |
| Closing/CTA | Character centered or branded card | Subscribe button, social handles |

### 6. Background System

5 mood variants controlled via shader/particle parameter lerps:

| Variant | Tone | Use |
|---------|------|-----|
| Calm/Neutral | Cool blue/purple, slow drift | Default, exposition |
| Energetic | Warmer, faster particles, brighter | Analysis, arguments |
| Tense/Dramatic | Dark, red/orange accents, heavy motion | Escalation, serious |
| Playful/Light | Bright, varied movement | Humor, light topics |
| Minimal/Focus | Nearly static, very muted | Breathers, dense content |

**Rules:**
- Loop duration: 30–60 seconds minimum
- Motion should be perceptible but not trackable
- Always crossfade (TRANS-08) over 2–4 seconds, never hard-swap
- Background should never contain recognizable objects, text, or distracting patterns
- The viewer should never consciously notice the background changed

### 7. Transition System — Complete Reference

**Cut types:**

| ID | Type | Duration | Usage | Frequency |
|----|------|----------|-------|-----------|
| CUT-01 | Hard cut (instant) | 0 frames | Topic changes, punchlines, reveals, escalation, energy-up moments | 60% of all transitions |
| CUT-02 | Jump cut (content swaps, character stays) | 0 frames for content | Swapping evidence within same argument, social post sequences | 10–15%, clusters of 2–3 max |
| CUT-03 | L-cut/J-cut (audio leads visual by 0.5–1.5s) | Audio lead 0.5–1.5s, visual cut instant | Story transitions, commentary to evidence, pulling viewer forward | 10–15% |

**Smooth transitions:**

| ID | Type | Duration | Usage | Frequency |
|----|------|----------|-------|-----------|
| TRANS-01 | Crossfade/dissolve | 0.5–1.0s | Headline to breakdown, breather entrances, energy-down | 15–20% |
| TRANS-02 | Camera push-in | 2–3s, ease-in-out, 100%→110–115% | Entering reaction, emphasizing point, building intensity | 5–10% |
| TRANS-03 | Camera pull-back | 2–3s, ease-in-out, 115%→100% | Entering breather, after reaction, calmer segment | 5–8% |
| TRANS-04 | Lateral slide (content cards) | 0.5–1.0s, ease-out | Swapping content cards within same story | 10–15% |
| TRANS-05 | Character slide reposition | 0.8–1.5s, ease-in-out | Story transitions (L→R or R→L), always paired with topic change | 2–4 per video |
| TRANS-06 | Character center pull | 0.5–1.0s | Entering reaction, breather, or closing; content fades out | 3–5 per video |
| TRANS-07 | Character side return | 0.5–1.0s | Exiting centered scene back to content commentary; content fades in | Matches TRANS-06 |
| TRANS-08 | Background mood crossfade | 2–4s | Scene archetype changes; always synchronized with content transition | 3–6 per video |

**Transition decision matrix:**

| Situation | Primary Transition | Alternative |
|-----------|-------------------|-------------|
| New story / topic change | CUT-01 + TRANS-05 | CUT-03 (audio lead) |
| Same story, new evidence | CUT-02 or TRANS-04 | CUT-01 |
| Commentary to reaction | TRANS-02 (push-in) + TRANS-06 (center) | CUT-01 |
| Reaction back to commentary | TRANS-03 (pull-back) + TRANS-07 (side return) | CUT-01 |
| Escalation moment | CUT-01 (hard cut) | CUT-02 rapid sequence |
| Breather entrance | TRANS-01 (dissolve) + TRANS-03 (pull-back) | TRANS-06 (center) |
| Punchline / reveal | CUT-01 (instant) | None. Always hard cut. |
| Closing / CTA | TRANS-06 (center pull) + TRANS-01 (dissolve) | Slow TRANS-02 |
| Content card swap (same argument) | TRANS-04 (lateral slide) | CUT-02 |
| Background mood shift | TRANS-08 (always) | Never hard-swap background |

**Ratio: 60% hard cuts, 40% smooth transitions.**

### 8. Audio Sync System

Reads script markers and audio amplitude to trigger character animations and scene changes.

**Voice pacing targets:**

| Mode | WPM | Usage |
|------|-----|-------|
| Conversational (default) | 130–160 | 60–70% of video |
| Emphatic | 160–180 | 20–25% |
| Dramatic pause | 1–2s full stop | 5–10% |
| Rapid fire | 180–200 | Max 15–20s at a time |

**Background music:**
- Present 70–80% of video at 20–30% of voice level
- 2–3 tracks per video, crossfade on transitions
- Drop entirely for dramatic pauses or the most important 1–2 sentences

**Sound effects:**
- 4–8 per minute max, all under 1 second
- Sit under vocals, never compete
- Subtle whoosh on card swaps, pop on headlines, thud on reveals
- Tonally consistent with brand

**Re-engagement cues:**
- Every 60–90s: vocal pattern break (sudden question, brief laugh, energy change)
- Audible transition cue at each topic change
- Verbally number stories for passive listeners ("Story number two...", "Okay, next up...")

### 9. Script/Timeline Driver

Consumes a per-video `Script.txt` file with timestamped markers for all events. The `ScriptFileReader` component parses this file and feeds markers to the avatar system and media presentation system.

Marker types the system must support:
- Time markers (triggering character animation states)
- Media markers (triggering content zone displays with type, asset name, duration)
- Position markers (triggering character repositioning with Left/Right/Center)
- Zoom markers (triggering camera zoom In/Out/Reset)

---

## Video Structure Template

**Target length:** 8–12 minutes, 3-act structure.

| Segment | Time | Purpose |
|---------|------|---------|
| Cold Open | 0:00–0:30 | Hook with strongest story, no intro, no greeting |
| Story 1 (Lead) | 0:30–4:00 | Main story, deepest analysis |
| Transition Beat | 4:00–4:15 | Verbal bridge |
| Story 2 (Secondary) | 4:15–7:00 | Solid coverage, slightly faster |
| Transition Beat | 7:00–7:15 | Verbal bridge |
| Story 3 (Quick Hit) | 7:15–9:00 | Headline + quick take |
| Synthesis & Close | 9:00–10:30 | Connect themes, final take, CTA |

**Hook (first 30 seconds):**
- 0–2s: Cold open — single most provocative/surprising claim
- 2–5s: Visual punch — headline, image, or clip backing the claim
- 5–10s: Context bridge — one sentence on why this matters
- 10–15s: Tease structure — hint at what else the video covers
- 15–25s: Quick branded intro if any (3–5s max)
- 25–30s: Transition into first full story segment

**Layout flow per segment:**

- **Intro:** C-01 (hook) → L-01 (headline) → L-02/L-05 (context) → C-05 (branded intro, optional)
- **Story 1 (Character Left):** L-01 → L-02/L-03/L-04/L-05 rotation → L-09/L-03 (evidence) → C-02 (reaction) → C-04 (breather)
- **Story 2 (Character Right):** R-01 → R-02/R-03/R-04/R-05 rotation → R-09/R-03 (evidence) → C-02 (reaction) → C-04 (breather)
- **Story 3 (Character Left):** L-01 → L-06 (quick take) → L-03/L-05 (minimal breakdown)
- **Closing:** L-06/R-06 (synthesis) → C-02 (final take) → C-03 (branded closing)

---

## Pacing Rules — Hard Constraints

| Parameter | Value |
|-----------|-------|
| Minor visual reset | Every 8–15 seconds |
| Major visual reset | Every 45–90 seconds |
| Max static hold | Never the same frame for more than 20 seconds |
| Max sustained high-energy | 45–60 seconds before cooldown |
| Always follow escalation with a breather | Required |
| Max big animated reactions per video | 2–3 |
| Content zone max empty time | 5–7 seconds |
| Hard cut : smooth transition ratio | 60:40 |
| Max rapid visual resets in sequence | 3 (under 5s each) |
| Force return to idle after high animation | 30+ seconds |

---

## Scene Archetypes

| Archetype | Duration | Layout | Transition In |
|-----------|----------|--------|--------------|
| Headline | 5–15s | Large headline dominates content zone, character idle | Hard cut or quick slide-in |
| Breakdown | 30–90s | Character is visual focus, content rotates every 10–20s | Dissolve or camera push from headline |
| Reaction | 10–30s | Character slightly larger (zoom 110–115%), content may shrink/fade | Quick camera push-in |
| Escalation | 20–45s | New evidence every 5–8s, character more animated, pacing quickens | Hard cut |
| Breather | 5–15s | Simplified visual, character relaxed idle, minimal content | Slow dissolve or gentle pull-back |
| Closing/CTA | 15–30s | Character centered or branded pose, end card | Smooth transition from last segment |

---

## Thumbnail & Title

**Thumbnail:** Max 3 elements (character face/reaction, one image/logo, 2–4 words text). Test readability at 168×94px.

**Title:** 50–65 characters. Front-load subject. No dates. Curiosity gap, not clickbait.

**First frame must:** match thumbnail colors/pose, show topic immediately, be readable on mobile. Never open on blank screen, loading animation, or generic intro card.

---

## Key Constraints & Rules Summary

These constraints are absolute. Do not build systems that violate them:

- Character side swaps ONLY on story transitions, never mid-story
- Character centering max 30 seconds before returning to sided layout
- Never combine camera movement with character movement simultaneously
- 60:40 ratio of hard cuts to smooth transitions
- All text must have semi-opaque background plate or drop shadow
- Medium or bold font weights only (no thin fonts)
- Reserve large reactions for genuinely significant moments (2–3 per video)
- Every character reposition must have a narrative reason
- Content zone priority waterfall: direct evidence → contextual imagery → key phrase card → comparison layout → centered character (last resort)
- Max camera zoom 115–120%, max pan 5–10% of frame width
- All camera moves use ease-in-out curves, never linear
- Background always crossfades, never hard-swaps
- The character's default state is composed and attentive, not constantly reacting
- Audio energy rises and falls in waves, never sustained at peak
- SFX 4–8 per minute max, all under 1 second, always under vocals

---

## Analytics Targets (For Reference)

| Metric | Purpose |
|--------|---------|
| CTR | Thumbnail/title effectiveness |
| Average View Duration | Pacing quality |
| Retention Graph | Drop-off pattern identification |
| Returning Viewers % | Format habit-building (target 30–40%+) |
| Impressions | Algorithm promotion |

---

## Source Document

All specifications in this README are derived from `youtube_production_blueprint_v2.docx` which is the authoritative production blueprint. If anything in this README conflicts with the blueprint, the blueprint takes precedence.