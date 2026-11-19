using System;
using System.Collections.Generic;
using System.Linq;

// Бриф «Живое население глобальной карты» (19.11.2026), [РАБОЧЕЕ — ПРОТОТИП].
// Владелец жизни групп мира: заводит runtime-состояние из GeneratedMap
// (и обёртку старых партий), отдаёт положение групп показу, открывает
// контакты с героем штатным «решением похода» (пауза, два варианта),
// применяет выбор и итог боя ровно один раз, пополняет источники по
// резерву и сроку. Движение и ИИ по тактам — WorldPopulationSimulation.
public static class WorldPopulationRules
{
    // Настройки слоя для игры; Unity-слой назначает их из ассета генератора.
    private static WorldPopulationSettings current = WorldPopulationSettings.CreateDefault();

    public static WorldPopulationSettings Current
    {
        get => current;
        set
        {
            current = value ?? WorldPopulationSettings.CreateDefault();
            current.Sanitize();
        }
    }
}

public static partial class WorldPopulationService
{
    public const string DecisionDefinitionId = "population.contact";
    public const string OptionA = "population.a";
    public const string OptionB = "population.b";
    public const string HeroTarget = "hero";

    private static int nextDecisionId = -300000;

    private static WorldPopulationSettings Settings => WorldPopulationRules.Current;

    // ------------------------------------------------------------------
    // Состояние и миграция

    // Заводит состояние один раз на карту партии (идемпотентно). Старая
    // партия без состава групп получает обёртку существующих групп по роли и
    // спокойные первые часы; новых зверей и существ ей не добавляется.
    public static WorldPopulationStateData EnsureState(GameState state, double now)
    {
        if (state == null || !state.HasGeneratedMap)
            return null;
        GeneratedWorldMap map = state.GeneratedMap;
        WorldPopulationStateData data = state.Population;
        if (data != null && data.Initialized && data.MapSeed == map.Seed)
        {
            data.Sources ??= new List<PopulationSourceState>();
            data.Groups ??= new List<PopulationGroupState>();
            data.Conflicts ??= new List<PopulationConflictState>();
            data.Contacts ??= new List<PopulationContactState>();
            return data;
        }

        data = new WorldPopulationStateData
        {
            Version = WorldPopulationStateData.CurrentVersion,
            Initialized = true,
            MapSeed = map.Seed,
            LastSimHour = now,
            NextUpkeepHour = Math.Floor(now) + 1.0
        };
        state.Population = data;
        WorldMapHexGrid grid = map.CreateGrid();
        map.Actors ??= new List<GeneratedMapActor>();
        bool legacy = map.Actors.All(a => a == null || a.Members == null || a.Members.Count == 0) &&
                      (map.PopulationSources == null || map.PopulationSources.Count == 0);
        data.Legacy = legacy;

        if (map.PopulationSources != null)
        {
            foreach (GeneratedPopulationSource s in map.PopulationSources.Where(s => s != null))
                data.Sources.Add(new PopulationSourceState
                {
                    SourceId = s.SourceId, Kind = s.Kind, PresetId = s.PresetId, Name = s.Name, X = s.XPercent, Y = s.YPercent,
                    RadiusHexes = s.RadiusHexes, Reserve = s.Reserve, Signs = s.Signs ?? string.Empty
                });
        }

        foreach (GeneratedMapActor actor in map.Actors.Where(a => a != null).OrderBy(a => a.ActorId, StringComparer.Ordinal))
        {
            if (legacy)
                MigrateLegacyActor(data, map, actor);
            if (string.IsNullOrEmpty(actor.Behavior) && !legacy)
                continue;
            if (actor.State != "Travel" && string.IsNullOrEmpty(actor.HomeId))
                continue;
            PopulationGroupState group = NewGroup(actor, actor.ActorId, actor.Members, now);
            MapPointData at = WorldMapActorMotion.PositionAt(actor, now, grid, out float dx, out float dy, out _);
            SetPosition(group, at.XPercent, at.YPercent, dx, dy);
            // Старая партия: враждебные группы рядом с героем не нападают
            // сразу после загрузки (без внезапной засады).
            if (legacy && WorldPopulationBehavior.IsHostileCapable(group.Behavior))
                group.CalmUntilHour = now + Settings.RepeatContactHours;
            data.Groups.Add(group);
        }
        return data;
    }

    // Старое сохранение: состав по роли (детерминированно по ID группы),
    // источник — дом группы. Записывается в определение один раз.
    private static void MigrateLegacyActor(WorldPopulationStateData data, GeneratedWorldMap map, GeneratedMapActor actor)
    {
        if (actor.Members != null && actor.Members.Count > 0)
            return;
        WorldPopulationPreset preset = WorldPopulationCatalog.DefaultForRole(Settings, actor.Role);
        actor.Behavior = actor.Role == WorldMapGroupRule.Master ? WorldPopulationBehavior.Peaceful : preset?.Behavior ?? WorldPopulationBehavior.Peaceful;
        actor.Members = new List<GeneratedActorMember>();
        if (preset != null && actor.Role != WorldMapGroupRule.Master)
        {
            actor.PresetId = preset.Id;
            Random random = new Random(WorldMapGenerator.StreamSeed(map.Seed, "population.legacy." + actor.ActorId));
            foreach (WorldPopulationMember m in preset.Members)
            {
                int n = m.Min + random.Next(Math.Max(1, m.Max - m.Min + 1));
                for (int k = 1; k <= n; k++)
                    actor.Members.Add(new GeneratedActorMember { MemberId = actor.ActorId + ":" + m.UnitTypeId + ":" + k, UnitTypeId = m.UnitTypeId });
            }
            actor.UnitArtId = actor.Members.Count > 0 ? actor.Members[0].UnitTypeId : string.Empty;
        }
        actor.TerritoryXPercent = actor.HomeXPercent;
        actor.TerritoryYPercent = actor.HomeYPercent;
        actor.TerritoryRadiusHexes = preset?.ChaseHexes ?? 3f;
        if (string.IsNullOrEmpty(actor.HomeId))
            return;
        actor.SourceId = actor.HomeId;
        if (data.Sources.Any(s => s.SourceId == actor.HomeId))
            return;
        bool camp = actor.Role == WorldMapGroupRule.Band;
        data.Sources.Add(new PopulationSourceState
        {
            SourceId = actor.HomeId,
            Kind = camp ? GeneratedPopulationSource.BandCamp : GeneratedPopulationSource.Village,
            PresetId = preset?.Id ?? string.Empty,
            Name = camp ? "Лагерь ватаги" : actor.HomeId,
            X = actor.HomeXPercent,
            Y = actor.HomeYPercent,
            RadiusHexes = actor.TerritoryRadiusHexes,
            Reserve = camp ? Settings.BandReserve : -1,
            Signs = preset?.Signs ?? string.Empty
        });
    }

