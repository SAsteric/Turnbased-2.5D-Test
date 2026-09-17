using UnityEngine;
using UnityEngine.UI;

// A UI Graphic that draws ONE arbitrary convex polygon.
// Used for the glass shards in the mirror-break transition.
public class ShardGraphic : Graphic
{
    private Vector2[] polygon;

    public void SetPolygon(Vector2[] localVerts)
    {
        polygon = localVerts;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (polygon == null || polygon.Length < 3) return;

        UIVertex v = UIVertex.simpleVert;
        for (int i = 0; i < polygon.Length; i++)
        {
            v.position = polygon[i];
            v.color = color;
            vh.AddVert(v);
        }
        for (int i = 1; i < polygon.Length - 1; i++)
            vh.AddTriangle(0, i, i + 1);
    }
}