using System;
using System.Collections.Generic;

// ПР-12К (канон v1.54 §28.3): неизменяемая авторская конфигурация
// исследуемого места. Место — рисунок своего размера (пиксели, Y вниз, как
// у глобальной карты). По нему отряд ходит так же, как по глобальной карте:
// своя разметка местности (сетка WorldMapHexGrid + WorldMapTerrainLayer),
// поиск пути с обходом и сглаживанием (WorldMapPathfinder), свои числа
// движения. Клетки боя появляются только в бою: кадр поля из Базы полей
// боя кладётся на рисунок в заданном месте (ArenaCenter столкновения).

[Serializable]
public sealed class LocalPointData
{
    public float X;
    public float Y;

    public LocalPointData()
    {
    }

    public LocalPointData(float x, float y)
    {
        X = x;
        Y = y;
    }

    public double DistanceTo(double x, double y)
    {
        double dx = X - x;
        double dy = Y - y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override string ToString() => "(" + X.ToString("0") + ", " + Y.ToString("0") + ")";
}

// Клетка арены боя (q, r) — в данных запроса и расстановки.
[Serializable]
public sealed class LocalCellData
{
    public int Q;
    public int R;

    public LocalCellData()
    {
    }

    public LocalCellData(int q, int r)
    {
        Q = q;
        R = r;
    }

    public bool Is(int q, int r) => Q == q && R == r;
    public override string ToString() => "(" + Q + ", " + R + ")";
}

// Прямоугольник на рисунке места (пиксели) — зона угрозы.
[Serializable]
public sealed class LocalAreaData
{
    public float X;
    public float Y;
    public float Width;
    public float Height;

    public LocalAreaData()
    {
    }

    public LocalAreaData(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public bool Contains(double x, double y) =>
        x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}

public enum LocalObjectKind
{
    // Подойти и открыть диалог из Базы диалогов.
    Dialogue = 0,
    // Подойти и прочитать короткий текст (без выбора).
    Inspect = 1
}

// Вход и выход места: через него входят с глобальной карты и уходят обратно.
[Serializable]
public sealed class LocalEntranceDefinition
{
    public string Id = string.Empty;
    public string Label = string.Empty;
    public LocalPointData Point = new LocalPointData();
}

[Serializable]
public sealed class LocalObjectDefinition
{
    public string Id = string.Empty;
    // Подпись над объектом и текст кнопки действия.
    public string Label = string.Empty;
    public string ActionLabel = string.Empty;
    public LocalObjectKind Kind = LocalObjectKind.Dialogue;
    // Точка объекта на рисунке; подходят на расстояние InteractRadius.
    public LocalPointData Point = new LocalPointData();
    public float InteractRadius = 70f;
    public string DialogueId = string.Empty;
    // Текст для Inspect и короткая подсказка.
    public string Text = string.Empty;
    // Отработал один раз — больше не предлагается.
    public bool OnceOnly = true;
    // Объект виден, только если флаг истории поставлен / не поставлен.
    public string RequiresFlag = string.Empty;
    public string HiddenWhenFlag = string.Empty;
}

// Противник места: один экземпляр со своим здоровьем и исходом. Погибший не
// появляется снова; раненый не исцеляется молча.
[Serializable]
public sealed class LocalEnemyDefinition
{
    public string InstanceId = string.Empty;
    public string UnitTypeId = string.Empty;
    public int Level = 1;
    // Логово: здесь противник стоит при входе и сюда возвращается после
    // отхода отряда (раны остаются).
    public LocalPointData Point = new LocalPointData();
    public string EncounterId = string.Empty;
}

// Столкновение: зона угрозы, кадр арены, вовлечённые противники и отход.
[Serializable]
public sealed class LocalEncounterDefinition
{
    public string Id = string.Empty;
    // ID боя = префикс + номер попытки (как у истории места).
    public string BattleIdPrefix = string.Empty;
    // Командир вошёл в зону — столкновение начинается.
    public LocalAreaData TriggerArea = new LocalAreaData();
    // Центр кадра поля боя на рисунке места; не задан — середина между
    // отрядом и противниками.
    public bool HasArenaCenter;
    public LocalPointData ArenaCenter = new LocalPointData();
    // Диалог перед боем (одна реплика за шагом); пусто — бой сразу.
    public string IntroDialogueId = string.Empty;
    public bool AllowRetreat = true;
    // Куда отряд отходит при разрешённом отходе — безопасная точка.
    public LocalPointData RetreatPoint = new LocalPointData();
    // Подготовленное начало, если этот спутник присутствует.
    public string PreparedStartCompanionId = string.Empty;
    // Флаг истории, исчерпывающий столкновение (например, «логово очищено»).
    public string ResolvedFlag = string.Empty;
}

[Serializable]
public sealed class LocalLocationDefinition
{
    public string Id = string.Empty;
    // Режим кампании (CampaignConfiguration.CrisisId), в котором место
    // открывается локальной картой; пусто — во всех режимах.
    public string ModeId = string.Empty;
    // Место глобальной карты, у которого этот вход.
    public string WorldLocationId = string.Empty;
    public string DisplayName = string.Empty;
    // Поле Базы полей боя: настройки сетки и вид клеток боя на месте.
    public string BattlefieldId = string.Empty;
    // Фон — временная заглушка, рисунка ещё нет (показывается игроку).
    public bool PlaceholderArt;

