using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.MapLab;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // Лес карты партии и База ассетов: прежние породы MapLab остаются, к ним
    // добавляются деревья, которым автор дал назначение «Карта и арена» или
    // «Только карта» (тот же ID и PNG, что у арены). Без таких деревьев карта
    // рисуется как раньше.
    public sealed class MapLabMapForestTests
    {
        private static ArtAssetDefinition PackTree(string id) =>
            ArtAssetDatabaseAsset.Current?.assets.FirstOrDefault(asset => asset != null && asset.ImportKey == "pack69_" + id.ToLowerInvariant());

        [Test]
        public void WithoutMapTrees_MapForestStaysAsBefore()
        {
            MapLabGameAssets assets = MapLabGameAssets.Load();
            if (assets == null || assets.ArtSet03 == null)
                Assert.Ignore("Нет MapLabGameAssets с набором этапа 03.");
            bool anyMapTree = ArtAssetDatabaseAsset.Current.assets.Any(asset => asset != null && asset.UsableOnMap &&
                                                                               asset.Category == ArtAssetCategory.Trees);
            if (anyMapTree)
                Assert.Ignore("В Базе уже есть деревья для карты — проверяется во втором тесте.");
            MapLabArtSet set = MapLabArtAssets.BuildMapForestSet(assets.ArtSet03, 0.8f, out int deciduous, out int conifers);
            Assert.That(set, Is.Null, "Нет деревьев для карты — набор карты не меняется.");
            Assert.That(deciduous + conifers, Is.Zero);
        }

        [Test]
        public void ConnectedTrees_AreAddedToOldSpecies_OnBuiltMap()
        {
            MapLabGameAssets assets = MapLabGameAssets.Load();
            if (assets == null || assets.Profile == null || assets.ArtSet03 == null || !assets.MapForestFromAssets)
                Assert.Ignore("Нет MapLabGameAssets или лес из Базы ассетов выключен.");
            ArtAssetDefinition oak = PackTree("Oak"), spruce = PackTree("NorwaySpruce");
            if (oak == null || spruce == null)
                Assert.Ignore("Нет деревьев пакета арены в Базе ассетов.");
            ArtAssetPurpose oakWas = oak.Purpose, spruceWas = spruce.Purpose;
            // Автор подключает два дерева на карту (только в памяти теста).
            oak.Purpose = ArtAssetPurpose.Both;
            spruce.Purpose = ArtAssetPurpose.MapOnly;
            try
            {
                MapLabArtSet set = MapLabArtAssets.BuildMapForestSet(assets.ArtSet03, 0.8f, out int deciduous, out int conifers);
                try
                {
                    Assert.That(set, Is.Not.Null);
                    Assert.That(deciduous, Is.GreaterThanOrEqualTo(1));
                    Assert.That(conifers, Is.GreaterThanOrEqualTo(1), "Ель распознана как хвойная.");
                    foreach (MapLabArtEntry entry in assets.ArtSet03.Entries.Where(e => e != null))
                        Assert.That(set.Entries.Contains(entry), Is.True, "Прежняя запись карты на месте: " + entry.Id);
                    Assert.That(set.Find(MapLabArtAssets.IdPrefix + oak.Id)?.Kind, Is.EqualTo(MapLabObjectKind.Deciduous));
                    Assert.That(set.Find(MapLabArtAssets.IdPrefix + spruce.Id)?.Kind, Is.EqualTo(MapLabObjectKind.Conifer));
                    Assert.That(set.Entries.Count(e => e.Id.StartsWith(MapLabArtAssets.IdPrefix)), Is.EqualTo(deciduous + conifers),
                        "Добавлены только подключённые деревья.");
                }
                finally
                {
                    if (set != null)
                        Object.DestroyImmediate(set);
                }

                WorldMapGenerationSettings planSettings = WorldMapGenerationSettings.CreateDefault();
                planSettings.HexesAcross = 24;
                GeneratedWorldMap plan = WorldMapGenerator.Generate(planSettings, 7);
                MapLabGameLive.World world = MapLabGameLive.Build(plan, null);
                try
                {
                    Assert.That(world.Ready, Is.True, world.Error);
                    MapLabArtBuilder.Plan art = world.View.ArtPlan;
                    int connected = art.Cards.Where(pair => pair.Key.Id.StartsWith(MapLabArtAssets.IdPrefix)).Sum(pair => pair.Value.Count);
                    int old = art.Cards.Where(pair => !pair.Key.Id.StartsWith(MapLabArtAssets.IdPrefix) &&
                                                      (pair.Key.Kind == MapLabObjectKind.Deciduous || pair.Key.Kind == MapLabObjectKind.Conifer)).Sum(pair => pair.Value.Count);
                    Debug.Log($"Лес карты: подключённых деревьев {connected}, прежних пород {old}; сборка {world.BuildMs / 1000.0:0.0} с.");
                    Assert.That(connected, Is.GreaterThan(0), "Подключённые деревья стоят на карте.");
                    Assert.That(old, Is.GreaterThan(connected), "Прежние породы остаются основой леса.");
                    SaveShot(world, new Rect(25f, 25f, 50f, 50f), "map-forest-overview.png");
                }
                finally
                {
                    MapLabGameLive.Clear();
                }
            }
            finally
            {
                oak.Purpose = oakWas;
                spruce.Purpose = spruceWas;
            }
        }

        private static void SaveShot(MapLabGameLive.World world, Rect visible, string name)
        {
            RenderTexture rt = MapLabGameLive.Render(world, visible, 1600, 900);
            Assert.That(rt, Is.Not.Null);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "MapLabForest");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name), image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
    }
}
