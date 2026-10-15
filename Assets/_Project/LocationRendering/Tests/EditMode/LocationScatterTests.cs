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

        // Рисование поверх: прежние экземпляры не мешают ни плотности, ни
        // расстоянию — мазок досыпает ещё столько же; внутри мазка
        // наименьшее расстояние соблюдается. Нажим — доля недостающего за отпечаток.
        [Test]
        public void PaintOver_AddsAnotherLayer_AndFlowFillsGradually()
        {
            Asset("grass");
            LocalLocationDefinition location = Location();
            LocationScatterLayer layer = Layer("grass");
            layer.Brush.Radius = 200;
            layer.Brush.Density = 6;
            layer.Brush.MinDistance = 30;
            Vector2 center = new Vector2(960, 540);
            System.Random random = new System.Random(3);
            LocationScatterPainter.Stamp(layer, location, center, random, null, LocationScatterIndex.Of(layer, location));
            int first = layer.Instances.Count;
            Assert.That(first, Is.GreaterThan(20));
            Assert.That(LocationScatterPainter.Stamp(layer, location, center, random, null, LocationScatterIndex.Of(layer, location)).Count, Is.LessThanOrEqualTo(2),
                "Без «поверх» заполненное место не досыпается.");

            int before = layer.Instances.Count;
            HashSet<int> stroke = new HashSet<int>();
            LocationScatterIndex strokeIndex = new LocationScatterIndex(layer.Brush.MinDistance);
            for (int i = 0; i < 4; i++)
                foreach (LocationScatterInstance added in LocationScatterPainter.Stamp(layer, location, center, random, null, strokeIndex, null, stroke))
                    stroke.Add(added.Key);
            Assert.That(stroke.Count, Is.InRange(first * .7f, first * 1.3f + 2), "Поверх — ещё один слой той же плотности, повторные отпечатки мазка его не утраивают.");
            Assert.That(layer.Instances.Count, Is.EqualTo(before + stroke.Count));
            List<Vector2> strokePoints = layer.Instances.Where(item => stroke.Contains(item.Key)).Select(item => LocationVisualGeometry.ToPixel(location, item.Position)).ToList();
            for (int i = 0; i < strokePoints.Count; i++)
                for (int j = i + 1; j < strokePoints.Count; j++)
                    Assert.That(Vector2.Distance(strokePoints[i], strokePoints[j]), Is.GreaterThanOrEqualTo(30 - .01f), "Наименьшее расстояние — внутри мазка.");

            LocationScatterLayer gentle = Layer("grass");
            gentle.Brush.Radius = 200;
            gentle.Brush.Density = 6;
            gentle.Brush.MinDistance = 0;
            gentle.Brush.Flow = .1f;
            LocationScatterIndex gentleIndex = LocationScatterIndex.Of(gentle, location);
            LocationScatterPainter.Stamp(gentle, location, center, random, null, gentleIndex);
            Assert.That(gentle.Instances.Count, Is.LessThanOrEqualTo(first * .2f + 1), "Нажим 0,1 — отпечаток добавляет десятую долю.");
            for (int i = 0; i < 60; i++) LocationScatterPainter.Stamp(gentle, location, center, random, null, gentleIndex);
            Assert.That(gentle.Instances.Count, Is.InRange(first * .8f, first * 1.2f + 2), "Мазок с малым нажимом набирает ту же плотность.");

            LocationScatterLayer filled = Layer("grass");
            filled.Brush.Density = 1;
            filled.Brush.MinDistance = 0;
            int once = LocationScatterPainter.Fill(filled, location, new System.Random(1), null, null).Count;
            Assert.That(LocationScatterPainter.Fill(filled, location, new System.Random(2), null, null).Count, Is.EqualTo(0));
            Assert.That(LocationScatterPainter.Fill(filled, location, new System.Random(2), null, new LocationScatterIndex(8), null, 20000, true).Count, Is.EqualTo(once),
                "Заливка поверх — ещё один слой плотности.");
        }

        [Test]
        public void NewBrush_RandomViewAndHueSpread_ShadowsFromSunAndFireSeparate()
        {
            LocationScatterBrush brush = new LocationScatterBrush();
            Assert.That(brush.ViewMode, Is.EqualTo(LocationScatterViewMode.Random), "По умолчанию — случайный ракурс из имеющихся.");
            Assert.That(brush.HueJitter, Is.EqualTo(15));
            Assert.That(brush.Flow, Is.EqualTo(1));
            Assert.That(brush.PaintOver, Is.False);

            // Прежний слой (без отдельной галочки огня): тень от огня — как от солнца.
            LocationScatterLayer old = JsonUtility.FromJson<LocationScatterLayer>("{\"Name\":\"Старая\",\"ProjectsShadow\":true}");
            Assert.That(old.ProjectsFireShadow, Is.True);
            old.ProjectsFireShadow = false;
            Assert.That(old.ProjectsShadow, Is.True, "Галочки независимы.");
            LocationScatterLayer copy = JsonUtility.FromJson<LocationScatterLayer>(JsonUtility.ToJson(old));
            Assert.That(copy.ProjectsShadow && !copy.ProjectsFireShadow, Is.True, "Раздельные тени сохраняются.");

            Asset("bush");
            copy.Assets.Add(new LocationScatterEntry { AssetId = "bush" });
            copy.Instances.Add(new LocationScatterInstance { Key = 1, AssetId = "bush", Position = new Vector2(.5f, .5f) });
            LocationResolvedPart part = LocationVisualResolver.Resolve(copy.ToObject(copy.Instances[0])).Main;
            Assert.That(part.ProjectsShadow, Is.True);
            Assert.That(part.ProjectsFireShadow, Is.False);

            // Часть ассета: тени от солнца и огня — отдельными галочками, у экземпляра — как у ассета.
            ArtAssetDefinition asset = catalog.Find("bush");
            asset.MainPart.ProjectsFireShadow = false;
            LocationVisualObject instance = new LocationVisualObject { Id = "one", AssetId = "bush", Scale = 1 };
            part = LocationVisualResolver.Resolve(instance).Main;
            Assert.That(part.ProjectsShadow && !part.ProjectsFireShadow, Is.True);
            asset.MainPart.ProjectsShadow = false;
            asset.MainPart.ProjectsFireShadow = true;
            part = LocationVisualResolver.Resolve(instance).Main;
            Assert.That(!part.ProjectsShadow && part.ProjectsFireShadow, Is.True);
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

        // Тени слоя только выключают тени ассета: трава без тени в Базе ассетов
        // остаётся без тени в слое с галочкой; длина тени и перекрытие света —
        // как у ассета.
        [Test]
        public void Instance_ShadowsFromLayer_OnlyTurnAssetShadowsOff()
        {
            Asset("grass");
            ArtAssetDefinition asset = catalog.Find("grass");
            asset.MainPart.ProjectsShadow = false;
            asset.MainPart.ProjectsFireShadow = false;
            asset.ShadowLength = 1.7f;
            asset.OccludesLight = true;
            LocationScatterLayer layer = Layer("grass");
            layer.ProjectsShadow = true;
            layer.ProjectsFireShadow = true;
            layer.Instances.Add(new LocationScatterInstance { Key = 1, AssetId = "grass", Position = new Vector2(.5f, .5f), Scale = 1 });
            LocationResolvedVisual resolved = LocationVisualResolver.Resolve(layer.ToObject(layer.Instances[0]));
            Assert.That(resolved.Main.ProjectsShadow, Is.False, "Галочка слоя не включает тень ассету без тени.");
            Assert.That(resolved.Main.ProjectsFireShadow, Is.False);
            Assert.That(resolved.ShadowLength, Is.EqualTo(1.7f).Within(1e-4f), "Длина тени — как у ассета.");
            Assert.That(resolved.OccludesLight, Is.True, "Перекрытие света — как у ассета.");

            asset.MainPart.ProjectsShadow = true;
            asset.MainPart.ProjectsFireShadow = true;
            Assert.That(LocationVisualResolver.Resolve(layer.ToObject(layer.Instances[0])).Main.ProjectsShadow, Is.True, "У ассета с тенью — есть.");
            layer.ProjectsShadow = false;
            layer.ProjectsFireShadow = false;
            resolved = LocationVisualResolver.Resolve(layer.ToObject(layer.Instances[0]));
            Assert.That(resolved.Main.ProjectsShadow || resolved.Main.ProjectsFireShadow, Is.False, "Слой без теней выключает их всем.");
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

        // Анимация вразнобой: даже у ассета без «Случайной фазы» экземпляры
        // ковра разложены по фазовым группам со своим темпом, у объектов —
        // своя фаза и темп; выключено — весь слой в такт.
        [Test]
        public void Animation_IsDesynchronized_EvenWithoutRandomPhase()
        {
            ArtAssetDefinition asset = Asset("wind");
            asset.FramesPerSecond = 8;
            asset.RandomPhase = false;
            for (int i = 0; i < 5; i++)
                asset.MainPart.View(ArtAssetView.Front).Frames.Add(new ArtAssetFrame { Sprite = Sprite(new Color(.2f, .6f + i * .05f, .2f)) });
            catalog.MarkChanged();
            LocalLocationDefinition location = Location();
            foreach (bool desync in new[] { true, false })
            {
                LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = location.Id, UseWorldLighting = false };
                LocationScatterLayer carpet = Layer("wind");
                carpet.Mode = LocationScatterMode.Carpet;
                carpet.DesyncAnimation = desync;
                carpet.Brush.Density = 20;
                carpet.Brush.MinDistance = 4;
                LocationScatterPainter.Stamp(carpet, location, new Vector2(800, 500), new System.Random(5), null, LocationScatterIndex.Of(carpet, location));
                LocationScatterLayer objects = Layer("wind");
                objects.DesyncAnimation = desync;
                LocationScatterPainter.Stamp(objects, location, new Vector2(1300, 500), new System.Random(6), null, LocationScatterIndex.Of(objects, location));
                visual.ScatterLayers.Add(carpet);
                visual.ScatterLayers.Add(objects);
                using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
                {
                    int groups = renderer.ScatterCarpet(carpet.Id).GroupCount;
                    renderer.SetTime(13, 2.37f);
                    HashSet<Sprite> shown = new HashSet<Sprite>();
                    foreach (LocationScatterInstance instance in objects.Instances)
                        shown.Add(renderer.ScatterImages(objects.Id, instance.Key)[0].sprite);
                    if (desync)
                    {
                        Assert.That(groups, Is.GreaterThan(4), "Ковёр — несколько фазовых групп: " + carpet.Instances.Count + " шт., кадров " + LocationVisualResolver.Resolve(new LocationVisualObject { Id = "x", AssetId = "wind", Scale = 1 }).Main.Frames?.Length);
                        Assert.That(shown.Count, Is.GreaterThan(2), "Объекты показывают разные кадры в один момент.");
                    }
                    else
                    {
                        Assert.That(groups, Is.EqualTo(1), "Выключено — ковёр одной группой, в такт.");
                        Assert.That(shown.Count, Is.EqualTo(1), "Выключено — объекты в такт.");
                    }
                }
            }
            Assert.That(new LocationScatterLayer().AnimationSpeed("a"), Is.InRange(.88f, 1.12f));
            // Фазы ID, различающихся последними символами, расходятся по всему кругу.
            int[] bins = new int[10];
            for (int i = 1; i <= 200; i++) bins[Mathf.Min(9, Mathf.FloorToInt(ArtAssetAnimation.StablePhase("abc#" + i) * 10))]++;
            Assert.That(bins.Min(), Is.GreaterThan(5), "Фазы по кругу: " + string.Join(", ", bins));
        }

        // «Копировать / вставить» настроек слоя: всё, кроме экземпляров,
        // названия и своих ассетов; совпадающие ассеты получают настройки копии.
        [Test]
        public void LayerSettings_CopyPaste_KeepsInstancesNameAndOwnAssets()
        {
            LocationScatterLayer source = Layer("grass", "stone");
            source.Name = "Источник";
            source.Mode = LocationScatterMode.Carpet;
            source.Brush.Density = 17;
            source.Reaction.Enabled = true;
            source.Reaction.Bend = .5f;
            source.DesyncAnimation = false;
            source.Assets[0].Bends = false;
            source.Assets[0].Weight = 3;
            source.Instances.Add(new LocationScatterInstance { Key = 1, AssetId = "grass" });
            LocationScatterLayer target = Layer("grass", "flower");
            target.Name = "Цель";
            target.Instances.Add(new LocationScatterInstance { Key = 5, AssetId = "flower" });
            LocationDatabaseWindow.ApplyScatterSettings(source, target);
            Assert.That(target.Name, Is.EqualTo("Цель"));
            Assert.That(target.Instances.Single().Key, Is.EqualTo(5), "Экземпляры остаются свои.");
            Assert.That(target.Mode, Is.EqualTo(LocationScatterMode.Carpet));
            Assert.That(target.Brush.Density, Is.EqualTo(17));
            Assert.That(target.Brush, Is.Not.SameAs(source.Brush), "Копия, а не общая ссылка.");
            Assert.That(target.Reaction.Enabled && target.Reaction.SafeBend > .45f, Is.True, "Реакция и сила прогиба.");
            Assert.That(target.DesyncAnimation, Is.False);
            Assert.That(target.Assets.Select(item => item.AssetId), Is.EqualTo(new[] { "grass", "flower" }), "Ассеты кисти — свои.");
            Assert.That(target.Assets[0].Bends, Is.False);
            Assert.That(target.Assets[0].Weight, Is.EqualTo(3), "Совпадающий ассет — с настройками копии.");
            LocationScatterLayer empty = new LocationScatterLayer();
            LocationDatabaseWindow.ApplyScatterSettings(source, empty);
            Assert.That(empty.Assets.Count, Is.EqualTo(2), "Пустая кисть получает ассеты копии.");
        }

        // Боковая панель — сворачиваемыми блоками по разделам.
        [Test]
        public void SettingsPanel_IsGroupedIntoBlocks()
        {
            LocationDatabaseWindow window = EditorWindow.GetWindow<LocationDatabaseWindow>();
            try
            {
                window.CreateGUI();
                System.Type type = typeof(LocationDatabaseWindow);
                FieldInfo tab = type.GetField("tab", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (string name in new[] { "Light", "Scatter", "Place" })
                {
                    tab.SetValue(window, System.Enum.Parse(tab.FieldType, name));
                    type.GetMethod("BuildSettings", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
                    ScrollView settings = (ScrollView)type.GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                    List<Foldout> blocks = settings.Children().OfType<Foldout>().ToList();
                    if (settings.childCount <= 2) continue;
                    Assert.That(blocks.Count, Is.GreaterThan(0), name + ": разделы — блоками.");
                    Assert.That(settings.Children().Count(item => !(item is Foldout)), Is.LessThanOrEqualTo(2), name + ": сверху только место и вкладки.");
                }
            }
            finally
            {
                window.Close();
            }
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
