using System;
using System.Collections.Generic;

// ПР-06А: люди партии как одна личность во всех системах. Один PersonId —
// одна запись: боец, говорящий в диалоге и участник похода ссылаются на
// тот же объект. Канон v1.59: исходного Дома нет — реестр хранит героя,
// бойцов, свиту и наёмников текущей партии.

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

// Значения сериализуются числами: порядок не менять и не переиспользовать.
public enum ResidentMembership
{
    // Свой человек партии (прежнее имя HomeMember, то же значение 0).
    Member,
    Offered,
    Departed,
    // ПР-12К (канон v1.53 §28.10): внешний участник похода (наёмник,
    // существо-союзник). Добавлено строго в конец.
    External
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
    public string DialogueSpeakerId = string.Empty;
    public ResidentMembership Membership = ResidentMembership.Member;
    public ResidentLifeStatus LifeStatus = ResidentLifeStatus.Alive;
    public ResidentAgeGroup AgeGroup = ResidentAgeGroup.Adult;
    public ResidentInjury Injury = ResidentInjury.None;
    public ResidentTravelRole TravelRole = ResidentTravelRole.None;

    // ПР-12К (канон v1.53 §28.10): происхождение участия. Свой человек — по
    // умолчанию (старые сохранения); внешний участник — Membership.External.
    public CampaignParticipantOrigin Origin = CampaignParticipantOrigin.HomePerson;
    // Допуск в поселения: пусто — допускается; иначе причина отказа,
    // которую игрок видит заранее («Не допускается: …»).
    public string SettlementAdmissionBlock = string.Empty;
    // Флаг истории, снимающий запрет (авторское исключение); пусто — нет.
    public string SettlementAdmissionExceptionFlag = string.Empty;

    // Индивидуальные боевые данные: шаблон и формулы — в UnitDatabase,
    // здесь только текущее состояние этого человека.
    public string UnitTypeId = string.Empty;
    public bool HasCombatState;
    public int CurrentHitPoints;
    public int MaxHitPoints;

    // ПР-08: изнеможение — дискретное состояние (−1 атака, −1 инициатива),
    // снимается отдыхом в лагере или отваром трав.
    public bool Exhausted;

    public string DeathReasonId = string.Empty;
    public string DepartureReasonId = string.Empty;

    public bool IsAlive => LifeStatus == ResidentLifeStatus.Alive;
    public bool IsMember => IsAlive && Membership == ResidentMembership.Member;
}

[Serializable]
public sealed class PeopleState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion = CurrentSchemaVersion;
    public List<ResidentState> Residents = new List<ResidentState>();
}
