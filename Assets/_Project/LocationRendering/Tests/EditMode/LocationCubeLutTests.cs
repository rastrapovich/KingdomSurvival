using System.Collections.Generic;
using System.IO;
using KingdomSurvival.LocationRendering.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12М: LUT из .cube и из полосок любого размера — в полоску URP 1024×32.
    public sealed class LocationCubeLutTests
    {
        private static List<string> Cube(int size, System.Func<float, float, float, Vector3> color, params string[] header)
        {
            List<string> lines = new List<string>(header) { "# проба", "LUT_3D_SIZE " + size };
            for (int b = 0; b < size; b++)
                for (int g = 0; g < size; g++)
                    for (int r = 0; r < size; r++)
                    {
                        Vector3 c = color(r / (size - 1f), g / (size - 1f), b / (size - 1f));
                        lines.Add(c.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " +
                                  c.y.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " +
                                  c.z.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
            return lines;
        }

        private static void AssertColor(Color actual, Color expected, string message = null)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(.01f), message);
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(.01f), message);
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(.01f), message);
        }

        [Test]
        public void IdentityCube_BakesToNeutralStrip()
        {
            CubeLut cube = CubeLut.Parse(Cube(17, (r, g, b) => new Vector3(r, g, b), "TITLE \"Тождественный\""), out string error);
            Assert.That(cube, Is.Not.Null, error);
            Assert.That(cube.Title, Is.EqualTo("Тождественный"));
            Texture2D strip = LutTools.Bake(cube);
            try
            {
                Assert.That(strip.width, Is.EqualTo(1024));
                Assert.That(strip.height, Is.EqualTo(32));
                foreach (Vector2Int p in new[] { new Vector2Int(0, 0), new Vector2Int(1023, 31), new Vector2Int(5 * 32 + 3, 7), new Vector2Int(600, 20) })
                    AssertColor(strip.GetPixel(p.x, p.y), LutTools.NeutralAt(p.x, p.y), "Клетка " + p);
            }
            finally { Object.DestroyImmediate(strip); }
        }

        // Красный меняется быстрее всех, потом зелёный, потом синий — как в .cube.
        [Test]
        public void CubeOrder_RedFastest_ChannelsNotSwapped()
        {
            CubeLut cube = CubeLut.Parse(Cube(2, (r, g, b) => new Vector3(1 - r, g * .5f, b)), out string error);
            Assert.That(cube, Is.Not.Null, error);
            AssertColor(cube.Sample(new Color(0, 0, 0)), new Color(1, 0, 0));
            AssertColor(cube.Sample(new Color(1, 1, 0)), new Color(0, .5f, 0));
            AssertColor(cube.Sample(new Color(.5f, .5f, 1)), new Color(.5f, .25f, 1), "Трилинейно между узлами.");
        }

        [Test]
        public void Domain_And_OneDimensional_AreHonoured()
        {
            CubeLut wide = CubeLut.Parse(Cube(2, (r, g, b) => new Vector3(r, g, b), "DOMAIN_MIN 0 0 0", "DOMAIN_MAX 2 2 2"), out string error);
            Assert.That(wide, Is.Not.Null, error);
            AssertColor(wide.Sample(new Color(1, 1, 1)), new Color(.5f, .5f, .5f), "Вход 1 при диапазоне 0..2 — середина.");

            CubeLut oneD = CubeLut.Parse(new[] { "LUT_1D_SIZE 3", "0 0 0", "0.2 0.5 0.8", "1 1 1" }, out error);
            Assert.That(oneD, Is.Not.Null, error);
            Assert.That(oneD.OneDimensional, Is.True);
            AssertColor(oneD.Sample(new Color(.5f, .5f, .5f)), new Color(.2f, .5f, .8f), "Каждый канал — своей кривой.");
        }

        [TestCase(new[] { "0 0 0", "1 1 1" }, "LUT_3D_SIZE")]
        [TestCase(new[] { "LUT_3D_SIZE 2", "0 0 0" }, "нужно 8")]
        [TestCase(new[] { "LUT_3D_SIZE 2", "0 0 x" }, "Строка 2")]
        [TestCase(new[] { "LUT_3D_SIZE 1" }, "размер")]
        public void BrokenCube_GivesReadableError(string[] lines, string fragment)
        {
            Assert.That(CubeLut.Parse(lines, out string error), Is.Null);
            Assert.That(error, Does.Contain(fragment));
        }

        [Test]
        public void StripOfOtherSize_IsResampledTo32()
        {
            const int size = 16;
            Color[] pixels = new Color[size * size * size];
            for (int g = 0; g < size; g++)
                for (int x = 0; x < size * size; x++)
                {
                    int r = x % size, b = x / size;
                    // Синий и красный местами: видно, что пересчёт идёт по таблице.
                    pixels[g * size * size + x] = new Color(b / (size - 1f), g / (size - 1f), r / (size - 1f));
                }
            CubeLut cube = CubeLut.FromStrip(pixels, size * size, size);
            Assert.That(cube, Is.Not.Null);
            AssertColor(cube.Sample(new Color(1, .4f, 0)), new Color(0, .4f, 1));
            Assert.That(CubeLut.FromStrip(new Color[512 * 512], 512, 512), Is.Null, "Сетка 8×8 — не полоска.");
        }

        // Файлы с диска: .cube и полоска 256×16 — в LUT проекта; сетка 512×512
        // и .cube в поле бумаги — понятный отказ.
        [Test]
        public void Files_BecomeProjectLuts_OrReadableRefusals()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ks_cube_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            List<string> created = new List<string>();
            try
            {
                string cubePath = Path.Combine(folder, "zz_test_warm.cube");
                File.WriteAllLines(cubePath, Cube(9, (r, g, b) => new Vector3(Mathf.Min(1, r * 1.1f), g, b * .9f)));
                Texture2D fromCube = FrameTextureDrop.FromFile(cubePath, FrameTextureKind.Lut, out string error);
                Assert.That(fromCube, Is.Not.Null, error);
                created.Add(AssetDatabase.GetAssetPath(fromCube));
                Assert.That(LutTools.Problem(fromCube), Is.Null, "Полоска 1024×32 без sRGB.");
                Assert.That(Path.GetFileName(created[0]), Does.StartWith("zz_test_warm_LUT"));

                Assert.That(FrameTextureDrop.FromFile(cubePath, FrameTextureKind.Paper, out error), Is.Null);
                Assert.That(error, Does.Contain("поле LUT"));

                string stripPath = Path.Combine(folder, "zz_test_strip16.png");
                Texture2D strip = new Texture2D(256, 16, TextureFormat.RGBA32, false);
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 256; x++)
                        strip.SetPixel(x, y, new Color((x % 16) / 15f, y / 15f, (x / 16) / 15f));
                strip.Apply();
                File.WriteAllBytes(stripPath, strip.EncodeToPNG());
                Object.DestroyImmediate(strip);
                Texture2D fromStrip = LutTools.FromImageFile(stripPath, out error);
                Assert.That(fromStrip, Is.Not.Null, error);
                created.Add(AssetDatabase.GetAssetPath(fromStrip));
                Assert.That(fromStrip.width, Is.EqualTo(1024));

                string gridPath = Path.Combine(folder, "zz_test_grid.png");
                Texture2D grid = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                File.WriteAllBytes(gridPath, grid.EncodeToPNG());
                Object.DestroyImmediate(grid);
                Assert.That(LutTools.FromImageFile(gridPath, out error), Is.Null, "Сетку молча в LUT не превращать.");
                Assert.That(error, Does.Contain("512×512"));
            }
            finally
            {
                foreach (string asset in created) AssetDatabase.DeleteAsset(asset);
                if (AssetDatabase.IsValidFolder(LutTools.Folder) && AssetDatabase.FindAssets("", new[] { LutTools.Folder }).Length == 0)
                    AssetDatabase.DeleteAsset(LutTools.Folder);
                Directory.Delete(folder, true);
            }
        }
    }
}
