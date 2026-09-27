using System;
using System.Collections.Generic;
using System.Globalization;

// 12Е-5: первая партия особенностей вне боя (каталог §6.1) на существующих
// системах. Варианты утверждены автором 27.09.2026: Счастливчик — переброс
// 1/поход; Зацепка — +1 к следующей проверке; Почти получилось — цена
// первой ошибки остаётся; Полевой лекарь — до 1/4 здоровья, 1/ночёвку.
// Все формулировки и числа каталога — [РАБОЧЕЕ].
//
// Проверки 2d6 в игре пока проходит только Командир, поэтому особенности,
// которые работают через проверки, бойцу в выбор не предлагаются.

public enum FeaturePendingKind
{
    // Переброс, если следующая подходящая проверка провалится.
    Reroll,
    // Прибавка к следующей подходящей проверке.
    Bonus,
    // Отпереть возвратную проверку, когда появится новое сведение.
    UnlockOnNewFact
}

// Отложенный эффект особенности. Пустые поля — «любая»: CompetencyId —
// любая компетенция, SceneId — любая сцена, LocationId — любое место.
[Serializable]
public sealed class FeaturePendingData
{
    public string PersonId = string.Empty;
    public string FeatureId = string.Empty;
    public FeaturePendingKind Kind;
    public string CompetencyId = string.Empty;
    public int Value;
    public string SceneId = string.Empty;
    public string LocationId = string.Empty;
    public string CheckId = string.Empty;
}

public static class FeatureIds
{
    public const string Lucky = "schastlivchik";
    public const string AlmostThere = "pochti_poluchilos";
    public const string SecondVersion = "vtoraya_versiya";
    public const string FirstGlance = "pervyy_vzglyad";
    public const string Lead = "zatsepka";
    public const string RainyDayStock = "zapas_na_chyornyy_den";
    public const string SpareRoute = "zapasnoy_put";
    public const string FieldCamp = "polevoy_nochleg";
    public const string FieldMedic = "polevoy_lekar";
    public const string Mentor = "nastavnik";
}

// Сцена — один запуск диалога или встречи: «в той же сцене» для Зацепки и
// «Почти получилось». Отложенные эффекты прошлой сцены с новой пропадают.
public static class FeatureScenes
{
    public static string Begin(GameState state, string dialogueId)
    {
        if (state == null)
            return dialogueId ?? string.Empty;
        ProgressionStateData progression = CharacterProgressionService.EnsureState(state);
        progression.SceneCounter++;
        progression.FeaturePendings.RemoveAll(pending => pending != null && !string.IsNullOrEmpty(pending.SceneId));
        return (dialogueId ?? string.Empty) + "#" + progression.SceneCounter.ToString(CultureInfo.InvariantCulture);
    }
}

// Особенности на проверках: прибавки видны в разборе проверки (как
// контекстные модификаторы), отложенные перебросы и события — после броска.
public static class FeatureCheckHooks
{
    private const string SourcePrefix = "feature:";

    // Прибавки от особенностей к активной проверке Командира.
    public static void CollectModifiers(NarrativeCheckSpec spec, NarrativeEvaluationContext context, List<NarrativeContextModifierRule> rules)
    {
        if (spec == null || context?.GameState == null || spec.Kind == NarrativeCheckKind.Passive)
            return;
        string personId = CommanderIdFor(context);
        if (personId == null)
            return;
        foreach (FeaturePendingData pending in CharacterProgressionService.EnsureState(context.GameState).FeaturePendings)
        {
            if (pending == null || pending.Kind != FeaturePendingKind.Bonus || !Matches(pending, personId, spec, context))
                continue;
            rules.Add(new NarrativeContextModifierRule
            {
                SourceId = SourcePrefix + pending.FeatureId,
                Label = FeatureName(pending.FeatureId),
                Value = pending.Value,
                Condition = null
            });
        }
    }

