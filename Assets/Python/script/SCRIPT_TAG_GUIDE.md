# AutoAvatarGen — Script Tag Reference & Authoring Guide

**Audience:** the AI (Claude / Opus 4.8) that writes `Script.txt`.
**Goal:** produce a script whose tags are formatted *exactly* the way the pipeline expects, so nothing breaks and every visual fires on time.

Paste this whole file to the model before asking it to write a script, or keep it as the system instruction for script generation.

> **Media tags (`Image`, `Video`, `BigImage`, `Logo`, `BigMedia`, `BRoll`) point at real files/assets by name.** The names are project-specific — they are **not** in this guide. They live in a companion file, `MEDIA_LIBRARY.md`, produced by `generate_media_library.py`. Paste that file in **alongside** this one and use only the names it lists. **If you were not given a `MEDIA_LIBRARY.md`, do not guess a media name** — a name that doesn't exist makes the card show nothing (see §4 *"Media & asset cards"*). Use a self-contained text card instead (`Headline`, `Excerpt`, `Quote`, `Stat`, `BigText` — these carry their own content and always work).

---

## 1. How the pipeline reads your script (read this first)

1. The script is split into **segments** at every `## SECTION` heading. Each segment is sent to ElevenLabs as a separate text-to-speech render, then stitched back together.
2. **Before** the text is spoken, every tag is *stripped out* by a regex. ElevenLabs only ever "reads" the plain narration — never the tags.
3. After the audio comes back, the processor finds where each tag sat in the text and bakes in a precise timestamp (`T=…`). At playback the visual fires when the narration reaches that point.

**The single most important consequence:** if a tag is even slightly malformed, the strip regex won't recognize it, the tag is **left in the text, and ElevenLabs reads it out loud as gibberish** — which also shoves every later timestamp seconds too late. Correct syntax is not cosmetic; it is what keeps the whole video in sync.

> You write tags **without** a `T=` value. The processor adds timing automatically. Never write `T=` yourself.

---

## 2. HARD RULES (break these and the video breaks)

1. **Start the file with a `## SECTION` heading.** Any text before the first heading is silently discarded.
2. **One tag per line, on its own line.** Don't put two tags on one line and don't bury a tag mid-sentence (emotion tags are one exception; the **transition line** is the other — a `{Transition:…}` deliberately gathers all of a section's tags onto one line, see §4).
3. **Use straight ASCII double quotes `"` only.** Never curly/smart quotes (`“ ”`). Smart quotes break card tags.
4. **Never put a `"` *inside* a quoted card field.** The field ends at the first `"`. If you need a quote inside quoted text, rephrase or use single quotes `'`. (Commas, periods, em‑dashes `—`, `%`, `$`, `€` inside quotes are fine.)
5. **Every content card, `Logo`, `BRoll`, `BigMedia`, `BigText`, and `BigImage` MUST carry a duration number** (`,5` = 5 seconds; decimals allowed, `,4.5`) right after its text/name fields. It's normally the **last** value — the only things that may follow it are the optional `,bigCenter` (Headline) or `,Left`/`,Right` (side cards) modifiers.
6. **Unquoted names (`Logo`, `BRoll`, `BigMedia`, `BigText`, `BigImage`) must not contain commas.** The comma is a delimiter. Use `+` to join multiple items (`BigImage` is a single image — no `+`).
7. **Spell fixed keywords exactly, capitalized as shown:** emotions, `Position`, `Left/Right/Center`, `Zoom`, `In/Out/Reset/Pullback/ExtremeIn/ExtremeOut`, `Cut`, `Smooth`, `bigCenter`, `Transition`, `Wipe/Shutter/Iris`, `Mood`, `Calm/Energetic/Tense/Playful/Minimal`. A mis-spelled or mis-capitalized tag is still **stripped** from the narration (a catch-all removes anything in `{…}`, so it is never read aloud), but it does **nothing** — the effect is silently skipped. Emotion names must match the avatar's emotion list exactly or the expression won't change.
8. **Side content cards only appear while the character is at `Left` or `Right`.** Moving to `Center` hides/suppresses side cards (`Headline`, `Excerpt`, `Quote`, `Stat`, `Logo`, `BRoll`) — they'd overlap the centered character. Put the character on a side before showing one (see §5). **Fullscreen feature cards (`BigText`, `BigMedia`, `BigCenter`, `BigImage`) are exempt** — they render in front of everything and appear in any position, including `Center`. (`BigImage` covers only the left 3/4, so it's meant to *share* the screen — stand the presenter on the **right** with `{Position:Right}`; see §4.)
9. Keep the narration itself natural — it can contain quotes, commas, anything. The rules above apply to **tags only**.
10. **Never put a space next to the commas or quotes that separate a tag's fields.** The strip regex is exact: `{Quote:"a","b","c",5}` works, but `{Quote:"a", "b", "c", 5}` (spaces after the commas) does **not** — the tag is left in the narration and read aloud. Spaces are only allowed **inside** a quoted value (`"VP of Comms, MegaCorp"`) or inside an unquoted name/description (`{BRoll:server room,4}`). Never around the `,` between fields, and never between `"` and `"`.
11. **Every quoted field must be non-empty, and the field count must be exact.** `Headline` = 2 quoted fields; `Excerpt`/`Quote`/`Stat` = 3. An empty field (`""`) or the wrong number of fields makes the card **silently not appear** (it's still stripped, so it's not read aloud — it just does nothing). If you don't have a value for a field, don't use that card; pick one that fits what you have.
12. **Media names must be real.** `Image` / `Video` / `BigImage` / `Logo` / `BigMedia` / `BRoll` reference an existing file or asset by name (from `MEDIA_LIBRARY.md`). An unknown name parses fine and is stripped, but the card **shows nothing** (or a bare text fallback). Never invent one. No `MEDIA_LIBRARY.md` in front of you → use a text card instead (§4 *"Media & asset cards"*).

