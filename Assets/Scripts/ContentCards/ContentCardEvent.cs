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
    BigImage,
    // Added at the END so the scene's serialized per-type rows (by number) keep their meaning.
    Compare,
    Versus,
    Document,
    Timeline,
    Map
}

// The Document card's stamp, picked by an optional ",Allegation" / ",Filing" /
// ",Ruling" / ",Report" keyword after the tag's duration. The stamp's words
// come from the keyword (DocumentTypography.StampWords), never from the script.
public enum DocumentStamp
{
    None,
    Allegation,
    Filing,
    Ruling,
    Report
}

// The Stat card's win/loss arrow, chosen by an optional ",Win" / ",Loss" /
// ",NoArrow" keyword after the tag's duration. None is also what a tag with no
// keyword gets — the arrow is never guessed from the card's text.
public enum StatArrow
{
    None,
    Win,
    Loss
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
    //   Document:  primaryText=passage, secondaryText=highlighted phrase, tertiaryText=source
    //   Timeline:  primaryText=date, secondaryText=event (one node; the first opens the card)
    //   Map:       fields=region, countries, caption, source (primaryText=caption)
    public string primaryText;
    public string secondaryText;
    public string tertiaryText;

    // Cards with more than three text fields keep all of them here, in tag
    // order (Compare: value A, label A, value B, label B, source;
    // Versus: left label, left text, right label, right text;
    // Map: region, countries, caption, source).
    public string[] fields;

    // Versus only: the ",Vs" keyword — the glyph between the panels reads VS
    // instead of ≠.
    public bool versusVs;

    // Document only: the stamp keyword (None = no stamp).
    public DocumentStamp documentStamp;

    // Side cards (Headline/Excerpt/Quote/Stat/Logo/BRoll) accept an optional
    // trailing ",Left" / ",Right" that forces the side the card flies in from.
    // null = no per-tag choice; fall back to the CardEntryAnimator's per-card
    // default (Left) / runtime direction. See ContentCard.SetDirectionOverride.
    public EntryDirection? entryDirectionOverride;

    // Stat only: the arrow picked by the tag's ",Win" / ",Loss" / ",NoArrow"
    // keyword. Win = green up arrow, Loss = red down arrow, None = no arrow.
    public StatArrow statArrow;

    // The closing edge of a held pair — {BigText:End} for the persistent
    // BigText stack, or {Headline:End}/{Quote:End}/{Logo:End}/… for a side
    // card (or a ",bigCenter" headline) opened with ",Start" in its duration slot. The event shows nothing;
    // ContentZoneController closes the active card of the same type. See
    // ContentZoneTagParser.ExtractBigTexts / ExtractCardEnds.
    // A duration-less (duration <= 0) event WITHOUT this flag is the held
    // opening edge: it stays up with no timer until its End tag arrives (for
    // BigText, later duration-less lines append to the stack instead).
    public bool dismissesCard;
}
