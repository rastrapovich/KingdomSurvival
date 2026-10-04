using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;

namespace KingdomSurvival.FreePlay.Editor
{
    // ПР-12К: база исследуемых мест заводится один раз из значений по
    // умолчанию (пилот «Старая шахта»), дальше правится в Inspector.
    // Существующая база не перезаписывается.
    [InitializeOnLoad]
    public static class LocalLocationDatabaseBootstrap
    {
        static LocalLocationDatabaseBootstrap()
        {
            EditorApplication.delayCall += EnsureAsset;
        }

        public static void EnsureAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath) != null)
                return;
            LocalLocationDatabaseAsset asset = UnityEngine.ScriptableObject.CreateInstance<LocalLocationDatabaseAsset>();
            asset.locations.Add(FreePlayMineLocal.Create());
            AssetDatabase.CreateAsset(asset, LocalLocationDatabaseAsset.AssetPath);
            AssetDatabase.SaveAssets();
        }
    }
}
