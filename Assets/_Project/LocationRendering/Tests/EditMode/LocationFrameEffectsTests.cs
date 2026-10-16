using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12М: окно «Обработка кадра» — стили (запомнить, применить по
    // разделам, сбросить), атмосфера, эффекты рисунков и их материалы.
    public sealed class LocationFrameEffectsTests
    {
        private static (LocalLocationDefinition, LocationVisualDefinition) Place()
        {
            LocalLocationDefinition location = new LocalLocationDefinition { Id = "zz_frame", DisplayName = "Поляна", CanvasWidth = 1920, CanvasHeight = 1080 };
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            return (location, visual);
        }

        [Test]
        public void StyleCopy_TakesOnlyChosenSections_AndIsIndependent()
        {
            LocationPostEffects style = new LocationPostEffects();
            style.Day.Saturation = -40;
            style.Bloom = false;
            style.Tone.SplitToning = true;
            style.Canvas.Fog = true;
            style.Atmosphere.Rain.Enabled = true;
            LocationPostEffects place = new LocationPostEffects { Enabled = false };

            LocationFrameSections.Copy(style, place, LocationFrameSection.Color | LocationFrameSection.Canvas);
            Assert.That(place.Enabled, Is.True, "Применённый стиль включает обработку.");
            Assert.That(place.Day.Saturation, Is.EqualTo(-40), "Цвет взят.");
            Assert.That(place.Canvas.Fog, Is.True, "Холст взят.");
            Assert.That(place.Bloom, Is.True, "Свет и линза — нет.");
            Assert.That(place.Tone.SplitToning, Is.False, "Тон — нет.");
            Assert.That(place.Atmosphere.Rain.Enabled, Is.False, "Атмосфера — нет.");

            style.Canvas.FogDensity = .9f;
            Assert.That(place.Canvas.FogDensity, Is.Not.EqualTo(.9f), "Копия, а не ссылка: стиль и место дальше живут отдельно.");
        }

        [Test]
        public void SectionReset_ReturnsNeutral_KeepsSwitch()
        {
            LocationPostEffects post = new LocationPostEffects { Enabled = true };
            post.Canvas.Paper = true;
            post.Sprites.Wind = true;
            LocationFrameSections.Reset(post, LocationFrameSection.Canvas);
            Assert.That(post.Canvas.Paper, Is.False);
            Assert.That(post.Sprites.Wind, Is.True, "Другие разделы не тронуты.");
            Assert.That(post.Enabled, Is.True, "Главный выключатель не трогается.");
        }

        [Test]
        public void OldData_WithoutNewSections_IsRepaired()
        {
            LocationPostEffects old = JsonUtility.FromJson<LocationPostEffects>("{\"Enabled\":true,\"Grain\":0.3}");
            old.Tone = null;
            old.Atmosphere = null;
            old.Repair();
            Assert.That(old.Tone, Is.Not.Null);
            Assert.That(old.Atmosphere.Breath, Is.Not.Null);
            Assert.That(old.Tone.Master.length, Is.GreaterThan(0), "Кривые — тождественные, а не пустые.");
        }

        [TestCase(LocationParticleTime.Always, 13f, 1f, true)]
        [TestCase(LocationParticleTime.Day, 13f, 1f, true)]
        [TestCase(LocationParticleTime.Day, 1f, 0f, false)]
        [TestCase(LocationParticleTime.Night, 1f, 0f, true)]
        [TestCase(LocationParticleTime.Night, 13f, 1f, false)]
        public void ParticleVisibility_FollowsDaylight(LocationParticleTime time, float hour, float day, bool visible)
        {
            LocationParticleLayer layer = new LocationParticleLayer { Enabled = true, Time = time };
            Assert.That(layer.Visibility(hour, day) > .5f, Is.EqualTo(visible));
            layer.Enabled = false;
            Assert.That(layer.Visibility(hour, day), Is.EqualTo(0), "Выключенный слой не виден никогда.");
        }

        [TestCase(20f, true)]
        [TestCase(2f, true)]
        [TestCase(12f, false)]
        public void ParticleHours_CrossMidnight(float hour, bool visible)
        {
            LocationParticleLayer layer = new LocationParticleLayer { Enabled = true, Time = LocationParticleTime.Hours, FromHour = 18, ToHour = 4 };
            Assert.That(layer.Visibility(hour, .5f) > .5f, Is.EqualTo(visible));
        }

        [Test]
        public void SpriteStructure_ChangesOnlyWithSpriteEffects()
        {
            LocationPostEffects post = new LocationPostEffects { Enabled = true };
            string plain = LocationWorldRenderer.SpriteStructureKey(post);
            post.Canvas.Fog = true;
            post.Bloom = true;
            Assert.That(LocationWorldRenderer.SpriteStructureKey(post), Is.EqualTo(plain), "Холст и свет — без пересборки места.");
            post.Sprites.Wind = true;
            Assert.That(LocationWorldRenderer.SpriteStructureKey(post), Is.Not.EqualTo(plain), "Ветер — материалы рисунков другие.");
            post.Enabled = false;
            Assert.That(LocationWorldRenderer.SpriteStructureKey(post), Is.EqualTo(LocationWorldRenderer.SpriteStructureKey(null)));
            Assert.That(LocationWorldRenderer.SpriteStructureKey(null).Length, Is.EqualTo(2));
        }

        [Test]
        public void Renderer_AppliesToneLensAndQuality_AndHidesForBefore()
        {
            (LocalLocationDefinition location, LocationVisualDefinition visual) = Place();
            visual.Post = new LocationPostEffects { Enabled = true };
            visual.Post.Tone.SplitToning = true;
            visual.Post.Lens.ChromaticAberration = .3f;
            visual.Post.Quality.Antialiasing = LocationAntialiasing.SmaaHigh;
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                renderer.SetTime(13, 1);
                UniversalAdditionalCameraData data = renderer.Camera.GetUniversalAdditionalCameraData();
                Assert.That(renderer.PostActive, Is.True);
                Assert.That(data.renderPostProcessing, Is.True);
                Assert.That(data.antialiasing, Is.EqualTo(AntialiasingMode.SubpixelMorphologicalAntiAliasing));
                Assert.That(data.antialiasingQuality, Is.EqualTo(AntialiasingQuality.High));
                VolumeProfile profile = renderer.Root.GetComponentInChildren<Volume>().sharedProfile;
                Assert.That(profile.TryGet(out SplitToning split) && split.active, Is.True, "Разделение тонов — в объёме места.");
                Assert.That(profile.TryGet(out ChromaticAberration aberration) && aberration.active && Mathf.Approximately(aberration.intensity.value, .3f), Is.True);

                renderer.ShowFrameEffects(false);
                Assert.That(renderer.PostActive, Is.False, "«До» сравнения — без обработки.");
                Assert.That(data.renderPostProcessing, Is.False);
                Assert.That(renderer.ActiveCanvas, Is.Null);
                renderer.ShowFrameEffects(true);
                Assert.That(renderer.PostActive, Is.True);
            }
        }

        // Камера предпросмотра окна (PreviewRenderUtility) — типа Preview, а к
        // такой URP не применяет обработку кадра и свои проходы: окно
        // показывало бы кадр без единого эффекта.
        [Test]
        public void BoundPreviewCamera_RendersAsGameCamera()
        {
            (LocalLocationDefinition location, LocationVisualDefinition visual) = Place();
            GameObject host = new GameObject("Камера предпросмотра");
            try
            {
                Camera camera = host.AddComponent<Camera>();
                camera.cameraType = CameraType.Preview;
                using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
                {
                    renderer.BindCamera(camera);
                    Assert.That(camera.cameraType, Is.EqualTo(CameraType.Game));
                    Assert.That(camera.GetUniversalAdditionalCameraData().volumeLayerMask.value, Is.EqualTo(1 << LocationWorldRenderer.PostVolumeLayer));
                }
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void Pulse_AddsLensForItsDuration()
        {
            (LocalLocationDefinition location, LocationVisualDefinition visual) = Place();
            visual.Post = new LocationPostEffects { Enabled = true };
            visual.Post.Lens.PulseAberration = .8f;
            visual.Post.Lens.PulseDuration = .5f;
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                renderer.SetTime(13, 10);
                VolumeProfile profile = renderer.Root.GetComponentInChildren<Volume>().sharedProfile;
                profile.TryGet(out ChromaticAberration aberration);
                Assert.That(aberration.active, Is.False, "Без всплеска линза чистая.");
                renderer.Pulse();
                renderer.SetTime(13, 10.05f);
                Assert.That(aberration.active && aberration.intensity.value > .5f, Is.True, "На пике — аберрация всплеска.");
                renderer.SetTime(13, 10.6f);
                Assert.That(aberration.active, Is.False, "После длительности — сошла на нет.");
            }
        }

        [Test]
        public void Atmosphere_EmitsOnlyWhenEnabledAndVisible()
        {
            (LocalLocationDefinition location, LocationVisualDefinition visual) = Place();
            visual.Post = new LocationPostEffects { Enabled = true };
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                for (float t = 0; t < 1; t += .1f) renderer.SetTime(13, t);
                Assert.That(renderer.AtmosphereParticleCount, Is.EqualTo(0), "Слои выключены — частиц нет.");

                visual.Post.Atmosphere.Dust.Enabled = true;
                visual.Post.Atmosphere.Dust.Time = LocationParticleTime.Always;
                for (float t = 1; t < 2; t += .1f) renderer.SetTime(13, t);
                Assert.That(renderer.AtmosphereParticleCount, Is.GreaterThan(0), "Пыль включена — летит.");

                visual.Post.Enabled = false;
                renderer.SetTime(13, 2.1f);
                Assert.That(renderer.AtmosphereParticleCount, Is.EqualTo(0), "Обработка выключена — частицы убраны.");
            }
        }

        [Test]
        public void SpriteEffects_SwitchPeopleToStyledMaterial_OnlyWhenNeeded()
        {
            (LocalLocationDefinition location, LocationVisualDefinition visual) = Place();
            LocationWorldRenderer.ActorFrame[] hero = { new LocationWorldRenderer.ActorFrame { Id = "hero", Pixel = new Vector2(960, 700) } };
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                renderer.SetActors(hero, 0);
                SpriteRenderer image = Hero(renderer);
                Assert.That(image.sharedMaterial.shader.name, Is.Not.EqualTo("Kingdom Survival/Location Sprite Adjust"), "Без эффектов рисунков — прежний общий материал.");
            }
            visual.Post = new LocationPostEffects { Enabled = true };
            visual.Post.Sprites.Outline = true;
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                renderer.SetActors(hero, 0);
                SpriteRenderer image = Hero(renderer);
                Assert.That(image.sharedMaterial.shader.name, Is.EqualTo("Kingdom Survival/Location Sprite Adjust"), "Обводка людей — материалом со стилем.");
                Assert.That(image.sharedMaterial.GetVector("_KsStyle").y, Is.EqualTo(1), "Роль — человек.");
                renderer.SetTime(13, 1);
                Assert.That(Shader.GetGlobalVector("_KsOutlineParams").w, Is.EqualTo(1), "Обводка включена в глобальных значениях.");
                LocationWorldRenderer.ClearFrameGlobals();
                Assert.That(Shader.GetGlobalVector("_KsOutlineParams").w, Is.EqualTo(0), "После кадра — снята.");
            }
        }

        private static SpriteRenderer Hero(LocationWorldRenderer renderer)
        {
            foreach (SpriteRenderer image in renderer.Root.GetComponentsInChildren<SpriteRenderer>())
                if (image.transform.parent != null && image.transform.parent.name == "hero") return image;
            Assert.Fail("Фигура героя не найдена.");
            return null;
        }

        [Test]
        public void NeutralLut_MapsEachCellToItsColor()
        {
            Color corner = LutTools.NeutralAt(0, 0);
            Assert.That(corner, Is.EqualTo(new Color(0, 0, 0, 1)));
            Color white = LutTools.NeutralAt(LutTools.Size * LutTools.Size - 1, LutTools.Size - 1);
            Assert.That(white.r, Is.EqualTo(1).Within(1e-4f));
            Assert.That(white.g, Is.EqualTo(1).Within(1e-4f));
            Assert.That(white.b, Is.EqualTo(1).Within(1e-4f));
            Color blueCell = LutTools.NeutralAt(LutTools.Size * 5 + 3, 7);
            Assert.That(blueCell.r, Is.EqualTo(3 / 31f).Within(1e-4f), "Красный — внутри клетки по x.");
            Assert.That(blueCell.b, Is.EqualTo(5 / 31f).Within(1e-4f), "Синий — номер клетки.");
            Assert.That(blueCell.g, Is.EqualTo(7 / 31f).Within(1e-4f), "Зелёный — по y.");
        }
    }
}
