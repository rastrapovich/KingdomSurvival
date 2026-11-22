using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф «Поверхности и окружение боевой арены», этап 4: темы арены.
    // Расширение прежнего генератора SandboxRoster (тот же формат арены 10×7,
    // те же места спавна); без темы бой строится прежним способом, поэтому
    // старые бои и тесты с seed получают прежние холмы и преграды.
    public enum SandboxArenaTheme
    {
        DryGlade,
        ForestEdge,
        BogGlade,
        Ford,
        FordBridge,
        RockySlope,
        RuinedHillfort,
        WinterGlade,
        // Авторский вариант: явно заданный обрядовый камень в центре.
        RitualGlade
    }

    public static class SandboxArenaThemes
    {
        // Обычные пресеты (мост — вариант берегового пресета).
        public static readonly SandboxArenaTheme[] Ordinary =
        {
            SandboxArenaTheme.DryGlade,
            SandboxArenaTheme.ForestEdge,
            SandboxArenaTheme.BogGlade,
            SandboxArenaTheme.Ford,
            SandboxArenaTheme.FordBridge,
            SandboxArenaTheme.RockySlope,
            SandboxArenaTheme.RuinedHillfort,
            SandboxArenaTheme.WinterGlade
        };

        public static IEnumerable<SandboxArenaTheme> All =>
            Enum.GetValues(typeof(SandboxArenaTheme)).Cast<SandboxArenaTheme>();

        public static string Id(SandboxArenaTheme theme)
        {
            switch (theme)
            {
                case SandboxArenaTheme.DryGlade: return "dry_glade";
                case SandboxArenaTheme.ForestEdge: return "forest_edge";
                case SandboxArenaTheme.BogGlade: return "bog_glade";
                case SandboxArenaTheme.Ford: return "ford";
                case SandboxArenaTheme.FordBridge: return "ford_bridge";
                case SandboxArenaTheme.RockySlope: return "rocky_slope";
                case SandboxArenaTheme.RuinedHillfort: return "ruined_hillfort";
                case SandboxArenaTheme.WinterGlade: return "winter_glade";
                case SandboxArenaTheme.RitualGlade: return "ritual_glade";
                default: return theme.ToString();
            }
        }

        public static string Label(SandboxArenaTheme theme)
        {
            switch (theme)
            {
                case SandboxArenaTheme.DryGlade: return "Сухая поляна";
                case SandboxArenaTheme.ForestEdge: return "Лесная опушка";
                case SandboxArenaTheme.BogGlade: return "Болотная поляна";
                case SandboxArenaTheme.Ford: return "Берег и брод";
                case SandboxArenaTheme.FordBridge: return "Берег с мостом";
                case SandboxArenaTheme.RockySlope: return "Каменистый склон";
                case SandboxArenaTheme.RuinedHillfort: return "Разрушенное городище";
                case SandboxArenaTheme.WinterGlade: return "Зимняя поляна";
                case SandboxArenaTheme.RitualGlade: return "Обрядовое место";
                default: return theme.ToString();
            }
        }

        public static bool TryParse(string id, out SandboxArenaTheme theme)
        {
            foreach (SandboxArenaTheme candidate in All)
            {
                if (string.Equals(Id(candidate), id, StringComparison.OrdinalIgnoreCase))
                {
                    theme = candidate;
                    return true;
                }
            }
            theme = SandboxArenaTheme.DryGlade;
            return false;
        }
    }

    // Настройки генератора тем (доли клеток — не художественное правило
    // 70/25/5). Правятся в окне «Генератор арен» (ассет ArenaGeneratorAsset),
    // игра берёт их из Current. Значения по умолчанию дают прежние арены.
    [Serializable]
    public sealed class SandboxArenaGenerationSettings
    {
        private static SandboxArenaGenerationSettings current = new SandboxArenaGenerationSettings();

        public static SandboxArenaGenerationSettings Current
        {
            get => current;
            set { current = value ?? new SandboxArenaGenerationSettings(); Resolver = null; }
        }

        // Настройки по теме (окно «Генератор арен»: у темы могут быть свои). Нет
        // разрешителя — общие Current. Присвоение Current разрешитель сбрасывает.
        public static Func<SandboxArenaTheme, SandboxArenaGenerationSettings> Resolver;

        public static SandboxArenaGenerationSettings For(SandboxArenaTheme theme) => Resolver?.Invoke(theme) ?? current;

        public float FreeShare = 0.60f;
        public float SpecialShare = 0.25f;
        public float ObstacleShare = 0.15f;
        public int MaxAttempts = 48;
        // Кратчайший путь сближения не длиннее прямой × этот множитель.
        public float MaxDetourFactor = 2.2f;

        // Объём частей рецептов (1 — как задумано темой): холмы, кустарник и
        // лес, особое основание (топь, грязь, мелководье, щебень, снег, лёд),
        // преграды (валуны, стволы, завалы, стенки).
        public float HillAmount = 1f;
        public float VegetationAmount = 1f;
        public float GroundAmount = 1f;
        public float ObstacleAmount = 1f;

        // Река (берег и брод, берег с мостом): изгиб русла в клетках вбок от
        // прямой, ширина русла (1–2 клетки), число бродов (1–3).
        public int RiverBend = 2;
        public int RiverWidth = 1;
        public int FordCount = 2;

        public SandboxArenaGenerationSettings Clone() => (SandboxArenaGenerationSettings)MemberwiseClone();
    }

    // Что объём части рецепта меняет.
    public enum SandboxArenaPart
    {
        Hills,
        Vegetation,
        Ground
    }

    // Диагностика генерации: тема, источник выбора, seed, попытки, доли,
    // намеренные исключения общего правила и найденные ошибки.
    public sealed class SandboxArenaReport
    {
        public SandboxArenaTheme Theme;
        public int Seed;
        public string Source = string.Empty;
        public int Attempts;
        public bool Simplified;
        public bool BareFallback;
        public string FallbackReason = string.Empty;
        public int FreeCells;
        public int SpecialCells;
        public int ObstacleCells;
        public int HillsNearPlayer;
        public int HillsNearEnemy;
        public bool SecondApproach;
        public readonly List<string> Intentional = new List<string>();
        public readonly List<string> Issues = new List<string>();

        public int TotalCells => FreeCells + SpecialCells + ObstacleCells;

        public string Summary()
        {
            int total = Math.Max(1, TotalCells);
            string text = SandboxArenaThemes.Label(Theme) + " (seed " + Seed + ", попыток " + Attempts + ")" +
                          "; источник: " + (string.IsNullOrEmpty(Source) ? "не указан" : Source) +
                          "; свободно " + Percent(FreeCells, total) + ", особая местность " + Percent(SpecialCells, total) +
                          ", преграды " + Percent(ObstacleCells, total) +
                          "; высоты у отряда/противника " + HillsNearPlayer + "/" + HillsNearEnemy +
                          "; второй подход: " + (SecondApproach ? "есть" : "нет");
            if (Simplified)
                text += "; упрощённый вариант: " + FallbackReason;
            if (BareFallback)
                text += "; без особой местности: " + FallbackReason;
            if (Intentional.Count > 0)
                text += "; намеренно: " + string.Join("; ", Intentional);
            if (Issues.Count > 0)
                text += "; ошибки: " + string.Join("; ", Issues);
            return text;
        }

        private static string Percent(int value, int total) => (int)Math.Round(value * 100.0 / total) + "%";
    }

    // Итог генерации: всё, что ядро боя кладёт в бой до Start().
    public sealed class SandboxArenaPlan
    {
        public SandboxArenaTheme Theme;
        public readonly Dictionary<HexCoord, SandboxTerrain> Terrain = new Dictionary<HexCoord, SandboxTerrain>();
        public readonly Dictionary<HexCoord, SandboxGround> Ground = new Dictionary<HexCoord, SandboxGround>();
        public readonly Dictionary<HexCoord, SandboxVegetation> Vegetation = new Dictionary<HexCoord, SandboxVegetation>();
        public readonly List<SandboxLayoutObject> Objects = new List<SandboxLayoutObject>();
        public SandboxArenaReport Report = new SandboxArenaReport();

        public void ApplyTo(SandboxBattle battle)
        {
            foreach (KeyValuePair<HexCoord, SandboxGround> pair in Ground)
                battle.SetGround(pair.Key, pair.Value);
            foreach (KeyValuePair<HexCoord, SandboxVegetation> pair in Vegetation)
                battle.SetVegetation(pair.Key, pair.Value);
            foreach (SandboxLayoutObject placed in Objects)
                battle.PlaceTerrainObject(placed.Kind, placed.Cells, placed.HitPoints);
            battle.ArenaReport = Report;
        }

        // Клетка закрыта для обычной наземной пехоты.
        public bool Blocks(HexCoord cell)
        {
            if (!SandboxArenaShape.Contains(cell))
                return true;
            if (Terrain.TryGetValue(cell, out SandboxTerrain terrain) && terrain == SandboxTerrain.Impassable)
                return true;
            SandboxLayoutObject item = ObjectAt(cell);
            if (item != null)
                return SandboxTerrainObjects.Get(item.Kind).BlocksMovement;
            return Ground.TryGetValue(cell, out SandboxGround ground) &&
                   (ground == SandboxGround.DeepWater || ground == SandboxGround.Cliff);
        }

        public SandboxLayoutObject ObjectAt(HexCoord cell) => Objects.FirstOrDefault(item => item.Cells.Contains(cell));

        public bool IsSpecial(HexCoord cell)
        {
            if (Blocks(cell))
                return false;
            if (Terrain.TryGetValue(cell, out SandboxTerrain terrain) && terrain == SandboxTerrain.Difficult)
                return true;
            if (Vegetation.ContainsKey(cell) || ObjectAt(cell) != null)
                return true;
            return Ground.TryGetValue(cell, out SandboxGround ground) && ground != SandboxGround.Default &&
                   ground != SandboxGround.Ground;
        }

        // Цена шага обычной пехоты (для проверки «нет непомерного обхода»).
        public int StepCost(HexCoord cell)
        {
            SandboxSurfaceRules rules = SandboxSurfaceRules.Current;
            int cost = 1;
            if (Terrain.TryGetValue(cell, out SandboxTerrain terrain) && terrain == SandboxTerrain.Difficult)
                cost = Math.Max(cost, rules.HillCost);
            SandboxLayoutObject item = ObjectAt(cell);
            if (item != null && item.Kind == SandboxObjectKind.Bridge)
                return 1;
            if (item != null)
                cost = Math.Max(cost, SandboxTerrainObjects.Get(item.Kind).StepCost);
            if (Vegetation.TryGetValue(cell, out SandboxVegetation vegetation))
                cost = Math.Max(cost, vegetation == SandboxVegetation.Forest ? rules.ForestCost : rules.BrushCost);
            if (Ground.TryGetValue(cell, out SandboxGround ground) && ground != SandboxGround.Default && ground != SandboxGround.Ground)
            {
                switch (ground)
                {
                    case SandboxGround.Mud: cost = Math.Max(cost, rules.MudCost); break;
                    case SandboxGround.Bog: cost = Math.Max(cost, rules.BogCost); break;
                    case SandboxGround.ShallowWater: cost = Math.Max(cost, rules.ShallowWaterCost); break;
                    case SandboxGround.Rubble: cost = Math.Max(cost, rules.RubbleCost); break;
                    case SandboxGround.DeepSnow: cost = Math.Max(cost, rules.DeepSnowCost); break;
                    case SandboxGround.Ice: cost = Math.Max(cost, rules.IceCost); break;
                }
            }
            return Math.Max(1, cost);
        }
    }

    public static class SandboxArenaGenerator
    {
        private static readonly HexCoord ArenaCenter = new HexCoord(5, 3);

        public static SandboxArenaPlan Generate(
            SandboxArenaTheme theme,
            int seed,
            IReadOnlyCollection<SandboxUnitState> units,
            ICollection<HexCoord> blocked = null,
            string source = null)
        {
            SandboxArenaGenerationSettings settings = SandboxArenaGenerationSettings.For(theme);
            HashSet<HexCoord> disabled = blocked != null ? new HashSet<HexCoord>(blocked) : new HashSet<HexCoord>();
            List<HexCoord> players = units.Where(u => u.Team == SandboxTeam.Player).Select(u => u.Position).ToList();
            List<HexCoord> enemies = units.Where(u => u.Team != SandboxTeam.Player).Select(u => u.Position).ToList();
            int attempts = Math.Max(1, settings.MaxAttempts);
            List<string> lastIssues = new List<string>();

            // Полный вариант, затем заранее проверенный упрощённый того же
            // пресета, затем — честно — без особой местности.
            for (int pass = 0; pass < 2; pass++)
            {
                bool simple = pass == 1;
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    Random random = new Random(Mix(seed, (int)theme * 7919 + attempt * 31 + (simple ? 100003 : 0)));
                    SandboxArenaPlan plan = BasePlan(theme, disabled);
                    Builder builder = new Builder(plan, random, players, enemies, settings, simple);
                    Recipe(theme, builder);
                    List<string> issues = Validate(plan, players, enemies, settings, theme);
                    if (issues.Count == 0)
                    {
                        Finish(plan, seed, source, attempt + 1 + (simple ? attempts : 0), players, enemies);
                        if (simple)
                        {
                            plan.Report.Simplified = true;
                            plan.Report.FallbackReason = "полный вариант не прошёл проверку (" + string.Join("; ", lastIssues) + ")";
                        }
                        return plan;
                    }
                    lastIssues = issues;
                }
            }

            SandboxArenaPlan bare = BasePlan(theme, disabled);
            Finish(bare, seed, source, attempts * 2, players, enemies);
            bare.Report.BareFallback = true;
            bare.Report.FallbackReason = "ни один вариант пресета не прошёл проверку: " + string.Join("; ", lastIssues);
            List<string> bareIssues = Validate(bare, players, enemies, settings, theme);
            bare.Report.Issues.AddRange(bareIssues.Where(issue => !issue.StartsWith("нет второго подхода", StringComparison.Ordinal)));
            return bare;
        }

        private static SandboxArenaPlan BasePlan(SandboxArenaTheme theme, HashSet<HexCoord> disabled)
        {
            SandboxArenaPlan plan = new SandboxArenaPlan { Theme = theme };
            plan.Report.Theme = theme;
            foreach (HexCoord inactive in SandboxArenaShape.InactiveCells())
                plan.Terrain[inactive] = SandboxTerrain.Impassable;
            foreach (HexCoord cell in disabled)
                plan.Terrain[cell] = SandboxTerrain.Impassable;
            return plan;
        }

        private static void Finish(SandboxArenaPlan plan, int seed, string source, int attempts, List<HexCoord> players, List<HexCoord> enemies)
        {
            SandboxArenaReport report = plan.Report;
            report.Seed = seed;
            report.Source = source ?? string.Empty;
            report.Attempts = attempts;
            foreach (HexCoord cell in SandboxArenaShape.Cells())
            {
                if (plan.Terrain.TryGetValue(cell, out SandboxTerrain t) && t == SandboxTerrain.Impassable && !plan.Ground.ContainsKey(cell) && plan.ObjectAt(cell) == null)
                    continue; // отключённый гекс поля — не часть раскладки
                if (plan.Blocks(cell))
                    report.ObstacleCells++;
                else if (plan.IsSpecial(cell))
                    report.SpecialCells++;
                else
                    report.FreeCells++;
            }
            foreach (HexCoord cell in SandboxArenaShape.Cells().Where(c => plan.Terrain.TryGetValue(c, out SandboxTerrain t) && t == SandboxTerrain.Difficult))
            {
                int toPlayer = players.Count == 0 ? 99 : players.Min(p => p.DistanceTo(cell));
                int toEnemy = enemies.Count == 0 ? 99 : enemies.Min(p => p.DistanceTo(cell));
                if (toPlayer < toEnemy)
                    report.HillsNearPlayer++;
                else if (toEnemy < toPlayer)
                    report.HillsNearEnemy++;
            }
            report.SecondApproach = HasSecondApproach(plan, players, enemies);
            if (plan.Theme == SandboxArenaTheme.FordBridge)
                AddOnce(report.Intentional, "единственный проход — мост");
            if (plan.Theme == SandboxArenaTheme.RitualGlade)
                AddOnce(report.Intentional, "обрядовый камень в центре — авторский объект");
        }

        private static void AddOnce(List<string> list, string text)
        {
            if (!list.Contains(text))
                list.Add(text);
        }

        // ---------- Проверка раскладки ----------

        public static List<string> Validate(SandboxArenaPlan plan, List<HexCoord> players, List<HexCoord> enemies,
            SandboxArenaGenerationSettings settings, SandboxArenaTheme theme)
        {
            List<string> issues = new List<string>();
            foreach (HexCoord spawn in players.Concat(enemies))
            {
                if (plan.Blocks(spawn))
                    issues.Add("фигура на закрытой клетке " + spawn);
            }
            if (issues.Count > 0)
                return issues;

            HexCoord start = players.Count > 0 ? players[0] : enemies[0];
            HashSet<HexCoord> region = Region(plan, start, null);
            foreach (HexCoord spawn in players.Concat(enemies))
            {
                if (!region.Contains(spawn))
                {
                    issues.Add("стороны не связаны проходом");
                    return issues;
                }
            }
            // Нет тупиковых карманов: каждая проходимая клетка достижима.
            int pockets = SandboxArenaShape.Cells().Count(cell => !plan.Blocks(cell) && !region.Contains(cell));
            if (pockets > 0)
                issues.Add("отрезанные клетки: " + pockets);

            // Путь сближения без непомерного обхода.
            if (players.Count > 0 && enemies.Count > 0)
            {
                Dictionary<HexCoord, int> cost = Costs(plan, players[0], null);
                int best = enemies.Where(cost.ContainsKey).Select(e => cost[e]).DefaultIfEmpty(int.MaxValue).Min();
                int straight = enemies.Min(e => e.DistanceTo(players[0]));
                if (best == int.MaxValue || best > straight * settings.MaxDetourFactor + 2)
                    issues.Add("слишком длинный обход (" + best + " при прямой " + straight + ")");
            }

            // Высоты не отданы одной стороне.
            int nearPlayer = 0, nearEnemy = 0;
            foreach (HexCoord cell in SandboxArenaShape.Cells().Where(c => plan.Terrain.TryGetValue(c, out SandboxTerrain t) && t == SandboxTerrain.Difficult))
            {
                int toPlayer = players.Count == 0 ? 99 : players.Min(p => p.DistanceTo(cell));
                int toEnemy = enemies.Count == 0 ? 99 : enemies.Min(p => p.DistanceTo(cell));
                if (toPlayer < toEnemy) nearPlayer++;
                else if (toEnemy < toPlayer) nearEnemy++;
            }
            if (Math.Abs(nearPlayer - nearEnemy) > Math.Max(2, (nearPlayer + nearEnemy) / 2))
                issues.Add("высоты у одной стороны (" + nearPlayer + "/" + nearEnemy + ")");

            if (theme != SandboxArenaTheme.FordBridge && !HasSecondApproach(plan, players, enemies))
                issues.Add("нет второго подхода");

            int obstacles = SandboxArenaShape.Cells().Count(plan.Blocks) - plan.Terrain.Count(pair => pair.Value == SandboxTerrain.Impassable && SandboxArenaShape.Contains(pair.Key));
            if (obstacles > SandboxArenaShape.CellCount * (settings.ObstacleShare + 0.10f))
                issues.Add("слишком много преград: " + obstacles);
            return issues;
        }

        // Второй подход: если перекрыть середину кратчайшего пути, стороны
        // всё ещё связаны.
        private static bool HasSecondApproach(SandboxArenaPlan plan, List<HexCoord> players, List<HexCoord> enemies)
        {
            if (players.Count == 0 || enemies.Count == 0)
                return true;
            List<HexCoord> path = ShortestPath(plan, players[0], enemies[0], null);
            if (path == null)
                return false;
            HashSet<HexCoord> cut = new HashSet<HexCoord>(path.Where(cell =>
                players.All(p => p.DistanceTo(cell) > 2) && enemies.All(e => e.DistanceTo(cell) > 2)));
            if (cut.Count == 0)
                return true;
            return Region(plan, players[0], cut).Contains(enemies[0]);
        }

        private static HashSet<HexCoord> Region(SandboxArenaPlan plan, HexCoord start, HashSet<HexCoord> extraBlocked)
        {
            HashSet<HexCoord> visited = new HashSet<HexCoord> { start };
            Queue<HexCoord> frontier = new Queue<HexCoord>();
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                HexCoord current = frontier.Dequeue();
                foreach (HexCoord next in current.Neighbors())
                {
                    if (visited.Contains(next) || plan.Blocks(next) || (extraBlocked != null && extraBlocked.Contains(next)))
                        continue;
                    visited.Add(next);
                    frontier.Enqueue(next);
                }
            }
            return visited;
        }

        private static Dictionary<HexCoord, int> Costs(SandboxArenaPlan plan, HexCoord start, Dictionary<HexCoord, HexCoord> parents)
        {
            Dictionary<HexCoord, int> cost = new Dictionary<HexCoord, int> { { start, 0 } };
            SortedSet<(int, HexCoord)> open = new SortedSet<(int, HexCoord)> { (0, start) };
            while (open.Count > 0)
            {
                (int value, HexCoord current) = open.Min;
                open.Remove(open.Min);
                if (value > cost[current])
                    continue;
                foreach (HexCoord next in current.Neighbors())
                {
                    if (plan.Blocks(next))
                        continue;
                    int candidate = value + plan.StepCost(next);
                    if (cost.TryGetValue(next, out int known) && known <= candidate)
                        continue;
                    if (cost.ContainsKey(next))
                        open.Remove((known, next));
                    cost[next] = candidate;
                    if (parents != null)
                        parents[next] = current;
                    open.Add((candidate, next));
                }
            }
            return cost;
        }

        private static List<HexCoord> ShortestPath(SandboxArenaPlan plan, HexCoord from, HexCoord to, HashSet<HexCoord> extraBlocked)
        {
            Dictionary<HexCoord, HexCoord> parents = new Dictionary<HexCoord, HexCoord>();
            Dictionary<HexCoord, int> cost = Costs(plan, from, parents);
            if (!cost.ContainsKey(to))
                return null;
            List<HexCoord> path = new List<HexCoord> { to };
            HexCoord cursor = to;
            while (cursor != from)
            {
                cursor = parents[cursor];
                path.Add(cursor);
            }
            path.Reverse();
            return path;
        }

        // ---------- Рецепты тем ----------

        private static void Recipe(SandboxArenaTheme theme, Builder b)
        {
            switch (theme)
            {
                case SandboxArenaTheme.DryGlade: DryGlade(b, false); break;
                case SandboxArenaTheme.RitualGlade: DryGlade(b, true); break;
                case SandboxArenaTheme.ForestEdge: ForestEdge(b); break;
                case SandboxArenaTheme.BogGlade: BogGlade(b); break;
                case SandboxArenaTheme.Ford: Ford(b, false); break;
                case SandboxArenaTheme.FordBridge: Ford(b, true); break;
                case SandboxArenaTheme.RockySlope: RockySlope(b); break;
                case SandboxArenaTheme.RuinedHillfort: RuinedHillfort(b); break;
                case SandboxArenaTheme.WinterGlade: WinterGlade(b); break;
            }
        }

        // Сухая поляна: холмы, кустарник, поваленное дерево; валуны.
        private static void DryGlade(Builder b, bool ritual)
        {
            if (ritual)
            {
                b.Object(SandboxObjectKind.RitualStone, ArenaCenter);
                foreach (HexCoord cell in ArenaCenter.Neighbors().Where(b.Free).Where((_, i) => i % 2 == 0))
                    b.Vegetation(cell, SandboxVegetation.Brush);
            }
            int hills = b.Special(0.45, SandboxArenaPart.Hills);
            b.Hills(b.Middle(), hills / 2 + hills % 2);
            b.Hills(b.Middle(), hills / 2);
            b.Brush(b.Middle(), b.Special(0.30, SandboxArenaPart.Vegetation), SandboxVegetation.Brush);
            b.FallenLogs(b.Simple ? 1 : 2);
            b.Scatter(SandboxObjectKind.Boulder, b.Obstacles(0.75), 2);
            b.Scatter(SandboxObjectKind.Deadfall, b.Obstacles(0.25), 1);
        }

        // Лесная опушка: полоса леса вдоль длинного края, заросли у кромки,
        // толстые стволы в лесу, завал у кромки.
        private static void ForestEdge(Builder b)
        {
            bool top = b.Rng.Next(2) == 0;
            int row = top ? 1 : 5;
            List<HexCoord> forest = b.Blob(new HexCoord(3 + b.Rng.Next(4), row), b.Special(0.55, SandboxArenaPart.Vegetation), cell => top ? cell.R <= 2 : cell.R >= 4);
            foreach (HexCoord cell in forest)
                b.Vegetation(cell, SandboxVegetation.Forest);
            List<HexCoord> edge = forest.SelectMany(c => c.Neighbors()).Where(b.Free).Distinct().OrderBy(c => c).ToList();
            HexCoord brushStart = edge.Count > 0 ? edge[b.Rng.Next(edge.Count)] : b.Middle();
            b.Brush(brushStart, b.Special(0.30, SandboxArenaPart.Vegetation), SandboxVegetation.Brush);
            int trunks = b.Obstacles(0.6);
            foreach (HexCoord cell in forest.OrderBy(_ => b.Rng.Next()).Take(trunks))
                b.Object(SandboxObjectKind.Trunk, cell);
            b.Scatter(SandboxObjectKind.Deadfall, b.Obstacles(0.4), 1, cell => edge.Contains(cell));
            b.FallenLogs(1);
        }

        // Болотная поляна: топь в центре, мелководье и грязь по краям,
        // камыш; небольшой глубокий омут, коряги.
        private static void BogGlade(Builder b)
        {
            HexCoord center = new HexCoord(4 + b.Rng.Next(2), 2 + b.Rng.Next(3));
            List<HexCoord> bog = b.Blob(center, b.Special(0.45), b.Free);
            foreach (HexCoord cell in bog)
                b.Ground(cell, SandboxGround.Bog);
            int pool = b.Simple ? 0 : Math.Min(3, b.Obstacles(0.45));
            if (pool > 0)
            {
                List<HexCoord> deep = b.Blob(bog[b.Rng.Next(bog.Count)], pool, cell => bog.Contains(cell) || b.Free(cell));
                foreach (HexCoord cell in deep)
                    b.Ground(cell, SandboxGround.DeepWater);
            }
            List<HexCoord> rim = bog.SelectMany(c => c.Neighbors()).Where(b.Free).Distinct().OrderBy(c => c).ToList();
            int shallow = b.Special(0.20), mud = b.Special(0.20);
            foreach (HexCoord cell in rim.OrderBy(_ => b.Rng.Next()).Take(shallow))
                b.Ground(cell, SandboxGround.ShallowWater);
            foreach (HexCoord cell in rim.Where(b.Free).OrderBy(_ => b.Rng.Next()).Take(mud))
                b.Ground(cell, SandboxGround.Mud);
            b.Brush(b.Anywhere(), b.Special(0.15, SandboxArenaPart.Vegetation), SandboxVegetation.Brush);
            b.Scatter(SandboxObjectKind.Trunk, b.Obstacles(0.3), 1);
            b.Scatter(SandboxObjectKind.Deadfall, b.Obstacles(0.25), 1);
        }

        // Берег и брод: глубокая река сверху донизу, два брода (короткий
        // открытый в середине и дальний у края), грязь и камыш по берегу.
        // Вариант с мостом: единственный проход — мост (намеренно).
        private static void Ford(Builder b, bool bridge)
        {
            HexCoord top = new HexCoord(4 + b.Rng.Next(3), 0);
            HexCoord bottom = new HexCoord(3 + b.Rng.Next(3), SandboxArenaShape.Height - 1);
            // Русло с изгибом: через точку в середине поля, смещённую вбок
            // (изгиб — настройка «Изгиб реки», 0 — прямое русло).
            int bendSize = Math.Max(0, Math.Min(4, b.Settings.RiverBend));
            int bend = b.Rng.Next(2) == 0 ? -bendSize : bendSize;
            HexCoord turn = new HexCoord(Math.Max(2, Math.Min(SandboxArenaShape.Width - 3, (top.Q + bottom.Q) / 2 + bend)), SandboxArenaShape.Height / 2);
            List<HexCoord> river = SandboxHexLine.Line(top, turn).Concat(SandboxHexLine.Line(turn, bottom)).Distinct().Where(SandboxArenaShape.Contains).ToList();
            // Ширина русла 2 — соседняя клетка ряда справа.
            if (b.Settings.RiverWidth >= 2)
                river = river.Concat(river.Select(c => new HexCoord(c.Q + 1, c.R))).Distinct().Where(SandboxArenaShape.Contains).ToList();
            bool wide = b.Settings.RiverWidth >= 2;
            foreach (HexCoord cell in river)
                b.Ground(cell, SandboxGround.DeepWater);
            List<HexCoord> middle = river.Where(c => c.R >= 2 && c.R <= 4).ToList();
            List<HexCoord> far = river.Where(c => c.R <= 1 || c.R >= 5).ToList();
            // Широкое русло переходится только всем рядом: брод и мост — на ряд.
            List<HexCoord> RowOf(HexCoord cell) => wide ? river.Where(c => c.R == cell.R).ToList() : new List<HexCoord> { cell };
            // Мост через реку — всегда (и у «Берега и брода», где к нему добавлены броды).
            HexCoord deck = middle[b.Rng.Next(middle.Count)];
            foreach (HexCoord cell in RowOf(deck))
                b.ObjectOver(SandboxObjectKind.Bridge, cell);
            if (!bridge)
            {
                int fords = Math.Max(1, Math.Min(3, b.Settings.FordCount));
                List<HexCoord> shortPool = middle.Where(c => c.R != deck.R).ToList();
                if (shortPool.Count == 0) shortPool = middle;
                HexCoord shortFord = shortPool[b.Rng.Next(shortPool.Count)];
                foreach (HexCoord cell in RowOf(shortFord))
                    b.Ground(cell, SandboxGround.ShallowWater);
                if (fords >= 2)
                {
                    HexCoord farFord = far[0];
                    List<HexCoord> farPool = far.Where(c => c.R != deck.R).ToList();
                    if (farPool.Count > 0) farFord = farPool[b.Rng.Next(farPool.Count)];
                    foreach (HexCoord cell in RowOf(farFord))
                        b.Ground(cell, SandboxGround.ShallowWater);
                    foreach (HexCoord cell in farFord.Neighbors().Where(river.Contains).Take(b.Simple ? 0 : 1))
                        b.Ground(cell, SandboxGround.ShallowWater);
                }
                if (fords >= 3)
                {
                    // Третий брод — в ряду, где брода ещё нет.
                    List<HexCoord> rest = river.Where(c => b.Plan.Ground.TryGetValue(c, out SandboxGround g) && g == SandboxGround.DeepWater &&
                                                           !river.Any(o => o.R == c.R && b.Plan.Ground.TryGetValue(o, out SandboxGround og) && og == SandboxGround.ShallowWater)).ToList();
                    if (rest.Count > 0)
                        foreach (HexCoord cell in RowOf(rest[b.Rng.Next(rest.Count)]))
                            b.Ground(cell, SandboxGround.ShallowWater);
                }
            }
            List<HexCoord> shore = river.SelectMany(c => c.Neighbors()).Where(b.Free).Distinct().OrderBy(c => c).ToList();
            foreach (HexCoord cell in shore.OrderBy(_ => b.Rng.Next()).Take(b.Special(0.35)))
                b.Ground(cell, SandboxGround.Mud);
            foreach (HexCoord cell in shore.Where(b.Free).OrderBy(_ => b.Rng.Next()).Take(b.Special(0.25, SandboxArenaPart.Vegetation)))
                b.Vegetation(cell, SandboxVegetation.Brush);
            b.Scatter(SandboxObjectKind.Boulder, Math.Max(0, b.Obstacles(1.0) - river.Count), 1, cell => !shore.Contains(cell));
        }

        // Каменистый склон: полоса холмов поперёк поля, щебень у подножия,
        // валуны и короткий обрыв.
        private static void RockySlope(Builder b)
        {
            int shift = b.Rng.Next(3) - 1;
            List<HexCoord> slope = SandboxHexLine.Line(new HexCoord(5 + shift, 0), new HexCoord(4 - shift, SandboxArenaShape.Height - 1))
                .Where(SandboxArenaShape.Contains).ToList();
            List<HexCoord> wide = slope.Concat(slope.Select(c => new HexCoord(c.Q + 1, c.R)))
                .Where(b.Free).Distinct().OrderBy(_ => b.Rng.Next()).Take(b.Special(0.55, SandboxArenaPart.Hills)).ToList();
            foreach (HexCoord cell in wide)
                b.Hill(cell);
            List<HexCoord> foot = wide.SelectMany(c => c.Neighbors()).Where(b.Free).Distinct().OrderBy(c => c).ToList();
            foreach (HexCoord cell in foot.OrderBy(_ => b.Rng.Next()).Take(b.Special(0.35)))
                b.Ground(cell, SandboxGround.Rubble);
            if (!b.Simple)
            {
                List<HexCoord> cliff = wide.Where(c => c.R <= 1 || c.R >= 5).OrderBy(_ => b.Rng.Next()).Take(2).ToList();
                foreach (HexCoord cell in cliff)
                {
                    b.Unhill(cell);
                    b.Ground(cell, SandboxGround.Cliff);
                }
            }
            b.Scatter(SandboxObjectKind.Boulder, b.Obstacles(0.75), 2);
        }

        // Разрушенное городище: холм в центре, дуга каменной стенки с
        // проломами, щебень, плетень.
        private static void RuinedHillfort(Builder b)
        {
            HexCoord center = new HexCoord(4 + b.Rng.Next(2), 3);
            b.Hills(center, b.Special(0.50, SandboxArenaPart.Hills));
            List<HexCoord> ring = SandboxArenaShape.Cells().Where(c => c.DistanceTo(center) == 2).OrderBy(c => Angle(center, c)).ToList();
            int start = b.Rng.Next(ring.Count);
            int wall = b.Simple ? 2 : Math.Min(5, b.Obstacles(0.6));
            for (int i = 0; i < ring.Count && wall > 0; i++)
            {
                HexCoord cell = ring[(start + i * 2) % ring.Count];
                if (b.CanBlock(cell))
                {
                    b.Object(SandboxObjectKind.StoneWall, cell);
                    wall--;
                }
            }
            List<HexCoord> outer = SandboxArenaShape.Cells().Where(c => c.DistanceTo(center) == 3 && b.Free(c)).OrderBy(c => c).ToList();
            foreach (HexCoord cell in outer.OrderBy(_ => b.Rng.Next()).Take(b.Special(0.30)))
                b.Ground(cell, SandboxGround.Rubble);
            b.Scatter(SandboxObjectKind.Wattle, b.Simple ? 0 : 1, 1);
            b.Scatter(SandboxObjectKind.Boulder, b.Obstacles(0.25), 1);
        }

        // Зимняя поляна: сугробы, замёрзший пруд, ели и валуны.
        private static void WinterGlade(Builder b)
        {
            int snow = b.Special(0.70);
            List<HexCoord> drifts = b.Blob(b.Middle(), snow / 2 + snow % 2, b.Free);
            drifts.AddRange(b.Blob(b.Middle(), snow / 2, b.Free));
            foreach (HexCoord cell in drifts)
                b.Ground(cell, SandboxGround.DeepSnow);
            foreach (HexCoord cell in b.Blob(b.Middle(), b.Special(0.25), b.Free))
                b.Ground(cell, SandboxGround.Ice);
            b.Scatter(SandboxObjectKind.Trunk, b.Obstacles(0.5), 1);
            b.Scatter(SandboxObjectKind.Boulder, b.Obstacles(0.5), 2);
        }

        private static double Angle(HexCoord center, HexCoord cell) =>
            Math.Atan2(cell.R - center.R, cell.Q - center.Q + (cell.R - center.R) * 0.5);

        private static int Mix(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u ^ (uint)salt * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                return (int)(h & 0x7fffffff);
            }
        }

        // ---------- Построитель ----------

        private sealed class Builder
        {
            private readonly SandboxArenaPlan plan;
            public SandboxArenaPlan Plan => plan;
            private readonly HashSet<HexCoord> spawns;
            private readonly int candidates;
            public readonly Random Rng;
            public readonly bool Simple;
            private readonly SandboxArenaGenerationSettings settings;

            public Builder(SandboxArenaPlan plan, Random random, List<HexCoord> players, List<HexCoord> enemies,
                SandboxArenaGenerationSettings settings, bool simple)
            {
                this.plan = plan;
                Rng = random;
                Simple = simple;
                this.settings = settings;
                spawns = new HashSet<HexCoord>(players.Concat(enemies));
                candidates = SandboxArenaShape.Cells().Count(c => !plan.Terrain.ContainsKey(c) && !spawns.Contains(c));
            }

            private double Scale => Simple ? 0.5 : 1.0;

            // Бюджет особой проходимой местности / преград на долю рецепта.
            public int Special(double part, SandboxArenaPart kind = SandboxArenaPart.Ground) => Amount(kind) <= 0.0 ? 0 : Math.Max(Simple ? 0 : 1, (int)Math.Round(candidates * settings.SpecialShare * part * Scale * Amount(kind)));

            private double Amount(SandboxArenaPart kind) => Math.Max(0.0, kind == SandboxArenaPart.Hills ? settings.HillAmount : kind == SandboxArenaPart.Vegetation ? settings.VegetationAmount : settings.GroundAmount);
            public int Obstacles(double part) => Math.Max(0, (int)Math.Round(candidates * settings.ObstacleShare * part * Scale * Math.Max(0.0, settings.ObstacleAmount)));
            public SandboxArenaGenerationSettings Settings => settings;

            private bool Active(HexCoord cell) => SandboxArenaShape.Contains(cell) && !plan.Terrain.ContainsKey(cell);

            // Клетка пустая: без особой местности и объектов.
            public bool Free(HexCoord cell) =>
                Active(cell) && !plan.Ground.ContainsKey(cell) && !plan.Vegetation.ContainsKey(cell) && plan.ObjectAt(cell) == null;

            // Закрывать нельзя спавн и его соседей (место для отступа).
            public bool CanBlock(HexCoord cell) =>
                Free(cell) && !spawns.Contains(cell) && spawns.All(s => s.DistanceTo(cell) > 1);

            public HexCoord Middle()
            {
                List<HexCoord> cells = SandboxArenaShape.Cells().Where(c => c.Q >= 3 && c.Q <= 6 && Free(c)).ToList();
                return cells.Count > 0 ? cells[Rng.Next(cells.Count)] : ArenaCenter;
            }

            public HexCoord Anywhere()
            {
                List<HexCoord> cells = SandboxArenaShape.Cells().Where(c => Free(c) && !spawns.Contains(c)).ToList();
                return cells.Count > 0 ? cells[Rng.Next(cells.Count)] : ArenaCenter;
            }

            // Связное пятно: рост от центра в случайного соседа.
            public List<HexCoord> Blob(HexCoord center, int size, Func<HexCoord, bool> allowed)
            {
                List<HexCoord> result = new List<HexCoord>();
                if (size <= 0 || !allowed(center) || !Active(center))
                    return result;
                HashSet<HexCoord> set = new HashSet<HexCoord> { center };
                result.Add(center);
                int guard = 0;
                while (result.Count < size && guard++ < size * 20)
                {
                    HexCoord from = result[Rng.Next(result.Count)];
                    HexCoord[] around = from.Neighbors().Where(c => Active(c) && allowed(c) && !set.Contains(c)).ToArray();
                    if (around.Length == 0)
                        continue;
                    HexCoord next = around[Rng.Next(around.Length)];
                    set.Add(next);
                    result.Add(next);
                }
                return result;
            }

            public void Hill(HexCoord cell)
            {
                if (Active(cell))
                    plan.Terrain[cell] = SandboxTerrain.Difficult;
            }

            public void Unhill(HexCoord cell)
            {
                if (plan.Terrain.TryGetValue(cell, out SandboxTerrain t) && t == SandboxTerrain.Difficult)
                    plan.Terrain.Remove(cell);
            }

            public void Hills(HexCoord center, int size)
            {
                foreach (HexCoord cell in Blob(center, size, Free))
                    Hill(cell);
            }

            public void Brush(HexCoord center, int size, SandboxVegetation kind)
            {
                foreach (HexCoord cell in Blob(center, size, Free))
                    Vegetation(cell, kind);
            }

            public void Vegetation(HexCoord cell, SandboxVegetation kind)
            {
                if (SandboxArenaShape.Contains(cell))
                    plan.Vegetation[cell] = kind;
            }

            public void Ground(HexCoord cell, SandboxGround ground)
            {
                if (!SandboxArenaShape.Contains(cell))
                    return;
                if ((ground == SandboxGround.DeepWater || ground == SandboxGround.Cliff) && spawns.Contains(cell))
                    return;
                plan.Ground[cell] = ground;
                if (ground == SandboxGround.DeepWater || ground == SandboxGround.Cliff)
                    plan.Vegetation.Remove(cell);
            }

            public void Object(SandboxObjectKind kind, HexCoord cell)
            {
                SandboxTerrainObjectInfo info = SandboxTerrainObjects.Get(kind);
                if (plan.ObjectAt(cell) != null || !Active(cell))
                    return;
                if (info.BlocksMovement && (plan.Ground.ContainsKey(cell) || spawns.Any(s => s.DistanceTo(cell) <= 1)))
                    return;
                SandboxLayoutObject item = new SandboxLayoutObject { Kind = kind, HitPoints = info.Indestructible ? 0 : info.HitPoints };
                item.Cells.Add(cell);
                plan.Objects.Add(item);
            }

            // Объект поверх особого основания (мост над водой).
            public void ObjectOver(SandboxObjectKind kind, HexCoord cell)
            {
                SandboxLayoutObject item = new SandboxLayoutObject { Kind = kind };
                item.Cells.Add(cell);
                plan.Objects.Add(item);
            }

            public void FallenLogs(int count)
            {
                Scatter(SandboxObjectKind.FallenLog, count, 1);
            }

            // Группы объектов размером до groupSize в средней части поля.
            public void Scatter(SandboxObjectKind kind, int count, int groupSize, Func<HexCoord, bool> where = null)
            {
                int guard = 0;
                while (count > 0 && guard++ < 40)
                {
                    List<HexCoord> options = SandboxArenaShape.Cells()
                        .Where(c => c.Q >= 2 && c.Q <= 7 && CanBlock(c) && (where == null || where(c))).ToList();
                    if (options.Count == 0)
                        return;
                    HexCoord start = options[Rng.Next(options.Count)];
                    foreach (HexCoord cell in Blob(start, Math.Min(groupSize, count), CanBlock))
                    {
                        Object(kind, cell);
                        count--;
                    }
                }
            }
        }
    }
}
