using UnityEngine;
using UnityEngine.UI;

public class UIBloom : MonoBehaviour
{
    [Header("Target")]
    public Image targetImage;

    [Header("Bloom Color")]
    public Color bloomColor = new Color(0.4f, 0.5f, 1f, 1f);

    [Header("Bloom Layers")]
    public int layerCount = 6;
    public float scaleStart = 1.3f;
    public float scaleEnd = 4.0f;
    public float opacityStart = 0.5f;
    public float opacityEnd = 0.02f;

    [Header("Pulse")]
    public bool pulse = true;
    public float pulseSpeed = 1.2f;
    public float pulseAmount = 0.06f;

    private Image[] _layers;
    private Vector2 _baseSize;
    private Material _additiveMat;

    void Start()
    {
        if (_layers == null || _layers.Length == 0)
            BuildLayers();
    }

    void BuildLayers()
    {
        if (targetImage == null) return;

        // Cleanup old bloom children on the targetImage itself
        for (int i = targetImage.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = targetImage.transform.GetChild(i);
            if (child.name.StartsWith("_Bloom"))
                DestroyImmediate(child.gameObject);
        }

        _baseSize = targetImage.rectTransform.sizeDelta;
        _additiveMat = new Material(Shader.Find("Custom/UIBloomLayer"));
        _layers = new Image[layerCount];

        for (int idx = 0; idx < layerCount; idx++)
        {
            float t = layerCount == 1 ? 0f : (float)idx / (layerCount - 1);
            float scale = Mathf.Lerp(scaleStart, scaleEnd, t);
            float opacity = Mathf.Lerp(opacityStart, opacityEnd, t);

            _layers[idx] = CreateLayer("_Bloom" + idx, scale, opacity);

            // Behind the targetImage's own content but inside it
            _layers[idx].transform.SetSiblingIndex(idx);
        }
    }

    Image CreateLayer(string layerName, float scale, float opacity)
    {
        GameObject go = new GameObject(layerName);

        // Parent directly TO the targetImage, not its parent
        go.transform.SetParent(targetImage.transform, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero; // centered on parent
        rt.sizeDelta = _baseSize * scale;

        Image img = go.AddComponent<Image>();
        img.sprite = targetImage.sprite;
        img.type = targetImage.type;
        img.preserveAspect = targetImage.preserveAspect;
        img.useSpriteMesh = true;

        Color c = bloomColor;
        c.a = opacity;
        img.color = c;
        img.material = _additiveMat;

        return img;
    }

    void Update()
    {
        if (_layers == null) return;

        float pulse_t = pulse
            ? 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount
            : 1f;

        for (int idx = 0; idx < _layers.Length; idx++)
        {
            if (_layers[idx] == null) continue;
            float t = layerCount == 1 ? 0f : (float)idx / (layerCount - 1);
            float scale = Mathf.Lerp(scaleStart, scaleEnd, t) * pulse_t;

            // Size is relative to the targetImage's own size
            _layers[idx].rectTransform.sizeDelta = _baseSize * scale;
        }
    }
}