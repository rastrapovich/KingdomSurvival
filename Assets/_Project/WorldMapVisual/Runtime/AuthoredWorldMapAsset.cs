using UnityEngine;

namespace KingdomSurvival.WorldMapVisual
{
    // 12Ф «Редактор карты» (канон v1.71 §9.9.1): авторская карта свободной
    // игры. Хранит чистые данные AuthoredWorldMapData; правится окном
    // Kingdom Survival → Редактор карты. Новая партия собирает из неё карту
    // (AuthoredWorldMapBuilder) — генератор географию больше не строит.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalAuthoredWorldMap",
        menuName = "Kingdom Survival/Карта/Авторская карта")]
    public sealed class AuthoredWorldMapAsset : ScriptableObject
    {
        public const string ResourcesPath = "WorldMapVisual/KingdomSurvivalAuthoredWorldMap";
        public const string AssetPath = "Assets/_Project/WorldMapVisual/Resources/WorldMapVisual/KingdomSurvivalAuthoredWorldMap.asset";

        [SerializeField] private AuthoredWorldMapData data = AuthoredWorldMapData.CreateDefault();

        public AuthoredWorldMapData Data
        {
            get
            {
                if (data == null)
                    data = AuthoredWorldMapData.CreateDefault();
                return data;
            }
        }

        public void Replace(AuthoredWorldMapData fresh)
        {
            data = fresh ?? AuthoredWorldMapData.CreateDefault();
            data.Invalidate();
        }

        public GeneratedWorldMap Build(int seed) => AuthoredWorldMapBuilder.Build(Data, seed);

        // После Undo и загрузки ассета разобранные массивы надо прочитать заново.
        private void OnValidate() => data?.Invalidate();
    }
}
