using System;
using System.Collections.Generic;

// Бриф «Живое население глобальной карты» (19.11.2026): изменяемое состояние
// населения конкретной партии (GameState.Population). GeneratedMap задаёт
// исходные данные (определения групп, маршруты, источники); здесь — их
// жизнь: состав с HP по устойчивым ID участников, занятие и опорная точка,
// контакты с героем, столкновения групп, резервы источников, знание игрока.
// Рендер, бой и сохранение читают одну эту модель; UI своей истины о
// группах не держит. Чистые данные для JsonUtility (без словарей и null).
[Serializable]
public sealed class PopulationMemberState
{
    public string MemberId = string.Empty;
    public string UnitTypeId = string.Empty;
    // 0 — полный запас шаблона Базы существ.
    public int HitPoints;
    public bool Alive = true;
}

[Serializable]
public sealed class PopulationGroupState
{
    // Статус группы.
    public const string Active = "active";
    public const string Destroyed = "destroyed";

    // Занятия (компактный автомат состояний).
    public const string Route = "route";
    public const string Wait = "wait";
    public const string Observe = "observe";
    public const string Chase = "chase";
    public const string Avoid = "avoid";
    public const string Return = "return";
    public const string Conflict = "conflict";

    public string GroupId = string.Empty;
    // Определение в GeneratedMap.Actors (маршрут, территория, облик).
    public string DefinitionId = string.Empty;
    public string SourceId = string.Empty;
    public string PresetId = string.Empty;
    public string Role = string.Empty;
    public string Behavior = string.Empty;
    public string Name = string.Empty;
    public string Status = Active;
    public string Activity = Route;
    public List<PopulationMemberState> Members = new List<PopulationMemberState>();

    // Текущая позиция (проценты холста) и направление; для Route — значение
    // на последнем такте, между тактами показ берёт маршрут по времени.
    public float X;
    public float Y;
    public float DirX;
    public float DirY = 1f;
    public bool HasPosition;

    // Цель занятия: "hero", ID группы или пусто.
    public string TargetId = string.Empty;
    public double ActivityStartHour;
    public double ActivityUntilHour;
    public float ActivityOriginX;
    public float ActivityOriginY;

    // Ритм: до этого часа группа не проявляет враждебной инициативы.
    public double CalmUntilHour;
    public double LastContactHour = -1000.0;
    public int ContactCount;
    // Предупреждение текущего приближения уже показано.
    public bool Warned;
    public bool ObserveReported;
    public string ConflictId = string.Empty;
    public double SpawnHour;
    public double DestroyedHour;
    public string Outcome = string.Empty;

    // Знание игрока: 0 — неизвестна, 1 — следы, 2 — приблизительно (слухи),
    // 3 — видели. Последняя известная позиция отличается от живой.
    public int Knowledge;
    public float LastKnownX;
    public float LastKnownY;
    public double LastKnownHour;

    public bool IsActive => Status == Active;
}

[Serializable]
public sealed class PopulationSourceState
{
    public const string Active = "active";
    // Вылазка разгромлена — до срока пополнения источник молчит.
    public const string Weakened = "weakened";
    // Резерв исчерпан — источник больше не даёт групп.
    public const string Exhausted = "exhausted";
    // Окончательно устранён/разрешён игроком.
    public const string Eliminated = "eliminated";

    public string SourceId = string.Empty;
    public string Kind = string.Empty;
    public string PresetId = string.Empty;
    public string Name = string.Empty;
    public float X;
    public float Y;
    public float RadiusHexes;
    public string Status = Active;
    // -1 — без предела (деревня).
    public int Reserve;
    public double NextRefillHour;
    public int Spawned;
    public string Signs = string.Empty;
    // 0 — неизвестен, 1 — следы найдены, 2 — место известно (рассказали/видели).
    public int Knowledge;
    public double CampCooldownUntil;
    public double EliminatedHour;
}

[Serializable]
public sealed class PopulationConflictState
{
    public const string Open = "open";
    public const string Resolved = "resolved";

    public string ConflictId = string.Empty;
    public string AttackerId = string.Empty;
    public string DefenderId = string.Empty;
    public float X;
    public float Y;
    public double StartHour;
    public double ResolveHour;
    public string Status = Open;
    // Итог: attacker-won, defender-won, rescued (вмешался герой).
    public string Outcome = string.Empty;
    // Герою уже предлагали вмешаться — второй раз не спрашивать.
    public bool HeroOffered;
}

[Serializable]
public sealed class PopulationContactState
{
    // Виды контакта с героем (одно обязательное окно за раз).
    public const string BeastAttack = "beast";
    public const string BandDemand = "band";
    public const string CampApproach = "camp";
    public const string ConflictHelp = "conflict";

    public const string Pending = "pending";
    // Выбран бой, он ещё не начат (переживает загрузку).
    public const string BattlePending = "battle-pending";
    // Бой начат; итог применяет CampaignBattleBridge.
    public const string InBattle = "battle";
    public const string Resolved = "resolved";

    public string ContactId = string.Empty;
    public string Kind = string.Empty;
    public string GroupId = string.Empty;
    public string SourceId = string.Empty;
    public string ConflictId = string.Empty;
    public double Hour;
    public float X;
    public float Y;
    public string Status = Pending;
    public string Choice = string.Empty;
    public int DemandAmount;
    public string Outcome = string.Empty;

    public string BattleId => "population." + ContactId;
}

[Serializable]
public sealed class WorldPopulationStateData
{
    public const int CurrentVersion = 1;

    public int Version;
    public bool Initialized;
    public int MapSeed;
    // Партия создана до слоя населения: группы обёрнуты миграцией, новые
    // звери и существа не добавлялись.
    public bool Legacy;
    public double LastSimHour = -1.0;
    public double RespiteUntilHour;
    public int NextContactNumber;
    public int NextConflictNumber;
    public double NextUpkeepHour;
    public List<PopulationSourceState> Sources = new List<PopulationSourceState>();
    public List<PopulationGroupState> Groups = new List<PopulationGroupState>();
    public List<PopulationConflictState> Conflicts = new List<PopulationConflictState>();
    public List<PopulationContactState> Contacts = new List<PopulationContactState>();
}
