using System;
using System.Collections.Generic;
using System.Linq;

// Бриф «Живое население глобальной карты» (19.11.2026), [РАБОЧЕЕ — ПРОТОТИП].
// Стадия населения генератора: идёт после проходимости, деревень, мест,
// дорог, переправ и групп сторон (BuildFactions) и читает уже созданную
// географию — второго леса, реки или дорожной сети не создаёт.
//   1) составы людских групп мира (дозор, обоз, банда) из пресетов по
//      реальным UnitTypeId Базы существ, источники — деревня или лагерь;
//   2) звериные территории и необычные обитатели по биотопам с жёсткими
//      фильтрами (доступность, биотоп, отступ от старта и услуг, места со
//      своими обитателями) и мягкими предпочтениями;
//   3) отчёт: бюджеты, размещённые и пропущенные пресеты с причинами,
//      дистанция угроз до старта, спокойный ранний путь.
// Отдельные потоки seed: размещение, составы, маршруты — по устойчивым ID
// пресетов и групп; порядок записей каталога и декоративные настройки
// расстановку не двигают. Нет кандидата — группа пропускается с причиной;
// fallback в поле, воду или стартовую точку не делается.
[Serializable]
public sealed class GeneratedPopulationSource
{
    public const string Territory = "territory";
    public const string BandCamp = "band-camp";
    public const string Village = "village";

    public string SourceId = string.Empty;
    public string Kind = Territory;
    public string PresetId = string.Empty;
    public string Name = string.Empty;
    public int Cell;
    public float XPercent;
    public float YPercent;
    public float RadiusHexes;
    public string Biome = string.Empty;
    // Сколько новых групп источник ещё может дать (-1 — без предела: деревня).
    public int Reserve;
    public bool Strong;
    public string Signs = string.Empty;
    public List<string> ActorIds = new List<string>();
}

[Serializable]
public sealed class GeneratedPopulationNote
{
    public string PresetId = string.Empty;
    public bool Placed;
    public string Message = string.Empty;
}

[Serializable]
public sealed class GeneratedPopulationReport
{
    public const string CurrentRulesVersion = "population-1";

    public string RulesVersion = CurrentRulesVersion;
    public bool Enabled;
    public bool CatalogChecked;
    public int LandHexes;
    public int OrdinaryBudget;
    public int CreatureBudget;
    public int StrongBudget;
    // Ближайший враждебный источник к старту, клетки (−1 — угроз нет).
    public float MinThreatDistanceToStart = -1f;
    // Ранний путь старт → ближайшая деревня по дороге: длина и сколько
    // клеток вне всех враждебных территорий.
    public int EarlyRouteHexes;
    public int EarlyRouteCalmHexes;
    public List<GeneratedPopulationNote> Notes = new List<GeneratedPopulationNote>();

    public string Summary()
    {
        int placed = Notes.Count(n => n.Placed);
        return "Население: " + (Enabled ? "включено" : "выключено") + ", правила " + RulesVersion +
               ", суша " + LandHexes + " кл., бюджеты: обычные " + OrdinaryBudget + ", необычные " + CreatureBudget +
               ", сильные " + StrongBudget + "; размещено " + placed + ", пропущено " + (Notes.Count - placed) +
               (MinThreatDistanceToStart >= 0f ? "; ближайшая угроза к старту — " + MinThreatDistanceToStart.ToString("0.#") + " кл." : "; угроз нет") +
               (EarlyRouteHexes > 0 ? "; ранний путь " + EarlyRouteHexes + " кл., спокойных " + EarlyRouteCalmHexes : "") +
               (CatalogChecked ? "" : "; База существ не подключена — проверка существ пропущена");
    }
}

public static partial class WorldMapGenerator
{
    private sealed partial class Builder
    {
        private static readonly string[] RuinKinds = { "abandoned-village", "abandoned-yard", "barrow", "hillfort" };

