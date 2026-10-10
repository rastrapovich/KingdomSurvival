using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Tests
{
    // ПР-12О: ручные слои земли — Color, Normal и Height по отдельности:
    // земля из одной картинки любого размера, замена слоя с пересчётом под
    // сетку, подгонка места под новый рисунок, удаление и сохранение целиком;
    // терпимый импорт экспорта Blender (изменённые Normal/Height, проход не
    // загружается).
    public sealed class GroundLayersTests
    {
        private string folder;
        private string locationId;
        private LocalLocationDatabaseAsset database;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetFullPath("Temp"), "KSGroundLayers_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            locationId = "test_layers_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            database = ScriptableObject.CreateInstance<LocalLocationDatabaseAsset>();
            database.locations.Add(new LocalLocationDefinition
            {
                Id = locationId, DisplayName = "Тест слоёв", CanvasWidth = 128, CanvasHeight = 96, HexesAcross = 20,
                Entrances = new List<LocalEntranceDefinition> { new LocalEntranceDefinition { Id = "entry", Label = "Вход", Point = new LocalPointData(64, 48) } }
            });
            database.visuals.Add(new LocationVisualDefinition { LocationId = locationId, TechnicalTest = true });
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            AssetDatabase.DeleteAsset(GroundImporter.Root + "/" + locationId);
            UnityEngine.Object.DestroyImmediate(database);
        }

        private LocalLocationDefinition Location => database.locations[0];
        private LocationVisualDefinition Visual => database.visuals[0];

        private bool Apply(GroundLayers.Layer layer, string path, GroundLayers.Options options, out GroundLayers.Plan plan, out string message)
        {
            plan = GroundLayers.Analyze(Location, Visual.Ground, layer, path, options ?? new GroundLayers.Options());
            if (!plan.CanApply) { message = string.Join("\n", plan.Errors); return false; }
            return GroundLayers.Apply(plan, database, locationId, (target, w, h) => LocationDatabaseWindow.ResizeLocation(target, w, h, target.HexesAcross, true), out message);
        }

        // Картинка: пиксель (x, y сверху) = (x mod 256, y mod 256, mark, 255).
        private string Picture(int width, int height, byte mark, string name)
        {
            PngCodec.RowWriter writer = new PngCodec.RowWriter(width, height);
            byte[] row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) { row[x * 4] = (byte)x; row[x * 4 + 1] = (byte)y; row[x * 4 + 2] = mark; row[x * 4 + 3] = 255; }
                writer.WriteRow(row, 0);
            }
            string path = Path.Combine(folder, name);
            File.WriteAllBytes(path, writer.Finish());
            return path;
        }

        // Ровная нормаль «вверх по экрану» (0, 1, 0)… по умолчанию — прямо на зрителя.
        private string FlatNormal(int width, int height, string name, Vector3 normal)
        {
            ushort[] samples = new ushort[width * height * 3];
            for (int i = 0; i < width * height; i++)
            {
                samples[i * 3] = (ushort)Mathf.RoundToInt((normal.x * .5f + .5f) * 255);
                samples[i * 3 + 1] = (ushort)Mathf.RoundToInt((normal.y * .5f + .5f) * 255);
                samples[i * 3 + 2] = (ushort)Mathf.RoundToInt((normal.z * .5f + .5f) * 255);
            }
            string path = Path.Combine(folder, name);
            File.WriteAllBytes(path, PngCodec.Encode(width, height, 8, PngCodec.Rgb, samples));
            return path;
        }

        // Высота — наклонная плоскость: значение растёт слева направо, левый край без покрытия.
        private string Ramp(int width, int height, string name)
        {
            ushort[] samples = new ushort[width * height * 2];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 2;
                    samples[i] = (ushort)Math.Round(x / (double)(width - 1) * 65535);
                    samples[i + 1] = (ushort)(x == 0 ? 0 : 65535);
                }
            string path = Path.Combine(folder, name);
            File.WriteAllBytes(path, PngCodec.Encode(width, height, 16, PngCodec.GrayAlpha, samples));
            return path;
        }

        private static Color32 TilePixel(LocationGroundTile tile, int x, int yTop, out PngCodec.Header header)
        {
            ushort[] samples = PngCodec.Decode(File.ReadAllBytes(AssetDatabase.GetAssetPath(tile.Color.texture)), out header);
            int i = (yTop * header.Width + x) * header.Channels;
            return new Color32((byte)samples[i], (byte)samples[i + 1], (byte)samples[i + 2], (byte)samples[i + 3]);
        }

        [Test]
        public void GroundFromOneColorPicture_AnySize()
        {
            // Шире участка: три участка в ряд и добивка 2 пикселя справа.
            string color = Picture(4099, 20, 77, "field.png");
            Assert.That(Apply(GroundLayers.Layer.Color, color, null, out GroundLayers.Plan plan, out string message), Is.True, message);
            Assert.That(plan.NewGround, Is.True);
            LocationGroundDefinition ground = Visual.Ground;
            Assert.That(ground.IsTiled, Is.True);
            Assert.That(new Vector2Int(ground.Columns, ground.Rows), Is.EqualTo(new Vector2Int(3, 1)));
            Assert.That(ground.VirtualSize, Is.EqualTo(new Vector2Int(4101, 20)));
            Assert.That(new Vector2(Location.CanvasWidth, Location.CanvasHeight), Is.EqualTo(new Vector2(4101, 20)), "Место подстроено под рисунок (с добивкой).");
            Assert.That(LocationGroundLayout.RuntimeErrors(Visual, Location), Is.Empty);
            // Пиксели — без пересчёта; добивка прозрачная.
            Assert.That(TilePixel(ground.Find(1, 0), 10, 4, out _), Is.EqualTo(new Color32((byte)((1367 + 10) % 256), 4, 77, 255)));
            Assert.That(TilePixel(ground.Find(2, 0), 1366, 4, out _).a, Is.EqualTo(0), "Добивка до целого участка — прозрачная.");
            // Вход сохранил долю рисунка.
            Assert.That(Location.Entrances[0].Point.X, Is.EqualTo(64 * 4099 / 128f).Within(.5f));
            Assert.That(GroundLayers.Describe(ground, GroundLayers.Layer.Normal), Does.StartWith("нет"));
            Assert.That(GroundImporter.IsIncomplete(ground), Is.False, "Без Normal и Height земля полная.");
        }

        [Test]
        public void ColorOfOtherSize_KeepPlace_IsResampled_NormalStays()
        {
            Assert.That(Apply(GroundLayers.Layer.Color, Picture(128, 96, 10, "base.png"), null, out _, out string message), Is.True, message);
            Assert.That(Apply(GroundLayers.Layer.Normal, FlatNormal(50, 40, "base_normal.png", Vector3.forward), null, out GroundLayers.Plan normalPlan, out message), Is.True, message);
            Assert.That(normalPlan.Exact, Is.False, "Нормаль другого размера пересчитана под карту.");
            LocationGroundTile tile = Visual.Ground.Find(0, 0);
            Assert.That(new Vector2Int(tile.Normal.width, tile.Normal.height), Is.EqualTo(new Vector2Int(128, 96)));
            Assert.That(SpriteNormalMaps.Find(tile.Color), Is.EqualTo(tile.Normal), "Нормаль подключена второй текстурой.");

            // Чуть другой кадр — растягивается на ту же землю, место прежнее.
            GroundLayers.Options keep = new GroundLayers.Options { Fit = GroundLayers.Fit.KeepPlace };
            Assert.That(Apply(GroundLayers.Layer.Color, Picture(130, 95, 200, "paint.png"), keep, out GroundLayers.Plan plan, out message), Is.True, message);
            Assert.That(plan.Exact, Is.False);
            tile = Visual.Ground.Find(0, 0);
            Assert.That(tile.Color.rect.size, Is.EqualTo(new Vector2(128, 96)));
            Assert.That(TilePixel(tile, 64, 48, out _).b, Is.EqualTo(200));
            Assert.That(tile.IsPainted, Is.True);
            Assert.That(SpriteNormalMaps.Find(tile.Color), Is.EqualTo(tile.Normal), "Новый Color получил прежнюю нормаль.");
            Assert.That(new Vector2(Location.CanvasWidth, Location.CanvasHeight), Is.EqualTo(new Vector2(128, 96)));

            // Целое кратное — без пересчёта, разрешение ×2.
            Assert.That(Apply(GroundLayers.Layer.Color, Picture(256, 192, 90, "paint2.png"), keep, out plan, out message), Is.True, message);
            Assert.That(plan.Exact, Is.True);
            Assert.That(Visual.Ground.ColorScale, Is.EqualTo(2));
            Assert.That(Visual.Ground.Find(0, 0).Color.rect.size, Is.EqualTo(new Vector2(256, 192)));
            Assert.That(LocationGroundLayout.RuntimeErrors(Visual, Location), Is.Empty);
        }

        [Test]
        public void ColorFitPicture_RegridsNormalAndHeight()
        {
            Assert.That(Apply(GroundLayers.Layer.Color, Picture(128, 96, 10, "base.png"), null, out _, out string message), Is.True, message);
            GroundLayers.Options height = new GroundLayers.Options { HeightMin = -1, HeightMax = 3, MetersPerBlenderUnit = 1 };
            Assert.That(Apply(GroundLayers.Layer.Height, Ramp(128, 96, "base_height.png"), height, out GroundLayers.Plan heightPlan, out message), Is.True, message);
            Assert.That(heightPlan.Exact, Is.True);
            Assert.That(Apply(GroundLayers.Layer.Normal, FlatNormal(128, 96, "base_n.png", Vector3.up), null, out _, out message), Is.True, message);
            LocationHeightField before = LocationHeightField.Load(Visual.Ground, Location, null);
            Assert.That(before.TrySampleCanvas(new Vector2(96, 48), out HeightSample old), Is.True);

            GroundLayers.Options fit = new GroundLayers.Options { Fit = GroundLayers.Fit.FitPicture };
            Assert.That(Apply(GroundLayers.Layer.Color, Picture(256, 192, 50, "big.png"), fit, out GroundLayers.Plan plan, out message), Is.True, message);
            Assert.That(plan.Regrid, Is.True);
            LocationGroundDefinition ground = Visual.Ground;
            Assert.That(ground.VirtualSize, Is.EqualTo(new Vector2Int(256, 192)));
            Assert.That(new Vector2(Location.CanvasWidth, Location.CanvasHeight), Is.EqualTo(new Vector2(256, 192)));
            LocationGroundTile tile = ground.Find(0, 0);
            Assert.That(tile.Normal, Is.Not.Null, "Normal пересчитан на новый кадр.");
            Assert.That(new Vector2Int(tile.Normal.width, tile.Normal.height), Is.EqualTo(new Vector2Int(256, 192)));
            Assert.That(SpriteNormalMaps.Find(tile.Color), Is.EqualTo(tile.Normal));
            LocationHeightField after = LocationHeightField.Load(ground, Location, null);
            Assert.That(after, Is.Not.Null, "Height пересчитан на новый кадр.");
            Assert.That(after.TrySampleCanvas(new Vector2(192, 96), out HeightSample now), Is.True);
            Assert.That(now.Blender, Is.EqualTo(old.Blender).Within(.05), "Та же точка земли — та же высота.");
            Assert.That(LocationGroundLayout.RuntimeErrors(Visual, Location), Is.Empty);
        }

        [Test]
        public void LayersRemoveAndExportWhole()
        {
            Assert.That(Apply(GroundLayers.Layer.Color, Picture(128, 96, 10, "base.png"), null, out _, out string message), Is.True, message);
            GroundLayers.Options height = new GroundLayers.Options { HeightMin = 0, HeightMax = 10, MetersPerBlenderUnit = 2 };
            Assert.That(Apply(GroundLayers.Layer.Height, Ramp(64, 48, "small_height.png"), height, out GroundLayers.Plan plan, out message), Is.True, message);
            Assert.That(plan.Exact, Is.False);
            Assert.That(Apply(GroundLayers.Layer.Normal, FlatNormal(128, 96, "n.png", Vector3.forward), null, out _, out message), Is.True, message);
            LocationGroundDefinition ground = Visual.Ground;
            Assert.That(ground.HeightEnabled, Is.True);
            Assert.That(ground.MetersPerBlenderUnit, Is.EqualTo(2));

            // Сохранить Height целиком и загрузить снова — без пересчёта, те же значения.
            string whole = Path.Combine(folder, "whole_height.png");
            Assert.That(GroundLayers.ExportWhole(ground, GroundLayers.Layer.Height, whole, out message), Is.True, message);
            LocationHeightField before = LocationHeightField.Load(ground, Location, null);
            Assert.That(Apply(GroundLayers.Layer.Height, whole, height, out plan, out message), Is.True, message);
            Assert.That(plan.Exact, Is.True);
            LocationHeightField again = LocationHeightField.Load(Visual.Ground, Location, null);
            foreach (Vector2 point in new[] { new Vector2(20, 10), new Vector2(100, 80) })
            {
                Assert.That(before.TrySampleCanvas(point, out HeightSample a, true) && again.TrySampleCanvas(point, out HeightSample b, true), Is.True);
                again.TrySampleCanvas(point, out HeightSample c, true);
                Assert.That(c.Blender, Is.EqualTo(a.Blender).Within(1e-3));
            }
            string normal = Path.Combine(folder, "whole_normal.png");
            Assert.That(GroundLayers.ExportWhole(Visual.Ground, GroundLayers.Layer.Normal, normal, out message), Is.True, message);
            Assert.That(PngSizeOf(normal), Is.EqualTo(new Vector2Int(128, 96)));

            // Удалить Normal и Height — земля остаётся, нормаль снята со спрайтов.
            Assert.That(GroundLayers.Remove(database, locationId, GroundLayers.Layer.Normal, out message), Is.True, message);
            Assert.That(Visual.Ground.Tiles.All(tile => tile.Normal == null), Is.True);
            Assert.That(SpriteNormalMaps.Find(Visual.Ground.Find(0, 0).Color), Is.Null);
            Assert.That(GroundLayers.Remove(database, locationId, GroundLayers.Layer.Height, out message), Is.True, message);
            Assert.That(Visual.Ground.HasAnyHeight, Is.False);
            Assert.That(Visual.Ground.HeightEnabled, Is.False);
            Assert.That(LocationGroundLayout.RuntimeErrors(Visual, Location), Is.Empty);

            // Удалить землю — место снова на «Рисунке места», файлов земли нет.
            Assert.That(GroundLayers.Remove(database, locationId, GroundLayers.Layer.Color, out message), Is.True, message);
            Assert.That(Visual.Ground.IsTiled, Is.False);
            string root = GroundImporter.Root + "/" + locationId;
            Assert.That(Directory.Exists(root) ? Directory.GetFiles(root, "*.png", SearchOption.AllDirectories).Length : 0, Is.EqualTo(0));
        }

        [Test]
        public void NormalOrHeightWithoutColor_IsRefused()
        {
            GroundLayers.Plan plan = GroundLayers.Analyze(Location, Visual.Ground, GroundLayers.Layer.Normal, FlatNormal(8, 8, "n.png", Vector3.forward), new GroundLayers.Options());
            Assert.That(plan.CanApply, Is.False);
            Assert.That(string.Join("\n", plan.Errors), Does.Contain("Color"));
            Assert.That(GroundLayers.Guess("map_height.png"), Is.EqualTo(GroundLayers.Layer.Height));
            Assert.That(GroundLayers.Guess("map_n.png"), Is.EqualTo(GroundLayers.Layer.Normal));
            Assert.That(GroundLayers.Guess("map_paint.png"), Is.EqualTo(GroundLayers.Layer.Color));
        }

        // Экспорт целиком: изменённый Normal — предупреждение, Normal другого
        // размера — не загружается; Normal можно не загружать — у участков
        // остаётся прежний.
        [Test]
        public void WholeExport_ChangedOrMismatchedNormalDoesNotBlock()
        {
            GroundSyntheticExport export = new GroundSyntheticExport { MapId = "Test_Map", Columns = 2, Rows = 2, Width = 64, Height = 48 };
            string manifest = export.WriteWhole(Path.Combine(folder, "whole"));
            string normalPath = Directory.GetFiles(Path.Combine(folder, "whole"), "*normal*.png").Single();
            File.WriteAllBytes(normalPath, File.ReadAllBytes(FlatNormal(128, 96, "flat.png", Vector3.forward)));
            GroundExportPackage package = GroundExportPackage.Load(manifest);
            Assert.That(package.Errors, Is.Empty, string.Join("\n", package.Errors));
            Assert.That(string.Join("\n", package.Warnings), Does.Contain("Normal изменён"));
            Assert.That(package.Requested(GroundExportPackage.Normal), Is.True);
            GroundImporter.Plan plan = GroundImporter.Analyze(package, Location, Visual);
            Assert.That(GroundImporter.Apply(plan, database, (target, w, h) => LocationDatabaseWindow.ResizeLocation(target, w, h, target.HexesAcross, true), out string message), Is.True, message);
            Texture2D normal = Visual.Ground.Find(0, 0).Normal;
            Assert.That(normal, Is.Not.Null);

            // Normal не загружать: Color обновляется, прежний Normal остаётся.
            File.Copy(Picture(128, 96, 33, "repaint.png"), Directory.GetFiles(Path.Combine(folder, "whole"), "*color*.png").Single(), true);
            package = GroundExportPackage.Load(manifest);
            package.Excluded.Add(GroundExportPackage.Normal);
            plan = GroundImporter.Analyze(package, Location, Visual);
            Assert.That(GroundImporter.Apply(plan, database, (target, w, h) => LocationDatabaseWindow.ResizeLocation(target, w, h, target.HexesAcross, true), out message), Is.True, message);
            LocationGroundTile tile = Visual.Ground.Find(0, 0);
            Assert.That(tile.Normal, Is.EqualTo(normal), "Normal из пакета не загружался — остался прежний.");
            Assert.That(SpriteNormalMaps.Find(tile.Color), Is.EqualTo(normal), "Новый Color с прежней нормалью.");

            // Normal другого размера — проход не загружается, импорт не блокируется.
            File.WriteAllBytes(normalPath, File.ReadAllBytes(FlatNormal(100, 50, "odd.png", Vector3.forward)));
            package = GroundExportPackage.Load(manifest);
            Assert.That(package.Errors, Is.Empty, string.Join("\n", package.Errors));
            Assert.That(package.Unavailable, Does.Contain(GroundExportPackage.Normal));
            Assert.That(package.Requested(GroundExportPackage.Normal), Is.False);
            Assert.That(package.ReadyTiles, Is.EqualTo(4));
        }

        private static Vector2Int PngSizeOf(string path)
        {
            GroundExportPackage.ReadHeader(path, out PngCodec.Header header, out _);
            return new Vector2Int(header.Width, header.Height);
        }
    }
}
