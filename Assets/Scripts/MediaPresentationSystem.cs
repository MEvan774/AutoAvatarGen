using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DG.Tweening;    // media entry slide/fade — same tween library the content cards use
using MugsTech;       // TimestampMarkerLog — the {Timestamp:"..."} chapter-marker capture buffer
using MugsTech.Style; // EntryDirection — shared with the content-card side modifiers

// ============================================================================
// MediaPresentationSystem — expanded with character position markers.
//
// WHAT'S NEW:
//   - {Position:Left}, {Position:Right}, {Position:Center} markers in scripts
//   - Three position Transforms in Inspector (left, right, center)
//   - Character faces toward content zone automatically (flipX)
//   - Position changes are tracked against audio time, same as emotions/media
//
// WHAT'S UNCHANGED:
//   - {Image:name,duration} and {Video:name,duration} markers work as before.
//     Both also accept an optional trailing ",Left"/",Right" (like the side
//     content cards) picking the screen side the media rests on — default is
//     the authored (left) slot — and enter with the cards' slide+fade
//     overshoot animation (see ShowDisplay/PlayMediaEntry).
//   - MoveAvatar easing and DisplayMedia — all the same ({Video:} no longer
//     pauses the narration; the clip is a silent overlay, see ShowMedia)
//   - HybridAvatarSystem handles emotions/sway — completely untouched
//
// SCRIPT FORMAT EXAMPLE:
//   {Neutral}
//   {Position:Center}
//   Hello everyone, welcome to the show!
//   {Excited}
//   {Position:Left}
//   Breaking news in AI today!
//   {Image:ai_headline,3}
//   {Serious}
//   {Position:Right}
//   But there's a catch...
//   {Position:Center}
//   That's all for today, thanks for watching!
// ============================================================================

public class MediaPresentationSystem : MonoBehaviour
{
    [Header("Components")]
    public HybridAvatarSystem avatarSystem;
    public Transform avatarParent;
    public AudioSource voiceAudio;

    [Header("Character Positions")]
    [Tooltip("Where the character stands when on the left side.")]
    public Transform leftLocation;
    [Tooltip("Where the character stands when centered (your existing CenterLocation).")]
    public Transform centerLocation;
    [Tooltip("Where the character stands when on the right side (your existing PresentationLocation).")]
    public Transform rightLocation;

    [Header("Character Facing")]
    [Tooltip("The SpriteRenderer on the character — used to flip facing direction.")]
    public SpriteRenderer characterRenderer;

    [Header("Media Display")]
    public Canvas mediaCanvas;
    public RawImage mediaDisplay;
    public VideoPlayer videoPlayer;

    [Header("Avatar Positioning")]
    public float transitionDuration = 0.5f;
    [Tooltip("When true, position changes snap instantly. When false, smooth easing.")]
    public bool useHardCuts = false;

    [Header("Camera Zoom")]
    [Tooltip("Main camera — used for zoom in/out.")]
    public Camera mainCamera;
    [Tooltip("Length of the eased glide for each {Zoom:In} step, {Zoom:Out}, the ,D= release, and " +
             "the glide back when a BigText / BigMedia / BigImage ends a zoom run. " +
             "Ignored by ,Cut / Reset / Pullback / ExtremeIn / ExtremeOut, which snap.")]
    public float zoomDuration = 0.7f;

    // {Zoom:In} stacks: each one leans in a step further until something ends
    // the run (see EndZoomRun). Step 1 keeps the old single multiplier's
    // serialized value, so existing scenes push in exactly as before.
    [Tooltip("Step 1 of a {Zoom:In} run — the first push-in (1.12 = 112% zoom, blueprint says 110-115%).")]
    [UnityEngine.Serialization.FormerlySerializedAs("zoomInMultiplier")]
    [Range(1.01f, 1.6f)]
    public float zoomStep1Multiplier = 1.12f;
    [Tooltip("Step 2 — a second {Zoom:In} before the run ends (1.24 = 124%). Never less than step 1.")]
    [Range(1.01f, 1.6f)]
    public float zoomStep2Multiplier = 1.24f;
    [Tooltip("Step 3 — the last step (1.36 = 136%). Never less than step 2. A fourth {Zoom:In} " +
             "in the same run is ignored.")]
    [Range(1.01f, 1.6f)]
    public float zoomStep3Multiplier = 1.36f;

    [Header("Extreme Zoom ({Zoom:ExtremeIn} / {Zoom:ExtremeOut})")]
    [Tooltip("How far in the extreme zoom punches (2 = 200%). Much harder than {Zoom:In} — " +
             "this is a close-up on Mugs's face, not a push for emphasis.")]
    [Range(1.3f, 4f)]
    public float extremeZoomMultiplier = 2f;

    [Tooltip("Re-centre the camera on the character while the close-up is held, so the punch " +
             "lands on Mugs's face wherever he's standing. Off = zoom straight into the middle " +
             "of the frame, which only frames him at {Position:Center}.")]
    public bool extremeZoomFollowsCharacter = true;

    [Tooltip("World-space offset from the character's position up towards his head. The position " +
             "markers sit at floor level (y = -3.14), so a large positive Y is what lifts the " +
             "close-up off his body.\n\n" +
             "NOTE this is measured from the floor marker, NOT from the camera's rest position — " +
             "the camera sits at y = 0, so it ends up at (marker + this). The 2.69 default puts it " +
             "at world y -0.45. For reference, his head centre is at world y +2.66 (offset 5.8), " +
             "so this framing sits well below the head and holds his body rather than his face. " +
             "Every emotion sprite shares the same aspect and is height-matched by Normalize " +
             "Sprite Size, so one offset frames them all.")]
    public Vector2 extremeZoomFaceOffset = new Vector2(0f, 2.69f);

    [Tooltip("{Zoom:ExtremeIn} cuts every card and {Image:}/{Video:} away on the camera snap's " +
             "frame. Writers stack the open visual's End tag, {Position:Center,Cut} and the " +
             "ExtremeIn on the same word, and those can fire a frame or two apart — so a fade, " +
             "End or dismissal that comes due within this many seconds of an ExtremeIn is " +
             "left for the cut instead of starting a visible fade in the wide shot.")]
    [Range(0f, 0.3f)]
    public float closeUpCutWindow = 0.1f;

    [Header("Pullback Effect ({Zoom:Pullback})")]
    [Tooltip("Initial wide framing — orthographicSize is snapped to defaultSize * this on trigger.")]
    [Range(1.1f, 4f)]
    public float pullbackStartMultiplier = 1.8f;
    [Tooltip("End of the slow drift. The camera linearly drifts from start → end, then jump-cuts back.")]
    [Range(1.1f, 4f)]
    public float pullbackEndMultiplier = 1.9f;
    [Tooltip("How long the slow drift lasts (seconds). Overridden per-marker by ',D=seconds'.")]
    public float pullbackDuration = 3f;

    [Tooltip("Black border planes (or any GameObjects) that frame the default camera view in " +
             "world space. Activated during {Zoom:Pullback} so anything outside the original " +
             "framing is cropped to black; deactivated when the effect ends. Position them just " +
             "outside the default camera frame edges and large enough to extend beyond the maximum " +
             "pullback view.")]
    public GameObject[] pullbackBorderPlanes;

    [Header("Content Cards")]
    [Tooltip("Content zone card system — displays branded text cards alongside media.")]
    public ContentZoneController contentZoneController;

    [Header("Black Panel")]
    [Tooltip("Fullscreen black panel controller — jump-cuts a black overlay via {Black:duration} markers.")]
    public BlackPanelController blackPanelController;

    [Header("Transitions & Mood")]
    [Tooltip("Whole-screen scene transitions ({Transition:Wipe/Shutter/Iris}). Auto-found, or a " +
             "'TransitionDirector' GameObject is created at runtime if none exists. The overlay is " +
             "hosted under mediaCanvas so it's captured by the recorder.")]
    public ScreenTransitionController screenTransitionController;
    [Tooltip("Synthwave-style mood target for {Mood:Calm/Energetic/Tense/Playful/Minimal} (and for " +
             "{Mood:...} bundled onto a transition line). Mood calls are ROUTED by " +
             "BackgroundStyleManager to whichever background style is active; this reference just " +
             "pins the synthwave instance. Optional: auto-found, and {Mood:} is a no-op if the " +
             "scene hosts no matching background.")]
    public MugsTech.Background.BackgroundMoodController moodController;
    [Tooltip("Seconds the background mood crossfade takes when a {Mood:...} fires. Independent of the " +
             "~0.7s transition; may finish slightly after the reveal (per the blueprint's 2-4s guidance).")]
    public float moodCrossfadeSeconds = 3f;

    [Header("Transition Sound Effects")]
    // Each transition has two halves: cover ("in", the screen closes) and
    // reveal ("out", it opens again). Each half has its own optional clip —
    // any slot left empty is simply silent for that half.
    [Tooltip("Optional sound played as a {Transition:Wipe} cover (in) starts. Drag an AudioClip " +
             "here in the Inspector; leave empty for a silent half.")]
    public AudioClip transitionWipeSfx;
    [Tooltip("Optional sound played as the {Transition:Wipe} reveal (out) starts. Leave empty for silent.")]
    public AudioClip transitionWipeOutSfx;
    [Tooltip("Optional sound played as a {Transition:Shutter} cover (in) starts. Leave empty for silent.")]
    public AudioClip transitionShutterSfx;
    [Tooltip("Optional sound played as the {Transition:Shutter} reveal (out) starts. Leave empty for silent.")]
    public AudioClip transitionShutterOutSfx;
    [Tooltip("Optional sound played as a {Transition:Iris} cover (in) starts. Leave empty for silent.")]
    public AudioClip transitionIrisSfx;
    [Tooltip("Optional sound played as the {Transition:Iris} reveal (out) starts. Leave empty for silent.")]
    public AudioClip transitionIrisOutSfx;
    [Range(0f, 1f)]
    [Tooltip("Playback volume for all the transition sound effects above (0 = silent, 1 = full).")]
    public float transitionSfxVolume = 1f;

    [Header("Media Settings")]
    [Tooltip("Legacy fallback — Resources subfolder used only if external folders below are blank or a file can't be found on disk.")]
    public string mediaFolderPath = "Media";

    [Header("External Media Folders (absolute paths outside the project)")]
    [Tooltip("Root folder on disk that holds the media subfolders below. Leave blank to fall back to Resources. " +
             "Overridden at runtime by the main menu's 'Media folder' input (saved to PlayerPrefs). " +
             "Missing subfolders are created automatically — one path is all that needs setting.")]
    public string externalMediaRoot = "";
    [Tooltip("Subfolder under the root that holds video b-roll files. Searched first by {Video:name,...}.")]
    public string bRollSubfolder = "BRoll";
    [Tooltip("Subfolder under the root that holds general images/screenshots. Searched first by {Image:name,...} / {BigImage:name,...}.")]
    public string imagesSubfolder = "Images";
    [Tooltip("Subfolder under the root that holds CEO / executive portraits. Searched by {Image:} after Images.")]
    public string ceoSubfolder = "CEO";
    [Tooltip("Subfolder under the root that holds tweet / social-post screenshots. Searched by {Image:} after CEO.")]
    public string twitterPostsSubfolder = "TwitterPosts";
    [Tooltip("Subfolder under the root that holds country flags. Searched by {Image:} after TwitterPosts.")]
    public string flagsSubfolder = "Flags";
    [Tooltip("Subfolder under the root that holds company logos. Searched by {Image:} after Flags.")]
    public string logosSubfolder = "Logos";

    [Tooltip("How long a {Video:} clip may spend preparing before it is given up on. " +
             "The clip counts as an on-screen visual for the whole prepare, so an " +
             "unreadable file would otherwise hold the take open until the recorder's " +
             "safety cap.")]
    public float videoPrepareTimeout = 10f;

    // Kept in sync with MainMenuController.MediaRootFolderPrefKey. If you
    // rename one, rename the other.
    public const string MediaRootFolderPrefKey = "AutoAvatarGen.ExternalMediaRoot";

