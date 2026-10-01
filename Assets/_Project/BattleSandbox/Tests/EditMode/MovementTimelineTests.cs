using System.Collections.Generic;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // ПР-12З: перемещение показывается с постоянной скоростью по всему
    // маршруту, трудная местность замедляет только свой отрезок.
    public sealed class MovementTimelineTests
    {
        [Test]
        public void ConstantSpeed_AcrossSegments_WithoutStops()
        {
            List<HexCoord> path = new List<HexCoord> { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0) };
            float[] durations = SandboxMovementTimeline.BuildSegmentDurations(null, path, 2f);
            Assert.AreEqual(2, durations.Length);
            Assert.AreEqual(0.5f, durations[0], 0.0001f);
            Assert.AreEqual(1f, SandboxMovementTimeline.Total(durations), 0.0001f);

            Assert.IsTrue(SandboxMovementTimeline.Locate(durations, 0.25f, out int segment, out float progress));
            Assert.AreEqual(0, segment);
            Assert.AreEqual(0.5f, progress, 0.0001f, "Половина времени отрезка — половина пути: без разгона и торможения.");

            Assert.IsTrue(SandboxMovementTimeline.Locate(durations, 0.75f, out segment, out progress));
            Assert.AreEqual(1, segment);
            Assert.AreEqual(0.5f, progress, 0.0001f);

            Assert.IsFalse(SandboxMovementTimeline.Locate(durations, 1.2f, out segment, out progress), "Путь пройден.");
            Assert.AreEqual(1, segment);
            Assert.AreEqual(1f, progress, 0.0001f);
        }

        [Test]
        public void DifficultTerrain_SlowsOnlyItsSegment()
        {
            SandboxUnitDefinition definition = new SandboxUnitDefinition("u", "u", SandboxUnitRole.Militia, 10, 1, 1, 1, 3, 1, 1);
            SandboxBattle battle = new SandboxBattle(
                4,
                3,
                new[]
                {
                    new SandboxUnitState("u", definition, SandboxTeam.Player, new HexCoord(0, 0)),
                    new SandboxUnitState("e", definition, SandboxTeam.Enemy, new HexCoord(3, 2))
                },
                new Dictionary<HexCoord, SandboxTerrain> { { new HexCoord(2, 0), SandboxTerrain.Difficult } });
            List<HexCoord> path = new List<HexCoord> { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0) };

            float[] durations = SandboxMovementTimeline.BuildSegmentDurations(battle, path, 1f);
            Assert.AreEqual(1f, durations[0], 0.0001f);
            Assert.AreEqual(SandboxMovementTimeline.DifficultTerrainSlowdown, durations[1], 0.0001f);
        }
    }
}
