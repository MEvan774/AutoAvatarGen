using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages content card timeline playback in the content zone.
/// Self-building: creates card GameObjects programmatically — no prefabs needed.
/// Auto-setup: if contentZone is unassigned, falls back to mediaDisplay's RectTransform.
/// </summary>
public class ContentZoneController : MonoBehaviour
{
    [Header("Content Zone")]
    [Tooltip("RectTransform where cards appear. If left empty, falls back to mediaDisplay's RectTransform.")]
    public RectTransform contentZone;

    [Header("Assets")]
    [Tooltip("Optional — maps company names to logos, and b-roll descriptions to video clips.")]
    public ContentCardAssets cardAssets;

    [Header("Media Coexistence")]
    [Tooltip("Reference to the existing media display — hidden while a card is active. If left empty, auto-finds from MediaPresentationSystem.")]
    public RawImage mediaDisplay;

    // Timeline state
    private List<ContentCardEvent> timeline;
    private int lastTriggeredIndex = -1;
    private AudioSource voiceAudio;
    private bool isPaused = false;

    // Active card state
    private ContentCard activeCard;
    private Coroutine durationCoroutine;
    private Coroutine hideAndShowCoroutine;

    /// <summary>True when a content card is currently visible.</summary>
    public bool IsCardActive => activeCard != null;

    void Awake()
    {
        // Auto-wire mediaDisplay from MediaPresentationSystem if not set
        if (mediaDisplay == null)
        {
            MediaPresentationSystem mps = GetComponent<MediaPresentationSystem>();
            if (mps == null)
                mps = FindObjectOfType<MediaPresentationSystem>();
            if (mps != null && mps.mediaDisplay != null)
            {
                mediaDisplay = mps.mediaDisplay;
                Debug.Log("ContentZoneController: auto-wired mediaDisplay from MediaPresentationSystem");
            }
        }

        // If no content zone assigned, create one as a SIBLING of the media display
        // (same parent, same size/position — but always active, independent of media display state)
        if (contentZone == null && mediaDisplay != null)
        {
            GameObject zoneGO = new GameObject("ContentZone_Cards", typeof(RectTransform));
            zoneGO.transform.SetParent(mediaDisplay.transform.parent, false);

            RectTransform zoneRT = zoneGO.GetComponent<RectTransform>();
            RectTransform mediaRT = mediaDisplay.rectTransform;

            // Copy layout from the media display
            zoneRT.anchorMin = mediaRT.anchorMin;
            zoneRT.anchorMax = mediaRT.anchorMax;
            zoneRT.pivot = mediaRT.pivot;
            zoneRT.anchoredPosition = mediaRT.anchoredPosition;
            zoneRT.sizeDelta = mediaRT.sizeDelta;
            zoneRT.localScale = mediaRT.localScale;

            // Render cards above the media display
            zoneGO.transform.SetSiblingIndex(mediaDisplay.transform.GetSiblingIndex() + 1);

            contentZone = zoneRT;
            Debug.Log("ContentZoneController: created content zone as sibling of mediaDisplay");
        }

        if (contentZone == null)
        {
            Debug.LogError("ContentZoneController: no contentZone assigned and no mediaDisplay to fall back to. Cards will not appear!");
        }
    }

    /// <summary>
    /// Stores the timeline and audio reference for time-based tracking.
    /// Called by MediaPresentationSystem after parsing.
    /// </summary>
    public void SetTimeline(List<ContentCardEvent> events, AudioSource audio)
    {
        timeline = events;
        voiceAudio = audio;
        lastTriggeredIndex = -1;
        isPaused = false;

        Debug.Log($"ContentZoneController: Timeline set with {events.Count} events");
    }

