using KingdomSurvival.MapLab;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Tests
{
    // 12Ф-2: авторская карта → композиция MapLab: рельеф автора (PlanHeight),
    // река линией, озеро и уступ.
    public sealed class MapLabAuthoredPlanTests
    {
        private static AuthoredPoint At(AuthoredWorldMapData data, int column, int row)
        {
            data.Grid.CellCenter(new WorldMapHexCell(column, row), out double x, out double y);
            return new AuthoredPoint(x, y);
        }

        [Test]
        public void AuthoredRiverLakeAndLedge_ReachTheComposition()
        {
            AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault(40, 30);
            WorldMapHexGrid g = data.Grid;
            AuthoredPoint hill = At(data, 30, 6);
            foreach ((int index, float _) in AuthoredWorldMapEditing.CellsInBrush(data, hill.X, hill.Y, 2.5f, 0f))
                data.Heights[index] = 0.8f;
            AuthoredRiver river = new AuthoredRiver();
            river.Points.Add(At(data, 2, 2));
            river.Points.Add(At(data, 8, 14));
            river.Points.Add(At(data, 6, 28));
            data.Rivers.Add(river);
            AuthoredLedge ledge = new AuthoredLedge();
            ledge.Points.Add(At(data, 22, 24));
            ledge.Points.Add(At(data, 36, 24));
            data.Ledges.Add(ledge);
            for (int c = 30; c <= 32; c++)
                data.Terrain[g.IndexOf(new WorldMapHexCell(c, 14))] = (byte)WorldMapGameplayTerrainType.Water;
            data.Commit();

            GeneratedWorldMap plan = AuthoredWorldMapBuilder.Build(data, 5);
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            try
            {
                MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.RiverValley);
                MapLabComposition c = MapLabRegionPlan.Build(plan, s, false);
                Assert.IsNotNull(c.PlanHeight, "Рельеф — высоты автора.");
                Assert.Greater(Max(c.PlanHeight), 0.3f, "Холм автора виден в рельефе.");
                Assert.AreEqual(1, c.Rivers.Count, "Река автора — река MapLab.");
                Assert.AreEqual(1, c.Cliffs.Count, "Уступ автора — обрыв MapLab.");
                Assert.AreEqual(1, c.Lakes.Count, "Нарисованная вода — озеро.");
                Assert.AreEqual(0, c.Hills.Count, "Холмов-капель по типам клеток нет.");
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        private static float Max(float[] values)
        {
            float max = 0f;
            foreach (float v in values)
                max = Mathf.Max(max, v);
            return max;
        }
    }
}
