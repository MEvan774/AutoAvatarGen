# AutoAvatarGen — Script Tag Reference & Authoring Guide

**Audience:** the AI (Claude / Opus 5) that writes `Script.txt`.
**Goal:** produce a script whose tags are formatted *exactly* the way the pipeline expects, so nothing breaks and every visual fires on time.

Paste this whole file to the model before asking it to write a script, or keep it as the system instruction for script generation.

> **Media tags (`Image`, `Video`, `BigImage`, `Logo`, `BigMedia`, `BRoll`) point at real files/assets by name.** The names are project-specific — they are **not** in this guide. They live in a companion file, `MEDIA_LIBRARY.md`, produced by `generate_media_library.py`. Paste that file in **alongside** this one and use only the names it lists. **If you were not given a `MEDIA_LIBRARY.md`, do not guess a media name** — a name that doesn't exist makes the card show nothing (see §4 *"Media & asset cards"*). Use a self-contained text card instead (`Headline`, `Excerpt`, `Quote`, `Stat`, `BigText` — these carry their own content and always work).

---

## 1. How the pipeline reads your script (read this first)

1. The script is split into **sections** at every `## SECTION` heading. The voice is rendered by ElevenLabs in **two long takes** (so it stays one consistent voice from start to finish), then cut back into one piece per section, and the pieces are stitched together with a short gap at each heading.
2. **Before** the text is spoken, every tag is *stripped out* by a regex. ElevenLabs only ever "reads" the plain narration — never the tags. (`[stage directions]` are the one thing sent along with it, as delivery cues — see §4.)
3. After the audio comes back, the processor finds where each tag sat in the text and bakes in a precise timestamp (`T=…`). At playback the visual fires when the narration reaches that point.

**The single most important consequence:** if a tag is even slightly malformed, the strip regex won't recognize it, the tag is **left in the text, and ElevenLabs reads it out loud as gibberish** — which also shoves every later timestamp seconds too late. Correct syntax is not cosmetic; it is what keeps the whole video in sync.

> You write tags **without** a `T=` value. The processor adds timing automatically. Never write `T=` yourself.

---

## 2. HARD RULES (break these and the video breaks)

