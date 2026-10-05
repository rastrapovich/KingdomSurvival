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
    // нормали и перекрытие героя крышей и кроной. Не игровое место.
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
            if (database.locations.Any(item => item.Id == Id) && database.FindVisual(Id) != null) return;
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
            void Place(string assetId, string name, float x, float y, ArtAssetView view)
            {
                ArtAssetDefinition asset = catalog.Find(assetId);
                if (asset == null) return;
                ArtAssetViewSettings settings = asset.Settings(view);
                visual.Objects.Add(new LocationVisualObject
                {
                    Name = name, AssetId = assetId, View = view, Position = new Vector2(x, y), Height = asset.Height, Pivot = settings.Pivot,
                    BlocksMovement = asset.BlocksMovement, Footprint = settings.FootprintSize, CastsShadow = asset.OccludesLight
                });
            }
            Place(ArtAssetTechnicalSet.HouseId, "Дом (тест)", .5f, .42f, ArtAssetView.Front);
            Place(ArtAssetTechnicalSet.TreeId, "Дерево (тест)", .25f, .5f, ArtAssetView.FrontRight);
            Place(ArtAssetTechnicalSet.TreeId, "Дерево 2 (тест)", .78f, .55f, ArtAssetView.Back);
            Place(ArtAssetTechnicalSet.CrateId, "Ящик (тест)", .4f, .7f, ArtAssetView.FrontLeft);
            Place(ArtAssetTechnicalSet.CrateId, "Ящик 2 (тест)", .62f, .74f, ArtAssetView.Front);
            LocationVisualObject fire = new LocationVisualObject { Name = "Свет у дома", LightOnly = true, Position = new Vector2(.36f, .6f) };
            fire.Light.Enabled = true; fire.Light.Radius = 5; fire.Light.Intensity = 1.6f; fire.Light.NormalMaps = true; fire.Light.NightOnly = true;
            visual.Objects.Add(fire);
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssetIfDirty(database);
        }

        // Для пакетного запуска (-executeMethod).
        public static void EnsureBatch()
        {
            Ensure();
            AssetDatabase.SaveAssets();
        }
    }
}
