using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.ProgressionDatabase.Editor
{
    // Страница каталога особенностей, приёмов и приказов (каталог §0.1):
    // слева — отфильтрованный список, справа — карточка выбранной записи.
    public sealed partial class ProgressionDatabaseWindow
    {
        private const string AllFilter = "Все";

        [SerializeField] private string traitSearch = string.Empty;
        [SerializeField] private string traitLayerFilter = AllFilter;
        [SerializeField] private string traitOwnerFilter = AllFilter;
        [SerializeField] private string traitAreaFilter = AllFilter;
        [SerializeField] private string traitStatusFilter = AllFilter;
        [SerializeField] private string selectedTraitId = string.Empty;

        private VisualElement traitDetails;

        private void BuildTraitsPage(VisualElement page)
        {
            AddTitle(page, "Особенности, приёмы и приказы");
            AddNote(page, "Рабочий каталог ProjectDocs/FEATURES_REFERENCES_ADAPTATION.md и его правила §0.1 — [РАБОЧЕЕ], не канон. " +
                          "Особенность меняет правило, приём — конкретное действие бойца, приказ — команда Командира. " +
                          "Статус: «Кандидат» — только данные; «Заглушка» — ждёт механики; «Активна» — в игре есть код, может попасть в выбор развития. " +
                          "ID используются в сохранениях и диалогах — не менять у записей, которые уже в игре.");

            List<TraitRecord> all = database.traits.Where(trait => trait != null).ToList();
            AddNote(page, "Всего " + all.Count + ": особенностей " + all.Count(t => t.layer == FeatureLayer.Feature) +
                          " (боевых " + all.Count(t => t.layer == FeatureLayer.Feature && t.combat) + "), приёмов " +
                          all.Count(t => t.layer == FeatureLayer.Technique) + ", приказов " + all.Count(t => t.layer == FeatureLayer.Order) +
                          " · активных " + all.Count(t => t.status == FeatureStatus.Active) + ", заглушек " +
                          all.Count(t => t.status == FeatureStatus.Stub) + ", кандидатов " + all.Count(t => t.status == FeatureStatus.Candidate) + ".");

            VisualElement filters = new VisualElement();
            filters.style.flexDirection = FlexDirection.Row;
            filters.style.flexWrap = Wrap.Wrap;
            filters.style.marginBottom = 6f;
            page.Add(filters);

            TextField search = new TextField("Поиск") { value = traitSearch };
            search.style.minWidth = 260f;
            search.RegisterValueChangedCallback(evt => { traitSearch = evt.newValue; RefreshTraitList(page); });
            filters.Add(search);
            AddFilter(filters, "Слой", new[] { AllFilter, "Особенность", "Приём", "Приказ" }, traitLayerFilter, value => { traitLayerFilter = value; RefreshTraitList(page); });
            AddFilter(filters, "Кому", new[] { AllFilter, "Оба", "Командир", "Боец", "Личная" }, traitOwnerFilter, value => { traitOwnerFilter = value; RefreshTraitList(page); });
            AddFilter(filters, "Где", new[] { AllFilter, "Бой", "Вне боя" }, traitAreaFilter, value => { traitAreaFilter = value; RefreshTraitList(page); });
            AddFilter(filters, "Статус", new[] { AllFilter, "Кандидат", "Заглушка", "Активна" }, traitStatusFilter, value => { traitStatusFilter = value; RefreshTraitList(page); });

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginBottom = 6f;
            page.Add(buttons);
            buttons.Add(new Button(AddNewTrait) { text = "+ ЗАПИСЬ" });
            buttons.Add(new Button(() =>
            {
                serializedDatabase.ApplyModifiedProperties();
                int added = database.AddMissingTraits();
                EditorUtility.SetDirty(database);
                serializedDatabase.Update();
                OnDatabaseChanged();
                EditorUtility.DisplayDialog("База развития", added > 0
                    ? "Добавлено записей из каталога по умолчанию: " + added + "."
                    : "Все записи каталога по умолчанию уже есть в базе.", "OK");
                ShowPage(TraitsPage);
            }) { text = "Добавить недостающие из каталога", tooltip = "Существующие записи не меняются." });

            VisualElement split = new VisualElement();
            split.name = "trait-split";
            split.style.flexDirection = FlexDirection.Row;
            split.style.minHeight = 620f;
            page.Add(split);

            ListView list = new ListView
            {
                name = "trait-list",
                fixedItemHeight = 22f,
                selectionType = SelectionType.Single,
                makeItem = () =>
                {
                    Label label = new Label();
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    label.style.paddingLeft = 4f;
                    return label;
                }
            };
            list.style.width = new Length(42f, LengthUnit.Percent);
            list.style.minWidth = 320f;
            list.style.height = 620f;
            list.style.marginRight = 10f;
            split.Add(list);

            traitDetails = new VisualElement();
            traitDetails.style.flexGrow = 1f;
            traitDetails.style.flexShrink = 1f;
            split.Add(traitDetails);

            list.selectionChanged += selection =>
            {
                TraitRecord record = selection.OfType<TraitRecord>().FirstOrDefault();
                selectedTraitId = record != null ? record.id : string.Empty;
                BuildTraitDetails();
            };
            RefreshTraitList(page);
        }

        private static void AddFilter(VisualElement parent, string label, string[] choices, string current, Action<string> changed)
        {
            DropdownField field = new DropdownField(label, choices.ToList(), Math.Max(0, Array.IndexOf(choices, current)));
            field.style.minWidth = 170f;
            field.RegisterValueChangedCallback(evt => changed(evt.newValue));
            parent.Add(field);
        }

        private void RefreshTraitList(VisualElement page)
        {
            ListView list = page.Q<ListView>("trait-list");
            if (list == null)
                return;
            List<TraitRecord> items = database.traits.Where(trait => trait != null && TraitMatches(trait)).ToList();
            list.itemsSource = items;
            list.bindItem = (element, index) =>
            {
                TraitRecord trait = items[index];
                Label label = (Label)element;
                label.text = (string.IsNullOrWhiteSpace(trait.code) ? string.Empty : trait.code + "  ") + trait.displayName +
                             "   · " + FeatureLabels.OwnerShort(trait.owner) + " · " + FeatureLabels.Status(trait.status);
                label.style.color = trait.status == FeatureStatus.Active ? new Color(0.55f, 0.85f, 0.55f)
                    : trait.status == FeatureStatus.Stub ? GoldColor : new Color(0.82f, 0.82f, 0.82f);
            };
            list.Rebuild();
            int selected = items.FindIndex(trait => trait.id == selectedTraitId);
            if (selected >= 0)
                list.SetSelectionWithoutNotify(new[] { selected });
            BuildTraitDetails();
        }

        private bool TraitMatches(TraitRecord trait)
        {
            if (traitLayerFilter != AllFilter && FeatureLabels.Layer(trait.layer) != traitLayerFilter)
                return false;
            if (traitOwnerFilter != AllFilter && FeatureLabels.Owner(trait.owner) != traitOwnerFilter)
                return false;
            if (traitAreaFilter == "Бой" && !trait.combat)
                return false;
            if (traitAreaFilter == "Вне боя" && trait.combat)
                return false;
            if (traitStatusFilter != AllFilter && FeatureLabels.Status(trait.status) != traitStatusFilter)
                return false;
            if (string.IsNullOrWhiteSpace(traitSearch))
                return true;
            string needle = traitSearch.Trim();
            return Contains(trait.code, needle) || Contains(trait.displayName, needle) || Contains(trait.id, needle) ||
                   Contains(trait.group, needle) || Contains(trait.description, needle) || Contains(trait.mergedFrom, needle);
        }

        private static bool Contains(string text, string needle)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void BuildTraitDetails()
        {
            if (traitDetails == null)
                return;
            traitDetails.Clear();
            traitDetails.Unbind();
            int index = database.traits.FindIndex(trait => trait != null && trait.id == selectedTraitId);
            if (index < 0)
            {
                AddNote(traitDetails, "Выберите запись слева.");
                return;
            }

            serializedDatabase.Update();
            SerializedProperty entry = serializedDatabase.FindProperty("traits").GetArrayElementAtIndex(index);
            TraitRecord record = database.traits[index];
            VisualElement card = CreateCard(traitDetails);

            Label header = new Label((string.IsNullOrWhiteSpace(record.code) ? string.Empty : record.code + " · ") + record.displayName);
            header.style.fontSize = 15f;
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(header);
            AddNote(card, FeatureLabels.Layer(record.layer) + " · " + FeatureLabels.Owner(record.owner) + " · " +
                          FeatureLabels.Kind(record.kind) + " · " + (record.combat ? "бой" : "вне боя") + " · реализация " +
                          FeatureLabels.Implementation(record.implementation) + " · предел " + FeatureLabels.Limit(record.limit) +
                          (ProgressionFeatureImplementations.IsImplemented(record.id) ? " · код в игре есть" : string.Empty) +
                          (ProgressionFeatureImplementations.TryGetStubTrigger(record.id, out FeatureTrigger waits)
                              ? " · заглушка в движке, ждёт события «" + FeatureTriggers.Label(waits) + "»"
                              : string.Empty));

            AddSection(card, "КАРТОЧКА");
            AddProperty(card, entry.FindPropertyRelative("id"), "ID");
            AddProperty(card, entry.FindPropertyRelative("code"), "Номер в каталоге");
            AddProperty(card, entry.FindPropertyRelative("displayName"), "Название");
            AddProperty(card, entry.FindPropertyRelative("group"), "Раздел");
            AddEnumDropdown(card, entry.FindPropertyRelative("layer"), "Слой", Enum.GetValues(typeof(FeatureLayer)).Cast<FeatureLayer>().Select(FeatureLabels.Layer).ToArray());
            AddEnumDropdown(card, entry.FindPropertyRelative("owner"), "Кому", Enum.GetValues(typeof(FeatureOwner)).Cast<FeatureOwner>().Select(FeatureLabels.Owner).ToArray());
            AddEnumDropdown(card, entry.FindPropertyRelative("kind"), "Тип", Enum.GetValues(typeof(FeatureKind)).Cast<FeatureKind>().Select(FeatureLabels.Kind).ToArray());
            AddProperty(card, entry.FindPropertyRelative("combat"), "Боевая");
            AddEnumDropdown(card, entry.FindPropertyRelative("status"), "Статус", Enum.GetValues(typeof(FeatureStatus)).Cast<FeatureStatus>().Select(FeatureLabels.Status).ToArray());
            AddEnumDropdown(card, entry.FindPropertyRelative("limit"), "Предел", Enum.GetValues(typeof(FeatureLimit)).Cast<FeatureLimit>().Select(FeatureLabels.Limit).ToArray());
            AddFlags(card, entry.FindPropertyRelative("implementation"), "Реализация", new[]
            {
                (int)FeatureImplementation.System, (int)FeatureImplementation.Tag, (int)FeatureImplementation.Authored,
                (int)FeatureImplementation.Future, (int)FeatureImplementation.Action
            }, new[] { "С — система", "Т — тег сцены", "А — автор", "Ф — будущая механика", "П — действие" });
            AddFlags(card, entry.FindPropertyRelative("sources"), "Откуда берётся", Enum.GetValues(typeof(FeatureSource)).Cast<FeatureSource>()
                    .Where(value => value != FeatureSource.None).Select(value => (int)value).ToArray(),
                Enum.GetValues(typeof(FeatureSource)).Cast<FeatureSource>().Where(value => value != FeatureSource.None)
                    .Select(value => FeatureLabels.Sources(value)).ToArray());

            AddSection(card, "ЭФФЕКТ И РАНГИ (1–" + TraitCatalogEntry.MaxRanks + ")");
            AddProperty(card, entry.FindPropertyRelative("description"), "Эффект целиком");
            AddProperty(card, entry.FindPropertyRelative("ranks"), "Ранги", "Название ранга пусто — показывается «Название I/II/III».");

            AddSection(card, "КАК ОТКРЫВАЕТСЯ");
            AddProperty(card, entry.FindPropertyRelative("unlockText"), "Словами");
            AddProperty(card, entry.FindPropertyRelative("requirements"), "Требования",
                "Компетенция — ID и ступень; качество — Strength/Dexterity/Fortitude/Instinct/Judgment/Character и значение; особенность — ID и ранг.");
            AddProperty(card, entry.FindPropertyRelative("requirementsAnyOf"), "Достаточно одного требования");
            AddProperty(card, entry.FindPropertyRelative("excludesIds"), "Взаимоисключения (ID)");
            AddProperty(card, entry.FindPropertyRelative("opensIds"), "Открывает приём или приказ (ID)");

            AddSection(card, "ОПОРА И ПОКАЗ");
            AddProperty(card, entry.FindPropertyRelative("support"), "Опора в игре");
            AddProperty(card, entry.FindPropertyRelative("dependency"), "Ждёт механики");
            AddProperty(card, entry.FindPropertyRelative("display"), "Способ отображения");
            AddProperty(card, entry.FindPropertyRelative("mergedFrom"), "Слито из · источники");
            AddProperty(card, entry.FindPropertyRelative("note"), "Заметка");

            List<string> openedBy = database.traits.Where(trait => trait != null && trait.opensIds != null && trait.opensIds.Contains(record.id))
                .Select(trait => trait.code + " " + trait.displayName).ToList();
            if (openedBy.Count > 0)
                AddNote(card, "Открывается особенностью: " + string.Join(", ", openedBy));
            List<string> requiredBy = database.traits.Where(trait => trait != null && trait.requirements != null &&
                                                                     trait.requirements.Any(r => r != null && r.kind == FeatureRequirementKind.Feature && r.id == record.id))
                .Select(trait => trait.code + " " + trait.displayName).ToList();
            if (requiredBy.Count > 0)
                AddNote(card, "Нужна для: " + string.Join(", ", requiredBy));

            AddRemoveButton(card, "Удалить запись", () =>
            {
                selectedTraitId = string.Empty;
                RemoveArrayElement("traits", index, TraitsPage);
            });
            traitDetails.Bind(serializedDatabase);
        }

        // Выпадающий список с русскими названиями для enum-поля.
        private void AddEnumDropdown(VisualElement parent, SerializedProperty property, string label, string[] names)
        {
            int current = Math.Max(0, Math.Min(names.Length - 1, property.intValue));
            DropdownField field = new DropdownField(label, names.ToList(), current);
            field.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            string path = property.propertyPath;
            field.RegisterValueChangedCallback(evt =>
            {
                int index = Array.IndexOf(names, evt.newValue);
                if (index < 0)
                    return;
                serializedDatabase.Update();
                serializedDatabase.FindProperty(path).intValue = index;
                OnDatabaseChanged();
                RefreshTraitList(content);
            });
            parent.Add(field);
        }

        // Набор флажков для [Flags]-поля.
        private void AddFlags(VisualElement parent, SerializedProperty property, string label, int[] values, string[] names)
        {
            Label title = new Label(label);
            title.style.marginTop = 4f;
            parent.Add(title);
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginLeft = 12f;
            parent.Add(row);
            string path = property.propertyPath;
            int mask = property.intValue;
            for (int i = 0; i < values.Length; i++)
            {
                int flag = values[i];
                Toggle toggle = new Toggle(names[i]) { value = (mask & flag) != 0 };
                toggle.style.marginRight = 12f;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    serializedDatabase.Update();
                    SerializedProperty target = serializedDatabase.FindProperty(path);
                    target.intValue = evt.newValue ? target.intValue | flag : target.intValue & ~flag;
                    OnDatabaseChanged();
                });
                row.Add(toggle);
            }
        }

        private void AddNewTrait()
        {
            string id = "trait_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            selectedTraitId = id;
            AddArrayElement("traits", TraitsPage, element =>
            {
                element.FindPropertyRelative("id").stringValue = id;
                element.FindPropertyRelative("displayName").stringValue = "Новая запись";
                element.FindPropertyRelative("description").stringValue = string.Empty;
                element.FindPropertyRelative("code").stringValue = string.Empty;
                element.FindPropertyRelative("group").stringValue = string.Empty;
                element.FindPropertyRelative("status").intValue = (int)FeatureStatus.Candidate;
                element.FindPropertyRelative("sources").intValue = (int)FeatureSource.LevelChoice;
                SerializedProperty ranks = element.FindPropertyRelative("ranks");
                ranks.ClearArray();
                ranks.arraySize = 1;
                ranks.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = string.Empty;
                ranks.GetArrayElementAtIndex(0).FindPropertyRelative("effect").stringValue = string.Empty;
                element.FindPropertyRelative("requirements").ClearArray();
                element.FindPropertyRelative("excludesIds").ClearArray();
                element.FindPropertyRelative("opensIds").ClearArray();
            });
        }
    }
}
