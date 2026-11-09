using KingdomSurvival.MapLab;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // 12У-F: всё, что нужно MapLab в игре (и в сборке без AssetDatabase):
    // профиль рисунка, наборы арта, шейдеры. Лежит в Resources, создаётся и
    // обновляется в окне «Kingdom Survival → Генератор карты» → «Сервис» →
    // Подготовить рисунок MapLab для игры».
    [CreateAssetMenu(menuName = "Kingdom Survival/MapLab/Game Assets", fileName = "MapLabGameAssets")]
    public sealed class MapLabGameAssets : ScriptableObject
    {
        public const string ResourcesPath = "MapLabGameAssets";

        [Tooltip("Рисовать карту партии MapLab. Выключено — прежний рисовальщик карты.")]
        public bool UseInGame = true;
        public MapLabSettings Profile;
        [Tooltip("Обычная арена боя (дорожный бой, полигон) — рисунок MapLab по раскладке боя: холмы на клетках холма, лес по краям. Выключено — прежняя картинка поля.")]
        public bool ArenaInBattle = true;
        [Tooltip("Текселей на единицу мира в снимке карты (больше — чётче вблизи, дольше и тяжелее).")]
        [Range(2f, 12f)] public float TexelsPerUnit = 6f;
        [Tooltip("Наклон камеры снимка, градусов (рабочий наклон рисунков — 50°).")]
        [Range(30f, 70f)] public float PitchDegrees = 50f;
        public int TileSize = 2048;

        public MapLabArtSet ArtSet02;
        public MapLabMarkerSet MarkerSet02;
        public MapLabArtSet ArtSet03;
        public MapLabMarkerSet MarkerSet04;
        public MapLabArtSet ArtSet05;
        public MapLabMarkerSet MarkerSet05;
        public MapLabArtSet ArtSet06;
        public MapLabArtSet ArtSetPoi;

        public Shader Terrain, Water, Card, Solid, Lines, ArtCard, Contact, Ink, Grass, Module, Cliff;

        private static MapLabGameAssets loaded;
        private static bool searched;

        public static MapLabGameAssets Load()
        {
            if (!searched || loaded == null)
            {
                loaded = Resources.Load<MapLabGameAssets>(ResourcesPath);
                searched = true;
            }
            return loaded;
        }

        public void ApplyTo(MapLabView view)
        {
            view.ArtSet = ArtSet02;
            view.MarkerSet = MarkerSet02;
            view.ArtSet03 = ArtSet03;
            view.MarkerSet04 = MarkerSet04;
            view.ArtSet05 = ArtSet05;
            view.MarkerSet05 = MarkerSet05;
            view.ArtSet06 = ArtSet06;
            view.ArtSetPoi = ArtSetPoi;
            view.TerrainShader = Terrain;
            view.WaterShader = Water;
            view.CardShader = Card;
            view.SolidShader = Solid;
            view.LinesShader = Lines;
            view.ArtCardShader = ArtCard;
            view.ContactShader = Contact;
            view.InkShader = Ink;
            view.GrassShader = Grass;
            view.ModuleShader = Module;
            view.CliffShader = Cliff;
        }
    }
}