    private static PopulationGroupState NewGroup(GeneratedMapActor actor, string groupId, List<GeneratedActorMember> members, double now)
    {
        PopulationGroupState group = new PopulationGroupState
        {
            GroupId = groupId,
            DefinitionId = actor.ActorId,
            SourceId = actor.SourceId ?? string.Empty,
            PresetId = actor.PresetId ?? string.Empty,
            Role = actor.Role ?? string.Empty,
            Behavior = string.IsNullOrEmpty(actor.Behavior) ? WorldPopulationBehavior.Peaceful : actor.Behavior,
            Name = actor.Name ?? string.Empty,
            SpawnHour = now
        };
        foreach (GeneratedActorMember m in members ?? new List<GeneratedActorMember>())
            group.Members.Add(new PopulationMemberState { MemberId = m.MemberId, UnitTypeId = m.UnitTypeId });
        return group;
    }

    // ------------------------------------------------------------------
    // Запросы для показа и боя

    public static GeneratedMapActor Definition(GameState state, string definitionId) =>
        state?.GeneratedMap?.Actors?.FirstOrDefault(a => a != null && a.ActorId == definitionId);

    public static PopulationGroupState FindGroup(GameState state, string groupId) =>
        state?.Population?.Groups?.FirstOrDefault(g => g != null && g.GroupId == groupId);

    public static PopulationSourceState FindSource(GameState state, string sourceId) =>
        state?.Population?.Sources?.FirstOrDefault(s => s != null && s.SourceId == sourceId);

    public static WorldPopulationPreset Preset(PopulationGroupState group) =>
        Settings.Find(group?.PresetId) ?? FallbackPreset(group?.Behavior);

    private static WorldPopulationPreset FallbackPreset(string behavior) =>
        new WorldPopulationPreset { Id = "fallback", Behavior = behavior ?? WorldPopulationBehavior.Peaceful };

    // Положение группы в час мира: на маршруте — по времени (между тактами
    // плавно), вне маршрута — опорная точка текущего занятия.
    public static MapPointData GroupPosition(GameState state, PopulationGroupState group, double hour, WorldMapHexGrid grid,
        out float dirX, out float dirY, out bool waiting)
    {
        dirX = group.DirX;
        dirY = group.DirY;
        waiting = true;
        GeneratedMapActor def = Definition(state, group.DefinitionId);
        if (group.Activity == PopulationGroupState.Route && def != null && def.State == "Travel")
            return WorldMapActorMotion.PositionAt(def, hour, grid, out dirX, out dirY, out waiting);
        waiting = group.Activity != PopulationGroupState.Chase && group.Activity != PopulationGroupState.Return &&
                  group.Activity != PopulationGroupState.Avoid;
        return new MapPointData(group.X, group.Y);
    }

    public static IEnumerable<PopulationGroupState> ActiveGroups(GameState state) =>
        state?.Population?.Groups?.Where(g => g != null && g.IsActive) ?? Enumerable.Empty<PopulationGroupState>();

    // Подпись при наведении: название, занятие и приблизительный состав по
    // уровню знания (без секретной базы и оценки победы).
    public static string HoverText(GameState state, PopulationGroupState group, double distanceHexes)
    {
        string activity;
        switch (group.Activity)
        {
            case PopulationGroupState.Chase: activity = group.TargetId == HeroTarget ? "идёт на вас" : "кого-то преследует"; break;
            case PopulationGroupState.Observe: activity = "следит за отрядом"; break;
            case PopulationGroupState.Avoid: activity = "уходит"; break;
            case PopulationGroupState.Wait: activity = group.Role == WorldMapGroupRule.Caravan ? "встал и выжидает" : "стоит"; break;
            case PopulationGroupState.Conflict: activity = "схватка"; break;
            case PopulationGroupState.Return: activity = "возвращается"; break;
            default: activity = group.Role == WorldMapGroupRule.Beast || group.Role == WorldMapGroupRule.Creature ? "на своём участке" : "в пути"; break;
        }
        int alive = group.Members.Count(m => m.Alive);
        string composition;
        if (alive == 0)
            composition = group.Role == WorldMapGroupRule.Master ? "один путник" : "без оружия";
        else if (distanceHexes <= 2.0)
            composition = string.Join(", ", group.Members.Where(m => m.Alive).GroupBy(m => m.UnitTypeId)
                .Select(g => UnitLabel(g.Key) + (g.Count() > 1 ? " ×" + g.Count() : string.Empty)));
        else
            composition = alive == 1 ? "один" : alive <= 3 ? "несколько" : "много";
        return group.Name + " — " + activity + " (" + composition + ")";
    }