    // После броска активной проверки: израсходовать отложенные прибавки,
    // применить отложенные перебросы, сообщить особенностям о проверке.
    // Возвращает окончательный результат (после перебросов).
    public static NarrativeCheckResult AfterResolved(NarrativeCheckSpec spec, NarrativeEvaluationContext context, NarrativeCheckResult result)
    {
        GameState state = context?.GameState;
        string personId = CommanderIdFor(context);
        if (state == null || personId == null || spec == null)
            return result;

        List<FeatureActivation> activations = new List<FeatureActivation>();
        ProgressionStateData progression = CharacterProgressionService.EnsureState(state);
        foreach (FeaturePendingData pending in progression.FeaturePendings.ToArray())
        {
            if (pending == null || pending.Kind == FeaturePendingKind.UnlockOnNewFact || !Matches(pending, personId, spec, context))
                continue;
            progression.FeaturePendings.Remove(pending);
            if (pending.Kind != FeaturePendingKind.Reroll)
                continue;
            NarrativeCheckResult current = context.State.FindHistory(spec.CheckId)?.LatestResult;
            if (current == null || current.Success || !NarrativeCheckReroll.CanReroll(context.State, spec.CheckId))
                continue;
            NarrativeCheckResult rerolled = NarrativeCheckReroll.Reroll(context.State, context.WorldSeed, spec.CheckId, SourcePrefix + pending.FeatureId);
            if (rerolled != null)
                activations.Add(RerollActivation(personId, pending.FeatureId, current, rerolled));
        }
        foreach (FeatureActivation activation in activations)
            FeatureDispatcher.Record(state, activation);

        NarrativeCheckResult latest = context.State.FindHistory(spec.CheckId)?.LatestResult ?? result;
        FeatureDispatcher.Raise(new FeatureEvent
        {
            Trigger = FeatureTrigger.CheckResolved,
            State = state,
            PersonId = personId,
            EventKey = "check:" + spec.CheckId + ":" + latest.AttemptNumber,
            CheckId = spec.CheckId,
            CompetencyId = spec.CompetencyId ?? string.Empty,
            CheckResult = latest,
            SceneId = context.SceneId ?? string.Empty,
            WorldSeed = context.WorldSeed,
            Narrative = context.State
        });
        return context.State.FindHistory(spec.CheckId)?.LatestResult ?? latest;
    }

    public static FeatureActivation RerollActivation(string personId, string featureId, NarrativeCheckResult before, NarrativeCheckResult after)
    {
        return new FeatureActivation
        {
            PersonId = personId,
            FeatureId = featureId,
            Trigger = FeatureTrigger.CheckResolved,
            Text = FeatureName(featureId) + ": переброс, кубик " + after.RerolledDieBefore + " → " +
                   (after.DieOne + after.DieTwo - (before.DieOne + before.DieTwo) + after.RerolledDieBefore) +
                   (after.Success ? " — теперь получилось." : " — всё равно не вышло.")
        };
    }

    private static bool Matches(FeaturePendingData pending, string personId, NarrativeCheckSpec spec, NarrativeEvaluationContext context)
    {
        if (pending.PersonId != personId)
            return false;
        if (!string.IsNullOrEmpty(pending.CompetencyId) && pending.CompetencyId != spec.CompetencyId)
            return false;
        if (!string.IsNullOrEmpty(pending.SceneId) && pending.SceneId != context.SceneId)
            return false;
        if (!string.IsNullOrEmpty(pending.LocationId))
        {
            ExpeditionData expedition = context.GameState.ActiveExpedition;
            if (expedition == null || !expedition.IsActive || expedition.Phase != CommanderState.AtLocation ||
                expedition.LocationId != pending.LocationId)
                return false;
        }
        return true;
    }

    public static string CommanderIdFor(NarrativeEvaluationContext context)
    {
        CommanderData commander = context?.GameState?.GetSelectedCommander();
        return commander != null && ReferenceEquals(commander.HeroProfile, context.Hero) ? commander.Id : null;
    }

