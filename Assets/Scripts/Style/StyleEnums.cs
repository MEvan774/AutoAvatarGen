namespace MugsTech.Style
{
    /// <summary>
    /// High-level visual register of a channel preset. Drives quick conditional
    /// styling decisions ("if register == Whimsical, allow random rotation") in
    /// places where reading individual numeric values is overkill.
    /// </summary>
    public enum StyleRegister
    {
        Whimsical,
        Casual,
        Balanced,
        Corporate,
        Serious,
    }

    /// <summary>
    /// How a card's entry direction is determined.
    /// CharacterFacing reads the live position of the character and chooses
    /// FromLeft/FromRight/FromBottom dynamically each card spawn.
    /// The other modes are fixed.
    /// </summary>
    public enum EntryDirectionMode
    {
        CharacterFacing,
        FromLeft,
        FromRight,
        FromBottom,
        FromTop,
    }

    /// <summary>
    /// Resolved entry direction passed to a ContentCard. The card uses this to
    /// pick a starting offset relative to its final position.
    /// </summary>
    public enum EntryDirection
    {
        FromLeft,
        FromRight,
        FromBottom,
        FromTop,
    }

    /// <summary>
    /// Easing curve options for the card entry animation.
    /// </summary>
    public enum EntryAnimationCurve
    {
        Elastic,    // overshoot bounce
        EaseOut,    // smooth deceleration
        EaseOutBack,// small overshoot
        Linear,     // constant velocity
    }
}
