using System;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12М: солнце места. В 2D-свете Unity у общего света нет теней, поэтому
    // тень от солнца — отброшенный силуэт рисунка (LocationShadowProjector):
    // направление идёт с утра к вечеру, длина — длинная на восходе и закате,
    // короткая в полдень; ночью тени гаснут или их даёт луна.
    // Углы — на экране: 0 — вправо, 90 — вверх (от зрителя), 180 — влево.
    [Serializable]
    public sealed class LocationSunDefinition
    {
        public bool Enabled = true;
        public float Sunrise = 6;
        public float Sunset = 20;
        public float MorningAngle = 165;
        public float EveningAngle = 15;
        // Длина тени в долях высоты предмета.
        public float NoonLength = .45f;
        public float LowLength = 1.8f;
        public float Opacity = .42f;
        public float Softness = .3f;
        public Color Color = new Color(.05f, .06f, .10f);
        public bool MoonShadows;
        public float MoonAngle = 110;
        public float MoonLength = .9f;
        public float MoonOpacity = .15f;

        // Тень в этот час: направление (единичный вектор экрана, Y вверх),
        // длина и непрозрачность (0 — тени нет).
        public void Evaluate(float hour, out Vector2 direction, out float length, out float opacity)
        {
            direction = Vector2.right;
            length = 0;
            opacity = 0;
            if (!Enabled)
                return;
            hour = Mathf.Repeat(hour, 24);
            float sunrise = Mathf.Repeat(Sunrise, 24), sunset = Mathf.Repeat(Sunset, 24);
            float day = sunset > sunrise ? sunset - sunrise : sunset + 24 - sunrise;
            float since = Mathf.Repeat(hour - sunrise, 24);
            if (since <= day && day > .01f)
            {
                float t = since / day;
                float angle = Mathf.LerpAngle(MorningAngle, EveningAngle, t) * Mathf.Deg2Rad;
                // Без перехода через противоположную сторону: угол идёт по
                // кратчайшей дуге, как солнце по небу.
                direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                length = Mathf.Lerp(NoonLength, LowLength, 1 - Mathf.Sin(Mathf.PI * t));
                opacity = Opacity * Mathf.Clamp01(since) * Mathf.Clamp01(day - since);
                return;
            }
            if (!MoonShadows)
                return;
            float moon = MoonAngle * Mathf.Deg2Rad;
            direction = new Vector2(Mathf.Cos(moon), Mathf.Sin(moon));
            length = MoonLength;
            float fromDay = Mathf.Min(Mathf.Repeat(sunrise - hour, 24), Mathf.Repeat(hour - sunset, 24));
            opacity = MoonOpacity * Mathf.Clamp01(fromDay);
        }
    }

    // ПР-12М: обработка кадра (URP Volume) — днём и ночью свои значения
    // цвета, между ними — по яркости суток. Под крышей — дневные значения.
    [Serializable]
    public sealed class LocationPostEffects
    {
        public bool Enabled;
        public LocationColorGrade Day = new LocationColorGrade();
        public LocationColorGrade Night = new LocationColorGrade
        {
            Exposure = -.25f, Contrast = 8, Saturation = -30, Temperature = -18, Filter = new Color(.82f, .88f, 1f)
        };
        public bool Bloom = true;
        public float BloomIntensity = .6f;
        public float BloomThreshold = .9f;
        public float BloomScatter = .6f;
        public Color BloomTint = Color.white;
        public bool Vignette;
        public float VignetteIntensity = .28f;
        public float VignetteSmoothness = .45f;
        public Color VignetteColor = Color.black;
        public float Grain;
        public LocationTonemapping Tonemapping = LocationTonemapping.None;
    }

    [Serializable]
    public sealed class LocationColorGrade
    {
        public float Exposure;
        public float Contrast;
        public float Saturation;
        public float Temperature;
        public float Tint;
        public Color Filter = Color.white;

        public static LocationColorGrade Lerp(LocationColorGrade a, LocationColorGrade b, float t) => new LocationColorGrade
        {
            Exposure = Mathf.Lerp(a.Exposure, b.Exposure, t),
            Contrast = Mathf.Lerp(a.Contrast, b.Contrast, t),
            Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t),
            Temperature = Mathf.Lerp(a.Temperature, b.Temperature, t),
            Tint = Mathf.Lerp(a.Tint, b.Tint, t),
            Filter = Color.Lerp(a.Filter, b.Filter, t)
        };
    }

    public enum LocationTonemapping { None, Neutral, ACES }
}

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12М: стиль теней от солнца и огня. Тень — прямая проекция рисунка
    // (силуэт): верна, пока свет спереди или сзади, и плавно гаснет, когда
    // свет уходит вбок — плоский рисунок там дал бы линию. Углы и темнота
    // подбираются в «Стиле теней» Базы локаций. Для огня — какая нижняя
    // часть рисунка перекрывает свет (тень-конус Unity по контуру).
    [System.Serializable]
    public sealed class LocationShadowStyle
    {
        // Угол света от бока (0° — строго сбоку, 90° — спереди или сзади):
        // ниже «исчезает» силуэта нет, выше «виден полностью» — полный.
        public float SilhouetteHiddenBelow = 12;
        public float SilhouetteFullAbove = 35;
        public bool SilhouetteBehind = true;
        public float SilhouetteOpacity = 1;
        // Свет огня перекрывает только нижняя часть рисунка (доля его высоты
        // от земли): тень-конус идёт от того, чем предмет стоит на земле, —
        // между ногами свет проходит дальше, под руками светло. 1 — весь рисунок.
        public float FireBlockPeople = .3f;
        public float FireBlockObjects = .3f;

        // 0..1: насколько виден силуэт при тени вдоль vector (экран).
        public float SilhouetteShare(UnityEngine.Vector2 vector)
        {
            if (vector.sqrMagnitude < 1e-8f) return 1;
            UnityEngine.Vector2 direction = vector.normalized;
            if (direction.y < 0 && !SilhouetteBehind) return 0;
            float angle = UnityEngine.Mathf.Asin(UnityEngine.Mathf.Clamp(UnityEngine.Mathf.Abs(direction.y), 0, 1)) * UnityEngine.Mathf.Rad2Deg;
            float from = UnityEngine.Mathf.Clamp(SilhouetteHiddenBelow, 0, 90), to = UnityEngine.Mathf.Clamp(SilhouetteFullAbove, 0, 90);
            if (to <= from) return angle >= from ? 1 : 0;
            float t = UnityEngine.Mathf.Clamp01((angle - from) / (to - from));
            return t * t * (3 - 2 * t);
        }

        public LocationShadowStyle Clone() =>
            UnityEngine.JsonUtility.FromJson<LocationShadowStyle>(UnityEngine.JsonUtility.ToJson(this));
    }

    // ПР-12М: общий свет мира — небо одно на все места: кривая суток и цвет
    // неба, солнце и луна, стиль теней, обработка кадра. Место либо берёт его
    // («Общий свет мира»), либо своё. «Улица / под крышей», источники и
    // предметы всегда у места.
    [System.Serializable]
    public sealed class LocationWorldLighting
    {
        public LocationDaylight Daylight = new LocationDaylight();
        public LocationSunDefinition Sun = new LocationSunDefinition();
        public LocationPostEffects Post = new LocationPostEffects();
        public bool PeopleCastShadows = true;
        public float PeopleShadowLength = 1;
        // Прежняя «толщина тени сбоку» — не используется (данные).
        public float ShadowMinLean = .4f;
        public LocationShadowStyle ShadowStyle = new LocationShadowStyle();
    }

    // Небо места: общий свет мира или свой — одна точка чтения и правки
    // для рендерера, окна базы и готовых настроек.
    public sealed class LocationSky
    {
        private readonly LocationVisualDefinition visual;
        private readonly LocationWorldLighting world;

        public LocationSky(LocationVisualDefinition visual, LocationWorldLighting world)
        {
            this.visual = visual;
            this.world = world;
        }

        public bool IsWorld => visual == null || (visual.UseWorldLighting && world != null);
        private LocationWorldLighting World => world ?? new LocationWorldLighting();

        public LocationDaylight Daylight
        {
            get => IsWorld ? World.Daylight : visual.Daylight;
            set { if (IsWorld) World.Daylight = value; else visual.Daylight = value; }
        }

        public LocationSunDefinition Sun
        {
            get => IsWorld ? World.Sun : visual.Sun;
            set { if (IsWorld) World.Sun = value; else visual.Sun = value; }
        }

        public LocationPostEffects Post
        {
            get => IsWorld ? World.Post : visual.Post;
            set { if (IsWorld) World.Post = value; else visual.Post = value; }
        }

        public bool PeopleCastShadows
        {
            get => IsWorld ? World.PeopleCastShadows : visual.PeopleCastShadows;
            set { if (IsWorld) World.PeopleCastShadows = value; else visual.PeopleCastShadows = value; }
        }

        public float PeopleShadowLength
        {
            get => IsWorld ? World.PeopleShadowLength : visual.PeopleShadowLength;
            set { if (IsWorld) World.PeopleShadowLength = value; else visual.PeopleShadowLength = value; }
        }

        public LocationShadowStyle ShadowStyle
        {
            get
            {
                if (IsWorld) return World.ShadowStyle ?? (World.ShadowStyle = new LocationShadowStyle());
                return visual.ShadowStyle ?? (visual.ShadowStyle = new LocationShadowStyle());
            }
            set { if (IsWorld) World.ShadowStyle = value; else visual.ShadowStyle = value; }
        }

        // Своё небо места — копия общего (кнопка «Скопировать общий свет мира»).
        public static void CopyWorldToOwn(LocationWorldLighting world, LocationVisualDefinition visual)
        {
            if (world == null || visual == null) return;
            visual.Daylight = UnityEngine.JsonUtility.FromJson<LocationDaylight>(UnityEngine.JsonUtility.ToJson(world.Daylight));
            visual.Sun = UnityEngine.JsonUtility.FromJson<LocationSunDefinition>(UnityEngine.JsonUtility.ToJson(world.Sun));
            visual.Post = UnityEngine.JsonUtility.FromJson<LocationPostEffects>(UnityEngine.JsonUtility.ToJson(world.Post));
            visual.PeopleCastShadows = world.PeopleCastShadows;
            visual.PeopleShadowLength = world.PeopleShadowLength;
            visual.ShadowStyle = (world.ShadowStyle ?? new LocationShadowStyle()).Clone();
        }

        public static LocationWorldLighting FromOwn(LocationVisualDefinition visual)
        {
            LocationWorldLighting world = new LocationWorldLighting();
            if (visual == null) return world;
            world.Daylight = UnityEngine.JsonUtility.FromJson<LocationDaylight>(UnityEngine.JsonUtility.ToJson(visual.Daylight));
            world.Sun = UnityEngine.JsonUtility.FromJson<LocationSunDefinition>(UnityEngine.JsonUtility.ToJson(visual.Sun));
            world.Post = UnityEngine.JsonUtility.FromJson<LocationPostEffects>(UnityEngine.JsonUtility.ToJson(visual.Post));
            world.PeopleCastShadows = visual.PeopleCastShadows;
            world.PeopleShadowLength = visual.PeopleShadowLength;
            world.ShadowStyle = (visual.ShadowStyle ?? new LocationShadowStyle()).Clone();
            return world;
        }
    }
}