        private void BuildPopulation()
        {
            WorldPopulationSettings pop = settings.Population ?? WorldPopulationSettings.CreateDefault();
            GeneratedPopulationReport report = result.PopulationReport;
            report.Enabled = pop.Enabled;
            report.CatalogChecked = WorldPopulationCatalog.Availability != null;
            if (!pop.Enabled)
                return;

            bool[] reachable = Reachable();
            int land = 0;
            for (int i = 0; i < count; i++)
                if (reachable[i] && terrain[i] != Water && terrain[i] != Cliffs && terrain[i] != Mountains)
                    land++;
            report.LandHexes = land;
            float scale = Math.Max(0f, pop.ThreatScale);
            report.OrdinaryBudget = Math.Min(pop.OrdinaryThreatBudget, (int)Math.Round(land / (double)pop.LandHexesPerOrdinaryThreat * scale));
            report.CreatureBudget = Math.Min(pop.CreatureBudget, (int)Math.Round(land / (double)pop.LandHexesPerCreature * scale));
            report.StrongBudget = scale > 0f && land >= pop.LandHexesPerOrdinaryThreat ? pop.StrongThreatBudget : 0;

            List<int> earlyRoute = EarlyRoute();
            List<int> threatCenters = new List<int>();
            List<(int Cell, float Radius)> hostileAreas = new List<(int, float)>();

            // Сильная угроза: подготовленная банда (если есть дальний лагерь)
            // или медведь — одна на карту по умолчанию.
            int strongLeft = report.StrongBudget;
            string strongChoice = string.Empty;
            List<GeneratedMapActor> bands = result.Actors.Where(a => a.Role == WorldMapGroupRule.Band && a.State == "Travel")
                .OrderBy(a => a.ActorId, StringComparer.Ordinal).ToList();
            if (strongLeft > 0)
            {
                Random pick = new Random(StreamSeed(seed, "population.pick.strong"));
                List<string> options = new List<string>();
                WorldPopulationPreset prepared = pop.Find("band-prepared");
                if (prepared != null && prepared.Enabled && bands.Any(b => DistanceToStart(b) >= pop.StrongMinStartDistanceHexes))
                    options.Add("band-prepared");
                foreach (WorldPopulationPreset p in pop.Presets.Where(p => p.Enabled && p.Budget == WorldPopulationPreset.Strong && p.Role != WorldMapGroupRule.Band)
                             .OrderBy(p => p.Id, StringComparer.Ordinal))
                    options.Add(p.Id);
                if (options.Count > 0)
                    strongChoice = options[pick.Next(options.Count)];
            }

            // 1. Люди: составы и источники групп сторон.
            int ordinaryBands = 0;
            bool preparedAssigned = false;
            List<GeneratedMapActor> patrols = result.Actors.Where(a => a.Role == WorldMapGroupRule.Patrol).OrderBy(a => a.ActorId, StringComparer.Ordinal).ToList();
            foreach (GeneratedMapActor actor in result.Actors.OrderBy(a => a.ActorId, StringComparer.Ordinal))
            {
                WorldPopulationPreset preset;
                if (actor.Role == WorldMapGroupRule.Band && !preparedAssigned && strongChoice == "band-prepared" &&
                    actor.State == "Travel" && DistanceToStart(actor) >= pop.StrongMinStartDistanceHexes)
                {
                    preset = pop.Find("band-prepared");
                    preparedAssigned = true;
                    strongLeft--;
                }
                else if (actor.Role == WorldMapGroupRule.Patrol && patrols.Count >= 2 && actor == patrols[patrols.Count - 1] && pop.Find("patrol-strong")?.Enabled == true)
                    preset = pop.Find("patrol-strong");
                else
                    preset = WorldPopulationCatalog.DefaultForRole(pop, actor.Role);

                actor.Behavior = actor.Role == WorldMapGroupRule.Master ? WorldPopulationBehavior.Peaceful : preset?.Behavior ?? WorldPopulationBehavior.Peaceful;
                if (actor.Role == WorldMapGroupRule.Band && actor.State != "Travel" && string.IsNullOrEmpty(actor.HomeId))
                {
                    Note(report, preset?.Id ?? actor.RuleId, false, $"Группа «{actor.Name}» ({actor.ActorId}): {actor.BlockReason} — в населении не участвует.");
                    actor.Behavior = string.Empty;
                    continue;
                }
                if (preset != null && actor.Role != WorldMapGroupRule.Master)
                {
                    if (!WorldPopulationCatalog.CheckUnits(preset, out string why))
                    {
                        Note(report, preset.Id, false, $"Группа «{actor.Name}» ({actor.ActorId}): состав «{preset.Name}» не собран — {why}; группа идёт без боевого состава.");
                    }
                    else
                    {
                        actor.PresetId = preset.Id;
                        actor.Strong = preset.Budget == WorldPopulationPreset.Strong;
                        actor.Members = RollMembers(preset, actor.ActorId);
                        actor.UnitArtId = actor.Members.Count > 0 ? actor.Members[0].UnitTypeId : string.Empty;
                        if (actor.Role == WorldMapGroupRule.Band && !actor.Strong)
                            ordinaryBands++;
                        if (actor.Strong && actor.Role == WorldMapGroupRule.Band)
                            actor.Name = preset.Name;
                    }
                }
                actor.TerritoryXPercent = actor.HomeXPercent;
                actor.TerritoryYPercent = actor.HomeYPercent;
                actor.TerritoryRadiusHexes = preset?.ChaseHexes ?? 3f;

                string kind = actor.Role == WorldMapGroupRule.Band ? GeneratedPopulationSource.BandCamp : GeneratedPopulationSource.Village;
                GeneratedPopulationSource source = result.PopulationSources.FirstOrDefault(s => s.SourceId == actor.HomeId);
                if (source == null && !string.IsNullOrEmpty(actor.HomeId))
                {
                    int cell = grid.IndexOf(grid.CellAtPercent(actor.HomeXPercent, actor.HomeYPercent));
                    source = new GeneratedPopulationSource
                    {
                        SourceId = actor.HomeId,
                        Kind = kind,
                        PresetId = preset?.Id ?? string.Empty,
                        Name = kind == GeneratedPopulationSource.BandCamp ? "Лагерь ватаги" : SiteName(actor.HomeId),
                        Cell = cell,
                        XPercent = actor.HomeXPercent,
                        YPercent = actor.HomeYPercent,
                        RadiusHexes = preset?.ChaseHexes ?? 3f,
                        Biome = WorldPopulationBiome.Road,
                        Reserve = kind == GeneratedPopulationSource.BandCamp ? pop.BandReserve : -1,
                        Strong = actor.Strong,
                        Signs = preset?.Signs ?? string.Empty
                    };
                    result.PopulationSources.Add(source);
                    if (kind == GeneratedPopulationSource.BandCamp)
                    {
                        threatCenters.Add(cell);
                        hostileAreas.Add((cell, source.RadiusHexes + (preset?.DetectHexes ?? 3f)));
                    }
                }
                if (source != null)
                {
                    actor.SourceId = source.SourceId;
                    source.ActorIds.Add(actor.ActorId);
                }
                if (preset != null && actor.Members.Count > 0)
                    Note(report, preset.Id, true, $"«{actor.Name}» ({actor.ActorId}): " + string.Join(", ", actor.Members.Select(m => m.UnitTypeId)) + ".");
            }

            // 2. Звери и необычные обитатели по биотопам.
            int[] forestDepth = DistanceField(i => terrain[i] != Forest, 4);
            List<int> localPlaceCells = result.Landmarks.Where(l => !string.IsNullOrEmpty(l.LocationId)).Select(l => l.Cell).ToList();
            List<int> landmarkCells = result.Landmarks.Select(l => l.Cell).ToList();
            List<int> ruinCells = result.Landmarks.Where(l => string.IsNullOrEmpty(l.LocationId) && Array.IndexOf(RuinKinds, l.Kind) >= 0).Select(l => l.Cell).ToList();
            List<int> lairCells = result.Landmarks.Where(l => l.Kind == "lair").Select(l => l.Cell).ToList();
            List<int> villageCells = result.Villages.Select(v => v.Cell).ToList();
            Dictionary<string, int> placedCount = new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<string> exhausted = new HashSet<string>(StringComparer.Ordinal);

            bool TryPlace(WorldPopulationPreset preset)
            {
                if (!WorldPopulationCatalog.CheckUnits(preset, out string why))
                {
                    Note(report, preset.Id, false, $"«{preset.Name}»: {why}.");
                    return false;
                }
                int n = placedCount.TryGetValue(preset.Id, out int c) ? c : 0;
                Random random = new Random(StreamSeed(seed, "population.place." + preset.Id + "." + n));
                int radius = (int)Math.Ceiling(preset.TerritoryRadiusHexes);
                bool hostile = preset.IsHostileCapable;
                int startGap = pop.StartSafeRadiusHexes + radius + 1;
                if (preset.Budget == WorldPopulationPreset.Strong)
                    startGap = Math.Max(startGap, pop.StrongMinStartDistanceHexes);
                int serviceGap = pop.ServiceSafeRadiusHexes + (hostile ? radius : 0);
                List<int> candidates = new List<int>();
                List<double> weights = new List<double>();
                bool anyBiome = false;
                for (int i = 0; i < count; i++)
                {
                    if (!InBiome(preset.Biome, i, forestDepth, ruinCells))
                        continue;
                    anyBiome = true;
                    if (!reachable[i] || InEdgeBand(i) || terrain[i] == Road || terrain[i] == Trail || terrain[i] == Water ||
                        terrain[i] == Cliffs || terrain[i] == Mountains)
                        continue;
                    if (Distance(i, startCell) < startGap)
                        continue;
                    if (villageCells.Count > 0 && NearestDistance(i, villageCells) < serviceGap)
                        continue;
                    if (threatCenters.Count > 0 && NearestDistance(i, threatCenters) < pop.MinSourceDistanceHexes)
                        continue;
                    if (localPlaceCells.Count > 0 && NearestDistance(i, localPlaceCells) <= 1)
                        continue;
                    if (landmarkCells.Contains(i))
                        continue;
                    if (hostile && earlyRoute.Count > 0 && NearestDistance(i, earlyRoute) < radius + 2)
                        continue;
                    double w = 1.0;
                    if (preset.Id == "wolf-pack" && lairCells.Count > 0 && NearestDistance(i, lairCells) <= 3)
                        w *= 4.0;
                    if (preset.Biome == WorldPopulationBiome.DeepForest)
                        w *= 1.0 + Math.Min(4, forestDepth[i]) * 0.5;
                    candidates.Add(i);
                    weights.Add(w);
                }
                if (candidates.Count == 0)
                {
                    Note(report, preset.Id, false, anyBiome
                        ? $"«{preset.Name}»: биотоп «{preset.Biome}» есть, но весь занят отступами от старта, деревень, ранней дороги или других угроз."
                        : $"«{preset.Name}»: на карте нет биотопа «{preset.Biome}».");
                    return false;
                }
                double total = weights.Sum();
                double roll = random.NextDouble() * total;
                int center = candidates[candidates.Count - 1];
                for (int k = 0; k < candidates.Count; k++)
                {
                    roll -= weights[k];
                    if (roll <= 0)
                    {
                        center = candidates[k];
                        break;
                    }
                }

                string actorId = preset.Id + "-" + (n + 1);
                List<int> route = TerritoryRoute(center, radius, actorId);
                if (route.Count < 2)
                {
                    Note(report, preset.Id, false, $"«{preset.Name}»: у клетки {center} нет проходимой территории.");
                    return false;
                }
                placedCount[preset.Id] = n + 1;
                threatCenters.Add(center);
                if (hostile)
                    hostileAreas.Add((center, preset.TerritoryRadiusHexes + preset.WarnHexes));

                grid.CellCenter(grid.CellAt(center), out double cx, out double cy);
                GeneratedMapActor actor = new GeneratedMapActor
                {
                    ActorId = actorId,
                    RuleId = preset.Id,
                    Role = preset.Role,
                    Name = preset.Name,
                    FigureSet = preset.FigureSet ?? string.Empty,
                    Description = preset.Description ?? string.Empty,
                    HomeId = "territory-" + actorId,
                    HomeXPercent = grid.PixelToPercentX(cx),
                    HomeYPercent = grid.PixelToPercentY(cy),
                    RouteCells = route,
                    Route = ToPercent(CellPoints(route)),
                    Loop = true,
                    WaitHours = preset.RestHours,
                    PhaseHours = (float)(new Random(StreamSeed(seed, "population.phase." + actorId)).NextDouble() * 24.0),
                    SourceId = "territory-" + actorId,
                    PresetId = preset.Id,
                    Behavior = preset.Behavior,
                    Members = RollMembers(preset, actorId),
                    TerritoryXPercent = grid.PixelToPercentX(cx),
                    TerritoryYPercent = grid.PixelToPercentY(cy),
                    TerritoryRadiusHexes = preset.TerritoryRadiusHexes,
                    Strong = preset.Budget == WorldPopulationPreset.Strong,
                };
                actor.UnitArtId = actor.Members.Count > 0 ? actor.Members[0].UnitTypeId : string.Empty;
                result.Actors.Add(actor);
                result.PopulationSources.Add(new GeneratedPopulationSource
                {
                    SourceId = actor.SourceId,
                    Kind = GeneratedPopulationSource.Territory,
                    PresetId = preset.Id,
                    Name = preset.Name,
                    Cell = center,
                    XPercent = actor.HomeXPercent,
                    YPercent = actor.HomeYPercent,
                    RadiusHexes = preset.TerritoryRadiusHexes,
                    Biome = preset.Biome,
                    Reserve = preset.Reserve,
                    Strong = actor.Strong,
                    Signs = preset.Signs ?? string.Empty,
                    ActorIds = new List<string> { actorId }
                });
                Note(report, preset.Id, true, $"«{preset.Name}» ({actorId}): {preset.Biome}, клетка {center}, до старта {Distance(center, startCell)} кл.; " +
                                               string.Join(", ", actor.Members.Select(m => m.UnitTypeId)) + ".");
                return true;
            }

            // Сильная (если выбран зверь).
            if (strongLeft > 0 && !string.IsNullOrEmpty(strongChoice) && strongChoice != "band-prepared")
            {
                WorldPopulationPreset strong = pop.Find(strongChoice);
                if (strong != null && TryPlace(strong))
                    strongLeft--;
            }
            foreach (WorldPopulationPreset p in pop.Presets.Where(p => p.Budget == WorldPopulationPreset.Strong && p.Id != strongChoice))
                Note(report, p.Id, false, $"«{p.Name}»: бюджет сильных угроз занят ({(string.IsNullOrEmpty(strongChoice) ? "нет" : strongChoice)}).");

            // Обычные звери: взвешенный выбор среди совместимых пресетов.
            int beastBudget = Math.Max(0, report.OrdinaryBudget - ordinaryBands);
            List<WorldPopulationPreset> ordinary = pop.Presets
                .Where(p => p.Budget == WorldPopulationPreset.Ordinary && p.Role != WorldMapGroupRule.Band && p.Role != WorldMapGroupRule.Patrol && p.Role != WorldMapGroupRule.Caravan)
                .OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            foreach (WorldPopulationPreset p in ordinary.Where(p => !p.Enabled))
                Note(report, p.Id, false, $"«{p.Name}»: пресет выключен. {p.Status}");
            Random ordinaryPick = new Random(StreamSeed(seed, "population.pick.ordinary"));
            for (int k = 0, attempts = 0; k < beastBudget && attempts < beastBudget * 4 + 4; attempts++)
            {
                List<WorldPopulationPreset> open = ordinary.Where(p => p.Enabled && p.Weight > 0 && !exhausted.Contains(p.Id) &&
                                                                       (placedCount.TryGetValue(p.Id, out int c) ? c : 0) < p.MaxPerMap).ToList();
                if (open.Count == 0)
                    break;
                int total = open.Sum(p => p.Weight);
                int roll = ordinaryPick.Next(total);
                WorldPopulationPreset chosen = open[open.Count - 1];
                foreach (WorldPopulationPreset p in open)
                {
                    roll -= p.Weight;
                    if (roll < 0)
                    {
                        chosen = p;
                        break;
                    }
                }
                if (TryPlace(chosen))
                    k++;
                else
                    exhausted.Add(chosen.Id);
            }
            if (beastBudget == 0)
                Note(report, "beasts", false, "Бюджет обычных зверей пуст (малая суша, настройка «Враги» или его заняли банды).");

            // Необычные обитатели: каждый пресет — попытка в порядке потока seed.
            List<WorldPopulationPreset> creatures = pop.Presets.Where(p => p.Budget == WorldPopulationPreset.Creature)
                .OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            Random creaturePick = new Random(StreamSeed(seed, "population.pick.creature"));
            for (int i = creatures.Count - 1; i > 0; i--)
            {
                int j = creaturePick.Next(i + 1);
                (creatures[i], creatures[j]) = (creatures[j], creatures[i]);
            }
            int creatureLeft = report.CreatureBudget;
            foreach (WorldPopulationPreset p in creatures)
            {
                if (!p.Enabled)
                {
                    Note(report, p.Id, false, $"«{p.Name}»: пресет выключен. {p.Status}");
                    continue;
                }
                if (creatureLeft <= 0)
                {
                    Note(report, p.Id, false, $"«{p.Name}»: бюджет необычных обитателей исчерпан.");
                    continue;
                }
                if (TryPlace(p))
                    creatureLeft--;
            }

            // 3. Итог: дистанция угроз до старта и спокойный ранний путь.
            List<int> hostileCells = result.Actors.Where(a => WorldPopulationBehavior.IsHostileCapable(a.Behavior))
                .Select(a => grid.IndexOf(grid.CellAtPercent(a.HomeXPercent, a.HomeYPercent))).ToList();
            report.MinThreatDistanceToStart = hostileCells.Count > 0 ? hostileCells.Min(c => Distance(c, startCell)) : -1f;
            report.EarlyRouteHexes = earlyRoute.Count;
            report.EarlyRouteCalmHexes = earlyRoute.Count(cell => hostileAreas.All(a => Distance(cell, a.Cell) > a.Radius));
            foreach (GeneratedPopulationNote note in report.Notes.Where(n => !n.Placed))
                result.Report.Issues.Add(new GeneratedRegionIssue
                {
                    Code = "P01", Error = false, XPercent = result.StartXPercent, YPercent = result.StartYPercent,
                    Message = "Население: " + note.Message
                });
        }