---

## 3. Quick cheat sheet

| Tag | Syntax (what you write) | Duration required? |
|---|---|---|
| Emotion | any single-word name from the project's emotion list — `{Neutral}` `{Smirk}` `{Sip}` … | no |
| Character position | `{Position:Left}` (+ optional `,Cut` or `,Smooth`) | no |
| Camera zoom | `{Zoom:In}` (+ optional `,Cut` and/or `,D=seconds`) | no |
| Extreme close-up (punchline) | `{Zoom:ExtremeIn}` … `{Zoom:ExtremeOut}` — **always a pair** | no (you place the out tag) |
| Black cut | `{Black:3}` | yes |
| Scene transition | `{Transition:Wipe}` `{Transition:Iris,1.2}` (Wipe/Shutter/Iris, optional speed) | no |
| Background mood | `{Mood:Tense}` (Calm/Energetic/Tense/Playful/Minimal) | no |
| Image 📁 | `{Image:name}` or `{Image:name,4}` | optional (default 3s) |
| Video clip 📁 | `{Video:name}` — **no duration** | no (runs until the next beat) |
| Headline card ✅ | `{Headline:"headline text","Source",5}` (+ optional `,bigCenter` **or** `,Left`/`,Right`) | yes |
| Excerpt card ✅ | `{Excerpt:"full passage","phrase to highlight","Attribution",6}` (+ optional `,Left`/`,Right`) | yes |
| Quote card ✅ | `{Quote:"the quote","Person Name","Role / Title",5}` (+ optional `,Left`/`,Right`) | yes |
| Stat card ✅ | `{Stat:"2.3 billion","Label","Context line",5}` (+ optional `,Left`/`,Right`) | yes |
| Logo card 🎬 | `{Logo:Google,4}` (+ optional `,Left`/`,Right`) | yes |
| B‑roll card 🎬 | `{BRoll:description,4}` (+ optional `,Left`/`,Right`) | yes |
| Big media (fullscreen) 🎬 | `{BigMedia:Google,4}` or `{BigMedia:Google+Brave+X,4}` (≤4) | yes |
| Big text (fullscreen) ✅ | `{BigText:ONE LINE,3}` or `{BigText:LINE 1+LINE 2,4}` (≤4) | yes |
| Big image (article, left 3/4) 📁 | `{BigImage:name,5}` (pair with `{Position:Right}`) | yes |
| Stage direction ✅ | `[deadpan]` `[slowing down, serious]` | no |
| Chapter timestamp ✅ | `{Timestamp:"Cold Open"}` | no |
| Section heading ✅ | `## COLD OPEN` | n/a |

