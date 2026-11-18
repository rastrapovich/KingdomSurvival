using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;

// Рисованная сетка боя (перо дорог MapLab): каждое общее ребро — один раз,
// рисунок штрихов не зависит от масштаба и сдвига кадра (нет мерцания при
// зуме), разрывы — только в середине ребра (углы клеток читаются), есть
// вариативность нажима между рёбрами.
public sealed class BattlefieldInkGridTests
{
    private static List<Vector2> Grid(float radius, Vector2 offset, float verticalScale, int width = 6, int height = 5)
    {
        List<Vector2> centers = new List<Vector2>();
        float dx = Mathf.Sqrt(3f) * radius, dy = 1.5f * radius * verticalScale;
        for (int r = 0; r < height; r++)
            for (int q = 0; q < width; q++)
                centers.Add(offset + new Vector2(q * dx + (r % 2) * dx * 0.5f, r * dy));
        return centers;
    }

    private static BattlefieldInkGrid.Options Options(float pixels = 1f) =>
        new BattlefieldInkGrid.Options { Opacity = 0.55f, PixelScale = pixels, Endings = true };

    [Test]
    public void SharedEdges_AreDrawnOnce()
    {
        const int w = 6, h = 5;
        BattlefieldInkGrid.Result result = BattlefieldInkGrid.Build(Grid(40f, Vector2.zero, 0.75f, w, h), 40f, 0.75f, null, new BattlefieldInkPen(), Options());
        // Уникальные рёбра шестиугольной решётки (смещённые ряды): 3·N + граница.
        int perCell = w * h * 6;
        Assert.Less(result.Edges, perCell * 0.62f, "Общие рёбра соседних клеток не дублируются.");
        Assert.Greater(result.Edges, w * h * 3, "Все рёбра нарисованы.");
    }

    [Test]
    public void Pattern_IsStable_UnderZoomAndPan()
    {
        BattlefieldInkPen pen = new BattlefieldInkPen();
        BattlefieldInkGrid.Result a = BattlefieldInkGrid.Build(Grid(40f, Vector2.zero, 0.75f), 40f, 0.75f, null, pen, Options());
        BattlefieldInkGrid.Result b = BattlefieldInkGrid.Build(Grid(90f, new Vector2(313f, -71f), 0.75f), 90f, 0.75f, null, pen, Options());
        Assert.AreEqual(a.Edges, b.Edges);
        Assert.AreEqual(a.Gaps, b.Gaps, "Разрывы те же при другом масштабе и сдвиге.");
        Assert.AreEqual(a.Endings, b.Endings);
        Assert.AreEqual(a.Strokes.Count, b.Strokes.Count);
        for (int i = 0; i < a.Strokes.Count; i++)
        {
            Assert.AreEqual(a.Strokes[i].Points.Count, b.Strokes[i].Points.Count);
            for (int k = 0; k < a.Strokes[i].Points.Count; k++)
            {
                Vector2 pa = a.Strokes[i].Points[k] / 40f, pb = (b.Strokes[i].Points[k] - new Vector2(313f, -71f)) / 90f;
                Assert.Less((pa - pb).magnitude, 1e-3f, "Форма штриха — та же в долях радиуса.");
                Assert.AreEqual(a.Strokes[i].Alphas[k], b.Strokes[i].Alphas[k], 1e-4f, "Пигмент — тот же.");
            }
        }
        BattlefieldInkGrid.Result again = BattlefieldInkGrid.Build(Grid(40f, Vector2.zero, 0.75f), 40f, 0.75f, null, pen, Options());
        Assert.AreEqual(a.Strokes[3].Points[2], again.Strokes[3].Points[2], "Повторная сборка — без случайности.");
    }

    [Test]
    public void Gaps_StayInsideEdges_AndLinesVary()
    {
        BattlefieldInkGrid.Result result = BattlefieldInkGrid.Build(Grid(40f, Vector2.zero, 1f, 10, 8), 40f, 1f, null, new BattlefieldInkPen(), Options());
        Assert.Greater(result.Gaps, 0, "Есть редкие разрывы.");
        Assert.Less(result.Gaps, result.Edges * 0.5f, "Разрывов не больше половины рёбер — клетки читаются.");
        Assert.Greater(result.Endings, 0, "У части разрывов — свободные окончания (§2.5).");
        // Толщина в середине рёбер различается — нет одинакового рисунка.
        HashSet<int> widths = new HashSet<int>();
        foreach (BattlefieldInkGrid.Stroke s in result.Strokes)
            if (s.Points.Count > 6)
                widths.Add(Mathf.RoundToInt(s.HalfWidths[s.Points.Count / 2] * 100f));
        Assert.Greater(widths.Count, 5, "Нажим различается от ребра к ребру.");
        // Все точки — в пределах решётки (дрожь руки небольшая).
        foreach (BattlefieldInkGrid.Stroke s in result.Strokes)
            foreach (float a in s.Alphas)
                Assert.That(a, Is.InRange(0f, 1f));
    }

    [Test]
    public void Width_StaysInPixelLimits()
    {
        BattlefieldInkPen pen = new BattlefieldInkPen();
        foreach (float radius in new[] { 8f, 40f, 400f })
        {
            BattlefieldInkGrid.Result result = BattlefieldInkGrid.Build(Grid(radius, Vector2.zero, 0.75f), radius, 0.75f, null, pen, Options());
            float max = 0f;
            foreach (BattlefieldInkGrid.Stroke s in result.Strokes)
                foreach (float half in s.HalfWidths)
                    max = Mathf.Max(max, half * 2f);
            // Нажим до +45 % от пера, пределы пера 1,6–6 px.
            Assert.LessOrEqual(max, pen.MaxPixels * (1f + pen.Drift) + 0.01f, "Радиус " + radius);
            Assert.GreaterOrEqual(max, pen.MinPixels * 0.9f, "Радиус " + radius);
        }
    }

    [Test]
    public void Triangulate_MakesFeatheredRibbon()
    {
        BattlefieldInkGrid.Result result = BattlefieldInkGrid.Build(Grid(40f, Vector2.zero, 0.75f, 2, 2), 40f, 0.75f, null, new BattlefieldInkPen(), Options());
        List<Vector2> positions = new List<Vector2>();
        List<float> alphas = new List<float>();
        List<int> indices = new List<int>();
        BattlefieldInkGrid.Triangulate(result.Strokes, 1f, positions, alphas, indices);
        Assert.Greater(positions.Count, 0);
        Assert.AreEqual(0, indices.Count % 3);
        Assert.AreEqual(0f, alphas[0], "Край ленты растушёван в ноль.");
        Assert.Greater(alphas[1], 0f, "Середина — пигмент.");
        foreach (int i in indices)
            Assert.That(i, Is.InRange(0, positions.Count - 1));
    }
}