    // Подпись типа для показа без ссылки на Базу существ; Unity-слой может
    // подставить настоящие названия.
    public static Func<string, string> UnitLabelProvider { get; set; }

    private static string UnitLabel(string unitTypeId) => UnitLabelProvider?.Invoke(unitTypeId) ?? unitTypeId;

    // ------------------------------------------------------------------
    // Сила (для выбора цели ИИ и фонового столкновения; не бой героя)

    public static double GroupStrength(PopulationGroupState group)
    {
        double total = 0;
        foreach (PopulationMemberState m in group.Members.Where(m => m.Alive))
        {
            UnitCombatStats stats = Stats(m.UnitTypeId);
            int hp = m.HitPoints > 0 ? m.HitPoints : stats.MaxHitPoints;
            total += hp * Power(stats);
        }
        return total;
    }

    public static double PartyStrength(GameState state)
    {
        double total = 0;
        foreach (string id in PartyPresence.BattleCandidateIds(state))
        {
            ResidentState resident = PeopleService.Find(state, id);
            string unit = resident != null && !string.IsNullOrEmpty(resident.UnitTypeId) ? resident.UnitTypeId : CampaignBattleBridge.HeroFallbackUnitTypeId;
            UnitCombatStats stats = Stats(unit);
            int hp = resident != null && resident.HasCombatState && resident.CurrentHitPoints > 0 ? resident.CurrentHitPoints : stats.MaxHitPoints;
            total += hp * Power(stats);
        }
        return total;
    }

    private static double Power(UnitCombatStats s) => Math.Max(1, s.Attack + s.Damage + s.Defense + (s.AttackRange > 1 ? 2 : 0));

    private static UnitCombatStats Stats(string unitTypeId)
    {
        if (GameState.UnitStatsProvider != null && GameState.UnitStatsProvider.TryGetCombatStats(unitTypeId, out UnitCombatStats stats) && stats.MaxHitPoints > 0)
            return stats;
        return new UnitCombatStats { MaxHitPoints = 12, Attack = 2, Defense = 1, Damage = 3, Movement = 3, Initiative = 3, AttackRange = 1 };
    }

    // ------------------------------------------------------------------
    // Контакт с героем: штатное решение похода

    public static PopulationContactState PendingContact(GameState state) =>
        state?.Population?.Contacts?.LastOrDefault(c => c != null && c.Status != PopulationContactState.Resolved);

    public static bool IsPopulationDecision(GameState state) =>
        state != null && state.HasPendingExpeditionDecision && state.ActiveExpedition.PendingDecision.DefinitionId == DecisionDefinitionId;

    // Арбитраж с суточной дорожной возможностью: пока идёт контакт с
    // реальной группой или действует передышка после боя, атмосферный
    // дорожный эпизод не добавляется.
    public static bool SuppressesRoadEpisodes(GameState state, double now)
    {
        WorldPopulationStateData data = state?.Population;
        if (data == null)
            return false;
        return PendingContact(state) != null || now < data.RespiteUntilHour;
    }

    internal static bool RaiseContact(GameState state, PopulationContactState contact, List<string> messages)
    {
        WorldPopulationStateData data = state.Population;
        if (data == null || PendingContact(state) != null || !state.HasActiveExpedition || state.HasPendingExpeditionDecision)
            return false;
        contact.ContactId = "c" + (++data.NextContactNumber) + "-" + (string.IsNullOrEmpty(contact.GroupId) ? contact.SourceId : contact.GroupId);
        ExpeditionDecisionOccurrence decision = BuildDecision(state, contact);
        if (decision == null)
            return false;
        data.Contacts.Add(contact);
        state.ActiveExpedition.PendingDecision = decision;
        messages?.Add("<color=#E5BD63>" + decision.Title + "</color> " + FirstSentence(decision.Description));
        return true;
    }

    private static string FirstSentence(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        int dot = text.IndexOf(". ", StringComparison.Ordinal);
        return dot > 0 ? text.Substring(0, dot + 1) : text;
    }

