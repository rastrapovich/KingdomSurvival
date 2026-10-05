using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.ArtAssets.Tests
{
    // ПР-12Н: каталог, разрешение ссылки экземпляра, совместимость прежних
    // предметов, перевод в каталог и запрет удаления используемой записи.
    public sealed class ArtAssetCatalogTests
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
            ArtAssetUsages.Providers.RemoveAll(provider => provider.Method.Name.Contains("Test"));
            foreach (Object item in owned) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        private Sprite Sprite(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            owned.Add(texture);
            Sprite sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 100);
            owned.Add(sprite);
            return sprite;
        }

        private ArtAssetDefinition Asset(string name = "Телега", float pixelsPerUnit = 100)
        {
            ArtAssetDefinition asset = new ArtAssetDefinition { Name = name, PixelsPerUnit = pixelsPerUnit };
            catalog.assets.Add(asset);
            catalog.MarkChanged();
            return asset;
        }

        [Test]
        public void Ids_AreStable_UniqueAndIndependentOfName()
        {
            HashSet<string> ids = new HashSet<string>();
            for (int i = 0; i < 500; i++) Assert.That(ids.Add(ArtAssetDefinition.NewId()), Is.True);
            ArtAssetDefinition asset = Asset();
            string id = asset.Id;
            asset.Name = "Телега с сеном";
            catalog.assets.Insert(0, new ArtAssetDefinition { Name = "Другое" });
            Assert.That(catalog.Find(id), Is.SameAs(asset), "Ссылка не зависит от названия и позиции в списке.");
            Assert.That(asset.Id, Is.EqualTo(id));
            ArtAssetDefinition clone = ArtAssetImporter.Clone(asset);
            Assert.That(clone.Id, Is.EqualTo(id), "Повторный импорт в ту же запись сохраняет ID.");
        }

        [Test]
        public void Find_SeesRecordsAddedWithoutNotification()
        {
            Asset();
            Assert.That(catalog.Find("missing"), Is.Null);
            ArtAssetDefinition late = new ArtAssetDefinition { Name = "Поздняя" };
            catalog.assets.Add(late);
            Assert.That(catalog.Find(late.Id), Is.SameAs(late));
        }

        [Test]
        public void ViewOrder_MatchesAnimationDatabaseContract()
        {
            Assert.That(ArtAssetLabels.Views.Length, Is.EqualTo(CreatureAnimationLabels.Directions.Length));
            for (int i = 0; i < ArtAssetLabels.Views.Length; i++)
            {
                ArtAssetView view = ArtAssetLabels.Views[i];
                CreatureAnimationDirection direction = ArtAssetViewMapping.ToDirection(view);
                Assert.That(direction, Is.EqualTo(CreatureAnimationLabels.Directions[i]));
                Assert.That(ArtAssetLabels.ViewFolder(view), Is.EqualTo(CreatureAnimationLabels.DirectionFolder(direction)));
                Assert.That(ArtAssetLabels.ViewTitle(view), Is.EqualTo(CreatureAnimationLabels.DirectionTitle(direction)));
                Assert.That(ArtAssetViewMapping.FromDirection(direction), Is.EqualTo(view));
            }
        }

        [Test]
        public void TransparentMarginsDoNotChangeScale_AndPivotStaysOnTheAnchor()
        {
            ArtAssetDefinition asset = Asset(pixelsPerUnit: 100);
            asset.MainPart.View(ArtAssetView.Front).Sprite = Sprite(200, 300);
            asset.MainPart.View(ArtAssetView.Back).Sprite = Sprite(240, 360);
            asset.Settings(ArtAssetView.Front).Pivot = new Vector2(.5f, .1f);
            asset.Settings(ArtAssetView.Back).Pivot = new Vector2(.45f, .2f);
            LocationVisualObject item = new LocationVisualObject { AssetId = asset.Id, View = ArtAssetView.Front, Scale = 2 };
            LocationResolvedPart front = LocationVisualResolver.Resolve(item).Main;
            item.View = ArtAssetView.Back;
            LocationResolvedPart back = LocationVisualResolver.Resolve(item).Main;
            Assert.That(front.Height, Is.EqualTo(6).Within(1e-4f), "300 px / 100 × масштаб 2.");
            Assert.That(back.Height, Is.EqualTo(7.2f).Within(1e-4f), "Масштаб общий: 360 px дают больше, а не «вписываются».");
            Assert.That(front.Pivot, Is.EqualTo(new Vector2(.5f, .1f)));
            Assert.That(back.Pivot, Is.EqualTo(new Vector2(.45f, .2f)), "Опора — своя у ракурса, мировая точка опоры не меняется.");
            Assert.That(asset.Height, Is.EqualTo(3).Within(1e-4f));
            asset.Height = 1.5f;
            Assert.That(asset.PixelsPerUnit, Is.EqualTo(200).Within(1e-3f), "Высота меняет масштаб всех ракурсов сразу.");
        }

        [Test]
        public void MissingView_FallsBackVisibly_AndPartsAlignToTheBase()
        {
            ArtAssetDefinition asset = Asset(pixelsPerUnit: 100);
            asset.MainPart.View(ArtAssetView.FrontLeft).Sprite = Sprite(100, 100);
            ArtAssetPart roof = new ArtAssetPart { Name = "Крыша", OrderOffset = 1 };
            asset.Parts.Add(roof);
            ArtAssetPartView roofView = roof.View(ArtAssetView.FrontLeft);
            roofView.Sprite = Sprite(100, 50);
            roofView.Offset = new Vector2(0, .5f);
            asset.Settings(ArtAssetView.FrontLeft).Pivot = new Vector2(.5f, 0);
            LocationResolvedVisual resolved = LocationVisualResolver.Resolve(new LocationVisualObject { AssetId = asset.Id, View = ArtAssetView.Front });
            Assert.That(resolved.ShownView, Is.EqualTo(ArtAssetView.FrontLeft));
            Assert.That(resolved.ViewFallback, Is.True, "Отсутствующий ракурс виден явно.");
            Assert.That(resolved.Parts.Count, Is.EqualTo(2));
            LocationResolvedPart part = resolved.Parts[1];
            // Крыша 100×50 начинается на 50 px выше основы: опора (50, 0) основы — в (50, −50) рисунка крыши.
            Assert.That(part.Pivot.x, Is.EqualTo(.5f).Within(1e-4f));
            Assert.That(part.Pivot.y, Is.EqualTo(-1f).Within(1e-4f));
            Assert.That(part.Height, Is.EqualTo(.5f).Within(1e-4f));
            Assert.That(part.OrderOffset, Is.EqualTo(1));

            ArtAssetDefinition empty = Asset("Пустой");
            LocationResolvedVisual none = LocationVisualResolver.Resolve(new LocationVisualObject { AssetId = empty.Id });
            Assert.That(none.NoArt, Is.True);
            Assert.That(none.Main.Placeholder, Is.True, "Неполный ассет можно разместить: показывается заглушка.");
        }

        [Test]
        public void Overrides_AreExplicit_AndInheritedValuesComeFromTheCatalog()
        {
            ArtAssetDefinition asset = Asset();
            asset.MainPart.View(ArtAssetView.Front).Sprite = Sprite(100, 100);
            asset.BlocksMovement = true;
            asset.MainPart.Layer = ArtAssetLayer.World;
            asset.Settings(ArtAssetView.Front).FootprintSize = new Vector2(2, 1);
            asset.Settings(ArtAssetView.Front).FootprintOffset = new Vector2(.5f, .25f);
            LocationVisualObject item = new LocationVisualObject
            {
                AssetId = asset.Id, BlocksMovement = false, Band = LocationVisualBand.Foreground, Footprint = new Vector2(9, 9), Scale = 2, FlipX = true
            };
            LocationResolvedVisual inherited = LocationVisualResolver.Resolve(item);
            Assert.That(inherited.BlocksMovement, Is.True);
            Assert.That(inherited.Main.Band, Is.EqualTo(LocationVisualBand.World));
            Assert.That(inherited.FootprintSize, Is.EqualTo(new Vector2(4, 2)), "Основание масштабируется вместе с экземпляром.");
            Assert.That(inherited.FootprintOffset, Is.EqualTo(new Vector2(-1, .5f)), "Отражение зеркалит смещение основания.");

            item.Overrides = LocationAssetOverride.Passability | LocationAssetOverride.Layer;
            LocationResolvedVisual own = LocationVisualResolver.Resolve(item);
            Assert.That(own.BlocksMovement, Is.False);
            Assert.That(own.FootprintSize, Is.EqualTo(new Vector2(9, 9)));
            Assert.That(own.Main.Band, Is.EqualTo(LocationVisualBand.Foreground));

            asset.BlocksMovement = false;
            asset.Settings(ArtAssetView.Front).FootprintSize = new Vector2(1, 1);
            item.Overrides = LocationAssetOverride.None;
            Assert.That(LocationVisualResolver.Resolve(item).FootprintSize, Is.EqualTo(new Vector2(2, 2)), "Изменение каталога видно экземпляру.");
        }

        [Test]
        public void LegacyDirectSprite_ResolvesExactlyAsBefore()
        {
            Sprite sprite = Sprite(64, 128);
            LocationVisualObject legacy = new LocationVisualObject
            {
                Sprite = sprite, Height = 2.5f, Pivot = new Vector2(.4f, .2f), Band = LocationVisualBand.World, OrderOffset = 3,
                BlocksMovement = true, Footprint = new Vector2(1.5f, .5f), CastsShadow = true, ShadowLength = .7f
            };
            LocationResolvedVisual resolved = LocationVisualResolver.Resolve(legacy);
            Assert.That(resolved.FromAsset, Is.False);
            Assert.That(resolved.Main.Sprite, Is.SameAs(sprite));
            Assert.That(resolved.Main.Height, Is.EqualTo(2.5f));
            Assert.That(resolved.Main.Pivot, Is.EqualTo(new Vector2(.4f, .2f)));
            Assert.That(resolved.Main.OrderOffset, Is.EqualTo(3));
            Assert.That(resolved.FootprintSize, Is.EqualTo(new Vector2(1.5f, .5f)));
            Assert.That(resolved.FootprintOffset, Is.EqualTo(Vector2.zero));

            LocalLocationDefinition location = new LocalLocationDefinition();
            legacy.Position = new Vector2(.5f, .5f);
            Rect blocked = LocationVisualGeometry.BlockedAreas(new LocationVisualDefinition { Objects = { legacy } }, location).Single();
            Assert.That(blocked.center, Is.EqualTo(new Vector2(960, 540)));
            Assert.That(blocked.size.x, Is.EqualTo(1.5f * LocationVisualGeometry.PixelsPerUnit).Within(1e-3f));

            // Ссылка на исчезнувший ассет: запасной прямой рисунок и ошибка проверки.
            legacy.AssetId = "asset_missing";
            Assert.That(LocationVisualResolver.Resolve(legacy).Main.Sprite, Is.SameAs(sprite));
            Assert.That(LocationVisualResolver.Resolve(legacy).AssetMissing, Is.True);
        }

        [Test]
        public void AssetFootprint_BlocksOnlyItsOwnArea()
        {
            ArtAssetDefinition house = Asset("Дом");
            house.MainPart.View(ArtAssetView.Front).Sprite = Sprite(300, 300);
            house.BlocksMovement = true;
            house.Settings(ArtAssetView.Front).FootprintSize = new Vector2(2, 1);
            house.Settings(ArtAssetView.Front).FootprintOffset = new Vector2(0, .5f);
            LocalLocationDefinition location = new LocalLocationDefinition();
            LocationVisualObject item = new LocationVisualObject { AssetId = house.Id, Position = new Vector2(.5f, .5f) };
            LocationVisualDefinition visual = new LocationVisualDefinition { Objects = { item } };
            LocalLocationGeometry geometry = new LocalLocationGeometry(location, null, LocationVisualGeometry.BlockedAreas(visual, location));
            Assert.That(geometry.IsPassable(960, 540 - 54), Is.False, "Центр основания — на полединицы «вглубь» (вверх по рисунку).");
            Assert.That(geometry.IsPassable(960, 540 + 60), Is.True, "Перед домом — проходимо.");
            Assert.That(geometry.IsPassable(960 + 140, 540 - 54), Is.True, "Сбоку от основания — проходимо.");
            house.BlocksMovement = false;
            Assert.That(LocationVisualGeometry.BlockedAreas(visual, location), Is.Empty);
        }

        [Test]
        public void Validation_ReportsMissingAsset()
        {
            LocalLocationDefinition location = new LocalLocationDefinition { Id = "zz" };
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "zz", TestStartPoint = new Vector2(.1f, .1f) };
            visual.Objects.Add(new LocationVisualObject { Name = "Телега", AssetId = "asset_nope", Position = new Vector2(.5f, .5f) });
            List<string> errors = LocationVisualGeometry.Validate(location, visual, new BattlefieldDefinitionData());
            Assert.That(errors.Any(error => error.Contains("не найден в Базе ассетов")), Is.True, string.Join("; ", errors));
            ArtAssetDefinition asset = Asset();
            visual.Objects[0].AssetId = asset.Id;
            errors = LocationVisualGeometry.Validate(location, visual, new BattlefieldDefinitionData());
            Assert.That(errors.Any(error => error.Contains("Телега")), Is.False, "Неполный ассет — не ошибка: " + string.Join("; ", errors));
        }

        [Test]
        public void Migration_KeepsTheLook_IsRepeatable_AndReusesTheSameArt()
        {
            Sprite sprite = Sprite(100, 200);
            LocationVisualObject first = new LocationVisualObject
            {
                Name = "Палатка", Sprite = sprite, Height = 2, Pivot = new Vector2(.5f, .1f), BlocksMovement = true, Footprint = new Vector2(1.4f, .6f),
                CastsShadow = true, Band = LocationVisualBand.World, Position = new Vector2(.3f, .4f)
            };
            first.Variants.Add(new LocationVisualVariant { Id = "burned", Name = "Сгоревшая", Sprite = Sprite(100, 150) });
            LocationVisualObject second = JsonUtility.FromJson<LocationVisualObject>(JsonUtility.ToJson(first));
            second.Id = "second"; second.Height = 3; second.Position = new Vector2(.7f, .4f);
            second.Footprint = new Vector2(2.1f, .9f);
            LocationVisualObject light = new LocationVisualObject { Name = "Свет", LightOnly = true };
            List<LocationVisualObject> items = new List<LocationVisualObject> { first, second, light };
            LocationResolvedVisual before = LocationVisualResolver.Resolve(second);

            ArtAssetMigrationReport preview = ArtAssetMigration.Preview(items, catalog);
            Assert.That(preview.Created, Is.EqualTo(1));
            Assert.That(preview.Reused, Is.EqualTo(1));
            Assert.That(preview.Skipped, Is.EqualTo(1));

            ArtAssetMigrationReport report = ArtAssetMigration.Migrate(items, catalog, null);
            Assert.That(report.Failed, Is.EqualTo(0), string.Join("; ", report.Problems));
            Assert.That(catalog.assets.Count, Is.EqualTo(1), "Одинаковые рисунки — одна запись.");
            Assert.That(first.AssetId, Is.EqualTo(catalog.assets[0].Id));
            Assert.That(second.AssetId, Is.EqualTo(first.AssetId), "Десять экземпляров ссылаются на одну запись — здесь два.");
            Assert.That(second.Scale, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(ArtAssetMigration.Difference(before, LocationVisualResolver.Resolve(second)), Is.Null, "Вид после перевода совпадает.");
            Assert.That(first.Variants.Count, Is.EqualTo(1), "Состояния рисунка не удаляются.");
            Assert.That(LocationVisualResolver.Resolve(first, "burned").Main.Sprite, Is.SameAs(first.Variants[0].Sprite), "Состояние работает и после перевода.");

            ArtAssetMigrationReport again = ArtAssetMigration.Migrate(items, catalog, null);
            Assert.That(again.Created + again.Reused, Is.EqualTo(0), "Повторный перевод — без дубликатов.");
            Assert.That(catalog.assets.Count, Is.EqualTo(1));
        }

        [Test]
        public void UsedAsset_CannotBeDeleted_UntilReferencesAreReplaced()
        {
            ArtAssetDefinition used = Asset("Используемый");
            ArtAssetDefinition other = Asset("Замена");
            LocalLocationDatabaseAsset locations = ScriptableObject.CreateInstance<LocalLocationDatabaseAsset>();
            owned.Add(locations);
            locations.locations.Add(new LocalLocationDefinition { Id = "zz_place", DisplayName = "Тестовое место" });
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "zz_place" };
            for (int i = 0; i < 10; i++) visual.Objects.Add(new LocationVisualObject { Name = "Телега " + i, AssetId = used.Id, View = (ArtAssetView)(i % 6) });
            locations.visuals.Add(visual);
            ArtAssetUsages.Providers.Add(TestUsages);
            IEnumerable<ArtAssetUsage> TestUsages() => ArtAssetUsages.LocationUsages(new[] { locations });

            Assert.That(ArtAssetUsages.TryDelete(catalog, used, out List<ArtAssetUsage> usages), Is.False);
            Assert.That(usages.Count(item => item.OwnerId == "zz_place"), Is.EqualTo(10));
            Assert.That(usages[0].Title, Does.Contain("Тестовое место"));
            Assert.That(catalog.Find(used.Id), Is.Not.Null);

            foreach (LocationVisualObject item in visual.Objects) item.AssetId = other.Id;
            Assert.That(ArtAssetUsages.TryDelete(catalog, used, out _), Is.True);
            Assert.That(catalog.Find(used.Id), Is.Null);
            Assert.That(visual.Objects.All(item => item.AssetId == other.Id && !string.IsNullOrEmpty(item.Id)));
        }

        [Test]
        public void LargeCatalog_SearchAndResolveStayFast()
        {
            Sprite sprite = Sprite(64, 64);
            for (int i = 0; i < 3000; i++)
            {
                ArtAssetDefinition asset = new ArtAssetDefinition { Name = "Объект " + i, Tags = new List<string> { i % 2 == 0 ? "камень" : "дерево" } };
                asset.MainPart.View(ArtAssetView.Front).Sprite = sprite;
                catalog.assets.Add(asset);
            }
            catalog.MarkChanged();
            Stopwatch watch = Stopwatch.StartNew();
            int found = catalog.assets.Count(item => item.MatchesQuery("камень"));
            string last = catalog.assets[2999].Id;
            for (int i = 0; i < 10000; i++) Assert.That(catalog.Find(last), Is.Not.Null);
            foreach (ArtAssetDefinition asset in catalog.assets)
                ArtAssetDrawing.Resolve(catalog, asset, ArtAssetView.Back);
            watch.Stop();
            Assert.That(found, Is.EqualTo(1500));
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(2000), "Поиск, разрешение ссылок и раскладка 3000 записей: " + watch.ElapsedMilliseconds + " мс");
        }

        [Test]
        public void RuntimeCatalog_HasNoEditorDependencies()
        {
            string runtime = Path.Combine(Application.dataPath, "_Project/ArtAssetDatabase/Runtime");
            string asmdef = File.ReadAllText(Path.Combine(runtime, "KingdomSurvival.ArtAssetDatabase.asmdef"));
            StringAssert.Contains("\"includePlatforms\": []", asmdef, "Каталог собирается в Player.");
            foreach (string file in Directory.GetFiles(runtime, "*.cs"))
                StringAssert.DoesNotContain("UnityEditor", File.ReadAllText(file), Path.GetFileName(file));
            StringAssert.StartsWith("Assets/_Project/ArtAssetDatabase/Resources/", ArtAssetDatabaseAsset.AssetPath, "Каталог грузится через Resources.");
        }
    }
}