1. **Start the file with a `## SECTION` heading.** Any text before the first heading is silently discarded.
2. **One tag per line, on its own line.** Don't put two tags on one line and don't bury a tag mid-sentence (emotion tags are one exception; the **transition line** is another — a `{Transition:…}` deliberately gathers all of a section's tags onto one line; and **duration-less `{BigText:LINE}`/`{BigText:End}` tags** sit mid-sentence on purpose, on the exact word their line belongs to — see §4).
3. **Use straight ASCII double quotes `"` only.** Never curly/smart quotes (`“ ”`). Smart quotes break card tags.
4. **Never put a `"` *inside* a quoted card field.** The field ends at the first `"`. If you need a quote inside quoted text, rephrase or use single quotes `'`. (Commas, periods, em‑dashes `—`, `%`, `$`, `€` inside quotes are fine.)
5. **Every content card (`Headline`, `Excerpt`, `Quote`, `Stat`, `Compare`, `Document`, `Map`), `Logo`, `BRoll`, `BigMedia`, `BigText`, `BigImage` and `Versus` MUST carry a duration number** (`,5` = 5 seconds; decimals allowed, `,4.5`) right after its text/name fields. It's normally the **last** value — the only things that may follow it are the optional `,bigCenter` (Headline), `,Win`/`,Loss`/`,NoArrow` (Stat's arrow — see §4 *"Stat"*), `,Allegation`/`,Filing`/`,Ruling`/`,Report` (Document's stamp), `,Vs` (Versus) or `,Left`/`,Right` (side cards and the `{Image:}`/`{Video:}` media tags) modifiers. **Three exceptions:** `BigText` may drop the duration to enter its line-by-line mode (`{BigText:LINE}` … `{BigText:End}`, see §4) — then the closing `{BigText:End}` is mandatory instead. `Timeline` **never** takes a duration — its nodes are always line-by-line, closed by a mandatory `{Timeline:End}`. And every **side card** (`Headline`/`Excerpt`/`Quote`/`Stat`/`Compare`/`Document`/`Map`/`Logo`/`BRoll`), the **`,bigCenter` headline**, `Versus`, and `{Image:}` may write the keyword **`Start`** in the duration slot to open a **held** card/image, closed with a matching `{Tag:End}` later (`{Headline:End}` for a big headline) — see §4 *"Holding a card"*; then that End tag is mandatory instead.
6. **Unquoted names (`Logo`, `BRoll`, `BigMedia`, `BigText`, `BigImage`) must not contain commas.** The comma is a delimiter. Use `+` to join multiple items (`BigImage` is a single image — no `+`).
7. **Spell fixed keywords exactly, capitalized as shown:** emotions, `Position`, `Left/Right/Center`, `Zoom`, `In/Out/Reset/Pullback/ExtremeIn/ExtremeOut`, `Cut`, `Smooth`, `bigCenter`, `Win/Loss/NoArrow` (Stat arrow), `Allegation/Filing/Ruling/Report` (Document stamp), `Vs` (Versus), `Start/End` (held pairs), `Transition`, `Wipe/Shutter/Iris`, `Mood`, `Calm/Energetic/Tense/Playful/Minimal`, and the Map regions `World/Europe/NorthAmerica/SouthAmerica/Africa/MiddleEast/Asia/Oceania`. A mis-spelled or mis-capitalized tag is still **stripped** from the narration (a catch-all removes anything in `{…}`, so it is never read aloud), but it does **nothing** — the effect is silently skipped. Emotion names must match the avatar's emotion list exactly or the expression won't change.
8. **Side content cards only appear while the character is at `Left` or `Right`.** Moving to `Center` hides/suppresses side cards (`Headline`, `Excerpt`, `Quote`, `Stat`, `Compare`, `Document`, `Timeline`, `Map`, `Logo`, `BRoll`) — they'd overlap the centered character. Put the character on a side before showing one (see §5). **Fullscreen feature cards (`BigText`, `BigMedia`, `BigCenter`, `BigImage`, `Versus`) are exempt** — they render in front of everything and appear in any position, including `Center`. (`BigImage` covers only the left 3/4, so it's meant to *share* the screen — stand the presenter on the **right** with `{Position:Right}`; see §4.)
9. Keep the narration itself natural — it can contain quotes, commas, anything. The rules above apply to **tags only**.
10. **Never put a space next to the commas or quotes that separate a tag's fields.** The strip regex is exact: `{Quote:"a","b","c",5}` works, but `{Quote:"a", "b", "c", 5}` (spaces after the commas) does **not** — the tag is left in the narration and read aloud. Spaces are only allowed **inside** a quoted value (`"VP of Comms, MegaCorp"`) or inside an unquoted name/description (`{BRoll:server room,4}`). Never around the `,` between fields, and never between `"` and `"`.
11. **Every quoted field must be non-empty, and the field count must be exact.** `Headline`/`Timeline` = 2 quoted fields; `Excerpt`/`Quote`/`Stat`/`Document` = 3; `Versus`/`Map` = 4; `Compare` = 5. An empty field (`""`) or the wrong number of fields makes the card **silently not appear** (it's still stripped, so it's not read aloud — it just does nothing). If you don't have a value for a field, don't use that card; pick one that fits what you have.
12. **Media names must be real.** `Image` / `Video` / `BigImage` / `Logo` / `BigMedia` / `BRoll` reference an existing file or asset by name (from `MEDIA_LIBRARY.md`). An unknown name parses fine and is stripped, but the card **shows nothing** (or a bare text fallback). Never invent one. No `MEDIA_LIBRARY.md` in front of you → use a text card instead (§4 *"Media & asset cards"*). (One exception: `{Video:End}` is not a name — it's the keyword that closes a running clip, see §4 *"Video"*.)

---

## 3. Quick cheat sheet

| Tag | Syntax (what you write) | Duration required? |
|---|---|---|
| Emotion | any single-word name from the project's emotion list — `{Neutral}` `{Smirk}` `{Sip}` … | no |
| Character position | `{Position:Left}` (+ optional `,Cut` or `,Smooth`) | no |
| Camera zoom | `{Zoom:In}` (+ optional `,Cut` and/or `,D=seconds`) — stacks up to 3 steps; `{Zoom:Out}` releases the run | no |
| Extreme close-up (punchline) | `{Zoom:ExtremeIn}` … `{Zoom:ExtremeOut}` — **always a pair** | no (you place the out tag) |
| Black cut | timed: `{Black:3}` — or held: `{Black:Start}` … `{Black:End}` | timed: yes / held: no (you place the End tag) |
| Scene transition | `{Transition:Wipe}` `{Transition:Iris,1.2}` (Wipe/Shutter/Iris, optional speed) | no |
| Background mood | `{Mood:Tense}` (Calm/Energetic/Tense/Playful/Minimal) | no |
| Image 📁 | `{Image:name}` or `{Image:name,4}` (+ optional `,Left`/`,Right`) — or held: `{Image:name,Start}` … `{Image:End}` | optional (default 3s) — held: you place the End tag |
| Video clip 📁 | `{Video:name}` … `{Video:End}` — **no duration, close it with the end tag** (+ optional `,Left`/`,Right` on the opening tag) | no (you place the end tag) |
| Headline card ✅ | `{Headline:"headline text","Source",5}` (+ optional `,bigCenter` **or** `,Left`/`,Right`) | yes — or `Start` … `{Headline:End}` (also with `,bigCenter`) |
| Excerpt card ✅ | `{Excerpt:"full passage","phrase to highlight","Attribution",6}` (+ optional `,Left`/`,Right`) | yes — or `Start` … `{Excerpt:End}` |
| Quote card ✅ | `{Quote:"the quote","Person Name","Role / Title",5}` (+ optional `,Left`/`,Right`) | yes — or `Start` … `{Quote:End}` |
| Stat card ✅ | `{Stat:"2.3 billion","Label","Context line",5}` (+ optional arrow `,Win`/`,Loss`/`,NoArrow`, then optional `,Left`/`,Right`) | yes — or `Start` … `{Stat:End}` |
| Compare card ✅ | `{Compare:"$6B+","Binance","$1.5B","Bybit","Source",5}` — value A, label A, value B, label B, source (+ optional `,Left`/`,Right`) | yes — or `Start` … `{Compare:End}` |
| Document card ✅ | `{Document:"passage","phrase to highlight","Source",6}` (+ optional stamp `,Allegation`/`,Filing`/`,Ruling`/`,Report`, then optional `,Left`/`,Right`) | yes — or `Start` … `{Document:End}` |
| Timeline card ✅ | `{Timeline:"Mar. 3","what happened"}` — one node per tag (side only on the first), ≤5, closed by `{Timeline:End}` | **no** — never a duration; always close with `{Timeline:End}` |
| Map card ✅ | `{Map:"Europe","BE+LU+NL","Caption","Source",5}` — region, ISO country codes joined with `+`, caption, source (+ optional `,Left`/`,Right`) | yes — or `Start` … `{Map:End}` |
| Logo card 🎬 | `{Logo:Google,4}` (+ optional `,Left`/`,Right`) | yes — or `Start` … `{Logo:End}` |
| B‑roll card 🎬 | `{BRoll:description,4}` (+ optional `,Left`/`,Right`) | yes — or `Start` … `{BRoll:End}` |
| Big media (fullscreen) 🎬 | `{BigMedia:Google,4}` or `{BigMedia:Google+Brave+X,4}` (≤4) | yes |
| Big text (fullscreen) ✅ | timed: `{BigText:ONE LINE,3}` / `{BigText:LINE 1+LINE 2,4}` (≤4) — or line-by-line: `{BigText:LINE}` … `{BigText:End}` (no durations) | timed: yes / line-by-line: no (you place the End tag) |
| Big image (article, left 3/4) 📁 | `{BigImage:name,5}` (pair with `{Position:Right}`) | yes |
| Versus (fullscreen) ✅ | `{Versus:"They say","the claim","The record says","the evidence",5}` (+ optional `,Vs`) | yes — or `Start` … `{Versus:End}` |
| Stage direction ✅ | `[deadpan]` `[slowing down, serious]` | no |
| Pause ✅ | `{Pause:0.8}` — seconds, 0.1–3.0, on its own line | no (the value IS the length) |
| Chapter timestamp ✅ | `{Timestamp:"Cold Open"}` | no |
| Thumbnail echo ✅ | `{Echo:"NO KEY LEAKED?","LEAKED",3}` — the thumbnail's HEADLINE and accent word, **once**, on its own line under the cold open's opening tags | yes (seconds it holds) |
| Section heading ✅ | `## COLD OPEN` | n/a |

**Held side cards, big headlines & images:** every side card (`Headline`/`Excerpt`/`Quote`/`Stat`/`Compare`/`Document`/`Map`/`Logo`/`BRoll`), the `,bigCenter` headline, `Versus`, and `{Image:}` may write **`Start`** in place of its duration number; the card then stays up until its matching `{Tag:End}` — e.g. `{Quote:"…","…","…",Start}` … `{Quote:End}`, or `{Headline:"…","…",Start,bigCenter}` … `{Headline:End}`. Same in-tag/out-tag principle as `{Black:Start}`…`{Black:End}` and `{Video:name}`…`{Video:End}` — see §4 *"Holding a card"*.

**Legend — does the tag need a real asset to exist?**
- ✅ **Self-contained — always works.** The tag carries its own text; nothing external to resolve. Safe to use anytime.
- 📁 **Needs a real file on disk.** The `name` is a filename (no extension) in a media folder — `Image`/`BigImage` → any image folder under the media root (`Images/`, `CEO/`, `TwitterPosts/`, `Flags/`, `Logos/`); `Video` → `BRoll/`. The folder is never part of the name — just the file name. Use only names from `MEDIA_LIBRARY.md`.
- 🎬 **Needs a real entry in the Unity `ContentCardAssets` asset** (a *different* place from the disk folders). `Logo`/`BigMedia` → a configured company logo; `BRoll` → a configured clip description. Use only names confirmed in `MEDIA_LIBRARY.md` / Unity.

If you don't have a confirmed name for a 📁 or 🎬 tag, use a ✅ card instead — see §4 *"Media & asset cards — names must be real"*.

---

## 4. Every tag in detail

### Section headings — `## NAME`
Splits the script into sections — each one cut from the voice takes as its own piece and stitched back in order. Each section gets a short natural pause after it — and a longer one, sized to the transition, when the next section opens with a `{Transition:…}` (see below). Use them for major beats (cold open, setup, breakdown, take, closer, etc.). **The file must begin with one.**
```
## COLD OPEN
```

### Chapter timestamp — `{Timestamp:"Label"}`
A **YouTube chapter marker**. It is **not spoken**, and it changes nothing about the character, camera, mood, or cards. It records *"a chapter called Label starts here"* against the audio timeline, so the editor can copy a ready-made YouTube chapter list out of the **MugsTech ▸ Timestamps** window after a run.

**From the second chapter on, the label is also shown on screen** as a small chapter title — in capitals, with a row of dots for "part 3 of 5" — in the strip above the cards, on the side away from the character, for about 2.5 s. It appears just after the section's opening `{Transition}` has revealed (or on the Timestamp's word when the section has none). The **first** chapter (the cold open) never shows a title. A `{Black}`, a close-up, a transition or a fullscreen card hides it. (Main menu *Show chapter titles*, on by default; off = never shown, the chapter list is unchanged.)
- Put one on its **own line, directly under each `## SECTION` heading** (above the transition line, if the section has one), so each chapter begins where that section does.
- `Label` is free text in straight double quotes — spaces, commas, and punctuation are fine. Same quoting rule as any card field: **no `"` inside the label.** Since it's shown on screen, keep it **short** — two to four words (`"The Breakdown"`, `"Who Signed?"`); a long one is shrunk, and past that cut short on screen (the YouTube chapter list keeps the full label).
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

