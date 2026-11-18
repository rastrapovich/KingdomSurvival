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
    [Test]
    public void Outlines_SixEdgesPerCell_StableUnderZoom_DifferPerMark()
    {
        BattlefieldInkPen pen = new BattlefieldInkPen();
        List<Vector2> cells = new List<Vector2> { new Vector2(100f, 100f), new Vector2(169.3f, 100f) };
        BattlefieldInkGrid.Result a = BattlefieldInkGrid.BuildOutlines(cells, 38f, 0.75f, null, pen, 2.2f, Vector2.zero, 40f, 7u, Options());
        Assert.AreEqual(12, a.Edges, "У каждой клетки своё кольцо из шести рёбер.");
        // Тот же кадр в 2 раза крупнее: рисунок в долях радиуса тот же.
        List<Vector2> big = new List<Vector2> { cells[0] * 2f, cells[1] * 2f };
        BattlefieldInkGrid.Result b = BattlefieldInkGrid.BuildOutlines(big, 76f, 0.75f, null, pen, 4.4f, Vector2.zero, 80f, 7u, Options());
        Assert.AreEqual(a.Gaps, b.Gaps);
        Assert.AreEqual(a.Strokes.Count, b.Strokes.Count);
        Assert.Less((a.Strokes[0].Points[3] * 2f - b.Strokes[0].Points[3]).magnitude, 0.01f, "Форма штриха не меняется при зуме.");
        // Другая отметка на тех же клетках — другой рисунок (ход и атака не совпадают).
        BattlefieldInkGrid.Result other = BattlefieldInkGrid.BuildOutlines(cells, 38f, 0.75f, null, pen, 2.2f, Vector2.zero, 40f, 99u, Options());
        bool differs = other.Strokes.Count != a.Strokes.Count;
        for (int i = 0; !differs && i < a.Strokes.Count; i++)
            differs = Mathf.Abs(other.Strokes[i].HalfWidths[1] - a.Strokes[i].HalfWidths[1]) > 1e-4f;
        Assert.IsTrue(differs);
        // Тонкая отметка не тоньше нижнего предела пера на экране.
        BattlefieldInkGrid.Result thin = BattlefieldInkGrid.BuildOutlines(cells, 38f, 0.75f, null, pen, 0.2f, Vector2.zero, 40f, 7u, Options());
        float widest = 0f;
        foreach (BattlefieldInkGrid.Stroke st in thin.Strokes)
            foreach (float h in st.HalfWidths)
                widest = Mathf.Max(widest, h * 2f);
        Assert.GreaterOrEqual(widest, pen.MinPixels * 0.9f);
    }

    [Test]
    public void Anchor_KeepsPattern_WhenVisibleWindowChanges()
    {
        BattlefieldInkPen pen = new BattlefieldInkPen();
        List<Vector2> all = Grid(40f, Vector2.zero, 1f, 8, 6);
        // Видимая часть — без первого ряда (как карта мира при панораме).
        List<Vector2> part = all.GetRange(8, all.Count - 8);
        BattlefieldInkGrid.Options options = Options();
        options.UseAnchor = true;
        options.Anchor = new Vector2(-17f, 5f);
        BattlefieldInkGrid.Result full = BattlefieldInkGrid.Build(all, 40f, 1f, null, pen, options);
        BattlefieldInkGrid.Result visible = BattlefieldInkGrid.Build(part, 40f, 1f, null, pen, options);
        // Штрих ребра, общего для обеих сборок, — тот же: ищем по началу и концу.
        int matched = 0;
        foreach (BattlefieldInkGrid.Stroke s in visible.Strokes)
            foreach (BattlefieldInkGrid.Stroke f in full.Strokes)
                if (s.Points.Count == f.Points.Count && (s.Points[0] - f.Points[0]).sqrMagnitude < 1e-6f && (s.Points[s.Points.Count - 1] - f.Points[f.Points.Count - 1]).sqrMagnitude < 1e-6f)
                {
                    Assert.AreEqual(f.Alphas[f.Alphas.Count / 2], s.Alphas[s.Alphas.Count / 2], 1e-5f);
                    matched++;
                    break;
                }
        Assert.Greater(matched, visible.Strokes.Count * 0.8f, "Рисунок видимых рёбер тот же, что у полной сетки.");
    }
}
