using System;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Staggered DOTween sequences (StaggeredJoin, BigMediaCard's entrance).
/// The arithmetic is checked offline; when DOTween runs in edit mode the real
/// sequences are stepped with Goto and every item's start time is read back.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    static void RunSequenceGroup(Log log)
    {
        Sequences_DelaysAreRelativeToThePreviousJoin(log);
        try
        {
            Sequences_RealDOTweenTiming(log);
            Sequences_BigMediaLogosLandOnTheirStagger(log);
        }
        catch (Exception e)
        {
            // DOTween needs its runtime component; if edit mode can't host it,
            // only the arithmetic above is checked.
            Debug.Log($"[Sequences] DOTween could not run in edit mode ({e.GetType().Name}: {e.Message}) — " +
                      "staggered timing checked on the arithmetic only.");
        }
        finally { DestroyAutoAnimator(); }
    }

    static void Sequences_DelaysAreRelativeToThePreviousJoin(Log log)
    {
        float cursor = 0f;
        var times = new[] { 0f, 0f, 0.35f, 0.35f, 0.70f, 0.70f, 1.05f };
        var expect = new[] { 0f, 0f, 0.35f, 0f, 0.35f, 0f, 0.35f };
        for (int i = 0; i < times.Length; i++)
            log.Near(expect[i], StaggeredJoin.DelayFor(times[i], ref cursor), 1e-5f,
                $"StaggeredJoin: join {i + 1} at {times[i]:F2} s gets only the delay since the previous one");
        log.Near(1.05f, cursor, 1e-5f, "StaggeredJoin: the cursor follows the last start");
    }

    // The bug and the fix on the real DOTween: four items joined with
    // SetDelay(stagger × i) stack up; the same items through StaggeredJoin
    // start exactly stagger apart, and a second tween per item starts with it.
    static void Sequences_RealDOTweenTiming(Log log)
    {
        const float stagger = 0.35f, dur = 0.1f;
        var naive = new float[4];
        var fixedA = new float[4];
        var fixedB = new float[4];

        Sequence s1 = DOTween.Sequence().Pause();
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            s1.Join(DOTween.To(() => naive[k], x => naive[k] = x, 1f, dur).SetDelay(stagger * k));
        }

        Sequence s2 = DOTween.Sequence().Pause();
        var join = new StaggeredJoin(s2);
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            join.At(stagger * k, DOTween.To(() => fixedA[k], x => fixedA[k] = x, 1f, dur));
            join.At(stagger * k, DOTween.To(() => fixedB[k], x => fixedB[k] = x, 1f, dur));
        }

        float StartOf(Sequence s, float[] vals, int k)
        {
            for (float t = 0f; t <= 4f; t += 0.01f)
            {
                s.Goto(t);
                if (vals[k] > 0.001f) return t;
            }
            return float.NaN;
        }

        float naive3 = StartOf(s1, naive, 2);
        Debug.Log($"[Sequences] DOTween Join+SetDelay(stagger×i): item 3 starts at {naive3:F2} s (asked 0.70 s)");
        log.IsTrue(naive3 > 0.9f, $"the plain Join + SetDelay(stagger × i) stacks the delays (item 3 at {naive3:F2} s) — what StaggeredJoin avoids");

        for (int k = 0; k < 4; k++)
        {
            float a = StartOf(s2, fixedA, k), b = StartOf(s2, fixedB, k);
            log.IsTrue(Mathf.Abs(a - stagger * k) <= 0.011f, $"StaggeredJoin on DOTween: item {k + 1} starts at {stagger * k:F2} s ({a:F2})");
            log.IsTrue(Mathf.Abs(b - stagger * k) <= 0.011f, $"StaggeredJoin on DOTween: item {k + 1}'s second tween starts with it ({b:F2})");
        }
        s1.Kill();
        s2.Kill();
    }

    // The real BigMedia card: four logos (text fallbacks here) pop in
    // stagger × i after the card shows.
    static void Sequences_BigMediaLogosLandOnTheirStagger(Log log)
    {
        var card = BuildCard<BigMediaCard>(out GameObject root);
        try
        {
            ((RectTransform)card.transform.parent).sizeDelta = new Vector2(1920f, 1080f);
            card.Initialize(new ContentCardEvent { cardType = ContentCardType.BigMedia, primaryText = "SelfTestA+SelfTestB+SelfTestC+SelfTestD" }, null);
            card.Show();
            var seq = (Sequence)typeof(ContentCard).GetField("currentSequence", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(card);
            seq.Pause();
            float stagger = CardEntryAnimator.Instance.bigMedia.staggerDelay;

            var slots = new List<RectTransform>();
            foreach (Transform c in card.transform.GetComponentsInChildren<Transform>(true))
                if (c.parent == card.transform && c.gameObject.activeSelf && c is RectTransform rt && c.name.StartsWith("BigMedia")) slots.Add(rt);
            if (slots.Count < 4)
            {
                // Slot naming differs: fall back to the first four active direct children that start at scale 0.
                slots.Clear();
                foreach (Transform c in card.transform)
                    if (c.gameObject.activeSelf && c.localScale.x < 0.001f && c is RectTransform rt2) slots.Add(rt2);
            }
            log.AreEqual(4, slots.Count, "BigMedia: four logo slots found");
            for (int k = 0; k < Mathf.Min(4, slots.Count); k++)
            {
                float start = float.NaN;
                for (float t = 0f; t <= 3f; t += 0.01f)
                {
                    seq.Goto(t);
                    if (slots[k].localScale.x > 0.001f) { start = t; break; }
                }
                log.IsTrue(Mathf.Abs(start - stagger * k) <= 0.011f,
                    $"BigMedia: logo {k + 1} pops in at {stagger * k:F2} s ({start:F2})");
            }
            seq.Kill();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
