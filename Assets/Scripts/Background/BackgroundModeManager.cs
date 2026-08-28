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
    ///   • Normal      — the SynthwaveBackground prefab instance (scrolling
    ///                   neon grid + animated sky) shows behind the avatar.
    ///                   The retired BackgroundPanel mp4 backdrop (plus its
    ///                   ambient shader, scrolling shapes and mood
    ///                   transitions) is force-disabled so it can't play
    ///                   behind the synthwave quads.
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
    ///
    ///   Mode value 0 used to be "Video" (mp4 backdrop). It was replaced by
    ///   Normal at the same enum value on purpose: existing PlayerPrefs keep
    ///   working, and the mp4 backdrop path is retired everywhere.
    /// </summary>
    public static class BackgroundModeManager
    {
        public enum Mode
        {
            Normal      = 0,
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

        /// <summary>
        /// Name of the scene GameObject holding the synthwave background
        /// prefab instance (Assets/Prefabs/SynthwaveBackground.prefab).
        /// Activated in Normal mode, deactivated in GreenScreen and
        /// Transparent modes — its quads are opaque 3D geometry, so leaving
        /// it on would ruin the chroma plate / alpha capture. If no
        /// GameObject with this name exists, the toggle is a silent no-op.
        /// </summary>
        public const string SynthwaveObjectName = "SynthwaveBackground";

        public static Mode LoadMode()
            => (Mode)PlayerPrefs.GetInt(ModePrefKey, (int)Mode.Normal);

        public static void SaveMode(Mode mode)
        {
            PlayerPrefs.SetInt(ModePrefKey, (int)mode);
            PlayerPrefs.Save();
        }

        public static string Label(Mode m)
        {
            switch (m)
            {
                case Mode.Normal:      return "Normal";
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

            // The GreenScreenBackground plane is the chroma-key backdrop (the
            // camera clear colour doesn't survive this scene's post-camera, so a
            // real Image is what actually shows green). It runs in ALL modes so
            // Green → Normal still hides it, and ToggleGreenScreenBackground forces
            // it BEHIND every foreground layer so it no longer occludes cards.
            bool gsToggled = ToggleGreenScreenBackground(mode == Mode.GreenScreen);

            // Same deal for the two Normal-mode backdrops, mirrored: only the
            // SELECTED style shows in Normal, and everything is OFF in
            // GreenScreen/Transparent where opaque quads would pollute the
            // chroma plate / alpha channel. Which style is selected lives in
            // BackgroundStyleManager (default Synthwave — original behavior).
            var style = BackgroundStyleManager.LoadStyle();
            bool swPresent = ToggleSynthwaveBackground(
                mode == Mode.Normal && style == BackgroundStyleManager.Style.Synthwave);

            // The LateNightDesk / NightCityBokeh rigs ship in no scene —
            // they're created on demand, but only in backdrop-swapping scenes
            // (those hosting the synthwave object), so the main menu keeps
            // its own decorations.
            ToggleLateNightDeskBackground(
                mode == Mode.Normal && style == BackgroundStyleManager.Style.LateNightDesk,
                allowCreate: swPresent);
            ToggleNightCityBokehBackground(
                mode == Mode.Normal && style == BackgroundStyleManager.Style.NightCityBokeh,
                allowCreate: swPresent);
            ToggleVioletDoodlesBackground(
                mode == Mode.Normal && style == BackgroundStyleManager.Style.VioletDoodles);

            // Scenes without a SynthwaveBackground object (the main menu) keep
            // their own decorations in Normal mode — only the recording scene
            // swaps backdrops. Chroma/alpha modes still strip every scene.
            if (mode == Mode.Normal && !swPresent)
            {
                if (gsToggled)
                    Debug.Log($"[BackgroundModeManager] Mode=Normal: deactivated '{GreenScreenObjectName}'.");
                return;
            }

            // The retired mp4 backdrop and its satellites are disabled in every
            // remaining case: Normal replaces it with the synthwave prefab,
            // GreenScreen/Transparent need a clean plate.
            int panels    = DisableBackgroundPanels();
            int ambient   = DisableBackgroundAmbientRenderers();
            int shapes    = DisableScrollingShapes();
            int moods     = DisableMoodControllers();

            // Content-side sparkle (floating shapes, UI bloom) stays on in
            // Normal mode — it belongs to the foreground look. Chroma/alpha
            // modes disable it to reclaim GPU for the encoder, but ONLY in
            // the recording scene (the one hosting the synthwave backdrop):
            // the encoder isn't running anywhere else, and stripping every
            // scene silently froze decorative shapes in the menu and in
            // background-sandbox scenes whenever a chroma mode was saved.
            int floating = 0, blooms = 0;
            if (mode != Mode.Normal && swPresent)
            {
                floating = DisableComponentsOfType<FloatingShape>();
                blooms   = DisableComponentsOfType<UIBloom>();
            }

            Debug.Log($"[BackgroundModeManager] Mode={mode} on scene '{SceneManager.GetActiveScene().name}': " +
                      $"disabled {panels} background panel(s), " +
                      $"{ambient} ambient-shader renderer(s), " +
                      $"{shapes} scrolling-shape controller(s), " +
                      $"{moods} mood controller(s), " +
                      $"{floating} floating-shape(s), " +
                      $"{blooms} UI bloom(s), " +
                      $"greenscreen-bg={(mode == Mode.GreenScreen ? "ON" : "OFF")}, " +
                      $"style={style}.");

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
        // to have finished their own work.
        static IEnumerator DeferredRedisable()
        {
            // Wait a few frames so any post-sceneLoaded Prepare() calls from
            // other systems have had a chance to fire OnVideoPrepared (which
            // re-Plays on hijacked players).
            for (int i = 0; i < 5; i++) yield return null;

            Mode mode = LoadMode();

            // Re-assert every backdrop state in case anything toggled them
            // between our first pass and now.
            ToggleGreenScreenBackground(mode == Mode.GreenScreen);
            var style = BackgroundStyleManager.LoadStyle();
            bool swPresent = ToggleSynthwaveBackground(
                mode == Mode.Normal && style == BackgroundStyleManager.Style.Synthwave);
            ToggleLateNightDeskBackground(
                mode == Mode.Normal && style == BackgroundStyleManager.Style.LateNightDesk,
                allowCreate: swPresent);
            ToggleNightCityBokehBackground(
                mode == Mode.Normal && style == BackgroundStyleManager.Style.NightCityBokeh,
                allowCreate: swPresent);
            ToggleVioletDoodlesBackground(
                mode == Mode.Normal && style == BackgroundStyleManager.Style.VioletDoodles);

            // Mirror ApplyToActiveScene: in Normal mode only scenes hosting
            // the synthwave object retire the mp4 backdrop.
            if (mode == Mode.Normal && !swPresent) yield break;

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

            if (shouldBeActive)
                ForceBehindEverything(target);

            if (target.activeSelf != shouldBeActive)
                target.SetActive(shouldBeActive);
            return true;
        }

        // Pins the green plane to the very back so it acts as a chroma-key
        // backdrop instead of occluding the foreground. The content cards proved
        // sorting order is compared globally here — the fullscreen feature zone
        // at order 31000 renders OVER the green, while side cards at the default
        // 0 tie with it and lose. Giving the green its own override-sorting canvas
        // at the minimum order puts it behind every card, the media display, and
        // the character. Idempotent.
        static void ForceBehindEverything(GameObject green)
        {
            var canvas = green.GetComponent<Canvas>();
            if (canvas == null) canvas = green.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MinValue; // -32768: as far back as sorting goes
        }

        // Same find-including-inactive pattern as the green screen toggle, but
        // no sorting-canvas forcing: the synthwave quads are opaque 3D world
        // geometry far behind the content, depth handles the ordering.
        // Returns true if the GameObject exists in the active scene.
        static bool ToggleSynthwaveBackground(bool shouldBeActive)
        {
            GameObject target = FindInActiveScene(SynthwaveObjectName);
            if (target == null) return false;

            // The vignette overlay rides the synthwave object, so the chroma
            // and alpha modes strip it together with the backdrop — a vignette
            // over a green/transparent plate would contaminate the key.
            // Auto-added like the presenter's shadow; add the component to the
            // prefab in the Inspector to override its knobs.
            if (shouldBeActive && target.GetComponent<BackgroundVignette>() == null)
                target.AddComponent<BackgroundVignette>();

            if (target.activeSelf != shouldBeActive)
                target.SetActive(shouldBeActive);
            return true;
        }

        // LateNightDesk twin of ToggleSynthwaveBackground. The rig is not
        // authored in any scene: when it should show and the scene is a
        // backdrop-swapping one (allowCreate — i.e. a SynthwaveBackground
        // object exists there), an empty root is created and the component
        // builds the whole rig itself in Start. Deactivating it also kills
        // any in-flight mood-crossfade coroutine on it — that's what makes
        // style switches clean. Same vignette auto-add as the synthwave path
        // so both backdrops get the identical frame treatment.
        // Returns true if the rig exists (or was just created).
        static bool ToggleLateNightDeskBackground(bool shouldBeActive, bool allowCreate)
        {
            GameObject target = FindInActiveScene(LateNightDeskBackground.RootObjectName);
            if (target == null)
            {
                if (!shouldBeActive || !allowCreate) return false;
                target = new GameObject(LateNightDeskBackground.RootObjectName);
                target.AddComponent<LateNightDeskBackground>();
            }

            if (shouldBeActive && target.GetComponent<BackgroundVignette>() == null)
                target.AddComponent<BackgroundVignette>();

            if (target.activeSelf != shouldBeActive)
                target.SetActive(shouldBeActive);
            return true;
        }

        /// <summary>
        /// Name of the scene GameObject holding the Violet Doodles backdrop —
        /// an instance of the user-authored prefab
        /// Assets/Art/BackgroundEffects/NewBackGround.prefab, authored into
        /// SampleScene under this name (deliberately DIFFERENT from the
        /// prefab's own root name so sandbox scenes hosting the prefab under
        /// its default name are never toggled by recording modes).
        /// </summary>
        public const string VioletDoodlesObjectName = "VioletDoodlesBackground";

        // Violet Doodles twin — find-and-toggle only: the rig is a regular
        // prefab authored into the scene (no on-demand creation), and it
        // carries its own BackgroundVignette child, so no auto-add here.
        static bool ToggleVioletDoodlesBackground(bool shouldBeActive)
        {
            GameObject target = FindInActiveScene(VioletDoodlesObjectName);
            if (target == null) return false;

            if (target.activeSelf != shouldBeActive)
                target.SetActive(shouldBeActive);
            return true;
        }

        // NightCityBokeh twin of ToggleLateNightDeskBackground — identical
        // on-demand creation, vignette auto-add and clean-switch semantics.
        static bool ToggleNightCityBokehBackground(bool shouldBeActive, bool allowCreate)
        {
            GameObject target = FindInActiveScene(NightCityBokehBackground.RootObjectName);
            if (target == null)
            {
                if (!shouldBeActive || !allowCreate) return false;
                target = new GameObject(NightCityBokehBackground.RootObjectName);
                target.AddComponent<NightCityBokehBackground>();
            }

            if (shouldBeActive && target.GetComponent<BackgroundVignette>() == null)
                target.AddComponent<BackgroundVignette>();

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