        private int DistanceToStart(GeneratedMapActor actor) =>
            Distance(grid.IndexOf(grid.CellAtPercent(actor.HomeXPercent, actor.HomeYPercent)), startCell);

        private string SiteName(string homeId)
        {
            if (homeId != null && homeId.StartsWith("village-", StringComparison.Ordinal) &&
                int.TryParse(homeId.Substring(8), out int v) && v >= 0 && v < result.Villages.Count)
                return result.Villages[v].Name;
            return homeId ?? string.Empty;
        }

        private static void Note(GeneratedPopulationReport report, string presetId, bool placed, string message) =>
            report.Notes.Add(new GeneratedPopulationNote { PresetId = presetId ?? string.Empty, Placed = placed, Message = message });

        // Состав: число каждого участника — в пределах Min..Max по потоку
        // seed группы; ID участников устойчивы внутри партии.
        private List<GeneratedActorMember> RollMembers(WorldPopulationPreset preset, string actorId)
        {
            Random random = new Random(StreamSeed(seed, "population.comp." + actorId));
            List<GeneratedActorMember> members = new List<GeneratedActorMember>();
            Dictionary<string, int> perType = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (WorldPopulationMember m in preset.Members)
            {
                int n = m.Min + random.Next(Math.Max(1, m.Max - m.Min + 1));
                for (int k = 0; k < n && members.Count < 8; k++)
                {
                    int index = perType.TryGetValue(m.UnitTypeId, out int c) ? c + 1 : 1;
                    perType[m.UnitTypeId] = index;
                    members.Add(new GeneratedActorMember { MemberId = actorId + ":" + m.UnitTypeId + ":" + index, UnitTypeId = m.UnitTypeId });
                }
            }
            return members;
        }

