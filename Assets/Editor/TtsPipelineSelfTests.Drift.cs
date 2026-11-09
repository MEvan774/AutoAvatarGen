using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MugsTech.Motion;

/// <summary>
/// Tier 3 step 3 — idle camera drift: the motion's size and periods (never
/// commensurate), the envelope's ease in / hand-off, the camera exactly on its
/// rest framing at the first frame of a zoom fired 0.1 s, 1 s and 10 s into a
/// drift (pure and on a real Camera), black and foreign camera moves, the
/// background's 40 % parallax, and no exposed edge for 16:9 and 3:2
/// backdrops at the drift's extremes. Plus the switch: off makes nothing.
/// </summary>
public static partial class TtsPipelineSelfTests
{
    [MenuItem("MugsTech/Motion/Run Idle Drift Self-Tests")]
    public static void RunDrift()
    {
        var log = new Log();
        RunDriftGroup(log);
        log.Report();
    }

    static void RunDriftGroup(Log log)
    {
        Drift_Motion(log);
        Drift_PeriodsNeverLineUp(log);
        Drift_EnvelopeHandsOffAtZero(log);
        Drift_CameraAtRestWhenZoomStarts(log);
        Drift_BlackAndForeignMoves(log);
        Drift_Parallax(log);
        Drift_BackgroundEdges(log);
        Drift_SwitchOff(log);
    }

    const float DriftHalfH = 5f, DriftAspect = 16f / 9f;
    static float DriftHalfW => DriftHalfH * DriftAspect;
    static float DriftSway => IdleDriftMath.SwayFraction * 2f * DriftHalfW;

    static void Drift_Motion(Log log)
    {
        log.AreEqual(0f, IdleDriftMath.Push(0f), "drift: the push starts at rest (scale 1.000)");
        float maxPush = 0f, maxSway = 0f;
        for (float t = 0f; t < 60f; t += 0.01f)
        {
            maxPush = Mathf.Max(maxPush, IdleDriftMath.Push(t));
            maxSway = Mathf.Max(maxSway, Mathf.Abs(IdleDriftMath.Sway(t)));
        }
        log.Near(1.025f, 1f + maxPush, 1e-4f, "drift: the push peaks at scale 1.025");
        log.Near(1f, maxSway, 1e-3f, "drift: the sway swings a full ±1 of its amplitude");
        log.Near(0.0533f, DriftSway, 1e-3f, "drift: the sway is 0.3 % of the 17.78-unit frame width");
        IdleDriftMath.Pose(IdleDriftMath.PushPeriod / 2f, 1f, DriftSway, out float zoom, out _);
        log.Near(1.025f, zoom, 1e-5f, "drift: at full gain, mid-period, the camera is pushed to 1.025");
        IdleDriftMath.Pose(7f, 0f, DriftSway, out zoom, out float dx);
        log.IsTrue(zoom == 1f && dx == 0f, "drift: gain 0 is exactly the rest framing");
    }

    static void Drift_PeriodsNeverLineUp(Log log)
    {
        float p1 = IdleDriftMath.PushPeriod, p2 = IdleDriftMath.SwayPeriod;
        log.IsTrue(p1 >= 19f && p1 <= 22f, $"drift: the push period is ~20 s ({p1:F2})");
        log.IsTrue(p2 >= 12f && p2 <= 14f, $"drift: the sway period is ~13 s ({p2:F2})");
        float ratio = p1 / p2;
        log.Near(IdleDriftMath.GoldenRatio, ratio, 1e-5f, "drift: the periods are in the golden ratio");
        float closest = float.PositiveInfinity;
        string near = "";
        for (int q = 1; q <= 12; q++)
        {
            int p = Mathf.RoundToInt(ratio * q);
            float d = Mathf.Abs(ratio - (float)p / q);
            if (d < closest) { closest = d; near = $"{p}/{q}"; }
        }
        log.IsTrue(closest > 0.004f, $"drift: the period ratio is no simple fraction (nearest {near}, off by {closest:F4})");

        // The pose never comes back to its start (within 5 % of either cycle) inside 100 s —
        // longer than the longest bare stretch measured (69 s).
        float worst = 1f, at = 0f;
        for (float t = 5f; t <= 100f; t += 0.01f)
        {
            float a = t / p1 - Mathf.Floor(t / p1), b = t / p2 - Mathf.Floor(t / p2);
            float da = Mathf.Min(a, 1f - a), db = Mathf.Min(b, 1f - b);
            float both = Mathf.Max(da, db);
            if (both < worst) { worst = both; at = t; }
        }
        log.IsTrue(worst > 0.05f, $"drift: no repeat of the starting pose within 100 s (closest {worst:F3} of a cycle at {at:F1} s)");
    }

