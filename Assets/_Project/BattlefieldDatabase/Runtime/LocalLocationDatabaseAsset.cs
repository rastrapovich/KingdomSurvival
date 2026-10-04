using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12К (канон v1.54 §28.3): исследуемые места — рисунок своего размера,
    // разметка местности (как у глобальной карты), входы, объекты,
    // противники, зоны угрозы и кадры боя. Сетка боя — у поля Базы полей боя
    // (BattlefieldId), здесь не дублируется. Правятся в окне «База локаций».
    // Записи заменяют значения по умолчанию модуля содержания с тем же ID;
    // кнопка «Проверить» в Inspector запускает проверку места.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalLocalLocations",
        menuName = "Kingdom Survival/Исследуемые места")]
    public sealed class LocalLocationDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "BattlefieldDatabase/KingdomSurvivalLocalLocations";
        public const string AssetPath = "Assets/_Project/BattlefieldDatabase/Resources/BattlefieldDatabase/KingdomSurvivalLocalLocations.asset";

        [Tooltip("Исследуемые места. Точки — пиксели рисунка места (Y вниз).")]
        public List<LocalLocationDefinition> locations = new List<LocalLocationDefinition>();

        // Художественная сборка ссылается на ID места: игровые данные остаются выше.
        public List<LocationVisualDefinition> visuals = new List<LocationVisualDefinition>();

        // 1 — места в точках рисунка (канон v1.54); 0 — прежние клетки поля,
        // переводятся один раз при загрузке редактора.
        public int pointFormatVersion;
        public const int CurrentPointFormatVersion = 1;

        public LocationVisualDefinition FindVisual(string id) => visuals?.Find(item => item != null && item.LocationId == id);

        // Подставить правленые места в каталог ядра (при запуске игры).
        public static void ApplyToCatalog()
        {
            LocalLocationDatabaseAsset asset = Resources.Load<LocalLocationDatabaseAsset>(ResourcesPath);
            // База в прежнем формате клеток не подставляется: места кода целы.
            LocalLocationCatalog.SetOverrides(asset != null && asset.locations != null && asset.locations.Count > 0 &&
                                              asset.pointFormatVersion >= CurrentPointFormatVersion
                ? asset.locations
                : null);
        }
    }
}
