using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.ArtAssets
{
    // ПР-12Н: единый каталог художественных объектов (дома, деревья, телеги,
    // камни, предметы, поверхности). Места хранят ссылку (Id), ракурс и свои
    // настройки экземпляра, а не копии рисунков. Каталог лежит в Resources:
    // в сборке он доступен без AssetDatabase и внешних путей. Правится в окне
    // «Kingdom Survival/База ассетов».
    [CreateAssetMenu(fileName = "KingdomSurvivalArtAssets", menuName = "Kingdom Survival/База ассетов")]
    public sealed class ArtAssetDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "ArtAssetDatabase/KingdomSurvivalArtAssets";
        public const string AssetPath = "Assets/_Project/ArtAssetDatabase/Resources/ArtAssetDatabase/KingdomSurvivalArtAssets.asset";

        public List<ArtAssetDefinition> assets = new List<ArtAssetDefinition>();

        // Каталог изменился (правка в окне, Undo): места пересобирают показ.
        public static event Action Changed;

        // Тесты и предпросмотры подставляют свой каталог.
        public static ArtAssetDatabaseAsset Override;

        private static ArtAssetDatabaseAsset loaded;
        private static bool loadAttempted;

        [NonSerialized] private Dictionary<string, ArtAssetDefinition> index;
        [NonSerialized] private int indexedRevision = -1;
        [NonSerialized] private int indexedCount = -1;
        [NonSerialized] public int Revision;

        public static ArtAssetDatabaseAsset Current
        {
            get
            {
                if (Override != null) return Override;
                if (loaded == null && !loadAttempted)
                {
                    loadAttempted = true;
                    loaded = Resources.Load<ArtAssetDatabaseAsset>(ResourcesPath);
                }
                return loaded;
            }
        }

        // Редактор создал каталог позже первого обращения.
        public static void ResetCurrent()
        {
            loaded = null;
            loadAttempted = false;
        }

        public static ArtAssetDefinition FindCurrent(string id) => string.IsNullOrEmpty(id) ? null : Current?.Find(id);

        public ArtAssetDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id) || assets == null) return null;
            // Список мог измениться без MarkChanged (прямая правка): сверяем число записей.
            if (index == null || indexedRevision != Revision || indexedCount != assets.Count)
                RebuildIndex();
            if (index.TryGetValue(id, out ArtAssetDefinition found) && found != null && found.Id == id)
                return found;
            if (found == null) return null;
            RebuildIndex();
            return index.TryGetValue(id, out found) ? found : null;
        }

        private void RebuildIndex()
        {
            index = new Dictionary<string, ArtAssetDefinition>(StringComparer.Ordinal);
            foreach (ArtAssetDefinition asset in assets)
            {
                if (asset != null && !string.IsNullOrEmpty(asset.Id) && !index.ContainsKey(asset.Id))
                    index.Add(asset.Id, asset);
            }
            indexedRevision = Revision;
            indexedCount = assets.Count;
        }

        public void MarkChanged()
        {
            Revision++;
            Changed?.Invoke();
        }

        private void OnValidate()
        {
            // Undo/Redo и правка в Inspector: индекс и показ мест пересобираются.
            Revision++;
            Changed?.Invoke();
        }
    }
}
