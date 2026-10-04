using System;
using System.Collections.Generic;

// ПР-12К (канон v1.53 §28.3): изменяемое состояние исследуемых мест в
// партии. Авторская конфигурация места — LocalLocationDefinition; здесь
// только то, что изменилось и должно пережить выход, повторный вход и
// сохранение. Находки, у которых уже есть флаг истории (железо шахты),
// хранятся флагом истории, а не здесь: двух источников истины нет.
// Координаты — точки рисунка места (пиксели, Y вниз), как на глобальной карте.

[Serializable]
public sealed class LocalActorStateData
{
    // PersonId участника похода или InstanceId противника места.
    public string ActorId = string.Empty;
    public float X;
    public float Y;
    // Ракурс фигуры (0..5, порядок направлений гекса).
    public int Facing;
    // Противник: текущее здоровье (0 — полный запас шаблона) и исход.
    public int HitPoints;
    public bool Defeated;
}

[Serializable]
public sealed class LocalLocationStateData
{
    public string LocalLocationId = string.Empty;
    public bool Visited;
    // Однократные действия, которые уже отработали (ID объекта места).
    public List<string> DoneInteractionIds = new List<string>();
    // Противники места по устойчивым InstanceId: здоровье, позиция, исход.
    public List<LocalActorStateData> Enemies = new List<LocalActorStateData>();
    // Столкновения, завершённые победой: повторно не начинаются.
    public List<string> ResolvedEncounterIds = new List<string>();
    // Применённые к месту итоги боёв (BattleId): повтор не меняет место.
    public List<string> AppliedBattleIds = new List<string>();
}

[Serializable]
public sealed class LocalExplorationStateData
{
    public List<LocalLocationStateData> Locations = new List<LocalLocationStateData>();

    // Активный слой: отряд внутри исследуемого места. False — глобальная
    // карта (или прежний текстовый вход в место).
    public bool IsActive;
    public string ActiveLocalLocationId = string.Empty;
    // Каким входом вошли — туда же ведёт выход по умолчанию.
    public string EntranceId = string.Empty;
    // Позиции и ракурсы присутствующих участников похода внутри места.
    public List<LocalActorStateData> Party = new List<LocalActorStateData>();
    // Сообщение игроку после восстановления (место недоступно и т. п.).
    public string PendingNotice = string.Empty;
}
