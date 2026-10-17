using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12М: состояния героя на экране, удар (вспышка, тряска), уровни
    // качества и новые разделы данных обработки кадра.
    public sealed class LocationFrameStatesTests
    {
        private static LocationWorldRenderer Renderer(out LocationVisualDefinition visual)
        {
            LocalLocationDefinition location = new LocalLocationDefinition { Id = "zz_states", DisplayName = "Поляна", CanvasWidth = 1920, CanvasHeight = 1080 };
            visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            visual.Post = new LocationPostEffects { Enabled = true };
            return new LocationWorldRenderer(location, visual, null);
        }

        [Test]
        public void HeroStates_FollowGameCalls()
        {
            using (LocationWorldRenderer renderer = Renderer(out LocationVisualDefinition visual))
            {
                renderer.SetTime(13, 1);
                LocationFrameRuntime state = renderer.FrameRuntime();
                Assert.That(state.LowHealth, Is.EqualTo(0), "Здоров — без красных краёв.");
                renderer.SetHeroHealth(.1f);
                Assert.That(renderer.FrameRuntime().LowHealth, Is.GreaterThan(.5f), "Ниже порога — края.");

                renderer.SetStun(1, .5f);
                renderer.SetTime(13, 1.25f);
                renderer.SetTime(13, 1.5f);
                Assert.That(renderer.FrameRuntime().Stun, Is.EqualTo(1).Within(.05f), "Оглушение дошло за полсекунды.");

                renderer.FadeTo(1, 1);
                renderer.SetTime(13, 2.6f);
                Assert.That(renderer.FrameRuntime().Fade, Is.EqualTo(1).Within(.01f));

                visual.Post.Game.Letterbox = .05f;
                renderer.SetLetterbox(.12f, 0);
                Assert.That(renderer.FrameRuntime().Letterbox, Is.EqualTo(.12f).Within(.001f), "Полосы сцены — поверх постоянных.");
                renderer.SetLetterbox(0, 0);
                Assert.That(renderer.FrameRuntime().Letterbox, Is.EqualTo(.05f).Within(.001f), "Остаются постоянные.");
            }
        }

        [Test]
        public void Hit_FlashesAndShakes_ThenSettles()
        {
            using (LocationWorldRenderer renderer = Renderer(out LocationVisualDefinition visual))
            {
                visual.Post.Lens.PulseFlash = true;
                visual.Post.Lens.PulseShake = true;
                renderer.SetTime(13, 10);
                renderer.HitEffects(1);
                renderer.SetTime(13, 10.05f);
                Assert.That(renderer.FrameRuntime().Flash, Is.GreaterThan(0), "Вспышка.");
                Assert.That(renderer.ShakeOffset.magnitude, Is.GreaterThan(0), "Тряска.");
                renderer.SetTime(13, 12);
                Assert.That(renderer.FrameRuntime().Flash, Is.EqualTo(0));
                Assert.That(renderer.ShakeOffset, Is.EqualTo(Vector2.zero), "Сошла на нет.");
            }
        }

        [Test]
        public void LowQuality_CapsAntialiasing()
        {
            LocationFrameQualityLevel keep = LocationWorldRenderer.QualityLevel;
            try
            {
                using (LocationWorldRenderer renderer = Renderer(out LocationVisualDefinition visual))
                {
                    visual.Post.Quality.Antialiasing = LocationAntialiasing.SmaaHigh;
                    LocationWorldRenderer.QualityLevel = LocationFrameQualityLevel.Low;
                    renderer.SetTime(13, 1);
                    Assert.That(renderer.Camera.GetUniversalAdditionalCameraData().antialiasing, Is.EqualTo(AntialiasingMode.FastApproximateAntialiasing));
                    LocationWorldRenderer.QualityLevel = LocationFrameQualityLevel.High;
                    renderer.SetTime(13, 2);
                    Assert.That(renderer.Camera.GetUniversalAdditionalCameraData().antialiasing, Is.EqualTo(AntialiasingMode.SubpixelMorphologicalAntiAliasing));
                }
            }
            finally { LocationWorldRenderer.QualityLevel = keep; }
        }

        [Test]
        public void NewSections_RepairAndCopyWithStyles()
        {
            LocationPostEffects old = new LocationPostEffects { Air = null, Game = null };
            old.Tone.Selective = null;
            old.Repair();
            Assert.That(old.Air, Is.Not.Null);
            Assert.That(old.Game, Is.Not.Null);
            Assert.That(old.Tone.Selective.Count, Is.EqualTo(6), "Шесть цветов выборочной коррекции.");

            LocationPostEffects style = new LocationPostEffects();
            style.Air.Clouds = true;
            style.Game.Letterbox = .1f;
            LocationPostEffects place = new LocationPostEffects();
            LocationFrameSections.Copy(style, place, LocationFrameSection.Atmosphere | LocationFrameSection.Game);
            Assert.That(place.Air.Clouds, Is.True, "Свет и воздух — с атмосферой.");
            Assert.That(place.Game.Letterbox, Is.EqualTo(.1f));

            LocationPostEffects rim = new LocationPostEffects { Enabled = true };
            rim.Sprites.Rim = true;
            rim.Sprites.RimObjects = true;
            Assert.That(LocationWorldRenderer.SpriteStructureKey(rim)[0], Is.EqualTo('O'), "Подсветка предметов — материалы со стилем.");
        }
    }
}