    public static string FeatureName(string featureId)
    {
        TraitCatalogEntry entry = ProgressionCatalog.Current.FindTrait(featureId);
        return entry != null && !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name : featureId;
    }
}

// Особенности, которые меняют числа похода и лагеря.
public static class FeatureEffects
{
    // Запас на чёрный день: первая потеря Припасов от события за поход −1.
    public static int ReduceEventSupplyLoss(GameState state, int loss, List<string> notes)
    {
        if (state == null || loss <= 0)
            return loss;
        string owner = PartyOwner(state, FeatureIds.RainyDayStock);
        if (owner == null || !FeatureLimits.TryConsume(state, owner, FeatureIds.RainyDayStock))
            return loss;
        Note(state, notes, owner, FeatureIds.RainyDayStock, FeatureTrigger.RoadEncounterDetected,
            "припрятанный запас — потеря на 1 меньше.");
        return loss - 1;
    }

    // Запасной путь I: первая задержка от небоевого дорожного события за поход −1 ч.
    public static int ReduceRoadDelay(GameState state, int hours, List<string> notes)
    {
        if (state == null || hours <= 0)
            return hours;
        string owner = PartyOwner(state, FeatureIds.SpareRoute);
        if (owner == null || !FeatureLimits.TryConsume(state, owner, FeatureIds.SpareRoute))
            return hours;
        Note(state, notes, owner, FeatureIds.SpareRoute, FeatureTrigger.RoadEncounterDetected,
            "нашёлся обход — задержка на час меньше.");
        return hours - 1;
    }

    // Полевой ночлег: лишнее дело на ночь (первое обустройство не тратит выбор).
    public static int ExtraCampActions(GameState state)
    {
        return PartyOwner(state, FeatureIds.FieldCamp) != null ? 1 : 0;
    }

    // Кто в походе владеет особенностью (первый по порядку отряда).
    public static string PartyOwner(GameState state, string featureId)
    {
        if (state == null || !state.HasActiveExpedition)
            return null;
        foreach (string personId in CharacterProgressionService.PartyPersonIds(state))
        {
            if (CharacterFeatureService.Has(state, personId, featureId))
                return personId;
        }
        return null;
    }

    private static void Note(GameState state, List<string> notes, string owner, string featureId, FeatureTrigger trigger, string text)
    {
        string line = CharacterProgressionService.DisplayName(state, owner) + " («" + FeatureCheckHooks.FeatureName(featureId) + "»): " + text;
        notes?.Add(line);
        FeatureDispatcher.Record(state, new FeatureActivation { PersonId = owner, FeatureId = featureId, Trigger = trigger, Text = line });
    }
}

public static class FeatureImplementationsFirstBatch
{
    public static void RegisterAll()
    {
        // Порядок: сначала переброс Счастливчика, потом те, кто смотрит на
        // окончательный итог проверки.
        FeatureDispatcher.Register(FeatureIds.Lucky, FeatureTrigger.CheckResolved, OnLucky, order: 0);
        FeatureDispatcher.Register(FeatureIds.AlmostThere, FeatureTrigger.CheckResolved, OnAlmostThere, order: 10);
        FeatureDispatcher.Register(FeatureIds.SecondVersion, FeatureTrigger.CheckResolved, OnSecondVersionFailed, order: 10);
        FeatureDispatcher.Register(FeatureIds.SecondVersion, FeatureTrigger.KnowledgeGained, OnSecondVersionFact);
        FeatureDispatcher.Register(FeatureIds.FirstGlance, FeatureTrigger.LocationEntered, OnFirstGlance);
        FeatureDispatcher.Register(FeatureIds.Lead, FeatureTrigger.CheckResolved, OnLead, order: 10);
        FeatureDispatcher.Register(FeatureIds.FieldMedic, FeatureTrigger.CampNight, OnFieldMedic);
        FeatureDispatcher.Register(FeatureIds.Mentor, FeatureTrigger.CampNight, OnMentor);
        // Эти работают через FeatureEffects (изменение чисел похода и лагеря).
        ProgressionFeatureImplementations.Register(FeatureIds.RainyDayStock);
        ProgressionFeatureImplementations.Register(FeatureIds.SpareRoute);
        ProgressionFeatureImplementations.Register(FeatureIds.FieldCamp);

        foreach (string id in new[] { FeatureIds.Lucky, FeatureIds.AlmostThere, FeatureIds.SecondVersion, FeatureIds.FirstGlance, FeatureIds.Lead })
            ProgressionFeatureImplementations.SetCommanderChecksOnly(id);
        // Реализован только первый ранг; следующие не предлагаются.
        ProgressionFeatureImplementations.SetImplementedRanks(FeatureIds.SpareRoute, 1);
        ProgressionFeatureImplementations.SetImplementedRanks(FeatureIds.FieldMedic, 1);
        ProgressionFeatureImplementations.SetImplementedRanks(FeatureIds.Mentor, 1);
    }