    private static ExpeditionDecisionOccurrence BuildDecision(GameState state, PopulationContactState contact)
    {
        PopulationGroupState group = FindGroup(state, contact.GroupId);
        WorldPopulationPreset preset = Preset(group);
        ExpeditionDecisionOccurrence d = new ExpeditionDecisionOccurrence
        {
            Id = nextDecisionId--,
            Day = state.Day,
            DefinitionId = DecisionDefinitionId,
            OptionA = new ExpeditionDecisionOptionView { Id = OptionA },
            OptionB = new ExpeditionDecisionOptionView { Id = OptionB }
        };
        string alive = group != null ? CompositionText(group) : string.Empty;
        switch (contact.Kind)
        {
            case PopulationContactState.BeastAttack:
                if (group == null)
                    return null;
                d.Title = group.Name;
                if (group.Behavior == WorldPopulationBehavior.Shy)
                {
                    d.Description = Text(preset.AttackText, group.Name + " прижат к валежнику.") + " Зверь сам не нападает.";
                    d.OptionA.Label = "Напасть";
                    d.OptionA.ConsequencePreview = "Бой с " + alive + ".";
                    d.OptionB.Label = "Оставить зверя";
                    d.OptionB.ConsequencePreview = "Отряд идёт дальше, зверь уходит в чащу.";
                    break;
                }
                d.Description = Text(preset.AttackText, group.Name + " идёт на отряд.");
                d.OptionA.Label = "Принять бой";
                d.OptionA.ConsequencePreview = "Бой с " + alive + ".";
                if (group.Behavior == WorldPopulationBehavior.Predator && preset.AppeaseSupply > 0)
                {
                    d.OptionB.Label = "Бросить стае припасы (−" + preset.AppeaseSupply + ")";
                    d.OptionB.ConsequencePreview = "Стая займётся добычей и отстанет.";
                }
                else if (preset.RetreatSupplyCost > 0)
                {
                    d.OptionB.Label = "Отступить, бросив поклажу (−" + preset.RetreatSupplyCost + ")";
                    d.OptionB.ConsequencePreview = "Отряд уходит с участка, зверь не преследует дальше своего места.";
                }
                else
                {
                    d.OptionB.Label = "Отступить";
                    d.OptionB.ConsequencePreview = "Отряд пятится и обходит участок.";
                }
                break;
            case PopulationContactState.BandDemand:
                if (group == null)
                    return null;
                int demand = Math.Max(Settings.DemandMinimum, (int)Math.Round(state.ArmyGold * Settings.DemandShare));
                contact.DemandAmount = demand;
                d.Title = group.Name + " на дороге";
                d.Description = Text(preset.AttackText, "Вооружённые люди перегородили дорогу и требуют платы.") +
                                " Их " + group.Members.Count(m => m.Alive) + ".";
                if (state.ArmyGold >= demand)
                {
                    d.OptionA.Label = "Заплатить " + demand + " золота";
                    d.OptionA.ConsequencePreview = "Ватага пропустит отряд и уйдёт к себе.";
                }
                else
                {
                    contact.DemandAmount = 0;
                    d.OptionA.Label = "Отдать припасы (−2)";
                    d.OptionA.ConsequencePreview = "Серебра мало — возьмут едой.";
                }
                d.OptionB.Label = "Драться";
                d.OptionB.ConsequencePreview = "Бой с " + alive + ".";
                break;
            case PopulationContactState.CampApproach:
            {
                PopulationSourceState source = FindSource(state, contact.SourceId);
                if (source == null)
                    return null;
                PopulationGroupState home = CampDefenders(state, source);
                contact.GroupId = home?.GroupId ?? string.Empty;
                d.Title = source.Name;
                if (home != null)
                {
                    d.Description = "Шалаши у опушки, дымок над кострищем. В лагере — " + CompositionText(home) + ".";
                    d.OptionA.Label = "Напасть на лагерь";
                    d.OptionA.ConsequencePreview = "Бой. Победа выжжет лагерь: отсюда больше не выйдут.";
                }
                else
                {
                    d.Description = "Шалаши у опушки, кострище присыпано землёй. Людей в лагере нет — ушли на дорогу или разбежались.";
                    d.OptionA.Label = "Разорить лагерь";
                    d.OptionA.ConsequencePreview = "Шалаши сожжены: новая вылазка отсюда не выйдет.";
                }
                d.OptionB.Label = "Уйти";
                d.OptionB.ConsequencePreview = "Лагерь останется как есть.";
                break;
            }
            case PopulationContactState.ConflictHelp:
            {
                PopulationConflictState conflict = state.Population.Conflicts.FirstOrDefault(c => c.ConflictId == contact.ConflictId);
                PopulationGroupState attacker = conflict != null ? FindGroup(state, conflict.AttackerId) : null;
                PopulationGroupState defender = conflict != null ? FindGroup(state, conflict.DefenderId) : null;
                if (attacker == null || defender == null)
                    return null;
                contact.GroupId = attacker.GroupId;
                d.Title = attacker.Name + " напала на " + (defender.Role == WorldMapGroupRule.Caravan ? "обоз" : defender.Name.ToLowerInvariant());
                d.Description = "Впереди крики и звон: " + defender.Name.ToLowerInvariant() + " отбивается от людей с оружием. Нападающих — " +
                                attacker.Members.Count(m => m.Alive) + ".";
                d.OptionA.Label = "Вмешаться";
                d.OptionA.ConsequencePreview = "Бой с нападающими; " + defender.Name.ToLowerInvariant() + " держится в стороне.";
                d.OptionB.Label = "Не вмешиваться";
                d.OptionB.ConsequencePreview = "Схватка решится без отряда.";
                break;
            }
            default:
                return null;
        }
        return d;
    }

    private static string Text(string preferred, string fallback) => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static string CompositionText(PopulationGroupState group)
    {
        int alive = group.Members.Count(m => m.Alive);
        return string.Join(", ", group.Members.Where(m => m.Alive).GroupBy(m => m.UnitTypeId)
            .Select(g => UnitLabel(g.Key) + (g.Count() > 1 ? " ×" + g.Count() : string.Empty))) + (alive == 0 ? "никого" : string.Empty);
    }

    // Группа лагеря дома: своя вылазка в пределах полутора клеток от лагеря.
    public static PopulationGroupState CampDefenders(GameState state, PopulationSourceState source)
    {
        GeneratedWorldMap map = state.GeneratedMap;
        if (map == null)
            return null;
        WorldMapHexGrid grid = map.CreateGrid();
        double now = ContinuousSimulationSystem.GetAbsoluteHours(state);
        foreach (PopulationGroupState g in ActiveGroups(state).Where(g => g.SourceId == source.SourceId && g.Members.Any(m => m.Alive)))
        {
            MapPointData at = GroupPosition(state, g, now, grid, out _, out _, out _);
            if (grid.DistanceHexes(at.XPercent, at.YPercent, source.X, source.Y) <= 1.5 && g.Activity != PopulationGroupState.Chase)
                return g;
        }
        return null;
    }

