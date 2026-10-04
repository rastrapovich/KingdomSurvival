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
                return;
            }
            LocalLocationDatabaseAsset asset = ScriptableObject.CreateInstance<LocalLocationDatabaseAsset>();
            asset.locations.Add(FreePlayMineLocal.Create());
            asset.pointFormatVersion = LocalLocationDatabaseAsset.CurrentPointFormatVersion;
            AssetDatabase.CreateAsset(asset, LocalLocationDatabaseAsset.AssetPath);
            AssetDatabase.SaveAssets();
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
