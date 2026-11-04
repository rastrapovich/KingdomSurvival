using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomSurvival.MapLab
{
    // Узоры поверхности для шейдера Solid (uv2.z × 8).
    public static class MapLabPattern
    {
        public const int Plain = 0;
        public const int Logs = 1;
        public const int Thatch = 2;
        public const int Shingle = 3;
        public const int Rock = 4;
        public const int Planks = 5;
        public const int Terrain = 6; // цвет из живописной текстуры поверхности
    }

    // Сборщик меша с плоскими гранями: позиция, нормаль, локальные координаты
    // грани (для тонкой линии по рёбрам), размер грани, индекс палитры и узор.
    public sealed class MapLabMeshBuilder
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uv0 = new List<Vector2>();
        public readonly List<Vector2> Uv1 = new List<Vector2>();
        public readonly List<Vector4> Uv2 = new List<Vector4>();
        public readonly List<int> Indices = new List<int>();

        public int VertexCount => Positions.Count;

        // Четырёхугольник a-b-c-d (против часовой при взгляде снаружи).
        // u идёт от a к b, v — от a к d; линия — у рёбер грани.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int palette, int pattern, float ink, float brightness = 1f, bool edgeInkAll = true)
        {
            Vector3 normal = Vector3.Cross(d - a, b - a).normalized;
            if (normal.sqrMagnitude < 0.5f)
                normal = Vector3.up;
            float width = Vector3.Distance(a, b);
            float height = Vector3.Distance(a, d);
            int start = Positions.Count;
            Positions.Add(a); Positions.Add(b); Positions.Add(c); Positions.Add(d);
            for (int i = 0; i < 4; i++)
                Normals.Add(normal);
            Uv0.Add(new Vector2(0f, 0f));
            Uv0.Add(new Vector2(width, 0f));
            Uv0.Add(new Vector2(width, height));
            Uv0.Add(new Vector2(0f, height));
            Vector2 size = new Vector2(width, edgeInkAll ? height : -height);
            Vector4 meta = new Vector4((palette + 0.5f) / MapLabPaletteIndex.Count, brightness, pattern / 8f + 0.01f, ink);
            for (int i = 0; i < 4; i++)
            {
                Uv1.Add(size);
                Uv2.Add(meta);
            }
            Indices.Add(start); Indices.Add(start + 3); Indices.Add(start + 2);
            Indices.Add(start); Indices.Add(start + 2); Indices.Add(start + 1);
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, int palette, int pattern, float ink, float brightness = 1f)
        {
            Vector3 normal = Vector3.Cross(c - a, b - a).normalized;
            if (normal.sqrMagnitude < 0.5f)
                normal = Vector3.up;
            float width = Vector3.Distance(a, b);
            Vector3 ab = (b - a).normalized;
            float cu = Vector3.Dot(c - a, ab);
            float cv = (c - a - ab * cu).magnitude;
            int start = Positions.Count;
            Positions.Add(a); Positions.Add(b); Positions.Add(c);
            for (int i = 0; i < 3; i++)
                Normals.Add(normal);
            Uv0.Add(new Vector2(0f, 0f));
            Uv0.Add(new Vector2(width, 0f));
            Uv0.Add(new Vector2(cu, cv));
            Vector4 meta = new Vector4((palette + 0.5f) / MapLabPaletteIndex.Count, brightness, pattern / 8f + 0.01f, ink);
            for (int i = 0; i < 3; i++)
            {
                Uv1.Add(new Vector2(width, -Mathf.Max(0.01f, cv)));
                Uv2.Add(meta);
            }
            Indices.Add(start); Indices.Add(start + 2); Indices.Add(start + 1);
        }

        // Ящик по осям ось-X/ось-Z со смещением и поворотом.
        public void Box(Vector3 center, Vector3 size, float yawDegrees, int palette, int pattern, float ink, float brightness = 1f)
        {
            Quaternion q = Quaternion.Euler(0f, -yawDegrees, 0f);
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + q * new Vector3(x * h.x, y * h.y, z * h.z);
            Vector3 p000 = P(-1, -1, -1), p100 = P(1, -1, -1), p110 = P(1, 1, -1), p010 = P(-1, 1, -1);
            Vector3 p001 = P(-1, -1, 1), p101 = P(1, -1, 1), p111 = P(1, 1, 1), p011 = P(-1, 1, 1);
            Quad(p000, p100, p110, p010, palette, pattern, ink, brightness); // -Z
            Quad(p101, p001, p011, p111, palette, pattern, ink, brightness); // +Z
            Quad(p001, p000, p010, p011, palette, pattern, ink, brightness); // -X
            Quad(p100, p101, p111, p110, palette, pattern, ink, brightness); // +X
            Quad(p010, p110, p111, p011, palette, pattern, ink, brightness); // верх
        }

        public Mesh ToMesh(string name)
        {
            Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            if (Positions.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uv0);
            mesh.SetUVs(1, Uv1);
            mesh.SetUVs(2, Uv2);
            mesh.SetTriangles(Indices, 0, true);
            return mesh;
        }
    }

    public static class MapLabMeshes
    {
        // 12У-C: разрезать сетку на участки мира (по центру треугольника) — у
        // каждого участка свои границы, и ближний кадр рисует только видимое.
        // Треугольник целиком уходит в один участок: швов и дыр нет. Каналы
        // UV0–UV2 копируются как есть.
        public static List<Mesh> SplitByChunks(Mesh source, float chunk, string name)
        {
            List<Mesh> result = new List<Mesh>();
            if (source == null)
                return result;
            Vector3[] v = source.vertices;
            int[] t = source.triangles;
            List<Vector2> u0 = new List<Vector2>(), u1 = new List<Vector2>();
            List<Vector4> u2 = new List<Vector4>();
            source.GetUVs(0, u0);
            source.GetUVs(1, u1);
            source.GetUVs(2, u2);
            Dictionary<long, List<int>> groups = new Dictionary<long, List<int>>();
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                long key = ((long)Mathf.FloorToInt(c.x / chunk) << 32) ^ (uint)Mathf.FloorToInt(c.z / chunk);
                if (!groups.TryGetValue(key, out List<int> list))
                    groups[key] = list = new List<int>();
                list.Add(i);
            }
            List<long> keys = new List<long>(groups.Keys);
            keys.Sort();
            foreach (long key in keys)
            {
                Dictionary<int, int> remap = new Dictionary<int, int>();
                List<Vector3> pos = new List<Vector3>();
                List<Vector2> a0 = new List<Vector2>(), a1 = new List<Vector2>();
                List<Vector4> a2 = new List<Vector4>();
                List<int> tri = new List<int>();
                foreach (int i in groups[key])
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int src = t[i + k];
                        if (!remap.TryGetValue(src, out int dst))
                        {
                            dst = pos.Count;
                            remap[src] = dst;
                            pos.Add(v[src]);
                            if (u0.Count > src) a0.Add(u0[src]);
                            if (u1.Count > src) a1.Add(u1[src]);
                            if (u2.Count > src) a2.Add(u2[src]);
                        }
                        tri.Add(dst);
                    }
                }
                Mesh m = new Mesh { name = name + " " + result.Count, hideFlags = HideFlags.DontSave };
                if (pos.Count > 65000)
                    m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(pos);
                if (a0.Count == pos.Count) m.SetUVs(0, a0);
                if (a1.Count == pos.Count) m.SetUVs(1, a1);
                if (a2.Count == pos.Count) m.SetUVs(2, a2);
                m.SetTriangles(tri, 0, true);
                Bounds b = m.bounds;
                b.Expand(2f);
                m.bounds = b;
                result.Add(m);
            }
            return result;
        }

        // ---------- Рельеф ----------

        public static List<Mesh> BuildTerrainChunks(MapLabData data, int chunkVertices)
        {
            List<Mesh> meshes = new List<Mesh>();
            int n = data.Resolution;
            int span = Mathf.Clamp(chunkVertices, 9, 129) - 1;
            int chunks = Mathf.CeilToInt((n - 1) / (float)span);
            for (int cz = 0; cz < chunks; cz++)
            {
                for (int cx = 0; cx < chunks; cx++)
                {
                    int x0 = cx * span, z0 = cz * span;
                    int x1 = Mathf.Min(n - 1, x0 + span), z1 = Mathf.Min(n - 1, z0 + span);
                    int w = x1 - x0 + 1, h = z1 - z0 + 1;
                    Vector3[] positions = new Vector3[w * h];
                    Vector3[] normals = new Vector3[w * h];
                    Vector2[] uvs = new Vector2[w * h];
                    for (int z = 0; z < h; z++)
                    for (int x = 0; x < w; x++)
                    {
                        int gi = data.Index(x0 + x, z0 + z);
                        int vi = z * w + x;
                        positions[vi] = new Vector3((x0 + x) * data.Cell, data.Heights[gi], (z0 + z) * data.Cell);
                        normals[vi] = data.Normals[gi];
                        uvs[vi] = new Vector2((x0 + x) / (float)(n - 1), (z0 + z) / (float)(n - 1));
                    }
                    int[] indices = new int[(w - 1) * (h - 1) * 6];
                    int t = 0;
                    for (int z = 0; z < h - 1; z++)
                    for (int x = 0; x < w - 1; x++)
                    {
                        int a = z * w + x, b = a + 1, c = a + w, d = c + 1;
                        // Диагональ по меньшему перепаду — без «ёлочки» на гребнях.
                        if (Mathf.Abs(positions[a].y - positions[d].y) < Mathf.Abs(positions[b].y - positions[c].y))
                        {
                            indices[t++] = a; indices[t++] = c; indices[t++] = d;
                            indices[t++] = a; indices[t++] = d; indices[t++] = b;
                        }
                        else
                        {
                            indices[t++] = a; indices[t++] = c; indices[t++] = b;
                            indices[t++] = b; indices[t++] = c; indices[t++] = d;
                        }
                    }
                    Mesh mesh = new Mesh { name = $"MapLab Terrain {cx},{cz}", hideFlags = HideFlags.DontSave };
                    if (positions.Length > 65000)
                        mesh.indexFormat = IndexFormat.UInt32;
                    mesh.vertices = positions;
                    mesh.normals = normals;
                    mesh.uv = uvs;
                    mesh.triangles = indices;
                    mesh.RecalculateBounds();
                    meshes.Add(mesh);
                }
            }
            return meshes;
        }

        public static Mesh BuildWireframe(MapLabData data)
        {
            int n = data.Resolution;
            int step = n > 257 ? 2 : 1;
            List<Vector3> positions = new List<Vector3>();
            List<int> indices = new List<int>();
            int Vertex(int x, int z)
            {
                positions.Add(new Vector3(x * data.Cell, data.Heights[data.Index(x, z)] + 0.03f, z * data.Cell));
                return positions.Count - 1;
            }
            for (int z = 0; z < n; z += step)
            for (int x = 0; x < n - step; x += step)
            {
                indices.Add(Vertex(x, z));
                indices.Add(Vertex(x + step, z));
            }
            for (int x = 0; x < n; x += step)
            for (int z = 0; z < n - step; z += step)
            {
                indices.Add(Vertex(x, z));
                indices.Add(Vertex(x, z + step));
            }
            Mesh mesh = new Mesh { name = "MapLab Wireframe", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(positions);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            return mesh;
        }

        // ---------- Вода ----------

        // Озеро: горизонтальная поверхность из клеток котловины; край прячется под
        // сухой берег (берег выше уровня), поэтому вода не выходит за котловину.
        public static Mesh BuildLakes(MapLabData data)
        {
            List<Vector3> positions = new List<Vector3>();
            List<Vector2> uv0 = new List<Vector2>();
            List<Vector2> uv1 = new List<Vector2>();
            List<Vector3> uv2 = new List<Vector3>();
            List<int> indices = new List<int>();
            int n = data.Resolution;
            if (data.Lakes.Count == 0 || data.LakeField == null)
                return null;
            for (int z = 0; z < n - 1; z++)
            {
                for (int x = 0; x < n - 1; x++)
                {
                    float f = Mathf.Max(Mathf.Max(data.LakeField[data.Index(x, z)], data.LakeField[data.Index(x + 1, z)]),
                        Mathf.Max(data.LakeField[data.Index(x, z + 1)], data.LakeField[data.Index(x + 1, z + 1)]));
                    if (f < -0.08f)
                        continue;
                    Vector2 center = new Vector2((x + 0.5f) * data.Cell, (z + 0.5f) * data.Cell);
                    float level = NearestLake(data, center).Level;
                    int start = positions.Count;
                    for (int k = 0; k < 4; k++)
                    {
                        int dx = k == 1 || k == 2 ? 1 : 0;
                        int dz = k >= 2 ? 1 : 0;
                        Vector3 p = new Vector3((x + dx) * data.Cell, level, (z + dz) * data.Cell);
                        positions.Add(p);
                        uv0.Add(new Vector2(p.x, p.z));
                        uv1.Add(new Vector2(1f, 0.12f));
                        // Этап 05: z = 0 — озеро (штрихи в мире, спокойнее реки).
                        uv2.Add(new Vector3(p.x, p.z, 0f));
                    }
                    indices.Add(start); indices.Add(start + 3); indices.Add(start + 2);
                    indices.Add(start); indices.Add(start + 2); indices.Add(start + 1);
                }
            }
            if (positions.Count == 0)
                return null;
            Mesh mesh = new Mesh { name = "MapLab Lakes", hideFlags = HideFlags.DontSave };
            if (positions.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        private static MapLabLake NearestLake(MapLabData data, Vector2 p)
        {
            MapLabLake best = data.Lakes[0];
            float bestD = float.MaxValue;
            for (int i = 0; i < data.Lakes.Count; i++)
            {
                float d = Vector2.Distance(data.Lakes[i].Center, p);
                if (d < bestD)
                {
                    bestD = d;
                    best = data.Lakes[i];
                }
            }
            return best;
        }

        // Река: лента по осевой линии; высота — монотонный профиль уровня.
        public static Mesh BuildRivers(MapLabData data)
        {
            List<Vector3> positions = new List<Vector3>();
            List<Vector2> uv0 = new List<Vector2>();
            List<Vector2> uv1 = new List<Vector2>();
            List<Vector3> uv2 = new List<Vector3>();
            List<int> indices = new List<int>();
            for (int r = 0; r < data.Rivers.Count; r++)
            {
                MapLabRiver river = data.Rivers[r];
                int count = river.Points.Count;
                if (count < 2)
                    continue;
                int rowStart = -1;
                // Этап 05: параметризация русла — длина вдоль осевой и расстояние
                // поперёк (мир): штрихи течения следуют руслу без швов на изгибах.
                float along = r * 1000f;
                for (int i = 0; i < count; i++)
                {
                    Vector2 p = river.Points[i];
                    if (i > 0)
                        along += Vector2.Distance(river.Points[i - 1], p);
                    Vector2 tangent = (river.Points[Mathf.Min(count - 1, i + 1)] - river.Points[Mathf.Max(0, i - 1)]).normalized;
                    Vector2 normal = MapLabGeometry.LeftNormal(tangent);
                    float half = river.Widths[i] * 0.5f + 1.6f;
                    float level = river.Levels[i] - 0.01f;
                    bool insideLake = data.LakeField != null && data.Bilinear(data.LakeField, p.x, p.y) > 0.12f;
                    Vector2 left = p + normal * half;
                    Vector2 right = p - normal * half;
                    int start = positions.Count;
                    positions.Add(new Vector3(left.x, level, left.y));
                    positions.Add(new Vector3(right.x, level, right.y));
                    uv0.Add(left);
                    uv0.Add(right);
                    uv1.Add(tangent);
                    uv1.Add(tangent);
                    uv2.Add(new Vector3(along, half, 1f));
                    uv2.Add(new Vector3(along, -half, 1f));
                    if (rowStart >= 0 && !insideLake)
                    {
                        indices.Add(rowStart); indices.Add(start); indices.Add(start + 1);
                        indices.Add(rowStart); indices.Add(start + 1); indices.Add(rowStart + 1);
                    }
                    rowStart = start;
                }
            }
            if (indices.Count == 0)
                return null;
            Mesh mesh = new Mesh { name = "MapLab Rivers", hideFlags = HideFlags.DontSave };
            if (positions.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetTriangles(indices, 0);
            FixWinding(mesh);
            mesh.RecalculateNormals();
            return mesh;
        }

        // Треугольники ленты смотрят вверх независимо от направления течения.
        private static void FixWinding(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (n.y < 0f)
                {
                    int tmp = t[i + 1];
                    t[i + 1] = t[i + 2];
                    t[i + 2] = tmp;
                }
            }
            mesh.triangles = t;
        }

        // ---------- Карточки растительности ----------

        public static List<Mesh> BuildVegetationCards(MapLabData data, float chunkSize, HashSet<MapLabObjectKind> skip = null)
        {
            Dictionary<int, List<int>> byChunk = new Dictionary<int, List<int>>();
            int chunks = Mathf.Max(1, Mathf.CeilToInt(data.Size / chunkSize));
            for (int i = 0; i < data.Objects.Count; i++)
            {
                MapLabObjectKind kind = data.Objects[i].Kind;
                if (kind != MapLabObjectKind.Deciduous && kind != MapLabObjectKind.DeciduousGroup && kind != MapLabObjectKind.Conifer
                    && kind != MapLabObjectKind.Bush && kind != MapLabObjectKind.Reed || (skip != null && skip.Contains(kind)))
                    continue;
                Vector3 p = data.Objects[i].Position;
                int cx = Mathf.Clamp((int)(p.x / chunkSize), 0, chunks - 1);
                int cz = Mathf.Clamp((int)(p.z / chunkSize), 0, chunks - 1);
                int key = cz * chunks + cx;
                if (!byChunk.TryGetValue(key, out List<int> list))
                    byChunk[key] = list = new List<int>();
                list.Add(i);
            }
            List<Mesh> meshes = new List<Mesh>();
            List<int> keys = new List<int>(byChunk.Keys);
            keys.Sort();
            foreach (int key in keys)
            {
                List<int> list = byChunk[key];
                // Дальние — первыми: при равной глубине ближние перекрывают.
                list.Sort((a, b) => data.Objects[b].Position.z.CompareTo(data.Objects[a].Position.z));
                List<Vector3> positions = new List<Vector3>(list.Count * 4);
                List<Vector2> uv0 = new List<Vector2>(list.Count * 4);
                List<Vector2> uv1 = new List<Vector2>(list.Count * 4);
                List<Color> colors = new List<Color>(list.Count * 4);
                List<int> indices = new List<int>(list.Count * 6);
                float maxHeight = 0f;
                foreach (int index in list)
                {
                    MapLabPlacement o = data.Objects[index];
                    int cell;
                    float width, height, kindCode, sink;
                    switch (o.Kind)
                    {
                        case MapLabObjectKind.Conifer:
                            cell = MapLabArt.ConiferFirst + o.Variant % MapLabArt.ConiferCount;
                            width = height = o.Scale * 1.3f;
                            kindCode = 0.25f;
                            sink = 0.12f;
                            break;
                        case MapLabObjectKind.DeciduousGroup:
                            cell = MapLabArt.GroupFirst + o.Variant % MapLabArt.GroupCount;
                            width = height = o.Scale;
                            kindCode = 0f;
                            sink = 0.12f;
                            break;
                        case MapLabObjectKind.Bush:
                            cell = MapLabArt.BushFirst + o.Variant % MapLabArt.BushCount;
                            width = height = o.Scale;
                            kindCode = 0.5f;
                            sink = 0.08f;
                            break;
                        case MapLabObjectKind.Reed:
                            cell = MapLabArt.ReedFirst + o.Variant % MapLabArt.ReedCount;
                            width = height = o.Scale;
                            kindCode = 0.75f;
                            sink = 0f;
                            break;
                        default:
                            cell = MapLabArt.DeciduousFirst + o.Variant % MapLabArt.DeciduousCount;
                            width = height = o.Scale;
                            kindCode = 0f;
                            sink = 0.12f;
                            break;
                    }
                    Rect r = MapLabArt.CellRect(cell);
                    Vector3 basePoint = o.Position - new Vector3(0f, sink * o.Scale * 0.3f, 0f);
                    int start = positions.Count;
                    for (int k = 0; k < 4; k++)
                        positions.Add(basePoint);
                    float inset = 0.5f / (MapLabArt.AtlasColumns * MapLabArt.Cell);
                    uv0.Add(new Vector2(r.xMin + inset, r.yMin + inset));
                    uv0.Add(new Vector2(r.xMax - inset, r.yMin + inset));
                    uv0.Add(new Vector2(r.xMax - inset, r.yMax - inset));
                    uv0.Add(new Vector2(r.xMin + inset, r.yMax - inset));
                    uv1.Add(new Vector2(-width * 0.5f, 0f));
                    uv1.Add(new Vector2(width * 0.5f, 0f));
                    uv1.Add(new Vector2(width * 0.5f, height));
                    uv1.Add(new Vector2(-width * 0.5f, height));
                    Color color = new Color(o.Tone, Mathf.Clamp01(o.Brightness / 1.25f), kindCode + 0.01f, 1f);
                    for (int k = 0; k < 4; k++)
                        colors.Add(color);
                    indices.Add(start); indices.Add(start + 3); indices.Add(start + 2);
                    indices.Add(start); indices.Add(start + 2); indices.Add(start + 1);
                    maxHeight = Mathf.Max(maxHeight, basePoint.y + height * 2.2f);
                }
                Mesh mesh = new Mesh { name = "MapLab Vegetation " + key, hideFlags = HideFlags.DontSave };
                if (positions.Count > 65000)
                    mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(positions);
                mesh.SetUVs(0, uv0);
                mesh.SetUVs(1, uv1);
                mesh.SetColors(colors);
                mesh.SetTriangles(indices, 0, false);
                // Карточки разворачиваются в шейдере — границы задаём с запасом.
                int kx = key % chunks, kz = key / chunks;
                Vector3 min = new Vector3(kx * chunkSize - 8f, data.MinHeight - 2f, kz * chunkSize - 8f);
                Vector3 max = new Vector3((kx + 1) * chunkSize + 8f, maxHeight + 4f, (kz + 1) * chunkSize + 16f);
                mesh.bounds = new Bounds((min + max) * 0.5f, max - min);
                meshes.Add(mesh);
            }
            return meshes;
        }

        // ---------- Скалы ----------

        // Скальная стенка из столбов-модулей: несколько вертикальных граней с
        // разным наклоном (свет даёт ритм), неровный верх, уступ-полка на высоких
        // стенках, запас под землю у подножия и вглубь площадки сзади.
        public static Mesh BuildCliffs(MapLabData data, MapLabSettings settings)
        {
            MapLabMeshBuilder mb = new MapLabMeshBuilder();
            MapLabRandom rng = MapLabRandom.ForStream(data.Seed, "cliff-mesh", MapLabData.AlgorithmVersion);
            float w = Mathf.Max(0.4f, settings.Cliffs.CliffSharpness);
            float jag = settings.Cliffs.Jaggedness;
            // Крупный ритм вдоль линии (этап 02): выпуклые «бастионы», короткие впадины
            // с травяными полками, разная высота — вместо частокола одинаковых столбов.
            MapLabNoise rhythm = new MapLabNoise(MapLabRandom.StreamSeed(data.Seed, "cliff-rhythm", MapLabData.AlgorithmVersion));
            for (int k = 0; k < data.CliffLines.Count; k++)
            {
                MapLabCliffLine line = data.CliffLines[k];
                float station = 0f;
                for (int i = 0; i < line.Columns.Count; i++)
                {
                    MapLabCliffColumn c = line.Columns[i];
                    // Обход граней — так, чтобы лицевая сторона смотрела наружу (вниз по уступу).
                    Vector2 run = c.End - c.Start;
                    if (Vector2.Dot(new Vector2(run.y, -run.x), c.Outward) < 0f)
                    {
                        Vector2 swap = c.Start;
                        c.Start = c.End;
                        c.End = swap;
                        if (c.Module == MapLabCliffModule.Start) c.Module = MapLabCliffModule.End;
                        else if (c.Module == MapLabCliffModule.End) c.Module = MapLabCliffModule.Start;
                    }
                    float height = c.Top - c.Bottom;
                    Vector2 o = c.Outward;
                    // Столбы выступают по-разному: глубина даёт тень трещин между ними.
                    station += Vector2.Distance(c.Start, c.End);
                    float lf = rhythm.Fbm(station / 15f + k * 7.3f, k * 3.1f, 2);
                    float bastion = MapLabGeometry.SmoothStep(-0.05f, 0.55f, lf);
                    float hollow = MapLabGeometry.SmoothStep(-0.2f, -0.55f, lf);
                    float protrude = (rng.Range(0f, 0.3f) + bastion * 1.5f - hollow * 0.55f) * w;
                    bool notch = height > 3f && rng.Chance(0.08f + hollow * 0.5f);
                    float top = notch ? c.Top - height * rng.Range(0.12f, 0.28f) : c.Top;
                    int facets = rng.Range(2, 5);
                    Vector2 frontA = c.Start + o * (w * 1.2f + protrude);
                    Vector2 frontB = c.End + o * (w * 1.2f + protrude);
                    Vector2 backA = c.Start - o * w * 2f;
                    Vector2 backB = c.End - o * w * 2f;
                    // Точки передней грани: выпуклость и откат верха (небольшой «батер»).
                    Vector2[] front = new Vector2[facets + 1];
                    float[] topJag = new float[facets + 1];
                    for (int f = 0; f <= facets; f++)
                    {
                        float t = f / (float)facets;
                        float bulge = (f == 0 || f == facets) ? 0f : rng.Range(-0.15f, 0.7f) * w;
                        front[f] = Vector2.Lerp(frontA, frontB, t) + o * bulge;
                        topJag[f] = rng.Signed() * 0.45f * jag * Mathf.Min(1f, height / 4f);
                    }
                    float batter = Mathf.Min(1.1f * w, height * 0.12f);
                    bool tiered = height > 3.2f && rng.Chance(0.25f + hollow * 0.6f + bastion * 0.15f);
                    float split = tiered ? c.Bottom + height * rng.Range(0.42f, 0.62f) : top;
                    float brightness = rng.Range(0.9f, 1.08f);
                    float shelf = tiered ? rng.Range(0.35f, 0.8f) * w : 0f;
                    const float rim = 0.45f;
                    for (int f = 0; f < facets; f++)
                    {
                        Vector2 a = front[f], b = front[f + 1];
                        Vector2 aTop = a - o * batter * 0.5f;
                        Vector2 bTop = b - o * batter * 0.5f;
                        float ya = top + topJag[f], yb = top + topJag[f + 1];
                        if (tiered)
                        {
                            Vector2 aMid = a - o * batter * 0.25f;
                            Vector2 bMid = b - o * batter * 0.25f;
                            Vector2 aUp = aMid - o * shelf;
                            Vector2 bUp = bMid - o * shelf;
                            mb.Quad(V(a, c.Bottom), V(b, c.Bottom), V(bMid, split), V(aMid, split), MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0.5f, brightness);
                            mb.Quad(V(aMid, split), V(bMid, split), V(bUp, split), V(aUp, split), MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0.4f, brightness * 1.12f, false);
                            mb.Quad(V(aUp, split), V(bUp, split), V(bTop - o * shelf, yb), V(aTop - o * shelf, ya), MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0.5f, brightness * 1.03f);
                        }
                        else
                        {
                            mb.Quad(V(a, c.Bottom), V(b, c.Bottom), V(bTop, yb), V(aTop, ya), MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0.5f, brightness);
                        }
                        // Светлый каменный край и травяная крышка: крышка берёт цвет
                        // живописной поверхности под собой и сливается с площадкой.
                        Vector2 aT = tiered ? aTop - o * shelf : aTop;
                        Vector2 bT = tiered ? bTop - o * shelf : bTop;
                        Vector2 aBack = Vector2.Lerp(backA, backB, f / (float)facets);
                        Vector2 bBack = Vector2.Lerp(backA, backB, (f + 1) / (float)facets);
                        mb.Quad(V(aT, ya), V(bT, yb), V(bT - o * rim, yb - 0.04f), V(aT - o * rim, ya - 0.04f), MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0.7f, brightness * 1.04f, false);
                        // Крышка уходит под рельеф назад: там, где земля выше, её не видно (без «стеклянных» граней).
                        mb.Quad(V(aT - o * rim, ya - 0.06f), V(bT - o * rim, yb - 0.06f), V(bBack, c.Top - 1.5f), V(aBack, c.Top - 1.5f), MapLabPaletteIndex.GrassB, MapLabPattern.Terrain, 0f, 1f, false);
                    }
                    // Боковые грани каждого столба: соседи разного выноса без щелей.
                    Vector2 sideTopA = front[0] - o * (batter * 0.5f + shelf);
                    Vector2 sideTopB = front[facets] - o * (batter * 0.5f + shelf);
                    float sideBright = brightness * 0.95f;
                    mb.Quad(V(backA, c.Bottom), V(frontA, c.Bottom), V(sideTopA, top + topJag[0]), V(backA, c.Top - 1.5f), MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0.45f, sideBright);
                    mb.Quad(V(frontB, c.Bottom), V(backB, c.Bottom), V(backB, c.Top - 1.5f), V(sideTopB, top + topJag[facets]), MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0.45f, sideBright);
                }
            }
            return mb.VertexCount > 0 ? mb.ToMesh("MapLab Cliffs") : null;
        }

        private static Vector3 V(Vector2 p, float y)
        {
            return new Vector3(p.x, y, p.y);
        }

        // ---------- Постройки и мелкие объекты ----------

        public static Mesh BuildStructures(MapLabData data, MapLabSettings settings, HashSet<MapLabObjectKind> skip = null, bool skipBridges = false)
        {
            MapLabMeshBuilder mb = new MapLabMeshBuilder();
            for (int i = 0; i < data.Objects.Count; i++)
            {
                MapLabPlacement o = data.Objects[i];
                if (skip != null && skip.Contains(o.Kind))
                    continue;
                switch (o.Kind)
                {
                    case MapLabObjectKind.House:
                        House(mb, data, o, false);
                        break;
                    case MapLabObjectKind.Barn:
                        House(mb, data, o, true);
                        break;
                    case MapLabObjectKind.Haystack:
                        Haystack(mb, o);
                        break;
                }
            }
            bool vary = settings != null && settings.IsStage04;
            for (int i = 0; i < data.Fences.Count; i++)
                Fence(mb, data, data.Fences[i], vary);
            // Этап 05: мосты собираются из модулей (MapLabModules05).
            for (int i = 0; i < data.Bridges.Count && !skipBridges; i++)
                Bridge(mb, data, data.Bridges[i]);
            for (int i = 0; i < data.Piers.Count; i++)
                Pier(mb, data.Piers[i]);
            return mb.VertexCount > 0 ? mb.ToMesh("MapLab Structures") : null;
        }

        // Только срубы и амбары: отдельная группа, чтобы переключаться с карточками без перестройки.
        public static Mesh BuildHouses(MapLabData data)
        {
            MapLabMeshBuilder mb = new MapLabMeshBuilder();
            for (int i = 0; i < data.Objects.Count; i++)
            {
                MapLabPlacement o = data.Objects[i];
                if (o.Kind == MapLabObjectKind.House || o.Kind == MapLabObjectKind.Barn)
                    House(mb, data, o, o.Kind == MapLabObjectKind.Barn);
            }
            return mb.VertexCount > 0 ? mb.ToMesh("MapLab Houses") : null;
        }

        public static Mesh BuildBoulders(MapLabData data, HashSet<MapLabObjectKind> skip = null)
        {
            MapLabMeshBuilder mb = new MapLabMeshBuilder();
            for (int i = 0; i < data.Objects.Count; i++)
            {
                MapLabPlacement o = data.Objects[i];
                if (o.Kind == MapLabObjectKind.Boulder && (skip == null || !skip.Contains(o.Kind)))
                    Boulder(mb, o);
            }
            return mb.VertexCount > 0 ? mb.ToMesh("MapLab Boulders") : null;
        }

        // Сруб с двускатной кровлей; ось конька — вдоль длинной стороны.
        private static void House(MapLabMeshBuilder mb, MapLabData data, MapLabPlacement o, bool barn)
        {
            float w = o.Size.x, d = o.Size.y;
            Quaternion q = Quaternion.Euler(0f, -o.Yaw, 0f);
            Vector3 center = new Vector3(o.Position.x, 0f, o.Position.z);
            float minH = float.MaxValue, maxH = float.MinValue;
            for (int k = 0; k < 4; k++)
            {
                Vector3 corner = center + q * new Vector3((k % 2 == 0 ? -0.5f : 0.5f) * w, 0f, (k < 2 ? -0.5f : 0.5f) * d);
                float h = data.SampleHeight(corner.x, corner.z);
                minH = Mathf.Min(minH, h);
                maxH = Mathf.Max(maxH, h);
            }
            float floor = minH - 0.35f;
            float wall = maxH + (barn ? 1.45f : 1.85f) * Mathf.Sqrt(w / 4.4f);
            float ridge = wall + d * (barn ? 0.5f : 0.62f);
            float over = 0.4f;
            Vector3 P(float x, float y, float z) => center + q * new Vector3(x, 0f, z) + Vector3.up * y;
            int wallPalette = barn ? MapLabPaletteIndex.WoodDark : MapLabPaletteIndex.WoodWall;
            int wallPattern = barn ? MapLabPattern.Planks : MapLabPattern.Logs;
            float hw = w * 0.5f, hd = d * 0.5f;
            float b = o.Brightness;
            mb.Quad(P(-hw, floor, -hd), P(hw, floor, -hd), P(hw, wall, -hd), P(-hw, wall, -hd), wallPalette, wallPattern, 0.45f, b);
            mb.Quad(P(hw, floor, hd), P(-hw, floor, hd), P(-hw, wall, hd), P(hw, wall, hd), wallPalette, wallPattern, 0.45f, b);
            mb.Quad(P(-hw, floor, hd), P(-hw, floor, -hd), P(-hw, wall, -hd), P(-hw, wall, hd), wallPalette, wallPattern, 0.45f, b);
            mb.Quad(P(hw, floor, -hd), P(hw, floor, hd), P(hw, wall, hd), P(hw, wall, -hd), wallPalette, wallPattern, 0.45f, b);
            mb.Triangle(P(-hw, wall, hd), P(-hw, wall, -hd), P(-hw, ridge, 0f), wallPalette, wallPattern, 0.45f, b * 0.97f);
            mb.Triangle(P(hw, wall, -hd), P(hw, wall, hd), P(hw, ridge, 0f), wallPalette, wallPattern, 0.45f, b * 0.97f);
            int roofPalette;
            int roofPattern;
            switch (barn ? 3 + o.Variant % 2 : o.Variant % 4)
            {
                case 0: case 1: roofPalette = MapLabPaletteIndex.RoofThatch; roofPattern = MapLabPattern.Thatch; break;
                case 2: roofPalette = MapLabPaletteIndex.RoofShingle; roofPattern = MapLabPattern.Shingle; break;
                default: roofPalette = MapLabPaletteIndex.RoofDark; roofPattern = MapLabPattern.Shingle; break;
            }
            float eave = wall - 0.2f;
            float rx = hw + over, rz = hd + over;
            mb.Quad(P(-rx, eave, -rz), P(rx, eave, -rz), P(rx, ridge + 0.08f, 0f), P(-rx, ridge + 0.08f, 0f), roofPalette, roofPattern, 0.6f, b);
            mb.Quad(P(rx, eave, rz), P(-rx, eave, rz), P(-rx, ridge + 0.08f, 0f), P(rx, ridge + 0.08f, 0f), roofPalette, roofPattern, 0.6f, b);
        }

        private static void Haystack(MapLabMeshBuilder mb, MapLabPlacement o)
        {
            const int sides = 7;
            float radius = 1.3f * o.Scale;
            float height = 2.5f * o.Scale;
            Vector3 c = o.Position - Vector3.up * 0.2f;
            Vector3 apex = c + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * Mathf.PI * 2f + o.Yaw * Mathf.Deg2Rad;
                float a1 = ((i + 1f) / sides) * Mathf.PI * 2f + o.Yaw * Mathf.Deg2Rad;
                Vector3 p0 = c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                Vector3 p1 = c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                Vector3 m0 = c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius * 0.95f + Vector3.up * height * 0.45f;
                Vector3 m1 = c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius * 0.95f + Vector3.up * height * 0.45f;
                mb.Quad(p0, p1, m1, m0, MapLabPaletteIndex.FieldStraw, MapLabPattern.Thatch, 0.2f, o.Brightness);
                mb.Triangle(m0, m1, apex, MapLabPaletteIndex.FieldStraw, MapLabPattern.Thatch, 0.2f, o.Brightness);
            }
        }

        // vary (этап 04): небольшие устойчивые различия высоты и толщины
        // столбов и жердей, тона дерева — от места пролёта; соединения те же.
        private static void Fence(MapLabMeshBuilder mb, MapLabData data, MapLabFence f, bool vary = false)
        {
            Vector2 dir = f.B - f.A;
            float length = dir.magnitude;
            if (length < 0.1f)
                return;
            float yaw = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float ha = data.SampleHeight(f.A.x, f.A.y), hb = data.SampleHeight(f.B.x, f.B.y);
            MapLabRandom rng = new MapLabRandom((ulong)MapLabRandom.Hash(Mathf.RoundToInt(f.A.x * 10f), Mathf.RoundToInt(f.A.y * 10f), 0x0FE11CEu) + 1UL);
            float postH = vary ? 1.1f * rng.Range(0.9f, 1.08f) : 1.1f;
            float postW = vary ? rng.Range(0.13f, 0.17f) : 0.16f;
            float tone = vary ? rng.Range(0.9f, 1.06f) : 1f;
            float upper = vary ? 0.62f + rng.Signed() * 0.05f : 0.62f;
            float lower = vary ? 0.3f + rng.Signed() * 0.04f : 0.3f;
            float railA = vary ? rng.Range(0.1f, 0.13f) : 0.12f;
            float railB = vary ? rng.Range(0.09f, 0.11f) : 0.1f;
            mb.Box(new Vector3(f.A.x, ha + postH * 0.5f - 0.2f, f.A.y), new Vector3(postW, postH, postW), yaw, MapLabPaletteIndex.WoodDark, MapLabPattern.Plain, 0.25f, tone);
            Vector2 mid = (f.A + f.B) * 0.5f;
            float hm = (ha + hb) * 0.5f;
            mb.Box(new Vector3(mid.x, hm + upper, mid.y), new Vector3(length, railA, 0.08f), yaw, MapLabPaletteIndex.WoodDark, MapLabPattern.Planks, 0.2f, tone * (vary ? rng.Range(0.96f, 1.04f) : 1f));
            mb.Box(new Vector3(mid.x, hm + lower, mid.y), new Vector3(length, railB, 0.08f), yaw, MapLabPaletteIndex.WoodDark, MapLabPattern.Planks, 0.2f, tone * (vary ? rng.Range(0.94f, 1.02f) : 1f));
        }

        // Мост: настил поперёк русла, перила, сваи в воде.
        private static void Bridge(MapLabMeshBuilder mb, MapLabData data, MapLabBridge bridge)
        {
            Vector2 dir = bridge.EndB - bridge.EndA;
            float length = dir.magnitude;
            if (length < 0.5f)
                return;
            Vector2 axis = dir / length;
            Vector2 side = MapLabGeometry.LeftNormal(axis);
            float yaw = Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg;
            float deck = bridge.DeckHeight;
            float ha = data.SampleHeight(bridge.EndA.x, bridge.EndA.y);
            float hb = data.SampleHeight(bridge.EndB.x, bridge.EndB.y);
            Vector2 mid = (bridge.EndA + bridge.EndB) * 0.5f;
            int planks = Mathf.Max(4, Mathf.RoundToInt(length / 0.6f));
            for (int i = 0; i < planks; i++)
            {
                float t = (i + 0.5f) / planks;
                Vector2 p = Vector2.Lerp(bridge.EndA - axis * 0.6f, bridge.EndB + axis * 0.6f, t);
                float end = Mathf.Min(t, 1f - t) * 2f;
                float y = Mathf.Lerp(Mathf.Lerp(ha, hb, t), deck, MapLabGeometry.SmoothStep(0f, 0.35f, end)) + 0.05f;
                mb.Box(new Vector3(p.x, y, p.y), new Vector3((length + 1.2f) / planks * 0.92f, 0.22f, bridge.Width), yaw, MapLabPaletteIndex.WoodWall, MapLabPattern.Planks, 0.45f, (i % 3 == 0) ? 0.92f : 1f);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 railA = bridge.EndA + side * s * bridge.Width * 0.5f;
                Vector2 railB = bridge.EndB + side * s * bridge.Width * 0.5f;
                Vector2 rm = (railA + railB) * 0.5f;
                mb.Box(new Vector3(rm.x, deck + 0.75f, rm.y), new Vector3(length * 0.85f, 0.12f, 0.12f), yaw, MapLabPaletteIndex.WoodDark, MapLabPattern.Planks, 0.6f);
                int posts = Mathf.Max(2, Mathf.RoundToInt(length / 2.2f));
                for (int i = 0; i <= posts; i++)
                {
                    Vector2 p = Vector2.Lerp(railA, railB, 0.08f + 0.84f * i / posts);
                    mb.Box(new Vector3(p.x, deck + 0.4f, p.y), new Vector3(0.14f, 0.85f, 0.14f), yaw, MapLabPaletteIndex.WoodDark, MapLabPattern.Plain, 0.25f);
                }
                for (int i = 1; i <= 2; i++)
                {
                    Vector2 p = Vector2.Lerp(railA, railB, i / 3f);
                    float bottom = bridge.WaterLevel - 1.2f;
                    mb.Box(new Vector3(p.x, (deck + bottom) * 0.5f, p.y), new Vector3(0.3f, deck - bottom, 0.3f), yaw, MapLabPaletteIndex.WoodDark, MapLabPattern.Plain, 0.6f);
                }
            }
            _ = mid;
        }

        private static void Pier(MapLabMeshBuilder mb, MapLabPier pier)
        {
            Vector2 dir = pier.Tip - pier.Shore;
            float length = dir.magnitude;
            if (length < 0.5f)
                return;
            Vector2 axis = dir / length;
            Vector2 side = MapLabGeometry.LeftNormal(axis);
            float yaw = Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg;
            int planks = Mathf.Max(3, Mathf.RoundToInt(length / 0.55f));
            for (int i = 0; i < planks; i++)
            {
                Vector2 p = Vector2.Lerp(pier.Shore, pier.Tip, (i + 0.5f) / planks);
                mb.Box(new Vector3(p.x, pier.DeckHeight, p.y), new Vector3(length / planks * 0.9f, 0.18f, pier.Width), yaw, MapLabPaletteIndex.WoodWall, MapLabPattern.Planks, 0.4f, i % 4 == 0 ? 0.9f : 1f);
            }
            for (int i = 1; i <= 3; i++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 p = Vector2.Lerp(pier.Shore, pier.Tip, i / 3f) + side * s * pier.Width * 0.45f;
                    mb.Box(new Vector3(p.x, pier.DeckHeight - 0.6f, p.y), new Vector3(0.22f, 1.6f, 0.22f), yaw, MapLabPaletteIndex.WoodDark, MapLabPattern.Plain, 0.25f);
                }
            }
        }

        // Валун: приплюснутый многогранник с плоскими гранями.
        private static void Boulder(MapLabMeshBuilder mb, MapLabPlacement o)
        {
            MapLabRandom rng = new MapLabRandom((ulong)(o.Position.x * 7919f) ^ (ulong)(o.Position.z * 104729f) ^ (ulong)o.Variant);
            const int ring = 6;
            float r = o.Scale;
            Vector3 c = o.Position - Vector3.up * r * 0.25f;
            Vector3 top = c + Vector3.up * r * rng.Range(0.75f, 0.95f) + new Vector3(rng.Signed(), 0f, rng.Signed()) * r * 0.15f;
            Vector3[] mid = new Vector3[ring];
            Vector3[] low = new Vector3[ring];
            for (int i = 0; i < ring; i++)
            {
                float a = (i + rng.Signed() * 0.25f) / ring * Mathf.PI * 2f + o.Yaw * Mathf.Deg2Rad;
                float rr = r * rng.Range(0.8f, 1.15f);
                mid[i] = c + new Vector3(Mathf.Cos(a) * rr, r * rng.Range(0.35f, 0.5f), Mathf.Sin(a) * rr * 0.85f);
                low[i] = c + new Vector3(Mathf.Cos(a) * rr * 1.05f, -0.1f, Mathf.Sin(a) * rr * 0.9f);
            }
            for (int i = 0; i < ring; i++)
            {
                int j = (i + 1) % ring;
                mb.Triangle(mid[i], mid[j], top, MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0.55f, o.Brightness * 1.04f);
                mb.Quad(low[i], low[j], mid[j], mid[i], MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 0.55f, o.Brightness);
            }
        }
    }
}
