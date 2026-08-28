using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-tag sound effects. Every visual tag in the script can trigger a sound
/// effect with an optional start delay and per-cue volume, all assigned in the
/// Inspector — media ({Image}, {Video}), the black cut ({Black}), camera zoom
/// ({Zoom:In/Out/Reset/Pullback}), character position ({Position:Left/Right/
/// Center}), and every content / feature card ({Headline} … {BigText}).
///
/// SETUP (mirrors CardEntryAnimator): add this component to the
/// ContentZoneController / MediaPresentationSystem GameObject so the per-tag list
/// is tweakable in the Inspector, then drag an AudioClip onto each tag you want
/// to sound. Tags left without a clip are silent. If no TagSfxPlayer exists when
/// the first tag fires, one is auto-created with an all-empty (silent) list so
/// nothing ever throws.
///
/// One AudioSource plays every cue via PlayOneShot, so overlapping tags layer
/// instead of cutting each other off. Playback is fire-and-forget and fully
/// independent of the narration AudioSource — a {Video:} tag that pauses the
/// voice does not pause its own sound effect.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class TagSfxPlayer : MonoBehaviour
{
    private static TagSfxPlayer _instance;

    /// <summary>
    /// Singleton accessor. If no player exists in the scene, a default (silent)
    /// one is auto-created so trigger sites can always call <see cref="Play"/>.
    /// </summary>
    public static TagSfxPlayer Instance
    {
        get
        {
            if (_instance != null) return _instance;

            _instance = FindObjectOfType<TagSfxPlayer>();
            if (_instance != null) return _instance;

            GameObject go = new GameObject("TagSfxPlayer (auto)");
            _instance = go.AddComponent<TagSfxPlayer>();
            return _instance;
        }
    }

    [Serializable]
    public class TagSfxEntry
    {
        [Tooltip("Which visual tag this sound effect plays for.")]
        public TagSfxEvent tagEvent;

        [Tooltip("Sound effect to play when this tag fires. Leave empty for no sound.")]
        public AudioClip clip;

        [Tooltip("Seconds to wait after the tag fires before the sound plays. 0 = play immediately.")]
        [Min(0f)]
        public float delay = 0f;

        [Tooltip("Playback volume for this cue (0 = silent, 1 = full).")]
        [Range(0f, 1f)]
        public float volume = 1f;
    }

    [Header("Per-Tag Sound Effects")]
    [Tooltip("One row per visual tag. Drag a clip onto the tags you want to sound, set an " +
             "optional delay / volume, and leave the rest empty. Tags with no clip stay silent.")]
    public List<TagSfxEntry> entries = new List<TagSfxEntry>();

    private AudioSource source;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }
        _instance = this;

        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake  = false;
        source.spatialBlend = 0f; // 2D — non-positional SFX, captured by the recorder

        EnsureAllEventsPresent();
    }

    void Reset()
    {
        entries.Clear();
        EnsureAllEventsPresent();
    }

    void OnValidate()
    {
        EnsureAllEventsPresent();
    }

    // Adds a row for any tag event missing from the list so the Inspector always
    // shows every taggable visual. Existing rows — and the user's clip / delay /
    // volume edits — are left untouched.
    private void EnsureAllEventsPresent()
    {
        // Drop any stale rows whose event no longer exists in the enum (e.g. the
        // old Transition* events, now owned by ScreenTransitionController), so they
        // don't linger as broken entries in the Inspector.
        entries.RemoveAll(e => e == null || !Enum.IsDefined(typeof(TagSfxEvent), e.tagEvent));

        var present = new HashSet<TagSfxEvent>();
        foreach (var e in entries)
            if (e != null) present.Add(e.tagEvent);

        foreach (TagSfxEvent ev in Enum.GetValues(typeof(TagSfxEvent)))
        {
            if (present.Contains(ev)) continue;
            entries.Add(new TagSfxEntry { tagEvent = ev });
        }
    }

    // -----------------------------------------------------------------------
    // Play
    // -----------------------------------------------------------------------

    /// <summary>
    /// Play the configured sound for a visual tag event. No-op if no row exists,
    /// the row has no clip, or the audio source is missing.
    /// </summary>
    public void Play(TagSfxEvent ev)
    {
        TagSfxEntry entry = GetEntry(ev);
        if (entry == null || entry.clip == null || source == null) return;

        if (entry.delay > 0f)
            StartCoroutine(PlayDelayed(entry.clip, entry.delay, entry.volume));
        else
            source.PlayOneShot(entry.clip, entry.volume);
    }

    private IEnumerator PlayDelayed(AudioClip clip, float delay, float volume)
    {
        yield return new WaitForSeconds(delay);
        if (source != null && clip != null)
            source.PlayOneShot(clip, volume);
    }

    private TagSfxEntry GetEntry(TagSfxEvent ev)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            TagSfxEntry e = entries[i];
            if (e != null && e.tagEvent == ev) return e;
        }
        return null;
    }

    // -----------------------------------------------------------------------
    // Typed convenience overloads — map the existing tag enums to TagSfxEvent so
    // the trigger sites stay terse (TagSfxPlayer.Instance.Play(type)).
    // -----------------------------------------------------------------------

    public void Play(MediaType t)
        => Play(t == MediaType.VIDEO ? TagSfxEvent.Video : TagSfxEvent.Image);

    public void Play(ZoomType t)
    {
        switch (t)
        {
            case ZoomType.In:       Play(TagSfxEvent.ZoomIn);       break;
            case ZoomType.Out:      Play(TagSfxEvent.ZoomOut);      break;
            case ZoomType.Reset:    Play(TagSfxEvent.ZoomReset);    break;
            case ZoomType.Pullback: Play(TagSfxEvent.ZoomPullback); break;
            // Only the punch in is scored — the cut back out stays silent.
            case ZoomType.ExtremeIn: Play(TagSfxEvent.ZoomExtreme);  break;
        }
    }

    public void Play(CharacterPosition p)
    {
        switch (p)
        {
            case CharacterPosition.Left:   Play(TagSfxEvent.PositionLeft);   break;
            case CharacterPosition.Right:  Play(TagSfxEvent.PositionRight);  break;
            case CharacterPosition.Center: Play(TagSfxEvent.PositionCenter); break;
        }
    }

    // NOTE: Whole-screen transition SFX are NOT handled here — they live on
    // MediaPresentationSystem (its per-transition cover/in and reveal/out clip
    // slots) so each clip plays exactly as its half starts. Keeping them off
    // this shared list avoids a second place that could double-trigger the
    // same sound.

    public void Play(ContentCardType c)
    {
        switch (c)
        {
            case ContentCardType.Headline:  Play(TagSfxEvent.Headline);  break;
            case ContentCardType.Excerpt:   Play(TagSfxEvent.Excerpt);   break;
            case ContentCardType.Quote:     Play(TagSfxEvent.Quote);     break;
            case ContentCardType.Stat:      Play(TagSfxEvent.Stat);      break;
            case ContentCardType.Logo:      Play(TagSfxEvent.Logo);      break;
            case ContentCardType.BRoll:     Play(TagSfxEvent.BRoll);     break;
            case ContentCardType.BigMedia:  Play(TagSfxEvent.BigMedia);  break;
            case ContentCardType.BigCenter: Play(TagSfxEvent.BigCenter); break;
            case ContentCardType.BigText:   Play(TagSfxEvent.BigText);   break;
        }
    }
}

/// <summary>
/// Every visual tag that can carry a sound effect. Mirrors the tag set in the
/// authoring guide: media, the black cut, camera zoom, character position, and
/// all content / feature cards.
/// </summary>
public enum TagSfxEvent
{
    // Media
    Image,
    Video,
    // Black cut
    Black,
    // Camera zoom
    ZoomIn,
    ZoomOut,
    ZoomReset,
    ZoomPullback,
    // Character position
    PositionLeft,
    PositionRight,
    PositionCenter,
    // Content / feature cards
    Headline,
    Excerpt,
    Quote,
    Stat,
    Logo,
    BRoll,
    BigMedia,
    BigCenter,
    BigText,

    // Appended last, never inserted above: rows are serialized by integer value,
    // so renumbering an existing event would silently re-assign its wired clip.
    ZoomExtreme,   // {Zoom:Extreme} — leave empty for a silent punch-in.
}
