using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Уровни, как в Photoshop (19.11.2026): нейтральные ничего не меняют и
    // дают нулевые векторы шейдеру; вход и выход ведут себя как в Photoshop;
    // процессорная коррекция облаков совпадает с формулой.
    public sealed class MapLabLevelsTests
    {
        [Test]
        public void Neutral_IsIdentity_AndZeroForShaders()
        {
            MapLabLevels l = new MapLabLevels();
            Assert.IsTrue(l.IsIdentity);
            Assert.AreEqual(Vector4.zero, l.Shader);
            Assert.AreEqual(Vector4.zero, l.ShaderOut);
            for (int i = 0; i <= 255; i += 15)
                Assert.AreEqual(i / 255f, l.Apply(i / 255f), 1e-5f);
            Assert.IsFalse(new MapLabArtEntry().HasColorAdjust, "Новый ассет без коррекции.");
        }

        [Test]
        public void InputAndOutput_WorkLikePhotoshop()
        {
            MapLabLevels l = new MapLabLevels { InputBlack = 50, InputWhite = 200 };
            Assert.AreEqual(0f, l.Apply(40 / 255f), 1e-5f, "Темнее чёрной точки — чёрное.");
            Assert.AreEqual(1f, l.Apply(220 / 255f), 1e-5f, "Светлее белой — белое.");
            Assert.AreEqual(0.5f, l.Apply(125 / 255f), 1e-3f, "Середина диапазона — середина.");

            MapLabLevels gamma = new MapLabLevels { Gamma = 2f };
            Assert.Greater(gamma.Apply(0.5f), 0.5f, "Гамма больше 1 — средние тона светлее.");

            MapLabLevels output = new MapLabLevels { OutputBlack = 51, OutputWhite = 204 };
            Assert.AreEqual(0.2f, output.Apply(0f), 1e-3f);
            Assert.AreEqual(0.8f, output.Apply(1f), 1e-3f);

            Vector4 v = new MapLabLevels { InputBlack = 51, InputWhite = 204, Gamma = 1.5f, OutputBlack = 25.5f, OutputWhite = 229.5f }.Shader;
            Assert.AreEqual(0.2f, v.x, 1e-4f);
            Assert.AreEqual(0.2f, v.y, 1e-4f);
            Assert.AreEqual(0.5f, v.z, 1e-4f);
            Assert.AreEqual(0.1f, v.w, 1e-4f);
        }

        [Test]
        public void CloudShadow_IsSoftWhiteSilhouette_WithEmptyPadding()
        {
            Texture2D cloud = new Texture2D(200, 100, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[200 * 100];
            for (int y = 0; y < 100; y++)
                for (int x = 0; x < 200; x++)
                    px[y * 200 + x] = x > 40 && x < 160 && y > 20 && y < 80 ? new Color32(90, 80, 70, 255) : new Color32(0, 0, 0, 0);
            cloud.SetPixels32(px);
            cloud.Apply();
            Texture2D shadow = MapLabColorMath.CreateShadow(cloud, 0.1f, 0.03f, 128);
            try
            {
                Assert.IsNotNull(shadow);
                Assert.AreEqual(128, shadow.width);
                Color32[] s = shadow.GetPixels32();
                Color32 center = s[(shadow.height / 2) * shadow.width + shadow.width / 2];
                Assert.Greater(center.a, 200, "Под облаком тень плотная.");
                Assert.AreEqual(255, center.r, "Цвет тени задаёт подкраска: сам рисунок белый.");
                Assert.AreEqual(0, s[0].a, "Поле вокруг пусто.");
                // Край мягкий: есть промежуточные значения прозрачности.
                Assert.IsTrue(System.Array.Exists(s, c => c.a > 30 && c.a < 220), "Размытый край.");
            }
            finally
            {
                Object.DestroyImmediate(cloud);
                if (shadow != null)
                    Object.DestroyImmediate(shadow);
            }
        }

        [Test]
        public void CpuAdjust_AppliesLevels_KeepsAlpha_SkipsTransparent()
        {
            Color32[] pixels = { new Color32(128, 128, 128, 255), new Color32(10, 20, 30, 0), new Color32(255, 0, 0, 128) };
            MapLabColorMath.Adjust(pixels, 1f, 0f, 1f, new MapLabLevels { OutputWhite = 128 });
            Assert.AreEqual(64, pixels[0].r, 1);
            Assert.AreEqual(255, pixels[0].a);
            Assert.AreEqual(new Color32(10, 20, 30, 0), pixels[1], "Прозрачное не трогается.");
            Assert.AreEqual(128, pixels[2].r, 1);
            Assert.AreEqual(128, pixels[2].a, "Альфа не меняется.");
            Assert.IsTrue(MapLabColorMath.IsNeutral(1f, 0f, 1f, new MapLabLevels()));
        }
    }
}