    static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };
    static readonly string[] VideoExtensions = { ".mp4", ".mov", ".webm", ".avi" };

    // --- Existing state (unchanged) ---
    private List<MediaMarkerData> mediaMarkers;
    private int lastTriggeredMediaMarker = -1;
    private bool isShowingMedia = false;
    private Coroutine currentMediaCoroutine;
    private Coroutine movementCoroutine;

    // --- New: position tracking ---
    private List<PositionMarkerData> positionMarkers;
    private int lastTriggeredPositionMarker = -1;
    private CharacterPosition currentPosition = CharacterPosition.Center;

    /// <summary>
    /// Read-only access to the character's current position. Used by
    /// ContentZoneController to compute character-aware card entry directions.
    /// </summary>
    public CharacterPosition CurrentPosition => currentPosition;

    /// <summary>True while a {Video:} or {Image:} media element is on screen.</summary>
    public bool IsShowingMedia => isShowingMedia;

    /// <summary>
    /// True when a {Zoom:ExtremeIn} or {Zoom:ExtremeOut} marker fires within
    /// <paramref name="window"/> seconds of <paramref name="time"/>. Both are
    /// hard camera cuts, so HybridAvatarSystem uses this to jump-cut any emotion
    /// change that lands on the same beat instead of animating it (a shrink /
    /// grow / crossfade playing under a camera snap reads as a glitch — the cut
    /// itself is the transition). Zoom markers are parsed before the avatar
    /// receives the script, so the list is complete by the time emotions fire.
    /// </summary>
    public bool HasExtremeZoomCutNear(float time, float window)
    {
        if (zoomMarkers == null) return false;
        for (int i = 0; i < zoomMarkers.Count; i++)
        {
            var z = zoomMarkers[i];
            if (z.zoomType != ZoomType.ExtremeIn && z.zoomType != ZoomType.ExtremeOut) continue;
            if (Mathf.Abs(z.triggerTime - time) <= window) return true;
        }
        return false;
    }

    /// <summary>True while a {Zoom:ExtremeIn} close-up is held (until its ExtremeOut).</summary>
    public bool IsCloseUpHeld => !float.IsNaN(extremeRestoreSize);

    /// <summary>
    /// True when a {Zoom:ExtremeIn} that hasn't fired yet is due within
    /// <see cref="closeUpCutWindow"/> of the narration clock. Anything about to
    /// fade or close now is left for that cut, so it vanishes on the camera
    /// snap's frame rather than starting a fade a frame or two early. Goes
    /// false the moment the ExtremeIn fires — or, if it never does, once the
    /// window has passed, so a caller waiting on it always gets released.
    /// </summary>
    public bool IsCloseUpCutImminent
    {
        get
        {
            if (closeUpCutWindow <= 0f || zoomMarkers == null ||
                voiceAudio == null || !voiceAudio.isPlaying) return false;

            float now = voiceAudio.time;
            for (int i = lastTriggeredZoomMarker + 1; i < zoomMarkers.Count; i++)
            {
                var z = zoomMarkers[i];
                if (z.zoomType == ZoomType.ExtremeIn &&
                    Mathf.Abs(z.triggerTime - now) <= closeUpCutWindow) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// True while ANY trailing visual is still on screen — media, a content
    /// card, or the black panel. The recorder polls this after the narration
    /// ends so an end-of-script tag (a closing {Logo:...,D=8} end-card, a final
    /// {Black:2}, etc.) is captured for its full duration instead of being cut
    /// off the instant the audio stops.
    /// </summary>
    public bool HasActiveTrailingVisual =>
        isShowingMedia
        || (blackPanelController != null && blackPanelController.IsShowing)
        || (contentZoneController != null && contentZoneController.HasActiveOrQueuedCard);

    // --- New: zoom tracking ---
    private List<ZoomMarkerData> zoomMarkers;
    private int lastTriggeredZoomMarker = -1;
    private float defaultCameraSize;
    private Coroutine zoomCoroutine;
    private Coroutine pendingResetCoroutine;

    // Where the running AnimateZoom glide is heading; NaN when no glide runs.
    // Lets an ExtremeIn tell a run still gliding home from a camera at rest.
    private float zoomEaseTarget = float.NaN;

    // The {Zoom:In} run: 0 = normal framing, 1..MaxZoomSteps = steps leaned in.
    const int MaxZoomSteps = 3;
    private int zoomStep;

    // The run's ,D= release time. Set by the latest step that carried a D=,
    // and every later step restarts the countdown with it, so the release
    // always lands that long after the last push-in.
    private float zoomRunHold;

    /// <summary>
    /// How far the current {Zoom:In} run has leaned in: 0 = normal framing,
    /// 1-3 = the step. Reset to 0 by anything that ends the run.
    /// </summary>
    public int ZoomStep => zoomStep;

    // Framing captured the moment {Zoom:ExtremeIn} fired, restored verbatim by
    // {Zoom:ExtremeOut}. NaN means "no close-up currently held" — which is also
    // how an unmatched ExtremeOut is detected.
    private float extremeRestoreSize = float.NaN;
    private Vector3 extremeRestoreCameraPos;

    // --- Black panel tracking ---
    private List<BlackPanelMarkerData> blackPanelMarkers;
    private int lastTriggeredBlackPanelMarker = -1;

    // --- Transition + mood tracking ---
    private List<TransitionMarkerData> transitionMarkers;
    private int lastTriggeredTransitionMarker = -1;
    private List<MoodMarkerData> moodMarkers;
    private int lastTriggeredMoodMarker = -1;
    private AudioSource transitionSfxSource;

    // --- Timestamp (YouTube chapter) tracking ---
    private List<TimestampMarkerData> timestampMarkers;
    private int lastTriggeredTimestampMarker = -1;

    // Authored size of the mediaDisplay rect, captured before anything resizes
    // it. A video shrinks the rect to letterbox its own aspect and this puts it
    // back, so the next {Image:} sees the slot the scene actually authored.
    private Vector2 mediaDisplayBaseSize;

    // Authored side-slot layout of the mediaDisplay rect, captured in Awake
    // alongside mediaDisplayBaseSize. A media tag's ",Right" modifier mirrors
    // these about the parent's center for that one marker (see ApplyMediaSide);
    // the end-of-media cleanup puts the authored (left) values back.
    private Vector2 mediaDisplayBaseAnchorMin;
    private Vector2 mediaDisplayBaseAnchorMax;
    private Vector2 mediaDisplayBasePivot;
    private Vector2 mediaDisplayBaseAnchoredPos;

    // Fade handle for the media entry animation, added to mediaDisplay's own
    // GameObject in Awake (the RawImage has no CanvasGroup in the scene).
    private CanvasGroup mediaDisplayGroup;

    // Entry tween of the media on screen — killed by the end-of-media cleanup
    // so a dismissed media can't keep animating the rect the next marker uses.
    private Sequence mediaEntrySequence;

    /// <summary>
    /// Picks the VideoPlayer that {Video:} playback drives, and configures it.
    ///
    /// The scene wires <c>videoPlayer</c> to a VideoPlayer component that lives
    /// ON the Main Camera, in CameraFarPlane render mode with Direct audio.
    /// That single piece of wiring caused three separate faults the moment a
    /// {Video:} tag fired:
    ///   * far-plane rendering paints the clip across the whole frame instead
    ///     of into <c>mediaDisplay</c>;
    ///   * showing/hiding it meant SetActive() on the Main Camera itself, so
    ///     the scene's AudioListener disappeared at startup (Evereal spawns a
    ///     replacement) and then came back mid-take — two live AudioListeners,
    ///     and the narration drops out of the capture from that point on;
    ///   * bringing a second camera live mid-take re-ran TransparentCamera and
    ///     CrossPlatformRecorder.Awake, which is the jitter.
    ///
    /// So when the assigned player shares its GameObject with a Camera or an
    /// AudioListener, that object is parked once and never touched again, and
    /// playback moves to a dedicated child that owns nothing else.
    /// </summary>
    void SetUpVideoPlayer()
    {
        if (videoPlayer != null &&
            (videoPlayer.GetComponent<Camera>() != null ||
             videoPlayer.GetComponent<AudioListener>() != null))
        {
            videoPlayer.playOnAwake     = false;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            videoPlayer.enabled         = false;

            // Deactivating this object is inherited behaviour, not something
            // the media system needs: the take is rendered by a different
            // camera and has always run with the Main Camera disabled, so
            // leaving it on would add an extra camera to the frame. Keep it
            // off — the difference is that nothing switches it back on now.
            videoPlayer.gameObject.SetActive(false);

            Debug.LogWarning($"[Media] The assigned VideoPlayer shares '{videoPlayer.gameObject.name}' " +
                             "with a Camera/AudioListener — parked it and using a dedicated player instead.");

            var host = new GameObject("[MediaVideoPlayer]");
            host.transform.SetParent(transform, false);
            videoPlayer = host.AddComponent<VideoPlayer>();
        }

        if (videoPlayer == null) return;

        videoPlayer.playOnAwake       = false;
        videoPlayer.renderMode        = VideoRenderMode.RenderTexture;
        videoPlayer.aspectRatio       = VideoAspectRatio.FitInside;
        videoPlayer.audioOutputMode   = VideoAudioOutputMode.None;
        videoPlayer.isLooping         = false;
        videoPlayer.skipOnDrop        = true;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.gameObject.SetActive(false);
    }

    // Audio tracks can only be enumerated once the player is prepared, and
    // EnableAudioTrack takes effect on the next Play() — so this is called
    // between Prepare and Play. audioOutputMode alone should be enough, but the
    // scene asset still ships Direct audio on the borrowed player, so both are
    // asserted rather than trusted.
    static void MuteAudioTracks(VideoPlayer vp)
    {
        vp.audioOutputMode = VideoAudioOutputMode.None;
        for (ushort i = 0; i < vp.audioTrackCount; i++)
            vp.EnableAudioTrack(i, false);
    }

    // Minimum on-screen time for a {Video:} that carries no duration, and the
    // display time for a duration-less {Image:} (the guide's documented 3s).
    const float DefaultMediaSeconds = 3f;

    // Bumped by anything that supersedes on-screen media: the presenter actually
    // moving to a different position, or a content card appearing. A {Video:}
    // watches this and ends the moment it changes.
    private int mediaDismissToken;

    /// <summary>
    /// Ends whatever {Video:} is on screen, on the next frame. A script author
    /// (human or Claude) cannot know how long the narration under a clip runs,
    /// so a clip is not given a fixed lifetime — it lives until the beat it
    /// belongs to ends. No-op when nothing is showing.
    /// </summary>
    public void DismissActiveMedia() => mediaDismissToken++;

    // Bumped by every {Zoom:ExtremeIn} cut. Unlike mediaDismissToken it is
    // also watched by a timed {Image:} (which ignores ordinary dismissals) and
    // by a {Video:} still preparing, so nothing the close-up cut away can
    // finish its beat or appear late on top of his face.
    private int closeUpCutCount;

    // The {Zoom:ExtremeIn} jump cut: everything in the content zone and the
    // media slot goes on this frame, with no fade. The camera snap that follows
    // in ApplyZoom happens on the same frame. The black panel and the future
    // timeline are untouched — later cards and media still fire normally (and
    // are blocked only while the close-up is actually held).
    void CutAwayVisualsForCloseUp()
    {
        if (contentZoneController != null)
            contentZoneController.CutForCloseUp();

        bool mediaOnScreen = isShowingMedia;
        closeUpCutCount++;
        DismissActiveMedia();

        // The media coroutine notices the token on its next frame and runs its
        // normal teardown — hide the slot now so it isn't in the close-up's
        // first frame. Its shadow follows in LateUpdate, still this frame.
        if (mediaEntrySequence != null && mediaEntrySequence.IsActive())
            mediaEntrySequence.Kill();
        mediaEntrySequence = null;
        if (mediaDisplay != null && mediaDisplay.gameObject.activeSelf)
            mediaDisplay.gameObject.SetActive(false);

        if (mediaOnScreen)
            Debug.Log("[Zoom] ExtremeIn cut the on-screen media away.");
    }

    // True once the NEXT {Image:}/{Video:} marker's trigger time has arrived —
    // the third thing that supersedes a clip. Checking it here (rather than
    // letting TrackMediaByTime interrupt) means the running coroutine always
    // finishes its own cleanup before the next one starts.
    bool NextMediaMarkerDue()
    {
        int next = lastTriggeredMediaMarker + 1;
        return mediaMarkers != null
            && next < mediaMarkers.Count
            && voiceAudio != null
            && voiceAudio.time >= mediaMarkers[next].triggerTime;
    }

    // Reveal the display now that it holds a real texture. Kept as a single
    // call site so no future branch can activate an empty (white) RawImage.
    // Rests the slot on the marker's side — ",Right" mirrors the authored
    // left-side rect about the parent's center, exactly like a ",Right" content
    // card — then plays the cards' slide+fade entry from that side.
    void ShowDisplay(MediaMarkerData marker)
    {
        if (mediaDisplay == null || mediaDisplay.texture == null) return;

        bool onRight = marker.side == EntryDirection.FromRight;
        ApplyMediaSide(onRight);
        mediaDisplay.gameObject.SetActive(true);
        PlayMediaEntry(onRight ? EntryDirection.FromRight : EntryDirection.FromLeft);
    }

    // Rests the media slot on the requested side. Right reflects the authored
    // rect about the parent's center — the same reflection
    // ContentZoneController.EnsureRightContentZone applies to the card zone, so
    // a ",Right" media lands exactly where a ",Right" card does. The pivot
    // mirrors too, so letterboxing (FitDisplayToAspect) keeps hugging the outer
    // screen edge instead of drifting toward the center.
    void ApplyMediaSide(bool right)
    {
        if (mediaDisplay == null) return;
        RectTransform rt = mediaDisplay.rectTransform;

        if (right)
        {
            rt.anchorMin        = new Vector2(1f - mediaDisplayBaseAnchorMax.x, mediaDisplayBaseAnchorMin.y);
            rt.anchorMax        = new Vector2(1f - mediaDisplayBaseAnchorMin.x, mediaDisplayBaseAnchorMax.y);
            rt.pivot            = new Vector2(1f - mediaDisplayBasePivot.x,     mediaDisplayBasePivot.y);
            rt.anchoredPosition = new Vector2(-mediaDisplayBaseAnchoredPos.x,   mediaDisplayBaseAnchoredPos.y);
        }
        else
        {
            rt.anchorMin        = mediaDisplayBaseAnchorMin;
            rt.anchorMax        = mediaDisplayBaseAnchorMax;
            rt.pivot            = mediaDisplayBasePivot;
            rt.anchoredPosition = mediaDisplayBaseAnchoredPos;
        }
    }

    // Slide + fade the media in, matching the content-card entry: the central
    // entry ease (overshoot curve, or the menu's 'Ease in + fade' style) and
    // default timings from CardEntryAnimator, and the same
    // parent-mirror correction the cards need — the recorded canvas renders
    // horizontally mirrored, so a horizontal slide's offset must be flipped or
    // the media eases in from the wrong side (see ContentCard.Show and
    // ContentCard.SetParentMirrorSign for the card-side twin of this).
    void PlayMediaEntry(EntryDirection dir)
    {
        if (mediaEntrySequence != null && mediaEntrySequence.IsActive())
            mediaEntrySequence.Kill();

        CardEntryAnimator anim = CardEntryAnimator.Instance;
        RectTransform rt = mediaDisplay.rectTransform;

        float width = rt.rect.width > 1f ? rt.rect.width : Mathf.Max(1f, mediaDisplayBaseSize.x);
        Vector2 endPos = rt.anchoredPosition;
        Vector2 startOffset = new Vector2(dir == EntryDirection.FromRight ? width : -width, 0f);

        // Same reflection test ContentZoneController runs for the cards: a
        // negative 2D determinant means the parent chain mirrors X, so the
        // anchoredPosition offset must be negated to keep the on-screen motion
        // entering from the intended side.
        RectTransform parent = rt.parent as RectTransform;
        if (parent != null)
        {
            Matrix4x4 m = parent.localToWorldMatrix;
            float det2D = m.m00 * m.m11 - m.m01 * m.m10;
            if (det2D < 0f) startOffset.x = -startOffset.x;
        }

        rt.anchoredPosition = endPos + startOffset;
        if (mediaDisplayGroup != null) mediaDisplayGroup.alpha = 0f;

        // Ease + fade follow the menu's 'Card entry animation' choice exactly
        // like the cards do: the overshoot curve with a short fade, or the
        // smooth ease with the fade stretched over the whole slide.
        Sequence seq = DOTween.Sequence()
            .Join(anim.ApplyEntryEase(rt.DOAnchorPos(endPos, anim.defaultSlideDuration)));
        if (mediaDisplayGroup != null)
            seq.Join(mediaDisplayGroup.DOFade(1f, anim.DefaultEntryFadeDuration).SetEase(anim.EntryFadeEase));

        // Once landed, drift slowly like the cards do (Ease in + fade style only).
        if (anim.IdleFloatActive)
            seq.OnComplete(() => CardIdleFloat.Begin(rt, CardEntryAnimator.Instance.idleFloat));

        mediaEntrySequence = seq;
    }

    // A fresh RenderTexture contains whatever was last in that GPU memory. The
    // video only fills it on its first presented frame, so clear it to fully
    // transparent first — worst case the display is invisible for one frame
    // instead of flashing garbage or white.
    static void ClearToTransparent(RenderTexture rt)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = previous;
    }

    // Letterbox the display rect to the media's own aspect inside the authored
    // slot, so a 16:9 clip isn't squashed into a 5:4 box and a wide logo shown
    // via {Image:} isn't stretched to fill the slot. Fit-inside, never crop.
    void FitDisplayToAspect(int videoWidth, int videoHeight)
    {
        if (mediaDisplay == null || videoWidth <= 0 || videoHeight <= 0) return;
        if (mediaDisplayBaseSize.x <= 0f || mediaDisplayBaseSize.y <= 0f) return;

        float scale = Mathf.Min(mediaDisplayBaseSize.x / videoWidth,
                                mediaDisplayBaseSize.y / videoHeight);
        mediaDisplay.rectTransform.sizeDelta = new Vector2(videoWidth * scale, videoHeight * scale);
    }

    void Awake()
    {
        if (mediaDisplay != null)
        {
            RectTransform mrt = mediaDisplay.rectTransform;
            mediaDisplayBaseSize        = mrt.sizeDelta;
            mediaDisplayBaseAnchorMin   = mrt.anchorMin;
            mediaDisplayBaseAnchorMax   = mrt.anchorMax;
            mediaDisplayBasePivot       = mrt.pivot;
            mediaDisplayBaseAnchoredPos = mrt.anchoredPosition;

            mediaDisplayGroup = mediaDisplay.GetComponent<CanvasGroup>();
            if (mediaDisplayGroup == null)
                mediaDisplayGroup = mediaDisplay.gameObject.AddComponent<CanvasGroup>();

            // Soft drop shadow sibling behind the display — self-syncing, same
            // elevation the content cards get (see MediaDisplayShadow).
            MediaDisplayShadow.Create(mediaDisplay, mediaDisplayGroup);

            mediaDisplay.gameObject.SetActive(false);
        }

        SetUpVideoPlayer();

        // Auto-find ContentZoneController if not assigned
        if (contentZoneController == null)
        {
            contentZoneController = GetComponent<ContentZoneController>();
            if (contentZoneController == null)
                contentZoneController = FindObjectOfType<ContentZoneController>();
        }

        // Auto-find or create BlackPanelController if not assigned
        if (blackPanelController == null)
        {
            blackPanelController = GetComponent<BlackPanelController>();
            if (blackPanelController == null)
                blackPanelController = FindObjectOfType<BlackPanelController>();
            if (blackPanelController == null)
                blackPanelController = gameObject.AddComponent<BlackPanelController>();
        }

        // Hand the recorded canvas to the black panel so it lands in the frame
        // the recorder actually captures (the same canvas the content cards use).
        if (blackPanelController != null && mediaCanvas != null)
            blackPanelController.SetHostCanvas(mediaCanvas);

        // Auto-find the background mood controller. Optional — {Mood:...} is a
        // no-op when it's absent.
        if (moodController == null)
            moodController = FindObjectOfType<MugsTech.Background.BackgroundMoodController>();

        // Ensure a ScreenTransitionController exists (create a TransitionDirector
        // if the scene has none, mirroring the black-panel auto-create), and host
        // its overlay on the recorder-captured canvas so transitions are recorded.
        EnsureScreenTransitionController();
    }

    // Finds or creates the ScreenTransitionController and points its overlay at the
    // captured media canvas. The overlay itself is built lazily on the first Play.
    void EnsureScreenTransitionController()
    {
        if (screenTransitionController == null)
            screenTransitionController = ScreenTransitionController.Instance;
        if (screenTransitionController == null)
            screenTransitionController = FindObjectOfType<ScreenTransitionController>();
        if (screenTransitionController == null)
        {
            GameObject go = new GameObject("TransitionDirector");
            screenTransitionController = go.AddComponent<ScreenTransitionController>();
        }

        if (screenTransitionController != null && mediaCanvas != null)
            screenTransitionController.SetHostCanvas(mediaCanvas);
    }

    void Start()
    {
        // Honor any override saved from the main menu's "Media folder" input.
        // Scenes don't share MonoBehaviour state directly, so we pass the value
        // via PlayerPrefs — same pattern as ScriptFileReader's pythonOutputFolder.
        string overrideRoot = PlayerPrefs.GetString(MediaRootFolderPrefKey, "");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            externalMediaRoot = overrideRoot;
            Debug.Log($"[MediaPresentation] External media root overridden via main menu: {externalMediaRoot}");
        }

        // One root path is all the user sets — make sure the per-kind
        // subfolders exist under it before any {Image:} / {Video:} lookup.
        if (!string.IsNullOrWhiteSpace(externalMediaRoot))
        {
            if (Directory.Exists(externalMediaRoot))
                EnsureMediaSubfolders(externalMediaRoot, AllSubfolders);
            else
                Debug.LogWarning($"[MediaPresentation] External media root does not exist: '{externalMediaRoot}' — " +
                                 $"media names will only resolve from Resources/{mediaFolderPath}.");
        }

        // Store default camera size for zoom reset
        if (mainCamera != null)
            defaultCameraSize = mainCamera.orthographicSize;

        // Start at center
        if (avatarParent != null && centerLocation != null)
        {
            avatarParent.position = centerLocation.position;
            avatarParent.rotation = centerLocation.rotation;
            currentPosition = CharacterPosition.Center;
            Debug.Log("Avatar positioned at center");
        }
    }

    // -----------------------------------------------------------------------
    // Entry Point (called by ScriptFileReader — same signature as before)
    // -----------------------------------------------------------------------

    public void ProcessScriptWithMedia(string scriptWithMarkers, AudioClip audio)
    {
        // Debug snapshot — lets you confirm what Unity actually parsed (not the
        // raw Script.txt, but whichever _timed.txt / stitched variant was loaded).
        string preview = scriptWithMarkers.Length > 400
            ? scriptWithMarkers.Substring(0, 400) + "..."
            : scriptWithMarkers;
        Debug.Log($"[MediaPresentation] Loaded script ({scriptWithMarkers.Length} chars). " +
                  $"Contains '{{Black': {scriptWithMarkers.Contains("{Black")}\n---\n{preview}\n---");

        // Folder-mode background music: the assembled voice length is known
        // right here, so kick off the playlist planning + preload NOW — it
        // decodes and loudness-measures tracks while the parsers and image
        // preloader below do their work. BeginPlaybackWhenBackgroundReady
        // waits for it before the take starts (no-op when folder mode is off).
        MugsTech.Background.BackgroundMusicPlayer.BeginFolderPreload(audio.length);

        // Parse {Timestamp:"..."} chapter markers FIRST and strip them. They are
        // pure timeline markers — never voiced, never shown, and they drive nothing
        // visual — so removing them up front keeps them out of every other parser
        // (and out of the clean text that reaches display/TTS). BeginRun() resets
        // the capture buffer so the Timestamps window reflects only THIS run.
        TimestampMarkerLog.BeginRun();
        var tsResult = ParseTimestampMarkers(scriptWithMarkers, audio.length);
        string scriptAfterTimestamps = tsResult.Item1;
        timestampMarkers = tsResult.Item2;

        // Parse transition markers FIRST. A {Transition:...} claims the other
        // state tags on its line — {Position:...}, the emotion tag, {Mood:...} and
        // any content-card tag — and strips them from the script so they DON'T also
        // fire on their own timelines. Instead they're applied together at the
        // transition's full-cover midpoint (see ApplyTransitionCover), so Mugs is
        // already repositioned and the old card is gone when the screen reveals.
        var trResult = ParseTransitionMarkers(scriptAfterTimestamps, audio.length);
        string scriptAfterTransitions = trResult.Item1;
        transitionMarkers = trResult.Item2;

        // Parse standalone {Mood:X} markers (transition-claimed ones already gone).
        var moodResult = ParseMoodMarkers(scriptAfterTransitions, audio.length);
        string scriptAfterMood = moodResult.Item1;
        moodMarkers = moodResult.Item2;

        // Parse position markers (strips {Position:X} from script)
        var posResult = ParsePositionMarkers(scriptAfterMood, audio.length);
        string scriptAfterPositions = posResult.Item1;
        positionMarkers = posResult.Item2;

        // Parse zoom markers (strips {Zoom:X} from script)
        var zoomResult = ParseZoomMarkers(scriptAfterPositions, audio.length);
        string scriptAfterZoom = zoomResult.Item1;
        zoomMarkers = zoomResult.Item2;

        // Parse black panel markers (strips {Black:duration} from script)
        var blackResult = ParseBlackPanelMarkers(scriptAfterZoom, audio.length);
        scriptAfterZoom = blackResult.Item1;
        blackPanelMarkers = blackResult.Item2;

        // Parse content card tags (strips {Headline:...}, {Quote:...}, etc.)
        var cardResult = ContentZoneTagParser.ParseContentTags(scriptAfterZoom, audio.length);
        string scriptAfterCards = cardResult.Item1;
        if (contentZoneController != null)
            contentZoneController.SetTimeline(cardResult.Item2, voiceAudio);

        // Then parse media markers (strips {Image:X} and {Video:X})
        var mediaResult = ParseMediaMarkers(scriptAfterCards, audio.length);
        string cleanScript = mediaResult.Item1;
        mediaMarkers = mediaResult.Item2;

        // Strip stage directions like [pause,T=4.7] / [sips coffee,T=6.8] —
        // baked in by the ElevenLabs pre-processor as narrative cues only.
        cleanScript = Regex.Replace(cleanScript, @"\[[^\]]*\]", "");

        // PROJECT FIX (AutoAvatarGen): decode every disk image the script
        // references NOW — the recorder hasn't started yet (it starts inside
        // BeginPlaybackWhenBackgroundReady below), so this time is free
        // instead of a mid-take dropped-frame hitch.
        PreloadReferencedImages(cardResult.Item2);

        StartCoroutine(BeginPlaybackWhenBackgroundReady(cleanScript, audio));
    }

    // Waits for the BackgroundVideoOverride hijacker to finish preparing any
    // runtime-loaded mp4 before kicking off playback. Without this, a
    // recording started while the swapped-in .mp4 is still preparing captures
    // a blank or half-decoded frame at the very start of the output. No-op if
    // the override didn't hijack anything (in which case the inspector-
    // configured VideoPlayer with PlayOnAwake handled itself before Start).
    IEnumerator BeginPlaybackWhenBackgroundReady(string cleanScript, AudioClip audio)
    {
        const float timeout = 10f;
        float elapsed = 0f;
        while (elapsed < timeout && !MugsTech.Background.BackgroundVideoOverride.AllPrepared)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (elapsed >= timeout)
            Debug.LogWarning($"[MediaPresentation] Background video(s) not ready after {timeout}s — starting anyway.");

        // Also wait for the folder-mode music preload (kicked off at the top
        // of ProcessScriptWithMedia). This must finish BEFORE the trackers
        // below start: they bound their wait-for-playback at 10s, so gating
        // inside ProcessWithExistingAudio alone would strand them if the
        // preload ran long. The preloader self-caps at 30s and flags failures
        // via MusicTakeLog — a music hiccup never wastes a render.
        const float musicTimeout = 35f;
        float musicWaited = 0f;
        while (musicWaited < musicTimeout &&
               MugsTech.Background.BackgroundMusicPlayer.FolderPreloadPending)
        {
            musicWaited += Time.unscaledDeltaTime;
            yield return null;
        }
        if (musicWaited > 0f)
            Debug.Log($"[MediaPresentation] Waited {musicWaited:F1}s for background music preload.");

        // Forward to avatar system for emotion processing (unchanged)
        avatarSystem.ProcessWithExistingAudio(cleanScript, audio);

        // Track media, positions, zoom, content cards, transitions and mood
        // against audio time
        StartCoroutine(TrackMediaByTime());
        StartCoroutine(TrackPositionsByTime());
        StartCoroutine(TrackZoomByTime());
        StartCoroutine(TrackBlackPanelByTime());
        StartCoroutine(TrackTransitionsByTime());
        StartCoroutine(TrackMoodByTime());
        StartCoroutine(TrackTimestampsByTime());

        if (contentZoneController != null)
            StartCoroutine(contentZoneController.TrackCardsByTime());
    }

    // -----------------------------------------------------------------------
    // Position Tracking (NEW — follows same pattern as emotion tracking)
    // -----------------------------------------------------------------------

    // -----------------------------------------------------------------------
    // Every tracker below waits for this before its main loop. When a recording
    // is being made, voiceAudio.Play() happens a few frames AFTER the trackers
    // are started: CrossPlatformRecorder holds playback until the video
    // encoder's frame pacing has settled (see PlayWhenCaptureWarm), so the
    // narration never lands inside the encoder's spawn-stall padding — which is
    // what kept shifting every visual ~0.7s late in the muxed file. Trackers
    // that sampled isPlaying on their first frame would see FALSE during that
    // warm-up and exit before the take began.
    // -----------------------------------------------------------------------
    IEnumerator WaitForPlaybackStart()
    {
        float waited = 0f;
        while ((voiceAudio == null || !voiceAudio.isPlaying) && waited < 10f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    IEnumerator TrackPositionsByTime()
    {
        lastTriggeredPositionMarker = -1;
        yield return WaitForPlaybackStart();

        // `|| isShowingMedia` keeps tracking alive while a trailing {Image:} /
        // {Video:} is still on screen after the narration ends, so a position
        // change authored against the final words still fires.
        while (voiceAudio.isPlaying || isShowingMedia)
        {
            float currentTime = voiceAudio.time;

            for (int i = lastTriggeredPositionMarker + 1; i < positionMarkers.Count; i++)
            {
                if (currentTime >= positionMarkers[i].triggerTime)
                {
                    var marker = positionMarkers[i];
                    Debug.Log($"Triggering position: {marker.position} at {currentTime:F2}s");

                    MoveToPosition(marker.position, marker.hardCutOverride);
                    lastTriggeredPositionMarker = i;
                }
                else
                {
                    break;
                }
            }

            yield return null;
        }
    }

    /// <summary>
    /// Moves the character to the target position.
    /// cutOverride: null = use global useHardCuts, true = force hard cut, false = force smooth.
    /// </summary>
    void MoveToPosition(CharacterPosition targetPosition, bool? cutOverride = null)
    {
        Transform target = GetTransformForPosition(targetPosition);
        if (target == null) return;

        // The presenter genuinely changing sides ends any clip on screen — that
        // beat is over. A {Position:} that repeats the current side is a no-op
        // and must NOT dismiss, or a redundant tag would cut the clip short.
        if (targetPosition != currentPosition)
            DismissActiveMedia();

        // Per-tag sound effect ({Position:Left/Right/Center}).
        TagSfxPlayer.Instance.Play(targetPosition);

        // Stop any in-progress movement
        if (movementCoroutine != null)
            StopCoroutine(movementCoroutine);

        bool doHardCut = cutOverride ?? useHardCuts;

        if (doHardCut)
        {
            // Instant snap
            if (avatarParent != null)
            {
                avatarParent.position = target.position;
                avatarParent.rotation = target.rotation;

                if (avatarSystem != null)
                    avatarSystem.SetSwayBase(target.position, target.rotation);
            }
            UpdateFacing(targetPosition);
        }
        else
        {
            // Smooth eased movement; facing flips midway through the glide
            // (inside MoveAvatar), not here, so the presenter visibly turns
            // around mid-travel instead of popping at departure.
            Transform current = GetTransformForPosition(currentPosition);
            if (current == null) current = centerLocation;
            movementCoroutine = StartCoroutine(MoveAvatar(current, target, targetPosition));
        }

        currentPosition = targetPosition;

        // Pause/resume content cards based on character position
        if (contentZoneController != null)
        {
            if (targetPosition == CharacterPosition.Center)
                contentZoneController.PauseTimeline();
            else
                contentZoneController.ResumeTimeline();
        }
    }

    /// <summary>
    /// Sets sprite flipX so the character faces toward the content zone.
    /// Left = faces right, Right = faces left, Center = faces camera (no flip).
    /// </summary>
    void UpdateFacing(CharacterPosition pos)
    {
        if (characterRenderer == null) return;

        switch (pos)
        {
            case CharacterPosition.Left:
                characterRenderer.flipX = false; // Face right toward content
                break;
            case CharacterPosition.Right:
                characterRenderer.flipX = true;  // Face left toward content
                break;
            case CharacterPosition.Center:
                characterRenderer.flipX = false; // Face camera
                break;
        }
    }

    Transform GetTransformForPosition(CharacterPosition pos)
    {
        switch (pos)
        {
            case CharacterPosition.Left: return leftLocation;
            case CharacterPosition.Right: return rightLocation;
            case CharacterPosition.Center: return centerLocation;
            default: return centerLocation;
        }
    }

    // -----------------------------------------------------------------------
    // Zoom Tracking — follows same pattern as position tracking
    // -----------------------------------------------------------------------

    IEnumerator TrackZoomByTime()
    {
        lastTriggeredZoomMarker = -1;
        yield return WaitForPlaybackStart();

        // `|| isShowingMedia` keeps tracking alive while a trailing {Image:} /
        // {Video:} is still on screen after the narration ends, so a zoom
        // authored against the final words still fires.
        while (voiceAudio.isPlaying || isShowingMedia)
        {
            float currentTime = voiceAudio.time;

            for (int i = lastTriggeredZoomMarker + 1; i < zoomMarkers.Count; i++)
            {
                if (currentTime >= zoomMarkers[i].triggerTime)
                {
                    var marker = zoomMarkers[i];
                    string mods = (marker.cut ? " (cut)" : "") +
                                  (marker.holdDuration > 0f ? $" hold {marker.holdDuration:F2}s" : "");
                    Debug.Log($"Triggering zoom: {marker.zoomType}{mods} at {currentTime:F2}s");

                    ApplyZoom(marker.zoomType, marker.cut, marker.holdDuration);
                    lastTriggeredZoomMarker = i;
                }
                else
                {
                    break;
                }
            }

            yield return null;
        }

        // A {Zoom:ExtremeIn} whose {Zoom:ExtremeOut} was never written holds the
        // close-up for the rest of the take. That's visually obvious but silent in
        // the logs, so name it explicitly here.
        if (!float.IsNaN(extremeRestoreSize))
            Debug.LogWarning("[Zoom] Playback ended with the extreme close-up still held — " +
                             "a {Zoom:ExtremeIn} has no matching {Zoom:ExtremeOut}.");
    }

    void ApplyZoom(ZoomType type, bool cut = false, float holdDuration = 0f)
    {
        if (mainCamera == null) return;

        // A run already at its last step has nowhere left to lean. Drop the tag
        // before it touches anything — no sound, and the glide in progress and
        // the run's release timer carry on as if it weren't there.
        if (type == ZoomType.In && zoomStep >= MaxZoomSteps)
        {
            Debug.Log($"[Zoom] {{Zoom:In}} ignored — the run is already at step {MaxZoomSteps} " +
                      $"of {MaxZoomSteps}. Release it ({{Zoom:Out}}, ,D=, a transition, black or a " +
                      "big card) before leaning in again.");
            return;
        }

        // Per-tag sound effect ({Zoom:In/Out/Reset/Pullback/Extreme}).
        TagSfxPlayer.Instance.Play(type);

        // Read before StopZoomEase clears it: a camera still gliding home from
        // a released run counts as part of that run for an ExtremeIn below.
        bool glidingHome = zoomCoroutine != null && zoomEaseTarget == defaultCameraSize;

        // Stop any in-progress zoom + any pending ,D= release (an In restarts
        // it below). Also pop the pullback mask off — if the new zoom is itself
        // a Pullback, AnimatePullback will switch it back on at the start.
        StopZoomEase();
        CancelZoomRelease();
        SetPullbackMaskActive(false);

        // An ordinary zoom supersedes a held close-up. Those paths only drive
        // orthographicSize, so without this they'd inherit the close-up's
        // re-centred camera and frame the face at a moderate zoom. Hand the
        // camera back first; a later ExtremeOut then warns rather than snapping
        // to a stale size.
        if (type != ZoomType.ExtremeIn && type != ZoomType.ExtremeOut && !float.IsNaN(extremeRestoreSize))
        {
            mainCamera.transform.position = extremeRestoreCameraPos;
            extremeRestoreSize = float.NaN;
        }

        // Pullback is a self-contained multi-stage effect — handle it on its own.
        // It takes the camera wide and cuts back to default, so it ends a run.
        if (type == ZoomType.Pullback)
        {
            ClearZoomRun("{Zoom:Pullback}");
            float drift = holdDuration > 0f ? holdDuration : pullbackDuration;
            zoomCoroutine = StartCoroutine(AnimatePullback(drift));
            return;
        }

        // The extreme close-up is a matched pair, not a timed effect: the author
        // places the out tag where the beat ends, exactly like a {Video:} is ended
        // by the next beat rather than by a guessed number of seconds. Both edges
        // are a single assignment on a single frame — no coroutine, so there is no
        // ramp to accidentally ease.
        if (type == ZoomType.ExtremeIn)
        {
            // A jump cut, so every card and media item goes with it — on this
            // frame, no fade — and stays gone through ExtremeOut.
            CutAwayVisualsForCloseUp();

            // Only capture when nothing is held, so a doubled ExtremeIn can't
            // overwrite the real framing with the already-zoomed one.
            if (float.IsNaN(extremeRestoreSize))
            {
                // The close-up ends a {Zoom:In} run, so ExtremeOut comes back
                // to normal framing, not to the push-in. A run only ever drives
                // orthographicSize (an In hands a held close-up's camera back
                // first), so the position on screen now is already the normal
                // one. A run still gliding home counts too — capturing its
                // mid-glide size would strand the camera there after ExtremeOut.
                bool endsRun = zoomStep > 0 || glidingHome;
                extremeRestoreSize      = endsRun ? defaultCameraSize : mainCamera.orthographicSize;
                extremeRestoreCameraPos = mainCamera.transform.position;
                if (endsRun)
                    Debug.Log($"[Zoom] ExtremeIn ended the zoom run (step {zoomStep}) — " +
                              "ExtremeOut returns to normal framing.");
            }
            ClearZoomRun(null); // logged just above

            mainCamera.orthographicSize = defaultCameraSize / Mathf.Max(1.01f, extremeZoomMultiplier);

            // Put the close framing on the face rather than on whatever happens to
            // sit at screen centre. Reads the position transform the character was
            // moved to, so the crop is identical every time he punches from a given
            // position — it is not measured off his sprite bounds or scale.
            if (extremeZoomFollowsCharacter && avatarParent != null)
            {
                Vector3 focus = avatarParent.position;
                mainCamera.transform.position = new Vector3(
                    focus.x + extremeZoomFaceOffset.x,
                    focus.y + extremeZoomFaceOffset.y,
                    mainCamera.transform.position.z);
            }

            // Ground truth for framing the close-up. Everything here is measured
            // live, so it supersedes any offset derived from the scene asset.
            {
                float half = mainCamera.orthographicSize;
                float camY = mainCamera.transform.position.y;
                string head = characterRenderer != null
                    ? $"sprite world Y {characterRenderer.bounds.min.y:F2}..{characterRenderer.bounds.max.y:F2}"
                    : "characterRenderer not assigned";
                Debug.Log($"[Zoom] ExtremeIn — avatarParent Y {avatarParent?.position.y ?? 0f:F2}, " +
                          $"offset Y {extremeZoomFaceOffset.y:F2}, camera Y {camY:F2}, " +
                          $"orthoSize {half:F2} (frame Y {camY - half:F2}..{camY + half:F2}), {head}");
            }
            return;
        }

        if (type == ZoomType.ExtremeOut)
        {
            if (float.IsNaN(extremeRestoreSize))
            {
                Debug.LogWarning("[Zoom] {Zoom:ExtremeOut} fired with no close-up held — " +
                                 "the matching {Zoom:ExtremeIn} is missing or was superseded. Ignored.");
                return;
            }

            mainCamera.orthographicSize   = extremeRestoreSize;
            mainCamera.transform.position = extremeRestoreCameraPos;
            extremeRestoreSize            = float.NaN;
            return;
        }

        float targetSize;

        switch (type)
        {
            case ZoomType.In:
                // One step further in, from wherever the camera is now — the
                // glide above was stopped, so a step that lands mid-glide
                // carries straight on from that frame.
                zoomStep++;
                targetSize = defaultCameraSize / ZoomStepMultiplier(zoomStep);
                if (holdDuration > 0f) zoomRunHold = holdDuration;
                Debug.Log($"[Zoom] In — step {zoomStep} of {MaxZoomSteps} " +
                          $"({ZoomStepMultiplier(zoomStep) * 100f:F0}%)" +
                          (zoomRunHold > 0f ? $", run releases in {zoomRunHold:F2}s" : ""));
                break;
            case ZoomType.Out:
                // All the way back from any step, in one glide.
                ClearZoomRun("{Zoom:Out}");
                targetSize = defaultCameraSize;
                break;
            case ZoomType.Reset:
                // Reset is always an instant snap regardless of the cut flag —
                // that's its whole purpose.
                ClearZoomRun("{Zoom:Reset}");
                mainCamera.orthographicSize = defaultCameraSize;
                return;
            default:
                return;
        }

        if (cut)
            mainCamera.orthographicSize = targetSize;
        else
            StartZoomEase(targetSize);

        // ,D= releases the whole run, not just this step. Every step restarts
        // the countdown (a later D= replaces its length), so the release
        // always lands that long after the latest push-in.
        if (type == ZoomType.In && zoomRunHold > 0f)
            pendingResetCoroutine = StartCoroutine(ReleaseZoomRunAfter(zoomRunHold));
    }

    // Each step never sits wider than the one before it, so a mis-set
    // Inspector value can't turn a push-in into a pull-out.
    float ZoomStepMultiplier(int step)
    {
        float m = Mathf.Max(1.01f, zoomStep1Multiplier);
        if (step >= 2) m = Mathf.Max(m, zoomStep2Multiplier);
        if (step >= 3) m = Mathf.Max(m, zoomStep3Multiplier);
        return m;
    }

    /// <summary>
    /// Ends the {Zoom:In} run, if one is active: the step goes back to 0, the
    /// run's ,D= timer is cancelled and the camera returns to normal framing.
    /// <paramref name="hiddenBehindCover"/> = true when something opaque covers
    /// the whole frame on this very frame (black panel, transition cover, a
    /// big headline's panel) — the camera snaps back unseen behind it.
    /// Otherwise (BigText, BigMedia, BigImage — the scene stays visible) it
    /// glides back over zoomDuration as the visual appears. No-op when no run
    /// is active, so it never disturbs a held close-up or a pullback.
    /// </summary>
    public void EndZoomRun(string reason, bool hiddenBehindCover)
    {
        if (mainCamera == null || zoomStep == 0) return;

        Debug.Log($"[Zoom] {reason} ended the zoom run at step {zoomStep} — " +
                  (hiddenBehindCover ? "snapped back behind it." : $"gliding back over {zoomDuration:F2}s."));

        ClearZoomRun(null); // logged just above
        CancelZoomRelease();

        if (hiddenBehindCover)
        {
            StopZoomEase();
            mainCamera.orthographicSize = defaultCameraSize;
        }
        else
        {
            StartZoomEase(defaultCameraSize);
        }
    }

    // Step back to 0 and forget the run's ,D= length. Callers own the camera
    // and the release timer.
    void ClearZoomRun(string reason)
    {
        if (zoomStep > 0 && reason != null)
            Debug.Log($"[Zoom] Run ended at step {zoomStep} by {reason}.");
        zoomStep = 0;
        zoomRunHold = 0f;
    }

    // A side card, {Image:} or {Video:} isn't meant to come in while the camera
    // is leaned in — the script should release the run first. Logged only; the
    // visual still shows as normal.
    public void WarnIfZoomRunActive(string what)
    {
        if (zoomStep > 0)
            Debug.LogWarning($"[Zoom] {what} came due during a {{Zoom:In}} run (step {zoomStep}) — " +
                             "release the run first ({Zoom:Out}, a transition, black or a big card). " +
                             "Shown anyway.");
    }

    // Pullback: snap to a wide framing, drift slightly wider over `drift` seconds
    // (linear so the motion reads as a steady push-out), then jump back to default.
    // The pullbackBorderPlanes (assigned in the Inspector) crop anything outside
    // the original camera framing so the output reads as a video shrinking on
    // a black canvas.
    IEnumerator AnimatePullback(float drift)
    {
        float startSize = defaultCameraSize * Mathf.Max(1.01f, pullbackStartMultiplier);
        float endSize   = defaultCameraSize * Mathf.Max(pullbackStartMultiplier, pullbackEndMultiplier);

        mainCamera.orthographicSize = startSize;
        SetPullbackMaskActive(true);

        float elapsed = 0f;
        while (elapsed < drift)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, drift));
            mainCamera.orthographicSize = Mathf.Lerp(startSize, endSize, t);
            yield return null;
        }

        mainCamera.orthographicSize = defaultCameraSize;
        SetPullbackMaskActive(false);
    }

    void SetPullbackMaskActive(bool active)
    {
        if (pullbackBorderPlanes == null) return;
        for (int i = 0; i < pullbackBorderPlanes.Length; i++)
        {
            GameObject go = pullbackBorderPlanes[i];
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }
    }

    // The run's ,D= release. Always a glide back, even when the steps were
    // ,Cut — a step applied under a transition cover is forced to a cut, and
    // a snap back seconds later would land in plain view.
    IEnumerator ReleaseZoomRunAfter(float delay)
    {
        yield return new WaitForSeconds(delay);

        pendingResetCoroutine = null; // EndZoomRun must not stop this coroutine
        EndZoomRun("the ,D= timer", hiddenBehindCover: false);
    }

    void CancelZoomRelease()
    {
        if (pendingResetCoroutine != null) StopCoroutine(pendingResetCoroutine);
        pendingResetCoroutine = null;
    }

    // Starts an AnimateZoom glide from the camera's current size, replacing
    // any glide (or pullback) already running.
    void StartZoomEase(float targetSize)
    {
        StopZoomEase();
        zoomEaseTarget = targetSize;
        zoomCoroutine = StartCoroutine(AnimateZoom(targetSize));
    }

    void StopZoomEase()
    {
        if (zoomCoroutine != null) StopCoroutine(zoomCoroutine);
        zoomCoroutine = null;
        zoomEaseTarget = float.NaN;
    }

    // Ease-in-out glide shared by every animated zoom (each In step, Out, the
    // ,D= release and a run ended by a big card): the camera accelerates
    // gently out of its current framing and decelerates into the target, so
    // the push reads as one slow, deliberate motion. (The earlier ease-out
    // cubic put half the travel in the first fifth of the timeline and still
    // read as a snap on camera; the card-entry overshoot curve before it was
    // worse.) Start it through StartZoomEase.
    IEnumerator AnimateZoom(float targetSize)
    {
        float startSize = mainCamera.orthographicSize;
        float elapsed = 0f;

        float duration = Mathf.Max(0.0001f, zoomDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float p = Mathf.Clamp01(elapsed / duration);
            float t = p < 0.5f                       // ease-in-out cubic
                ? 4f * p * p * p
                : 1f - Mathf.Pow(-2f * p + 2f, 3f) / 2f;
            mainCamera.orthographicSize = Mathf.Lerp(startSize, targetSize, t);
            yield return null;
        }

        mainCamera.orthographicSize = targetSize;
        zoomCoroutine = null;
        zoomEaseTarget = float.NaN;
        Debug.Log($"Zoom complete: camera size = {targetSize:F2}");
    }

    // -----------------------------------------------------------------------
    // Parse Zoom Markers
    // Format: {Zoom:<In|Out|Reset|Pullback|ExtremeIn|ExtremeOut>[,Cut][,D=seconds][,T=seconds]}
    //   Cut       — instant snap instead of animating. (Ignored on Reset, which
    //               is always a snap, and on Pullback / ExtremeIn / ExtremeOut,
    //               which manage their own cuts.)
    //   D=seconds — In: release the whole {Zoom:In} run (glide back to
    //               default) this many seconds after the latest step.
    //               Pullback: overrides the slow-drift duration.
    //               Ignored for Out / Reset / ExtremeIn / ExtremeOut — the
    //               close-up's length is set by where ExtremeOut is placed.
    //   T=seconds — exact trigger time (appended by the ElevenLabs pre-processor).
    // Trailing options are order-independent.
    // -----------------------------------------------------------------------

    (string, List<ZoomMarkerData>) ParseZoomMarkers(string script, float audioDuration)
    {
        List<ZoomMarkerData> markerList = new List<ZoomMarkerData>();
        string clean = script;

        // Group 1 = type word; Group 2 = the rest of the comma-separated tokens
        // (including leading commas), parsed by hand below for order independence.
        Regex regex = new Regex(@"\{Zoom:(\w+)((?:,[^,}]+)*)\}");
        MatchCollection matches = regex.Matches(script);

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = Mathf.Max(1, scriptWithoutMarkers.Length);

        foreach (Match match in matches)
        {
            // Defaults
            float markerTime    = -1f;
            float holdDuration  = 0f;
            bool  cut           = false;

            // Walk the trailing tokens.
            string tail = match.Groups[2].Value;
            if (!string.IsNullOrEmpty(tail))
            {
                string[] tokens = tail.Split(',');
                foreach (string raw in tokens)
                {
                    string tok = raw.Trim();
                    if (tok.Length == 0) continue;

                    if (tok.Equals("Cut", System.StringComparison.OrdinalIgnoreCase))
                    {
                        cut = true;
                    }
                    else if (tok.StartsWith("T=", System.StringComparison.OrdinalIgnoreCase))
                    {
                        float.TryParse(tok.Substring(2),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out markerTime);
                    }
                    else if (tok.StartsWith("D=", System.StringComparison.OrdinalIgnoreCase))
                    {
                        float.TryParse(tok.Substring(2),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out holdDuration);
                    }
                    else
                    {
                        Debug.LogWarning($"Unknown zoom option '{tok}' in '{match.Value}' — ignored.");
                    }
                }
            }

            // Fall back to character-position estimate if no T= was supplied.
            if (markerTime < 0f)
            {
                string textBeforeMarker = script.Substring(0, match.Index);
                string cleanTextBefore = regex.Replace(textBeforeMarker, "");
                markerTime = (cleanTextBefore.Length / (float)totalChars) * audioDuration;
            }

            string zoomStr = match.Groups[1].Value.ToLower();
            ZoomType zoomType = ZoomType.Reset;

            switch (zoomStr)
            {
                case "in": zoomType = ZoomType.In; break;
                case "out": zoomType = ZoomType.Out; break;
                case "reset": zoomType = ZoomType.Reset; break;
                case "pullback": zoomType = ZoomType.Pullback; break;
                case "extremein": zoomType = ZoomType.ExtremeIn; break;
                case "extremeout": zoomType = ZoomType.ExtremeOut; break;
                default:
                    Debug.LogWarning($"Unknown zoom type: {zoomStr}, defaulting to Reset");
                    break;
            }

            markerList.Add(new ZoomMarkerData
            {
                triggerTime  = markerTime,
                zoomType     = zoomType,
                cut          = cut,
                holdDuration = holdDuration
            });

            string mods = (cut ? " cut" : "") + (holdDuration > 0f ? $" hold={holdDuration:F2}s" : "");
            Debug.Log($"Zoom marker '{zoomType}'{mods} will trigger at {markerTime:F2}s");

            clean = clean.Replace(match.Value, "");
        }

        return (clean, markerList);
    }

    // -----------------------------------------------------------------------
    // Black Panel Tracking — fullscreen jump-cut overlay
    // Format: {Black:duration}  (optional ,T=X.XXX or ,D=duration) — timed cut
    //         {Black:Start} … {Black:End}  — held pair: the panel stays up
    //         until the End tag's exact narration time, the same in-tag/out-tag
    //         principle as {Video:name}…{Video:End}. Start/On/In open,
    //         End/Stop/Out/Off close (Start/End canonical, rest typo-tolerance).
    // -----------------------------------------------------------------------

    // Hold used when a {Black:Start} is flushed AFTER the narration ends —
    // nothing spoken remains to place its {Black:End} against, so it plays as
    // a short timed cut instead (the guide's 2-3s black default).
    const float EndOfAudioBlackSeconds = 2f;

    IEnumerator TrackBlackPanelByTime()
    {
        lastTriggeredBlackPanelMarker = -1;

        Debug.Log($"[Black] TrackBlackPanelByTime started. markers={blackPanelMarkers?.Count ?? 0}, audio playing={voiceAudio != null && voiceAudio.isPlaying}");

        // Playback may start several frames late (recorder warm-up) — the old
        // one-frame latch isn't enough.
        yield return WaitForPlaybackStart();

        if (blackPanelMarkers == null || blackPanelMarkers.Count == 0)
        {
            Debug.Log("[Black] No black-panel markers to track — coroutine exiting.");
            yield break;
        }

        // `|| isShowingMedia` keeps tracking alive while a trailing {Image:} /
        // {Video:} is still on screen, so the end-of-audio flush below runs
        // only once nothing is left to display.
        while (voiceAudio != null && (voiceAudio.isPlaying || isShowingMedia))
        {
            float currentTime = voiceAudio.time;

            for (int i = lastTriggeredBlackPanelMarker + 1; i < blackPanelMarkers.Count; i++)
            {
                if (currentTime >= blackPanelMarkers[i].triggerTime)
                {
                    var marker = blackPanelMarkers[i];

                    if (marker.endsPanel)
                    {
                        // {Black:End} — jump-cut the held panel out exactly
                        // here. Harmless when nothing is showing (like a stray
                        // {Video:End}). Silent — the cut IN already got the sfx.
                        Debug.Log($"[Black] {{Black:End}} at {currentTime:F2}s");
                        if (blackPanelController != null)
                            blackPanelController.HideImmediate();
                    }
                    else
                    {
                        Debug.Log(marker.holdsOpen
                            ? $"[Black] Triggering held black panel at {currentTime:F2}s (until {{Black:End}})"
                            : $"[Black] Triggering black panel for {marker.duration:F2}s at {currentTime:F2}s");

                        // Per-tag sound effect ({Black}).
                        TagSfxPlayer.Instance.Play(TagSfxEvent.Black);

                        if (blackPanelController == null)
                            Debug.LogError("[Black] blackPanelController is NULL — cannot show panel. Assign it in the Inspector.");
                        else if (marker.holdsOpen)
                            blackPanelController.ShowHeld();
                        else
                            blackPanelController.Show(marker.duration);

                        EndZoomRunBehindBlack();
                    }

                    lastTriggeredBlackPanelMarker = i;
                }
                else
                {
                    break;
                }
            }

            yield return null;
        }

        // Audio finished. A {Black} placed on the script's final word is clamped
        // to the clip-end time, and the loop above stops the instant playback
        // ends — so without this it would never fire. Flush any still-pending
        // markers now (the recorder holds the take open to capture them). A
        // pending {Black:Start} can't be held — nothing after the narration
        // will ever close it — so it plays as a short timed cut instead, and
        // its own pending {Black:End} is consumed by that conversion.
        bool convertedHeldStart = false;
        for (int i = lastTriggeredBlackPanelMarker + 1; i < blackPanelMarkers.Count; i++)
        {
            var marker = blackPanelMarkers[i];

            if (marker.endsPanel)
            {
                if (convertedHeldStart)
                    convertedHeldStart = false;   // its Start already plays as a timed cut
                else if (blackPanelController != null)
                    blackPanelController.HideImmediate();   // closes a panel held from playback
                lastTriggeredBlackPanelMarker = i;
                continue;
            }

            float hold = marker.holdsOpen ? EndOfAudioBlackSeconds : marker.duration;
            if (marker.holdsOpen) convertedHeldStart = true;

            Debug.Log($"[Black] Flushing end-of-audio black panel for {hold:F2}s");
            TagSfxPlayer.Instance.Play(TagSfxEvent.Black);
            if (blackPanelController != null)
                blackPanelController.Show(hold);
            EndZoomRunBehindBlack();
            lastTriggeredBlackPanelMarker = i;
        }

        // Safety net: a {Black:Start} whose {Black:End} was never written keeps
        // the panel up forever, and the recorder holds the take open while it
        // shows — close it so the take can finish (same net as an unclosed
        // BigText stack).
        if (blackPanelController != null && blackPanelController.IsHeldOpen)
        {
            Debug.LogWarning("[Black] Narration ended with the black panel still held — " +
                             "missing {Black:End}. Closing it so the take can finish.");
            blackPanelController.HideImmediate();
        }

        Debug.Log("[Black] TrackBlackPanelByTime loop ended (audio no longer playing).");
    }

    // {Black:N} / {Black:Start} ends a {Zoom:In} run. The panel jump-cuts in
    // fully opaque on this frame, so the camera snaps back unseen behind it.
    void EndZoomRunBehindBlack()
    {
        if (blackPanelController != null && blackPanelController.IsShowing)
            EndZoomRun("{Black}", hiddenBehindCover: true);
    }

    (string, List<BlackPanelMarkerData>) ParseBlackPanelMarkers(string script, float audioDuration)
    {
        List<BlackPanelMarkerData> markerList = new List<BlackPanelMarkerData>();
        string clean = script;

        // Accepts: {Black:3}, {Black:D=3}, {Black:3,T=4.5}, {Black:T=4.5,D=3}, {Black:D=3,T=4.5}
        // — and the held pair form {Black:Start} … {Black:End} (group 5 keyword +
        // optional baked ,T= in group 6). Start/End are canonical; On/In and
        // Stop/Out/Off are typo-tolerated synonyms, matched case-insensitively.
        Regex regex = new Regex(
            @"\{Black:(?:(?:T=(\d+(?:\.\d+)?),)?(?:D=)?(\d+(?:\.\d+)?)|(?:D=)?(\d+(?:\.\d+)?)(?:,T=(\d+(?:\.\d+)?))?|(Start|On|In|End|Stop|Out|Off)(?:,T=(\d+(?:\.\d+)?))?)\}",
            RegexOptions.IgnoreCase);
        MatchCollection matches = regex.Matches(script);

        // Also run a very loose probe — if the script contains "{Black" at all
        // but the strict regex didn't match, log the raw text so we can see
        // what form the marker actually took in _timed.txt.
        if (matches.Count == 0 && script.Contains("{Black"))
        {
            int idx = script.IndexOf("{Black", System.StringComparison.Ordinal);
            int end = script.IndexOf('}', idx);
            string sample = end > idx ? script.Substring(idx, System.Math.Min(end - idx + 1, 80)) : script.Substring(idx, System.Math.Min(40, script.Length - idx));
            Debug.LogWarning($"[Black] Found literal '{{Black' in script but strict regex did not match. Raw: \"{sample}\" — check the marker form.");
        }

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = Mathf.Max(1, scriptWithoutMarkers.Length);

        foreach (Match match in matches)
        {
            // T= can be group 1 (T-first form), group 4 (duration-first form),
            // or group 6 (keyword pair form).
            Group tsGroup = match.Groups[1].Success ? match.Groups[1]
                          : match.Groups[4].Success ? match.Groups[4]
                          : match.Groups[6];
            float markerTime = TryParseTimestamp(tsGroup);
            if (markerTime < 0f)
            {
                string textBeforeMarker = script.Substring(0, match.Index);
                string cleanTextBefore = regex.Replace(textBeforeMarker, "");
                markerTime = (cleanTextBefore.Length / (float)totalChars) * audioDuration;
            }

            // Held pair form — {Black:Start} / {Black:End} (no duration).
            if (match.Groups[5].Success)
            {
                string kw = match.Groups[5].Value.ToLowerInvariant();
                bool ends = kw == "end" || kw == "stop" || kw == "out" || kw == "off";

                markerList.Add(new BlackPanelMarkerData
                {
                    triggerTime = markerTime,
                    duration = 0f,
                    holdsOpen = !ends,
                    endsPanel = ends
                });

                Debug.Log($"[Black] Parsed marker \"{match.Value}\" — " +
                          $"{(ends ? "closes the held panel" : "opens a held panel")} at {markerTime:F2}s");

                clean = clean.Replace(match.Value, "");
                continue;
            }

            string durStr = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            float duration = float.Parse(durStr, System.Globalization.CultureInfo.InvariantCulture);

            markerList.Add(new BlackPanelMarkerData
            {
                triggerTime = markerTime,
                duration = duration
            });

            Debug.Log($"[Black] Parsed marker \"{match.Value}\" — trigger at {markerTime:F2}s for {duration:F2}s");

            clean = clean.Replace(match.Value, "");
        }

        Debug.Log($"[Black] ParseBlackPanelMarkers found {markerList.Count} marker(s). blackPanelController={(blackPanelController != null ? blackPanelController.name : "NULL")}");

        return (clean, markerList);
    }

    // -----------------------------------------------------------------------
    // Media Tracking (unchanged logic)
    // -----------------------------------------------------------------------

    IEnumerator TrackMediaByTime()
    {
        lastTriggeredMediaMarker = -1;
        yield return WaitForPlaybackStart();

        while (voiceAudio.isPlaying || isShowingMedia)
        {
            if (!isShowingMedia)
            {
                // Skip media if a content card is currently active
                if (contentZoneController != null && contentZoneController.IsCardActive)
                {
                    yield return null;
                    continue;
                }

                float currentTime = voiceAudio.time;

                for (int i = lastTriggeredMediaMarker + 1; i < mediaMarkers.Count; i++)
                {
                    if (currentTime >= mediaMarkers[i].triggerTime)
                    {
                        // {Video:End} / {Image:End} — the media it closes
                        // already broke out of its display loop the frame this
                        // marker came due (NextMediaMarkerDue); there is
                        // nothing to show here.
                        if (mediaMarkers[i].endsMedia)
                        {
                            Debug.Log($"{{{(mediaMarkers[i].mediaType == MediaType.IMAGE ? "Image" : "Video")}:End}} consumed at {currentTime:F2}s");
                            lastTriggeredMediaMarker = i;
                            continue;
                        }

                        if (BlockedByCloseUp(mediaMarkers[i]))
                        {
                            lastTriggeredMediaMarker = i;
                            continue;
                        }

                        Debug.Log($"Triggering media: {mediaMarkers[i].mediaName} at {currentTime:F2}s");

                        if (currentMediaCoroutine != null)
                            StopCoroutine(currentMediaCoroutine);

                        currentMediaCoroutine = StartCoroutine(ShowMedia(mediaMarkers[i]));
                        lastTriggeredMediaMarker = i;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            yield return null;
        }

        // Audio finished. Flush trailing media clamped to the clip-end time
        // (same end-of-audio race as cards/black). Videos are included now that
        // they no longer pause and resume the narration.
        if (!isShowingMedia)
        {
            for (int i = lastTriggeredMediaMarker + 1; i < mediaMarkers.Count; i++)
            {
                // A trailing {Video:End} has nothing left to close — skip it.
                if (mediaMarkers[i].endsMedia || BlockedByCloseUp(mediaMarkers[i]))
                {
                    lastTriggeredMediaMarker = i;
                    continue;
                }
                Debug.Log($"Flushing end-of-audio media: {mediaMarkers[i].mediaName}");
                if (currentMediaCoroutine != null)
                    StopCoroutine(currentMediaCoroutine);
                currentMediaCoroutine = StartCoroutine(ShowMedia(mediaMarkers[i]));
                lastTriggeredMediaMarker = i;
            }
        }
    }

    // Nothing is shown over the extreme close-up — scripts never place media
    // between ExtremeIn and ExtremeOut, so one that comes due there is a script
    // mistake: skip it rather than put it over his face.
    // Also blocks one due a frame or two before the cut, which would only
    // flash up and be cut away.
    bool BlockedByCloseUp(MediaMarkerData marker)
    {
        if (!IsCloseUpHeld && !IsCloseUpCutImminent) return false;
        string tag = marker.mediaType == MediaType.IMAGE ? "Image" : "Video";
        Debug.LogWarning($"{{{tag}:{marker.mediaName}}} came due on a {{Zoom:ExtremeIn}} " +
                         "close-up — blocked. Move it outside the ExtremeIn…ExtremeOut pair.");
        return true;
    }

    // -----------------------------------------------------------------------
    // Show Media (simplified — no longer moves avatar, position markers do that)
    // -----------------------------------------------------------------------

    IEnumerator ShowMedia(MediaMarkerData marker)
    {
        isShowingMedia = true;

        WarnIfZoomRunActive($"{{{(marker.mediaType == MediaType.IMAGE ? "Image" : "Video")}:{marker.mediaName}}}");

        // Per-tag sound effect ({Image}/{Video}) — fires as the media appears,
        // on its own AudioSource.
        TagSfxPlayer.Instance.Play(marker.mediaType);

        // A {Video:} used to Pause() the narration for the length of its clip
        // and resume afterwards. It no longer does — the clip is a silent
        // overlay and the narration plays straight through it, so the audio
        // timeline (and every T= derived from it) is never interrupted.
        yield return StartCoroutine(DisplayMedia(marker));

        isShowingMedia = false;
    }

    // -----------------------------------------------------------------------
    // Move Avatar (same easing as your original)
    // -----------------------------------------------------------------------

    IEnumerator MoveAvatar(Transform currentLocation, Transform targetLocation, CharacterPosition targetPosition)
    {
        if (avatarParent == null || currentLocation == null || targetLocation == null)
        {
            UpdateFacing(targetPosition);
            yield break;
        }

        float time = 0f;
        Vector3 startPos = currentLocation.position;
        Vector3 targetPos = targetLocation.position;
        Quaternion startRot = currentLocation.rotation;
        Quaternion targetRot = targetLocation.rotation;
        bool turnedAround = false;

        Debug.Log($"Moving from {currentLocation.name} to {targetLocation.name}");

        while (time < transitionDuration)
        {
            float t = EaseInOutQuart(time / transitionDuration);

            // Keep the departure facing for the first half of the glide, then
            // turn around toward the new side. If the move is interrupted by a
            // new {Position:} before the midpoint, that move sets its own facing.
            if (!turnedAround && t >= 0.5f)
            {
                UpdateFacing(targetPosition);
                turnedAround = true;
            }

            avatarParent.position = Vector3.Lerp(startPos, targetPos, t);
            avatarParent.rotation = Quaternion.Slerp(startRot, targetRot, t);

            time += Time.deltaTime;
            yield return null;
        }

        if (!turnedAround)
            UpdateFacing(targetPosition);

        avatarParent.position = targetPos;
        avatarParent.rotation = targetRot;

        // Update sway base so idle sway works at new position
        if (avatarSystem != null)
            avatarSystem.SetSwayBase(targetPos, targetRot);

        Debug.Log($"Avatar reached {targetLocation.name}");
    }

    float EaseInOutQuart(float x)
    {
        return x < 0.5f ? 8f * x * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 4f) / 2f;
    }

    // -----------------------------------------------------------------------
    // Display Media (unchanged)
    // -----------------------------------------------------------------------

    IEnumerator DisplayMedia(MediaMarkerData marker)
    {
        // NOTE: mediaDisplay is deliberately NOT activated here. A RawImage with
        // no texture draws as a solid white quad, and everything below it —
        // resolving the file, spinning up the VideoPlayer, Prepare() — takes
        // frames. Activating up front is what flashed a white box before each
        // clip. Each branch calls ShowDisplay() once it has a real texture.
        Texture2D loadedDiskTexture = null;
        int cutCount = closeUpCutCount;

        if (marker.mediaType == MediaType.IMAGE)
        {
            Texture2D image = null;

            // PROJECT FIX (AutoAvatarGen): cache first — the image was decoded
            // before the take started. Cache hits belong to the cache:
            // loadedDiskTexture stays null so the teardown below never
            // destroys them (the same image can show again later).
            if (marker.mediaName != null &&
                preloadedImages.TryGetValue(marker.mediaName, out Texture2D preloaded) && preloaded != null)
            {
                image = preloaded;
            }
            else
            {
                string diskPath = ResolveImagePath(marker.mediaName);
                if (diskPath != null)
                {
                    image = LoadTextureFromDisk(diskPath);
                    if (image != null)
                    {
                        loadedDiskTexture = image;
                        Debug.Log($"Loaded image from disk: {diskPath}");
                    }
                }
            }

            if (image == null)
                image = Resources.Load<Texture2D>($"{mediaFolderPath}/{marker.mediaName}");

            if (image != null)
            {
                mediaDisplay.texture = image;
                FitDisplayToAspect(image.width, image.height);
                videoPlayer.gameObject.SetActive(false);
                ShowDisplay(marker);

                // A duration-less {Image:} reaches here as 0 through the TTS
                // pre-processors (they stamp D=0), not as the parser's 3s
                // fallback — apply the documented 3s default here so a
                // side-only tag like {Image:name,Right} doesn't flash for a
                // single frame.
                float hold = marker.displayDuration > 0f
                    ? marker.displayDuration : DefaultMediaSeconds;

                if (marker.holdsOpen)
                {
                    // Held image ({Image:name,Start}…{Image:End}) — no fixed
                    // lifetime. It lives until its End tag or the next media
                    // marker arrives (both surface as NextMediaMarkerDue), or
                    // the beat ends another way (position change / content
                    // card → dismiss token). Once the narration is over it
                    // holds the default beat, then closes itself so an
                    // unclosed pair can't keep the recorder open forever.
                    Debug.Log($"Displaying held image: {marker.mediaName} " +
                              "(until {Image:End} or the next beat)");

                    int   token       = mediaDismissToken;
                    float heldElapsed = 0f;
                    while (true)
                    {
                        if (closeUpCutCount != cutCount) break;  // {Zoom:ExtremeIn} cut it away

                        // An end that lands right on an ExtremeIn waits for the
                        // cut, so the image goes on the camera snap's frame.
                        if (!IsCloseUpCutImminent)
                        {
                            if (mediaDismissToken != token) break;   // position change / card
                            if (NextMediaMarkerDue())       break;   // {Image:End} or next media
                        }

                        // Narration over (an End tag on the final words can no
                        // longer fire — same end-of-audio race as the cards):
                        // hold the default beat, then close so an open pair
                        // can't keep the recorder waiting forever.
                        if ((voiceAudio == null || !voiceAudio.isPlaying) && heldElapsed >= hold)
                            break;

                        heldElapsed += Time.deltaTime;
                        yield return null;
                    }

                    Debug.Log($"Held image finished: {marker.mediaName} after {heldElapsed:F2}s");
                }
                else
                {
                    Debug.Log($"Displaying image: {marker.mediaName} for {hold}s");

                    // Holds for its duration, unaffected by ordinary dismissals
                    // as before — only a {Zoom:ExtremeIn} cut ends it early, and
                    // an expiry right on one waits for that cut.
                    float shown = 0f;
                    while ((shown < hold || IsCloseUpCutImminent) && closeUpCutCount == cutCount)
                    {
                        shown += Time.deltaTime;
                        yield return null;
                    }
                }
            }
            else
            {
                Debug.LogError($"Image not found on disk or in Resources: {marker.mediaName}");
            }

            if (loadedDiskTexture != null)
                Destroy(loadedDiskTexture);
        }
        else if (marker.mediaType == MediaType.VIDEO)
        {
            string diskPath = ResolveVideoPath(marker.mediaName);
            bool playingFromDisk = diskPath != null;
            VideoClip clip = null;

            if (!playingFromDisk)
                clip = Resources.Load<VideoClip>($"{mediaFolderPath}/{marker.mediaName}");

            if (playingFromDisk || clip != null)
            {
                videoPlayer.gameObject.SetActive(true);

                if (playingFromDisk)
                {
                    videoPlayer.source = VideoSource.Url;
                    videoPlayer.url = "file:///" + diskPath.Replace('\\', '/');
                    videoPlayer.clip = null;
                    Debug.Log($"Playing video from disk: {diskPath}");
                }
                else
                {
                    videoPlayer.source = VideoSource.VideoClip;
                    videoPlayer.clip = clip;
                }

                // Re-assert the frame-critical settings here rather than
                // trusting Awake — assigning a new source rebuilds the
                // player's internal track list.
                videoPlayer.renderMode      = VideoRenderMode.RenderTexture;
                videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
                videoPlayer.targetTexture   = null;

                // Looping, because the clip's lifetime is now decided by the
                // script's beats rather than by its own length — a short clip
                // under a long beat would otherwise freeze on its last frame.
                videoPlayer.isLooping = true;

                RenderTexture rt = null;

                videoPlayer.Prepare();

                // Bounded wait. A missing codec or unreadable file leaves
                // isPrepared false forever, and isShowingMedia stays true until
                // this coroutine returns — an unbounded loop would hold the
                // take open and stall the recorder waiting on it.
                float prepareElapsed = 0f;
                while (!videoPlayer.isPrepared && prepareElapsed < videoPrepareTimeout &&
                       closeUpCutCount == cutCount)
                {
                    prepareElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                // A {Zoom:ExtremeIn} while the clip was preparing cut it away
                // before it was ever on screen — don't let it appear late.
                bool cutWhilePreparing = closeUpCutCount != cutCount;
                if (cutWhilePreparing)
                {
                    Debug.Log($"Video '{marker.mediaName}' was cut away by {{Zoom:ExtremeIn}} " +
                              "while preparing — not shown.");
                }
                else if (videoPlayer.isPrepared)
                {
                    MuteAudioTracks(videoPlayer);

                    // Render target at the clip's native size. The old fixed
                    // 1920x1080 buffer rescaled every non-1080p clip on every
                    // frame, which is what showed up as stutter.
                    int vw = Mathf.Max(1, (int)videoPlayer.width);
                    int vh = Mathf.Max(1, (int)videoPlayer.height);
                    rt = new RenderTexture(vw, vh, 0);
                    rt.Create();
                    ClearToTransparent(rt);   // never present uninitialised GPU memory

                    videoPlayer.targetTexture = rt;
                    mediaDisplay.texture      = rt;
                    FitDisplayToAspect(vw, vh);

                    videoPlayer.Play();
                    ShowDisplay(marker);

                    // The tag's duration is a MINIMUM, not a lifetime. The clip
                    // runs until the beat it belongs to ends — the presenter
                    // changes side, a content card appears, or the next
                    // {Image:}/{Video:} is due — because the script author can't
                    // know how long the narration underneath it takes. The
                    // minimum only matters once the narration has stopped, so a
                    // clip authored on the final words still gets its time.
                    float minHold = marker.displayDuration > 0f
                        ? marker.displayDuration : DefaultMediaSeconds;

                    Debug.Log($"Playing video: {marker.mediaName} ({vw}x{vh}), " +
                              $"clip {(float)videoPlayer.length:F2}s, looping until superseded " +
                              $"(min {minHold:F2}s)");

                    int   token        = mediaDismissToken;
                    float videoElapsed = 0f;
                    bool  started      = false;
                    int   stalledFrames = 0;

                    while (true)
                    {
                        // isPlaying reads false for the first frames after Play()
                        // while waitForFirstFrame loads frame zero, so gating the
                        // loop on it directly ended the clip before it was ever on
                        // screen. Tolerate a run of false frames instead: a short
                        // one is a loop seam, a long one means playback is gone.
                        if (videoPlayer.isPlaying) { started = true; stalledFrames = 0; }
                        else if (++stalledFrames > (started ? 30 : 120))
                        {
                            Debug.LogError($"Video '{marker.mediaName}' " +
                                (started ? "stopped unexpectedly." : "never started playing."));
                            break;
                        }

                        if (closeUpCutCount != cutCount) break;    // {Zoom:ExtremeIn} cut it away

                        // An end that lands right on an ExtremeIn waits for the
                        // cut, so the clip goes on the camera snap's frame.
                        if (!IsCloseUpCutImminent)
                        {
                            if (mediaDismissToken != token) break;   // position change / card
                            if (NextMediaMarkerDue())         break;   // next clip is due
                        }

                        // Narration over: hold the authored minimum, then stop so
                        // the recorder isn't kept open by a clip nothing will end.
                        if ((voiceAudio == null || !voiceAudio.isPlaying) && videoElapsed >= minHold)
                            break;

                        videoElapsed += Time.deltaTime;
                        yield return null;
                    }

                    Debug.Log($"Video finished: {marker.mediaName} after {videoElapsed:F2}s");
                }
                else
                {
                    Debug.LogError($"Video '{marker.mediaName}' failed to prepare within " +
                                   $"{videoPrepareTimeout:F0}s — skipping it so the take can continue.");
                }

                videoPlayer.Stop();
                videoPlayer.targetTexture = null;
                videoPlayer.gameObject.SetActive(false);

                if (rt != null)
                    Destroy(rt);
            }
            else
            {
                Debug.LogError($"Video not found on disk or in Resources: {marker.mediaName}");
            }
        }

        // Stop the entry tween mid-flight if it's still running — a dismissed
        // media must not keep animating the rect the next marker is about to use.
        if (mediaEntrySequence != null && mediaEntrySequence.IsActive())
            mediaEntrySequence.Kill();
        mediaEntrySequence = null;

        // Stop the idle float too — ApplyMediaSide below re-seats the rect, and
        // the next entry re-bases the float from wherever it lands.
        if (mediaDisplay != null) CardIdleFloat.StopOn(mediaDisplay.rectTransform);

        // Undo any letterboxing, side mirroring (",Right") and entry fade so the
        // next {Image:} gets the authored slot at full opacity.
        if (mediaDisplay != null && mediaDisplayBaseSize.x > 0f && mediaDisplayBaseSize.y > 0f)
            mediaDisplay.rectTransform.sizeDelta = mediaDisplayBaseSize;
        ApplyMediaSide(false);
        if (mediaDisplayGroup != null) mediaDisplayGroup.alpha = 1f;

        // Drop the texture before hiding: whatever it points at is about to be
        // destroyed, and a RawImage holding a dead texture is the white quad
        // again the next time something activates it.
        if (mediaDisplay != null) mediaDisplay.texture = null;

        mediaDisplay.gameObject.SetActive(false);
    }

    // -----------------------------------------------------------------------
    // PROJECT FIX (AutoAvatarGen): pre-take image cache. Decoding a PNG/JPG
    // with File.ReadAllBytes + LoadImage takes 50-300 ms ON THE MAIN THREAD,
    // and it used to happen the moment an {Image:} marker or {BigImage:} card
    // fired — a guaranteed dropped-frame hitch in the recording, exactly
    // during the card's entry animation. PreloadReferencedImages decodes every
    // disk image the script references right after parsing, BEFORE the
    // recorder starts, and the show-time paths serve from this cache.
    // Cached textures are owned by this system (destroyed in OnDestroy);
    // callers must never Destroy() a cache hit — LoadImageTexture reports
    // them as ownedByCaller=false.
    // -----------------------------------------------------------------------

    readonly Dictionary<string, Texture2D> preloadedImages =
        new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

    public void PreloadImage(string mediaName)
    {
        if (string.IsNullOrWhiteSpace(mediaName) || preloadedImages.ContainsKey(mediaName))
            return;

        // Only external-folder files need pre-decoding; Resources fallbacks
        // and missing names keep today's show-time behavior (incl. warnings).
        string diskPath = ResolveImagePath(mediaName);
        if (diskPath == null) return;

        Texture2D tex = LoadTextureFromDisk(diskPath);
        if (tex != null)
            preloadedImages[mediaName] = tex;
    }

    void PreloadCardImages(List<ContentCardEvent> events)
    {
        if (events == null) return;
        foreach (ContentCardEvent e in events)
        {
            if (e != null && e.cardType == ContentCardType.BigImage && !e.dismissesCard)
                PreloadImage(e.primaryText);
        }
    }

    // Walks everything the parsers produced — the main media timeline, the
    // media/cards claimed by {Transition:} lines, and the content-card
    // timeline — and pre-decodes each referenced disk image once.
    void PreloadReferencedImages(List<ContentCardEvent> cardEvents)
    {
        float started = Time.realtimeSinceStartup;
        int before = preloadedImages.Count;

        if (mediaMarkers != null)
        {
            foreach (MediaMarkerData m in mediaMarkers)
                if (m.mediaType == MediaType.IMAGE && !m.endsMedia)
                    PreloadImage(m.mediaName);
        }

        if (transitionMarkers != null)
        {
            foreach (TransitionMarkerData t in transitionMarkers)
            {
                if (t.mediaMarkers != null)
                    foreach (MediaMarkerData m in t.mediaMarkers)
                        if (m.mediaType == MediaType.IMAGE && !m.endsMedia)
                            PreloadImage(m.mediaName);
                PreloadCardImages(t.contentCards);
            }
        }

        PreloadCardImages(cardEvents);

        int loaded = preloadedImages.Count - before;
        if (loaded > 0)
            Debug.Log($"[MediaPresentation] Preloaded {loaded} image(s) in " +
                      $"{(Time.realtimeSinceStartup - started) * 1000f:F0} ms (before the take starts).");
    }

    void OnDestroy()
    {
        // The preload cache owns its textures — release them with the scene so
        // repeated takes can't leak decoded images.
        foreach (var kv in preloadedImages)
            if (kv.Value != null) Destroy(kv.Value);
        preloadedImages.Clear();
    }

    // -----------------------------------------------------------------------
    // External-folder layout and resolution.
    //
    // One root, six subfolders (BRoll / Images / CEO / TwitterPosts / Flags /
    // Logos). A media name is looked up in EVERY subfolder — the tag's own
    // kind first ({Image:} starts at Images, {Video:} at BRoll), then the
    // rest in a fixed order, then the root itself — so a file only has to be
    // somewhere under the root to resolve, and sorting it into the right
    // folder never changes the tag. Names may include or omit an extension;
    // if omitted, common extensions are tried. Returns null if not found or
    // if externalMediaRoot is blank.
    //
    // generate_media_library.py mirrors this order when it writes
    // MEDIA_LIBRARY.md — keep the two in sync.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Subfolder names a freshly chosen root gets from the main menu, where no
    /// MediaPresentationSystem instance exists to read the Inspector names
    /// from. Same defaults as the fields above.
    /// </summary>
    public static readonly string[] DefaultMediaSubfolders =
        { "BRoll", "Images", "CEO", "TwitterPosts", "Flags", "Logos" };

    // The same default names split by kind, in search order — what the fields
    // above default to (and what SampleScene keeps).
    static readonly string[] DefaultImageSubfolders = { "Images", "CEO", "TwitterPosts", "Flags", "Logos" };
    static readonly string[] DefaultVideoSubfolders = { "BRoll" };

    // mediaFolderPath's default — the Resources fallback both lookups end at.
    const string DefaultResourcesMediaFolder = "Media";

    /// <summary>Image subfolders in {Image:} / {BigImage:} search order.</summary>
    string[] ImageSubfolders =>
        new[] { imagesSubfolder, ceoSubfolder, twitterPostsSubfolder, flagsSubfolder, logosSubfolder };

    /// <summary>Video subfolders in {Video:} search order.</summary>
    string[] VideoSubfolders => new[] { bRollSubfolder };

    /// <summary>Every configured subfolder, video first then images.</summary>
    string[] AllSubfolders
    {
        get
        {
            var all = new List<string>(VideoSubfolders);
            all.AddRange(ImageSubfolders);
            return all.ToArray();
        }
    }

    /// <summary>
    /// Creates each of <paramref name="subfolders"/> under <paramref name="root"/>
    /// when it is missing. The root itself is never created — a mistyped path
    /// must not grow a folder tree somewhere unexpected. Returns how many
    /// folders were created, or -1 when the root does not exist.
    /// </summary>
    public static int EnsureMediaSubfolders(string root, IEnumerable<string> subfolders)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return -1;

        int created = 0;
        foreach (string sub in subfolders)
        {
            if (string.IsNullOrWhiteSpace(sub)) continue;
            try
            {
                string path = Path.Combine(root, sub.Trim());
                // Directory.Exists is case-insensitive on Windows, so an
                // existing "logos" satisfies "Logos" and is left alone.
                if (Directory.Exists(path)) continue;
                Directory.CreateDirectory(path);
                created++;
                Debug.Log($"[MediaPresentation] Created media subfolder: {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MediaPresentation] Could not create media subfolder '{sub}' under '{root}': {e.Message}");
            }
        }
        return created;
    }

    string ResolveImagePath(string mediaName)
        => FindInMediaRoot(externalMediaRoot, mediaName, ImageExtensions, ImageSubfolders, VideoSubfolders);

    string ResolveVideoPath(string mediaName)
        => FindInMediaRoot(externalMediaRoot, mediaName, VideoExtensions, VideoSubfolders, ImageSubfolders);

    /// <summary>
    /// Whether a media name would find something to show — the {Image:} /
    /// {BigImage:} lookup, or {Video:}'s when <paramref name="video"/> — under
    /// <paramref name="root"/>, then in Resources/Media. For code that runs with
    /// no instance to ask (the TTS panel's tag check), so it uses the default
    /// folder names; the Resources fallback may load the asset to find it.
    /// </summary>
    public static bool MediaNameResolves(string root, string mediaName, bool video)
    {
        if (string.IsNullOrWhiteSpace(mediaName)) return false;
        try
        {
            string hit = video
                ? FindInMediaRoot(root, mediaName, VideoExtensions, DefaultVideoSubfolders, DefaultImageSubfolders)
                : FindInMediaRoot(root, mediaName, ImageExtensions, DefaultImageSubfolders, DefaultVideoSubfolders);
            if (hit != null) return true;

            string resource = $"{DefaultResourcesMediaFolder}/{mediaName}";
            return video
                ? Resources.Load<VideoClip>(resource) != null
                : Resources.Load<Texture2D>(resource) != null;
        }
        catch (System.ArgumentException)
        {
            return false; // a character no path may hold — nothing can match it
        }
    }

    // Preferred subfolders first, then the remaining ones, then the root.
    static string FindInMediaRoot(string root, string mediaName, string[] extensions,
                                  string[] preferred, string[] others)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(mediaName)) return null;

        string hit = FindInSubfolders(root, preferred, mediaName, extensions);
        if (hit != null) return hit;

        hit = FindInSubfolders(root, others, mediaName, extensions);
        if (hit != null) return hit;

        return FindFileInFolder(root, mediaName, extensions);
    }

    static string FindInSubfolders(string root, string[] subfolders, string mediaName, string[] extensions)
    {
        foreach (string sub in subfolders)
        {
            if (string.IsNullOrWhiteSpace(sub)) continue;
            string hit = FindFileInFolder(Path.Combine(root, sub.Trim()), mediaName, extensions);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>
    /// Resolves and loads an image by name for content cards that need a disk
    /// texture (e.g. the {BigImage:...} article card). Uses the SAME lookup as
    /// {Image:name}: every external media subfolder (Images first), then the
    /// root, then a final Resources/{mediaFolderPath} fallback. Returns null if
    /// nothing matched.
    ///
    /// <paramref name="ownedByCaller"/> is true when the returned texture was
    /// decoded from disk and the caller MUST Destroy() it when done; false for a
    /// Resources asset (shared — destroying it would break other users).
    /// </summary>
    public Texture2D LoadImageTexture(string mediaName, out bool ownedByCaller)
    {
        ownedByCaller = false;

        // PROJECT FIX (AutoAvatarGen): serve the pre-take cache first. Cache
        // hits are owned by this system — the caller must NOT Destroy() them,
        // hence ownedByCaller stays false (same contract as Resources hits).
        if (mediaName != null &&
            preloadedImages.TryGetValue(mediaName, out Texture2D cached) && cached != null)
            return cached;

        string diskPath = ResolveImagePath(mediaName);
        if (diskPath != null)
        {
            Texture2D tex = LoadTextureFromDisk(diskPath);
            if (tex != null) { ownedByCaller = true; return tex; }
        }

        return Resources.Load<Texture2D>($"{mediaFolderPath}/{mediaName}");
    }

    static string FindFileInFolder(string folder, string mediaName, string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;

        // Exact name (with extension) first
        string exact = Path.Combine(folder, mediaName);
        if (File.Exists(exact)) return exact;

        // Try each extension
        foreach (string ext in extensions)
        {
            string withExt = Path.Combine(folder, mediaName + ext);
            if (File.Exists(withExt)) return withExt;
        }

        return null;
    }

    static Texture2D LoadTextureFromDisk(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2);
            if (tex.LoadImage(data)) return tex;
            Object.Destroy(tex);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load image from disk '{path}': {e.Message}");
        }
        return null;
    }

    // -----------------------------------------------------------------------
    // Parse Position Markers
    // Format: {Position:Left}, {Position:Right,Cut}, {Position:Center,Smooth}
    //         (optional ,T=X.XXX appended by the ElevenLabs pre-processor)
    // -----------------------------------------------------------------------

    (string, List<PositionMarkerData>) ParsePositionMarkers(string script, float audioDuration)
    {
        List<PositionMarkerData> markerList = new List<PositionMarkerData>();
        string clean = script;

        Regex regex = new Regex(@"\{Position:(\w+)(?:,(Cut|Smooth))?(?:,T=(\d+(?:\.\d+)?))?\}");
        MatchCollection matches = regex.Matches(script);

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = Mathf.Max(1, scriptWithoutMarkers.Length);

        foreach (Match match in matches)
        {
            float markerTime = TryParseTimestamp(match.Groups[3]);
            if (markerTime < 0f)
            {
                string textBeforeMarker = script.Substring(0, match.Index);
                string cleanTextBefore = regex.Replace(textBeforeMarker, "");
                markerTime = (cleanTextBefore.Length / (float)totalChars) * audioDuration;
            }

            string posStr = match.Groups[1].Value;
            CharacterPosition pos = CharacterPosition.Center;

            switch (posStr.ToLower())
            {
                case "left": pos = CharacterPosition.Left; break;
                case "right": pos = CharacterPosition.Right; break;
                case "center": pos = CharacterPosition.Center; break;
                default:
                    Debug.LogWarning($"Unknown position: {posStr}, defaulting to Center");
                    break;
            }

            // Parse optional cut/smooth override
            bool? cutOverride = null;
            if (match.Groups[2].Success)
            {
                string transStr = match.Groups[2].Value.ToLower();
                if (transStr == "cut") cutOverride = true;
                else if (transStr == "smooth") cutOverride = false;
            }

            markerList.Add(new PositionMarkerData
            {
                triggerTime = markerTime,
                position = pos,
                hardCutOverride = cutOverride
            });

            Debug.Log($"Position marker '{pos}'{(cutOverride.HasValue ? (cutOverride.Value ? " (hard cut)" : " (smooth)") : "")} will trigger at {markerTime:F2}s");

            clean = clean.Replace(match.Value, "");
        }

        return (clean, markerList);
    }

    // -----------------------------------------------------------------------
    // Parse Media Markers
    // Format: {Image:name}, {Image:name,3}, or the pre-processed
    //         {Image:name,T=X.XXX,D=Y}. Also handles {Video:...}. All forms
    //         accept an optional trailing ",Left"/",Right" side modifier
    //         (kept last, after the duration/D=, like the side cards).
    //         {Image:name,Start} is the held pair form — no fixed lifetime,
    //         the image stays until {Image:End} or the next beat, mirroring
    //         {Black:Start}…{Black:End} (a {Video:} is already held by
    //         default, so Start on one changes nothing).
    // -----------------------------------------------------------------------

    (string, List<MediaMarkerData>) ParseMediaMarkers(string script, float audioDuration)
    {
        List<MediaMarkerData> markerList = new List<MediaMarkerData>();
        string clean = script;

        // Groups: 1=Image|Video, 2=name, 3=T (optional), 4=D= duration (optional),
        //         5=bare duration (optional, legacy pre-T= format),
        //         6=optional "Start" hold keyword (held pair opening edge),
        //         7=optional ",Left"/",Right" side modifier (same as the side cards)
        Regex regex = new Regex(
            @"\{(Image|Video):([^,}]+)(?:,T=(\d+(?:\.\d+)?))?(?:,D=(\d+(?:\.\d+)?))?(?:,(\d+(?:\.\d+)?))?(?:,\s*(Start))?(?:,\s*(Left|Right))?\}");
        MatchCollection matches = regex.Matches(script);

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = Mathf.Max(1, scriptWithoutMarkers.Length);

        foreach (Match match in matches)
        {
            float markerTime = TryParseTimestamp(match.Groups[3]);
            if (markerTime < 0f)
            {
                string textBeforeMarker = script.Substring(0, match.Index);
                string cleanTextBefore = regex.Replace(textBeforeMarker, "");
                markerTime = (cleanTextBefore.Length / (float)totalChars) * audioDuration;
            }

            MediaType type = match.Groups[1].Value == "Image" ? MediaType.IMAGE : MediaType.VIDEO;
            string mediaName = match.Groups[2].Value.Trim();

            // {Video:End} closes the running b-roll clip and {Image:End} the
            // held image, paired like {Zoom:ExtremeIn}/{Zoom:ExtremeOut}. "End"
            // is canonical (the guide documents only it); Stop/Out are accepted
            // as typo-tolerance. The name is reserved — a real file named
            // End.mp4/End.png can't be played.
            string lowerName = mediaName.ToLowerInvariant();
            bool endsMedia = lowerName == "end" || lowerName == "stop" || lowerName == "out";

            // ",Start" hold keyword — the held pair's opening edge: the image
            // stays until its {Image:End} or the next beat instead of a fixed
            // duration. (Redundant on a {Video:}, which is already held.)
            bool holdsOpen = !endsMedia && match.Groups[6].Success;

            float duration;
            if (match.Groups[4].Success)
                duration = float.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
            else if (match.Groups[5].Success)
                duration = float.Parse(match.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture);
            else
                duration = type == MediaType.IMAGE ? 3f : 0f;

            // Optional ",Left"/",Right" side — same modifier the side cards
            // take. null = authored default (the left slot).
            EntryDirection? side = null;
            if (match.Groups[7].Success)
                side = match.Groups[7].Value == "Right"
                    ? EntryDirection.FromRight : EntryDirection.FromLeft;

            markerList.Add(new MediaMarkerData
            {
                triggerTime = markerTime,
                mediaType = type,
                mediaName = mediaName,
                displayDuration = duration,
                endsMedia = endsMedia,
                holdsOpen = holdsOpen,
                side = side
            });

            if (endsMedia)
                Debug.Log($"Media marker {{{match.Groups[1].Value}:End}} will end the active media at {markerTime:F2}s");
            else if (holdsOpen)
                Debug.Log($"Media marker '{mediaName}' ({type}) will trigger at {markerTime:F2}s and hold until its End tag" +
                          (side.HasValue ? $" on the {(side == EntryDirection.FromRight ? "Right" : "Left")}" : ""));
            else
                Debug.Log($"Media marker '{mediaName}' ({type}) will trigger at {markerTime:F2}s for {duration}s" +
                          (side.HasValue ? $" on the {(side == EntryDirection.FromRight ? "Right" : "Left")}" : ""));

            clean = clean.Replace(match.Value, "");
        }

        return (clean, markerList);
    }

    // -----------------------------------------------------------------------
    // Transition + Mood Tracking
    //
    // {Transition:<Wipe|Shutter|Iris>[,<durationScale>]} fires a whole-screen
    // cover -> reveal. Under cover (onCovered) the scene is reconfigured so each
    // transition reads as a fresh section. {Transition:...} CLAIMS the other state
    // tags on its line ({Position:...}, the emotion tag, {Mood:...} and any content
    // card) at parse time and applies them at the cover midpoint, in snap form —
    // so Mugs never slides and the old card never pops out in view. A {Mood:...}
    // NOT on a transition line crossfades on its own timeline.
    //
    // Nothing here pauses the narration. A section-opening transition instead
    // gets its T= re-pointed into a gap of real silence that SegmentSequencer
    // bakes into the stitched clip ahead of that segment, so the cover+reveal
    // lands between the two sections' words rather than over them.
    // -----------------------------------------------------------------------

    // {Transition:Wipe} / {Transition:Iris,1.2} / {Transition:Wipe,T=5.0} / {Transition:Iris,1.2,T=5.0}
    static readonly Regex TransitionRegex = new Regex(
        @"\{Transition:(\w+)(?:,(\d+(?:\.\d+)?))?(?:,T=(\d+(?:\.\d+)?))?\}",
        RegexOptions.IgnoreCase);
    // {Mood:Tense} / {Mood:Tense,T=5.0}
    static readonly Regex MoodRegex = new Regex(
        @"\{Mood:(\w+)(?:,T=(\d+(?:\.\d+)?))?\}",
        RegexOptions.IgnoreCase);
    // Co-located {Position:...} on a transition line (only the position value is used — the
    // change is always a snap under cover, so any Cut/Smooth qualifier is ignored).
    static readonly Regex BundlePositionRegex = new Regex(
        @"\{Position:(\w+)(?:,(?:Cut|Smooth))?(?:,T=(\d+(?:\.\d+)?))?\}",
        RegexOptions.IgnoreCase);
    // Co-located emotion tag — a bare {Word}[,T=X] (colon tags are excluded by the
    // pattern), matching HybridAvatarSystem's own emotion regex.
    static readonly Regex BundleEmotionRegex = new Regex(
        @"\{(\w+)(?:,T=(\d+(?:\.\d+)?))?\}");

    (string, List<TransitionMarkerData>) ParseTransitionMarkers(string script, float audioDuration)
    {
        var markers = new List<TransitionMarkerData>();

        if (script.IndexOf("{Transition:", System.StringComparison.OrdinalIgnoreCase) < 0)
            return (script, markers);

        // Denominator for the no-T= fallback (same idiom as the other parsers:
        // strip only this marker type, measure the remainder).
        int totalChars = Mathf.Max(1, TransitionRegex.Replace(script, "").Length);

        string[] lines = script.Split('\n');
        int lineStartGlobal = 0;

        for (int li = 0; li < lines.Length; li++)
        {
            string line = lines[li];
            Match tr = TransitionRegex.Match(line);
            if (tr.Success)
            {
                ScreenTransition type = ParseTransitionType(tr.Groups[1].Value);

                float scale = 1f;
                if (tr.Groups[2].Success)
                    float.TryParse(tr.Groups[2].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out scale);
                if (scale <= 0f) scale = 1f;

                float markerTime = TryParseTimestamp(tr.Groups[3]);
                if (markerTime < 0f)
                {
                    string before = script.Substring(0, lineStartGlobal + tr.Index);
                    markerTime = (TransitionRegex.Replace(before, "").Length / (float)totalChars) * audioDuration;
                }

                // Work on the line with the transition tag removed, then claim the
                // co-located state tags off it (each removed as it's claimed).
                string work = line.Remove(tr.Index, tr.Length);
                var data = new TransitionMarkerData
                {
                    triggerTime = markerTime,
                    transition = type,
                    durationScale = scale
                };

                Match pos = BundlePositionRegex.Match(work);
                if (pos.Success)
                {
                    data.hasPosition = true;
                    data.position = ParsePositionValue(pos.Groups[1].Value);
                    work = work.Remove(pos.Index, pos.Length);
                }

                Match moodMatch = MoodRegex.Match(work);
                if (moodMatch.Success)
                {
                    if (TryMapMood(moodMatch.Groups[1].Value, out var mt))
                    {
                        data.hasMood = true;
                        data.mood = mt;
                    }
                    work = work.Remove(moodMatch.Index, moodMatch.Length);
                }

                // Reuse the content-card parser on the single line; the events'
                // own trigger times are unused (ShowCard is called directly at cover).
                var cardResult = ContentZoneTagParser.ParseContentTags(work, audioDuration);
                if (cardResult.Item2.Count > 0)
                {
                    data.contentCards = cardResult.Item2;
                    work = cardResult.Item1;
                }

                // Claim co-located {Image:}/{Video:} media and {Zoom:} off the line
                // too, so they ALSO fire at full cover (onCovered) instead of on
                // their own timelines at the transition's start time — otherwise
                // they'd pop in mid-sweep, while the old scene is still partly visible.
                var mediaResult = ParseMediaMarkers(work, audioDuration);
                if (mediaResult.Item2.Count > 0)
                {
                    data.mediaMarkers = mediaResult.Item2;
                    work = mediaResult.Item1;
                }

                var zoomResult = ParseZoomMarkers(work, audioDuration);
                if (zoomResult.Item2.Count > 0)
                {
                    data.zoomMarkers = zoomResult.Item2;
                    work = zoomResult.Item1;
                }

                // Emotion last — only bare {Word} tags remain (colon tags already claimed/left).
                Match emo = BundleEmotionRegex.Match(work);
                if (emo.Success)
                {
                    data.emotion = emo.Groups[1].Value;
                    work = work.Remove(emo.Index, emo.Length);
                }

                markers.Add(data);
                Debug.Log($"[Transition] '{type}' x{scale:F2} at {markerTime:F2}s — " +
                          $"pos={(data.hasPosition ? data.position.ToString() : "none")}, " +
                          $"emotion={(string.IsNullOrEmpty(data.emotion) ? "none" : data.emotion)}, " +
                          $"mood={(data.hasMood ? data.mood.ToString() : "none")}, " +
                          $"cards={(data.contentCards != null ? data.contentCards.Count : 0)}, " +
                          $"media={(data.mediaMarkers != null ? data.mediaMarkers.Count : 0)}, " +
                          $"zoom={(data.zoomMarkers != null ? data.zoomMarkers.Count : 0)}");

                lines[li] = work;
            }

            lineStartGlobal += line.Length + 1; // original line length + the split '\n'
        }

        return (string.Join("\n", lines), markers);
    }

    (string, List<MoodMarkerData>) ParseMoodMarkers(string script, float audioDuration)
    {
        var markers = new List<MoodMarkerData>();
        string clean = script;

        MatchCollection matches = MoodRegex.Matches(script);
        if (matches.Count == 0) return (clean, markers);

        int totalChars = Mathf.Max(1, MoodRegex.Replace(script, "").Length);

        foreach (Match match in matches)
        {
            float markerTime = TryParseTimestamp(match.Groups[2]);
            if (markerTime < 0f)
            {
                string before = script.Substring(0, match.Index);
                markerTime = (MoodRegex.Replace(before, "").Length / (float)totalChars) * audioDuration;
            }

            if (TryMapMood(match.Groups[1].Value, out var mt))
            {
                markers.Add(new MoodMarkerData { triggerTime = markerTime, mood = mt });
                Debug.Log($"[Mood] '{mt}' will crossfade at {markerTime:F2}s");
            }

            clean = clean.Replace(match.Value, "");
        }

        return (clean, markers);
    }

    IEnumerator TrackTransitionsByTime()
    {
        lastTriggeredTransitionMarker = -1;
        if (transitionMarkers == null || transitionMarkers.Count == 0) yield break;
        yield return WaitForPlaybackStart();

        // `|| isShowingMedia` keeps tracking alive while a trailing {Image:} /
        // {Video:} is still on screen, so the end-of-audio flush only runs once
        // nothing is left to display.
        while (voiceAudio != null && (voiceAudio.isPlaying || isShowingMedia))
        {
            float currentTime = voiceAudio.time;

            for (int i = lastTriggeredTransitionMarker + 1; i < transitionMarkers.Count; i++)
            {
                if (currentTime >= transitionMarkers[i].triggerTime)
                {
                    ApplyTransition(transitionMarkers[i]);
                    lastTriggeredTransitionMarker = i;
                }
                else
                {
                    break;
                }
            }

            yield return null;
        }

        // Flush a transition clamped to the final word (same end-of-audio race as
        // cards / black). The recorder holds the take open while a trailing visual
        // is on screen.
        for (int i = lastTriggeredTransitionMarker + 1; i < transitionMarkers.Count; i++)
        {
            ApplyTransition(transitionMarkers[i]);
            lastTriggeredTransitionMarker = i;
        }
    }

    void ApplyTransition(TransitionMarkerData m)
    {
        ScreenTransitionController controller =
            screenTransitionController != null ? screenTransitionController : ScreenTransitionController.Instance;

        if (controller == null)
        {
            Debug.LogWarning("[Transition] No ScreenTransitionController — applying the scene change with no cover.");
            ApplyTransitionCover(m);
            return;
        }

        // Play the transition's sound effect as the cover starts — but only if the
        // transition will actually run. Play() ignores the call while another
        // transition is mid-flight, so gating on IsBusy avoids a stray double whoosh.
        if (!controller.IsBusy)
            PlayTransitionSfx(m.transition);

        // Play does nothing if a transition is already running, so two transition
        // tags can't overlap. The mutation runs at the cover midpoint, and the
        // reveal (out) sound rides the same callback: it fires the instant the
        // screen is fully covered — which is when the reveal half begins. Because
        // onCovered only runs when the transition actually played, the out sound
        // inherits the same no-double-trigger gating as the transition itself.
        controller.Play(m.transition,
            () =>
            {
                ApplyTransitionCover(m);
                PlayTransitionSfx(m.transition, reveal: true);
            },
            null, m.durationScale);
    }

    // Plays the Inspector-assigned clip for this transition half (if any) on a
    // dedicated 2D AudioSource (created on first use), so it layers over the
    // narration and is captured by the recorder. Silent when the matching clip
    // slot is left empty. reveal=false → the cover (in) clip; true → the
    // reveal (out) clip.
    void PlayTransitionSfx(ScreenTransition type, bool reveal = false)
    {
        AudioClip clip;
        if (reveal)
            clip = type == ScreenTransition.Wipe    ? transitionWipeOutSfx :
                   type == ScreenTransition.Shutter ? transitionShutterOutSfx :
                                                      transitionIrisOutSfx;
        else
            clip = type == ScreenTransition.Wipe    ? transitionWipeSfx :
                   type == ScreenTransition.Shutter ? transitionShutterSfx :
                                                      transitionIrisSfx;
        if (clip == null) return;

        if (transitionSfxSource == null)
        {
            transitionSfxSource = gameObject.AddComponent<AudioSource>();
            transitionSfxSource.playOnAwake  = false;
            transitionSfxSource.spatialBlend = 0f; // 2D — non-positional, captured by the recorder
        }
        transitionSfxSource.PlayOneShot(clip, transitionSfxVolume);
    }

    // Runs at the instant of full cover (onCovered). Everything here is hidden
    // behind the overlay, so the reveal shows a fresh scene: Mugs already
    // repositioned (snap, never a visible slide), the new card swapped in (or the
    // zone cleared), the new expression set, the mood crossfade started, any
    // {Image:}/{Video:} now on screen, and the camera already at its new zoom —
    // none of it visible until the screen is fully covered.
    void ApplyTransitionCover(TransitionMarkerData m)
    {
        // A transition ends any {Zoom:In} run — snapped back behind the cover,
        // before anything on the line runs, so a {Zoom:In} on this line opens
        // the new section at step 1.
        EndZoomRun("{Transition}", hiddenBehindCover: true);

        if (m.hasPosition)
            MoveToPosition(m.position, cutOverride: true); // SNAP — never smooth, inside cover

        if (!string.IsNullOrEmpty(m.emotion) && avatarSystem != null)
            avatarSystem.SetEmotionImmediate(m.emotion);

        if (m.hasMood)
            MugsTech.Background.BackgroundStyleManager.RouteMood(
                m.mood, moodCrossfadeSeconds, moodController);

        if (contentZoneController != null)
        {
            // Clear first so a new card cleanly REPLACES the old one (ShowCard would
            // otherwise queue behind it). With no card on the line the zone stays
            // cleared — the headline is gone on reveal.
            contentZoneController.ClearForTransition();
            if (m.contentCards != null)
                for (int i = 0; i < m.contentCards.Count; i++)
                    contentZoneController.ShowCard(m.contentCards[i]);
        }

        // Zoom — snap under cover (cut), like the position snap, so the reveal shows
        // the new framing with no camera glide over the visible scene. Pullback /
        // Reset manage their own cuts; the flag is harmless for them. The run was
        // ended above, so an In here is step 1.
        if (m.zoomMarkers != null)
            for (int i = 0; i < m.zoomMarkers.Count; i++)
                ApplyZoom(m.zoomMarkers[i].zoomType, cut: true, holdDuration: m.zoomMarkers[i].holdDuration);

        // Media ({Image:}/{Video:}) — show at full cover so it's only ever visible
        // once the screen is covered, never mid-sweep. (A {Video:} plays silently
        // over the narration, exactly as it does on its own timeline.) A
        // {Video:End} on the transition line instead dismisses the running
        // clip under cover.
        if (m.mediaMarkers != null)
            for (int i = 0; i < m.mediaMarkers.Count; i++)
            {
                if (m.mediaMarkers[i].endsMedia)
                {
                    DismissActiveMedia();
                    continue;
                }
                if (BlockedByCloseUp(m.mediaMarkers[i]))
                    continue;
                if (currentMediaCoroutine != null)
                    StopCoroutine(currentMediaCoroutine);
                currentMediaCoroutine = StartCoroutine(ShowMedia(m.mediaMarkers[i]));
            }
    }

    IEnumerator TrackMoodByTime()
    {
        lastTriggeredMoodMarker = -1;
        if (moodMarkers == null || moodMarkers.Count == 0) yield break;
        yield return WaitForPlaybackStart();

        while (voiceAudio != null && (voiceAudio.isPlaying || isShowingMedia))
        {
            float currentTime = voiceAudio.time;

            for (int i = lastTriggeredMoodMarker + 1; i < moodMarkers.Count; i++)
            {
                if (currentTime >= moodMarkers[i].triggerTime)
                {
                    MugsTech.Background.BackgroundStyleManager.RouteMood(
                        moodMarkers[i].mood, moodCrossfadeSeconds, moodController);
                    lastTriggeredMoodMarker = i;
                }
                else
                {
                    break;
                }
            }

            yield return null;
        }
    }

    static ScreenTransition ParseTransitionType(string s)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "wipe": return ScreenTransition.Wipe;
            case "shutter": return ScreenTransition.Shutter;
            case "iris": return ScreenTransition.Iris;
            default:
                Debug.LogWarning($"[Transition] Unknown transition '{s}', defaulting to Wipe.");
                return ScreenTransition.Wipe;
        }
    }

    static CharacterPosition ParsePositionValue(string s)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "left": return CharacterPosition.Left;
            case "right": return CharacterPosition.Right;
            case "center": return CharacterPosition.Center;
            default:
                Debug.LogWarning($"[Transition] Unknown position '{s}', defaulting to Center.");
                return CharacterPosition.Center;
        }
    }

    // Maps the script's mood variant (Calm/Energetic/Tense/Playful/Minimal — the
    // blueprint names) onto BackgroundMoodController.MoodType. The enum's own names
    // are accepted too. Returns false (and logs) for an unknown variant.
    static bool TryMapMood(string s, out MugsTech.Background.BackgroundMoodController.MoodType mood)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "calm":
            case "calmneutral":   mood = MugsTech.Background.BackgroundMoodController.MoodType.CalmNeutral;   return true;
            case "energetic":     mood = MugsTech.Background.BackgroundMoodController.MoodType.Energetic;     return true;
            case "tense":
            case "tensedramatic": mood = MugsTech.Background.BackgroundMoodController.MoodType.TenseDramatic; return true;
            case "playful":
            case "playfullight":  mood = MugsTech.Background.BackgroundMoodController.MoodType.PlayfulLight;  return true;
            case "minimal":
            case "minimalfocus":  mood = MugsTech.Background.BackgroundMoodController.MoodType.MinimalFocus;  return true;
            default:
                mood = MugsTech.Background.BackgroundMoodController.MoodType.CalmNeutral;
                Debug.LogWarning($"[Mood] Unknown mood '{s}' — ignored. Use Calm/Energetic/Tense/Playful/Minimal.");
                return false;
        }
    }

    // -----------------------------------------------------------------------
    // Parse Timestamp Markers  ({Timestamp:"Label"} -> YouTube chapter marker)
    //
    // Pure timeline markers. They are NEVER voiced (stripped before TTS by
    // elevenlabs_tts_processor.py / TtsScriptProcessor.cs), NEVER shown, and drive
    // nothing visual — parsing here only records (label, triggerTime) and strips
    // the tag. The ElevenLabs pre-processor bakes a ,T=X.XXX onto each one (already
    // shifted onto the stitched global timeline by SegmentSequencer); the
    // char-proportional fallback covers a hand-written tag that has no T=.
    // Format: {Timestamp:"Cold Open"}  or pre-processed  {Timestamp:"Cold Open",T=X.XXX}
    // -----------------------------------------------------------------------

    (string, List<TimestampMarkerData>) ParseTimestampMarkers(string script, float audioDuration)
    {
        List<TimestampMarkerData> markerList = new List<TimestampMarkerData>();
        string clean = script;

        // Group 1 = the free-text label (spaces/punctuation allowed, never a ");
        // Group 2 = optional baked T= seconds.
        Regex regex = new Regex(@"\{Timestamp:""([^""]*)""(?:,T=(\d+(?:\.\d+)?))?\}");
        MatchCollection matches = regex.Matches(script);

        string scriptWithoutMarkers = regex.Replace(script, "");
        int totalChars = Mathf.Max(1, scriptWithoutMarkers.Length);

        foreach (Match match in matches)
        {
            float markerTime = TryParseTimestamp(match.Groups[2]);
            if (markerTime < 0f)
            {
                string textBeforeMarker = script.Substring(0, match.Index);
                string cleanTextBefore = regex.Replace(textBeforeMarker, "");
                markerTime = (cleanTextBefore.Length / (float)totalChars) * audioDuration;
            }

            markerList.Add(new TimestampMarkerData
            {
                triggerTime = markerTime,
                label       = match.Groups[1].Value   // verbatim — spaces preserved
            });

            Debug.Log($"[Timestamp] '{match.Groups[1].Value}' marker will log at {markerTime:F2}s");

            clean = clean.Replace(match.Value, "");
        }

        return (clean, markerList);
    }

    // -----------------------------------------------------------------------
    // Timestamp Tracking — DEFERRED, AUDIO-CLOCK-TIED CAPTURE
    //
    // WHICH CASE APPLIES HERE: this project PRE-READS the whole script into marker
    // lists up front (ProcessScriptWithMedia parses everything before a single word
    // plays), so the parse/read moment is NOT when a listener hears that point. To
    // record what is actually heard, this tracker does exactly what every other
    // tracker does: it polls the audio playback clock (voiceAudio.time) each frame
    // and acts the instant the clock reaches the marker's triggerTime. The value it
    // LOGS is voiceAudio.time at that frame — the real playback position — NOT the
    // parse-time triggerTime, NOT a frame count, NOT wall-clock time. voiceAudio
    // plays the single stitched clip, so .time is the global position across the
    // whole video, which is precisely the YouTube-chapter timeline.
    // (Firing on `>=` means at most ~one frame of overshoot, identical to every
    // other marker in this system; it rounds to the same whole second.)
    // -----------------------------------------------------------------------

    IEnumerator TrackTimestampsByTime()
    {
        lastTriggeredTimestampMarker = -1;
        if (timestampMarkers == null || timestampMarkers.Count == 0) yield break;

        // Playback may start several frames late (recorder warm-up) — the old
        // one-frame latch isn't enough.
        yield return WaitForPlaybackStart();

        // `|| isShowingMedia` keeps tracking alive while a trailing {Image:} /
        // {Video:} is still on screen, so the end-of-audio flush only runs once
        // nothing is left to display.
        while (voiceAudio != null && (voiceAudio.isPlaying || isShowingMedia))
        {
            float currentTime = voiceAudio.time;   // <-- the audio playback position (source of truth)

            for (int i = lastTriggeredTimestampMarker + 1; i < timestampMarkers.Count; i++)
            {
                if (currentTime >= timestampMarkers[i].triggerTime)
                {
                    // Log the ACTUAL playback clock at the moment this point is reached.
                    TimestampMarkerLog.Capture(timestampMarkers[i].label, currentTime);
                    Debug.Log($"[Timestamp] Captured '{timestampMarkers[i].label}' at {currentTime:F2}s (audio playback position)");
                    lastTriggeredTimestampMarker = i;
                }
                else
                {
                    break;
                }
            }

            yield return null;
        }

        // End-of-audio flush: a marker clamped to the final word is never hit by the
        // loop above (it stops the instant playback ends). Capture it at its clamped
        // global triggerTime — voiceAudio.time may have reset to 0 once the clip
        // stopped, so triggerTime (the clip-end value) is the faithful position here.
        for (int i = lastTriggeredTimestampMarker + 1; i < timestampMarkers.Count; i++)
        {
            TimestampMarkerLog.Capture(timestampMarkers[i].label, timestampMarkers[i].triggerTime);
            lastTriggeredTimestampMarker = i;
        }
    }

    // -----------------------------------------------------------------------
    // Shared helper — parse a T=X.XXX capture. Returns -1 if the group is
    // empty or the value can't be parsed.
    // -----------------------------------------------------------------------

    static float TryParseTimestamp(Group group)
    {
        if (group == null || !group.Success) return -1f;
        if (float.TryParse(group.Value,
                           System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture,
                           out float t))
            return t;
        return -1f;
    }
}

