using System;
using System.Collections.Generic;
using System.Text;

// 12И (канон v1.50 §9.9): тип местности клетки шестиугольной сетки.
// Значения сериализуются в разметку мира — новые типы добавлять только в
// конец. Список рабочий: типы задают проходимость и множители движения
// через WorldMapMovementRules, а не через код.
public enum WorldMapGameplayTerrainType
{
    OpenGround = 0,
    Road = 1,
    Field = 2,
    Forest = 3,
    Water = 4,
    Trail = 5,
    Swamp = 6,
    Hills = 7,
    Mountains = 8,
    Cliffs = 9
}

public static class WorldMapTerrainLabels
{
    public static readonly WorldMapGameplayTerrainType[] All =
    {
        WorldMapGameplayTerrainType.OpenGround,
        WorldMapGameplayTerrainType.Road,
        WorldMapGameplayTerrainType.Trail,
        WorldMapGameplayTerrainType.Field,
        WorldMapGameplayTerrainType.Forest,
        WorldMapGameplayTerrainType.Swamp,
        WorldMapGameplayTerrainType.Hills,
        WorldMapGameplayTerrainType.Mountains,
        WorldMapGameplayTerrainType.Water,
        WorldMapGameplayTerrainType.Cliffs
    };

    public static string Name(WorldMapGameplayTerrainType terrain)
    {
        switch (terrain)
        {
            case WorldMapGameplayTerrainType.OpenGround: return "Открытая местность";
            case WorldMapGameplayTerrainType.Road: return "Дорога";
            case WorldMapGameplayTerrainType.Trail: return "Тропа";
            case WorldMapGameplayTerrainType.Field: return "Поле";
            case WorldMapGameplayTerrainType.Forest: return "Лес";
            case WorldMapGameplayTerrainType.Swamp: return "Болото";
            case WorldMapGameplayTerrainType.Hills: return "Холмы";
            case WorldMapGameplayTerrainType.Mountains: return "Горы";
            case WorldMapGameplayTerrainType.Water: return "Вода";
            case WorldMapGameplayTerrainType.Cliffs: return "Скалы";
            default: return terrain.ToString();
        }
    }
}

// Разметка мира: тип местности каждой клетки. Пустая разметка — вся карта
// открытая местность (безопасное значение без авторского мира).
public sealed class WorldMapTerrainLayer
{
    private readonly byte[] cells;

    public WorldMapHexGrid Grid { get; }

    public WorldMapTerrainLayer(WorldMapHexGrid grid, byte[] cells = null)
    {
        Grid = grid ?? WorldMapHexGrid.CreateDefault();
        this.cells = new byte[Grid.CellCount];
        if (cells != null)
            Array.Copy(cells, this.cells, Math.Min(cells.Length, this.cells.Length));
    }

    public static WorldMapTerrainLayer CreateDefault() =>
        new WorldMapTerrainLayer(WorldMapHexGrid.CreateDefault());

    public byte[] CopyCells() => (byte[])cells.Clone();

    public WorldMapGameplayTerrainType Get(WorldMapHexCell cell) =>
        Grid.IsInside(cell) ? (WorldMapGameplayTerrainType)cells[Grid.IndexOf(cell)] : WorldMapGameplayTerrainType.OpenGround;

    public WorldMapGameplayTerrainType GetAtIndex(int index) =>
        index >= 0 && index < cells.Length ? (WorldMapGameplayTerrainType)cells[index] : WorldMapGameplayTerrainType.OpenGround;

    public void Set(WorldMapHexCell cell, WorldMapGameplayTerrainType terrain)
    {
        if (Grid.IsInside(cell))
            cells[Grid.IndexOf(cell)] = (byte)terrain;
    }

    public WorldMapGameplayTerrainType GetAtPixel(double x, double y) => Get(Grid.CellAtPixel(x, y));

    public WorldMapGameplayTerrainType GetAtPercent(float xPercent, float yPercent) =>
        Get(Grid.CellAtPercent(xPercent, yPercent));

    public bool IsEmpty
    {
        get
        {
            foreach (byte value in cells)
            {
                if (value != 0)
                    return false;
            }
            return true;
        }
    }

