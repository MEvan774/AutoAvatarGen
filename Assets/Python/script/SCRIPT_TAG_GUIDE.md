# AutoAvatarGen — Script Tag Reference & Authoring Guide

**Audience:** the AI (Claude / Opus 4.8) that writes `Script.txt`.
**Goal:** produce a script whose tags are formatted *exactly* the way the pipeline expects, so nothing breaks and every visual fires on time.

Paste this whole file to the model before asking it to write a script, or keep it as the system instruction for script generation.

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
2. **One tag per line, on its own line.** Don't put two tags on one line and don't bury a tag mid-sentence (emotion tags are the one exception — see §4).
3. **Use straight ASCII double quotes `"` only.** Never curly/smart quotes (`“ ”`). Smart quotes break card tags.
4. **Never put a `"` *inside* a quoted card field.** The field ends at the first `"`. If you need a quote inside quoted text, rephrase or use single quotes `'`. (Commas, periods, em‑dashes `—`, `%`, `$`, `€` inside quotes are fine.)
5. **Every content card, `Logo`, `BRoll`, `BigMedia`, and `BigText` MUST end with a duration number** (`,5` = 5 seconds). Decimals allowed (`,4.5`).
6. **Unquoted names (`Logo`, `BRoll`, `BigMedia`, `BigText`) must not contain commas.** The comma is a delimiter. Use `+` to join multiple items.
7. **Spell fixed keywords exactly, capitalized as shown:** emotions, `Position`, `Left/Right/Center`, `Zoom`, `In/Out/Reset/Pullback`, `Cut`, `Smooth`, `bigCenter`.
8. **Side content cards only appear while the character is at `Left` or `Right`.** Moving to `Center` hides/suppresses side cards (`Headline`, `Excerpt`, `Quote`, `Stat`, `Logo`, `BRoll`) — they'd overlap the centered character. Put the character on a side before showing one (see §5). **Fullscreen feature cards (`BigText`, `BigMedia`, `BigCenter`) are exempt** — they render in front of everything and appear in any position, including `Center`.
9. Keep the narration itself natural — it can contain quotes, commas, anything. The rules above apply to **tags only**.

---

## 3. Quick cheat sheet

| Tag | Syntax (what you write) | Duration required? |
|---|---|---|
| Emotion | `{Neutral}` `{Excited}` `{Serious}` `{Sad}` `{Concerned}` | no |
| Character position | `{Position:Left}` (+ optional `,Cut` or `,Smooth`) | no |
| Camera zoom | `{Zoom:In}` (+ optional `,Cut` and/or `,D=seconds`) | no |
| Black cut | `{Black:3}` | yes |
| Image | `{Image:name}` or `{Image:name,4}` | optional (default 3s) |
| Video clip | `{Video:name}` or `{Video:name,6}` | optional (full clip) |
| Headline card | `{Headline:"headline text","Source",5}` (+ optional `,bigCenter`) | yes |
| Excerpt card | `{Excerpt:"full passage","phrase to highlight","Attribution",6}` | yes |
| Quote card | `{Quote:"the quote","Person Name","Role / Title",5}` | yes |
| Stat card | `{Stat:"2.3 billion","Label","Context line",5}` | yes |
| Logo card | `{Logo:Google,4}` | yes |
| B‑roll card | `{BRoll:description,4}` | yes |
| Big media (fullscreen) | `{BigMedia:Google,4}` or `{BigMedia:Google+Brave+X,4}` (≤4) | yes |
| Big text (fullscreen) | `{BigText:ONE LINE,3}` or `{BigText:LINE 1+LINE 2,4}` (≤4) | yes |
| Stage direction | `[deadpan]` `[slowing down, serious]` | no |
| Section heading | `## COLD OPEN` | n/a |

---

## 4. Every tag in detail

### Section headings — `## NAME`
Splits the script into separately-rendered, stitched segments. Each section gets a short natural pause after it. Use them for major beats (cold open, setup, breakdown, take, closer, etc.). **The file must begin with one.**
```
## COLD OPEN
```

### Stage directions — `[ ... ]`
A delivery cue for the narration (pacing, tone). It is **not** spoken and produces no on‑screen visual; ElevenLabs just uses it to shape the read. Any characters except `]` are allowed (commas are fine).
```
[deadpan] A company did a thing. It was bad.
[slowing down, serious] Here's what the policy actually says.
```

### Emotions — `{Neutral}` `{Excited}` `{Serious}` `{Sad}` `{Concerned}`
Sets the avatar's facial expression. Exactly these five words. Place on its own line just before the line it should color (may also appear inline, but own-line is preferred for clarity).
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
`In` (push in for focus/intensity), `Out` (pull back), `Reset` (instant snap to default), `Pullback` (snap wide, drift wider, jump back).
- `,Cut` = snap instead of animating (ignored by `Reset`/`Pullback`).
- `,D=seconds` = for `In`, auto-reset after that long; for `Pullback`, the drift length.
```
{Zoom:In}
{Zoom:In,Cut,D=4}
{Zoom:Pullback,D=3}
{Zoom:Out}
```

### Black cut — `{Black:seconds}`
Hard-cuts a **fullscreen** black plane in (covering the character and all cards), holds for `seconds`, then cuts out. No fade — pure jump cut. Great for dramatic beats / scene breaks. Duration required.
```
{Black:2}
```
> Authoring is unchanged — you still just write `{Black:seconds}`. (Implementation note: the black plane now renders above the character via a high-sorting-order sprite on the recorded camera, so the character no longer shows through it.)

