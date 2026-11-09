using System.Collections.Generic;
using MugsTech.Background;
using UnityEngine;

namespace MugsTech.Motion
{
    /// <summary>
    /// Idle camera drift: whenever the frame is bare — no card, image, video,
    /// BigText or full-screen card up, no zoom run, no close-up, no black —
    /// the camera breathes (<see cref="IdleDriftMath"/>), so the frame is never
    /// perfectly still. Mugs is in the camera's view, so he drifts with it;
    /// the Violet Doodles backdrop drifts at 40 % of that (parallax).
    ///
    /// Main menu "Idle camera drift" (default on). Off: <see cref="Begin"/>
    /// makes nothing, so the take is exactly today's.
    ///
    /// HAND-OFF — the drift sees every zoom, close-up, card, image, black and
    /// transition coming on the voice clock (MediaPresentationSystem
    /// .NextCameraStop) and eases back to exactly the rest framing before it
    /// lands, so zoom steps start from the clean base and cards slide into a
    /// still frame. The camera is written as rest × drift each LateUpdate and
    /// put back bit-for-bit when the gain reaches zero. If anything else moves
    /// the camera while drifting, that move wins and the drift stands down;
    /// <see cref="Settle"/> (called by ApplyZoom) snaps home first if an
    /// unannounced zoom ever catches it mid-drift — and counts it.
    ///
    /// Time base: Time.deltaTime, like the zoom glides; the lookahead reads
    /// the voice clock, like the triggers.
    /// </summary>
    [DefaultExecutionOrder(-100)]   // before the vignette's LateUpdate follows the camera
    public class IdleDrift : MonoBehaviour
    {
        public const string PrefKey = "AutoAvatarGen.IdleCameraDrift";

