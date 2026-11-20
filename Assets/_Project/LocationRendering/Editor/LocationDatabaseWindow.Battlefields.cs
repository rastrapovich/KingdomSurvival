using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // 20.11.2026, просьба пользователя: собранных вручную полей боя (место
    // вида «Поле боя» — рисунок поля из ассетов) больше нет. Обычная арена
    // боя генерируется (окно «Kingdom Survival → Генератор арен»), там же —
    // общие настройки полей боя (ходьба, сетка и вид гекса поля полигона).
    // Записи базы полей боя (сетка, отключённые гексы, вид гекса, теги)
    // остаются: их берут бой на месте и сетка арены.
    public sealed partial class LocationDatabaseWindow
    {
        private Rect FieldFrameOnCanvas(BattlefieldDefinitionData field) => BattlefieldFrame.FrameOnPicture(CanvasSize, field);

        // Убрать прежние сборки полей боя (места вида «Поле боя» и их рисунки).
        // Возвращает число убранных; повторный вызов ничего не меняет.
        public static int RemoveBattlefieldScenes(LocalLocationDatabaseAsset locations)
        {
            if (locations == null || locations.locations == null) return 0;
            List<LocalLocationDefinition> scenes = locations.locations.Where(item => item != null && item.IsBattlefield).ToList();
            if (scenes.Count == 0) return 0;
            Undo.RecordObject(locations, "Убрать собранные поля боя");
            HashSet<string> ids = new HashSet<string>(scenes.Select(item => item.Id));
            locations.locations.RemoveAll(item => item != null && ids.Contains(item.Id));
            locations.visuals?.RemoveAll(item => item != null && ids.Contains(item.LocationId));
            EditorUtility.SetDirty(locations);
            AssetDatabase.SaveAssetIfDirty(locations);
            Debug.Log("База локаций: убраны собранные вручную поля боя — " + string.Join(", ", scenes.Select(item => item.DisplayName)) +
                      ". Арены генерируются (окно «Генератор арен»).");
            return scenes.Count;
        }

        // Для запуска без окна (batchmode).
        public static void RemoveBattlefieldScenesInProject()
        {
            LocalLocationDatabaseAsset locations = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            Debug.Log("Собранных полей боя убрано: " + RemoveBattlefieldScenes(locations));
        }
    }
}
