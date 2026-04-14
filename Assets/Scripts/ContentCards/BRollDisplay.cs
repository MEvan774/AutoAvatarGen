using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using DG.Tweening;
using System.Collections;

/// <summary>
/// Plays a b-roll video clip in the content zone with a Ken Burns effect.
/// Tag: {BRoll:description,duration}
/// Self-building: constructs its own UI hierarchy in Awake.
/// </summary>
public class BRollDisplay : ContentCard
{
    private RawImage videoImage;
    private VideoPlayer videoPlayer;
    private RectTransform videoImageRect;
    private RectTransform videoContainer;

    private RenderTexture renderTexture;
    private float cardDuration;
    private Tween kenBurnsTween;

    protected override void BuildUI()
    {
        // Container with RectMask2D for Ken Burns clipping
        videoContainer = ContentCardUIBuilder.CreateChild(rectTransform, "VideoContainer");
        videoContainer.gameObject.AddComponent<RectMask2D>();

        // Video image (will be scaled for Ken Burns)
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

        VideoClip clip = assets != null ? assets.GetBRoll(data.primaryText) : null;

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
            Debug.LogWarning($"BRollDisplay: No video clip found for \"{data.primaryText}\"");
        }
    }

    public override void Show()
    {
        base.Show();

        if (videoPlayer.clip == null) return;

        if (videoPlayer.isPrepared)
        {
            videoPlayer.Play();
            StartKenBurns();
        }
        else
        {
            StartCoroutine(WaitForPrepareAndPlay());
        }
    }

    private IEnumerator WaitForPrepareAndPlay()
    {
        while (!videoPlayer.isPrepared)
            yield return null;

        videoPlayer.Play();
        StartKenBurns();
    }

    private void StartKenBurns()
    {
        videoImageRect.localScale = Vector3.one;
        kenBurnsTween = videoImageRect.DOScale(Vector3.one * 1.05f, cardDuration).SetEase(Ease.Linear);
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

        if (kenBurnsTween != null && kenBurnsTween.IsActive())
            kenBurnsTween.Kill();

        if (videoPlayer != null)
            videoPlayer.Stop();

        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}
