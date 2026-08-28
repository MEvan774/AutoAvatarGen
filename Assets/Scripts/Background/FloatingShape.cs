using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gentle bob + wobble-rotate for a decorative floating shape.
///
/// Renderer-agnostic: attaches to whichever visual is on the GameObject —
///   • Image          — UI shapes under a canvas (the original menu use,
///                      behavior unchanged),
///   • SpriteRenderer — world-space sprites, NO canvas needed (drag any
///                      sprite like Art/Circle.png straight onto it),
///   • MeshRenderer   — a Quad with a material (opacity applied through the
///                      shader's _Color or _TintColor when it has one; use a
///                      transparent-capable shader like Sprites/Default).
///
/// The motion is pure Transform math, so it behaves the same everywhere —
/// but mind the units: on a Screen Space canvas bobAmplitude is in PIXELS,
/// in world space it's WORLD UNITS (the recording frame is ~17.8x10 units,
/// so 0.1–0.3 reads well). Prefer Quads over Plane primitives for meshes:
/// the spin is around the local z axis, which only looks right on a
/// camera-facing surface.
/// </summary>
public class FloatingShape : MonoBehaviour
{
    [Header("Motion")]
    [Tooltip("How fast the shape wanders around its start point (Perlin-driven — " +
             "smooth, never repeating). 0 = no drift, just bob.")]
    public float driftSpeed = 0.04f;
    [Tooltip("Max wander distance from the start point, in the transform's units " +
             "(world units in a scene, pixels on a canvas — so menu shapes with " +
             "the default radius are effectively unaffected).")]
    public float driftRadius = 0.6f;
    [Tooltip("Peak rocking speed in degrees/second. Together with swayAngle this " +
             "sets the rocking period (roughly 2*pi*swayAngle/rotateSpeed seconds).")]
    public float rotateSpeed = 4f;
    [Tooltip("How far the shape rocks to each side of its authored rotation, in degrees.")]
    public float swayAngle = 10f;
    public float bobAmplitude = 0.08f;
    public float bobFrequency = 0.4f;

    [Header("Fade")]
    public float opacity = 0.18f;

    private Vector3 startPos;
    private Quaternion startRot;
    private float timeOffset;
    private Image uiImage;
    private SpriteRenderer spriteRenderer;
    private Material meshMaterial;   // instance created for the mesh path only

    void Start()
    {
        startPos = transform.position;
        startRot = transform.localRotation;
        timeOffset = Random.Range(0f, 100f);
        ApplyOpacityToWhateverRendersThis();
    }

    void ApplyOpacityToWhateverRendersThis()
    {
        uiImage = GetComponent<Image>();
        if (uiImage != null)
        {
            Color c = uiImage.color;
            c.a = opacity;
            uiImage.color = c;
            return;
        }

        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            Color c = spriteRenderer.color;
            c.a = opacity;
            spriteRenderer.color = c;
            return;
        }

        var meshRenderer = GetComponent<Renderer>();
        if (meshRenderer != null && meshRenderer.sharedMaterial != null)
        {
            // Instance the material so fading one shape doesn't fade every
            // object sharing it.
            meshMaterial = meshRenderer.material;
            string prop = meshMaterial.HasProperty("_Color")     ? "_Color"
                        : meshMaterial.HasProperty("_TintColor") ? "_TintColor"
                        : null;
            if (prop != null)
            {
                Color c = meshMaterial.GetColor(prop);
                c.a = opacity;
                meshMaterial.SetColor(prop, c);
            }
            else
            {
                Debug.LogWarning($"[FloatingShape] '{name}' material has no _Color/_TintColor — " +
                                 "opacity not applied (the motion still runs). Use a shader with " +
                                 "a color property, e.g. Sprites/Default.");
            }
            return;
        }

        Debug.LogWarning($"[FloatingShape] '{name}' has no Image, SpriteRenderer or MeshRenderer — " +
                         "animating the transform only.");
    }

    void Update()
    {
        float t = Time.time + timeOffset;

        // Slow Perlin wander around the start point — smooth arcs that never
        // visibly repeat. timeOffset doubles as the per-shape noise seed so
        // no two shapes trace the same path.
        Vector3 drift = Vector3.zero;
        if (driftSpeed > 0f && driftRadius > 0f)
        {
            float nt = t * driftSpeed;
            drift = new Vector3(
                (Mathf.PerlinNoise(timeOffset + nt, 0.37f) - 0.5f) * 2f * driftRadius,
                (Mathf.PerlinNoise(0.71f, timeOffset * 1.618f + nt) - 0.5f) * 2f * driftRadius,
                0);
        }

        transform.position = startPos + drift + new Vector3(
            Mathf.Sin(t * bobFrequency * 0.7f) * bobAmplitude,
            Mathf.Sin(t * bobFrequency) * bobAmplitude,
            0);

        // Smooth rocking around the authored rotation — two golden-ratio-paced
        // sines so the sway never visibly repeats or syncs between shapes.
        // (The old version added a random delta every frame: a jittery random
        // walk with barely any net rotation.)
        if (rotateSpeed > 0f && swayAngle > 0f)
        {
            float omega = rotateSpeed / Mathf.Max(swayAngle, 0.01f); // peak deg/s -> rad/s
            float sway = swayAngle * (0.72f * Mathf.Sin(t * omega)
                                    + 0.28f * Mathf.Sin(t * omega * 0.618f + timeOffset));
            transform.localRotation = startRot * Quaternion.Euler(0f, 0f, sway);
        }
    }

    void OnDestroy()
    {
        if (meshMaterial != null) Destroy(meshMaterial);
    }
}
