using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.DialogueDatabase.Editor
{
    // Тестовые условия превью диалога: качества и компетенции героя, черты,
    // флаги, знания, отношения, спутники, предметы, seed и принудительный
    // исход проверки. Не связаны с игровым сохранением и живут только в
    // окне превью (UI/Editor/DialogueGamePreviewWindow.cs).
    [Serializable]
    public sealed class DialoguePreviewContext
    {
        public HeroProfileData Hero = new HeroProfileData();
        public string FlagsCsv = string.Empty;
        public string KnowledgeCsv = string.Empty;
        public string RelationsCsv = string.Empty;
        public string CompanionsCsv = string.Empty;
        public string ItemsCsv = string.Empty;
        public int WorldSeed = 12345;
        public NarrativeCheckForcedOutcome ForcedOutcome = NarrativeCheckForcedOutcome.None;
        // Авторский просмотр упущенного текста провалившихся пассивных
        // проверок. Выключен по умолчанию: превью показывает ровно то, что
        // видит игрок.
        public bool RevealHiddenTextForAuthor;

        private static readonly string[] ForcedOutcomeLabels = { "Честный бросок", "Всегда успех", "Всегда провал" };

        // Чистый запуск: история проверок и применённых эффектов обнуляется.
        public void Build(
            out HeroProfileData hero,
            out NarrativeStateData state,
            out List<string> companions,
            out List<string> items)
        {
            hero = Hero ?? new HeroProfileData();
            state = new NarrativeStateData();

            foreach (string flag in SplitCsv(FlagsCsv))
                state.SetFlag(flag);
            foreach (string knowledge in SplitCsv(KnowledgeCsv))
                state.AddKnowledge(knowledge);
            foreach (string pair in SplitCsv(RelationsCsv))
            {
                string[] parts = pair.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out int value))
                    state.ChangeRelation(parts[0].Trim(), value);
            }

            companions = SplitCsv(CompanionsCsv);
            items = SplitCsv(ItemsCsv);
        }

        // Возвращает true, если что-то изменилось (превью стоит перезапустить).
        public bool Draw()
        {
            if (Hero == null)
                Hero = new HeroProfileData();

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Герой", EditorStyles.boldLabel);
            Hero.Strength = EditorGUILayout.IntSlider("Сила", Hero.Strength, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);
            Hero.Dexterity = EditorGUILayout.IntSlider("Сноровка", Hero.Dexterity, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);
            Hero.Fortitude = EditorGUILayout.IntSlider("Стойкость", Hero.Fortitude, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);
            Hero.Instinct = EditorGUILayout.IntSlider("Чутьё", Hero.Instinct, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);
            Hero.Judgment = EditorGUILayout.IntSlider("Суждение", Hero.Judgment, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);
            Hero.Character = EditorGUILayout.IntSlider("Характер", Hero.Character, HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue);

            int fieldcraft = Hero.GetCompetency(NarrativeCompetencyIds.Fieldcraft);
            int nextFieldcraft = EditorGUILayout.IntSlider("Следопытство", fieldcraft, 0, 5);
            if (nextFieldcraft != fieldcraft)
                Hero.SetCompetency(NarrativeCompetencyIds.Fieldcraft, nextFieldcraft);

            DrawTrait(NarrativeTraitIds.KnowsTheWay, "Знающий дорогу");
            DrawTrait(NarrativeTraitIds.Naturalist, "Натуралист");

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Состояние мира (через запятую)", EditorStyles.boldLabel);
            FlagsCsv = EditorGUILayout.TextField("Флаги", FlagsCsv);
            KnowledgeCsv = EditorGUILayout.TextField("Знания", KnowledgeCsv);
            RelationsCsv = EditorGUILayout.TextField(new GUIContent("Отношения", "Формат: id:значение, id:значение"), RelationsCsv);
            CompanionsCsv = EditorGUILayout.TextField("Спутники рядом", CompanionsCsv);
            ItemsCsv = EditorGUILayout.TextField("Предметы с собой", ItemsCsv);
            WorldSeed = EditorGUILayout.IntField(new GUIContent("Seed мира", "Влияет на случайные варианты текста."), WorldSeed);

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Проверки", EditorStyles.boldLabel);
            int outcome = EditorGUILayout.Popup("Исход активной проверки", (int)ForcedOutcome, ForcedOutcomeLabels);
            ForcedOutcome = (NarrativeCheckForcedOutcome)Mathf.Clamp(outcome, 0, ForcedOutcomeLabels.Length - 1);
            RevealHiddenTextForAuthor = EditorGUILayout.ToggleLeft(
                new GUIContent("Показывать упущенный текст пассивных проверок",
                    "Только для автора: в игре текст провалившейся проверки не показывается никогда."),
                RevealHiddenTextForAuthor);

            return EditorGUI.EndChangeCheck();
        }

        private void DrawTrait(string traitId, string label)
        {
            bool has = Hero.HasTrait(traitId);
            if (EditorGUILayout.ToggleLeft(label, has) == has)
                return;
            if (has)
                Hero.RemoveTrait(traitId);
            else
                Hero.GrantTrait(traitId);
        }

        public static List<string> SplitCsv(string csv)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(csv))
                return result;

            foreach (string part in csv.Split(','))
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    result.Add(trimmed);
            }
            return result;
        }
    }

    // Окно превью «как в игре» живёт в сборке игрового UI
    // (UI/Editor/DialogueGamePreviewWindow.cs) и регистрирует себя здесь:
    // база диалогов не зависит от игрового UI и открывает превью по ID.
    public static class DialogueGamePreview
    {
        public static Action<string> Opener;

        public static bool Open(string dialogueId)
        {
            if (Opener == null)
            {
                Debug.LogWarning("Превью диалога недоступно: окно превью не зарегистрировано (UI/Editor/DialogueGamePreviewWindow.cs).");
                return false;
            }
            Opener(dialogueId);
            return true;
        }
    }
}
