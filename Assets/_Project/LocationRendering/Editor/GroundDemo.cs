using System.Collections.Generic;
using System.IO;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12О: техническое место «Земля из участков» — проверка импорта,
    // швов, высоты и камеры на карте 4×4 без настоящего арта. Повторный вызов
    // переимпортирует тот же пакет: дублей нет, точки места сохраняются.
    // Не игровое место и не факт мира.
    public static class GroundDemo
    {
        public const string LocationId = "technical_tiled_ground";
        public const string ExportFolder = "Temp/KSGroundDemo";

        [MenuItem("Kingdom Survival/Технические/Земля из участков (демо 4×4)")]
        public static void CreateFromMenu()
        {
            LocalLocationDatabaseAsset database = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            if (!Create(database, out string message))
            {
                EditorUtility.DisplayDialog("Земля из участков", message, "Понятно");
                return;
            }
            Debug.Log(message);
            LocationDatabaseWindow.OpenWindow();
        }

        public static GroundSyntheticExport DemoExport() => new GroundSyntheticExport
        {
            MapId = "KS_Demo_Tiles", Columns = 4, Rows = 4, Width = 640, Height = 360, HeightMin = -2, HeightMax = 4, Q = .02
        };

        public static bool Create(LocalLocationDatabaseAsset database, out string message)
        {
            LocationLightingTestBootstrap.EnsureCamp();
            if (database == null) { message = "Не найдена База локаций."; return false; }
            EnsureLocation(database);
            string folder = Path.GetFullPath(ExportFolder);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            string manifest = DemoExport().Write(folder);
            GroundExportPackage package = GroundExportPackage.Load(manifest);
            LocalLocationDefinition location = database.locations.Find(item => item.Id == LocationId);
            GroundImporter.Plan plan = GroundImporter.Analyze(package, location, database.FindVisual(LocationId));
            return GroundImporter.Apply(plan, database, (target, width, height) => LocationDatabaseWindow.ResizeLocation(target, width, height, target.HexesAcross, true), out message);
        }

        public static void EnsureLocation(LocalLocationDatabaseAsset database)
        {
            if (database.locations.Find(item => item.Id == LocationId) == null)
            {
                Undo.RecordObject(database, "Демо: земля из участков");
                database.locations.Add(new LocalLocationDefinition
                {
                    Id = LocationId, WorldLocationId = "__technical_tiled_ground", DisplayName = "Земля из участков (тех.)",
                    BattlefieldId = LocationLightingTestBootstrap.FieldId, PlaceholderArt = true, CanvasWidth = 2560, CanvasHeight = 1440,
                    HexesAcross = 80, BattleFrameWidth = 1920,
                    Entrances = new List<LocalEntranceDefinition> { new LocalEntranceDefinition { Id = "entry", Label = "Вход", Point = new LocalPointData(200, 1240) } }
                });
                EditorUtility.SetDirty(database);
            }
            if (database.FindVisual(LocationId) == null)
            {
                Undo.RecordObject(database, "Демо: земля из участков");
                LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = LocationId, TechnicalTest = true, TestStartPoint = new Vector2(.08f, .86f), TestFollowers = 2 };
                LocationVisualDefinition camp = database.FindVisual(LocationLightingTestBootstrap.CampId);
                if (camp != null) visual.TestUnitId = camp.TestUnitId;
                database.visuals.Add(visual);
                EditorUtility.SetDirty(database);
            }
        }
    }
}