    // Пересчёт под другую сетку (смена размера клетки): каждая новая клетка
    // берёт тип старой клетки под своим центром.
    public WorldMapTerrainLayer ResampleTo(WorldMapHexGrid target)
    {
        WorldMapTerrainLayer result = new WorldMapTerrainLayer(target);
        double scaleX = Grid.CanvasWidth / (double)target.CanvasWidth;
        double scaleY = Grid.CanvasHeight / (double)target.CanvasHeight;
        for (int index = 0; index < target.CellCount; index++)
        {
            WorldMapHexCell cell = target.CellAt(index);
            target.CellCenter(cell, out double x, out double y);
            result.cells[index] = (byte)GetAtPixel(x * scaleX, y * scaleY);
        }
        return result;
    }

    // Компактная запись для ассета: «число*тип» через запятую, одиночная
    // клетка — просто «тип». Пример: «4100*0,3*4,12*0».
    public string Encode()
    {
        StringBuilder builder = new StringBuilder();
        int i = 0;
        while (i < cells.Length)
        {
            byte value = cells[i];
            int run = 1;
            while (i + run < cells.Length && cells[i + run] == value)
                run++;
            if (builder.Length > 0)
                builder.Append(',');
            if (run > 1)
                builder.Append(run).Append('*');
            builder.Append(value);
            i += run;
        }
        return builder.ToString();
    }

    // Нераспознанные куски пропускаются; недостающие клетки — открытая местность.
    public static WorldMapTerrainLayer Decode(WorldMapHexGrid grid, string encoded)
    {
        WorldMapTerrainLayer layer = new WorldMapTerrainLayer(grid);
        if (string.IsNullOrWhiteSpace(encoded))
            return layer;

        int position = 0;
        foreach (string token in encoded.Split(','))
        {
            if (position >= layer.cells.Length)
                break;
            string trimmed = token.Trim();
            if (trimmed.Length == 0)
                continue;
            int run = 1;
            string valueText = trimmed;
            int star = trimmed.IndexOf('*');
            if (star >= 0)
            {
                if (!int.TryParse(trimmed.Substring(0, star), out run) || run <= 0)
                    continue;
                valueText = trimmed.Substring(star + 1);
            }
            if (!byte.TryParse(valueText, out byte value))
                value = 0;
            int end = Math.Min(layer.cells.Length, position + run);
            for (int i = position; i < end; i++)
                layer.cells[i] = value;
            position = end;
        }
        return layer;
    }
}

[Serializable]
public sealed class WorldMapTerrainRule
{
    public WorldMapGameplayTerrainType Terrain;
    public bool Traversable = true;
    // Скорость по игровому времени: часы на клетку = TravelHoursPerHex / множитель.
    public float TravelSpeedMultiplier = 1f;
    // Видимая скорость бега фигуры на экране.
    public float RunSpeedMultiplier = 1f;

    public WorldMapTerrainRule Clone() => new WorldMapTerrainRule
    {
        Terrain = Terrain,
        Traversable = Traversable,
        TravelSpeedMultiplier = TravelSpeedMultiplier,
        RunSpeedMultiplier = RunSpeedMultiplier
    };
}

// 12И: правила перемещения по карте — рабочие числа базы, а не канон.
// Ядро читает только Current; ассет настроек WorldMapVisual подменяет его
// при загрузке игры. Значения по умолчанию совпадают с исходным ассетом,
// кроме TravelHoursPerHex: ядро без мира берёт прежний эталон 4 ч/клетку.
[Serializable]
public sealed class WorldMapMovementRules
{
    public const float DefaultHeroRunSpeedHexesPerSecond = 2f;
    public const float DefaultTravelHoursPerHex = 4f;
    public const float DefaultDiscoveryRadiusHexes = 1.25f;
    public const float MinSpeedMultiplier = 0.05f;

    private static WorldMapMovementRules current = CreateDefault();

    public static WorldMapMovementRules Current
    {
        get => current;
        set => current = value ?? CreateDefault();
    }

    public float HeroRunSpeedHexesPerSecond = DefaultHeroRunSpeedHexesPerSecond;
    public float TravelHoursPerHex = DefaultTravelHoursPerHex;
    public float DiscoveryRadiusHexes = DefaultDiscoveryRadiusHexes;
    public List<WorldMapTerrainRule> Terrain = new List<WorldMapTerrainRule>();

    public static WorldMapMovementRules CreateDefault()
    {
        WorldMapMovementRules rules = new WorldMapMovementRules();
        rules.Terrain.AddRange(DefaultTerrainRules());
        return rules;
    }