// ============================================================================
// Enums & Data Classes
// ============================================================================

/// <summary>
/// The three character positions from Part 11 of the blueprint.
/// </summary>
public enum CharacterPosition
{
    Left,    // Left 25-30% of screen, faces right toward content
    Right,   // Right 25-30% of screen, faces left toward content
    Center   // Center of screen, faces camera
}

/// <summary>
/// Tracks when the character should move to a new position.
/// Same structure as TimeMarkerData and MediaMarkerData.
/// </summary>
[System.Serializable]
public class PositionMarkerData
{
    public float triggerTime;
    public CharacterPosition position;
    public bool? hardCutOverride; // null = use global, true = force cut, false = force smooth
}

// Existing types (unchanged)
public enum MediaType
{
    IMAGE,
    VIDEO
}

[System.Serializable]
public class MediaMarkerData
{
    public float triggerTime;
    public MediaType mediaType;
    public string mediaName;
    public float displayDuration;
    // {Video:End} / {Image:End} — dismisses the active media at triggerTime
    // instead of starting one. The pairs {Video:name}…{Video:End} and
    // {Image:name,Start}…{Image:End} mirror {Zoom:ExtremeIn}…{Zoom:ExtremeOut}.
    // The cut itself needs no extra code path: the active media's display loop
    // already breaks the frame the NEXT media marker comes due
    // (NextMediaMarkerDue), this marker included — the trackers just must
    // never try to play it as a file named "End".
    public bool endsMedia;

