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
            LocationPostEffects post = renderer.PostActive ? renderer.Post : null;
            if (post == null) return;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data == null || !data.renderPostProcessing) return;
            ScriptableRenderer scriptable = data.scriptableRenderer;
            Material shared = Material();
            if (scriptable == null || shared == null) return;
            LocationCanvasEffects canvas = post.Canvas;
            LocationFrameRuntime state = renderer.FrameRuntime();
            LocationFrameQualityLevel quality = LocationWorldRenderer.QualityLevel;
            bool heavy = quality == LocationFrameQualityLevel.High;
            bool early = canvas.Painterly && quality != LocationFrameQualityLevel.Low || canvas.Fog || canvas.Sharpen || canvas.TiltShift && heavy || canvas.Ink ||
                         post.Air.Clouds && state.Outdoor || post.Air.Rays && state.Outdoor && quality != LocationFrameQualityLevel.Low ||
                         post.Air.HeatHaze && state.Fires.Count > 0 && heavy || state.Stun > .001f;
            bool late = canvas.Palette || canvas.Paper || post.Tone.SelectiveColor || post.Tone.GradientMap || canvas.Hatching || canvas.Border || canvas.Film ||
                        state.LowHealth > .001f || state.Stun > .001f || state.Flash > .001f || state.Fade > .001f || state.Letterbox > .001f;
            if (!early && !late) return;
            Write(shared, canvas, renderer.Seconds);
            WriteExtra(shared, post, state, camera, quality);
            if (early) { before.Material = shared; scriptable.EnqueuePass(before); }
            if (late) { after.Material = shared; scriptable.EnqueuePass(after); }
        }

        private static readonly Vector4[] haze = new Vector4[8];
        private static readonly Vector4[] selective = new Vector4[6];
        private static readonly Dictionary<Gradient, KeyValuePair<int, Texture2D>> gradients = new Dictionary<Gradient, KeyValuePair<int, Texture2D>>();

        private static void WriteExtra(Material target, LocationPostEffects post, LocationFrameRuntime state, Camera camera, LocationFrameQualityLevel quality)
        {
            LocationCanvasEffects canvas = post.Canvas;
            LocationAirEffects air = post.Air;
            LocationGameEffects game = post.Game;
            LocationToneEffects tone = post.Tone;
            bool heavy = quality == LocationFrameQualityLevel.High;
            float seconds = state.Seconds;
            float halfHeight = camera.orthographic ? camera.orthographicSize : 5;
            Vector3 position = camera.transform.position;
            target.SetVector(WorldId, new Vector4(position.x, position.y, halfHeight * camera.aspect, halfHeight));

            // Живописность на среднем качестве — меньшим радиусом, на низком — нет.
            if (quality == LocationFrameQualityLevel.Medium && canvas.Painterly)
                target.SetVector(PainterlyId, new Vector4(Mathf.Clamp(canvas.PainterlyRadius, 1, 3), Mathf.Clamp01(canvas.PainterlyStrength), 1, 0));
            if (quality == LocationFrameQualityLevel.Low)
                target.SetVector(PainterlyId, Vector4.zero);

            int fires = 0;
            if (air.HeatHaze && heavy)
                foreach (Vector2 fire in state.Fires)
                {
                    if (fires >= haze.Length) break;
                    Vector3 viewport = camera.WorldToViewportPoint(new Vector3(fire.x, fire.y, 0));
                    if (viewport.x < -.3f || viewport.x > 1.3f || viewport.y < -.3f || viewport.y > 1.3f) continue;
                    haze[fires++] = new Vector4(viewport.x, viewport.y, Mathf.Max(.01f, air.HazeRadius) / (2 * halfHeight), Mathf.Clamp(air.HazeStrength, 0, 3) * state.FireShare);
                }
            target.SetVectorArray(HazeId, haze);
            target.SetVector(HazeParamsId, new Vector4(fires, Mathf.Max(.01f, air.HazeHeight) / (2 * halfHeight), seconds * air.HazeSpeed, 0));
            target.SetVector(SharpenId, new Vector4(canvas.Sharpen ? Mathf.Clamp(canvas.SharpenStrength, 0, 3) : 0, 0, 0, 0));
            target.SetVector(TiltId, new Vector4(Mathf.Clamp01(canvas.TiltFocus), Mathf.Clamp01(canvas.TiltBand), canvas.TiltShift && heavy ? Mathf.Clamp(canvas.TiltBlur, 0, 16) : 0,
                game.Stun ? Mathf.Clamp(game.StunBlur, 0, 16) * state.Stun : 0));
            target.SetColor(InkColorId, canvas.InkColor);
            target.SetVector(InkId, new Vector4(Mathf.Clamp01(canvas.InkThreshold), Mathf.Clamp(canvas.InkWidth, .5f, 4), Mathf.Clamp01(canvas.InkStrength), canvas.Ink ? 1 : 0));

            // Облака и лучи — днём и на улице.
            float day = state.Outdoor ? Mathf.Clamp01(state.DayFactor) : 0;
            float angle = air.CloudAngle * Mathf.Deg2Rad;
            Vector2 drift = state.Wind.sqrMagnitude > 1e-6f ? state.Wind.normalized : new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 offset = -drift * seconds * air.CloudSpeed;
            target.SetVector(CloudsId, new Vector4(Mathf.Clamp01(air.CloudCoverage), Mathf.Clamp01(air.CloudDarkness) * day, Mathf.Max(.5f, air.CloudScale), Mathf.Clamp(air.CloudSoftness, .02f, 1)));
            target.SetVector(CloudsMoveId, new Vector4(offset.x, offset.y, air.Clouds && day > .001f ? 1 : 0, 0));
            Vector2 rays = air.RaysFollowSun && state.Sun.sqrMagnitude > 1e-6f
                ? state.Sun.normalized
                : new Vector2(Mathf.Cos(air.RaysAngle * Mathf.Deg2Rad), Mathf.Sin(air.RaysAngle * Mathf.Deg2Rad));
            target.SetColor(RaysColorId, air.RaysColor);
            target.SetVector(RaysId, new Vector4(rays.x, rays.y, Mathf.Max(0, air.RaysIntensity) * day, Mathf.Max(.05f, air.RaysWidth)));
            target.SetVector(RaysMoveId, new Vector4(seconds * air.RaysSpeed, Mathf.Clamp01(air.RaysDensity),
                air.Rays && day > .001f && quality != LocationFrameQualityLevel.Low ? 1 : 0, 0));

            for (int i = 0; i < selective.Length; i++)
            {
                LocationSelectiveRange range = i < tone.Selective.Count ? tone.Selective[i] : null;
                selective[i] = range == null ? new Vector4(0, 1, 0, 0) : new Vector4(range.Hue / 360f, Mathf.Max(0, range.Saturation), Mathf.Clamp(range.Lightness, -1, 1), 0);
            }
            target.SetVectorArray(SelectiveId, selective);
            target.SetVector(SelectiveOnId, new Vector4(tone.SelectiveColor ? 1 : 0, 0, 0, 0));
            target.SetTexture(GradientTexId, tone.GradientMap ? GradientTexture(tone.GradientColors) : Texture2D.whiteTexture);
            target.SetVector(GradientId, new Vector4(Mathf.Clamp01(tone.GradientStrength), tone.GradientMap ? 1 : 0, 0, 0));
            target.SetColor(HatchColorId, canvas.HatchColor);
            target.SetVector(HatchId, new Vector4(Mathf.Clamp01(canvas.HatchThreshold), Mathf.Clamp(canvas.HatchSpacing, 2, 40), canvas.Hatching ? Mathf.Clamp01(canvas.HatchStrength) : 0,
                Mathf.Clamp01(canvas.HatchCross)));
            target.SetVector(FilmId, canvas.Film
                ? new Vector4(Mathf.Clamp01(canvas.FilmDust), Mathf.Clamp01(canvas.FilmScratches), Mathf.Clamp01(canvas.FilmFlicker), seconds)
                : new Vector4(0, 0, 0, seconds));
            target.SetTexture(BorderTexId, canvas.BorderTexture != null ? canvas.BorderTexture : Texture2D.blackTexture);
            target.SetColor(BorderColorId, canvas.BorderColor);
            target.SetVector(BorderId, new Vector4(Mathf.Clamp(canvas.BorderSize, 0, .3f), Mathf.Clamp01(canvas.BorderRoughness), canvas.BorderTexture != null ? 1 : 0, canvas.Border ? 1 : 0));

            target.SetColor(HealthColorId, game.LowHealthColor);
            target.SetVector(HealthId, new Vector4(game.LowHealth ? state.LowHealth * Mathf.Clamp01(game.LowHealthStrength) : 0, Mathf.Max(0, game.LowHealthPulse), seconds, 0));
            target.SetVector(StunId, game.Stun ? new Vector4(Mathf.Clamp01(game.StunDarkness) * state.Stun, Mathf.Clamp01(game.StunDesaturate) * state.Stun, 0, 0) : Vector4.zero);
            Color flash = post.Lens.PulseFlashColor;
            flash.a = state.Flash;
            target.SetColor(FlashId, flash);
            Color fade = game.FadeColor;
            fade.a = state.Fade;
            target.SetColor(FadeId, fade);
            target.SetVector(LetterboxId, new Vector4(Mathf.Clamp(state.Letterbox, 0, .45f), 0, 0, 0));
            target.SetColor(LetterboxColorId, game.LetterboxColor);
        }

        // Градиентная карта — текстура 256×1 (гамма экрана), пересобирается при правке.
        private static Texture2D GradientTexture(Gradient gradient)
        {
            if (gradient == null) return Texture2D.whiteTexture;
            int hash = 17;
            foreach (GradientColorKey key in gradient.colorKeys) hash = hash * 31 ^ key.color.GetHashCode() ^ key.time.GetHashCode() * 7;
            hash = hash * 31 ^ (int)gradient.mode;
            if (gradients.TryGetValue(gradient, out KeyValuePair<int, Texture2D> cached) && cached.Key == hash && cached.Value != null) return cached.Value;
            if (cached.Value != null) Object.DestroyImmediate(cached.Value);
            Texture2D texture = new Texture2D(256, 1, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int x = 0; x < 256; x++) texture.SetPixel(x, 0, gradient.Evaluate(x / 255f));
            texture.Apply();
            gradients[gradient] = new KeyValuePair<int, Texture2D>(hash, texture);
            return texture;
        }

        private static readonly int WorldId = Shader.PropertyToID("_KsWorld");
        private static readonly int HazeId = Shader.PropertyToID("_KsHaze");
        private static readonly int HazeParamsId = Shader.PropertyToID("_KsHazeParams");
        private static readonly int SharpenId = Shader.PropertyToID("_KsSharpen");
        private static readonly int TiltId = Shader.PropertyToID("_KsTilt");
        private static readonly int InkColorId = Shader.PropertyToID("_KsInkColor");
        private static readonly int InkId = Shader.PropertyToID("_KsInk");
        private static readonly int CloudsId = Shader.PropertyToID("_KsClouds");
        private static readonly int CloudsMoveId = Shader.PropertyToID("_KsCloudsMove");
        private static readonly int RaysColorId = Shader.PropertyToID("_KsRaysColor");
        private static readonly int RaysId = Shader.PropertyToID("_KsRays");
        private static readonly int RaysMoveId = Shader.PropertyToID("_KsRaysMove");
        private static readonly int SelectiveId = Shader.PropertyToID("_KsSelective");
        private static readonly int SelectiveOnId = Shader.PropertyToID("_KsSelectiveOn");
        private static readonly int GradientTexId = Shader.PropertyToID("_KsGradientTex");
        private static readonly int GradientId = Shader.PropertyToID("_KsGradient");
        private static readonly int HatchColorId = Shader.PropertyToID("_KsHatchColor");
        private static readonly int HatchId = Shader.PropertyToID("_KsHatch");
        private static readonly int FilmId = Shader.PropertyToID("_KsFilm");
        private static readonly int BorderTexId = Shader.PropertyToID("_KsBorderTex");
        private static readonly int BorderColorId = Shader.PropertyToID("_KsBorderColor");
        private static readonly int BorderId = Shader.PropertyToID("_KsBorder");
        private static readonly int HealthColorId = Shader.PropertyToID("_KsHealthColor");
        private static readonly int HealthId = Shader.PropertyToID("_KsHealth");
        private static readonly int StunId = Shader.PropertyToID("_KsStun");
        private static readonly int FlashId = Shader.PropertyToID("_KsFlash");
        private static readonly int FadeId = Shader.PropertyToID("_KsFade");
        private static readonly int LetterboxId = Shader.PropertyToID("_KsLetterbox");
        private static readonly int LetterboxColorId = Shader.PropertyToID("_KsLetterboxColor");

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