### Thumbnail echo — `{Echo:"HEADLINE","accent word",seconds}`
The **thumbnail's headline, on screen for the first seconds** — so the viewer sees the exact words they clicked while the character says the first line. It slides down into the strip above the cards from the video's very first frame, holds, and slides back up. It is **not** an intro: no sound, no dim, no pause in the voice.
- **Once per script**, on its **own line right after the cold open's opening tags, before the first spoken line.** A second `{Echo}` is ignored.
- `HEADLINE` is the thumbnail's headline **exactly** (it's shown in capitals; `?` and `!` are kept). `accent word` is the thumbnail's accent word — shown in the thumbnail's red, punctuation and all. Copy both from `thumbnail.md` so they can't drift apart.
- At most **5 words**. A longer headline, or an accent word that isn't one of the headline's words, still shows — but without the red accent (and the log says why).
- `seconds` is how long it holds once it has slid in (typical: `3`).
- A side card arriving under it is fine. A full-screen moment (a `,bigCenter` headline, `BigText`, any feature card, a `{Black}`, a transition or the close-up) ends it early.
```
## COLD OPEN
{Timestamp:"Cold Open"}
{Position:Center,Cut} {Mood:Minimal} {Neutral}
{Echo:"NO KEY LEAKED?","LEAKED",3}
[deadpan] More than six billion dollars, according to the FBI.
```

### Stage directions — `[ ... ]`
A delivery cue for the narration (pacing, tone). It is **not** spoken and produces no on‑screen visual; ElevenLabs just uses it to shape the read. Any characters except `]` are allowed (commas are fine).
```
[deadpan] A company did a thing. It was bad.
[slowing down, serious] Here's what the policy actually says.
```
These are **eleven_v3 audio tags** — they are sent to the model verbatim and consumed rather than voiced, which is why they read like directions to a performer (`[whispers]`, `[sighs]`, `[laughing]`) rather than like notes to an editor. Plain descriptive moods work well; the further a cue drifts from something a voice can *do*, the likelier v3 is to read it aloud instead. If a take ever speaks one, turn **Send [stage directions]** off in *MugsTech ▸ TTS ▸ Chunked Pipeline* (or `Chunked.Chunking.KeepStageDirections`) and they go back to being stripped before the request — everything else, including their `T=` timestamps, is unaffected.

### Pause — `{Pause:seconds}`
A **beat you can count on**: the gap in the voice at this point is **at least** that many seconds. eleven_v3 has no dependable pause control — a `[pause]`, an ellipsis or a dash is a suggestion it takes or ignores per take — so this tag is never sent to the voice. When the take is assembled, the engine measures the silence v3 actually left there and adds **only the difference**: a take that already paused long enough is left alone (at least N, never N on top). Nothing is re-rendered and no credits are spent, so adding, moving or changing a `{Pause}` is a **tag-only edit** (*Update Tags Only*).
```
Freezing only works on coins a company controls.
{Excerpt:End}
{Smirk}
{Pause:0.8}
[tired but clearly entertained] Somewhere, I like to think, a hacker is on hold with stablecoin customer support.
```
- **Own line, directly above the line that should land after the beat.** It marks the gap between the word before it and the word after it.
- **It splits the tags around it.** Tags written **above** the pause fire as the silence begins — right after the setup's last word. Tags written **below** it fire as the silence ends, on the punchline's first word. This holds even when no silence had to be added. Above, the card closes and the smirk lands the moment the setup ends; he holds it through the beat, then speaks.
- **The extreme close-up:** put the pause **below** the cut tags, so the cut fires, his face holds in silence, then the line comes:
  ```
  You can see where this is going.
  {Excerpt:End}
  {Position:Center,Cut}
  {SmugLookingAtViewer,Cut}
  {Zoom:ExtremeIn}
  {Pause:0.7}
  [flat] Yes. One laptop.
  ```
  The same `{Pause:0.7}` placed **above** the three cut tags gives the silence on the wide frame first; the cut then lands with the line.
- **How long:** 0.5–0.8 s before a punchline; 1.0–1.5 s for a question hang or a number drop; 2.0 s only for the single biggest beat of the video. Seconds only (`0.8`, no `s`), from 0.1 to 3.0 — a value outside that is clamped and logged.
- **It can only widen a silence that is there.** If the voice ran the two sentences together (no silence at all near the tag), nothing is inserted and the render log says so — the engine never cuts into speech. A pause placed mid-sentence by mistake lands on whatever silence is nearest, or on none. Either way, move it to a real sentence break, or regenerate that section. The log also flags a pause it had to look further afield for: listen to that beat.
- **At a section edge** — after a section's last word, or before its first — it widens the gap between the two sections instead. Below an opening `{Transition}` the extra hold plays on the revealed scene, before the first word; anywhere else on the boundary it comes before the transition. Before the video's very first word or after its last it does nothing.
- **One per gap.** Two pauses between the same two words count as one (the longer wins).

### Emotions — `{Neutral}` `{Excited}` `{Serious}` `{Sad}` `{Concerned}`
Sets the avatar's facial expression. These five are the default set. Place on its own line just before the line it should color (may also appear inline, but own-line is preferred for clarity).

> **The emotion set may be customized.** When the project uses a custom line-up — via the avatar's *Use Emotion Array Override* toggle in the Unity Inspector, or emotions added in the in-app Visuals menu — the five names above no longer apply. In that case use **exactly** the list handed to you by the **"Copy Emotion Names for Claude"** button (on the HybridAvatarSystem component), and use no emotion name outside that list.

**Emotion names are open-ended — there is no list to update anywhere.** Every tool in the chain recognises an emotion by its *shape*: a single bare word in curly braces, no colon. `{Smirk}`, `{Sip}`, `{SmugSip}`, `{BothEyebrowRaised}` all work the moment a sprite of that name exists in the avatar's emotion array. Nothing else has to change — the TTS pre-processor strips the tag automatically (so ElevenLabs never reads it aloud) and stamps it with a `T=` timestamp so the expression lands on the exact word.

Optionally add a per-tag transition style — `Cut`, `Blink`, `BlinkHeavy`, `SquashStretch`, `Crossfade`, `Shake`, `Grow`, `Shrink` — to override the global one for that swap (`Grow` swells the presenter up and back to its original size over 0.9s, swapping at the peak; `Shrink` swaps instantly, then dips the presenter ~7% smaller and springs back over 0.2s):
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
- **A glide takes about 1 second, and whatever it introduces waits for it.** That means `Smooth`, or no 2nd word (the scene default is a glide). A side card (`Headline`, `Excerpt`, `Quote`, `Stat`, `Compare`, `Document`, `Timeline`, `Map`, `Logo`, `BRoll`), `{Image:}` or `{Video:}` that comes due during the glide is held until the character lands, so it appears about a second after its word (never more than about 1.25 s). Keep writing the `{Position:…,Smooth}` directly above the tag it introduces. A `Cut`, or a `{Position:}` that repeats the current side, holds nothing.
- **Fullscreen cards (`BigText`, `BigMedia`, `BigImage`, `Versus`, `,bigCenter` headlines) and `{…:End}` tags don't wait for a glide** — they can land while the character is still moving. The one exception: if a held side card is ahead of them in the script, they wait behind it, so the order never changes.
- **Don't close a beat within that second.** A card opened with `,Start` whose `{…:End}` is already due when the character lands is skipped, End included. The same goes for an `{Image:name,Start}` or `{Video:}` whose End or next media is already due, and the render report then lists it as "never started". Leave more than a second of narration after a glide before closing what it introduced.
```
{Position:Left,Smooth}
{Position:Center,Cut}
```

### Camera zoom — `{Zoom:Type}` `[,Cut]` `[,D=seconds]`
`In` (push in for focus/intensity — **stacks**, see below), `Out` (pull all the way back), `Reset` (instant snap to default), `Pullback` (snap wide, drift wider, jump back), `ExtremeIn`/`ExtremeOut` (hard close-up on the punchline — see below).
- `In` and `Out` are **slow eased glides (~0.7 s, ease-in-out)**, not snaps — the camera settles about a second after the word the tag sits on. Add `,Cut` when you want the snap.
- **`{Zoom:In}` stacks, up to three steps.** Each `{Zoom:In}` leans in one step further than the last — **112% → 124% → 136%** by default — gliding on from wherever the camera is. Consecutive `In` tags build a slow push across a few lines. A **fourth** `{Zoom:In}` before the run ends is ignored (logged).
- **`{Zoom:Out}` releases the whole run** — one glide from any step straight back to normal framing, never one step at a time. `{Zoom:Reset}` does the same as an instant snap. Either way the next `{Zoom:In}` starts again at step 1.
- `,Cut` = snap instead of animating — on an `In` it snaps to the next step (ignored by `Reset`/`Pullback`/`ExtremeIn`/`ExtremeOut`).
- `,D=seconds` on an `In` = **release the whole run** that long after the tag, as one glide back to normal (even if the steps were `,Cut`). Every later `{Zoom:In}` in the run restarts that countdown from its own tag; a later `,D=` replaces the length. A `{Zoom:Out}` or anything else that ends the run cancels it. For `Pullback`, `,D=` is the drift length instead.
- **What else ends a run** — no `{Zoom:Out}` needed before any of these; the next `{Zoom:In}` after them starts at step 1:
  - **Things that cover the whole frame** — the camera **snaps** back to normal hidden behind them, so nothing is seen: `{Black:N}` and `{Black:Start}`; a `{Transition:…}` (at its covered midpoint, *before* any zoom on the transition line is applied — so `{Transition:Wipe} {Zoom:In}` opens the new section at step 1, under cover); a big headline (`,bigCenter`, timed or held — the snap lands once its panel has slid fully up).
  - **Things that leave the scene visible** — the camera **glides** back (~0.7 s) as they appear: `{BigText}` (a timed one, or the **first** line of a line-by-line stack), `{BigMedia}`, `{BigImage}`.
  - `{Zoom:ExtremeIn}` (its `ExtremeOut` comes back to **normal** framing — see below) and `{Zoom:Pullback}`.
