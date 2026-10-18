using UnityEngine;

namespace MugsTech.Style
{
    /// <summary>
    /// Pins a button's <see cref="UITheme"/> role instead of letting it be
    /// inferred from the GameObject name (e.g. toolbar buttons that should stay
    /// Neutral even though their name reads as an action).
    /// </summary>
    [DisallowMultipleComponent]
    public class UIRole : MonoBehaviour
    {
        public enum Role { Accent, Positive, Destructive, Neutral }

        public Role role;
    }
}
