using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.MapLab;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Арена MapLab по раскладке боя: видимый холм совпадает с клетками холма
    // правил, открытые клетки остаются ровными, выбор мышью находит вершину.
    public sealed class MapLabArenaTests
    {
        private static SandboxBattle Battle(int seed) => SandboxRoster.CreateDefaultBattle(
            new[] { "guard", "archer", "spearman", "scout" }, SandboxRoster.PlayerRoster, SandboxRoster.EnemyRoster, seed);

        // Проверки алгоритма — на настройках генератора по умолчанию, а не на
        // текущих настройках автора в ассете («Генератор арен»).
        private static MapLabArenaSpec DefaultSpec(SandboxBattle battle)
        {
            MapLabArenaSpec spec = MapLabArenaDriver.BuildSpec(null, battle);
            MapLabArenaLook look = new MapLabArenaLook();
            spec.Look = look;
            spec.HillLiftHexes = look.HillLiftHexes;
            spec.DecorDensity = look.DecorDensity;
            spec.AssetScale = look.AssetScale;
            spec.Frame = null;
            return spec;
        }

        [Test]
        public void Spec_FollowsAcceptedBattleLayout_AndIsStableForSameLayout()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                SandboxBattle battle = Battle(seed);
                MapLabArenaSpec spec = DefaultSpec(battle);
                List<HexCoord> cells = BattlefieldFrame.ActiveCells(null).ToList();
                Assert.That(spec.Cells.Count, Is.EqualTo(cells.Count), "Seed " + seed);
                for (int i = 0; i < cells.Count; i++)
                {
                    SandboxTerrain terrain = battle.GetTerrain(cells[i]);
                    MapLabArenaCellKind expected = terrain == SandboxTerrain.Difficult ? MapLabArenaCellKind.Hill
                        : terrain == SandboxTerrain.Impassable ? MapLabArenaCellKind.Blocked : MapLabArenaCellKind.Open;
                    Assert.That(spec.Cells[i].Kind, Is.EqualTo(expected), $"Seed {seed}, клетка {cells[i].Q},{cells[i].R}");
                }
                Assert.That(MapLabArenaDriver.BuildSpec(null, Battle(seed)).Seed, Is.EqualTo(spec.Seed), "Та же раскладка — тот же рисунок");
            }
        }

        [Test]
        public void HillHeight_PlateauOverHillCell_FootAtNeighbourCentre()
        {
            const float hex = 3f;
            List<Vector2> one = new List<Vector2> { Vector2.zero };
            Assert.That(MapLabArena.HillHeight(one, Vector2.zero, hex), Is.EqualTo(1f).Within(1e-4f));
            // Середина стороны гекса — ещё склон у вершины.
            Assert.That(MapLabArena.HillHeight(one, new Vector2(hex * 0.866f, 0f), hex), Is.GreaterThan(0.5f));
            // Центр соседней клетки — у подножия.
            Assert.That(MapLabArena.HillHeight(one, new Vector2(hex * Mathf.Sqrt(3f), 0f), hex), Is.LessThan(0.02f));
            // Две соседние клетки холма — гряда без глубокой седловины.
            List<Vector2> two = new List<Vector2> { Vector2.zero, new Vector2(hex * Mathf.Sqrt(3f), 0f) };
            Assert.That(MapLabArena.HillHeight(two, new Vector2(hex * Mathf.Sqrt(3f) * 0.5f, 0f), hex), Is.GreaterThan(0.85f));
        }

        [Test]
        public void BuiltArena_LiftsHillCells_KeepsOpenCellsLevel_AndPicksHillTops()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            foreach (int seed in new[] { 3, 11 })
            {
                SandboxBattle battle = Battle(seed);
                MapLabArenaSpec spec = DefaultSpec(battle);
                MapLabArena arena = MapLabArena.Build(spec);
                try
                {
                    Assert.That(arena.Ready, Is.True, arena.Error);
                    Debug.Log($"Арена MapLab seed {seed}: сборка {arena.BuildMs:0} мс, декор Базы ассетов: {arena.UsesAssetDatabase}, элементов {arena.DecorFeatures}.");
                    Rect frame = new Rect(0f, 0f, 1920f, 1080f);
                    float expected = spec.HillLiftHexes * spec.HexNominal;
                    List<HexCoord> cells = BattlefieldFrame.ActiveCells(null).ToList();
                    HashSet<HexCoord> hills = new HashSet<HexCoord>(cells.Where(c => battle.GetTerrain(c) == SandboxTerrain.Difficult));
                    for (int i = 0; i < cells.Count; i++)
                    {
                        Vector2 share = new Vector2(spec.Cells[i].Center.x / spec.Aspect, spec.Cells[i].Center.y);
                        float lift = arena.LiftShare(share);
                        string where = $"seed {seed}, клетка {cells[i].Q},{cells[i].R}";
                        if (hills.Contains(cells[i]))
                            Assert.That(lift, Is.GreaterThan(expected * 0.75f), "Холм поднят: " + where);
                        else if (cells[i].Neighbors().Any(hills.Contains))
                            Assert.That(lift, Is.LessThan(expected * 0.35f), "Сосед холма — у подножия: " + where);
                        else
                            Assert.That(Mathf.Abs(lift), Is.LessThan(expected * 0.12f), "Ровная клетка: " + where);
                    }

                    // Выбор мышью: видимая вершина каждой клетки холма выбирает её
                    // (передние ряды проверяются первыми, как у доски).
                    Relief relief = new Relief(arena);
                    float size = spec.HexNominal * frame.height;
                    foreach (HexCoord hill in hills)
                    {
                        Vector2 flat = FrameCenter(spec, cells, hill, frame);
                        Vector2 top = BattlefieldReliefMath.Raise(relief, frame, flat);
                        Assert.That(Pick(relief, frame, spec, cells, top, size), Is.EqualTo(hill), $"Вершина холма {hill.Q},{hill.R}, seed {seed}");
                        Vector2 back = BattlefieldReliefMath.Lower(relief, frame, top);
                        Assert.That(Vector2.Distance(back, flat), Is.LessThan(1f), "Обратное преобразование");
                    }

                    AssertDecorLeavesCellsVisible(arena, spec);
                    SavePreview(arena, spec, cells, battle, frame, seed);
                }
                finally
                {
                    arena.Dispose();
                }
            }
        }

        // Деревья и крупный декор не закрывают клетки на экране: центр клетки
        // позади рисунка не попадает в его середину (без прозрачных полей).
        private static void AssertDecorLeavesCellsVisible(MapLabArena arena, MapLabArenaSpec spec)
        {
            if (!arena.UsesAssetDatabase)
                return;
            Assert.That(arena.DecorFeatures, Is.GreaterThan(10), "Декор из Базы ассетов поставлен.");
            float cos = Mathf.Sqrt(1f - spec.VerticalScale * spec.VerticalScale), sin = spec.VerticalScale;
            foreach (KingdomSurvival.MapLab.MapLabFeatureSprite sprite in arena.View.Data.FeatureSprites)
            {
                if (sprite.Kind != "arena-decor" || sprite.Height < spec.HexWorld)
                    continue;
                float baseV = sprite.Base.y * cos + sprite.Base.z * sin;
                foreach ((Vector2 Center, MapLabArenaCellKind Kind) cell in spec.Cells)
                {
                    Vector2 center = cell.Center;
                    Vector2 w = arena.ToWorld(center);
                    float h = arena.View.Data.SampleHeight(w.x, w.y);
                    float v = h * cos + w.y * sin;
                    bool middle = Mathf.Abs(w.x - sprite.Base.x) < sprite.Width * 0.3f;
                    bool covered = w.y > sprite.Base.z && v > baseV + sprite.Height * cos * 0.15f && v < baseV + sprite.Height * cos * 0.9f;
                    Assert.That(middle && covered, Is.False, $"Рисунок {sprite.ArtId} закрывает клетку {center}");
                }
            }
        }

        [Test]
        public void ArtAssetSets_FollowPurpose_AndDecorDensityDoesNotChangeRelief()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            KingdomSurvival.ArtAssets.ArtAssetDatabaseAsset catalog = KingdomSurvival.ArtAssets.ArtAssetDatabaseAsset.Current;
            List<MapLabDecorArt> arenaDecor = new List<MapLabDecorArt>(), mapDecor = new List<MapLabDecorArt>();
            KingdomSurvival.MapLab.MapLabArtSet arenaSet = MapLabArtAssets.BuildSet(true, 1.8f, arenaDecor);
            KingdomSurvival.MapLab.MapLabArtSet mapSet = MapLabArtAssets.BuildSet(false, 1.8f, mapDecor);
            try
            {
                foreach (KingdomSurvival.MapLab.MapLabArtEntry entry in arenaSet.Entries)
                    Assert.That(catalog.Find(entry.Id.Substring(MapLabArtAssets.IdPrefix.Length)).UsableOnArena, Is.True, entry.Id);
                foreach (KingdomSurvival.MapLab.MapLabArtEntry entry in mapSet.Entries)
                    Assert.That(catalog.Find(entry.Id.Substring(MapLabArtAssets.IdPrefix.Length)).UsableOnMap, Is.True, entry.Id);
                // Общий объект — один ID и одна текстура у карты и арены.
                foreach (KingdomSurvival.MapLab.MapLabArtEntry entry in arenaSet.Entries)
                {
                    KingdomSurvival.MapLab.MapLabArtEntry shared = mapSet.Find(entry.Id);
                    if (shared != null)
                        Assert.That(shared.Texture, Is.SameAs(entry.Texture), entry.Id);
                }
            }
            finally
            {
                Object.DestroyImmediate(arenaSet);
                Object.DestroyImmediate(mapSet);
            }

            SandboxBattle battle = Battle(5);
            MapLabArenaSpec sparse = DefaultSpec(battle);
            sparse.DecorDensity = 0.3f;
            MapLabArenaSpec dense = DefaultSpec(battle);
            dense.DecorDensity = 2f;
            MapLabArena first = MapLabArena.Build(sparse), second = MapLabArena.Build(dense);
            try
            {
                Assert.That(first.Ready && second.Ready, Is.True, first.Error + second.Error);
                if (first.UsesAssetDatabase)
                    Assert.That(second.DecorFeatures, Is.GreaterThan(first.DecorFeatures), "Плотность меняет только декор.");
                for (int i = 0; i < sparse.Cells.Count; i++)
                {
                    Vector2 share = new Vector2(sparse.Cells[i].Center.x / sparse.Aspect, sparse.Cells[i].Center.y);
                    Assert.That(second.LiftShare(share), Is.EqualTo(first.LiftShare(share)).Within(1e-5f), "Рельеф не зависит от декора.");
                }
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        // 24.11.2026: лесная клетка — деревья и кусты на самой клетке (рядом с
        // её центром), по умолчанию без деревьев по краям кадра.
        [Test]
        public void ForestCells_GetTreesAndBushesOnTheCell()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            SandboxBattle battle = Battle(7);
            HashSet<HexCoord> taken = new HashSet<HexCoord>(battle.Units.Select(u => u.Position));
            List<HexCoord> forest = battle.DebugBoardCells()
                .Where(c => c.Q >= 3 && c.Q <= 6 && battle.GetTerrain(c) == SandboxTerrain.Normal && battle.TerrainObjectAt(c) == null && !taken.Contains(c))
                .Take(3).ToList();
            Assume.That(forest.Count, Is.GreaterThan(0), "есть открытые клетки в середине поля");
            foreach (HexCoord cell in forest)
                battle.SetVegetation(cell, SandboxVegetation.Forest);
            MapLabArenaSpec spec = DefaultSpec(battle);
            Assert.That(spec.Forest.Count, Is.EqualTo(forest.Count), "лесные клетки переданы рисунку");
            Assert.That(spec.Look.EdgeTrees, Is.False, "деревья по краям кадра по умолчанию выключены");
            MapLabArena arena = MapLabArena.Build(spec);
            try
            {
                Assert.That(arena.Ready, Is.True, arena.Error);
                if (!arena.UsesAssetDatabase)
                    Assert.Ignore("Нет деревьев Базы ассетов для арены.");
                Assert.That(arena.ForestFeatures, Is.GreaterThanOrEqualTo(forest.Count), "на каждой лесной клетке есть рисунки");
                int onCells = 0;
                foreach (MapLabFeatureSprite sprite in arena.View.Data.FeatureSprites)
                {
                    if (sprite.Kind != MapLabArena.ForestKind)
                        continue;
                    Vector2 at = new Vector2(sprite.Base.x, sprite.Base.z);
                    float nearest = spec.Forest.Min(center => Vector2.Distance(arena.ToWorld(center), at));
                    Assert.That(nearest, Is.LessThan(spec.HexWorld * 0.95f), $"Рисунок {sprite.ArtId} — на лесной клетке");
                    onCells++;
                }
                Assert.That(onCells, Is.GreaterThan(0), "рисунки лесных клеток построены");
            }
            finally
            {
                arena.Dispose();
            }

            // Кадр для глаз: сухая поляна с холмами и лесными клетками генератора
            // (Logs/MapLabArena/arena_seed91xx.png).
            for (int seed = 1; seed <= 2; seed++)
            {
                SandboxBattle themed = SandboxRoster.CreateBattle(SandboxRoster.PlayerRoster.Take(4).ToList(), SandboxRoster.EnemyRoster, seed,
                    theme: SandboxArenaTheme.DryGlade);
                MapLabArenaSpec themedSpec = DefaultSpec(themed);
                MapLabArena preview = MapLabArena.Build(themedSpec);
                try
                {
                    Assert.That(preview.Ready, Is.True, preview.Error);
                    SavePreview(preview, themedSpec, BattlefieldFrame.ActiveCells(null).ToList(), themed, new Rect(0f, 0f, 1920f, 1080f), 9100 + seed);
                }
                finally
                {
                    preview.Dispose();
                }
            }
        }

        private sealed class Relief : IBattlefieldRelief
        {
            private readonly MapLabArena arena;
            public Relief(MapLabArena arena) { this.arena = arena; }
            public float LiftShare(Vector2 frameShare) => arena.LiftShare(frameShare);
        }

        private static Vector2 FrameCenter(MapLabArenaSpec spec, List<HexCoord> cells, HexCoord cell, Rect frame)
        {
            Vector2 nominal = spec.Cells[cells.IndexOf(cell)].Center;
            return new Vector2(frame.x + nominal.x / spec.Aspect * frame.width, frame.y + nominal.y * frame.height);
        }

        private static HexCoord? Pick(IBattlefieldRelief relief, Rect frame, MapLabArenaSpec spec, List<HexCoord> cells, Vector2 point, float size)
        {
            foreach (HexCoord cell in cells.OrderByDescending(c => c.R))
                if (BattlefieldReliefMath.DrapedHexContains(relief, frame, FrameCenter(spec, cells, cell, frame), size, spec.VerticalScale, point))
                    return cell;
            return null;
        }

        // Бурьян заметен: несколько растений у групп деревьев и куртины по окраине
        // (на настройках по умолчанию), а куртины не закрывают клетки поля
        // (проверка AssertDecorLeavesCellsVisible). Без куртин и с одним растением
        // на группу бурьяна заметно меньше.
        [Test]
        public void Weeds_AreVisible_AtGroupsAndInClumps()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            HashSet<string> weedIds = new HashSet<string>();
            foreach (KingdomSurvival.ArtAssets.ArtAssetDefinition a in KingdomSurvival.ArtAssets.ArtAssetDatabaseAsset.Current.assets)
                if (a != null && a.UsableOnArena && a.ArenaPriority > 0f && MapLabArtAssets.ArenaObjectKindOf(a) == null && MapLabArtAssets.RoleOf(a) == MapLabDecorRole.TallPlant)
                    weedIds.Add(MapLabArtAssets.IdPrefix + a.Id);
            if (weedIds.Count == 0)
                Assert.Ignore("В Базе ассетов нет бурьяна для арены.");
            int Count(MapLabArenaLook look, bool checkCells)
            {
                int total = 0;
                for (int seed = 1; seed <= 3; seed++)
                {
                    MapLabArenaSpec spec = DefaultSpec(Battle(seed));
                    spec.Look = look;
                    MapLabArena arena = MapLabArena.Build(spec);
                    try
                    {
                        Assert.That(arena.Ready, Is.True, arena.Error);
                        foreach (var f in arena.View.Data.Composition.Features)
                            if (weedIds.Contains(f.Visual))
                                total++;
                        if (checkCells)
                            AssertDecorLeavesCellsVisible(arena, spec);
                    }
                    finally { arena.Dispose(); }
                }
                return total;
            }
            int more = Count(new MapLabArenaLook(), true);
            int fewer = Count(new MapLabArenaLook { WeedsPerGroup = 1, WeedClumps = 0f }, false);
            Assert.That(more, Is.GreaterThanOrEqualTo(3 * 8), "в среднем не меньше 8 растений бурьяна на арену");
            Assert.That(more, Is.GreaterThan(fewer * 2), "куртины и несколько растений у группы — бурьяна заметно больше");
        }

        // Роль рисунка — по тегам, а не по высоте холста: низкие растения пакета
        // с высоким холстом (модуль низкой травы, побеги, черника) — трава пятен и
        // россыпи, а не бурьян у групп деревьев (по одному на группу).
        [Test]
        public void LowPlantTags_AreLowGrass_NotWeeds()
        {
            KingdomSurvival.ArtAssets.ArtAssetDefinition Asset(string tag, float height) => new KingdomSurvival.ArtAssets.ArtAssetDefinition
            {
                Name = tag, Category = KingdomSurvival.ArtAssets.ArtAssetCategory.Vegetation,
                Tags = new List<string> { "arena-pack-69", tag },
            };
            foreach (string tag in new[] { "LowGrassModule", "SparseLowGrass", "ThinSparseGrass", "GrassCarpet", "GrassShoots", "Bilberry", "WildStrawberry" })
                Assert.That(MapLabArtAssets.RoleOf(Asset(tag, 1.5f)), Is.EqualTo(MapLabDecorRole.LowGrass), tag);
            Assert.That(MapLabArtAssets.RoleOf(Asset("TuftedHairgrass", 0.87f)), Is.EqualTo(MapLabDecorRole.TallGrass));
            Assert.That(MapLabArtAssets.RoleOf(Asset("Nettle", 1.3f)), Is.EqualTo(MapLabDecorRole.TallPlant));
        }

        // Приоритет ковра — общий для пучков и контурной травы: единственный пучок
        // с приоритетом 0,1 рядом с контурными приоритета 1 почти не встречается
        // (прежде он забирал всю «не контурную» долю ковра).
        [Test]
        public void CarpetPick_LowPriorityTuft_IsRare()
        {
            MapLabArtEntry Art(string id, float priority) => new MapLabArtEntry { Id = id + "#carpet", Weight = priority };
            List<MapLabArtEntry> tufts = new List<MapLabArtEntry> { Art("tuft", 0.1f) };
            List<MapLabArtEntry> inks = new List<MapLabArtEntry> { Art("ink_a", 1f), Art("ink_b", 1.5f), Art("ink_c", 1f) };
            int tuft = 0, total = 2000;
            for (int i = 0; i < total; i++)
                if (MapLabArena.PickCarpet(tufts, inks, 0.3f, (i + 0.5f) / total).Id == "tuft#carpet")
                    tuft++;
            Assert.That(tuft / (float)total, Is.LessThan(0.02f), "пучок с приоритетом 0,1 — редкость");
            // Равный приоритет: доля контурных — как задана.
            tufts = new List<MapLabArtEntry> { Art("tuft", 1f) };
            inks = new List<MapLabArtEntry> { Art("ink_a", 1f) };
            int inkCount = 0;
            for (int i = 0; i < total; i++)
                if (MapLabArena.PickCarpet(tufts, inks, 0.3f, (i + 0.5f) / total).Id == "ink_a#carpet")
                    inkCount++;
            Assert.That(inkCount / (float)total, Is.EqualTo(0.3f).Within(0.01f));
            // Приоритет 0 у всех пучков — только контурные.
            Assert.That(MapLabArena.PickCarpet(new List<MapLabArtEntry>(), inks, 0.3f, 0.1f).Id, Is.EqualTo("ink_a#carpet"));
        }

        // Арена с рекой (брод и мост): ни одной травинки на воде по готовому
        // рельефу (только камыш MapLab), а штрихи воды течут — кадры в разные
        // моменты различаются. Кадры — в Logs/MapLabArena/river_flow_*.png.
        [Test]
        public void RiverArena_NoGrassOnWater_AndStrokesFlow()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            int rivers = 0, grass = 0;
            bool flowChecked = false;
            foreach (bool author in new[] { false, true })
            foreach (SandboxArenaTheme theme in new[] { SandboxArenaTheme.Ford, SandboxArenaTheme.FordBridge })
            for (int seed = 1; seed <= 3; seed++)
            {
                SandboxBattle battle = SandboxRoster.CreateBattle(SandboxRoster.PlayerRoster.Take(2).ToList(), SandboxRoster.EnemyRoster.Take(2), seed, theme: theme);
                // Настройки по умолчанию и текущие настройки автора («Генератор арен», ковёр травы).
                MapLabArena arena = MapLabArena.Build(author ? MapLabArenaDriver.BuildSpec(null, battle) : DefaultSpec(battle));
                try
                {
                    Assert.That(arena.Ready, Is.True, arena.Error);
                    if (!arena.HasRiver)
                        continue;
                    rivers++;
                    KingdomSurvival.MapLab.MapLabData data = arena.View.Data;
                    HashSet<string> dry = data.Composition.DryOnlyFeatures;
                    foreach (KingdomSurvival.MapLab.MapLabFeatureSprite sprite in data.FeatureSprites)
                    {
                        if (!dry.Contains(sprite.ArtId))
                            continue;
                        grass++;
                        Assert.That(KingdomSurvival.MapLab.MapLabArtBuilder.NearWater(data, sprite.Point), Is.False,
                            $"{theme} seed {seed}: трава {sprite.ArtId} на воде в {sprite.Point}");
                    }
                    if (flowChecked || !author)
                        continue;
                    flowChecked = true;
                    // Ветер обработки кадра (если автор его включил) качает траву и тени — для проверки реки кадр без обработки.
                    KingdomSurvival.BattlefieldDatabase.LocationPostEffects authorFrame = arena.Spec.Frame;
                    arena.Spec.Frame = null;
                    Color[] first = Frame(arena, "river_flow_0");
                    System.Threading.Thread.Sleep(900);
                    Color[] second = Frame(arena, "river_flow_1");
                    arena.Spec.Frame = authorFrame;
                    int changed = 0;
                    for (int i = 0; i < first.Length; i++)
                        if (Mathf.Abs(first[i].r - second[i].r) + Mathf.Abs(first[i].g - second[i].g) + Mathf.Abs(first[i].b - second[i].b) > 0.03f)
                            changed++;
                    Assert.That(changed, Is.GreaterThan(first.Length / 2000), "штрихи реки движутся между кадрами");
                    Assert.That(changed, Is.LessThan(first.Length / 5), "меняется только вода, не весь кадр");
                }
                finally
                {
                    arena.Dispose();
                }
            }
            Assert.That(rivers, Is.GreaterThan(0), "арены брода собрались с рекой");
            Assert.That(grass, Is.GreaterThan(0), "трава на арене есть (на суше)");

            Color[] Frame(MapLabArena arena, string name)
            {
                RenderTexture rt = arena.Render(1280, 720);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                Texture2D image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = previous;
                Color[] pixels = image.GetPixels();
                string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "MapLabArena");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
                return pixels;
            }
        }

        // Кадр поля арены рисуется; снимок — в Logs/MapLabArena/arena_frame.png.
        [Test]
        public void ArenaFrame_Renders()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            MapLabArena arena = MapLabArena.Build(MapLabArenaDriver.BuildSpec(null, Battle(5)));
            try
            {
                Assert.That(arena.Ready, Is.True, arena.Error);
                RenderTexture rt = arena.Render(1280, 720);
                Assert.That(rt, Is.Not.Null);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                Texture2D image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                RenderTexture.active = previous;
                string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "MapLabArena");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "arena_frame.png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                arena.Dispose();
            }
        }

        // Кадр арены с наложенной сеткой — в Temp/MapLabArena для просмотра.
        private static void SavePreview(MapLabArena arena, MapLabArenaSpec spec, List<HexCoord> cells, SandboxBattle battle, Rect frame, int seed)
        {
            RenderTexture rt = arena.Render((int)frame.width, (int)frame.height);
            Assert.That(rt, Is.Not.Null, "Кадр арены не нарисован");
            // Текстура кадра — 16:9 своего размера: сетка — в её пикселях.
            frame = new Rect(0f, 0f, rt.width, rt.height);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = previous;
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "MapLabArena");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, $"arena_seed{seed}.png"), image.EncodeToPNG());
            Relief relief = new Relief(arena);
            float radius = spec.HexNominal * frame.height * 0.94f;
            foreach (HexCoord cell in cells)
            {
                Color color = battle.GetTerrain(cell) == SandboxTerrain.Difficult ? new Color(0.95f, 0.75f, 0.2f)
                    : battle.GetTerrain(cell) == SandboxTerrain.Impassable ? new Color(0.8f, 0.2f, 0.2f) : new Color(0.15f, 0.12f, 0.08f);
                Vector2 flat = FrameCenter(spec, cells, cell, frame);
                for (int i = 0; i < 6; i++)
                {
                    Vector2 a = BattlefieldReliefMath.HexCorner(flat, radius, spec.VerticalScale, i);
                    Vector2 b = BattlefieldReliefMath.HexCorner(flat, radius, spec.VerticalScale, (i + 1) % 6);
                    for (int s = 0; s <= 40; s++)
                    {
                        Vector2 p = BattlefieldReliefMath.Raise(relief, frame, Vector2.Lerp(a, b, s / 40f));
                        int x = Mathf.RoundToInt(p.x), y = image.height - 1 - Mathf.RoundToInt(p.y);
                        if (x >= 1 && y >= 1 && x < image.width - 1 && y < image.height - 1)
                        {
                            image.SetPixel(x, y, color);
                            image.SetPixel(x + 1, y, color);
                            image.SetPixel(x, y + 1, color);
                        }
                    }
                }
            }
            image.Apply();
            File.WriteAllBytes(Path.Combine(directory, $"arena_seed{seed}_grid.png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
    }
}
