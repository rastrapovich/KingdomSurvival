using System;
using System.Collections.Generic;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12М: обработка кадра окна «Обработка кадра» сверх цвета дня и ночи:
    // тон и линза (URP Volume), холст (LocationCanvasPass), эффекты рисунков
    // (общий шейдер LocationSpriteAdjust: глобальные значения перед кадром),
    // атмосфера (частицы, .Atmosphere) и сглаживание камеры.
    // Рисунки идут материалами со стилем (ветер, роль) только когда эффект
    // рисунков включён при сборке места: иначе — прежние общие материалы.
    // Смена такого набора (SpriteStructureKey) — пересборка места.
    public sealed partial class LocationWorldRenderer
    {
        private static readonly List<LocationWorldRenderer> live = new List<LocationWorldRenderer>();

        private static readonly int StyleId = Shader.PropertyToID("_KsStyle");
        private static readonly int WindParamsId = Shader.PropertyToID("_KsWindParams");
        private static readonly int WindShareId = Shader.PropertyToID("_KsWindShare");
        private static readonly int OutlineColorId = Shader.PropertyToID("_KsOutlineColor");
        private static readonly int OutlineParamsId = Shader.PropertyToID("_KsOutlineParams");
        private static readonly int DistanceParamsId = Shader.PropertyToID("_KsDistanceParams");
        private static readonly int DistanceHazeId = Shader.PropertyToID("_KsDistanceHaze");
        private static readonly int DissolveParamsId = Shader.PropertyToID("_KsDissolveParams");
        private static readonly int DissolveEdgeId = Shader.PropertyToID("_KsDissolveEdge");

        private Material styleObjects, styleTrees, stylePlants, stylePeople;
        private bool objectStyles, peopleStyles;
        private bool frameShown = true;
        private readonly MaterialPropertyBlock frameBlock = new MaterialPropertyBlock();
        private readonly List<Camera> boundCameras = new List<Camera>();
        private float pulseAt = float.NegativeInfinity, pulseStrength;
        private TextureCurve[] curves;
        private int curvesHash;

        // Набор материалов эффектов рисунков, с которым место собрано.
        public string BuiltSpriteStructure { get; private set; }

        // Обработка кадра сейчас идёт (включена и не скрыта «до» сравнения).
        public bool PostActive => frameShown && Sky.Post != null && Sky.Post.Enabled;

        // Холст для LocationCanvasPass (null — нет).
        public LocationCanvasEffects ActiveCanvas => PostActive ? Sky.Post.Canvas : null;

        // Какие материалы нужны рисункам при этих настройках: другой ключ — пересобрать место.
        public static string SpriteStructureKey(LocationPostEffects post)
        {
            LocationSpriteEffects s = post != null && post.Enabled ? post.Sprites : null;
            if (s == null) return "--";
            bool objects = s.Wind || s.Outline && s.OutlineObjects || s.Distance;
            bool people = s.Outline && s.OutlinePeople || s.Distance || s.Dissolve;
            return (objects ? "O" : "-") + (people ? "P" : "-");
        }

        private void InitializeFrame()
        {
            live.Add(this);
            LocationPostEffects post = Sky.Post?.Repair();
            BuiltSpriteStructure = SpriteStructureKey(post);
            objectStyles = BuiltSpriteStructure[0] == 'O';
            peopleStyles = BuiltSpriteStructure[1] == 'P';
            Shader adjust = Resources.Load<Shader>(AdjustShaderPath);
            if (adjust != null)
            {
                styleObjects = StyleMaterial(adjust, "предмет", new Vector4(0, 0, 0, 0));
                styleTrees = StyleMaterial(adjust, "дерево", new Vector4(1, 0, 0, 0));
                stylePlants = StyleMaterial(adjust, "растительность", new Vector4(2, 0, 0, 0));
                stylePeople = StyleMaterial(adjust, "человек", new Vector4(0, 1, 0, 0));
            }
            adjustGround?.SetVector(StyleId, new Vector4(0, 2, 0, 0));
            LocationCanvasPass.Bind(Camera, this);
        }

        private Material StyleMaterial(Shader shader, string name, Vector4 style)
        {
            Material material = Own(new Material(shader) { name = "Рисунок места · " + name });
            material.SetVector(StyleId, style);
            return material;
        }

        // Ветер по категории ассета: 1 — дерево, 2 — растительность.
        private static float WindKind(LocationResolvedVisual resolved)
        {
            ArtAssetCategory? category = resolved?.Asset?.Category;
            return category == ArtAssetCategory.Trees ? 1 : category == ArtAssetCategory.Vegetation ? 2 : 0;
        }

        // Общий материал со стилем для рисунка без своей поправки; null — прежний общий.
        private Material SharedStyleMaterial(LocationResolvedVisual resolved)
        {
            if (!objectStyles) return null;
            float kind = WindKind(resolved);
            return kind > 1.5f ? stylePlants : kind > .5f ? styleTrees : styleObjects;
        }

        private static Vector4 StyleFor(LocationResolvedVisual resolved, bool groundLit) =>
            groundLit ? new Vector4(0, 2, 0, 0) : new Vector4(WindKind(resolved), 0, 0, 0);

        // Стиль рисунка сейчас: из блока свойств, иначе из материала.
        private static Vector4 StyleOf(SpriteRenderer image, MaterialPropertyBlock block)
        {
            if (block != null && block.HasVector(StyleId)) return block.GetVector(StyleId);
            Material material = image != null ? image.sharedMaterial : null;
            return material != null && material.HasVector(StyleId) ? material.GetVector(StyleId) : Vector4.zero;
        }

        // Фигура человека — материалом со стилем «человек» (обводка, растворение).
        private void ApplyPersonStyle(SpriteRenderer image)
        {
            if (peopleStyles && stylePeople != null && image != null) image.sharedMaterial = stylePeople;
        }

        // Растворение рисунка человека 0..1 (блок свойств).
        private void SetDissolve(SpriteRenderer image, float amount)
        {
            if (image == null || !peopleStyles) return;
            image.GetPropertyBlock(frameBlock);
            Vector4 style = StyleOf(image, frameBlock);
            if (Mathf.Approximately(style.w, amount) && frameBlock.HasVector(StyleId)) return;
            style.y = 1;
            style.w = amount;
            frameBlock.SetVector(StyleId, style);
            image.SetPropertyBlock(frameBlock);
        }

        // Камера предпросмотра (окна баз): холст и сглаживание — как у своей.
        public void BindCamera(Camera camera)
        {
            if (camera == null) return;
            // Камера PreviewRenderUtility — типа Preview: URP не применяет к ней
            // ни обработку кадра, ни свои проходы (холст), кадр был бы «до».
            // Как игровая она рисует то же самое — только сцену предпросмотра.
            camera.cameraType = CameraType.Game;
            if (!boundCameras.Contains(camera)) boundCameras.Add(camera);
            camera.GetUniversalAdditionalCameraData().volumeLayerMask = 1 << PostVolumeLayer;
            LocationCanvasPass.Bind(camera, this);
            ApplyQuality(camera);
        }

        // «До» сравнения: без обработки кадра, эффектов рисунков и атмосферы.
        public void ShowFrameEffects(bool shown)
        {
            if (frameShown == shown) return;
            frameShown = shown;
            ApplyPost();
            UpdateAtmosphereVisibility();
        }

        // Краткий всплеск линзы (удар, страх, нечисть рядом): аберрация и
        // искажение из «Свет и линза» сходят на нет за свою длительность.
        public void Pulse(float strength = 1)
        {
            pulseAt = Seconds;
            pulseStrength = Mathf.Max(0, strength);
        }

        private float PulseNow(LocationLensEffects lens)
        {
            float duration = Mathf.Max(.02f, lens.PulseDuration);
            float t = (Seconds - pulseAt) / duration;
            if (t < 0 || t >= 1) return 0;
            float fall = 1 - t;
            return pulseStrength * fall * fall;
        }

        // Только эта обработка кадра видна камерам (два предпросмотра в
        // редакторе — два объёма на одном слое); другие места вернут свою
        // при следующем кадре.
        public void IsolatePost()
        {
            foreach (LocationWorldRenderer other in live)
                if (other != this && other.postVolume != null) other.postVolume.enabled = false;
            if (postVolume != null) postVolume.enabled = PostActive;
        }

        // Глобальные значения эффектов рисунков — в ноль (после кадра
        // предпросмотра: другие окна с тем же шейдером их не наследуют).
        public static void ClearFrameGlobals()
        {
            Shader.SetGlobalVector(WindParamsId, Vector4.zero);
            Shader.SetGlobalVector(WindShareId, Vector4.zero);
            Shader.SetGlobalVector(OutlineParamsId, Vector4.zero);
            Shader.SetGlobalVector(DistanceHazeId, Vector4.zero);
            Shader.SetGlobalVector(DissolveParamsId, Vector4.zero);
        }

        private void ApplyFrameGlobals(LocationPostEffects post)
        {
            LocationSpriteEffects s = PostActive ? post.Sprites : null;
            if (s == null)
            {
                ClearFrameGlobals();
                return;
            }
            Shader.SetGlobalVector(WindParamsId, s.Wind
                ? new Vector4(Mathf.Max(0, s.WindStrength), Mathf.Max(0, s.WindSpeed), Mathf.Clamp01(s.WindGusts), Mathf.Max(.05f, s.WindHeight))
                : Vector4.zero);
            Shader.SetGlobalVector(WindShareId, new Vector4(Mathf.Max(0, s.WindTrees), Mathf.Max(0, s.WindPlants), Mathf.Clamp(s.WindDirection, -1, 1), Seconds));
            Shader.SetGlobalColor(OutlineColorId, s.OutlineColor);
            Shader.SetGlobalVector(OutlineParamsId, new Vector4(Mathf.Max(.5f, s.OutlineWidth), s.OutlinePeople ? 1 : 0, s.OutlineObjects ? 1 : 0, s.Outline ? 1 : 0));
            Shader.SetGlobalVector(DistanceParamsId, new Vector4(Mathf.Clamp01(s.DistanceStart), Mathf.Clamp01(s.DistanceEnd),
                Mathf.Clamp01(s.DistanceDesaturate), Mathf.Clamp01(s.DistanceHazeStrength)));
            Color haze = s.DistanceHaze;
            haze.a = 1;
            Shader.SetGlobalColor(DistanceHazeId, s.Distance ? haze : Color.clear);
            Shader.SetGlobalVector(DissolveParamsId, new Vector4(Mathf.Clamp(s.DissolveEdgeWidth, .005f, .5f), Mathf.Max(1, s.DissolveScale), s.Dissolve ? 1 : 0, 0));
            Shader.SetGlobalColor(DissolveEdgeId, s.DissolveEdge);
        }

        private void InitializeFrameVolume()
        {
            postProfile.Add<ColorLookup>(true);
            postProfile.Add<SplitToning>(true);
            postProfile.Add<ShadowsMidtonesHighlights>(true);
            postProfile.Add<LiftGammaGain>(true);
            postProfile.Add<ChannelMixer>(true);
            postProfile.Add<ColorCurves>(true);
            postProfile.Add<ChromaticAberration>(true);
            postProfile.Add<LensDistortion>(true);
        }

        // Тон, линза и сглаживание — после цвета, свечения, виньетки и зерна (ApplyPost).
        private void ApplyFrameVolume(LocationPostEffects post)
        {
            LocationToneEffects tone = post.Tone;
            if (postProfile.TryGet(out ColorLookup lookup))
            {
                lookup.active = tone.Lut != null && tone.LutContribution > .001f;
                lookup.texture.Override(tone.Lut);
                lookup.contribution.Override(Mathf.Clamp01(tone.LutContribution));
            }
            if (postProfile.TryGet(out SplitToning split))
            {
                split.active = tone.SplitToning;
                split.shadows.Override(tone.SplitShadows);
                split.highlights.Override(tone.SplitHighlights);
                split.balance.Override(Mathf.Clamp(tone.SplitBalance, -100, 100));
            }
            if (postProfile.TryGet(out ShadowsMidtonesHighlights smh))
            {
                smh.active = tone.ShadowsMidtonesHighlights;
                smh.shadows.Override(tone.Shadows.Value);
                smh.midtones.Override(tone.Midtones.Value);
                smh.highlights.Override(tone.Highlights.Value);
            }
            if (postProfile.TryGet(out LiftGammaGain lgg))
            {
                lgg.active = tone.LiftGammaGain;
                lgg.lift.Override(tone.Lift.Value);
                lgg.gamma.Override(tone.Gamma.Value);
                lgg.gain.Override(tone.Gain.Value);
            }
            if (postProfile.TryGet(out ChannelMixer mixer))
            {
                mixer.active = tone.ChannelMixer;
                mixer.redOutRedIn.Override(tone.MixerRed.x); mixer.redOutGreenIn.Override(tone.MixerRed.y); mixer.redOutBlueIn.Override(tone.MixerRed.z);
                mixer.greenOutRedIn.Override(tone.MixerGreen.x); mixer.greenOutGreenIn.Override(tone.MixerGreen.y); mixer.greenOutBlueIn.Override(tone.MixerGreen.z);
                mixer.blueOutRedIn.Override(tone.MixerBlue.x); mixer.blueOutGreenIn.Override(tone.MixerBlue.y); mixer.blueOutBlueIn.Override(tone.MixerBlue.z);
            }
            if (postProfile.TryGet(out ColorCurves colorCurves))
            {
                colorCurves.active = tone.Curves;
                if (tone.Curves) ApplyCurves(colorCurves, tone);
            }

            LocationLensEffects lens = post.Lens;
            float pulse = PulseNow(lens);
            if (postProfile.TryGet(out ChromaticAberration aberration))
            {
                float value = Mathf.Clamp01(lens.ChromaticAberration + lens.PulseAberration * pulse);
                aberration.active = value > .001f;
                aberration.intensity.Override(value);
            }
            if (postProfile.TryGet(out LensDistortion distortion))
            {
                float value = Mathf.Clamp(lens.Distortion + lens.PulseDistortion * pulse, -1, 1);
                distortion.active = Mathf.Abs(value) > .001f;
                distortion.intensity.Override(value);
                distortion.scale.Override(Mathf.Clamp(lens.DistortionScale, .01f, 5));
            }
            if (postProfile.TryGet(out FilmGrain grain))
            {
                grain.type.Override(post.GrainType == LocationGrainType.Large ? FilmGrainLookup.Large01
                    : post.GrainType == LocationGrainType.Medium ? FilmGrainLookup.Medium1 : FilmGrainLookup.Thin1);
                grain.response.Override(Mathf.Clamp01(post.GrainResponse));
            }
        }

        // Кривые — текстуры URP: новые только когда кривые изменились.
        private void ApplyCurves(ColorCurves target, LocationToneEffects tone)
        {
            int hash = CurveHash(tone.Master) ^ CurveHash(tone.Red) * 3 ^ CurveHash(tone.Green) * 7 ^ CurveHash(tone.Blue) * 11
                       ^ CurveHash(tone.HueVsSaturation) * 13 ^ CurveHash(tone.SaturationVsSaturation) * 17 ^ CurveHash(tone.LuminanceVsSaturation) * 19;
            if (curves != null && hash == curvesHash) return;
            ReleaseCurves();
            curvesHash = hash;
            Vector2 range = new Vector2(0, 1);
            curves = new[]
            {
                new TextureCurve(tone.Master, 0, false, range), new TextureCurve(tone.Red, 0, false, range),
                new TextureCurve(tone.Green, 0, false, range), new TextureCurve(tone.Blue, 0, false, range),
                new TextureCurve(tone.HueVsSaturation, .5f, true, range), new TextureCurve(tone.SaturationVsSaturation, .5f, false, range),
                new TextureCurve(tone.LuminanceVsSaturation, .5f, false, range)
            };
            target.master.Override(curves[0]);
            target.red.Override(curves[1]);
            target.green.Override(curves[2]);
            target.blue.Override(curves[3]);
            target.hueVsSat.Override(curves[4]);
            target.satVsSat.Override(curves[5]);
            target.lumVsSat.Override(curves[6]);
        }

        private static int CurveHash(AnimationCurve curve)
        {
            if (curve == null) return 0;
            int hash = curve.length;
            foreach (Keyframe key in curve.keys)
                hash = hash * 31 ^ key.time.GetHashCode() ^ key.value.GetHashCode() * 5 ^ key.inTangent.GetHashCode() * 7 ^ key.outTangent.GetHashCode() * 13;
            return hash;
        }

        private void ReleaseCurves()
        {
            if (curves == null) return;
            foreach (TextureCurve curve in curves) curve?.Release();
            curves = null;
        }

        private void ApplyQuality(Camera camera)
        {
            if (camera == null) return;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            LocationAntialiasing mode = PostActive ? Sky.Post.Quality.Antialiasing : LocationAntialiasing.None;
            data.antialiasing = mode == LocationAntialiasing.None ? AntialiasingMode.None
                : mode == LocationAntialiasing.Fxaa ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = mode == LocationAntialiasing.SmaaHigh ? AntialiasingQuality.High
                : mode == LocationAntialiasing.SmaaMedium ? AntialiasingQuality.Medium : AntialiasingQuality.Low;
        }

        private void ApplyQualityAll()
        {
            ApplyQuality(Camera);
            for (int i = boundCameras.Count - 1; i >= 0; i--)
            {
                if (boundCameras[i] == null) { boundCameras.RemoveAt(i); continue; }
                ApplyQuality(boundCameras[i]);
            }
        }

        private void DisposeFrame()
        {
            live.Remove(this);
            LocationCanvasPass.Unbind(this);
            ReleaseCurves();
            boundCameras.Clear();
            DisposeAtmosphere();
        }
    }
}
