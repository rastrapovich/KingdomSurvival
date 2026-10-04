using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
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
        public void WorldAndCellCoordinatesRoundTrip()
        {
            foreach (HexCoord cell in SandboxArenaShape.Cells())
            {
                Vector2 world = LocationVisualGeometry.CellPosition(null, cell);
                Assert.That(LocationVisualGeometry.TryCell(null, world, out HexCoord found), Is.True);
                Assert.That(found, Is.EqualTo(cell));
                Assert.That(Vector2.Distance(world, LocationVisualGeometry.ToWorld(LocationVisualGeometry.ToNormalized(world))), Is.LessThan(.0001f));
            }
        }

        [Test]
        public void MovingFootprintMovesTheBlockedCellsAndUsesExistingNavigation()
        {
            HexCoord a = new HexCoord(4, 3), b = new HexCoord(6, 3);
            LocationVisualObject tent = new LocationVisualObject { BlocksMovement = true, Footprint = new Vector2(.5f, .5f),
                Position = LocationVisualGeometry.ToNormalized(LocationVisualGeometry.CellPosition(null, a)) };
            LocationVisualDefinition visual = new LocationVisualDefinition { Objects = new List<LocationVisualObject> { tent } };
            List<HexCoord> first = LocationVisualGeometry.BlockedCells(visual, null);
            CollectionAssert.Contains(first, a); CollectionAssert.DoesNotContain(first, b);
            tent.Position = LocationVisualGeometry.ToNormalized(LocationVisualGeometry.CellPosition(null, b));
            List<HexCoord> second = LocationVisualGeometry.BlockedCells(visual, null);
            CollectionAssert.DoesNotContain(second, a); CollectionAssert.Contains(second, b);
            LocalLocationGeometry geometry = new LocalLocationGeometry(new LocalLocationDefinition(), null, second);
            Assert.That(geometry.IsPassable(a), Is.True); Assert.That(geometry.IsPassable(b), Is.False);
            tent.Hidden = true;
            Assert.That(LocationVisualGeometry.BlockedCells(visual, null), Is.Empty);
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