    public static bool CanChooseOption(GameState state, string optionId)
    {
        PopulationContactState contact = PendingContact(state);
        if (contact == null || contact.Status != PopulationContactState.Pending)
            return false;
        PopulationGroupState group = FindGroup(state, contact.GroupId);
        WorldPopulationPreset preset = Preset(group);
        if (contact.Kind == PopulationContactState.BandDemand && optionId == OptionA)
            return contact.DemandAmount > 0 ? state.ArmyGold >= contact.DemandAmount : state.ArmySupply >= 2;
        if (contact.Kind == PopulationContactState.BeastAttack && optionId == OptionB && group != null)
        {
            if (group.Behavior == WorldPopulationBehavior.Predator && preset.AppeaseSupply > 0)
                return state.ArmySupply >= preset.AppeaseSupply;
            if (preset.RetreatSupplyCost > 0)
                return state.ArmySupply >= preset.RetreatSupplyCost;
        }
        return optionId == OptionA || optionId == OptionB;
    }

    // Применяет выбор игрока (штатная команда результата). Бой не
    // запускается здесь: контакт переходит в «бой ожидается», UI начинает
    // его штатным CampaignBattleBridge (TakeBattleRequest).
    public static bool TryApplyChoice(GameState state, string optionId, out string message)
    {
        message = "Не удалось выполнить приказ.";
        PopulationContactState contact = PendingContact(state);
        if (contact == null || contact.Status != PopulationContactState.Pending || !IsPopulationDecision(state))
            return false;
        if (!CanChooseOption(state, optionId))
        {
            message = "Для этого не хватает припасов или серебра.";
            return false;
        }
        double now = ContinuousSimulationSystem.GetAbsoluteHours(state);
        PopulationGroupState group = FindGroup(state, contact.GroupId);
        WorldPopulationPreset preset = Preset(group);
        WorldPopulationSettings s = Settings;
        contact.Choice = optionId;
        state.ActiveExpedition.PendingDecision = null;

        bool fight = false;
        switch (contact.Kind)
        {
            case PopulationContactState.BeastAttack:
                if (optionId == OptionA)
                    fight = true;
                else
                {
                    if (group.Behavior == WorldPopulationBehavior.Predator && preset.AppeaseSupply > 0)
                    {
                        state.ArmySupply = Math.Max(0, state.ArmySupply - preset.AppeaseSupply);
                        message = group.Name + ": стая растащила брошенное и отстала (−" + preset.AppeaseSupply + " припасов).";
                    }
                    else if (preset.RetreatSupplyCost > 0)
                    {
                        state.ArmySupply = Math.Max(0, state.ArmySupply - preset.RetreatSupplyCost);
                        message = "Отряд отступил, бросив часть поклажи (−" + preset.RetreatSupplyCost + " припасов). " + group.Name + " не идёт дальше своего места.";
                    }
                    else
                        message = group.Behavior == WorldPopulationBehavior.Shy
                            ? group.Name + " скрылась в чаще."
                            : "Отряд отступил. " + group.Name + " не идёт дальше своего места.";
                    Calm(group, now, s.RepeatContactHours);
                    StartActivity(group, PopulationGroupState.Avoid, HeroTarget, now, 1.0);
                    contact.Outcome = "avoided";
                }
                break;
            case PopulationContactState.BandDemand:
                if (optionId == OptionB)
                    fight = true;
                else
                {
                    if (contact.DemandAmount > 0)
                    {
                        state.ArmyGold = Math.Max(0, state.ArmyGold - contact.DemandAmount);
                        message = "Отряд заплатил " + contact.DemandAmount + " золота. " + group.Name + " отходит к своему лагерю.";
                    }
                    else
                    {
                        state.ArmySupply = Math.Max(0, state.ArmySupply - 2);
                        message = "Отряд отдал два мешка припасов. " + group.Name + " отходит к своему лагерю.";
                    }
                    Calm(group, now, Math.Max(24.0, s.RepeatContactHours));
                    StartActivity(group, PopulationGroupState.Return, string.Empty, now, 0);
                    contact.Outcome = "paid";
                }
                break;
            case PopulationContactState.CampApproach:
            {
                PopulationSourceState source = FindSource(state, contact.SourceId);
                if (optionId == OptionB || source == null)
                {
                    if (source != null)
                        source.CampCooldownUntil = now + s.RepeatContactHours;
                    message = "Отряд обошёл лагерь стороной.";
                    contact.Outcome = "left";
                    break;
                }
                if (group != null && group.IsActive && group.Members.Any(m => m.Alive))
                    fight = true;
                else
                {
                    EliminateSource(state, source, now);
                    message = "Отряд разорил пустой лагерь: шалаши сожжены, новая вылазка отсюда не выйдет.";
                    contact.Outcome = "razed";
                }
                break;
            }
            case PopulationContactState.ConflictHelp:
                if (optionId == OptionA)
                {
                    fight = true;
                    PopulationConflictState conflict = state.Population.Conflicts.FirstOrDefault(c => c.ConflictId == contact.ConflictId);
                    if (conflict != null)
                        conflict.ResolveHour = double.MaxValue;
                }
                else
                {
                    message = "Отряд не стал вмешиваться.";
                    contact.Outcome = "declined";
                }
                break;
        }

        if (fight)
        {
            contact.Status = PopulationContactState.BattlePending;
            message = "Бой: " + (group?.Name ?? "противник") + ".";
        }
        else
            contact.Status = PopulationContactState.Resolved;
        return true;
    }

