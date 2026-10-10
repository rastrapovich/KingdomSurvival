using System;
using System.Collections.Generic;
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
    public sealed class LocationScatterCarpet
    {
        public const int MaxPhaseBuckets = 8;

        private sealed class Group
        {
            public GameObject Object;
            public MeshRenderer Renderer;
            public Mesh Mesh;
            public Sprite[] Frames;
            public Texture[] Normals;
            public int Bucket, Buckets;
            public float FramesPerSecond;
            public ArtAssetPlayback Playback;
            public int Shown = -1;
            // Свет ассета: солнце и огонь по отдельности.
            public Vector4 LightShare = new Vector4(1, 1, 0, 0);
            public readonly List<Quad> Quads = new List<Quad>();
        }

        private struct Quad
        {
            public Vector2 A, B, C, D;
            public Color Tint;
            public Vector4 Hsv;
            public float Flip, Angle, Depth;
        }

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

        public int Count { get; private set; }
        public int GroupCount => groups.Count;

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
            Count = 0;
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
                for (int p = 0; p < resolved.Parts.Count; p++)
                {
                    LocationResolvedPart part = resolved.Parts[p];
                    if (part.Sprite == null) continue;
                    Sprite[] frames = part.Animated ? part.Frames : new[] { part.Sprite };
                    int buckets = frames.Length > 1 && resolved.FramesPerSecond > 0 ? Mathf.Min(MaxPhaseBuckets, frames.Length * 2) : 1;
                    int bucket = buckets > 1 && asset.RandomPhase
                        ? Mathf.Min(buckets - 1, Mathf.FloorToInt(ArtAssetAnimation.StablePhase(layer.InstanceId(instance)) * buckets)) : 0;
                    string key = baseKey + "|" + p + "|" + bucket;
                    if (!byKey.TryGetValue(key, out Group group))
                    {
                        group = CreateGroup(asset, resolved, frames, bucket, buckets, order + part.OrderOffset, key);
                        byKey[key] = group;
                    }
                    group.Quads.Add(QuadOf(instance, part, anchor));
                }
            }
            foreach (Group group in groups) Fill(group);
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
                LightShare = LocationWorldRenderer.LightShare(resolved)
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
        private static Quad QuadOf(LocationScatterInstance instance, LocationResolvedPart part, Vector2 anchor)
        {
            Rect rect = part.Sprite.rect;
            float scale = instance.Scale > 0 ? instance.Scale : 1;
            float stretch = instance.Stretch > .01f ? instance.Stretch : 1;
            float height = part.Height * scale;
            float width = height * rect.width / Mathf.Max(1, rect.height) * stretch;
            float pivotX = instance.FlipX ? 1 - part.Pivot.x : part.Pivot.x;
            float left = -pivotX * width, bottom = -part.Pivot.y * height;
            float angle = instance.Rotation * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            Vector2 Corner(float x, float y) => anchor + new Vector2(x * cos - y * sin, x * sin + y * cos);
            return new Quad
            {
                A = Corner(left, bottom), B = Corner(left + width, bottom), C = Corner(left + width, bottom + height), D = Corner(left, bottom + height),
                Tint = instance.Tint, Hsv = instance.ColorAdjust.ShaderHsv, Flip = instance.FlipX ? -1 : 1, Angle = angle, Depth = anchor.y
            };
        }

        private void Fill(Group group)
        {
            // Дальние (выше на рисунке) рисуются первыми.
            group.Quads.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            int count = group.Quads.Count;
            List<Vector3> vertices = new List<Vector3>(count * 4);
            List<Vector3> normals = new List<Vector3>(count * 4);
            List<Vector4> tangents = new List<Vector4>(count * 4);
            List<Color> colors = new List<Color>(count * 4);
            List<Vector4> uv0 = new List<Vector4>(count * 4);
            List<Vector3> uv1 = new List<Vector3>(count * 4);
            int[] indices = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                Quad quad = group.Quads[i];
                int start = i * 4;
                vertices.Add(quad.A); vertices.Add(quad.B); vertices.Add(quad.C); vertices.Add(quad.D);
                Vector4 tangent = new Vector4(Mathf.Cos(quad.Angle), Mathf.Sin(quad.Angle), 0, -1);
                for (int k = 0; k < 4; k++)
                {
                    normals.Add(Vector3.back);
                    tangents.Add(tangent);
                    colors.Add(quad.Tint);
                    uv1.Add(new Vector3(quad.Hsv.z, quad.Flip, quad.Angle));
                }
                uv0.Add(new Vector4(0, 0, quad.Hsv.x, quad.Hsv.y));
                uv0.Add(new Vector4(1, 0, quad.Hsv.x, quad.Hsv.y));
                uv0.Add(new Vector4(1, 1, quad.Hsv.x, quad.Hsv.y));
                uv0.Add(new Vector4(0, 1, quad.Hsv.x, quad.Hsv.y));
                indices[i * 6] = start; indices[i * 6 + 1] = start + 2; indices[i * 6 + 2] = start + 1;
                indices[i * 6 + 3] = start; indices[i * 6 + 4] = start + 3; indices[i * 6 + 5] = start + 2;
            }
            Mesh mesh = group.Mesh;
            mesh.Clear();
            mesh.indexFormat = count * 4 > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            Count += count;
            group.Quads.Clear();
            group.Shown = -1;
            ShowFrame(group, 0);
        }

        // Кадр анимации по времени места: своя фаза у каждой группы.
        public void Animate(double seconds)
        {
            foreach (Group group in groups)
            {
                if (group.Frames.Length < 2) continue;
                float phase = group.Buckets > 1 ? group.Bucket / (float)group.Buckets : 0;
                ShowFrame(group, ArtAssetAnimation.FrameIndex(group.Frames.Length, group.FramesPerSecond, group.Playback, seconds, phase));
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
