using System.Collections;
using MugsTech.Stage;
using UnityEngine;

// ============================================================================
// MediaPresentationSystem — the side move (Left ↔ Right ↔ Center).
//
// Two styles, chosen in the main menu ("Side move", StagePrefs.SideMoveIsDash;
// there is no tag keyword, as with the Hop):
//
//   Glide  the default, and the move every existing video was made with:
//          MoveAvatar in MediaPresentationSystem.cs, untouched — one eased
//          second (transitionDuration) and a mirror flip halfway.
//   Dash   DashMath: a lean away, a fast start that eases out, a turn of his
//          body instead of the mirror flip, a little past his mark, a squash.
//          0.45 s, so what waits for him to land waits half a second.
//
// Like the stage's own moves the dash is a pose per frame on avatarParent
// (the Hop / Grow / Shrink and the idle sway on the rig below carry on under
// it), and it starts from where he is, with whatever shape a cut-short move
// left on him.
// ============================================================================
public partial class MediaPresentationSystem
{
    [Header("Side move: Dash (main menu ▸ Side move)")]
    [Tooltip("How long a dash between Left, Right and Center takes, tag to rest. Cards and media that " +
             "wait for him to land wait this long (plus a quarter second at most).")]
    public float dashDuration = DashMath.TotalSeconds;
    [Tooltip("How far he leans into a dash at his fastest, in degrees.")]
    public float dashLean = DashMath.Lean;
    [Tooltip("How much longer (and half as much shorter) he is along a dash at his fastest. 0.06 = 6 %.")]
    public float dashStretch = DashMath.Stretch;

    // The main-menu choice, read when a take begins (BeginStageRun).
    private bool sideMoveDash;

    /// <summary>True when this take's side moves are dashes (main menu ▸ Side move).</summary>
    public bool SideMoveIsDash => sideMoveDash;

    /// <summary>How long a smooth Left ↔ Right ↔ Center move takes in the chosen style.</summary>
    public float SideMoveSeconds => sideMoveDash ? Mathf.Max(0.05f, dashDuration) : transitionDuration;

    // The smooth side move in the chosen style. Glide is MoveAvatar, called
    // exactly as it always was.
    IEnumerator SideMove(Transform current, Transform target, CharacterPosition targetPosition) =>
        sideMoveDash ? DashAvatar(target, targetPosition) : MoveAvatar(current, target, targetPosition);

    sealed class DashRun
    {
        public Vector2 A, B;                    // the moved transform: where he is, and his new mark
        public Quaternion StartRotation, EndRotation;
        public float Feet;                      // his feet below the transform: the line that holds still
        public float LeanSign;                  // +1: leaning into the travel turns him counter-clockwise (to the left)
        public bool Turns, StartFlip;           // his facing changes on the way; what it was
        public Vector2 Carried = Vector2.one;   // squash / stretch a cut-short move left on him…
        public float CarriedTilt;               // …and lean: eased away (StageMoves.CarryLeft)
        public Transform Mark;
        public CharacterPosition Target;
        public float Seconds;
    }

    IEnumerator DashAvatar(Transform targetLocation, CharacterPosition targetPosition)
    {
        DashRun run = avatarParent != null && targetLocation != null ? BeginDash(targetLocation, targetPosition) : null;
        if (run == null)
        {
            // Nowhere to go (a repeat of his own mark) or nothing to move.
            if (avatarParent != null && targetLocation != null)
            {
                avatarParent.position = targetLocation.position;
                avatarParent.rotation = targetLocation.rotation;
            }
            UpdateFacing(targetPosition);
            presenterGliding = false;
            yield break;
        }

        Debug.Log($"Dashing to {targetLocation.name}" + (run.Turns ? " (he turns on the way)" : ""));
        float t = 0f;
        while (t < run.Seconds)
        {
            PoseDash(run, t);
            t += Time.deltaTime;
            yield return null;
        }
        EndDash(run);
        Debug.Log($"Avatar reached {targetLocation.name}");
    }