    private static void Calm(PopulationGroupState group, double now, double hours)
    {
        if (group == null)
            return;
        group.CalmUntilHour = Math.Max(group.CalmUntilHour, now + hours);
        group.LastContactHour = now;
        group.ContactCount++;
    }

    internal static void StartActivity(PopulationGroupState group, string activity, string target, double now, double hours)
    {
        if (group == null)
            return;
        group.Activity = activity;
        group.TargetId = target ?? string.Empty;
        group.ActivityStartHour = now;
        group.ActivityUntilHour = hours > 0 ? now + hours : 0;
        group.ActivityOriginX = group.X;
        group.ActivityOriginY = group.Y;
    }

    internal static void SetPosition(PopulationGroupState group, float x, float y, float dx, float dy)
    {
        group.X = x;
        group.Y = y;
        if (Math.Abs(dx) + Math.Abs(dy) > 0.0001f)
        {
            group.DirX = dx;
            group.DirY = dy;
        }
        group.HasPosition = true;
    }

    // ------------------------------------------------------------------
    // Бой с группой карты

    // Контакт, бой по которому выбран и ещё не начат.
    public static PopulationContactState BattleToStart(GameState state) =>
        state?.Population?.Contacts?.FirstOrDefault(c => c != null && c.Status == PopulationContactState.BattlePending);

    // Запрос из того же состава, что движется по карте: устойчивые ID
    // участников и их текущие HP; null — сражаться не с кем (контакт закрыт).
    public static CampaignBattleRequest CreateBattleRequest(GameState state, PopulationContactState contact)
    {
        PopulationGroupState group = FindGroup(state, contact?.GroupId);
        if (group == null || !group.IsActive || !group.Members.Any(m => m.Alive))
        {
            if (contact != null)
            {
                contact.Status = PopulationContactState.Resolved;
                contact.Outcome = "no-enemy";
            }
            return null;
        }
        CampaignBattleRequest request = CampaignBattleBridge.CreateRequest(state, contact.BattleId);
        request.SourceId = "population:" + group.GroupId;
        request.AllowRetreat = true;
        request.PreparedStart = contact.Kind == PopulationContactState.CampApproach || contact.Kind == PopulationContactState.ConflictHelp;
        foreach (PopulationMemberState m in group.Members.Where(m => m.Alive).Take(8))
            request.Enemies.Add(new CampaignBattleEnemy { UnitTypeId = m.UnitTypeId, Count = 1, InstanceId = m.MemberId, CurrentHitPoints = m.HitPoints });
        request.Notes.Add("Противник: " + group.Name + ".");
        return request;
    }

    public static void MarkBattleStarted(GameState state, PopulationContactState contact)
    {
        if (contact != null && contact.Status == PopulationContactState.BattlePending)
            contact.Status = PopulationContactState.InBattle;
    }

    // Загрузка: бой, начатый и не вернувшийся (вылет, загрузка предбоевого
    // сохранения), снова ждёт начала; применённый — закрыт.
    public static void OnCampaignLoaded(GameState state)
    {
        if (state?.Population?.Contacts == null)
            return;
        foreach (PopulationContactState c in state.Population.Contacts.Where(c => c != null && c.Status == PopulationContactState.InBattle))
            c.Status = CampaignBattleBridge.IsApplied(state, c.BattleId) ? PopulationContactState.Resolved : PopulationContactState.BattlePending;
        // Решение похода о контакте не сохранилось — контакт не висит вечно.
        foreach (PopulationContactState c in state.Population.Contacts.Where(c => c != null && c.Status == PopulationContactState.Pending))
        {
            if (!IsPopulationDecision(state))
            {
                c.Status = PopulationContactState.Resolved;
                c.Outcome = "lost-on-load";
            }
        }
    }

    // Итог боя с группой карты — вызывается из CampaignBattleBridge.ApplyResult
    // тем же однократным применением (ID боя в NarrativeState).
    public static void ApplyBattle(GameState state, CampaignBattleResult result, List<string> notes = null)
    {
        WorldPopulationStateData data = state?.Population;
        if (data == null || result == null)
            return;
        PopulationContactState contact = data.Contacts.FirstOrDefault(c => c != null && c.BattleId == result.BattleId);
        if (contact == null || contact.Status == PopulationContactState.Resolved)
            return;
        double now = ContinuousSimulationSystem.GetAbsoluteHours(state);
        PopulationGroupState group = FindGroup(state, contact.GroupId);
        contact.Status = PopulationContactState.Resolved;
        contact.Outcome = result.Outcome.ToString();
        data.RespiteUntilHour = Math.Max(data.RespiteUntilHour, now + Settings.RespiteHours);
        if (group == null)
            return;

        foreach (CampaignBattleEnemyRecord record in result.Enemies ?? new List<CampaignBattleEnemyRecord>())
        {
            PopulationMemberState member = group.Members.FirstOrDefault(m => m.MemberId == record.InstanceId);
            if (member == null)
                continue;
            if (record.Defeated || record.HitPoints <= 0)
            {
                member.Alive = false;
                member.HitPoints = 0;
            }
            else
                member.HitPoints = record.HitPoints;
        }

        PopulationConflictState conflict = data.Conflicts.FirstOrDefault(c => c.ConflictId == contact.ConflictId);
        if (!group.Members.Any(m => m.Alive))
        {
            // Исход конфликта фиксируется до гибели группы: Destroy закрывает
            // её конфликт без итога.
            bool conflictOpen = conflict != null && conflict.Status == PopulationConflictState.Open;
            group.ConflictId = string.Empty;
            Destroy(state, group, now, "hero");
            notes?.Add(group.Name + " больше не держит дорогу.");
            if (contact.Kind == PopulationContactState.CampApproach)
            {
                PopulationSourceState source = FindSource(state, contact.SourceId);
                if (source != null)
                {
                    EliminateSource(state, source, now);
                    notes?.Add("Лагерь сожжён: новая вылазка отсюда не выйдет.");
                }
            }
            if (conflictOpen)
            {
                conflict.Status = PopulationConflictState.Resolved;
                conflict.Outcome = "rescued";
                PopulationGroupState defender = FindGroup(state, conflict.DefenderId);
                if (defender != null && defender.IsActive)
                {
                    defender.ConflictId = string.Empty;
                    Calm(defender, now, 12);
                    StartActivity(defender, PopulationGroupState.Return, string.Empty, now, 0);
                    defender.Outcome = "rescued";
                    Chronicle.Record(state, "population.rescued." + defender.GroupId, "Дорога",
                        "Отряд отбил " + (defender.Role == WorldMapGroupRule.Caravan ? "обоз" : defender.Name.ToLowerInvariant()) + " у ватаги. Он продолжает путь.");
                    notes?.Add(defender.Name + " продолжает путь.");
                }
            }
            return;
        }

        // Группа уцелела: остаточный состав, отход и пауза до нового контакта.
        Calm(group, now, Settings.RepeatContactHours);
        if (conflict != null && conflict.Status == PopulationConflictState.Open)
        {
            conflict.ResolveHour = now + 0.25;
            group.Activity = PopulationGroupState.Conflict;
        }
        else
            StartActivity(group, PopulationGroupState.Return, string.Empty, now, 0);
    }

