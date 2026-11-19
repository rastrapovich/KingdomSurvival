using System;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф «Поверхности и окружение боевой арены» (19.11.2026), [РАБОЧЕЕ —
    // ПРОТОТИП]. Клетка описывается слоями по смыслу, а не отдельным типом на
    // каждое сочетание:
    //   высота — прежний SandboxTerrain.Difficult (холм) и Impassable;
    //   основание — SandboxGround (земля, грязь, топь, вода, щебень, снег...);
    //   растительность — SandboxVegetation (лес, заросли);
    //   объекты и временные эффекты — модель состояний и объектов боя.
    // Старые арены ничего из нового не задают: основание Default и без
    // растительности ведут себя как прежде.
    public enum SandboxGround
    {
        // Не задано: прежнее поведение клетки.
        Default = 0,
        Ground = 1,
        Mud = 2,
        Bog = 3,
        ShallowWater = 4,
        Rubble = 5,
        DeepSnow = 6,
        Ice = 7,
        DeepWater = 8,
        Cliff = 9,
        Road = 10,
        Sand = 11,
        // Мокрая земля — во что тает лёд, если клетка изначально не вода.
        WetGround = 12
    }

    public enum SandboxVegetation
    {
        None = 0,
        Forest = 1,
        Brush = 2
    }

    // Пассивные особенности передвижения (теги Базы существ). Каждая снимает
    // только свой компонент стоимости/штрафа.
    public static class SandboxTerrainTags
    {
        public const string ForestWalker = "move.forest_walker";
        public const string SwampWalker = "move.swamp_walker";
        public const string Highlander = "move.highlander";
        public const string Snowshoe = "move.snowshoe";
        public const string SureFooted = "move.surefoot";
        public const string SpiderStep = "move.spider_step";
        public const string WaterWalker = "move.water_walker";
        public const string Swimmer = "move.swimmer";

        public static readonly string[] All =
        {
            ForestWalker, SwampWalker, Highlander, Snowshoe, SureFooted, SpiderStep, WaterWalker, Swimmer
        };
    }

    // Рабочие числа местности. Значения — стартовые настройки прототипа, не
    // окончательный баланс; правятся в Базе локаций («Общее для всех полей
    // боя» → поверхности), игра берёт их из Current.
    [Serializable]
    public sealed class SandboxSurfaceRules
    {
        private static SandboxSurfaceRules current = new SandboxSurfaceRules();

        public static SandboxSurfaceRules Current
        {
            get => current;
            set => current = value ?? new SandboxSurfaceRules();
        }

        // Стоимость входа (минимум 1 у проходимой клетки).
        public int HillCost = SandboxTerrainRules.HillMovementCost;
        public int ForestCost = 2;
        public int BrushCost = 2;
        public int MudCost = 2;
        public int BogCost = 2;
        public int ShallowWaterCost = 2;
        public int RubbleCost = 2;
        public int DeepSnowCost = 2;
        public int IceCost = 2;
        public int SandCost = 2;
        public int WetGroundCost = 1;
        public int RoadCost = 1;
        // Защита.
        public int HillDefense = SandboxTerrainRules.HillDefenseBonus;
        public int ForestCover = 2;
        public int BrushCover = 1;
        public int LowObjectCover = 2;
        public int BogDefensePenalty = 1;
        // Предел положительной защиты от местности (холм + укрытие).
        public int TerrainDefenseCap = 4;

        public SandboxSurfaceRules Clone() => (SandboxSurfaceRules)MemberwiseClone();

        public int SafeCost(int value) => Math.Max(1, value);
    }

    public static class SandboxSurfaceLabels
    {
        public static string Ground(SandboxGround ground)
        {
            switch (ground)
            {
                case SandboxGround.Ground: return "Земля";
                case SandboxGround.Mud: return "Грязь";
                case SandboxGround.Bog: return "Топь";
                case SandboxGround.ShallowWater: return "Мелководье";
                case SandboxGround.Rubble: return "Щебень";
                case SandboxGround.DeepSnow: return "Глубокий снег";
                case SandboxGround.Ice: return "Лёд";
                case SandboxGround.DeepWater: return "Глубокая вода";
                case SandboxGround.Cliff: return "Обрыв";
                case SandboxGround.Road: return "Дорога";
                case SandboxGround.Sand: return "Песок";
                case SandboxGround.WetGround: return "Мокрая земля";
                default: return "Земля";
            }
        }

        public static string Vegetation(SandboxVegetation vegetation)
        {
            switch (vegetation)
            {
                case SandboxVegetation.Forest: return "лес";
                case SandboxVegetation.Brush: return "заросли";
                default: return string.Empty;
            }
        }

        public static string Tag(string tagId)
        {
            switch (tagId)
            {
                case SandboxTerrainTags.ForestWalker: return "Лесной ходок";
                case SandboxTerrainTags.SwampWalker: return "Болотный ходок";
                case SandboxTerrainTags.Highlander: return "Привычный к высоте";
                case SandboxTerrainTags.Snowshoe: return "Снегоступ";
                case SandboxTerrainTags.SureFooted: return "Цепкая поступь";
                case SandboxTerrainTags.SpiderStep: return "Паучья поступь";
                case SandboxTerrainTags.WaterWalker: return "Водный ходок";
                case SandboxTerrainTags.Swimmer: return "Пловец";
                default: return tagId;
            }
        }
    }
}
