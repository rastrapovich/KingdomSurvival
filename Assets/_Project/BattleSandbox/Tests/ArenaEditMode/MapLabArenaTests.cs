using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
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

        [Test]
        public void Spec_FollowsAcceptedBattleLayout_AndIsStableForSameLayout()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                SandboxBattle battle = Battle(seed);
                MapLabArenaSpec spec = MapLabArenaDriver.BuildSpec(null, battle);
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
                MapLabArenaSpec spec = MapLabArenaDriver.BuildSpec(null, battle);
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
            MapLabArenaSpec sparse = MapLabArenaDriver.BuildSpec(null, battle);
            sparse.DecorDensity = 0.3f;
            MapLabArenaSpec dense = MapLabArenaDriver.BuildSpec(null, battle);
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