    // {Image:name,Start} — the held pair's opening edge: the image has no
    // fixed lifetime and stays up until its {Image:End}, the next beat, or the
    // end-of-narration hold, mirroring {Black:Start}…{Black:End}. Meaningless
    // on a {Video:} (clips are already held open by default).
    public bool holdsOpen;

    // Optional trailing ",Left"/",Right" — which side of the screen the media
    // slot rests on (and slides in from), mirroring the side cards' modifier.
    // null = no per-tag choice; the media stays in the authored (left) slot.
    public MugsTech.Style.EntryDirection? side;
}

/// <summary>
/// Zoom direction types from Part 12 (TRANS-02 / TRANS-03).
/// </summary>
public enum ZoomType
{
    In,         // Push in: 100% -> 110-115%. Signals focus/intensity.
    Out,        // Pull back: zoomed -> 100%. Signals de-escalation.
    Reset,      // Instant snap back to default. No easing.
    Pullback,   // Snap wide, slowly drift wider, jump-cut back to default.
    ExtremeIn,  // Jump-cut hard in to a close-up on the face. Held until ExtremeOut.
    ExtremeOut  // Jump-cut back to the exact framing ExtremeIn interrupted.
}

[System.Serializable]
public class ZoomMarkerData
{
    public float triggerTime;
    public ZoomType zoomType;