- **Release the run before a side card, `{Image:}` or `{Video:}`.** One that comes in while the camera is still leaned in is shown anyway, but it logs a warning.
```
{Zoom:In}
{Zoom:In,Cut,D=4}
{Zoom:Pullback,D=3}
{Zoom:Out}
```
A three-step push, released by `{Zoom:Out}`:
```
[deadpan] They called the outage "minor."
{Zoom:In}
[slowing down] It took down payments.
{Zoom:In}
[flat] Then logins.
{Zoom:In}
[deadpan] Then the status page that said it was minor.
{Zoom:Out}
[dry] Anyway.
```

### Extreme close-up — `{Zoom:ExtremeIn}` … `{Zoom:ExtremeOut}`
A hard punch-in to a **close-up on Mugs's face**, used to punctuate a punchline. Both edges are **jump cuts** — no push, no easing, no motion whatsoever. The frame appears, holds dead still, and is gone.

**These two tags are a pair. Every `{Zoom:ExtremeIn}` needs a `{Zoom:ExtremeOut}`.**

- **You control the length by where you put the out tag**, not with a number — exactly like `{Video:}` (§4). Put `ExtremeIn` before the line you want in close-up and `ExtremeOut` after it. Want the close-up over three sentences? Put the out tag after the third.
- **No duration value.** `,D=` is ignored on both tags; so is `,Cut` (they're always cuts).
- The camera **finds his face wherever he's standing** — `Left`, `Right` or `Center` all work, no position change needed.
- **A close-up ends a `{Zoom:In}` run.** `ExtremeOut` comes back to **normal framing**, not to the push-in — if you want to lean in again afterwards, start over with a new `{Zoom:In}` (step 1). With no run active, `ExtremeOut` restores the exact framing that was on screen before the punch. You never need a `{Zoom:Reset}` after one.
- `ExtremeIn` **instantly cuts away every card and media item on screen** (side cards, BigText/BigMedia/BigImage/`bigCenter` cards, anything queued, `{Image:}`/`{Video:}`) on the same frame as the camera snap, with no fade. They **do not come back on `ExtremeOut`**, and a later End tag for one of them (`{Stat:End}`, `{Image:End}`, `{Video:End}`…) is a harmless no-op. An End tag on the same word as `ExtremeIn` is part of that cut. Never put a card or media between `ExtremeIn` and `ExtremeOut`: it is blocked (with a warning) for as long as the close-up is held. `{Black:}` is not affected.
- An emotion tag placed on the same beat as `ExtremeIn` / `ExtremeOut` (e.g. `{Shocked}` right next to the in tag, or `{Shrug,Cut}` on its own line just above the out tag) **swaps instantly, on the cut's own frame** — the camera snap is the transition, so the global presenter animation (shrink / grow / crossfade…) is skipped for that swap. "The same beat" means no spoken word between the two tags: side by side, or on adjacent lines (a `[stage direction]` between them doesn't count). Such tags always fire together, on the next word's onset (§5), and any emotion within 0.4 s of either cut is moved onto it. Write an explicit style (`{Shocked,Shrink}`) if you really want an animation there.
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

### Black cut — `{Black:seconds}` or `{Black:Start}` … `{Black:End}`
Hard-cuts a **fullscreen** black plane in (covering the character and all cards), then cuts out. No fade — pure jump cut. Great for dramatic beats / scene breaks. Two forms:

**Timed** — holds for `seconds`, then cuts out on its own:
```
{Black:2}
```

**Held pair (preferred when narration plays under the black)** — you can't know how long the spoken words under a black will take, so instead of guessing a number, open it with `{Black:Start}` and cut back with **`{Black:End}`** placed after the last line the black should cover — the same in-tag/out-tag principle as `{Video:name}`…`{Video:End}` and `{Zoom:ExtremeIn}`…`{Zoom:ExtremeOut}`. Both edges are jump cuts, and the cut back out lands exactly on the word where you put the End tag.
```
{Black:Start}
[flat] And for three days, nobody noticed.
[deadpan] Three. Days.
{Black:End}
```
- **Always close the pair.** Unlike `{Video:}`, nothing else ends a held black — no position change, card, or media dismisses it. An unclosed `{Black:Start}` stays black until the narration ends, where it's force-closed with a warning.
- `{Black:End}` with no black showing is harmless (like a stray `{Video:End}`).
- **A `{Black:Start}` on the script's final words can't be held** — nothing after the narration will ever close it, so it plays as a short ~2s timed cut instead. For an intentional closing black, use the timed form (`{Black:2}`).
- **A question hang — hold the black in silence with `{Pause}`.** End the covered line, put a `{Pause}` on its own line, then `{Black:End}` on its own line **below** it. The silence is exactly where the tag is, and the End tag — being below the pause — cuts back on the payoff's first word:
  ```
  {Black:Start}
  [quietly] So if no key leaked... who signed?
  {Pause:1.2}
  {Black:End}
  [genuinely baffled, voice rising] {Position:Left,Cut}{Shocked}The keys did.
  ```
  **Scripts that still use a voiced `[pause]` for this** must close the black inline instead: put `{Black:End}` at the start of the return line, after its stage direction, with the return frame's tags behind it (`[genuinely baffled, voice rising] {Black:End}{Position:Left,Cut}{Shocked}The keys did.`). v3 puts the silence wherever it likes after a `[pause]`, so an own-line End can cut back early and leave the silence playing on the revealed frame. With `{Pause}` that workaround is no longer needed.

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
- **A transition on the first line of a section gets its own silence.** The section break plays out as: last word → a short beat → the transition → a shorter beat → the new section's first word. Nothing is ever spoken behind the cover. You don't author this and you don't write a duration for it — the gap is sized automatically from the transition's own length, so `{Transition:Iris,1.2}` gets a proportionally longer one.
- **A transition placed mid-section still plays over the narration.** Only a section-opening one (nothing spoken before it — see §5) is given silence. That's deliberate: a mid-read transition is a flourish, not a break.
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
| `{Image:name}` 📁 | disk folders `Images/`, `CEO/`, `TwitterPosts/`, `Flags/`, `Logos/` (searched in that order) | an image **filename**, no extension (e.g. `privacy_headline`) |
| `{BigImage:name}` 📁 | the same image folders as `{Image:}` | an image **filename**, no extension |
| `{Video:name}` 📁 | disk folder `BRoll/` | a video **filename**, no extension |
| `{Logo:name}` 🎬 | the Unity **`ContentCardAssets`** asset (logo list) | a configured **company name** (case-insensitive) |
| `{BigMedia:name}` 🎬 | the Unity **`ContentCardAssets`** asset (logo list), then `Resources/Media/` | a configured **company name** |
| `{BRoll:description}` 🎬 | the Unity **`ContentCardAssets`** asset (clip list) | a configured **clip description** (case-insensitive) |

