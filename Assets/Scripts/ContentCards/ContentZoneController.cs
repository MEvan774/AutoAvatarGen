using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages content card timeline playback in the content zone.
/// Instantiates card prefabs, handles one-card-at-a-time transitions,
/// and pauses/resumes when the character moves to/from center.
/// </summary>
public class ContentZoneController : MonoBehaviour
{
    [Header("Content Zone")]
    [Tooltip("RectTransform where cards are instantiated as children.")]
    public RectTransform contentZone;

    [Header("Card Prefabs")]
    public GameObject headlineCardPrefab;
    public GameObject excerptCardPrefab;
    public GameObject quoteCardPrefab;
    public GameObject statCardPrefab;
    public GameObject logoDisplayPrefab;
    public GameObject bRollDisplayPrefab;

    [Header("Assets")]
    public ContentCardAssets cardAssets;

    [Header("Media Coexistence")]
    [Tooltip("Reference to the existing media display — hidden while a card is active.")]
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

    /// <summary>
    /// True when a content card is currently visible (used by MediaPresentationSystem
    /// to avoid showing media while a card is active).
    /// </summary>
    public bool IsCardActive => activeCard != null;

    /// <summary>
    /// Called by MediaPresentationSystem after parsing content card tags.
    /// Stores the timeline and audio reference for time-based tracking.
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
    /// Same pattern as TrackMediaByTime in MediaPresentationSystem.
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

            // Wait for fast hide (0.15s)
            while (!hideComplete)
                yield return null;

            if (activeCard != null)
            {
                Destroy(activeCard.gameObject);
                activeCard = null;
            }
        }

        // Hide existing media display while card is showing
        if (mediaDisplay != null)
            mediaDisplay.gameObject.SetActive(false);

        // Instantiate the correct prefab
        GameObject prefab = GetPrefabForType(evt.cardType);
        if (prefab == null)
        {
            Debug.LogWarning($"No prefab assigned for card type: {evt.cardType}");
            yield break;
        }

        GameObject cardObj = Instantiate(prefab, contentZone);
        activeCard = cardObj.GetComponent<ContentCard>();

        if (activeCard == null)
        {
            Debug.LogError($"Prefab for {evt.cardType} is missing a ContentCard component!");
            Destroy(cardObj);
            yield break;
        }

        // Stretch to fill content zone
        RectTransform cardRect = cardObj.GetComponent<RectTransform>();
        cardRect.anchorMin = Vector2.zero;
        cardRect.anchorMax = Vector2.one;
        cardRect.offsetMin = Vector2.zero;
        cardRect.offsetMax = Vector2.zero;

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

    /// <summary>
    /// Fade out the currently active card and destroy it.
    /// </summary>
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

            // Re-enable media display when card is gone
            if (mediaDisplay != null)
                mediaDisplay.gameObject.SetActive(true);
        };

        activeCard.Hide(fast: false);
    }

    /// <summary>
    /// Pause the card timeline and hide the active card.
    /// Called when character moves to center position.
    /// </summary>
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

    /// <summary>
    /// Resume the card timeline.
    /// Called when character moves to a side position.
    /// </summary>
    public void ResumeTimeline()
    {
        isPaused = false;
        Debug.Log("ContentZoneController: Timeline resumed");
    }

    private GameObject GetPrefabForType(ContentCardType type)
    {
        switch (type)
        {
            case ContentCardType.Headline: return headlineCardPrefab;
            case ContentCardType.Excerpt: return excerptCardPrefab;
            case ContentCardType.Quote: return quoteCardPrefab;
            case ContentCardType.Stat: return statCardPrefab;
            case ContentCardType.Logo: return logoDisplayPrefab;
            case ContentCardType.BRoll: return bRollDisplayPrefab;
            default: return null;
        }
    }
}
