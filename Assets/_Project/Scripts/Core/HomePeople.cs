using System;
using System.Collections.Generic;

// ПР-06А: люди Дома как одна личность во всех системах (спецификация
// ProjectDocs/PR06_PEOPLE_SPEC.md). Один PersonId — одна запись: боец,
// говорящий в диалоге и член семьи ссылаются на тот же объект.
//
// Физическое место не хранится отдельно: человек в походе тогда и только
// тогда, когда он в составе активного похода и отряд уже сдвинулся с
// места (HomePeopleService.IsInExpedition). Так подготовка не выключает
// домашние функции, выход — выключает, возврат — восстанавливает, и
// сохранение не может рассинхронизировать место и поход.

public enum ResidentAgeGroup
{
    Adult,
    Child,
    Elder
}

public enum ResidentLifeStatus
{
    Alive,
    Dead
}

public enum ResidentMembership
{
    HomeMember,
    Offered,
    Departed
}

public enum ResidentInjury
{
    None,
    Recovering
}

// Какую роль человек может занимать в походе.
public enum ResidentTravelRole
{
    None,
    Commander,
    Combatant,
    Retinue
}

[Serializable]
public sealed class ResidentState
{
    public string PersonId = string.Empty;
    public string DisplayName = string.Empty;
    public string RoleLabel = string.Empty;
    public string ShortDescription = string.Empty;
    public string HouseholdId = string.Empty;
    public string DialogueSpeakerId = string.Empty;
    public ResidentMembership Membership = ResidentMembership.HomeMember;
    public ResidentLifeStatus LifeStatus = ResidentLifeStatus.Alive;
    public ResidentAgeGroup AgeGroup = ResidentAgeGroup.Adult;
    public ResidentInjury Injury = ResidentInjury.None;
    public ResidentTravelRole TravelRole = ResidentTravelRole.None;

    // Индивидуальные боевые данные: шаблон и формулы — в UnitDatabase,
    // здесь только текущее состояние этого человека.
    public string UnitTypeId = string.Empty;
    public bool HasCombatState;
    public int CurrentHitPoints;
    public int MaxHitPoints;

    // Уход: доля пройденного цикла и HP на его начало (прогресс не
    // округляется по кадрам и переживает сохранение).
    public double RecoveryProgress;
    public int RecoveryBaseHitPoints;

    // ПР-08: изнеможение — дискретное состояние (−1 атака, −1 инициатива),
    // снимается ночью дома или отваром трав.
    public bool Exhausted;

    public string DeathReasonId = string.Empty;
    public string DepartureReasonId = string.Empty;

    public bool IsAlive => LifeStatus == ResidentLifeStatus.Alive;
    public bool IsHomeMember => IsAlive && Membership == ResidentMembership.HomeMember;
    public bool NeedsCare => IsHomeMember &&
                             (Injury == ResidentInjury.Recovering ||
                              (HasCombatState && CurrentHitPoints < MaxHitPoints));
}

[Serializable]
public sealed class HouseholdState
{
    public string HouseholdId = string.Empty;
    public string DisplayName = string.Empty;
    public List<string> MemberIds = new List<string>();
}

// Домашняя работа с настоящим прогрессом (ПР-06А: хозяйственный настил).
[Serializable]
public sealed class HomeWorkState
{
    public string WorkId = string.Empty;
    public bool Started;
    public bool Completed;
    public double DoneWork;
    public double RequiredWork;
}

[Serializable]
public sealed class HomePeopleState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion = CurrentSchemaVersion;
    public List<ResidentState> Residents = new List<ResidentState>();
    public List<HouseholdState> Households = new List<HouseholdState>();
    public List<HomeWorkState> Works = new List<HomeWorkState>();

    // ПР-07Б: заработанный, но ещё не выплаченный улов (с дробной частью);
    // выплачивается целыми в полночь, остаток переходит дальше.
    public double FishingEarned;

    // ПР-08: сколько улова уже поступило в запасы — для сушёной рыбы в дорогу.
    public int FishingPaidTotal;
}
