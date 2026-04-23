using System.Collections.Generic;

public enum ContentCardType
{
    Headline,
    Excerpt,
    Quote,
    Stat,
    Logo,
    BRoll,
    BigMedia
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
    public string primaryText;
    public string secondaryText;
    public string tertiaryText;
}
