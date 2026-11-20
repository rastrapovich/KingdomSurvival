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
        private RenderTexture overlayTarget;
        private Material depthOnly;
        private static Shader depthOnlyShader;

        // Верхний слой последнего кадра (null — объектов нет или слой недоступен).
        public RenderTexture OverlayTexture => overlayRenderers.Count > 0 ? overlayTarget : null;
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
        private void RenderOverlay()
        {
            if (overlayRenderers.Count == 0 || depthOnly == null || target == null)
                return;
            if (overlayTarget == null || overlayTarget.width != target.width || overlayTarget.height != target.height)
            {
                if (overlayTarget != null)
                    released.Add((overlayTarget, Time.frameCount + ReleaseDelayFrames));
                overlayTarget = new RenderTexture(target.width, target.height, 24, GraphicsFormat.R8G8B8A8_SRGB)
                {
                    name = "MapLab Arena Objects",
                    antiAliasing = 1,
                    hideFlags = HideFlags.DontSave,
                };
                overlayTarget.Create();
            }
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
                if (EffectiveFrameMsaa > 1)
                {
                    RenderTextureDescriptor descriptor = new RenderTextureDescriptor(overlayTarget.width, overlayTarget.height, GraphicsFormat.R8G8B8A8_SRGB, 24) { msaaSamples = EffectiveFrameMsaa };
                    RenderTexture frame = RenderTexture.GetTemporary(descriptor);
                    camera.targetTexture = frame;
                    camera.Render();
                    camera.targetTexture = null;
                    Graphics.Blit(frame, overlayTarget);
                    RenderTexture.ReleaseTemporary(frame);
                }
                else
                {
                    camera.targetTexture = overlayTarget;
                    camera.Render();
                    camera.targetTexture = null;
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

        private void ReleaseOverlay()
        {
            if (overlayTarget != null)
            {
                overlayTarget.Release();
                Object.DestroyImmediate(overlayTarget);
                overlayTarget = null;
            }
            if (depthOnly != null)
            {
                Object.DestroyImmediate(depthOnly);
                depthOnly = null;
            }
        }
    }
}