    // A take's frames at ~30 fps with uneven steps, and a voice clock that moves in 20 ms audio blocks.
    static IEnumerable<(float dt, float voice)> Frames(float until, bool jitter)
    {
        float t = 0f;
        int i = 0;
        while (t < until + 0.2f)
        {
            float dt = jitter ? (i % 3 == 0 ? 1f / 20f : 1f / 45f) : 1f / 30f;
            t += dt;
            i++;
            yield return (dt, Mathf.Floor(t / 0.02f) * 0.02f);
        }
    }

    static void Drift_EnvelopeHandsOffAtZero(Log log)
    {
        var e = new DriftEnvelope();
        for (int i = 0; i < 45; i++) e.Step(1f / 60f, true, float.PositiveInfinity, false);   // 0.75 s
        log.Near(0.5f, e.Gain, 0.01f, "envelope: halfway through the 1.5 s ease-in the gain is ½ (smoothstep)");
        for (int i = 0; i < 60; i++) e.Step(1f / 60f, true, float.PositiveInfinity, false);
        log.AreEqual(1f, e.Gain, "envelope: full drift after 1.5 s of bare frame");
        for (int i = 0; i < 18; i++) e.Step(1f / 60f, false, float.PositiveInfinity, false);  // 0.3 s
        log.Near(0.5f, e.Gain, 0.01f, "envelope: a frame that stops being bare eases out over 0.6 s");
        e.Step(0.01f, true, float.PositiveInfinity, true);
        log.AreEqual(0f, e.Gain, "envelope: black / a busy camera stops it dead");

        foreach (bool jitter in new[] { false, true })
            foreach (float lead in new[] { 0.1f, 1f, 10f })
            {
                var env = new DriftEnvelope();
                float zoomAt = lead, prev = 0f, maxJump = 0f, peak = 0f, lastBefore = -1f, atZoom = -1f;
                foreach (var (dt, voice) in Frames(lead, jitter))
                {
                    float g = env.Step(dt, true, zoomAt - voice, false);
                    maxJump = Mathf.Max(maxJump, Mathf.Abs(g - prev));
                    prev = g;
                    peak = Mathf.Max(peak, g);
                    if (voice < zoomAt) lastBefore = g;
                    else if (atZoom < 0f) atZoom = g;
                }
                string how = (jitter ? "uneven frames" : "30 fps") + $", zoom {lead:0.#} s into the drift";
                log.AreEqual(0f, lastBefore, $"envelope ({how}): exactly 0 on the last frame before the zoom");
                log.AreEqual(0f, atZoom, $"envelope ({how}): exactly 0 on the zoom's frame");
                // 0.2 of the gain is under 1 px of sway and 0.5 % of the push at 1080p.
                log.IsTrue(maxJump < 0.2f, $"envelope ({how}): no pop — at most {maxJump:F3} gain per frame");
                if (lead >= 10f) log.AreEqual(1f, peak, $"envelope ({how}): it reached full drift before handing off");
                if (lead <= 0.1f) log.AreEqual(0f, peak, $"envelope ({how}): a zoom 0.1 s away never lets it start");
            }
    }

