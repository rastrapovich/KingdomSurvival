using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Правки карты этапа 05 (04.11.2026): акварельные пятна разного размера и
    // рисунка, тропинки от ворот доходят до дороги.
    public sealed class MapLabStage05MapFixTests
    {
        private static MapLabSettings Settings()
        {
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.RiverValley);
            s.General.Quality = MapLabQuality.Samples129;
            s.Stage03.Look = MapLabLook.Stage05;
            return s;
        }

        private static MapLabWash.Source Blob(string id)
        {
            Color32[] px = new Color32[32 * 24];
            for (int y = 0; y < 24; y++)
                for (int x = 0; x < 32; x++)
                {
                    float d = new Vector2((x - 16f) / 16f, (y - 12f) / 12f).magnitude;
                    px[y * 32 + x] = new Color32(90, 110, 60, (byte)(Mathf.Clamp01(1f - d) * 255f));
                }
            return MapLabWash.FromPixels(id, px, 32, 24, new Vector2(0.04f, 0.12f));
        }

        [Test]
        public void Wash_SizesDiffer_NeighboursUseDifferentDrawings()
        {
            MapLabSettings s = Settings();
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                List<MapLabWash.Source> sources = new List<MapLabWash.Source> { Blob("a"), Blob("b"), Blob("c"), Blob("d") };
                MapLabWash.Layer layer = MapLabWash.Place(d, s, sources);
                Assert.Greater(layer.Patches.Count, 8, "мало пятен для проверки");
                float min = float.MaxValue, max = 0f;
                foreach (MapLabWash.Patch p in layer.Patches)
                {
                    float size = Mathf.Max(p.HalfSize.x, p.HalfSize.y);
                    min = Mathf.Min(min, size);
                    max = Mathf.Max(max, size);
                    Assert.LessOrEqual(Mathf.Max(p.HalfSize.x, p.HalfSize.y) / Mathf.Min(p.HalfSize.x, p.HalfSize.y), 2.2f, "пятно сильно растянуто");
                }
                Assert.Greater(max / min, 2.2f, "размеры пятен почти одинаковые");
                // Соседи похожего размера не повторяют один рисунок (кроме нарочного наложения — другой рисунок).
                for (int i = 0; i < layer.Patches.Count; i++)
                    for (int j = i + 1; j < layer.Patches.Count; j++)
                    {
                        MapLabWash.Patch a = layer.Patches[i], b = layer.Patches[j];
                        if (a.Source != b.Source)
                            continue;
                        float reach = Mathf.Max(a.HalfSize.x, a.HalfSize.y) + Mathf.Max(b.HalfSize.x, b.HalfSize.y);
                        Assert.Greater((a.Center - b.Center).magnitude, reach, "рядом два пятна одного рисунка");
                    }
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void YardGates_PathsReachRoad()
        {
            MapLabSettings s = Settings();
            try
            {
                MapLabData d = MapLabGenerator.Generate(s, null);
                int houses = 0;
                foreach (MapLabPlacement o in d.Objects)
                    if (o.Kind == MapLabObjectKind.House)
                        houses++;
                Assume.That(houses > 0, "в деревне нет домов");
                int reaching = 0;
                foreach (MapLabPolyline path in d.YardPaths)
                {
                    Vector2 end = path.Points[path.Points.Count - 1];
                    foreach (MapLabPolyline road in d.Roads)
                    {
                        if (road.Widening)
                            continue;
                        bool on = false;
                        for (int i = 0; i + 1 < road.Points.Count && !on; i++)
                            on = MapLabGeometry.DistanceToSegment(end, road.Points[i], road.Points[i + 1], out _) <= road.Width * 0.5f + 0.05f;
                        if (on)
                        {
                            reaching++;
                            break;
                        }
                    }
                }
                Assert.GreaterOrEqual(reaching, Mathf.CeilToInt(houses * 0.8f), "тропинки от ворот не доходят до дороги");
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }
    }
}
