using System;
using System.Collections.Generic;
using System.Globalization;

// 12Е-4: единые события, на которые реагируют особенности, диспетчер,
// пределы срабатывания «раз за ход / бой / встречу / поход / до лагеря» и
// переброс без сейв-скама. Сценаристу не нужно помнить об особенностях в
// каждой сцене: игра сообщает о событии, диспетчер опрашивает особенности
// людей, которых оно касается (каталог §1 п. 6, §6 п. 4).

public enum FeatureTrigger
{
    // События, которые игра уже сообщает (12Е-4).
    CheckResolved,
    ExpeditionStarted,
    LocationEntered,
    RoadEncounterDetected,
    CampNight,
    BattleStarted,
    BattleEnded,
    ReturnedHome,

    // События внутри боя — подключаются вместе с боевыми особенностями (12Е-6).
    RoundStarted,
    AttackMade,
    DamageTaken,
    AllyDamaged,
    HitPointsBelowThreshold,
    Moved,

    // Механики, которых в игре ещё нет: на них подписаны только заглушки.
    EnemyEntersAdjacentHex,
    AllyAttacked,
    WeaponReloaded,
    ArrowsSpent,
    ItemDamaged
}

public static class FeatureTriggers
{
    private static readonly HashSet<FeatureTrigger> Raised = new HashSet<FeatureTrigger>
    {
        FeatureTrigger.CheckResolved,
        FeatureTrigger.ExpeditionStarted,
        FeatureTrigger.LocationEntered,
        FeatureTrigger.RoadEncounterDetected,
        FeatureTrigger.CampNight,
        FeatureTrigger.BattleStarted,
        FeatureTrigger.BattleEnded,
        FeatureTrigger.ReturnedHome
    };

    // Сообщает ли игра об этом событии сейчас.
    public static bool IsRaised(FeatureTrigger trigger)
    {
        return Raised.Contains(trigger);
    }

    public static string Label(FeatureTrigger trigger)
    {
        switch (trigger)
        {
            case FeatureTrigger.CheckResolved: return "проверка решена";
            case FeatureTrigger.ExpeditionStarted: return "выход в поход";
            case FeatureTrigger.LocationEntered: return "прибытие в место";
            case FeatureTrigger.RoadEncounterDetected: return "дорожная встреча замечена";
            case FeatureTrigger.CampNight: return "ночёвка";
            case FeatureTrigger.BattleStarted: return "начало боя";
            case FeatureTrigger.BattleEnded: return "итог боя";
            case FeatureTrigger.ReturnedHome: return "возвращение домой";
            case FeatureTrigger.RoundStarted: return "начало раунда";
            case FeatureTrigger.AttackMade: return "атака";
            case FeatureTrigger.DamageTaken: return "получен урон";
            case FeatureTrigger.AllyDamaged: return "союзник получил урон";
            case FeatureTrigger.HitPointsBelowThreshold: return "здоровье ниже порога";
            case FeatureTrigger.Moved: return "перемещение";
            case FeatureTrigger.EnemyEntersAdjacentHex: return "враг вошёл в соседний гекс";
            case FeatureTrigger.AllyAttacked: return "атака по союзнику";
            case FeatureTrigger.WeaponReloaded: return "перезарядка";
            case FeatureTrigger.ArrowsSpent: return "расход стрел";
            case FeatureTrigger.ItemDamaged: return "вещь повреждена";
            default: return trigger.ToString();
        }
    }
}

// Одно событие. Заполняются только поля, которые к нему относятся.
public sealed class FeatureEvent
{
    public FeatureTrigger Trigger;
    public GameState State;

    // Кого касается; пусто — всех людей похода (дома — Командира и бойцов).
    public string PersonId = string.Empty;

    // Устойчивый ключ: событие с тем же ключом повторно не обрабатывается
    // (загрузка, повтор текста, повторный вызов).
    public string EventKey = string.Empty;

    public string CheckId = string.Empty;
    public string CompetencyId = string.Empty;
    public NarrativeCheckResult CheckResult;
    public string LocationId = string.Empty;
    public string BattleId = string.Empty;
    public CampaignBattleResult BattleResult;

