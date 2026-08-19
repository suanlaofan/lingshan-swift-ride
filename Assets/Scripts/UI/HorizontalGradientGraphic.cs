using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class HorizontalGradientGraphic : MaskableGraphic
{
    public Color LeftColor = new Color(0.46f, 0.035f, 0.02f, 0.78f);
    public Color RightColor = new Color(0.16f, 0.015f, 0.01f, 0f);

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();

        Rect bounds = rectTransform.rect;
        AddVertex(vertexHelper, new Vector2(bounds.xMin, bounds.yMin), LeftColor);
        AddVertex(vertexHelper, new Vector2(bounds.xMin, bounds.yMax), LeftColor);
        AddVertex(vertexHelper, new Vector2(bounds.xMax, bounds.yMax), RightColor);
        AddVertex(vertexHelper, new Vector2(bounds.xMax, bounds.yMin), RightColor);

        vertexHelper.AddTriangle(0, 1, 2);
        vertexHelper.AddTriangle(0, 2, 3);
    }

    private static void AddVertex(VertexHelper vertexHelper, Vector2 position, Color color)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vertexHelper.AddVert(vertex);
    }
}
