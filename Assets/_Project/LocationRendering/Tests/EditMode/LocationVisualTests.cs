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
