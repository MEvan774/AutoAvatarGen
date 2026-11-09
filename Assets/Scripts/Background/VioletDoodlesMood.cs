using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MugsTech.Background
{
    /// <summary>
    /// {Mood:X} for the Violet Doodles backdrop (scene object
    /// <see cref="BackgroundModeManager.VioletDoodlesObjectName"/>, prefab
    /// Art/BackgroundEffects/NewBackGround). Each mood is its own picture —
    /// same shapes, different light — plus a doodle tint, doodle opacity,
    /// motion speed, dust amount and extra vignette, all from
    /// Resources/MoodBackgrounds.asset (<see cref="MoodBackgroundSet"/>).
    ///
    /// HOW IT DRAWS — the rig's own backdrop sprite (the one carrying
    /// <see cref="BackgroundBrightnessPulse"/>) keeps showing the Calm artwork
    /// untouched. Mood pictures are extra SpriteRenderers parented to it, one
    /// per layer of a <see cref="MoodLayerStack"/>, each a hair closer to the
    /// camera (everything here sorts by depth at order 0, so a higher
    /// sortingOrder would paint over the doodles). Every layer is cover-fitted
    /// to the Calm artwork's rectangle, so it inherits the backdrop's
    /// placement, zoom and pulse and nothing jumps during a crossfade. In plain
    /// Calm every extra layer is off: the backdrop renders exactly as before.
    ///
    /// The doodles, dust and vignette follow the same layer weights, so they
    /// move in lockstep with the picture. Speeds change through
    /// FloatingShape/DoodleParallaxDrift.speedMultiplier (accumulated time —
    /// no positional jump) and ParticleSystem simulationSpeed / emission
    /// multipliers (no restart, no burst).
    ///
    /// Time base: Time.deltaTime, the clock the zoom and position glides use.
    ///
    /// Attached at runtime (BackgroundModeManager when the rig activates, or
    /// <see cref="FindOrAttach"/> from the mood router), so neither the prefab
    /// nor the scene needed editing.
    /// </summary>
    public class VioletDoodlesMood : MonoBehaviour, IAnimatedBackground
    {
        // Local-space Z step between layers, toward the camera. Tiny next to
        // the ~1 unit gap to the nearest doodles, big enough for depth sorting.
        const float LayerZStep = 0.002f;

        MoodLayerStack stack;
        bool initialised;

        SpriteRenderer backdrop;
        Sprite calmSprite;
        readonly List<SpriteRenderer> layerRenderers = new List<SpriteRenderer>();

        readonly Sprite[] moodSprites = new Sprite[MoodLook.MoodCount];
        readonly string[] moodSources = new string[MoodLook.MoodCount];
        readonly MoodProfile[] profiles = new MoodProfile[MoodLook.MoodCount];
        readonly List<Sprite> createdSprites = new List<Sprite>();

        Material iconMaterial;
        readonly List<FloatingShape> shapes = new List<FloatingShape>();
        DoodleParallaxDrift drift;

        struct Dust { public ParticleSystem ps; public float speed; public float rate; }
        readonly List<Dust> dust = new List<Dust>();

        BackgroundVignette vignette;
        float vignetteAuthoredAlpha;

        // Scratch lists for blending (no per-frame allocation).
        readonly List<MoodProfile> blendProfiles = new List<MoodProfile>();
        readonly List<float> blendWeights = new List<float>();

        static VioletDoodlesMood instance;

        // ------------------------------------------------------------------
        // Lookup
        // ------------------------------------------------------------------

        /// <summary>
        /// The rig's mood driver, attached on first use. Null when the active
        /// scene has no Violet Doodles rig (e.g. the main menu).
        /// </summary>
        public static VioletDoodlesMood FindOrAttach()
        {
            if (instance != null) return instance;

            GameObject rig = BackgroundModeManager.FindVioletDoodlesRig();
            if (rig == null) return null;
            return Attach(rig);
        }

        /// <summary>Attach to (or return the driver already on) the rig root.</summary>
        public static VioletDoodlesMood Attach(GameObject rig)
        {
            var existing = rig.GetComponent<VioletDoodlesMood>();
            instance = existing != null ? existing : rig.AddComponent<VioletDoodlesMood>();
            instance.EnsureInitialised();
            return instance;
        }

        // ------------------------------------------------------------------
        // IAnimatedBackground
        // ------------------------------------------------------------------

        public BackgroundMoodController.MoodType CurrentMood
        {
            get { EnsureInitialised(); return stack.Target; }
        }

        public void SetMood(BackgroundMoodController.MoodType mood, float transitionDuration = 3f)
        {
            EnsureInitialised();
            if (!stack.Push(mood, transitionDuration)) return;
            ApplyFrame();   // the new layer exists (at alpha 0) from this very frame
        }

        public void ApplyMoodInstant(BackgroundMoodController.MoodType mood)
        {
            EnsureInitialised();
            stack.Reset(mood);
            ApplyFrame();   // now, not next Update — this is how frame 0 opens on the first mood
        }

        public void SetActive(bool active) => gameObject.SetActive(active);

        /// <summary>
        /// The rig's backdrop picture (the mood layers are its children) — the
        /// layer the idle camera drift moves at 40 % parallax. Null if the rig
        /// has none.
        /// </summary>
        public SpriteRenderer Backdrop
        {
            get { EnsureInitialised(); return backdrop; }
        }

        /// <summary>Where a mood's picture comes from, for the [Mood] log lines.</summary>
        public string DescribeBackground(BackgroundMoodController.MoodType mood)
        {
            EnsureInitialised();
            return moodSources[(int)mood];
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        void Awake()
        {
            instance = this;
            EnsureInitialised();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            foreach (Sprite s in createdSprites) if (s != null) Destroy(s);
            if (iconMaterial != null) Destroy(iconMaterial);
        }

        void EnsureInitialised()
        {
            if (initialised) return;
            initialised = true;

            stack = new MoodLayerStack(BackgroundMoodController.MoodType.CalmNeutral);
            MoodBackgroundSet set = MoodBackgroundSet.Load();
            if (set == null)
                Debug.LogWarning("[Mood] Resources/MoodBackgrounds.asset not found — built-in profiles, " +
                                 "and every mood keeps the Calm artwork.");

            FindBackdrop();
            LoadMoods(set);
            HookDoodles(set);
            HookDust();
            HookVignette();
            LogProfiles(set);
        }

        void FindBackdrop()
        {
            // The backdrop is whatever the brightness pulse sits on; fall back
            // to the first sprite that isn't a doodle.
            var pulse = GetComponentInChildren<BackgroundBrightnessPulse>(true);
            if (pulse != null) backdrop = pulse.GetComponent<SpriteRenderer>();
            if (backdrop == null)
            {
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr.GetComponent<FloatingShape>() != null) continue;
                    backdrop = sr;
                    break;
                }
            }

            calmSprite = backdrop != null ? backdrop.sprite : null;
            if (backdrop == null || calmSprite == null)
                Debug.LogWarning($"[Mood] '{name}' has no backdrop sprite — mood pictures disabled " +
                                 "(doodles, dust and vignette still follow the mood).");
        }

        void LoadMoods(MoodBackgroundSet set)
        {
            for (int i = 0; i < MoodLook.MoodCount; i++)
            {
                var mood = (BackgroundMoodController.MoodType)i;
                MoodBackgroundSet.Entry entry = set != null ? set.Find(mood) : null;
                profiles[i] = entry != null ? entry.look : MoodLook.DefaultProfile(mood);

                Texture2D tex = entry != null ? entry.background : null;
                if (tex != null && calmSprite != null)
                {
                    Sprite s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                             new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                    s.name = $"Mood_{MoodLook.ScriptName(mood)}";
                    createdSprites.Add(s);
                    moodSprites[i] = s;
                    moodSources[i] = $"built-in {tex.name} ({tex.width}x{tex.height})";
                }
                else
                {
                    moodSprites[i] = calmSprite;
                    string calmName = calmSprite != null ? calmSprite.texture.name : "none";
                    moodSources[i] = mood == BackgroundMoodController.MoodType.CalmNeutral
                        ? $"built-in {calmName} (the rig's artwork)"
                        : $"NO PICTURE — keeps {calmName}";
                    if (mood != BackgroundMoodController.MoodType.CalmNeutral && set != null)
                        Debug.LogWarning($"[Mood] {MoodLook.ScriptName(mood)} has no background picture in " +
                                         "MoodBackgrounds.asset — it keeps the Calm artwork.");
                }
            }
        }

        void HookDoodles(MoodBackgroundSet set)
        {
            drift = GetComponentInChildren<DoodleParallaxDrift>(true);
            shapes.AddRange(GetComponentsInChildren<FloatingShape>(true));

            Shader shader = set != null ? set.iconShader : null;
            if (shader == null) shader = Resources.Load<Shader>("Shaders/MoodTintSprite");
            if (shader == null) shader = Shader.Find("MugsTech/MoodTintSprite");
            if (shader == null)
            {
                Debug.LogWarning("[Mood] MugsTech/MoodTintSprite shader not found — doodles keep their " +
                                 "colour and opacity in every mood (speed still follows).");
                return;
            }

            iconMaterial = new Material(shader) { name = "MoodTintSprite (runtime)" };
            int skipped = 0;
            foreach (FloatingShape shape in shapes)
            {
                var sr = shape.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                // Only doodles drawn with plain Sprites/Default are swapped —
                // the tint shader is that shader plus the recolour, so the
                // swap is invisible at Calm. Anything custom is left alone.
                Material m = sr.sharedMaterial;
                if (m != null && m.shader != null && m.shader.name != "Sprites/Default") { skipped++; continue; }
                sr.sharedMaterial = iconMaterial;
            }
            if (skipped > 0)
                Debug.LogWarning($"[Mood] {skipped} doodle(s) use a custom material — left untinted.");
        }

        void HookDust()
        {
            foreach (ParticleSystem ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                dust.Add(new Dust
                {
                    ps = ps,
                    speed = ps.main.simulationSpeed,
                    rate = ps.emission.rateOverTimeMultiplier,
                });
            }
        }

        void HookVignette()
        {
            vignette = GetComponentInChildren<BackgroundVignette>(true);
            if (vignette != null) vignetteAuthoredAlpha = vignette.color.a;
        }

        void LogProfiles(MoodBackgroundSet set)
        {
            var sb = new StringBuilder("[Mood] profiles: ");
            for (int i = 0; i < MoodLook.MoodCount; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(MoodLook.ScriptName((BackgroundMoodController.MoodType)i))
                  .Append('=').Append(moodSources[i]);
            }
            sb.Append($" | numbers from {(set != null ? "MoodBackgrounds.asset" : "code defaults")}")
              .Append($", {shapes.Count} doodles (tint {(iconMaterial != null ? "on" : "off")})")
              .Append($", {dust.Count} dust system(s), vignette {(vignette != null ? "yes" : "none")}");
            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------
        // Per frame
        // ------------------------------------------------------------------

        void Update()
        {
            if (!initialised) return;
            if (stack.IsFading)
            {
                stack.Step(Time.deltaTime);
                ApplyLook();
            }
        }

        // After BackgroundBrightnessPulse's Update has tinted the backdrop, so
        // every mood layer breathes with it.
        void LateUpdate()
        {
            if (initialised) SyncLayers();
        }

        void ApplyFrame()
        {
            ApplyLook();
            SyncLayers();
        }

        void SyncLayers()
        {
            if (backdrop == null || calmSprite == null) return;

            // Plain Calm: no extra layers — the backdrop exactly as authored.
            bool plainCalm = stack.Count == 1 && stack.Target == BackgroundMoodController.MoodType.CalmNeutral;
            int needed = plainCalm ? 0 : stack.Count;

            while (layerRenderers.Count < needed) layerRenderers.Add(CreateLayer(layerRenderers.Count));

            Color pulse = backdrop.color;
            for (int i = 0; i < layerRenderers.Count; i++)
            {
                SpriteRenderer lr = layerRenderers[i];
                bool on = i < needed;
                if (lr.enabled != on) lr.enabled = on;
                if (!on) continue;

                Sprite s = moodSprites[(int)stack[i].Mood];
                if (lr.sprite != s) Place(lr, s, i);
                lr.color = new Color(pulse.r, pulse.g, pulse.b, pulse.a * stack[i].Alpha);
            }
        }

        SpriteRenderer CreateLayer(int index)
        {
            var go = new GameObject($"MoodLayer{index}");
            go.layer = backdrop.gameObject.layer;
            go.transform.SetParent(backdrop.transform, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial   = backdrop.sharedMaterial;
            sr.sortingLayerID   = backdrop.sortingLayerID;
            sr.sortingOrder     = backdrop.sortingOrder;
            sr.maskInteraction  = backdrop.maskInteraction;
            sr.drawMode         = SpriteDrawMode.Simple;
            sr.flipX            = backdrop.flipX;
            sr.flipY            = backdrop.flipY;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows   = false;
            sr.enabled          = false;
            return sr;
        }

        // Cover-fit the layer's picture to the Calm artwork's rectangle and
        // centre it there. Same transform for every layer showing the same
        // picture, whatever its index — only the depth step differs.
        void Place(SpriteRenderer lr, Sprite s, int index)
        {
            lr.sprite = s;
            Bounds calm = calmSprite.bounds;
            Bounds own = s.bounds;
            float k = MoodLook.CoverScale(own.size.x, own.size.y, calm.size.x, calm.size.y);
            Vector3 centre = calm.center - own.center * k;
            lr.transform.localPosition = new Vector3(centre.x, centre.y, -LayerZStep * (index + 1));
            lr.transform.localRotation = Quaternion.identity;
            lr.transform.localScale = new Vector3(k, k, 1f);
        }

        void ApplyLook()
        {
            blendProfiles.Clear();
            blendWeights.Clear();
            for (int i = 0; i < stack.Count; i++)
            {
                blendProfiles.Add(profiles[(int)stack[i].Mood]);
                blendWeights.Add(stack.Weight(i));
            }
            MoodProfile look = MoodLook.Blend(blendProfiles, blendWeights, out float tintAmount);

            if (iconMaterial != null)
            {
                iconMaterial.SetColor("_MoodTint", look.iconTint);
                iconMaterial.SetFloat("_MoodAmount", tintAmount);
                iconMaterial.SetFloat("_MoodAlpha", Mathf.Clamp01(look.iconAlpha));
            }

            float speed = Mathf.Max(0f, look.particleSpeed);
            if (drift != null) drift.speedMultiplier = speed;
            foreach (FloatingShape shape in shapes)
                if (shape != null) shape.speedMultiplier = speed;

            float emission = Mathf.Max(0f, look.particleEmission);
            foreach (Dust d in dust)
            {
                if (d.ps == null) continue;
                var main = d.ps.main;
                main.simulationSpeed = d.speed * speed;
                var em = d.ps.emission;
                em.rateOverTimeMultiplier = d.rate * emission;
            }

            if (vignette != null)
            {
                Color c = vignette.color;
                c.a = MoodLook.VignetteAlpha(vignetteAuthoredAlpha, look.vignette);
                vignette.color = c;
            }
        }
    }
}
