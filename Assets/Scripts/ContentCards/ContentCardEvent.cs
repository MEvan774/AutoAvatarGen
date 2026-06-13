using System.Collections.Generic;
using MugsTech.Style;

public enum ContentCardType
{
    Headline,
    Excerpt,
    Quote,
    Stat,
    Logo,
    BRoll,
    BigMedia,
    BigCenter,
    BigText,
    BigImage
}

[System.Serializable]
public class ContentCardEvent
{
    public float triggerTime;
    public float duration;
    public ContentCardType cardType;

    // Each card type maps its parameters into these fields:
    //   Headline:  primaryText=headline, secondaryText=source
    //   Excerpt:   primaryText=full text, secondaryText=highlighted phrase, tertiaryText=source
    //   Quote:     primaryText=quote, secondaryText=person name, tertiaryText=role/title
    //   Stat:      primaryText=number, secondaryText=label, tertiaryText=context
    //   Logo:      primaryText=company name
    //   BRoll:     primaryText=description
    //   BigMedia:  primaryText=logo or image name (logo lookup first, falls back to Resources/Media sprite)
    //   BigCenter: primaryText=headline, secondaryText=source (Headline tag with ",bigCenter" modifier)
    //   BigText:   primaryText=text or '+'-joined texts (e.g. "Line One+Line Two+Line Three")
    //   BigImage:  primaryText=image name (left-3/4 article/headline screenshot; same lookup as {Image:})
    public string primaryText;
    public string secondaryText;
    public string tertiaryText;

    // Side cards (Headline/Excerpt/Quote/Stat/Logo/BRoll) accept an optional
    // trailing ",Left" / ",Right" that forces the side the card flies in from.
    // null = no per-tag choice; fall back to the CardEntryAnimator's per-card
    // default (Left) / runtime direction. See ContentCard.SetDirectionOverride.
    public EntryDirection? entryDirectionOverride;
}
