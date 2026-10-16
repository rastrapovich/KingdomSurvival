using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12М: окно «Обработка кадра» — всё, что меняет вид готового кадра
    // места, сверх цвета дня и ночи, свечения, виньетки, зерна и тональной
    // кривой (LocationPostEffects). Разделы:
    //   Tone — LUT, разделение тонов, тени/полутона/света, Lift/Gamma/Gain,
    //          смешивание каналов, кривые (URP Volume);
    //   Lens — хроматическая аберрация и искажение линзы, постоянные и
    //          кратким всплеском (удар, страх);
    //   Canvas — свои полноэкранные эффекты: живописность (Кувахара), туман
    //          по высоте экрана, сведение к палитре, бумага (LocationCanvasPass);
    //   Sprites — общий шейдер рисунков: ветер, обводка, обесцвечивание
    //          дальнего плана, растворение при гибели (LocationSpriteAdjust);
    //   Atmosphere — частицы: пыль, мошкара, дождь, листья, искры у огня,
    //          пар изо рта ночью;
    //   Quality — сглаживание.
    // Только картинка: проходимость, бой и состояние мира не меняются.

    [Serializable]
    public sealed class LocationToneEffects
    {
        // LUT — полоска 1024×32 (32 клетки 32×32), без sRGB. Готовится и
        // извлекается из покрашенного снимка в окне «Обработка кадра».
        public Texture2D Lut;
        public float LutContribution = 1;

        public bool SplitToning;
        public Color SplitShadows = new Color(.42f, .5f, .62f);
        public Color SplitHighlights = new Color(.68f, .6f, .46f);
        public float SplitBalance;

        public bool ShadowsMidtonesHighlights;
        public LocationTrackball Shadows = new LocationTrackball();
        public LocationTrackball Midtones = new LocationTrackball();
        public LocationTrackball Highlights = new LocationTrackball();

        public bool LiftGammaGain;
        public LocationTrackball Lift = new LocationTrackball();
        public LocationTrackball Gamma = new LocationTrackball();
        public LocationTrackball Gain = new LocationTrackball();

        public bool ChannelMixer;
        // Выход канала из входов, проценты (−200..200), как в URP.
        public Vector3 MixerRed = new Vector3(100, 0, 0);
        public Vector3 MixerGreen = new Vector3(0, 100, 0);
        public Vector3 MixerBlue = new Vector3(0, 0, 100);

        public bool Curves;
        // Общая и по каналам: вход 0..1 → выход 0..1.
        public AnimationCurve Master = Identity();
        public AnimationCurve Red = Identity();
        public AnimationCurve Green = Identity();
        public AnimationCurve Blue = Identity();
        // Насыщенность по тону / насыщенности / яркости: 0.5 — без изменений.
        public AnimationCurve HueVsSaturation = Flat();
        public AnimationCurve SaturationVsSaturation = Flat();
        public AnimationCurve LuminanceVsSaturation = Flat();

        public static AnimationCurve Identity() => new AnimationCurve(new Keyframe(0, 0, 1, 1), new Keyframe(1, 1, 1, 1));
        public static AnimationCurve Flat() => new AnimationCurve(new Keyframe(0, .5f), new Keyframe(1, .5f));

        public void Repair()
        {
            Shadows ??= new LocationTrackball();
            Midtones ??= new LocationTrackball();
            Highlights ??= new LocationTrackball();
            Lift ??= new LocationTrackball();
            Gamma ??= new LocationTrackball();
            Gain ??= new LocationTrackball();
            if (Master == null || Master.length == 0) Master = Identity();
            if (Red == null || Red.length == 0) Red = Identity();
            if (Green == null || Green.length == 0) Green = Identity();
            if (Blue == null || Blue.length == 0) Blue = Identity();
            if (HueVsSaturation == null || HueVsSaturation.length == 0) HueVsSaturation = Flat();
            if (SaturationVsSaturation == null || SaturationVsSaturation.length == 0) SaturationVsSaturation = Flat();
            if (LuminanceVsSaturation == null || LuminanceVsSaturation.length == 0) LuminanceVsSaturation = Flat();
        }
    }

    // «Шар» цвета URP: оттенок (белый — без сдвига) и сдвиг яркости −1..1.
    [Serializable]
    public sealed class LocationTrackball
    {
        public Color Color = Color.white;
        public float Offset;

        public Vector4 Value => new Vector4(Color.r, Color.g, Color.b, Offset);
    }

    [Serializable]
    public sealed class LocationLensEffects
    {
        public float ChromaticAberration;
        public float Distortion;
        public float DistortionScale = 1;
        // Краткий всплеск (LocationWorldRenderer.Pulse): сколько добавить на
        // пике и за сколько секунд сойти на нет.
        public float PulseAberration = .6f;
        public float PulseDistortion = -.25f;
        public float PulseDuration = .45f;
    }

    public enum LocationFogDirection { FromTop, FromBottom }

    [Serializable]
    public sealed class LocationCanvasEffects
    {
        // Живописность: фильтр Кувахары — мазки вместо пикселей.
        public bool Painterly;
        public int PainterlyRadius = 3;
        public float PainterlyStrength = 1;

        // Туман по высоте экрана: от края (сверху — даль, снизу — низовой).
        public bool Fog;
        public LocationFogDirection FogDirection = LocationFogDirection.FromTop;
        public Color FogColor = new Color(.62f, .66f, .66f, 1);
        public float FogDensity = .35f;
        // Доли высоты экрана от края тумана: где он начинается и где полный.
        public float FogStart = .45f;
        public float FogEnd = 1;
        // Клочья: сила и скорость дрейфа шума.
        public float FogNoise = .35f;
        public float FogNoiseScale = 3;
        public float FogSpeed = .03f;

        // Сведение к палитре: уровни на канал или ближайший цвет из рисунка-палитры.
        public bool Palette;
        public int PaletteLevels = 8;
        // Палитра — любой рисунок до 64×64: каждый пиксель — цвет (берётся до 64 разных).
        public Texture2D PaletteTexture;
        public float PaletteStrength = 1;
        public float PaletteDither = .3f;

        // Бумага или холст поверх кадра (умножение с подъёмом света).
        public bool Paper;
        // Нет текстуры — своя, процедурная (волокна и зерно).
        public Texture2D PaperTexture;
        public float PaperStrength = .35f;
        public float PaperScale = 1;
        public float PaperFibers = .5f;
    }

    [Serializable]
    public sealed class LocationSpriteEffects
    {
        // Ветер: деревья и растительность (категории Базы ассетов) и ковёр раскидки.
        public bool Wind;
        // Размах верха, единицы мира; высота полного размаха — тоже мира.
        public float WindStrength = .08f;
        public float WindSpeed = 1;
        public float WindGusts = .5f;
        public float WindHeight = 2.5f;
        // Направление: −1 — на запад (влево), 1 — на восток (вправо), 0 — качание.
        public float WindDirection = .3f;
        public float WindTrees = .6f;
        public float WindPlants = 1;

        // Обводка по краю рисунка, изнутри (рисунок не растёт).
        public bool Outline;
        public Color OutlineColor = new Color(.08f, .06f, .05f, .85f);
        public float OutlineWidth = 1.5f;
        public bool OutlinePeople = true;
        public bool OutlineObjects;

        // Дальний план (выше на экране по опоре рисунка): обесцвечивание и дымка.
        public bool Distance;
        public float DistanceStart = .55f;
        public float DistanceEnd = 1;
        public float DistanceDesaturate = .45f;
        public Color DistanceHaze = new Color(.6f, .64f, .66f, 1);
        public float DistanceHazeStrength = .25f;

        // Растворение павших в бою на месте.
        public bool Dissolve;
        public float DissolveDuration = 1.2f;
        public Color DissolveEdge = new Color(1, .45f, .15f, 1);
        public float DissolveEdgeWidth = .08f;
        public float DissolveScale = 40;
        // До какой доли растворяется тело: 1 — исчезает, меньше — остаётся следом.
        public float DissolveAmount = 1;
    }

    [Serializable]
    public sealed class LocationAtmosphereEffects
    {
        public LocationParticleLayer Dust = LocationParticleLayer.DefaultDust();
        public LocationParticleLayer Midges = LocationParticleLayer.DefaultMidges();
        public LocationParticleLayer Rain = LocationParticleLayer.DefaultRain();
        public LocationParticleLayer Leaves = LocationParticleLayer.DefaultLeaves();
        public LocationParticleLayer Sparks = LocationParticleLayer.DefaultSparks();
        public LocationParticleLayer Breath = LocationParticleLayer.DefaultBreath();

        public IEnumerable<LocationParticleLayer> All
        {
            get { yield return Dust; yield return Midges; yield return Rain; yield return Leaves; yield return Sparks; yield return Breath; }
        }

        public void Repair()
        {
            Dust ??= LocationParticleLayer.DefaultDust();
            Midges ??= LocationParticleLayer.DefaultMidges();
            Rain ??= LocationParticleLayer.DefaultRain();
            Leaves ??= LocationParticleLayer.DefaultLeaves();
            Sparks ??= LocationParticleLayer.DefaultSparks();
            Breath ??= LocationParticleLayer.DefaultBreath();
        }
    }

    // Когда слой частиц виден: всегда, днём или ночью (по яркости суток), в часы.
    public enum LocationParticleTime { Always, Day, Night, Hours }

    // Один слой частиц. Плотность — частиц в секунду на 100 квадратных
    // единиц мира (у искр — на источник огня, у пара — на человека).
    [Serializable]
    public sealed class LocationParticleLayer
    {
        public bool Enabled;
        public float Density = 1;
        public float Size = .05f;
        public float SizeJitter = .5f;
        public float Lifetime = 4;
        public float Speed = .2f;
        // Направление движения, градусы (0 — вправо, 90 — вверх, 270 — вниз).
        public float Angle = 90;
        public float Spread = 180;
        // Тяга вниз (дождь, листья) или вверх (искры, пар) — отрицательная.
        public float Gravity;
        // Блуждание (шум): сила и частота.
        public float Wander = .1f;
        public float WanderFrequency = .5f;
        public Color Color = Color.white;
        public Color ColorB = Color.white;
        public float Opacity = .6f;
        // Сколько от ветра рисунков берёт слой (0 — не берёт).
        public float WindShare = 1;
        // Своя картинка частицы; нет — своя форма слоя.
        public Texture2D Texture;
        public LocationParticleTime Time = LocationParticleTime.Always;
        public float FromHour = 18;
        public float ToHour = 6;
        // Для дождя: длина штриха к ширине.
        public float Stretch = 1;
        // У мошкары: сколько роёв.
        public int Groups = 3;

        public static LocationParticleLayer DefaultDust() => new LocationParticleLayer
        {
            Density = 4, Size = .03f, Lifetime = 6, Speed = .05f, Angle = 90, Spread = 360, Wander = .12f, WanderFrequency = .3f,
            Color = new Color(1, .93f, .78f), ColorB = new Color(1, .85f, .6f), Opacity = .45f, WindShare = .3f, Time = LocationParticleTime.Day
        };

        public static LocationParticleLayer DefaultMidges() => new LocationParticleLayer
        {
            Density = 25, Size = .022f, SizeJitter = .3f, Lifetime = 3, Speed = .05f, Spread = 360, Wander = .9f, WanderFrequency = 3,
            Color = new Color(.1f, .09f, .07f), ColorB = new Color(.2f, .18f, .12f), Opacity = .8f, WindShare = .1f,
            Time = LocationParticleTime.Hours, FromHour = 17, ToHour = 22, Groups = 3
        };

        public static LocationParticleLayer DefaultRain() => new LocationParticleLayer
        {
            Density = 60, Size = .025f, SizeJitter = .3f, Lifetime = 1.2f, Speed = 9, Angle = 260, Spread = 4, Wander = 0,
            Color = new Color(.75f, .8f, .88f), ColorB = new Color(.65f, .7f, .8f), Opacity = .35f, WindShare = 1, Stretch = 14
        };

        public static LocationParticleLayer DefaultLeaves() => new LocationParticleLayer
        {
            Density = .6f, Size = .09f, SizeJitter = .4f, Lifetime = 9, Speed = .35f, Angle = 270, Spread = 40, Gravity = .03f,
            Wander = .5f, WanderFrequency = .4f, Color = new Color(.62f, .45f, .16f), ColorB = new Color(.45f, .3f, .12f),
            Opacity = .9f, WindShare = 1
        };

        public static LocationParticleLayer DefaultSparks() => new LocationParticleLayer
        {
            Density = 6, Size = .03f, SizeJitter = .5f, Lifetime = 1.6f, Speed = .9f, Angle = 90, Spread = 35, Gravity = -.05f,
            Wander = .5f, WanderFrequency = 1.5f, Color = new Color(1, .7f, .25f), ColorB = new Color(1, .35f, .08f),
            Opacity = 1, WindShare = .4f
        };

        public static LocationParticleLayer DefaultBreath() => new LocationParticleLayer
        {
            Density = .5f, Size = .16f, SizeJitter = .3f, Lifetime = 1.6f, Speed = .12f, Angle = 80, Spread = 25, Gravity = -.02f,
            Wander = .1f, WanderFrequency = .6f, Color = new Color(.9f, .92f, .95f), ColorB = new Color(.85f, .88f, .92f),
            Opacity = .22f, WindShare = .5f, Time = LocationParticleTime.Night
        };

        // Доля видимости в этот час (0..1) по яркости суток dayFactor.
        public float Visibility(float hour, float dayFactor)
        {
            if (!Enabled) return 0;
            switch (Time)
            {
                case LocationParticleTime.Day: return Mathf.Clamp01((dayFactor - .25f) / .5f);
                case LocationParticleTime.Night: return Mathf.Clamp01((.6f - dayFactor) / .4f);
                case LocationParticleTime.Hours:
                    float from = Mathf.Repeat(FromHour, 24), to = Mathf.Repeat(ToHour, 24), h = Mathf.Repeat(hour, 24);
                    bool inside = from <= to ? h >= from && h <= to : h >= from || h <= to;
                    if (!inside) return 0;
                    // Мягкие полчаса на входе и выходе.
                    float sinceStart = Mathf.Repeat(h - from, 24), untilEnd = Mathf.Repeat(to - h, 24);
                    return Mathf.Clamp01(Mathf.Min(sinceStart, untilEnd) / .5f);
                default: return 1;
            }
        }
    }

    public enum LocationAntialiasing { None, Fxaa, SmaaLow, SmaaMedium, SmaaHigh }

    [Serializable]
    public sealed class LocationFrameQuality
    {
        public LocationAntialiasing Antialiasing = LocationAntialiasing.None;
    }

    // Разделы стиля кадра: что применяет сохранённый стиль и что сбрасывается.
    [Flags]
    public enum LocationFrameSection
    {
        None = 0,
        Color = 1,
        LightLens = 2,
        Tone = 4,
        Canvas = 8,
        Sprites = 16,
        Atmosphere = 32,
        Quality = 64,
        All = Color | LightLens | Tone | Canvas | Sprites | Atmosphere | Quality
    }

    // Сохранённый стиль кадра («Запомнить настройки»): имя и полный снимок.
    [Serializable]
    public sealed class LocationFrameStyle
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Стиль";
        public string Note = "";
        public LocationPostEffects Post = new LocationPostEffects();
    }

    public static class LocationFrameSections
    {
        public static readonly LocationFrameSection[] Order =
        {
            LocationFrameSection.Color, LocationFrameSection.LightLens, LocationFrameSection.Tone, LocationFrameSection.Canvas,
            LocationFrameSection.Sprites, LocationFrameSection.Atmosphere, LocationFrameSection.Quality
        };

        public static string Title(LocationFrameSection section)
        {
            switch (section)
            {
                case LocationFrameSection.Color: return "Цвет";
                case LocationFrameSection.LightLens: return "Свет и линза";
                case LocationFrameSection.Tone: return "Тон";
                case LocationFrameSection.Canvas: return "Холст";
                case LocationFrameSection.Sprites: return "Рисунки";
                case LocationFrameSection.Atmosphere: return "Атмосфера";
                case LocationFrameSection.Quality: return "Качество";
                default: return section.ToString();
            }
        }

        // Скопировать разделы из source в target (копией: стиль и место дальше живут отдельно).
        public static void Copy(LocationPostEffects source, LocationPostEffects target, LocationFrameSection sections)
        {
            if (source == null || target == null) return;
            LocationPostEffects from = source.Clone();
            target.Repair();
            if ((sections & LocationFrameSection.Color) != 0)
            {
                target.Day = from.Day;
                target.Night = from.Night;
            }
            if ((sections & LocationFrameSection.LightLens) != 0)
            {
                target.Bloom = from.Bloom;
                target.BloomIntensity = from.BloomIntensity;
                target.BloomThreshold = from.BloomThreshold;
                target.BloomScatter = from.BloomScatter;
                target.BloomTint = from.BloomTint;
                target.Vignette = from.Vignette;
                target.VignetteIntensity = from.VignetteIntensity;
                target.VignetteSmoothness = from.VignetteSmoothness;
                target.VignetteColor = from.VignetteColor;
                target.Grain = from.Grain;
                target.GrainType = from.GrainType;
                target.GrainResponse = from.GrainResponse;
                target.Tonemapping = from.Tonemapping;
                target.Lens = from.Lens;
            }
            if ((sections & LocationFrameSection.Tone) != 0) target.Tone = from.Tone;
            if ((sections & LocationFrameSection.Canvas) != 0) target.Canvas = from.Canvas;
            if ((sections & LocationFrameSection.Sprites) != 0) target.Sprites = from.Sprites;
            if ((sections & LocationFrameSection.Atmosphere) != 0) target.Atmosphere = from.Atmosphere;
            if ((sections & LocationFrameSection.Quality) != 0) target.Quality = from.Quality;
            // Применённый стиль включает обработку кадра: иначе его не видно.
            if (sections != LocationFrameSection.None) target.Enabled = true;
        }

        // Сброс разделов к нейтральному виду (ничего не меняют).
        public static void Reset(LocationPostEffects target, LocationFrameSection sections)
        {
            if (target == null) return;
            LocationPostEffects neutral = new LocationPostEffects { Bloom = false, Night = new LocationColorGrade() };
            bool enabled = target.Enabled;
            Copy(neutral, target, sections);
            target.Enabled = enabled;
        }
    }
}
