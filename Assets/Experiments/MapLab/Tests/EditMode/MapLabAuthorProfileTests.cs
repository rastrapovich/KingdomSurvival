using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Этап 12У-B: профиль автора окна «Генератор карты» — макропараметры над
    // рабочими полями, переопределения, коррекция ассетов без пересоздания мира.
    public sealed class MapLabAuthorProfileTests
    {
        private static MapLabSettings Settings()
        {
            MapLabSettings s = ScriptableObject.CreateInstance<MapLabSettings>();
            MapLabSettings.ApplyPresetDefaults(s, MapLabPresetKind.RiverValley);
            s.General.Quality = MapLabQuality.Samples129;
            s.Stage03.Look = MapLabLook.Stage05;
            return s;
        }

        [Test]
        public void NeutralProfile_KeepsWorkingValues()
        {
            MapLabSettings s = Settings();
            try
            {
                s.Relief.HeightRange = 13.5f;
                s.Vegetation.ForestDensity = 0.41f;
                s.Stage05.InkDensity = 0.8f;
                s.Stage05.GrassDensity = 1.3f;
                MapLabAuthor.Capture(s);
                MapLabAuthor.ApplyStructure(s);
                MapLabAuthor.ApplyLook(s);
                Assert.AreEqual(13.5f, s.Relief.HeightRange, 1e-4f);
                Assert.AreEqual(0.41f, s.Vegetation.ForestDensity, 1e-4f);
                Assert.AreEqual(0.8f, s.Stage05.InkDensity, 1e-4f);
                Assert.AreEqual(1.3f, s.Stage05.GrassDensity, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void Macros_MoveFields_OverrideSurvivesMacroChange()
        {
            MapLabSettings s = Settings();
            try
            {
                MapLabAuthor.Capture(s);
                float baseRelief = s.Relief.HeightRange, baseForest = s.Vegetation.ForestDensity;
                s.Author.Relief = 1f;
                s.Author.Forest = 0f;
                s.Author.Details = 0f;
                MapLabAuthor.ApplyStructure(s);
                MapLabAuthor.ApplyLook(s);
                Assert.Greater(s.Relief.HeightRange, baseRelief, "рельеф не стал выраженнее");
                Assert.Less(s.Vegetation.ForestDensity, baseForest, "лес не поредел");
                Assert.AreEqual(0f, s.Stage05.GrassDensity, 1e-5f, "мелкие детали не убраны");

                s.Author.RiverWidthSet = true;
                s.Author.RiverWidth = 21f;
                MapLabAuthor.ApplyStructure(s);
                Assert.AreEqual(21f, s.Water.RiverWidth, 1e-4f);
                s.Author.Water = 0f;
                MapLabAuthor.ApplyStructure(s);
                Assert.AreEqual(21f, s.Water.RiverWidth, 1e-4f, "макропараметр стёр точную правку");

                s.Author.VillageSize = MapLabVillageSize.Small;
                MapLabAuthor.ApplyStructure(s);
                Assert.AreEqual(3, s.Stage05.YardsMin);
                Assert.AreEqual(6, s.Stage05.YardsMax);
            }
            finally
            {
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void ArtEntryCorrection_NeutralAndDisabled()
        {
            MapLabArtEntry e = new MapLabArtEntry { Weight = 2f, WorldHeight = 4f };
            Assert.IsFalse(e.HasColorAdjust, "по умолчанию коррекции нет");
            Assert.AreEqual(new Vector4(1f, 0f, 1f, 0f), e.ColorAdjust);
            Assert.AreEqual(2f, e.EffectiveWeight);
            float height = e.CardHeight;
            e.SizeScale = 1.5f;
            Assert.AreEqual(height * 1.5f, e.CardHeight, 1e-4f, "размер — равномерный множитель");
            e.Enabled = false;
            Assert.AreEqual(0f, e.EffectiveWeight, "выключенный вариант не выбирается");
            Assert.AreEqual(2f, e.Weight, "вес сохраняется для возврата");
        }

        [Test]
        public void SizeSides_AreStable()
        {
            Assert.AreEqual(256f, MapLabAuthor.SideOf(MapLabWorldSize.Medium));
            Assert.AreEqual(MapLabWorldSize.Large, MapLabAuthor.SizeOf(384f));
            Assert.IsFalse(MapLabAuthor.Verified(MapLabWorldSize.Huge));
        }
    }
}
