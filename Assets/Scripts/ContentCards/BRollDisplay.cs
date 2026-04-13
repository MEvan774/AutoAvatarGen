using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using DG.Tweening;
using System.Collections;

/// <summary>
/// Plays a b-roll video clip in the content zone with a Ken Burns effect.
/// Tag: {BRoll:description,duration}
///
/// Prefab hierarchy:
///   BRollDisplay [RectTransform + CanvasGroup + BRollDisplay]
///     VideoContainer [RectTransform + RectMask2D for Ken Burns clipping]
///       VideoImage [RawImage: receives RenderTexture from VideoPlayer]
///     VideoPlayer [VideoPlayer component, audioOutputMode = None]
/// </summary>
public class BRollDisplay : ContentCard
{
    [Header("UI References")]
    public RawImage videoImage;
    public VideoPlayer videoPlayer;
    public RectTransform videoImageRect;

    private RenderTexture renderTexture;
    private float cardDuration;
    private Tween kenBurnsTween;

    public override void Initialize(ContentCardEvent data, ContentCardAssets assets)
    {
        cardDuration = data.duration;

        VideoClip clip = assets != null ? assets.GetBRoll(data.primaryText) : null;

        if (clip != null && videoPlayer != null)
        {
            // Create render texture matching output resolution
            renderTexture = new RenderTexture(1920, 1080, 24);
            videoPlayer.clip = clip;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            videoPlayer.targetTexture = renderTexture;
            videoPlayer.isLooping = true;

            if (videoImage != null)
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

        if (videoPlayer != null && videoPlayer.isPrepared)
        {
            videoPlayer.Play();
            StartKenBurns();
        }
        else if (videoPlayer != null)
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
        // Slow zoom from 100% to 105% over the card duration
        if (videoImageRect != null)
        {
            videoImageRect.localScale = Vector3.one;
            kenBurnsTween = videoImageRect.DOScale(
                Vector3.one * 1.05f,
                cardDuration
            ).SetEase(Ease.Linear);
        }
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
