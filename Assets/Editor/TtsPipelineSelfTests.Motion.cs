using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using MugsTech.Stage;
using MugsTech.Style;
using MugsTech.Tts;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Mugs in motion (Tier 5).
/// Step 1 — weight: the pose of every stage leg (StageMoves): where it
/// starts, that it ends on exactly rest, that its drift past the mark, its
/// stretch and its give stay inside their limits for every t, and that the
/// line he stands on doesn't move when his shape does. The feel is CALM (the
/// creator's pick from a preview on 2026-11-17): no wind-up, no dead stop.
/// Step 2 — moves that chain: the plan for every pair of positions (always
/// out, then in), and that a move which arrives while another is running
/// either starts on the frame he is on or lets him finish leaving first.
/// Step 3 — the dash: its pose (DashMath), the turn, the main-menu choice
/// (Glide stays the move it always was), and the holds it shortens.
/// Step 4 — the handover ({Position:…,Over}): the word through the parser,
/// both stampers' shape and the retimer; when it applies; and on the rig that
/// the card under him stays as it is until it ends.
/// Step 5 — the lean ({Position:LeanLeft/LeanRight}): its pose inside the
/// peek's size limits, the plan against every other position, the words
/// through the stampers and the retimer, and its legs on the rig.
/// Step 6 — arrive on the word: which arrivals start early and when
/// (ArrivalLeads), the face he arrives with, and on the rig that the stage's
/// own change stays on the tag's time.
///
/// The Tier5_* checks without "Engine" in their name are pure and run
/// offline; the *_Engine one puts poses on the presenter rig in edit mode.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Stage/Run Motion Self-Tests")]
    public static void RunMotion()
    {
        var log = new Log();
        RunMotionGroup(log);
        log.Report();
    }

    static void RunMotionGroup(Log log)
    {
        Tier5_LegDurations(log);
        Tier5_ArrivalPoses(log);
        Tier5_DeparturePoses(log);
        Tier5_StandingEdgeHolds(log);
        Tier5_OutThenInPlans(log);
        Tier5_ComeBackPoses(log);
        Tier5_WhereHeIs(log);
        Tier5_DashPose(log);
        Tier5_HoldsAreCounted(log);
        Tier5_OverWordsParseAndStamp(log);
        Tier5_OverRules(log);
        Tier5_LeanPoseAndPlans(log);
        Tier5_ArrivalLeads(log);
        Tier5_ArrivalFaces(log);

        try
        {
            Tier5_PosesOnTheRig_Engine(log);
            Tier5_ChainOnTheRig_Engine(log);
            Tier5_SideMoveChoice_Engine(log);
            Tier5_DashOnTheRig_Engine(log);
            Tier5_HandoverDim_Engine(log);
            Tier5_HandoverOnTheRig_Engine(log);
            Tier5_LeanOnTheRig_Engine(log);
            Tier5_LedArrivalOnTheRig_Engine(log);
        }
        catch (Exception e)
        {
            // Scene objects need the engine — offline harnesses land here.
            log.IsTrue(false, $"motion engine checks threw {e.GetType().Name}: {e.Message}");
        }
    }

    const float MotionStep = 1f / 600f;   // every pose is checked at 600 samples a second

    static bool SamePose(StageMoves.Pose a, StageMoves.Pose b) =>
        a.Travel == b.Travel && a.ScaleX == b.ScaleX && a.ScaleY == b.ScaleY && a.Tilt == b.Tilt;

    static void Tier5_LegDurations(Log log)
    {
        // The calm timings the creator chose from the side-by-side preview (2026-11-17).
        log.Near(0.65f, StageMath.PopUpSeconds, 1e-6f, "weight: the pop-up takes 0.65 s");
        log.Near(0.60f, StageMath.SlideOutSeconds, 1e-6f, "weight: the slide-out takes 0.60 s (down, and through a side edge)");
        log.Near(0.40f, StageMath.PeekRiseSeconds, 1e-6f, "weight: the peek rises in 0.40 s");
        log.Near(0.35f, StageMath.PeekDropSeconds, 1e-6f, "weight: …and drops in 0.35 s");
        log.Near(0.40f, StageMath.LeanInSeconds, 1e-6f, "weight: the lean comes in in 0.40 s");
        log.Near(0.35f, StageMath.LeanOutSeconds, 1e-6f, "weight: …and pulls out in 0.35 s");
        log.Near(1.00f, StageMath.LongestArrivalSeconds, 1e-6f, "weight: the longest arrival is out of a peek, then the pop-up (1.00 s) — what caps a waiting card");

        var all = new[]
        {
            CharacterPosition.Left, CharacterPosition.Right, CharacterPosition.Center,
            CharacterPosition.Off, CharacterPosition.PeekLeft, CharacterPosition.PeekRight,
        };
        foreach (CharacterPosition from in all)
            foreach (CharacterPosition to in all)
            {
                float sum = 0f;
                foreach (StageLeg leg in StageMath.PlanTravel(from, to))
                {
                    float s = StageMath.LegSeconds(leg);
                    sum += s;
                    log.IsTrue(s <= StageMath.PopUpSeconds + 1e-6f, $"weight: no leg is longer than the pop-up ({leg}: {s:F2} s)");
                    log.IsTrue(SamePose(StageMoves.Rest, StageMoves.For(leg, s, sideways: true)) &&
                               SamePose(StageMoves.Rest, StageMoves.For(leg, s, sideways: false)),
                        $"weight: {leg} is at rest exactly when its {s:F2} s are up");
                }
                log.Near(sum, StageMath.TravelSeconds(from, to), 1e-6f, $"weight: {from} → {to} takes the sum of its legs ({sum:F2} s)");
            }

        log.Near(0.60f, StageMath.TravelSeconds(CharacterPosition.Left, CharacterPosition.Off), 1e-6f, "weight: Left → Off is the 0.60 s slide-out");
        log.Near(0.65f, StageMath.TravelSeconds(CharacterPosition.Off, CharacterPosition.Center), 1e-6f, "weight: Off → Center is the 0.65 s pop-up");
        log.Near(1.00f, StageMath.TravelSeconds(CharacterPosition.Left, CharacterPosition.PeekRight), 1e-6f, "weight: Left → PeekRight slides out and rises (1.00 s)");
        log.Near(1.00f, StageMath.TravelSeconds(CharacterPosition.PeekLeft, CharacterPosition.Right), 1e-6f, "weight: PeekLeft → Right drops and pops up (1.00 s)");
    }

    static void Tier5_ArrivalPoses(Log log)
    {
        var arrivals = new (string name, Func<float, StageMoves.Pose> at, float seconds, float amount)[]
        {
            ("the pop-up", StageMoves.PopUp, StageMath.PopUpSeconds, 1f),
            ("the peek's rise", StageMoves.PeekRise, StageMath.PeekRiseSeconds, StageMoves.PeekAmount),
            ("the lean-in", StageMoves.LeanIn, StageMath.LeanInSeconds, StageMoves.PeekAmount),
        };
        log.IsTrue(StageMoves.ArriveOvershoot > 0.02f && StageMoves.ArriveOvershoot < 0.04f,
            $"weight: an arrival drifts about 3 % of the leg past its mark ({StageMoves.ArriveOvershoot * 100f:F1} %)");

        foreach (var (name, at, seconds, amount) in arrivals)
        {
            float stretch = StageMoves.MaxStretch * amount, squash = StageMoves.MaxSquash * amount;
            float peakAt = seconds * StageMoves.ArrivePeakAt;

            StageMoves.Pose start = at(0f);
            log.IsTrue(start.Travel == 0f && start.Tilt == 0f, $"weight: {name} starts where the leg starts, upright");
            log.Near(1f + stretch, start.ScaleY, 1e-6f, $"weight: {name} comes in stretched ({stretch * 100f:F0} % taller)");
            log.Near(1f - stretch * 0.5f, start.ScaleX, 1e-6f, $"weight: …and {stretch * 50f:F0} % narrower");

            log.IsTrue(SamePose(StageMoves.Rest, at(seconds)) && SamePose(StageMoves.Rest, at(seconds + 5f)),
                $"weight: {name} ends on exactly rest (travel 1, scale 1 and 1, tilt 0) and stays there");

            float top = 0f, topAt = 0f, shortest = 1f, widest = 1f;
            float firstSpeed = at(MotionStep).Travel / MotionStep, fastest = 0f, previous = 0f, previousSpeed = float.PositiveInfinity;
            float previousShape = start.ScaleY, biggestShapeStep = 0f;
            bool inLimits = true, opposite = true, upright = true, noDip = true, slowing = true, comesBack = true;
            for (float t = MotionStep; t < seconds; t += MotionStep)
            {
                StageMoves.Pose p = at(t);
                if (p.Travel > top) { top = p.Travel; topAt = t; }
                shortest = Mathf.Min(shortest, p.ScaleY);
                widest = Mathf.Max(widest, p.ScaleX);
                float speed = (p.Travel - previous) / MotionStep;
                fastest = Mathf.Max(fastest, speed);
                if (t < peakAt && speed > previousSpeed + 1e-3f) slowing = false;
                if (t > peakAt + MotionStep && (p.Travel > previous + 1e-6f || p.Travel < 1f - 1e-6f)) comesBack = false;
                previous = p.Travel;
                previousSpeed = speed;
                biggestShapeStep = Mathf.Max(biggestShapeStep, Mathf.Abs(p.ScaleY - previousShape));
                previousShape = p.ScaleY;
                if (p.Travel < 0f) noDip = false;
                if (p.ScaleY > 1f + stretch + 1e-5f || p.ScaleY < 1f - squash - 1e-5f) inLimits = false;
                if (p.ScaleX > 1f + squash * 0.5f + 1e-5f || p.ScaleX < 1f - stretch * 0.5f - 1e-5f) inLimits = false;
                if ((p.ScaleY - 1f) * (p.ScaleX - 1f) > 1e-9f) opposite = false;
                if (p.Tilt != 0f) upright = false;
            }
            log.Near(1f + StageMoves.ArriveOvershoot, top, 1e-4f, $"weight: {name} drifts a little past its rest ({(top - 1f) * 100f:F1} % of the leg)");
            log.Near(peakAt, topAt, 3f * MotionStep, $"weight: …furthest at {topAt:F3} s; everything after that is the settle");
            log.IsTrue(noDip, $"weight: {name} never goes back below its start");
            log.IsTrue(inLimits, $"weight: {name} stays inside the stretch and give limits for every t");
            log.IsTrue(opposite, $"weight: {name} — taller means narrower, shorter means wider (he squashes, he doesn't shrink)");
            log.IsTrue(upright, $"weight: {name} never leans");
            log.IsTrue(slowing, $"weight: {name} slows all the way in — there is no dead stop to hit");
            log.Near(firstSpeed, fastest, firstSpeed * 0.02f, $"weight: …it is never faster than on its first frame");
            log.IsTrue(comesBack, $"weight: {name} — from there he only eases back down onto his mark, never below it");
            log.IsTrue(shortest < 1f - squash * 0.98f, $"weight: {name} settles with a soft give ({(1f - shortest) * 100f:F1} % shorter, {(widest - 1f) * 100f:F1} % wider)");
            log.IsTrue(biggestShapeStep < 0.0025f, $"weight: {name} — his shape never jumps (largest step at 600 samples a second: {biggestShapeStep * 100f:F2} %)");

            // Nothing left at the end: neither travel nor shape is still changing.
            float travelRate = Mathf.Abs(at(seconds - 0.001f).Travel - 1f) / 0.001f;
            float shapeRate = Mathf.Abs(at(seconds - 0.001f).ScaleY - 1f) / 0.001f;
            log.IsTrue(travelRate < 0.05f && shapeRate < 0.1f, $"weight: {name} settles with no speed left (travel {travelRate:F3} /s, shape {shapeRate:F3} /s a millisecond before the end)");

            // At the take's 30 fps: the frames he is seen on.
            float biggest = 0f, prev30 = 0f;
            int giving = 0, settling = 0;
            for (float t = 0f; t < seconds; t += 1f / 30f)
            {
                StageMoves.Pose p = at(t);
                biggest = Mathf.Max(biggest, Mathf.Abs(p.Travel - prev30));
                prev30 = p.Travel;
                if (p.ScaleY < 1f - 0.01f * amount) giving++;
                if (t > peakAt) settling++;
            }
            log.IsTrue(biggest < 0.50f, $"weight: {name} — no frame at 30 fps moves him more than half the leg (largest {biggest * 100f:F0} %)");
            log.IsTrue(giving >= 2, $"weight: {name} — the give is on screen for at least 2 frames at 30 fps ({giving})");
            log.IsTrue(settling >= 3, $"weight: {name} — the settle takes at least 3 frames at 30 fps ({settling})");
        }
    }

    static void Tier5_DeparturePoses(Log log)
    {
        var departures = new (string name, Func<float, StageMoves.Pose> at, float seconds, float amount, bool sideways)[]
        {
            ("the slide-out through a side edge", t => StageMoves.SlideOut(t, true), StageMath.SlideOutSeconds, 1f, true),
            ("the slide-out down from Center", t => StageMoves.SlideOut(t, false), StageMath.SlideOutSeconds, 1f, false),
            ("the peek's drop", StageMoves.PeekDrop, StageMath.PeekDropSeconds, StageMoves.PeekAmount, false),
            ("the lean-out", StageMoves.LeanOut, StageMath.LeanOutSeconds, StageMoves.PeekAmount, false),
        };

        foreach (var (name, at, seconds, amount, sideways) in departures)
        {
            float stretch = StageMoves.MaxStretch * amount;

            StageMoves.Pose start = at(0f);
            log.IsTrue(start.Travel == 0f && start.ScaleX == 1f && start.ScaleY == 1f && start.Tilt == 0f,
                $"weight: {name} starts from where and how he stands");
            log.IsTrue(SamePose(StageMoves.Rest, at(seconds)) && SamePose(StageMoves.Rest, at(seconds + 5f)),
                $"weight: {name} ends on exactly rest (travel 1, scale 1 and 1, tilt 0)");

            // Slow off the mark.
            log.IsTrue(at(MotionStep).Travel < 1e-4f, $"weight: {name} starts from standing");
            float third = at(seconds / 3f).Travel;
            log.IsTrue(third < 0.05f, $"weight: {name} is slow off the mark (a third of the way through he has moved {third * 100f:F1} % of the leg)");

            bool inLimits = true, faster = true, noWindUp = true, along = true;
            float previous = 0f, previousSpeed = 0f, longest = 1f, leanMost = 0f, previousShape = 1f, biggestShapeStep = 0f;
            for (float t = MotionStep; t < seconds; t += MotionStep)
            {
                StageMoves.Pose p = at(t);
                float alongScale = sideways ? p.ScaleX : p.ScaleY, across = sideways ? p.ScaleY : p.ScaleX;
                longest = Mathf.Max(longest, alongScale);
                leanMost = Mathf.Max(leanMost, p.Tilt);
                if (alongScale > 1f + stretch + 1e-5f || alongScale < 1f - 1e-5f) inLimits = false;
                if (across > 1f + 1e-5f || across < 1f - stretch * 0.5f - 1e-5f) inLimits = false;
                if (p.Travel < 0f || p.Tilt < 0f) noWindUp = false;          // never a step back, never a lean away
                if (!sideways && p.Tilt != 0f) inLimits = false;
                float speed = (p.Travel - previous) / MotionStep;
                if (speed < previousSpeed - 1e-3f) faster = false;
                if (t > seconds * 0.5f && alongScale < across) along = false;
                previous = p.Travel;
                previousSpeed = speed;
                biggestShapeStep = Mathf.Max(biggestShapeStep, Mathf.Abs(alongScale - previousShape));
                previousShape = alongScale;
            }
            log.IsTrue(noWindUp, $"weight: {name} has no wind-up — never a step back, never a lean away from the exit");
            log.IsTrue(inLimits, $"weight: {name} stays inside the stretch limits for every t" + (sideways ? "" : " (no lean: he goes straight)"));
            if (sideways)
                log.Near(StageMoves.DepartLean * amount, leanMost, 0.05f, $"weight: {name} — he leans up to {StageMoves.DepartLean:F0}° into it");
            log.IsTrue(faster, $"weight: {name} — he only gets faster (fastest as he crosses the edge)");
            log.IsTrue(along, $"weight: {name} — he is stretched along his travel ({(sideways ? "wider" : "taller")})");
            log.Near(1f + stretch, longest, 0.004f, $"weight: {name} — up to {stretch * 100f:F0} % at the edge");
            log.IsTrue(biggestShapeStep < 0.001f, $"weight: {name} — his shape never jumps (largest step at 600 samples a second: {biggestShapeStep * 100f:F3} %)");
        }

        // The peek's and the lean's moves are the full ones at two thirds.
        log.Near(2f / 3f, StageMoves.PeekAmount, 1e-6f, "weight: a peek's stretch and give are two thirds of a full move's");
    }

    static void Tier5_StandingEdgeHolds(Log log)
    {
        var origin = new Vector2(2f, -3.138f);
        // His feet (0.42 below the moved transform) and the frame's bottom edge (a peek).
        foreach (float edge in new[] { origin.y - 0.42f, -5f })
            foreach (float scaleY in new[] { 0.94f, 0.97f, 1f, 1.04f, 1.06f })
                foreach (float tilt in new[] { -4f, 0f, 3f, 12f })
                {
                    Vector2 moved = StageMoves.HoldEdge(origin, edge, scaleY, tilt);
                    // The point of him on that line: straight below the transform, squashed and turned with it.
                    float below = (origin.y - edge) * scaleY, r = tilt * Mathf.Deg2Rad;
                    var onLine = new Vector2(moved.x + Mathf.Sin(r) * below, moved.y - Mathf.Cos(r) * below);
                    log.IsTrue((onLine - new Vector2(origin.x, edge)).magnitude < 1e-4f,
                        $"weight: the standing edge stays put (edge {edge:F2}, height ×{scaleY:F2}, lean {tilt:F0}°)");
                }
        log.IsTrue((StageMoves.HoldEdge(origin, -5f, 1f, 0f) - origin).magnitude < 1e-6f, "weight: with his own shape nothing is moved");
    }

    // -----------------------------------------------------------------------
    // Step 2: one move, not two; moves that chain
    // -----------------------------------------------------------------------

    static string Legs(List<StageLeg> legs) => string.Join(",", legs);

    static void Tier5_OutThenInPlans(Log log)
    {
        // Every pair of positions, as a table: "from>to" → its legs. A peek and
        // a full position are always "out, then in": the creator watched a peek
        // rise straight into Center and it "looks wrong".
        var table = new Dictionary<string, string>
        {
            { "Left>Right", "" }, { "Left>Center", "" }, { "Left>Off", "SlideOut,Park" },
            { "Left>PeekLeft", "SlideOut,RiseToPeek" }, { "Left>PeekRight", "SlideOut,RiseToPeek" },
            { "Right>Left", "" }, { "Right>Center", "" }, { "Right>Off", "SlideOut,Park" },
            { "Right>PeekLeft", "SlideOut,RiseToPeek" }, { "Right>PeekRight", "SlideOut,RiseToPeek" },
            { "Center>Left", "" }, { "Center>Right", "" }, { "Center>Off", "SlideOut,Park" },
            { "Center>PeekLeft", "SlideOut,RiseToPeek" }, { "Center>PeekRight", "SlideOut,RiseToPeek" },
            { "Off>Left", "PopUp" }, { "Off>Right", "PopUp" }, { "Off>Center", "PopUp" },
            { "Off>PeekLeft", "RiseToPeek" }, { "Off>PeekRight", "RiseToPeek" },
            { "PeekLeft>Left", "DropFromPeek,PopUp" }, { "PeekLeft>Right", "DropFromPeek,PopUp" }, { "PeekLeft>Center", "DropFromPeek,PopUp" },
            { "PeekLeft>Off", "DropFromPeek,Park" }, { "PeekLeft>PeekRight", "DropFromPeek,RiseToPeek" },
            { "PeekRight>Left", "DropFromPeek,PopUp" }, { "PeekRight>Right", "DropFromPeek,PopUp" }, { "PeekRight>Center", "DropFromPeek,PopUp" },
            { "PeekRight>Off", "DropFromPeek,Park" }, { "PeekRight>PeekLeft", "DropFromPeek,RiseToPeek" },
        };
        var all = new[]
        {
            CharacterPosition.Left, CharacterPosition.Right, CharacterPosition.Center,
            CharacterPosition.Off, CharacterPosition.PeekLeft, CharacterPosition.PeekRight,
        };
        foreach (CharacterPosition from in all)
            foreach (CharacterPosition to in all)
            {
                string want = from == to ? "" : table[from + ">" + to];
                log.AreEqual(want, Legs(StageMath.PlanTravel(from, to)), $"chain: the plan for {from} → {to}");
                // "In view" changes only a move planned from Off (a pop-up turned to another mark).
                if (from != CharacterPosition.Off)
                    log.AreEqual(want, Legs(StageMath.PlanTravel(from, to, inView: true)), $"chain: …the same while he is in view ({from} → {to})");
            }
        foreach (CharacterPosition full in new[] { CharacterPosition.Left, CharacterPosition.Right, CharacterPosition.Center })
            log.AreEqual("ToFull", Legs(StageMath.PlanTravel(CharacterPosition.Off, full, inView: true)),
                $"chain: a pop-up that is turned to {full} while he is in view goes there from where he is");
        foreach (CharacterPosition peek in new[] { CharacterPosition.PeekLeft, CharacterPosition.PeekRight })
            log.AreEqual("ToPeek", Legs(StageMath.PlanTravel(CharacterPosition.Off, peek, inView: true)),
                $"chain: …and {peek} the same");

        log.Near(1.00f, StageMath.TravelSeconds(CharacterPosition.PeekRight, CharacterPosition.Center), 1e-6f, "chain: a peek to Center is the drop, then the pop-up: 0.35 + 0.65 s (the creator's pick)");
        log.Near(1.00f, StageMath.TravelSeconds(CharacterPosition.PeekLeft, CharacterPosition.Left), 1e-6f, "chain: …on its own side too");
        log.Near(1.00f, StageMath.TravelSeconds(CharacterPosition.Center, CharacterPosition.PeekRight), 1e-6f, "chain: a full position to a peek is the slide-out, then the rise: 0.60 + 0.40 s");
        log.IsTrue(StageMath.CardsWaitFor(CharacterPosition.PeekRight, CharacterPosition.Right), "chain: a side card still waits for him to land out of a peek");
        log.IsTrue(!StageMath.CardsWaitFor(CharacterPosition.Center, CharacterPosition.PeekRight), "chain: nothing waits for him to go into one");

        // A tag that arrives while he is still LEAVING.
        CharacterPosition L = CharacterPosition.Left, R = CharacterPosition.Right, C = CharacterPosition.Center, Off = CharacterPosition.Off,
                          PL = CharacterPosition.PeekLeft, PR = CharacterPosition.PeekRight, LL = CharacterPosition.LeanLeft;
        void Leaving(CharacterPosition leaving, CharacterPosition target, bool inView, string wantLegs, bool wantFinish, string what)
        {
            List<StageLeg> legs = StageMath.PlanWhileLeaving(leaving, target, inView, out bool finish);
            log.AreEqual(wantLegs, Legs(legs), $"chain: {what}");
            log.IsTrue(finish == wantFinish, $"chain: {what} — " + (wantFinish ? "the leg that was running is finished first" : "nothing is finished first"));
        }
        Leaving(L, L, true, "ToFull", false, "sliding out at Left and wanted back at Left, he comes back from where he is");
        Leaving(C, C, true, "ToFull", false, "sinking at Center and wanted back at Center, the same");
        Leaving(L, C, true, "PopUp", true, "sliding out at Left and wanted at Center, he finishes leaving, then pops up there");
        Leaving(L, PL, true, "RiseToPeek", true, "sliding out at Left and wanted as a peek — a full position never sinks into a peek");
        Leaving(L, LL, true, "LeanIn", true, "…nor steps back into a lean");
        Leaving(C, Off, true, "Park", true, "leaving, and told to be off: he simply finishes leaving");
        Leaving(PR, PR, true, "ToPeek", false, "dropping out of a peek and wanted back in it, he comes back up from where he is");
        Leaving(PR, C, true, "PopUp", true, "dropping out of a peek and wanted at Center — a peek never grows into an entrance");
        Leaving(PR, R, true, "PopUp", true, "…on its own side neither");
        Leaving(PR, PL, true, "RiseToPeek", true, "dropping out of one peek and wanted in the other");
        Leaving(PR, Off, true, "Park", true, "dropping out of a peek on his way somewhere, and told to be off");
        Leaving(LL, LL, true, "ToLean", false, "pulling out of a lean and wanted back in it, he comes back in from where he is");
        Leaving(LL, L, true, "PopUp", true, "pulling out of a lean and wanted at Left");
        Leaving(LL, PR, true, "RiseToPeek", true, "pulling out of a lean and wanted as a peek");
        // Already out of sight: the arrival starts at once, from its own hidden start.
        Leaving(L, L, false, "PopUp", false, "already out of sight at Left and wanted back");
        Leaving(PR, PR, false, "RiseToPeek", false, "already out of sight below a peek and wanted back in it");
        Leaving(PR, C, false, "PopUp", false, "already out of sight below a peek and wanted at Center");
        Leaving(L, Off, false, "Park", false, "already out of sight and told to be off");
        log.IsTrue(StageMath.IsLeaving(StageLeg.SlideOut) && StageMath.IsLeaving(StageLeg.DropFromPeek) && StageMath.IsLeaving(StageLeg.LeanOut) &&
                   !StageMath.IsLeaving(StageLeg.PopUp) && !StageMath.IsLeaving(StageLeg.RiseToPeek) && !StageMath.IsLeaving(StageLeg.LeanIn) &&
                   !StageMath.IsLeaving(StageLeg.ToFull) && !StageMath.IsLeaving(StageLeg.Park),
            "chain: the legs that take him out are the slide-out, the peek's drop and the lean-out");
    }

    static void Tier5_ComeBackPoses(Log log)
    {
        // ToFull / ToPeek / ToLean: only ever the answer to a tag that catches
        // him on his way out, or a pop-up turned to another mark.
        var legs = new (string name, Func<float, StageMoves.Pose> at, float seconds, float amount)[]
        {
            ("coming back to a full position", StageMoves.ToFull, StageMath.ToFullSeconds, 1f),
            ("coming back into a peek or a lean", StageMoves.ToPeek, StageMath.ToPeekSeconds, StageMoves.PeekAmount),
        };
        foreach (var (name, at, seconds, amount) in legs)
        {
            StageMoves.Pose start = at(0f);
            log.IsTrue(start.Travel == 0f && start.ScaleX == 1f && start.ScaleY == 1f && start.Tilt == 0f,
                $"chain: {name} starts from where and how he is");
            log.IsTrue(at(MotionStep).Travel < 0.001f, $"chain: {name} starts from standing");
            log.IsTrue(SamePose(StageMoves.Rest, at(seconds)) && SamePose(StageMoves.Rest, at(seconds + 3f)), $"chain: {name} ends on exactly rest");

            float top = 0f, previous = 0f, biggest600 = 0f, tallest = 1f;
            bool inLimits = true, rising = true;
            for (float t = MotionStep; t < seconds; t += MotionStep)
            {
                StageMoves.Pose p = at(t);
                top = Mathf.Max(top, p.Travel);
                tallest = Mathf.Max(tallest, p.ScaleY);
                if (p.Travel < previous - 1e-6f) rising = false;
                biggest600 = Mathf.Max(biggest600, Mathf.Abs(p.Travel - previous));
                previous = p.Travel;
                if (p.ScaleY > 1f + StageMoves.MaxStretch * amount * 0.5f + 1e-5f || p.ScaleY < 1f - 1e-6f || p.Tilt != 0f) inLimits = false;
                if ((p.ScaleY - 1f) * (p.ScaleX - 1f) > 1e-9f) inLimits = false;
            }
            log.IsTrue(top <= 1f + 1e-6f && rising, $"chain: {name} never passes its mark and never goes back — eased at both ends, like the Glide");
            log.IsTrue(inLimits, $"chain: {name} — a touch longer in the middle ({(tallest - 1f) * 100f:F1} %), never squashed, never leaning");
            log.IsTrue(biggest600 < 0.02f, $"chain: {name} — no jump anywhere (largest step at 600 samples a second: {biggest600 * 100f:F2} % of the way)");

            float biggest = 0f, prev30 = 0f;
            for (float t = 0f; t < seconds; t += 1f / 30f)
            {
                float travel = at(t).Travel;
                biggest = Mathf.Max(biggest, Mathf.Abs(travel - prev30));
                prev30 = travel;
            }
            log.IsTrue(biggest < 0.30f, $"chain: {name} — no frame at 30 fps moves him more than 30 % of the way (largest {biggest * 100f:F0} %)");
            float travelRate = Mathf.Abs(at(seconds - 0.001f).Travel - 1f) / 0.001f;
            float shapeRate = Mathf.Abs(at(seconds - 0.001f).ScaleY - 1f) / 0.001f;
            log.IsTrue(travelRate < 0.1f && shapeRate < 0.1f, $"chain: {name} — no speed left at the end (travel {travelRate:F3} /s, shape {shapeRate:F3} /s a millisecond before)");
        }
        log.IsTrue(SamePose(StageMoves.ToPeek(0.13f), StageMoves.For(StageLeg.ToLean, 0.13f, false)), "chain: a lean's way back in has the peek's shape");

        // What a cut-short move leaves on him is eased away, not dropped.
        log.Near(1f, StageMoves.CarryLeft(0f), 1e-6f, "chain: a leg starts with all of the shape he had");
        log.Near(0f, StageMoves.CarryLeft(StageMoves.CarrySeconds), 1e-6f, "chain: …and has eased it away after 0.10 s");
        bool falling = true;
        for (float t = MotionStep; t <= StageMoves.CarrySeconds; t += MotionStep)
            if (StageMoves.CarryLeft(t) > StageMoves.CarryLeft(t - MotionStep) + 1e-6f) falling = false;
        log.IsTrue(falling, "chain: …steadily");
        foreach (StageLeg leg in new[] { StageLeg.SlideOut, StageLeg.DropFromPeek, StageLeg.LeanOut, StageLeg.ToFull, StageLeg.ToPeek, StageLeg.ToLean })
            log.IsTrue(StageMoves.CarrySeconds < StageMath.LegSeconds(leg), $"chain: …before {leg} ends, so every leg still ends on exactly rest");
    }

    static void Tier5_WhereHeIs(Log log)
    {
        StageMath.Frame frame = TestFrame;
        StageMath.Body body = StageMath.DefaultBody;
        Vector2 left = new Vector2(-7.33f, -3.138f), right = new Vector2(6.92f, -3.138f), centre = new Vector2(0f, -3.138f);

        // At his marks the nearest edge is the one his position always used.
        log.AreEqual(CharacterPosition.Left, StageMath.ExitFrom(left.x, frame), "chain: at Left the nearest edge is the left one");
        log.AreEqual(CharacterPosition.Right, StageMath.ExitFrom(right.x, frame), "chain: at Right, the right one");
        log.AreEqual(CharacterPosition.Center, StageMath.ExitFrom(centre.x, frame), "chain: at Center he goes down");
        log.AreEqual(CharacterPosition.Center, StageMath.ExitFrom(-3f, frame), "chain: a third of the way from Center to Left he still goes down");
        log.AreEqual(CharacterPosition.Left, StageMath.ExitFrom(-5f, frame), "chain: most of the way there he leaves to the left");

        foreach (Vector2 mark in new[] { left, right, centre })
        {
            log.IsTrue(StageMath.InView(mark, body, frame), $"chain: at his mark he is in view (x {mark.x:F2})");
            log.IsTrue(!StageMath.InView(StageMath.BelowFrame(mark, body, frame), body, frame), "chain: …below the frame he is not");
        }
        log.IsTrue(!StageMath.InView(StageMath.SlideOutTarget(CharacterPosition.Left, left, body, frame), body, frame), "chain: slid out to the left he is out of view");
        StageMath.PeekPose peek = StageMath.Peek(true, body, frame);
        log.IsTrue(StageMath.InView(peek.Rest, body, frame, peek.Scale) && !StageMath.InView(peek.Hidden, body, frame, peek.Scale),
            "chain: a peek is in view, its hidden start is not");
    }

    // -----------------------------------------------------------------------
    // Engine: legs on the presenter rig (the travel itself is a coroutine, so
    // the checks begin and pose its legs by hand)
    // -----------------------------------------------------------------------

    static object BeginLeg(StageRig rig, StageLeg leg, CharacterPosition target, CharacterPosition exitAs) =>
        rig.Call("BeginStageLeg", leg, target, exitAs, TestFrame, StageMath.DefaultBody);

    static void PoseLeg(StageRig rig, object run, float t) => rig.Call("PoseStageLeg", run, t);

    static float LeanOf(Transform t) => t.eulerAngles.z > 180f ? t.eulerAngles.z - 360f : t.eulerAngles.z;

    static void Tier5_PosesOnTheRig_Engine(Log log)
    {
        StageRig rig = BuildStageRig();
        try
        {
            StageMath.Frame frame = TestFrame;
            StageMath.Body body = StageMath.DefaultBody;
            float feet = body.CentreOffset.y - body.Size.y * 0.5f;

            // The pop-up at Center: every pose keeps his feet on the travel's own line.
            rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);
            Vector2 rest = rig.Center.position, below = StageMath.BelowFrame(rest, body, frame);
            object popUp = BeginLeg(rig, StageLeg.PopUp, CharacterPosition.Center, CharacterPosition.Center);
            log.IsTrue(((Vector2)rig.Presenter.position - below).magnitude < 1e-5f, "rig: a pop-up starts below the frame under his mark");
            bool feetOnLine = true, widthOnIt = true;
            for (float t = 0f; t < StageMath.PopUpSeconds; t += 1f / 120f)
            {
                StageMoves.Pose pose = StageMoves.PopUp(t);
                PoseLeg(rig, popUp, t);
                float want = Vector2.LerpUnclamped(below, rest, pose.Travel).y + feet;
                float got = rig.Presenter.position.y + feet * rig.Presenter.localScale.y;
                if (Mathf.Abs(want - got) > 1e-4f) feetOnLine = false;
                if (Mathf.Abs(pose.ScaleX - rig.Presenter.localScale.x) > 1e-5f) widthOnIt = false;
            }
            log.IsTrue(feetOnLine, "rig: through the whole pop-up his feet are where the travel alone puts them");
            log.IsTrue(widthOnIt, "rig: …and his squash and stretch are on the moved transform");

            // Halfway through the settle: the give is at its deepest and he is still a touch over his mark.
            float settleAt = StageMath.PopUpSeconds * (StageMoves.ArrivePeakAt + (1f - StageMoves.ArrivePeakAt) * 0.5f);
            StageMoves.Pose settling = StageMoves.PopUp(settleAt);
            PoseLeg(rig, popUp, settleAt);
            log.IsTrue(settling.ScaleY < 0.95f && settling.Travel > 1f, "rig: (halfway through the settle he gives 6 % and is still a touch over his mark)");
            log.Near(Vector2.LerpUnclamped(below, rest, settling.Travel).y + feet, rig.Presenter.position.y + feet * rig.Presenter.localScale.y, 1e-4f,
                "rig: …and his feet are on the travel's own line while he does");

            PoseLeg(rig, popUp, StageMath.PopUpSeconds);
            log.IsTrue(((Vector2)rig.Presenter.position - rest).magnitude < 1e-5f && rig.Presenter.localScale == Vector3.one &&
                       Quaternion.Angle(rig.Presenter.rotation, Quaternion.identity) < 1e-3f,
                "rig: the pop-up's last pose is exactly his mark, his size, upright");
            rig.Call("EndStageShape");

            // The slide-out from Left: no wind-up, he leans into it, and his feet don't leave their line.
            rig.Call("MoveToPosition", CharacterPosition.Left, (bool?)true);
            Vector2 from = rig.Left.position, gone = StageMath.SlideOutTarget(CharacterPosition.Left, from, body, frame);
            object slide = BeginLeg(rig, StageLeg.SlideOut, CharacterPosition.Off, CharacterPosition.Left);
            bool noWindUp = true;
            for (float t = 0f; t < StageMath.SlideOutSeconds; t += 1f / 120f)
            {
                PoseLeg(rig, slide, t);
                if (LeanOf(rig.Presenter) < -1e-3f || rig.Presenter.position.x > from.x + 1e-4f) noWindUp = false;
            }
            log.IsTrue(noWindUp, "rig: leaving to the left he never leans or steps the other way first");
            float goingAt = StageMath.SlideOutSeconds * 0.6f;
            StageMoves.Pose lean = StageMoves.SlideOut(goingAt, true);
            PoseLeg(rig, slide, goingAt);
            Vector2 travelled = Vector2.LerpUnclamped(from, gone, lean.Travel);
            Vector2 feetNow = rig.Presenter.TransformPoint(new Vector3(0f, feet, 0f));
            log.IsTrue((feetNow - new Vector2(travelled.x, travelled.y + feet)).magnitude < 1e-4f,
                "rig: he leans about his feet — they stay on their line");
            log.IsTrue(LeanOf(rig.Presenter) > 0.5f && rig.Presenter.position.x < from.x && rig.Presenter.localScale.x > 1f,
                $"rig: he goes left, leaning into it ({LeanOf(rig.Presenter):F1}°), a little wider");
            rig.Call("EndStageShape");
            // The same from Right leans the other way.
            rig.Call("MoveToPosition", CharacterPosition.Right, (bool?)true);
            object slideRight = BeginLeg(rig, StageLeg.SlideOut, CharacterPosition.Off, CharacterPosition.Right);
            PoseLeg(rig, slideRight, StageMath.SlideOutSeconds * 0.6f);
            log.IsTrue(LeanOf(rig.Presenter) < -0.5f && rig.Presenter.position.x > rig.Right.position.x,
                $"rig: leaving to the right he leans to the right ({LeanOf(rig.Presenter):F1}°)");

            // A leg cut short by a ,Cut: his own shape, upright, on that frame.
            rig.Call("EndStageShape");
            log.IsTrue(rig.Presenter.localScale == Vector3.one && Quaternion.Angle(rig.Presenter.rotation, Quaternion.identity) < 1e-3f,
                "rig: a leg that a cut ends leaves no squash and no lean behind");
            log.IsTrue(!rig.Get<bool>("stageShaped"), "rig: …and the stage knows his shape is his own again");

            // A peek: the frame's bottom edge is the line that holds, and his peek size is kept apart from the squash.
            rig.Call("MoveToPosition", CharacterPosition.Off, (bool?)true);
            StageMath.PeekPose peek = StageMath.Peek(true, body, frame);
            object rise = BeginLeg(rig, StageLeg.RiseToPeek, CharacterPosition.PeekRight, CharacterPosition.Off);
            float landedAt = StageMath.PeekRiseSeconds * (StageMoves.ArrivePeakAt + (1f - StageMoves.ArrivePeakAt) * 0.5f);   // halfway through the settle
            StageMoves.Pose landed = StageMoves.PeekRise(landedAt);
            PoseLeg(rig, rise, landedAt);
            log.Near(peek.Scale * landed.ScaleX, rig.Presenter.localScale.x, 1e-5f, "rig: a peek's squash multiplies the peek's own scale");
            log.Near(peek.Scale, (float)rig.Call("PresenterScale"), 1e-6f, "rig: …and his size on the stage still reads as the peek's (not the squashed width)");
            // The part of him on the frame edge (where the travel has him, in his own shape) is still on it.
            float travelY = Vector2.LerpUnclamped(peek.Hidden, peek.Rest, landed.Travel).y;
            float onEdgeLocal = (frame.Bottom - travelY) / peek.Scale;   // in the transform's own units
            Vector2 onEdge = rig.Presenter.TransformPoint(new Vector3(0f, onEdgeLocal, 0f));
            log.Near(frame.Bottom, onEdge.y, 1e-4f, "rig: the line of him on the frame's bottom edge stays on the edge while he squashes");

            PoseLeg(rig, rise, StageMath.PeekRiseSeconds);
            log.IsTrue(((Vector2)rig.Presenter.position - peek.Rest).magnitude < 1e-5f, "rig: the rise ends exactly in the peek pose");
            log.Near(peek.Scale, rig.Presenter.localScale.y, 1e-6f, "rig: …at the peek's scale, unsquashed");
            rig.Call("EndStageShape");

            // ,Cut is unchanged: no move, no shape.
            rig.Set("currentPosition", CharacterPosition.PeekRight);
            rig.Call("MoveToPosition", CharacterPosition.Center, (bool?)true);
            log.IsTrue(((Vector2)rig.Presenter.position - (Vector2)rig.Center.position).magnitude < 1e-5f &&
                       rig.Presenter.localScale == Vector3.one && !rig.Mps.IsPresenterMoving,
                "rig: {Position:Center,Cut} from a peek puts him on his mark at his size on that frame");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig.Root);
        }
    }

    // A move that arrives while another is running never jumps: for every
    // leg, caught at 25 %, 50 % and 75 %, either the first pose of whatever
    // comes next IS the pose he was in — or he was leaving, and the leg that
    // was taking him out carries on untouched before the arrival begins.
    static void Tier5_ChainOnTheRig_Engine(Log log)
    {
        StageRig rig = BuildStageRig();
        try
        {
            StageMath.Frame frame = TestFrame;
            StageMath.Body body = StageMath.DefaultBody;
            CharacterPosition L = CharacterPosition.Left, R = CharacterPosition.Right, C = CharacterPosition.Center,
                              Off = CharacterPosition.Off, PL = CharacterPosition.PeekLeft, PR = CharacterPosition.PeekRight;

            // (what is running: he stood at `stood`, the tag was `to`) → the tags that may arrive meanwhile,
            // each with its plan (">" first: the running leg is finished before it).
            var running = new (string name, CharacterPosition stood, CharacterPosition to, StageLeg leg, (CharacterPosition next, string want)[] nexts)[]
            {
                ("a pop-up at Center", Off, C, StageLeg.PopUp,
                    new[] { (Off, "SlideOut,Park"), (PR, "SlideOut,RiseToPeek"), (PL, "SlideOut,RiseToPeek"), (L, "ToFull"), (R, "ToFull") }),
                ("a pop-up at Left", Off, L, StageLeg.PopUp,
                    new[] { (Off, "SlideOut,Park"), (PL, "SlideOut,RiseToPeek"), (PR, "SlideOut,RiseToPeek"), (C, "ToFull") }),
                ("a slide-out down from Center", C, Off, StageLeg.SlideOut,
                    new[] { (C, "ToFull"), (L, ">PopUp"), (PR, ">RiseToPeek") }),
                ("a slide-out from Left", L, Off, StageLeg.SlideOut,
                    new[] { (L, "ToFull"), (C, ">PopUp"), (PL, ">RiseToPeek") }),
                ("a peek rising (right)", Off, PR, StageLeg.RiseToPeek,
                    new[] { (Off, "DropFromPeek,Park"), (C, "DropFromPeek,PopUp"), (R, "DropFromPeek,PopUp"), (L, "DropFromPeek,PopUp"), (PL, "DropFromPeek,RiseToPeek") }),
                ("a peek dropping (right)", PR, Off, StageLeg.DropFromPeek,
                    new[] { (PR, "ToPeek"), (C, ">PopUp"), (PL, ">RiseToPeek") }),
                ("a peek dropping on his way to Center", PR, C, StageLeg.DropFromPeek,
                    new[] { (Off, ">Park"), (PR, "ToPeek"), (PL, ">RiseToPeek"), (L, ">PopUp") }),
                ("Center sliding out on his way to a peek", C, PR, StageLeg.SlideOut,
                    new[] { (Off, ">Park"), (C, "ToFull"), (R, ">PopUp"), (PL, ">RiseToPeek") }),
            };

            foreach (var (name, stood, to, leg, nexts) in running)
                foreach (float share in new[] { 0.25f, 0.5f, 0.75f })
                    foreach (var (next, want) in nexts)
                    {
                        // He stands at `stood`; the move to `to` runs and is `share` of the way through its leg.
                        rig.Set("currentPosition", Off);   // (so the cut below always places him, whatever the last round left)
                        rig.Call("MoveToPosition", stood, (bool?)true);
                        rig.Set("currentPosition", to);
                        object first = BeginLeg(rig, leg, to, stood);
                        rig.Set("stageRun", first);
                        rig.Set("stageMoving", true);
                        PoseLeg(rig, first, StageMath.LegSeconds(leg) * share);

                        Vector3 pos = rig.Presenter.position, scale = rig.Presenter.localScale;
                        Quaternion rot = rig.Presenter.rotation;
                        bool wasInView = StageMath.InView(pos, body, frame, (float)rig.Call("PresenterScale"));
                        string what = $"{name} caught at {share * 100f:F0} % by {{Position:{next}}}";
                        log.IsTrue(wasInView, $"chain: {what} — (he is in view)");

                        // The next tag: planned from where he is.
                        List<StageLeg> legs;
                        CharacterPosition exitAs;
                        object finishFirst;
                        if (StageMath.IsFull(to) && StageMath.IsFull(next))
                        {
                            var args = new object[] { next, null, null };   // (MoveToPosition's side path: RedirectStageArrival)
                            legs = (List<StageLeg>)rig.Call("PlanStageRedirect", args);
                            exitAs = (CharacterPosition)args[1];
                            finishFirst = args[2];
                        }
                        else
                        {
                            var args = new object[] { to, next, null, null };
                            legs = (List<StageLeg>)rig.Call("PlanStageMove", args);
                            exitAs = (CharacterPosition)args[2];
                            finishFirst = args[3];
                        }
                        bool finishes = want.StartsWith(">");
                        log.AreEqual(finishes ? want.Substring(1) : want, Legs(legs), $"chain: {what} — the plan");
                        log.IsTrue((finishFirst != null) == finishes, $"chain: {what} — " + (finishes ? "he finishes leaving first" : "it starts from where he is"));
                        log.IsTrue((rig.Presenter.position - pos).magnitude < 1e-6f && (rig.Presenter.localScale - scale).magnitude < 1e-6f,
                            $"chain: {what} — planning moves nothing");
                        if (legs.Count == 0) continue;

                        if (leg == StageLeg.PopUp && next == Off)
                            log.AreEqual(C, exitAs, $"chain: {what} — he goes back down the way he came");

                        object second = null;
                        if (finishFirst != null)
                        {
                            // The leg that was taking him out is the very same run, and it carries on to its end.
                            log.IsTrue(ReferenceEquals(finishFirst, first), $"chain: {what} — the leg that carries on is the one that was running");
                            log.AreEqual(stood, exitAs, $"chain: {what} — through the edge it was taking him to");
                            PoseLeg(rig, first, StageMath.LegSeconds(leg) * (share + 0.05f));
                            float step = (rig.Presenter.position - pos).magnitude;
                            PoseLeg(rig, first, StageMath.LegSeconds(leg));
                            float whole = (rig.Presenter.position - pos).magnitude;
                            log.IsTrue(step > 0f && step < whole * 0.5f, $"chain: {what} — its next frame is a small step on, not a restart");
                            log.IsTrue(!StageMath.InView(rig.Presenter.position, body, frame, (float)rig.Call("PresenterScale")),
                                $"chain: {what} — and it takes him out of the frame");
                            rig.Set("stageShaped", false);   // (what the travel does when a leg ends)
                            if (legs[0] != StageLeg.Park)
                            {
                                second = BeginLeg(rig, legs[0], next, exitAs);
                                PoseLeg(rig, second, 0f);
                                log.IsTrue(!StageMath.InView(rig.Presenter.position, body, frame, (float)rig.Call("PresenterScale")),
                                    $"chain: {what} — the arrival then starts out of sight");
                            }
                        }
                        else
                        {
                            second = BeginLeg(rig, legs[0], next, exitAs);
                            PoseLeg(rig, second, 0f);
                            log.IsTrue((rig.Presenter.position - pos).magnitude < 1e-4f, $"chain: {what} — the next move's first frame is where he was");
                            log.IsTrue((rig.Presenter.localScale - scale).magnitude < 1e-5f, $"chain: {what} — …at the size and shape he had");
                            log.IsTrue(Quaternion.Angle(rig.Presenter.rotation, rot) < 1e-2f, $"chain: {what} — …leaning as he was");
                        }

                        // …and it still ends exactly where that move ends, in his own shape.
                        if (second != null) PoseLeg(rig, second, StageMath.LegSeconds(legs[0]));
                        Vector3 end = rig.Presenter.localScale;
                        log.IsTrue(Mathf.Abs(end.x - end.y) < 1e-6f && Quaternion.Angle(rig.Presenter.rotation, Quaternion.identity) < 1e-3f,
                            $"chain: {what} — it ends unsquashed and upright");
                        if (legs[0] == StageLeg.ToFull || legs[0] == StageLeg.PopUp)
                        {
                            Vector3 mark = next == L ? rig.Left.position : next == R ? rig.Right.position : rig.Center.position;
                            log.IsTrue((rig.Presenter.position - mark).magnitude < 1e-4f && Mathf.Abs(end.x - 1f) < 1e-6f,
                                $"chain: {what} — on his mark at full size");
                        }
                        else if (legs[0] == StageLeg.ToPeek || legs[0] == StageLeg.RiseToPeek)
                        {
                            StageMath.PeekPose peek = StageMath.Peek(next == PR, body, frame);
                            log.IsTrue(((Vector2)rig.Presenter.position - peek.Rest).magnitude < 1e-4f && Mathf.Abs(end.x - peek.Scale) < 1e-5f,
                                $"chain: {what} — in the peek pose at the peek's size");
                        }
                        else
                        {
                            log.IsTrue(!StageMath.InView(rig.Presenter.position, body, frame, (float)rig.Call("PresenterScale")),
                                $"chain: {what} — out of the frame");
                        }

                        rig.Set("stageRun", null);
                        rig.Set("stageMoving", false);
                        rig.Call("EndStageShape");
                    }

            // Once he is out of sight the arrival simply starts: nothing is waited for.
            {
                rig.Set("currentPosition", Off);
                rig.Call("MoveToPosition", PR, (bool?)true);
                rig.Set("currentPosition", C);
                object drop = BeginLeg(rig, StageLeg.DropFromPeek, C, PR);
                rig.Set("stageRun", drop);
                rig.Set("stageMoving", true);
                PoseLeg(rig, drop, StageMath.PeekDropSeconds * 0.999f);
                log.IsTrue(!StageMath.InView(rig.Presenter.position, body, frame, (float)rig.Call("PresenterScale")), "chain: (at the very end of the drop he is out of sight)");
                var args = new object[] { C, PL, null, null };
                var legs = (List<StageLeg>)rig.Call("PlanStageMove", args);
                log.IsTrue(Legs(legs) == "RiseToPeek" && args[3] == null, "chain: out of sight below a peek, {Position:PeekLeft} brings the other peek up at once");
                rig.Set("stageRun", null);
                rig.Set("stageMoving", false);
                rig.Call("EndStageShape");
            }

            // A side glide that a stage move cuts short: he leaves through the edge nearest to where he is.
            foreach (var (x, edge) in new[] { (-6f, L), (-2f, C), (5f, R) })
            {
                rig.Call("MoveToPosition", R, (bool?)true);
                rig.Presenter.position = new Vector3(x, rig.Right.position.y, 0f);
                rig.Set("presenterGliding", true);
                var args = new object[] { R, Off, null, null };
                var legs = (List<StageLeg>)rig.Call("PlanStageMove", args);
                log.AreEqual("SlideOut,Park", Legs(legs), $"chain: a glide to Right cut short at x {x:F0} by {{Position:Off}} slides out from there");
                log.AreEqual(edge, (CharacterPosition)args[2], $"chain: …through the nearest edge ({(edge == C ? "down" : edge.ToString())})");
                rig.Set("presenterGliding", false);
            }
            // At rest nothing changes: his position's own edge.
            foreach (CharacterPosition p in new[] { L, R, C })
            {
                rig.Call("MoveToPosition", p, (bool?)true);
                var args = new object[] { p, Off, null, null };
                rig.Call("PlanStageMove", args);
                log.AreEqual(p, (CharacterPosition)args[2], $"chain: at rest at {p} he leaves the way he always did");
                log.IsTrue(args[3] == null, $"chain: …and there is nothing to finish first ({p})");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig.Root);
        }
    }
    // -----------------------------------------------------------------------
    // Step 3: the dash
    // -----------------------------------------------------------------------

    static void Tier5_DashPose(Log log)
    {
        float total = DashMath.TotalSeconds;
        float anticipation = DashMath.AnticipationShare * total, settle = DashMath.SettleShare * total;
        float go = total - anticipation - settle;

        log.Near(0.45f, total, 1e-6f, "dash: 0.45 s in all");
        log.IsTrue(total <= 0.5f, "dash: …under half a second");
        log.Near(0.07f, anticipation, 1e-6f, "dash: a 0.07 s anticipation");
        log.Near(1f, DashMath.AnticipationShare + DashMath.GoShare + DashMath.SettleShare, 1e-6f, "dash: anticipation, travel and settle add up to the whole move");
        log.Near(0.12f, DashMath.TurnShare * total, 1e-6f, "dash: the turn takes 0.12 s");

        foreach (bool turns in new[] { false, true })
        {
            string which = turns ? "with a turn" : "without a turn";
            DashMath.Pose start = DashMath.At(0f, turns);
            log.IsTrue(start.Travel == 0f && start.ScaleX == 1f && start.ScaleY == 1f && start.Tilt == 0f && start.Width == 1f && !start.Turned,
                $"dash ({which}): starts from where and how he stands");
            DashMath.Pose end = DashMath.At(total, turns);
            log.IsTrue(end.Travel == 1f && end.ScaleX == 1f && end.ScaleY == 1f && end.Tilt == 0f && end.Width == 1f && end.Turned == turns,
                $"dash ({which}): ends on exactly his mark — position, scale, upright, full width");

            DashMath.Pose gathered = DashMath.At(anticipation, turns);
            log.Near(-DashMath.AnticipationTravel, gathered.Travel, 1e-5f, $"dash ({which}): the anticipation takes him a little the other way");
            log.Near(-DashMath.AnticipationTilt, gathered.Tilt, 1e-3f, $"dash ({which}): …leaning away from where he is going");
            log.IsTrue(gathered.ScaleY < 1f && gathered.ScaleX > 1f, $"dash ({which}): …with a squash");

            float half = DashMath.At(anticipation + go * 0.5f, turns).Travel;
            log.IsTrue(half >= 0.70f, $"dash ({which}): a fast start that eases out — {half * 100f:F0} % of the distance in the first half of the travel time (70 % at least)");

            float furthest = 0f, lowest = 0f, mostLean = 0f, leastLean = 0f, longest = 1f, previous = 0f, biggest600 = 0f;
            bool inLimits = true, slowing = true;
            float previousSpeed = float.PositiveInfinity;
            for (float t = 0f; t < total; t += MotionStep)
            {
                DashMath.Pose p = DashMath.At(t, turns);
                furthest = Mathf.Max(furthest, p.Travel);
                lowest = Mathf.Min(lowest, p.Travel);
                mostLean = Mathf.Max(mostLean, p.Tilt);
                leastLean = Mathf.Min(leastLean, p.Tilt);
                longest = Mathf.Max(longest, p.ScaleX);
                biggest600 = Mathf.Max(biggest600, Mathf.Abs(p.Travel - previous));
                if (p.ScaleX > 1f + DashMath.Stretch + 1e-4f || p.ScaleX < 1f - 1e-5f) inLimits = false;
                if (p.ScaleY > 1f + 1e-5f || p.ScaleY < 1f - DashMath.LandSquash - 1e-4f) inLimits = false;
                if (p.Width < 0f || p.Width > 1f) inLimits = false;
                // Past the first third of the travel he only slows down.
                if (t > anticipation + go * 0.34f && t < anticipation + go)
                {
                    float speed = p.Travel - previous;
                    if (speed > previousSpeed + 1e-5f) slowing = false;
                    previousSpeed = speed;
                }
                previous = p.Travel;
            }
            log.IsTrue(furthest > 1f && furthest <= 1.03f, $"dash ({which}): he passes his mark by {(furthest - 1f) * 100f:F1} % of the distance (3 % at most)");
            log.IsTrue(lowest >= -DashMath.AnticipationTravel - 1e-5f, $"dash ({which}): never further back than the anticipation");
            log.Near(DashMath.Lean, mostLean, 0.05f, $"dash ({which}): he leans up to 5° into the travel");
            log.IsTrue(leastLean >= -DashMath.AnticipationTilt - 1e-3f, $"dash ({which}): …and never further away from it than the anticipation's lean");
            log.Near(1f + DashMath.Stretch, longest, 0.002f, $"dash ({which}): stretched up to 6 % along it");
            log.IsTrue(inLimits, $"dash ({which}): inside the stretch and squash limits for every t");
            log.IsTrue(slowing, $"dash ({which}): it eases out — after its first third the travel only slows");
            log.IsTrue(biggest600 < 0.03f, $"dash ({which}): no jump anywhere in it (largest step at 600 samples a second: {biggest600 * 100f:F2} %)");

            float travelRate = Mathf.Abs(DashMath.At(total - 0.001f, turns).Travel - 1f) / 0.001f;
            float scaleRate = Mathf.Abs(DashMath.At(total - 0.001f, turns).ScaleY - 1f) / 0.001f;
            float tiltRate = Mathf.Abs(DashMath.At(total - 0.001f, turns).Tilt) / 0.001f;
            log.IsTrue(travelRate < 0.1f && scaleRate < 0.1f && tiltRate < 2f,
                $"dash ({which}): no speed left at the end (travel {travelRate:F3} /s, shape {scaleRate:F3} /s, lean {tiltRate:F2} °/s a millisecond before)");

            float biggest = 0f, prev30 = 0f;
            for (float t = 0f; t < total; t += 1f / 30f)
            {
                float travel = DashMath.At(t, turns).Travel;
                biggest = Mathf.Max(biggest, Mathf.Abs(travel - prev30));
                prev30 = travel;
            }
            log.IsTrue(biggest < 0.25f, $"dash ({which}): no frame at 30 fps moves him more than a quarter of the way (largest {biggest * 100f:F0} %)");
        }

        // The turn: his width goes to nothing and back, and he is mirrored only at nothing.
        bool noTurn = true;
        for (float t = 0f; t <= total; t += MotionStep)
        {
            DashMath.Pose p = DashMath.At(t, false);
            if (p.Width != 1f || p.Turned) noTurn = false;
        }
        log.IsTrue(noTurn, "dash: a move that doesn't change his facing has no turn (full width, never mirrored)");

        const float fine = 1f / 12000f;
        float turnStart = (DashMath.TurnAt - DashMath.TurnShare * 0.5f) * total, turnEnd = (DashMath.TurnAt + DashMath.TurnShare * 0.5f) * total;
        float narrowest = 1f, widthAtFlip = 1f, widthBeforeFlip = 1f, flipAt = float.NaN;
        bool onlyInWindow = true, onceOnly = true, wasTurned = false;
        float lastWidth = 1f;
        for (float t = 0f; t <= total; t += fine)
        {
            DashMath.Pose p = DashMath.At(t, true);
            narrowest = Mathf.Min(narrowest, p.Width);
            if (p.Width < 1f - 1e-4f && (t < turnStart - 1e-4f || t > turnEnd + 1e-4f)) onlyInWindow = false;
            if (p.Turned && !wasTurned) { flipAt = t; widthAtFlip = p.Width; widthBeforeFlip = lastWidth; }
            if (!p.Turned && wasTurned) onceOnly = false;
            wasTurned = p.Turned;
            lastWidth = p.Width;
        }
        log.IsTrue(narrowest < 0.01f, $"dash: in a turn his width goes to zero (narrowest sampled: {narrowest:F4})");
        log.IsTrue(onlyInWindow, "dash: …only inside the 0.12 s of the turn");
        log.IsTrue(widthAtFlip < 0.01f && widthBeforeFlip < 0.01f, $"dash: the sprite is mirrored only at zero width ({widthBeforeFlip:F4} → {widthAtFlip:F4} across the flip)");
        log.Near(DashMath.TurnAt * total, flipAt, 2f * fine, "dash: …at the middle of the turn");
        log.IsTrue(onceOnly, "dash: …once");
        log.Near(0.5f, DashMath.At(turnStart + (turnEnd - turnStart) / 3f, true).Width, 0.01f, "dash: a third of the way into the turn he is at half his width (a turning sheet of paper)");

        // The three dials.
        DashMath.Pose slow = DashMath.At(0.14f, 0.90f, DashMath.Lean, DashMath.Stretch, false);
        log.Near(-DashMath.AnticipationTravel, slow.Travel, 1e-5f, "dash: at twice the duration the anticipation ends twice as late (the durations add up to the dial)");
        log.IsTrue(DashMath.At(0.85f, 0.90f, DashMath.Lean, DashMath.Stretch, false).Travel > 1f,
            "dash: …and he is still settling back onto his mark at 0.85 s");
        DashMath.Pose slowEnd = DashMath.At(0.90f, 0.90f, DashMath.Lean, DashMath.Stretch, false);
        log.IsTrue(slowEnd.Travel == 1f && slowEnd.ScaleX == 1f && slowEnd.Tilt == 0f, "dash: …at rest exactly when the dial's time is up");
        float stiffLean = 0f, stiffLong = 1f;
        for (float t = 0f; t < total; t += MotionStep)
        {
            DashMath.Pose p = DashMath.At(t, total, 0f, 0f, false);
            stiffLean = Mathf.Max(stiffLean, p.Tilt);
            stiffLong = Mathf.Max(stiffLong, p.ScaleX);
        }
        log.IsTrue(stiffLean <= 1e-4f, "dash: with the lean dial at 0 he never leans into the travel");
        log.IsTrue(stiffLong <= 1f + DashMath.LandSquash * 0.5f + 1e-4f, "dash: with the stretch dial at 0 he doesn't stretch");
        DashMath.Pose instant = DashMath.At(0.1f, 0f, DashMath.Lean, DashMath.Stretch, true);
        log.IsTrue(instant.Travel == 1f && instant.Width == 1f && instant.Turned, "dash: a zero-length dash is simply there, turned");
    }

    static void Tier5_HoldsAreCounted(Log log)
    {
        PresenterHolds.Reset();
        log.IsTrue(PresenterHolds.Count == 0 && PresenterHolds.TotalSeconds == 0f && PresenterHolds.LongestSeconds == 0f, "holds: a take starts with none");
        PresenterHolds.Add(1.04f);
        PresenterHolds.Add(0.50f);
        PresenterHolds.Add(0f);
        PresenterHolds.Add(-1f);
        log.AreEqual(2, PresenterHolds.Count, "holds: each wait for the presenter to land is counted (a zero-length one is not)");
        log.Near(1.54f, PresenterHolds.TotalSeconds, 1e-5f, "holds: …their total");
        log.Near(1.04f, PresenterHolds.LongestSeconds, 1e-5f, "holds: …and the longest");
        PresenterHolds.Reset();
        log.AreEqual(0, PresenterHolds.Count, "holds: the next take starts from zero again");
    }

    // Glide is the default and the old move; Dash only when the main menu says so.
    static void Tier5_SideMoveChoice_Engine(Log log)
    {
        bool had = PlayerPrefs.HasKey(StagePrefs.SideMoveKey);
        int saved = PlayerPrefs.GetInt(StagePrefs.SideMoveKey, 0);
        StageRig rig = BuildStageRig();
        try
        {
            rig.Mps.transitionDuration = 1f;   // the recording scene's value

            PlayerPrefs.DeleteKey(StagePrefs.SideMoveKey);
            log.IsTrue(!StagePrefs.SideMoveIsDash, "side move: with nothing saved the choice is Glide");
            rig.Call("BeginStageRun");
            log.IsTrue(!rig.Mps.SideMoveIsDash, "side move: a take then glides");
            log.Near(1f, rig.Mps.SideMoveSeconds, 1e-6f, "side move: …for the scene's transitionDuration");
            log.Near(1.25f, rig.Mps.PresenterLandCap, 1e-6f, "side move: …and nothing waits longer than 1.25 s for him, as before");
            object glide = rig.Call("SideMove", rig.Left, rig.Right, CharacterPosition.Right);
            log.IsTrue(glide != null && glide.GetType().Name.Contains("MoveAvatar"),
                $"side move: Glide runs MoveAvatar, exactly as before ({glide?.GetType().Name})");

            StagePrefs.SideMoveIsDash = true;
            log.AreEqual(1, PlayerPrefs.GetInt(StagePrefs.SideMoveKey, 0), "side move: Dash is saved as 1");
            rig.Call("BeginStageRun");
            log.IsTrue(rig.Mps.SideMoveIsDash, "side move: with Dash chosen a take dashes");
            log.Near(DashMath.TotalSeconds, rig.Mps.SideMoveSeconds, 1e-6f, "side move: …for the dash dial's 0.45 s");
            log.Near(StageMath.LongestArrivalSeconds + 0.25f, rig.Mps.PresenterLandCap, 1e-6f,
                "side move: …and nothing waits longer than 1.25 s for him — the cap still covers his longest stage arrival (out of a peek, then the pop-up)");
            object dash = rig.Call("SideMove", rig.Left, rig.Right, CharacterPosition.Right);
            log.IsTrue(dash != null && dash.GetType().Name.Contains("DashAvatar"), $"side move: Dash runs DashAvatar ({dash?.GetType().Name})");
            log.Near(DashMath.Lean, rig.Mps.dashLean, 1e-6f, "side move: the lean dial starts at 5°");
            log.Near(DashMath.Stretch, rig.Mps.dashStretch, 1e-6f, "side move: the stretch dial at 6 %");

            StagePrefs.SideMoveIsDash = false;
            rig.Call("BeginStageRun");
            log.IsTrue(!rig.Mps.SideMoveIsDash, "side move: back to Glide for the next take");
        }
        finally
        {
            if (had) PlayerPrefs.SetInt(StagePrefs.SideMoveKey, saved); else PlayerPrefs.DeleteKey(StagePrefs.SideMoveKey);
            PlayerPrefs.Save();
            UnityEngine.Object.DestroyImmediate(rig.Root);
        }
    }

    static void Tier5_DashOnTheRig_Engine(Log log)
    {
        StageRig rig = BuildStageRig();
        try
        {
            StageMath.Body body = StageMath.DefaultBody;
            float feet = body.CentreOffset.y - body.Size.y * 0.5f, total = DashMath.TotalSeconds;

            // His sprite, so there is a facing to turn.
            var spriteGo = new GameObject("Character");
            spriteGo.transform.SetParent(rig.Presenter, false);
            var sprite = spriteGo.AddComponent<SpriteRenderer>();
            rig.Mps.characterRenderer = sprite;

            // Left → Right: his facing changes, so he turns.
            rig.Call("MoveToPosition", CharacterPosition.Left, (bool?)true);
            log.IsTrue(!sprite.flipX, "dash rig: at Left he isn't mirrored");
            object run = rig.Call("BeginDash", rig.Right, CharacterPosition.Right);
            log.IsTrue(run != null, "dash rig: Left → Right is a dash");
            Vector2 a = rig.Left.position, b = rig.Right.position;

            rig.Call("PoseDash", run, 0f);
            log.IsTrue(((Vector2)rig.Presenter.position - a).magnitude < 1e-5f && rig.Presenter.localScale == Vector3.one,
                "dash rig: its first frame is where and how he stands");

            bool feetOnLine = true, mirroredOnlyNarrow = true, monotone = true;
            bool flipped = false;
            float narrowestSeen = 1f, lastX = a.x - 1f;
            for (float t = 0f; t < total; t += 1f / 240f)
            {
                DashMath.Pose pose = DashMath.At(t, true);
                rig.Call("PoseDash", run, t);
                Vector2 travelled = Vector2.LerpUnclamped(a, b, pose.Travel);
                Vector2 feetNow = rig.Presenter.TransformPoint(new Vector3(0f, feet, 0f));
                if ((feetNow - new Vector2(travelled.x, travelled.y + feet)).magnitude > 1e-3f) feetOnLine = false;
                if (sprite.flipX != flipped)
                {
                    // The frame the sprite is mirrored on: he is edge-on (at 240 samples a second, nearly).
                    if (rig.Presenter.localScale.x > 0.15f) mirroredOnlyNarrow = false;
                    flipped = sprite.flipX;
                }
                narrowestSeen = Mathf.Min(narrowestSeen, rig.Presenter.localScale.x);
                if (t > DashMath.AnticipationShare * total && t < (1f - DashMath.SettleShare) * total)
                {
                    if (travelled.x < lastX - 1e-4f) monotone = false;
                    lastX = travelled.x;
                }
            }
            log.IsTrue(feetOnLine, "dash rig: through the whole dash his feet stay on their line (lean, stretch and squash turn about them)");
            log.IsTrue(flipped && mirroredOnlyNarrow, "dash rig: the sprite is mirrored once, on a frame where he is edge-on");
            log.IsTrue(narrowestSeen < 0.15f && narrowestSeen > 0f, $"dash rig: his width closes for the turn (narrowest {narrowestSeen:F3}) and never reaches a zero scale");
            log.IsTrue(monotone, "dash rig: between the anticipation and the stop he only moves towards his mark");

            rig.Call("EndDash", run);
            log.IsTrue(((Vector2)rig.Presenter.position - b).magnitude < 1e-6f && rig.Presenter.localScale == Vector3.one &&
                       Quaternion.Angle(rig.Presenter.rotation, Quaternion.identity) < 1e-3f && sprite.flipX,
                "dash rig: it ends exactly on his mark — position, rotation, scale — facing the new way");
            log.IsTrue(!rig.Get<bool>("stageShaped") && !rig.Get<bool>("presenterGliding"), "dash rig: …and nothing waits on him any more");

            // Left → Center: same facing, no turn.
            rig.Set("currentPosition", CharacterPosition.Right);
            rig.Call("MoveToPosition", CharacterPosition.Left, (bool?)true);
            object straight = rig.Call("BeginDash", rig.Center, CharacterPosition.Center);
            bool neverNarrow = true, neverMirrored = true;
            for (float t = 0f; t < total; t += 1f / 240f)
            {
                rig.Call("PoseDash", straight, t);
                if (rig.Presenter.localScale.x < 0.99f) neverNarrow = false;
                if (sprite.flipX) neverMirrored = false;
            }
            log.IsTrue(neverNarrow && neverMirrored, "dash rig: Left → Center keeps his facing — no turn, never narrower, never mirrored");

            // Leaning: to the right he leans to the right, and away from it first.
            rig.Call("PoseDash", straight, DashMath.AnticipationShare * total);
            log.Near(DashMath.AnticipationTilt, LeanOf(rig.Presenter), 1e-2f, "dash rig: going right he first leans left (counter-clockwise)");
            rig.Call("PoseDash", straight, DashMath.AnticipationShare * total + 0.05f);
            log.IsTrue(LeanOf(rig.Presenter) < -2f, $"dash rig: …then right, into the dash ({LeanOf(rig.Presenter):F1}°)");

            // A dash that another {Position:} cuts short: the next one starts on the frame he is on.
            rig.Call("PoseDash", straight, total * 0.4f);
            Vector3 pos = rig.Presenter.position, scale = rig.Presenter.localScale;
            Quaternion rot = rig.Presenter.rotation;
            object back = rig.Call("BeginDash", rig.Right, CharacterPosition.Right);
            rig.Call("PoseDash", back, 0f);
            log.IsTrue((rig.Presenter.position - pos).magnitude < 1e-4f && (rig.Presenter.localScale - scale).magnitude < 1e-4f &&
                       Quaternion.Angle(rig.Presenter.rotation, rot) < 1e-2f,
                "dash rig: a dash that cuts a dash short starts from where he is, stretched and leaning as he was");
            rig.Call("EndDash", back);
            log.IsTrue(((Vector2)rig.Presenter.position - b).magnitude < 1e-6f && rig.Presenter.localScale == Vector3.one, "dash rig: …and still ends exactly on its mark");

            // …and so does a stage move.
            rig.Set("currentPosition", CharacterPosition.Left);
            rig.Call("MoveToPosition", CharacterPosition.Right, (bool?)true);
            object across = rig.Call("BeginDash", rig.Left, CharacterPosition.Left);
            rig.Call("PoseDash", across, total * 0.5f);
            rig.Set("currentPosition", CharacterPosition.Left);
            rig.Set("presenterGliding", true);
            pos = rig.Presenter.position; scale = rig.Presenter.localScale; rot = rig.Presenter.rotation;
            var args = new object[] { CharacterPosition.Left, CharacterPosition.Off, null, null };
            var legs = (List<StageLeg>)rig.Call("PlanStageMove", args);
            object slide = BeginLeg(rig, legs[0], CharacterPosition.Off, (CharacterPosition)args[2]);
            PoseLeg(rig, slide, 0f);
            log.IsTrue((rig.Presenter.position - pos).magnitude < 1e-4f && (rig.Presenter.localScale - scale).magnitude < 1e-4f &&
                       Quaternion.Angle(rig.Presenter.rotation, rot) < 1e-2f,
                "dash rig: {Position:Off} during a dash slides him out from where he is, as he is");
            PoseLeg(rig, slide, StageMath.SlideOutSeconds);
            log.IsTrue(Mathf.Abs(rig.Presenter.localScale.x - 1f) < 1e-5f && Quaternion.Angle(rig.Presenter.rotation, Quaternion.identity) < 1e-3f,
                "dash rig: …and he is his own shape again by the time he is out");
            rig.Set("presenterGliding", false);
            rig.Call("EndStageShape");

            // A cut during a dash: on his mark, his own shape, on that frame.
            rig.Set("currentPosition", CharacterPosition.Right);
            rig.Call("MoveToPosition", CharacterPosition.Left, (bool?)true);
            object cutShort = rig.Call("BeginDash", rig.Right, CharacterPosition.Right);
            rig.Call("PoseDash", cutShort, total * 0.56f);
            rig.Set("currentPosition", CharacterPosition.Right);
            rig.Call("MoveToPosition", CharacterPosition.Center, (bool?)true);
            log.IsTrue(((Vector2)rig.Presenter.position - (Vector2)rig.Center.position).magnitude < 1e-6f && rig.Presenter.localScale == Vector3.one &&
                       Quaternion.Angle(rig.Presenter.rotation, Quaternion.identity) < 1e-3f && !sprite.flipX,
                "dash rig: {Position:Center,Cut} during a dash (mid-turn) puts him on his mark, full width, upright, facing the camera");

            // A repeat of the mark he stands on is no dash at all.
            log.IsTrue(rig.Call("BeginDash", rig.Center, CharacterPosition.Center) == null, "dash rig: a repeat of his own mark goes nowhere (no lean-away on the spot)");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rig.Root);
        }
    }
    // -----------------------------------------------------------------------
    // Step 4: the handover — {Position:…,Over}
    // -----------------------------------------------------------------------

    static void Tier5_OverWordsParseAndStamp(Log log)
    {
        Regex tag = MediaPresentationSystem.PositionTagRegex;

        // The runtime parser's shape: side, Cut|Smooth, Over, a word after Over, the baked time.
        Match m = tag.Match("{Position:Center,Over}");
        log.IsTrue(m.Success && m.Groups[1].Value == "Center" && m.Groups[3].Success && !m.Groups[2].Success && !m.Groups[4].Success,
            "over: {Position:Center,Over} is a position tag with the word Over");
        m = tag.Match("{Position:PeekRight,Over,T=12.400}");
        log.IsTrue(m.Success && m.Length == "{Position:PeekRight,Over,T=12.400}".Length && m.Groups[1].Value == "PeekRight" &&
                   m.Groups[3].Success && m.Groups[5].Value == "12.400", "over: …and with its baked time, on a peek");
        m = tag.Match("{Position:Left,Cut,T=3.000}");
        log.IsTrue(m.Success && m.Groups[2].Value == "Cut" && !m.Groups[3].Success && m.Groups[5].Value == "3.000", "over: {Position:Left,Cut} reads as it always did");
        m = tag.Match("{Position:Right}");
        log.IsTrue(m.Success && !m.Groups[2].Success && !m.Groups[3].Success, "over: a bare {Position:Right} reads as it always did");
        m = tag.Match("{Position:Center,Over,Cut,T=1.000}");
        log.IsTrue(m.Success && m.Groups[3].Success && m.Groups[4].Value == "Cut" && m.Groups[5].Value == "1.000", "over: {Position:Center,Over,Cut} is read as Over with a third word");
        foreach (string old in new[] { "{Position:Left,Foo}", "{Position:Left,Cut,Over}", "{Position:Left,Smooth,Cut}" })
        {
            m = tag.Match(old);
            log.IsTrue(!m.Success || m.Length != old.Length, $"over: {old} is not a position tag (exactly as before the word existed)");
        }

        StageMath.ReadPositionWords("", "Over", "", out bool? cut, out bool over, out string warning);
        log.IsTrue(cut == null && over && warning == null, "over: the word alone means a handover, in the scene's move style");
        StageMath.ReadPositionWords("", "Over", "Cut", out cut, out over, out warning);
        log.IsTrue(cut == null && over && warning != null && warning.Contains("not a form"), "over: a third word is ignored with a warning (,Over,Cut is not a form)");
        StageMath.ReadPositionWords("Cut", "", "", out cut, out over, out warning);
        log.IsTrue(cut == true && !over && warning == null, "over: Cut is still a cut");
        StageMath.ReadPositionWords("Smooth", "", "", out cut, out over, out warning);
        log.IsTrue(cut == false && !over, "over: Smooth is still smooth");

        // Both stampers and the retimer: Unstamp(Stamp(x)) == x, and it is a known marker.
        foreach (string side in new[] { "Left", "Right", "Center", "PeekLeft", "PeekRight" })
        {
            string body = $"One rule for the apps. {{Position:{side},Over}}Different departments, probably.\n";
            StageAllRecognised(log, body, $"over: {{Position:{side},Over}}");
            string timed = StageRoundTrip(log, body, $"over: {{Position:{side},Over}}");
            Match stamped = tag.Match(timed);
            log.IsTrue(stamped.Success && stamped.Groups[1].Value == side && stamped.Groups[3].Success && stamped.Groups[5].Success,
                $"over: the stamped {{Position:{side},Over,T=…}} is what the runtime parser reads");
        }
        // The stamper's pattern for a position has not changed (the Python one is the same string).
        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Position:Center,Over}"), "over: {Position:Center,Over} is a known marker");
        var (_, clean) = TtsScriptProcessor.ExtractMarkers("Different {Position:Center,Over}departments.");
        log.IsTrue(!clean.Contains("{") && !clean.Contains("Over"), $"over: …and never read aloud (\"{clean}\")");
    }

    static void Tier5_OverRules(Log log)
    {
        CharacterPosition L = CharacterPosition.Left, C = CharacterPosition.Center, Off = CharacterPosition.Off,
                          PL = CharacterPosition.PeekLeft, PR = CharacterPosition.PeekRight;

        // When it applies: he was away, and something has the frame.
        foreach (CharacterPosition away in new[] { Off, PL, PR })
        {
            log.IsTrue(StageMath.CanHandOver(away, true, false), $"over: from {away}, with something on the frame, he comes up in front of it");
            log.IsTrue(!StageMath.CanHandOver(away, false, false), $"over: from {away} with nothing on the frame there is nothing to come up in front of");
        }
        log.IsTrue(!StageMath.CanHandOver(L, true, false) && !StageMath.CanHandOver(C, true, false), "over: already on stage, the word changes nothing");
        log.IsTrue(StageMath.CanHandOver(C, true, true), "over: …unless he is already standing in front of it (he stays there)");

        // Every full-screen card the brief names is one the stage leaves in place.
        foreach (ContentCardType type in new[]
        {
            ContentCardType.BigText, ContentCardType.BigMedia, ContentCardType.BigImage, ContentCardType.Versus,
            ContentCardType.Graph, ContentCardType.Clipping, ContentCardType.TimelineWide, ContentCardType.BigCenter,
        })
            log.IsTrue(ContentZoneController.IsFeatureCard(type), $"over: {type} is a full-screen card (drawn at 31000, so he needs the handover's layer to be seen)");

        // The layers: above the full-screen cards and the name label, below the Pop phrase and the rest.
        log.AreEqual(StageMath.OverSortingOrder, StageMath.PresenterOrder(true, false), "over: in a handover he takes the handover's order");
        log.AreEqual(StageMath.OverSortingOrder, StageMath.PresenterOrder(true, true), "over: …also while he is still rising");
        log.AreEqual(StageMath.PeekSortingOrder, StageMath.PresenterOrder(false, true), "over: without one, a peek's order");
        log.IsTrue(StageMath.PresenterOrder(false, false) == null, "over: …or the scene's own");
        log.IsTrue(StageMath.OverSortingOrder > 31000 && StageMath.OverSortingOrder > NameLabelMath.SortOrder, "over: in front of the full-screen cards (31000) and the {Name:} label");
        log.IsTrue(StageMath.OverSortingOrder + 1 < PopMath.SortOrder && PopMath.SortOrder < MugsTech.Captions.CaptionOverlay.SortOrder,
            "over: behind the {Pop:} phrase, the captions (his crossfade sprite, one order up, included)");
        log.IsTrue(StageMath.HandoverDimSortingOrder > 31000 && StageMath.HandoverDimSortingOrder < StageMath.OverSortingOrder,
            "over: the dim sits between the cards and him");
        log.Near(0.40f, StageMath.HandoverDimAlpha(false), 1e-5f, "over: what is under him is left at 60 % (gamma: a 40 % black sheet)");
        log.Near(1f - Mathf.Pow(0.60f, 2.2f), StageMath.HandoverDimAlpha(true), 1e-5f, "over: …the same brightness in linear colour space");
    }

    static void Tier5_HandoverDim_Engine(Log log)
    {
        var root = new GameObject("HandoverDimSelfTest", typeof(RectTransform), typeof(Canvas));
        try
        {
            HandoverDim dim = HandoverDim.CreateOn(root.transform);
            Canvas sub = root.transform.Find("Handover_Container").GetComponent<Canvas>();
            log.IsTrue(sub.overrideSorting && sub.sortingOrder == StageMath.HandoverDimSortingOrder, "dim: its own canvas at the handover's dim order");
            log.IsTrue(!dim.IsShowing && dim.Alpha == 0f, "dim: nothing shows until a handover");
            log.Near(StageMath.HandoverDimAlpha(QualitySettings.activeColorSpace == ColorSpace.Linear), dim.Peak, 1e-6f, "dim: its peak is the 60 % brightness sheet");

            dim.FadeTo(true, StageMath.PopUpSeconds);
            dim.Step(StageMath.PopUpSeconds * 0.5f);
            log.Near(dim.Peak * 0.5f, dim.Alpha, 1e-4f, "dim: halfway through his pop-up it is half there");
            dim.Step(StageMath.PopUpSeconds * 0.5f);
            log.Near(dim.Peak, dim.Alpha, 1e-5f, "dim: at the end of the pop-up what is under him is at 60 %");
            dim.FadeTo(true, 5f);
            dim.Step(1f);
            log.Near(dim.Peak, dim.Alpha, 1e-5f, "dim: asking again for what it already is changes nothing");

            dim.FadeTo(false, StageMath.HandoverDimOutSeconds);
            dim.Step(StageMath.HandoverDimOutSeconds);
            log.IsTrue(dim.Alpha == 0f && !dim.IsShowing, "dim: it goes with what was under him (0.30 s)");
            dim.FadeTo(true, 0f);
            log.Near(dim.Peak, dim.Alpha, 1e-6f, "dim: a cut dims on the frame");
            UnityEngine.Object.DestroyImmediate(dim.gameObject);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static T ZoneField<T>(ContentZoneController cz, string name) =>
        (T)typeof(ContentZoneController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(cz);

    static object ZoneCall(ContentZoneController cz, string name, params object[] args) =>
        typeof(ContentZoneController).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(cz, args);

    static void Tier5_HandoverOnTheRig_Engine(Log log)
    {
        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        StageRig rig = BuildStageRig();
        ContentZoneController cz = BuildStageZone(out GameObject zoneRoot, out RectTransform sideZone);
        try
        {
            cz.mediaPresentationSystem = rig.Mps;
            rig.Mps.contentZoneController = cz;
            rig.Mps.mediaCanvas = zoneRoot.GetComponent<Canvas>();   // where the dim is built
            CharacterPosition L = CharacterPosition.Left, R = CharacterPosition.Right, C = CharacterPosition.Center,
                              Off = CharacterPosition.Off, PR = CharacterPosition.PeekRight;

            ContentCardEvent Versus() => new ContentCardEvent
            {
                cardType = ContentCardType.Versus, triggerTime = 2f, duration = 0f,
                fields = new[] { "Apple's rule for apps", "Removed.", "Apple's own stores", "A 40% target" },
                primaryText = "Removed.", secondaryText = "A 40% target",
            };
            ContentCardEvent End(ContentCardType type) => new ContentCardEvent { cardType = type, triggerTime = 9f, dismissesCard = true };
            void Reset()
            {
                rig.Call("FinishHandover", "(test reset)", true);
                cz.ClearForTransition();
                rig.Set("isShowingMedia", false);
                rig.Set("mediaIsFullFrame", false);
                rig.Set("currentPosition", C);
                rig.Call("MoveToPosition", Off, (bool?)true);
            }

            // ---- Over a full-screen card ------------------------------------------------
            Reset();
            cz.ShowCard(Versus());
            ContentCard versus = cz.ActiveCard;
            log.IsTrue(versus != null && rig.Mps.IsStageWide, "handover: (a Versus is up and Mugs is off)");
            Transform versusParent = versus != null ? versus.transform.parent : null;
            List<string> warnings = WarningsFrom(() => rig.Call("MoveToPositionOver", C, (bool?)true));
            log.IsTrue(rig.Mps.IsHandover, "handover: {Position:Center,Over} with a Versus up — he is in front of it");
            log.AreEqual(0, warnings.Count, "handover: …with no warning");
            log.AreEqual(C, rig.Mps.CurrentPosition, "handover: he is at Center");
            log.IsTrue(((Vector2)rig.Presenter.position - (Vector2)rig.Center.position).magnitude < 1e-4f, "handover: …on his mark");
            log.AreEqual((int?)StageMath.OverSortingOrder, rig.Mps.PresenterOrderNow, "handover: drawn at the handover's order (above the full-screen cards)");
            log.IsTrue(cz.ActiveCard == versus && !versus.IsHiding && versus.transform.parent == versusParent,
                "handover: the Versus is the same card, not hidden, not moved (Center pauses nothing under him)");
            log.IsTrue(ZoneField<bool>(cz, "isPaused") && !cz.IsStageWide, "handover: for anything NEW the zone follows his position (Center: side cards are skipped, side slots)");
            log.IsTrue(rig.Mps.HandoverDimSheet != null && rig.Mps.HandoverDimSheet.Target == 1f, "handover: what is under him dims");
            log.AreEqual(MugsOnScreen.Full, (MugsOnScreen)rig.Call("StageState"), "handover: the meter counts him as on screen");
            log.AreEqual(1, rig.Get<int>("handoverCount"), "handover: one handover counted");

            // A zoom while he is over it: ignored, with the warning.
            float size = rig.Camera.orthographicSize;
            warnings = WarningsFrom(() =>
            {
                rig.Call("ApplyZoom", ZoomType.In, false, 0f);
                rig.Call("ApplyZoom", ZoomType.ExtremeIn, false, 0f);
            });
            log.IsTrue(Mathf.Approximately(rig.Camera.orthographicSize, size) && !rig.Mps.IsCloseUpHeld, "handover: zooms leave the camera alone while he is in front of it");
            log.AreEqual(2, warnings.FindAll(w => w.Contains("ignored") && w.Contains("in front of")).Count, "handover: …one warning each");
            warnings = WarningsFrom(() => rig.Call("ApplyZoom", ZoomType.ExtremeOut, false, 0f));
            log.AreEqual(0, warnings.Count, "handover: the ExtremeOut of the ignored ExtremeIn is quiet");

            // Its own End reaches it, though he is at Center; a new side card is still skipped there.
            log.IsTrue(!(bool)ZoneCall(cz, "OpensSomethingNew", End(ContentCardType.Versus)), "handover: {Versus:End} acts on the card that is up");
            log.IsTrue((bool)ZoneCall(cz, "OpensSomethingNew", StatEvent("40%", 5f)), "handover: a Stat that comes due is something new");

            // It ends (here: taken off the frame): his own layer again, the dim goes, the stage is his.
            cz.ClearForTransition();
            rig.Call("StepHandover");
            log.IsTrue(!rig.Mps.IsHandover, "handover: when what was under him has gone, the handover is over");
            log.AreEqual(0f, rig.Mps.HandoverDimSheet.Target, "handover: …the dim goes with it");
            // (Here the card went on one frame with the dim still up: he keeps his layer until the sheet has gone.)
            log.AreEqual((int?)StageMath.OverSortingOrder, rig.Mps.PresenterOrderNow, "handover: he is never under a dim that is still fading");
            rig.Mps.HandoverDimSheet.Step(1f);
            rig.Call("StepHandover");
            log.IsTrue(rig.Mps.PresenterOrderNow == null, "handover: …and is back at his own sorting order on the frame it has");
            log.IsTrue(ZoneField<bool>(cz, "isPaused") && !cz.IsStageWide, "handover: …and his position follows its normal rules (Center)");
            warnings = WarningsFrom(() => rig.Call("ApplyZoom", ZoomType.In, false, 0f));
            log.AreEqual(0, warnings.FindAll(w => w.Contains("ignored")).Count, "handover: zooms work again once it has closed");
            rig.Call("EndZoomRun", "(test)", true);

            // ---- Over a wide card -------------------------------------------------------
            Reset();
            ContentCardEvent statEvt = StatEvent("40%", 1f, EntryDirection.FromRight);
            cz.SetTimeline(new List<ContentCardEvent> { statEvt }, null);
            cz.ShowCard(statEvt);
            ContentCard stat = cz.ActiveCard;
            log.IsTrue(stat != null && stat.transform.parent.name == "ContentZone_Cards_Wide", "handover: (a Stat is up in the wide slot)");
            rig.Call("MoveToPositionOver", C, (bool?)true);
            log.IsTrue(rig.Mps.IsHandover && cz.ActiveCard == stat && !stat.IsHiding, "handover: over a wide card — it is not hidden by Center");
            log.AreEqual("ContentZone_Cards_Wide", stat.transform.parent.name, "handover: …and stays wide and centred (it does not glide to its side)");
            log.IsTrue(!(bool)ZoneCall(cz, "SkippedWhileCentered", End(ContentCardType.Stat)), "handover: its {Stat:End} is not skipped at Center");
            log.IsTrue((bool)ZoneCall(cz, "SkippedWhileCentered", StatEvent("60%", 6f)), "handover: a NEW side card at Center is skipped, as always");

            // A new beat closes what is under him first.
            rig.Mps.HandoverNewBeat("a Quote card");
            log.IsTrue(stat.IsHiding, "handover: a new card coming due closes the card under him first");
            rig.Call("StepHandover");
            log.IsTrue(rig.Mps.IsHandover, "handover: …he stays in front of it while it fades");
            log.AreEqual(0f, rig.Mps.HandoverDimSheet.Target, "handover: …and the dim fades with it");
            cz.ClearForTransition();
            rig.Call("StepHandover");
            log.IsTrue(!rig.Mps.IsHandover, "handover: …and it is over when the fade is done");

            // ---- {Position:Off} while over ----------------------------------------------
            Reset();
            cz.ShowCard(Versus());
            versus = cz.ActiveCard;
            rig.Set("isShowingMedia", true);
            rig.Set("mediaIsFullFrame", true);
            int token = rig.Get<int>("mediaDismissToken");
            rig.Call("MoveToPositionOver", C, (bool?)true);
            log.AreEqual(token, rig.Get<int>("mediaDismissToken"), "handover: his return does not end the full-frame picture that is up");
            rig.Call("MoveToPosition", L, (bool?)true);
            log.IsTrue(rig.Mps.IsHandover && rig.Get<int>("mediaDismissToken") == token && !ZoneField<bool>(cz, "isPaused"),
                "handover: a move to Left while over it keeps him in front of it (nothing ends, Center's rule lifts)");
            rig.Call("MoveToPosition", Off, (bool?)true);
            log.IsTrue(!rig.Mps.IsHandover && rig.Mps.IsPresenterOff, "handover: {Position:Off} — he leaves as usual");
            log.IsTrue(cz.ActiveCard == versus && !versus.IsHiding, "handover: …what was under him carries on");
            log.AreEqual(token, rig.Get<int>("mediaDismissToken"), "handover: …the picture too");
            log.AreEqual(0f, rig.Mps.HandoverDimSheet.Target, "handover: …back to full brightness");
            log.IsTrue(cz.IsStageWide && !ZoneField<bool>(cz, "isPaused"), "handover: …and the stage is wide again");

            // ---- Over a full-frame picture only -----------------------------------------
            Reset();
            rig.Set("isShowingMedia", true);
            rig.Set("mediaIsFullFrame", true);
            token = rig.Get<int>("mediaDismissToken");
            rig.Call("MoveToPositionOver", R, (bool?)true);
            log.IsTrue(rig.Mps.IsHandover && rig.Get<int>("mediaDismissToken") == token, "handover: over a full-frame image — it stays (his return would have ended it)");
            log.IsTrue(!ZoneField<bool>(cz, "isPaused"), "handover: at Right nothing is paused");
            rig.Mps.HandoverNewBeat("an image or clip");
            log.AreEqual(token + 1, rig.Get<int>("mediaDismissToken"), "handover: a new picture coming due ends the one under him");
            rig.Set("isShowingMedia", false);
            rig.Call("StepHandover");
            rig.Mps.HandoverDimSheet.Step(1f);
            rig.Call("StepHandover");
            log.IsTrue(!rig.Mps.IsHandover && rig.Mps.PresenterOrderNow == null, "handover: when the picture has gone the handover is over");

            // ---- A peek written with ,Over ----------------------------------------------
            Reset();
            cz.ShowCard(Versus());
            rig.Call("MoveToPositionOver", PR, (bool?)true);
            log.IsTrue(rig.Mps.IsHandover && rig.Mps.IsPresenterPeeking, "handover: {Position:PeekRight,Over} — the peek is in front of the full-screen card");
            log.AreEqual((int?)StageMath.OverSortingOrder, rig.Mps.PresenterOrderNow, "handover: …at the handover's order (a plain peek is behind it)");
            log.IsTrue(rig.Mps.HandoverDimSheet.Target == 0f, "handover: …and dims nothing");
            log.IsTrue(cz.IsStageWide, "handover: …the stage stays wide");
            log.AreEqual(MugsOnScreen.Peek, (MugsOnScreen)rig.Call("StageState"), "handover: …counted as a peek");
            int count = rig.Get<int>("handoverCount");
            rig.Call("MoveToPositionOver", C, (bool?)true);
            log.IsTrue(rig.Mps.IsHandover && rig.Mps.HandoverDimSheet.Target == 1f, "handover: from that peek, {Position:Center,Over} — still in front, and now it dims");
            log.AreEqual(count, rig.Get<int>("handoverCount"), "handover: …the same handover, not a second one");
            // A plain peek from there: he leaves it up and is a plain peek again.
            rig.Call("MoveToPosition", PR, (bool?)true);
            log.IsTrue(!rig.Mps.IsHandover && rig.Mps.PresenterOrderNow == StageMath.PeekSortingOrder && cz.ActiveCard != null && !cz.ActiveCard.IsHiding,
                "handover: a peek without the word ends it — the card stays up, he is a plain peek (behind it)");

            // ---- The word where it means nothing ----------------------------------------
            Reset();
            warnings = WarningsFrom(() => rig.Call("MoveToPositionOver", C, (bool?)true));
            log.IsTrue(!rig.Mps.IsHandover && rig.Mps.CurrentPosition == C, "handover: with nothing under him he simply pops up");
            log.AreEqual(1, warnings.FindAll(w => w.Contains("nothing has the frame")).Count, "handover: …with one warning");
            log.IsTrue(rig.Mps.PresenterOrderNow == null && !cz.IsStageWide, "handover: …at his own order, the stage narrow, as without the word");

            warnings = WarningsFrom(() => rig.Call("MoveToPositionOver", L, (bool?)true));
            log.IsTrue(!rig.Mps.IsHandover && rig.Mps.CurrentPosition == L, "handover: already on stage, {Position:Left,Over} is a plain move to Left");
            log.AreEqual(1, warnings.FindAll(w => w.Contains("already on stage")).Count, "handover: …with one warning");

            // A third word: ignored, with a warning, at parse time.
            List<PositionMarkerData> parsed = null;
            warnings = WarningsFrom(() =>
            {
                object r = rig.Call("ParsePositionMarkers", "One rule. {Position:Center,Over,Cut,T=4.000}Different departments.", 10f);
                parsed = (List<PositionMarkerData>)r.GetType().GetField("Item2").GetValue(r);
            });
            log.IsTrue(parsed != null && parsed.Count == 1 && parsed[0].over && parsed[0].hardCutOverride == null && parsed[0].position == C,
                "handover: {Position:Center,Over,Cut} is a handover — the third word does nothing");
            log.AreEqual(1, warnings.FindAll(w => w.Contains("not a form")).Count, "handover: …and is named in one warning");
            warnings = WarningsFrom(() =>
            {
                object r = rig.Call("ParsePositionMarkers", "{Position:Left,Cut,T=1.000} a {Position:Right,T=2.000} b {Position:Off,T=3.000}", 10f);
                parsed = (List<PositionMarkerData>)r.GetType().GetField("Item2").GetValue(r);
            });
            log.IsTrue(parsed.Count == 3 && !parsed[0].over && !parsed[1].over && !parsed[2].over && parsed[0].hardCutOverride == true && warnings.Count == 0,
                "handover: tags without the word parse exactly as before");

            // On a transition line the word is ignored (the transition clears the frame).
            warnings = WarningsFrom(() =>
            {
                object r = rig.Call("ParseTransitionMarkers", "{Transition:Wipe,T=10.000} {Position:Center,Over,T=10.000}\nWords.", 30f);
                var markers = (List<TransitionMarkerData>)r.GetType().GetField("Item2").GetValue(r);
                log.IsTrue(markers.Count == 1 && markers[0].hasPosition && markers[0].position == C, "handover: a {Position:Center,Over} on a transition line is still the transition's position");
                string left = (string)r.GetType().GetField("Item1").GetValue(r);
                log.IsTrue(!left.Contains("Position"), "handover: …claimed by it (not left to fire on its own)");
            });
            log.AreEqual(1, warnings.FindAll(w => w.Contains("\",Over\" is ignored")).Count, "handover: …with one warning that the word does nothing there");

            // ---- A transition takes what is under him -----------------------------------
            Reset();
            cz.ShowCard(Versus());
            rig.Call("MoveToPositionOver", C, (bool?)true);
            rig.Call("FinishHandover", "a transition clears the frame", true);
            log.IsTrue(!rig.Mps.IsHandover && rig.Mps.HandoverDimSheet.Alpha == 0f && rig.Mps.PresenterOrderNow == null,
                "handover: a transition ends it at once — no dim left under the cover, his own order");

            // ---- The take's figures ------------------------------------------------------
            rig.Set("positionMarkers", new List<PositionMarkerData>());
            rig.Call("BeginStageRun");
            log.IsTrue(rig.Get<int>("handoverCount") == 0 && rig.Get<float>("handoverSeconds") == 0f && !rig.Mps.IsHandover, "handover: a new take starts with none");
        }
        finally
        {
            VisualsRuntimeApplier.CardFontOverride = savedFont;
            UnityEngine.Object.DestroyImmediate(zoneRoot);
            UnityEngine.Object.DestroyImmediate(rig.Root);
        }
    }
    // -----------------------------------------------------------------------
    // Step 5: the lean — {Position:LeanLeft} / {Position:LeanRight}
    // -----------------------------------------------------------------------

    // His outline as measured in the recording scene, facing right (his tail behind him, to the left).
    static StageMath.Body LeanTestBody => new StageMath.Body { CentreOffset = new Vector2(-0.63f, 3.33f), Size = new Vector2(5.63f, 6.02f) };

    static void Tier5_LeanPoseAndPlans(Log log)
    {
        CharacterPosition L = CharacterPosition.Left, R = CharacterPosition.Right, C = CharacterPosition.Center, Off = CharacterPosition.Off,
                          PL = CharacterPosition.PeekLeft, PR = CharacterPosition.PeekRight,
                          LL = CharacterPosition.LeanLeft, LR = CharacterPosition.LeanRight;

        // The words.
        log.IsTrue(StageMath.TryParsePosition("LeanLeft", out CharacterPosition p) && p == LL, "lean: {Position:LeanLeft} is a position");
        log.IsTrue(StageMath.TryParsePosition("leanright", out p) && p == LR, "lean: …and LeanRight (case-insensitive, like the others)");
        log.IsTrue(!StageMath.TryParsePosition("LeanUp", out p) && p == C, "lean: an unknown word is still unknown (Center, with the caller's warning)");
        foreach (CharacterPosition lean in new[] { LL, LR })
        {
            log.IsTrue(StageMath.IsLean(lean) && StageMath.IsSmall(lean) && StageMath.IsWide(lean), $"lean: {lean} is a small appearance — the stage stays wide");
            log.IsTrue(!StageMath.IsPeek(lean) && !StageMath.IsFull(lean), $"lean: {lean} is neither a peek nor a full position");
        }
        log.IsTrue(StageMath.IsSmall(PL) && !StageMath.IsLean(PL) && !StageMath.IsSmall(Off) && !StageMath.IsSmall(L), "lean: a peek is small too; Off and Left are not");

        // The pose: inside the peek's size limits at 16:9, tilted into the frame, mirrored from side to side.
        StageMath.Frame frame = TestFrame;
        foreach (StageMath.Body body in new[] { LeanTestBody, StageMath.DefaultBody })
        {
            StageMath.LeanPose left = StageMath.Lean(false, body, frame);
            StageMath.LeanPose right = StageMath.Lean(true, StageMath.Mirrored(body), frame);
            string which = body.Size.x > 5f ? "the scene's sprite" : "the default outline";
            log.IsTrue(left.VisibleWidth > 0.5f && left.VisibleWidth <= StageMath.PeekMaxWidthOfFrame * frame.Width + 1e-3f,
                $"lean ({which}): what shows is at most 22 % of the frame's width ({left.VisibleWidth / frame.Width:P1})");
            log.IsTrue(left.VisibleHeight > 0.5f && left.VisibleHeight <= StageMath.PeekMaxHeightOfFrame * frame.Height + 1e-3f,
                $"lean ({which}): …and 35 % of its height ({left.VisibleHeight / frame.Height:P1})");
            log.IsTrue(left.Scale > 0.3f && left.Scale <= 1f, $"lean ({which}): he is scaled down to fit, never up ({left.Scale:F2})");
            log.Near(-StageMath.LeanTilt, left.Tilt, 1e-4f, $"lean ({which}): from the left he tilts clockwise, into the frame");
            log.Near(StageMath.LeanTilt, right.Tilt, 1e-4f, $"lean ({which}): from the right, counter-clockwise");
            log.IsTrue(Mathf.Abs(left.Rest.x + right.Rest.x) < 1e-3f && Mathf.Abs(left.Rest.y - right.Rest.y) < 1e-3f &&
                       Mathf.Abs(left.Scale - right.Scale) < 1e-4f && Mathf.Abs(left.VisibleWidth - right.VisibleWidth) < 1e-3f,
                $"lean ({which}): LeanRight is LeanLeft mirrored");
            log.IsTrue(left.Rest.x < frame.Left && right.Rest.x > frame.Right, $"lean ({which}): the transform he stands on is outside the frame — his body trails out of it");
            log.Near(left.Rest.x - left.VisibleWidth - StageMath.OffMargin, left.Hidden.x, 1e-3f, $"lean ({which}): he comes in from wholly outside the left edge…");
            log.IsTrue(Mathf.Approximately(left.Hidden.y, left.Rest.y), $"lean ({which}): …travelling sideways");
            log.IsTrue(right.Hidden.x > right.Rest.x, $"lean ({which}): …and from outside the right edge on that side");
        }
        log.Near(35f, StageMath.LeanTilt, 1e-4f, "lean: the tilt is 35° (the brief's 12° read as standing cut off by the frame — the creator chose this pose)");

        // The plan against every other position.
        var table = new Dictionary<string, string>
        {
            { "LeanLeft>Left", "LeanOut,PopUp" }, { "LeanLeft>Right", "LeanOut,PopUp" }, { "LeanLeft>Center", "LeanOut,PopUp" },
            { "LeanLeft>Off", "LeanOut,Park" }, { "LeanLeft>PeekLeft", "LeanOut,RiseToPeek" }, { "LeanLeft>PeekRight", "LeanOut,RiseToPeek" },
            { "LeanLeft>LeanRight", "LeanOut,LeanIn" },
            { "LeanRight>Left", "LeanOut,PopUp" }, { "LeanRight>Right", "LeanOut,PopUp" }, { "LeanRight>Center", "LeanOut,PopUp" },
            { "LeanRight>Off", "LeanOut,Park" }, { "LeanRight>PeekLeft", "LeanOut,RiseToPeek" }, { "LeanRight>PeekRight", "LeanOut,RiseToPeek" },
            { "LeanRight>LeanLeft", "LeanOut,LeanIn" },
            { "Left>LeanLeft", "SlideOut,LeanIn" }, { "Left>LeanRight", "SlideOut,LeanIn" },
            { "Right>LeanLeft", "SlideOut,LeanIn" }, { "Right>LeanRight", "SlideOut,LeanIn" },
            { "Center>LeanLeft", "SlideOut,LeanIn" }, { "Center>LeanRight", "SlideOut,LeanIn" },
            { "Off>LeanLeft", "LeanIn" }, { "Off>LeanRight", "LeanIn" },
            { "PeekLeft>LeanLeft", "DropFromPeek,LeanIn" }, { "PeekLeft>LeanRight", "DropFromPeek,LeanIn" },
            { "PeekRight>LeanLeft", "DropFromPeek,LeanIn" }, { "PeekRight>LeanRight", "DropFromPeek,LeanIn" },
        };
        foreach (var row in table)
        {
            string[] ends = row.Key.Split('>');
            var from = (CharacterPosition)Enum.Parse(typeof(CharacterPosition), ends[0]);
            var to = (CharacterPosition)Enum.Parse(typeof(CharacterPosition), ends[1]);
            List<StageLeg> legs = StageMath.PlanTravel(from, to);
            log.AreEqual(row.Value, Legs(legs), $"lean: the plan for {from} → {to}");
            float sum = 0f;
            foreach (StageLeg leg in legs) sum += StageMath.LegSeconds(leg);
            log.Near(sum, StageMath.TravelSeconds(from, to), 1e-6f, $"lean: {from} → {to} takes the sum of its legs ({sum:F2} s)");
        }
        log.AreEqual("ToLean", Legs(StageMath.PlanWhileLeaving(LL, LL, true, out bool finishLean)), "lean: pulling out and still in view, the same lean brings him back in from where he is");
        log.IsTrue(!finishLean, "lean: …with nothing to finish first");
        log.AreEqual(0, StageMath.PlanTravel(LL, LL).Count, "lean: the same lean again is a no-op");

        log.Near(0.40f, StageMath.TravelSeconds(Off, LL), 1e-6f, "lean: he leans in in 0.40 s");
        log.Near(0.35f, StageMath.TravelSeconds(LR, Off), 1e-6f, "lean: …and is out again in 0.35 s");
        log.Near(1.00f, StageMath.TravelSeconds(LR, R), 1e-6f, "lean: a lean to the full position on its side is out, then the pop-up (0.35 + 0.65 s)");
        log.Near(0.75f, StageMath.TravelSeconds(LL, PL), 1e-6f, "lean: a lean to a peek is out, then in (0.75 s)");
        log.IsTrue(!StageMath.CardsWaitFor(Off, LL) && !StageMath.CardsWaitFor(L, LL), "lean: cards never wait for a lean");
        log.IsTrue(StageMath.CardsWaitFor(LL, L), "lean: …they do wait for him to land out of one");
        log.IsTrue(!StageMath.MoveEndsMedia(Off, LL) && !StageMath.MoveEndsMedia(LL, PR) && !StageMath.MoveEndsMedia(LR, Off),
            "lean: Off ↔ lean ↔ peek keeps the stage wide, so the full-frame media stays");
        log.IsTrue(StageMath.MoveEndsMedia(LL, L), "lean: a full position ends it, as from a peek");

        // Its moves are the peek's shapes, in its own time.
        foreach (float t in new[] { 0f, 0.05f, 0.12f, 0.2f, 0.33f })
        {
            log.IsTrue(SamePose(StageMoves.Arrive(t, StageMath.LeanInSeconds, StageMoves.PeekAmount), StageMoves.For(StageLeg.LeanIn, t, false)), $"lean: leaning in has the peek rise's shape (t {t:F2})");
            log.IsTrue(SamePose(StageMoves.Depart(t, StageMath.LeanOutSeconds, StageMoves.PeekAmount, false), StageMoves.For(StageLeg.LeanOut, t, false)), $"lean: pulling out has the peek drop's (t {t:F2})");
        }
        log.IsTrue(SamePose(StageMoves.Rest, StageMoves.For(StageLeg.LeanIn, StageMath.LeanInSeconds, false)) &&
                   SamePose(StageMoves.Rest, StageMoves.For(StageLeg.LeanOut, StageMath.LeanOutSeconds, false)) &&
                   SamePose(StageMoves.Rest, StageMoves.For(StageLeg.ToLean, StageMath.ToPeekSeconds, false)),
            "lean: each of its legs is at rest exactly when its time is up");

        // The side edge holds still while he stretches along his own length.
        foreach (float tilt in new[] { -35f, 35f, -20f })
            foreach (float scale in new[] { 0.96f, 1.04f })
            {
                float edge = tilt < 0f ? frame.Left : frame.Right;
                var origin = new Vector2(edge + (tilt < 0f ? -1.3f : 1.3f), -2.2f);
                float r = tilt * Mathf.Deg2Rad;
                var up = new Vector2(-Mathf.Sin(r), Mathf.Cos(r));
                float along = (edge - origin.x) / up.x;
                Vector2 onEdge = origin + up * along;
                Vector2 moved = StageMoves.HoldSideEdge(origin, edge, tilt, scale);
                log.IsTrue((moved + up * (along * scale) - onEdge).magnitude < 1e-4f,
                    $"lean: the point of him on the frame's side edge stays put (tilt {tilt:F0}°, length ×{scale:F2})");
            }
        log.IsTrue((StageMoves.HoldSideEdge(new Vector2(-10f, -2f), frame.Left, -35f, 1f) - new Vector2(-10f, -2f)).magnitude < 1e-6f, "lean: in his own shape nothing is moved");

        // The meter: a lean's seconds are peek seconds; leans and peeks are counted apart.
        var meter = new StageMeter();
        meter.Add(0f, MugsOnScreen.Off);
        meter.Add(6f, MugsOnScreen.Off);
        meter.Add(1f, MugsOnScreen.Lean);
        meter.Add(1f, MugsOnScreen.Lean);
        meter.Add(1f, MugsOnScreen.Off);
        meter.Add(1f, MugsOnScreen.Peek);
        log.Near(0.3f, meter.PeekShare, 1e-5f, "lean: a lean counts with the peek share (mugsPeekShare)");
        log.IsTrue(meter.LeanCount == 1 && meter.PeekCount == 1, "lean: leans and peeks are counted separately");
        log.Near(0f, meter.OnScreenShare, 1e-6f, "lean: …and not as a full appearance");

        // Both stampers' shape and the retimer.
        foreach (string tag in new[] { "{Position:LeanLeft}", "{Position:LeanRight}", "{Position:LeanRight,Cut}", "{Position:LeanLeft,Over}" })
        {
            string body = $"Four buyers in ten.\n{tag}\n[dry] Or sixty.\n";
            StageAllRecognised(log, body, $"lean: {tag}");
            string timed = StageRoundTrip(log, body, $"lean: {tag}");
            Match stamped = MediaPresentationSystem.PositionTagRegex.Match(timed);
            log.IsTrue(stamped.Success && StageMath.TryParsePosition(stamped.Groups[1].Value, out CharacterPosition lean) && StageMath.IsLean(lean) && stamped.Groups[5].Success,
                $"lean: the stamped {tag} is what the runtime parser reads");
        }
    }

    static void Tier5_LeanOnTheRig_Engine(Log log)
    {
        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        StageRig rig = BuildStageRig();
        ContentZoneController cz = BuildStageZone(out GameObject zoneRoot, out RectTransform sideZone);
        try
        {
            StageMath.Frame frame = TestFrame;
            StageMath.Body body = StageMath.DefaultBody;
            CharacterPosition L = CharacterPosition.Left, R = CharacterPosition.Right, C = CharacterPosition.Center, Off = CharacterPosition.Off,
                              PR = CharacterPosition.PeekRight, LL = CharacterPosition.LeanLeft, LR = CharacterPosition.LeanRight;
            StageMath.LeanPose left = StageMath.Lean(false, body, frame), right = StageMath.Lean(true, body, frame);
            bool Upright() => Quaternion.Angle(rig.Presenter.rotation, Quaternion.identity) < 1e-3f;

            // Placed on a cut: the pose, tilted, at its scale; the stage wide; counted as a lean.
            rig.Call("MoveToPosition", Off, (bool?)true);
            rig.Call("MoveToPosition", LR, (bool?)true);
            log.IsTrue(rig.Mps.IsStageWide && rig.Mps.IsPresenterPeeking && rig.Mps.CurrentPosition == LR, "lean rig: {Position:LeanRight,Cut} — the stage stays wide, he is a small appearance");
            log.IsTrue(((Vector2)rig.Presenter.position - right.Rest).magnitude < 1e-4f, "lean rig: …in the lean pose");
            log.Near(right.Scale, rig.Presenter.localScale.x, 1e-5f, "lean rig: …at its scale");
            log.Near(StageMath.LeanTilt, LeanOf(rig.Presenter), 1e-3f, "lean rig: …tilted 35° into the frame");
            log.AreEqual(MugsOnScreen.Lean, (MugsOnScreen)rig.Call("StageState"), "lean rig: …counted as a lean");
            log.AreEqual((int?)StageMath.PeekSortingOrder, rig.Mps.PresenterOrderNow, "lean rig: …at the peek's sorting order (in front of side cards, behind full-screen ones)");

            // Every way out on a cut leaves him upright.
            rig.Call("MoveToPosition", Off, (bool?)true);
            log.IsTrue(rig.Mps.IsPresenterOff && Upright() && OutsideFrame(rig.Presenter.position), "lean rig: {Position:Off,Cut} from a lean — gone, and upright again");
            rig.Call("MoveToPosition", LL, (bool?)true);
            log.Near(-StageMath.LeanTilt, LeanOf(rig.Presenter), 1e-3f, "lean rig: from the left he tilts the other way");
            rig.Call("MoveToPosition", PR, (bool?)true);
            StageMath.PeekPose peek = StageMath.Peek(true, body, frame);
            log.IsTrue(Upright() && ((Vector2)rig.Presenter.position - peek.Rest).magnitude < 1e-4f, "lean rig: a cut from a lean to a peek — the peek pose, upright");
            rig.Call("MoveToPosition", LL, (bool?)true);
            rig.Call("MoveToPosition", L, (bool?)true);
            log.IsTrue(Upright() && (rig.Presenter.position - rig.Left.position).magnitude < 1e-4f && rig.Presenter.localScale == Vector3.one,
                "lean rig: a cut from a lean to Left — on his mark, full size, upright");

            // Leaning in: from wholly outside the edge, already tilted, to exactly the pose.
            rig.Call("MoveToPosition", Off, (bool?)true);
            rig.Set("currentPosition", LL);
            object leanIn = BeginLeg(rig, StageLeg.LeanIn, LL, Off);
            log.IsTrue(((Vector2)rig.Presenter.position - left.Hidden).magnitude < 1e-4f, "lean rig: the lean-in starts wholly outside the left edge");
            log.Near(-StageMath.LeanTilt, LeanOf(rig.Presenter), 1e-3f, "lean rig: …already tilted");
            float landedAt = StageMath.LeanInSeconds * (StageMoves.ArrivePeakAt + (1f - StageMoves.ArrivePeakAt) * 0.5f);   // halfway through the settle
            PoseLeg(rig, leanIn, landedAt);
            StageMoves.Pose landed = StageMoves.LeanIn(landedAt);
            log.IsTrue(landed.ScaleY < 0.97f, "lean rig: (halfway through the settle he gives 4 %)");
            log.Near(left.Scale * landed.ScaleY, rig.Presenter.localScale.y, 1e-5f, "lean rig: his squash is along his own length (on top of the lean's scale)");
            // The point of him on the frame's side edge, along his length, is where the travel alone has it.
            float r = -StageMath.LeanTilt * Mathf.Deg2Rad;
            var up = new Vector2(-Mathf.Sin(r), Mathf.Cos(r));
            Vector2 travelAt = Vector2.LerpUnclamped(left.Hidden, left.Rest, landed.Travel);
            float alongHim = (frame.Left - travelAt.x) / up.x;
            Vector2 now = (Vector2)rig.Presenter.position + up * (alongHim * landed.ScaleY);
            log.IsTrue((now - (travelAt + up * alongHim)).magnitude < 1e-3f, "lean rig: …and the frame's side edge is the line that holds still");
            PoseLeg(rig, leanIn, StageMath.LeanInSeconds);
            log.IsTrue(((Vector2)rig.Presenter.position - left.Rest).magnitude < 1e-5f && Mathf.Abs(rig.Presenter.localScale.x - left.Scale) < 1e-6f &&
                       Mathf.Abs(rig.Presenter.localScale.y - left.Scale) < 1e-6f, "lean rig: it ends exactly in the pose, unsquashed");
            log.Near(-StageMath.LeanTilt, LeanOf(rig.Presenter), 1e-3f, "lean rig: …at the pose's tilt");
            rig.Set("stageShaped", false);   // (what the travel does when a leg ends: he stays as the leg left him)

            // Pulling out: from the pose as it is, tilted all the way, out of the frame.
            Vector3 pos = rig.Presenter.position, scale = rig.Presenter.localScale;
            Quaternion rot = rig.Presenter.rotation;
            var args = new object[] { LL, Off, null, null };
            var legs = (List<StageLeg>)rig.Call("PlanStageMove", args);
            log.AreEqual("LeanOut,Park", Legs(legs), "lean rig: {Position:Off} from a lean pulls him out");
            object leanOut = BeginLeg(rig, legs[0], Off, (CharacterPosition)args[2]);
            PoseLeg(rig, leanOut, 0f);
            log.IsTrue((rig.Presenter.position - pos).magnitude < 1e-5f && (rig.Presenter.localScale - scale).magnitude < 1e-6f && Quaternion.Angle(rig.Presenter.rotation, rot) < 1e-3f,
                "lean rig: its first frame is the pose he is in");
            PoseLeg(rig, leanOut, StageMath.LeanOutSeconds);
            log.IsTrue(rig.Presenter.position.x < left.Rest.x - 1f && Mathf.Abs(rig.Presenter.position.y - left.Rest.y) < 1e-4f, "lean rig: he leaves sideways, back through the left edge");
            log.Near(-StageMath.LeanTilt, LeanOf(rig.Presenter), 1e-3f, "lean rig: …still tilted as he goes");
            rig.Call("EndStageShape");

            // A lean to the full position on its side is out, then in: he never straightens up into an entrance.
            rig.Set("currentPosition", Off);
            rig.Call("MoveToPosition", LR, (bool?)true);
            args = new object[] { LR, R, null, null };
            legs = (List<StageLeg>)rig.Call("PlanStageMove", args);
            log.AreEqual("LeanOut,PopUp", Legs(legs), "lean rig: {Position:Right} from the lean on that side pulls him out, then he pops up");
            object pullOut = BeginLeg(rig, legs[0], R, (CharacterPosition)args[2]);
            PoseLeg(rig, pullOut, StageMath.LeanOutSeconds);
            log.IsTrue(rig.Presenter.position.x > right.Rest.x + 1f, "lean rig: …out through the right edge");
            log.Near(StageMath.LeanTilt, LeanOf(rig.Presenter), 1e-3f, "lean rig: …still tilted as he goes");
            rig.Set("stageShaped", false);   // (what the travel does when a leg ends)
            object comeUp = BeginLeg(rig, legs[1], R, (CharacterPosition)args[2]);
            PoseLeg(rig, comeUp, 0f);
            log.IsTrue(Upright() && Mathf.Abs(rig.Presenter.position.x - rig.Right.position.x) < 1e-4f && rig.Presenter.position.y < rig.Right.position.y - 1f,
                "lean rig: the pop-up starts below his mark at Right, upright");
            log.Near(1f, (float)rig.Call("PresenterScale"), 1e-6f, "lean rig: …at his full size");
            PoseLeg(rig, comeUp, StageMath.PopUpSeconds);
            log.IsTrue((rig.Presenter.position - rig.Right.position).magnitude < 1e-4f && Mathf.Abs(rig.Presenter.localScale.x - 1f) < 1e-6f && Upright(),
                "lean rig: …and ends on his mark at Right, full size, upright");
            rig.Call("EndStageShape");

            // And back: from Right he slides out through his own edge, then leans in.
            rig.Set("currentPosition", C);
            rig.Call("MoveToPosition", R, (bool?)true);
            args = new object[] { R, LR, null, null };
            legs = (List<StageLeg>)rig.Call("PlanStageMove", args);
            log.AreEqual("SlideOut,LeanIn", Legs(legs), "lean rig: {Position:LeanRight} from Right slides him out, then he leans in");
            log.AreEqual(R, (CharacterPosition)args[2], "lean rig: …out through the right edge");
            object slideOff = BeginLeg(rig, legs[0], LR, (CharacterPosition)args[2]);
            PoseLeg(rig, slideOff, StageMath.SlideOutSeconds);
            rig.Set("stageShaped", false);
            object leanBack = BeginLeg(rig, legs[1], LR, (CharacterPosition)args[2]);
            log.IsTrue(((Vector2)rig.Presenter.position - right.Hidden).magnitude < 1e-4f, "lean rig: the lean-in starts wholly outside the right edge");
            PoseLeg(rig, leanBack, StageMath.LeanInSeconds);
            log.IsTrue(((Vector2)rig.Presenter.position - right.Rest).magnitude < 1e-4f && Mathf.Abs(rig.Presenter.localScale.x - right.Scale) < 1e-5f,
                "lean rig: …and ends in the lean pose at its scale");
            log.Near(StageMath.LeanTilt, LeanOf(rig.Presenter), 1e-3f, "lean rig: …tilted into the frame");
            rig.Call("EndStageShape");

            // A lean-in cut short: the next move starts on the frame he is on.
            rig.Set("currentPosition", Off);
            rig.Call("MoveToPosition", Off, (bool?)true);
            rig.Set("currentPosition", LL);
            object half = BeginLeg(rig, StageLeg.LeanIn, LL, Off);
            rig.Set("stageRun", half);
            rig.Set("stageMoving", true);
            PoseLeg(rig, half, StageMath.LeanInSeconds * 0.5f);
            pos = rig.Presenter.position; scale = rig.Presenter.localScale; rot = rig.Presenter.rotation;
            args = new object[] { LL, Off, null, null };
            legs = (List<StageLeg>)rig.Call("PlanStageMove", args);
            object backOut = BeginLeg(rig, legs[0], Off, (CharacterPosition)args[2]);
            PoseLeg(rig, backOut, 0f);
            log.IsTrue((rig.Presenter.position - pos).magnitude < 1e-4f && (rig.Presenter.localScale - scale).magnitude < 1e-5f && Quaternion.Angle(rig.Presenter.rotation, rot) < 1e-2f,
                "lean rig: a lean-in cut short at 50 % by {Position:Off} goes back out from where he is");
            rig.Set("stageRun", null);
            rig.Set("stageMoving", false);
            rig.Call("EndStageShape");

            // A lean written with ,Over is in front of a full-screen card, and dims nothing.
            cz.mediaPresentationSystem = rig.Mps;
            rig.Mps.contentZoneController = cz;
            rig.Mps.mediaCanvas = zoneRoot.GetComponent<Canvas>();
            rig.Set("currentPosition", C);
            rig.Call("MoveToPosition", Off, (bool?)true);
            cz.ShowCard(new ContentCardEvent
            {
                cardType = ContentCardType.Versus, triggerTime = 2f, duration = 0f,
                fields = new[] { "Apple's rule for apps", "Removed.", "Apple's own stores", "A 40% target" },
                primaryText = "Removed.", secondaryText = "A 40% target",
            });
            rig.Call("MoveToPositionOver", LR, (bool?)true);
            log.IsTrue(rig.Mps.IsHandover && rig.Mps.PresenterOrderNow == StageMath.OverSortingOrder, "lean rig: {Position:LeanRight,Over} — the lean is in front of the full-screen card");
            log.IsTrue(rig.Mps.HandoverDimSheet == null || rig.Mps.HandoverDimSheet.Target == 0f, "lean rig: …and dims nothing");
            log.IsTrue(cz.IsStageWide, "lean rig: …the stage stays wide");
            rig.Call("FinishHandover", "(test)", true);
        }
        finally
        {
            VisualsRuntimeApplier.CardFontOverride = savedFont;
            UnityEngine.Object.DestroyImmediate(zoneRoot);
            UnityEngine.Object.DestroyImmediate(rig.Root);
        }
    }
    // -----------------------------------------------------------------------
    // Step 6: arrive on the word
    // -----------------------------------------------------------------------

    static PositionCue Cue(float time, CharacterPosition position, bool cut = false, bool onTransition = false) =>
        new PositionCue { Time = time, Position = position, Cut = cut, OnTransition = onTransition };

    static void Tier5_ArrivalLeads(Log log)
    {
        CharacterPosition L = CharacterPosition.Left, R = CharacterPosition.Right, C = CharacterPosition.Center, Off = CharacterPosition.Off,
                          PR = CharacterPosition.PeekRight, LL = CharacterPosition.LeanLeft;
        var noBlack = new List<Vector2>();

        // An arrival with room: it starts early by its own duration.
        ArrivalLead[] leads = ArrivalLeads.Plan(new[] { Cue(1f, C), Cue(10f, Off), Cue(20f, C), Cue(30f, Off), Cue(40f, PR), Cue(50f, Off), Cue(60f, LL) }, noBlack);
        log.IsTrue(!leads[0].Led && leads[0].Why.Contains("first"), "lead: the script's first position is never led");
        log.IsTrue(!leads[1].Led && leads[1].Start == 10f, "lead: he leaves on the tag");
        log.IsTrue(leads[2].Led && !leads[2].Shortened, "lead: a pop-up from off stage is led");
        log.Near(20f - StageMath.PopUpSeconds, leads[2].Start, 1e-5f, "lead: …it starts its 0.65 s early, so he is at rest on the tag's time");
        log.Near(StageMath.PopUpSeconds, leads[2].Seconds, 1e-5f, "lead: …by exactly the pop-up's duration");
        log.Near(40f - StageMath.PeekRiseSeconds, leads[4].Start, 1e-5f, "lead: a peek's rise starts its 0.40 s early");
        log.Near(60f - StageMath.LeanInSeconds, leads[6].Start, 1e-5f, "lead: a lean-in starts its 0.40 s early");

        // Not led: he is on screen (a side move, a peek → a full position), a cut, a repeat.
        leads = ArrivalLeads.Plan(new[] { Cue(1f, L), Cue(10f, R), Cue(20f, PR), Cue(22f, C), Cue(30f, Off), Cue(40f, C, cut: true), Cue(50f, C) }, noBlack);
        log.IsTrue(!leads[1].Led && leads[1].Why.Contains("on screen"), "lead: Left → Right is not led (he is on screen: moving on the word is right)");
        log.IsTrue(!leads[2].Led, "lead: Right → a peek (out, then in) is not led either");
        log.IsTrue(!leads[3].Led, "lead: a peek → Center is not led (he is on screen: he drops on the word, then pops up)");
        log.IsTrue(!leads[5].Led && leads[5].Why == "a cut", "lead: {Position:Center,Cut} from off stage is not led");
        log.IsTrue(!leads[6].Led, "lead: a repeat of where he is is nothing at all");

        // A shortened lead: the move that took him off has not ended early enough.
        leads = ArrivalLeads.Plan(new[] { Cue(1f, C), Cue(10f, Off), Cue(10.70f, C) }, noBlack);
        log.IsTrue(leads[2].Led && leads[2].Shortened, "lead: with his slide-out ending 0.10 s before the tag the lead is shortened");
        log.Near(10f + StageMath.SlideOutSeconds, leads[2].Start, 1e-5f, "lead: …it starts when the slide-out has ended");
        log.Near(0.10f, leads[2].Seconds, 1e-4f, "lead: …0.10 s early, so he lands 0.55 s after the tag");
        leads = ArrivalLeads.Plan(new[] { Cue(1f, C), Cue(10f, Off), Cue(10.20f, C) }, noBlack);
        log.IsTrue(!leads[2].Led && leads[2].Start == 10.20f, "lead: with no room at all it starts on its tag");
        leads = ArrivalLeads.Plan(new[] { Cue(1f, C), Cue(10f, Off, cut: true), Cue(10.20f, C) }, noBlack);
        log.IsTrue(leads[2].Led && leads[2].Shortened && Mathf.Abs(leads[2].Start - 10f) < 1e-5f, "lead: never before the previous {Position:} fired (a cut to Off 0.20 s earlier)");
        leads = ArrivalLeads.Plan(new[] { Cue(0.12f, Off, cut: true), Cue(0.25f, C) }, noBlack);
        log.IsTrue(leads[1].Led && leads[1].Start >= 0f && leads[1].Seconds <= 0.25f + 1e-5f, $"lead: never before the take starts (starts at {leads[1].Start:F2} s)");
        leads = ArrivalLeads.Plan(new[] { Cue(0.12f, Off), Cue(14f, C) }, noBlack);
        log.IsTrue(leads[1].Led && !leads[1].Shortened, "lead: after an opening {Position:Off} his first entrance is led in full");

        // Blacks and transitions.
        var blacks = new List<Vector2> { new Vector2(18f, 22f) };
        leads = ArrivalLeads.Plan(new[] { Cue(1f, C), Cue(10f, Off), Cue(20f, C) }, blacks);
        log.IsTrue(!leads[2].Led && leads[2].Why.Contains("Black"), "lead: an arrival under a {Black} is not led");
        leads = ArrivalLeads.Plan(new[] { Cue(1f, C), Cue(10f, Off), Cue(25f, C) }, blacks);
        log.IsTrue(leads[2].Led && !leads[2].Shortened, "lead: an arrival after a {Black} has gone is led as usual");
        leads = ArrivalLeads.Plan(new[] { Cue(1f, C), Cue(10f, Off), Cue(22f, C) }, blacks);
        log.IsTrue(leads[2].Led, "lead: one on the very beat the {Black} ends is led — he is standing there when it lifts");
        leads = ArrivalLeads.Plan(new[] { Cue(1f, C), Cue(10f, Off), Cue(20f, C, cut: true, onTransition: true) }, noBlack);
        log.IsTrue(!leads[2].Led && leads[2].Why.Contains("Transition"), "lead: an arrival on a {Transition} line is not led (it snaps under the cover)");
        leads = ArrivalLeads.Plan(new[] { Cue(1f, C), Cue(10f, Off, cut: true, onTransition: true), Cue(10.5f, C) }, noBlack);
        log.IsTrue(leads[2].Led && leads[2].Shortened && Mathf.Abs(leads[2].Start - (10f + ArrivalLeads.TransitionCoverSeconds)) < 1e-5f,
            "lead: after a {Transition} line took him off, not before its cover has landed");

        // In a run with a {Pause}: the tag's time is the pinned one — the lead is measured from it, like any other.
        // (Tags above the pause fire as the gap opens, at 30.0; the position below it as it closes, at 31.5.)
        leads = ArrivalLeads.Plan(new[] { Cue(1f, C), Cue(30.0f, Off), Cue(31.5f, C) }, noBlack);
        log.IsTrue(leads[2].Led && !leads[2].Shortened, "lead: a position pinned to the end of a {Pause} is led from that final time");
        log.Near(31.5f - StageMath.PopUpSeconds, leads[2].Start, 1e-5f, "lead: …he rises inside the pause and has landed as the line begins");
        log.Near(31.5f, 31.5f - leads[2].Seconds + StageMath.PopUpSeconds, 1e-5f, "lead: …and the tag's own time is not touched");
        // A pause too short for both moves: the lead is shortened, never the slide-out.
        leads = ArrivalLeads.Plan(new[] { Cue(1f, C), Cue(30.0f, Off), Cue(30.8f, C) }, noBlack);
        log.IsTrue(leads[2].Led && leads[2].Shortened && Mathf.Abs(leads[2].Start - (30f + StageMath.SlideOutSeconds)) < 1e-5f,
            "lead: 0.8 s between leaving and coming back holds the 0.60 s slide-out and a 0.20 s head start");

        // The black stretches, from their markers.
        List<Vector2> stretches = ArrivalLeads.BlackStretches(new[] { (5f, 2f, false, false), (10f, 0f, true, false), (13f, 0f, false, true), (40f, 0f, true, false) }, 60f);
        log.IsTrue(stretches.Count == 3 && stretches[0] == new Vector2(5f, 7f) && stretches[1] == new Vector2(10f, 13f) && stretches[2] == new Vector2(40f, 60f),
            "lead: a timed {Black} lasts its seconds, a held one to its End (or the take's)");
        log.IsTrue(ArrivalLeads.UnderBlack(6f, stretches) && !ArrivalLeads.UnderBlack(7f, stretches) && !ArrivalLeads.UnderBlack(9f, stretches), "lead: under a black means it is still up at that time");
    }

    static void Tier5_ArrivalFaces(Log log)
    {
        List<TimeMarkerData> Faces() => new List<TimeMarkerData>
        {
            new TimeMarkerData { triggerTime = 5.00f, emotion = "Neutral" },
            new TimeMarkerData { triggerTime = 19.20f, emotion = "Smirk" },          // before his rise: he is off, nothing to move
            new TimeMarkerData { triggerTime = 20.00f, emotion = "EyebrowRaised" },  // written with the arrival
            new TimeMarkerData { triggerTime = 20.60f, emotion = "Shrug" },          // after he has landed (below a {Pause}, say)
        };
        var windows = new List<Vector2> { new Vector2(20f - StageMath.PopUpSeconds, 20f) };

        List<TimeMarkerData> faces = Faces();
        List<string> moved = HybridAvatarSystem.ApplyArrivalFaces(faces, windows);
        TimeMarkerData arrival = faces.Find(m => m.emotion == "EyebrowRaised");
        log.AreEqual(1, moved.Count, "face: one emotion is the face he arrives with");
        log.Near(20f - StageMath.PopUpSeconds, arrival.triggerTime, 1e-5f, "face: it is applied on the frame his rise starts");
        log.IsTrue(arrival.transitionOverride == PresenterTransitionSettings.Style.Cut, "face: …as a cut (he comes up wearing it, no change on landing)");
        log.Near(19.20f, faces.Find(m => m.emotion == "Smirk").triggerTime, 1e-6f, "face: an emotion before the rise is left alone");
        log.IsTrue(faces.Find(m => m.emotion == "Smirk").transitionOverride == null, "face: …in its own style");
        log.Near(20.60f, faces.Find(m => m.emotion == "Shrug").triggerTime, 1e-6f, "face: an emotion after he has landed is left alone (it belongs to the line)");
        log.IsTrue(faces[0].emotion == "Neutral" && faces[1].emotion == "Smirk" && faces[2].emotion == "EyebrowRaised" && faces[3].emotion == "Shrug", "face: the markers stay in time order");

        // A face stamped a few ms after the tag is still the arrival's.
        faces = Faces();
        faces[2].triggerTime = 20.012f;
        log.AreEqual(1, HybridAvatarSystem.ApplyArrivalFaces(faces, windows).Count, "face: a face stamped 12 ms after the tag is still his arrival face");
        // Two faces in the window: both on the start frame, in script order (the later one wins, as always).
        faces = Faces();
        faces[1].triggerTime = 19.90f;
        HybridAvatarSystem.ApplyArrivalFaces(faces, windows);
        log.IsTrue(Mathf.Approximately(faces[1].triggerTime, faces[2].triggerTime) && faces[1].emotion == "Smirk" && faces[2].emotion == "EyebrowRaised",
            "face: two faces written into the rise both land on its first frame, in script order");
        // No led arrival: nothing changes.
        faces = Faces();
        log.AreEqual(0, HybridAvatarSystem.ApplyArrivalFaces(faces, new List<Vector2>()).Count, "face: with no led arrival nothing is moved");
        log.IsTrue(faces[2].triggerTime == 20f && faces[2].transitionOverride == null, "face: …every emotion as written (an old script is untouched)");
    }

    static void Tier5_LedArrivalOnTheRig_Engine(Log log)
    {
        TMP_FontAsset savedFont = VisualsRuntimeApplier.CardFontOverride;
        VisualsRuntimeApplier.CardFontOverride = null;
        StageRig rig = BuildStageRig();
        ContentZoneController cz = BuildStageZone(out GameObject zoneRoot, out RectTransform sideZone);
        try
        {
            cz.mediaPresentationSystem = rig.Mps;
            rig.Mps.contentZoneController = cz;
            CharacterPosition C = CharacterPosition.Center, R = CharacterPosition.Right, Off = CharacterPosition.Off, PR = CharacterPosition.PeekRight;

            // The plan on the parsed markers: positions on transition lines are part of the chain.
            var markers = new List<PositionMarkerData>
            {
                new PositionMarkerData { triggerTime = 1f, position = C },
                new PositionMarkerData { triggerTime = 10f, position = Off },
                new PositionMarkerData { triggerTime = 20f, position = R },                              // led
                new PositionMarkerData { triggerTime = 30f, position = Off, hardCutOverride = true },
                new PositionMarkerData { triggerTime = 40f, position = C, hardCutOverride = true },      // a cut: not led
                new PositionMarkerData { triggerTime = 60f, position = PR },                             // after the transition's Off: led
                new PositionMarkerData { triggerTime = 70f, position = C },                              // out of a peek: not led
            };
            rig.Set("positionMarkers", markers);
            rig.Set("transitionMarkers", new List<TransitionMarkerData> { new TransitionMarkerData { triggerTime = 50f, hasPosition = true, position = Off } });
            rig.Set("blackPanelMarkers", new List<BlackPanelMarkerData>());
            rig.Call("PlanArrivalLeads", 100f);
            log.IsTrue(markers[2].led && !markers[2].leadShortened, "led rig: the pop-up at Right from off stage is led");
            log.Near(20f - StageMath.PopUpSeconds, markers[2].startTime, 1e-5f, "led rig: …starting 0.65 s before its tag");
            log.Near(20f, markers[2].triggerTime, 1e-6f, "led rig: …whose own time is unchanged");
            log.IsTrue(!markers[0].led && !markers[1].led && !markers[3].led && !markers[4].led && !markers[6].led, "led rig: the first position, the exits, the cut and the move out of a peek are not");
            log.IsTrue(markers[5].led && Mathf.Abs(markers[5].startTime - (60f - StageMath.PeekRiseSeconds)) < 1e-5f, "led rig: a peek after a {Transition} line took him off is led");

            // The captions and the camera are told the START of his move.
            rig.Set("lastTriggeredPositionMarker", 1);
            rig.Set("currentPosition", Off);
            log.Near(markers[2].startTime, rig.Mps.NextCaptionStop(12f), 1e-5f, "led rig: the next caption stop is the start of his rise, not the tag");
            log.Near(markers[2].startTime, rig.Mps.NextCameraStop(12f), 1e-5f, "led rig: …and so is the next camera stop (the drift is home before he rises)");
            rig.Set("lastTriggeredPositionMarker", 3);
            log.Near(50f, rig.Mps.NextCameraStop(32f), 1e-5f, "led rig: a cut arrival (at 40 s) is no camera stop, as before — the next one is the transition");
            List<Vector2> windows = rig.Mps.LedArrivalWindows();
            log.IsTrue(windows.Count == 2 && Mathf.Approximately(windows[0].y, 20f) && Mathf.Approximately(windows[1].y, 60f), "led rig: the arrival-face windows are the two led arrivals");

            // He starts early over a wide card: it stays wide and centred while he rises…
            rig.Set("currentPosition", C);
            rig.Call("MoveToPosition", Off, (bool?)true);
            ContentCardEvent statEvt = StatEvent("40%", 1f, EntryDirection.FromRight);
            cz.SetTimeline(new List<ContentCardEvent> { statEvt }, null);
            cz.ShowCard(statEvt);
            ContentCard stat = cz.ActiveCard;
            rig.Set("isShowingMedia", true);
            rig.Set("mediaIsFullFrame", true);
            int token = rig.Get<int>("mediaDismissToken");
            rig.Set("positionLedUntil", 20f);
            rig.Call("MoveToPosition", C, (bool?)null);
            log.AreEqual(C, rig.Mps.CurrentPosition, "led rig: started early, he is on his way to Center");
            log.IsTrue(rig.Get<bool>("stageMoving") && !rig.Get<bool>("presenterGliding"), "led rig: nothing waits for a led arrival (he will have landed by the tag)");
            log.IsTrue(cz.ActiveCard == stat && !stat.IsHiding && stat.transform.parent.name == "ContentZone_Cards_Wide" && cz.IsStageWide,
                "led rig: the wide card stays wide and up while he rises (the stage has not changed yet)");
            log.AreEqual(token, rig.Get<int>("mediaDismissToken"), "led rig: the full-frame picture is still up too");
            log.AreEqual(MugsOnScreen.Full, (MugsOnScreen)rig.Call("StageState"), "led rig: the meter counts him from the start of his move");
            log.AreEqual(1, rig.Get<int>("stageLedArrivals"), "led rig: one led arrival counted");
            // He lands before the stage changes: he must stay in FRONT of the wide card that is still up.
            rig.Call("SetPresenter", (Vector2)rig.Center.position, 1f, rig.Get<bool>("frontHeld"));   // what the travel does on landing
            log.AreEqual((int?)StageMath.PeekSortingOrder, rig.Mps.PresenterOrderNow, "led rig: landed early, he keeps the peek's layer — the card is still up in front of where he stands");
            rig.Call("StepFrontHold");
            log.AreEqual((int?)StageMath.PeekSortingOrder, rig.Mps.PresenterOrderNow, "led rig: …for as long as the stage has not changed");
            log.AreEqual(0, rig.Get<int>("stageShortenedLeads"), "led rig: …not shortened");

            // …and the stage changes on the tag's time.
            rig.Set("stageMoving", false);   // (his rise has ended: the travel is a coroutine)
            rig.Set("stageRun", null);
            rig.Call("ApplyPendingStageChange");
            log.IsTrue(stat.IsHiding && !cz.IsStageWide, "led rig: on the tag's time Center hides the side card and the stage is narrow");
            log.AreEqual(token + 1, rig.Get<int>("mediaDismissToken"), "led rig: …and his return ends the picture, as it always did");
            log.IsTrue(!rig.Get<bool>("presenterGliding"), "led rig: he had landed, so nothing is held");
            rig.Call("ApplyPendingStageChange");
            log.AreEqual(token + 1, rig.Get<int>("mediaDismissToken"), "led rig: the stage changes once");
            rig.Call("StepFrontHold");
            log.AreEqual((int?)StageMath.PeekSortingOrder, rig.Mps.PresenterOrderNow, "led rig: while the card fades he is still in front of it");
            cz.ClearForTransition();   // (the fade has finished)
            rig.Call("StepFrontHold");
            log.IsTrue(rig.Mps.PresenterOrderNow == null && !rig.Get<bool>("frontHeld"), "led rig: once it has gone he is back at his own sorting order");

            // A lead that was cut short: on the tag he is still rising, and what comes due waits for the rest.
            cz.ClearForTransition();
            rig.Set("currentPosition", C);
            rig.Call("MoveToPosition", Off, (bool?)true);
            rig.Set("positionLedUntil", 30f);
            rig.Call("MoveToPosition", R, (bool?)null);
            rig.Call("ApplyPendingStageChange");   // the tag's time, his rise still running
            log.IsTrue(rig.Get<bool>("presenterGliding"), "led rig: with a shortened lead a side card waits for the rest of his rise");

            // Told to leave again before the tag: the stage never changed, so nothing changes back.
            rig.Call("FinishStageTravelNow");
            rig.Set("currentPosition", C);
            rig.Call("MoveToPosition", Off, (bool?)true);
            cz.ShowCard(statEvt);
            stat = cz.ActiveCard;
            token = rig.Get<int>("mediaDismissToken");
            rig.Set("positionLedUntil", 40f);
            rig.Call("MoveToPosition", C, (bool?)null);
            rig.Call("MoveToPosition", Off, (bool?)true);
            log.IsTrue(rig.Mps.IsPresenterOff && cz.ActiveCard == stat && !stat.IsHiding && cz.IsStageWide, "led rig: {Position:Off} before the tag — the wide card never moved");
            log.AreEqual(token, rig.Get<int>("mediaDismissToken"), "led rig: …and the picture was never ended");

            // A peek's rise that starts early has nothing to keep for later.
            rig.Set("positionLedUntil", 50f);
            rig.Call("MoveToPosition", PR, (bool?)null);
            log.IsTrue(rig.Mps.IsPresenterPeeking && cz.IsStageWide, "led rig: a led peek — the stage simply stays wide");

            // Without a lead nothing is different: the pop-up changes the stage on the spot.
            rig.Call("FinishStageTravelNow");
            rig.Set("currentPosition", C);
            rig.Call("MoveToPosition", Off, (bool?)true);
            cz.ClearForTransition();
            cz.ShowCard(statEvt);
            stat = cz.ActiveCard;
            rig.Call("MoveToPosition", R, (bool?)null);
            log.IsTrue(!cz.IsStageWide && rig.Get<bool>("presenterGliding"), "led rig: an arrival that is not led changes the stage at once and holds side cards, as before");

            rig.Set("positionMarkers", new List<PositionMarkerData>());
            rig.Call("BeginStageRun");
            log.IsTrue(rig.Get<int>("stageLedArrivals") == 0 && rig.Get<int>("stageShortenedLeads") == 0, "led rig: a new take starts its counts at zero");
        }
        finally
        {
            VisualsRuntimeApplier.CardFontOverride = savedFont;
            UnityEngine.Object.DestroyImmediate(zoneRoot);
            UnityEngine.Object.DestroyImmediate(rig.Root);
        }
    }
}
