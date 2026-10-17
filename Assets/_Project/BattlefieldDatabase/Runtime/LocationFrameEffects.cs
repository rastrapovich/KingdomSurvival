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
        // Тон по тону: 0.5 — без сдвига.
        public AnimationCurve HueVsHue = Flat();
        // Пределы теней и светов для «тени / полутона / света» (доли яркости).
        public float ShadowsStart = 0;
        public float ShadowsEnd = .3f;
        public float HighlightsStart = .55f;
        public float HighlightsEnd = 1;

        // Выборочная коррекция: 6 цветов (красный, жёлтый, зелёный, голубой, синий, пурпурный).
        public bool SelectiveColor;
        public List<LocationSelectiveRange> Selective = new List<LocationSelectiveRange>
        {
            new LocationSelectiveRange(), new LocationSelectiveRange(), new LocationSelectiveRange(),
            new LocationSelectiveRange(), new LocationSelectiveRange(), new LocationSelectiveRange()
        };

        // Градиентная карта: яркость кадра → свой ряд цветов (2–8 точек).
        public bool GradientMap;
        public Gradient GradientColors = DefaultGradient();
        public float GradientStrength = .5f;

        public static Gradient DefaultGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(.08f, .07f, .12f), 0), new GradientColorKey(new Color(.45f, .38f, .3f), .5f), new GradientColorKey(new Color(.98f, .9f, .74f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return gradient;
        }

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
            if (HueVsHue == null || HueVsHue.length == 0) HueVsHue = Flat();
            Selective ??= new List<LocationSelectiveRange>();
            while (Selective.Count < 6) Selective.Add(new LocationSelectiveRange());
            GradientColors ??= DefaultGradient();
        }
    }

    // Один цвет выборочной коррекции: сдвиг тона (°), насыщенность (множитель), светлота (−1..1).
    [Serializable]
    public sealed class LocationSelectiveRange
    {
        public float Hue;
        public float Saturation = 1;
        public float Lightness;

        public bool IsIdentity => Mathf.Abs(Hue) < .01f && Mathf.Abs(Saturation - 1) < .001f && Mathf.Abs(Lightness) < .001f;
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
        // Всплеск при ударе в бою на месте: включён ли, по каким ударам и
        // насколько сильный (доля пика выше; при гибели цели — своя доля).
        public bool PulseOnHit;
        public LocationPulseTargets PulseOnHitTargets = LocationPulseTargets.Any;
        public float PulseHitStrength = .6f;
        public float PulseKillStrength = 1;
        // Тряска камеры (доля высоты кадра на пике) и вспышка экрана при ударе — той же силой.
        public bool PulseShake = true;
        public float PulseShakeAmount = .012f;
        public bool PulseFlash;
        public Color PulseFlashColor = new Color(1, .92f, .85f, 1);
        public float PulseFlashStrength = .25f;

        // Сила всплеска для удара (0 — без всплеска).
        public float HitPulse(bool targetOurs, bool defeated)
        {
            if (!PulseOnHit) return 0;
            if (PulseOnHitTargets == LocationPulseTargets.Ours && !targetOurs) return 0;
            if (PulseOnHitTargets == LocationPulseTargets.Enemies && targetOurs) return 0;
            return Mathf.Max(0, defeated ? PulseKillStrength : PulseHitStrength);
        }
    }

    public enum LocationFogDirection { FromTop, FromBottom }

    // По каким ударам всплеск: любым, по нашим бойцам, по врагам.
    public enum LocationPulseTargets { Any, Ours, Enemies }

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

        // Резкость: возвращает чёткость уменьшенным рисункам.
        public bool Sharpen;
        public float SharpenStrength = .6f;

        // Тилт-шифт: размытие верха и низа кадра (диорама). Фокус и полоса — доли высоты экрана.
        public bool TiltShift;
        public float TiltFocus = .5f;
        public float TiltBand = .35f;
        public float TiltBlur = 5;

        // Чернильный контур по краям форм всего кадра.
        public bool Ink;
        public Color InkColor = new Color(.1f, .07f, .05f, 1);
        public float InkThreshold = .35f;
        public float InkWidth = 1;
        public float InkStrength = .8f;

        // Штриховка в тенях (гравюра): ниже порога — штрих, ниже второго — перекрёстный.
        public bool Hatching;
        public Color HatchColor = new Color(.12f, .09f, .07f, 1);
        public float HatchThreshold = .35f;
        public float HatchCross = .18f;
        public float HatchSpacing = 6;
        public float HatchStrength = .6f;

        // Рамка кадра: своя текстура (с прозрачностью) или неровный край.
        public bool Border;
        public Texture2D BorderTexture;
        public Color BorderColor = new Color(.08f, .06f, .05f, 1);
        public float BorderSize = .04f;
        public float BorderRoughness = .5f;

        // Старая плёнка: пыль, царапины, мерцание.
        public bool Film;
        public float FilmDust = .4f;
        public float FilmScratches = .3f;
        public float FilmFlicker = .3f;
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

        // Подсветка контура огнём: край рисунка со стороны костра светлеет цветом его света.
        public bool Rim;
        public float RimStrength = .8f;
        public float RimWidth = 2;
        public bool RimPeople = true;
        public bool RimObjects;
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
            if (Version < 1)
            {
                // Первые значения по умолчанию были мельче пикселя на виде
                // места: нетронутые (равные им) — на новые, правки остаются.
                Upgrade(Dust, LocationParticleLayer.DefaultDust(), .03f, 4, .45f, 0);
                Upgrade(Midges, LocationParticleLayer.DefaultMidges(), .022f, 25, .8f, 0, new Color(.1f, .09f, .07f));
                Upgrade(Rain, LocationParticleLayer.DefaultRain(), .025f, 60, .35f, 14);
                Upgrade(Leaves, LocationParticleLayer.DefaultLeaves(), .09f, .6f, .9f, 0, new Color(.62f, .45f, .16f));
                Upgrade(Sparks, LocationParticleLayer.DefaultSparks(), .03f, 6, 1, 0);
                Upgrade(Breath, LocationParticleLayer.DefaultBreath(), .16f, .5f, .22f, 0);
                if (Mathf.Approximately(Breath.Speed, .12f) && Mathf.Approximately(Breath.Gravity, -.02f))
                {
                    Breath.Speed = .07f; Breath.Gravity = -.005f; Breath.Lifetime = 1.3f; Breath.Spread = 40;
                }
                Version = 1;
            }
        }

        // Версия значений по умолчанию (0 — первые, мелкие).
        public int Version;

        private static void Upgrade(LocationParticleLayer layer, LocationParticleLayer fresh, float oldSize, float oldDensity, float oldOpacity, float oldStretch, Color? oldColor = null)
        {
            if (Mathf.Approximately(layer.Size, oldSize)) layer.Size = fresh.Size;
            if (Mathf.Approximately(layer.Density, oldDensity)) layer.Density = fresh.Density;
            if (Mathf.Approximately(layer.Opacity, oldOpacity)) layer.Opacity = fresh.Opacity;
            if (oldStretch > 0 && Mathf.Approximately(layer.Stretch, oldStretch)) layer.Stretch = fresh.Stretch;
            if (oldColor != null && ((Vector4)(layer.Color - oldColor.Value)).sqrMagnitude < 1e-4f) { layer.Color = fresh.Color; layer.ColorB = fresh.ColorB; }
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
            Density = 5, Size = .12f, Lifetime = 6, Speed = .05f, Angle = 90, Spread = 360, Wander = .12f, WanderFrequency = .3f,
            Color = new Color(1, .93f, .78f), ColorB = new Color(1, .85f, .6f), Opacity = .6f, WindShare = .3f, Time = LocationParticleTime.Day
        };

        public static LocationParticleLayer DefaultMidges() => new LocationParticleLayer
        {
            Density = 60, Size = .14f, SizeJitter = .3f, Lifetime = 3, Speed = .05f, Spread = 360, Wander = 1.4f, WanderFrequency = 3,
            Color = new Color(.55f, .5f, .38f), ColorB = new Color(.82f, .76f, .58f), Opacity = .75f, WindShare = .1f,
            Time = LocationParticleTime.Hours, FromHour = 17, ToHour = 22, Groups = 3
        };

        public static LocationParticleLayer DefaultRain() => new LocationParticleLayer
        {
            Density = 160, Size = .045f, SizeJitter = .3f, Lifetime = 1.2f, Speed = 9, Angle = 260, Spread = 4, Wander = 0,
            Color = new Color(.75f, .8f, .88f), ColorB = new Color(.65f, .7f, .8f), Opacity = .55f, WindShare = 1, Stretch = 12
        };

        public static LocationParticleLayer DefaultLeaves() => new LocationParticleLayer
        {
            Density = 2, Size = .45f, SizeJitter = .4f, Lifetime = 9, Speed = .35f, Angle = 270, Spread = 40, Gravity = .03f,
            Wander = .5f, WanderFrequency = .4f, Color = new Color(.86f, .58f, .14f), ColorB = new Color(.7f, .26f, .09f),
            Opacity = .9f, WindShare = 1
        };

        public static LocationParticleLayer DefaultSparks() => new LocationParticleLayer
        {
            Density = 16, Size = .12f, SizeJitter = .5f, Lifetime = 1.6f, Speed = .9f, Angle = 90, Spread = 35, Gravity = -.05f,
            Wander = .5f, WanderFrequency = 1.5f, Color = new Color(1, .7f, .25f), ColorB = new Color(1, .35f, .08f),
            Opacity = 1, WindShare = .4f
        };

        public static LocationParticleLayer DefaultBreath() => new LocationParticleLayer
        {
            Density = 2, Size = .5f, SizeJitter = .3f, Lifetime = 1.3f, Speed = .07f, Angle = 80, Spread = 40, Gravity = -.005f,
            Wander = .1f, WanderFrequency = .6f, Color = new Color(.9f, .92f, .95f), ColorB = new Color(.85f, .88f, .92f),
            Opacity = .4f, WindShare = .5f, Time = LocationParticleTime.Night
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

    // Вид существа на месте: цвет поверх рисунка (до света) и сколько его
    // освещают солнце и огонь. Общая поправка — для всех, своя — у типа из
    // Базы существ; итог — общая, затем своя (тон и контраст складываются,
    // насыщенность, яркость, подкраска и свет умножаются).
    [Serializable]
    public sealed class LocationCreatureLook
    {
        // ID типа из Базы существ; пусто — общая поправка.
        public string UnitId = "";
        // Сдвиг тона, градусы (−180..180).
        public float Hue;
        public float Saturation = 1;
        public float Brightness = 1;
        // −1..1: меньше — мягче, больше — резче.
        public float Contrast;
        // Подкраска (умножение), белый — без неё.
        public Color Tint = Color.white;
        // Доля солнца (общего света суток) и огня: 0 — не темнеет ночью / не освещается огнём.
        public float SunLight = 1;
        public float FireLight = 1;

        public bool IsIdentity =>
            Mathf.Abs(Mathf.DeltaAngle(0, Hue)) < .01f && Mathf.Abs(Saturation - 1) < .001f && Mathf.Abs(Brightness - 1) < .001f &&
            Mathf.Abs(Contrast) < .001f && Tint == Color.white && Mathf.Abs(SunLight - 1) < .001f && Mathf.Abs(FireLight - 1) < .001f;

        // Для шейдера рисунков: тон в оборотах, насыщенность, яркость, контраст.
        public Vector4 ShaderHsv => new Vector4(Mathf.DeltaAngle(0, Hue) / 360f, Mathf.Max(0, Saturation), Mathf.Max(0, Brightness), Mathf.Clamp(Contrast, -1, 1));

        public void CopyFrom(LocationCreatureLook other)
        {
            Hue = other.Hue; Saturation = other.Saturation; Brightness = other.Brightness; Contrast = other.Contrast;
            Tint = other.Tint; SunLight = other.SunLight; FireLight = other.FireLight;
        }

        public static LocationCreatureLook Combine(LocationCreatureLook all, LocationCreatureLook own)
        {
            LocationCreatureLook result = new LocationCreatureLook();
            if (all != null) result.CopyFrom(all);
            if (own == null) return result;
            result.UnitId = own.UnitId;
            result.Hue += own.Hue;
            result.Saturation *= own.Saturation;
            result.Brightness *= own.Brightness;
            result.Contrast += own.Contrast;
            result.Tint *= own.Tint;
            result.SunLight *= own.SunLight;
            result.FireLight *= own.FireLight;
            return result;
        }
    }

    // Раздел «Персонажи» обработки кадра: подгонка цвета существ к месту.
    [Serializable]
    public sealed class LocationCharacterEffects
    {
        public LocationCreatureLook All = new LocationCreatureLook();
        public List<LocationCreatureLook> Creatures = new List<LocationCreatureLook>();

        public LocationCreatureLook Find(string unitId) =>
            string.IsNullOrEmpty(unitId) ? null : Creatures?.Find(item => item != null && item.UnitId == unitId);

        // Своя поправка типа; нет — создаётся (для правки в окне).
        public LocationCreatureLook Own(string unitId)
        {
            LocationCreatureLook look = Find(unitId);
            if (look != null || string.IsNullOrEmpty(unitId)) return look;
            Creatures ??= new List<LocationCreatureLook>();
            look = new LocationCreatureLook { UnitId = unitId };
            Creatures.Add(look);
            return look;
        }

        public LocationCreatureLook Resolve(string unitId) => LocationCreatureLook.Combine(All, Find(unitId));

        // Хоть что-то меняет вид существ.
        public bool AnyActive => (All != null && !All.IsIdentity) || (Creatures != null && Creatures.Exists(item => item != null && !item.IsIdentity));

        public void Repair()
        {
            All ??= new LocationCreatureLook();
            All.UnitId = "";
            Creatures ??= new List<LocationCreatureLook>();
            Creatures.RemoveAll(item => item == null || string.IsNullOrEmpty(item.UnitId));
        }
    }

    public enum LocationAntialiasing { None, Fxaa, SmaaLow, SmaaMedium, SmaaHigh }

    // Свет и воздух: тени облаков по земле, лучи солнца, марево над огнём.
    // Облака и лучи — в мире (не ездят с камерой), днём и на улице.
    [Serializable]
    public sealed class LocationAirEffects
    {
        public bool Clouds;
        public float CloudCoverage = .45f;
        public float CloudDarkness = .35f;
        // Размер облака, единицы мира; мягкость края 0..1.
        public float CloudScale = 8;
        public float CloudSoftness = .35f;
        public float CloudSpeed = .4f;
        // Направление движения, градусы; ветер раздела «Рисунки» — если включён.
        public float CloudAngle = 20;

        public bool Rays;
        // Угол лучей — по солнцу места (иначе свой).
        public bool RaysFollowSun = true;
        public float RaysAngle = 250;
        public float RaysIntensity = .2f;
        public Color RaysColor = new Color(1, .9f, .7f, 1);
        // Ширина полосы, единицы мира; густота 0..1; скорость дрожания.
        public float RaysWidth = 1.2f;
        public float RaysDensity = .5f;
        public float RaysSpeed = .15f;

        public bool HeatHaze;
        public float HazeStrength = 1;
        // Ширина и высота марева над огнём, единицы мира.
        public float HazeRadius = .6f;
        public float HazeHeight = 1.6f;
        public float HazeSpeed = 1;
    }

    // Игра: состояния героя на экране и кинополосы (их силу задаёт игра:
    // LocationWorldRenderer.SetHeroHealth / SetStun / FadeTo / SetLetterbox).
    [Serializable]
    public sealed class LocationGameEffects
    {
        public bool LowHealth = true;
        // Ниже этой доли здоровья — красные пульсирующие края.
        public float LowHealthThreshold = .35f;
        public Color LowHealthColor = new Color(.55f, 0, 0, 1);
        public float LowHealthStrength = .75f;
        public float LowHealthPulse = 1.1f;

        public bool Stun = true;
        public float StunDarkness = .45f;
        public float StunDesaturate = .6f;
        // Размытие, пиксели кадра.
        public float StunBlur = 4;

        public Color FadeColor = Color.black;

        // Кинополосы: постоянные (доля высоты сверху и снизу) и цвет; сцены добавляют свои.
        public float Letterbox;
        public Color LetterboxColor = Color.black;
    }

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
        Characters = 128,
        Game = 256,
        All = Color | LightLens | Tone | Canvas | Sprites | Atmosphere | Quality | Characters | Game
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
            LocationFrameSection.Sprites, LocationFrameSection.Characters, LocationFrameSection.Atmosphere, LocationFrameSection.Game, LocationFrameSection.Quality
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
                case LocationFrameSection.Characters: return "Персонажи";
                case LocationFrameSection.Game: return "Игра";
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
            if ((sections & LocationFrameSection.Characters) != 0) target.Characters = from.Characters;
            if ((sections & LocationFrameSection.Atmosphere) != 0) target.Air = from.Air;
            if ((sections & LocationFrameSection.Game) != 0) target.Game = from.Game;
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
