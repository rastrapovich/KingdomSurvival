using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Бриф «Поверхности и окружение боевой арены», этап 4: темы арены,
    // проверка раскладки, отчёт, ИИ на местности, отладочные команды полигона.
    public sealed class ArenaThemeGeneratorTests
    {
        [SetUp]
        public void SetUp()
        {
            SandboxSurfaceRules.Current = new SandboxSurfaceRules();
            SandboxArenaGenerationSettings.Current = new SandboxArenaGenerationSettings();
        }

        private static List<SandboxUnitDefinition> Players(int count) =>
            Enumerable.Range(0, count).Select(i => SandboxRoster.PlayerRoster[i % SandboxRoster.PlayerRoster.Count]).ToList();

        private static List<SandboxUnitDefinition> Enemies(int count) =>
            Enumerable.Range(0, count).Select(i => SandboxRoster.EnemyRoster[i % SandboxRoster.EnemyRoster.Count]).ToList();

        private static bool Walkable(SandboxBattle battle, HexCoord cell) =>
            battle.IsInside(cell) && battle.GetTerrain(cell) != SandboxTerrain.Impassable && !battle.IsGroundBlocking(cell) &&
            (battle.TerrainObjectAt(cell) == null || !SandboxTerrainObjects.Get(battle.TerrainObjectAt(cell).Kind).BlocksMovement);

        private static HashSet<HexCoord> Region(SandboxBattle battle, HexCoord start)
        {
            HashSet<HexCoord> seen = new HashSet<HexCoord> { start };
            Queue<HexCoord> open = new Queue<HexCoord>();
            open.Enqueue(start);
            while (open.Count > 0)
                foreach (HexCoord next in open.Dequeue().Neighbors())
                    if (!seen.Contains(next) && Walkable(battle, next))
                    {
                        seen.Add(next);
                        open.Enqueue(next);
                    }
            return seen;
        }

        [Test]
        public void EveryOrdinaryTheme_ThirtySeeds_FairPlayableArena([ValueSource(nameof(OrdinaryThemes))] SandboxArenaTheme theme)
        {
            int simplified = 0;
            foreach ((int players, int enemies) in new[] { (5, 6), (1, 1), (6, 8) })
            {
                for (int seed = 1; seed <= 30; seed++)
                {
                    SandboxBattle battle = SandboxRoster.CreateBattle(Players(players), Enemies(enemies), seed, theme: theme, themeSource: "тест");
                    SandboxArenaReport report = battle.ArenaReport;
                    string context = theme + " seed " + seed + " " + players + "×" + enemies + ": " + report.Summary();
                    Assert.That(report, Is.Not.Null);
                    Assert.That(report.BareFallback, Is.False, context);
                    Assert.That(report.Issues, Is.Empty, context);
                    if (report.Simplified)
                        simplified++;

                    foreach (SandboxUnitState unit in battle.Units)
                        Assert.That(Walkable(battle, unit.Position), Is.True, "Фигура на закрытой клетке. " + context);
                    HashSet<HexCoord> region = Region(battle, battle.Units[0].Position);
                    Assert.That(battle.Units.All(u => region.Contains(u.Position)), Is.True, "Стороны связаны. " + context);
                    Assert.That(battle.CellEffects, Is.Empty, "Нет активной опасности на старте. " + context);

                    if (theme == SandboxArenaTheme.FordBridge)
                        Assert.That(report.Intentional, Has.Some.Contains("мост"), context);
                    else
                        Assert.That(report.SecondApproach, Is.True, "Второй подход. " + context);
                    Assert.That(report.ObstacleCells, Is.LessThanOrEqualTo(report.TotalCells * 0.25), context);

                    // 2–3 значимых вида местности (не весь справочник).
                    int kinds = battle.GroundCells.Values.Where(g => g != SandboxGround.Default && g != SandboxGround.Ground && g != SandboxGround.DeepWater && g != SandboxGround.Cliff).Distinct().Count() +
                                battle.DebugBoardCells().Select(battle.GetVegetation).Where(v => v != SandboxVegetation.None).Distinct().Count() +
                                (battle.DebugBoardCells().Any(c => battle.GetTerrain(c) == SandboxTerrain.Difficult) ? 1 : 0);
                    Assert.That(kinds, Is.InRange(1, 4), context);
                }
            }
            Assert.That(simplified, Is.LessThan(30), theme + ": упрощённый вариант — исключение, а не правило.");
        }

        private static IEnumerable<SandboxArenaTheme> OrdinaryThemes() => SandboxArenaThemes.Ordinary;

        [Test]
        public void SameSeed_SameArena_DifferentSeed_Differs()
        {
            SandboxBattle a = SandboxRoster.CreateBattle(Players(3), Enemies(3), 42, theme: SandboxArenaTheme.BogGlade);
            SandboxBattle b = SandboxRoster.CreateBattle(Players(3), Enemies(3), 42, theme: SandboxArenaTheme.BogGlade);
            Assert.That(b.GroundCells.OrderBy(p => p.Key), Is.EqualTo(a.GroundCells.OrderBy(p => p.Key)));
            Assert.That(b.TerrainObjects.Select(o => o.Kind + ":" + o.Cells[0]), Is.EqualTo(a.TerrainObjects.Select(o => o.Kind + ":" + o.Cells[0])));
            bool differs = Enumerable.Range(43, 10).Any(seed =>
                !SandboxRoster.CreateBattle(Players(3), Enemies(3), seed, theme: SandboxArenaTheme.BogGlade)
                    .GroundCells.OrderBy(p => p.Key).SequenceEqual(a.GroundCells.OrderBy(p => p.Key)));
            Assert.That(differs, Is.True);
        }

        [Test]
        public void NoTheme_KeepsLegacyGenerator()
        {
            SandboxBattle battle = SandboxRoster.CreateBattle(Players(2), Enemies(2), 3);
            Assert.That(battle.ArenaReport, Is.Null);
            Assert.That(battle.GroundCells, Is.Empty);
            Assert.That(battle.TerrainObjects, Is.Empty);
        }

        [Test]
        public void ThemeSource_IsRecordedInReport()
        {
            SandboxBattle battle = SandboxRoster.CreateBattle(Players(2), Enemies(2), 7, theme: SandboxArenaTheme.ForestEdge,
                themeSource: "местность под отрядом: лес");
            Assert.That(battle.ArenaReport.Summary(), Does.Contain("местность под отрядом: лес"));
            Assert.That(battle.ArenaReport.Summary(), Does.Contain("Лесная опушка"));
        }

        [Test]
        public void ImpossibleLayout_FallsBackHonestly_WithReason()
        {
            // Стена поперёк поля с одним проходом: второго подхода нет ни в
            // полном, ни в упрощённом варианте.
            List<SandboxUnitState> units = new List<SandboxUnitState>
            {
                new SandboxUnitState("p", SandboxRoster.PlayerRoster[0], SandboxTeam.Player, new HexCoord(1, 3)),
                new SandboxUnitState("e", SandboxRoster.EnemyRoster[0], SandboxTeam.Enemy, new HexCoord(8, 3))
            };
            HashSet<HexCoord> wall = new HashSet<HexCoord>(SandboxArenaShape.Cells().Where(c => c.Q == 5 && c.R != 3));
            SandboxArenaPlan plan = SandboxArenaGenerator.Generate(SandboxArenaTheme.DryGlade, 11, units, wall, "тест");
            Assert.That(plan.Report.BareFallback, Is.True);
            Assert.That(plan.Report.FallbackReason, Is.Not.Empty);
            Assert.That(plan.Report.Summary(), Does.Contain("второй подход: нет"), plan.Report.Summary());
            Assert.That(plan.Blocks(new HexCoord(5, 3)), Is.False, "Единственный проход не закрыт.");
        }

        [Test]
        public void RitualGlade_MarksAuthoredStoneAsIntentional()
        {
            SandboxBattle battle = SandboxRoster.CreateBattle(Players(2), Enemies(2), 9, theme: SandboxArenaTheme.RitualGlade);
            Assert.That(battle.TerrainObjects.Any(o => o.Kind == SandboxObjectKind.RitualStone), Is.True);
            Assert.That(battle.ArenaReport.Intentional, Has.Some.Contains("обрядовый"));
        }

        // ---------- ИИ ----------

        private static SandboxUnitState Unit(string id, SandboxTeam team, HexCoord at, int range = 1, int movement = 4, int initiative = 1, string[] tags = null) =>
            new SandboxUnitState(id, new SandboxUnitDefinition(id + "-type", id, team == SandboxTeam.Enemy ? SandboxUnitRole.Beast : SandboxUnitRole.Militia,
                30, 4, 2, 6, movement, initiative, range, tags), team, at);

        private static SandboxBattle Field(params SandboxUnitState[] units)
        {
            Dictionary<HexCoord, SandboxTerrain> terrain = new Dictionary<HexCoord, SandboxTerrain>();
            SandboxBattle battle = new SandboxBattle(10, 7, units, terrain);
            SandboxTerrainRules.RegisterBattle(battle.Units, terrain);
            return battle;
        }

        [Test]
        public void Archer_MovesToHill_WhenItCanShootFromThere()
        {
            SandboxUnitState archer = Unit("archer", SandboxTeam.Enemy, new HexCoord(8, 3), range: 4, movement: 6, initiative: 20, tags: new[] { SandboxCombatTagRules.Ranged });
            SandboxUnitState target = Unit("target", SandboxTeam.Player, new HexCoord(1, 3));
            SandboxBattle battle = Field(archer, target);
            battle.DebugSetHill(new HexCoord(5, 2), true);
            battle.Start();
            Assert.That(battle.CurrentUnit, Is.EqualTo(archer));
            SandboxAiExecutor.TakeTurn(battle);
            Assert.That(archer.Position, Is.EqualTo(new HexCoord(5, 2)), "Стрелок встаёт на холм, откуда стреляет.");
            Assert.That(target.HitPoints, Is.LessThan(target.MaxHitPoints));
        }

        [Test]
        public void Archer_DoesNotShootThroughBoulder_RepositionsForClearLine()
        {
            SandboxUnitState archer = Unit("archer", SandboxTeam.Enemy, new HexCoord(8, 3), range: 4, movement: 4, initiative: 20, tags: new[] { SandboxCombatTagRules.Ranged });
            SandboxUnitState target = Unit("target", SandboxTeam.Player, new HexCoord(5, 3));
            SandboxBattle battle = Field(archer, target);
            battle.PlaceTerrainObject(SandboxObjectKind.Boulder, new[] { new HexCoord(6, 3) });
            battle.Start();
            Assert.That(battle.PreviewAttack(archer.Id, target.Id).IsValid, Is.False);
            SandboxAiExecutor.TakeTurn(battle);
            Assert.That(archer.Position, Is.Not.EqualTo(new HexCoord(8, 3)));
            Assert.That(battle.GetLineOfFire(archer.Position, target.Position).Clear, Is.True);
            Assert.That(target.HitPoints, Is.LessThan(target.MaxHitPoints), "Выстрел с чистой линии.");
        }

        [Test]
        public void Fighter_StepsOutOfFire_ThenStrikes()
        {
            SandboxUnitState beast = Unit("beast", SandboxTeam.Enemy, new HexCoord(5, 3), initiative: 20);
            SandboxUnitState target = Unit("target", SandboxTeam.Player, new HexCoord(4, 3));
            SandboxBattle battle = Field(beast, target);
            battle.AddEffect(new HexCoord(5, 3), SandboxEffectKind.Fire, 0);
            battle.Start();
            SandboxAiExecutor.TakeTurn(battle);
            Assert.That(battle.HazardAt(beast, beast.Position), Is.EqualTo(0), "Вышел из огня.");
            Assert.That(beast.Position.DistanceTo(target.Position), Is.EqualTo(1));
            Assert.That(target.HitPoints, Is.LessThan(target.MaxHitPoints), "И ударил ту же цель.");
        }

        [Test]
        public void SwampWalker_CrossesBog_OrdinaryPaysMore()
        {
            HexCoord[] bog = Enumerable.Range(1, 6).Select(r => new HexCoord(4, r)).ToArray();

            SandboxUnitState walker = Unit("walker", SandboxTeam.Enemy, new HexCoord(2, 3), movement: 3, initiative: 20, tags: new[] { SandboxTerrainTags.SwampWalker });
            SandboxBattle a = Field(walker, Unit("t", SandboxTeam.Player, new HexCoord(9, 3)));
            foreach (HexCoord cell in bog)
                a.SetGround(cell, SandboxGround.Bog);
            a.Start();
            SandboxAiExecutor.TakeTurn(a);

            SandboxUnitState ordinary = Unit("ordinary", SandboxTeam.Enemy, new HexCoord(2, 3), movement: 3, initiative: 20);
            SandboxBattle b = Field(ordinary, Unit("t", SandboxTeam.Player, new HexCoord(9, 3)));
            foreach (HexCoord cell in bog)
                b.SetGround(cell, SandboxGround.Bog);
            b.Start();
            SandboxAiExecutor.TakeTurn(b);

            Assert.That(walker.Position.Q, Is.GreaterThanOrEqualTo(5), "Болотный ходок не платит штраф топи.");
            Assert.That(ordinary.Position.Q, Is.LessThan(walker.Position.Q));
        }

        [Test]
        public void AiOnlyBattle_OnThemedArena_Finishes()
        {
            foreach (SandboxArenaTheme theme in new[] { SandboxArenaTheme.RuinedHillfort, SandboxArenaTheme.Ford, SandboxArenaTheme.BogGlade })
            {
                SandboxBattle battle = SandboxRoster.CreateBattle(
                    new[] { SandboxRoster.PlayerRoster[0], SandboxRoster.PlayerRoster[1] },
                    new[] { SandboxRoster.EnemyRoster[2], SandboxRoster.EnemyRoster[3] }, 5, theme: theme);
                int turns = 0;
                while (battle.Phase == SandboxBattlePhase.InProgress && turns++ < 600)
                    SandboxAiExecutor.TakeTurn(battle);
                Assert.That(battle.Phase, Is.Not.EqualTo(SandboxBattlePhase.InProgress), theme + ": бой без вечного цикла позиций.");
            }
        }

        // ---------- Отладка полигона ----------

        [Test]
        public void DebugCommands_ChangeTerrainForPathsAndClearEffects()
        {
            SandboxUnitState hero = Unit("hero", SandboxTeam.Player, new HexCoord(2, 3), initiative: 20);
            SandboxBattle battle = Field(hero, Unit("foe", SandboxTeam.Enemy, new HexCoord(8, 3)));
            battle.Start();
            HexCoord cell = new HexCoord(3, 3);
            Assert.That(battle.DebugPlaceObject(hero.Position, SandboxObjectKind.Boulder), Is.False, "Не под фигуру.");
            Assert.That(battle.DebugPlaceObject(cell, SandboxObjectKind.Boulder), Is.True);
            Assert.That(battle.GetReachable(hero.Id).ContainsKey(cell), Is.False);
            Assert.That(battle.DebugRemoveObject(cell), Is.True);
            Assert.That(battle.GetReachable(hero.Id).ContainsKey(cell), Is.True);

            battle.DebugSetGround(cell, SandboxGround.ShallowWater);
            battle.ApplyImpulse(SandboxImpulse.Cold, new[] { cell });
            Assert.That(battle.HasEffect(cell, SandboxEffectKind.Ice), Is.True);
            battle.AddEffect(new HexCoord(2, 4), SandboxEffectKind.Fire, 2);
            Assert.That(battle.DebugClearEffects(), Is.EqualTo(2));
            Assert.That(battle.CellEffects, Is.Empty);

            battle.DebugSetHill(cell, true);
            Assert.That(battle.GetTerrain(cell), Is.EqualTo(SandboxTerrain.Difficult));
            Assert.That(battle.DebugCellCategory(cell), Is.EqualTo("special"));
        }
    }
}
