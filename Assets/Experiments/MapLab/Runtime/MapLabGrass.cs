using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomSurvival.MapLab
{
    // Редкие пучки травы этапа 04: 2–4 изогнутых рисованных штриха цвета
    // местного луга, отдельные — темнее (как внутренние линии крон). Только у
    // опор деревьев и столбов, на межах и у троп — не на полотне дороги, не во
    // дворах, не в воде. Места — от seed; на общем виде шейдер их гасит.
    public static class MapLabGrass
    {
        public enum Source
        {
            Tree = 0,
            Post = 1,
            Path = 2,
            Balk = 3,
        }

        public struct Tuft
        {
            public Vector3 Base;
            public Source Source;
            public uint Hash;
        }

        public static List<Tuft> Place(MapLabData data, MapLabSettings settings)
        {
            List<Tuft> tufts = new List<Tuft>();
            float density = Mathf.Clamp01(settings.Stage04.Grass);
            if (density <= 0f)
                return tufts;
            uint stream = (uint)MapLabRandom.StreamSeed(data.Seed, "grass04", MapLabData.AlgorithmVersion);
            void Try(Vector2 p, Source source, int a, int b, float chance)
            {
                uint h = MapLabRandom.Hash(a, b, stream ^ ((uint)source * 0x9E3779B9u));
                if ((h & 0xFFFF) / 65536f >= chance * density)
                    return;
                if (!Allowed(data, p))
                    return;
                tufts.Add(new Tuft { Base = new Vector3(p.x, data.SampleHeight(p.x, p.y), p.y), Source = source, Hash = h });
            }
            // У стволов: рядом с опорой, сбоку или чуть перед ней.
            for (int i = 0; i < data.Objects.Count; i++)
            {
                MapLabPlacement o = data.Objects[i];
                if (o.Kind != MapLabObjectKind.Deciduous && o.Kind != MapLabObjectKind.Conifer && o.Kind != MapLabObjectKind.DeciduousGroup)
                    continue;
                if (data.SampleMask(data.Forest, o.Position.x, o.Position.z) > 0.6f)
                    continue;
                uint h = MapLabRandom.Hash(i, 17, stream);
                float angle = (h & 0xFFF) / 4096f * Mathf.PI * 2f;
                float r = 0.5f + ((h >> 12) & 0xFF) / 255f * 0.6f;
                Try(new Vector2(o.Position.x, o.Position.z) + new Vector2(Mathf.Cos(angle), -Mathf.Abs(Mathf.Sin(angle))) * r, Source.Tree, i, 3, 0.45f);
            }
            // У столбов оград.
            for (int i = 0; i < data.Fences.Count; i++)
                Try(data.Fences[i].A + new Vector2(0.25f, -0.2f), Source.Post, i, 5, 0.3f);
            // Вдоль троп — у кромки.
            List<MapLabPolyline> paths = new List<MapLabPolyline>();
            foreach (MapLabPolyline r in data.Roads)
                if (r.Kind == MapLabRoadKind.Path)
                    paths.Add(r);
            paths.AddRange(data.YardPaths);
            for (int k = 0; k < paths.Count; k++)
            {
                List<Vector2> points = MapLabGeometry.Resample(paths[k].Points, 2.5f);
                for (int s = 1; s < points.Count; s++)
                {
                    Vector2 d = (points[s] - points[s - 1]).normalized;
                    Vector2 side = new Vector2(-d.y, d.x) * ((s & 1) == 0 ? 1f : -1f);
                    Try(points[s] + side * (paths[k].Width * 0.5f + 0.3f), Source.Path, k * 4096 + s, 7, 0.22f);
                }
            }
            // Межи: точки дрожащей сетки у границы надела.
            if (data.Fields != null)
            {
                for (int gz = 0; gz < Mathf.CeilToInt(data.Size / 3f); gz++)
                for (int gx = 0; gx < Mathf.CeilToInt(data.Size / 3f); gx++)
                {
                    uint h = MapLabRandom.Hash(gx, gz, stream ^ 0x51u);
                    Vector2 p = new Vector2((gx + (h & 0xFF) / 255f) * 3f, (gz + ((h >> 8) & 0xFF) / 255f) * 3f);
                    if (data.SampleMask(data.Field, p.x, p.y) < 0.5f || !data.Fields.Evaluate(p, out MapLabFields.Sample fs) || fs.BorderDistance > 0.3f)
                        continue;
                    Try(p, Source.Balk, gx, gz, 0.3f);
                }
            }
            return tufts;
        }

        // Разрешённое место: суша, не полотно дороги, не двор, не круча.
        public static bool Allowed(MapLabData data, Vector2 p)
        {
            if (!data.Contains(p.x, p.y) || data.IsUnderWater(p.x, p.y, 0.15f) || data.SampleSlope(p.x, p.y) > 28f)
                return false;
            if (data.SampleMask(data.Rock, p.x, p.y) > 0.4f || data.SampleMask(data.Cliff, p.x, p.y) > 0.3f)
                return false;
            foreach (MapLabYardRect yard in data.YardRects)
                if (yard.Contains(p, 0.2f))
                    return false;
            List<MapLabPolyline> roads = new List<MapLabPolyline>(data.Roads);
            roads.AddRange(data.YardPaths);
            foreach (MapLabPolyline road in roads)
                for (int s = 0; s < road.Points.Count - 1; s++)
                {
                    Vector2 a = road.Points[s], b = road.Points[s + 1];
                    if (Mathf.Abs(a.x - p.x) > 30f && Mathf.Abs(b.x - p.x) > 30f)
                        continue;
                    if (MapLabGeometry.DistanceToSegment(p, a, b, out _) < road.Width * 0.5f + 0.15f)
                        return false;
                }
            return true;
        }

        // Штрих — лента из 4 точек от земли вверх; ширину и разворот к камере
        // задаёт шейдер (uv0: сторона, доля высоты; цвет — вершин).
        public static Mesh Build(MapLabData data, MapLabPaintFields fields, List<Tuft> tufts)
        {
            if (tufts.Count == 0)
                return null;
            List<Vector3> positions = new List<Vector3>();
            List<Vector2> uv0 = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<int> indices = new List<int>();
            foreach (Tuft t in tufts)
            {
                MapLabRandom rng = new MapLabRandom(((ulong)t.Hash << 21) ^ 0x6A09E667UL);
                Color meadow = Color.gray;
                if (fields != null)
                {
                    fields.Get(new Vector2(t.Base.x, t.Base.z), out MapLabPaintFields.Sample s);
                    meadow = new Color(s.R, s.G, s.B, 1f);
                }
                int strokes = rng.Range(2, 5);
                for (int k = 0; k < strokes; k++)
                {
                    float height = rng.Range(0.32f, 0.62f) * (t.Source == Source.Balk ? 0.85f : 1f);
                    float lean = rng.Signed() * 0.45f + (k - (strokes - 1) * 0.5f) * 0.22f;
                    Vector3 root = t.Base + new Vector3(rng.Signed() * 0.09f, 0f, rng.Signed() * 0.05f);
                    bool dark = rng.Chance(0.45f);
                    Color c = dark ? Color.Lerp(meadow * 0.72f, new Color(0.3f, 0.3f, 0.18f, 1f), 0.25f) : meadow * 1.06f;
                    c.a = 1f;
                    int start = positions.Count;
                    const int segments = 3;
                    for (int j = 0; j <= segments; j++)
                    {
                        float f = j / (float)segments;
                        Vector3 p = root + new Vector3(lean * height * f * f, height * f, 0f);
                        for (int side = -1; side <= 1; side += 2)
                        {
                            positions.Add(p);
                            uv0.Add(new Vector2(side, f));
                            colors.Add(c);
                        }
                    }
                    for (int j = 0; j < segments; j++)
                    {
                        int a = start + j * 2;
                        indices.Add(a); indices.Add(a + 2); indices.Add(a + 3);
                        indices.Add(a); indices.Add(a + 3); indices.Add(a + 1);
                    }
                }
            }
            Mesh mesh = new Mesh { name = "MapLab Grass Tufts", hideFlags = HideFlags.DontSave };
            if (positions.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uv0);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, true);
            Bounds bounds = mesh.bounds;
            bounds.Expand(2f);
            mesh.bounds = bounds;
            return mesh;
        }
    }
}
