using System.Collections.Generic;
using MugsTech.Stage;
using MugsTech.Tts;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The "Hop" emotion transition — a main-menu Presenter transition style
/// only, with no tag keyword: its motion, and that no tag reaches it. All
/// pure — runs offline.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Stage/Run Hop Self-Tests")]
    public static void RunHop()
    {
        var log = new Log();
        RunHopGroup(log);
        log.Report();
    }

    static void RunHopGroup(Log log)
    {
        Hop_Motion(log);
        Hop_StyleAndTag(log);
    }

    static void Hop_Motion(Log log)
    {
        HopMath.Pose start = HopMath.At(0f);
        log.IsTrue(start.Lift == 0f && start.ScaleX == 1f && start.ScaleY == 1f && !start.Swapped, "hop: starts from his rest pose, old face on");

        HopMath.Pose bottom = HopMath.At(HopMath.DuckSeconds);
        log.Near(-HopMath.DuckOfHeight, bottom.Lift, 1e-5f, "hop: ducks 5 % of his height in 0.10 s");
        log.Near(1f - HopMath.Squash, bottom.ScaleY, 1e-5f, "hop: …8 % shorter at the bottom");
        log.Near(1f + HopMath.Squash * 0.5f, bottom.ScaleX, 1e-5f, "hop: …and 4 % wider (he squashes, he doesn't shrink)");
        log.IsTrue(bottom.Swapped && !HopMath.At(HopMath.DuckSeconds - 1e-3f).Swapped, "hop: the expression changes exactly at the bottom of the duck");

        HopMath.Pose end = HopMath.At(HopMath.TotalSeconds);
        log.IsTrue(end.Lift == 0f && end.ScaleX == 1f && end.ScaleY == 1f && end.Swapped, "hop: ends exactly on his rest pose, new face on");
        log.IsTrue(HopMath.At(10f).Lift == 0f, "hop: …and stays there");
        log.Near(0.36f, HopMath.TotalSeconds, 1e-5f, "hop: 0.36 s in all");

        float top = float.NegativeInfinity, low = float.PositiveInfinity, tallest = 0f, biggestStep = 0f, previous = 0f;
        const float frame = 1f / 60f;
        for (float t = 0f; t <= HopMath.TotalSeconds + frame; t += frame)
        {
            HopMath.Pose p = HopMath.At(t);
            top = Mathf.Max(top, p.Lift);
            low = Mathf.Min(low, p.Lift);
            tallest = Mathf.Max(tallest, p.ScaleY);
            biggestStep = Mathf.Max(biggestStep, Mathf.Abs(p.Lift - previous));
            previous = p.Lift;
        }
        log.IsTrue(top > 0.010f && top < 0.025f, $"hop: he springs a little past rest ({top * 100f:F1} % of his height above it)");
        log.IsTrue(low >= -HopMath.DuckOfHeight - 1e-5f, "hop: never lower than the bottom of the duck");
        log.IsTrue(tallest > 1.01f && tallest < 1.04f, $"hop: a touch taller at the top ({tallest:F3})");
        log.IsTrue(biggestStep < 0.02f, $"hop: no jump between frames at 60 fps (largest step {biggestStep * 100f:F2} % of his height)");
        log.IsTrue(Mathf.Abs(HopMath.At(HopMath.TotalSeconds - frame).Lift) < 0.002f, "hop: he lands softly — almost home a frame before the end");

        // The three dials on the avatar (duration, duck, squash): the defaults ARE the hop above…
        for (float t = 0f; t <= HopMath.TotalSeconds; t += 0.03f)
        {
            HopMath.Pose a = HopMath.At(t), b = HopMath.At(t, HopMath.TotalSeconds, HopMath.DuckOfHeight, HopMath.Squash);
            log.IsTrue(a.Lift == b.Lift && a.ScaleX == b.ScaleX && a.ScaleY == b.ScaleY && a.Swapped == b.Swapped, $"hop: the default dials give the same pose at {t:F2} s");
        }
        // …a longer hop keeps its shape, a deeper one ducks further, and no squash means no squash.
        HopMath.Pose slow = HopMath.At(0.20f, 0.72f, HopMath.DuckOfHeight, HopMath.Squash);
        log.Near(-HopMath.DuckOfHeight, slow.Lift, 1e-5f, "hop: at twice the duration the bottom of the duck comes twice as late");
        log.IsTrue(HopMath.At(0.21f, 0.72f, HopMath.DuckOfHeight, HopMath.Squash).Swapped &&
                   !HopMath.At(0.19f, 0.72f, HopMath.DuckOfHeight, HopMath.Squash).Swapped, "hop: …and so does the change of expression");
        log.Near(-0.10f, HopMath.At(HopMath.DuckSeconds, HopMath.TotalSeconds, 0.10f, HopMath.Squash).Lift, 1e-5f, "hop: a 10 % duck ducks 10 %");
        HopMath.Pose stiff = HopMath.At(HopMath.DuckSeconds, HopMath.TotalSeconds, HopMath.DuckOfHeight, 0f);
        log.IsTrue(stiff.ScaleX == 1f && stiff.ScaleY == 1f, "hop: with squash 0 he only moves");
        log.IsTrue(HopMath.At(0.1f, 0f, 0.05f, 0.08f).Swapped && HopMath.At(0.1f, 0f, 0.05f, 0.08f).Lift == 0f, "hop: a zero-length hop is an instant swap at rest");

        // The area under him stays plausible: width and height never both grow.
        HopMath.Pose mid = HopMath.At(HopMath.DuckSeconds * 0.5f);
        log.IsTrue(mid.ScaleY < 1f && mid.ScaleX > 1f, "hop: while ducking he gets shorter and wider together");
    }

    static void Hop_StyleAndTag(Log log)
    {
        // The style: new value at the end, so every saved choice keeps its meaning.
        log.AreEqual(8, (int)PresenterTransitionSettings.Style.Hop, "hop: the style is appended (saved menu choices keep their numbers)");
        log.AreEqual(1, (int)PresenterTransitionSettings.Style.Crossfade, "hop: …Crossfade is still 1");
        log.AreEqual(7, (int)PresenterTransitionSettings.Style.Shrink, "hop: …Shrink is still 7");
        log.AreEqual("Hop", PresenterTransitionSettings.Label(PresenterTransitionSettings.Style.Hop), "hop: the main menu calls it Hop");
        log.AreEqual(PresenterTransitionSettings.Style.Hop,
            PresenterTransitionSettings.Cycle(PresenterTransitionSettings.Style.Shrink, +1), "hop: the menu reaches it after Shrink");
        log.AreEqual(PresenterTransitionSettings.Style.SquashStretch,
            PresenterTransitionSettings.Cycle(PresenterTransitionSettings.Style.Hop, +1), "hop: …and wraps round to the first style");
        // Main menu only: no word names it in a tag…
        foreach (string word in new[] { "Hop", "hop", "bounce" })
            log.IsTrue(!PresenterTransitionSettings.TryParse(word, out _), $"hop: \"{word}\" is not a per-tag style keyword");
        log.IsTrue(PresenterTransitionSettings.TryParse("pop", out var grow) && grow == PresenterTransitionSettings.Style.Grow,
            "hop: \"pop\" still means Grow, as it always did");
        log.IsTrue(PresenterTransitionSettings.TryParse("Shrink", out var shrink) && shrink == PresenterTransitionSettings.Style.Shrink,
            "hop: the per-tag keywords that exist still parse");

        // …so {Smirk,Hop} is not an emotion tag for the avatar (exactly as before Hop existed),
        foreach (string tag in new[] { "{Smirk,Hop}", "{Smirk,Hop,T=1.234}", "{Smirk,T=1.234,Hop}" })
        {
            var m = HybridAvatarSystem.EmotionTagRegex.Match(tag);
            log.IsTrue(!m.Success || m.Length != tag.Length, $"hop: {tag} is not a styled emotion tag");
        }
        var bare = HybridAvatarSystem.EmotionTagRegex.Match("{Hop}");
        log.IsTrue(bare.Success && bare.Groups[1].Value == "Hop" && !bare.Groups[2].Success && !bare.Groups[4].Success,
            "hop: a bare {Hop} is still just an emotion called Hop");
        var shrinkTag = HybridAvatarSystem.EmotionTagRegex.Match("{Smirk,Shrink,T=1.234}");
        log.IsTrue(shrinkTag.Success && shrinkTag.Groups[2].Value == "Shrink", "hop: {Smirk,Shrink} is read as it always was");

        // …and not a known marker for the TTS processor — but, like any unknown tag, never read aloud.
        log.IsTrue(!TtsScriptProcessor.IsRecognisedMarker("{Smirk,Hop}"), "hop: {Smirk,Hop} is not a known marker");
        log.IsTrue(TtsScriptProcessor.IsRecognisedMarker("{Smirk,Shrink}"), "hop: {Smirk,Shrink} still is");
        var (_, clean) = TtsScriptProcessor.ExtractMarkers("Some words {Smirk,Hop} and more words.");
        log.IsTrue(!clean.Contains("{") && !clean.Contains("}") && !clean.Contains("Hop"), $"hop: a stray {{Smirk,Hop}} is still stripped from the narration (\"{clean}\")");
    }
}