    // ------------------------------------------------------------------
    // Гибель групп, источники, пополнение

    internal static void Destroy(GameState state, PopulationGroupState group, double now, string cause)
    {
        if (group == null || !group.IsActive)
            return;
        group.Status = PopulationGroupState.Destroyed;
        group.Activity = string.Empty;
        group.DestroyedHour = now;
        group.Outcome = cause;
        foreach (PopulationMemberState m in group.Members)
        {
            m.Alive = false;
            m.HitPoints = 0;
        }
        if (!string.IsNullOrEmpty(group.ConflictId))
        {
            PopulationConflictState c = state.Population.Conflicts.FirstOrDefault(x => x.ConflictId == group.ConflictId);
            if (c != null && c.Status == PopulationConflictState.Open)
                c.Status = PopulationConflictState.Resolved;
            group.ConflictId = string.Empty;
        }
        PopulationSourceState source = FindSource(state, group.SourceId);
        WorldPopulationSettings s = Settings;
        if (source != null && source.Status != PopulationSourceState.Eliminated)
        {
            if (source.Kind == GeneratedPopulationSource.Village)
                source.NextRefillHour = Math.Max(source.NextRefillHour, now + s.ReplacementHours);
            else if (source.Reserve > 0)
            {
                source.Status = PopulationSourceState.Weakened;
                source.NextRefillHour = now + (source.Kind == GeneratedPopulationSource.BandCamp ? s.BandRefillHours : s.BeastRefillHours);
            }
            else
                source.Status = PopulationSourceState.Exhausted;
        }
        string what = group.Role == WorldMapGroupRule.Caravan ? "Обоз разграблен" : group.Role == WorldMapGroupRule.Band ? group.Name + " разбита" : group.Name;
        if (group.Role == WorldMapGroupRule.Caravan || group.Role == WorldMapGroupRule.Band || group.Role == WorldMapGroupRule.Patrol || cause == "hero")
            Chronicle.Record(state, "population.destroyed." + group.GroupId, "Дорога",
                cause == "hero" ? "Отряд разбил: " + group.Name.ToLowerInvariant() + "." : what + " — без участия отряда.");
    }

    public static void EliminateSource(GameState state, PopulationSourceState source, double now)
    {
        if (source == null || source.Status == PopulationSourceState.Eliminated)
            return;
        source.Status = PopulationSourceState.Eliminated;
        source.Reserve = 0;
        source.EliminatedHour = now;
        source.Knowledge = Math.Max(source.Knowledge, 2);
        Chronicle.Record(state, "population.eliminated." + source.SourceId, "Дорога",
            source.Kind == GeneratedPopulationSource.BandCamp ? "Лагерь ватаги сожжён. Дорога у этого леса станет спокойнее." : source.Name + " — источник угрозы устранён.");
    }

