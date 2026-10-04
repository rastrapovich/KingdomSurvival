using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase.Editor
{
    // ПР-12К: Inspector базы исследуемых мест — поля мест и проверка до игры:
    // ID, поле, входы, объекты, противники, зоны угрозы, точка отхода,
    // диалоги и существа.
    [CustomEditor(typeof(LocalLocationDatabaseAsset))]
    public sealed class LocalLocationDatabaseAssetEditor : UnityEditor.Editor
    {
        private readonly List<string> lastIssues = new List<string>();
        private bool checkedOnce;

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Исследуемые места (ПР-12К). Фон, сетка и стены — у поля в Базе полей боя (BattlefieldId). " +
                "Клетки — (Q, R) арены 7/8/9/10/9/8/7. Объект стоит на стене или краю, к нему подходят с соседней клетки.",
                MessageType.Info);
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("Проверить места"))
            {
                Check((LocalLocationDatabaseAsset)target, lastIssues);
                checkedOnce = true;
            }
            if (checkedOnce)
            {
                if (lastIssues.Count == 0)
                    EditorGUILayout.HelpBox("Ошибок нет.", MessageType.Info);
                foreach (string issue in lastIssues)
                    EditorGUILayout.HelpBox(issue, MessageType.Error);
            }
        }

        public static void Check(LocalLocationDatabaseAsset asset, List<string> issues)
        {
            issues.Clear();
            BattlefieldDatabaseAsset battlefields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
            DialogueDatabaseAsset dialogues = DialogueDatabaseRuntime.LoadDefaultDatabase();
            UnitDatabaseAsset units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
            HashSet<string> ids = new HashSet<string>();
            foreach (LocalLocationDefinition location in asset.locations)
            {
                if (location != null && !ids.Add(location.Id))
                    issues.Add("Повторный ID места: " + location.Id + ".");
                LocationVisualDefinition visual = location != null ? asset.FindVisual(location.Id) : null;
                BattlefieldDefinitionData field = location != null && battlefields != null ? battlefields.FindById(location.BattlefieldId) : null;
                issues.AddRange(LocalLocationValidator.Validate(location, battlefields,
                    id => dialogues != null && dialogues.FindDialogue(id) != null,
                    id => units != null && units.FindById(id) != null,
                    visual != null && field != null ? LocationVisualGeometry.BlockedCells(visual, field) : null));
            }
        }
    }
}
