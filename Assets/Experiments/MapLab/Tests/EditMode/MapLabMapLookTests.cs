using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Облик карты: облака тумана войны «Обычного» (пять PNG из папки classic)
    // и их цветокоррекция — те же числа, что были у прежнего вида «Обычный».
    public sealed class MapLabMapLookTests
    {
        [Test]
        public void Defaults_MatchFormerClassicFogCloudAdjust()
        {
            MapLabMapLook look = ScriptableObject.CreateInstance<MapLabMapLook>();
            try
            {
                Assert.AreEqual(0.95f, look.FogCloudBrightness, 1e-5f);
                Assert.AreEqual(0f, look.FogCloudHue, 1e-5f);
                Assert.AreEqual(0.8f, look.FogCloudSaturation, 1e-5f);
                Assert.AreEqual(0f, look.FogCloudLevels.InputBlack, 1e-5f);
                Assert.AreEqual(255f, look.FogCloudLevels.InputWhite, 1e-5f);
                Assert.AreEqual(0.95f, look.FogCloudLevels.Gamma, 1e-5f);
                Assert.AreEqual(40f, look.FogCloudLevels.OutputBlack, 1e-5f);
                Assert.AreEqual(222f, look.FogCloudLevels.OutputWhite, 1e-5f);
                Assert.IsFalse(look.FogCloudAdjustNeutral, "Коррекция облаков «Обычного» не нейтральна.");
            }
            finally
            {
                Object.DestroyImmediate(look);
            }
        }

        [Test]
        public void ShippedAsset_HasFiveClassicClouds_WithClassicAdjust()
        {
            MapLabMapLook.ResetCache();
            MapLabMapLook look = MapLabMapLook.Load();
            Assert.IsNotNull(look);
            Assert.AreEqual(5, look.FogCloudCount, "Пять облаков тумана из Art/MapFogClouds/classic.");
            foreach (Texture2D cloud in look.FogClouds)
                Assert.IsTrue(UnityEditor.AssetDatabase.GetAssetPath(cloud).StartsWith(MapLabMapLook.FogCloudsFolder), "Облако из папки classic: " + cloud.name);
            Assert.AreEqual(0.95f, look.FogCloudBrightness, 1e-5f);
            Assert.AreEqual(0f, look.FogCloudHue, 1e-5f);
            Assert.AreEqual(0.8f, look.FogCloudSaturation, 1e-5f);
            Assert.AreEqual(0.95f, look.FogCloudLevels.Gamma, 1e-5f);
            Assert.AreEqual(40f, look.FogCloudLevels.OutputBlack, 1e-5f);
            Assert.AreEqual(222f, look.FogCloudLevels.OutputWhite, 1e-5f);
        }

        [Test]
        public void FogCloudAdjustKey_ChangesWithAdjust_AndNeutralIsDash()
        {
            MapLabMapLook look = ScriptableObject.CreateInstance<MapLabMapLook>();
            try
            {
                string key = look.FogCloudAdjustKey;
                look.FogCloudBrightness = 1.1f;
                Assert.AreNotEqual(key, look.FogCloudAdjustKey);
                look.FogCloudBrightness = look.FogCloudSaturation = 1f;
                look.FogCloudHue = 0f;
                look.FogCloudLevels.Reset();
                Assert.IsTrue(look.FogCloudAdjustNeutral);
                Assert.AreEqual("-", look.FogCloudAdjustKey);
            }
            finally
            {
                Object.DestroyImmediate(look);
            }
        }
    }
}
