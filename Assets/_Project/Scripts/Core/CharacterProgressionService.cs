using System;
using System.Collections.Generic;
using System.Linq;

// Команды единой прогрессии (канон v1.48 §27). Прогрессируют Командир и
// постоянные бойцы (GameState.Fighters); свита и прочие жители — нет.
public static class CharacterProgressionService
{
    private const string SourceSeparator = "|";

    public static ProgressionStateData EnsureState(GameState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (state.Progression == null)
            state.Progression = new ProgressionStateData();
        ProgressionStateData progression = state.Progression;
        if (progression.People == null)
            progression.People = new List<PersonProgressionData>();
        if (progression.AppliedSources == null)
            progression.AppliedSources = new List<string>();
        if (progression.Repeats == null)
            progression.Repeats = new List<ProgressionRepeatData>();

        CommanderData hero = state.GetSelectedCommander();
        if (hero != null)
            Get(state, hero.Id);
        if (state.Fighters != null)
        {
            foreach (FighterData fighter in state.Fighters)
            {
                if (fighter != null)
                    Get(state, fighter.Id);
            }
        }
        return progression;
    }

    public static bool IsProgressing(GameState state, string personId)
    {
        if (state == null || string.IsNullOrEmpty(personId))
            return false;
        CommanderData hero = state.GetSelectedCommander();
        return (hero != null && hero.Id == personId) || FindFighter(state, personId) != null;
    }

    // Запись человека; создаётся при первом обращении с уровнем, с которым
    // он пришёл (FighterData.Level — биография, §27.8).
    public static PersonProgressionData Get(GameState state, string personId)
    {
        if (!IsProgressing(state, personId))
            return null;
        if (state.Progression == null)
            state.Progression = new ProgressionStateData();
        if (state.Progression.People == null)
            state.Progression.People = new List<PersonProgressionData>();

        foreach (PersonProgressionData existing in state.Progression.People)
        {
            if (existing != null && existing.PersonId == personId)
                return existing;
        }

        FighterData data = FindPersonData(state, personId);
        ProgressionProfile profile = ProfileFor(state, personId);
        int startingLevel = Math.Max(1, Math.Min(CharacterProgression.MaxLevel,
            Math.Max(data != null ? data.Level : 1, profile.StartingLevel)));
        PersonProgressionData record = new PersonProgressionData
        {
            PersonId = personId,
            Level = startingLevel,
            StartingLevel = startingLevel,
            Experience = profile.TotalExperienceForLevel(startingLevel)
        };
        state.Progression.People.Add(record);
        GrantStartingCompetencies(state, record, profile);
        SyncDisplayedLevel(state, record);
        return record;
    }

    // Профиль развития человека: Командир — «hero», боец — его тип из Базы
    // существ (карта уровней, выборы, стартовые умения, оружие).
    public static ProgressionProfile ProfileFor(GameState state, string personId)
    {
        ProgressionRules rules = ProgressionRules.Current;
        if (IsHero(state, personId))
            return rules.GetProfile(ProgressionRules.HeroProfileId);
        ResidentState resident = HomePeopleService.Find(state, personId);
        string unitTypeId = resident != null && !string.IsNullOrEmpty(resident.UnitTypeId)
            ? resident.UnitTypeId
            : FindFighter(state, personId)?.UnitTypeId;
        return rules.GetProfile(unitTypeId);
    }

    // ----------------------------------------------------------------
    // Общий опыт
    // ----------------------------------------------------------------

    public static bool WasApplied(GameState state, string sourceId, string personId)
    {
        return state?.Progression?.AppliedSources != null &&
               state.Progression.AppliedSources.Contains(sourceId + SourceSeparator + personId);
    }

    // Один источник — один раз на человека (§25.4). Null — ничего не дано.
    public static ExperienceGain AwardExperience(GameState state, string personId, string sourceId, int amount)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("У источника опыта должен быть устойчивый ID.", nameof(sourceId));