    // A camera like the scene's (orthographic 5, 16:9) and the Violet Doodles backdrop's rectangle.
    static (Camera cam, IdleDrift drift, Transform backdrop, GameObject[] all) MakeDriftRig()
    {
        var camGo = new GameObject("DriftTestCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = DriftHalfH;
        cam.aspect = DriftAspect;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "DriftTestBackdrop";
        quad.transform.position = new Vector3(0f, -0.29f, 2f);
        quad.transform.localScale = new Vector3(19.06f, 10.73f, 1f);
        var host = new GameObject("DriftTestHost");
        var drift = host.AddComponent<IdleDrift>();
        drift.BackdropOverride = quad.transform;
        drift.Init(cam, null);
        return (cam, drift, quad.transform, new[] { camGo, quad, host });
    }

    static void Drift_CameraAtRestWhenZoomStarts(Log log)
    {
        foreach (float lead in new[] { 0.1f, 1f, 10f })
        {
            var (cam, drift, backdrop, all) = MakeDriftRig();
            try
            {
                Vector3 restPos = cam.transform.position, bdPos = backdrop.position, bdScale = backdrop.localScale;
                bool moved = false, backdropMoved = false;
                int snapsBefore = IdleDrift.Last.Snaps;
                foreach (var (dt, voice) in Frames(lead, false))
                {
                    if (voice >= lead) break;
                    drift.Advance(dt, new IdleDrift.Inputs { Bare = true, TimeToStop = lead - voice });
                    moved |= cam.orthographicSize != DriftHalfH || cam.transform.position != restPos;
                    backdropMoved |= backdrop.position != bdPos || backdrop.localScale != bdScale;
                }
                // The zoom's frame: ApplyZoom settles the drift first, then reads the camera.
                drift.SettleNow("test zoom");
                string how = $"zoom {lead:0.#} s into a drift";
                log.IsTrue(cam.orthographicSize == DriftHalfH && cam.transform.position == restPos,
                    $"camera ({how}): exactly on its rest framing when the zoom starts");
                log.IsTrue(backdrop.position == bdPos && backdrop.localScale == bdScale,
                    $"camera ({how}): the backdrop exactly as authored when the zoom starts");
                log.AreEqual(snapsBefore, IdleDrift.Last.Snaps, $"camera ({how}): already home — no snap needed");
                if (lead >= 1f) log.IsTrue(moved && backdropMoved, $"camera ({how}): it did breathe before handing off");
                else log.IsTrue(!moved, $"camera ({how}): too close to start — never moved");

                // The zoom now owns the camera: the drift keeps its hands off.
                cam.orthographicSize = DriftHalfH / 1.12f;
                for (int i = 0; i < 10; i++)
                    drift.Advance(1f / 30f, new IdleDrift.Inputs { Bare = false, Hard = true, TimeToStop = float.PositiveInfinity });
                log.IsTrue(cam.orthographicSize == DriftHalfH / 1.12f && cam.transform.position == restPos,
                    $"camera ({how}): the zoom step is left exactly as it set it");
            }
            finally { foreach (var go in all) UnityEngine.Object.DestroyImmediate(go); }
        }
    }

    static void Drift_BlackAndForeignMoves(Log log)
    {
        var (cam, drift, backdrop, all) = MakeDriftRig();
        try
        {
            var bare = new IdleDrift.Inputs { Bare = true, TimeToStop = float.PositiveInfinity };
            Vector3 restPos = cam.transform.position, bdPos = backdrop.position;
            for (int i = 0; i < 300; i++) drift.Advance(1f / 30f, bare);    // 10 s
            log.IsTrue(drift.Gain == 1f && cam.orthographicSize < DriftHalfH, "drift: 10 s of bare frame → full drift, pushed in");

            drift.Advance(1f / 30f, new IdleDrift.Inputs { Bare = false, Hard = true, TimeToStop = float.PositiveInfinity });
            log.IsTrue(cam.orthographicSize == DriftHalfH && cam.transform.position == restPos && backdrop.position == bdPos,
                "drift: {Black} puts the camera and backdrop back on the same frame (unseen behind it)");
            drift.Advance(1f / 30f, bare);
            log.IsTrue(drift.Gain > 0f && drift.Gain < 0.01f, $"drift: after black it resumes from zero (gain {drift.Gain:F4} on the first frame)");

            for (int i = 0; i < 90; i++) drift.Advance(1f / 30f, bare);
            cam.orthographicSize = 4f;   // something else (a zoom path nobody announced) moves the camera
            drift.Advance(1f / 30f, bare);
            log.AreEqual(4f, cam.orthographicSize, "drift: a foreign camera move wins — the drift doesn't overwrite it");
            log.IsTrue(cam.transform.position == restPos && backdrop.position == bdPos,
                "drift: …and the drift's own sway is taken back off what the move didn't touch");
            log.AreEqual(0f, drift.Gain, "drift: …and it stands down from there");

            for (int i = 0; i < 60; i++) drift.Advance(1f / 30f, bare);
            int snaps = IdleDrift.Last.Snaps;
            drift.SettleNow("test unannounced zoom");
            log.IsTrue(cam.orthographicSize == 4f && cam.transform.position == restPos,
                "drift: a zoom that catches it mid-drift snaps to the (new) rest framing first");
            log.AreEqual(snaps + 1, IdleDrift.Last.Snaps, "drift: …and the snap is counted for the result JSON");
        }
        finally { foreach (var go in all) UnityEngine.Object.DestroyImmediate(go); }
    }

    static void Drift_Parallax(Log log)
    {
        const float k = 1.02f, dx = 0.05f, p = IdleDriftMath.BackgroundParallax;
        IdleDriftMath.Background(k, dx, 1f, p, 1f, out float f, out float shift);
        // Screen position (rest units) of a world point X under the drifted camera: (X − C)·k.
        float screenScale = f * k, screenShift = (dx - shift) * k;
        float mugsShift = k * dx;                          // a world-fixed point at the centre
        log.Near(p * dx, screenShift, 1e-6f, "parallax: the backdrop slides 40 % of the camera's sway");
        log.Near(0.4f, screenShift / dx, 1e-4f, "parallax: …while Mugs (in the world) slides the full amount");
        log.IsTrue(mugsShift > screenShift * 2.4f, "parallax: Mugs moves ~2.5× the backdrop");
        log.Near(1f + p * (k - 1f), screenScale, 1e-6f, "parallax: the backdrop zooms 40 % of the push");
        IdleDriftMath.Background(1f, 0f, 0f, p, 1.03f, out f, out shift);
        log.IsTrue(f == 1f && shift == 0f, "parallax: at rest the backdrop is exactly as authored, overscan or not");
    }

    static void Drift_BackgroundEdges(Log log)
    {
        float hw = DriftHalfW, hh = DriftHalfH, a = DriftSway, p = IdleDriftMath.BackgroundParallax;
        var cases = new (string name, float xMin, float xMax, float yMin, float yMax)[]
        {
            ("a 16:9 backdrop that exactly fills the frame", -hw, hw, -hh, hh),
            ("a 3:2 backdrop cover-fitted to the frame",     -hw, hw, -hw / 1.5f, hw / 1.5f),
            ("the Violet Doodles backdrop (19.06 × 10.73)",   -9.53f, 9.53f, -0.29f - 5.365f, -0.29f + 5.365f),
        };
        foreach (var c in cases)
        {
            float o = IdleDriftMath.RequiredOverscan(c.xMin, c.xMax, hw, a, p);
            bool ok = true, rawOk = true;
            string where = "";
            for (int gi = 0; gi <= 20 && ok; gi++)
            {
                float g = gi / 20f;
                for (float t = 0f; t < 300f; t += 0.05f)
                {
                    IdleDriftMath.Pose(t, g, a, out float zoom, out float dx);
                    IdleDriftMath.Background(zoom, dx, g, p, o, out float f, out float shift);
                    if (!IdleDriftMath.Covers(c.xMin, c.xMax, c.yMin, c.yMax, hw, hh, f * zoom, (dx - shift) * zoom))
                    { ok = false; where = $"g {g:F2}, t {t:F2} s"; break; }
                    IdleDriftMath.Background(zoom, dx, g, p, 1f, out f, out shift);
                    rawOk &= IdleDriftMath.Covers(c.xMin, c.xMax, c.yMin, c.yMax, hw, hh, f * zoom, (dx - shift) * zoom);
                }
            }
            log.IsTrue(ok, $"edges: {c.name}, overscanned {(o - 1f) * 100f:F2} %, never shows an edge at any drift ({where})");
            log.IsTrue(o <= 1.03f, $"edges: {c.name} needs at most the brief's ~3 % overscan ({(o - 1f) * 100f:F2} %)");
            if (c.xMax - hw < p * a) log.IsTrue(!rawOk, $"edges: {c.name} WOULD show an edge without its overscan");
        }
        log.AreEqual(1f, IdleDriftMath.RequiredOverscan(-9.53f, 9.53f, hw, a, p),
            "edges: the Violet Doodles backdrop has room — no overscan, rendered as authored");

        // A backdrop the drift can't move: the sway is cut to its side margins instead.
        foreach (var c in cases)
        {
            float room = IdleDriftMath.SwayRoom(c.xMin, c.xMax, hw, a);
            bool ok = true;
            for (float t = 0f; t < 300f && ok; t += 0.05f)
            {
                IdleDriftMath.Pose(t, 1f, room, out float zoom, out float dx);
                ok = IdleDriftMath.Covers(c.xMin, c.xMax, c.yMin, c.yMax, hw, hh, zoom, zoom * dx);
            }
            log.IsTrue(ok, $"edges: {c.name} left in the world, sway {room:F3} units — never shows an edge");
        }
        log.AreEqual(0f, IdleDriftMath.SwayRoom(-hw, hw, hw, a), "edges: a world backdrop with no margin gets no sway (push only)");
        log.AreEqual(a, IdleDriftMath.SwayRoom(-9.53f, 9.53f, hw, a), "edges: a world backdrop with room keeps the full sway");
    }

    static void Drift_SwitchOff(Log log)
    {
        bool had = PlayerPrefs.HasKey(IdleDrift.PrefKey);
        int saved = PlayerPrefs.GetInt(IdleDrift.PrefKey, 1);
        try
        {
            PlayerPrefs.DeleteKey(IdleDrift.PrefKey);
            log.IsTrue(IdleDrift.Enabled, "drift switch: on by default");
            IdleDrift.Enabled = false;
            IdleDrift.Begin(null);
            log.IsTrue(GameObject.Find("IdleDrift") == null && IdleDrift.Last != null && !IdleDrift.Last.Enabled,
                "drift switch: off → no drift object at all, and the result says so");
            IdleDrift.Settle("test");   // with nothing running: a no-op
            log.IsTrue(GameObject.Find("IdleDrift") == null, "drift switch: off → a zoom's settle call does nothing");
            IdleDrift.Enabled = true;
            IdleDrift.Begin(null);
            log.IsTrue(GameObject.Find("IdleDrift") == null, "drift switch: on but no presentation camera → nothing made");
        }
        finally
        {
            if (had) PlayerPrefs.SetInt(IdleDrift.PrefKey, saved); else PlayerPrefs.DeleteKey(IdleDrift.PrefKey);
            PlayerPrefs.Save();
            for (GameObject go = GameObject.Find("IdleDrift"); go != null; go = GameObject.Find("IdleDrift"))
                UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
