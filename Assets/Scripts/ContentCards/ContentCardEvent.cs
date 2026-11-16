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
    Map,
    // The evidence-first stage (2026-11). All three are fullscreen feature cards.
    Clipping,       // {Clipping:"headline","phrase","source"} … {Clipping:End} — a pile of press clippings
    TimelineWide,   // {Timeline:"date","event",Wide} — the horizontal, full-screen timeline
    Graph           // {Graph:"title","label=value;…","source",duration|Start[,Bars|,Line|,Share][,Cropped]}
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

// The Graph card's chart, picked by an optional ",Bars" / ",Line" / ",Share"
// keyword after the tag's duration (Bars when there is none).
public enum GraphKind
{
    Bars,
    Line,
    Share
}

// What became of a {Mark:"phrase"} (or a logo strike): the card that is open
// answers, ContentZoneController logs the warning.
public enum MarkResult
{
    Ok,
    NotFound,   // the phrase isn't in the card's passage (it must match exactly)
    TooMany,    // the card already carries three marks
    Repeated,   // that phrase is already marked
}

/// <summary>A text card whose highlighter can move on: Document, Excerpt, the top sheet of a Clipping run.</summary>
public interface IMarkable
{
    /// <summary>Sweeps the marker over <paramref name="phrase"/>; the earlier highlight fades to a pale tint and stays.</summary>
    MarkResult AddMark(string phrase);
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

    // Quote only: ",Photo=image_name" — a round portrait beside the name and
    // role (an image file name, looked up the way {Image:} looks one up). Null = none.
    public string photoName;

    // BigImage only: ",Scroll" — a tall page fitted to the slot's width and
    // panned from its top to its bottom over the card's time on screen.
    public bool scroll;

    // {BigMedia:Strike,name} — crosses out a logo already in the open
    // line-by-line logo run. primaryText = the logo's name. Shows nothing itself.
    public bool strikesLogo;

    // {Mark:"phrase"} — moves the highlighter of the text card that is open
    // (Document, Excerpt, the top Clipping) on to another phrase.
    // primaryText = the phrase. Never a card of its own, never queued.
    public bool isMark;

    // {Timeline:Span,"text"} — the closing bracket and label of a wide
    // timeline. primaryText = the text. cardType = TimelineWide.
    public bool timelineSpan;

    // {Timeline:"date","event",Wide} — ",Wide" on a run's FIRST node makes it
    // the full-screen horizontal timeline (the parser then types the whole run
    // TimelineWide; on a later node the keyword is dropped with a warning).
    public bool timelineWide;

    // Where the tag sits in the script it was parsed from — set for the tags
    // whose order matters beyond their time (Timeline runs, Clipping, Mark).
    public int sourceIndex;

    // Graph only: the chart (",Bars" / ",Line" / ",Share"; Bars when the tag
    // has none) and the Line chart's ",Cropped" (its axis may start just under
    // the smallest value — the card then says so on screen).
    public GraphKind graphKind;
    public bool graphCropped;

    // The voice time at which this held card's {Tag:End} is due, when the
    // controller could find it in the timeline (NaN otherwise). A wide Quote
    // spreads its type-on up to it.
    public float endTime = float.NaN;

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
