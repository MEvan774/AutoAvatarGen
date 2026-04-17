using System;
using UnityEngine;

namespace MugsTech.Style
{
    /// <summary>
    /// Singleton runtime accessor for the currently active <see cref="ChannelStylePreset"/>.
    /// All card spawners, typography setters, and entry-animation drivers should read
    /// styling values from <see cref="Instance"/>?<see cref="ActivePreset"/> instead of
    /// hardcoding. If no preset is active the existing hardcoded defaults remain in use.
    ///
    /// Setup: drop this component on any GameObject in your scene (typically the same
    /// one as MediaPresentationSystem). Optionally drag a default preset into the
    /// Inspector — that becomes the baked-in preset for builds.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class StyleManager : MonoBehaviour
    {
        // -------------------------------------------------------------------
        // Inspector
        // -------------------------------------------------------------------

        [Tooltip("Preset assigned at build time (and used as the fallback in the Editor " +
                 "if no override has been set via the editor window).")]
        [SerializeField] private ChannelStylePreset defaultPreset;

        [Tooltip("If true, hot-swapping a preset at runtime triggers OnPresetChanged " +
                 "so subscribed components can re-render. Disable for slight perf gain " +
                 "if you never hot-swap.")]
        [SerializeField] private bool fireChangeEvents = true;

        // -------------------------------------------------------------------
        // Singleton
        // -------------------------------------------------------------------

        private static StyleManager s_Instance;

        /// <summary>
        /// Active StyleManager in the scene. Returns null if none exists, in which
        /// case all consumers should fall back to their hardcoded defaults.
        /// </summary>
        public static StyleManager Instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = FindObjectOfType<StyleManager>();
                return s_Instance;
            }
        }

        // -------------------------------------------------------------------
        // Active preset
        // -------------------------------------------------------------------

        private ChannelStylePreset activePreset;

        /// <summary>
        /// Currently active preset, or null if none is active.
        /// Consumers MUST null-check before reading any field.
        /// </summary>
        public ChannelStylePreset ActivePreset => activePreset;

        /// <summary>
        /// Fires whenever <see cref="ActivePreset"/> changes (including when set to null).
        /// Subscribers should re-apply styling. The argument is the new preset (may be null).
        /// </summary>
        public event Action<ChannelStylePreset> OnPresetChanged;

        // -------------------------------------------------------------------
        // Editor-pref override
        // -------------------------------------------------------------------

#if UNITY_EDITOR
        // The editor window writes the GUID of the user-activated preset to this key.
        // The instance reads it on Awake (Editor only) and applies it over `defaultPreset`.
        public const string EditorPrefKey = "MugsTech.Style.ActivePresetGuid";
#endif

        // -------------------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------------------

        void Awake()
        {
            s_Instance = this;

            ChannelStylePreset toLoad = defaultPreset;

#if UNITY_EDITOR
            // In the Editor, prefer the user's activated preset over the baked default.
            string guid = UnityEditor.EditorPrefs.GetString(EditorPrefKey, "");
            if (!string.IsNullOrEmpty(guid))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<ChannelStylePreset>(path);
                    if (loaded != null) toLoad = loaded;
                }
            }
#endif

            if (toLoad != null)
                LoadPreset(toLoad);
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }

        // -------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------

        /// <summary>
        /// Swap the active preset. All subscribers of <see cref="OnPresetChanged"/>
        /// are notified so they can re-render with the new values.
        /// </summary>
        public void LoadPreset(ChannelStylePreset preset)
        {
            activePreset = preset;
            if (fireChangeEvents)
                OnPresetChanged?.Invoke(preset);
            Debug.Log(preset != null
                ? $"[StyleManager] Active preset: {preset.channelName} ({preset.identifier})"
                : "[StyleManager] Active preset cleared.");
        }

        /// <summary>
        /// Clear the active preset. Cards revert to their hardcoded defaults.
        /// </summary>
        public void RestoreDefault()
        {
            LoadPreset(null);
        }

        /// <summary>
        /// Convenience: returns true if a preset is active and has the given register.
        /// Useful for quick conditional logic without null-checking everywhere.
        /// </summary>
        public bool IsRegister(StyleRegister register)
        {
            return activePreset != null && activePreset.register == register;
        }
    }
}
