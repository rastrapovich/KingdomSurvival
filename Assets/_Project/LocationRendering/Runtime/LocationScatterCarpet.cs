using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using UnityEngine.Rendering;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12Р: ковёр раскидки — все экземпляры группы одной сеткой
    // (прямоугольник на экземпляр, одна отрисовка на тысячи штук), как
    // неподвижные частицы. ParticleSystem Unity здесь не годится: в
    // предпросмотре окон редактора (PreviewRenderUtility) он не рисуется,
    // а частицы ковра всё равно не движутся. Каждый прямоугольник — рисунок
    // ассета в своём месте, размере, растяжении, повороте вокруг опоры,
    // отражении и цвете: тон, насыщенность, яркость и отражение — в UV
    // для шейдера (_KS_PARTICLE), подкраска — цвет вершин. Освещается 2D-светом
    // с нормалями ассета.
    // Группа — один ассет, ракурс, часть и фаза: кадр анимации меняется сменой
    // текстуры группы, а фаза экземпляров разложена по нескольким группам (до
    // восьми), чтобы заросль не качалась в такт. Порядок внутри группы —
    // порядок прямоугольников: дальние (выше на рисунке) раньше.
    // Теней, проходимости и сортировки с людьми у ковра нет — это слой.
    // Реакция на проход: у гнущихся ассетов (слой «Реакция на проход») —
    // несколько горизонтальных рядов вершин на рисунок вместо двух, позиции
    // вершин — отдельным потоком сетки: прогиб пишется на CPU от исходных
    // вершин, на GPU уходит только изменённый диапазон. Остальная раскидка —
    // прежние четыре вершины. Кадры, UV, цвет и блок свойств прогиб не трогает.
    public sealed class LocationScatterCarpet
    {
        // Фазовых групп на ассет, ракурс и часть: больше — меньше видно такт.
        public const int MaxPhaseBuckets = 16;
        // Ряды гнущегося рисунка над опорой (плюс опора и низ рисунка).
        public const int BendRows = 5;

        private sealed class Group
        {
            public GameObject Object;
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public Sprite[] Frames;
            public Texture[] Normals;
            public int Bucket, Buckets;
            public float FramesPerSecond;
            // Свой темп группы (вразнобой с другими): кадры не меняются разом.
            public float Speed = 1;
            public ArtAssetPlayback Playback;
            public int Shown = -1;
            // Свет ассета: солнце и огонь по отдельности.
            public Vector4 LightShare = new Vector4(1, 1, 0, 0);
            public readonly List<Quad> Quads = new List<Quad>();
            // Ряды вершин: доли высоты рисунка снизу (0 — низ, 1 — верх).
            public float[] Rows;
            public bool Bends;
            // Гнущаяся группа: исходные и текущие позиции, высота вершины над
            // опорой в долях высоты растения, ось «вверх» рисунка.
            public Vector3[] Base, Positions;
            public float[] VertexHeight;
            public Vector2[] QuadUp;
            // Тронутые с прошлой выгрузки прямоугольники (без повторов).
            public int DirtyMin = int.MaxValue, DirtyMax = -1;
            public bool[] QuadDirty;
        }

        private struct Quad
        {
            public int Key;
            public Vector2 Anchor, Right, Up;
            public float PivotX, PivotY, PlantHeight;
            public Color Tint;
            public Vector4 Hsv;
            public float Flip, Angle, Depth;
        }

        // Гнущийся экземпляр: опора (пиксели рисунка места), высота над
        // опорой (единицы мира) и его прямоугольники (группа, номер).
        public sealed class BendingPlant
        {
            public int Key;
            public Vector2 Pixel;
            public float Height;
            public readonly List<Vector2Int> Quads = new List<Vector2Int>();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VertexData
        {
            public Vector3 Normal;
            public Vector4 Tangent;
            public Color Color;
            public Vector4 Uv0;
            public Vector3 Uv1;
        }

        // Поток 0 — позиции (переписываются при прогибе), поток 1 — остальное.
        private static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 1),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 3, 1)
        };

        private const MeshUpdateFlags QuietUpdate = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontResetBoneBounds |
                                                    MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds;
        private static readonly float[] FlatRows = { 0, 1 };

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int NormalMapId = Shader.PropertyToID("_NormalMap");
        private static readonly int UvRectId = Shader.PropertyToID("_KsUvRect");
        private static readonly int LightShareId = Shader.PropertyToID("_KsLight");

        private readonly LocationScatterLayer layer;
        private readonly LocalLocationDefinition location;
        private readonly Transform root;
        private readonly Material material;
        private readonly List<Group> groups = new List<Group>();
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        private readonly Dictionary<int, BendingPlant> bending = new Dictionary<int, BendingPlant>();

        public int Count { get; private set; }
        public int GroupCount => groups.Count;
        // Растёт с каждой пересборкой: прежние ссылки на прямоугольники недействительны.
        public int Version { get; private set; }
        public IEnumerable<BendingPlant> BendingPlants => bending.Values;
        public int BendingCount => bending.Count;
        public int VertexCount
        {
            get { int total = 0; foreach (Group group in groups) total += group.Mesh != null ? group.Mesh.vertexCount : 0; return total; }
        }

        public LocationScatterCarpet(LocationScatterLayer layer, LocalLocationDefinition location, Transform root, Material material)
        {
            this.layer = layer;
            this.location = location;
            this.root = root;
            this.material = material;
            Rebuild();
        }

        public IEnumerable<MeshRenderer> Renderers
        {
            get { foreach (Group group in groups) yield return group.Renderer; }
        }

        public void Clear()
        {
            foreach (Group group in groups)
            {
                if (group.Object != null) Destroy(group.Object);
                if (group.Mesh != null) Destroy(group.Mesh);
            }
            groups.Clear();
            bending.Clear();
            Count = 0;
            Version++;
        }

        // Собрать ковёр заново по слою (после мазка кисти и правок слоя).
        public void Rebuild()
        {
            Clear();
            if (layer == null || layer.Hidden || material == null) return;
            Dictionary<string, Group> byKey = new Dictionary<string, Group>(StringComparer.Ordinal);
            Dictionary<string, LocationResolvedVisual> bases = new Dictionary<string, LocationResolvedVisual>(StringComparer.Ordinal);
            int order = LocationVisualGeometry.SortOrder(layer.CarpetBand, 0, layer.OrderOffset);
            foreach (LocationScatterInstance instance in layer.Instances)
            {
                if (instance == null || string.IsNullOrEmpty(instance.AssetId)) continue;
                ArtAssetDefinition asset = ArtAssetDatabaseAsset.FindCurrent(instance.AssetId);
                if (asset == null) continue;
                string baseKey = instance.AssetId + "|" + (int)instance.View;
                if (!bases.TryGetValue(baseKey, out LocationResolvedVisual resolved))
                {
                    resolved = LocationVisualResolver.Resolve(new LocationVisualObject { Id = "carpet", AssetId = instance.AssetId, View = instance.View, Scale = 1 });
                    bases[baseKey] = resolved;
                }
                Vector2 anchor = LocationVisualGeometry.ToWorld(location, instance.Position);
                bool bends = layer.Bends(instance.AssetId);
                // Высота растения над опорой — по самой высокой части.
                float plantHeight = 0;
                if (bends)
                    foreach (LocationResolvedPart part in resolved.Parts)
                        if (part.Sprite != null) plantHeight = Mathf.Max(plantHeight, part.Height * Scale(instance) * (1 - Mathf.Clamp01(part.Pivot.y)));
                bends &= plantHeight > 1e-4f;
                for (int p = 0; p < resolved.Parts.Count; p++)
                {
                    LocationResolvedPart part = resolved.Parts[p];
                    if (part.Sprite == null) continue;
                    Sprite[] frames = part.Animated ? part.Frames : new[] { part.Sprite };
                    int buckets = frames.Length > 1 && resolved.FramesPerSecond > 0 ? Mathf.Min(MaxPhaseBuckets, frames.Length * 2) : 1;
                    int bucket = buckets > 1 && (asset.RandomPhase || layer.DesyncAnimation)
                        ? Mathf.Min(buckets - 1, Mathf.FloorToInt(ArtAssetAnimation.StablePhase(layer.InstanceId(instance)) * buckets)) : 0;
                    string key = baseKey + "|" + p + "|" + bucket + (bends ? "|гнётся" : "");
                    if (!byKey.TryGetValue(key, out Group group))
                    {
                        group = CreateGroup(asset, resolved, frames, bucket, buckets, order + part.OrderOffset, key);
                        // Темп — по ассету и фазе, не по части: части одного рисунка идут вместе.
                        group.Speed = layer.AnimationSpeed(layer.Id + "|" + baseKey + "|" + bucket);
                        group.Bends = bends;
                        group.Rows = bends ? RowsFor(part.Pivot.y) : FlatRows;
                        byKey[key] = group;
                    }
                    group.Quads.Add(QuadOf(instance, part, anchor, plantHeight));
                    if (bends && !bending.ContainsKey(instance.Key))
                        bending[instance.Key] = new BendingPlant { Key = instance.Key, Pixel = LocationVisualGeometry.ToPixel(location, instance.Position), Height = plantHeight };
                }
            }
            for (int i = 0; i < groups.Count; i++) Fill(groups[i], i);
        }

        private static float Scale(LocationScatterInstance instance) => instance.Scale > 0 ? instance.Scale : 1;

        // Ряды гнущегося рисунка: низ, опора и равные доли выше опоры.
        private static float[] RowsFor(float pivotY)
        {
            float pivot = Mathf.Clamp01(pivotY);
            if (pivot >= .999f) return FlatRows;
            List<float> rows = new List<float>(BendRows + 2);
            if (pivot > .001f) rows.Add(0);
            rows.Add(pivot);
            for (int i = 1; i <= BendRows; i++) rows.Add(pivot + (1 - pivot) * i / BendRows);
            return rows.ToArray();
        }

        private Group CreateGroup(ArtAssetDefinition asset, LocationResolvedVisual resolved, Sprite[] frames, int bucket, int buckets, int sortingOrder, string key)
        {
            GameObject target = new GameObject("Ковёр · " + key);
            target.transform.SetParent(root, false);
            MeshFilter filter = target.AddComponent<MeshFilter>();
            MeshRenderer renderer = target.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Mesh mesh = new Mesh { name = "Ковёр · " + key, hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            Group group = new Group
            {
                Object = target, Renderer = renderer, Mesh = mesh, Frames = frames, Bucket = bucket, Buckets = buckets,
                FramesPerSecond = resolved.FramesPerSecond, Playback = resolved.Playback, Normals = new Texture[frames.Length],
                // Ковёр под людьми освещается как земля под ним.
                LightShare = LocationWorldRenderer.LightShare(resolved, layer.CarpetBand != LocationVisualBand.Foreground)
            };
            for (int i = 0; i < frames.Length; i++) group.Normals[i] = NormalOf(asset, resolved.ShownView, frames[i]);
            groups.Add(group);
            return group;
        }

        // Нормаль кадра: из записи Базы ассетов, иначе вторая текстура рисунка.
        private static Texture NormalOf(ArtAssetDefinition asset, ArtAssetView view, Sprite sprite)
        {
            if (asset?.Parts != null)
                foreach (ArtAssetPart part in asset.Parts)
                {
                    Texture2D normal = part?.FindView(view)?.NormalOf(sprite);
                    if (normal != null) return normal;
                }
            int count = sprite.GetSecondaryTextureCount();
            if (count > 0)
            {
                SecondarySpriteTexture[] textures = new SecondarySpriteTexture[count];
                sprite.GetSecondaryTextures(textures);
                foreach (SecondarySpriteTexture texture in textures)
                    if (texture.name == "_NormalMap" && texture.texture != null) return texture.texture;
            }
            return null;
        }

        // Прямоугольник рисунка: от опоры с учётом размера, растяжения,
        // отражения и поворота вокруг опоры (против часовой).
        // Оси рисунка: Right — ширина, Up — высота (с поворотом); вершина
        // (u, v) = опора + Right·(u − опора X) + Up·(v − опора Y).
        private static Quad QuadOf(LocationScatterInstance instance, LocationResolvedPart part, Vector2 anchor, float plantHeight)
        {
            Rect rect = part.Sprite.rect;
            float stretch = instance.Stretch > .01f ? instance.Stretch : 1;
            float height = part.Height * Scale(instance);
            float width = height * rect.width / Mathf.Max(1, rect.height) * stretch;
            float angle = instance.Rotation * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            return new Quad
            {
                Key = instance.Key, Anchor = anchor, Right = new Vector2(cos, sin) * width, Up = new Vector2(-sin, cos) * height,
                PivotX = instance.FlipX ? 1 - part.Pivot.x : part.Pivot.x, PivotY = part.Pivot.y, PlantHeight = plantHeight,
                Tint = instance.Tint, Hsv = instance.ColorAdjust.ShaderHsv, Flip = instance.FlipX ? -1 : 1, Angle = angle, Depth = anchor.y
            };
        }

        private void Fill(Group group, int groupIndex)
        {
            // Дальние (выше на рисунке) рисуются первыми.
            group.Quads.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            int count = group.Quads.Count;
            float[] rows = group.Rows ?? FlatRows;
            int perQuad = rows.Length * 2;
            int vertexCount = count * perQuad;
            Vector3[] positions = new Vector3[vertexCount];
            VertexData[] data = new VertexData[vertexCount];
            int[] indices = new int[count * (rows.Length - 1) * 6];
            if (group.Bends)
            {
                group.VertexHeight = new float[vertexCount];
                group.QuadUp = new Vector2[count];
                group.QuadDirty = new bool[count];
            }
            float tallest = 0;
            int index = 0;
            for (int i = 0; i < count; i++)
            {
                Quad quad = group.Quads[i];
                int start = i * perQuad;
                Vector4 tangent = new Vector4(Mathf.Cos(quad.Angle), Mathf.Sin(quad.Angle), 0, -1);
                float height = quad.Up.magnitude;
                for (int r = 0; r < rows.Length; r++)
                {
                    float v = rows[r];
                    for (int u = 0; u < 2; u++)
                    {
                        int vertex = start + r * 2 + u;
                        positions[vertex] = quad.Anchor + quad.Right * (u - quad.PivotX) + quad.Up * (v - quad.PivotY);
                        data[vertex] = new VertexData
                        {
                            Normal = Vector3.back, Tangent = tangent, Color = quad.Tint,
                            Uv0 = new Vector4(u, v, quad.Hsv.x, quad.Hsv.y), Uv1 = new Vector3(quad.Hsv.z, quad.Flip, quad.Angle)
                        };
                        if (group.Bends) group.VertexHeight[vertex] = (v - quad.PivotY) * height / Mathf.Max(1e-4f, quad.PlantHeight);
                    }
                    if (r == 0) continue;
                    int bottomLeft = start + (r - 1) * 2, topLeft = start + r * 2;
                    indices[index++] = bottomLeft; indices[index++] = topLeft + 1; indices[index++] = bottomLeft + 1;
                    indices[index++] = bottomLeft; indices[index++] = topLeft; indices[index++] = topLeft + 1;
                }
                if (group.Bends)
                {
                    group.QuadUp[i] = height > 1e-6f ? quad.Up / height : Vector2.up;
                    tallest = Mathf.Max(tallest, quad.PlantHeight);
                    if (bending.TryGetValue(quad.Key, out BendingPlant plant)) plant.Quads.Add(new Vector2Int(groupIndex, i));
                }
            }
            Mesh mesh = group.Mesh;
            mesh.Clear();
            mesh.SetVertexBufferParams(vertexCount, Layout);
            mesh.SetVertexBufferData(positions, 0, 0, vertexCount, 0);
            mesh.SetVertexBufferData(data, 0, 0, vertexCount, 1);
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length));
            mesh.RecalculateBounds();
            if (group.Bends)
            {
                group.Base = positions;
                group.Positions = (Vector3[])positions.Clone();
                // Запас под наибольший прогиб: край кадра не обрезает верхушки.
                Bounds bounds = mesh.bounds;
                bounds.Expand(new Vector3(2, 2, 0) * (LocationScatterReaction.MaxBend + LocationScatterReaction.MaxPress) * tallest);
                mesh.bounds = bounds;
            }
            Count += count;
            group.Quads.Clear();
            group.Shown = -1;
            ShowFrame(group, 0);
        }

        // Прогиб экземпляра: offset — смещение верха растения (единицы мира),
        // drop — опускание верха вдоль оси рисунка. Вершины считаются от
        // исходных — ошибка не накапливается; ноль возвращает исходную форму.
        public void SetBend(BendingPlant plant, Vector2 offset, float drop, float pinned, float curve = 2)
        {
            foreach (Vector2Int handle in plant.Quads)
            {
                if (handle.x < 0 || handle.x >= groups.Count) continue;
                Group group = groups[handle.x];
                if (group.Positions == null) continue;
                int perQuad = group.Rows.Length * 2;
                int start = handle.y * perQuad;
                if (handle.y < 0 || start + perQuad > group.Positions.Length) continue;
                Vector2 up = group.QuadUp[handle.y];
                Vector3 shift = offset - up * drop;
                for (int k = 0; k < perQuad; k++)
                {
                    int vertex = start + k;
                    float weight = LocationGrassBend.Weight(group.VertexHeight[vertex], pinned, curve);
                    group.Positions[vertex] = weight > 0 ? group.Base[vertex] + shift * weight : group.Base[vertex];
                }
                if (group.QuadDirty[handle.y]) continue;
                group.QuadDirty[handle.y] = true;
                group.DirtyMin = Mathf.Min(group.DirtyMin, handle.y);
                group.DirtyMax = Mathf.Max(group.DirtyMax, handle.y);
            }
        }

        // Выгрузить изменённые позиции: отрезками подряд идущих тронутых
        // прямоугольников (короткий промежуток выгружается вместе с ними),
        // а не всей сеткой и не одним диапазоном между далёкими фигурами.
        public void FlushBends()
        {
            const int gap = 8;
            foreach (Group group in groups)
            {
                if (group.DirtyMax < group.DirtyMin) continue;
                int perQuad = group.Rows.Length * 2;
                int from = -1, to = -1;
                for (int quad = group.DirtyMin; quad <= group.DirtyMax + gap + 1; quad++)
                {
                    bool dirty = quad <= group.DirtyMax && group.QuadDirty[quad];
                    if (dirty)
                    {
                        group.QuadDirty[quad] = false;
                        if (from < 0) from = quad;
                        to = quad;
                    }
                    else if (from >= 0 && quad - to > gap)
                    {
                        if (group.Mesh != null)
                            group.Mesh.SetVertexBufferData(group.Positions, from * perQuad, from * perQuad, (to - from + 1) * perQuad, 0, QuietUpdate);
                        from = -1;
                    }
                }
                group.DirtyMin = int.MaxValue;
                group.DirtyMax = -1;
            }
        }

        // Текущие позиции вершин прямоугольника (для проверок).
        public Vector3[] QuadPositions(Vector2Int handle)
        {
            Group group = handle.x >= 0 && handle.x < groups.Count ? groups[handle.x] : null;
            if (group == null) return Array.Empty<Vector3>();
            int perQuad = (group.Rows ?? FlatRows).Length * 2;
            Vector3[] source = group.Positions ?? group.Mesh.vertices;
            Vector3[] result = new Vector3[perQuad];
            Array.Copy(source, handle.y * perQuad, result, 0, perQuad);
            return result;
        }

        // Кадр анимации по времени места: своя фаза у каждой группы.
        public void Animate(double seconds)
        {
            foreach (Group group in groups)
            {
                if (group.Frames.Length < 2) continue;
                float phase = group.Buckets > 1 ? group.Bucket / (float)group.Buckets : 0;
                ShowFrame(group, ArtAssetAnimation.FrameIndex(group.Frames.Length, group.FramesPerSecond, group.Playback, seconds * group.Speed, phase));
            }
        }

        private void ShowFrame(Group group, int index)
        {
            if (group.Shown == index || group.Renderer == null) return;
            group.Shown = index;
            Sprite sprite = group.Frames[index];
            Texture texture = sprite.texture;
            Rect rect = sprite.rect;
            block.Clear();
            block.SetTexture(MainTexId, texture);
            block.SetTexture(NormalMapId, group.Normals[index] != null ? group.Normals[index] : Texture2D.normalTexture);
            block.SetVector(UvRectId, new Vector4(rect.x / texture.width, rect.y / texture.height, rect.width / texture.width, rect.height / texture.height));
            block.SetVector(LightShareId, group.LightShare);
            group.Renderer.SetPropertyBlock(block);
        }

        // Кадр, показанный группой (для проверок).
        public Sprite ShownSprite(int group) =>
            group >= 0 && group < groups.Count && groups[group].Shown >= 0 ? groups[group].Frames[groups[group].Shown] : null;

        private static void Destroy(UnityEngine.Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