    // Н-01 Счастливчик: переброс одного d6 проваленной проверки, 1/поход.
    private static void OnLucky(FeatureEvent featureEvent, string owner, int rank)
    {
        NarrativeCheckResult result = featureEvent.CheckResult;
        if (result == null || result.Success || featureEvent.Narrative == null ||
            !NarrativeCheckReroll.CanReroll(featureEvent.Narrative, featureEvent.CheckId) ||
            !FeatureLimits.TryConsume(featureEvent.State, owner, FeatureIds.Lucky))
            return;
        NarrativeCheckResult rerolled = NarrativeCheckReroll.Reroll(featureEvent.Narrative, featureEvent.WorldSeed, featureEvent.CheckId, "feature:" + FeatureIds.Lucky);
        if (rerolled == null)
            return;
        featureEvent.CheckResult = rerolled;
        FeatureActivation activation = FeatureCheckHooks.RerollActivation(owner, FeatureIds.Lucky, result, rerolled);
        featureEvent.Activations.Add(activation);
    }

    // Н-03 Почти получилось: провал на 1–2 — следующая проверка в этой сцене
    // получает переброс. Цена первой ошибки остаётся.
    private static void OnAlmostThere(FeatureEvent featureEvent, string owner, int rank)
    {
        NarrativeCheckResult result = featureEvent.CheckResult;
        if (result == null || result.Success || string.IsNullOrEmpty(featureEvent.SceneId))
            return;
        int miss = result.Difficulty - result.Total;
        if (miss < 1 || miss > 2)
            return;
        ReplacePending(featureEvent.State, new FeaturePendingData
        {
            PersonId = owner,
            FeatureId = FeatureIds.AlmostThere,
            Kind = FeaturePendingKind.Reroll,
            SceneId = featureEvent.SceneId
        });
        FeatureDispatcher.Activate(featureEvent, owner, FeatureIds.AlmostThere,
            "Почти получилось: не хватило " + miss + " — следующая попытка в этом разговоре получит переброс.");
    }

    // Н-07 Вторая версия: проваленная возвратная проверка откроется снова,
    // когда появится новое сведение.
    private static void OnSecondVersionFailed(FeatureEvent featureEvent, string owner, int rank)
    {
        NarrativeCheckResult result = featureEvent.CheckResult;
        NarrativeCheckHistoryEntry history = featureEvent.Narrative?.FindHistory(featureEvent.CheckId);
        if (result == null || result.Success || history == null || history.Kind != NarrativeCheckKind.ActiveReturnable || !history.IsLocked)
            return;
        ReplacePending(featureEvent.State, new FeaturePendingData
        {
            PersonId = owner,
            FeatureId = FeatureIds.SecondVersion,
            Kind = FeaturePendingKind.UnlockOnNewFact,
            CheckId = featureEvent.CheckId
        });
        FeatureDispatcher.Activate(featureEvent, owner, FeatureIds.SecondVersion,
            "Вторая версия: к этому можно будет вернуться, когда появится новое сведение.");
    }