        /// <summary>Main menu "Idle camera drift" (default on).</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 1) != 0;
            set { PlayerPrefs.SetInt(PrefKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>This take's drift, for the result JSON (stays after the scene).</summary>
        public sealed class Report
        {
            public bool Enabled;
            public float DriftSeconds;     // time the camera was off its rest framing
            public int Stretches;          // bare stretches that drifted
            public int Snaps;              // zooms that found the drift not yet home (should be 0)
            public string Background;      // how the background was treated
        }

        public static Report Last { get; private set; }

        /// <summary>What the drift reads each frame (public so the self-tests can drive it).</summary>
        public struct Inputs
        {
            public bool Bare;          // nothing on the frame, camera free, voice started
            public bool Hard;          // black up, or the zoom system has the camera
            public float TimeToStop;   // seconds to the next event that lands on the frame (∞: none)
        }

        static IdleDrift s_Instance;

        Camera cam;
        MediaPresentationSystem mps;
        readonly DriftEnvelope envelope = new DriftEnvelope();
        float clock;

        // The rest framing while the drift is applied, and what we last wrote.
        bool applied;
        float restSize, writtenSize;
        Vector3 restPos, writtenPos;

        // The parallax backdrop (Violet Doodles), if any.
        Transform backdrop;
        Vector3 backdropRestPos, backdropRestScale, backdropWrittenPos, backdropWrittenScale;
        float overscan = 1f;
        float swayAmplitude;

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        /// <summary>Starts the drift for this take (nothing at all when the switch is off).</summary>
        public static void Begin(MediaPresentationSystem presentation)
        {
            Last = new Report { Enabled = Enabled };
            if (!Enabled)
            {
                Debug.Log("[Drift] Idle camera drift is off.");
                return;
            }
            if (presentation == null || presentation.mainCamera == null)
            {
                Last.Background = "no camera";
                return;
            }
            if (s_Instance == null)
                s_Instance = new GameObject("IdleDrift").AddComponent<IdleDrift>();
            s_Instance.Init(presentation.mainCamera, presentation);
        }

        /// <summary>Binds to a camera (and the presentation system for its live inputs; null in the self-tests).</summary>
        public void Init(Camera camera, MediaPresentationSystem presentation)
        {
            if (applied) Restore();
            cam = camera;
            mps = presentation;
            envelope.Reset();
            clock = 0f;
            backdrop = null;
            if (Last == null) Last = new Report { Enabled = true };
        }

        void OnDestroy()
        {
            if (applied) Restore();
            if (s_Instance == this) s_Instance = null;
        }

        /// <summary>
        /// Puts the camera (and backdrop) back on its rest framing at once.
        /// ApplyZoom calls it before any zoom touches the camera; normally the
        /// drift is already home, and a snap is counted and logged.
        /// </summary>
        public static void Settle(string why)
        {
            if (s_Instance == null) return;
            s_Instance.SettleNow(why);
        }

        public void SettleNow(string why)
        {
            if (!applied) { envelope.Reset(); return; }
            if (Last != null) Last.Snaps++;
            Debug.LogWarning($"[Drift] {why} came while the drift was {envelope.Gain:P0} out — snapped to the rest framing first.");
            Restore();
            envelope.Reset();
        }

        void LateUpdate()
        {
            if (cam == null || mps == null) return;
            Advance(Time.deltaTime, LiveInputs());
        }

        Inputs LiveInputs()
        {
            bool started = TakeLoudness.VoiceStarted;
            bool card = mps.contentZoneController != null && mps.contentZoneController.HasActiveOrQueuedCard;
            bool black = mps.IsBlackShowing;
            bool busy = mps.IsCameraBusy;
            float now = TakeLoudness.VoiceTime;
            float next = float.IsNaN(now) ? float.PositiveInfinity : mps.NextCameraStop(now);
            return new Inputs
            {
                Bare = started && !card && !mps.IsShowingMedia && !busy && !black,
                Hard = black || busy,
                TimeToStop = next - now,
            };
        }

        // ---------------------------------------------------------------------
        // The frame step (LateUpdate; the self-tests call it directly)
        // ---------------------------------------------------------------------

        /// <summary>The camera's current gain (0 = rest framing).</summary>
        public float Gain => applied ? envelope.Gain : 0f;

        public void Advance(float dt, Inputs inputs)
        {
            if (cam == null) return;

            // Someone else moved the camera (or the backdrop) since our write:
            // their move wins and the drift stands down — taking its own offset
            // back off whatever they didn't touch, so none of it is left baked in.
            if (applied && (cam.orthographicSize != writtenSize || cam.transform.position != writtenPos ||
                            (backdrop != null && (backdrop.position != backdropWrittenPos || backdrop.localScale != backdropWrittenScale))))
            {
                if (cam.orthographicSize == writtenSize) cam.orthographicSize = restSize;
                if (cam.transform.position == writtenPos) cam.transform.position = restPos;
                if (backdrop != null && backdrop.position == backdropWrittenPos && backdrop.localScale == backdropWrittenScale)
                {
                    backdrop.position = backdropRestPos;
                    backdrop.localScale = backdropRestScale;
                }
                applied = false;
                envelope.Reset();
                Debug.Log("[Drift] The camera was moved by something else — the drift stands down from there.");
                return;
            }

            float g = envelope.Step(dt, inputs.Bare, inputs.TimeToStop, inputs.Hard);
            if (g <= 0f)
            {
                if (applied) Restore();
                return;
            }

            if (!applied)
            {
                CaptureRest();
                if (Last != null) Last.Stretches++;
            }
            clock += dt;
            if (Last != null) Last.DriftSeconds += dt;
            Apply(g);
        }

        void CaptureRest()
        {
            restSize = cam.orthographicSize;
            restPos = cam.transform.position;
            MeasureLayout();
            if (backdrop != null)
            {
                backdropRestPos = backdrop.position;
                backdropRestScale = backdrop.localScale;
            }
            applied = true;
        }

        void Apply(float g)
        {
            IdleDriftMath.Pose(clock, g, swayAmplitude, out float zoom, out float dx);
            writtenSize = restSize / zoom;
            writtenPos = restPos + new Vector3(dx, 0f, 0f);
            cam.orthographicSize = writtenSize;
            cam.transform.position = writtenPos;
            writtenSize = cam.orthographicSize;     // read back what the engine stored
            writtenPos = cam.transform.position;

            if (backdrop != null)
            {
                IdleDriftMath.Background(zoom, dx, g, IdleDriftMath.BackgroundParallax, overscan, out float f, out float shift);
                Vector3 rel = backdropRestPos - restPos;
                backdrop.position = new Vector3(restPos.x + shift + rel.x * f, restPos.y + rel.y * f, backdropRestPos.z);
                backdrop.localScale = backdropRestScale * f;
                backdropWrittenPos = backdrop.position;
                backdropWrittenScale = backdrop.localScale;
            }
        }

        void Restore()
        {
            if (cam != null)
            {
                cam.orthographicSize = restSize;
                cam.transform.position = restPos;
            }
            if (backdrop != null)
            {
                backdrop.position = backdropRestPos;
                backdrop.localScale = backdropRestScale;
            }
            applied = false;
        }

        // ---------------------------------------------------------------------
        // The background: parallax on the Violet Doodles backdrop; any other
        // backdrop stays in the world, so the sway is kept inside its margins.
        // ---------------------------------------------------------------------

        /// <summary>For the self-tests: a backdrop to drive with parallax instead of the scene's.</summary>
        public Transform BackdropOverride;

        void MeasureLayout()
        {
            float halfH = restSize, halfW = restSize * cam.aspect;
            float full = IdleDriftMath.SwayFraction * 2f * halfW;
            swayAmplitude = full;
            overscan = 1f;
            backdrop = null;

            Renderer doodles = null;
            if (BackdropOverride != null) doodles = BackdropOverride.GetComponent<Renderer>();
            else if (mps != null && BackgroundModeManager.LoadMode() == BackgroundModeManager.Mode.Normal)
            {
                VioletDoodlesMood mood = VioletDoodlesMood.FindOrAttach();
                if (mood != null && mood.isActiveAndEnabled && mood.Backdrop != null) doodles = mood.Backdrop;
            }

            string note;
            if (doodles != null)
            {
                backdrop = doodles.transform;
                Bounds b = doodles.bounds;
                overscan = IdleDriftMath.RequiredOverscan(b.min.x - restPos.x, b.max.x - restPos.x, halfW, full,
                                                          IdleDriftMath.BackgroundParallax);
                note = $"backdrop at {IdleDriftMath.BackgroundParallax:P0} parallax, overscan {(overscan - 1f) * 100f:F2} %";
            }
            else
            {
                Renderer cover = mps != null ? LargestBackgroundRenderer() : null;
                if (cover != null)
                {
                    Bounds b = cover.bounds;
                    swayAmplitude = IdleDriftMath.SwayRoom(b.min.x - restPos.x, b.max.x - restPos.x, halfW, full);
                    note = swayAmplitude < full
                        ? $"'{cover.name}' stays put; sway cut to {swayAmplitude:F3} of {full:F3} units to keep its edges out of frame"
                        : $"'{cover.name}' stays put (room for the full sway)";
                }
                else note = "no world backdrop found — full sway";
            }
            if (Last != null && Last.Background != note)
            {
                Last.Background = note;
                Debug.Log($"[Drift] Breathing: push {IdleDriftMath.PushAmount:P1} / {IdleDriftMath.PushPeriod:F0} s, " +
                          $"sway {swayAmplitude:F3} units / {IdleDriftMath.SwayPeriod:F2} s; {note}.");
            }
        }

        // The biggest non-particle renderer under the active backdrop's root —
        // the plane it paints on. The vignette follows the camera, so it's skipped.
        static Renderer LargestBackgroundRenderer()
        {
            var roots = new List<GameObject>();
            foreach (string n in new[] { BackgroundModeManager.SynthwaveObjectName, LateNightDeskBackground.RootObjectName,
                                         NightCityBokehBackground.RootObjectName, BackgroundModeManager.VioletDoodlesObjectName })
            {
                GameObject go = GameObject.Find(n);
                if (go != null && go.activeInHierarchy) roots.Add(go);
            }
            Renderer best = null;
            float bestArea = 0f;
            foreach (GameObject root in roots)
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (r.GetComponent<BackgroundVignette>() != null) continue;
                    float area = r.bounds.size.x * r.bounds.size.y;
                    if (area > bestArea) { bestArea = area; best = r; }
                }
            return best;
        }
    }
}