        private bool InBiome(string biome, int i, int[] forestDepth, List<int> ruinCells)
        {
            byte t = terrain[i];
            switch (biome)
            {
                case WorldPopulationBiome.ForestEdge:
                    return t == Forest && HasNeighbor(i, n => terrain[n] == OpenGround || terrain[n] == Field || terrain[n] == Hills || terrain[n] == Road || terrain[n] == Trail);
                case WorldPopulationBiome.Underbrush:
                    return (t == Forest && forestDepth[i] <= 1) || ((t == OpenGround || t == Hills) && HasNeighbor(i, n => terrain[n] == Forest));
                case WorldPopulationBiome.DeepForest:
                    return t == Forest && forestDepth[i] >= 3;
                case WorldPopulationBiome.Shore:
                    return t != Water && t != Swamp && waterDistance != null && waterDistance[i] == 1;
                case WorldPopulationBiome.Swamp:
                    return t == Swamp && waterDistance != null && waterDistance[i] <= 3;
                case WorldPopulationBiome.Rocky:
                    return (t == Hills || t == OpenGround) && HasNeighbor(i, n => terrain[n] == Cliffs || terrain[n] == Mountains);
                case WorldPopulationBiome.Ruins:
                    return ruinCells.Count > 0 && NearestDistance(i, ruinCells) <= 2 && !ruinCells.Contains(i);
                default:
                    return false;
            }
        }

