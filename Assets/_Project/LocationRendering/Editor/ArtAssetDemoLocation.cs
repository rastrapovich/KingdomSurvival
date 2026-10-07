using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.ArtAssets;
using KingdomSurvival.ArtAssets.Editor;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12Н: техническое место «Ассеты · тест» — проверка Базы ассетов в
    // служебном запуске: ящик, дерево с кроной и дом с крышей из технического
    // набора стоят экземплярами (ссылками), у костра можно проверить ночь,
    // нормали и перекрытие героя крышей и кроной. ПР-12П: заросль технической
    // травы — покадровая анимация, у каждого куста своя фаза. Не игровое место.
    public static class ArtAssetDemoLocation
    {
        public const string Id = "technical_asset_demo";

        [MenuItem("Kingdom Survival/Служебное/База ассетов: техническое место")]
        public static void CreateFromMenu()
        {
            Ensure();
            EditorApplication.ExecuteMenuItem("Kingdom Survival/База локаций");
            ArtAssetUsages.OpenLocation(Id, null);
        }

        // Повторный вызов ничего не дублирует.
        public static void Ensure()
        {
            LocationLightingTestBootstrap.EnsureCamp();
            ArtAssetDatabaseAsset catalog = ArtAssetImporter.LoadOrCreateCatalog();
            ArtAssetTechnicalSet.Ensure(catalog);
            LocalLocationDatabaseAsset database = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            if (database == null) return;
            LocationVisualDefinition existing = database.locations.Any(item => item.Id == Id) ? database.FindVisual(Id) : null;
            if (existing != null)
            {
                // Место создано раньше — дописать заросль травы (12П) и раскидку (12Р).
                bool grass = existing.Objects.Any(item => item != null && item.AssetId == ArtAssetTechnicalSet.GrassId);
                bool scatter = existing.ScatterLayers != null && existing.ScatterLayers.Count > 0;
                if (grass && scatter) return;
                Undo.RecordObject(database, "Техническое место Базы ассетов: трава");
                if (!grass) PlaceGrass(existing, catalog);
                if (!scatter) PlaceScatter(existing, database.locations.Find(item => item.Id == Id));
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
                return;
            }
            Undo.RecordObject(database, "Техническое место Базы ассетов");
            if (database.locations.All(item => item.Id != Id))
                database.locations.Add(new LocalLocationDefinition
                {
                    Id = Id, WorldLocationId = "__technical_asset_demo", DisplayName = "Ассеты · тест",
                    BattlefieldId = LocationLightingTestBootstrap.FieldId, PlaceholderArt = true,
                    Entrances = new List<LocalEntranceDefinition> { new LocalEntranceDefinition { Id = "asset_demo_entry", Label = "Вход", Point = new LocalPointData(400, 760) } }
                });
            LocationVisualDefinition visual = database.FindVisual(Id);
            if (visual == null)
            {
                visual = new LocationVisualDefinition { LocationId = Id, TechnicalTest = true, TestStartPoint = new Vector2(.2f, .72f) };
                LocationVisualDefinition camp = database.FindVisual(LocationLightingTestBootstrap.CampId);
                if (camp != null) visual.TestUnitId = camp.TestUnitId;
                database.visuals.Add(visual);
            }
            Place(visual, catalog, ArtAssetTechnicalSet.HouseId, "Дом (тест)", .5f, .42f, ArtAssetView.Front);
            Place(visual, catalog, ArtAssetTechnicalSet.TreeId, "Дерево (тест)", .25f, .5f, ArtAssetView.FrontRight);
            Place(visual, catalog, ArtAssetTechnicalSet.TreeId, "Дерево 2 (тест)", .78f, .55f, ArtAssetView.Back);
            Place(visual, catalog, ArtAssetTechnicalSet.CrateId, "Ящик (тест)", .4f, .7f, ArtAssetView.FrontLeft);
            Place(visual, catalog, ArtAssetTechnicalSet.CrateId, "Ящик 2 (тест)", .62f, .74f, ArtAssetView.Front);
            PlaceGrass(visual, catalog);
            PlaceScatter(visual, database.locations.Find(item => item.Id == Id));
            LocationVisualObject fire = new LocationVisualObject { Name = "Свет у дома", LightOnly = true, Position = new Vector2(.36f, .6f) };
            fire.Light.Enabled = true; fire.Light.Radius = 5; fire.Light.Intensity = 1.6f; fire.Light.NormalMaps = true; fire.Light.NightOnly = true;
            visual.Objects.Add(fire);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
        }

        private static void Place(LocationVisualDefinition visual, ArtAssetDatabaseAsset catalog, string assetId, string name, float x, float y,
            ArtAssetView view, bool flip = false)
        {
            ArtAssetDefinition asset = catalog.Find(assetId);
            if (asset == null) return;
            ArtAssetViewSettings settings = asset.Settings(view);
            visual.Objects.Add(new LocationVisualObject
            {
                Name = name, AssetId = assetId, View = view, Position = new Vector2(x, y), Height = asset.Height, Pivot = settings.Pivot, FlipX = flip,
                BlocksMovement = asset.BlocksMovement, Footprint = settings.FootprintSize, CastsShadow = asset.OccludesLight
            });
        }

        // Заросль у дорожки: одинаковые кусты качаются вразнобой (своя фаза по ID).
        private static void PlaceGrass(LocationVisualDefinition visual, ArtAssetDatabaseAsset catalog)
        {
            Vector2[] points =
            {
                new Vector2(.28f, .78f), new Vector2(.31f, .81f), new Vector2(.34f, .77f), new Vector2(.37f, .83f),
                new Vector2(.70f, .80f), new Vector2(.73f, .84f), new Vector2(.76f, .79f)
            };
            for (int i = 0; i < points.Length; i++)
                Place(visual, catalog, ArtAssetTechnicalSet.GrassId, "Трава " + (i + 1) + " (тест)", points[i].x, points[i].y, ArtAssetView.Front, i % 2 == 1);
        }

        // ПР-12Р: раскидка технической травой — низкая ковром у дома, высокая
        // объектами справа (герой заходит за неё). Разброс размера, поворота,
        // тона и яркости — по кисти; повтор даёт то же (постоянное зерно).
        private static void PlaceScatter(LocationVisualDefinition visual, LocalLocationDefinition location)
        {
            if (location == null) return;
            if (visual.ScatterLayers == null) visual.ScatterLayers = new List<LocationScatterLayer>();
            LocationScatterLayer carpet = new LocationScatterLayer { Name = "Низкая трава (тест)", Mode = LocationScatterMode.Carpet, Band = LocationVisualBand.GroundDetail };
            carpet.Assets.Add(new LocationScatterEntry { AssetId = ArtAssetTechnicalSet.GrassId });
            carpet.Brush.Radius = 220;
            carpet.Brush.Density = 10;
            carpet.Brush.MinDistance = 8;
            carpet.Brush.Scale = new Vector2(.45f, .75f);
            carpet.Brush.Rotation = new Vector2(-6, 6);
            carpet.Brush.HueJitter = 12;
            carpet.Brush.Brightness = new Vector2(.8f, 1.1f);
            LocationScatterLayer tall = new LocationScatterLayer { Name = "Высокая трава (тест)", Mode = LocationScatterMode.Objects, Band = LocationVisualBand.World, ProjectsShadow = true };
            tall.Assets.Add(new LocationScatterEntry { AssetId = ArtAssetTechnicalSet.GrassId });
            tall.Brush.Radius = 160;
            tall.Brush.Density = 2.5f;
            tall.Brush.MinDistance = 26;
            tall.Brush.Scale = new Vector2(.9f, 1.4f);
            tall.Brush.Stretch = new Vector2(.85f, 1.2f);
            tall.Brush.Rotation = new Vector2(-5, 5);
            tall.Brush.HueShift = -8;
            tall.Brush.HueJitter = 10;
            System.Random random = new System.Random(12);
            foreach (Vector2 center in new[] { new Vector2(560, 820), new Vector2(820, 860) })
                LocationScatterPainter.Stamp(carpet, location, center, random, LocationScatterPainter.Mask(carpet.Brush, location, visual),
                    LocationScatterIndex.Of(carpet, location));
            LocationScatterPainter.Stamp(tall, location, new Vector2(1500, 860), random, LocationScatterPainter.Mask(tall.Brush, location, visual),
                LocationScatterIndex.Of(tall, location));
            visual.ScatterLayers.Add(carpet);
            visual.ScatterLayers.Add(tall);
        }

        // Для пакетного запуска (-executeMethod).
        public static void EnsureBatch()
        {
            Ensure();
            AssetDatabase.SaveAssets();
        }
    }
}
