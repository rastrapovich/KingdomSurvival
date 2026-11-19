using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.AnimationDatabase;
using KingdomSurvival.AnimationDatabase.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UnitDatabase.Editor
{
    // Рисунки по видам (19.11.2026, CREATURE_VIEWS_ART_BRIEF.md): переключатель
    // «Обычный / Мрачный / Сепия» над карточками портрета, миниатюры и
    // анимации. «Обычный» правит прежние поля существа, другие виды — свои
    // слоты; пустой слот в игре берёт рисунок «Обычного». Кадрирование
    // портрета и масштаб миниатюры общие для всех видов (холсты одинаковые).
    public sealed partial class UnitDatabaseWindow
    {
        [SerializeField] private string editedViewId = UnitArtViews.ClassicId;
        private readonly List<(UnitArtKind Kind, Label Label)> viewSlotNotes = new List<(UnitArtKind, Label)>();

        private string EditedViewId => string.IsNullOrEmpty(editedViewId) ? UnitArtViews.ClassicId : editedViewId;
        private bool EditingClassic => UnitArtViews.IsClassic(EditedViewId);

        private string ViewSuffix()
        {
            return EditingClassic ? string.Empty : " · " + UnitArtViews.Title(EditedViewId).ToUpperInvariant();
        }

        private VisualElement BuildViewSwitch()
        {
            viewSlotNotes.Clear();
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.width = Length.Percent(100f);
            row.style.marginTop = 4f;
            row.style.marginBottom = 6f;

            Label title = new Label("РИСУНКИ ВИДА");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginRight = 8f;
            row.Add(title);

            foreach (string id in UnitArtViews.Ids)
            {
                string captured = id;
                Button button = new Button(() =>
                {
                    if (editedViewId == captured)
                        return;
                    editedViewId = captured;
                    ShowSelectedUnit();
                })
                {
                    text = UnitArtViews.Title(id),
                    tooltip = UnitArtViews.IsClassic(id)
                        ? "Основные рисунки существа."
                        : "Свои рисунки вида «" + UnitArtViews.Title(id) + "». Пустой слот — рисунок «Обычного»."
                };
                if (id == EditedViewId)
                {
                    button.style.backgroundColor = new Color(0.88f, 0.71f, 0.38f, 1f);
                    button.style.color = new Color(0.1f, 0.08f, 0.05f, 1f);
                    button.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
                row.Add(button);
            }

            Label hint = Hint(EditingClassic
                ? "«Обычный» — основные рисунки. Пустые слоты «Мрачного» и «Сепии» берут их."
                : "Свои рисунки вида «" + UnitArtViews.Title(EditedViewId) + "». Пустой слот — в игре рисунок «Обычного». Кадрирование портрета и масштаб миниатюры общие для всех видов.");
            hint.style.flexShrink = 1f;
            hint.style.marginLeft = 8f;
            row.Add(hint);
            return row;
        }

        private static string SlotProperty(UnitArtKind kind)
        {
            return kind == UnitArtKind.Portrait ? "portrait" : "battlefieldSprite";
        }

        // «Обычный» — поле существа; вид — слот viewArt (create — завести).
        private SerializedProperty ArtSlotProperty(SerializedProperty unit, UnitArtKind kind, bool create)
        {
            if (EditingClassic)
                return unit.FindPropertyRelative(ArtProperty(kind));
            SerializedProperty slot = FindViewSlot(unit, EditedViewId, create);
            return slot?.FindPropertyRelative(SlotProperty(kind));
        }

        private static SerializedProperty FindViewSlot(SerializedProperty unit, string viewId, bool create)
        {
            SerializedProperty list = unit.FindPropertyRelative("viewArt");
            if (list == null)
                return null;
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("viewId").stringValue == viewId)
                    return element;
            }
            if (!create)
                return null;
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            SerializedProperty added = list.GetArrayElementAtIndex(index);
            added.FindPropertyRelative("viewId").stringValue = viewId;
            added.FindPropertyRelative("portrait").objectReferenceValue = null;
            added.FindPropertyRelative("battlefieldSprite").objectReferenceValue = null;
            added.FindPropertyRelative("animationSetId").stringValue = string.Empty;
            return added;
        }

        // Рисунок самого слота (без подстановки «Обычного»).
        private Sprite CurrentSlotSprite(UnitDefinitionData unit, UnitArtKind kind)
        {
            if (unit == null)
                return null;
            if (EditingClassic)
                return kind == UnitArtKind.Portrait ? unit.Portrait : unit.BattlefieldSprite;
            UnitViewArtData slot = unit.FindViewArt(EditedViewId);
            return slot == null ? null : kind == UnitArtKind.Portrait ? slot.Portrait : slot.BattlefieldSprite;
        }

        private VisualElement ArtFileField(SerializedProperty unit, UnitArtKind kind)
        {
            if (EditingClassic)
                return SpriteField(unit, ArtProperty(kind));
            ObjectField field = new ObjectField("Файл")
            {
                objectType = typeof(Sprite),
                allowSceneObjects = false,
                tooltip = "Свой рисунок вида «" + UnitArtViews.Title(EditedViewId) + "». Пусто — рисунок «Обычного»."
            };
            field.labelElement.style.minWidth = 70f;
            field.labelElement.style.width = 70f;
            field.SetValueWithoutNotify(CurrentSlotSprite(database.Units[selectedUnitIndex], kind));
            field.RegisterValueChangedCallback(evt => AssignArt(kind, evt.newValue as Sprite));
            return field;
        }

        private Label ViewSlotNote(UnitArtKind kind)
        {
            Label label = Hint(string.Empty);
            viewSlotNotes.Add((kind, label));
            return label;
        }

        private void RefreshViewSlotNotes(UnitDefinitionData unit)
        {
            foreach ((UnitArtKind kind, Label label) in viewSlotNotes)
            {
                if (EditingClassic || unit == null)
                {
                    label.style.display = DisplayStyle.None;
                    continue;
                }
                Sprite classic = kind == UnitArtKind.Portrait ? unit.Portrait : unit.BattlefieldSprite;
                label.text = CurrentSlotSprite(unit, kind) != null
                    ? "Свой рисунок вида."
                    : classic != null
                        ? "Своего рисунка нет — в игре и здесь показан «Обычный»."
                        : "Рисунка нет ни у вида, ни у «Обычного».";
                label.style.display = DisplayStyle.Flex;
            }
        }

        // ------------------------------------------------------------------
        // Карточка «Анимация на поле»: набор по видам и статичная фигура.
        // ------------------------------------------------------------------

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

            string current = EditingClassic
                ? unit.AnimationSetId
                : unit.FindViewArt(EditedViewId)?.AnimationSetId ?? string.Empty;
            List<string> setIds = new List<string> { string.Empty };
            if (animationDatabase != null)
                setIds.AddRange(animationDatabase.Sets.Where(item => item != null).Select(item => item.Id));
            if (!setIds.Contains(current))
                setIds.Add(current);
            string Format(string setId) => string.IsNullOrEmpty(setId) && !EditingClassic
                ? "— как в «Обычном» —"
                : DescribeAnimationSet(setId);
            PopupField<string> setField = new PopupField<string>(
                "Набор анимаций" + (EditingClassic ? string.Empty : " (" + UnitArtViews.Title(EditedViewId) + ")"),
                setIds,
                current,
                Format,
                Format);
            setField.tooltip = EditingClassic
                ? "Набор из Базы анимаций. Один набор можно дать нескольким существам."
                : "Свой набор вида. Пусто — набор «Обычного».";
            setField.RegisterValueChangedCallback(evt => AssignAnimationSet(evt.newValue));
            animationSetRow.Add(setField);

            string effectiveId = EditingClassic || string.IsNullOrEmpty(current) ? unit.AnimationSetId : current;
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

        // Как в игре для редактируемого вида: статичная фигура — без набора.
        private void RefreshAnimationPreviewData()
        {
            if (animationPreview == null || database == null ||
                selectedUnitIndex < 0 || selectedUnitIndex >= database.Units.Count)
            {
                return;
            }
            UnitDefinitionData unit = database.Units[selectedUnitIndex];
            CreatureAnimationSetData set = animationDatabase != null
                ? animationDatabase.FindSet(unit.AnimationSetIdFor(EditedViewId))
                : null;
            animationPreview.SetData(animationDatabase, set, unit.BattlefieldSpriteFor(EditedViewId));
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
            SerializedProperty unit = unitsProperty.GetArrayElementAtIndex(selectedUnitIndex);
            SerializedProperty property = EditingClassic
                ? unit.FindPropertyRelative("animationSetId")
                : FindViewSlot(unit, EditedViewId, !string.IsNullOrEmpty(setId))?.FindPropertyRelative("animationSetId");
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
