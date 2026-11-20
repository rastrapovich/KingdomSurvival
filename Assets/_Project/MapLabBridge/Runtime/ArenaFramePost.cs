using System;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace KingdomSurvival.MapLabBridge
{
    // Обработка кадра арены (окно «Генератор арен» → «Обработка кадра»): те
    // же настройки LocationPostEffects, что у мест, на камере арены MapLab.
    // Свой объём URP на слое арены, включённый только на время кадра этой
    // арены: другие камеры и арены его не видят. Действуют разделы,
    // выполняемые объёмом URP: «Цвет» (день), «Свет и линза» (свечение,
    // виньетка, зерно, тональная кривая, линза), «Тон» (LUT, разделение
    // тонов, тени/полутона/света, Lift/Gamma/Gain, каналы, кривые) и
    // сглаживание. Холст места, эффекты рисунков, атмосфера, персонажи,
    // выборочная коррекция и градиентная карта (проходы LocationCanvasPass
    // мира места) на арене не действуют.
    // Числа повторяют LocationWorldRenderer.ApplyPost / ApplyFrameVolume —
    // правка одного требует правки другого.
    public sealed class ArenaFramePost : IDisposable
    {
        private readonly GameObject host;
        private readonly Volume volume;
        private readonly VolumeProfile profile;
        private TextureCurve[] curves;
        private int curvesHash;

        public ArenaFramePost(int layer)
        {
            host = new GameObject("Обработка кадра арены") { hideFlags = HideFlags.HideAndDontSave, layer = layer };
            volume = host.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            volume.enabled = false;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            profile.Add<ColorAdjustments>(true);
            profile.Add<WhiteBalance>(true);
            profile.Add<Bloom>(true);
            profile.Add<Vignette>(true);
            profile.Add<FilmGrain>(true);
            profile.Add<Tonemapping>(true);
            profile.Add<ColorLookup>(true);
            profile.Add<SplitToning>(true);
            profile.Add<ShadowsMidtonesHighlights>(true);
            profile.Add<LiftGammaGain>(true);
            profile.Add<ChannelMixer>(true);
            profile.Add<ColorCurves>(true);
            profile.Add<ChromaticAberration>(true);
            profile.Add<LensDistortion>(true);
            volume.sharedProfile = profile;
            Layer = layer;
        }

        public int Layer { get; }

        // Перед кадром камеры: true — обработка идёт (объём включён).
        public bool Begin(Camera camera, LocationPostEffects post)
        {
            UniversalAdditionalCameraData data = camera != null ? camera.GetUniversalAdditionalCameraData() : null;
            if (data == null)
                return false;
            bool active = post != null && post.Enabled;
            data.renderPostProcessing = active;
            data.volumeLayerMask = 1 << Layer;
            data.antialiasing = AntialiasingMode.None;
            if (!active)
            {
                volume.enabled = false;
                return false;
            }
            Apply(post);
            LocationAntialiasing mode = post.Quality != null ? post.Quality.Antialiasing : LocationAntialiasing.None;
            data.antialiasing = mode == LocationAntialiasing.None ? AntialiasingMode.None
                : mode == LocationAntialiasing.Fxaa ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = mode == LocationAntialiasing.SmaaHigh ? AntialiasingQuality.High
                : mode == LocationAntialiasing.SmaaMedium ? AntialiasingQuality.Medium : AntialiasingQuality.Low;
            volume.enabled = true;
            return true;
        }

        // Без обработки (слой объектов с прозрачным фоном — для альфы).
        public void Suspend(Camera camera)
        {
            UniversalAdditionalCameraData data = camera != null ? camera.GetUniversalAdditionalCameraData() : null;
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
            }
            volume.enabled = false;
        }

        public void End(Camera camera)
        {
            volume.enabled = false;
            UniversalAdditionalCameraData data = camera != null ? camera.GetUniversalAdditionalCameraData() : null;
            if (data != null)
                data.renderPostProcessing = false;
        }

        private void Apply(LocationPostEffects post)
        {
            LocationColorGrade grade = post.Day ?? new LocationColorGrade();
            if (profile.TryGet(out ColorAdjustments color))
            {
                color.postExposure.Override(grade.Exposure);
                color.contrast.Override(Mathf.Clamp(grade.Contrast, -100, 100));
                color.saturation.Override(Mathf.Clamp(grade.Saturation, -100, 100));
                color.colorFilter.Override(grade.Filter);
            }
            if (profile.TryGet(out WhiteBalance balance))
            {
                balance.temperature.Override(Mathf.Clamp(grade.Temperature, -100, 100));
                balance.tint.Override(Mathf.Clamp(grade.Tint, -100, 100));
            }
            if (profile.TryGet(out Bloom bloom))
            {
                bloom.active = post.Bloom;
                bloom.intensity.Override(Mathf.Max(0, post.BloomIntensity));
                bloom.threshold.Override(Mathf.Max(0, post.BloomThreshold));
                bloom.scatter.Override(Mathf.Clamp01(post.BloomScatter));
                bloom.tint.Override(post.BloomTint);
            }
            if (profile.TryGet(out Vignette vignette))
            {
                vignette.active = post.Vignette;
                vignette.intensity.Override(Mathf.Clamp01(post.VignetteIntensity));
                vignette.smoothness.Override(Mathf.Clamp(post.VignetteSmoothness, .01f, 1));
                vignette.color.Override(post.VignetteColor);
            }
            if (profile.TryGet(out FilmGrain grain))
            {
                grain.active = post.Grain > .001f;
                grain.intensity.Override(Mathf.Clamp01(post.Grain));
                grain.type.Override(post.GrainType == LocationGrainType.Large ? FilmGrainLookup.Large01
                    : post.GrainType == LocationGrainType.Medium ? FilmGrainLookup.Medium1 : FilmGrainLookup.Thin1);
                grain.response.Override(Mathf.Clamp01(post.GrainResponse));
            }
            if (profile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping.active = post.Tonemapping != LocationTonemapping.None;
                tonemapping.mode.Override(post.Tonemapping == LocationTonemapping.ACES ? TonemappingMode.ACES : TonemappingMode.Neutral);
            }

            LocationToneEffects tone = post.Tone ?? new LocationToneEffects();
            if (profile.TryGet(out ColorLookup lookup))
            {
                lookup.active = tone.Lut != null && tone.LutContribution > .001f;
                lookup.texture.Override(tone.Lut);
                lookup.contribution.Override(Mathf.Clamp01(tone.LutContribution));
            }
            if (profile.TryGet(out SplitToning split))
            {
                split.active = tone.SplitToning;
                split.shadows.Override(tone.SplitShadows);
                split.highlights.Override(tone.SplitHighlights);
                split.balance.Override(Mathf.Clamp(tone.SplitBalance, -100, 100));
            }
            if (profile.TryGet(out ShadowsMidtonesHighlights smh))
            {
                smh.active = tone.ShadowsMidtonesHighlights;
                smh.shadows.Override(tone.Shadows.Value);
                smh.midtones.Override(tone.Midtones.Value);
                smh.highlights.Override(tone.Highlights.Value);
                smh.shadowsStart.Override(Mathf.Max(0, tone.ShadowsStart));
                smh.shadowsEnd.Override(Mathf.Max(tone.ShadowsStart + .01f, tone.ShadowsEnd));
                smh.highlightsStart.Override(Mathf.Max(0, tone.HighlightsStart));
                smh.highlightsEnd.Override(Mathf.Max(tone.HighlightsStart + .01f, tone.HighlightsEnd));
            }
            if (profile.TryGet(out LiftGammaGain lgg))
            {
                lgg.active = tone.LiftGammaGain;
                lgg.lift.Override(tone.Lift.Value);
                lgg.gamma.Override(tone.Gamma.Value);
                lgg.gain.Override(tone.Gain.Value);
            }
            if (profile.TryGet(out ChannelMixer mixer))
            {
                mixer.active = tone.ChannelMixer;
                mixer.redOutRedIn.Override(tone.MixerRed.x); mixer.redOutGreenIn.Override(tone.MixerRed.y); mixer.redOutBlueIn.Override(tone.MixerRed.z);
                mixer.greenOutRedIn.Override(tone.MixerGreen.x); mixer.greenOutGreenIn.Override(tone.MixerGreen.y); mixer.greenOutBlueIn.Override(tone.MixerGreen.z);
                mixer.blueOutRedIn.Override(tone.MixerBlue.x); mixer.blueOutGreenIn.Override(tone.MixerBlue.y); mixer.blueOutBlueIn.Override(tone.MixerBlue.z);
            }
            if (profile.TryGet(out ColorCurves colorCurves))
            {
                colorCurves.active = tone.Curves;
                if (tone.Curves) ApplyCurves(colorCurves, tone);
            }

            LocationLensEffects lens = post.Lens ?? new LocationLensEffects();
            if (profile.TryGet(out ChromaticAberration aberration))
            {
                float value = Mathf.Clamp01(lens.ChromaticAberration);
                aberration.active = value > .001f;
                aberration.intensity.Override(value);
            }
            if (profile.TryGet(out LensDistortion distortion))
            {
                float value = Mathf.Clamp(lens.Distortion, -1, 1);
                distortion.active = Mathf.Abs(value) > .001f;
                distortion.intensity.Override(value);
                distortion.scale.Override(Mathf.Clamp(lens.DistortionScale, .01f, 5));
            }
        }

        private void ApplyCurves(ColorCurves target, LocationToneEffects tone)
        {
            int hash = CurveHash(tone.Master) ^ CurveHash(tone.Red) * 3 ^ CurveHash(tone.Green) * 7 ^ CurveHash(tone.Blue) * 11
                       ^ CurveHash(tone.HueVsSaturation) * 13 ^ CurveHash(tone.SaturationVsSaturation) * 17 ^ CurveHash(tone.LuminanceVsSaturation) * 19 ^ CurveHash(tone.HueVsHue) * 23;
            if (curves != null && hash == curvesHash) return;
            ReleaseCurves();
            curvesHash = hash;
            Vector2 range = new Vector2(0, 1);
            curves = new[]
            {
                new TextureCurve(tone.Master, 0, false, range), new TextureCurve(tone.Red, 0, false, range),
                new TextureCurve(tone.Green, 0, false, range), new TextureCurve(tone.Blue, 0, false, range),
                new TextureCurve(tone.HueVsSaturation, .5f, true, range), new TextureCurve(tone.SaturationVsSaturation, .5f, false, range),
                new TextureCurve(tone.LuminanceVsSaturation, .5f, false, range), new TextureCurve(tone.HueVsHue, .5f, true, range)
            };
            target.master.Override(curves[0]);
            target.red.Override(curves[1]);
            target.green.Override(curves[2]);
            target.blue.Override(curves[3]);
            target.hueVsSat.Override(curves[4]);
            target.satVsSat.Override(curves[5]);
            target.lumVsSat.Override(curves[6]);
            target.hueVsHue.Override(curves[7]);
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

        public void Dispose()
        {
            ReleaseCurves();
            if (host != null) Object.DestroyImmediate(host);
            if (profile != null) Object.DestroyImmediate(profile);
        }
    }
}