        private bool HasNeighbor(int cell, Func<int, bool> test)
        {
            for (int d = 0; d < 6; d++)
            {
                int n = neighbors[cell * 6 + d];
                if (n >= 0 && test(n))
                    return true;
            }
            return false;
        }

        // Распорядок зверя: от места отдыха к двум точкам кормления/обхода
        // внутри территории и обратно (поиск ограничен радиусом).
        private List<int> TerritoryRoute(int center, int radius, string actorId)
        {
            int[] parent = new int[count];
            int[] depth = new int[count];
            for (int i = 0; i < count; i++)
                depth[i] = -1;
            List<int> area = new List<int>();
            Queue<int> queue = new Queue<int>();
            depth[center] = 0;
            parent[center] = -1;
            queue.Enqueue(center);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                area.Add(c);
                if (depth[c] >= radius)
                    continue;
                for (int d = 0; d < 6; d++)
                {
                    int n = neighbors[c * 6 + d];
                    if (n < 0 || depth[n] >= 0 || terrain[n] == Water || terrain[n] == Cliffs || terrain[n] == Mountains)
                        continue;
                    depth[n] = depth[c] + 1;
                    parent[n] = c;
                    queue.Enqueue(n);
                }
            }
            List<int> far = area.Where(c => depth[c] >= Math.Max(1, radius / 2)).ToList();
            if (far.Count == 0)
                return new List<int> { center };
            Random random = new Random(StreamSeed(seed, "population.route." + actorId));
            List<int> route = new List<int> { center };
            for (int leg = 0; leg < 2; leg++)
            {
                int target = far[random.Next(far.Count)];
                List<int> path = new List<int>();
                for (int c = target; c >= 0; c = parent[c])
                    path.Add(c);
                path.Reverse();
                route.AddRange(path.Skip(1));
                path.Reverse();
                route.AddRange(path.Skip(1));
            }
            return route;
        }

        // Ранний путь новой партии: от ближайшей к старту дороги до ближайшей
        // по дороге деревни. На нём обычные звериные территории не ставятся.
        private List<int> EarlyRoute()
        {
            int nearestRoad = -1, best = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (terrain[i] != Road)
                    continue;
                int d = Distance(i, startCell);
                if (d < best)
                {
                    best = d;
                    nearestRoad = i;
                }
            }
            if (nearestRoad < 0 || result.Villages.Count == 0)
                return new List<int>();
            int[] distance = RoadBfs(nearestRoad, out _);
            int village = -1, villageDistance = int.MaxValue;
            for (int v = 0; v < result.Villages.Count; v++)
            {
                int d = distance[result.Villages[v].Cell];
                if (d >= 0 && d < villageDistance)
                {
                    villageDistance = d;
                    village = v;
                }
            }
            return village < 0 ? new List<int>() : RoadPath(nearestRoad, result.Villages[village].Cell) ?? new List<int>();
        }
    }
}
