using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.FreePlay.Editor
{
    // ПР-12К: база исследуемых мест заводится один раз из значений по
    // умолчанию (пилот «Старая шахта»), дальше правится в окне «База
    // локаций». Существующая база не перезаписывается — кроме однократного
    // перевода из прежнего формата клеток поля в точки рисунка (канон v1.54).
    [InitializeOnLoad]
    public static class LocalLocationDatabaseBootstrap
    {
        static LocalLocationDatabaseBootstrap()
        {
            EditorApplication.delayCall += EnsureAsset;
        }

        public static void EnsureAsset()
        {
            LocalLocationDatabaseAsset existing = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            if (existing != null)
            {
                MigrateToPoints(existing);
                EnsureMineVisual(existing);
                EnsureWorldLighting(existing);
                return;
            }
            LocalLocationDatabaseAsset asset = ScriptableObject.CreateInstance<LocalLocationDatabaseAsset>();
            asset.locations.Add(FreePlayMineLocal.Create());
            asset.visuals.Add(CreateMineVisual());
            asset.pointFormatVersion = LocalLocationDatabaseAsset.CurrentPointFormatVersion;
            asset.worldLightingInitialized = true;
            AssetDatabase.CreateAsset(asset, LocalLocationDatabaseAsset.AssetPath);
            AssetDatabase.SaveAssets();
        }

        // ПР-12М: шахта — место под крышей: постоянный тусклый свет, днём —
        // свет от входа [РАБОЧЕЕ — ТЕХНИЧЕСКАЯ НАСТРОЙКА]. Рисунка нет — заглушка.
        public static bool EnsureMineVisual(LocalLocationDatabaseAsset asset)
        {
            if (asset == null || asset.FindVisual(FreePlayMineLocal.LocalLocationId) != null)
                return false;
            asset.visuals.Add(CreateMineVisual());
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return true;
        }

        // ПР-12М: общий свет мира заводится один раз — из неба первого места
        // на улице (технический лагерь), чтобы прежние правки не пропали.
        public static bool EnsureWorldLighting(LocalLocationDatabaseAsset asset)
        {
            if (asset == null || asset.worldLightingInitialized)
                return false;
            LocationVisualDefinition outdoor = asset.visuals.Find(item => item != null && item.Lighting == LocationLightingMode.Outdoor);
            asset.worldLighting = outdoor != null ? LocationSky.FromOwn(outdoor) : new LocationWorldLighting();
            asset.worldLightingInitialized = true;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return true;
        }

        public static LocationVisualDefinition CreateMineVisual()
        {
            LocationVisualDefinition visual = new LocationVisualDefinition
            {
                LocationId = FreePlayMineLocal.LocalLocationId,
                Lighting = LocationLightingMode.Indoor,
                IndoorColor = new Color(.58f, .53f, .47f),
                IndoorIntensity = .5f,
                TestStartPoint = new Vector2(368f / FreePlayMineLocal.CanvasWidth, 551f / FreePlayMineLocal.CanvasHeight),
                TestUnitId = "militia",
                TestFollowers = 2
            };
            LocationVisualObject entrance = new LocationVisualObject
            {
                Name = "Свет от входа",
                LightOnly = true,
                ProjectsShadow = false,
                Position = new Vector2(250f / FreePlayMineLocal.CanvasWidth, 551f / FreePlayMineLocal.CanvasHeight)
            };
            entrance.Light.Enabled = true;
            entrance.Light.Color = new Color(1f, .93f, .80f);
            entrance.Light.Intensity = 1.1f;
            entrance.Light.Radius = 6;
            entrance.Light.Softness = .9f;
            entrance.Light.Animation = LocationLightAnimation.None;
            entrance.Light.Height = 2.5f;
            // Свет снаружи — только днём.
            entrance.Light.NightOnly = true;
            entrance.Light.StartsAt = 6;
            entrance.Light.EndsAt = 20;
            visual.Objects.Add(entrance);
            return visual;
        }

        // Клетки прежнего формата не переводятся в точки один к одному:
        // пилот шахты берётся заново из кода, у прочих мест вход — в центр
        // рисунка (разметки нет — всё проходимо). Художественная сборка цела.
        public static bool MigrateToPoints(LocalLocationDatabaseAsset asset)
        {
            if (asset == null || asset.pointFormatVersion >= LocalLocationDatabaseAsset.CurrentPointFormatVersion)
                return false;
            for (int i = 0; i < asset.locations.Count; i++)
            {
                LocalLocationDefinition location = asset.locations[i];
                if (location == null)
                    continue;
                if (location.Id == FreePlayMineLocal.LocalLocationId)
                {
                    asset.locations[i] = FreePlayMineLocal.Create();
                    continue;
                }
                if (location.Movement == null)
                    location.Movement = LocalLocationDefinition.DefaultMovement();
                foreach (LocalEntranceDefinition entrance in location.Entrances)
                {
                    if (entrance != null && (entrance.Point == null || (entrance.Point.X == 0 && entrance.Point.Y == 0)))
                        entrance.Point = new LocalPointData(location.CanvasWidth / 2f, location.CanvasHeight / 2f);
                }
            }
            asset.pointFormatVersion = LocalLocationDatabaseAsset.CurrentPointFormatVersion;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            Debug.Log("База локаций переведена в точки рисунка места (канон v1.54).");
            return true;
        }
    }
}