Rules for every 📁 / 🎬 tag:
- **Use only names that appear in `MEDIA_LIBRARY.md`.** Type them **exactly**, matching case, **without the file extension**. Don't invent names and don't reuse placeholders like `ArticleTemp` / `VideoTemp`.
- **Note the two `BRoll`s are different tags.** `{Video:name}` plays a **file** from the `BRoll/` disk folder (silently, under the narration, until its `{Video:End}` or the next beat). `{BRoll:description,4}` is a **side card** that resolves a *description* through the Unity asset. They are not interchangeable.
- **No `MEDIA_LIBRARY.md`? Don't use these tags at all.** Reach for a self-contained ✅ card instead: a `Headline`, `Stat`, `Quote`, `Excerpt`, or `BigText` conveys the same beat with text you write inline, and it always renders. A script full of working text cards beats a script with blank media slots.

---

### Image — `{Image:name}` or `{Image:name,seconds}`  📁
Shows an image in the media area. `name` is the file name (**extension omitted**) found in any of the image folders under the media root (`Images/`, `CEO/`, `TwitterPosts/`, `Flags/`, `Logos/` — the folder itself is never written in the tag) — see *"Media & asset cards"* above; use a real name from `MEDIA_LIBRARY.md`. Duration optional — defaults to **3s**.

**Choosing a side — optional `,Left` / `,Right` (last value).** Like the side content cards, the image sits on the **left** of the screen by default and slides in from that side with the same eased overshoot entry the cards use. Append `,Left` or `,Right` as the **final** value to pick the side it rests on (and slides in from) — `,Right` mirrors the media slot to the right of the screen. Place the presenter on the opposite side (`{Position:...}`) so they don't overlap it.
```
{Image:privacy_headline,4}
{Image:privacy_headline,4,Right}
{Image:stock_chart,Right}
```

**Holding an image — `{Image:name,Start}` … `{Image:End}` (optional).** When an image should stay up for as long as some narration runs — which you can't know in seconds — swap the duration for the keyword **`Start`** and close with **`{Image:End}`** placed after the last line the image should cover. It works exactly like `{Video:name}`…`{Video:End}`: the image stays up under everything between the two tags and leaves precisely where the End tag sits in the narration. A `,Left`/`,Right` side still goes **last, on the opening tag only** (`{Image:name,Start,Right}`), never on `{Image:End}`. Like a `{Video:}`, the end tag is a safety-netted pair: an unclosed held image still ends at the next beat (a position change, a content card, the next `{Image:}`/`{Video:}`) or shortly after the narration ends. `End` is a **reserved name** here too — an image file literally called `End` can't be shown (rename it).
```
{Image:stock_chart,Start,Right}
Look at the cliff in March. That is the exact day the policy leaked, and the market never forgave them for it.
{Image:End}
```

### Video — `{Video:name}` … `{Video:End}`  📁
Plays a video clip file from the `BRoll/` disk folder — `name` is the **filename without extension** (a real one from `MEDIA_LIBRARY.md`). **The clip is always silent and the narration keeps playing right over it** — treat it as b-roll under the voice, not as a break in the read. (Not to be confused with the `{BRoll:description}` **side card**, which resolves a description through the Unity asset — see *"Media & asset cards"*.)

**Don't give a video a duration — you can't know one.** You have no idea how many seconds the narration under a clip will take once it's spoken, so any number you write is a guess that cuts the b-roll off mid-sentence. Instead the tags work **exactly like `{Zoom:ExtremeIn}` … `{Zoom:ExtremeOut}`**: `{Video:name}` opens the clip, and you close it with **`{Video:End}`** placed after the last line the b-roll should cover. The clip loops under everything between the two tags and cuts precisely where the end tag sits in the narration.

```
{Position:Right}
{Video:hardDriveSpinning}
He killed the process. Most of it was already gone. Twenty years of files, walking out the door while he watched.
{Video:End}
[flat] That's the whole story.
```
Here the clip runs under the three narrated sentences and ends exactly before the last line — no guessing required.

**Unlike `ExtremeOut`, the end tag is optional.** A clip with no `{Video:End}` still ends on its own at the next beat, so an unclosed video never runs away:

- the presenter **moves to a different position** (`{Position:Center}`, or the other side),
- a **content card** appears (`Stat`, `Quote`, `Headline`, `Excerpt`, `Logo`, `BRoll`, or a feature card),
- the **next `{Image:}` or `{Video:}`** fires,
- or the narration ends.

Reach for `{Video:End}` whenever none of those beats falls where you want the clip to stop — it's the only way to end b-roll **mid-read**, without moving the presenter or spending a card. `{Video:End}` where no clip is playing is harmless. `End` is a **reserved name**: a clip file literally called `End` can't be played (rename it).

A number is still accepted (`{Video:name,6}`) but it only sets a *minimum* — it can not cut a clip short, and it matters only for a clip placed on the script's final words.
```
{Video:datacenter_broll,6}
```

**Choosing a side — optional `,Left` / `,Right` (last value, opening tag only).** Like `{Image:}` and the side cards, the clip plays on the **left** of the screen by default and slides in with the cards' eased overshoot entry. Append `,Left` or `,Right` as the **final** value of the *opening* tag to pick the side it rests on (and slides in from) — never on `{Video:End}`. Place the presenter on the opposite side so they don't overlap it.
```
{Video:hardDriveSpinning,Right}
{Video:datacenter_broll,6,Right}
```

### Content cards (side panel — character must be Left/Right)

**Choosing a side — optional `,Left` / `,Right`.** Every card in this group appears on the **left** of the screen by default. Add `,Left` or `,Right` as the **final** value (right after the duration) to choose which side it **sits on** (and slides in from); omit it to keep the default (`Left`). A `,Right` card mirrors the left layout over to the right side of the screen. This is independent of rule 8 — the character must still be at `Left`/`Right` for any side card to show at all, and the card's side does **not** move the character (place the presenter with `{Position:...}` so they don't overlap a same-side card). (For `Headline`, the side is an alternative to `,bigCenter` — a `bigCenter` headline is centered, so it takes no side.)

