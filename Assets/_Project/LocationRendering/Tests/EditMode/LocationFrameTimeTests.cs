using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12М: ключи суток, зоны места и переходы обработки кадра — на одном
    // смешивании настроек (LocationFrameBlend).
    public sealed class LocationFrameTimeTests
    {
        [Test]
        public void Blend_NumbersSmooth_SwitchesAtHalf_Independent()
        {
            LocationPostEffects a = new LocationPostEffects();
            LocationPostEffects b = new LocationPostEffects();
            a.Day.Exposure = 0; b.Day.Exposure = 2;
            a.Canvas.FogColor = Color.black; b.Canvas.FogColor = Color.white;
            a.Canvas.Fog = false; b.Canvas.Fog = true;
            a.Characters.Own("wolf").Hue = 0; b.Characters.Own("wolf").Hue = 40;
            LocationPostEffects mid = LocationFrameBlend.Lerp(a, b, .5f);
            Assert.That(mid.Day.Exposure, Is.EqualTo(1).Within(1e-4f));
            Assert.That(mid.Canvas.FogColor.r, Is.EqualTo(.5f).Within(1e-4f));
            Assert.That(mid.Canvas.Fog, Is.True, "Галочка — на середине.");
            Assert.That(LocationFrameBlend.Lerp(a, b, .4f).Canvas.Fog, Is.False);
            Assert.That(mid.Characters.Find("wolf").Hue, Is.EqualTo(20).Within(1e-4f), "Списки одной длины — поэлементно.");
            mid.Day.Exposure = 9;
            mid.Characters.Find("wolf").Hue = 99;
            Assert.That(a.Day.Exposure, Is.EqualTo(0), "Итог — своя копия.");
            Assert.That(b.Characters.Find("wolf").Hue, Is.EqualTo(40));
        }

        [TestCase(6f, 0f)]
        [TestCase(12f, 1f)]
        [TestCase(18f, 2f)]
        [TestCase(0f, 1f)]
        public void Keys_InterpolateByHour_AcrossMidnight(float hour, float exposure)
        {
            LocationFrameKeys keys = new LocationFrameKeys { Enabled = true };
            keys.Keys.Add(new LocationFrameKey { Hour = 6, Post = new LocationPostEffects { Day = new LocationColorGrade { Exposure = 0 } } });
            keys.Keys.Add(new LocationFrameKey { Hour = 18, Post = new LocationPostEffects { Day = new LocationColorGrade { Exposure = 2 } } });
            Assert.That(keys.AtHour(hour, null).Day.Exposure, Is.EqualTo(exposure).Within(.01f));
        }

        [Test]
        public void ZoneWeight_FullInside_FadesOverFeather()
        {
            LocationFrameZone zone = new LocationFrameZone { Center = new Vector2(500, 500), Size = new Vector2(200, 200), Feather = 100, Ellipse = false };
            Assert.That(zone.Weight(new Vector2(500, 500)), Is.EqualTo(1));
            Assert.That(zone.Weight(new Vector2(600, 500)), Is.EqualTo(1), "Край области — ещё полностью.");
            Assert.That(zone.Weight(new Vector2(650, 500)), Is.EqualTo(.5f).Within(.01f));
            Assert.That(zone.Weight(new Vector2(700, 500)), Is.EqualTo(0));
            zone.Enabled = false;
            Assert.That(zone.Weight(new Vector2(500, 500)), Is.EqualTo(0));
        }

        private static float Exposure(LocationWorldRenderer renderer)
        {
            renderer.Root.GetComponentInChildren<Volume>().sharedProfile.TryGet(out ColorAdjustments color);
            return color.postExposure.value;
        }

        [Test]
        public void Renderer_UsesKeysZonesAndTransitions()
        {
            LocalLocationDefinition location = new LocalLocationDefinition { Id = "zz_time", DisplayName = "Поляна", CanvasWidth = 1920, CanvasHeight = 1080 };
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false, Lighting = LocationLightingMode.Indoor };
            visual.Post = new LocationPostEffects { Enabled = true };
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                renderer.SetTime(13, 1);
                Assert.That(Exposure(renderer), Is.EqualTo(0).Within(1e-4f), "Без ключей — основные.");
                Assert.That(renderer.Post, Is.SameAs(visual.Post), "Без ключей и зон — без копии.");

                visual.FrameKeys.Enabled = true;
                visual.FrameKeys.Keys.Add(new LocationFrameKey { Hour = 6, Post = new LocationPostEffects { Day = new LocationColorGrade { Exposure = 0 } } });
                visual.FrameKeys.Keys.Add(new LocationFrameKey { Hour = 18, Post = new LocationPostEffects { Day = new LocationColorGrade { Exposure = 2 } } });
                renderer.SetTime(12, 2);
                Assert.That(Exposure(renderer), Is.EqualTo(1).Within(.02f), "Полдень между ключами 6 и 18.");
                Assert.That(renderer.Post.Enabled, Is.True, "Главный выключатель — основной.");

                visual.FrameKeys.Enabled = false;
                visual.FrameZones.Add(new LocationFrameZone
                {
                    Center = new Vector2(300, 300), Size = new Vector2(200, 200), Feather = 1, Post = new LocationPostEffects { Day = new LocationColorGrade { Exposure = 1.5f } }
                });
                renderer.SetFocus(new Vector2(300, 300));
                renderer.SetTime(13, 3);
                Assert.That(Exposure(renderer), Is.EqualTo(1.5f).Within(.02f), "Взгляд в зоне — её вид.");
                renderer.SetFocus(new Vector2(1500, 900));
                renderer.SetTime(13, 4);
                Assert.That(Exposure(renderer), Is.EqualTo(0).Within(.02f), "Вне зоны — основные.");

                renderer.TransitionTo(new LocationPostEffects { Day = new LocationColorGrade { Exposure = -2 } }, 2);
                renderer.SetTime(13, 5);
                renderer.SetTime(13, 7);
                Assert.That(Exposure(renderer), Is.EqualTo(-2).Within(.02f), "Переход дошёл до цели.");
                renderer.EndTransition(1);
                renderer.SetTime(13, 8.5f);
                Assert.That(Exposure(renderer), Is.EqualTo(0).Within(.02f), "И вернулся.");
                Assert.That(renderer.TransitionWeight(), Is.EqualTo(0));
            }
        }
    }
}