    public readonly List<FeatureActivation> Activations = new List<FeatureActivation>();
}

// Сработавшая особенность: кто, какая и что дала словами — для показа
// игроку (12Е-7) и Хроники.
[Serializable]
public sealed class FeatureActivation
{
    public string PersonId = string.Empty;
    public string FeatureId = string.Empty;
    public FeatureTrigger Trigger;
    public string Text = string.Empty;
}

// Обработчик особенности: владелец и его ранг.
public delegate void FeatureHandler(FeatureEvent featureEvent, string ownerId, int rank);

// Сколько раз особенность уже сработала в своём пределе.
[Serializable]
public sealed class FeatureUseData
{
    public string PersonId = string.Empty;
    public string FeatureId = string.Empty;
    public string ScopeKey = string.Empty;
    public int Count;
}

public static class FeatureDispatcher
{
    public const int KeptEventKeys = 400;
    public const int KeptActivations = 30;

    private sealed class Registration
    {
        public string FeatureId;
        public FeatureTrigger Trigger;
        public FeatureHandler Handler;
    }

    private static readonly List<Registration> Handlers = new List<Registration>();

    static FeatureDispatcher()
    {
        ProgressionFeatureImplementations.EnsureRegistered();
    }

    // Код особенности: подписка на событие. Особенность становится
    // «реализованной» и может быть активной.
    public static void Register(string featureId, FeatureTrigger trigger, FeatureHandler handler)
    {
        if (string.IsNullOrWhiteSpace(featureId))
            throw new ArgumentException("Нужен ID особенности.", nameof(featureId));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        Handlers.RemoveAll(existing => existing.FeatureId == featureId && existing.Trigger == trigger);
        Handlers.Add(new Registration { FeatureId = featureId, Trigger = trigger, Handler = handler });
        ProgressionFeatureImplementations.Register(featureId);
    }

    public static void Unregister(string featureId)
    {
        Handlers.RemoveAll(existing => existing.FeatureId == featureId);
        ProgressionFeatureImplementations.Unregister(featureId);
    }

    public static bool HasHandler(string featureId, FeatureTrigger trigger)
    {
        return Handlers.Exists(existing => existing.FeatureId == featureId && existing.Trigger == trigger);
    }

    // Сообщить о событии. Возвращает сработавшие особенности.
    public static List<FeatureActivation> Raise(FeatureEvent featureEvent)
    {
        if (featureEvent == null)
            throw new ArgumentNullException(nameof(featureEvent));
        GameState state = featureEvent.State;
        if (state == null)
            return featureEvent.Activations;

        ProgressionStateData progression = CharacterProgressionService.EnsureState(state);
        if (!string.IsNullOrWhiteSpace(featureEvent.EventKey))
        {
            if (progression.HandledFeatureEvents.Contains(featureEvent.EventKey))
                return featureEvent.Activations;
            progression.HandledFeatureEvents.Add(featureEvent.EventKey);
            if (progression.HandledFeatureEvents.Count > KeptEventKeys)
                progression.HandledFeatureEvents.RemoveRange(0, progression.HandledFeatureEvents.Count - KeptEventKeys);
        }

        // Эпохи похода и ночёвки — основа пределов «1/поход» и «до лагеря».
        if (featureEvent.Trigger == FeatureTrigger.ExpeditionStarted)
            progression.ExpeditionEpoch++;
        else if (featureEvent.Trigger == FeatureTrigger.CampNight)
            progression.CampEpoch++;

        List<Registration> handlers = Handlers.FindAll(existing => existing.Trigger == featureEvent.Trigger);
        if (handlers.Count == 0)
            return featureEvent.Activations;

        foreach (string personId in Participants(featureEvent))
        {
            foreach (OwnedFeature owned in CharacterFeatureService.GetFeatures(state, personId))
            {
                foreach (Registration registration in handlers)
                {
                    if (registration.FeatureId == owned.FeatureId && !ProgressionFeatureImplementations.IsStub(owned.FeatureId))
                        registration.Handler(featureEvent, personId, owned.Rank);
                }
            }
        }

        foreach (FeatureActivation activation in featureEvent.Activations)
            progression.RecentFeatureActivations.Add(activation);
        if (progression.RecentFeatureActivations.Count > KeptActivations)
            progression.RecentFeatureActivations.RemoveRange(0, progression.RecentFeatureActivations.Count - KeptActivations);
        return featureEvent.Activations;
    }

