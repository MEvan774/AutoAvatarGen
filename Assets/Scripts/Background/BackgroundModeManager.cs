using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace MugsTech.Background
{
    /// <summary>
    /// Central toggle for what shows behind the avatar during recording.
    /// Drives three mutually-exclusive modes:
    ///
    ///   • Video       — the normal scene: BackgroundPanel mp4 + ambient shader
    ///                   + scrolling shapes + mood transitions all run.
    ///   • GreenScreen — flat #00FF00 behind the character so it can be
    ///                   chroma-keyed in post. Disables every background
    ///                   GameObject / system to reclaim GPU for the recorder.
    ///   • Transparent — same as GreenScreen but the camera clears to alpha=0
    ///                   and <see cref="CrossPlatformRecorder"/> captures the
    ///                   alpha channel (MOV output). Drop the resulting clip
    ///                   directly into any video editor over your own bg.
    ///
    /// Lifecycle:
    ///   The mode lives in PlayerPrefs (set from the main menu) and is
    ///   applied automatically every scene load via a RuntimeInitializeOn-
    ///   LoadMethod. <see cref="CrossPlatformRecorder"/> reads the same key
    ///   in its Awake so the Evereal `transparent` flag and camera clear
    ///   stay in sync with the user's choice.
    /// </summary>
    public static class BackgroundModeManager
    {
        public enum Mode
        {
            Video       = 0,
            GreenScreen = 1,
            Transparent = 2,
        }

        public const string ModePrefKey = "AutoAvatarGen.BackgroundRecordingMode";

        /// <summary>
        /// Name of the scene GameObject that hosts the solid-green chroma-key
        /// backdrop. Activated in GreenScreen mode, deactivated in Video and
        /// Transparent modes. If no GameObject with this name exists, the
        /// toggle is a silent no-op.
        /// </summary>
        public const string GreenScreenObjectName = "GreenScreenBackground";

        public static Mode LoadMode()
            => (Mode)PlayerPrefs.GetInt(ModePrefKey, (int)Mode.Video);

        public static void SaveMode(Mode mode)
        {
            PlayerPrefs.SetInt(ModePrefKey, (int)mode);
            PlayerPrefs.Save();
        }

        public static string Label(Mode m)
        {
            switch (m)
            {
                case Mode.Video:       return "Video";
                case Mode.GreenScreen: return "Green Screen";
                case Mode.Transparent: return "Transparent";
                default:               return m.ToString();
            }
        }

        public static Mode Cycle(Mode current, int direction = +1)
        {
            int next = ((int)current + direction + 3) % 3;
            return (Mode)next;
        }

        // ----------------------------------------------------------------
        // Scene application — disables every "background" system the user
        // doesn't need when chroma-keying, freeing the GPU for encode.
        // ----------------------------------------------------------------

        // RuntimeInitializeOnLoadMethod fires exactly once per process lifetime
        // (right after the first scene loads — usually the main menu, where
        // there's nothing to disable). For the recording scene that loads
        // afterwards we need a separate hook: subscribe to SceneManager.
        // sceneLoaded here so every subsequent scene-load also runs through
        // ApplyToActiveScene. Same pattern VisualsRuntimeApplier uses, kept
        // in sync so the two static bootstraps don't fight each other.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;   // idempotent — guards against domain-reload duplication
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyToActiveScene();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyToActiveScene();

        public static void ApplyToActiveScene()
        {
            Mode mode = LoadMode();

            // The legacy full-screen green Image is no longer used: green-screen
            // mode now gets its green from the recorded camera's clear colour
            // (set by CrossPlatformRecorder), which sits strictly BEHIND the
            // content cards instead of occluding them — the same way transparent
            // mode works. We force the Image OFF in every mode so any scene that
            // still has it enabled can't cover the cards.
            bool gsToggled = ToggleGreenScreenBackground(false);

            if (mode == Mode.Video)
            {
                if (gsToggled)
                    Debug.Log($"[BackgroundModeManager] Mode=Video: deactivated '{GreenScreenObjectName}'.");
                return;
            }

            int panels    = DisableBackgroundPanels();
            int ambient   = DisableBackgroundAmbientRenderers();
            int shapes    = DisableScrollingShapes();
            int moods     = DisableMoodControllers();
            int floating  = DisableComponentsOfType<FloatingShape>();
            int blooms    = DisableComponentsOfType<UIBloom>();

            Debug.Log($"[BackgroundModeManager] Mode={mode} on scene '{SceneManager.GetActiveScene().name}': " +
                      $"disabled {panels} background panel(s), " +
                      $"{ambient} ambient-shader renderer(s), " +
                      $"{shapes} scrolling-shape controller(s), " +
                      $"{moods} mood controller(s), " +
                      $"{floating} floating-shape(s), " +
                      $"{blooms} UI bloom(s), " +
                      $"greenscreen-bg={(mode == Mode.GreenScreen ? "ON" : "OFF")}.");

            // Second pass on the next frame — VisualsRuntimeApplier and
            // BackgroundVideoOverride also subscribe to sceneLoaded; if one of
            // them runs AFTER us, it may re-prepare a VideoPlayer we just
            // stopped. The deferred re-disable catches that. Hosted on a
            // hidden DontDestroyOnLoad runner so the coroutine survives even
            // if no scene MonoBehaviour holds it.
            CoroutineRunner.Run(DeferredRedisable());
        }

        // Re-runs the disable pass once after a few frames of settling time,
        // long enough for other sceneLoaded subscribers (in undefined order)
        // to have finished their own work. Cheap — only fires when mode != Video.
        static IEnumerator DeferredRedisable()
        {
            // Wait a few frames so any post-sceneLoaded Prepare() calls from
            // other systems have had a chance to fire OnVideoPrepared (which
            // re-Plays on hijacked players).
            for (int i = 0; i < 5; i++) yield return null;

            Mode mode = LoadMode();

            // Keep the legacy green Image OFF (green is the camera clear now).
            ToggleGreenScreenBackground(false);

            if (mode == Mode.Video) yield break;
            int panels = DisableBackgroundPanels();
            if (panels > 0)
            {
                Debug.Log($"[BackgroundModeManager] Deferred re-disable caught {panels} re-enabled panel(s).");
            }
        }

        // Walks the active scene's root hierarchy (including inactive
        // descendants) and SetActives the GreenScreenBackground GameObject.
        // GameObject.Find skips inactive objects so we can't use it here —
        // the user's GreenScreenBackground is presumably inactive by default
        // and only flips on for green-screen mode.
        // Returns true if a matching GameObject was found and its active
        // state changed (or it was already in the desired state).
        static bool ToggleGreenScreenBackground(bool shouldBeActive)
        {
            GameObject target = FindInActiveScene(GreenScreenObjectName);
            if (target == null) return false;
            if (target.activeSelf != shouldBeActive)
                target.SetActive(shouldBeActive);
            return true;
        }

        static GameObject FindInActiveScene(string name)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = FindRecursive(root.transform, name);
                if (found != null) return found.gameObject;
            }
            return null;
        }

        static Transform FindRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            for (int i = 0; i < parent.childCount; i++)
            {
                var found = FindRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        // Tiny hidden MonoBehaviour to host coroutines from a static context.
        // Lazy-created; persists across scenes via DontDestroyOnLoad so the
        // deferred pass continues even if the active scene unloads mid-wait.
        class CoroutineRunner : MonoBehaviour
        {
            static CoroutineRunner _instance;
            public static void Run(IEnumerator routine)
            {
                if (_instance == null)
                {
                    var go = new GameObject("[BackgroundModeManager.Runner]");
                    Object.DontDestroyOnLoad(go);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    _instance = go.AddComponent<CoroutineRunner>();
                }
                _instance.StartCoroutine(routine);
            }
        }

        // Any GameObject hosting a RenderTexture-mode VideoPlayer counts as a
        // background panel — same filter BackgroundVideoOverride and
        // SeamlessVideoLoop use, so we're consistent across the system.
        static int DisableBackgroundPanels()
        {
            int n = 0;
            var players = Object.FindObjectsOfType<VideoPlayer>(includeInactive: false);
            foreach (var vp in players)
            {
                if (vp == null) continue;
                if (vp.renderMode != VideoRenderMode.RenderTexture) continue;
                // Stop the player explicitly before disabling, otherwise some
                // Unity versions keep decoding in the background even when the
                // GameObject is inactive.
                vp.Stop();
                vp.gameObject.SetActive(false);
                n++;
            }
            return n;
        }

        // Renderer.enabled = false skips the draw call entirely; cheaper than
        // toggling materials or alpha. Disabling at the Renderer level (not
        // the GameObject) preserves any sibling components that need to keep
        // running (lighting probes, etc.).
        static int DisableBackgroundAmbientRenderers()
        {
            int n = 0;
            var renderers = Object.FindObjectsOfType<Renderer>(includeInactive: false);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                Material mat = r.sharedMaterial;
                if (mat == null || mat.shader == null) continue;
                if (mat.shader.name != "Custom/BackgroundAmbient") continue;
                r.enabled = false;
                n++;
            }
            return n;
        }

        // Particle systems are the real GPU cost on this layer — disabling
        // the controller component pauses Update (we made it change-detected
        // earlier), and stopping + clearing the ParticleSystem frees the
        // simulation buffers.
        static int DisableScrollingShapes()
        {
            int n = 0;
            var shapes = Object.FindObjectsOfType<ScrollingShapeController>();
            foreach (var s in shapes)
            {
                if (s == null) continue;
                var ps = s.GetComponent<ParticleSystem>();
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                s.enabled = false;
                n++;
            }
            return n;
        }

        // Mood controller is cheap (no per-frame Update outside transitions)
        // but disabling it stops the coroutine if a transition is in flight,
        // which would otherwise still try to mutate a disabled material.
        static int DisableMoodControllers()
        {
            int n = 0;
            var moods = Object.FindObjectsOfType<BackgroundMoodController>();
            foreach (var m in moods)
            {
                if (m == null) continue;
                m.enabled = false;
                n++;
            }
            return n;
        }

        // Generic catch-all for the smaller per-frame visual components that
        // live in the global namespace (FloatingShape, UIBloom). Disabling
        // .enabled is enough — these scripts gate their Update on the flag,
        // so the per-frame transform mutations and Vector2 allocations stop.
        static int DisableComponentsOfType<T>() where T : MonoBehaviour
        {
            int n = 0;
            var components = Object.FindObjectsOfType<T>(includeInactive: false);
            foreach (var c in components)
            {
                if (c == null) continue;
                c.enabled = false;
                n++;
            }
            return n;
        }
    }
}
