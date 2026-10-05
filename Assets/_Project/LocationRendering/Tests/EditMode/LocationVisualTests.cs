using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Tests
{
    public sealed class LocationVisualTests
    {
        [Test]
        public void MidnightAndNegativeTimesArePeriodic()
        {
            LocationDaylight light = new LocationDaylight();
            Assert.That(light.Evaluate(24), Is.EqualTo(light.Evaluate(0)).Within(.001f));
            Assert.That(light.Evaluate(-1), Is.EqualTo(light.Evaluate(23)).Within(.001f));
            Assert.That(light.EvaluateColor(24), Is.EqualTo(light.EvaluateColor(0)));
            Assert.That(light.Evaluate(23.999f), Is.EqualTo(light.Evaluate(0)).Within(.001f));
        }

        [TestCase(18.5f, true)]
        [TestCase(23.9f, true)]
        [TestCase(0f, true)]
        [TestCase(5.49f, true)]
        [TestCase(5.5f, false)]
        [TestCase(13f, false)]
        public void LightScheduleCrossesMidnight(float hour, bool expected)
        {
            LocationLightDefinition light = new LocationLightDefinition { Enabled = true, NightOnly = true };
            Assert.That(light.ActiveAt(hour), Is.EqualTo(expected));
            light.Enabled = false;
            Assert.That(light.ActiveAt(hour), Is.False);
        }

        [Test]
        public void WorldAndPixelCoordinatesRoundTrip()
        {
            LocalLocationDefinition location = new LocalLocationDefinition { CanvasWidth = 2400, CanvasHeight = 1200 };
            Assert.That(LocationVisualGeometry.PixelToWorld(location, new Vector2(1200, 600)), Is.EqualTo(Vector2.zero));
            Assert.That(LocationVisualGeometry.PixelToWorld(location, new Vector2(1200 + LocationVisualGeometry.PixelsPerUnit, 600)).x,
                Is.EqualTo(1).Within(.0001f), "Единица мира — PixelsPerUnit пикселей рисунка.");
            Assert.That(LocationVisualGeometry.PixelToWorld(location, new Vector2(1200, 0)).y, Is.GreaterThan(0), "Y рисунка вниз, Y мира вверх.");
            foreach (Vector2 pixel in new[] { new Vector2(0, 0), new Vector2(2400, 1200), new Vector2(317, 905) })
            {
                Vector2 world = LocationVisualGeometry.PixelToWorld(location, pixel);
                Assert.That(Vector2.Distance(pixel, LocationVisualGeometry.WorldToPixel(location, world)), Is.LessThan(.001f));
                Vector2 normalized = LocationVisualGeometry.ToNormalized(location, pixel);
                Assert.That(Vector2.Distance(world, LocationVisualGeometry.ToWorld(location, normalized)), Is.LessThan(.0001f));
            }
        }

        [Test]
        public void MovingFootprintMovesTheObstacle()
        {
            LocalLocationDefinition location = new LocalLocationDefinition();
            Vector2 a = new Vector2(600, 500), b = new Vector2(1300, 500);
            LocationVisualObject tent = new LocationVisualObject { BlocksMovement = true, Footprint = new Vector2(1, 1),
                Position = LocationVisualGeometry.ToNormalized(location, a) };
            LocationVisualDefinition visual = new LocationVisualDefinition { Objects = new List<LocationVisualObject> { tent } };
            LocalLocationGeometry first = new LocalLocationGeometry(location, null, LocationVisualGeometry.BlockedAreas(visual, location));
            Assert.That(first.IsPassable(a.x, a.y), Is.False); Assert.That(first.IsPassable(b.x, b.y), Is.True);
            tent.Position = LocationVisualGeometry.ToNormalized(location, b);
            LocalLocationGeometry second = new LocalLocationGeometry(location, null, LocationVisualGeometry.BlockedAreas(visual, location));
            Assert.That(second.IsPassable(a.x, a.y), Is.True); Assert.That(second.IsPassable(b.x, b.y), Is.False);
            tent.Hidden = true;
            Assert.That(LocationVisualGeometry.BlockedAreas(visual, location), Is.Empty);
        }

        // ПР-12М: тень от солнца — утром в сторону утреннего угла, длинная;
        // в полдень короткая; ночью нет (или от луны).
        [Test]
        public void SunShadowFollowsTheDay()
        {
            LocationSunDefinition sun = new LocationSunDefinition { Sunrise = 6, Sunset = 20, MorningAngle = 170, EveningAngle = 10, NoonLength = .4f, LowLength = 2 };
            sun.Evaluate(7, out Vector2 morning, out float morningLength, out float morningOpacity);
            sun.Evaluate(13, out Vector2 noon, out float noonLength, out _);
            sun.Evaluate(19, out Vector2 evening, out _, out _);
            sun.Evaluate(1, out _, out _, out float night);
            Assert.That(morning.x, Is.LessThan(-.5f), "Утром тень уходит влево.");
            Assert.That(evening.x, Is.GreaterThan(.5f), "Вечером — вправо.");
            Assert.That(noon.y, Is.GreaterThan(.9f), "В полдень — вверх, по кратчайшей дуге.");
            Assert.That(morningLength, Is.GreaterThan(noonLength));
            Assert.That(noonLength, Is.EqualTo(.4f).Within(.01f));
            Assert.That(morningOpacity, Is.GreaterThan(0));
            Assert.That(night, Is.EqualTo(0));
            sun.MoonShadows = true;
            sun.Evaluate(1, out _, out _, out float moon);
            Assert.That(moon, Is.EqualTo(sun.MoonOpacity).Within(.001f));
        }

        // Тень от огня строго сбоку не сплющивается в линию: есть наклон к земле.
        [Test]
        public void ShadowAlwaysLeansOntoTheGround()
        {
            Assert.That(LocationWorldRenderer.Lean(new Vector2(2, 0), .4f), Is.EqualTo(new Vector2(2, .4f)));
            Assert.That(LocationWorldRenderer.Lean(new Vector2(2, -.1f), .4f), Is.EqualTo(new Vector2(2, -.4f)), "Свет сзади — тень к зрителю.");
            Assert.That(LocationWorldRenderer.Lean(new Vector2(.3f, .9f), .4f), Is.EqualTo(new Vector2(.3f, .9f)));
            Assert.That(LocationWorldRenderer.Lean(new Vector2(2, 0), 0), Is.EqualTo(new Vector2(2, 0)), "0 — честная проекция.");
        }

        // Светящий предмет (костёр) отбрасывает свою тень во все стороны:
        // слои силуэта от точки огня; потушен — тени нет; днём слабее.
        [Test]
        public void LightEmittingObject_CastsOwnRadialShadow()
        {
            Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(.5f, .5f), 100);
            LocationVisualObject fire = new LocationVisualObject { Id = "ring", Name = "Кольцо", Sprite = sprite, Position = new Vector2(.5f, .5f), Height = 1 };
            fire.Light.Enabled = true; fire.Light.NightOnly = true; fire.Light.StartsAt = 18; fire.Light.EndsAt = 6;
            fire.Light.Animation = LocationLightAnimation.None;
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "zz", UseWorldLighting = false, Objects = { fire } };
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(new LocalLocationDefinition { Id = "zz" }, visual, null))
            {
                List<SpriteRenderer> layers = new List<SpriteRenderer>();
                foreach (SpriteRenderer item in renderer.Root.GetComponentsInChildren<SpriteRenderer>(true))
                    if (item.gameObject.name == "Своя тень огня") layers.Add(item);
                Assert.That(layers.Count, Is.EqualTo(6), "Слои своей тени у светящего предмета.");
                Assert.That(layers[5].transform.localScale.x, Is.GreaterThan(layers[0].transform.localScale.x), "Слои расходятся от огня.");

                float Alpha()
                {
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    layers[0].GetPropertyBlock(block);
                    return layers[0].enabled ? block.GetColor("_ShadowColor").a : 0;
                }
                renderer.SetTime(1, 0);
                float night = Alpha();
                renderer.SetTime(13, 0);
                Assert.That(night, Is.GreaterThan(0));
                Assert.That(Alpha(), Is.EqualTo(0), "Днём огонь по расписанию потушен — своей тени нет.");
                fire.Light.NightOnly = false;
                renderer.SetTime(13, 0);
                Assert.That(Alpha(), Is.GreaterThan(0).And.LessThan(night), "Днём своя тень огня слабее.");
            }
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }

        // Свет проходит через «строго сбоку»: одна тень, её наклон меняется
        // непрерывно и монотонно — без скачка вверх-вниз и без второй тени.
        // Свет спереди и сзади — честная проекция.
        [Test]
        public void SideLight_SingleShadowLeansSmoothly()
        {
            const float lean = .4f;
            Assert.That(LocationWorldRenderer.SoftLean(new Vector2(2, 0), lean).y, Is.EqualTo(lean).Within(1e-5f), "Строго сбоку — тень «вглубь».");
            Assert.That(LocationWorldRenderer.SoftLean(new Vector2(.3f, 1), lean), Is.EqualTo(new Vector2(.3f, 1)), "Свет спереди — без добавки.");
            Assert.That(LocationWorldRenderer.SoftLean(new Vector2(.3f, -1), lean), Is.EqualTo(new Vector2(.3f, -1)), "Свет сзади — тень к зрителю.");
            Assert.That(LocationWorldRenderer.SoftLean(new Vector2(2, 0), 0), Is.EqualTo(new Vector2(2, 0)), "0 — честная проекция.");
            float previous = float.NegativeInfinity;
            for (float y = -1.2f; y <= 1.2f; y += .01f)
            {
                float leaned = LocationWorldRenderer.SoftLean(new Vector2(2, y), lean).y;
                if (!float.IsNegativeInfinity(previous))
                {
                    Assert.That(leaned - previous, Is.LessThan(.02f), "Шаг света в 0,01 не даёт скачка тени (y = " + y + ").");
                    Assert.That(leaned, Is.GreaterThan(previous), "Наклон меняется монотонно (y = " + y + ").");
                }
                previous = leaned;
            }
        }

        // Небо места: общий свет мира или своё; своё начинается с копии общего.
        [Test]
        public void SkyIsWorldOrOwn()
        {
            LocationWorldLighting world = new LocationWorldLighting { PeopleShadowLength = 2 };
            LocationVisualDefinition visual = new LocationVisualDefinition { PeopleShadowLength = .5f };
            LocationSky sky = new LocationSky(visual, world);
            Assert.That(sky.IsWorld, Is.True);
            Assert.That(sky.PeopleShadowLength, Is.EqualTo(2));
            sky.Sun.Opacity = .9f;
            Assert.That(world.Sun.Opacity, Is.EqualTo(.9f), "Правка общего неба — в общем свете мира.");
            LocationSky.CopyWorldToOwn(world, visual);
            visual.UseWorldLighting = false;
            Assert.That(sky.PeopleShadowLength, Is.EqualTo(2));
            Assert.That(sky.Sun.Opacity, Is.EqualTo(.9f));
            sky.Sun.Opacity = .1f;
            Assert.That(world.Sun.Opacity, Is.EqualTo(.9f), "Своё небо — отдельная копия.");
        }

        [Test]
        public void LightAnimationKeepsTheBaseIntensity()
        {
            LocationLightDefinition light = new LocationLightDefinition { Intensity = 2, Animation = LocationLightAnimation.None };
            Assert.That(light.AnimatedIntensity(3.3f, 1), Is.EqualTo(2));
            light.Animation = LocationLightAnimation.Pulse; light.Flicker = .5f;
            Assert.That(light.AnimatedIntensity(.5f, 1), Is.EqualTo(3).Within(.01f), "Пульс: вершина волны.");
            light.Animation = LocationLightAnimation.Strobe;
            Assert.That(light.AnimatedIntensity(.75f, 1), Is.LessThan(2));
        }

        [Test]
        public void GroundObjectsAndForegroundKeepTheirRelativeOrder()
        {
            Assert.That(LocationVisualGeometry.SortOrder(LocationVisualBand.Ground, 0), Is.LessThan(LocationVisualGeometry.SortOrder(LocationVisualBand.World, 2)));
            Assert.That(LocationVisualGeometry.SortOrder(LocationVisualBand.World, -1), Is.GreaterThan(LocationVisualGeometry.SortOrder(LocationVisualBand.World, 1)));
            Assert.That(LocationVisualGeometry.SortOrder(LocationVisualBand.Foreground, 0), Is.GreaterThan(LocationVisualGeometry.SortOrder(LocationVisualBand.World, -2)));
        }

        [Test]
        public void VisualDefinitionsSerializeWithoutLosingGroupsAndStateIds()
        {
            LocationVisualDefinition original = new LocationVisualDefinition { LocationId = "test",
                Objects = new List<LocationVisualObject> { new LocationVisualObject { GroupId = "house", DefaultVariantId = "burned",
                    Variants = new List<LocationVisualVariant> { new LocationVisualVariant { Id = "burned", Name = "Сгоревший" } } } } };
            LocationVisualDefinition copy = JsonUtility.FromJson<LocationVisualDefinition>(JsonUtility.ToJson(original));
            Assert.That(copy.LocationId, Is.EqualTo("test"));
            Assert.That(copy.Objects[0].GroupId, Is.EqualTo("house"));
            Assert.That(copy.Objects[0].Variants[0].Id, Is.EqualTo(copy.Objects[0].DefaultVariantId));
            Assert.That(copy.Daylight.Evaluate(13), Is.EqualTo(original.Daylight.Evaluate(13)));
        }
    }
}
