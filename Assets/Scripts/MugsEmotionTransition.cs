using System;
using System.Collections;
using UnityEngine;

// NOTE: CubicBezier is defined once in Assets/Scripts/Transitions/ScreenTransitionController.cs
// (global namespace) and reused here — do NOT add a second definition or it won't compile.

public enum EmotionTransition { Cut, Blink, BlinkHeavy }

[DisallowMultipleComponent]
public class MugsEmotionTransition : MonoBehaviour
{
    [Header("Default style (pick the option here)")]
    public EmotionTransition defaultStyle = EmotionTransition.Blink;

    [Header("Blink (1x baseline — matches the approved preview)")]
    public float blinkClose = 0.085f;
    public float blinkOpen = 0.10f;
    [Range(0f, 0.2f)] public float blinkClosedScaleY = 0.06f;

    [Header("Heavy slow-blink (sells the exhaustion)")]
    public float heavyClose = 0.14f;
    public float heavyHold = 0.09f;   // eyes held shut
    public float heavyOpen = 0.16f;
    [Range(0f, 0.2f)] public float heavyClosedScaleY = 0.05f;

    static readonly CubicBezier EaseIn  = new CubicBezier(0.42f, 0f, 1f, 1f);  // close
    static readonly CubicBezier EaseOut = new CubicBezier(0f, 0f, 0.58f, 1f);  // open

    Vector3 baseScale;
    Coroutine running;

    void Awake() { baseScale = transform.localScale; }

    /// <summary>
    /// Change Mugs's expression with the selected transition. `applyExpression` performs the actual
    /// sprite/animator swap — it is invoked at the fully-closed instant (for Cut, immediately).
    /// Pass `styleOverride` to override the default for this one change (e.g. Cut on a deadpan beat).
    /// Safe to call again mid-blink. Does NOT pause audio.
    /// </summary>
    public void ChangeEmotion(Action applyExpression, EmotionTransition? styleOverride = null)
    {
        EmotionTransition style = styleOverride ?? defaultStyle;

        if (style == EmotionTransition.Cut)
        {
            CancelToRest();
            applyExpression?.Invoke();
            return;
        }

        if (running != null) CancelToRest();          // restore rest pose, keep captured baseScale
        else baseScale = transform.localScale;         // capture rest only when idle

        running = StartCoroutine(style == EmotionTransition.BlinkHeavy
            ? Heavy(applyExpression)
            : Blink(applyExpression));
    }

    void CancelToRest()
    {
        if (running != null) { StopCoroutine(running); running = null; }
        transform.localScale = baseScale;
    }

    IEnumerator Blink(Action apply)
    {
        yield return ScaleY(1f, blinkClosedScaleY, blinkClose, EaseIn);
        apply?.Invoke();
        yield return ScaleY(blinkClosedScaleY, 1f, blinkOpen, EaseOut);
        transform.localScale = baseScale;
        running = null;
    }

    IEnumerator Heavy(Action apply)
    {
        yield return ScaleY(1f, heavyClosedScaleY, heavyClose, EaseIn);
        apply?.Invoke();
        if (heavyHold > 0f) yield return new WaitForSeconds(heavyHold);
        yield return ScaleY(heavyClosedScaleY, 1f, heavyOpen, EaseOut);
        transform.localScale = baseScale;
        running = null;
    }

    IEnumerator ScaleY(float from, float to, float dur, CubicBezier ease)
    {
        if (dur <= 0f) { SetY(to); yield break; }
        float t = 0f;
        SetY(from);
        while (t < dur)
        {
            t += Time.deltaTime;
            SetY(Mathf.Lerp(from, to, ease.Evaluate(Mathf.Clamp01(t / dur))));
            yield return null;
        }
        SetY(to);
    }

    void SetY(float yFactor)
    {
        transform.localScale = new Vector3(baseScale.x, baseScale.y * yFactor, baseScale.z);
    }
}
