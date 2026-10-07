using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering
{
    // ПР-12Р: кисть раскидки — плотность и наименьшее расстояние, маска
    // местности, веса ассетов, разброс облика в заданных пределах, ластик,
    // перекраска, заливка; экземпляры как предметы (слой, проходимость),
    // совместимость прежних данных; рендерер дополняет слой по мазку,
    // ковёр виден в предпросмотре окна; вкладка «Раскидка» открывается.
    public sealed class LocationScatterTests
    {
        private readonly List<Object> owned = new List<Object>();
        private ArtAssetDatabaseAsset catalog;

        [SetUp]
        public void SetUp()
        {
            catalog = ScriptableObject.CreateInstance<ArtAssetDatabaseAsset>();
            owned.Add(catalog);
            ArtAssetDatabaseAsset.Override = catalog;
        }

        [TearDown]
        public void TearDown()
        {
            ArtAssetDatabaseAsset.Override = null;
            foreach (Object item in owned) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        private Sprite Sprite(Color color, int width = 24, int height = 40)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] colors = new Color[width * height];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;
            texture.SetPixels(colors);
            texture.Apply();
            owned.Add(texture);
            Sprite sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, 0), 100);
            owned.Add(sprite);
            return sprite;
        }

        private ArtAssetDefinition Asset(string id, bool blocks = false, params ArtAssetView[] views)
        {
            ArtAssetDefinition asset = new ArtAssetDefinition { Id = id, Name = id, PixelsPerUnit = 40, BlocksMovement = blocks };
            if (views.Length == 0) views = new[] { ArtAssetView.Front };
            foreach (ArtAssetView view in views)
            {
                asset.MainPart.View(view).Sprite = Sprite(new Color(.3f, .7f, .2f));
                asset.Settings(view).FootprintSize = new Vector2(.4f, .2f);
            }
            catalog.assets.Add(asset);
            catalog.MarkChanged();
            return asset;
        }

        private static LocalLocationDefinition Location() =>
            new LocalLocationDefinition { Id = "zz_scatter", DisplayName = "Раскидка", CanvasWidth = 1920, CanvasHeight = 1080 };

        private static LocationScatterLayer Layer(params string[] assets)
        {
            LocationScatterLayer layer = new LocationScatterLayer { Name = "Трава" };
            foreach (string id in assets) layer.Assets.Add(new LocationScatterEntry { AssetId = id });
            layer.Brush.Falloff = 0;
            return layer;
        }

        private static float MinDistance(LocationScatterLayer layer, LocalLocationDefinition location)
        {
            List<Vector2> points = layer.Instances.Select(item => LocationVisualGeometry.ToPixel(location, item.Position)).ToList();
            float best = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
                for (int j = i + 1; j < points.Count; j++)
                    best = Mathf.Min(best, Vector2.Distance(points[i], points[j]));
            return best;
        }

        [Test]
        public void Stamp_FillsCircleToDensity_KeepsMinDistance_AndDoesNotOverfill()
        {
            Asset("grass");
            LocalLocationDefinition location = Location();
            LocationScatterLayer layer = Layer("grass");
            layer.Brush.Radius = 200;
            layer.Brush.Density = 6;
            layer.Brush.MinDistance = 12;
            LocationScatterIndex index = LocationScatterIndex.Of(layer, location);
            System.Random random = new System.Random(7);
            Vector2 center = new Vector2(960, 540);
            float target = 6 / LocationScatterPainter.DensityArea * Mathf.PI * 200 * 200;
            LocationScatterPainter.Stamp(layer, location, center, random, null, index);
            Assert.That(layer.Instances.Count, Is.InRange(target * .85f, target + 1), "Мазок досыпает до плотности (~" + target + ").");
            Assert.That(MinDistance(layer, location), Is.GreaterThanOrEqualTo(12 - .01f));
            int first = layer.Instances.Count;
            for (int i = 0; i < 5; i++) LocationScatterPainter.Stamp(layer, location, center, random, null, index);
            Assert.That(layer.Instances.Count, Is.LessThanOrEqualTo(first + 3), "Повторный мазок по заполненному месту сверх плотности не добавляет.");
            Assert.That(layer.Instances.Select(item => item.Key).Distinct().Count(), Is.EqualTo(layer.Instances.Count), "Ключи экземпляров уникальны.");
            Assert.That(layer.Instances.All(item => (LocationVisualGeometry.ToPixel(location, item.Position) - center).magnitude <= 200.01f));
        }

        [Test]
        public void Mask_LimitsToTerrainTypes_PassableAndAwayFromObjects()
        {
            Asset("grass");
            ArtAssetDefinition rock = Asset("rock", true);
            rock.Settings(ArtAssetView.Front).FootprintSize = new Vector2(3, 3);
            LocalLocationDefinition location = Location();
            // Левая половина — лес, правая — вода.
            WorldMapTerrainLayer terrain = new WorldMapTerrainLayer(location.CreateGrid());
            for (int i = 0; i < terrain.Grid.CellCount; i++)
            {
                WorldMapHexCell cell = terrain.Grid.CellAt(i);
                terrain.Grid.CellCenter(cell, out double x, out _);
                terrain.Set(cell, x < 960 ? WorldMapGameplayTerrainType.Forest : WorldMapGameplayTerrainType.Water);
            }
            location.TerrainCells = terrain.Encode();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id };
            visual.Objects.Add(new LocationVisualObject { Id = "big_rock", AssetId = "rock", Position = new Vector2(.25f, .5f) });

            LocationScatterLayer layer = Layer("grass");
            layer.Brush.Density = 4;
            layer.Brush.MinDistance = 4;
            layer.Brush.Terrain.Add(WorldMapGameplayTerrainType.Forest);
            System.Func<Vector2, bool> mask = LocationScatterPainter.Mask(layer.Brush, location, visual);
            LocationScatterPainter.Fill(layer, location, new System.Random(3), mask, LocationScatterIndex.Of(layer, location));
            Assert.That(layer.Instances.Count, Is.GreaterThan(100));
            Assert.That(layer.Instances.All(item => item.Position.x < .52f), "Только на лесе (слева).");
            Rect footprint = LocationVisualGeometry.FootprintRect(location, visual.Objects[0], LocationVisualResolver.Resolve(visual.Objects[0]));
            Assert.That(layer.Instances.Any(item => footprint.Contains(LocationVisualGeometry.ToPixel(location, item.Position))), Is.False,
                "Не на основании камня.");

            LocationScatterLayer passable = Layer("grass");
            passable.Brush.OnlyPassable = true;
            passable.Brush.AvoidObjects = false;
            mask = LocationScatterPainter.Mask(passable.Brush, location, visual);
            Assert.That(mask(new Vector2(400, 540)), Is.True);
            Assert.That(mask(new Vector2(1500, 540)), Is.False, "Вода непроходима.");
            Assert.That(mask(new Vector2(-5, 540)), Is.False, "За краем рисунка — нельзя.");
        }

        [Test]
        public void Assets_ArePickedByWeight_LookStaysWithinBrushRanges()
        {
            Asset("a", false, ArtAssetView.Front, ArtAssetView.Back);
            Asset("b");
            LocalLocationDefinition location = Location();
            LocationScatterLayer layer = Layer("a", "b");
            layer.Assets[0].Weight = 3;
            layer.Assets[1].ScaleMultiplier = 2;
            layer.Assets.Add(new LocationScatterEntry { AssetId = "missing", Weight = 100 });
            layer.Assets.Add(new LocationScatterEntry { AssetId = "b", Weight = 100, Enabled = false });
            LocationScatterBrush brush = layer.Brush;
            brush.Density = 20;
            brush.MinDistance = 0;
            brush.Scale = new Vector2(.5f, .8f);
            brush.Stretch = new Vector2(.7f, 1.4f);
            brush.Rotation = new Vector2(-10, 15);
            brush.HueShift = 20;
            brush.HueJitter = 5;
            brush.Saturation = new Vector2(.4f, .6f);
            brush.Brightness = new Vector2(1.2f, 1.3f);
            brush.TintA = Color.red;
            brush.TintB = Color.blue;
            brush.Flip = LocationScatterFlip.Always;
            brush.ViewMode = LocationScatterViewMode.Random;
            LocationScatterPainter.Fill(layer, location, new System.Random(11), null, null, limit: 2000);
            Assert.That(layer.Instances.Count, Is.EqualTo(2000));
            float shareA = layer.Instances.Count(item => item.AssetId == "a") / 2000f;
            Assert.That(shareA, Is.InRange(.7f, .8f), "Вес 3 к 1; отсутствующий и выключенный ассет не выпадают.");
            foreach (LocationScatterInstance item in layer.Instances)
            {
                float multiplier = item.AssetId == "b" ? 2 : 1;
                Assert.That(item.Scale, Is.InRange(.5f * multiplier - .001f, .8f * multiplier + .001f));
                Assert.That(item.Stretch, Is.InRange(.7f - .001f, 1.4f + .001f));
                Assert.That(item.Rotation, Is.InRange(-10.001f, 15.001f));
                Assert.That(item.Hue, Is.InRange(15 - .001f, 25 + .001f));
                Assert.That(item.Saturation, Is.InRange(.4f - .001f, .6f + .001f));
                Assert.That(item.Brightness, Is.InRange(1.2f - .001f, 1.3f + .001f));
                Assert.That(item.Tint.g, Is.LessThan(.01f), "Подкраска — между красным и синим.");
                Assert.That(item.FlipX, Is.True);
                Assert.That(item.AssetId == "a" ? item.View == ArtAssetView.Front || item.View == ArtAssetView.Back : item.View == ArtAssetView.Front, Is.True,
                    "Случайный ракурс — только из имеющихся у ассета.");
            }
            Assert.That(layer.Instances.Select(item => item.View).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void Erase_AndRecolor_WorkInsideCircle_OnlyBrushAssetsFilter()
        {
            Asset("a");
            Asset("b");
            LocalLocationDefinition location = Location();
            LocationScatterLayer layer = Layer("a");
            layer.Brush.Density = 10;
            layer.Brush.MinDistance = 0;
            LocationScatterPainter.Fill(layer, location, new System.Random(5), null, null, limit: 400);
            foreach (LocationScatterInstance item in layer.Instances.Where((item, i) => i % 2 == 0)) item.AssetId = "b";
            Vector2 center = new Vector2(960, 540);
            layer.Brush.Radius = 300;
            bool Inside(LocationScatterInstance item) => (LocationVisualGeometry.ToPixel(location, item.Position) - center).magnitude <= 300;
            int insideB = layer.Instances.Count(item => Inside(item) && item.AssetId == "b");

            layer.Brush.OnlyBrushAssets = true;
            Dictionary<int, Vector2> positions = layer.Instances.ToDictionary(item => item.Key, item => item.Position);
            layer.Brush.Scale = new Vector2(3, 3);
            List<int> recolored = LocationScatterPainter.Recolor(layer, location, center, new System.Random(1));
            Assert.That(recolored.Count, Is.EqualTo(layer.Instances.Count(item => Inside(item) && item.AssetId == "a")));
            Assert.That(layer.Instances.Where(item => recolored.Contains(item.Key)).All(item => item.Scale == 3 && item.Position == positions[item.Key]),
                "Перекраска меняет облик, не положение.");
            Assert.That(LocationScatterPainter.Recolor(layer, location, center, new System.Random(1), null, recolored), Is.Empty, "Уже перекрашенные этим мазком пропускаются.");

            List<int> erased = LocationScatterPainter.Erase(layer, location, center);
            Assert.That(layer.Instances.Any(item => Inside(item) && item.AssetId == "a"), Is.False);
            Assert.That(layer.Instances.Count(item => Inside(item) && item.AssetId == "b"), Is.EqualTo(insideB), "Только ассеты кисти.");
            layer.Brush.OnlyBrushAssets = false;
            LocationScatterPainter.Erase(layer, location, center);
            Assert.That(layer.Instances.Any(Inside), Is.False);
            Assert.That(erased.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Instance_AsObject_TakesLayerBandAndPassability_AndBlocksOnlyWhenAsked()
        {
            Asset("rock", true);
            LocalLocationDefinition location = Location();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id };
            LocationScatterLayer layer = Layer("rock");
            layer.Band = LocationVisualBand.GroundDetail;
            layer.Instances.Add(new LocationScatterInstance { Key = 1, AssetId = "rock", Position = new Vector2(.5f, .5f), Stretch = 2, Scale = 1 });
            visual.ScatterLayers.Add(layer);
            LocationVisualObject item = layer.ToObject(layer.Instances[0]);
            Assert.That(item.Id, Is.EqualTo(layer.Id + "#1"));
            LocationResolvedVisual resolved = LocationVisualResolver.Resolve(item);
            Assert.That(resolved.Main.Band, Is.EqualTo(LocationVisualBand.GroundDetail));
            Assert.That(resolved.BlocksMovement, Is.False, "По умолчанию раскидка проходу не мешает.");
            Assert.That(LocationVisualGeometry.BlockedAreas(visual, location), Is.Empty);
            layer.BlocksMovement = true;
            List<Rect> blocked = LocationVisualGeometry.BlockedAreas(visual, location);
            Assert.That(blocked.Count, Is.EqualTo(1), "«Как у ассета»: камень мешает.");
            Assert.That(blocked[0].width, Is.EqualTo(.4f * 2 * LocationVisualGeometry.PixelsPerUnit).Within(.01f), "Растянутый камень занимает землю шире.");
            layer.Mode = LocationScatterMode.Carpet;
            Assert.That(LocationVisualGeometry.BlockedAreas(visual, location), Is.Empty, "Ковёр проходу не мешает.");
        }

        [Test]
        public void OldData_WithoutNewFields_ReadsAsPlainLook()
        {
            LocationVisualObject item = JsonUtility.FromJson<LocationVisualObject>("{\"Id\":\"old\",\"Name\":\"Старый\",\"Scale\":1}");
            Assert.That(item.Stretch, Is.EqualTo(1));
            Assert.That(item.Rotation, Is.EqualTo(0));
            Assert.That(item.ColorAdjust, Is.Not.Null);
            Assert.That(item.ColorAdjust.IsIdentity, Is.True);
            LocationVisualDefinition visual = JsonUtility.FromJson<LocationVisualDefinition>("{\"LocationId\":\"old\"}");
            Assert.That(visual.ScatterLayers, Is.Not.Null.And.Empty);
            LocationScatterInstance instance = JsonUtility.FromJson<LocationScatterInstance>("{\"Key\":3,\"AssetId\":\"x\"}");
            Assert.That(instance.ColorAdjust.IsIdentity, Is.True);
            Assert.That(instance.Scale, Is.EqualTo(1));
            Assert.That(new LocationColorAdjust { Hue = 360 }.HasHsv, Is.False, "Поворот тона на полный круг — без изменений.");
        }

        [Test]
        public void Renderer_FollowsStrokes_ObjectsAndCarpet()
        {
            Asset("grass");
            LocalLocationDefinition location = Location();
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
            LocationScatterLayer objects = Layer("grass");
            objects.Brush.Radius = 150;
            objects.Brush.Density = 5;
            LocationScatterLayer carpet = Layer("grass");
            carpet.Mode = LocationScatterMode.Carpet;
            carpet.Brush.Radius = 150;
            carpet.Brush.Density = 20;
            carpet.Brush.MinDistance = 4;
            visual.ScatterLayers.Add(objects);
            visual.ScatterLayers.Add(carpet);
            LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null);
            try
            {
                System.Random random = new System.Random(2);
                LocationScatterPainter.Stamp(objects, location, new Vector2(500, 500), random, null, LocationScatterIndex.Of(objects, location));
                renderer.RefreshScatter(objects);
                Assert.That(renderer.ScatterObjectCount(objects.Id), Is.EqualTo(objects.Instances.Count).And.GreaterThan(0));

                LocationScatterInstance first = objects.Instances[0];
                first.Rotation = 30;
                first.Stretch = 2;
                first.Hue = 90;
                renderer.RefreshScatter(objects, new[] { first.Key });
                SpriteRenderer image = renderer.ScatterImages(objects.Id, first.Key)[0];
                Assert.That(image.transform.localEulerAngles.z, Is.EqualTo(30).Within(.01f), "Поворот вокруг опоры.");
                Assert.That(image.transform.localScale.x / image.transform.localScale.y, Is.EqualTo(2).Within(.001f), "Растяжение по ширине.");
                Assert.That(image.sharedMaterial.shader.name, Is.EqualTo("Kingdom Survival/Location Sprite Adjust"), "Тон — материалом с поправкой цвета.");

                LocationScatterPainter.Erase(objects, location, new Vector2(500, 500));
                renderer.RefreshScatter(objects);
                Assert.That(renderer.ScatterObjectCount(objects.Id), Is.EqualTo(0));

                LocationScatterPainter.Stamp(carpet, location, new Vector2(1200, 600), random, null, LocationScatterIndex.Of(carpet, location));
                renderer.RefreshScatter(carpet);
                Assert.That(renderer.ScatterCarpetCount(carpet.Id), Is.EqualTo(carpet.Instances.Count).And.GreaterThan(50));
                Assert.That(renderer.ScatterObjectCount(carpet.Id), Is.EqualTo(0), "Ковёр — без объектов.");

                carpet.Hidden = true;
                renderer.RefreshScatter(carpet);
                Assert.That(renderer.ScatterCarpetCount(carpet.Id), Is.EqualTo(0));
            }
            finally
            {
                renderer.Dispose();
            }
        }

        // Окно базы рисует сцену через PreviewRenderUtility — ковёр
        // должен быть виден и там, а не только в игре.
        [Test]
        public void Carpet_IsVisibleInEditorPreview()
        {
            Asset("grass");
            float Render(bool withCarpet)
            {
                LocalLocationDefinition location = Location();
                LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
                if (withCarpet)
                {
                    LocationScatterLayer layer = Layer("grass");
                    layer.Mode = LocationScatterMode.Carpet;
                    layer.Brush.Density = 20;
                    layer.Brush.MinDistance = 6;
                    LocationScatterPainter.Fill(layer, location, new System.Random(4), null, null);
                    foreach (LocationScatterInstance item in layer.Instances) item.Tint = Color.white;
                    visual.ScatterLayers.Add(layer);
                }
                LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null);
                renderer.Camera.enabled = false;
                PreviewRenderUtility preview = new PreviewRenderUtility(true);
                try
                {
                    preview.AddSingleGO(renderer.Root);
                    preview.camera.orthographic = true;
                    UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(preview.camera).SetRenderer(0);
                    preview.camera.clearFlags = CameraClearFlags.SolidColor;
                    preview.camera.backgroundColor = Color.black;
                    preview.camera.transform.position = new Vector3(0, 0, -10);
                    preview.camera.orthographicSize = 5;
                    renderer.SetTime(13, 0);
                    preview.BeginStaticPreview(new Rect(0, 0, 200, 120));
                    preview.Render(true);
                    Texture2D image = preview.EndStaticPreview();
                    float green = 0;
                    foreach (Color color in image.GetPixels()) green += color.g - color.r;
                    Object.DestroyImmediate(image);
                    return green / (200 * 120);
                }
                finally
                {
                    preview.Cleanup();
                    renderer.Dispose();
                }
            }
            float empty = Render(false), filled = Render(true);
            Assert.That(filled, Is.GreaterThan(empty + .05f), "Трава ковром видна в предпросмотре окна: " + empty + " → " + filled);
        }

        [Test]
        public void ScatterTab_OpensInLocationWindow()
        {
            LocationDatabaseWindow window = EditorWindow.GetWindow<LocationDatabaseWindow>();
            try
            {
                window.CreateGUI();
                System.Type type = typeof(LocationDatabaseWindow);
                FieldInfo tab = type.GetField("tab", BindingFlags.Instance | BindingFlags.NonPublic);
                tab.SetValue(window, System.Enum.Parse(tab.FieldType, "Scatter"));
                type.GetMethod("BuildSettings", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                List<string> texts = window.rootVisualElement.Query<TextElement>().ToList().Select(item => item.text).ToList();
                Assert.That(texts, Has.Member("Раскидка"));
                Assert.That(texts.Any(text => text == "Слои раскидки" || text == "Раскидка"), Is.True);
            }
            finally
            {
                window.Close();
            }
        }
    }
}
