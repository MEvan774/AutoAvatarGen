using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// Slow parallax drift for the Violet Doodles backdrop: carries every
    /// <see cref="FloatingShape"/> under this object across the frame in one
    /// shared direction, and wraps each one around to the opposite edge once
    /// it has fully left the picture. Without it the doodles only wander a
    /// fraction of a unit around fixed spots, so minute 0 and minute 4 of a
    /// video show the same composition.
    ///
    /// PARALLAX — closer doodles move faster, like scenery from a train
    /// window. "Closer" is read from each doodle's local Z under this object
    /// (smaller Z = nearer the camera), so depth is authored the same way it
    /// already was: close -1, near -0.5 (the sharp focal plane), mid 0,
    /// far +0.5. Speed = baseSpeed * parallaxRatio^(-z).
    ///
    /// The LOOK of depth is not this script's job: doodles off the focal
    /// plane use pre-blurred sprite copies from Art/Stuff/Depth (made by
    /// make_depth_variants.py there) and a darker SpriteRenderer colour.
    /// They stay fully opaque — fading them out instead lets the backdrop
    /// and the dust show through, which reads as "ghost", not "far away".
    ///
    /// HOW IT MOVES THEM — FloatingShape rebuilds its position from a fixed
    /// start point every frame, so moving the transform from here would be
    /// overwritten. Instead this script writes FloatingShape.externalOffset
    /// and FloatingShape adds it in. Wander, bob and sway keep working on top.
    ///
    /// WRAPPING — the teleport from one edge to the other must never be
    /// visible, so each doodle wraps inside a box bigger than the frame by its
    /// own size (plus its wander radius and bob). Big doodles therefore spend
    /// longer off-screen than small ones; that also means the composition
    /// never repeats exactly from loop to loop.
    ///
    /// Attach to the parent of the doodles (the "Shapes" prefab root).
    /// </summary>
    public class DoodleParallaxDrift : MonoBehaviour
    {
        [Header("Drift")]
        [Range(0f, 360f)]
        [Tooltip("Direction everything drifts in, in degrees. 0 = right, 180 = left, 90 = up. " +
                 "Keep it on an axis (0 or 180). A shallow diagonal looks tempting but empties the " +
                 "frame: the vertical speed is then tiny, so a doodle that leaves through the top " +
                 "needs minutes to come back in at the bottom. Horizontal keeps every doodle in the " +
                 "row it was authored in; FloatingShape's wander and bob supply the vertical life.")]
        public float driftAngleDeg = 0f;

        [Tooltip("Speed of a doodle at local Z = 0, in world units per second. The frame is " +
                 "17.78 units wide, so 0.07 crosses it in about 4 minutes. Raise for a livelier " +
                 "background; much above 0.15 starts to pull the eye away from the content.")]
        public float baseSpeed = 0.07f;

        [Tooltip("How many times faster a doodle one Z unit closer to the camera moves. " +
                 "1 = everything moves together (no depth). 2.3 gives far 0.046 / mid 0.07 / " +
                 "near 0.11 / foreground 0.16 units per second.")]
        public float parallaxRatio = 2.3f;

        [Header("Frame (the area the recording actually shows)")]
        [Tooltip("World-space centre of the recorded frame. The main camera sits at (0, 0).")]
        public Vector2 frameCenter = Vector2.zero;

        [Tooltip("World-space size of the recorded frame at the default zoom: orthographic size 5 " +
                 "at 16:9 = 17.78 x 10. Typed in rather than read from the camera on purpose — " +
                 "{Zoom:...} tags change the camera's size mid-take, and a Pullback is masked back " +
                 "to this same frame, so this is the only area ever visible.")]
        public Vector2 frameSize = new Vector2(17.78f, 10f);

        [Tooltip("Extra distance past the frame edge, on top of each doodle's own size, before it " +
                 "wraps to the other side. Raise it if you ever catch a doodle popping in or out.")]
        public float extraMargin = 0.3f;

        [Header("Variation")]
        [Tooltip("Start each take at a random point in the drift, so no two videos open on the " +
                 "same composition. Off = every take starts from the layout authored in the prefab.")]
        public bool randomizeStart = true;

        // One entry per doodle. A struct (plain value bundle) rather than a
        // class because it is just four numbers and a reference filled in once
        // at Start — no behaviour, nothing shared.
        private struct Doodle
        {
            public FloatingShape shape;
            public Vector2 home;    // authored world position (where it sits with no drift)
            public float speed;     // units per second, already parallax-scaled
            public float margin;    // how far past the frame edge it must be before wrapping
        }

        private Doodle[] doodles = new Doodle[0];
        private float startOffsetSeconds;

        void Start()
        {
            // true = include inactive children, so a doodle switched off in
            // the Inspector and re-enabled mid-session still gets a slot.
            FloatingShape[] shapes = GetComponentsInChildren<FloatingShape>(true);
            doodles = new Doodle[shapes.Length];

            for (int i = 0; i < shapes.Length; i++)
            {
                FloatingShape shape = shapes[i];

                // Depth under THIS object, whatever the nesting — for direct
                // children this is just their local Z.
                float localZ = transform.InverseTransformPoint(shape.transform.position).z;

                // Visible radius: half the diagonal of the renderer's world
                // bounds, so the margin still covers the sprite when it sways.
                float radius = 0f;
                Renderer rend = shape.GetComponent<Renderer>();
                if (rend != null)
                {
                    Vector3 extents = rend.bounds.extents;
                    radius = new Vector2(extents.x, extents.y).magnitude;
                }

                doodles[i].shape = shape;
                doodles[i].home = shape.transform.position;
                doodles[i].speed = baseSpeed * Mathf.Pow(Mathf.Max(parallaxRatio, 0.01f), -localZ);
                // The wrap is checked per axis, and on either axis FloatingShape
                // can push the doodle at most driftRadius (wander) plus
                // bobAmplitude (bob) away from where this script puts it.
                doodles[i].margin = radius + shape.driftRadius + shape.bobAmplitude + extraMargin;
            }

            // Ten minutes of range is plenty: the layers move at different
            // speeds, so they land at unrelated phases of their loops. Kept
            // short on purpose — doodles in one layer wrap over slightly
            // different distances (bigger sprite = bigger margin), so their
            // even spacing slips a little every loop; a huge offset would
            // start the take with a layer already bunched up.
            startOffsetSeconds = randomizeStart ? Random.Range(0f, 600f) : 0f;
        }

        void Update()
        {
            float t = Time.time + startOffsetSeconds;
            float angleRad = driftAngleDeg * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

            for (int i = 0; i < doodles.Length; i++)
            {
                Doodle d = doodles[i];
                if (d.shape == null) continue;   // destroyed at runtime

                Vector2 travelled = direction * (d.speed * t);

                // Wrap box for this doodle: the frame grown by its margin.
                float halfW = frameSize.x * 0.5f + d.margin;
                float halfH = frameSize.y * 0.5f + d.margin;

                float x = Wrap(d.home.x + travelled.x, frameCenter.x - halfW, frameCenter.x + halfW);
                float y = Wrap(d.home.y + travelled.y, frameCenter.y - halfH, frameCenter.y + halfH);

                // FloatingShape wants a displacement from its start point,
                // not an absolute position.
                d.shape.externalOffset = new Vector3(x - d.home.x, y - d.home.y, 0f);
            }
        }

        void OnDisable()
        {
            // Hand the doodles back to their authored spots so a disabled
            // driver never leaves them parked mid-drift.
            for (int i = 0; i < doodles.Length; i++)
            {
                if (doodles[i].shape != null) doodles[i].shape.externalOffset = Vector3.zero;
            }
        }

        /// <summary>Folds value into [min, max), like the hands of a clock.</summary>
        private static float Wrap(float value, float min, float max)
        {
            float span = max - min;
            if (span <= 0f) return min;
            // Mathf.Repeat is a modulo that stays positive for negative input.
            return min + Mathf.Repeat(value - min, span);
        }
    }
}
