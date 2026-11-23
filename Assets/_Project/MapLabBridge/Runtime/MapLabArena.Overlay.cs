using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace KingdomSurvival.MapLabBridge
{
    // 20.11.2026: клетки — на полу, объекты — поверх клеток. Кадр арены
    // рисуется двумя слоями: нижний — весь мир (как прежде, под доской),
    // верхний — только стоящие объекты (деревья, пни, бурьян, камни, стенки,
    // завалы) на прозрачном фоне. Доска кладёт верхний слой над сеткой,
    // отметками и заливками, но под фигурами: линии клеток уходят под
    // деревья, а не ложатся поверх них. Земля в верхнем слое пишет только
    // глубину — объект за холмом закрыт холмом и там. Низкая трава и мост
    // остаются «полом» (под сеткой).
    public sealed partial class MapLabArena
    {
        // Слой стоящих объектов арены (рисуется и в нижнем кадре).
        public const int OverlayLayer = 28;
        private const int ArenaMask = (1 << Layer) | (1 << OverlayLayer);

        private static readonly HashSet<string> OverlayGroups = new HashSet<string>
        {
            "ArtDecor05", "ArtVegetation", "ArtBoulders", "ArtCliffDecor", "Vegetation", "Boulders",
        };

        // Рисунки «пола» (низкая трава, настил моста) — не в верхнем слое.
        private readonly HashSet<string> groundArtIds = new HashSet<string>();
        private readonly List<Renderer> overlayRenderers = new List<Renderer>();
        private List<Renderer> overlayOthers;
        private readonly List<Renderer> groundRenderers = new List<Renderer>();
        // Низкая трава и кусты «пола»: в кадре под сеткой, но перед бойцом они его закрывают.
        private readonly List<Renderer> floorCards = new List<Renderer>();
        private RenderTexture overlayTarget, overlayGraded;
        private bool overlayGradedShown;
        private Material depthOnly;
        private static Shader depthOnlyShader;

        // Верхний слой последнего кадра (null — объектов нет или слой недоступен).
        public RenderTexture OverlayTexture => overlayRenderers.Count == 0 ? null : overlayGradedShown && overlayGraded != null ? overlayGraded : overlayTarget;
        public int OverlayRendererCount => overlayRenderers.Count;

        private void SplitOverlay()
        {
            overlayRenderers.Clear();
            overlayOthers = null;
            groundRenderers.Clear();
            floorCards.Clear();
            if (depthOnlyShader == null)
                depthOnlyShader = Resources.Load<Shader>("MapLabArenaDepthOnly");
            if (depthOnlyShader == null || !depthOnlyShader.isSupported)
            {
                Debug.LogWarning("[MapLabArena] Нет шейдера MapLabArenaDepthOnly — объекты арены остаются под сеткой.");
                return;
            }
            foreach (Renderer renderer in view.GetComponentsInChildren<Renderer>(true))
            {
                string group = GroupOf(renderer.transform);
                if (group == "Terrain")
                {
                    groundRenderers.Add(renderer);
                    continue;
                }
                if (group == null || !OverlayGroups.Contains(group))
                    continue;
                Material material = renderer.sharedMaterial;
                string name = material != null ? material.name : string.Empty;
                const string prefix = "MapLab Art ";
                if (name.StartsWith(prefix) && groundArtIds.Contains(name.Substring(prefix.Length)))
                {
                    floorCards.Add(renderer);
                    continue;
                }
                renderer.gameObject.layer = OverlayLayer;
                overlayRenderers.Add(renderer);
            }
            if (overlayRenderers.Count > 0)
                depthOnly = new Material(depthOnlyShader) { name = "MapLab Arena Depth Only", hideFlags = HideFlags.HideAndDontSave };
        }

        // Имя группы мира MapLab (прямой потомок корня вида), в которой лежит объект.
        private string GroupOf(Transform t)
        {
            Transform viewRoot = view.transform;
            while (t != null && t.parent != null && t.parent.parent != viewRoot && t.parent != viewRoot)
                t = t.parent;
            return t != null && t.parent != null && t.parent.parent == viewRoot ? t.name : null;
        }

        // Верхний слой: только объекты, земля — глубиной, фон прозрачный.
        // С обработкой кадра (graded) слой рисуется дважды: без обработки —
        // ради прозрачности, с обработкой — ради цвета; склейка — шейдером
        // MapLabArenaAlphaMerge (URP после обработки выводит альфу 1).
        private void RenderOverlay(bool graded)
        {
            overlayGradedShown = false;
            if (overlayRenderers.Count == 0 || depthOnly == null || target == null)
                return;
            overlayTarget = EnsureLayerTexture(overlayTarget, "MapLab Arena Objects");
            // Кусты и трава «пола» в этом слое — только глубина: ближний куст вырезает дерево за ним,
            // сам куст остаётся в нижнем кадре (под сеткой).
            FloorDepthOnly(true);
            try { overlayGradedShown = RenderOverlayInto(overlayTarget, ref overlayGraded, graded); }
            finally { FloorDepthOnly(false); }
        }

        private bool RenderOverlayInto(RenderTexture rawTarget, ref RenderTexture gradedTarget, bool graded, RenderTexture keyTarget = null)
        {
            Shader.SetGlobalVector(ShadowId, Vector4.zero);
            bool gradedDone = false;
            int mask = camera.cullingMask;
            Color background = camera.backgroundColor;
            Material[] saved = new Material[groundRenderers.Count];
            for (int i = 0; i < groundRenderers.Count; i++)
            {
                Renderer ground = groundRenderers[i];
                if (ground == null)
                    continue;
                saved[i] = ground.sharedMaterial;
                ground.sharedMaterial = depthOnly;
                ground.gameObject.layer = OverlayLayer;
            }
            try
            {
                camera.cullingMask = 1 << OverlayLayer;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                if (graded)
                    framePost.Suspend(camera);
                // Ключевой режим (общий слой строк «впереди бойца»): альфа берётся из слоя-ключа, исходный цветной слой не нужен.
                bool merging = graded && MergeMaterial() != null;
                if (keyTarget != null)
                {
                    Shader.SetGlobalFloat(KeyOutId, 1f);
                    try { RenderLayer(keyTarget); }
                    finally { Shader.SetGlobalFloat(KeyOutId, 0f); }
                }
                if (keyTarget == null || !merging)
                    RenderLayer(rawTarget);
                if (graded && MergeMaterial() != null)
                {
                    framePost.Begin(camera, Spec.Frame, LensPulse != null ? LensPulse() : 0f);
                    RenderTexture colour = RenderTexture.GetTemporary(new RenderTextureDescriptor(rawTarget.width, rawTarget.height, GraphicsFormat.R8G8B8A8_SRGB, 24));
                    RenderLayer(colour);
                    gradedTarget = EnsureLayerTexture(gradedTarget, "MapLab Arena Objects Graded");
                    alphaMerge.SetTexture(GradedTexId, colour);
                    Graphics.Blit(keyTarget != null ? keyTarget : rawTarget, gradedTarget, alphaMerge);
                    RenderTexture.ReleaseTemporary(colour);
                    gradedDone = true;
                }
            }
            finally
            {
                camera.cullingMask = mask;
                camera.backgroundColor = background;
                for (int i = 0; i < groundRenderers.Count; i++)
                {
                    Renderer ground = groundRenderers[i];
                    if (ground == null)
                        continue;
                    ground.sharedMaterial = saved[i];
                    ground.gameObject.layer = Layer;
                }
            }
            return gradedDone;
        }



        private readonly Dictionary<Material, Material> floorDepthMaterials = new Dictionary<Material, Material>();
        private Material[] floorSaved;

        // Карточки «пола» на время слоя объектов: тот же шейдер и прогиб, но без цвета и раньше
        // остальных — они пишут глубину, и дальние объекты (стволы) за ними не рисуются.
        private void FloorDepthOnly(bool on)
        {
            if (on)
            {
                floorSaved = new Material[floorCards.Count];
                for (int i = 0; i < floorCards.Count; i++)
                {
                    Renderer card = floorCards[i];
                    if (card == null) continue;
                    Material source = card.sharedMaterial;
                    if (source == null) continue;
                    floorSaved[i] = source;
                    if (!floorDepthMaterials.TryGetValue(source, out Material copy) || copy == null)
                        floorDepthMaterials[source] = copy = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
                    else
                        copy.CopyPropertiesFromMaterial(source);
                    copy.SetFloat("_ColorMask", 0f);
                    copy.renderQueue = 2400;
                    card.sharedMaterial = copy;
                    card.gameObject.layer = OverlayLayer;
                }
                return;
            }
            if (floorSaved == null) return;
            for (int i = 0; i < floorCards.Count; i++)
            {
                Renderer card = floorCards[i];
                if (card == null || floorSaved[i] == null) continue;
                card.sharedMaterial = floorSaved[i];
                card.gameObject.layer = Layer;
            }
            floorSaved = null;
        }
        // ---------- слои «впереди бойца» ----------
        // Фигуры бойцов — элементы UI над кадром арены, поэтому куст или дерево перед
        // бойцом без этого слоя оказывались бы под ним. Для каждой строки земли с бойцами
        // рисуется слой только тех объектов, чья опора ближе к зрителю, чем ноги бойца;
        // доска кладёт его поверх бойцов этой строки (и дальних), под ближними.
        private const int MaxFrontLayers = 10;
        private readonly RenderTexture[] frontRaw = new RenderTexture[MaxFrontLayers];
        private readonly RenderTexture[] frontGraded = new RenderTexture[MaxFrontLayers];
        private readonly List<Renderer> frontHidden = new List<Renderer>();
        private static readonly int FrontOnId = Shader.PropertyToID("_MapLabFrontOn");
        private static readonly int FrontZId = Shader.PropertyToID("_MapLabFrontZ");

        // Кадр доли высоты (Y вниз) → мировая глубина земли.
        public float GroundZ(float frameShareY) => ToWorld(new Vector2(0f, frameShareY)).y;

        // Слой объектов впереди глубины clipZ (мир) в слот index; null — объектов нет.
        // Вызывать сразу после Render (камера стоит как в кадре).
        public RenderTexture RenderFront(int index, float clipZ)
        {
            if (index < 0 || index >= MaxFrontLayers || overlayRenderers.Count == 0 || depthOnly == null || target == null || !Ready)
                return null;
            if (frontSourceReady)
                return ComposeFront(index, clipZ);
            frontRaw[index] = EnsureLayerTexture(frontRaw[index], "MapLab Arena Front " + index);
            bool done = RenderFrontInto(frontRaw[index], ref frontGraded[index], null, clipZ);
            return done ? frontGraded[index] : frontRaw[index];
        }

        // Строки «впереди бойца» из одного общего слоя: 2 прохода камеры вместо 2 на каждую строку.
        // Вызывать после Render, до RenderFront; rows — сколько строк будет запрошено.
        public const int FrontSourceMinRows = 3;
        private bool frontSourceReady, frontSourceGraded;
        private RenderTexture srcRaw, srcGraded, srcKey;
        private Material rowMaterial;
        private static Shader rowShader;
        private static readonly int KeyOutId = Shader.PropertyToID("_MapLabKeyOut");
        private static readonly int KeyRangeId = Shader.PropertyToID("_MapLabKeyRange");
        private static readonly int KeyTexId = Shader.PropertyToID("_KeyTex");
        private static readonly int ClipKeyId = Shader.PropertyToID("_ClipKey");

        private float KeyMin => -view.Data.Size * 0.5f;
        private float KeyLength => view.Data.Size * 2f;

        public void PrepareFronts(int rows)
        {
            frontSourceReady = false;
            if (rows < FrontSourceMinRows || overlayRenderers.Count == 0 || depthOnly == null || target == null || !Ready)
                return;
            if (rowShader == null) rowShader = Resources.Load<Shader>("MapLabArenaFrontRow");
            if (rowShader == null || !rowShader.isSupported) return;
            if (rowMaterial == null) rowMaterial = new Material(rowShader) { name = "MapLab Arena Front Row", hideFlags = HideFlags.HideAndDontSave };
            srcRaw = EnsureLayerTexture(srcRaw, "MapLab Arena Front Source");
            srcKey = EnsureLayerTexture(srcKey, "MapLab Arena Front Key");
            srcKey.filterMode = FilterMode.Point;
            Shader.SetGlobalVector(KeyRangeId, new Vector4(KeyMin, 1f / KeyLength, 0f, 0f));
            frontSourceGraded = RenderFrontInto(srcRaw, ref srcGraded, srcKey, 0f);
            frontSourceReady = true;
        }

        private RenderTexture ComposeFront(int index, float clipZ)
        {
            frontRaw[index] = EnsureLayerTexture(frontRaw[index], "MapLab Arena Front " + index);
            rowMaterial.SetTexture(KeyTexId, srcKey);
            rowMaterial.SetFloat(ClipKeyId, Mathf.Clamp01((clipZ - 0.05f - KeyMin) / KeyLength));
            Graphics.Blit(frontSourceGraded ? srcGraded : srcRaw, frontRaw[index], rowMaterial);
            return frontRaw[index];
        }

        private bool RenderFrontInto(RenderTexture raw, ref RenderTexture gradedTex, RenderTexture key, float clipZ)
        {
            frontHidden.Clear();
            // Клип есть только у шейдера карточек рисунков: прочие объекты в этом слое не нужны.
            // Список не-карточек собирается один раз (имя шейдера — строка, на каждый слой кадра это сотни выделений).
            if (overlayOthers == null)
            {
                overlayOthers = new List<Renderer>();
                foreach (Renderer renderer in overlayRenderers)
                {
                    Material material = renderer != null ? renderer.sharedMaterial : null;
                    if (!(material != null && material.shader != null && material.shader.name == "Kingdom Survival/MapLab/Art Card"))
                        overlayOthers.Add(renderer);
                }
            }
            foreach (Renderer renderer in overlayOthers)
            {
                if (renderer == null || !renderer.enabled) continue;
                renderer.enabled = false;
                frontHidden.Add(renderer);
            }
            foreach (Renderer card in floorCards)
                if (card != null) card.gameObject.layer = OverlayLayer;
            Shader.SetGlobalFloat(FrontOnId, key != null ? 0f : 1f);
            Shader.SetGlobalFloat(FrontZId, clipZ - 0.05f);
            ApplyWalkers();
            // Тот же миг ветра, что у кадра (слой впереди бойца совпадает с ним).
            ApplyWind(false);
            try
            {
                bool graded = Spec.Frame != null && Spec.Frame.Enabled && framePost != null;
                return RenderOverlayInto(raw, ref gradedTex, graded, key);
            }
            finally
            {
                Shader.SetGlobalFloat(FrontOnId, 0f);
                foreach (Renderer card in floorCards)
                    if (card != null) card.gameObject.layer = Layer;
                ClearWalkers();
                ClearWind();
                foreach (Renderer renderer in frontHidden)
                    if (renderer != null) renderer.enabled = true;
                frontHidden.Clear();
                framePost?.End(camera);
            }
        }
        private RenderTexture EnsureLayerTexture(RenderTexture texture, string name)
        {
            if (texture != null && texture.width == target.width && texture.height == target.height)
                return texture;
            if (texture != null)
                released.Add((texture, Time.frameCount + ReleaseDelayFrames));
            texture = new RenderTexture(target.width, target.height, 24, GraphicsFormat.R8G8B8A8_SRGB)
            {
                name = name,
                antiAliasing = 1,
                hideFlags = HideFlags.DontSave,
            };
            texture.Create();
            return texture;
        }

        private void RenderLayer(RenderTexture destination)
        {
            if (EffectiveFrameMsaa > 1)
            {
                RenderTextureDescriptor descriptor = new RenderTextureDescriptor(destination.width, destination.height, GraphicsFormat.R8G8B8A8_SRGB, 24) { msaaSamples = EffectiveFrameMsaa };
                RenderTexture frame = RenderTexture.GetTemporary(descriptor);
                camera.targetTexture = frame;
                camera.Render();
                camera.targetTexture = null;
                Graphics.Blit(frame, destination);
                RenderTexture.ReleaseTemporary(frame);
            }
            else
            {
                camera.targetTexture = destination;
                camera.Render();
                camera.targetTexture = null;
            }
        }

        private static readonly int GradedTexId = Shader.PropertyToID("_GradedTex");
        private static Shader alphaMergeShader;
        private Material alphaMerge;

        private Material MergeMaterial()
        {
            if (alphaMerge != null)
                return alphaMerge;
            if (alphaMergeShader == null)
                alphaMergeShader = Resources.Load<Shader>("MapLabArenaAlphaMerge");
            if (alphaMergeShader == null || !alphaMergeShader.isSupported)
                return null;
            alphaMerge = new Material(alphaMergeShader) { name = "MapLab Arena Alpha Merge", hideFlags = HideFlags.HideAndDontSave };
            return alphaMerge;
        }

        // Обработка кадра этой арены (Spec.Frame): true — кадр идёт с ней.
        private ArenaFramePost framePost;

        private bool BeginFrame()
        {
            KingdomSurvival.BattlefieldDatabase.LocationPostEffects post = Spec.Frame;
            if (post == null || !post.Enabled)
            {
                framePost?.Begin(camera, null);
                return false;
            }
            framePost ??= new ArenaFramePost(Layer);
            return framePost.Begin(camera, post, LensPulse != null ? LensPulse() : 0f);
        }

        private void ReleaseOverlay()
        {
            foreach (RenderTexture texture in new[] { overlayTarget, overlayGraded, srcRaw, srcGraded, srcKey })
            {
                if (texture == null)
                    continue;
                texture.Release();
                Object.DestroyImmediate(texture);
            }
            for (int i = 0; i < MaxFrontLayers; i++)
                foreach (RenderTexture texture in new[] { frontRaw[i], frontGraded[i] })
                    if (texture != null)
                    {
                        texture.Release();
                        Object.DestroyImmediate(texture);
                    }
            foreach (Material copy in floorDepthMaterials.Values)
                if (copy != null) Object.DestroyImmediate(copy);
            floorDepthMaterials.Clear();
            System.Array.Clear(frontRaw, 0, MaxFrontLayers);
            System.Array.Clear(frontGraded, 0, MaxFrontLayers);
            overlayTarget = null;
            overlayGraded = null;
            srcRaw = srcGraded = srcKey = null;
            frontSourceReady = false;
            if (rowMaterial != null) { Object.DestroyImmediate(rowMaterial); rowMaterial = null; }
            if (depthOnly != null)
            {
                Object.DestroyImmediate(depthOnly);
                depthOnly = null;
            }
            if (alphaMerge != null)
            {
                Object.DestroyImmediate(alphaMerge);
                alphaMerge = null;
            }
        }
    }
}
