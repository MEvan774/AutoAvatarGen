using UnityEngine;
using UnityEngine.UI;

public class FloatingShape : MonoBehaviour
{
    [Header("Motion")]
    public float driftSpeed = 0.04f;
    public float rotateSpeed = 4f;
    public float bobAmplitude = 0.08f;
    public float bobFrequency = 0.4f;

    [Header("Fade")]
    public float opacity = 0.18f;

    private Vector3 startPos;
    private float timeOffset;
    private Image sr;

    void Start()
    {
        startPos = transform.position;
        timeOffset = Random.Range(0f, 100f);
        sr = GetComponent<Image>();
        Color c = sr.color;
        c.a = opacity;
        sr.color = c;
    }

    void Update()
    {
        float t = Time.time + timeOffset;
        transform.position = startPos + new Vector3(
            Mathf.Sin(t * bobFrequency * 0.7f) * bobAmplitude,
            Mathf.Sin(t * bobFrequency) * bobAmplitude,
            0);
        transform.Rotate(0, 0, rotateSpeed * Time.deltaTime);
    }
}