    public static List<WorldMapTerrainRule> DefaultTerrainRules()
    {
        return new List<WorldMapTerrainRule>
        {
            Rule(WorldMapGameplayTerrainType.OpenGround, true, 1.00f, 1.00f),
            Rule(WorldMapGameplayTerrainType.Road, true, 1.30f, 1.15f),
            Rule(WorldMapGameplayTerrainType.Trail, true, 1.10f, 1.05f),
            Rule(WorldMapGameplayTerrainType.Field, true, 0.90f, 0.95f),
            Rule(WorldMapGameplayTerrainType.Forest, true, 0.70f, 0.80f),
            Rule(WorldMapGameplayTerrainType.Swamp, true, 0.50f, 0.65f),
            Rule(WorldMapGameplayTerrainType.Hills, true, 0.50f, 0.75f),
            Rule(WorldMapGameplayTerrainType.Mountains, true, 0.33f, 0.60f),
            Rule(WorldMapGameplayTerrainType.Water, false, 1.00f, 1.00f),
            Rule(WorldMapGameplayTerrainType.Cliffs, false, 1.00f, 1.00f)
        };
    }

    private static WorldMapTerrainRule Rule(WorldMapGameplayTerrainType terrain, bool traversable, float travel, float run) =>
        new WorldMapTerrainRule
        {
            Terrain = terrain,
            Traversable = traversable,
            TravelSpeedMultiplier = travel,
            RunSpeedMultiplier = run
        };

    public WorldMapTerrainRule GetRule(WorldMapGameplayTerrainType terrain)
    {
        if (Terrain != null)
        {
            foreach (WorldMapTerrainRule rule in Terrain)
            {
                if (rule != null && rule.Terrain == terrain)
                    return rule;
            }
        }
        foreach (WorldMapTerrainRule rule in DefaultTerrainRules())
        {
            if (rule.Terrain == terrain)
                return rule;
        }
        return Rule(terrain, true, 1f, 1f);
    }

    public bool IsTraversable(WorldMapGameplayTerrainType terrain) => GetRule(terrain).Traversable;

    public double SafeTravelHoursPerHex =>
        TravelHoursPerHex > 0f && !float.IsNaN(TravelHoursPerHex) ? TravelHoursPerHex : DefaultTravelHoursPerHex;

    public double SafeRunSpeedHexesPerSecond =>
        HeroRunSpeedHexesPerSecond > 0f && !float.IsNaN(HeroRunSpeedHexesPerSecond)
            ? HeroRunSpeedHexesPerSecond
            : DefaultHeroRunSpeedHexesPerSecond;

    // Игровые часы на одну клетку пути в этой местности.
    public double HoursPerHex(WorldMapGameplayTerrainType terrain) =>
        SafeTravelHoursPerHex / Math.Max(MinSpeedMultiplier, GetRule(terrain).TravelSpeedMultiplier);

    public double RunSpeedMultiplier(WorldMapGameplayTerrainType terrain) =>
        Math.Max(MinSpeedMultiplier, GetRule(terrain).RunSpeedMultiplier);

    // Относительная цена клетки для поиска пути (открытая местность = 1).
    public double PathCost(WorldMapGameplayTerrainType terrain) =>
        1.0 / Math.Max(MinSpeedMultiplier, GetRule(terrain).TravelSpeedMultiplier);

    public double MinPathCost
    {
        get
        {
            double min = double.MaxValue;
            foreach (WorldMapGameplayTerrainType terrain in WorldMapTerrainLabels.All)
            {
                if (IsTraversable(terrain))
                    min = Math.Min(min, PathCost(terrain));
            }
            return min == double.MaxValue ? 1.0 : min;
        }
    }

    // Пока отряд бежит, часы идут со скоростью бега: клетки в секунду × часы на клетку.
    public double RunningGameHoursPerRealSecond(WorldMapGameplayTerrainType terrain) =>
        SafeRunSpeedHexesPerSecond * RunSpeedMultiplier(terrain) * HoursPerHex(terrain);

    public WorldMapMovementRules Clone()
    {
        WorldMapMovementRules copy = new WorldMapMovementRules
        {
            HeroRunSpeedHexesPerSecond = HeroRunSpeedHexesPerSecond,
            TravelHoursPerHex = TravelHoursPerHex,
            DiscoveryRadiusHexes = DiscoveryRadiusHexes
        };
        if (Terrain != null)
        {
            foreach (WorldMapTerrainRule rule in Terrain)
            {
                if (rule != null)
                    copy.Terrain.Add(rule.Clone());
            }
        }
        return copy;
    }
}