    /// <summary>
    /// Coroutine that checks voiceAudio.time each frame and triggers cards.
    /// </summary>
    public IEnumerator TrackCardsByTime()
    {
        lastTriggeredIndex = -1;

        while (voiceAudio != null && voiceAudio.isPlaying)
        {
            if (!isPaused && timeline != null)
            {
                float currentTime = voiceAudio.time;

                for (int i = lastTriggeredIndex + 1; i < timeline.Count; i++)
                {
                    if (currentTime >= timeline[i].triggerTime)
                    {
                        Debug.Log($"Triggering card: {timeline[i].cardType} at {currentTime:F2}s");
                        ShowCard(timeline[i]);
                        lastTriggeredIndex = i;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            yield return null;
        }
    }

    /// <summary>
    /// Display a specific card. If another card is active, fast-hide it first.
    /// </summary>
    public void ShowCard(ContentCardEvent evt)
    {
        if (contentZone == null)
        {
            Debug.LogError("ContentZoneController: Cannot show card, contentZone is not set!");
            return;
        }

        if (hideAndShowCoroutine != null)
            StopCoroutine(hideAndShowCoroutine);

        hideAndShowCoroutine = StartCoroutine(HideAndShowSequence(evt));
    }

    private IEnumerator HideAndShowSequence(ContentCardEvent evt)
    {
        // If a card is already showing, fast-hide it first
        if (activeCard != null)
        {
            if (durationCoroutine != null)
            {
                StopCoroutine(durationCoroutine);
                durationCoroutine = null;
            }

            bool hideComplete = false;
            activeCard.OnHideComplete = () => hideComplete = true;
            activeCard.Hide(fast: true);

            while (!hideComplete)
                yield return null;

            if (activeCard != null)
            {
                Destroy(activeCard.gameObject);
                activeCard = null;
            }
        }

        // Create a GameObject and add the appropriate card component
        GameObject cardObj = new GameObject(
            evt.cardType + "Card",
            typeof(RectTransform),
            typeof(CanvasGroup));
        cardObj.transform.SetParent(contentZone, false);

        // Counter any mirror in the parent hierarchy so text reads left-to-right.
        // Compute the 2D determinant of the XY-plane portion of the parent's
        // localToWorld matrix — if it's negative, the parent chain contains a
        // reflection (from a negative scale OR a 180° Y-rotation), and we flip
        // the card's X scale to counter it.
        Matrix4x4 parentMatrix = contentZone.localToWorldMatrix;
        float det2D = parentMatrix.m00 * parentMatrix.m11 - parentMatrix.m01 * parentMatrix.m10;
        float xSign = det2D < 0f ? -1f : 1f;
        cardObj.transform.localScale = new Vector3(xSign, 1f, 1f);

        switch (evt.cardType)
        {
            case ContentCardType.Headline: activeCard = cardObj.AddComponent<HeadlineCard>(); break;
            case ContentCardType.Excerpt: activeCard = cardObj.AddComponent<ExcerptCard>(); break;
            case ContentCardType.Quote: activeCard = cardObj.AddComponent<QuoteCard>(); break;
            case ContentCardType.Stat: activeCard = cardObj.AddComponent<StatCard>(); break;
            case ContentCardType.Logo: activeCard = cardObj.AddComponent<LogoDisplay>(); break;
            case ContentCardType.BRoll: activeCard = cardObj.AddComponent<BRollDisplay>(); break;
            default:
                Debug.LogWarning($"Unknown card type: {evt.cardType}");
                Destroy(cardObj);
                yield break;
        }

        activeCard.Initialize(evt, cardAssets);
        activeCard.Show();

        // Start duration timer
        durationCoroutine = StartCoroutine(DurationTimer(evt.duration));
    }

    private IEnumerator DurationTimer(float duration)
    {
        yield return new WaitForSeconds(duration);
        HideCurrentCard();
    }

    /// <summary>Fade out the currently active card and destroy it.</summary>
    public void HideCurrentCard()
    {
        if (activeCard == null) return;

        if (durationCoroutine != null)
        {
            StopCoroutine(durationCoroutine);
            durationCoroutine = null;
        }

        activeCard.OnHideComplete = () =>
        {
            if (activeCard != null)
            {
                Destroy(activeCard.gameObject);
                activeCard = null;
            }
        };

        activeCard.Hide(fast: false);
    }

    /// <summary>Pause the card timeline and hide the active card.</summary>
    public void PauseTimeline()
    {
        isPaused = true;

        if (activeCard != null)
        {
            if (durationCoroutine != null)
            {
                StopCoroutine(durationCoroutine);
                durationCoroutine = null;
            }

            activeCard.OnHideComplete = () =>
            {
                if (activeCard != null)
                {
                    Destroy(activeCard.gameObject);
                    activeCard = null;
                }
            };

            activeCard.Hide(fast: true);
        }

        Debug.Log("ContentZoneController: Timeline paused (character centered)");
    }

    /// <summary>Resume the card timeline.</summary>
    public void ResumeTimeline()
    {
        isPaused = false;
        Debug.Log("ContentZoneController: Timeline resumed");
    }
}
