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
    /// <see cref="BackgroundVideoLoop"/> isn't attached, so the PlayerPrefs override
    /// keys it reads have nowhere to fire. This class fills that gap: on every
    /// scene load it finds RenderTexture-mode <see cref="VideoPlayer"/>s in the
    /// active scene and, if either the main-menu override or the
    /// visuals-preset path is set, swaps their source to that file URL.
    ///
    /// Resolution matches BackgroundVideoLoop (so the precedence stays
    /// consistent for users who have both wired up):
    ///   1. <see cref="BackgroundVideoLoop.OverridePathPrefKey"/> — main menu
    ///   2. <see cref="BackgroundVideoLoop.PresetPathPrefKey"/> — visuals preset
    ///   3. Leave the VideoPlayer alone (whatever the inspector configured).
    ///
    /// VideoPlayers with renderMode = CameraNearPlane/CameraFarPlane are skipped
    /// (those are typically compositing/transparency setups, not the background).
    /// </summary>
    public static class BackgroundVideoOverride
    {
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
            string overridePath = PlayerPrefs.GetString(BackgroundVideoLoop.OverridePathPrefKey, "");
            string presetPath   = PlayerPrefs.GetString(BackgroundVideoLoop.PresetPathPrefKey,   "");

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
                vp.Prepare();
                hijacked++;
            }

            Debug.Log($"[BgVideoDiag] BackgroundVideoOverride applied to {hijacked} VideoPlayer(s) in scene " +
                      $"'{SceneManager.GetActiveScene().name}'.");
        }

        static void OnVideoPrepared(VideoPlayer source)
        {
            source.prepareCompleted -= OnVideoPrepared;
            source.Play();
            Debug.Log($"[BgVideoDiag] Hijacked VideoPlayer on '{source.gameObject.name}' is now playing url='{source.url}'.");
        }

        static void OnVideoError(VideoPlayer source, string message)
        {
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