    // When true, the zoom snaps instantly instead of animating over zoomDuration.
    // Auto-reset (if scheduled) inherits this style.
    public bool cut;

    // Seconds after triggerTime to auto-reset the camera back to default. <= 0
    // means "no auto-reset" and the zoom stays until a later marker changes it.
    public float holdDuration;
}

/// <summary>
/// Fullscreen black panel marker. Timed form: jump-cuts in, holds for duration,
/// jump-cuts out. Held pair form ({Black:Start}…{Black:End}): opens with no
/// timer and is cut back out at the End tag's exact narration time — the same
/// in-tag/out-tag principle as {Video:name}…{Video:End}.
/// </summary>
[System.Serializable]
public class BlackPanelMarkerData
{
    public float triggerTime;
    public float duration;

    // {Black:Start} — jump-cut in and stay up until the matching {Black:End}.
    public bool holdsOpen;

    // {Black:End} — jump-cut the held panel out at triggerTime instead of
    // showing anything. Harmless when nothing is showing.
    public bool endsPanel;
}

/// <summary>
/// A whole-screen scene transition ({Transition:Wipe/Shutter/Iris}) plus the scene
/// mutation it carries — the state tags that shared its script line, applied
/// together at the cover midpoint so each transition reveals a fresh section.
/// </summary>
[System.Serializable]
public class TransitionMarkerData
{
    public float triggerTime;
    public ScreenTransition transition;
    public float durationScale = 1f;     // 1 = the 1x baseline; 1.2 = 20% slower, 0.8 = faster

