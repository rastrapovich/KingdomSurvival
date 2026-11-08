using KingdomSurvival.MapLab;
using KingdomSurvival.WorldMapVisual;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Editor
{
    // 12У-G (запрос пользователя 08.11.2026): общие настройки игры в окне
    // «Генератор карты» — не в самой игре. Общая скорость передвижения по
    // карте (герой и все группы), независимое время и его скорость. Значения
    // пишутся в ассет настроек движения (Resources), игра читает их при старте
    // и сразу, если она запущена.
    [InitializeOnLoad]
    public static class MapLabGameSettingsSection
    {
        private static bool open = true;

        static MapLabGameSettingsSection()
        {
            MapLabPlanSource.DrawGameSettings = Draw;
        }

        private static WorldMapMovementSettingsAsset Asset() =>
            AssetDatabase.LoadAssetAtPath<WorldMapMovementSettingsAsset>(WorldMapMovementSettingsAsset.AssetPath);

        private static void Draw()
        {
            open = EditorGUILayout.Foldout(open, "Время и движение (игра)", true);
            if (!open)
                return;
            WorldMapMovementSettingsAsset asset = Asset();
            if (asset == null)
            {
                EditorGUILayout.HelpBox("Нет ассета настроек движения: " + WorldMapMovementSettingsAsset.AssetPath, MessageType.Warning);
                return;
            }
            EditorGUI.BeginChangeCheck();
            float speed = EditorGUILayout.Slider(new GUIContent("Скорость по карте, гексов/с",
                "Общая скорость передвижения по карте для всех: герой, обозы, дозоры, мастера, ватаги (у каждой группы своя доля этой скорости). Внутри поселения герой идёт на 70% этой скорости."),
                asset.HeroRunSpeedHexesPerSecond, 0.1f, 4f);
            bool independent = EditorGUILayout.Toggle(new GUIContent("Время идёт само",
                "Часы мира идут ровно, даже когда герой стоит; группы движутся по часам сами. Выключено — прежняя модель: время идёт, только пока отряд в пути или занят делом."),
                asset.IndependentTime);
            float hours;
            using (new EditorGUI.DisabledScope(!independent))
                hours = EditorGUILayout.Slider(new GUIContent("Скорость времени, ч/с",
                    "Игровых часов за секунду реального времени. Кнопки ×1/×3 в игре умножают её."),
                    asset.WorldGameHoursPerRealSecond, 0.02f, 2f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(asset, "Время и движение");
                asset.SetPace(speed, independent, hours);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            if (independent)
            {
                double hexHours = hours / Mathf.Max(0.01f, speed);
                EditorGUILayout.HelpBox(
                    $"Сутки длятся {24f / Mathf.Max(0.01f, hours):0} с. Клетка открытой местности — {hexHours:0.##} ч пути " +
                    $"({speed:0.##} гекса/с). Обоз идёт со скоростью ~{0.45f * speed:0.##} гекса/с, дозор ~{0.75f * speed:0.##}.",
                    MessageType.None);
            }
            else
                EditorGUILayout.HelpBox($"Прежняя модель: время идёт при движении; клетка — {asset.TravelHoursPerHex:0.##} ч.", MessageType.None);
        }
    }
}
