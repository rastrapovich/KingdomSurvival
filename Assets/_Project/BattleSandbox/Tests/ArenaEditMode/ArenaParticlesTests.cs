using System;
using System.Collections.Generic;
using System.IO;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering;
using KingdomSurvival.MapLab;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // 23.11.2026: частицы атмосферы арены (MapLabArena.Particles) — слой
    // включён: частицы есть; выключен: нет; слой «Ночью»/«В часы» днём не виден;
    // искры — только над клетками с огнём; число частиц ограничено. Кадры —
    // в Logs/ArenaParticles для осмотра.
    public sealed class ArenaParticlesTests
    {
        private const string ShotFolder = "Logs/ArenaParticles";

        private static SandboxBattle Battle() => SandboxRoster.CreateDefaultBattle(
            new[] { "guard", "archer", "spearman", "scout" }, SandboxRoster.PlayerRoster, SandboxRoster.EnemyRoster, 5);

        private static LocationPostEffects Post(Action<LocationAtmosphereEffects> setup)
        {
            LocationPostEffects post = new LocationPostEffects { Enabled = true }.Repair();
            setup(post.Atmosphere);
            return post;
        }

        private static MapLabArena Arena(LocationPostEffects frame, float hour)
        {
            MapLabArena arena = MapLabArena.Build(MapLabArenaDriver.BuildSpec(null, Battle()));
            Assert.That(arena.Ready, Is.True, arena.Error);
            arena.Spec.Frame = frame;
            arena.AtmosphereHour = hour;
            return arena;
        }

        private static void Skip()
        {
            if (!MapLabArena.Available)
                Assert.Ignore("Нет MapLabGameAssets с профилем — арена MapLab выключена.");
        }

        [Test]
        public void EnabledLayer_HasParticles_DisabledLayer_None()
        {
            Skip();
            MapLabArena arena = Arena(Post(a => { a.Dust.Enabled = true; a.Dust.Time = LocationParticleTime.Always; a.Dust.Density = 8; }), 13f);
            try
            {
                arena.Render(1280, 720);
                arena.AdvanceParticles(1f);
                Assert.That(arena.AtmosphereParticleCount, Is.GreaterThan(20), "пыль выпущена");
                Assert.That(arena.ParticlesActive, Is.True);

                arena.Spec.Frame = Post(a => { });
                arena.Render(1280, 720);
                Assert.That(arena.AtmosphereParticleCount, Is.EqualTo(0), "слой выключен — частиц нет");
                Assert.That(arena.ParticlesActive, Is.False);

                arena.Spec.Frame = null;
                arena.Render(1280, 720);
                Assert.That(arena.AtmosphereParticleCount, Is.EqualTo(0), "обработка кадра выключена — частиц нет");
            }
            finally { arena.Dispose(); }
        }

        [Test]
        public void NightAndHoursLayers_InvisibleByDay_VisibleAtTheirTime()
        {
            Skip();
            // Мошкара по умолчанию — в часы 17–22; пыль «Ночью».
            LocationPostEffects frame = Post(a =>
            {
                a.Midges.Enabled = true;
                a.Dust.Enabled = true; a.Dust.Time = LocationParticleTime.Night; a.Dust.Density = 8;
            });
            MapLabArena arena = Arena(frame, 13f);
            try
            {
                arena.Render(1280, 720);
                arena.AdvanceParticles(1f);
                Assert.That(arena.AtmosphereParticleCount, Is.EqualTo(0), "днём ночные слои и мошкара не видны");
                Assert.That(arena.ParticlesActive, Is.False, "днём кадр не перерисовывается ради невидимых частиц");

                arena.AtmosphereHour = 19.5f;
                arena.Render(1280, 720);
                arena.AdvanceParticles(1f);
                int evening = arena.AtmosphereParticleCount;
                Assert.That(evening, Is.GreaterThan(20), "вечером мошкара видна");

                arena.AtmosphereHour = 1f;
                arena.Spec.Frame = Post(a => { a.Dust.Enabled = true; a.Dust.Time = LocationParticleTime.Night; a.Dust.Density = 8; });
                arena.Render(1280, 720);
                arena.AdvanceParticles(1f);
                Assert.That(arena.AtmosphereParticleCount, Is.GreaterThan(20), "ночью пыль «Ночью» видна");
            }
            finally { arena.Dispose(); }
        }

        [Test]
        public void Sparks_OnlyOverFireCells_Breath_OnlyAtHeads()
        {
            Skip();
            LocationPostEffects frame = Post(a =>
            {
                a.Sparks.Enabled = true; a.Sparks.Time = LocationParticleTime.Always;
                a.Breath.Enabled = true; a.Breath.Time = LocationParticleTime.Always;
            });
            MapLabArena arena = Arena(frame, 13f);
            try
            {
                arena.Render(1280, 720);
                arena.AdvanceParticles(1f);
                Assert.That(arena.AtmosphereParticleCount, Is.EqualTo(0), "нет огня и голов — нет искр и пара");
                Assert.That(arena.WantsHeads, Is.True);

                Vector2 cell = arena.Spec.Cells[arena.Spec.Cells.Count / 2].Center;
                arena.SetSurfaces(new[] { (cell, MapLabArenaSurfaces.Bit(MapLabArenaSurface.Fire)) });
                List<Vector2> viewport = new List<Vector2>();
                arena.CollectFireViewport(viewport);
                Assert.That(viewport.Count, Is.EqualTo(1), "клетка с огнём найдена");
                Assert.That(viewport[0].x, Is.InRange(0f, 1f));
                Assert.That(viewport[0].y, Is.InRange(0f, 1f));
                arena.Render(1280, 720);
                arena.AdvanceParticles(1f);
                int sparks = arena.AtmosphereParticleCount;
                Assert.That(sparks, Is.GreaterThan(5), "искры над огнём");

                arena.SetHeads(new[] { cell + new Vector2(0f, -.1f) });
                arena.AdvanceParticles(1f);
                Assert.That(arena.AtmosphereParticleCount, Is.GreaterThan(sparks), "пар у головы добавил частиц");
            }
            finally { arena.Dispose(); }
        }

        // Рои мошкары разбросаны по кадру, а не слиплись в полосу; места устойчивы между кадрами.
        [Test]
        public void MidgeSwarms_AreSpreadAcrossFrame()
        {
            Skip();
            MapLabArena arena = Arena(Post(a => { a.Midges.Enabled = true; a.Midges.Groups = 3; }), 19.5f);
            try
            {
                arena.Render(1280, 720);
                arena.AdvanceParticles(1f);
                List<Vector2> shares = new List<Vector2>();
                arena.CollectSwarmShares(shares);
                Assert.That(shares.Count, Is.EqualTo(3));
                float minX = 9, maxX = -9, minY = 9, maxY = -9;
                foreach (Vector2 s in shares) { minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x); minY = Mathf.Min(minY, s.y); maxY = Mathf.Max(maxY, s.y); }
                Debug.Log("Рои: разброс по X " + (maxX - minX).ToString("0.00") + ", по Y " + (maxY - minY).ToString("0.00"));
                Assert.That(maxX - minX, Is.GreaterThanOrEqualTo(.25f), "рои разбросаны по ширине");
                Assert.That(maxY - minY, Is.GreaterThanOrEqualTo(.25f), "рои разбросаны по высоте");
                List<Vector2> before = new List<Vector2>(shares);
                arena.AdvanceParticles(.1f);
                arena.CollectSwarmShares(shares);
                for (int i = 0; i < 3; i++)
                    Assert.That((shares[i] - before[i]).magnitude, Is.LessThan(.05f), "рой не прыгает между кадрами");
            }
            finally { arena.Dispose(); }
        }

        [Test]
        public void ParticleCount_IsCapped()
        {
            Skip();
            MapLabArena arena = Arena(Post(a =>
            {
                a.Rain.Enabled = true; a.Rain.Density = 4000; a.Rain.Lifetime = 10;
                a.Dust.Enabled = true; a.Dust.Time = LocationParticleTime.Always; a.Dust.Density = 4000; a.Dust.Lifetime = 20;
            }), 13f);
            try
            {
                arena.Render(1280, 720);
                arena.AdvanceParticles(3f);
                Assert.That(arena.AtmosphereParticleCount, Is.LessThanOrEqualTo(3000), "не больше 1500 частиц у слоя");
            }
            finally { arena.Dispose(); }
        }

        [Test]
        public void Particles_AreDrawnInFrame_DayAndNightShots()
        {
            Skip();
            LocationPostEffects day = Post(a =>
            {
                a.Dust.Enabled = true; a.Dust.Time = LocationParticleTime.Always; a.Dust.Density = 10;
                a.Leaves.Enabled = true; a.Leaves.Density = 3;
                a.Rain.Enabled = true; a.Rain.Density = 60;
            });
            day.Sprites.Wind = true;
            MapLabArena arena = Arena(null, 13f);
            ArenaFrameBinding binding = new ArenaFrameBinding(arena, null);
            LocationPostEffects current = null;
            binding.State.PostSource = () => current;
            try
            {
                // Дневной кадр: без частиц и с ними — различаются.
                current = Post(a => { });
                arena.Spec.Frame = current;
                binding.BeforeRender();
                Texture2D clean = Read(arena.Render(1280, 720));
                current = day;
                arena.Spec.Frame = day;
                binding.BeforeRender();
                arena.Render(1280, 720);
                arena.AdvanceParticles(2f);
                Texture2D withParticles = Read(arena.Render(1280, 720));
                Save(withParticles, "day.png");
                float changed = Difference(clean, withParticles);
                Debug.Log("Частицы арены, день: доля изменившихся пикселей " + changed.ToString("0.0000") + ", частиц " + arena.AtmosphereParticleCount);
                Assert.That(changed, Is.GreaterThan(.0003f), "частицы нарисованы в кадре");

                // Вечер: мошкара (светлячки), искры и марево над огнём, пар у голов.
                LocationPostEffects night = Post(a =>
                {
                    a.Midges.Enabled = true;
                    a.Midges.Density = 120; a.Midges.Size = .22f; a.Midges.Opacity = 1f;
                    a.Midges.Color = new Color(1f, .95f, .5f); a.Midges.ColorB = new Color(.8f, 1f, .4f);
                    a.Sparks.Enabled = true;
                    a.Breath.Enabled = true; a.Breath.Time = LocationParticleTime.Always;
                });
                night.Air.HeatHaze = true;
                current = night;
                arena.Spec.Frame = night;
                arena.AtmosphereHour = 19.5f;
                Vector2 cell = arena.Spec.Cells[arena.Spec.Cells.Count / 2].Center;
                arena.SetSurfaces(new[] { (cell, MapLabArenaSurfaces.Bit(MapLabArenaSurface.Fire)) });
                arena.SetHeads(new[] { cell + new Vector2(.25f, -.1f), cell + new Vector2(-.3f, .05f) });
                binding.BeforeRender();
                arena.Render(1280, 720);
                arena.AdvanceParticles(3f);
                Texture2D evening = Read(arena.Render(1280, 720));
                Save(evening, "evening.png");
                Assert.That(binding.Animating, Is.True, "огонь с маревом — кадр живой");
                Assert.That(arena.AtmosphereParticleCount, Is.GreaterThan(50), "вечером частицы есть");
                // Огонь крупно: искры и марево. Марево включено/выключено — кадр у огня различается.
                LocationPostEffects fire = Post(a => { a.Sparks.Enabled = true; a.Sparks.Density = 40; a.Sparks.Size = .25f; });
                fire.Air.HeatHaze = true;
                fire.Air.HazeStrength = 2f;
                current = fire;
                arena.Spec.Frame = fire;
                arena.SetHeads(null);
                binding.BeforeRender();
                arena.Render(1280, 720);
                arena.AdvanceParticles(2f);
                Texture2D hot = Read(arena.Render(1280, 720));
                Save(Crop(hot, arena, cell), "fire.png");
                fire.Air.HeatHaze = false;
                Texture2D noHaze = Read(arena.Render(1280, 720));
                float hazeChange = Difference(hot, noHaze);
                Debug.Log("Марево арены: доля изменившихся пикселей " + hazeChange.ToString("0.0000"));
                Assert.That(hazeChange, Is.GreaterThan(.0002f), "марево над огнём искажает кадр");
                Object.DestroyImmediate(hot);
                Object.DestroyImmediate(noHaze);
                Object.DestroyImmediate(clean);
                Object.DestroyImmediate(withParticles);
                Object.DestroyImmediate(evening);
            }
            finally
            {
                binding.Dispose();
                arena.Dispose();
            }
        }

        private static Texture2D Read(RenderTexture texture)
        {
            Assert.That(texture, Is.Not.Null);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            Texture2D copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            return copy;
        }

        // Вырезка 400x300 вокруг клетки (доли кадра, Y вниз).
        private static Texture2D Crop(Texture2D image, MapLabArena arena, Vector2 cell)
        {
            int cx = Mathf.RoundToInt(cell.x / arena.Spec.Aspect * image.width), cy = Mathf.RoundToInt((1f - cell.y) * image.height);
            int x = Mathf.Clamp(cx - 200, 0, image.width - 400), y = Mathf.Clamp(cy - 150, 0, image.height - 300);
            Texture2D crop = new Texture2D(400, 300, TextureFormat.RGBA32, false);
            crop.SetPixels(image.GetPixels(x, y, 400, 300));
            crop.Apply();
            return crop;
        }

        private static void Save(Texture2D image, string name)
        {
            Directory.CreateDirectory(ShotFolder);
            File.WriteAllBytes(Path.Combine(ShotFolder, name), image.EncodeToPNG());
        }

        private static float Difference(Texture2D a, Texture2D b)
        {
            Color32[] pa = a.GetPixels32(), pb = b.GetPixels32();
            int changed = 0;
            for (int i = 0; i < pa.Length; i++)
                if (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b) > 24)
                    changed++;
            return changed / (float)pa.Length;
        }
    }
}