    // Обработчик сообщает, что особенность сработала.
    public static FeatureActivation Activate(FeatureEvent featureEvent, string personId, string featureId, string text)
    {
        FeatureActivation activation = new FeatureActivation
        {
            PersonId = personId ?? string.Empty,
            FeatureId = featureId ?? string.Empty,
            Trigger = featureEvent.Trigger,
            Text = text ?? string.Empty
        };
        featureEvent.Activations.Add(activation);
        return activation;
    }

    private static List<string> Participants(FeatureEvent featureEvent)
    {
        if (!string.IsNullOrWhiteSpace(featureEvent.PersonId))
            return new List<string> { featureEvent.PersonId };

        GameState state = featureEvent.State;
        if (state.HasActiveExpedition || featureEvent.Trigger == FeatureTrigger.ReturnedHome)
            return CharacterProgressionService.PartyPersonIds(state);

        List<string> people = new List<string>();
        CommanderData commander = state.GetSelectedCommander();
        if (commander != null)
            people.Add(commander.Id);
        if (state.Fighters != null)
        {
            foreach (FighterData fighter in state.Fighters)
            {
                if (fighter != null && CharacterProgressionService.IsProgressing(state, fighter.Id))
                    people.Add(fighter.Id);
            }
        }
        return people;
    }
}

// Пределы срабатывания вместо отдельного ресурса (каталог §1 п. 8).
// «1/поход» и «до лагеря» считаются по эпохам в сохранении, поэтому
// загрузка их не сбрасывает; бой, встреча, сцена, разговор, сделка, ход и
// раунд — по ключу, который передаёт вызывающий (ID боя, встречи…).
public static class FeatureLimits
{
    public static string ScopeKey(GameState state, FeatureLimit limit, string contextKey = null)
    {
        ProgressionStateData progression = CharacterProgressionService.EnsureState(state);
        string epoch = progression.ExpeditionEpoch.ToString(CultureInfo.InvariantCulture);
        switch (limit)
        {
            case FeatureLimit.None:
                return string.Empty;
            case FeatureLimit.Expedition:
                return "expedition:" + epoch;
            case FeatureLimit.UntilCamp:
            case FeatureLimit.Night:
                return "camp:" + epoch + ":" + progression.CampEpoch.ToString(CultureInfo.InvariantCulture);
            default:
                return string.IsNullOrWhiteSpace(contextKey) ? null : limit + ":" + contextKey;
        }
    }

    public static int Used(GameState state, string personId, string featureId, FeatureLimit limit, string contextKey = null)
    {
        string scope = ScopeKey(state, limit, contextKey);
        if (string.IsNullOrEmpty(scope))
            return 0;
        FeatureUseData use = Find(state, personId, featureId, scope);
        return use != null ? use.Count : 0;
    }

    // Можно ли сработать ещё раз: предел берётся из карточки каталога.
    // Без ключа для «1/бой», «1/встречу»… — нельзя (непонятно, чей бой).
    public static bool CanUse(GameState state, string personId, string featureId, string contextKey = null, int timesPerScope = 1)
    {
        FeatureLimit limit = LimitOf(featureId);
        if (limit == FeatureLimit.None)
            return true;
        string scope = ScopeKey(state, limit, contextKey);
        if (scope == null)
            return false;
        FeatureUseData use = Find(state, personId, featureId, scope);
        return use == null || use.Count < Math.Max(1, timesPerScope);
    }

