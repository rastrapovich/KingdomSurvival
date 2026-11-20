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
        private readonly List<Renderer> groundRenderers = new List<Renderer>();
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
            groundRenderers.Clear();
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
                    continue;
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
                RenderLayer(overlayTarget);
                if (graded && MergeMaterial() != null)
                {
                    framePost.Begin(camera, Spec.Frame);
                    RenderTexture colour = RenderTexture.GetTemporary(new RenderTextureDescriptor(overlayTarget.width, overlayTarget.height, GraphicsFormat.R8G8B8A8_SRGB, 24));
                    RenderLayer(colour);
                    overlayGraded = EnsureLayerTexture(overlayGraded, "MapLab Arena Objects Graded");
                    alphaMerge.SetTexture(GradedTexId, colour);
                    Graphics.Blit(overlayTarget, overlayGraded, alphaMerge);
                    RenderTexture.ReleaseTemporary(colour);
                    overlayGradedShown = true;
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
            return framePost.Begin(camera, post);
        }

        private void ReleaseOverlay()
        {
            foreach (RenderTexture texture in new[] { overlayTarget, overlayGraded })
            {
                if (texture == null)
                    continue;
                texture.Release();
                Object.DestroyImmediate(texture);
            }
            overlayTarget = null;
            overlayGraded = null;
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
