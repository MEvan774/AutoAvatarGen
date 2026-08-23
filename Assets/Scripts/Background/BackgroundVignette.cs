using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// Screen-space vignette over the synthwave backdrop — the first consumer
    /// of the (previously orphaned) Custom/PostProcessOverlay shader, driven
    /// vignette-only: bloom and grain stay zeroed here, ready to be dialed in
    /// later from the same material.
    ///
    /// A fullscreen quad glues itself to the active camera's FRAME every
    /// LateUpdate — so camera zooms darken the same screen edges instead of
    /// zooming the vignette rings — at a depth just behind the presenter.
    /// Everything in this scene sorts in the transparent queue at order 0, so
    /// DEPTH is what layers it: over the scrolling background layers (~8.5+
    /// units behind the presenter), under the presenter and his drop shadow
    /// (whose own camera-forward nudge is 0.05 — see PresenterShadow). The
    /// shader's own queue tag is Overlay+100 (it was authored as a
    /// whole-frame overlay), so the material is pulled back to the plain
    /// transparent queue here; cards and media, on their high-order canvases,
    /// stay above it either way.
    ///
    /// Lives ON the SynthwaveBackground object (auto-added by
    /// BackgroundModeManager when Normal mode activates the backdrop), so
    /// GreenScreen/Transparent modes strip it together with the backdrop — a
    /// vignette on a chroma/alpha plate would contaminate the key.
    /// </summary>
    public class BackgroundVignette : MonoBehaviour
    {
        [Range(0f, 1f)]
        [Tooltip("How far the darkening reaches toward the screen center.")]
        public float intensity = 0.3f;

        [Range(0.01f, 1f)]
        [Tooltip("Width of the fade band — higher = softer, more gradual edge.")]
        public float smoothness = 0.6f;

        [Range(0f, 1f)]
        [Tooltip("0 = circular vignette, 1 = follows the frame's rectangle.")]
        public float roundness = 0.8f;

        [Tooltip("Vignette color; alpha is the maximum darkening at the frame corners.")]
        public Color color = new Color(0f, 0f, 0f, 0.45f);

        [Tooltip("World units the quad sits BEHIND the presenter. Keep well " +
                 "under the backdrop's ~8.5 unit distance so the vignette " +
                 "stays in front of the scrolling layers.")]
        public float depthBehindPresenter = 1.5f;

        [Tooltip("Extra scale so the quad over-covers the frame edges.")]
        public float sizeBleed = 1.05f;

        private MeshRenderer quadRenderer;
        private Material material;
        private Camera viewCamera;
        private SpriteRenderer presenter;

        void Start() => EnsureBuilt();

        /// <summary>Builds the quad + material once. Public so editor tooling
        /// can construct the overlay outside play mode.</summary>
        public void EnsureBuilt()
        {
            if (quadRenderer != null) return;

            // Resources.Load rather than a bare Shader.Find — nothing else
            // references this shader, so only its Resources placement gets it
            // into a build (same story as MugsTech/SpriteShadowBlur).
            Shader shader = Resources.Load<Shader>("Shaders/PostProcessOverlay");
            if (shader == null) shader = Shader.Find("Custom/PostProcessOverlay");
            if (shader == null)
            {
                Debug.LogWarning("[BackgroundVignette] Custom/PostProcessOverlay shader not found — no vignette.");
                enabled = false;
                return;
            }

            material = new Material(shader);
            material.renderQueue = 3000;   // Transparent — sort by depth with the scene, not over the UI
            ApplyMaterialSettings();

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "BackgroundVignetteQuad";
            quad.transform.SetParent(transform, false);

            Collider col = quad.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = material;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
        }

        void LateUpdate() => UpdatePlacement(ActiveCamera());

        /// <summary>Glues the quad to the given camera's frame at the resting
        /// depth. Public so editor tooling can place it for a static render.</summary>
        public void UpdatePlacement(Camera cam)
        {
            if (cam == null || quadRenderer == null) return;

            // Cheap enough to re-assert every frame; makes the Inspector
            // sliders live-tweakable during a take.
            ApplyMaterialSettings();

            float depth = PresenterDepth(cam) + depthBehindPresenter;

            Transform t = quadRenderer.transform;
            t.position = cam.transform.position + cam.transform.forward * depth;
            t.rotation = cam.transform.rotation;

            // Frustum size at the quad's depth. Reading orthographicSize every
            // frame keeps the vignette frame-glued through {Zoom:...} moves.
            float h = cam.orthographic
                ? cam.orthographicSize * 2f
                : 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * cam.aspect;

            // Counter any scale on the SynthwaveBackground parent so the quad
            // spans exactly the computed frame regardless of the rig above it.
            Vector3 ls = transform.lossyScale;
            t.localScale = new Vector3(
                w * sizeBleed / (Mathf.Abs(ls.x) > 1e-5f ? ls.x : 1f),
                h * sizeBleed / (Mathf.Abs(ls.y) > 1e-5f ? ls.y : 1f),
                1f);
        }

        void ApplyMaterialSettings()
        {
            if (material == null) return;
            material.SetColor("_VignetteColor",      color);
            material.SetFloat("_VignetteIntensity",  intensity);
            material.SetFloat("_VignetteSmoothness", smoothness);
            material.SetFloat("_VignetteRoundness",  roundness);
            // Vignette only — the shader also carries bloom + grain, kept off
            // until someone opts in (they'd make ready polish toggles).
            material.SetFloat("_BloomIntensity", 0f);
            material.SetFloat("_GrainIntensity", 0f);
        }

        // Distance from the camera to the presenter along the view axis — the
        // reference plane the quad rests behind. Falls back to a typical
        // camera→presenter distance for this scene when no avatar exists yet.
        float PresenterDepth(Camera cam)
        {
            if (presenter == null)
            {
                var avatar = FindObjectOfType<HybridAvatarSystem>();
                if (avatar != null) presenter = avatar.avatarRenderer;
            }
            if (presenter == null) return 5f;
            return Vector3.Dot(presenter.transform.position - cam.transform.position,
                               cam.transform.forward);
        }

        // The recording flow runs with Camera.main disabled, so ask the
        // enabled-camera list; cached once found (same pattern as
        // PresenterShadow).
        Camera ActiveCamera()
        {
            if (viewCamera == null || !viewCamera.isActiveAndEnabled)
            {
                viewCamera = Camera.main;
                if (viewCamera == null && Camera.allCamerasCount > 0)
                    viewCamera = Camera.allCameras[0];
            }
            return viewCamera;
        }

        void OnDestroy()
        {
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }
    }
}
