using System;
using System.Collections.Generic;
using System.Reflection;
using MugsTech.Stage;
using MugsTech.Style;
using MugsTech.Tts;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The evidence-first stage, steps 1–2: {Position:Off} (Mugs leaves, the stage
/// goes wide, a Stat alone becomes the big-number beat), the peeks, his
/// screen-time count and the empty-stage fault.
///
/// The Stage_* checks without "Engine" in their name are pure maths and run
/// offline; the *_Engine ones build a card zone or a presenter rig in edit mode
/// and need the editor (menu or batchmode) — no play mode, key or credits.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Stage/Run Stage Self-Tests")]
    public static void RunStage()
    {
        var log = new Log();
        RunStageGroup(log);
        log.Report();
    }

    static void RunStageGroup(Log log)
    {
        Stage_PositionWordsParseAndStamp(log);
        Stage_TravelPlans(log);
        Stage_WhereHeGoes(log);
        Stage_Easing(log);
        Stage_MeterCountsHisScreenTime(log);
        Stage_EmptyStageWarnsOncePerStretch(log);
        Stage_WideStatValueSize(log);
        Stage_PeekPose(log);

        try
        {
            Stage_WideCardZone_Engine(log);
            Stage_WideCardsFitTheirHeight_Engine(log);
            Stage_HeldStatAcrossOffAndOn_Engine(log);
            Stage_OffAndBackOnTheRig_Engine(log);
            Stage_PeekOnTheRig_Engine(log);
        }
        catch (Exception e)
        {
            // TMP and scene objects need the engine — offline harnesses land here.
            log.IsTrue(false, $"stage engine checks threw {e.GetType().Name}: {e.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers shared by the stage / media / device test files
    // -----------------------------------------------------------------------

    /// <summary>
    /// Stamps a one-section script the way a generation does and un-stamps it
    /// again: Unstamp(Stamp(x)) must be x. Returns the stamped (timed) script.
    /// </summary>
    static string StageRoundTrip(Log log, string body, string what)
    {
        string script = "## LAB\n" + body.TrimEnd('\n') + "\n";
        string timed = "";
        foreach (var section in TagGeneration(script, keepCues: false))
        {
            timed = section.TimedScript;
            string raw = TagOnlyRetimer.Unstamp(section.TimedScript, new List<float>());
            string original = TtsScriptProcessor.SplitIntoSegments(script)[0].Raw;
            log.AreEqual(original, raw, what + ": Unstamp(Stamp(x)) gives back the tags as written");
        }
        return timed;
    }

    /// <summary>Every {…} tag of a script body is one the TTS marker list knows (not just caught by the catch-all).</summary>
    static void StageAllRecognised(Log log, string body, string what)
    {
        var (markers, clean) = TtsScriptProcessor.ExtractMarkers(body);
        foreach (var m in markers)
            if (m.Text.StartsWith("{", StringComparison.Ordinal))
                log.IsTrue(TtsScriptProcessor.IsRecognisedMarker(m.Text), $"{what}: {m.Text} is a known marker");
        log.IsTrue(!clean.Contains("{") && !clean.Contains("}"), $"{what}: nothing of a tag is left in the spoken text (\"{clean}\")");
    }

    static StageMath.Frame TestFrame => new StageMath.Frame { Centre = Vector2.zero, HalfHeight = 5f, HalfWidth = 5f * 16f / 9f };

    // -----------------------------------------------------------------------
    // Pure
    // -----------------------------------------------------------------------

    static void Stage_PositionWordsParseAndStamp(Log log)
    {
        var words = new (string w, CharacterPosition p)[]
        {
            ("Left", CharacterPosition.Left), ("Right", CharacterPosition.Right), ("Center", CharacterPosition.Center),
            ("Off", CharacterPosition.Off), ("PeekLeft", CharacterPosition.PeekLeft), ("PeekRight", CharacterPosition.PeekRight),
        };
        foreach (var (w, p) in words)
        {
            log.IsTrue(StageMath.TryParsePosition(w, out CharacterPosition got) && got == p, $"position: \"{w}\" parses");
            log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Position:" + w + "}"), $"position: {{Position:{w}}} is a known marker");
            log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Position:" + w + ",Cut}"), $"position: {{Position:{w},Cut}} is a known marker");
        }
        log.IsTrue(!StageMath.TryParsePosition("Offstage", out _), "position: an unknown word is refused (the parser keeps its Center fallback)");

        // The three old values keep their numbers (the enum is serialised by number in places).
        log.AreEqual(0, (int)CharacterPosition.Left, "position: Left is still 0");
        log.AreEqual(1, (int)CharacterPosition.Right, "position: Right is still 1");
        log.AreEqual(2, (int)CharacterPosition.Center, "position: Center is still 2");

        log.IsTrue(StageMath.IsWide(CharacterPosition.Off) && StageMath.IsWide(CharacterPosition.PeekLeft) &&
                   StageMath.IsWide(CharacterPosition.PeekRight), "position: Off and both peeks make the stage wide");
        log.IsTrue(!StageMath.IsWide(CharacterPosition.Left) && !StageMath.IsWide(CharacterPosition.Right) &&
                   !StageMath.IsWide(CharacterPosition.Center), "position: Left / Right / Center don't");

        string body =
            "{Position:Off,Cut}\n" +
            "The number is the story.\n" +
            "{Position:PeekRight}\n" +
            "Of course it is.\n" +
            "{Position:Off}\n" +
            "And then some more.\n" +
            "{Position:Center}\n" +
            "Back to me.";
        StageAllRecognised(log, body, "position");
        string timed = StageRoundTrip(log, body, "position");
        log.IsTrue(timed.Contains("{Position:Off,Cut,T=") && timed.Contains("{Position:PeekRight,T=") &&
                   timed.Contains("{Position:Off,T="), "position: Off and the peeks are stamped with a T= like any position");

        // Never an emotion: the avatar's regex must not take a stamped position.
        foreach (string tag in new[] { "{Position:Off,T=1.200}", "{Position:PeekLeft,T=1.200}", "{Position:Off,Cut,T=1.200}" })
            log.IsTrue(!HybridAvatarSystem.EmotionTagRegex.IsMatch(tag), $"position: {tag} is not an emotion");
    }

    static void Stage_TravelPlans(Log log)
    {
        var full = new[] { CharacterPosition.Left, CharacterPosition.Right, CharacterPosition.Center };

        foreach (CharacterPosition p in full)
        {
            List<StageLeg> off = StageMath.PlanTravel(p, CharacterPosition.Off);
            log.IsTrue(off.Count == 2 && off[0] == StageLeg.SlideOut && off[1] == StageLeg.Park,
                $"plan: {p} → Off slides out, then parks him out of reach");
            log.Near(0.60f, StageMath.TravelSeconds(p, CharacterPosition.Off), 0.001f, $"plan: {p} → Off takes 0.60 s");

            List<StageLeg> back = StageMath.PlanTravel(CharacterPosition.Off, p);
            log.IsTrue(back.Count == 1 && back[0] == StageLeg.PopUp, $"plan: Off → {p} pops up from below");
            log.Near(0.65f, StageMath.TravelSeconds(CharacterPosition.Off, p), 0.001f, $"plan: Off → {p} takes 0.65 s");

            log.IsTrue(!StageMath.CardsWaitFor(p, CharacterPosition.Off), $"plan: nothing waits for him to leave from {p}");
            log.IsTrue(StageMath.CardsWaitFor(CharacterPosition.Off, p), $"plan: a side card waits for him to land at {p}");
            log.IsTrue(StageMath.MoveEndsMedia(p, CharacterPosition.Off) && StageMath.MoveEndsMedia(CharacterPosition.Off, p),
                $"plan: the layout changing ends the media that is up ({p} ↔ Off)");

            foreach (CharacterPosition peek in new[] { CharacterPosition.PeekLeft, CharacterPosition.PeekRight })
            {
                // A peek and a full position are always out, then in — on its own
                // side too (Tier 5 tried one leg; the creator kept the drop and pop-up).
                List<StageLeg> toPeek = StageMath.PlanTravel(p, peek);
                List<StageLeg> fromPeek = StageMath.PlanTravel(peek, p);
                log.IsTrue(toPeek.Count == 2 && toPeek[0] == StageLeg.SlideOut && toPeek[1] == StageLeg.RiseToPeek,
                    $"plan: {p} → {peek} slides him out, then brings the peek up");
                log.IsTrue(fromPeek.Count == 2 && fromPeek[0] == StageLeg.DropFromPeek && fromPeek[1] == StageLeg.PopUp,
                    $"plan: {peek} → {p} drops, then pops up full size");
            }
        }

        log.AreEqual(0, StageMath.PlanTravel(CharacterPosition.Off, CharacterPosition.Off).Count,
            "plan: {Position:Off} while already off is a no-op");
        log.AreEqual(0, StageMath.PlanTravel(CharacterPosition.Left, CharacterPosition.Right).Count,
            "plan: Left → Right is not a stage move (the old glide runs)");

        List<StageLeg> rise = StageMath.PlanTravel(CharacterPosition.Off, CharacterPosition.PeekLeft);
        log.IsTrue(rise.Count == 1 && rise[0] == StageLeg.RiseToPeek, "plan: Off → PeekLeft is one rise");
        log.Near(0.40f, StageMath.TravelSeconds(CharacterPosition.Off, CharacterPosition.PeekLeft), 0.001f, "plan: …of 0.40 s");
        List<StageLeg> drop = StageMath.PlanTravel(CharacterPosition.PeekRight, CharacterPosition.Off);
        log.IsTrue(drop.Count == 2 && drop[0] == StageLeg.DropFromPeek && drop[1] == StageLeg.Park, "plan: PeekRight → Off drops and parks");
        log.Near(0.35f, StageMath.TravelSeconds(CharacterPosition.PeekRight, CharacterPosition.Off), 0.001f, "plan: …in 0.35 s");

        log.IsTrue(!StageMath.MoveEndsMedia(CharacterPosition.Off, CharacterPosition.PeekLeft) &&
                   !StageMath.MoveEndsMedia(CharacterPosition.PeekRight, CharacterPosition.Off),
            "plan: Off ↔ peek keeps the stage wide, so the full-frame media stays");
        log.IsTrue(!StageMath.CardsWaitFor(CharacterPosition.Off, CharacterPosition.PeekLeft), "plan: nothing waits for a peek");
    }

    static void Stage_WhereHeGoes(Log log)
    {
        StageMath.Frame frame = TestFrame;
        StageMath.Body body = StageMath.DefaultBody;
        float halfW = body.Size.x * 0.5f, halfH = body.Size.y * 0.5f;

        Vector2 left = new Vector2(-7.33f, -3.138f), right = new Vector2(6.92f, -3.138f), centre = new Vector2(0f, -3.138f);

        Vector2 offL = StageMath.SlideOutTarget(CharacterPosition.Left, left, body, frame);
        log.IsTrue(offL.x + body.CentreOffset.x + halfW < frame.Left - 0.5f && Mathf.Approximately(offL.y, left.y),
            $"where: from Left he ends wholly past the left edge, same height (x {offL.x:F2})");
        Vector2 offR = StageMath.SlideOutTarget(CharacterPosition.Right, right, body, frame);
        log.IsTrue(offR.x + body.CentreOffset.x - halfW > frame.Right + 0.5f && Mathf.Approximately(offR.y, right.y),
            $"where: from Right, past the right edge (x {offR.x:F2})");
        Vector2 offC = StageMath.SlideOutTarget(CharacterPosition.Center, centre, body, frame);
        log.IsTrue(offC.y + body.CentreOffset.y + halfH < frame.Bottom - 0.5f && Mathf.Approximately(offC.x, centre.x),
            $"where: from Center, straight down below the frame (y {offC.y:F2})");

        Vector2 parked = StageMath.Park(CharacterPosition.Left, offL);
        log.IsTrue(parked.x < offL.x - 20f, "where: once out he is parked far out of reach (a pullback can't bring him back)");

        foreach (Vector2 rest in new[] { left, right, centre })
        {
            Vector2 below = StageMath.BelowFrame(rest, body, frame);
            log.IsTrue(Mathf.Approximately(below.x, rest.x) && below.y + body.CentreOffset.y + halfH < frame.Bottom - 0.5f,
                $"where: a pop-up at x {rest.x:F2} starts wholly below the frame");
        }
    }

    static void Stage_Easing(Log log)
    {
        // His own moves are poses now (StageMoves; the Tier5_* checks in
        // TtsPipelineSelfTests.Motion.cs). What is left here is the card's glide.
        log.Near(0.5f, StageMath.GlideEase(0.5f), 1e-4f, "ease: a card's glide is symmetric");
        log.IsTrue(StageMath.GlideEase(0.1f) < 0.05f && StageMath.GlideEase(0.9f) > 0.95f, "ease: …eased in and out");
    }

    static void Stage_MeterCountsHisScreenTime(Log log)
    {
        var m = new StageMeter();
        void Feed(float seconds, MugsOnScreen state)
        {
            const float dt = 1f / 30f;
            for (float t = 0f; t < seconds - 1e-4f; t += dt) m.Add(dt, state);
        }

        Feed(10f, MugsOnScreen.Full);
        Feed(5f, MugsOnScreen.Off);
        Feed(3f, MugsOnScreen.Full);
        Feed(2f, MugsOnScreen.Peek);
        Feed(1f, MugsOnScreen.Off);
        Feed(3f, MugsOnScreen.Covered);
        Feed(1f, MugsOnScreen.Full);

        log.Near(25f, m.Total, 0.05f, "meter: every second of the voice is counted once");
        log.Near(14f / 25f, m.OnScreenShare, 0.005f, "meter: on screen = at a full position, in view");
        log.Near(2f / 25f, m.PeekShare, 0.005f, "meter: a peek is counted on its own");
        log.Near(3f, m.CoveredSeconds, 0.05f, "meter: time behind a full-frame cover isn't screen time");
        log.AreEqual(2, m.OffCount, "meter: he went off twice (peek → Off counts, Off → Off doesn't)");
        log.AreEqual(1, m.PeekCount, "meter: one peek");

        string line = m.Line();
        log.IsTrue(line.StartsWith("[Stage] Mugs on screen 56.0 % of 25.0 s (off 2 times", StringComparison.Ordinal),
            $"meter: the take-end line reads as the brief gives it (\"{line}\")");
        log.IsTrue(line.Contains("peek 8.0 %"), "meter: …with the peek share when there was one");

        var plain = new StageMeter();
        plain.Add(41.2f, MugsOnScreen.Full);
        plain.Add(58.8f, MugsOnScreen.Off);
        log.AreEqual("[Stage] Mugs on screen 41.2 % of 100.0 s (off 1 time)", plain.Line(), "meter: the plain form, singular");

        var opening = new StageMeter();
        opening.Add(0f, MugsOnScreen.Off);
        opening.Add(4f, MugsOnScreen.Off);
        opening.Add(6f, MugsOnScreen.Full);
        log.AreEqual(1, opening.OffCount, "meter: a take that opens without him counts as off once");
        log.Near(0.6f, opening.OnScreenShare, 1e-4f, "meter: …and his share starts from his entrance");

        log.Near(0f, new StageMeter().OnScreenShare, 1e-6f, "meter: an empty take divides by nothing");
    }

    static void Stage_EmptyStageWarnsOncePerStretch(Log log)
    {
        const float dt = 0.1f;
        var w = new EmptyStageWatch();
        var warnings = new List<string>();
        float t = 0f;
        void Run(float seconds, bool empty)
        {
            for (float e = 0f; e < seconds - 1e-4f; e += dt)
            {
                string s = w.Step(t, dt, empty);
                if (s != null) warnings.Add(s);
                t += dt;
            }
        }

        Run(2f, false);
        Run(2.9f, true);
        Run(1f, false);
        log.AreEqual(0, warnings.Count, "empty stage: 2.9 s with nothing on screen is not a fault");

        Run(4.2f, true);     // starts at t = 5.9
        log.AreEqual(0, warnings.Count, "empty stage: nothing is said while the stretch is still running");
        Run(1f, false);
        log.AreEqual(1, warnings.Count, "empty stage: over 3 s gives ONE warning, when the stretch ends");
        log.IsTrue(warnings.Count == 1 && warnings[0].Contains("t=5.90s") && warnings[0].Contains("4.2 s"),
            $"empty stage: …with its time and length (\"{(warnings.Count > 0 ? warnings[0] : "")}\")");

        Run(3.5f, true);
        string last = w.Finish();
        log.IsTrue(last != null && last.Contains("3.5 s"), "empty stage: a stretch still open when the take ends is reported then");
        log.AreEqual(2, w.Warnings, "empty stage: two faults counted");
        log.Near(4.2f, w.LongestSeconds, 0.05f, "empty stage: the longest is kept for the result JSON");
        log.IsTrue(w.Finish() == null, "empty stage: closing twice says nothing more");
    }

    static void Stage_WideStatValueSize(Log log)
    {
        // The wide slot at 1080p: 1440 px wide, so k = 1.44 and the inner width is 86 % of it.
        const float frameH = 1080f, k = 1.44f, capRatio = 0.727f;
        float innerW = 1440f * (1f - 2f * CardTypography.PaddingFraction);
        float ceiling = StageMath.WideStatCapCeiling(frameH);

        // Width of the value at size 100, roughly 62 px a character in the heavy face.
        float two = CardTypography.StatValueSize(2 * 62f, capRatio, innerW, false, k, ceiling);
        float nine = CardTypography.StatValueSize(9 * 62f, capRatio, innerW, false, k, ceiling);
        float twoCap = two * capRatio, nineCap = nine * capRatio;

        log.IsTrue(twoCap <= ceiling + 0.5f, $"wide Stat: a 2-character value's capitals stop at 27 % of the frame ({twoCap:F0} px)");
        log.IsTrue(twoCap >= 0.20f * frameH, $"wide Stat: …and are about a quarter of its height ({twoCap / frameH:P0})");
        log.IsTrue(nine * 9 * 62f / 100f <= innerW * CardTypography.StatValueWidthLimit + 0.5f,
            "wide Stat: a 9-character value still fits the card's width");
        log.IsTrue(nineCap <= twoCap + 0.5f && nine >= CardTypography.StatLabelPx * k * 1.8f,
            $"wide Stat: …with capitals of {nineCap / frameH:P0} of the frame — still much the largest thing on the card");

        // Without the ceiling (a side card) nothing changes.
        log.Near(CardTypography.StatValueSize(2 * 62f, capRatio, 860f, false, 1f),
                 CardTypography.StatValueSize(2 * 62f, capRatio, 860f, false, 1f, -1f), 1e-3f,
            "wide Stat: the side card's value size is what it was");
    }

    static void Stage_PeekPose(Log log)
    {
        StageMath.Frame frame = TestFrame;
        StageMath.Body body = StageMath.DefaultBody;

        foreach (bool right in new[] { false, true })
        {
            StageMath.PeekPose p = StageMath.Peek(right, body, frame);
            string side = right ? "PeekRight" : "PeekLeft";
            log.IsTrue(p.Scale <= 1f && p.Scale > 0.5f, $"peek: {side} scales him down at most a little ({p.Scale:F2})");
            log.IsTrue(p.VisibleWidth <= StageMath.PeekMaxWidthOfFrame * frame.Width + 1e-3f,
                $"peek: {side} takes at most 22 % of the frame's width ({p.VisibleWidth / frame.Width:P0})");
            log.IsTrue(p.VisibleHeight <= StageMath.PeekMaxHeightOfFrame * frame.Height + 1e-3f,
                $"peek: {side} takes at most 35 % of its height ({p.VisibleHeight / frame.Height:P0})");

            float centreX = p.Rest.x + body.CentreOffset.x * p.Scale;
            float top = p.Rest.y + (body.CentreOffset.y + body.Size.y * 0.5f) * p.Scale;
            float bottom = p.Rest.y + (body.CentreOffset.y - body.Size.y * 0.5f) * p.Scale;
            log.IsTrue(right ? centreX > 0f : centreX < 0f, $"peek: {side} is in its own lower corner");
            log.IsTrue(centreX - p.VisibleWidth * 0.5f >= frame.Left - 1e-3f && centreX + p.VisibleWidth * 0.5f <= frame.Right + 1e-3f,
                $"peek: {side} is inside the frame sideways");
            log.Near(frame.Bottom + p.VisibleHeight, top, 1e-3f, $"peek: {side}'s head is that far above the bottom edge");
            log.IsTrue(bottom < frame.Bottom, $"peek: {side}'s body stays below the edge");

            float hiddenTop = p.Hidden.y + (body.CentreOffset.y + body.Size.y * 0.5f) * p.Scale;
            log.IsTrue(hiddenTop < frame.Bottom - 0.5f && Mathf.Approximately(p.Hidden.x, p.Rest.x),
                $"peek: {side} rises from wholly below the frame");
        }

        log.IsTrue(StageMath.PeekSortingOrder > 30000 && StageMath.PeekSortingOrder < 31000,
            "peek: he is drawn in front of the cards (30000) and behind the full-screen cards (31000)");
    }

    // -----------------------------------------------------------------------
    // Engine: the card zone
    // -----------------------------------------------------------------------

    // A 1920×1080 frame with the side slot where the recording scene has it
    // (1000×800, 200 px from the left edge), and a controller on it.
    static ContentZoneController BuildStageZone(out GameObject root, out RectTransform sideZone)
    {
        root = new GameObject("StageSelfTest", typeof(RectTransform), typeof(Canvas));
        var frame = new GameObject("Frame", typeof(RectTransform)).GetComponent<RectTransform>();
        frame.SetParent(root.transform, false);
        frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
        frame.sizeDelta = new Vector2(1920f, 1080f);

        sideZone = new GameObject("Zone", typeof(RectTransform)).GetComponent<RectTransform>();
        sideZone.SetParent(frame, false);
        sideZone.anchorMin = sideZone.anchorMax = new Vector2(0f, 0.5f);
        sideZone.pivot = new Vector2(0f, 0.5f);
        sideZone.anchoredPosition = new Vector2(200f, 0f);
        sideZone.sizeDelta = new Vector2(CardTypography.ReferenceCardWidth, CardTypography.ReferenceCardHeight);

        var czGo = new GameObject("CZ");
        czGo.transform.SetParent(root.transform, false);
        var cz = czGo.AddComponent<ContentZoneController>();
        cz.contentZone = sideZone;
        return cz;
    }

    static ContentCardEvent StatEvent(string value, float t, EntryDirection? side = null, float duration = 0f) => new ContentCardEvent
    {
        cardType = ContentCardType.Stat, primaryText = value, secondaryText = "Sign-up target per employee",
        tertiaryText = "some US Apple Stores · MacRumors", triggerTime = t, duration = duration, entryDirectionOverride = side,
    };

    static void Stage_WideCardZone_Engine(Log log)
    {
        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        ContentZoneController cz = BuildStageZone(out GameObject root, out RectTransform sideZone);
        try
        {
            foreach (string value in new[] { "40", "$31.6B+ a" })   // 2 and 9 characters
            {
                var evt = StatEvent(value, 1f, EntryDirection.FromRight);   // its ,Right is accepted and ignored
                cz.SetTimeline(new List<ContentCardEvent> { evt }, null);
                cz.SetStageWide(true, instant: true);
                cz.ShowCard(evt);

                var stat = cz.ActiveCard as StatCard;
                log.IsTrue(stat != null, $"wide zone: a Stat (\"{value}\") shows while Mugs is off");
                if (stat == null) continue;
                var rt = (RectTransform)stat.transform;
                log.AreEqual("ContentZone_Cards_Wide", rt.parent.name, $"wide zone: \"{value}\" sits in the wide slot, its ,Right ignored");
                log.Near(1440f, rt.rect.width, 2f, "wide zone: the slot is 75 % of the frame's width");
                log.Near(842.4f, rt.rect.height, 2f, "wide zone: …and 78 % of its height (clear of the top strip)");
                log.IsTrue(stat.IsWideLayout, "wide zone: the card knows it is wide");

                CheckCard(log, stat, stat.Layout, $"wide Stat \"{value}\"");
                float cap = stat.ValueCapPx;
                log.IsTrue(cap <= StageMath.WideStatCapCeiling(1080f) + 1f,
                    $"wide Stat \"{value}\": capitals {cap:F0} px — at most 27 % of the frame");
                if (value.Length <= 2)
                    log.IsTrue(cap >= 0.20f * 1080f, $"wide Stat \"{value}\": capitals about a quarter of the frame ({cap / 1080f:P0})");
                foreach (TMP_Text text in stat.GetComponentsInChildren<TMP_Text>())
                    if (text.gameObject.activeInHierarchy && text.name != "NumberText" && !string.IsNullOrEmpty(text.text))
                        log.IsTrue(text.fontSize < cap / 0.7f, $"wide Stat \"{value}\": {text.name} is smaller than the value");

                cz.ClearForTransition();
                cz.SetStageWide(false, instant: true);
            }
        }
        finally
        {
            VisualsRuntimeApplier.CardFontOverride = savedFont;
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void Stage_HeldStatAcrossOffAndOn_Engine(Log log)
    {
        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        ContentZoneController cz = BuildStageZone(out GameObject root, out RectTransform sideZone);
        try
        {
            var evt = StatEvent("40%", 1f, EntryDirection.FromRight);
            cz.SetTimeline(new List<ContentCardEvent> { evt }, null);
            cz.ShowCard(evt);
            var stat = cz.ActiveCard as StatCard;
            log.IsTrue(stat != null, "held Stat: opens with Mugs on screen");
            if (stat == null) return;
            var rt = (RectTransform)stat.transform;
            log.AreEqual("ContentZone_Cards_Right", rt.parent.name, "held Stat: on its own side (,Right)");
            log.Near(1000f, rt.rect.width, 1f, "held Stat: 1000 px wide in the side slot");
            InvokeLateUpdate(stat);
            float sideCap = stat.ValueCapPx;

            // He leaves: the same card, now centred and wide.
            cz.SetStageWide(true, instant: true);
            InvokeLateUpdate(stat);
            log.IsTrue(cz.ActiveCard == stat, "held Stat: it is the SAME card after he leaves (not closed and reopened)");
            log.AreEqual("ContentZone_Cards_Wide", rt.parent.name, "held Stat: it moved to the wide slot");
            log.Near(1440f, rt.rect.width, 2f, "held Stat: and grew to it");
            log.IsTrue(stat.IsWideLayout, "held Stat: laid out wide");
            log.IsTrue(stat.ValueCapPx > sideCap + 10f, $"held Stat: the value grew with it ({sideCap:F0} → {stat.ValueCapPx:F0} px)");
            CheckCard(log, stat, stat.Layout, "held Stat, wide");

            // He comes back: it returns to the side it was written on.
            cz.SetStageWide(false, instant: true);
            InvokeLateUpdate(stat);
            log.IsTrue(cz.ActiveCard == stat, "held Stat: still the same card when he returns");
            log.AreEqual("ContentZone_Cards_Right", rt.parent.name, "held Stat: back on its side");
            log.Near(1000f, rt.rect.width, 1f, "held Stat: back to 1000 px");
            log.Near(sideCap, stat.ValueCapPx, 1.5f, "held Stat: and to the size it had");
            log.IsTrue(!stat.IsWideLayout, "held Stat: laid out as a side card again");

            // A full-screen card is never moved by the stage.
            cz.ClearForTransition();
            var versus = new ContentCardEvent
            {
                cardType = ContentCardType.Versus, triggerTime = 2f, duration = 0f,
                fields = new[] { "They say", "No key leaked", "The record says", "Eleven keys signed" },
                primaryText = "No key leaked", secondaryText = "Eleven keys signed",
            };
            cz.ShowCard(versus);
            Transform before = cz.ActiveCard != null ? cz.ActiveCard.transform.parent : null;
            cz.SetStageWide(true, instant: true);
            log.IsTrue(cz.ActiveCard != null && cz.ActiveCard.transform.parent == before, "wide zone: a full-screen card stays where it is");
        }
        finally
        {
            VisualsRuntimeApplier.CardFontOverride = savedFont;
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void InvokeLateUpdate(Component c)
    {
        MethodInfo m = c.GetType().GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (m != null) m.Invoke(c, null);
    }

    // -----------------------------------------------------------------------
    // Engine: the presenter rig (cuts only — travel is a coroutine, and its
    // plan and geometry are checked above)
    // -----------------------------------------------------------------------

    sealed class StageRig
    {
        public GameObject Root;
        public MediaPresentationSystem Mps;
        public Transform Presenter, Left, Center, Right;
        public Camera Camera;

        static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public object Call(string method, params object[] args)
        {
            MethodInfo m = typeof(MediaPresentationSystem).GetMethod(method, Any);
            if (m == null) throw new MissingMethodException("MediaPresentationSystem." + method);
            return m.Invoke(Mps, args);
        }

        public void Set(string field, object value)
        {
            FieldInfo f = typeof(MediaPresentationSystem).GetField(field, Any);
            if (f == null) throw new MissingFieldException("MediaPresentationSystem." + field);
            f.SetValue(Mps, value);
        }

        public T Get<T>(string field)
        {
            FieldInfo f = typeof(MediaPresentationSystem).GetField(field, Any);
            if (f == null) throw new MissingFieldException("MediaPresentationSystem." + field);
            return (T)f.GetValue(Mps);
        }
    }

    static StageRig BuildStageRig()
    {
        var rig = new StageRig { Root = new GameObject("StageRigSelfTest") };
        Transform T(string name, float x, float y)
        {
            var t = new GameObject(name).transform;
            t.SetParent(rig.Root.transform, false);
            t.position = new Vector3(x, y, 0f);
            return t;
        }
        rig.Left = T("LeftPosition", -7.33f, -3.138f);
        rig.Center = T("CenterPosition", 0f, -3.138f);
        rig.Right = T("RightPosition", 6.92f, -3.138f);
        rig.Presenter = T("Presentator", 0f, -3.138f);

        var camGo = new GameObject("Camera");
        camGo.transform.SetParent(rig.Root.transform, false);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        rig.Camera = camGo.AddComponent<Camera>();
        rig.Camera.orthographic = true;
        rig.Camera.orthographicSize = 5f;
        rig.Camera.aspect = 16f / 9f;

        var mpsGo = new GameObject("MPS");
        mpsGo.transform.SetParent(rig.Root.transform, false);
        rig.Mps = mpsGo.AddComponent<MediaPresentationSystem>();
        rig.Mps.avatarParent = rig.Presenter;
        rig.Mps.leftLocation = rig.Left;
        rig.Mps.centerLocation = rig.Center;
        rig.Mps.rightLocation = rig.Right;
        rig.Mps.mainCamera = rig.Camera;
        rig.Set("defaultCameraSize", 5f);
        rig.Set("currentPosition", CharacterPosition.Center);
        return rig;
    }

    static bool OutsideFrame(Vector3 presenter)
    {
        // The default body: centre 2.63 above the mark, 4.6 × 6.1.
        StageMath.Frame f = TestFrame;
        StageMath.Body b = StageMath.DefaultBody;
        float cx = presenter.x + b.CentreOffset.x, cy = presenter.y + b.CentreOffset.y;
        return cx + b.Size.x * 0.5f < f.Left || cx - b.Size.x * 0.5f > f.Right ||
               cy + b.Size.y * 0.5f < f.Bottom || cy - b.Size.y * 0.5f > f.Top;
    }

    static void Stage_OffAndBackOnTheRig_Engine(Log log)
    {
        var warnings = new List<string>();
        void OnLog(string msg, string stack, LogType type) { if (type == LogType.Warning) warnings.Add(msg); }
        Application.logMessageReceived += OnLog;
        StageRig rig = BuildStageRig();
        try
        {
            // Off and back from each of the three positions, on a cut.
            foreach (CharacterPosition p in new[] { CharacterPosition.Left, CharacterPosition.Right, CharacterPosition.Center })
            {
                rig.Call("MoveToPosition", p, (bool?)true);
                Vector3 rest = rig.Presenter.position;
                rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);
                log.IsTrue(rig.Mps.IsPresenterOff && rig.Mps.IsStageWide, $"rig: {{Position:Off,Cut}} from {p} — he is off, the stage is wide");
                log.IsTrue(OutsideFrame(rig.Presenter.position), $"rig: …and out of frame on that frame (at {rig.Presenter.position.x:F1}, {rig.Presenter.position.y:F1})");
                log.AreEqual(CharacterPosition.Off, rig.Mps.CurrentPosition, "rig: CurrentPosition reads Off");

                // Off while already off: nothing happens.
                Vector3 parked = rig.Presenter.position;
                rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)null);
                log.IsTrue(rig.Presenter.position == parked && rig.Mps.IsPresenterOff, "rig: {Position:Off} while already off is a no-op");

                rig.Call("MoveToPosition", p, (bool?)true);
                log.IsTrue(!rig.Mps.IsStageWide && rig.Mps.CurrentPosition == p, $"rig: {{Position:{p},Cut}} brings him back");
                log.IsTrue((rig.Presenter.position - rest).magnitude < 1e-3f, $"rig: …to exactly his {p} mark");
            }

            // A zoom while he is off: ignored, one warning each; the ExtremeOut after it is quiet.
            rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);
            float size = rig.Camera.orthographicSize;
            Vector3 camPos = rig.Camera.transform.position;
            warnings.Clear();
            rig.Call("ApplyZoom", ZoomType.ExtremeIn, false, 0f);
            log.IsTrue(Mathf.Approximately(rig.Camera.orthographicSize, size) && rig.Camera.transform.position == camPos,
                "rig: {Zoom:ExtremeIn} while off leaves the camera alone");
            log.IsTrue(!rig.Mps.IsCloseUpHeld, "rig: …no close-up is held");
            log.AreEqual(1, warnings.FindAll(w => w.Contains("{Zoom:ExtremeIn} ignored")).Count, "rig: …with one warning");
            warnings.Clear();
            rig.Call("ApplyZoom", ZoomType.ExtremeOut, false, 0f);
            log.AreEqual(0, warnings.Count, "rig: the matching {Zoom:ExtremeOut} is a quiet no-op");
            log.IsTrue(Mathf.Approximately(rig.Camera.orthographicSize, size), "rig: …and changes nothing");
            rig.Call("ApplyZoom", ZoomType.In, false, 0f);
            rig.Call("ApplyZoom", ZoomType.Pullback, false, 0f);
            log.IsTrue(Mathf.Approximately(rig.Camera.orthographicSize, size) && rig.Mps.ZoomStep == 0,
                "rig: {Zoom:In} and {Zoom:Pullback} are ignored too");
            log.AreEqual(2, warnings.FindAll(w => w.Contains("ignored")).Count, "rig: …one warning each");

            // Off on a transition line and back on the next one: positions on a
            // transition line are claimed by it and applied as a snap.
            object parsed = rig.Call("ParseTransitionMarkers",
                "{Transition:Wipe,T=10.000} {Position:Off,T=10.000} {Neutral,T=10.000}\nWords.\n" +
                "{Transition:Iris,T=20.000} {Position:Right,T=20.000}\nMore words.", 30f);
            var markers = (List<TransitionMarkerData>)parsed.GetType().GetField("Item2").GetValue(parsed);
            log.IsTrue(markers.Count == 2 && markers[0].hasPosition && markers[0].position == CharacterPosition.Off,
                "rig: a {Position:Off} on a transition line is claimed by the transition");
            log.IsTrue(markers.Count == 2 && markers[1].hasPosition && markers[1].position == CharacterPosition.Right,
                "rig: …and so is the position that brings him back on the next one");
            rig.Call("MoveToPosition", CharacterPosition.Center, (bool?)true);
            rig.Call("MoveToPosition", markers[0].position, (bool?)true);   // what ApplyTransitionCover does
            log.IsTrue(rig.Mps.IsPresenterOff && OutsideFrame(rig.Presenter.position), "rig: under the cover he is simply gone");
            rig.Call("MoveToPosition", markers[1].position, (bool?)true);
            log.IsTrue((rig.Presenter.position - rig.Right.position).magnitude < 1e-3f && !rig.Mps.IsPresenterMoving,
                "rig: the next transition reveals him already in place (no pop-up)");

            // A script whose first position is Off: gone before the take starts.
            rig.Call("MoveToPosition", CharacterPosition.Center, (bool?)true);
            rig.Set("positionMarkers", new List<PositionMarkerData>
            {
                new PositionMarkerData { triggerTime = 0.12f, position = CharacterPosition.Off, hardCutOverride = true },
                new PositionMarkerData { triggerTime = 14.0f, position = CharacterPosition.Center },
            });
            rig.Set("transitionMarkers", new List<TransitionMarkerData>());
            rig.Call("BeginStageRun");
            log.IsTrue(rig.Mps.IsPresenterOff && OutsideFrame(rig.Presenter.position),
                "rig: a script whose first {Position:} is Off opens without him — before the first frame");
            log.IsTrue(rig.Get<bool>("openedWithoutPresenter"), "rig: …and Start() won't put him back at Center");

            // …but not when a full position comes first.
            rig.Call("MoveToPosition", CharacterPosition.Center, (bool?)true);
            rig.Set("openedWithoutPresenter", false);
            rig.Set("positionMarkers", new List<PositionMarkerData>
            {
                new PositionMarkerData { triggerTime = 0.12f, position = CharacterPosition.Left, hardCutOverride = true },
                new PositionMarkerData { triggerTime = 9.0f, position = CharacterPosition.Off },
            });
            rig.Call("BeginStageRun");
            log.IsTrue(!rig.Mps.IsPresenterOff && !rig.Get<bool>("openedWithoutPresenter"),
                "rig: a script that opens on Left / Right / Center starts with him there, as before");
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            UnityEngine.Object.DestroyImmediate(rig.Root);
        }
    }

    static void Stage_PeekOnTheRig_Engine(Log log)
    {
        StageRig rig = BuildStageRig();
        try
        {
            StageMath.Frame frame = TestFrame;
            StageMath.Body body = StageMath.DefaultBody;

            foreach (CharacterPosition peek in new[] { CharacterPosition.PeekLeft, CharacterPosition.PeekRight })
            {
                // From Off and from each full position, and on to Off and to a full position.
                foreach (CharacterPosition from in new[] { CharacterPosition.Off, CharacterPosition.Left, CharacterPosition.Right, CharacterPosition.Center })
                {
                    rig.Call("MoveToPosition", from, (bool?)true);
                    rig.Call("MoveToPosition", peek, (bool?)true);
                    StageMath.PeekPose pose = StageMath.Peek(peek == CharacterPosition.PeekRight, body, frame);
                    log.IsTrue(rig.Mps.IsPresenterPeeking && rig.Mps.IsStageWide, $"rig: {peek} from {from} — peeking, the stage stays wide");
                    log.IsTrue(((Vector2)rig.Presenter.position - pose.Rest).magnitude < 1e-3f, $"rig: …in the peek pose ({peek} from {from})");
                    log.Near(pose.Scale, rig.Presenter.localScale.x, 1e-3f, $"rig: …at the peek's scale ({peek} from {from})");
                }

                rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);
                log.IsTrue(rig.Mps.IsPresenterOff && OutsideFrame(rig.Presenter.position), $"rig: {peek} → Off drops him out of frame");
                log.Near(1f, rig.Presenter.localScale.x, 1e-4f, $"rig: …at full scale again ({peek} → Off)");

                rig.Call("MoveToPosition", peek, (bool?)true);
                rig.Call("MoveToPosition", CharacterPosition.Left, (bool?)true);
                log.IsTrue(!rig.Mps.IsStageWide && (rig.Presenter.position - rig.Left.position).magnitude < 1e-3f,
                    $"rig: {peek} → Left puts him at his mark");
                log.Near(1f, rig.Presenter.localScale.x, 1e-4f, $"rig: …full size ({peek} → Left)");
            }

            // Zooms are ignored during a peek, as while he is off.
            rig.Call("MoveToPosition", CharacterPosition.PeekLeft, (bool?)true);
            float size = rig.Camera.orthographicSize;
            rig.Call("ApplyZoom", ZoomType.ExtremeIn, false, 0f);
            rig.Call("ApplyZoom", ZoomType.In, false, 0f);
            log.IsTrue(Mathf.Approximately(rig.Camera.orthographicSize, size) && !rig.Mps.IsCloseUpHeld,
                "rig: zooms and the close-up are ignored during a peek");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig.Root);
        }
    }

    // The wide slot is wider than the side card but hardly taller. On the first
    // real take (2026-11-16) a wide Compare laid out at the full width's scale
    // ran 115 px past its inner height and its source line fell off the card.
    static void Stage_WideCardsFitTheirHeight_Engine(Log log)
    {
        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        const float sideScaleFloor = 842.4f / 800f / 1.44f;   // the scale at which the side card itself fits
        try
        {
            T Wide<T>(out GameObject root, ContentCardEvent evt) where T : ContentCard
            {
                T card = BuildCard<T>(out root);
                ((RectTransform)card.transform.parent).sizeDelta = new Vector2(1440f, 842.4f);
                card.SetStageLayout(true, 1080f);
                card.Initialize(evt, null);
                return card;
            }

            // The Compare of that take.
            string[] fields = { "40%", "MacRumors, some US stores", "60%", "one employee's store", "MacRumors · Louis Rossmann" };
            CompareCard compare = Wide<CompareCard>(out GameObject r1,
                new ContentCardEvent { cardType = ContentCardType.Compare, fields = fields, primaryText = fields[0] });
            try
            {
                CardTypography.Report l = compare.Layout;
                log.IsTrue(l.GroupHeight <= l.InnerHeight + 1f, $"wide Compare: rows and source fit the slot's height (group {l.GroupHeight:F0} of {l.InnerHeight:F0} px)");
                log.IsTrue(compare.WideFit < 0.999f, $"wide Compare: …at a smaller scale than the full width's ({compare.WideFit:F2})");
                log.IsTrue(compare.WideFit >= sideScaleFloor - 1e-3f, "wide Compare: …never smaller than the side card's own");
                compare.StartGrowthForTest();
                compare.Advance(2f);   // bars fully grown — the values ride their bars' ends
                CheckCard(log, compare, l, "wide Compare");
            }
            finally { UnityEngine.Object.DestroyImmediate(r1); }

            // A sparse card keeps the full width's scale.
            StatCard stat = Wide<StatCard>(out GameObject r2, StatEvent("40%", 1f));
            try { log.Near(1f, stat.WideFit, 1e-4f, "wide Stat: nothing to shrink — full scale"); }
            finally { UnityEngine.Object.DestroyImmediate(r2); }

            // A long quote, a two-line headline and a five-node timeline all fit too.
            QuoteCard quote = Wide<QuoteCard>(out GameObject r3, new ContentCardEvent
            {
                cardType = ContentCardType.Quote, primaryText = Quote40, secondaryText = "Sarah Mitchell", tertiaryText = "VP of Communications, MegaCorp",
            });
            try
            {
                log.IsTrue(quote.Layout.GroupHeight <= quote.Layout.InnerHeight + 1f,
                    $"wide Quote (40 words): fits the slot's height (group {quote.Layout.GroupHeight:F0} of {quote.Layout.InnerHeight:F0} px, scale {quote.WideFit:F2})");
                CheckCard(log, quote, quote.Layout, "wide 40-word Quote");
            }
            finally { UnityEngine.Object.DestroyImmediate(r3); }

            TimelineCard timeline = BuildCard<TimelineCard>(out GameObject r4);
            try
            {
                ((RectTransform)timeline.transform.parent).sizeDelta = new Vector2(1440f, 842.4f);
                timeline.SetStageLayout(true, 1080f);
                var nodes = new List<(string, string)>
                {
                    ("Oct. 25", "Rossmann's first video"), ("Early Nov.", "Apple removes the target"), ("Same week", "A memo about consent"),
                    ("Nov. 8", "MacRumors confirms both"), ("Nov. 11", "The holiday season explanation is given to reporters"),
                };
                timeline.AnticipatedNodes = nodes;
                timeline.Initialize(new ContentCardEvent { cardType = ContentCardType.Timeline, primaryText = nodes[0].Item1, secondaryText = nodes[0].Item2 }, null);
                log.IsTrue(timeline.Layout.GroupHeight <= timeline.Layout.InnerHeight + 1f,
                    $"wide Timeline (5 nodes): fits the slot's height (group {timeline.Layout.GroupHeight:F0} of {timeline.Layout.InnerHeight:F0} px, scale {timeline.WideFit:F2})");
            }
            finally { UnityEngine.Object.DestroyImmediate(r4); }

            // The side layout is untouched: no fitting, scale 1.
            CompareCard side = BuildCard<CompareCard>(out GameObject r5);
            try
            {
                side.Initialize(new ContentCardEvent { cardType = ContentCardType.Compare, fields = fields, primaryText = fields[0] }, null);
                log.Near(1f, side.WideFit, 1e-6f, "side Compare: the side layout never shrinks its scale");
            }
            finally { UnityEngine.Object.DestroyImmediate(r5); }
        }
        finally { VisualsRuntimeApplier.CardFontOverride = savedFont; }
    }
}
