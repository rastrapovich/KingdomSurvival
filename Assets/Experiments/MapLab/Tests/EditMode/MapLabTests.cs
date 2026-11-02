using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    public sealed class MapLabTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created)
                if (obj != null)
                    Object.DestroyImmediate(obj);
            created.Clear();
        }

        private MapLabSettings Settings(MapLabPresetKind kind, int seed = 0, MapLabQuality quality = MapLabQuality.Samples129)
        {
            MapLabSettings settings = ScriptableObject.CreateInstance<MapLabSettings>();
            created.Add(settings);
            MapLabSettings.ApplyPresetDefaults(settings, kind);
            settings.General.Quality = quality;
            if (seed != 0)
                settings.General.Seed = seed;
            return settings;
        }

        [Test]
        public void SameSettingsAndSeed_ReproduceTheSameMap()
        {
            MapLabData a = MapLabGenerator.Generate(Settings(MapLabPresetKind.RiverValley));
            MapLabData b = MapLabGenerator.Generate(Settings(MapLabPresetKind.RiverValley));
            CollectionAssert.AreEqual(a.Heights, b.Heights);
            Assert.AreEqual(a.Objects.Count, b.Objects.Count);
            for (int i = 0; i < a.Objects.Count; i++)
                Assert.AreEqual(a.Objects[i].Position, b.Objects[i].Position);
            Assert.AreEqual(a.Bridges.Count, b.Bridges.Count);
        }

        [Test]
        public void DifferentSeeds_ChangeDetails_ButKeepTheComposition()
        {
            MapLabData a = MapLabGenerator.Generate(Settings(MapLabPresetKind.RiverValley, 304));
            MapLabData b = MapLabGenerator.Generate(Settings(MapLabPresetKind.RiverValley, 305));
            CollectionAssert.AreNotEqual(a.Heights, b.Heights);
            Assert.AreEqual(1, a.Rivers.Count);
            Assert.AreEqual(1, b.Rivers.Count);
            Assert.GreaterOrEqual(a.Bridges.Count, 1);
            Assert.GreaterOrEqual(b.Bridges.Count, 1);
            Assert.GreaterOrEqual(b.CountObjects(MapLabObjectKind.House), 12);
        }

        // Отдельные потоки: число кустов не сдвигает рельеф, деревья и дома.
        [Test]
        public void ChangingBushes_DoesNotMoveTreesHousesOrRelief()
        {
            MapLabSettings settings = Settings(MapLabPresetKind.RiverValley);
            MapLabData a = MapLabGenerator.Generate(settings);
            settings.Vegetation.Bushes = 0.05f;
            MapLabData b = MapLabGenerator.Generate(settings);
            CollectionAssert.AreEqual(a.Heights, b.Heights);
            Assert.AreNotEqual(a.CountObjects(MapLabObjectKind.Bush), b.CountObjects(MapLabObjectKind.Bush));
            CollectionAssert.AreEqual(Positions(a, MapLabObjectKind.House), Positions(b, MapLabObjectKind.House));
            CollectionAssert.AreEqual(Positions(a, MapLabObjectKind.Deciduous), Positions(b, MapLabObjectKind.Deciduous));
        }

        [Test]
        public void RandomStreams_AreStableAndIndependent()
        {
            Assert.AreEqual(MapLabRandom.StreamSeed(304, "forest", 1), MapLabRandom.StreamSeed(304, "forest", 1));
            Assert.AreNotEqual(MapLabRandom.StreamSeed(304, "forest", 1), MapLabRandom.StreamSeed(304, "bushes", 1));
            Assert.AreNotEqual(MapLabRandom.StreamSeed(304, "forest", 1), MapLabRandom.StreamSeed(304, "forest", 2));
            // FNV-1a стабилен между запусками (в отличие от string.GetHashCode).
            Assert.AreEqual(0xCBF29CE484222325UL, MapLabRandom.Fnv1a(""));
            MapLabRandom r1 = MapLabRandom.ForStream(1, "x", 1);
            MapLabRandom r2 = MapLabRandom.ForStream(1, "x", 1);
            for (int i = 0; i < 16; i++)
                Assert.AreEqual(r1.NextULong(), r2.NextULong());
        }

        [Test]
        public void ChunkBorders_ShareHeightsAndNormals()
        {
            MapLabData data = MapLabGenerator.Generate(Settings(MapLabPresetKind.HillAndLedge));
            List<Mesh> chunks = MapLabMeshes.BuildTerrainChunks(data, 33);
            try
            {
                Assert.Greater(chunks.Count, 1);
                Dictionary<Vector2, (Vector3 position, Vector3 normal)> seen = new Dictionary<Vector2, (Vector3, Vector3)>();
                int shared = 0;
                foreach (Mesh mesh in chunks)
                {
                    Vector3[] v = mesh.vertices;
                    Vector3[] n = mesh.normals;
                    for (int i = 0; i < v.Length; i++)
                    {
                        Vector2 key = new Vector2(Mathf.Round(v[i].x * 100f), Mathf.Round(v[i].z * 100f));
                        if (seen.TryGetValue(key, out (Vector3 position, Vector3 normal) other))
                        {
                            shared++;
                            Assert.AreEqual(other.position.y, v[i].y, 0f, "Высота на стыке чанков различается");
                            Assert.Less(Vector3.Distance(other.normal, n[i]), 1e-6f, "Нормаль на стыке чанков различается");
                        }
                        else
                            seen[key] = (v[i], n[i]);
                    }
                }
                Assert.Greater(shared, 0);
            }
            finally
            {
                foreach (Mesh mesh in chunks)
                    Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void GeneratedValues_AreFinite_AndMeshIndicesValid()
        {
            foreach (MapLabPresetKind kind in new[] { MapLabPresetKind.HillAndLedge, MapLabPresetKind.ForestLake, MapLabPresetKind.RiverValley })
            {
                MapLabSettings settings = Settings(kind);
                MapLabData data = MapLabGenerator.Generate(settings);
                for (int i = 0; i < data.Heights.Length; i++)
                {
                    Assert.IsFalse(float.IsNaN(data.Heights[i]) || float.IsInfinity(data.Heights[i]), kind + ": высота не конечна");
                    Assert.AreEqual(1f, data.Normals[i].magnitude, 1e-3f, kind + ": нормаль не единичная");
                }
                foreach (MapLabPlacement o in data.Objects)
                    Assert.IsFalse(float.IsNaN(o.Position.x + o.Position.y + o.Position.z) || o.Scale <= 0f, kind + ": неверный объект");
                List<Mesh> meshes = new List<Mesh>();
                meshes.AddRange(MapLabMeshes.BuildTerrainChunks(data, 129));
                meshes.AddRange(MapLabMeshes.BuildVegetationCards(data, 64f));
                meshes.Add(MapLabMeshes.BuildCliffs(data, settings));
                meshes.Add(MapLabMeshes.BuildStructures(data, settings));
                meshes.Add(MapLabMeshes.BuildBoulders(data));
                meshes.Add(MapLabMeshes.BuildLakes(data));
                meshes.Add(MapLabMeshes.BuildRivers(data));
                foreach (Mesh mesh in meshes)
                {
                    if (mesh == null)
                        continue;
                    int count = mesh.vertexCount;
                    foreach (int index in mesh.triangles)
                        Assert.IsTrue(index >= 0 && index < count, kind + ": индекс вне меша " + mesh.name);
                    foreach (Vector3 v in mesh.vertices)
                        Assert.IsFalse(float.IsNaN(v.x + v.y + v.z), kind + ": NaN в " + mesh.name);
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        [Test]
        public void TreesAndHouses_StayOutOfWaterRoadsAndCliffWalls()
        {
            foreach (MapLabPresetKind kind in new[] { MapLabPresetKind.ForestLake, MapLabPresetKind.RiverValley })
            {
                MapLabData data = MapLabGenerator.Generate(Settings(kind));
                foreach (MapLabPlacement o in data.Objects)
                {
                    float x = o.Position.x, z = o.Position.z;
                    switch (o.Kind)
                    {
                        case MapLabObjectKind.Deciduous:
                        case MapLabObjectKind.Conifer:
                        case MapLabObjectKind.DeciduousGroup:
                            Assert.IsFalse(data.IsUnderWater(x, z, 0.2f), $"{kind}: дерево в воде ({x:0.0}, {z:0.0})");
                            Assert.Less(data.SampleMask(data.Road, x, z), 0.5f, $"{kind}: дерево на дороге ({x:0.0}, {z:0.0})");
                            break;
                        case MapLabObjectKind.House:
                        case MapLabObjectKind.Barn:
                            Assert.IsFalse(data.IsUnderWater(x, z, 0.5f), $"{kind}: дом в воде");
                            Assert.Less(data.SampleMask(data.Road, x, z), 0.5f, $"{kind}: дом на дороге");
                            Assert.Less(data.SampleMask(data.Cliff, x, z), 0.1f, $"{kind}: дом на стенке уступа");
                            Assert.Less(data.SampleSlope(x, z), 18f, $"{kind}: дом на крутом склоне");
                            break;
                    }
                }
            }
        }

        [Test]
        public void Bridges_RestOnTwoDryBanks_AndRiverLevelNeverRises()
        {
            foreach (MapLabPresetKind kind in new[] { MapLabPresetKind.ForestLake, MapLabPresetKind.RiverValley })
            {
                MapLabData data = MapLabGenerator.Generate(Settings(kind));
                Assert.GreaterOrEqual(data.Bridges.Count, 1, kind + ": нет моста");
                foreach (MapLabBridge bridge in data.Bridges)
                {
                    Assert.Greater(data.SampleHeight(bridge.EndA.x, bridge.EndA.y), bridge.WaterLevel, kind + ": конец моста A в воде");
                    Assert.Greater(data.SampleHeight(bridge.EndB.x, bridge.EndB.y), bridge.WaterLevel, kind + ": конец моста B в воде");
                    Assert.Greater(bridge.DeckHeight, bridge.WaterLevel, kind + ": настил ниже воды");
                }
                foreach (MapLabRiver river in data.Rivers)
                    for (int i = 1; i < river.Levels.Count; i++)
                        Assert.LessOrEqual(river.Levels[i], river.Levels[i - 1] + 1e-4f, kind + ": уровень реки растёт вниз по течению");
            }
        }

        [Test]
        public void LakeLevel_IsBelowDryBank_AndAboveBed()
        {
            MapLabData data = MapLabGenerator.Generate(Settings(MapLabPresetKind.ForestLake, 0, MapLabQuality.Samples257));
            Assert.AreEqual(1, data.Lakes.Count);
            float level = data.Lakes[0].Level;
            int wet = 0, dryRing = 0;
            for (int i = 0; i < data.Heights.Length; i++)
            {
                float f = data.LakeField[i];
                if (f > 0.05f)
                {
                    wet++;
                    Assert.Less(data.Heights[i], level, "дно озера выше воды");
                }
                else if (f < -0.02f && f > -0.25f && data.Water[i] == (byte)MapLabWaterKind.Dry)
                {
                    dryRing++;
                    Assert.Greater(data.Heights[i], level, "берег озера под водой");
                }
            }
            Assert.Greater(wet, 100);
            Assert.Greater(dryRing, 100);
        }

        [Test]
        public void ExternalHeightmap_ControlValuesMapToExpectedHeights()
        {
            MapLabReliefSettings relief = new MapLabReliefSettings { ExternalScale = 1f, ExternalOffset = 0f };
            Assert.AreEqual(0f, MapLabHeightmapReader.Remap(0f, relief), 1e-5f);
            Assert.AreEqual(0.5f, MapLabHeightmapReader.Remap(0.5f, relief), 1e-5f);
            Assert.AreEqual(1f, MapLabHeightmapReader.Remap(1f, relief), 1e-5f);
            relief.ExternalScale = 2f;
            relief.ExternalOffset = 0.25f;
            Assert.AreEqual(1.25f, MapLabHeightmapReader.Remap(0.5f, relief), 1e-5f);
            relief.ExternalInvert = true;
            Assert.AreEqual(2.25f, MapLabHeightmapReader.Remap(0f, relief), 1e-5f);
        }

        [Test]
        public void ExtremeSettings_StillProduceAValidMap()
        {
            MapLabSettings settings = Settings(MapLabPresetKind.ForestLake);
            settings.Relief.HeightRange = 2f;
            settings.Water.LevelOffset = 3f;
            settings.Vegetation.ForestDensity = 0f;
            settings.Vegetation.SingleTrees = 0f;
            settings.Settlement.HousesMax = 0;
            settings.Cliffs.CliffHeight = 0f;
            MapLabData low = MapLabGenerator.Generate(settings);
            Assert.AreEqual(0, low.CountObjects(MapLabObjectKind.House));
            // Лесного массива нет; одиночные деревья у дорог остаются (своя ручка).
            foreach (float f in low.Forest)
                Assert.AreEqual(0f, f);
            Assert.Less(low.CountObjects(MapLabObjectKind.Deciduous), 40);
            Assert.AreEqual(0, low.CountObjects(MapLabObjectKind.DeciduousGroup) + low.CountObjects(MapLabObjectKind.Conifer));
            settings.Relief.HeightRange = 40f;
            settings.Water.LevelOffset = -3f;
            settings.Vegetation.ForestDensity = 1f;
            settings.Cliffs.CliffHeight = 20f;
            settings.Cliffs.CliffSharpness = 0.6f;
            MapLabData high = MapLabGenerator.Generate(settings);
            foreach (float h in high.Heights)
                Assert.IsFalse(float.IsNaN(h) || float.IsInfinity(h));
            Assert.Greater(high.CountObjects(MapLabObjectKind.Deciduous), 1000);
        }

        private static List<Vector3> Positions(MapLabData data, MapLabObjectKind kind)
        {
            List<Vector3> list = new List<Vector3>();
            foreach (MapLabPlacement o in data.Objects)
                if (o.Kind == kind)
                    list.Add(o.Position);
            return list;
        }
    }
}