        PersonProgressionData record = Get(state, personId);
        if (record == null || WasApplied(state, sourceId, personId))
            return null;
        ResidentState resident = HomePeopleService.Find(state, personId);
        if (resident != null && !resident.IsAlive)
            return null;
        ProgressionProfile profile = ProfileFor(state, personId);
        if (!profile.Progresses)
            return null;

        state.Progression.AppliedSources.Add(sourceId + SourceSeparator + personId);
        if (amount <= 0)
            return null;

        int oldLevel = record.Level;
        int pendingBefore = PendingChoices(state, personId);
        int cap = profile.TotalExperienceForLevel(CharacterProgression.MaxLevel);
        record.Experience = Math.Min(cap, record.Experience + amount);
        record.Level = Math.Max(record.Level, profile.LevelForExperience(record.Experience));
        SyncDisplayedLevel(state, record);

        return new ExperienceGain
        {
            PersonId = personId,
            DisplayName = DisplayName(state, personId),
            Amount = amount,
            OldLevel = oldLevel,
            NewLevel = record.Level,
            NewChoiceAvailable = PendingChoices(state, personId) > pendingBefore
        };
    }

    // Одно пережитое всеми, кто был рядом: каждый получает свой опыт.
    public static List<ExperienceGain> AwardShared(GameState state, string sourceId, int amount, IEnumerable<string> personIds)
    {
        List<ExperienceGain> gains = new List<ExperienceGain>();
        if (personIds == null)
            return gains;
        foreach (string personId in personIds.Distinct())
        {
            ExperienceGain gain = AwardExperience(state, personId, sourceId, amount);
            if (gain != null)
                gains.Add(gain);
        }
        return gains;
    }

    // Кто сейчас в пути: герой и живые бойцы похода (свита не прогрессирует).
    public static List<string> PartyPersonIds(GameState state)
    {
        List<string> ids = new List<string>();
        CommanderData hero = state?.GetSelectedCommander();
        if (hero != null)
            ids.Add(hero.Id);
        if (state != null && state.HasActiveExpedition && state.ActiveExpedition.FighterIds != null)
        {
            foreach (string fighterId in state.ActiveExpedition.FighterIds)
            {
                ResidentState resident = HomePeopleService.Find(state, fighterId);
                if ((resident == null || resident.IsAlive) && IsProgressing(state, fighterId))
                    ids.Add(fighterId);
            }
        }
        return ids;
    }

    // Опыт внутри текущего уровня: сколько набрано и сколько нужно.
    public static void GetLevelProgress(GameState state, PersonProgressionData record, out int current, out int required)
    {
        current = 0;
        required = 0;
        if (record == null || record.Level >= CharacterProgression.MaxLevel)
            return;
        ProgressionProfile profile = ProfileFor(state, record.PersonId);
        current = Math.Max(0, record.Experience - profile.TotalExperienceForLevel(record.Level));
        required = profile.ExperienceToNextLevel(record.Level);
    }

    // ----------------------------------------------------------------
    // Компетенции и практика
    // ----------------------------------------------------------------

    public static int GetCompetencyRank(GameState state, string personId, string competencyId)
    {
        CommanderData hero = state?.GetSelectedCommander();
        if (hero != null && hero.Id == personId)
            return hero.HeroProfile != null ? hero.HeroProfile.GetCompetency(competencyId) : 0;
        PersonProgressionData record = Get(state, personId);
        CompetencyProgressData entry = record?.FindCompetency(competencyId);
        return entry != null ? NarrativeCheckMath.ClampCompetency(entry.Rank) : 0;
    }

    public static void SetCompetencyRank(GameState state, string personId, string competencyId, int rank)
    {
        int clamped = Math.Max(0, Math.Min(CharacterProgression.MaxCompetencyRank, rank));
        CommanderData hero = state?.GetSelectedCommander();
        if (hero != null && hero.Id == personId)
        {
            if (hero.HeroProfile == null)
                hero.HeroProfile = new HeroProfileData();
            hero.HeroProfile.SetCompetency(competencyId, clamped);
            return;
        }
        PersonProgressionData record = Get(state, personId);
        if (record != null)
            record.GetOrCreateCompetency(competencyId).Rank = clamped;
    }

    // Все компетенции человека, в которых есть ступень или практика.
    public static List<string> KnownCompetencies(GameState state, string personId)
    {
        List<string> result = new List<string>();
        PersonProgressionData record = Get(state, personId);
        foreach (string competencyId in NarrativeCompetencyIds.Known)
        {
            CompetencyProgressData entry = record?.FindCompetency(competencyId);
            if (GetCompetencyRank(state, personId, competencyId) > 0 || (entry != null && entry.Practice > 0))
                result.Add(competencyId);
        }
        return result;
    }

    // Практика одного содержательного применения. repeatKey — одинаковое
    // содержание (тот же состав врагов): с повтором практика угасает.
    public static PracticeGain AddPractice(GameState state, string personId, string competencyId, int points, string repeatKey = null)
    {
        if (state == null || string.IsNullOrEmpty(competencyId) || !NarrativeCompetencyIds.IsKnown(competencyId))
            return null;
        PersonProgressionData record = Get(state, personId);
        if (record == null)
            return null;

        if (!string.IsNullOrEmpty(repeatKey))
        {
            string key = "practice:" + personId + ":" + competencyId + ":" + repeatKey;
            points = Math.Min(points, CharacterProgression.RepeatPractice(RepeatCount(state, key)));
            IncrementRepeat(state, key);
        }
        if (points <= 0)
            return null;

        CompetencyProgressData entry = record.GetOrCreateCompetency(competencyId);
        int ceiling = Math.Max(CharacterProgression.PracticeCeiling, Math.Min(CharacterProgression.MaxCompetencyRank, entry.Ceiling));
        int oldRank = GetCompetencyRank(state, personId, competencyId);
        int rank = oldRank;
        PracticeGain gain = new PracticeGain
        {
            PersonId = personId,
            DisplayName = DisplayName(state, personId),
            CompetencyId = competencyId,
            Points = points,
            OldRank = oldRank
        };

        entry.PracticeSinceChoice += points;
        if (rank >= ceiling)
        {
            // Собственной практикой дальше не вырасти: нужен новый принцип.
            entry.Practice = 0;
            gain.NewRank = rank;
            gain.ReachedCeiling = rank < CharacterProgression.MaxCompetencyRank;
            return gain;
        }

        entry.Practice += points;
        while (rank < ceiling && entry.Practice >= CharacterProgression.PracticeToNextRank(rank))
        {
            entry.Practice -= CharacterProgression.PracticeToNextRank(rank);
            rank++;
        }
        if (rank >= ceiling)
            entry.Practice = 0;

        if (rank != oldRank)
            SetCompetencyRank(state, personId, competencyId, rank);
        gain.NewRank = rank;
        gain.ReachedCeiling = rank >= ceiling && rank < CharacterProgression.MaxCompetencyRank;
        return gain;
    }

    // Наставник или новое знание открывает следующий принцип (§27.6): потолок
    // практики поднимается, но саму ступень ещё нужно освоить практикой.
    public static bool Teach(GameState state, string personId, string competencyId, int newCeiling)
    {
        PersonProgressionData record = Get(state, personId);
        if (record == null || !NarrativeCompetencyIds.IsKnown(competencyId))
            return false;
        CompetencyProgressData entry = record.GetOrCreateCompetency(competencyId);
        int clamped = Math.Max(CharacterProgression.PracticeCeiling, Math.Min(CharacterProgression.MaxCompetencyRank, newCeiling));
        if (clamped <= entry.Ceiling)
            return false;
        entry.Ceiling = clamped;
        return true;
    }

    // Какую компетенцию развивает удар этого человека — из его профиля.
    public static string WeaponCompetencyFor(GameState state, string personId, bool rangedAttack)
    {
        ProgressionProfile profile = ProfileFor(state, personId);
        string competencyId = rangedAttack ? profile.RangedCompetencyId : profile.MeleeCompetencyId;
        if (!string.IsNullOrWhiteSpace(competencyId))
            return competencyId;
        return rangedAttack ? NarrativeCompetencyIds.Shooting : NarrativeCompetencyIds.ChoppingWeapons;
    }

    // ----------------------------------------------------------------
    // Значимый выбор — на уровнях, отмеченных в карте развития (канон: каждые 3)
    // ----------------------------------------------------------------

    public static int PendingChoices(GameState state, string personId)
    {
        PersonProgressionData record = Get(state, personId);
        if (record == null)
            return 0;
        int earned = ProfileFor(state, personId).CountChoiceLevels(record.StartingLevel, record.Level);
        return Math.Max(0, earned - record.ChoicesTaken);
    }

    public static List<DevelopmentOption> GetChoiceOptions(GameState state, string personId)
    {
        List<DevelopmentOption> options = new List<DevelopmentOption>();
        PersonProgressionData record = Get(state, personId);
        if (record == null || PendingChoices(state, personId) <= 0)
            return options;

        bool isHero = IsHero(state, personId);
        ProgressionProfile profile = ProfileFor(state, personId);
        IReadOnlyList<string> known = NarrativeCompetencyIds.Known;
        IReadOnlyList<string> catalog = profile.ChoiceCompetencies != null && profile.ChoiceCompetencies.Count > 0
            ? profile.ChoiceCompetencies.Where(id => known.Contains(id)).ToList()
            : isHero ? known : NarrativeCompetencyIds.FighterCatalog;

        // Персональные: то, что человек реально делал с прошлого выбора.
        List<CompetencyProgressData> practiced = record.Competencies
            .Where(entry => entry != null && entry.PracticeSinceChoice > 0 &&
                            catalog.Contains(entry.CompetencyId) &&
                            GetCompetencyRank(state, personId, entry.CompetencyId) < Ceiling(entry))
            .OrderByDescending(entry => entry.PracticeSinceChoice)
            .ThenBy(entry => IndexOf(catalog, entry.CompetencyId))
            .Take(2)
            .ToList();
        foreach (CompetencyProgressData entry in practiced)
        {
            int rank = GetCompetencyRank(state, personId, entry.CompetencyId);
            string label = NarrativeCompetencyLabels.GetLabel(entry.CompetencyId);
            options.Add(new DevelopmentOption
            {
                Id = "deepen:" + entry.CompetencyId,
                Kind = DevelopmentOptionKind.Deepen,
                CompetencyId = entry.CompetencyId,
                Title = "Углубить: " + label + " " + rank + " → " + (rank + 1),
                Description = "Вырастает из того, что человек реально делал с прошлого выбора. " +
                              NarrativeCompetencyLabels.GetDescription(entry.CompetencyId),
                IsPersonal = true
            });
        }

        // Нейтральные: новое дело — сначала то, что уже пробовал, затем по
        // кругу каталога, чтобы каждый выбор предлагал другое.
        List<string> untouched = catalog
            .Where(id => GetCompetencyRank(state, personId, id) == 0 && options.All(option => option.CompetencyId != id))
            .ToList();
        List<string> learn = untouched.Where(id => record.FindCompetency(id)?.Practice > 0).ToList();
        if (untouched.Count > 0)
        {
            int offset = (record.ChoicesTaken * 2) % untouched.Count;
            for (int i = 0; i < untouched.Count && learn.Count < 2; i++)
            {
                string id = untouched[(offset + i) % untouched.Count];
                if (!learn.Contains(id))
                    learn.Add(id);
            }
        }
        foreach (string competencyId in learn.Take(2))
        {
            options.Add(new DevelopmentOption
            {
                Id = "learn:" + competencyId,
                Kind = DevelopmentOptionKind.Learn,
                CompetencyId = competencyId,
                Title = "Новое дело: " + NarrativeCompetencyLabels.GetLabel(competencyId) + " 0 → 1",
                Description = NarrativeCompetencyLabels.GetDescription(competencyId) +
                              " Дальше растёт от применения."
            });
        }

        if (record.ToughnessChoices < CharacterProgression.MaxToughnessChoices)
        {
            options.Add(new DevelopmentOption
            {
                Id = "toughness",
                Kind = DevelopmentOptionKind.Toughness,
                Title = "Крепость тела: +" + CharacterProgression.ToughnessHitPoints + " к здоровью",
                Description = "Тело привыкло к дороге и ударам. Можно выбрать не больше " +
                              CharacterProgression.MaxToughnessChoices + " раз (сейчас " +
                              record.ToughnessChoices + ")."
            });
        }

        return options;
    }

    public static bool TryApplyChoice(GameState state, string personId, string optionId, out string message)
    {
        message = string.Empty;
        PersonProgressionData record = Get(state, personId);
        if (record == null || PendingChoices(state, personId) <= 0)
        {
            message = "Выбора развития сейчас нет.";
            return false;
        }

        DevelopmentOption option = GetChoiceOptions(state, personId).FirstOrDefault(candidate => candidate.Id == optionId);
        if (option == null)
        {
            message = "Этот вариант сейчас недоступен.";
            return false;
        }

        string name = DisplayName(state, personId);
        switch (option.Kind)
        {
            case DevelopmentOptionKind.Deepen:
            case DevelopmentOptionKind.Learn:
            {
                int rank = GetCompetencyRank(state, personId, option.CompetencyId) + 1;
                SetCompetencyRank(state, personId, option.CompetencyId, rank);
                CompetencyProgressData entry = record.GetOrCreateCompetency(option.CompetencyId);
                entry.Practice = 0;
                message = name + ": " + NarrativeCompetencyLabels.GetLabel(option.CompetencyId) + " " + rank + ".";
                break;
            }
            case DevelopmentOptionKind.Toughness:
                record.ToughnessChoices++;
                ItemService.RefreshMaxHitPoints(state, personId);
                message = name + ": крепость тела, +" + CharacterProgression.ToughnessHitPoints + " к здоровью.";
                break;
        }

        record.ChoicesTaken++;
        record.ChosenOptionIds.Add(option.Id);
        foreach (CompetencyProgressData entry in record.Competencies)
        {
            if (entry != null)
                entry.PracticeSinceChoice = 0;
        }
        Chronicle.Record(state, "development." + personId + "." + record.ChoicesTaken, "Развитие", message);
        return true;
    }

    // Добавка к здоровью от выбранной крепости тела.
    public static int BonusMaxHitPoints(GameState state, string personId)
    {
        PersonProgressionData record = state?.Progression?.People?.FirstOrDefault(entry => entry != null && entry.PersonId == personId);
        return record != null ? record.ToughnessChoices * CharacterProgression.ToughnessHitPoints : 0;
    }

    // ----------------------------------------------------------------
    // Повторы (антифарм)
    // ----------------------------------------------------------------

    public static int RepeatCount(GameState state, string key)
    {
        ProgressionRepeatData entry = state?.Progression?.Repeats?.FirstOrDefault(repeat => repeat != null && repeat.Key == key);
        return entry != null ? entry.Count : 0;
    }

    public static void IncrementRepeat(GameState state, string key)
    {
        EnsureState(state);
        ProgressionRepeatData entry = state.Progression.Repeats.FirstOrDefault(repeat => repeat != null && repeat.Key == key);
        if (entry == null)
        {
            entry = new ProgressionRepeatData { Key = key };
            state.Progression.Repeats.Add(entry);
        }
        entry.Count++;
    }

    // ----------------------------------------------------------------
    // Текст для донесений
    // ----------------------------------------------------------------

    public static List<string> DescribeLevelUps(IEnumerable<ExperienceGain> gains)
    {
        List<string> lines = new List<string>();
        foreach (ExperienceGain gain in gains ?? Enumerable.Empty<ExperienceGain>())
        {
            if (gain == null || gain.NewLevel <= gain.OldLevel)
                continue;
            lines.Add(gain.DisplayName + " — уровень " + gain.NewLevel +
                      (gain.NewChoiceAvailable ? ", ждёт выбор развития (экран героя)." : "."));
        }
        return lines;
    }

    public static string DescribeShared(string title, IList<ExperienceGain> gains)
    {
        if (gains == null || gains.Count == 0)
            return string.Empty;
        List<string> parts = gains.Select(gain => gain.DisplayName + " +" + gain.Amount).ToList();
        List<string> lines = new List<string> { title + ": " + string.Join(", ", parts) + " опыта." };
        lines.AddRange(DescribeLevelUps(gains));
        return string.Join(" ", lines);
    }

    public static List<string> DescribePractice(IEnumerable<PracticeGain> gains)
    {
        List<string> lines = new List<string>();
        foreach (PracticeGain gain in gains ?? Enumerable.Empty<PracticeGain>())
        {
            if (gain == null)
                continue;
            string label = NarrativeCompetencyLabels.GetLabel(gain.CompetencyId);
            if (gain.NewRank > gain.OldRank)
                lines.Add(gain.DisplayName + ": " + label + " — ступень " + gain.NewRank + ".");
        }
        return lines;
    }

    // ----------------------------------------------------------------

    private static bool IsHero(GameState state, string personId)
    {
        CommanderData hero = state?.GetSelectedCommander();
        return hero != null && hero.Id == personId;
    }

    private static int Ceiling(CompetencyProgressData entry)
    {
        return Math.Max(CharacterProgression.PracticeCeiling, Math.Min(CharacterProgression.MaxCompetencyRank, entry.Ceiling));
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
                return i;
        }
        return list.Count;
    }

    // Человек приходит с тем, что умеет его тип (§27.8): стартовые компетенции
    // профиля. У Командира ступень пишется в HeroProfile и не понижается.
    private static void GrantStartingCompetencies(GameState state, PersonProgressionData record, ProgressionProfile profile)
    {
        if (profile?.StartingCompetencies == null)
            return;
        foreach (CompetencyRank starting in profile.StartingCompetencies)
        {
            if (starting == null || string.IsNullOrWhiteSpace(starting.CompetencyId) || starting.Rank <= 0)
                continue;
            int rank = Math.Min(CharacterProgression.MaxCompetencyRank, starting.Rank);
            if (IsHero(state, record.PersonId))
            {
                if (GetCompetencyRank(state, record.PersonId, starting.CompetencyId) < rank)
                    SetCompetencyRank(state, record.PersonId, starting.CompetencyId, rank);
            }
            else
            {
                CompetencyProgressData entry = record.GetOrCreateCompetency(starting.CompetencyId);
                entry.Rank = Math.Max(entry.Rank, rank);
            }
        }
    }

    private static void SyncDisplayedLevel(GameState state, PersonProgressionData record)
    {
        FighterData data = FindPersonData(state, record.PersonId);
        if (data != null)
            data.Level = record.Level;
    }

    private static FighterData FindPersonData(GameState state, string personId)
    {
        CommanderData hero = state.GetSelectedCommander();
        if (hero != null && hero.Id == personId)
            return hero;
        return FindFighter(state, personId);
    }

    private static FighterData FindFighter(GameState state, string personId)
    {
        if (state?.Fighters == null)
            return null;
        foreach (FighterData fighter in state.Fighters)
        {
            if (fighter != null && fighter.Id == personId)
                return fighter;
        }
        return null;
    }

    public static string DisplayName(GameState state, string personId)
    {
        ResidentState resident = HomePeopleService.Find(state, personId);
        if (resident != null && !string.IsNullOrEmpty(resident.DisplayName))
            return resident.DisplayName;
        FighterData data = FindPersonData(state, personId);
        return data != null ? data.Name : personId;
    }
}
