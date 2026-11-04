using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Свободные окончания пера (MAP_STYLE_GUIDE.md §2.5): только у подлинных
    // свободных концов видимого следа; технические концы, замкнутый шов и
    // примыкание не украшаются; хвосты не встречаются и не выходят за
    // траекторию; рисунок стабилен по seed; география не меняется.
    public sealed class MapLabInkEndingsTests
    {
        private static MapLabInkEndings.Options Options(uint seed = 7u, float unit = 0.13f) =>
            new MapLabInkEndings.Options { Unit = unit, Variety = 0.6f, Seed = seed };

        private static void Line(float length, float step, System.Func<float, float> alphaAt, out List<Vector2> points, out List<float> arc, out List<float> alpha)
        {
            points = new List<Vector2>();
            arc = new List<float>();
            alpha = new List<float>();
            for (float s = 0f; s <= length + 1e-4f; s += step)
            {
                points.Add(new Vector2(3f + s, 5f + Mathf.Sin(s * 0.3f)));
                alpha.Add(alphaAt(s));
            }
            arc.Add(0f);
            for (int i = 1; i < points.Count; i++)
                arc.Add(arc[i - 1] + Vector2.Distance(points[i - 1], points[i]));
        }

        [Test]
        public void OpenLineFullyVisible_TechnicalEnds_NoEndings()
        {
            Line(20f, 0.2f, s => 1f, out var p, out var arc, out var a);
            MapLabInkEndings.Plan plan = MapLabInkEndings.Build(p, arc, a, false, 1f, Options());
            Assert.AreEqual(0, plan.Considered, "край массива (край карты) — технический конец");
            Assert.AreEqual(0, plan.Elements.Count);
            Assert.AreEqual(0, plan.Cuts.Count);
        }

        [Test]
        public void InnerGap_BothEnds_TailsInsideTrajectory_NoOverlap()
        {
            int decorated = 0;
            for (uint seed = 1; seed <= 40; seed++)
            {
                Line(20f, 0.2f, s => s > 9f && s < 11f ? 0f : 1f, out var p, out var arc, out var a);
                MapLabInkEndings.Plan plan = MapLabInkEndings.Build(p, arc, a, false, 1f, Options(seed));
                Assert.AreEqual(2, plan.Considered, "просвет внутри линии — два свободных конца");
                decorated += plan.Decorated;
                float total = arc[arc.Count - 1];
                float endZone = float.MinValue, startZone = float.MaxValue;
                foreach (MapLabInkEndings.Cut c in plan.Cuts)
                {
                    Assert.IsTrue(c.Dir > 0 ? c.S < arc[45] : c.S > arc[55], "обрыв основной линии — внутри видимого следа");
                    if (c.Dir > 0)
                        endZone = c.ZoneEnd;
                    else
                        startZone = c.ZoneEnd;
                }
                Assert.LessOrEqual(endZone, startZone + 1e-4f, "зоны двух завершений одного просвета пересекаются");
                List<MapLabInkEndings.Element> e = plan.Elements;
                e.Sort((x, y) => x.S0.CompareTo(y.S0));
                for (int i = 0; i < e.Count; i++)
                {
                    Assert.IsTrue(e[i].S0 >= 0f && e[i].S1 <= total, "элемент вне траектории");
                    Assert.IsFalse(float.IsNaN(e[i].S0) || float.IsNaN(e[i].Alpha) || float.IsNaN(e[i].Width), "NaN в завершении");
                    Assert.Greater(e[i].S1, e[i].S0 - 1e-6f);
                    if (i > 0)
                        Assert.GreaterOrEqual(e[i].S0, e[i - 1].S1 - 1e-4f, "элементы завершений перекрываются");
                }
            }
            Assert.Greater(decorated, 40, "большая часть подходящих концов получает завершение");
        }

        [Test]
        public void SameInput_SameEndings_SeedChangesVariant()
        {
            Line(30f, 0.2f, s => (s > 8f && s < 10f) || (s > 19f && s < 21f) ? 0f : 1f, out var p, out var arc, out var a);
            MapLabInkEndings.Plan x = MapLabInkEndings.Build(p, arc, a, false, 1f, Options(11u));
            MapLabInkEndings.Plan y = MapLabInkEndings.Build(p, arc, a, false, 1f, Options(11u));
            Assert.AreEqual(x.Elements.Count, y.Elements.Count);
            for (int i = 0; i < x.Elements.Count; i++)
            {
                Assert.AreEqual(x.Elements[i].S0, y.Elements[i].S0);
                Assert.AreEqual(x.Elements[i].Width, y.Elements[i].Width);
                Assert.AreEqual(x.Elements[i].Kind, y.Elements[i].Kind);
            }
            bool differs = false;
            for (uint seed = 12; seed < 30 && !differs; seed++)
            {
                MapLabInkEndings.Plan z = MapLabInkEndings.Build(p, arc, a, false, 1f, Options(seed));
                differs = z.Elements.Count != x.Elements.Count || (z.Elements.Count > 0 && Mathf.Abs(z.Elements[0].S0 - x.Elements[0].S0) > 1e-4f);
            }
            Assert.IsTrue(differs, "окончания не повторяют один шаблон");
        }

        [Test]
        public void ShortTrace_KeepsPlainLostEnd()
        {
            // Видимый след короче шести единиц завершения — без хвоста.
            Line(20f, 0.1f, s => s > 9.7f && s < 10.3f ? 1f : 0f, out var p, out var arc, out var a);
            MapLabInkEndings.Plan plan = MapLabInkEndings.Build(p, arc, a, false, 1f, Options());
            Assert.AreEqual(2, plan.Considered);
            Assert.AreEqual(0, plan.Decorated);
        }

        [Test]
        public void Adjacency_NoEnding()
        {
            // След гаснет у полотна дороги (занятые вершины сразу за концом).
            Line(20f, 0.2f, s => s > 9f && s < 11f ? 0f : 1f, out var p, out var arc, out var a);
            List<bool> hold = new List<bool>();
            for (int i = 0; i < p.Count; i++)
                hold.Add(i * 0.2f > 9.2f && i * 0.2f < 10.8f);
            MapLabInkEndings.Plan plan = MapLabInkEndings.Build(p, arc, a, false, 1f, Options(), hold);
            Assert.AreEqual(0, plan.Decorated, "примыкание — не свободный конец");
            Assert.AreEqual(0, plan.Elements.Count);
        }

        [Test]
        public void FreeStroke_TailStaysInsideStroke()
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                Line(3f, 0.2f, s => 0.8f, out var p, out var arc, out var a);
                MapLabInkEndings.Plan plan = MapLabInkEndings.Build(p, arc, a, true, 1f, Options(seed));
                Assert.AreEqual(2, plan.Considered, "оба конца штриха борозды — свободные");
                foreach (MapLabInkEndings.Element e in plan.Elements)
                    Assert.IsTrue(e.S0 >= -1e-4f && e.S1 <= arc[arc.Count - 1] + 1e-4f, "хвост вышел за штрих");
            }
        }

        [Test]
        public void RepeatedPoints_NoNaN()
        {
            List<Vector2> p = new List<Vector2>();
            List<float> a = new List<float>();
            for (int i = 0; i < 120; i++)
            {
                Vector2 q = new Vector2(i / 2 * 0.2f, 0f); // каждая точка повторена
                p.Add(q);
                a.Add(i > 50 && i < 70 ? 0f : 1f);
            }
            List<float> arc = new List<float> { 0f };
            for (int i = 1; i < p.Count; i++)
                arc.Add(arc[i - 1] + Vector2.Distance(p[i - 1], p[i]));
            MapLabInkEndings.Plan plan = MapLabInkEndings.Build(p, arc, a, false, 1f, Options());
            foreach (MapLabInkEndings.Element e in plan.Elements)
            {
                Vector2 q = MapLabInkEndings.PointAt(p, arc, (e.S0 + e.S1) * 0.5f);
                Assert.IsFalse(float.IsNaN(q.x) || float.IsNaN(e.S0) || float.IsNaN(e.Alpha), "NaN на совпавших точках");
            }
        }

        private static MapLabSettings Settings()
        {
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.RiverValley);
            s.General.Quality = MapLabQuality.Samples129;
            s.Stage03.Look = MapLabLook.Stage05;
            return s;
        }

        [Test]
        public void ClosedRing_SeamIsNotAnEnd_GapGetsTwoEnds()
        {
            MapLabSettings s = Settings();
            try
            {
                MapLabData d = new MapLabData(65, 64f);
                int n = 33;
                float[] field = new float[n * n];
                // Стиль без художественных разрывов — видимость задают веса.
                MapLabInkStyle style = MapLabInk.Style(s);
                style.BreakFrequency = -1f;
                style.Weights = Vector4.one;
                MapLabInk.Contour Ring(System.Func<float, float> weight)
                {
                    MapLabInk.Contour c = new MapLabInk.Contour { Closed = true };
                    for (int i = 0; i <= 200; i++)
                    {
                        float t = i / 200f * Mathf.PI * 2f;
                        c.Points.Add(new Vector2(32f + Mathf.Cos(t) * 10f, 32f + Mathf.Sin(t) * 10f));
                        c.Weights.Add(weight(t));
                    }
                    return c;
                }
                // Целое кольцо: ни одного конца, в том числе на нулевом индексе.
                Mesh full = MapLabInk.BuildRibbon(d, s, new List<MapLabInk.Contour> { Ring(t => 1f) }, field, n, "test", out MapLabInk.Stats fs, style, 2f);
                Assert.AreEqual(0, fs.FreeEnds, "шов замкнутого контура — не конец");
                Assert.AreEqual(0, fs.EndingElements);
                Object.DestroyImmediate(full);
                // Один просвет (около π): два конца, шов массива — в просвете.
                Mesh gap = MapLabInk.BuildRibbon(d, s, new List<MapLabInk.Contour> { Ring(t => Mathf.Abs(t - Mathf.PI) < 0.3f ? 0f : 1f) }, field, n, "test", out MapLabInk.Stats gs, style, 2f);
                Assert.AreEqual(2, gs.FreeEnds);
                foreach (Vector3 v in gap.vertices)
                    Assert.IsFalse(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z), "NaN в ленте");
                Object.DestroyImmediate(gap);
                // Выключено — прежняя лента без завершений.
                s.Stage05.InkEndings = false;
                Mesh off = MapLabInk.BuildRibbon(d, s, new List<MapLabInk.Contour> { Ring(t => Mathf.Abs(t - Mathf.PI) < 0.3f ? 0f : 1f) }, field, n, "test", out MapLabInk.Stats os, style, 2f);
                Assert.AreEqual(0, os.FreeEnds);
                Assert.AreEqual(200 * 2, os.Vertices);
                Object.DestroyImmediate(off);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void RealMap_EndingsStable_GeographyUnchanged()
        {
            MapLabSettings s = Settings();
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                MapLabPalette palette = MapLabPalette.CreateFallback();
                MapLabPaintFields fields = new MapLabPaintFields(d, s, palette);
                Object.DestroyImmediate(palette);
                float hBefore = Sum(d.Heights), rBefore = Sum(d.RoadDistance);
                s.Stage05.InkEndings = true;
                Mesh a = MapLabInk.BuildRoadInk(d, s, fields, out MapLabInk.Stats sa);
                Mesh b = MapLabInk.BuildRoadInk(d, s, fields, out MapLabInk.Stats sb);
                Assert.AreEqual(sa.Vertices, sb.Vertices, "одинаковые входы — одинаковые завершения");
                Vector3[] va = a.vertices, vb = b.vertices;
                for (int i = 0; i < va.Length; i++)
                {
                    Assert.AreEqual(va[i], vb[i]);
                    Assert.IsFalse(float.IsNaN(va[i].x) || float.IsNaN(va[i].y) || float.IsNaN(va[i].z), "NaN в пере дороги");
                }
                s.Stage05.InkEndings = false;
                Mesh c = MapLabInk.BuildRoadInk(d, s, fields, out MapLabInk.Stats sc);
                Assert.AreEqual(0, sc.EndingElements);
                Assert.AreEqual(sa.Polylines, sc.Polylines, "окончания не меняют сами линии");
                Assert.AreEqual(hBefore, Sum(d.Heights), "рельеф не меняется");
                Assert.AreEqual(rBefore, Sum(d.RoadDistance), "дороги не меняются");
                // Генерация с выключенным эффектом даёт ту же географию.
                MapLabData d2 = MapLabGenerator.Generate(s, null);
                Assert.AreEqual(hBefore, Sum(d2.Heights));
                Assert.AreEqual(rBefore, Sum(d2.RoadDistance));
                Assert.AreEqual(d.Objects.Count, d2.Objects.Count, "расстановка объектов не меняется");
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(c);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        private static float Sum(float[] values)
        {
            double sum = 0;
            if (values != null)
                foreach (float v in values)
                    sum += v;
            return (float)sum;
        }
    }
}