    private static void OnSecondVersionFact(FeatureEvent featureEvent, string owner, int rank)
    {
        ProgressionStateData progression = CharacterProgressionService.EnsureState(featureEvent.State);
        NarrativeStateData narrative = featureEvent.Narrative ?? featureEvent.State.Narrative;
        foreach (FeaturePendingData pending in progression.FeaturePendings.ToArray())
        {
            if (pending == null || pending.PersonId != owner || pending.FeatureId != FeatureIds.SecondVersion ||
                pending.Kind != FeaturePendingKind.UnlockOnNewFact)
                continue;
            progression.FeaturePendings.Remove(pending);
            if (narrative == null || !narrative.IsCheckLocked(pending.CheckId))
                continue;
            narrative.UnlockCheck(pending.CheckId);
            FeatureDispatcher.Activate(featureEvent, owner, FeatureIds.SecondVersion,
                "Вторая версия: новое сведение — к проваленной попытке можно вернуться.");
        }
    }

    // Н-08 Первый взгляд: первая Наблюдательность после прибытия в место —
    // переброс при провале.
    private static void OnFirstGlance(FeatureEvent featureEvent, string owner, int rank)
    {
        if (string.IsNullOrEmpty(featureEvent.LocationId))
            return;
        ProgressionStateData progression = CharacterProgressionService.EnsureState(featureEvent.State);
        progression.FeaturePendings.RemoveAll(pending => pending != null && pending.PersonId == owner && pending.FeatureId == FeatureIds.FirstGlance);
        progression.FeaturePendings.Add(new FeaturePendingData
        {
            PersonId = owner,
            FeatureId = FeatureIds.FirstGlance,
            Kind = FeaturePendingKind.Reroll,
            CompetencyId = NarrativeCompetencyIds.Observation,
            LocationId = featureEvent.LocationId
        });
    }

    // Н-09 Зацепка: после успешной Наблюдательности следующее Расследование
    // в той же сцене +1.
    private static void OnLead(FeatureEvent featureEvent, string owner, int rank)
    {
        NarrativeCheckResult result = featureEvent.CheckResult;
        if (result == null || !result.Success || featureEvent.CompetencyId != NarrativeCompetencyIds.Observation ||
            string.IsNullOrEmpty(featureEvent.SceneId))
            return;
        ReplacePending(featureEvent.State, new FeaturePendingData
        {
            PersonId = owner,
            FeatureId = FeatureIds.Lead,
            Kind = FeaturePendingKind.Bonus,
            CompetencyId = NarrativeCompetencyIds.Investigation,
            Value = 1,
            SceneId = featureEvent.SceneId
        });
        FeatureDispatcher.Activate(featureEvent, owner, FeatureIds.Lead,
            "Зацепка: замеченное пригодится — следующее Расследование в этом разговоре +1.");
    }

    // Н-70 Полевой лекарь I: 1/ночёвку самый тяжело раненый в походе
    // восстанавливает до 1/4 здоровья.
    private static void OnFieldMedic(FeatureEvent featureEvent, string owner, int rank)
    {
        GameState state = featureEvent.State;
        ResidentState worst = null;
        double worstShare = 1.0;
        foreach (string personId in CampRest.PartyIds(state))
        {
            ResidentState resident = HomePeopleService.Find(state, personId);
            if (resident == null || !resident.IsAlive || !resident.HasCombatState || resident.MaxHitPoints <= 0 ||
                resident.CurrentHitPoints >= resident.MaxHitPoints)
                continue;
            double share = (double)resident.CurrentHitPoints / resident.MaxHitPoints;
            if (worst == null || share < worstShare)
            {
                worst = resident;
                worstShare = share;
            }
        }
        if (worst == null || !FeatureLimits.TryConsume(state, owner, FeatureIds.FieldMedic))
            return;
        int heal = Math.Min(worst.MaxHitPoints - worst.CurrentHitPoints, Math.Max(1, (worst.MaxHitPoints + 3) / 4));
        worst.CurrentHitPoints += heal;
        FeatureDispatcher.Activate(featureEvent, owner, FeatureIds.FieldMedic,
            CharacterProgressionService.DisplayName(state, owner) + " («Полевой лекарь») выхаживает " + worst.DisplayName + ": +" + heal + " здоровья");
    }

