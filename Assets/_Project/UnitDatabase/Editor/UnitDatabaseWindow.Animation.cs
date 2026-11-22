using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.AnimationDatabase.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UnitDatabase.Editor
{
    // Карточка «Анимация на поле»: набор анимаций существа и «статичная фигура».
    public sealed partial class UnitDatabaseWindow
    {
        private void RefreshAnimationCard()
        {
            if (animationSetRow == null || database == null ||
                selectedUnitIndex < 0 || selectedUnitIndex >= database.Units.Count)
            {
                return;
            }

            UnitDefinitionData unit = database.Units[selectedUnitIndex];
            animationSetRow.Clear();

            Toggle staticToggle = new Toggle("Статичная фигура вместо анимаций")
            {
                tooltip = "Пока анимации не нужны: в бою и в местах — статичная миниатюра. Наборы анимаций остаются " +
                          "привязанными и вернутся, когда флажок снят."
            };
            staticToggle.SetValueWithoutNotify(unit.StaticFigureOnly);
            staticToggle.RegisterValueChangedCallback(evt =>
            {
                serializedDatabase.Update();
                unitsProperty.GetArrayElementAtIndex(selectedUnitIndex)
                    .FindPropertyRelative("staticFigureOnly").boolValue = evt.newValue;
                serializedDatabase.ApplyModifiedProperties();
                EditorUtility.SetDirty(database);
                CreatureAnimationEditorData.NotifyChanged();
                animationSetRow.schedule.Execute(RefreshAnimationCard);
            });
            animationSetRow.Add(staticToggle);

            string current = unit.AnimationSetId;
            List<string> setIds = new List<string> { string.Empty };
            if (animationDatabase != null)
                setIds.AddRange(animationDatabase.Sets.Where(item => item != null).Select(item => item.Id));
            if (!setIds.Contains(current))
                setIds.Add(current);
            PopupField<string> setField = new PopupField<string>(
                "Набор анимаций",
                setIds,
                current,
                DescribeAnimationSet,
                DescribeAnimationSet);
            setField.tooltip = "Набор из Базы анимаций. Один набор можно дать нескольким существам.";
            setField.RegisterValueChangedCallback(evt => AssignAnimationSet(evt.newValue));
            animationSetRow.Add(setField);

            string effectiveId = current;
            CreatureAnimationSetData set = animationDatabase != null ? animationDatabase.FindSet(effectiveId) : null;
            string text = set != null
                ? "Используют: " + CreatureAnimationEditorData.DescribeUsers(database, set.Id) +
                  ". Размер и опору на поле задаёт набор; «Масштаб миниатюры» и «Смещение миниатюры» для этого существа не применяются."
                : string.IsNullOrEmpty(effectiveId)
                    ? "Набора нет: в бою статичная миниатюра, без неё — жетон."
                    : "Набор «" + effectiveId + "» не найден в Базе анимаций: в бою статичная миниатюра.";
            if (unit.StaticFigureOnly)
            {
                text = "Включена статичная фигура: в игре набор не используется" +
                       (string.IsNullOrEmpty(effectiveId) ? "." : ", ссылка «" + effectiveId + "» сохранена.") +
                       " Предпросмотр ниже — как в игре.";
            }
            Label note = new Label(text);
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.fontSize = 10f;
            note.style.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            animationSetRow.Add(note);

            Button open = new Button(() => CreatureAnimationDatabaseWindow.Open(unit.Id))
            {
                text = "ОТКРЫТЬ В БАЗЕ АНИМАЦИЙ"
            };
            open.style.marginTop = 4f;
            animationSetRow.Add(open);

            RefreshAnimationPreviewData();
        }

        // Как в игре: статичная фигура — без набора.
        private void RefreshAnimationPreviewData()
        {
            if (animationPreview == null || database == null ||
                selectedUnitIndex < 0 || selectedUnitIndex >= database.Units.Count)
            {
                return;
            }
            UnitDefinitionData unit = database.Units[selectedUnitIndex];
            CreatureAnimationSetData set = animationDatabase != null
                ? animationDatabase.FindSet(unit.ActiveAnimationSetId)
                : null;
            animationPreview.SetData(animationDatabase, set, unit.ActiveBattlefieldSprite);
        }

        private string DescribeAnimationSet(string setId)
        {
            if (string.IsNullOrEmpty(setId))
                return "— нет —";
            CreatureAnimationSetData set = animationDatabase != null ? animationDatabase.FindSet(setId) : null;
            return set == null
                ? setId + " (не найден)"
                : set.DisplayName + " [" + set.Id + "] · " + CreatureAnimationLabels.StatusTitle(set.Status);
        }

        private void AssignAnimationSet(string setId)
        {
            if (selectedUnitIndex < 0 || selectedUnitIndex >= unitsProperty.arraySize)
                return;
            serializedDatabase.Update();
            SerializedProperty property = unitsProperty.GetArrayElementAtIndex(selectedUnitIndex)
                .FindPropertyRelative("animationSetId");
            if (property == null)
                return;
            property.stringValue = setId ?? string.Empty;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            CreatureAnimationEditorData.NotifyChanged();
            RefreshAnimationCard();
        }
    }
}