    // Sets a dash up from the transform as it is now: his place, and whatever
    // squash and lean a cut-short move left on him. Null when he is already there.
    DashRun BeginDash(Transform targetLocation, CharacterPosition targetPosition)
    {
        Vector2 here = avatarParent.position, mark = targetLocation.position;
        if ((mark - here).sqrMagnitude < 1e-4f && !stageShaped) return null;

        StageMath.Body body = StageBody();
        Quaternion upright = stageShaped ? stageShapeRotation : avatarParent.rotation;
        Vector3 own = presenterRestScale * presenterUniform, now = avatarParent.localScale;
        bool faceFlipped = targetPosition == CharacterPosition.Right;   // UpdateFacing: only Right is mirrored
        var run = new DashRun
        {
            A = here, B = mark,
            StartRotation = upright, EndRotation = targetLocation.rotation,
            Feet = (body.CentreOffset.y - body.Size.y * 0.5f) * presenterUniform,
            LeanSign = mark.x < here.x ? 1f : -1f,
            StartFlip = characterRenderer != null && characterRenderer.flipX,
            Carried = new Vector2(Mathf.Abs(own.x) > 1e-5f ? now.x / own.x : 1f,
                                  Mathf.Abs(own.y) > 1e-5f ? now.y / own.y : 1f),
            CarriedTilt = Mathf.DeltaAngle(upright.eulerAngles.z, avatarParent.eulerAngles.z),
            Mark = targetLocation,
            Target = targetPosition,
            Seconds = Mathf.Max(0.05f, dashDuration),
        };
        run.Turns = characterRenderer != null && run.StartFlip != faceFlipped;

        stageShapeRotation = run.EndRotation;
        stageShaped = true;
        return run;
    }

    // One frame of a dash: DashMath's pose for that time on the moved transform.
    void PoseDash(DashRun run, float t)
    {
        DashMath.Pose pose = DashMath.At(t, run.Seconds, dashLean, dashStretch, run.Turns);
        float carry = StageMoves.CarryLeft(t);

        Vector2 p = Vector2.LerpUnclamped(run.A, run.B, pose.Travel);
        // (What a cut-short move left on him is eased away about the transform
        // itself, so the dash's first frame is exactly the frame he was on.)
        float tilt = pose.Tilt * run.LeanSign;
        p = StageMoves.HoldEdge(p, p.y + run.Feet, pose.ScaleY, tilt);

        avatarParent.position = new Vector3(p.x, p.y, avatarParent.position.z);
        Vector3 own = presenterRestScale * presenterUniform;
        // The turn is his width going through nothing; the sprite is mirrored on the frame it does.
        float width = pose.ScaleX * Mathf.Max(pose.Width, 0.001f) * Mathf.Lerp(1f, run.Carried.x, carry);
        avatarParent.localScale = new Vector3(own.x * width, own.y * pose.ScaleY * Mathf.Lerp(1f, run.Carried.y, carry), own.z);
        avatarParent.rotation = Quaternion.Slerp(run.StartRotation, run.EndRotation, Mathf.Clamp01(pose.Travel)) *
                                Quaternion.Euler(0f, 0f, tilt + run.CarriedTilt * carry);
        if (run.Turns && characterRenderer != null)
            characterRenderer.flipX = pose.Turned ? !run.StartFlip : run.StartFlip;
    }

    // Exactly his mark: place, rotation, scale, facing — then the sway base, as the glide does.
    void EndDash(DashRun run)
    {
        avatarParent.position = new Vector3(run.B.x, run.B.y, avatarParent.position.z);
        avatarParent.rotation = run.EndRotation;
        avatarParent.localScale = presenterRestScale * presenterUniform;
        stageShaped = false;
        UpdateFacing(run.Target);
        if (avatarSystem != null)
            avatarSystem.SetSwayBase(run.Mark.position, run.Mark.rotation);
        presenterGliding = false;
    }
}
