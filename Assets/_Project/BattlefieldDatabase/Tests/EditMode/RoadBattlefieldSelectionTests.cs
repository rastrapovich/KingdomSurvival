using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase.Tests
{
    // ПР-12К (канон v1.53 §28.3): дорожный бой — арена по местности. Поле
    // места без рисунка (шахта) не выбирается для дороги; нет подходящего
    // рисунка — null, вызывающий берёт отмеченный временный вариант.
    public sealed class RoadBattlefieldSelectionTests
    {
        private static BattlefieldDatabaseAsset Load()
        {
            BattlefieldDatabaseAsset database = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
            Assert.IsNotNull(database);
            return database;
        }

        [Test]
        public void ForestRoad_GetsForestClearing()
        {
            BattlefieldDefinitionData field = Load().FindForTags(RoadBattlefieldTags.For(WorldMapGameplayTerrainType.Forest));
            Assert.IsNotNull(field);
            Assert.IsTrue(field.HasTag("biome.forest"));
        }

        [Test]
        public void TerrainWithoutArt_ReturnsNull_NotAWrongPicture()
        {
            Assert.IsNull(Load().FindForTags(RoadBattlefieldTags.For(WorldMapGameplayTerrainType.Hills)));
        }

        [Test]
        public void PlaceholderMineField_IsNeverARoadArena()
        {
            BattlefieldDatabaseAsset database = Load();
            BattlefieldDefinitionData mine = database.FindById("old_mine_01");
            Assert.IsNotNull(mine);
            Assert.IsTrue(mine.AwaitingArt, "Рисунка шахты нет — заглушка помечена.");
            Assert.IsNull(database.FindForTags(new[] { "terrain.mine" }));
        }
    }
}