    // Пополнение: новая группа с новым ID только по резерву, сроку и
    // отсутствию живой группы источника; не на глазах у отряда.
    internal static void Upkeep(GameState state, double now, double heroX, double heroY, bool heroOnMap, WorldMapHexGrid grid)
    {
        WorldPopulationStateData data = state.Population;
        WorldPopulationSettings s = Settings;
        foreach (PopulationSourceState source in data.Sources)
        {
            if (source.Status == PopulationSourceState.Eliminated || source.Status == PopulationSourceState.Exhausted || source.Reserve == 0)
                continue;
            if (now < source.NextRefillHour || source.NextRefillHour <= 0)
                continue;
            // У деревни несколько групп (дозор, обоз, мастер): заменяется
            // та, у определения которой сейчас нет живой группы.
            GeneratedMapActor def = state.GeneratedMap.Actors
                .Where(a => a != null && a.SourceId == source.SourceId && a.State == "Travel")
                .OrderBy(a => a.ActorId, StringComparer.Ordinal)
                .FirstOrDefault(a => !data.Groups.Any(g => g.IsActive && g.DefinitionId == a.ActorId) &&
                                     data.Groups.Any(g => g.DefinitionId == a.ActorId));
            if (def == null)
            {
                source.NextRefillHour = 0;
                continue;
            }
            if (heroOnMap && grid.DistanceHexes((float)heroX, (float)heroY, source.X, source.Y) < 6.0)
            {
                source.NextRefillHour = now + 2.0;
                continue;
            }
            WorldPopulationPreset preset = Settings.Find(def.PresetId) ?? WorldPopulationCatalog.DefaultForRole(s, def.Role);
            source.Spawned++;
            string groupId = def.ActorId + "#" + (source.Spawned + 1);
            List<GeneratedActorMember> members = new List<GeneratedActorMember>();
            Random random = new Random(WorldMapGenerator.StreamSeed(state.GeneratedMap.Seed, "population.refill." + groupId));
            if (preset != null && preset.Members.Count > 0)
            {
                foreach (WorldPopulationMember m in preset.Members)
                {
                    int n = m.Min + random.Next(Math.Max(1, m.Max - m.Min + 1));
                    for (int k = 1; k <= n && members.Count < 8; k++)
                        members.Add(new GeneratedActorMember { MemberId = groupId + ":" + m.UnitTypeId + ":" + k, UnitTypeId = m.UnitTypeId });
                }
            }
            else
                members = def.Members.Select(m => new GeneratedActorMember { MemberId = groupId + ":" + m.MemberId, UnitTypeId = m.UnitTypeId }).ToList();
            PopulationGroupState group = NewGroup(def, groupId, members, now);
            MapPointData at = WorldMapActorMotion.PositionAt(def, now, grid, out float dx, out float dy, out _);
            SetPosition(group, at.XPercent, at.YPercent, dx, dy);
            data.Groups.Add(group);
            if (source.Reserve > 0)
                source.Reserve--;
            source.Status = PopulationSourceState.Active;
            source.NextRefillHour = 0;
        }
    }

    // ------------------------------------------------------------------
    // Разговор с мирной группой (клик по фигуре рядом с отрядом)

    public static bool TryTalk(GameState state, string groupId, float heroX, float heroY, out string title, out string text)
    {
        title = string.Empty;
        text = string.Empty;
        PopulationGroupState group = FindGroup(state, groupId);
        if (group == null || !group.IsActive || state.GeneratedMap == null)
            return false;
        WorldMapHexGrid grid = state.GeneratedMap.CreateGrid();
        double now = ContinuousSimulationSystem.GetAbsoluteHours(state);
        MapPointData at = GroupPosition(state, group, now, grid, out _, out _, out _);
        if (grid.DistanceHexes(heroX, heroY, at.XPercent, at.YPercent) > Settings.TalkHexes)
            return false;
        if (group.Behavior != WorldPopulationBehavior.Peaceful && group.Behavior != WorldPopulationBehavior.Patrol)
            return false;
        title = group.Name;
        List<string> lines = new List<string>();
        if (group.Outcome == "rescued")
            lines.Add("«Если б не вы — лежать бы нашим возам в канаве. Дорога вам открыта».");
        PopulationSourceState camp = state.Population.Sources
            .Where(s => s.Kind == GeneratedPopulationSource.BandCamp)
            .OrderBy(s => grid.DistanceHexes(heroX, heroY, s.X, s.Y)).FirstOrDefault();
        if (camp != null && camp.Status == PopulationSourceState.Eliminated)
            lines.Add("«Ватагу у леса, говорят, выжгли. На дороге спокойнее стало».");
        else if (camp != null && state.Population.Groups.Any(g => g.IsActive && g.SourceId == camp.SourceId))
        {
            camp.Knowledge = Math.Max(camp.Knowledge, 2);
            lines.Add("«Берегитесь ватаги: стоят лагерем " + Direction(heroX, heroY, camp.X, camp.Y) + ", в лесу, выходят к дороге. " +
                      (group.Behavior == WorldPopulationBehavior.Patrol ? "Мы их гоняем, да всех не переловишь»." : "Мы туда только с охраной ходим»."));
        }
        else
            lines.Add("«Про лихих людей в округе не слыхать. Зверь только шалит у опушек».");
        PopulationGroupState beast = ActiveGroups(state)
            .Where(g => (g.Role == WorldMapGroupRule.Beast || g.Role == WorldMapGroupRule.Creature) && WorldPopulationBehavior.IsHostileCapable(g.Behavior))
            .OrderBy(g => grid.DistanceHexes(heroX, heroY, g.X, g.Y)).FirstOrDefault();
        if (beast != null && grid.DistanceHexes(heroX, heroY, beast.X, beast.Y) < 14)
        {
            PopulationSourceState territory = FindSource(state, beast.SourceId);
            if (territory != null)
                territory.Knowledge = Math.Max(territory.Knowledge, 2);
            lines.Add("«" + Direction(heroX, heroY, beast.X, beast.Y, true) + " — " + beast.Name.ToLowerInvariant() + ". Обходите стороной»." );
        }
        if (group.Role == WorldMapGroupRule.Caravan)
            lines.Add("Торговать с обозом прямо на дороге пока нельзя — только в поселении.");
        text = string.Join("\n", lines);
        group.Knowledge = 3;
        return true;
    }

    private static string Direction(float fromX, float fromY, float toX, float toY, bool capital = false)
    {
        double angle = Math.Atan2(-(toY - fromY), toX - fromX) * 180.0 / Math.PI;
        string[] names = { "к востоку", "к северо-востоку", "к северу", "к северо-западу", "к западу", "к юго-западу", "к югу", "к юго-востоку" };
        int index = (int)Math.Round(((angle % 360) + 360) % 360 / 45.0) % 8;
        string word = names[index];
        return capital ? char.ToUpperInvariant(word[0]) + word.Substring(1) : word;
    }
}
