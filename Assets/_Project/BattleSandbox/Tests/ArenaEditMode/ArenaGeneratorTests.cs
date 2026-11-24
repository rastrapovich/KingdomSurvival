using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // «Генератор арен» (20.11.2026): общие настройки генерации меняют клетки
    // и рисунок арены, по умолчанию — прежние арены; обработка кадра арены
    // общая, меняет кадр и не трогает прозрачность слоя
    // объектов.
    public sealed class ArenaGeneratorTests
    {
        private SandboxArenaGenerationSettings saved;

        [SetUp]
        public void SetUp() => saved = SandboxArenaGenerationSettings.Current;

        [TearDown]
        public void TearDown() => SandboxArenaGenerationSettings.Current = saved;

        private static IReadOnlyCollection<SandboxUnitState> Units() =>
            SandboxRoster.CreateDefaultBattle(new[] { "guard", "archer", "spearman", "scout" }, SandboxRoster.PlayerRoster, SandboxRoster.EnemyRoster, 1).Units;

        private static string Key(SandboxArenaPlan plan) =>
            string.Join(";", plan.Terrain.OrderBy(p => p.Key.Q).ThenBy(p => p.Key.R).Select(p => p.Key + "t" + (int)p.Value)) + "|" +
            string.Join(";", plan.Ground.OrderBy(p => p.Key.Q).ThenBy(p => p.Key.R).Select(p => p.Key + "g" + (int)p.Value)) + "|" +
            string.Join(";", plan.Vegetation.OrderBy(p => p.Key.Q).ThenBy(p => p.Key.R).Select(p => p.Key + "v" + (int)p.Value)) + "|" +
            string.Join(";", plan.Objects.Select(o => o.Kind + ":" + string.Join(",", o.Cells)));

        [Test]
        public void DefaultSettings_GiveSameArenasAsBefore()
        {
            IReadOnlyCollection<SandboxUnitState> units = Units();
            foreach (SandboxArenaTheme theme in SandboxArenaThemes.All)
                for (int seed = 0; seed < 6; seed++)
                {
                    SandboxArenaGenerationSettings.Current = new SandboxArenaGenerationSettings();
                    string baseline = Key(SandboxArenaGenerator.Generate(theme, seed, units));
                    ArenaGeneratorAsset fresh = ScriptableObject.CreateInstance<ArenaGeneratorAsset>();
                    SandboxArenaGenerationSettings.Current = fresh.Tactics;
                    Assert.That(Key(SandboxArenaGenerator.Generate(theme, seed, units)), Is.EqualTo(baseline), theme + " seed " + seed);
                    Object.DestroyImmediate(fresh);
                }
        }

        [Test]
        public void HillAndVegetationAmounts_ZeroRemoveThemFromPlans()
        {
            IReadOnlyCollection<SandboxUnitState> units = Units();
            SandboxArenaGenerationSettings.Current = new SandboxArenaGenerationSettings { HillAmount = 0f, VegetationAmount = 0f, ForestAmount = 0f };
            for (int seed = 0; seed < 10; seed++)
            {
                SandboxArenaPlan plan = SandboxArenaGenerator.Generate(SandboxArenaTheme.DryGlade, seed, units);
                Assert.That(plan.Terrain.Values.Count(t => t == SandboxTerrain.Difficult), Is.Zero, "холмов нет, seed " + seed);
                Assert.That(plan.Vegetation.Count, Is.Zero, "кустарника нет, seed " + seed);
            }
            SandboxArenaGenerationSettings.Current = new SandboxArenaGenerationSettings { HillAmount = 2.5f };
            int more = 0, normal = 0;
            for (int seed = 0; seed < 10; seed++)
            {
                more += SandboxArenaGenerator.Generate(SandboxArenaTheme.RockySlope, seed, units).Terrain.Values.Count(t => t == SandboxTerrain.Difficult);
                SandboxArenaGenerationSettings.Current = new SandboxArenaGenerationSettings();
                normal += SandboxArenaGenerator.Generate(SandboxArenaTheme.RockySlope, seed, units).Terrain.Values.Count(t => t == SandboxTerrain.Difficult);
                SandboxArenaGenerationSettings.Current = new SandboxArenaGenerationSettings { HillAmount = 2.5f };
            }
            Assert.That(more, Is.GreaterThan(normal), "объём холмов 2,5 даёт больше холмов");
        }

        // 24.11.2026: лесные клетки у всех тем — как холмы, пятнами в средней
        // части поля, только на пустых клетках; объём 0 — без них.
        [Test]
        public void ForestCells_AppearInAllThemes_OnFreeCellsOnly()
        {
            IReadOnlyCollection<SandboxUnitState> units = Units();
            HashSet<HexCoord> spawns = new HashSet<HexCoord>(units.Select(u => u.Position));
            SandboxArenaGenerationSettings.Current = new SandboxArenaGenerationSettings();
            foreach (SandboxArenaTheme theme in SandboxArenaThemes.All)
            {
                int forest = 0;
                for (int seed = 0; seed < 8; seed++)
                {
                    SandboxArenaPlan plan = SandboxArenaGenerator.Generate(theme, seed, units);
                    foreach (KeyValuePair<HexCoord, SandboxVegetation> pair in plan.Vegetation.Where(p => p.Value == SandboxVegetation.Forest))
                    {
                        forest++;
                        // Полоса леса опушки — свой рецепт (стволы в лесу, лес у края).
                        if (theme == SandboxArenaTheme.ForestEdge)
                            continue;
                        Assert.That(plan.Blocks(pair.Key), Is.False, theme + ": лес на закрытой клетке " + pair.Key);
                        Assert.That(plan.Terrain.ContainsKey(pair.Key), Is.False, theme + ": лес на холме " + pair.Key);
                    }
                    if (theme != SandboxArenaTheme.ForestEdge)
                        foreach (HexCoord spawn in spawns)
                            Assert.That(plan.Vegetation.TryGetValue(spawn, out SandboxVegetation v) && v == SandboxVegetation.Forest, Is.False, theme + ": лес на старте " + spawn);
                }
                Assert.That(forest, Is.GreaterThan(0), theme + ": лесные клетки есть");
            }
            SandboxArenaGenerationSettings.Current = new SandboxArenaGenerationSettings { ForestAmount = 0f };
            for (int seed = 0; seed < 8; seed++)
                Assert.That(SandboxArenaGenerator.Generate(SandboxArenaTheme.DryGlade, seed, units).Vegetation.ContainsValue(SandboxVegetation.Forest), Is.False, "объём 0 — без леса");
        }

        [Test]
        public void WideRiverWithThreeFords_StaysCrossableAndWider()
        {
            IReadOnlyCollection<SandboxUnitState> units = Units();
            int wideDeep = 0, narrowDeep = 0, valid = 0, rowsWithFord = 0;
            for (int seed = 0; seed < 12; seed++)
            {
                SandboxArenaGenerationSettings.Current = new SandboxArenaGenerationSettings();
                narrowDeep += SandboxArenaGenerator.Generate(SandboxArenaTheme.Ford, seed, units).Ground.Values.Count(g => g == SandboxGround.DeepWater);
                SandboxArenaGenerationSettings.Current = new SandboxArenaGenerationSettings { RiverWidth = 2, FordCount = 3, RiverBend = 0 };
                SandboxArenaPlan plan = SandboxArenaGenerator.Generate(SandboxArenaTheme.Ford, seed, units);
                wideDeep += plan.Ground.Values.Count(g => g == SandboxGround.DeepWater);
                if (!plan.Report.BareFallback && plan.Report.Issues.Count == 0)
                {
                    valid++;
                    rowsWithFord = Mathf.Max(rowsWithFord, plan.Ground.Where(p => p.Value == SandboxGround.ShallowWater).Select(p => p.Key.R).Distinct().Count());
                }
            }
            Assert.That(valid, Is.GreaterThan(6), "широкая река чаще всего проходит проверку связности");
            Assert.That(wideDeep, Is.GreaterThan(narrowDeep), "русло шире");
            Assert.That(rowsWithFord, Is.GreaterThanOrEqualTo(3), "три брода в разных рядах");
        }

        [Test]
        public void MoreGrass_MoreDecor_SameRelief()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            SandboxBattle battle = SandboxRoster.CreateDefaultBattle(new[] { "guard", "archer", "spearman", "scout" }, SandboxRoster.PlayerRoster, SandboxRoster.EnemyRoster, 5);
            MapLabArenaSpec plain = MapLabArenaDriver.BuildSpec(null, battle);
            plain.Look = new MapLabArenaLook();
            MapLabArenaSpec grassy = MapLabArenaDriver.BuildSpec(null, battle);
            grassy.Look = new MapLabArenaLook { GrassPatchesMin = 40, GrassPatchesMax = 50, GrassPerPatchMin = 10, GrassPerPatchMax = 16, GrassSpacing = .4f };
            MapLabArena a = MapLabArena.Build(plain), b = MapLabArena.Build(grassy);
            try
            {
                Assert.That(a.Ready, Is.True, a.Error);
                Assert.That(b.Ready, Is.True, b.Error);
                if (!a.UsesAssetDatabase)
                    Assert.Ignore("В Базе ассетов меньше трёх деревьев для арены — декор MapLab по-старому.");
                Debug.Log($"Генератор арен: элементов декора — обычно {a.DecorFeatures}, «много травы» {b.DecorFeatures}.");
                Assert.That(b.DecorFeatures, Is.GreaterThan(a.DecorFeatures * 2), "травы заметно больше");
                foreach ((Vector2 center, MapLabArenaCellKind _) in plain.Cells)
                {
                    Vector2 share = new Vector2(center.x / plain.Aspect, center.y);
                    Assert.That(b.LiftShare(share), Is.EqualTo(a.LiftShare(share)).Within(1e-4f), "рельеф от травы не зависит");
                }
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
            // Кадры — по одной арене (две сразу на одном слое видят друг друга).
            foreach ((MapLabArenaSpec spec, string name) in new[] { (plain, "grass_default.png"), (grassy, "grass_many.png"), (null, "grass_carpet.png") })
            {
                MapLabArenaSpec copy = MapLabArenaDriver.BuildSpec(null, battle);
                copy.Look = spec != null ? spec.Look : new MapLabArenaLook { GrassPatchesMin = 40, GrassPatchesMax = 50, GrassPerPatchMin = 10, GrassPerPatchMax = 16, GrassSpacing = .4f, GrassScale = 1.3f, GrassCarpet = 10f, GrassCarpetScale = 1.2f, GrassSingles = 1.5f };
                MapLabArena single = MapLabArena.Build(copy);
                try { if (single.Ready) Shot(single, name); }
                finally { single.Dispose(); }
            }
        }

        [Test]
        public void FramePost_ChangesFrame_KeepsObjectLayerAlpha()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            SandboxBattle battle = SandboxRoster.CreateDefaultBattle(new[] { "guard", "archer", "spearman", "scout" }, SandboxRoster.PlayerRoster, SandboxRoster.EnemyRoster, 3);
            MapLabArenaSpec spec = MapLabArenaDriver.BuildSpec(null, battle);
            spec.Frame = null;
            MapLabArena arena = MapLabArena.Build(spec);
            try
            {
                Assert.That(arena.Ready, Is.True, arena.Error);
                Color[] ground = Read(arena.Render(640, 360), out _);
                Color[] objects = Read(arena.OverlayTexture, out bool hasObjects);
                LocationPostEffects post = new LocationPostEffects { Enabled = true, Bloom = false };
                post.Day.Saturation = -100;
                post.Day.Exposure = -1;
                arena.Spec.Frame = post;
                Color[] gradedGround = Read(arena.Render(640, 360), out _);
                Color[] gradedObjects = Read(arena.OverlayTexture, out _);
                Assert.That(Saturation(gradedGround), Is.LessThan(Saturation(ground) * .3f), "земля обесцвечена обработкой кадра");
                Assert.That(Brightness(gradedGround), Is.LessThan(Brightness(ground)), "экспозиция −1 темнее");
                if (hasObjects)
                {
                    float alphaDelta = 0f;
                    for (int i = 0; i < objects.Length; i++) alphaDelta = Mathf.Max(alphaDelta, Mathf.Abs(objects[i].a - gradedObjects[i].a));
                    Assert.That(alphaDelta, Is.LessThan(.02f), "прозрачность слоя объектов та же");
                    Assert.That(Saturation(gradedObjects, true), Is.LessThan(Saturation(objects, true) * .3f + .01f), "деревья и камни обработаны так же");
                }
                arena.Spec.Frame = null;
                Color[] back = Read(arena.Render(640, 360), out _);
                Assert.That(Saturation(back), Is.EqualTo(Saturation(ground)).Within(.01f), "без обработки — прежний кадр");
            }
            finally
            {
                arena.Dispose();
            }
        }

        [Test]
        public void GrassZone_GladeAndOutside_ShiftGrassBetweenFieldAndEdges()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            SandboxBattle battle = SandboxRoster.CreateDefaultBattle(new[] { "guard", "archer", "spearman", "scout" }, SandboxRoster.PlayerRoster, SandboxRoster.EnemyRoster, 7);
            float Share(MapLabArenaLook look)
            {
                MapLabArenaSpec spec = MapLabArenaDriver.BuildSpec(null, battle);
                spec.Look = look;
                MapLabArena arena = MapLabArena.Build(spec);
                try
                {
                    Assert.That(arena.Ready, Is.True, arena.Error);
                    return arena.GrassOnFieldShare;
                }
                finally { arena.Dispose(); }
            }
            MapLabArenaLook Look(float inField, float outside) => new MapLabArenaLook { GrassCarpet = 6f, GrassSingles = 1f, GrassInField = inField, GrassOutsideField = outside };
            float even = Share(Look(1f, 1f)), glade = Share(Look(3f, .3f)), edges = Share(Look(.3f, 3f));
            Debug.Log($"Генератор арен: доля травы на поле — ровно {even:0.00}, поляна {glade:0.00}, гуще за полем {edges:0.00}.");
            Assert.That(glade, Is.GreaterThan(even + .1f), "поляна: на поле травы заметно больше");
            Assert.That(edges, Is.LessThan(even - .1f), "гуще за полем: на поле травы заметно меньше");
        }

        [Test]
        public void ArenaCanvas_LetterboxDrawnOnArenaFrame()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
            SandboxBattle battle = SandboxRoster.CreateDefaultBattle(new[] { "guard", "archer", "spearman", "scout" }, SandboxRoster.PlayerRoster, SandboxRoster.EnemyRoster, 3);
            MapLabArenaSpec spec = MapLabArenaDriver.BuildSpec(null, battle);
            MapLabArena arena = MapLabArena.Build(spec);
            LocationPostEffects post = new LocationPostEffects { Enabled = true, Bloom = false };
            post.Game.Letterbox = .2f;
            post.Game.LetterboxColor = new Color(1f, 0f, 1f, 1f);
            KingdomSurvival.LocationRendering.ArenaFrameState state = new KingdomSurvival.LocationRendering.ArenaFrameState { PostSource = () => post };
            try
            {
                Assert.That(arena.Ready, Is.True, arena.Error);
                KingdomSurvival.LocationRendering.LocationCanvasPass.Bind(arena.FrameCamera, state);
                arena.Spec.Frame = post;
                RenderTexture frame = arena.Render(640, 360);
                Color[] pixels = Read(frame, out _);
                Color top = pixels[(frame.height - 3) * frame.width + frame.width / 2];
                Debug.Log("Холст на арене: верхняя полоса " + top);
                Assert.That(top.r, Is.GreaterThan(.8f), "кинополоса цвета раздела «Игра» сверху кадра арены");
                Assert.That(top.g, Is.LessThan(.2f));
                Assert.That(top.b, Is.GreaterThan(.8f));
            }
            finally
            {
                KingdomSurvival.LocationRendering.LocationCanvasPass.Unbind(state);
                arena.Dispose();
            }
        }

        private static void Shot(MapLabArena arena, string name)
        {
            RenderTexture frame = arena.Render(1280, 720);
            Color[] ground = Read(frame, out _);
            Color[] objects = Read(arena.OverlayTexture, out bool any);
            if (any && objects.Length == ground.Length)
                for (int i = 0; i < ground.Length; i++) ground[i] = Color.Lerp(ground[i], new Color(objects[i].r, objects[i].g, objects[i].b, 1f), objects[i].a);
            Texture2D image = new Texture2D(frame.width, frame.height, TextureFormat.RGBA32, false);
            image.SetPixels(ground);
            image.Apply();
            System.IO.Directory.CreateDirectory("Logs/ArenaGenerator");
            System.IO.File.WriteAllBytes("Logs/ArenaGenerator/" + name, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }

        private static Color[] Read(RenderTexture texture, out bool any)
        {
            any = texture != null;
            if (texture == null) return new Color[0];
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            Texture2D copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            Color[] pixels = copy.GetPixels();
            Object.DestroyImmediate(copy);
            return pixels;
        }

        private static float Saturation(Color[] pixels, bool opaqueOnly = false)
        {
            double sum = 0;
            int count = 0;
            for (int i = 0; i < pixels.Length; i += 7)
            {
                if (opaqueOnly && pixels[i].a < .9f) continue;
                Color.RGBToHSV(pixels[i], out _, out float s, out _);
                sum += s;
                count++;
            }
            return count > 0 ? (float)(sum / count) : 0f;
        }

        private static float Brightness(Color[] pixels)
        {
            double sum = 0;
            for (int i = 0; i < pixels.Length; i += 7) sum += pixels[i].grayscale;
            return (float)(sum / Mathf.Max(1, pixels.Length / 7));
        }
    }
}
