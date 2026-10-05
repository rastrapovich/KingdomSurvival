using System;
using System.Collections.Generic;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.BattleSandbox;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    [Serializable]
    public sealed class LocationDaylight
    {
        public AnimationCurve Brightness = new AnimationCurve(
            new Keyframe(0, .16f), new Keyframe(5, .16f), new Keyframe(8, .8f),
            new Keyframe(13, 1), new Keyframe(18, .7f), new Keyframe(21, .16f), new Keyframe(24, .16f));
        public Gradient Color = DefaultColors();
        public float Intensity = 1;

        public float Evaluate(float hours) => Mathf.Max(0, Brightness.Evaluate(Mathf.Repeat(hours, 24))) * Intensity;
        public UnityEngine.Color EvaluateColor(float hours) => Color.Evaluate(Mathf.Repeat(hours, 24) / 24);
        public static Gradient DefaultColors()
        {
            Gradient result = new Gradient();
            result.SetKeys(new[] {
                new GradientColorKey(new UnityEngine.Color(.48f, .60f, .85f), 0),
                new GradientColorKey(new UnityEngine.Color(.48f, .60f, .85f), .21f),
                new GradientColorKey(new UnityEngine.Color(1, .84f, .66f), .30f),
                new GradientColorKey(UnityEngine.Color.white, .5f),
                new GradientColorKey(new UnityEngine.Color(1, .75f, .52f), .77f),
                new GradientColorKey(new UnityEngine.Color(.48f, .60f, .85f), .88f),
                new GradientColorKey(new UnityEngine.Color(.48f, .60f, .85f), 1)
            }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return result;
        }
    }

    [Serializable]
    public sealed class LocationLightDefinition
    {
        public bool Enabled;
        public Color Color = new Color(1, .48f, .16f);
        public float Intensity = 1.6f;
        public float Radius = 3;
        public float Softness = .7f;
        public bool Shadows = true;
        public float ShadowStrength = .8f;
        public float ShadowSoftness = .25f;
        public bool NightOnly;
        public float StartsAt = 18.5f;
        public float EndsAt = 5.5f;
        public float Flicker = .08f;
        public Vector2 Offset = new Vector2(0, .2f);

        // ПР-12М: остальные возможности 2D-света Unity.
        public LocationLightShape Shape = LocationLightShape.Point;
        // Конус (Shape = Spot): поворот в градусах (0 — вправо, 90 — вверх) и углы.
        public float Direction = 270;
        public float InnerAngle = 40;
        public float OuterAngle = 70;
        // Резкость спада к краю (0 — мягко, 1 — резко).
        public float Falloff = .5f;
        // Свет произвольной формы: точки контура (единицы мира от источника) и мягкость края.
        public List<Vector2> FreeformPoints = new List<Vector2>
        { new Vector2(-1, -.6f), new Vector2(1, -.6f), new Vector2(1, .6f), new Vector2(-1, .6f) };
        public float FreeformFalloff = .5f;
        // Свет-рисунок (маска).
        public Sprite Cookie;
        public Vector2 CookieSize = new Vector2(3, 3);
        // Свечение воздуха.
        public bool Volumetric;
        public float VolumeIntensity = .25f;
        public bool VolumetricShadows;
        public float VolumeShadowIntensity = .5f;
        // Смешивание (стиль рендерера 2D, 0..3), перекрытие и порядок.
        public int BlendStyle;
        public bool AlphaOverlap;
        public int Order;
        // Жизнь огня.
        public LocationLightAnimation Animation = LocationLightAnimation.Flicker;
        public float AnimationSpeed = 1;
        // Карты нормалей: свет «облегает» рисунки, у которых они есть.
        public bool NormalMaps;
        public bool NormalMapsAccurate;
        public float NormalMapDistance = 3;
        // Тень предметов и людей от этого источника — силуэт по рисунку,
        // отброшенный от света; длина зависит от высоты источника.
        public bool ProjectsShadows = true;
        public float Height = 1.4f;
        public float ProjectedShadowOpacity = .55f;
        public float ProjectedShadowMaxLength = 2.5f;
        public float ShadowSoftnessFalloff = .5f;

        // Яркость с учётом жизни огня в момент seconds.
        public float AnimatedIntensity(float seconds, float seed)
        {
            float speed = Mathf.Max(.01f, AnimationSpeed);
            switch (Animation)
            {
                case LocationLightAnimation.Flicker:
                    float wave = Mathf.PerlinNoise(seconds * 1.8f * speed, seed * 31);
                    return Intensity * (1 + (wave * 2 - 1) * Flicker);
                case LocationLightAnimation.Pulse:
                    return Intensity * (1 + Mathf.Sin(seconds * Mathf.PI * speed) * Flicker);
                case LocationLightAnimation.Strobe:
                    return Mathf.Repeat(seconds * speed, 1) < .5f ? Intensity : Intensity * (1 - Mathf.Clamp01(Flicker * 4));
                default:
                    return Intensity;
            }
        }

        // Интервал включает начало, исключает конец; одинаковые часы = весь день.
        public bool ActiveAt(float hour)
        {
            if (!Enabled) return false;
            if (!NightOnly) return true;
            hour = Mathf.Repeat(hour, 24);
            float start = Mathf.Repeat(StartsAt, 24), end = Mathf.Repeat(EndsAt, 24);
            return Mathf.Approximately(start, end) || (start < end ? hour >= start && hour < end : hour >= start || hour < end);
        }
    }

    public enum LocationLightShape { Point, Spot, Freeform, Sprite }
    public enum LocationLightAnimation { None, Flicker, Pulse, Strobe }
    // Улица — общий свет по суткам и солнце; под крышей (пещера, изба) —
    // постоянный свет, без солнечных теней.
    public enum LocationLightingMode { Outdoor, Indoor }

    public enum LocationVisualBand { Ground, GroundDetail, World, Foreground }
    public enum LocationPlaceholder { None, Tent, Fire, Crate, Rock, Bush }

    // ПР-12Н: какие настройки экземпляр ассета задаёт сам, а не берёт из
    // Базы ассетов. Слой — у основы; проходимость — блокировка и размер
    // основания; тени — силуэт, длина и перекрытие света.
    [Flags]
    public enum LocationAssetOverride { None = 0, Layer = 1, Passability = 2, Shadows = 4 }

    [Serializable]
    public sealed class LocationVisualVariant
    {
        public string Id;
        public string Name;
        public Sprite Sprite;
        // Карта нормалей состояния (подключается к его рисунку при импорте).
        public Texture2D NormalMap;
    }

    [Serializable]
    public sealed class LocationVisualObject
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Объект";
        public string GroupId = "";
        public Sprite Sprite;
        public List<LocationVisualVariant> Variants = new List<LocationVisualVariant>();
        public string DefaultVariantId = "";
        public LocationPlaceholder Placeholder;
        // Доли рисунка места, Y растёт вниз (как точки места).
        public Vector2 Position = new Vector2(.5f, .5f);
        public Vector2 Pivot = new Vector2(.5f, .15f);
        public float Height = 1.8f;
        public bool FlipX;
        public bool Hidden;
        public bool Locked;
        public LocationVisualBand Band = LocationVisualBand.World;
        public int OrderOffset;
        public bool BlocksMovement;
        public Vector2 Footprint = new Vector2(1.2f, .55f);
        public bool CastsShadow;
        public LocationLightDefinition Light = new LocationLightDefinition();
        // ПР-12М: только источник света, без рисунка.
        public bool LightOnly;
        // Отброшенная тень-силуэт (от солнца и местных источников).
        public bool ProjectsShadow = true;
        public float ShadowLength = 1;
        // Свой силуэт тени (дерево: тень кроны не совпадает с рисунком).
        public Sprite ShadowSprite;
        // Карта нормалей рисунка (подключается к спрайту при импорте).
        public Texture2D NormalMap;

        // ПР-12Н: ссылка на запись Базы ассетов. Пусто — прежний прямой
        // рисунок (Sprite, Height, Pivot выше). Задано — рисунки ракурсов,
        // части, опора, основание и рекомендуемые настройки берутся из
        // каталога; поля выше остаются запасным показом и значениями
        // явных переопределений (Overrides).
        public string AssetId = "";
        public ArtAssetView View = ArtAssetView.Front;
        // Локальный масштаб экземпляра (1 — игровой размер ассета).
        public float Scale = 1;
        public LocationAssetOverride Overrides;

        public bool UsesAsset => !string.IsNullOrEmpty(AssetId);
        public bool IsOverridden(LocationAssetOverride flag) => (Overrides & flag) != 0;

        public Sprite ResolveSprite(string variantId = null)
        {
            string id = variantId ?? DefaultVariantId;
            LocationVisualVariant variant = Variants?.Find(item => item != null && item.Id == id);
            return variant?.Sprite != null ? variant.Sprite : Sprite;
        }
    }

    [Serializable]
    public sealed class LocationVisualDefinition
    {
        public string LocationId;
        public bool TechnicalTest;
        // Рисунок места целиком (растягивается на размер рисунка места). Нет —
        // техническая заглушка по разметке местности.
        public Sprite Background;
        public LocationLightingMode Lighting = LocationLightingMode.Outdoor;
        // Небо (сутки, солнце, луна, стиль теней, обработка кадра): общий свет
        // мира (База локаций) или своё — поля ниже.
        public bool UseWorldLighting = true;
        public Color IndoorColor = new Color(.62f, .57f, .50f);
        public float IndoorIntensity = .55f;
        public LocationDaylight Daylight = new LocationDaylight();
        public LocationSunDefinition Sun = new LocationSunDefinition();
        public LocationPostEffects Post = new LocationPostEffects();
        // Тени людей (исследование и бой на месте).
        public bool PeopleCastShadows = true;
        public float PeopleShadowLength = 1;
        // Наименьший наклон тени «от зрителя» (доля высоты): иначе тень от огня
        // сбоку или от низкого солнца сплющивается в линию.
        public float ShadowMinLean = .4f;
        public Texture2D BackgroundNormalMap;
        public List<LocationVisualObject> Objects = new List<LocationVisualObject>();
        // Точка появления тестового отряда — доли рисунка места.
        public Vector2 TestStartPoint = new Vector2(.25f, .5f);
        public string TestUnitId = "militia";
        public int TestFollowers = 2;
    }

    // ПР-12К (канон v1.54 §28.3): мир места. Позиции объектов — доли рисунка
    // места (Y вниз), мир — пиксели рисунка / PixelsPerUnit, начало — центр
    // рисунка, Y вверх. Высоты, основания и радиусы света — в единицах мира.
    public static class LocationVisualGeometry
    {
        // 1080 пикселей рисунка = 10 единиц мира (прежний кадр 16:9).
        public const float PixelsPerUnit = 108f;

        public static Vector2 CanvasSize(LocalLocationDefinition location) => location != null
            ? new Vector2(Mathf.Max(1, location.CanvasWidth), Mathf.Max(1, location.CanvasHeight))
            : new Vector2(1920, 1080);

        public static Vector2 WorldSize(LocalLocationDefinition location) => CanvasSize(location) / PixelsPerUnit;

        public static Vector2 PixelToWorld(LocalLocationDefinition location, Vector2 pixel)
        {
            Vector2 canvas = CanvasSize(location);
            return new Vector2((pixel.x - canvas.x / 2) / PixelsPerUnit, (canvas.y / 2 - pixel.y) / PixelsPerUnit);
        }

        public static Vector2 WorldToPixel(LocalLocationDefinition location, Vector2 world)
        {
            Vector2 canvas = CanvasSize(location);
            return new Vector2(world.x * PixelsPerUnit + canvas.x / 2, canvas.y / 2 - world.y * PixelsPerUnit);
        }

        public static Vector2 ToPixel(LocalLocationDefinition location, Vector2 normalized) =>
            Vector2.Scale(normalized, CanvasSize(location));

        public static Vector2 ToNormalized(LocalLocationDefinition location, Vector2 pixel)
        {
            Vector2 canvas = CanvasSize(location);
            return new Vector2(pixel.x / canvas.x, pixel.y / canvas.y);
        }

        public static Vector2 ToWorld(LocalLocationDefinition location, Vector2 normalized) =>
            PixelToWorld(location, ToPixel(location, normalized));

        // Основания предметов, которые не пропускают (пиксели рисунка).
        public static List<Rect> BlockedAreas(LocationVisualDefinition visual, LocalLocationDefinition location)
        {
            List<Rect> result = new List<Rect>();
            if (visual == null)
                return result;
            foreach (LocationVisualObject item in visual.Objects)
            {
                if (item == null || item.Hidden) continue;
                LocationResolvedVisual resolved = LocationVisualResolver.Resolve(item);
                if (!resolved.BlocksMovement) continue;
                result.Add(FootprintRect(location, item, resolved));
            }
            return result;
        }

        // Основание предмета на рисунке места (пиксели, Y вниз).
        public static Rect FootprintRect(LocalLocationDefinition location, LocationVisualObject item, LocationResolvedVisual resolved)
        {
            Vector2 center = ToPixel(location, item.Position) +
                             new Vector2(resolved.FootprintOffset.x, -resolved.FootprintOffset.y) * PixelsPerUnit;
            Vector2 size = Vector2.Max(Vector2.zero, resolved.FootprintSize) * PixelsPerUnit;
            return new Rect(center - size / 2, size);
        }

        public static int SortOrder(LocationVisualBand band, float groundY, int offset = 0)
        {
            if (band == LocationVisualBand.Ground) return -30000 + offset;
            if (band == LocationVisualBand.GroundDetail) return -25000 + offset;
            if (band == LocationVisualBand.Foreground) return 25000 + offset;
            return Mathf.Clamp(Mathf.RoundToInt(-groundY * 100) + offset, -20000, 20000);
        }

        public static List<string> Validate(LocalLocationDefinition location, LocationVisualDefinition visual, BattlefieldDefinitionData field)
        {
            List<string> errors = new List<string>();
            if (location == null || visual == null || field == null) { errors.Add("Не найдены место, художественная сборка или поле."); return errors; }
            HashSet<string> ids = new HashSet<string>();
            foreach (LocationVisualObject item in visual.Objects)
            {
                if (item == null) { errors.Add("Пустой объект."); continue; }
                if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)) errors.Add(item.Name + ": пустой или повторный ID.");
                if (item.UsesAsset)
                {
                    if (ArtAssetDatabaseAsset.FindCurrent(item.AssetId) == null)
                        errors.Add(item.Name + ": ассет «" + item.AssetId + "» не найден в Базе ассетов.");
                    if (item.Scale <= 0) errors.Add(item.Name + ": неверный масштаб экземпляра.");
                }
                else if (!item.LightOnly && item.Sprite == null && item.Placeholder == LocationPlaceholder.None) errors.Add(item.Name + ": нет спрайта.");
                if ((!item.UsesAsset && item.Height <= 0) || item.Footprint.x < 0 || item.Footprint.y < 0) errors.Add(item.Name + ": неверный размер.");
                if (item.Position.x < 0 || item.Position.x > 1 || item.Position.y < 0 || item.Position.y > 1) errors.Add(item.Name + ": за пределами рисунка.");
                if (item.Light.Enabled && (item.Light.Radius <= 0 || item.Light.Intensity < 0)) errors.Add(item.Name + ": неверный свет.");
            }
            LocalLocationGeometry geometry = new LocalLocationGeometry(location, field, BlockedAreas(visual, location));
            Vector2 start = ToPixel(location, visual.TestStartPoint);
            if (!geometry.IsPassable(start.x, start.y)) errors.Add("Точка тестового появления непроходима.");
            return errors;
        }
    }
}
