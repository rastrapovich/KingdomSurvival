using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12К (канон v1.53 §28.3): исследуемые места для правки в Inspector —
    // входы, объекты, противники, зоны угрозы, трудная местность. Фон, сетка
    // и стены — у поля Базы полей боя (BattlefieldId), здесь не дублируются.
    // Записи заменяют значения по умолчанию модуля содержания с тем же ID;
    // кнопка «Проверить» в Inspector запускает проверку места.
    [CreateAssetMenu(
        fileName = "KingdomSurvivalLocalLocations",
        menuName = "Kingdom Survival/Исследуемые места")]
    public sealed class LocalLocationDatabaseAsset : ScriptableObject
    {
        public const string ResourcesPath = "BattlefieldDatabase/KingdomSurvivalLocalLocations";
        public const string AssetPath = "Assets/_Project/BattlefieldDatabase/Resources/BattlefieldDatabase/KingdomSurvivalLocalLocations.asset";

        [Tooltip("Исследуемые места. Клетки — (столбец Q, ряд R) арены 7/8/9/10/9/8/7 поля места.")]
        public List<LocalLocationDefinition> locations = new List<LocalLocationDefinition>();

        // Подставить правленые места в каталог ядра (при запуске игры).
        public static void ApplyToCatalog()
        {
            LocalLocationDatabaseAsset asset = Resources.Load<LocalLocationDatabaseAsset>(ResourcesPath);
            LocalLocationCatalog.SetOverrides(asset != null && asset.locations != null && asset.locations.Count > 0
                ? asset.locations
                : null);
        }
    }
}