**Legend — does the tag need a real asset to exist?**
- ✅ **Self-contained — always works.** The tag carries its own text; nothing external to resolve. Safe to use anytime.
- 📁 **Needs a real file on disk.** The `name` is a filename (no extension) in a media folder — `Image`/`BigImage` → `Images/` or `Logos/`; `Video` → `BRoll/`. Use only names from `MEDIA_LIBRARY.md`.
- 🎬 **Needs a real entry in the Unity `ContentCardAssets` asset** (a *different* place from the disk folders). `Logo`/`BigMedia` → a configured company logo; `BRoll` → a configured clip description. Use only names confirmed in `MEDIA_LIBRARY.md` / Unity.

If you don't have a confirmed name for a 📁 or 🎬 tag, use a ✅ card instead — see §4 *"Media & asset cards — names must be real"*.

---

## 4. Every tag in detail

### Section headings — `## NAME`
Splits the script into separately-rendered, stitched segments. Each section gets a short natural pause after it. Use them for major beats (cold open, setup, breakdown, take, closer, etc.). **The file must begin with one.**
```
## COLD OPEN
```

### Chapter timestamp — `{Timestamp:"Label"}`
A **YouTube chapter marker**. It is **not spoken and not shown** — no audio, no on-screen visual, and it changes nothing about the character, camera, mood, or cards. It only records *"a chapter called Label starts here"* against the audio timeline, so the editor can copy a ready-made YouTube chapter list out of the **MugsTech ▸ Timestamps** window after a run.
- Put one on its **own line, directly under each `## SECTION` heading** (above the transition line, if the section has one), so each chapter begins where that section does.
- `Label` is free text in straight double quotes — spaces, commas, and punctuation are fine. Same quoting rule as any card field: **no `"` inside the label.**
- **No duration** — it's an instant marker, not a card.
- Give the **first** section a `{Timestamp:"..."}` at the very top so its chapter lands at **0:00** (YouTube only enables chapters when the first one is 0:00).
```
## COLD OPEN
{Timestamp:"Cold Open"}
{Position:Center,Cut} {Neutral}
[deadpan] A company did a thing. It was bad. Let's talk about it.

## BREAKDOWN
{Timestamp:"The Breakdown"}
{Transition:Wipe} {Position:Left} {Serious}
[slowing down, serious] Here's what the policy change actually says.
```

### Stage directions — `[ ... ]`
A delivery cue for the narration (pacing, tone). It is **not** spoken and produces no on‑screen visual; ElevenLabs just uses it to shape the read. Any characters except `]` are allowed (commas are fine).
```
[deadpan] A company did a thing. It was bad.
[slowing down, serious] Here's what the policy actually says.
```

### Emotions — `{Neutral}` `{Excited}` `{Serious}` `{Sad}` `{Concerned}`
Sets the avatar's facial expression. These five are the default set. Place on its own line just before the line it should color (may also appear inline, but own-line is preferred for clarity).

> **The emotion set may be customized.** When the project uses a custom line-up — via the avatar's *Use Emotion Array Override* toggle in the Unity Inspector, or emotions added in the in-app Visuals menu — the five names above no longer apply. In that case use **exactly** the list handed to you by the **"Copy Emotion Names for Claude"** button (on the HybridAvatarSystem component), and use no emotion name outside that list.

**Emotion names are open-ended — there is no list to update anywhere.** Every tool in the chain recognises an emotion by its *shape*: a single bare word in curly braces, no colon. `{Smirk}`, `{Sip}`, `{SmugSip}`, `{BothEyebrowRaised}` all work the moment a sprite of that name exists in the avatar's emotion array. Nothing else has to change — the TTS pre-processor strips the tag automatically (so ElevenLabs never reads it aloud) and stamps it with a `T=` timestamp so the expression lands on the exact word.

Optionally add a per-tag transition style — `Cut`, `Blink`, `BlinkHeavy`, `SquashStretch`, `Crossfade`, `Shake` — to override the global one for that swap:
```
{Sip}
Let me be precise, because the lazy version of this story is wrong.
{Shocked,Cut}
It deleted a man's photographs.
```
```
{Excited}
This is the part that actually matters.
```

### Character position — `{Position:Side}` `[,Cut|,Smooth]`
Moves the character. `Left` / `Right` / `Center`.
- `Left` = character on the left, faces right toward the content zone.
- `Right` = character on the right, faces left.
- `Center` = front and center, faces camera — **and pauses/hides content cards.**
- Optional 2nd word: `Cut` (instant snap) or `Smooth` (eased glide). Omit to use the scene default.
```
{Position:Left,Smooth}
{Position:Center,Cut}
```

