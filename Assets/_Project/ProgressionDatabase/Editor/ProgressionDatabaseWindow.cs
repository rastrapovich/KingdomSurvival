using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.ProgressionDatabase.Editor
{
    // «База развития» — всё про опыт и уровни в одном окне: общие правила,
    // карта развития до 100-го уровня для Командира и каждого типа персонажа
    // (бойцы, противники, существа) и перечни качеств, особенностей,
    // компетенций и тегов. Правки в Play Mode сразу применяются к игре.
    public sealed partial class ProgressionDatabaseWindow : EditorWindow
    {
        private const string RulesPage = "rules";
        private const string QualitiesPage = "qualities";
        private const string TraitsPage = "traits";
        private const string CompetenciesPage = "competencies";
        private const string TagsPage = "tags";
        private const string ValidationPage = "validation";
        private const string ProfilePrefix = "profile:";

        private static readonly Color MutedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Color GoldColor = new Color(0.87f, 0.71f, 0.39f, 1f);
        private static readonly Color SelectedColor = new Color(0.24f, 0.33f, 0.45f, 1f);

        [SerializeField] private string selectedPage = RulesPage;

        private ProgressionDatabaseAsset database;
        private SerializedObject serializedDatabase;
        private UnitDatabaseAsset units;
        private SerializedObject serializedUnits;

        private ScrollView navigation;
        private VisualElement content;
        private Label statusLabel;

        [MenuItem("Kingdom Survival/База развития")]
        public static void OpenWindow()
        {
            ProgressionDatabaseWindow window = GetWindow<ProgressionDatabaseWindow>();
            window.titleContent = new GUIContent("База развития");
            window.minSize = new Vector2(1100f, 640f);
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1f;

            database = ProgressionDatabaseBootstrap.LoadOrCreate();
            units = AssetDatabase.LoadAssetAtPath<UnitDatabaseAsset>(ProgressionDatabaseBootstrap.UnitDatabasePath);
            if (ProgressionDatabaseBootstrap.SyncWithUnits(database, units))
                AssetDatabase.SaveAssetIfDirty(database);

            serializedDatabase = new SerializedObject(database);
            serializedUnits = units != null ? new SerializedObject(units) : null;

            BuildToolbar();

            TwoPaneSplitView split = new TwoPaneSplitView(0, 260f, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1f;
            rootVisualElement.Add(split);

            navigation = new ScrollView(ScrollViewMode.Vertical);
            navigation.style.paddingLeft = 6f;
            navigation.style.paddingRight = 6f;
            navigation.style.paddingTop = 6f;
            split.Add(navigation);

            content = new VisualElement();
            content.style.flexGrow = 1f;
            split.Add(content);

            RebuildNavigation();
            ShowPage(selectedPage);
            RefreshStatus();
        }

        private void OnDisable()
        {
            if (database != null)
                AssetDatabase.SaveAssetIfDirty(database);
            if (units != null)
                AssetDatabase.SaveAssetIfDirty(units);
        }

        private void OnLostFocus()
        {
            OnDisable();
        }

        // ------------------------------------------------------------------
        // Каркас
        // ------------------------------------------------------------------

        private void BuildToolbar()
        {
            VisualElement toolbar = new VisualElement();
            toolbar.style.height = 38f;
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 8f;
            toolbar.style.paddingRight = 8f;
            toolbar.style.borderBottomWidth = 1f;
            toolbar.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f, 1f);

            AddToolbarButton(toolbar, "ПРОВЕРИТЬ БАЗУ", () => ShowPage(ValidationPage));
            AddToolbarButton(toolbar, "ДОБАВИТЬ НОВЫЕ ТИПЫ ИЗ БАЗЫ СУЩЕСТВ", () =>
            {
                if (ProgressionDatabaseBootstrap.SyncWithUnits(database, units))
                {
                    serializedDatabase.Update();
                    AssetDatabase.SaveAssetIfDirty(database);
                }
                RebuildNavigation();
                RefreshStatus();
            });
            AddToolbarButton(toolbar, "БАЗА СУЩЕСТВ", () => EditorApplication.ExecuteMenuItem("Kingdom Survival/База существ"));

            statusLabel = new Label();
            statusLabel.style.flexGrow = 1f;
            statusLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            toolbar.Add(statusLabel);
            rootVisualElement.Add(toolbar);
        }

        private static void AddToolbarButton(VisualElement toolbar, string text, Action action)
        {
            Button button = new Button(action) { text = text };
            button.style.height = 26f;
            button.style.marginRight = 6f;
            toolbar.Add(button);
        }

        private void RebuildNavigation()
        {
            navigation.Clear();
            AddNavHeader("ПРАВИЛА");
            AddNavItem("Общие правила опыта", RulesPage);

            AddNavHeader("КАРТЫ РАЗВИТИЯ 1–100");
            AddNavItem("Командир (герой)", ProfilePrefix + ProgressionRules.HeroProfileId);

            HashSet<string> shown = new HashSet<string> { ProgressionRules.HeroProfileId };
            if (units != null)
            {
                foreach (KeyValuePair<UnitCategory, string> group in new[]
                         {
                             new KeyValuePair<UnitCategory, string>(UnitCategory.Fighter, "Бойцы"),
                             new KeyValuePair<UnitCategory, string>(UnitCategory.Creature, "Противники и существа"),
                             new KeyValuePair<UnitCategory, string>(UnitCategory.Commander, "Командиры"),
                             new KeyValuePair<UnitCategory, string>(UnitCategory.Other, "Прочие")
                         })
                {
                    List<UnitDefinitionData> members = units.Units.Where(unit => unit != null && unit.Category == group.Key).ToList();
                    if (members.Count == 0)
                        continue;
                    AddNavSubheader(group.Value);
                    foreach (UnitDefinitionData unit in members)
                    {
                        AddNavItem(unit.DisplayLabel + "  ·  " + unit.Id, ProfilePrefix + unit.Id);
                        shown.Add(unit.Id);
                    }
                }
            }

            List<ProgressionProfileRecord> orphans = database.profiles.Where(profile => profile != null && !shown.Contains(profile.id)).ToList();
            if (orphans.Count > 0)
            {
                AddNavSubheader("Профили без типа в Базе существ");
                foreach (ProgressionProfileRecord profile in orphans)
                    AddNavItem((string.IsNullOrWhiteSpace(profile.displayName) ? profile.id : profile.displayName) + "  ·  " + profile.id, ProfilePrefix + profile.id);
            }

            AddNavHeader("ПЕРЕЧНИ");
            AddNavItem("Качества", QualitiesPage);
            AddNavItem("Особенности", TraitsPage);
            AddNavItem("Компетенции", CompetenciesPage);
            AddNavItem("Теги (База существ)", TagsPage);

            AddNavHeader("КОНТРОЛЬ");
            AddNavItem("Проверка базы", ValidationPage);
        }

        private void AddNavHeader(string text)
        {
            Label label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = GoldColor;
            label.style.marginTop = 10f;
            label.style.marginBottom = 4f;
            navigation.Add(label);
        }

        private void AddNavSubheader(string text)
        {
            Label label = new Label(text);
            label.style.color = MutedColor;
            label.style.fontSize = 11f;
            label.style.marginTop = 6f;
            label.style.marginLeft = 4f;
            navigation.Add(label);
        }

        private void AddNavItem(string text, string page)
        {
            Button button = new Button(() => ShowPage(page)) { text = text };
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.height = 24f;
            button.style.marginLeft = 0f;
            button.style.marginRight = 0f;
            if (page == selectedPage)
                button.style.backgroundColor = SelectedColor;
            navigation.Add(button);
        }

        private void ShowPage(string pageId)
        {
            selectedPage = string.IsNullOrEmpty(pageId) ? RulesPage : pageId;
            serializedDatabase.Update();
            content.Clear();
            RebuildNavigation();

            if (selectedPage.StartsWith(ProfilePrefix, StringComparison.Ordinal))
            {
                string id = selectedPage.Substring(ProfilePrefix.Length);
                int index = database.profiles.FindIndex(profile => profile != null && profile.id == id);
                if (index >= 0)
                {
                    content.Add(BuildProfilePage(index));
                    return;
                }
                selectedPage = RulesPage;
            }

            ScrollView page = CreatePageScroll();
            switch (selectedPage)
            {
                case QualitiesPage: BuildQualitiesPage(page); break;
                case TraitsPage: BuildTraitsPage(page); break;
                case CompetenciesPage: BuildCompetenciesPage(page); break;
                case TagsPage: BuildTagsPage(page); break;
                case ValidationPage: BuildValidationPage(page); break;
                default: BuildRulesPage(page); break;
            }
            content.Add(page);
            page.Bind(serializedDatabase);
        }

        private ScrollView CreatePageScroll()
        {
            ScrollView page = new ScrollView(ScrollViewMode.Vertical);
            page.style.flexGrow = 1f;
            page.style.paddingLeft = 16f;
            page.style.paddingRight = 16f;
            page.style.paddingTop = 10f;
            page.style.paddingBottom = 16f;
            page.RegisterCallback<SerializedPropertyChangeEvent>(_ => OnDatabaseChanged());
            return page;
        }

        // Любая правка: пометить базу изменённой, в Play Mode — сразу в игру.
        private void OnDatabaseChanged()
        {
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            if (EditorApplication.isPlaying)
                ProgressionDatabaseRuntime.Apply(database);
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            if (statusLabel == null || database == null)
                return;
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();
            database.CollectValidationIssues(errors, warnings);
            statusLabel.text = errors.Count == 0 && warnings.Count == 0
                ? "База в порядке"
                : "Ошибок: " + errors.Count + " · предупреждений: " + warnings.Count;
            statusLabel.style.color = errors.Count > 0 ? new Color(0.95f, 0.45f, 0.4f) : warnings.Count > 0 ? GoldColor : MutedColor;
        }

        // ------------------------------------------------------------------
        // Общие элементы страниц
        // ------------------------------------------------------------------

        private static Label AddTitle(VisualElement parent, string text)
        {
            Label label = new Label(text);
            label.style.fontSize = 18f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 4f;
            parent.Add(label);
            return label;
        }

        private static Label AddSection(VisualElement parent, string text)
        {
            Label label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = GoldColor;
            label.style.marginTop = 14f;
            label.style.marginBottom = 4f;
            parent.Add(label);
            return label;
        }

        private static Label AddNote(VisualElement parent, string text)
        {
            Label label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = MutedColor;
            label.style.fontSize = 11f;
            label.style.marginBottom = 4f;
            parent.Add(label);
            return label;
        }

        private static void AddProperty(VisualElement parent, SerializedProperty property, string label, string tooltip = null)
        {
            PropertyField field = new PropertyField(property, label);
            if (!string.IsNullOrEmpty(tooltip))
                field.tooltip = tooltip;
            parent.Add(field);
        }

        private SerializedProperty Rules(string name)
        {
            return serializedDatabase.FindProperty("rules").FindPropertyRelative(name);
        }

        // ------------------------------------------------------------------
        // Общие правила
        // ------------------------------------------------------------------

        private void BuildRulesPage(VisualElement page)
        {
            AddTitle(page, "Общие правила опыта");
            AddNote(page, "Канон v1.48 §27: общий уровень 1–100, значимый выбор каждые 3 уровня, боевой банк 60% участие / 40% " +
                          "реальный вклад, уровень сам не раздувает характеристики. Остальные числа — рабочие и настраиваются здесь. " +
                          "Кривые опыта, выборы и прибавки по уровням — в картах развития слева.");

            AddSection(page, "БОЕВОЙ БАНК");
            AddNote(page, "Банк боя = сумма «цены» противников. Цена берётся из карты развития типа на его уровне, а если там 0 — " +
                          "по формуле: вес HP × здоровье + вес чисел × (атака + защита + урон).");
            AddProperty(page, Rules("participationPercent"), "Доля участия, %", "Остальное делится по реальному вкладу. Канон — 60.");
            AddProperty(page, Rules("retreatBankPercent"), "Отход из боя, % банка");
            AddProperty(page, Rules("repeatBattlePercents"), "Повтор того же состава, % банка", "1-й бой, 2-й, 3-й… последнее значение — дальше.");
            AddProperty(page, Rules("enemyHitPointWeight"), "Формула цены: вес HP");
            AddProperty(page, Rules("enemyStatWeight"), "Формула цены: вес атаки+защиты+урона");

            AddSection(page, "ОПЫТ ВНЕ БОЯ");
            AddProperty(page, Rules("explorationExperience"), "Впервые исследованное место");
            AddProperty(page, Rules("encounterReactionExperience"), "Встреча: реакция");
            AddProperty(page, Rules("encounterMicroExperience"), "Встреча: микро");
            AddProperty(page, Rules("encounterShortExperience"), "Встреча: короткая");
            AddProperty(page, Rules("encounterStandardExperience"), "Встреча: стандартная");
            AddProperty(page, Rules("encounterComplexExperience"), "Встреча: сложная");
            AddProperty(page, Rules("encounterQuestSeedExperience"), "Встреча: завязка истории");

            AddSection(page, "ПРАКТИКА КОМПЕТЕНЦИЙ");
            AddNote(page, "Практика — отдельно от уровня: реальные применения конкретного умения. Одинаковое содержание учит всё меньше.");
            AddProperty(page, Rules("practicePerUse"), "Практика за применение");
            AddProperty(page, Rules("repeatPracticePoints"), "Практика при повторе того же боя", "1-й раз, 2-й… после списка — 0.");
            AddProperty(page, Rules("practiceToNextRank"), "Практика до ступени (0→1 … 4→5)");
            AddProperty(page, Rules("practiceCeiling"), "Потолок собственной практики", "Выше — только после наставника или нового знания.");
            AddProperty(page, Rules("combatBonusFirstRank"), "Ступень владения: +1 к атаке/защите");
            AddProperty(page, Rules("combatBonusSecondRank"), "Ступень владения: +2 к атаке/защите");

            AddSection(page, "ВЫБОР РАЗВИТИЯ");
            AddProperty(page, Rules("toughnessHitPoints"), "«Крепость тела»: +HP");
            AddProperty(page, Rules("maxToughnessChoices"), "«Крепость тела»: не больше раз");
        }

        // ------------------------------------------------------------------
        // Перечни
        // ------------------------------------------------------------------

        private void BuildQualitiesPage(VisualElement page)
        {
            AddTitle(page, "Качества");
            AddNote(page, "Шесть качеств есть только у Командира (1–10). Растут редко — через сюжетный рубеж или значимый выбор. " +
                          "Название и описание видны на экране героя и в проверках диалогов.");
            SerializedProperty list = serializedDatabase.FindProperty("qualities");
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                VisualElement row = CreateCard(page);
                Label id = new Label(((HeroQuality)entry.FindPropertyRelative("quality").enumValueIndex).ToString());
                id.style.color = MutedColor;
                row.Add(id);
                AddProperty(row, entry.FindPropertyRelative("displayName"), "Название");
                AddProperty(row, entry.FindPropertyRelative("description"), "Описание");
            }
        }

        private void BuildTraitsPage(VisualElement page)
        {
            AddTitle(page, "Особенности");
            AddNote(page, "Особенности героя (стабильные ID). Каталог особенностей для выбора развития в каноне пока открыт (§25.3–25.4): " +
                          "здесь можно вести рабочий перечень. «knows_the_way» и «naturalist» используются игрой — их ID не менять.");
            SerializedProperty list = serializedDatabase.FindProperty("traits");
            for (int i = 0; i < list.arraySize; i++)
            {
                int index = i;
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                VisualElement card = CreateCard(page);
                AddProperty(card, entry.FindPropertyRelative("id"), "ID");
                AddProperty(card, entry.FindPropertyRelative("displayName"), "Название");
                AddProperty(card, entry.FindPropertyRelative("description"), "Описание");
                AddRemoveButton(card, "Удалить особенность", () => RemoveArrayElement("traits", index, TraitsPage));
            }
            page.Add(new Button(() => AddArrayElement("traits", TraitsPage, element =>
            {
                element.FindPropertyRelative("id").stringValue = "trait_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                element.FindPropertyRelative("displayName").stringValue = "Новая особенность";
                element.FindPropertyRelative("description").stringValue = string.Empty;
            })) { text = "+ ОСОБЕННОСТЬ" });
        }

        private void BuildCompetenciesPage(VisualElement page)
        {
            AddTitle(page, "Компетенции");
            AddNote(page, "Первый базовый каталог — 24 компетенции канона v1.49 §27.11; их ID закреплены в игре и диалогах. " +
                          "Новую компетенцию добавляют, только если у неё несколько разных применений. «Каталог бойца» — из чего строятся " +
                          "выборы постоянного бойца (бой и поход), если в его карте развития не задан свой список.");
            HashSet<string> canonical = new HashSet<string>(ProgressionCatalog.CreateDefault().Competencies.Select(entry => entry.Id));
            SerializedProperty list = serializedDatabase.FindProperty("competencies");
            for (int i = 0; i < list.arraySize; i++)
            {
                int index = i;
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                string id = entry.FindPropertyRelative("id").stringValue;
                VisualElement card = CreateCard(page);
                if (canonical.Contains(id))
                {
                    Label idLabel = new Label("ID: " + id + "  ·  канон v1.49");
                    idLabel.style.color = MutedColor;
                    card.Add(idLabel);
                }
                else
                {
                    AddProperty(card, entry.FindPropertyRelative("id"), "ID");
                }
                AddProperty(card, entry.FindPropertyRelative("displayName"), "Название");
                AddProperty(card, entry.FindPropertyRelative("description"), "Базовая функция");
                AddProperty(card, entry.FindPropertyRelative("fighterCatalog"), "Каталог бойца");
                if (!canonical.Contains(id))
                    AddRemoveButton(card, "Удалить компетенцию", () => RemoveArrayElement("competencies", index, CompetenciesPage));
            }
            page.Add(new Button(() => AddArrayElement("competencies", CompetenciesPage, element =>
            {
                element.FindPropertyRelative("id").stringValue = "competency_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                element.FindPropertyRelative("displayName").stringValue = "Новая компетенция";
                element.FindPropertyRelative("description").stringValue = string.Empty;
                element.FindPropertyRelative("fighterCatalog").boolValue = false;
            })) { text = "+ КОМПЕТЕНЦИЯ" });
        }

        private void BuildTagsPage(VisualElement page)
        {
            AddTitle(page, "Теги");
            AddNote(page, "Боевые теги хранятся в Базе существ и правятся здесь же. Какие теги у какого типа — назначается в Базе существ.");
            if (serializedUnits == null)
            {
                page.Add(new HelpBox("База существ не найдена: " + ProgressionDatabaseBootstrap.UnitDatabasePath, HelpBoxMessageType.Error));
                return;
            }

            serializedUnits.Update();
            SerializedProperty tags = serializedUnits.FindProperty("tags");
            VisualElement container = new VisualElement();
            for (int i = 0; i < tags.arraySize; i++)
            {
                int index = i;
                SerializedProperty tag = tags.GetArrayElementAtIndex(i);
                string id = tag.FindPropertyRelative("id").stringValue;
                VisualElement card = CreateCard(container);
                AddProperty(card, tag.FindPropertyRelative("id"), "ID");
                AddProperty(card, tag.FindPropertyRelative("displayLabel"), "Название");
                AddProperty(card, tag.FindPropertyRelative("category"), "Категория");
                AddProperty(card, tag.FindPropertyRelative("color"), "Цвет");
                AddProperty(card, tag.FindPropertyRelative("description"), "Описание");
                List<string> users = units.Units.Where(unit => unit != null && unit.HasTag(id)).Select(unit => unit.DisplayLabel).ToList();
                AddNote(card, users.Count > 0 ? "У типов: " + string.Join(", ", users) : "Пока ни у одного типа.");
                AddRemoveButton(card, "Удалить тег", () =>
                {
                    serializedUnits.Update();
                    serializedUnits.FindProperty("tags").DeleteArrayElementAtIndex(index);
                    serializedUnits.ApplyModifiedProperties();
                    EditorUtility.SetDirty(units);
                    ShowPage(TagsPage);
                });
            }
            container.Add(new Button(() =>
            {
                serializedUnits.Update();
                SerializedProperty list = serializedUnits.FindProperty("tags");
                list.arraySize++;
                SerializedProperty added = list.GetArrayElementAtIndex(list.arraySize - 1);
                added.FindPropertyRelative("id").stringValue = "tag." + Guid.NewGuid().ToString("N").Substring(0, 6);
                added.FindPropertyRelative("displayLabel").stringValue = "Новый тег";
                added.FindPropertyRelative("category").stringValue = string.Empty;
                added.FindPropertyRelative("description").stringValue = string.Empty;
                serializedUnits.ApplyModifiedProperties();
                EditorUtility.SetDirty(units);
                ShowPage(TagsPage);
            }) { text = "+ ТЕГ" });

            container.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                serializedUnits.ApplyModifiedProperties();
                EditorUtility.SetDirty(units);
            });
            page.Add(container);
            container.Bind(serializedUnits);
        }

        private void BuildValidationPage(VisualElement page)
        {
            AddTitle(page, "Проверка базы");
            List<string> errors = new List<string>();
            List<string> warnings = new List<string>();
            database.CollectValidationIssues(errors, warnings);
            if (errors.Count == 0 && warnings.Count == 0)
            {
                page.Add(new HelpBox("Ошибок и отклонений от канона нет.", HelpBoxMessageType.Info));
                return;
            }
            foreach (string error in errors)
                page.Add(new HelpBox(error, HelpBoxMessageType.Error));
            foreach (string warning in warnings)
                page.Add(new HelpBox(warning, HelpBoxMessageType.Warning));
        }

        private static VisualElement CreateCard(VisualElement parent)
        {
            VisualElement card = new VisualElement();
            card.style.marginBottom = 8f;
            card.style.paddingLeft = 8f;
            card.style.paddingRight = 8f;
            card.style.paddingTop = 6f;
            card.style.paddingBottom = 6f;
            card.style.backgroundColor = new Color(0.17f, 0.17f, 0.17f, 1f);
            card.style.borderLeftWidth = 2f;
            card.style.borderLeftColor = new Color(0.36f, 0.31f, 0.24f, 1f);
            parent.Add(card);
            return card;
        }

        private static void AddRemoveButton(VisualElement parent, string text, Action action)
        {
            Button button = new Button(() =>
            {
                if (EditorUtility.DisplayDialog("База развития", text + "?", "Удалить", "Отмена"))
                    action();
            }) { text = text };
            button.style.alignSelf = Align.FlexEnd;
            parent.Add(button);
        }

        private void AddArrayElement(string listName, string page, Action<SerializedProperty> init)
        {
            serializedDatabase.Update();
            SerializedProperty list = serializedDatabase.FindProperty(listName);
            list.arraySize++;
            init(list.GetArrayElementAtIndex(list.arraySize - 1));
            serializedDatabase.ApplyModifiedProperties();
            OnDatabaseChanged();
            ShowPage(page);
        }

        private void RemoveArrayElement(string listName, int index, string page)
        {
            serializedDatabase.Update();
            SerializedProperty list = serializedDatabase.FindProperty(listName);
            if (index >= 0 && index < list.arraySize)
                list.DeleteArrayElementAtIndex(index);
            serializedDatabase.ApplyModifiedProperties();
            OnDatabaseChanged();
            ShowPage(page);
        }

        // Названия компетенций для выпадающих списков.
        private List<string> CompetencyIds()
        {
            return database.competencies.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.id)).Select(entry => entry.id).ToList();
        }

        private string CompetencyName(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "— нет —";
            CompetencyRecord entry = database.competencies.Find(candidate => candidate != null && candidate.id == id);
            return entry != null && !string.IsNullOrWhiteSpace(entry.displayName) ? entry.displayName : id;
        }
    }
}