**Holding a card — `Start` … `{Tag:End}` (optional, every card in this group except `Timeline`, which is always held).** When a card should stay up for as long as some narration runs — which you can't know in seconds — swap its duration number for the keyword **`Start`** and close it with the matching End tag (`{Headline:End}`, `{Excerpt:End}`, `{Quote:End}`, `{Stat:End}`, `{Logo:End}`, `{BRoll:End}`, `{Compare:End}`, `{Document:End}`, `{Map:End}`) placed after the last line the card should cover. Same in-tag/out-tag principle as `{Black:Start}`…`{Black:End}`, `{Video:name}`…`{Video:End}` and `{Zoom:ExtremeIn}`…`{Zoom:ExtremeOut}`: the card slides in exactly where the `Start` tag sits and leaves exactly where the End tag sits, so its timing always matches the words.
```
{Position:Left}
{Quote:"We believe transparency is at the core of everything we do.","Sarah Mitchell","VP of Communications, MegaCorp",Start}
[dry] Keep that sentence on screen while I read what they actually did.
[flat] Because the contrast is the whole story.
{Quote:End}
```
- `Start` sits **exactly where the duration number would**; an optional `,Left`/`,Right` may still follow it (`{Stat:"…","…","…",Start,Right}`), and it goes on the **opening** tag only — never on the End tag. (A `Stat` arrow or a `Document` stamp keyword sits between them: `…,Start,Loss,Right`, `…,Start,Allegation,Right`.)
- **Always close the pair, with the matching type** — `{Quote:End}` closes a Quote, not a Logo. An End tag with no matching card up is harmless. An unclosed held card is force-closed when the narration ends, with a warning — but everything that normally clears cards still applies first: a `{Transition:…}` wipes it, moving to `Center` hides it, and a later card replaces it.
- `End` is a **reserved word** for `Logo` names and `BRoll` descriptions (a company literally named "End" can't be shown).
- **Side cards and the big headline.** A `,bigCenter` headline holds the same way — see *"Holding a big headline"* under **Headline** below — and so does the fullscreen `Versus` (`…,Start` … `{Versus:End}`). The other fullscreen feature cards (`BigMedia`, `BigImage`) stay timed. (`BigText` has its own held mode — the line-by-line flow, see below; `Timeline` works the same line-by-line way.)

**Headline** — a news headline with its source. Add `,bigCenter` to promote it to a fullscreen centered card, **or** `,Left`/`,Right` to choose which side of the screen a normal side-panel headline sits on.
```
{Headline:"Tech Giant Quietly Changes Privacy Policy","The Verge",5}
{Headline:"Markets React Within the Hour","Bloomberg",5,Right}
{Headline:"40% User Growth After Privacy Backlash","Android Authority",5,bigCenter}
```
Fields: `"headline","source",duration|Start[,bigCenter|,Left|,Right]`

**Holding a big headline — `{Headline:"…","…",Start,bigCenter}` … `{Headline:End}` (preferred when narration plays under it).** You can't know how long the words under a fullscreen headline will take to say, so a number either cuts it off mid-sentence or leaves it hanging over the next beat. Put **`Start`** where the duration goes, keep `,bigCenter` after it, and close it with **`{Headline:End}`** after the last line it should cover — the same in-tag/out-tag principle as `{Black:Start}`…`{Black:End}` and `{Zoom:ExtremeIn}`…`{Zoom:ExtremeOut}`. The headline slides in exactly where the `Start` tag sits and slides out exactly where the End tag sits.
```
{Position:Center} {Serious}
{Headline:"Tech Giant Quietly Changes Privacy Policy","The Verge",Start,bigCenter}
[slowing down, serious] Read that again. Quietly. On a Friday night.
[dry] Nobody was supposed to notice.
{Headline:End}
```
- `Start` sits **exactly where the duration number would**, and `,bigCenter` stays **last**: `"…","…",Start,bigCenter` — never `bigCenter,Start`.
- It's closed by the plain **`{Headline:End}`** — there is no `{BigCenter:End}`. Same End tag as a held side headline; don't hold a side headline and a big headline at the same time.
- It works from **any position, including `Center`** — it covers the presenter, and its `{Headline:End}` lands even while the presenter is centered.
- **Always close the pair.** An unclosed big headline is force-closed when the narration ends, with a warning — but a `{Transition:…}` or a `{Zoom:ExtremeIn}` still wipes it first (its later `{Headline:End}` is then harmless).
- A card that comes due while it's up waits behind it and slides in after `{Headline:End}` — so don't put other cards inside the pair.
- **A closing end-card on the script's final words should stay timed** (`{Headline:"…","…",5,bigCenter}`) — nothing is spoken after it to hold it open, so a held one would be force-closed as soon as it appears.

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

**Stat** — a big number with a label and context, plus an **optional win/loss arrow** next to the number.
```
{Stat:"2.3 billion","Monthly Active Users","as of Q2 2025",5}
{Stat:"+40%","Revenue Growth","vs the same quarter last year",5,Win}
{Stat:"$13B","OpenAI 2024 Losses","a widening deficit",5,Loss,Right}
```
Fields: `"value","label","context",duration[,Win|,Loss|,NoArrow][,Left|,Right]`

**Choosing the arrow — optional `,Win` / `,Loss` / `,NoArrow` (right after the duration).** The arrow is **only ever what you write** — it is never guessed from the card's text, so a `-`, `+`, `↑` or `↓` inside a field changes nothing.
- `,Win` — a big **green arrow pointing up**; the number and context line turn green. Use it when the stat is good news *for the story's subject* (growth, a record, a gain).
- `,Loss` — a big **red arrow pointing down**; the number and context line turn red. Use it for a drop, a loss, a decline.
- `,NoArrow` — no arrow. **This is also what you get when you write no keyword at all**, so it's only there for when you want to be explicit.
- **Leave the arrow off for a neutral number** — a count, a date, a size, a share (`"8","Airlines Asked"`, `"0.3%","Users Who Read Full ToS"`). An arrow claims a direction; don't add one unless the stat actually went up or down.
- The keyword sits **right after the duration (or `Start`)**; if the card also takes a side, the side stays **last**: `{Stat:"…","…","…",5,Loss,Right}`, `{Stat:"…","…","…",Start,Win}`. Opening tag only — never on `{Stat:End}`. **Stat only** — writing `,Win`/`,Loss` on any other card makes that card silently not appear.

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

**Compare** ✅ — two numbers as proportional bars, for scale ("about four times the Bybit hack"). Two rows, A above B: each a label over a bar with its value at the bar's end; the source sits underneath. **A is the story's subject** (drawn in the accent colour), B is the reference (neutral). The bars are honest: linear from zero, the larger value fills the track, the smaller is drawn at its true ratio.
```
{Compare:"$6B+","Binance, 2026","$1.5B","Bybit, 2025","FBI; TRM Labs",5}
{Compare:"$6B+","Binance, 2026","$1.5B","Bybit, 2025","FBI; TRM Labs",Start,Right}
```
Fields: `"value A","label A","value B","label B","source",duration|Start[,Left|,Right]` — exactly **5** quoted fields.
- **Both values must be the same kind of number** so they can share one scale: both money in the same currency (`$6B+` / `$1.5B`), both percentages (`95%` / `40%`), both multiples (`4x` / `1x`), or both plain numbers. A value may carry a currency sign (`$ € £ ¥`), thousands commas (`€360,000` — fine, it's inside quotes), a magnitude `K`/`M`/`B`/`T`, and a trailing `+`.
- If a value isn't a number, or the two kinds differ (`$6B` vs `40%`, `50°C`), the card still shows — but **without bars**: both values big, side by side, with a small "vs" between them (and a warning in the log). Write comparable values when you want the bars.
- Every word shown is one of the five fields, exactly as written.

**Document** ✅ — a filing, complaint, alert or report quoted **on paper**, so it doesn't look like a person's quote. Same first three fields as `Excerpt`, so you can switch between them: a passage, the phrase to highlight (it gets a yellow marker sweep), and the source (shown in the paper's header strip).
```
{Document:"The attacker still holds about USD 2.4 billion in assets beyond issuer reach.","beyond issuer reach","TRM Labs",5,Report}
{Document:"Plaintiffs allege the company sold user data without consent.","sold user data","Class-action complaint",Start,Allegation,Right}
```
Fields: `"passage","phrase to highlight","source",duration|Start[,Allegation|,Filing|,Ruling|,Report][,Left|,Right]`
- The phrase must appear in the passage **exactly** (same rule as `Excerpt`) — otherwise the card shows without a highlight.
- **The stamp — optional keyword, right after the duration (or `Start`).** It slams a rubber stamp onto the paper; the words are fixed by the keyword, never free text:

  | Keyword | Stamp |
  |---|---|
  | `Allegation` | **ALLEGATION** / *NOT PROVEN IN COURT*, red |
  | `Filing` | **COURT FILING**, blue |
  | `Ruling` | **RULING**, green |
  | `Report` | **REPORT**, grey |

  No keyword → no stamp. **Use `Allegation` for anything a lawsuit, complaint or regulator claims that hasn't been decided** — it puts "not proven in court" on screen for you. `Ruling` only for an actual court decision.
- If the card also takes a side, the side stays **last**: `…,Start,Allegation,Right` — never `…,Right,Allegation`.

**Timeline** ✅ — dated events that appear **as they're spoken**, one node per tag, stacked vertically (a line down the left of the card, each node a dot with its date and event). It works line-by-line like `{BigText:LINE}` … `{BigText:End}`: **no duration** — each tag adds one node at its spot in the narration, and **`{Timeline:End}`** closes the card.
```
{Position:Left}
{Timeline:"Mar. 3","Privacy policy quietly updated",Right}
Three weeks later, somebody actually read it.
{Timeline:"Mar. 24","A user spots the AI clause"}
And nine days after that...
{Timeline:"Apr. 2","The company apologizes"}
...came the apology.
{Timeline:End}
```
Fields: `"date","event"[,Left|,Right]` — exactly **2** quoted fields, no duration.
- **The first `{Timeline:…}` opens the card**; a `,Left`/`,Right` on it chooses the side. A side on any **later** node is ignored (with a warning).
- **Up to 5 nodes.** A 6th is ignored (with a warning). Keep each event short — it gets up to two lines.
- **Always close it with `{Timeline:End}`.** Like a held side card it is cleared early by a `{Transition:…}`, by moving to `Center`, or by an extreme close-up — after that, any further nodes are ignored and its `{Timeline:End}` is a harmless no-op.
- Like every side card it needs the presenter at `Left` or `Right` (rule 8).

**Map** ✅ — a region map with the countries you list lit up one after another, plus your caption and source underneath. For "at least eight more countries" — a list nobody can hold in their head.
```
{Map:"Europe","BE+LU+SE+IE+NL+EE+FI+PT","At least 8 more countries","AFP, Nov. 2",5}
{Map:"Asia","JP+KR+SG","Rolled out in three markets","Company announcement",Start,Right}
```
Fields: `"region","countries","caption","source",duration|Start[,Left|,Right]` — exactly **4** quoted fields.
- `region` is **one of exactly**: `World`, `Europe`, `NorthAmerica`, `SouthAmerica`, `Africa`, `MiddleEast`, `Asia`, `Oceania` (spelled and capitalized like that, no spaces).
- `countries` are **ISO 3166-1 two-letter codes** (`US`, `GB`, `DE`, `BE`, `LU` …) joined with `+` — not names. An unknown code, or one outside the region's view, is skipped (with a warning) — never guessed. Tiny countries (Luxembourg, Estonia…) still show, as a lit dot.
- **No country names are drawn on the map** — only your caption and source. If the names matter, say them, or put them in the caption.

### Feature cards (fullscreen, in front of the character)

**BigMedia** 🎬 — 1 to 4 logos/images shown large and centered. Each `name` resolves through the Unity `ContentCardAssets` asset (then `Resources/Media/`) — same source as `{Logo:}`, *not* the disk image folders — so use configured logo names from `MEDIA_LIBRARY.md`. Join multiple with `+` (≤4). A name that doesn't resolve is shown as plain text (its own letters), not an image.
```
{BigMedia:Google,4}
{BigMedia:Google+Brave+X,4}
```

**BigText** — 1 to 4 big centered text lines. No commas in the text (use `+`; each `+` starts a new stacked line). It has **two modes**:

**Line-by-line (preferred for multi-line)** — `{BigText:LINE}` … `{BigText:End}`, **no duration numbers**. Each duration-less tag lands ONE line at its exact spot in the narration: the first opens the stack, each following tag slides its line in beneath the ones on screen (the stack re-centers), and **`{BigText:End}` closes the whole stack** — the same in-tag/out-tag principle as `{Zoom:ExtremeIn}`…`{Zoom:ExtremeOut}` and `{Video:}`…`{Video:End}`. This is how you sync each line to the words being spoken. These tags may sit **mid-sentence, right on the word they belong to** (an exception to the own-line rule, like emotion tags). Up to 4 lines; `End` is a **reserved word** (never a displayable line). Always close the stack — an unclosed one is force-closed when the narration ends, with a warning.
```
Your data {BigText:YOUR DATA} goes straight into {BigText:→} their model. {BigText:THEIR MODEL}
And that should worry you. {BigText:End}
```

**Timed (one tag, fixed lifetime)** — text plus a duration; `+`-joined lines appear together on a quick internal stagger and the card hides itself after the number:
```
{BigText:ANOTHER ONE,3}
{BigText:YOUR DATA+→+THEIR MODEL,6}
```
Don't mix the modes in one beat — a duration-less line landing while a timed BigText is up joins it but keeps the timed lifetime.

**BigImage** 📁 — a large website-article or headline **screenshot** that covers the **left 3/4** of the screen, leaving the right quarter open for the presenter. It fills that area edge-to-edge (cropped to fit, no distortion) and drops in from the top. `name` is an image **filename (no extension)** in the same disk image folders `{Image:}` uses — use a real one from `MEDIA_LIBRARY.md` (drop your screenshot there first). It's a feature card (not suppressed at `Center`), but it's designed to **share** the screen: put the presenter on the **right** so they stand in the open quarter beside the article, not behind it.
```
{Position:Right}
{BigImage:tesla_q3_article,6}
```

**Versus** ✅ — **the claim against the evidence**, full screen: two panels side by side, a large **≠** stamped between them. For the beat where what someone says and what the record shows don't match.
```
{Versus:"Binance says","No private key leaked","The on-chain record says","Eleven of its own keys signed",Start}
[flat] One of those two sentences is wrong.
{Versus:End}
{Versus:"Old policy","Your data stays yours","New policy","Your data trains their model",5,Vs}
```
Fields: `"left label","left text","right label","right text",duration|Start[,Vs]` — exactly **4** quoted fields.
- Each panel shows its **label** small (left in slate, right in the accent colour) above its **text** in big bold type (up to 4 lines) — so keep each text a short claim, not a paragraph.
- `,Vs` (optional, last) puts **VS** in the badge instead of **≠** — use it for a match-up of two things rather than a claim contradicted by evidence. It takes **no** `,Left`/`,Right` (it's centered).
- A fullscreen feature card: it shows at **any position, including `Center`**, dims the scene behind it, and is wiped by a `{Transition:…}` or cut by `{Zoom:ExtremeIn}`. Hold it with `Start` … `{Versus:End}` while the narration under it plays, or give it a duration.

---

## 5. Placement & timing

- A tag fires at the moment the narration reaches **the next spoken word after the tag**. So place a tag on its own line *immediately before* the sentence (or right after the sentence whose end you're reacting to).
- **Tags with no spoken word between them are one beat and fire on the same frame**, whether they share a line or sit on adjacent lines (`[stage directions]` don't count as spoken words). `{Shrug,Cut}` on one line and `{Zoom:ExtremeOut}` on the next land together on the next word's onset. The voice's timing data spreads stacked tags across the silence before that word, so the engine lines them back up on the measured onset; it only ever pulls a tag earlier, by at most 0.9 s. A tag that should fire *later* than its neighbours needs a spoken word between them, e.g. inline in the next line (`So if buyers {Shrug}stopped…`).
- **The one exception is a `{Pause}`.** Tags written above a `{Pause}` fire as its silence begins — right after the previous word — and the tags below it fire on the next word, after the silence (§4 *"Pause"*). That split wins over the same-beat rule above.
- Typical pattern: the spoken line, then the visual that should accompany the *next* line:
```
[deadpan] A company did a thing. It was bad. Let's talk about it.
{Zoom:In}
[genuine disbelief] No — actually bad.
```
- **Side cards need a side position.** Set `{Position:Left,...}` or `{Position:Right,...}` *before* a `Headline`/`Excerpt`/`Quote`/`Stat`/`Compare`/`Document`/`Timeline`/`Map`/`Logo`/`BRoll` tag. While the character is `Center`, side cards are suppressed (they'd overlap the centered character).
- **Pick the side (optional).** A side card sits on the **left** of the screen by default. Append `,Left` or `,Right` as the card's **last** value to choose which side it rests on and slides in from (e.g. `{Quote:"…","…","…",5,Right}`, `{Logo:Brave,4,Right}`) — `,Right` mirrors it to the right side. It's independent of where the character stands, so with the presenter on the `Left` you can drop a card on the `Right`. For `Headline`, it's an alternative to `,bigCenter`. **The `{Image:}` and `{Video:}` media tags take the same modifier** (e.g. `{Image:privacy_headline,4,Right}`, `{Video:hardDriveSpinning,Right}` — on `{Video:}` it goes on the *opening* tag, never on `{Video:End}`) and enter with the same eased slide.
- **Fullscreen feature cards work anywhere.** `BigText`/`BigMedia`/`Versus` (and a `Headline` with `,bigCenter`) render in front of everything, so they appear in any position — including `Center`. Use them for the "front and center" moments.
- **`BigImage` is the feature card that wants a side.** It isn't suppressed at `Center`, but it covers only the left 3/4 — set `{Position:Right}` so the presenter stands in the open right quarter beside the article instead of hidden behind it.
- **Transitions open a new section.** Put `{Transition:…}` on the **first line of a section** and group that section's whole scene change onto the same line (position, emotion, mood, a card, an image, a zoom). They're applied under cover — see §4. This is the one place you deliberately stack several tags on a single line. A side card grouped on a transition line still needs a `Left`/`Right` position on that same line (rule 8). Placing it there is also what earns the transition its silent gap: "first line" means **nothing spoken before it** — other tags (`{Timestamp:"…"}`, and the ones on its own line) are fine, a narration line above it is not.
- **A `{Video:}` has no duration — you end it with `{Video:End}`.** Put `{Video:name}` before the lines the b-roll should cover and `{Video:End}` after the last one; the clip loops under everything in between and cuts exactly there. The end tag is optional: an unclosed clip still ends at the next beat (a `{Position:...}` change, a content card, another `{Image:}`/`{Video:}`, or the end of the narration) — but `{Video:End}` is the only way to stop b-roll mid-read without spending one of those beats (§4).
- **`{Zoom:ExtremeIn}` has no duration either — you end it with `{Zoom:ExtremeOut}`.** Same in-tag/out-tag principle as `{Video:}`: put the in tag before the line you want in close-up and the out tag after the last line it covers. It works from any position (`Left`/`Right`/`Center`), needs no `{Zoom:Reset}` afterwards, and **must always be closed** — an unpaired `ExtremeIn` holds the close-up for the rest of the video (§4).
- **A held black works the same way.** `{Black:Start}` before the first line the black should cover, `{Black:End}` after the last one — the cut back out lands exactly on that word. **Always close it** — nothing else ends a held black; an unclosed one stays black until the narration ends (§4). For a black with no narration under it, the timed `{Black:2}` is simpler.
- **A held side card, big headline, Versus or image works the same way too.** Swap the duration for `Start` (`{Stat:"…","…","…",Start}`, `{Logo:Brave,Start,Right}`, `{Headline:"…","…",Start,bigCenter}`, `{Versus:"…","…","…","…",Start}`, `{Image:stock_chart,Start}`) and close with the matching `{Stat:End}` / `{Logo:End}` / `{Headline:End}` / `{Versus:End}` / `{Image:End}` after the last line it should cover (§4 *"Holding a card"* / *"Holding an image"*). Reach for it whenever a card should track a stretch of narration instead of guessing seconds — the entry and exit then land exactly on your words, like every other held pair.
- **`{Echo}` is the one tag that ignores the next word.** It always opens on the video's first frame, as long as it sits at the top of the cold open (above the first spoken line). Placed anywhere else it fires like any other tag — and the log warns that it belongs at the top.
- **Don't place a tag on the script's very last word and expect it to fire late** — it's fine, the recording now holds until trailing tags (e.g. an end-card `{Logo:...,8}` or final `{Black:2}`) finish their full duration.
- **A `Timeline` builds as you speak.** Put each `{Timeline:"date","event"}` on its own line right before the sentence that mentions that date, and `{Timeline:End}` after the last line the timeline should stay up for — like a line-by-line `BigText`.
- Reasonable default durations: cards 5s, excerpts and documents 6s, compare and map 5–6s, versus 5s (or held), big text 3–4s, logos 3–4s, black cuts 2–3s.

### Captions in the gaps (automatic, no tag)
With the main menu's **Captions in gaps** on (the default), the script's own spoken words appear in a band along the bottom whenever nothing else is on screen. There is no tag. Captions follow from where you leave the frame bare, so this is what decides when they show:
- **A caption is a phrase of 3–7 words, shown whole or not at all.** It appears on its first word and stays until just after its last (bridged to the next phrase when that follows within 0.6 s). It never pops in halfway through a line.
- **It only starts if nothing is due before its last word**: a card or its End, an `{Image:}`/`{Video:}`, a zoom, `{Black}`, a transition, a chapter title, the `{Echo}`, or Mugs gliding to a new `{Position}`. A phrase that would be cut off is skipped, and the next one gets its turn.
- **The frame must have been bare for 0.4 s first**, so a caption never arrives in the same breath as a card leaving. A line that starts on the same word a card ends gets no caption; the next phrase does.
- **Never over a zoom.** No caption during a `{Zoom:In}` run, the glide home or an extreme close-up, and the cold open's first line never gets one.
- The upshot: a stretch with no visual for a sentence or two shows captions; a section packed with cards shows almost none. To keep a passage text-free, put a visual on it; to give it captions, leave it bare.

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
- [ ] Every `{...}` card / Logo / BRoll / BigMedia / BigImage / Versus ends in `,number` (before any modifier keywords). `BigText` too, **unless** it's the line-by-line mode — then **no** tag in the run carries a number and the run **ends with `{BigText:End}`** (count opens and Ends; a stack left open is force-closed at narration end). **Side cards** (`Headline`/`Excerpt`/`Quote`/`Stat`/`Compare`/`Document`/`Map`/`Logo`/`BRoll`) and `Versus` may instead use `,Start`, and a big headline `,Start,bigCenter` — then each has a matching same-type `{Tag:End}` later in the script (count them per type, the totals must be equal; a held big headline counts as a `Headline`).
- [ ] **Every `Timeline` run has no durations, at most 5 nodes, a `,Left`/`,Right` only on its first node, and ends with `{Timeline:End}`.**
- [ ] **`Compare` values are the same kind** (same currency, both `%`, both `x`, or both plain) so the bars can be drawn; value A is the story's subject.
- [ ] **`Map` region is one of** `World`/`Europe`/`NorthAmerica`/`SouthAmerica`/`Africa`/`MiddleEast`/`Asia`/`Oceania`, and the countries are two-letter ISO codes joined with `+` (no names, no spaces).
- [ ] **Every `Document` stamp is accurate** — `,Allegation` on anything claimed but not decided, `,Ruling` only on an actual court decision; the highlight phrase appears in the passage exactly.
- [ ] **No `{Video:}` carries a duration** — each one is closed by a `{Video:End}` after the last line it covers (preferred), or ended by the next `{Position:...}`, content card, or media tag you placed after it.
- [ ] **Every `{Image:name,Start}` has a matching `{Image:End}` later in the script** (an unclosed one is ended by the next beat or the end of the narration, like a `{Video:}`).
- [ ] **Every `{Zoom:ExtremeIn}` has a matching `{Zoom:ExtremeOut}` later in the script** — count them, the totals must be equal. Neither tag carries a duration.
- [ ] **No more than three `{Zoom:In}` in a run**, and every run is released (`{Zoom:Out}`, `,D=`, or something that ends it — §4 *"Camera zoom"*) before any side card, `{Image:}` or `{Video:}`.
- [ ] **Every `{Black:Start}` has a matching `{Black:End}` later in the script** — count them, the totals must be equal. (The timed `{Black:seconds}` form needs no End tag.)
- [ ] **Every `{Pause:seconds}` is on its own line between two sentences** — never on a line with spoken words, never mid-sentence — with a value from 0.1 to 3.0, **below** the tags that should fire before the beat and **above** the ones that should fire after it.
- [ ] **Every 📁/🎬 media name (`Image` / `Video` / `BigImage` / `Logo` / `BigMedia` / `BRoll`) is a real name copied from `MEDIA_LIBRARY.md`** — exact spelling and case, no extension. Any beat without a confirmed name uses a ✅ text card (`Headline`/`Stat`/`Quote`/`Excerpt`/`BigText`) instead. No invented or placeholder names.
- [ ] **No space next to a field-separating comma or between `"` and `"`** — `"a","b","c",5`, never `"a", "b", "c", 5`.
- [ ] **No empty `""` fields; field counts exact** — `Headline`/`Timeline` have 2 quoted fields, `Excerpt`/`Quote`/`Stat`/`Document` have 3, `Versus`/`Map` have 4, `Compare` has 5.
- [ ] No smart quotes anywhere; no `"` inside a quoted field.
- [ ] No commas inside `Logo` / `BRoll` / `BigMedia` / `BigText` / `BigImage` names (used `+` for multiples, ≤4; `BigImage` is a single image).
- [ ] Emotion / Position / Zoom keywords spelled exactly and capitalized.
- [ ] `Transition` / `Mood` (and their variants) spelled exactly and capitalized.
- [ ] Each `{Transition:…}` is on the first line of its section, with that section's other tags grouped onto the same line.
- [ ] Every content card is preceded by a `Left` or `Right` position.
- [ ] Any `,Left`/`,Right` entry-side modifier (side cards, `{Image:}`, `{Video:}`) is the **last** value in the tag (after the duration **or** `Start`, and after a Stat arrow or Document stamp), spelled exactly (capitalized), not combined with `,bigCenter`, never on `Versus`, and never on an `End` tag.
- [ ] **Every `Stat` arrow is deliberate** — `,Win` (green up) only on a real gain, `,Loss` (red down) only on a real drop, nothing (or `,NoArrow`) on a neutral number. The keyword sits right after the duration/`Start`, before any `,Left`/`,Right`, and appears on `Stat` tags only.
- [ ] No `T=` written by hand.
- [ ] One tag per line.
- [ ] Each `## SECTION` that should be a YouTube chapter has a `{Timestamp:"..."}` on its first line, and the first section's is at the very top (so its chapter is `0:00`).
- [ ] **At most one `{Echo:"HEADLINE","accent",seconds}`**, on its own line under the cold open's opening tags, above the first spoken line — HEADLINE and accent word copied exactly from the thumbnail, ≤5 words, the accent one of those words.