    // Размер рисунка места в пикселях и плотность сетки проходимости
    // (клеток по ширине) — как у глобальной карты.
    public float CanvasWidth = 1920f;
    public float CanvasHeight = 1080f;
    public int HexesAcross = 60;
    // Разметка местности (WorldMapTerrainLayer.Encode). Пусто — всё проходимо.
    public string TerrainCells = string.Empty;
    // Ширина кадра поля боя (16:9) на рисунке места: из неё — размер клетки
    // боя и фигур в исследовании и в бою (одинаковый).
    public float BattleFrameWidth = 1920f;
    // Те же поля, что «Перемещение» глобальной карты, но свои числа:
    // шаг по шахте не стоит часов глобальной клетки [РАБОЧЕЕ].
    public WorldMapMovementRules Movement = DefaultMovement();

    public List<LocalEntranceDefinition> Entrances = new List<LocalEntranceDefinition>();
    public List<LocalObjectDefinition> Objects = new List<LocalObjectDefinition>();
    public List<LocalEnemyDefinition> Enemies = new List<LocalEnemyDefinition>();
    public List<LocalEncounterDefinition> Encounters = new List<LocalEncounterDefinition>();
    public double HoursPerInteraction = 0.25;

    public static WorldMapMovementRules DefaultMovement()
    {
        WorldMapMovementRules rules = WorldMapMovementRules.CreateDefault();
        rules.HeroRunSpeedHexesPerSecond = 9f;
        rules.TravelHoursPerHex = 0.0125f;
        return rules;
    }

    public WorldMapHexGrid CreateGrid() => new WorldMapHexGrid(
        Math.Max(1f, CanvasWidth), Math.Max(1f, CanvasHeight), WorldMapHexGrid.SanitizeHexesAcross(HexesAcross));

    public WorldMapTerrainLayer CreateTerrainLayer() => WorldMapTerrainLayer.Decode(CreateGrid(), TerrainCells);

    public WorldMapMovementRules MovementRules => Movement ?? DefaultMovement();

    public LocalEntranceDefinition FindEntrance(string id)
    {
        return Entrances.Find(entrance => entrance != null && entrance.Id == id) ??
               (Entrances.Count > 0 ? Entrances[0] : null);
    }

    public LocalObjectDefinition FindObject(string id) => Objects.Find(item => item != null && item.Id == id);
    public LocalEnemyDefinition FindEnemy(string id) => Enemies.Find(enemy => enemy != null && enemy.InstanceId == id);
    public LocalEncounterDefinition FindEncounter(string id) => Encounters.Find(encounter => encounter != null && encounter.Id == id);
}

// Каталог исследуемых мест. Значения по умолчанию регистрирует модуль
// содержания (свободная игра — пилот «Старая шахта»); Unity-слой при запуске
// подставляет правленые в Базе локаций записи с теми же ID.
public static class LocalLocationCatalog
{
    private static readonly List<LocalLocationDefinition> Defaults = new List<LocalLocationDefinition>();
    private static List<LocalLocationDefinition> overrides;

    public static IReadOnlyList<LocalLocationDefinition> Current
    {
        get
        {
            if (overrides == null || overrides.Count == 0)
                return Defaults;
            List<LocalLocationDefinition> merged = new List<LocalLocationDefinition>();
            foreach (LocalLocationDefinition location in Defaults)
                merged.Add(overrides.Find(item => item != null && item.Id == location.Id) ?? location);
            foreach (LocalLocationDefinition location in overrides)
            {
                if (location != null && !merged.Exists(item => item.Id == location.Id))
                    merged.Add(location);
            }
            return merged;
        }
    }

    // Значение по умолчанию от модуля содержания (повторная регистрация того
    // же ID заменяет прежнюю).
    public static void RegisterDefault(LocalLocationDefinition location)
    {
        if (location == null || string.IsNullOrEmpty(location.Id))
            return;
        Defaults.RemoveAll(existing => existing.Id == location.Id);
        Defaults.Add(location);
    }

    // Правленые записи из базы (Inspector); null — только значения по умолчанию.
    public static void SetOverrides(IEnumerable<LocalLocationDefinition> locations)
    {
        overrides = locations != null ? new List<LocalLocationDefinition>(locations) : null;
    }

    public static IReadOnlyList<LocalLocationDefinition> RegisteredDefaults => Defaults;

    public static LocalLocationDefinition Find(string localLocationId)
    {
        if (string.IsNullOrEmpty(localLocationId))
            return null;
        foreach (LocalLocationDefinition location in Current)
        {
            if (location != null && location.Id == localLocationId)
                return location;
        }
        return null;
    }

    // Исследуемое место у входа в место глобальной карты в режиме этой
    // партии; null — у места нет локальной карты, работает прежний
    // текстовый вход.
    public static LocalLocationDefinition ForWorldLocation(GameState state, string worldLocationId)
    {
        if (string.IsNullOrEmpty(worldLocationId))
            return null;
        string modeId = state?.Configuration != null ? state.Configuration.CrisisId : string.Empty;
        foreach (LocalLocationDefinition location in Current)
        {
            if (location != null && location.WorldLocationId == worldLocationId &&
                (string.IsNullOrEmpty(location.ModeId) || location.ModeId == modeId))
                return location;
        }
        return null;
    }
}
