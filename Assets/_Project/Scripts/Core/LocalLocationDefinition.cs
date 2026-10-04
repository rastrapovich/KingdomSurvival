using System;
using System.Collections.Generic;

// ПР-12К (канон v1.53 §28.3): неизменяемая авторская конфигурация
// исследуемого места. Фон, сетка и стены — поле из Базы полей боя
// (BattlefieldId, его отключённые гексы); здесь только то, чего в поле нет:
// входы, объекты, противники, столкновения и трудная местность. Одна и та же
// геометрия служит и исследованию, и бою. Координаты — клетки (q, r)
// скрытой гексовой разметки поля.

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
    public LocalCellData Cell = new LocalCellData();
}

[Serializable]
public sealed class LocalObjectDefinition
{
    public string Id = string.Empty;
    // Подпись над объектом и текст кнопки действия.
    public string Label = string.Empty;
    public string ActionLabel = string.Empty;
    public LocalObjectKind Kind = LocalObjectKind.Dialogue;
    // Клетка объекта; объект занимает её, к нему подходят на соседнюю.
    public LocalCellData Cell = new LocalCellData();
    public string DialogueId = string.Empty;
    // Текст для Inspect и короткая подсказка при наведении.
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
    public LocalCellData Cell = new LocalCellData();
    public string EncounterId = string.Empty;
}

// Столкновение: зона угрозы, вовлечённые противники и правила отхода.
[Serializable]
public sealed class LocalEncounterDefinition
{
    public string Id = string.Empty;
    // ID боя = префикс + номер попытки (как у истории места).
    public string BattleIdPrefix = string.Empty;
    // Шаг командира в одну из клеток начинает столкновение.
    public List<LocalCellData> TriggerCells = new List<LocalCellData>();
    // Диалог перед боем (одна реплика за шагом); пусто — бой сразу.
    public string IntroDialogueId = string.Empty;
    public bool AllowRetreat = true;
    // Куда отряд отходит при разрешённом отходе — безопасная точка.
    public LocalCellData RetreatCell = new LocalCellData();
    // Подготовленное начало, если этот спутник присутствует (заметит заранее).
    public string PreparedStartCompanionId = string.Empty;
    // Флаг истории, означающий, что столкновение исчерпано (например,
    // «логово очищено»): тогда оно не начинается и противников нет.
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
    // Поле Базы полей боя: фон, сетка и стены.
    public string BattlefieldId = string.Empty;
    // Фон — временная заглушка, рисунка ещё нет (показывается игроку).
    public bool PlaceholderArt;
    public List<LocalEntranceDefinition> Entrances = new List<LocalEntranceDefinition>();
    public List<LocalObjectDefinition> Objects = new List<LocalObjectDefinition>();
    public List<LocalEnemyDefinition> Enemies = new List<LocalEnemyDefinition>();
    public List<LocalEncounterDefinition> Encounters = new List<LocalEncounterDefinition>();
    public List<LocalCellData> DifficultCells = new List<LocalCellData>();
    // Рабочие числа времени [РАБОЧЕЕ]: шаг по месту и значимое действие
    // стоят своих минут, а не часов глобальной клетки.
    public double HoursPerCell = 0.05;
    public double HoursPerInteraction = 0.25;

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
// подставляет правленые в Inspector записи с теми же ID.
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
