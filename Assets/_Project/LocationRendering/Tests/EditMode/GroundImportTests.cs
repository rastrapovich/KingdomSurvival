using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Tests
{
    // ПР-12О: импорт земли из Blender в Базу локаций — проверка пакета,
    // безопасность путей, транзакционность, повторный импорт без дублей,
    // замена одного участка, несовместимые наборы и неполный пакет.
    public sealed class GroundImportTests
    {
        private string folder;
        private string locationId;
        private LocalLocationDatabaseAsset database;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetFullPath("Temp"), "KSGroundTests_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            locationId = "test_ground_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            database = ScriptableObject.CreateInstance<LocalLocationDatabaseAsset>();
            database.locations.Add(new LocalLocationDefinition
            {
                Id = locationId, DisplayName = "Тест земли", CanvasWidth = 128, CanvasHeight = 96, HexesAcross = 20,
                Entrances = new List<LocalEntranceDefinition> { new LocalEntranceDefinition { Id = "entry", Label = "Вход", Point = new LocalPointData(64, 48) } }
            });
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = locationId, TechnicalTest = true };
            visual.Objects.Add(new LocationVisualObject { Id = "crate", Name = "Ящик", Placeholder = LocationPlaceholder.Crate, Position = new Vector2(.25f, .5f) });
            database.visuals.Add(visual);
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

        private string Export(GroundSyntheticExport export, string name = "a") => export.Write(Path.Combine(folder, name));

        private static GroundSyntheticExport Small() => new GroundSyntheticExport { MapId = "Test_Map", Columns = 2, Rows = 2, Width = 64, Height = 48 };

        private bool Apply(GroundExportPackage package, out string message, bool partial = false, IEnumerable<string> selected = null)
        {
            GroundImporter.Plan plan = GroundImporter.Analyze(package, Location, Visual, selected);
            plan.AllowPartial = partial;
            return GroundImporter.Apply(plan, database, (target, w, h) => LocationDatabaseWindow.ResizeLocation(target, w, h, target.HexesAcross, true), out message);
        }

        private int GroundFiles() => Directory.Exists(GroundImporter.Root + "/" + locationId)
            ? Directory.GetFiles(GroundImporter.Root + "/" + locationId, "*", SearchOption.AllDirectories).Count(path => !path.EndsWith(".meta"))
            : 0;

        // ---------------- Проверка пакета ----------------

        [Test]
        public void TwoByTwoExportValidates()
        {
            GroundExportPackage package = GroundExportPackage.Load(Export(Small()));
            Assert.That(package.Errors, Is.Empty, string.Join("\n", package.Errors));
            Assert.That(package.ReadyTiles, Is.EqualTo(4));
            Assert.That(package.Partial, Is.False);
            Assert.That(package.Summary(), Does.Contain("Сетка 2×2").And.Contain("128×96"));
            // Папка экспорта вместо файла manifest.
            Assert.That(GroundExportPackage.Load(Path.Combine(folder, "a")).Errors, Is.Empty);
        }

        [Test]
        public void UnsafePathsAndBadHashesAreNotUsed()
        {
            string manifest = Export(Small());
            string text = File.ReadAllText(manifest);
            text = text.Replace("\"Color/Test_Map_X000_Y000.png\"", "\"../outside.png\"")
                       .Replace("\"Color/Test_Map_X001_Y000.png\"", "\"C:/Windows/win.ini\"")
                       .Replace("\"Normal/Test_Map_X000_Y001_normal.png\"", "\"Normal\\\\Test_Map_X000_Y001_normal.png\"");
            File.WriteAllText(manifest, text);
            string height = Path.Combine(folder, "a", "Height", "Test_Map_X001_Y001_height.png");
            byte[] bytes = File.ReadAllBytes(height);
            bytes[bytes.Length / 2] ^= 0x10;
            File.WriteAllBytes(height, bytes);
            GroundExportPackage package = GroundExportPackage.Load(manifest);
            Assert.That(package.Errors, Is.Empty);
            Assert.That(string.Join(";", package.Find(0, 0).Problems), Does.Contain("выход за папку"));
            Assert.That(string.Join(";", package.Find(1, 0).Problems), Does.Contain("относительный путь"));
            Assert.That(string.Join(";", package.Find(0, 1).Problems), Does.Contain("относительный путь"));
            Assert.That(string.Join(";", package.Find(1, 1).Problems), Does.Contain("sha256"));
            Assert.That(package.Partial, Is.True);
            Assert.That(package.ReadyTiles, Is.EqualTo(0));
        }

        [Test]
        public void StructuralErrorsBlockThePackage()
        {
            string manifest = Export(Small());
            string original = File.ReadAllText(manifest);
            void Expect(string from, string to, string error)
            {
                File.WriteAllText(manifest, original.Replace(from, to));
                GroundExportPackage package = GroundExportPackage.Load(manifest);
                Assert.That(string.Join("\n", package.Errors), Does.Contain(error), from + " → " + to);
                Assert.That(package.CanApply, Is.False);
            }
            Expect("\"schema_version\": 1", "\"schema_version\": 3", "schema_version");
            Expect("\"pixel_rect\": [64, 48, 64, 48]", "\"pixel_rect\": [64, 0, 64, 48]", "pixel_rect");
            Expect("\"virtual_size\": [128, 96]", "\"virtual_size\": [96, 128]", "virtual_size");
            Expect("\"tile_indices\": \"bottom_left, X right, Y up\"", "\"tile_indices\": \"top_left\"", "индексов");
            Expect("\"max\": 3", "\"max\": -1", "Height Range");
            Expect("{\"x\": 1, \"y\": 1,", "{\"x\": 0, \"y\": 1,", "повторяется");
            File.WriteAllText(manifest, "{ not json");
            Assert.That(string.Join("\n", GroundExportPackage.Load(manifest).Errors), Does.Contain("JSON"));
        }

        [Test]
        public void NotRequestedIsFineAndPendingIsPartial()
        {
            GroundSyntheticExport export = Small();
            export.Normals = false;
            GroundExportPackage package = GroundExportPackage.Load(Export(export));
            Assert.That(package.Errors, Is.Empty);
            Assert.That(package.Partial, Is.False);
            Assert.That(package.Find(0, 0).Problems, Is.Empty);

            string manifest = Export(Small(), "b");
            File.WriteAllText(manifest, File.ReadAllText(manifest)
                .Replace("\"status\": \"complete\", \"path\": \"Height/Test_Map_X001_Y001_height.png\"", "\"status\": \"pending\", \"path\": \"Height/Test_Map_X001_Y001_height.png\"")
                .Replace("\n\"status\": \"complete\"", "\n\"status\": \"partial\""));
            GroundExportPackage pending = GroundExportPackage.Load(manifest);
            Assert.That(pending.Partial, Is.True);
            Assert.That(string.Join(";", pending.Find(1, 1).Problems), Does.Contain("не завершён").And.Contain("файл есть"));
            Assert.That(string.Join(";", pending.Warnings), Does.Contain("частичный"));
        }

        // ---------------- Применение ----------------

        [Test]
        public void ImportBuildsTilesAndKeepsSixteenBitHeights()
        {
            GroundSyntheticExport export = Small();
            GroundExportPackage package = GroundExportPackage.Load(Export(export));
            Location.CanvasWidth = 256; Location.CanvasHeight = 192;
            Assert.That(Apply(package, out string message), Is.True, message);
            LocationGroundDefinition ground = Visual.Ground;
            Assert.That(ground.IsTiled, Is.True);
            Assert.That(ground.Tiles.Count, Is.EqualTo(4));
            Assert.That(ground.Incomplete, Is.False);
            Assert.That(LocationGroundLayout.RuntimeErrors(Visual, Location), Is.Empty);
            Assert.That(Location.CanvasWidth, Is.EqualTo(128), "размер места подогнан под землю 1:1");
            Assert.That(Location.Entrances[0].Point.X, Is.EqualTo(32).Within(1e-3f), "вход сохранил долю рисунка");
            Assert.That(Visual.Objects.Single().Id, Is.EqualTo("crate"), "предметы места не тронуты");
            Assert.That(Visual.Camera.Version, Is.EqualTo(1), "новой карте — плавное следование");
            foreach (LocationGroundTile tile in ground.Tiles)
            {
                Assert.That(tile.Color, Is.Not.Null);
                Assert.That(tile.Color.rect.size, Is.EqualTo(new Vector2(64, 48)));
                Assert.That(tile.Normal, Is.Not.Null);
                Assert.That(ArtAssets.Editor.SpriteNormalMaps.Find(tile.Color), Is.EqualTo(tile.Normal));
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tile.Color));
                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect));
                Assert.That(importer.mipmapEnabled, Is.False);
                LocationHeightTileData data = LocationHeightTileData.Deserialize(tile.Height.bytes);
                for (int y = 0; y < data.Height; y += 7)
                    for (int x = 0; x < data.Width; x += 5)
                        Assert.That(data.Values[y * data.Width + x], Is.EqualTo(export.RawHeight(tile.X * 64 + x + .5, tile.Y * 48 + y + .5)), tile.Key + " " + x + "," + y);
            }
            // Сервис высоты: точка рисунка места → то же значение, что в исходном PNG.
            LocationHeightField field = LocationHeightField.Load(ground, Location, null);
            Assert.That(field.TrySampleCanvas(new Vector2(70.5f, 10.5f), out HeightSample sample, true), Is.True);
            double expected = -1 + 4 * export.RawHeight(70.5, 96 - 10.5) / 65535.0;
            Assert.That(sample.Blender, Is.EqualTo(expected).Within(1e-9));
            Assert.That(sample.Column, Is.EqualTo(1)); Assert.That(sample.Row, Is.EqualTo(1));
        }

        [Test]
        public void ReimportCreatesNoDuplicatesAndReplacesOnlyChangedTile()
        {
            string manifest = Export(Small());
            Assert.That(Apply(GroundExportPackage.Load(manifest), out string message), Is.True, message);
            int files = GroundFiles();
            Sprite keep = Visual.Ground.Find(0, 0).Color;
            string keepPath = AssetDatabase.GetAssetPath(keep);

            GroundImporter.Plan again = GroundImporter.Analyze(GroundExportPackage.Load(manifest), Location, Visual);
            Assert.That(again.Actions.Values, Is.All.EqualTo(GroundImporter.TileAction.Unchanged));
            Assert.That(Apply(GroundExportPackage.Load(manifest), out message), Is.True, message);
            Assert.That(GroundFiles(), Is.EqualTo(files), "повторный импорт — без новых файлов");
            Assert.That(Visual.Ground.Tiles.Count, Is.EqualTo(4));
            Assert.That(Visual.Ground.Find(0, 0).Color, Is.SameAs(keep));

            // Новый рисунок одного участка (X001_Y000) в том же экспорте.
            string color = Path.Combine(folder, "a", "Color", "Test_Map_X001_Y000.png");
            string oldSha = GroundExportPackage.Sha256Hex(File.ReadAllBytes(color));
            ushort[] samples = Enumerable.Repeat((ushort)200, 64 * 48 * 4).ToArray();
            byte[] png = PngCodec.Encode(64, 48, 8, PngCodec.Rgba, samples);
            File.WriteAllBytes(color, png);
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(oldSha, GroundExportPackage.Sha256Hex(png)));
            GroundImporter.Plan replace = GroundImporter.Analyze(GroundExportPackage.Load(manifest), Location, Visual);
            Assert.That(replace.Actions["X001_Y000"], Is.EqualTo(GroundImporter.TileAction.Replace));
            Assert.That(replace.Actions.Where(pair => pair.Key != "X001_Y000").Select(pair => pair.Value), Is.All.EqualTo(GroundImporter.TileAction.Unchanged));
            Assert.That(Apply(GroundExportPackage.Load(manifest), out message), Is.True, message);
            Assert.That(Visual.Ground.Find(0, 0).Color, Is.SameAs(keep));
            Assert.That(AssetDatabase.GetAssetPath(Visual.Ground.Find(0, 0).Color), Is.EqualTo(keepPath));
            Assert.That(Visual.Ground.Find(1, 0).ColorSha256, Is.EqualTo(GroundExportPackage.Sha256Hex(png)));
            Assert.That(GroundFiles(), Is.EqualTo(files), "старый рисунок участка удалён, новый на его месте");
        }

        [Test]
        public void ChangedFileAfterCheckLeavesLocationUntouched()
        {
            string manifest = Export(Small());
            GroundExportPackage package = GroundExportPackage.Load(manifest);
            GroundImporter.Plan plan = GroundImporter.Analyze(package, Location, Visual);
            string height = Path.Combine(folder, "a", "Height", "Test_Map_X001_Y001_height.png");
            byte[] bytes = File.ReadAllBytes(height);
            bytes[bytes.Length - 20] ^= 1;
            File.WriteAllBytes(height, bytes);
            bool ok = GroundImporter.Apply(plan, database, (t, w, h) => LocationDatabaseWindow.ResizeLocation(t, w, h, t.HexesAcross, true), out string message);
            Assert.That(ok, Is.False);
            Assert.That(message, Does.Contain("изменился").And.Contain("не изменено"));
            Assert.That(Visual.Ground.IsTiled, Is.False);
            Assert.That(Location.CanvasWidth, Is.EqualTo(128));
            Assert.That(GroundFiles(), Is.EqualTo(0));
        }

        [Test]
        public void IncompatibleSecondExportIsRejectedUnlessWholeMap()
        {
            Assert.That(Apply(GroundExportPackage.Load(Export(Small())), out string message), Is.True, message);
            GroundSyntheticExport moved = Small();
            moved.Origin = new double[] { 4, 0 };
            GroundExportPackage other = GroundExportPackage.Load(Export(moved, "b"));
            GroundImporter.Plan partial = GroundImporter.Analyze(other, Location, Visual, new[] { "X000_Y000" });
            Assert.That(string.Join("\n", partial.Blockers), Does.Contain("несовместимы").And.Contain("origin"));
            GroundImporter.Plan whole = GroundImporter.Analyze(other, Location, Visual);
            Assert.That(whole.Blockers, Is.Empty);
            Assert.That(whole.ReplacesWholeGround, Is.True);
        }

        [Test]
        public void PartialPackageOnlyAsMarkedPreview()
        {
            string manifest = Export(Small());
            File.WriteAllText(manifest, File.ReadAllText(manifest)
                .Replace("\"status\": \"complete\", \"path\": \"Color/Test_Map_X001_Y001.png\"", "\"status\": \"pending\", \"path\": \"Color/Test_Map_X001_Y001.png\""));
            GroundExportPackage package = GroundExportPackage.Load(manifest);
            Assert.That(Apply(package, out string message), Is.False);
            Assert.That(Visual.Ground.IsTiled, Is.False);
            Assert.That(Apply(GroundExportPackage.Load(manifest), out message, true), Is.True, message);
            Assert.That(Visual.Ground.Incomplete, Is.True);
            Assert.That(string.Join("\n", LocationGroundLayout.RuntimeErrors(Visual, Location)), Does.Contain("X001_Y001").And.Contain("не завершён"));
        }

        [Test]
        public void InvertedGreenIsFlippedExactlyOnce()
        {
            ushort[] samples = { 128, 200, 230, 255 };
            byte[] png = PngCodec.Encode(1, 1, 8, PngCodec.Rgba, samples);
            Assert.That(GroundImporter.FixNormals(png, false, 0), Is.Null, "без инверсии файл не меняется");
            ushort[] fixedSamples = PngCodec.Decode(GroundImporter.FixNormals(png, true, 0), out _);
            Assert.That(fixedSamples[1], Is.EqualTo(255 - 200).Within(1));
            Assert.That(fixedSamples[0], Is.EqualTo(128).Within(1));
        }

        [Test]
        public void ManualPairingRejectsAmbiguity()
        {
            string manualFolder = Path.Combine(folder, "manual");
            Directory.CreateDirectory(manualFolder);
            GroundSyntheticExport export = Small();
            File.WriteAllBytes(Path.Combine(manualFolder, "Map_X000_Y000.png"), export.ColorPng(0, 0));
            File.WriteAllBytes(Path.Combine(manualFolder, "Map_X000_Y000_height.png"), export.HeightPng(0, 0));
            File.WriteAllBytes(Path.Combine(manualFolder, "Other_X001_Y000.png"), export.ColorPng(1, 0));
            GroundImporter.ManualOptions options = new GroundImporter.ManualOptions { HeightMin = -1, HeightMax = 3, Confirmed = true };
            GroundExportPackage mixed = GroundImporter.FromFiles(Directory.GetFiles(manualFolder), options);
            Assert.That(string.Join("\n", mixed.Errors), Does.Contain("разных карт"));
            File.Delete(Path.Combine(manualFolder, "Other_X001_Y000.png"));
            GroundExportPackage single = GroundImporter.FromFiles(Directory.GetFiles(manualFolder), options);
            Assert.That(single.Errors, Is.Empty, string.Join("\n", single.Errors));
            Assert.That(single.ReadyTiles, Is.EqualTo(1));
            options.Confirmed = false;
            Assert.That(string.Join("\n", GroundImporter.FromFiles(Directory.GetFiles(manualFolder), options).Errors), Does.Contain("Подтвердите"));
        }

        // Земля только в части карты (плоскость: x 2,5…12,5, y 1,25…8,75 при q = 0,125).
        private static GroundSyntheticExport Partial(double originX = 0)
        {
            GroundSyntheticExport export = Small();
            export.Origin = new[] { originX, 0.0 };
            export.Covered = (x, y) => x >= 2.5 && x < 12.5 && y >= 1.25 && y < 8.75;
            return export;
        }

        private HeightSample HeightAt(Vector2 canvas)
        {
            LocationHeightField field = LocationHeightField.Load(Visual.Ground, Location, null);
            field.TrySampleCanvas(canvas, out HeightSample sample, true);
            return sample;
        }

        [Test]
        public void CropByContentRemovesEmptyAndKeepsLayoutOnGround()
        {
            string manifest = Partial().WriteWhole(Path.Combine(folder, "whole"));
            Assert.That(Apply(GroundExportPackage.Load(manifest), out string message), Is.True, message);
            Location.Entrances[0].Point = new LocalPointData(50.5f, 40.5f);
            Visual.Objects[0].Position = new Vector2(32.5f / 128, 48.5f / 96);
            HeightSample before = HeightAt(new Vector2(50.5f, 40.5f));
            Assert.That(before.Valid, Is.True);

            Assert.That(GroundCrop.TryContentBounds(Visual.Ground, Location, out RectInt bounds, out string problem), Is.True, problem);
            // Плоскость x 2,5…12,5 → пиксели 20…100; y 1,25…8,75 → снизу 10…70 → на рисунке сверху 26…86.
            Assert.That(bounds, Is.EqualTo(new RectInt(20, 26, 80, 60)));
            GroundCrop.Plan plan = GroundCrop.Analyze(Visual.Ground, Location, bounds);
            Assert.That(plan.Errors, Is.Empty);
            Assert.That(plan.Canvas, Is.EqualTo(new RectInt(20, 26, 80, 60)));
            Assert.That(new Vector2Int(plan.Columns, plan.Rows), Is.EqualTo(new Vector2Int(2, 2)));
            Assert.That(new Vector2Int(plan.TileWidth, plan.TileHeight), Is.EqualTo(new Vector2Int(40, 30)));
            Assert.That(GroundCrop.Apply(plan, database, locationId, out message), Is.True, message);

            LocationGroundDefinition ground = Visual.Ground;
            Assert.That(ground.Cropped, Is.True);
            Assert.That(new Vector2(Location.CanvasWidth, Location.CanvasHeight), Is.EqualTo(new Vector2(80, 60)));
            Assert.That(LocationGroundLayout.RuntimeErrors(Visual, Location), Is.Empty);
            Assert.That(new Vector2(Location.Entrances[0].Point.X, Location.Entrances[0].Point.Y), Is.EqualTo(new Vector2(30.5f, 14.5f)));
            Assert.That(Vector2.Scale(Visual.Objects[0].Position, new Vector2(80, 60)).x, Is.EqualTo(12.5f).Within(1e-3f));
            HeightSample after = HeightAt(new Vector2(30.5f, 14.5f));
            Assert.That(after.Blender, Is.EqualTo(before.Blender).Within(1e-9), "вход на той же точке земли");
            Assert.That(ground.Origin.x, Is.EqualTo(2.5f).Within(1e-5f));
            Assert.That(ground.Origin.y, Is.EqualTo(1.25f).Within(1e-5f));
            // Вся земля в рамке: углы рамки — покрытые пиксели, пустота убрана.
            Assert.That(HeightAt(new Vector2(.5f, .5f)).Valid, Is.True);
            Assert.That(HeightAt(new Vector2(79.5f, 59.5f)).Valid, Is.True);

            // Переимпорт того же экспорта — обрезка повторяется, раскладка на месте.
            Assert.That(Apply(GroundExportPackage.Load(manifest), out message), Is.True, message);
            Assert.That(message, Does.Contain("Обрезка повторена"));
            Assert.That(new Vector2(Location.CanvasWidth, Location.CanvasHeight), Is.EqualTo(new Vector2(80, 60)));
            Assert.That(new Vector2(Location.Entrances[0].Point.X, Location.Entrances[0].Point.Y), Is.EqualTo(new Vector2(30.5f, 14.5f)));
            Assert.That(HeightAt(new Vector2(30.5f, 14.5f)).Blender, Is.EqualTo(before.Blender).Within(1e-9));

            // Экспорт с другой областью (сдвиг на 8 пикселей) — земля та же, вход на той же точке.
            string shifted = Partial(-1.0).WriteWhole(Path.Combine(folder, "shifted"));
            Assert.That(Apply(GroundExportPackage.Load(shifted), out message), Is.True, message);
            Assert.That(message, Does.Contain("Обрезка повторена"));
            Assert.That(new Vector2(Location.CanvasWidth, Location.CanvasHeight), Is.EqualTo(new Vector2(80, 60)));
            Assert.That(new Vector2(Location.Entrances[0].Point.X, Location.Entrances[0].Point.Y), Is.EqualTo(new Vector2(30.5f, 14.5f)));
            Assert.That(HeightAt(new Vector2(30.5f, 14.5f)).Blender, Is.EqualTo(before.Blender).Within(1e-9));
        }

        // KS Ground Renderer 2: карта целиком тремя файлами — база режет сама.
        [Test]
        public void WholeMapExportImportsLikeTiles()
        {
            GroundSyntheticExport export = Small();
            string manifest = export.WriteWhole(Path.Combine(folder, "whole"));
            GroundExportPackage package = GroundExportPackage.Load(manifest);
            Assert.That(package.Errors, Is.Empty, string.Join("\n", package.Errors));
            Assert.That(package.WholeMap, Is.True);
            Assert.That(package.ColorPainted, Is.False);
            Assert.That(package.ReadyTiles, Is.EqualTo(4));
            Assert.That(Apply(package, out string message), Is.True, message);
            LocationGroundDefinition ground = Visual.Ground;
            Assert.That(ground.Tiles.Count, Is.EqualTo(4));
            Assert.That(ground.IsPainted, Is.False);
            Assert.That(ground.ExportCharacterPx, Is.EqualTo(120));
            Assert.That(LocationGroundLayout.RuntimeErrors(Visual, Location), Is.Empty);
            foreach (LocationGroundTile tile in ground.Tiles)
            {
                Assert.That(tile.Color.rect.size, Is.EqualTo(new Vector2(64, 48)));
                Assert.That(ArtAssets.Editor.SpriteNormalMaps.Find(tile.Color), Is.EqualTo(tile.Normal));
                LocationHeightTileData data = LocationHeightTileData.Deserialize(tile.Height.bytes);
                for (int y = 0; y < data.Height; y += 5)
                    for (int x = 0; x < data.Width; x += 3)
                        Assert.That(data.Values[y * data.Width + x], Is.EqualTo(export.RawHeight(tile.X * 64 + x + .5, tile.Y * 48 + y + .5)), tile.Key + " " + x + "," + y);
                // Тот же участок, что в экспорте участками (schema 1).
                Assert.That(TilePixel(tile, 5, 7, out _), Is.EqualTo(Decoded(export.ColorPng(tile.X, tile.Y), 5, 7)));
            }
            // Повторная проверка того же экспорта — нарезка из кэша, участки «без изменений».
            GroundImporter.Plan again = GroundImporter.Analyze(GroundExportPackage.Load(manifest), Location, Visual);
            Assert.That(again.Actions.Values, Is.All.EqualTo(GroundImporter.TileAction.Unchanged));
        }

        [Test]
        public void WholeMapColorPaintedInPlaceAndChangedHeightRejected()
        {
            GroundSyntheticExport export = Small();
            string manifest = export.WriteWhole(Path.Combine(folder, "whole"));
            // Обрисовка прямо в файле экспорта, ×2.
            File.Copy(PaintedMap(256, 192, 180, "paint.png"), Path.Combine(folder, "whole", "Test_Map_color.png"), true);
            GroundExportPackage package = GroundExportPackage.Load(manifest);
            Assert.That(package.Errors, Is.Empty, string.Join("\n", package.Errors));
            Assert.That(package.ColorPainted, Is.True);
            Assert.That(package.ColorScale, Is.EqualTo(2));
            Assert.That(Apply(package, out string message), Is.True, message);
            LocationGroundDefinition ground = Visual.Ground;
            Assert.That(ground.IsPainted, Is.True);
            Assert.That(ground.ColorScale, Is.EqualTo(2));
            foreach (LocationGroundTile tile in ground.Tiles)
            {
                Assert.That(tile.Color.rect.size, Is.EqualTo(new Vector2(128, 96)));
                Color32 pixel = TilePixel(tile, 0, 0, out _);
                Assert.That(new Vector2Int(pixel.r, pixel.g), Is.EqualTo(new Vector2Int((byte)(tile.X * 128), (byte)((1 - tile.Y) * 96))), tile.Key);
            }
            Assert.That(LocationGroundLayout.RuntimeErrors(Visual, Location), Is.Empty);
            // Изменённая карта высот — отказ с понятной причиной.
            File.WriteAllBytes(Path.Combine(folder, "whole", "Test_Map_height.png"), PngCodec.Encode(128, 96, 16, PngCodec.GrayAlpha, new ushort[128 * 96 * 2]));
            Assert.That(string.Join("\n", GroundExportPackage.Load(manifest).Errors), Does.Contain("Height изменён"));
        }

        private static Color32 Decoded(byte[] png, int x, int y)
        {
            ushort[] samples = PngCodec.Decode(png, out PngCodec.Header header);
            int i = (y * header.Width + x) * header.Channels;
            return new Color32((byte)samples[i], (byte)samples[i + 1], (byte)samples[i + 2], 255);
        }

        // Настоящий экспорт KS Ground Renderer 2 из Blender (папка — в KS_GROUND_V2_EXPORT).
        [Test]
        public void RealBlenderV2ExportImportsWhenPresent()
        {
            string path = Environment.GetEnvironmentVariable("KS_GROUND_V2_EXPORT");
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) Assert.Ignore("Нет экспорта Blender 2.0 (переменная KS_GROUND_V2_EXPORT).");
            GroundExportPackage package = GroundExportPackage.Load(path);
            Assert.That(package.Errors, Is.Empty, string.Join("\n", package.Errors));
            Assert.That(package.ReadyTiles, Is.EqualTo(package.Columns * package.Rows));
            Assert.That(Apply(package, out string message), Is.True, message);
            LocationGroundDefinition ground = Visual.Ground;
            Assert.That(LocationGroundLayout.RuntimeErrors(Visual, Location), Is.Empty);
            LocationHeightField field = LocationHeightField.Load(ground, Location, null);
            int valid = 0;
            for (int i = 0; i < 400; i++)
            {
                Vector2 pixel = new Vector2((i * 7919 % 1000) / 1000f * Location.CanvasWidth, (i * 104729 % 1000) / 1000f * Location.CanvasHeight);
                if (field.TrySampleCanvas(pixel, out HeightSample sample) && sample.Valid)
                {
                    Assert.That(sample.Blender, Is.InRange(ground.HeightMin - 1e-6, ground.HeightMax + 1e-6));
                    valid++;
                }
            }
            Assert.That(valid, Is.GreaterThan(50), "на карте есть земля с высотой");
            TestContext.WriteLine("Экспорт Blender 2: " + package.MapWidth + "×" + package.MapHeight + ", участков " + ground.Tiles.Count +
                                  ", рост эталона " + ground.ExportCharacterPx.ToString("0") + " px, точек с высотой " + valid + "/400");

            // Обрезка пустоты по земле.
            Assert.That(GroundCrop.TryContentBounds(ground, Location, out RectInt bounds, out string problem), Is.True, problem);
            GroundCrop.Plan plan = GroundCrop.Analyze(ground, Location, bounds);
            Assert.That(plan.Errors, Is.Empty, string.Join("\n", plan.Errors));
            string summary = plan.Summary(new Vector2(Location.CanvasWidth, Location.CanvasHeight));
            Assert.That(GroundCrop.Apply(plan, database, locationId, out message), Is.True, message);
            Assert.That(LocationGroundLayout.RuntimeErrors(Visual, Location), Is.Empty);
            Assert.That(Location.CanvasWidth * Location.CanvasHeight, Is.LessThan(package.MapWidth * package.MapHeight));
            TestContext.WriteLine("Обрезка: " + summary);
        }

        // Обрисованная карта целиком: пиксель (x, y сверху) = (x mod 256, y mod 256, mark).
        private string PaintedMap(int width, int height, byte mark, string name, Func<int, int, bool> marked = null)
        {
            PngCodec.RowWriter writer = new PngCodec.RowWriter(width, height);
            byte[] row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    row[x * 4] = (byte)x; row[x * 4 + 1] = (byte)y;
                    row[x * 4 + 2] = marked == null || marked(x, y) ? mark : (byte)7; row[x * 4 + 3] = 255;
                }
                writer.WriteRow(row, 0);
            }
            string path = Path.Combine(folder, name);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, writer.Finish());
            return path;
        }

        private static Color32 TilePixel(LocationGroundTile tile, int x, int yTop, out PngCodec.Header header)
        {
            ushort[] samples = PngCodec.Decode(File.ReadAllBytes(AssetDatabase.GetAssetPath(tile.Color.texture)), out header);
            int i = (yTop * header.Width + x) * header.Channels;
            return new Color32((byte)samples[i], (byte)samples[i + 1], (byte)samples[i + 2], 255);
        }

        [Test]
        public void PaintOverWholeMapIsSlicedOnGridAndKept()
        {
            string manifest = Export(Small());
            Assert.That(Apply(GroundExportPackage.Load(manifest), out string message), Is.True, message);
            LocationGroundDefinition ground = Visual.Ground;
            string renderPath = AssetDatabase.GetAssetPath(ground.Find(1, 0).Color.texture);

            // Неверные размеры — понятный отказ.
            Assert.That(string.Join("\n", GroundPaintOver.Analyze(PaintedMap(130, 96, 1, "bad.png"), ground).Errors), Does.Contain("не кратен"));
            Assert.That(string.Join("\n", GroundPaintOver.Analyze(PaintedMap(64, 48, 1, "small.png"), ground).Errors), Does.Contain("меньше рендера"));

            // ×2: участки 128×96 на тех же прямоугольниках; (i, j) — снизу слева.
            GroundPaintOver.Plan plan = GroundPaintOver.Analyze(PaintedMap(256, 192, 200, "paint.png"), ground);
            Assert.That(plan.Errors, Is.Empty, string.Join("\n", plan.Errors));
            Assert.That(plan.Scale, Is.EqualTo(2));
            Assert.That(GroundPaintOver.Apply(plan, database, locationId, out message), Is.True, message);
            Assert.That(ground.IsPainted, Is.True);
            Assert.That(ground.ColorScale, Is.EqualTo(2));
            foreach (LocationGroundTile tile in ground.Tiles)
            {
                Assert.That(tile.Color.rect.size, Is.EqualTo(new Vector2(128, 96)));
                Assert.That(ArtAssets.Editor.SpriteNormalMaps.Find(tile.Color), Is.EqualTo(tile.Normal), "нормаль экспорта подключена к обрисовке");
                Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(tile.RenderColorGuid)), Is.Not.Null, "рендер сохранён");
                int left = tile.X * 128, top = (1 - tile.Y) * 96;
                foreach ((int x, int y) in new[] { (0, 0), (127, 0), (0, 95), (127, 95) })
                {
                    Color32 pixel = TilePixel(tile, x, y, out _);
                    Assert.That(new Vector2Int(pixel.r, pixel.g), Is.EqualTo(new Vector2Int((byte)(left + x), (byte)(top + y))), tile.Key + " " + x + "," + y);
                }
            }
            Assert.That(LocationGroundLayout.RuntimeErrors(Visual, Location), Is.Empty);

            // Тот же файл — без изменений; правка внутри X001_Y000 — меняется только он.
            Assert.That(GroundPaintOver.Apply(GroundPaintOver.Analyze(Path.Combine(folder, "paint.png"), ground), database, locationId, out message), Is.True);
            Assert.That(message, Does.Contain("без изменений"));
            Dictionary<string, string> before = ground.Tiles.ToDictionary(tile => tile.Key, tile => tile.PaintSha256);
            string again = PaintedMap(256, 192, 200, "paint2.png", (x, y) => !(x >= 150 && x < 160 && y >= 120 && y < 130));
            Assert.That(GroundPaintOver.Apply(GroundPaintOver.Analyze(again, ground), database, locationId, out message), Is.True, message);
            foreach (LocationGroundTile tile in ground.Tiles)
                Assert.That(tile.PaintSha256 != before[tile.Key], Is.EqualTo(tile.Key == "X001_Y000"), tile.Key);

            // Сшивание: карта целиком из текущих участков — та же, что обрисовка.
            string whole = Path.Combine(folder, "whole.png");
            Assert.That(GroundPaintOver.ExportWhole(ground, whole, out message), Is.True, message);
            Assert.That(PngCodec.Decode(File.ReadAllBytes(whole), out PngCodec.Header wholeHeader), Is.EqualTo(PngCodec.Decode(File.ReadAllBytes(again), out _)));
            Assert.That(wholeHeader.Width, Is.EqualTo(256));

            // Переимпорт экспорта с новым рендером X001_Y000: обрисовка остаётся.
            Sprite painted = ground.Find(1, 0).Color;
            string color = Path.Combine(folder, "a", "Color", "Test_Map_X001_Y000.png");
            string oldSha = GroundExportPackage.Sha256Hex(File.ReadAllBytes(color));
            byte[] render = PngCodec.Encode(64, 48, 8, PngCodec.Rgba, Enumerable.Repeat((ushort)90, 64 * 48 * 4).ToArray());
            File.WriteAllBytes(color, render);
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(oldSha, GroundExportPackage.Sha256Hex(render)));
            Assert.That(Apply(GroundExportPackage.Load(manifest), out message), Is.True, message);
            Assert.That(message, Does.Contain("Рендер Color изменился"));
            Assert.That(ground.Find(1, 0).Color, Is.SameAs(painted));
            Assert.That(AssetDatabase.GUIDToAssetPath(ground.Find(1, 0).RenderColorGuid), Is.Not.EqualTo(renderPath));

            // Возврат к рендеру.
            Assert.That(GroundPaintOver.RevertToRender(database, locationId, out message), Is.True, message);
            Assert.That(ground.IsPainted, Is.False);
            Assert.That(ground.ColorScale, Is.EqualTo(1));
            Assert.That(ground.Find(1, 0).Color.rect.size, Is.EqualTo(new Vector2(64, 48)));
            Assert.That(TilePixel(ground.Find(1, 0), 3, 3, out _).r, Is.EqualTo(90));
            Assert.That(Directory.GetFiles(GroundImporter.Root + "/" + locationId, "*_paint__*.png", SearchOption.AllDirectories), Is.Empty);
        }

        // Демо 4×4 (меню «Технические») и вкладка «Земля» окна базы — на
        // временной копии базы: настоящая база места не меняется.
        [Test]
        public void DemoImportsFourByFourAndGroundTabBuilds()
        {
            string demoFolder = GroundImporter.Root + "/" + GroundDemo.LocationId;
            bool existed = AssetDatabase.IsValidFolder(demoFolder);
            LocalLocationDatabaseAsset temp = ScriptableObject.CreateInstance<LocalLocationDatabaseAsset>();
            LocalLocationDatabaseAsset real = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            temp.locations.Add(real.locations.Find(item => item.Id == LocationLightingTestBootstrap.CampId));
            temp.visuals.Add(real.FindVisual(LocationLightingTestBootstrap.CampId));
            LocationDatabaseWindow window = null;
            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            try
            {
                Assert.That(GroundDemo.Create(temp, out string message), Is.True, message);
                LocalLocationDefinition location = temp.locations.Find(item => item.Id == GroundDemo.LocationId);
                LocationVisualDefinition visual = temp.FindVisual(GroundDemo.LocationId);
                Assert.That(visual.Ground.Tiles.Count, Is.EqualTo(16));
                Assert.That(location.CanvasWidth, Is.EqualTo(2560)); Assert.That(location.CanvasHeight, Is.EqualTo(1440));
                BattlefieldDefinitionData field = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath).FindById(location.BattlefieldId);
                Assert.That(LocationVisualGeometry.Validate(location, visual, field), Is.Empty, "демо можно запустить кнопкой «Запустить локацию»");
                // Повторный вызов — переимпорт без дублей.
                Assert.That(GroundDemo.Create(temp, out message), Is.True, message);
                Assert.That(temp.locations.Count(item => item.Id == GroundDemo.LocationId), Is.EqualTo(1));
                Assert.That(visual.Ground.Tiles.Count, Is.EqualTo(16));

                window = EditorWindow.GetWindow<LocationDatabaseWindow>();
                window.CreateGUI();
                typeof(LocationDatabaseWindow).GetField("database", flags).SetValue(window, temp);
                typeof(LocationDatabaseWindow).GetField("selectedId", flags).SetValue(window, GroundDemo.LocationId);
                System.Reflection.FieldInfo tab = typeof(LocationDatabaseWindow).GetField("tab", flags);
                tab.SetValue(window, Enum.Parse(tab.FieldType, "Ground"));
                typeof(LocationDatabaseWindow).GetMethod("RebuildPreview", flags).Invoke(window, null);
                typeof(LocationDatabaseWindow).GetMethod("BuildSettings", flags).Invoke(window, null);
                ScrollView settings = (ScrollView)typeof(LocationDatabaseWindow).GetField("settings", flags).GetValue(window);
                List<string> texts = settings.Query<TextElement>().ToList().Select(item => item.text).ToList();
                Assert.That(texts, Has.Some.EqualTo("Импортировать экспорт Blender…"));
                Assert.That(texts, Has.Some.EqualTo("Карта высот"));
                Assert.That(texts, Has.Some.EqualTo("Камера"));
                Assert.That(texts, Has.Some.EqualTo("Проверка"));
                // Предпросмотр Normal / Height / Color.
                System.Reflection.FieldInfo view = typeof(LocationDatabaseWindow).GetField("groundView", flags);
                LocationWorldRenderer renderer = (LocationWorldRenderer)typeof(LocationDatabaseWindow).GetField("renderer", flags).GetValue(window);
                Assert.That(renderer.Height.LoadedTiles, Is.EqualTo(16));
                Sprite color = renderer.GroundRenderer(0, 0).sprite;
                foreach (string mode in new[] { "Normal", "Height", "Color" })
                {
                    view.SetValue(window, Enum.Parse(view.FieldType, mode));
                    typeof(LocationDatabaseWindow).GetMethod("ApplyGroundView", flags).Invoke(window, null);
                    Assert.That(renderer.GroundRenderer(0, 0).sprite == color, Is.EqualTo(mode == "Color"), mode);
                }
                // Проверка пакета в окне: тот же экспорт — «без изменений».
                typeof(LocationDatabaseWindow).GetMethod("LoadPackage", flags).Invoke(window, new object[] { Path.GetFullPath(GroundDemo.ExportFolder) });
                texts = settings.Query<TextElement>().ToList().Select(item => item.text).ToList();
                Assert.That(texts, Has.Some.EqualTo("Импорт: проверка пакета"));
                Assert.That(texts, Has.Some.Contains("без изменений"));
                List<string> check = (List<string>)typeof(LocationDatabaseWindow).GetMethod("CheckGround", flags).Invoke(window, new object[] { location });
                Assert.That(check.First(), Does.StartWith("✓"), string.Join("\n", check));
            }
            finally
            {
                if (window != null)
                {
                    typeof(LocationDatabaseWindow).GetField("database", flags).SetValue(window, real);
                    typeof(LocationDatabaseWindow).GetField("selectedId", flags).SetValue(window, LocationLightingTestBootstrap.CampId);
                    window.Close();
                }
                if (!existed) AssetDatabase.DeleteAsset(demoFolder);
                UnityEngine.Object.DestroyImmediate(temp);
            }
        }

        // Независимая сверка: декодер Unity (ImageConversion) читает тот же PNG.
        // 16 бит у Unity — RGBA64: значения и покрытие совпадают точно; строки
        // текстуры Unity снизу вверх — как в данных высоты.
        private static void CrossCheckWithUnityDecoder(byte[] png, LocationHeightTileData data, string label)
        {
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA64, false, true);
            try
            {
                Assert.That(texture.LoadImage(png, false), Is.True, label);
                Assert.That(texture.width, Is.EqualTo(data.Width));
                if (texture.format != TextureFormat.RGBA64)
                {
                    // Unity 6000.5 читает 16-битный PNG в 8 бит (ARGB32) — младшие
                    // биты высоты теряются; поэтому нужен свой декодер. Сверка —
                    // на уровне старших 8 бит (±1 на округление).
                    Color32[] colors = texture.GetPixels32();
                    for (int i = 0; i < data.Width * data.Height; i++)
                    {
                        Assert.That(Mathf.Abs(colors[i].a - data.Coverage[i]), Is.LessThanOrEqualTo(1), label + " покрытие пикселя " + i);
                        if (data.Coverage[i] > 1)
                            Assert.That(Mathf.Abs(colors[i].r - data.Values[i] * 255f / (data.BitDepth == 16 ? 65535 : 255)), Is.LessThanOrEqualTo(1.01f), label + " высота пикселя " + i);
                    }
                    return;
                }
                Unity.Collections.NativeArray<ushort> pixels = texture.GetPixelData<ushort>(0);
                for (int i = 0; i < data.Width * data.Height; i++)
                {
                    Assert.That(pixels[i * 4 + 3] > 0, Is.EqualTo(data.Coverage[i] > 0), label + " покрытие пикселя " + i);
                    if (data.Coverage[i] > 0) Assert.That(pixels[i * 4], Is.EqualTo(data.Values[i]), label + " высота пикселя " + i);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // Настоящие экспорты KS Ground Renderer 1.0.0 (приёмка аддона), если они есть на этом компьютере.
        [Test]
        public void RealBlenderExportsLoadWhenPresent()
        {
            const string root = "E:/разработка игры/KS_Ground_Renderer_Work/acceptance_output";
            if (!Directory.Exists(root)) Assert.Ignore("Нет папки приёмки аддона: " + root);
            int checkedCount = 0, complete = 0, unfinished = 0;
            foreach (string map in new[] { "Grid_2x2", "Grid_4x4", "Rectangle", "Cancel_test", "Error_test" })
            {
                string mapFolder = Path.Combine(root, map);
                if (!Directory.Exists(mapFolder)) continue;
                foreach (string manifest in Directory.GetFiles(mapFolder, "*_manifest.json", SearchOption.AllDirectories))
                {
                    GroundExportPackage package = GroundExportPackage.Load(manifest);
                    Assert.That(package.Errors, Is.Empty, manifest + "\n" + string.Join("\n", package.Errors));
                    if (package.Status == "complete")
                    {
                        Assert.That(package.ReadyTiles, Is.EqualTo(package.Columns * package.Rows), manifest);
                        Assert.That(package.Partial, Is.False, manifest);
                        complete++;
                    }
                    else
                    {
                        // Отменённый экспорт или ошибка: ничего не выдаётся за готовое.
                        Assert.That(package.Partial, Is.True, manifest);
                        Assert.That(string.Join("\n", package.Warnings), Does.Contain("Статус экспорта"), manifest);
                        unfinished++;
                    }
                    int covered = 0;
                    foreach (GroundExportTile tile in package.Tiles.Where(t => t.Usable(GroundExportPackage.Height)))
                    {
                        LocationHeightTileData data = LocationHeightTileData.FromPng(File.ReadAllBytes(tile.Pass(GroundExportPackage.Height).FullPath), package.HeightMin, package.HeightMax);
                        Assert.That(data.Width, Is.EqualTo(package.TileWidth));
                        covered += data.Coverage.Count(c => c > 0);
                        CrossCheckWithUnityDecoder(File.ReadAllBytes(tile.Pass(GroundExportPackage.Height).FullPath), data, tile.Key + " · " + manifest);
                    }
                    TestContext.WriteLine(Path.GetFileName(manifest) + ": покрытых пикселей высоты " + covered);
                    checkedCount++;
                }
            }
            Assert.That(checkedCount, Is.GreaterThan(0));
            Assert.That(complete, Is.GreaterThan(0));
            TestContext.WriteLine("Экспортов проверено: " + checkedCount + ", завершённых " + complete + ", незавершённых " + unfinished);
        }
    }
}