    // Н-81 Наставник I: кто с последнего привала применял ту же компетенцию,
    // что и наставник (у наставника 4+, у ученика ≤2; бой считается),
    // получает +1 практики — не больше раза за ночь и не выше потолка.
    private static void OnMentor(FeatureEvent featureEvent, string owner, int rank)
    {
        GameState state = featureEvent.State;
        ProgressionStateData progression = CharacterProgressionService.EnsureState(state);
        string since = ApplicationEpoch(progression.ExpeditionEpoch, progression.CampEpoch - 1);
        List<string> mentorCompetencies = AppliedSince(progression, since, owner);
        foreach (string competencyId in mentorCompetencies)
        {
            if (CharacterProgressionService.GetCompetencyRank(state, owner, competencyId) < 4)
                continue;
            foreach (string studentId in CharacterProgressionService.PartyPersonIds(state))
            {
                if (studentId == owner || !AppliedSince(progression, since, studentId).Contains(competencyId) ||
                    CharacterProgressionService.GetCompetencyRank(state, studentId, competencyId) > 2 ||
                    !FeatureLimits.TryConsume(state, studentId, FeatureIds.Mentor))
                    continue;
                CharacterProgressionService.AddPractice(state, studentId, competencyId, 1, countsAsApplication: false);
                FeatureDispatcher.Activate(featureEvent, owner, FeatureIds.Mentor,
                    CharacterProgressionService.DisplayName(state, owner) + " («Наставник») показал " +
                    CharacterProgressionService.DisplayName(state, studentId) + ", как надо: " +
                    NarrativeCompetencyLabels.GetLabel(competencyId) + " +1 практики");
            }
        }
    }

    // Применение компетенции с последнего привала: «эпоха похода:эпоха ночёвки|человек|компетенция».
    public static void RecordApplication(GameState state, string personId, string competencyId)
    {
        ProgressionStateData progression = CharacterProgressionService.EnsureState(state);
        string epoch = ApplicationEpoch(progression.ExpeditionEpoch, progression.CampEpoch);
        string key = epoch + "|" + personId + "|" + competencyId;
        progression.RecentApplications.RemoveAll(entry => entry == null ||
                                                         (!entry.StartsWith(epoch + "|", StringComparison.Ordinal) &&
                                                          !entry.StartsWith(ApplicationEpoch(progression.ExpeditionEpoch, progression.CampEpoch - 1) + "|", StringComparison.Ordinal)));
        if (!progression.RecentApplications.Contains(key))
            progression.RecentApplications.Add(key);
    }

    private static string ApplicationEpoch(int expeditionEpoch, int campEpoch)
    {
        return expeditionEpoch.ToString(CultureInfo.InvariantCulture) + ":" + campEpoch.ToString(CultureInfo.InvariantCulture);
    }

    private static List<string> AppliedSince(ProgressionStateData progression, string epoch, string personId)
    {
        List<string> competencies = new List<string>();
        string prefix = epoch + "|" + personId + "|";
        foreach (string entry in progression.RecentApplications)
        {
            if (entry != null && entry.StartsWith(prefix, StringComparison.Ordinal))
                competencies.Add(entry.Substring(prefix.Length));
        }
        return competencies;
    }

    private static void ReplacePending(GameState state, FeaturePendingData pending)
    {
        ProgressionStateData progression = CharacterProgressionService.EnsureState(state);
        progression.FeaturePendings.RemoveAll(existing => existing != null && existing.PersonId == pending.PersonId &&
                                                          existing.FeatureId == pending.FeatureId &&
                                                          existing.Kind == pending.Kind && existing.CheckId == pending.CheckId);
        progression.FeaturePendings.Add(pending);
    }
}
