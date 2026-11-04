using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomSurvival.MapLab
{
    // Рисованные отвесные скалы (пакет ревизии 06) вместо процедурных граней.
    // Вдоль сглаженной линии обрыва — одна непрерывная лента-стена; рисунок
    // натягивается по длине дуги (U) и по высоте (V): линия rock_top_y — край
    // площадки, base_y — земля у подножия. Над краем травяная кромка ложится
    // назад на площадку, под подножием подошва (осыпь, трава) — вперёд на
    // землю; силуэт верха и низа даёт альфа рисунка, прямых кромок меша нет.
    // Класс высоты (tall/medium/low) — по высоте обрыва линии, ширина плитки
    // следует высоте (без растяжения); варианты одного класса стыкуются по
    // совпадающим крайним столбцам, торцы — на концах линии (у medium торцов
    // нет — концы плавно растворяются). Обход — так, чтобы рисунок смотрел
    // наружу без отражения (U растёт вправо для зрителя снаружи).
    public static class MapLabCliffs06
    {
        public static readonly string[] Classes = { "tall", "medium", "low" };

        public sealed class Kit
        {
            public readonly Dictionary<string, List<MapLabArtEntry>> Faces = new Dictionary<string, List<MapLabArtEntry>>();
            public readonly Dictionary<string, MapLabArtEntry> EndLeft = new Dictionary<string, MapLabArtEntry>();
            public readonly Dictionary<string, MapLabArtEntry> EndRight = new Dictionary<string, MapLabArtEntry>();
            public MapLabArtEntry Buttress;
            public readonly List<MapLabArtEntry> Scree = new List<MapLabArtEntry>();
            public bool HasFaces => Faces.Count > 0;
        }

        public sealed class Piece
        {
            public MapLabArtEntry Entry;
            public float S0, S1;
            public bool FadeStart, FadeEnd;
            public bool IsEnd;
        }

        public sealed class Stats
        {
            public int Lines, Pieces, Ends, Faded, Buttresses, Skipped;
            public readonly Dictionary<string, int> ClassLines = new Dictionary<string, int>();
            public float MeanScale;
        }

        public sealed class Result
        {
            public readonly Dictionary<MapLabArtEntry, MeshData> Meshes = new Dictionary<MapLabArtEntry, MeshData>();
            public readonly Stats Stats = new Stats();
        }

        public sealed class MeshData
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Vector2> Outward = new List<Vector2>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int> Indices = new List<int>();

            public Mesh ToMesh(string name)
            {
                Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                if (Positions.Count > 65000)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(Positions);
                mesh.SetNormals(Normals);
                mesh.SetUVs(0, Uv);
                mesh.SetUVs(1, Outward);
                mesh.SetColors(Colors);
                mesh.SetTriangles(Indices, 0, true);
                Bounds b = mesh.bounds;
                b.Expand(2f);
                mesh.bounds = b;
                return mesh;
            }
        }

        // Места осыпей у подножия (мир XZ, высота земли) и направление наружу.
        public static readonly List<Vector3> ScreeSpots = new List<Vector3>();
        public static Stats LastStats = new Stats();

        public static Kit KitOf(MapLabArtSet set)
        {
            Kit kit = new Kit();
            if (set == null)
                return kit;
            foreach (MapLabArtEntry e in set.Entries)
            {
                if (e == null || !e.HasTexture || e.Category != MapLabArtCategory.CliffModule || e.EffectiveWeight <= 0f)
                    continue;
                switch (e.Role)
                {
                    case "cliff_face":
                        if (e.RockTopPx < 0f || e.BasePx <= e.RockTopPx)
                            break;
                        if (!kit.Faces.TryGetValue(e.HeightClass, out List<MapLabArtEntry> list))
                            kit.Faces[e.HeightClass] = list = new List<MapLabArtEntry>();
                        list.Add(e);
                        break;
                    case "cliff_end":
                        (e.Side == "left" ? kit.EndLeft : kit.EndRight)[e.HeightClass] = e;
                        break;
                    case "cliff_buttress":
                        kit.Buttress = e;
                        break;
                    case "cliff_scree":
                        kit.Scree.Add(e);
                        break;
                }
            }
            foreach (List<MapLabArtEntry> list in kit.Faces.Values)
                list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            kit.Scree.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return kit;
        }

        public static float RockUnits(MapLabArtEntry e) => (e.BasePx - e.RockTopPx) / Mathf.Max(1f, e.PxPerUnit);

        // Класс по высоте обрыва: ближайший по высоте камня из имеющихся.
        public static string ClassFor(Kit kit, float height)
        {
            string best = null;
            float bestScore = float.MaxValue;
            foreach (KeyValuePair<string, List<MapLabArtEntry>> pair in kit.Faces)
            {
                float score = Mathf.Abs(Mathf.Log(Mathf.Max(0.3f, height) / RockUnits(pair.Value[0])));
                if (score < bestScore)
                {
                    bestScore = score;
                    best = pair.Key;
                }
            }
            return best;
        }

        // Раскладка кусков вдоль линии длиной length: [левый торец][стены…][правый
        // торец]. Ширина плитки — по высоте (scale = высота / высота камня),
        // общий горизонтальный коэффициент подгоняет сумму к длине линии.
        public static List<Piece> Layout(Kit kit, string cls, float length, float height, uint seed, out float scale)
        {
            List<Piece> pieces = new List<Piece>();
            scale = 1f;
            if (cls == null || !kit.Faces.TryGetValue(cls, out List<MapLabArtEntry> faces) || length < 1.5f)
                return pieces;
            MapLabArtEntry first = faces[0];
            scale = Mathf.Clamp(height / RockUnits(first), 0.55f, 1.6f);
            float wf = first.Width / first.PxPerUnit * scale;
            kit.EndLeft.TryGetValue(cls, out MapLabArtEntry endL);
            kit.EndRight.TryGetValue(cls, out MapLabArtEntry endR);
            float we = endL != null && endR != null ? Mathf.Max(endL.Width, endR.Width) / first.PxPerUnit * scale : 0f;
            bool ends = we > 0f && length >= we * 2f * 0.55f;
            int n = ends ? Mathf.Max(0, Mathf.RoundToInt((length - 2f * we) / wf)) : Mathf.Max(1, Mathf.RoundToInt(length / wf));
            float wl = ends ? endL.Width / first.PxPerUnit * scale : 0f, wr = ends ? endR.Width / first.PxPerUnit * scale : 0f;
            float total = wl + wr + n * wf;
            float k = length / Mathf.Max(0.01f, total);
            float s = 0f;
            if (ends)
            {
                pieces.Add(new Piece { Entry = endL, S0 = 0f, S1 = wl * k, IsEnd = true });
                s = wl * k;
            }
            for (int i = 0; i < n; i++)
            {
                // Варианты чередуются протяжёнными участками (по две плитки).
                uint h = MapLabRandom.Hash(i / 2, (int)(seed & 0x7FFFFFFF), 0x2545F491u);
                MapLabArtEntry face = faces[(int)(h % (uint)faces.Count)];
                pieces.Add(new Piece { Entry = face, S0 = s, S1 = s + wf * k });
                s += wf * k;
            }
            if (ends)
                pieces.Add(new Piece { Entry = endR, S0 = s, S1 = length, IsEnd = true });
            else if (pieces.Count > 0)
            {
                pieces[0].FadeStart = true;
                pieces[pieces.Count - 1].FadeEnd = true;
                pieces[pieces.Count - 1].S1 = length;
            }
            return pieces;
        }

        private struct Station
        {
            public Vector2 P, Outward;
        }

        public static Result Build(MapLabData data, MapLabSettings settings, Kit kit)
        {
            Result result = new Result();
            ScreeSpots.Clear();
            if (kit == null || !kit.HasFaces)
            {
                LastStats = result.Stats;
                return result;
            }
            float w = Mathf.Max(0.4f, settings.Cliffs.CliffSharpness);
            MapLabRandom rng = MapLabRandom.ForStream(data.Seed, "cliff06", MapLabData.AlgorithmVersion);
            float scaleSum = 0f;
            for (int k = 0; k < data.CliffLines.Count; k++)
            {
                List<Station> line = Smooth(data.CliffLines[k]);
                if (line.Count < 3)
                {
                    result.Stats.Skipped++;
                    continue;
                }
                // Профиль вдоль дуги: положение стены, наружу, край площадки, подножие.
                int count = line.Count;
                float[] arc = new float[count];
                Vector2[] wall = new Vector2[count];
                Vector2[] outward = new Vector2[count];
                float[] top = new float[count], bottom = new float[count];
                for (int i = 0; i < count; i++)
                {
                    if (i > 0)
                        arc[i] = arc[i - 1] + Vector2.Distance(line[i - 1].P, line[i].P);
                    Vector2 o = line[i].Outward;
                    outward[i] = o;
                    // Стена — у основания перепада: склон рельефа не выглядывает из-под подошвы.
                    wall[i] = line[i].P + o * w * 1.5f;
                    // Край площадки — верх склона за линией (не середина перепада):
                    // иначе над рисунком остаётся полоса склона.
                    top[i] = Mathf.Max(data.SampleHeight(line[i].P.x - o.x * w * 1.25f, line[i].P.y - o.y * w * 1.25f),
                                       data.SampleHeight(line[i].P.x - o.x * w * 2.2f, line[i].P.y - o.y * w * 2.2f));
                    bottom[i] = data.SampleHeight(wall[i].x + o.x * 0.5f, wall[i].y + o.y * 0.5f);
                }
                float length = arc[count - 1];
                List<float> heights = new List<float>();
                for (int i = count / 5; i <= count - 1 - count / 5; i++)
                    heights.Add(top[i] - bottom[i]);
                heights.Sort();
                float height = heights.Count > 0 ? heights[heights.Count / 2] : 0f;
                if (height < 0.8f)
                {
                    result.Stats.Skipped++;
                    continue;
                }
                string cls = ClassFor(kit, height);
                List<Piece> pieces = Layout(kit, cls, length, height, (uint)(data.Seed * 7919 + k * 104729), out float scale);
                if (pieces.Count == 0)
                {
                    result.Stats.Skipped++;
                    continue;
                }
                result.Stats.Lines++;
                result.Stats.ClassLines[cls] = result.Stats.ClassLines.TryGetValue(cls, out int c) ? c + 1 : 1;
                scaleSum += scale;
                Profile profile = new Profile { Arc = arc, Wall = wall, Outward = outward, Top = top, Bottom = bottom, Data = data };
                foreach (Piece piece in pieces)
                {
                    if (!result.Meshes.TryGetValue(piece.Entry, out MeshData mesh))
                        result.Meshes[piece.Entry] = mesh = new MeshData();
                    Strip(mesh, profile, piece, 0f, 1f, length, 0f);
                    result.Stats.Pieces++;
                    if (piece.IsEnd) result.Stats.Ends++;
                    if (piece.FadeStart || piece.FadeEnd) result.Stats.Faded++;
                }
                // Контрфорс — изредка на длинном высоком участке, чуть впереди стены.
                if (kit.Buttress != null && cls == "tall" && pieces.Count >= 4 && rng.Chance(0.45f))
                {
                    int at = rng.Range(1, pieces.Count - 2);
                    float sb = pieces[at].S1;
                    float half = kit.Buttress.Width / kit.Buttress.PxPerUnit * scale * 0.5f;
                    Piece rib = new Piece { Entry = kit.Buttress, S0 = Mathf.Max(0f, sb - half), S1 = Mathf.Min(length, sb + half) };
                    if (!result.Meshes.TryGetValue(rib.Entry, out MeshData mesh))
                        result.Meshes[rib.Entry] = mesh = new MeshData();
                    Strip(mesh, profile, rib, 0f, 1f, length, 0.14f);
                    result.Stats.Buttresses++;
                }
                // Осыпи у подножия: редкие места по длине линии.
                for (float s = rng.Range(3f, 9f); s < length - 2f; s += rng.Range(9f, 17f))
                {
                    if (!rng.Chance(0.55f))
                        continue;
                    profile.At(s, out Vector2 p, out Vector2 o, out _, out _);
                    Vector2 q = p + o * rng.Range(0.6f, 1.5f);
                    ScreeSpots.Add(new Vector3(q.x, data.SampleHeight(q.x, q.y), q.y));
                }
            }
            result.Stats.MeanScale = result.Stats.Lines > 0 ? scaleSum / result.Stats.Lines : 0f;
            LastStats = result.Stats;
            return result;
        }

        private sealed class Profile
        {
            public float[] Arc, Top, Bottom;
            public Vector2[] Wall, Outward;
            public MapLabData Data;

            public void At(float s, out Vector2 wall, out Vector2 outward, out float top, out float bottom)
            {
                int i = System.Array.BinarySearch(Arc, s);
                if (i < 0) i = ~i;
                i = Mathf.Clamp(i, 1, Arc.Length - 1);
                float t = Mathf.InverseLerp(Arc[i - 1], Arc[i], s);
                wall = Vector2.Lerp(Wall[i - 1], Wall[i], t);
                outward = Vector2.Lerp(Outward[i - 1], Outward[i], t).normalized;
                top = Mathf.Lerp(Top[i - 1], Top[i], t);
                bottom = Mathf.Lerp(Bottom[i - 1], Bottom[i], t);
            }
        }

        // Полоса одного куска: подошва (на земле), стена (вертикально, с батером),
        // травяная кромка (назад на площадку). V-линии рисунка — по привязкам.
        private static void Strip(MeshData mesh, Profile profile, Piece piece, float u0, float u1, float length, float push)
        {
            MapLabArtEntry e = piece.Entry;
            float ppu = Mathf.Max(1f, e.PxPerUnit);
            float vBase = 1f - e.BasePx / e.Height, vTop = 1f - e.RockTopPx / e.Height;
            float rock = RockUnits(e);
            float footUnits = (e.Height - e.BasePx) / ppu, lipUnits = e.RockTopPx / ppu;
            float span = piece.S1 - piece.S0;
            int steps = Mathf.Max(2, Mathf.CeilToInt(span / 0.75f));
            int start = mesh.Positions.Count;
            for (int j = 0; j <= steps; j++)
            {
                float f = j / (float)steps;
                float s = piece.S0 + span * f;
                profile.At(s, out Vector2 wall, out Vector2 o, out float top, out float bottom);
                float vs = Mathf.Max(0.3f, (top - bottom) / rock);
                Vector2 basePoint = wall + o * push;
                float batter = Mathf.Min(0.6f, (top - bottom) * 0.07f);
                Vector2 topPoint = basePoint - o * batter;
                Vector2 footPoint = basePoint + o * footUnits * vs * 1.1f;
                Vector2 lipPoint = topPoint - o * lipUnits * vs * 1.8f;
                float u = Mathf.Lerp(u0, u1, f);
                float alpha = 1f;
                if (piece.FadeStart) alpha = Mathf.Min(alpha, Mathf.SmoothStep(0f, 1f, (s - piece.S0) / 3f));
                if (piece.FadeEnd) alpha = Mathf.Min(alpha, Mathf.SmoothStep(0f, 1f, (piece.S1 - s) / 3f));
                Color col = new Color(1f, 1f, 1f, alpha);
                Vector3 n = new Vector3(o.x, 0f, o.y);
                MapLabData d = profile.Data;
                Add(mesh, new Vector3(footPoint.x, d.SampleHeight(footPoint.x, footPoint.y) + 0.03f, footPoint.y), Vector3.up, new Vector2(u, 0f), o, col);
                Add(mesh, new Vector3(basePoint.x, bottom - 0.05f, basePoint.y), n, new Vector2(u, vBase), o, col);
                Add(mesh, new Vector3(topPoint.x, top, topPoint.y), n, new Vector2(u, vTop), o, col);
                Add(mesh, new Vector3(lipPoint.x, Mathf.Max(top, d.SampleHeight(lipPoint.x, lipPoint.y)) + 0.04f, lipPoint.y), Vector3.up, new Vector2(u, 1f), o, col);
            }
            for (int j = 0; j < steps; j++)
            {
                int a = start + j * 4, b = a + 4;
                for (int r = 0; r < 3; r++)
                {
                    mesh.Indices.Add(a + r); mesh.Indices.Add(a + r + 1); mesh.Indices.Add(b + r + 1);
                    mesh.Indices.Add(a + r); mesh.Indices.Add(b + r + 1); mesh.Indices.Add(b + r);
                }
            }
        }

        private static void Add(MeshData m, Vector3 p, Vector3 n, Vector2 uv, Vector2 o, Color c)
        {
            m.Positions.Add(p);
            m.Normals.Add(n);
            m.Uv.Add(uv);
            m.Outward.Add(o);
            m.Colors.Add(c);
        }

        // Линия обрыва по станциям столбов, сглаженная (Chaikin) и
        // переразбитая через 0.5 ед.; наружу — по нормали линии в сторону
        // нижней стороны. Обход — U вправо для зрителя снаружи.
        private static List<Station> Smooth(MapLabCliffLine line)
        {
            List<MapLabCliffColumn> cols = line.Columns;
            List<Vector2> points = new List<Vector2>();
            Vector2 meanOut = Vector2.zero;
            for (int i = 0; i < cols.Count; i++)
            {
                Vector2 p = cols[i].Start;
                if (points.Count == 0 || Vector2.Distance(points[points.Count - 1], p) > 0.05f)
                    points.Add(p);
                meanOut += cols[i].Outward;
            }
            if (cols.Count > 0)
                points.Add(cols[cols.Count - 1].End);
            List<Station> result = new List<Station>();
            if (points.Count < 2 || meanOut.sqrMagnitude < 1e-6f)
                return result;
            meanOut.Normalize();
            Vector2 right = new Vector2(-meanOut.y, meanOut.x);
            if (Vector2.Dot(points[points.Count - 1] - points[0], right) < 0f)
                points.Reverse();
            for (int pass = 0; pass < 2; pass++)
            {
                List<Vector2> next = new List<Vector2> { points[0] };
                for (int i = 0; i < points.Count - 1; i++)
                {
                    next.Add(Vector2.Lerp(points[i], points[i + 1], 0.25f));
                    next.Add(Vector2.Lerp(points[i], points[i + 1], 0.75f));
                }
                next.Add(points[points.Count - 1]);
                points = next;
            }
            List<Vector2> even = MapLabGeometry.Resample(points, 0.5f);
            for (int i = 0; i < even.Count; i++)
            {
                Vector2 t = even[Mathf.Min(even.Count - 1, i + 1)] - even[Mathf.Max(0, i - 1)];
                t = t.sqrMagnitude > 1e-8f ? t.normalized : Vector2.right;
                // Для зрителя снаружи U растёт вправо: наружу = поворот касательной.
                Vector2 o = new Vector2(t.y, -t.x);
                if (Vector2.Dot(o, meanOut) < 0f)
                    o = -o;
                result.Add(new Station { P = even[i], Outward = o });
            }
            return result;
        }
    }
}
