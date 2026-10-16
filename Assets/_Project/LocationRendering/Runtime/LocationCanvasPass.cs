using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12М: «Холст» обработки кадра — свои полноэкранные проходы поверх
    // кадра места (шейдер LocationCanvas). Без правки ассетов рендерера:
    // перед кадром камеры места (своей или предпросмотра окна базы) проход
    // добавляется в очередь её рендерера. Камера без обработки кадра
    // (renderPostProcessing выключен — «до» сравнения) холста не получает.
    // До обработки URP — живописность и туман; после — палитра и бумага.
    public static class LocationCanvasPass
    {
        private const string ShaderPath = "LocationRendering/LocationCanvas";
        public const int MaxPaletteColors = 64;

        private static readonly Dictionary<Camera, LocationWorldRenderer> bound = new Dictionary<Camera, LocationWorldRenderer>();
        private static readonly CanvasPass before = new CanvasPass(0, RenderPassEvent.BeforeRenderingPostProcessing, "Холст места · до обработки");
        private static readonly CanvasPass after = new CanvasPass(1, RenderPassEvent.AfterRenderingPostProcessing, "Холст места · после обработки");
        private static readonly Vector4[] palette = new Vector4[MaxPaletteColors];
        private static readonly Dictionary<Texture2D, KeyValuePair<string, Vector4[]>> paletteCache = new Dictionary<Texture2D, KeyValuePair<string, Vector4[]>>();
        private static Material material;
        private static bool registered;

        private static readonly int PainterlyId = Shader.PropertyToID("_KsPainterly");
        private static readonly int FogColorId = Shader.PropertyToID("_KsFogColor");
        private static readonly int FogId = Shader.PropertyToID("_KsFog");
        private static readonly int FogNoiseId = Shader.PropertyToID("_KsFogNoise");
        private static readonly int PaletteId = Shader.PropertyToID("_KsPalette");
        private static readonly int PaletteColorsId = Shader.PropertyToID("_KsPaletteColors");
        private static readonly int PaperId = Shader.PropertyToID("_KsPaper");
        private static readonly int PaperTexId = Shader.PropertyToID("_KsPaperTex");
        private static readonly int TexelId = Shader.PropertyToID("_KsCanvasTexel");

        // Сколько раз проходы холста попали в кадр (проверки).
        public static int RecordedPasses { get; private set; }

        public static void Bind(Camera camera, LocationWorldRenderer renderer)
        {
            if (camera == null || renderer == null) return;
            if (!registered)
            {
                registered = true;
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            }
            bound[camera] = renderer;
        }

        public static void Unbind(LocationWorldRenderer renderer)
        {
            List<Camera> gone = null;
            foreach (KeyValuePair<Camera, LocationWorldRenderer> entry in bound)
                if (entry.Value == renderer || entry.Key == null) (gone ??= new List<Camera>()).Add(entry.Key);
            if (gone != null) foreach (Camera camera in gone) bound.Remove(camera);
        }

        private static Material Material()
        {
            if (material != null) return material;
            Shader shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null || !shader.isSupported) return null;
            material = new Material(shader) { name = "Холст места", hideFlags = HideFlags.HideAndDontSave };
            material.SetVectorArray(PaletteColorsId, palette);
            return material;
        }

        private static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || !bound.TryGetValue(camera, out LocationWorldRenderer renderer) || renderer == null) return;
            LocationCanvasEffects canvas = renderer.ActiveCanvas;
            if (canvas == null) return;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data == null || !data.renderPostProcessing) return;
            ScriptableRenderer scriptable = data.scriptableRenderer;
            Material shared = Material();
            if (scriptable == null || shared == null) return;
            bool early = canvas.Painterly || canvas.Fog;
            bool late = canvas.Palette || canvas.Paper;
            if (!early && !late) return;
            Write(shared, canvas, renderer.Seconds);
            if (early) { before.Material = shared; scriptable.EnqueuePass(before); }
            if (late) { after.Material = shared; scriptable.EnqueuePass(after); }
        }

        private static void Write(Material target, LocationCanvasEffects canvas, float seconds)
        {
            target.SetVector(PainterlyId, new Vector4(Mathf.Clamp(canvas.PainterlyRadius, 1, 8), Mathf.Clamp01(canvas.PainterlyStrength), canvas.Painterly ? 1 : 0, 0));
            target.SetColor(FogColorId, canvas.FogColor);
            target.SetVector(FogId, new Vector4(canvas.Fog ? Mathf.Clamp01(canvas.FogDensity) : 0, Mathf.Clamp01(canvas.FogStart), Mathf.Clamp01(canvas.FogEnd),
                canvas.FogDirection == LocationFogDirection.FromBottom ? 1 : 0));
            target.SetVector(FogNoiseId, new Vector4(Mathf.Clamp01(canvas.FogNoise), Mathf.Max(.1f, canvas.FogNoiseScale), canvas.FogSpeed, seconds));
            int colors = 0;
            if (canvas.Palette && canvas.PaletteTexture != null)
            {
                Vector4[] source = PaletteOf(canvas.PaletteTexture);
                colors = source.Length;
                for (int i = 0; i < MaxPaletteColors; i++) palette[i] = i < colors ? source[i] : Vector4.zero;
                target.SetVectorArray(PaletteColorsId, palette);
            }
            target.SetVector(PaletteId, new Vector4(Mathf.Clamp(canvas.PaletteLevels, 2, 64), canvas.Palette ? Mathf.Clamp01(canvas.PaletteStrength) : 0,
                Mathf.Clamp01(canvas.PaletteDither), colors));
            target.SetVector(PaperId, new Vector4(canvas.Paper ? Mathf.Clamp01(canvas.PaperStrength) : 0, Mathf.Max(.05f, canvas.PaperScale),
                Mathf.Clamp01(canvas.PaperFibers), canvas.PaperTexture != null ? 1 : 0));
            target.SetTexture(PaperTexId, canvas.PaperTexture != null ? canvas.PaperTexture : Texture2D.grayTexture);
        }

        // Цвета палитры из рисунка: каждый пиксель — цвет, до 64 разных (по
        // порядку строк сверху). Текстура может быть нечитаемой — через
        // временную цель отрисовки. Кэш — по экземпляру и размеру.
        public static Vector4[] PaletteOf(Texture2D texture)
        {
            if (texture == null) return System.Array.Empty<Vector4>();
            string key = texture.width + "x" + texture.height + ":" + texture.updateCount;
            if (paletteCache.TryGetValue(texture, out KeyValuePair<string, Vector4[]> cached) && cached.Key == key) return cached.Value;
            int width = Mathf.Clamp(texture.width, 1, 64), height = Mathf.Clamp(texture.height, 1, 64);
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            temporary.filterMode = FilterMode.Point;
            RenderTexture active = RenderTexture.active;
            Graphics.Blit(texture, temporary);
            RenderTexture.active = temporary;
            Texture2D read = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            read.Apply();
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(temporary);
            Color32[] pixels = read.GetPixels32();
            Object.DestroyImmediate(read);
            List<Vector4> colors = new List<Vector4>();
            HashSet<int> seen = new HashSet<int>();
            for (int y = height - 1; y >= 0 && colors.Count < MaxPaletteColors; y--)
            {
                for (int x = 0; x < width && colors.Count < MaxPaletteColors; x++)
                {
                    Color32 c = pixels[y * width + x];
                    if (c.a < 128) continue;
                    int id = c.r << 16 | c.g << 8 | c.b;
                    if (!seen.Add(id)) continue;
                    // Цвета палитры — в sRGB, как в рисунке: шейдер сравнивает в гамме экрана.
                    Color color = new Color32(c.r, c.g, c.b, 255);
                    colors.Add(new Vector4(color.r, color.g, color.b, 1));
                }
            }
            Vector4[] result = colors.ToArray();
            paletteCache[texture] = new KeyValuePair<string, Vector4[]>(key, result);
            return result;
        }

        private sealed class CanvasPass : ScriptableRenderPass
        {
            private readonly int shaderPass;
            private readonly string name;
            public Material Material;

            public CanvasPass(int shaderPass, RenderPassEvent passEvent, string name)
            {
                this.shaderPass = shaderPass;
                this.name = name;
                renderPassEvent = passEvent;
                requiresIntermediateTexture = true;
                profilingSampler = new ProfilingSampler(name);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (Material == null) return;
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData camera = frameData.Get<UniversalCameraData>();
                if (resources == null || resources.isActiveTargetBackBuffer) return;
                TextureHandle source = resources.activeColorTexture;
                if (!source.IsValid()) return;
                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = "_KsCanvasColor";
                desc.clearBuffer = false;
                desc.msaaSamples = MSAASamples.None;
                desc.depthBufferBits = DepthBits.None;
                int width = camera.cameraTargetDescriptor.width, height = camera.cameraTargetDescriptor.height;
                Material.SetVector(TexelId, new Vector4(1f / Mathf.Max(1, width), 1f / Mathf.Max(1, height), width, height));
                TextureHandle destination = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, Material, shaderPass), name);
                resources.cameraColor = destination;
                RecordedPasses++;
            }
        }
    }
}