### Camera zoom — `{Zoom:Type}` `[,Cut]` `[,D=seconds]`
`In` (push in for focus/intensity), `Out` (pull back), `Reset` (instant snap to default), `Pullback` (snap wide, drift wider, jump back), `ExtremeIn`/`ExtremeOut` (hard close-up on the punchline — see below).
- `,Cut` = snap instead of animating (ignored by `Reset`/`Pullback`/`ExtremeIn`/`ExtremeOut`).
- `,D=seconds` = for `In`, auto-reset after that long; for `Pullback`, the drift length.
```
{Zoom:In}
{Zoom:In,Cut,D=4}
{Zoom:Pullback,D=3}
{Zoom:Out}
```

### Extreme close-up — `{Zoom:ExtremeIn}` … `{Zoom:ExtremeOut}`
A hard punch-in to a **close-up on Mugs's face**, used to punctuate a punchline. Both edges are **jump cuts** — no push, no easing, no motion whatsoever. The frame appears, holds dead still, and is gone.

**These two tags are a pair. Every `{Zoom:ExtremeIn}` needs a `{Zoom:ExtremeOut}`.**

- **You control the length by where you put the out tag**, not with a number — exactly like `{Video:}` (§4). Put `ExtremeIn` before the line you want in close-up and `ExtremeOut` after it. Want the close-up over three sentences? Put the out tag after the third.
- **No duration value.** `,D=` is ignored on both tags; so is `,Cut` (they're always cuts).
- The camera **finds his face wherever he's standing** — `Left`, `Right` or `Center` all work, no position change needed.
- `ExtremeOut` restores **the exact framing that was on screen before the punch**, so a close-up inside a `{Zoom:In}` section drops back into that zoom rather than undoing it. You never need a `{Zoom:Reset}` after one.
- Silent by default — the missing sound is usually the joke.
- **Forget the out tag and the close-up stays up for the rest of the video.** This is the one way to really break the effect.

```
[deadpan] They shipped it on a Friday. To production. With no tests.
{Zoom:ExtremeIn}
[flat] On a Friday.
{Zoom:ExtremeOut}
[dry] Anyway.
```
Hold it across a few lines when the whole beat is the joke:
```
{Zoom:ExtremeIn}
[flat] No tests. No staging. No rollback plan.
[deadpan] Just vibes, and a production database.
{Zoom:ExtremeOut}
```
Place each tag on its own line, like any other tag (§5) — it fires as the narration reaches the next spoken word.

### Black cut — `{Black:seconds}`
Hard-cuts a **fullscreen** black plane in (covering the character and all cards), holds for `seconds`, then cuts out. No fade — pure jump cut. Great for dramatic beats / scene breaks. Duration required.
```
{Black:2}
```
> Authoring is unchanged — you still just write `{Black:seconds}`. (Implementation note: the black plane now renders above the character via a high-sorting-order sprite on the recorded camera, so the character no longer shows through it.)

### Scene transitions — `{Transition:Type}` `[,speed]`
A **whole-screen transition** that covers the screen, reconfigures the scene *behind* the cover, then reveals — so each one reads as a fresh section break. Three variants:

| Type | What it looks like | Total |
|---|---|---|
| `Wipe` | An orange panel, slightly skewed, sweeps left → right across the whole screen. | ~0.72s |
| `Shutter` | Two dark bars close in from the top and bottom, meet in the middle (a thin orange line on each inner edge), then retract. | ~0.68s |
| `Iris` | A dark circle grows from the center until it covers the frame, then shrinks back open on the new scene. | ~0.72s |

Optional second value = **speed scale** — `1.0` is normal, `1.2` is 20% slower, `0.8` is 20% faster:
```
{Transition:Wipe}
{Transition:Iris,1.2}
{Transition:Shutter,0.8}
```

**The key rule — put the whole scene change on the transition's own line.** Unlike every other tag, a transition *gathers the other tags sitting on its line* and applies them **at the hidden midpoint, the moment the screen is fully covered** — not when the narration reaches each one. So write the transition first, then on the **same line** put every tag for the new section: position, emotion, mood, a content card, an image/video, a zoom. The instant the screen reveals, the character is already in its new spot, the old card already swapped or cleared, the new image already up, and the camera already at its new zoom — nothing is ever seen sliding in or popping up.
```
{Transition:Wipe} {Position:Right} {Serious} {Mood:Tense}
Okay, here's the part that should actually worry them.
```
- **Position and zoom are snapped** under cover (no visible glide).
- **No content card on the line → the content zone is cleared** under cover (any headline/card on screen disappears). **A content card on the line → it replaces** whatever was showing.
- It does **not** pause narration — the audio keeps playing right over it.
- A transition fired while another is still playing is ignored, so two can't overlap.

> Place a `{Transition:…}` on the **first line of a new section** (just under the `## HEADING`). Want a *smooth* zoom that glides in over the new section instead of a snap? Put that `{Zoom:In}` on the **next** line, not the transition line — only tags on the transition's own line are applied under cover.

### Background mood — `{Mood:Variant}`
Crossfades the animated background to a new mood over ~3 seconds. Variants: `Calm`, `Energetic`, `Tense`, `Playful`, `Minimal`. Use it on a transition line (it starts crossfading at cover) or on its own line anywhere. No duration needed.
```
{Mood:Energetic}
{Transition:Iris} {Position:Center} {Mood:Calm}
```
> No-op if the scene has no background mood system wired up — safe to use either way.

### Media & asset cards — names must be real (read before using any 📁 or 🎬 tag)

Six tags don't carry their own content — they **look up a name** and display whatever file/asset that name points at. If the name doesn't resolve, the tag is still stripped from the narration (so it isn't read aloud) but **the card shows nothing** — a blank slot, or for `BigMedia` a plain-text fallback of the name. This silent failure is the #1 reason "a media card didn't work."

There are **two separate places** a name can resolve against, and they do **not** share names:

| Tag | Resolves against | `name` is… |
|---|---|---|
| `{Image:name}` 📁 | disk folders `Images/` then `Logos/` | an image **filename**, no extension (e.g. `privacy_headline`) |
| `{BigImage:name}` 📁 | disk folders `Images/` then `Logos/` | an image **filename**, no extension |
| `{Video:name}` 📁 | disk folder `BRoll/` | a video **filename**, no extension |
| `{Logo:name}` 🎬 | the Unity **`ContentCardAssets`** asset (logo list) | a configured **company name** (case-insensitive) |
| `{BigMedia:name}` 🎬 | the Unity **`ContentCardAssets`** asset (logo list), then `Resources/Media/` | a configured **company name** |
| `{BRoll:description}` 🎬 | the Unity **`ContentCardAssets`** asset (clip list) | a configured **clip description** (case-insensitive) |

Rules for every 📁 / 🎬 tag:
- **Use only names that appear in `MEDIA_LIBRARY.md`.** Type them **exactly**, matching case, **without the file extension**. Don't invent names and don't reuse placeholders like `ArticleTemp` / `VideoTemp`.
- **Note the two `BRoll`s are different tags.** `{Video:name}` plays a **file** from the `BRoll/` disk folder (silently, under the narration, until the next beat). `{BRoll:description,4}` is a **side card** that resolves a *description* through the Unity asset. They are not interchangeable.
- **No `MEDIA_LIBRARY.md`? Don't use these tags at all.** Reach for a self-contained ✅ card instead: a `Headline`, `Stat`, `Quote`, `Excerpt`, or `BigText` conveys the same beat with text you write inline, and it always renders. A script full of working text cards beats a script with blank media slots.

---

### Image — `{Image:name}` or `{Image:name,seconds}`  📁
Shows an image in the media area. `name` is the file name (**extension omitted**) found in the configured `Images/` then `Logos/` disk folders — see *"Media & asset cards"* above; use a real name from `MEDIA_LIBRARY.md`. Duration optional — defaults to **3s**.
```
{Image:privacy_headline,4}
```

### Video — `{Video:name}`  📁
Plays a video clip file from the `BRoll/` disk folder — `name` is the **filename without extension** (a real one from `MEDIA_LIBRARY.md`). **The clip is always silent and the narration keeps playing right over it** — treat it as b-roll under the voice, not as a break in the read. (Not to be confused with the `{BRoll:description}` **side card**, which resolves a description through the Unity asset — see *"Media & asset cards"*.)

**Don't give a video a duration — you can't know one.** You have no idea how many seconds the narration under a clip will take once it's spoken, so any number you write is a guess that cuts the b-roll off mid-sentence. Instead the clip **stays on screen, looping, until the next beat ends it**:

- the presenter **moves to a different position** (`{Position:Center}`, or the other side),
- a **content card** appears (`Stat`, `Quote`, `Headline`, `Excerpt`, `Logo`, `BRoll`, or a feature card),
- the **next `{Image:}` or `{Video:}`** fires,
- or the narration ends.

So you control how long b-roll is on screen by **where you put the tag that ends it**, not by a number. A number is still accepted (`{Video:name,6}`) but it only sets a *minimum* — it can no longer cut a clip short, and it matters only for a clip placed on the script's final words.

```
{Position:Right}
{Video:hardDriveSpinning}
He killed the process. Most of it was already gone. Twenty years of files, walking out the door while he watched.
{Position:Center}
[flat] That's the whole story.
```
Here the clip runs under all three narrated sentences and ends exactly when the presenter steps to center — no guessing required.
```
{Video:datacenter_broll,6}
```

### Content cards (side panel — character must be Left/Right)

**Choosing a side — optional `,Left` / `,Right`.** Every card in this group appears on the **left** of the screen by default. Add `,Left` or `,Right` as the **final** value (right after the duration) to choose which side it **sits on** (and slides in from); omit it to keep the default (`Left`). A `,Right` card mirrors the left layout over to the right side of the screen. This is independent of rule 8 — the character must still be at `Left`/`Right` for any side card to show at all, and the card's side does **not** move the character (place the presenter with `{Position:...}` so they don't overlap a same-side card). (For `Headline`, the side is an alternative to `,bigCenter` — a `bigCenter` headline is centered, so it takes no side.)

**Headline** — a news headline with its source. Add `,bigCenter` to promote it to a fullscreen centered card, **or** `,Left`/`,Right` to choose which side of the screen a normal side-panel headline sits on.
```
{Headline:"Tech Giant Quietly Changes Privacy Policy","The Verge",5}
{Headline:"Markets React Within the Hour","Bloomberg",5,Right}
{Headline:"40% User Growth After Privacy Backlash","Android Authority",5,bigCenter}
```
Fields: `"headline","source",duration[,bigCenter|,Left|,Right]`

**Excerpt** — a longer quoted passage with one phrase highlighted, plus attribution. Use for document / terms-of-service / report quotes. The 2nd field must be a phrase that appears in the 1st field.
```
{Excerpt:"Users hereby grant a perpetual, royalty-free license to use submitted content for any purpose, including training AI models.","training AI models","Official Terms of Service",6}
```
Fields: `"full passage","phrase to highlight","attribution",duration[,Left|,Right]`

**Quote** — something a *person* said.
```
{Quote:"We believe transparency is at the core of everything we do.","Sarah Mitchell","VP of Communications, MegaCorp",5}
```
Fields: `"quote","person name","role/title",duration[,Left|,Right]` (note the comma inside `"VP of Communications, MegaCorp"` is fine — it's inside quotes)

**Stat** — a big number with a label and context.
```
{Stat:"2.3 billion","Monthly Active Users","as of Q2 2025",5}
```
Fields: `"value","label","context",duration[,Left|,Right]`

**Logo** 🎬 — a single company logo. `name` must be a **company name configured in the Unity `ContentCardAssets` asset** (case-insensitive), *not* a filename — see *"Media & asset cards"*. Use a name from `MEDIA_LIBRARY.md`. No commas.
```
{Logo:Google,4}
{Logo:Brave,4,Right}
```

**B‑roll card** 🎬 — a background video clip resolved by **description**, matched against the Unity `ContentCardAssets` asset (case-insensitive), *not* the `BRoll/` disk folder. Use a description from `MEDIA_LIBRARY.md`. No commas in the description. (For a clip **file** from the `BRoll/` folder, use `{Video:...}` instead.)
```
{BRoll:server room,4}
{BRoll:trading floor,4,Left}
```

### Feature cards (fullscreen, in front of the character)

**BigMedia** 🎬 — 1 to 4 logos/images shown large and centered. Each `name` resolves through the Unity `ContentCardAssets` asset (then `Resources/Media/`) — same source as `{Logo:}`, *not* the disk `Images/`/`Logos/` folders — so use configured logo names from `MEDIA_LIBRARY.md`. Join multiple with `+` (≤4). A name that doesn't resolve is shown as plain text (its own letters), not an image.
```
{BigMedia:Google,4}
{BigMedia:Google+Brave+X,4}
```

**BigText** — 1 to 4 big centered text lines. Join lines with `+`. No commas (use `+`); each `+` starts a new stacked line.
```
{BigText:ANOTHER ONE,3}
{BigText:YOUR DATA+→+THEIR MODEL,6}
```

**BigImage** 📁 — a large website-article or headline **screenshot** that covers the **left 3/4** of the screen, leaving the right quarter open for the presenter. It fills that area edge-to-edge (cropped to fit, no distortion) and drops in from the top. `name` is an image **filename (no extension)** in the same disk `Images/`/`Logos/` folders `{Image:}` uses — use a real one from `MEDIA_LIBRARY.md` (drop your screenshot there first). It's a feature card (not suppressed at `Center`), but it's designed to **share** the screen: put the presenter on the **right** so they stand in the open quarter beside the article, not behind it.
```
{Position:Right}
{BigImage:tesla_q3_article,6}
```

---

## 5. Placement & timing

- A tag fires at the moment the narration reaches **the next spoken word after the tag**. So place a tag on its own line *immediately before* the sentence (or right after the sentence whose end you're reacting to).
- Typical pattern: the spoken line, then the visual that should accompany the *next* line:
```
[deadpan] A company did a thing. It was bad. Let's talk about it.
{Zoom:In}
[genuine disbelief] No — actually bad.
```
- **Side cards need a side position.** Set `{Position:Left,...}` or `{Position:Right,...}` *before* a `Headline`/`Excerpt`/`Quote`/`Stat`/`Logo`/`BRoll` tag. While the character is `Center`, side cards are suppressed (they'd overlap the centered character).
- **Pick the side (optional).** A side card sits on the **left** of the screen by default. Append `,Left` or `,Right` as the card's **last** value to choose which side it rests on and slides in from (e.g. `{Quote:"…","…","…",5,Right}`, `{Logo:Brave,4,Right}`) — `,Right` mirrors it to the right side. It's independent of where the character stands, so with the presenter on the `Left` you can drop a card on the `Right`. For `Headline`, it's an alternative to `,bigCenter`.
- **Fullscreen feature cards work anywhere.** `BigText`/`BigMedia`/`BigCenter` (and a `Headline` with `,bigCenter`) render in front of everything, so they appear in any position — including `Center`. Use them for the "front and center" moments.
- **`BigImage` is the feature card that wants a side.** It isn't suppressed at `Center`, but it covers only the left 3/4 — set `{Position:Right}` so the presenter stands in the open right quarter beside the article instead of hidden behind it.
- **Transitions open a new section.** Put `{Transition:…}` on the **first line of a section** and group that section's whole scene change onto the same line (position, emotion, mood, a card, an image, a zoom). They're applied under cover — see §4. This is the one place you deliberately stack several tags on a single line. A side card grouped on a transition line still needs a `Left`/`Right` position on that same line (rule 8).
- **A `{Video:}` has no duration — you end it by placing the next beat.** The clip loops under the narration until the presenter changes position, a content card appears, or another `{Image:}`/`{Video:}` fires. Want b-roll under three sentences? Put the tag before them and the `{Position:...}` or card after them (§4).
- **`{Zoom:ExtremeIn}` has no duration either — you end it with `{Zoom:ExtremeOut}`.** Same principle as `{Video:}`: put the in tag before the line you want in close-up and the out tag after the last line it covers. It works from any position (`Left`/`Right`/`Center`), needs no `{Zoom:Reset}` afterwards, and **must always be closed** — an unpaired `ExtremeIn` holds the close-up for the rest of the video (§4).
- **Don't place a tag on the script's very last word and expect it to fire late** — it's fine, the recording now holds until trailing tags (e.g. an end-card `{Logo:...,8}` or final `{Black:2}`) finish their full duration.
- Reasonable default durations: cards 5s, excerpts 6s, big text 3–4s, logos 3–4s, black cuts 2–3s.

---

## 6. Complete worked example

```
## COLD OPEN
{Timestamp:"Cold Open"}
{Position:Center,Cut} {Neutral}
[deadpan] A company did a thing. It was bad. Let's talk about it.
{Zoom:In}
[genuine disbelief] No — actually bad. Like, read it twice to make sure bad.

## BREAKDOWN
{Timestamp:"The Breakdown"}
{Transition:Wipe} {Position:Left} {Serious}
[slowing down, serious] Here's what the policy change actually says.
{Headline:"Tech Giant Quietly Changes Privacy Policy","The Verge",5}
[dry] Page eleven. Buried under the cookie banner.
{Excerpt:"Users hereby grant a perpetual, royalty-free license to use submitted content for any purpose, including training AI models.","any purpose","Official Terms of Service",6}
[genuine disbelief] Any purpose. They wrote that and published it.
{Stat:"0.3%","Users Who Read Full ToS","Stanford study, 2023",5}
[dry] Point three percent. The rest of us just hit "I Agree."

## TAKE
{Timestamp:"The Take"}
{Transition:Iris} {Position:Center} {Concerned} {Zoom:In} {Mood:Tense}
[tired but amused] Every few months a company quietly rewrites the rules.
{BigText:YOUR DATA+→+THEIR MODEL,6}
[slowing down, serious] That's the real transaction here.
{Zoom:Reset}
[dry] You're welcome.
```

Notes on the example:
- Starts with `## COLD OPEN` (rule 1).
- `BREAKDOWN` and `TAKE` each open with a `{Transition:…}` on the section's first line, with that section's other tags on the same line — so the `Wipe` snaps the character to `Left` + sets `Serious` under cover, and the `Iris` snaps to `Center` + `Concerned` + zoom-in + a `Tense` mood crossfade under cover. Each section reveals already reconfigured (§4).
- The `Headline`, `Excerpt`, and `Stat` cards all appear in the `BREAKDOWN` section while the character is at `Left` (rule 8) — they're on their own later lines, so they animate in a beat after the transition rather than under cover.
- The narration `…hit "I Agree."` uses quotes freely — that's fine, it's narration, not a tag field.
- `{BigText:YOUR DATA+→+THEIR MODEL,6}` is three stacked lines via `+`.
- Each section opens with a `{Timestamp:"..."}` on its own line so the run produces a clean YouTube chapter list; the cold open's sits at the very top so its chapter is `0:00`.
- No tag has a `T=`; the processor adds those.

---

## 7. Final self-check before returning a script

- [ ] File begins with `## SECTION`.
- [ ] Every `{...}` card / Logo / BRoll / BigMedia / BigText / BigImage ends in `,number`.
- [ ] **No `{Video:}` carries a duration** — each one is ended by the next `{Position:...}`, content card, or media tag you placed after it.
- [ ] **Every `{Zoom:ExtremeIn}` has a matching `{Zoom:ExtremeOut}` later in the script** — count them, the totals must be equal. Neither tag carries a duration.
- [ ] **Every 📁/🎬 media name (`Image` / `Video` / `BigImage` / `Logo` / `BigMedia` / `BRoll`) is a real name copied from `MEDIA_LIBRARY.md`** — exact spelling and case, no extension. Any beat without a confirmed name uses a ✅ text card (`Headline`/`Stat`/`Quote`/`Excerpt`/`BigText`) instead. No invented or placeholder names.
- [ ] **No space next to a field-separating comma or between `"` and `"`** — `"a","b","c",5`, never `"a", "b", "c", 5`.
- [ ] **No empty `""` fields; field counts exact** — `Headline` has 2 quoted fields, `Excerpt`/`Quote`/`Stat` have 3.
- [ ] No smart quotes anywhere; no `"` inside a quoted field.
- [ ] No commas inside `Logo` / `BRoll` / `BigMedia` / `BigText` / `BigImage` names (used `+` for multiples, ≤4; `BigImage` is a single image).
- [ ] Emotion / Position / Zoom keywords spelled exactly and capitalized.
- [ ] `Transition` / `Mood` (and their variants) spelled exactly and capitalized.
- [ ] Each `{Transition:…}` is on the first line of its section, with that section's other tags grouped onto the same line.
- [ ] Every content card is preceded by a `Left` or `Right` position.
- [ ] Any `,Left`/`,Right` entry-side modifier is the **last** value in the tag, spelled exactly (capitalized), and not combined with `,bigCenter`.
- [ ] No `T=` written by hand.
- [ ] One tag per line.
- [ ] Each `## SECTION` that should be a YouTube chapter has a `{Timestamp:"..."}` on its first line, and the first section's is at the very top (so its chapter is `0:00`).