    // Отметить срабатывание. False — предел исчерпан, ничего не записано.
    public static bool TryConsume(GameState state, string personId, string featureId, string contextKey = null, int timesPerScope = 1)
    {
        if (!CanUse(state, personId, featureId, contextKey, timesPerScope))
            return false;
        FeatureLimit limit = LimitOf(featureId);
        if (limit == FeatureLimit.None)
            return true;

        ProgressionStateData progression = CharacterProgressionService.EnsureState(state);
        string scope = ScopeKey(state, limit, contextKey);
        // Отметки прошлых походов и ночёвок больше не нужны.
        if (limit == FeatureLimit.Expedition || limit == FeatureLimit.UntilCamp || limit == FeatureLimit.Night)
        {
            progression.FeatureUses.RemoveAll(use => use != null && use.PersonId == personId && use.FeatureId == featureId &&
                                                    use.ScopeKey != scope);
        }
        FeatureUseData existing = Find(state, personId, featureId, scope);
        if (existing == null)
        {
            existing = new FeatureUseData { PersonId = personId, FeatureId = featureId, ScopeKey = scope };
            progression.FeatureUses.Add(existing);
        }
        existing.Count++;
        return true;
    }

    public static FeatureLimit LimitOf(string featureId)
    {
        TraitCatalogEntry entry = ProgressionCatalog.Current.FindTrait(featureId);
        return entry != null ? entry.Limit : FeatureLimit.None;
    }

    private static FeatureUseData Find(GameState state, string personId, string featureId, string scope)
    {
        ProgressionStateData progression = CharacterProgressionService.EnsureState(state);
        return progression.FeatureUses.Find(use => use != null && use.PersonId == personId &&
                                                  use.FeatureId == featureId && use.ScopeKey == scope);
    }
}

// Переброс (каталог §0): перебросить один d6 из 2d6 активной проверки,
// новый результат обязателен. Перебрасывается меньший кубик. Кубик
// детерминирован от seed мира, ID проверки, номера попытки и источника
// переброса — загрузка сохранения не даёт другого результата. Один
// результат перебрасывается один раз; практику переброс не даёт.
public static class NarrativeCheckReroll
{
    public static bool CanReroll(NarrativeStateData state, string checkId)
    {
        NarrativeCheckResult latest = state?.FindHistory(checkId)?.LatestResult;
        return latest != null && latest.HasDice && !latest.IsForcedByPreview && string.IsNullOrEmpty(latest.RerolledBy);
    }

    public static NarrativeCheckResult Reroll(NarrativeStateData state, int worldSeed, string checkId, string sourceId)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("Нужен источник переброса.", nameof(sourceId));
        if (!CanReroll(state, checkId))
            return null;

        NarrativeCheckHistoryEntry history = state.FindHistory(checkId);
        NarrativeCheckResult previous = history.LatestResult;
        NarrativeDeterministicRandom.RollTwoDice(worldSeed, checkId + "|reroll|" + sourceId, previous.AttemptNumber, out int newDie, out _);

        bool rerollFirst = previous.DieOne <= previous.DieTwo;
        int dieOne = rerollFirst ? newDie : previous.DieOne;
        int dieTwo = rerollFirst ? previous.DieTwo : newDie;
        int bonus = previous.Total - previous.DieOne - previous.DieTwo;
        int total = dieOne + dieTwo + bonus;
        NarrativeCheckResult result = new NarrativeCheckResult(
            previous.CheckId,
            previous.AttemptNumber,
            total >= previous.Difficulty,
            dieOne,
            dieTwo,
            previous.QualityValue,
            previous.CompetencyValue,
            previous.RawContextModifier,
            previous.AppliedContextModifier,
            total,
            previous.Difficulty,
            appliedModifiers: previous.AppliedModifiers)
        {
            RerolledBy = sourceId,
            RerolledDieBefore = rerollFirst ? previous.DieOne : previous.DieTwo
        };

        history.Attempts[history.Attempts.Count - 1] = result;
        if (history.Kind == NarrativeCheckKind.ActiveReturnable)
            history.IsLocked = !result.Success;
        return result;
    }
}
