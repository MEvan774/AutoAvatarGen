using UnityEngine;
using UnityEngine.UI;

// Gives the wipe panel its -7 degree skewed leading edge (UGUI has no built-in skew).
[RequireComponent(typeof(Graphic))]
public class UISkew : BaseMeshEffect
{
    public float angleDeg = -7f;

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        Rect r = graphic.rectTransform.rect;
        if (r.height <= 0f) return;
        float amt = Mathf.Tan(angleDeg * Mathf.Deg2Rad) * r.height;
        UIVertex v = default;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            float ny = (v.position.y - r.yMin) / r.height; // 0 bottom .. 1 top
            v.position.x += amt * (ny - 0.5f);
            vh.SetUIVertex(v, i);
        }
    }
}
