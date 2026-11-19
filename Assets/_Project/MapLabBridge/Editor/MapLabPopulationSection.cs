using System.Linq;
using System.Text;
using KingdomSurvival.WorldMapVisual;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge.Editor
{
    // Бриф «Живое население глобальной карты» (19.11.2026): компактная секция
    // «Население и угрозы» окна «Генератор карты». Правит настройки слоя в
    // ассете генератора (WorldMapGenerationSettings.Population): включение,
    // бюджеты, отступы, радиусы, ритм, резервы, фоновые столкновения и
    // пресеты (составы по UnitTypeId, биотопы, поведение, тексты признаков).
    // Кнопка «Сводка» строит план для seed и выводит отчёт размещения с
    // причинами пропусков. Схема плана показывает источники и территории.
    public static class MapLabPopulationSection
    {
        private static bool open;
        private static bool presetsOpen;
        private static int seed = 1;

        public static void Draw()
        {
            open = EditorGUILayout.Foldout(open, "Население и угрозы (игра)", true);
            if (!open)
                return;
            WorldMapGeneratorAsset generator = WorldMapVisualRuntime.LoadGenerator();
            if (generator == null)
            {
                EditorGUILayout.HelpBox("Нет ассета генератора карты в базе карты.", MessageType.Warning);
                return;
            }
            SerializedObject so = new SerializedObject(generator);
            SerializedProperty population = so.FindProperty("settings.Population");
            if (population == null)
            {
                EditorGUILayout.HelpBox("В ассете генератора нет секции населения — сохраните ассет после импорта.", MessageType.Warning);
                return;
            }
            EditorGUILayout.HelpBox("[РАБОЧЕЕ — ПРОТОТИП] Составы, биотопы и числа — рабочие настройки, не канон. Дистанции — клетки, время — игровые часы. " +
                                    "«Враги» при новой партии меняют только враждебные бюджеты.", MessageType.None);
            (string Name, string Label)[] fields =
            {
                ("Enabled", "Слой включён"),
                ("OrdinaryThreatBudget", "Обычные угрозы, не больше"),
                ("StrongThreatBudget", "Сильные угрозы, не больше"),
                ("CreatureBudget", "Необычные обитатели, не больше"),
                ("LandHexesPerOrdinaryThreat", "Суши на обычную угрозу, кл."),
                ("LandHexesPerCreature", "Суши на обитателя, кл."),
                ("StartSafeRadiusHexes", "Защита старта, кл."),
                ("ServiceSafeRadiusHexes", "Защита деревень, кл."),
                ("MinSourceDistanceHexes", "Между источниками угроз, кл."),
                ("StrongMinStartDistanceHexes", "Сильная угроза от старта, кл."),
                ("ContactHexes", "Контакт, кл."),
                ("TraceHexes", "Следы видны с, кл."),
                ("CampApproachHexes", "Подход к лагерю, кл."),
                ("TalkHexes", "Разговор с группой, кл."),
                ("RespiteHours", "Передышка после боя, ч"),
                ("RepeatContactHours", "Повтор контакта той же группы, ч"),
                ("ChaseMaxHours", "Погоня не дольше, ч"),
                ("ConflictWindowHours", "Окно вмешательства, ч"),
                ("PredatorObserveHours", "Стая наблюдает до решения, ч"),
                ("BandReserve", "Резерв лагеря, вылазок"),
                ("BandRefillHours", "Пополнение лагеря, ч"),
                ("BeastRefillHours", "Новый зверь на участке, ч"),
                ("ReplacementHours", "Замена обоза/дозора, ч"),
                ("NpcConflicts", "Столкновения групп без героя"),
                ("DemandMinimum", "Плата банде, минимум"),
                ("DemandShare", "Плата банде, доля серебра"),
            };
            foreach ((string name, string label) in fields)
            {
                SerializedProperty p = population.FindPropertyRelative(name);
                if (p != null)
                    EditorGUILayout.PropertyField(p, new GUIContent(label));
            }
            presetsOpen = EditorGUILayout.Foldout(presetsOpen, "Пресеты групп (составы, биотопы, поведение)", true);
            if (presetsOpen)
            {
                EditorGUILayout.HelpBox("Роль: patrol / caravan / band / beast / creature. Поведение: " + string.Join(", ", WorldPopulationBehavior.All) +
                                        ". Биотоп: " + string.Join(", ", WorldPopulationBiome.All) + ". Бюджет: peaceful / ordinary / strong / creature.", MessageType.None);
                EditorGUILayout.PropertyField(population.FindPropertyRelative("Presets"), new GUIContent("Пресеты"), true);
            }
            if (so.ApplyModifiedProperties())
            {
                EditorUtility.SetDirty(generator);
                AssetDatabase.SaveAssetIfDirty(generator);
                WorldPopulationRules.Current = generator.Settings.Population?.Clone();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                seed = EditorGUILayout.IntField("Seed сводки", seed);
                if (GUILayout.Button("Сводка населения", GUILayout.Width(150)))
                    LogSummary(generator, seed);
            }
        }

        private static void LogSummary(WorldMapGeneratorAsset generator, int planSeed)
        {
            GeneratedWorldMap plan = WorldMapGenerator.Generate(generator.Settings, planSeed);
            GeneratedPopulationReport report = plan.PopulationReport;
            StringBuilder text = new StringBuilder();
            text.AppendLine("Seed " + planSeed + ". " + report.Summary());
            text.AppendLine("Группы по ролям: " + string.Join(", ", plan.Actors.GroupBy(a => a.Role).Select(g => g.Key + " ×" + g.Count())));
            foreach (GeneratedPopulationNote note in report.Notes)
                text.AppendLine((note.Placed ? "  + " : "  − ") + note.Message);
            Debug.Log(text.ToString());
        }
    }
}
