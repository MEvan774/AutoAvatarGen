using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;

/// <summary>
/// Plays a b-roll video clip in the content zone at a fixed scale.
/// Tag: {BRoll:description,duration}
/// Self-building: constructs its own UI hierarchy in Awake.
/// </summary>
public class BRollDisplay : ContentCard
{
    protected override ContentCardType CardType => ContentCardType.BRoll;

    private RawImage videoImage;
    private VideoPlayer videoPlayer;
    private RectTransform videoImageRect;
    private RectTransform videoContainer;

    private RenderTexture renderTexture;
    private float cardDuration;

    protected override void BuildUI()
    {
        // Soft drop shadow behind the clip — same elevation the panel cards get
        // from CreateBackground. Square corners: RectMask2D clips the video to
        // the card's rectangular bounds, so a rounded shadow wouldn't match.
        ContentCardUIBuilder.CreateShadow(rectTransform, 0f);

        // Container — kept for layout. RectMask2D clips the video to the card
        // bounds (still useful at fixed scale because aspect-ratio mismatch
        // could otherwise bleed past the card edges).
        videoContainer = ContentCardUIBuilder.CreateChild(rectTransform, "VideoContainer");
        videoContainer.gameObject.AddComponent<RectMask2D>();

        // Video image — fills the container at 1:1 scale (no Ken Burns).
        GameObject videoGO = new GameObject("VideoImage", typeof(RectTransform));
        videoGO.transform.SetParent(videoContainer, false);
        videoImage = videoGO.AddComponent<RawImage>();
        videoImage.color = Color.white;
        videoImage.raycastTarget = false;
        videoImageRect = videoImage.rectTransform;
        videoImageRect.anchorMin = Vector2.zero;
        videoImageRect.anchorMax = Vector2.one;
        videoImageRect.offsetMin = Vector2.zero;
        videoImageRect.offsetMax = Vector2.zero;

        // VideoPlayer component on self
        videoPlayer = gameObject.AddComponent<VideoPlayer>();
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        videoPlayer.isLooping = true;
        videoPlayer.playOnAwake = false;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
    }

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        cardDuration = data.duration;

        // Tier 1 — ContentCardAssets b-roll dictionary (if an SO is wired up).
        VideoClip clip = assets != null ? assets.GetBRoll(data.primaryText) : null;

        // Tier 2 — direct Resources lookup. Drop VideoTemp.mp4 into
        // Assets/Resources/Media/ and {BRoll:VideoTemp,...} plays it without
        // needing a ContentCardAssets SO. Unity imports .mp4 as a VideoClip
        // automatically via VideoClipImporter.
        if (clip == null)
        {
            string folder = (assets != null && !string.IsNullOrEmpty(assets.bigMediaResourcesFolder))
                ? assets.bigMediaResourcesFolder
                : "Media";
            clip = Resources.Load<VideoClip>($"{folder}/{data.primaryText}");
        }

        if (clip != null)
        {
            renderTexture = new RenderTexture(1920, 1080, 24);
            videoPlayer.clip = clip;
            videoPlayer.targetTexture = renderTexture;
            videoImage.texture = renderTexture;
            videoPlayer.Prepare();
        }
        else
        {
            Debug.LogWarning($"BRollDisplay: No video clip found for \"{data.primaryText}\" " +
                             $"(checked ContentCardAssets.bRollClips and Resources.Load<VideoClip>)");
        }
    }

    public override void Show()
    {
        base.Show();

        if (videoPlayer.clip == null) return;

        // Lock scale at 1:1 — the Ken Burns slow-zoom was removed by request,
        // so the video plays at its natural framing for the full card duration.
        videoImageRect.localScale = Vector3.one;

        if (videoPlayer.isPrepared)
            videoPlayer.Play();
        else
            StartCoroutine(WaitForPrepareAndPlay());
    }

    private IEnumerator WaitForPrepareAndPlay()
    {
        while (!videoPlayer.isPrepared)
            yield return null;

        videoPlayer.Play();
    }

    public override void Hide(bool fast = false)
    {
        if (videoPlayer != null && videoPlayer.isPlaying)
            videoPlayer.Stop();

        base.Hide(fast);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        if (videoPlayer != null)
            videoPlayer.Stop();

        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}