### Image — `{Image:name}` or `{Image:name,seconds}`
Shows an image in the media area. `name` is the file name (extension optional) found in the configured Images/Logos media folders. Duration optional — defaults to **3s**.
```
{Image:privacy_headline,4}
```

### Video — `{Video:name}` or `{Video:name,seconds}`
Plays a video clip from the BRoll media folder. **Narration pauses while the video plays.** Omit the duration to play the whole clip; give one to cap it.
```
{Video:datacenter_broll,6}
```

### Content cards (side panel — character must be Left/Right)

**Headline** — a news headline with its source. Add `,bigCenter` to promote it to a fullscreen centered card.
```
{Headline:"Tech Giant Quietly Changes Privacy Policy","The Verge",5}
{Headline:"40% User Growth After Privacy Backlash","Android Authority",5,bigCenter}
```
Fields: `"headline","source",duration[,bigCenter]`

**Excerpt** — a longer quoted passage with one phrase highlighted, plus attribution. Use for document / terms-of-service / report quotes. The 2nd field must be a phrase that appears in the 1st field.
```
{Excerpt:"Users hereby grant a perpetual, royalty-free license to use submitted content for any purpose, including training AI models.","training AI models","Official Terms of Service",6}
```
Fields: `"full passage","phrase to highlight","attribution",duration`

**Quote** — something a *person* said.
```
{Quote:"We believe transparency is at the core of everything we do.","Sarah Mitchell","VP of Communications, MegaCorp",5}
```
Fields: `"quote","person name","role/title",duration` (note the comma inside `"VP of Communications, MegaCorp"` is fine — it's inside quotes)

**Stat** — a big number with a label and context.
```
{Stat:"2.3 billion","Monthly Active Users","as of Q2 2025",5}
```
Fields: `"value","label","context",duration`

**Logo** — a single company logo. `name` must match a configured logo entry (no commas).
```
{Logo:Google,4}
```

**B‑roll card** — a background video clip mapped by description (no commas in the description).
```
{BRoll:server room,4}
```

### Feature cards (fullscreen, in front of the character)

**BigMedia** — 1 to 4 logos/images shown large and centered. Join multiple with `+`.
```
{BigMedia:Google,4}
{BigMedia:Google+Brave+X,4}
```

**BigText** — 1 to 4 big centered text lines. Join lines with `+`. No commas (use `+`); each `+` starts a new stacked line.
```
{BigText:ANOTHER ONE,3}
{BigText:YOUR DATA+→+THEIR MODEL,6}
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
- **Fullscreen feature cards work anywhere.** `BigText`/`BigMedia`/`BigCenter` (and a `Headline` with `,bigCenter`) render in front of everything, so they appear in any position — including `Center`. Use them for the "front and center" moments.
- **Don't place a tag on the script's very last word and expect it to fire late** — it's fine, the recording now holds until trailing tags (e.g. an end-card `{Logo:...,8}` or final `{Black:2}`) finish their full duration.
- Reasonable default durations: cards 5s, excerpts 6s, big text 3–4s, logos 3–4s, black cuts 2–3s.

---

## 6. Complete worked example

```
## COLD OPEN
{Position:Center,Cut} {Neutral}
[deadpan] A company did a thing. It was bad. Let's talk about it.
{Zoom:In}
[genuine disbelief] No — actually bad. Like, read it twice to make sure bad.

## BREAKDOWN
{Position:Left,Smooth} {Serious}
[slowing down, serious] Here's what the policy change actually says.
{Headline:"Tech Giant Quietly Changes Privacy Policy","The Verge",5}
[dry] Page eleven. Buried under the cookie banner.
{Excerpt:"Users hereby grant a perpetual, royalty-free license to use submitted content for any purpose, including training AI models.","any purpose","Official Terms of Service",6}
[genuine disbelief] Any purpose. They wrote that and published it.
{Stat:"0.3%","Users Who Read Full ToS","Stanford study, 2023",5}
[dry] Point three percent. The rest of us just hit "I Agree."

## TAKE
{Position:Center,Smooth} {Concerned}
{Zoom:In}
[tired but amused] Every few months a company quietly rewrites the rules.
{BigText:YOUR DATA+→+THEIR MODEL,6}
[slowing down, serious] That's the real transaction here.
{Zoom:Reset}
[dry] You're welcome.
```

Notes on the example:
- Starts with `## COLD OPEN` (rule 1).
- The `Headline`, `Excerpt`, and `Stat` cards all appear in the `BREAKDOWN` section while the character is at `Left` (rule 8).
- The narration `…hit "I Agree."` uses quotes freely — that's fine, it's narration, not a tag field.
- `{BigText:YOUR DATA+→+THEIR MODEL,6}` is three stacked lines via `+`.
- No tag has a `T=`; the processor adds those.

---

## 7. Final self-check before returning a script

- [ ] File begins with `## SECTION`.
- [ ] Every `{...}` card / Logo / BRoll / BigMedia / BigText ends in `,number`.
- [ ] No smart quotes anywhere; no `"` inside a quoted field.
- [ ] No commas inside `Logo` / `BRoll` / `BigMedia` / `BigText` names (used `+` for multiples, ≤4).
- [ ] Emotion / Position / Zoom keywords spelled exactly and capitalized.
- [ ] Every content card is preceded by a `Left` or `Right` position.
- [ ] No `T=` written by hand.
- [ ] One tag per line.
