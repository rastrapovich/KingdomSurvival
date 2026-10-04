using System;
using System.Collections.Generic;
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

    public enum LocationVisualBand { Ground, GroundDetail, World, Foreground }
    public enum LocationPlaceholder { None, Tent, Fire, Crate, Rock, Bush }

    [Serializable]
    public sealed class LocationVisualVariant
    {
        public string Id;
        public string Name;
        public Sprite Sprite;
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
        // Доли единого кадра поля, Y растёт вниз — как у BattlefieldFrame.
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
        public LocationDaylight Daylight = new LocationDaylight();
        public List<LocationVisualObject> Objects = new List<LocationVisualObject>();
        public Vector2Int TestStart = new Vector2Int(4, 4);
        public string TestUnitId = "militia";
        public int TestFollowers = 2;
        // Единый размер и в предпросмотре, и в тесте. Это размер авторского кадра.
        public const float WorldHeight = 10;
        public const float WorldWidth = WorldHeight * BattlefieldFrame.Aspect;
    }

    public static class LocationVisualGeometry
    {
        public static BattlefieldGridLayout Layout(BattlefieldDefinitionData field) =>
            BattlefieldFrame.ComputeLayout(new Rect(0, 0, LocationVisualDefinition.WorldWidth, LocationVisualDefinition.WorldHeight),
                BattlefieldFrame.GetGridArea(field));

        public static Vector2 ToWorld(Vector2 normalized) => new Vector2(
            (normalized.x - .5f) * LocationVisualDefinition.WorldWidth,
            (.5f - normalized.y) * LocationVisualDefinition.WorldHeight);

        public static Vector2 ToNormalized(Vector2 world) => new Vector2(
            world.x / LocationVisualDefinition.WorldWidth + .5f,
            .5f - world.y / LocationVisualDefinition.WorldHeight);

        public static Vector2 CellPosition(BattlefieldDefinitionData field, HexCoord cell)
        {
            Vector2 p = Layout(field).GetCenter(cell.Q, cell.R);
            return new Vector2(p.x - LocationVisualDefinition.WorldWidth / 2, LocationVisualDefinition.WorldHeight / 2 - p.y);
        }

        public static bool TryCell(BattlefieldDefinitionData field, Vector2 world, out HexCoord cell)
        {
            Vector2 p = new Vector2(world.x + LocationVisualDefinition.WorldWidth / 2, LocationVisualDefinition.WorldHeight / 2 - world.y);
            bool found = Layout(field).TryGetCell(p, out int q, out int r);
            cell = new HexCoord(q, r);
            return found;
        }

        public static List<HexCoord> BlockedCells(LocationVisualDefinition visual, BattlefieldDefinitionData field)
        {
            HashSet<HexCoord> result = new HashSet<HexCoord>();
            foreach (LocationVisualObject item in visual.Objects)
            {
                if (item == null || item.Hidden || !item.BlocksMovement) continue;
                Rect area = new Rect(ToWorld(item.Position) - item.Footprint / 2, item.Footprint);
                foreach (HexCoord cell in SandboxArenaShape.Cells())
                    if (area.Contains(CellPosition(field, cell))) result.Add(cell);
            }
            return new List<HexCoord>(result);
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
                if (item.Sprite == null && item.Placeholder == LocationPlaceholder.None) errors.Add(item.Name + ": нет спрайта.");
                if (item.Height <= 0 || item.Footprint.x < 0 || item.Footprint.y < 0) errors.Add(item.Name + ": неверный размер.");
                if (item.Position.x < 0 || item.Position.x > 1 || item.Position.y < 0 || item.Position.y > 1) errors.Add(item.Name + ": за пределами кадра.");
                if (item.Light.Enabled && (item.Light.Radius <= 0 || item.Light.Intensity < 0)) errors.Add(item.Name + ": неверный свет.");
            }
            LocalLocationGeometry geometry = new LocalLocationGeometry(location, field, BlockedCells(visual, field));
            HexCoord start = new HexCoord(visual.TestStart.x, visual.TestStart.y);
            if (!geometry.IsPassable(start)) errors.Add("Точка тестового появления непроходима.");
            return errors;
        }
    }
}