    // --- Bundled scene mutation, applied at full cover (onCovered) ---
    public bool hasPosition;
    public CharacterPosition position;   // snap, never a smooth slide
    public string emotion;               // null/empty = no emotion change
    public bool hasMood;
    public MugsTech.Background.BackgroundMoodController.MoodType mood;
    public System.Collections.Generic.List<ContentCardEvent> contentCards;  // null/empty = clear the zone
    public System.Collections.Generic.List<MediaMarkerData> mediaMarkers;    // {Image:}/{Video:} shown at cover
    public System.Collections.Generic.List<ZoomMarkerData> zoomMarkers;      // {Zoom:} snapped at cover
}

/// <summary>
/// A standalone {Mood:...} marker — crossfades the background mood on its own
/// timeline (moods bundled onto a transition line are applied at cover instead).
/// </summary>
[System.Serializable]
public class MoodMarkerData
{
    public float triggerTime;
    public MugsTech.Background.BackgroundMoodController.MoodType mood;
}

/// <summary>
/// A pure timeline/chapter marker from a {Timestamp:"Label"} tag. Non-visual and
/// non-spoken — it only carries a label and the audio time at which the marker
/// should be logged (used to emit YouTube chapter timestamps). triggerTime is the
/// baked/global T= value; the actually-logged time is the live voiceAudio.time
/// captured when playback reaches it (see TrackTimestampsByTime).
/// </summary>
[System.Serializable]
public class TimestampMarkerData
{
    public float triggerTime;
    public string label;
}