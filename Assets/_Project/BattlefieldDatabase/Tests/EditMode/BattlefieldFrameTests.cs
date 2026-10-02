using System.Linq;
using KingdomSurvival.BattleSandbox;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase.Tests
{
    // Кадр поля боя: фон и гексы связаны одной геометрией при любом окне.
    public sealed class BattlefieldFrameTests
    {
        private const float Epsilon = 0.01f;

        [Test]
        public void ContainFit_KeepsFrameAspectAndCentre()
        {
            Rect frame = BattlefieldFrame.FitFrame(new Rect(0f, 0f, 1000f, 1000f), BattlefieldFrame.DefaultGridArea, false);

            Assert.AreEqual(1000f, frame.width, Epsilon);
            Assert.AreEqual(1000f / BattlefieldFrame.Aspect, frame.height, Epsilon);
            Assert.AreEqual(500f, frame.center.y, Epsilon);
        }

        [Test]
        public void CoverFit_FillsSixteenByNineExactly()
        {
            Rect area = new Rect(0f, 0f, 1920f, 1080f);
            Rect frame = BattlefieldFrame.FitFrame(area, BattlefieldFrame.DefaultGridArea, true);

            Assert.AreEqual(area.width, frame.width, Epsilon);
            Assert.AreEqual(area.height, frame.height, Epsilon);
        }

        [TestCase(1920f, 1200f)]
        [TestCase(2560f, 1080f)]
        [TestCase(1024f, 768f)]
        [TestCase(800f, 1200f)]
        public void CoverFit_KeepsWholeArenaOnScreen(float width, float height)
        {
            Rect area = new Rect(0f, 0f, width, height);
            Rect grid = BattlefieldFrame.DefaultGridArea;
            Rect frame = BattlefieldFrame.FitFrame(area, grid, true);
            Rect arena = BattlefieldFrame.ComputeLayout(frame, grid).ArenaRect;

            Assert.GreaterOrEqual(arena.xMin, -Epsilon);
            Assert.GreaterOrEqual(arena.yMin, -Epsilon);
            Assert.LessOrEqual(arena.xMax, width + Epsilon);
            Assert.LessOrEqual(arena.yMax, height + Epsilon);
        }

        [Test]
        public void HexCentres_StayInPlaceRelativeToFrame_ForAnyWindowSize()
        {
            Rect grid = BattlefieldFrame.DefaultGridArea;
            Vector2 small = Normalized(new Rect(0f, 0f, 640f, 900f), grid, 4, 3);
            Vector2 large = Normalized(new Rect(0f, 0f, 2400f, 700f), grid, 4, 3);

            Assert.AreEqual(small.x, large.x, 0.001f);
            Assert.AreEqual(small.y, large.y, 0.001f);
        }

        [Test]
        public void Layout_ArenaEdgesMatchOutermostHexes()
        {
            BattlefieldGridLayout layout = BattlefieldFrame.ComputeLayout(
                new Rect(0f, 0f, 1600f, 900f), BattlefieldFrame.DefaultGridArea);
            float halfWidth = Mathf.Sqrt(3f) * layout.Size * 0.5f;

            Assert.AreEqual(layout.ArenaRect.xMin, layout.GetCenter(0, 3).x - halfWidth, Epsilon);
            Assert.AreEqual(layout.ArenaRect.xMax, layout.GetCenter(9, 3).x + halfWidth, Epsilon);
            Assert.AreEqual(layout.ArenaRect.yMin, layout.GetCenter(2, 0).y - layout.Size * layout.VerticalScale, Epsilon);
            Assert.AreEqual(layout.ArenaRect.yMax, layout.GetCenter(2, 6).y + layout.Size * layout.VerticalScale, Epsilon);
            Assert.IsTrue(layout.TryGetCell(layout.GetCenter(5, 4), out int q, out int r));
            Assert.AreEqual(new Vector2Int(5, 4), new Vector2Int(q, r));
        }

        [Test]
        public void GridScaleAndOffset_MoveTheArena()
        {
            BattlefieldDefinitionData field = JsonUtility.FromJson<BattlefieldDefinitionData>(
                "{\"gridScale\":0.5,\"gridOffset\":{\"x\":0.1,\"y\":0}}");
            Rect area = BattlefieldFrame.GetGridArea(field);

            Assert.AreEqual(BattlefieldFrame.DefaultGridArea.width * 0.5f, area.width, 0.0001f);
            Assert.AreEqual(BattlefieldFrame.DefaultGridArea.center.x + 0.1f, area.center.x, 0.0001f);
        }

        [Test]
        public void DisabledCells_AreCountedAndPassedToBattle()
        {
            BattlefieldDefinitionData field = JsonUtility.FromJson<BattlefieldDefinitionData>(
                "{\"disabledCells\":[{\"x\":0,\"y\":3},{\"x\":5,\"y\":2},{\"x\":0,\"y\":0}]}");

            Assert.AreEqual(SandboxArenaShape.CellCount - 2, BattlefieldFrame.CountActiveCells(field));
            Assert.IsFalse(BattlefieldFrame.IsActiveCell(field, 0, 3));
            Assert.IsTrue(BattlefieldFrame.IsActiveCell(field, 1, 3));
            // (0, 0) вне арены 7/8/9/10/9/8/7 — в бой не передаётся.
            CollectionAssert.AreEquivalent(
                new[] { new HexCoord(0, 3), new HexCoord(5, 2) },
                BattlefieldFrame.DisabledCells(field));
            Assert.IsTrue(BattlefieldFrame.AreActiveCellsConnected(field));
        }

        [Test]
        public void IsolatedCorner_IsReportedAsTornField()
        {
            HexCoord corner = new HexCoord(2, 0);
            string cells = string.Join(",", corner.Neighbors()
                .Where(SandboxArenaShape.Contains)
                .Select(cell => "{\"x\":" + cell.Q + ",\"y\":" + cell.R + "}"));
            BattlefieldDefinitionData field = JsonUtility.FromJson<BattlefieldDefinitionData>(
                "{\"disabledCells\":[" + cells + "]}");

            Assert.IsFalse(BattlefieldFrame.AreActiveCellsConnected(field));
        }

        private static Vector2 Normalized(Rect area, Rect grid, int q, int r)
        {
            Rect frame = BattlefieldFrame.FitFrame(area, grid, false);
            Vector2 center = BattlefieldFrame.ComputeLayout(frame, grid).GetCenter(q, r);
            return new Vector2((center.x - frame.x) / frame.width, (center.y - frame.y) / frame.height);
        }
    }
}
