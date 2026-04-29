using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace MugsTech.Background
{
    /// <summary>
    /// Runtime hijacker that lets the main-menu and visuals-menu background-mp4
    /// fields actually take effect.
    ///
    /// The recording scene's background video plays via a raw <see cref="VideoPlayer"/>
    /// component (configured directly in the Inspector with a VideoClip + RenderTexture).
    /// On every scene load this class finds RenderTexture-mode VideoPlayers in
    /// the active scene and, if either the main-menu override or the
    /// visuals-preset path is set, swaps their source to that file URL.
    ///
    /// Resolution precedence:
    ///   1. <see cref="OverridePathPrefKey"/> — set by main menu
    ///   2. <see cref="PresetPathPrefKey"/>   — written by VisualsRuntimeApplier from active save
    ///   3. Leave the VideoPlayer alone (whatever the inspector configured)
    ///
    /// VideoPlayers with renderMode = CameraNearPlane/CameraFarPlane are skipped
    /// (those are typically compositing/transparency setups, not the background).
    /// </summary>
    public static class BackgroundVideoOverride
    {
        /// <summary>Set by the main menu's "Background Video Override" field.</summary>
        public const string OverridePathPrefKey = "AutoAvatarGen.BackgroundVideoOverride";

        /// <summary>Written by VisualsRuntimeApplier from the active VisualsSave.</summary>
        public const string PresetPathPrefKey   = "AutoAvatarGen.BackgroundVideoPreset";

        // Hijacked players currently in their Prepare() phase. MediaPresentation
        // waits on this to avoid kicking off recording before the swapped-in
        // mp4 has rendered its first frame.
        static int s_pendingPrepares;

        /// <summary>
        /// True when every hijacked VideoPlayer has finished preparing. False
        /// only between starting a Prepare() in <see cref="ApplyToActiveScene"/>
        /// and receiving the corresponding prepareCompleted callback.
        /// </summary>
        public static bool AllPrepared => s_pendingPrepares == 0;


        // VisualsRuntimeApplier explicitly invokes ApplyToActiveScene() at the
        // end of its sceneLoaded work, after writing PresetPathPrefKey — that's
        // the only hookup needed during runtime. The bootstrap below covers
        // the very first scene load (where VisualsRuntimeApplier runs a single
        // time via its own AfterSceneLoad bootstrap and chains here).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // No-op — VisualsRuntimeApplier.Bootstrap already calls
            // ApplyToActiveScene() through the chain. Kept as a marker /
            // anchor in case future code wants a dedicated entry point.
        }

        public static void ApplyToActiveScene()
        {
            string overridePath = PlayerPrefs.GetString(OverridePathPrefKey, "");
            string presetPath   = PlayerPrefs.GetString(PresetPathPrefKey,   "");

            string path, sourceLabel;
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                path        = overridePath.Trim();
                sourceLabel = "main menu override";
            }
            else if (!string.IsNullOrWhiteSpace(presetPath))
            {
                path        = presetPath.Trim();
                sourceLabel = "visuals preset";
            }
            else
            {
                Debug.Log("[BgVideoDiag] BackgroundVideoOverride: neither override nor preset path is set; " +
                          "leaving scene VideoPlayers untouched.");
                return;
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[BgVideoDiag] BackgroundVideoOverride: {sourceLabel} path not found on disk: '{path}'. " +
                                 "Leaving scene VideoPlayers untouched.");
                return;
            }

            string url = ToFileUrl(path);

            int hijacked = 0;
            // Includes inactive=false: an inactive BackgroundPanel (the disabled
            // sibling) shouldn't get repointed; only the live one should.
            VideoPlayer[] players = UnityEngine.Object.FindObjectsOfType<VideoPlayer>(includeInactive: false);
            foreach (VideoPlayer vp in players)
            {
                if (vp == null) continue;
                // Skip camera-mode players — those are compositing/overlay
                // setups, not the background panel.
                if (vp.renderMode != VideoRenderMode.RenderTexture) continue;

                Debug.Log($"[BgVideoDiag] Hijacking VideoPlayer on '{vp.gameObject.name}' " +
                          $"(was source={vp.source} clip={(vp.clip != null ? vp.clip.name : "<null>")} url='{vp.url}') " +
                          $"→ {sourceLabel} url='{url}'");

                vp.Stop();
                vp.source    = VideoSource.Url;
                vp.url       = url;
                vp.isLooping = true;
                vp.errorReceived    += OnVideoError;
                vp.prepareCompleted += OnVideoPrepared;
                s_pendingPrepares++;
                vp.Prepare();
                hijacked++;
            }

            Debug.Log($"[BgVideoDiag] BackgroundVideoOverride applied to {hijacked} VideoPlayer(s) in scene " +
                      $"'{SceneManager.GetActiveScene().name}'.");
        }

        static void OnVideoPrepared(VideoPlayer source)
        {
            source.prepareCompleted -= OnVideoPrepared;
            if (s_pendingPrepares > 0) s_pendingPrepares--;
            source.Play();
            Debug.Log($"[BgVideoDiag] Hijacked VideoPlayer on '{source.gameObject.name}' is now playing url='{source.url}'.");
        }

        static void OnVideoError(VideoPlayer source, string message)
        {
            // Decrement the pending counter so MediaPresentation doesn't wait forever on a failed prepare.
            if (s_pendingPrepares > 0) s_pendingPrepares--;
            Debug.LogError($"[BgVideoDiag] Hijacked VideoPlayer on '{source.gameObject.name}' error: {message} " +
                           $"(url='{source.url}')");
        }

        static string ToFileUrl(string absolutePath)
        {
            try { return new Uri(absolutePath).AbsoluteUri; }
            catch (Exception e)
            {
                Debug.LogWarning($"[BgVideoDiag] Uri construction failed for '{absolutePath}': {e.Message}; " +
                                 "falling back to 'file://' + path.");
                return "file://" + absolutePath;
            }
        }
    }
}
