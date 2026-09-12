using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.Encounters.Editor
{
    public sealed partial class EncounterDatabaseWindow
    {
        private enum StatusFilter { All, Draft, Production, Disabled, Deprecated }

        // "Reactive" в исходной терминологии — это Reaction + Micro (§93-95).
        // Отдельного значения ReactiveOnly не завожу: фильтр по конкретному
        // DurationClass уже покрывает и "покажи мне только Reaction", и
        // "только Micro" по отдельности, что строже и полезнее одной общей
        // группы.
        private enum DurationFilter { All, Reaction, Micro, Short, Standard, Complex, QuestSeed }

        private readonly List<int> visibleEncounterIndices = new List<int>();
        private TextField encounterSearch;
        private EnumField encounterStatusFilter;
        private EnumField encounterDurationFilter;
        private ListView encounterList;
        private ScrollView encounterDetails;
        private ScrollView diagnosticsPane;
        private Label encounterEmptyHint;

        // Preview Context для Eligibility (§48): ручной ввод, без Play Mode.
        private TextField previewRegion;
        private TextField previewLocationTags;
        private FloatField previewWorldHour;
        private TextField previewFlags;
        private IntegerField previewPartySize;
        private readonly Dictionary<HeroQuality, IntegerField> previewQualities = new Dictionary<HeroQuality, IntegerField>();
        private VisualElement diagnosticsResult;

        private void BuildEncountersTab(VisualElement root)
        {
            VisualElement toolbar = new VisualElement();
            toolbar.style.height = 42f;
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.paddingLeft = 8f;
            toolbar.style.paddingRight = 8f;
            AddToolbarButton(toolbar, "+ НОВЫЙ", AddEncounter);
            AddToolbarButton(toolbar, "ДУБЛИРОВАТЬ", DuplicateEncounter);
            AddToolbarButton(toolbar, "СПИСАТЬ", DeprecateEncounter);
            AddToolbarButton(toolbar, "ПРОВЕРИТЬ БАЗУ", ValidateEncounterDatabase);
            validationLabel = new Label();
            validationLabel.style.flexGrow = 1f;
            validationLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            toolbar.Add(validationLabel);
            root.Add(toolbar);

            TwoPaneSplitView outerSplit = new TwoPaneSplitView(0, 320f, TwoPaneSplitViewOrientation.Horizontal);
            outerSplit.style.flexGrow = 1f;
            outerSplit.Add(BuildEncounterListPane());

            TwoPaneSplitView innerSplit = new TwoPaneSplitView(0, 480f, TwoPaneSplitViewOrientation.Horizontal);
            innerSplit.Add(BuildEncounterDetailPane());
            innerSplit.Add(BuildDiagnosticsPane());
            outerSplit.Add(innerSplit);

            root.Add(outerSplit);

            RefreshEncounterList();
            RestoreEncounterSelection();
        }

        // ---------------------------------------------------------------
        // Список (левая колонка)
        // ---------------------------------------------------------------

        private VisualElement BuildEncounterListPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.paddingLeft = 8f;
            pane.style.paddingRight = 8f;
            pane.style.paddingTop = 8f;
            pane.style.paddingBottom = 8f;

            encounterSearch = new TextField("Поиск");
            encounterSearch.RegisterValueChangedCallback(_ => RefreshEncounterList());
            pane.Add(encounterSearch);

            encounterStatusFilter = new EnumField("Статус", StatusFilter.All);
            encounterStatusFilter.RegisterValueChangedCallback(_ => RefreshEncounterList());
            pane.Add(encounterStatusFilter);

            encounterDurationFilter = new EnumField("Класс длительности", DurationFilter.All);
            encounterDurationFilter.RegisterValueChangedCallback(_ => RefreshEncounterList());
            pane.Add(encounterDurationFilter);

            Foldout poolsFoldout = new Foldout { text = "ПУЛЫ" };
            poolsFoldout.value = false;
            poolsFoldout.Add(new PropertyField(poolsProperty, string.Empty));
            poolsFoldout.Bind(serializedDatabase);
            pane.Add(poolsFoldout);

            encounterList = new ListView();
            encounterList.style.flexGrow = 1f;
            encounterList.style.marginTop = 8f;
            encounterList.fixedItemHeight = 56f;
            encounterList.selectionType = SelectionType.Single;
            encounterList.makeItem = MakeEncounterListItem;
            encounterList.bindItem = BindEncounterListItem;
            encounterList.selectionChanged += _ => SelectVisibleEncounter(encounterList.selectedIndex);
            pane.Add(encounterList);

            return pane;
        }

        private static VisualElement MakeEncounterListItem()
        {
            VisualElement row = new VisualElement();
            row.style.paddingTop = 4f;
            row.style.paddingBottom = 4f;

            Label title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(title);

            Label id = new Label { name = "id" };
            id.style.fontSize = 10f;
            id.style.color = MutedColor;
            row.Add(id);

            Label meta = new Label { name = "meta" };
            meta.style.fontSize = 10f;
            meta.style.color = MutedColor;
            row.Add(meta);

            return row;
        }

        private void BindEncounterListItem(VisualElement row, int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleEncounterIndices.Count)
                return;

            EncounterDefinition encounter = database.Encounters[visibleEncounterIndices[visibleIndex]];
            string statusDot = StatusDot(encounter.Status);

            row.Q<Label>("title").text = statusDot + " " + encounter.DisplayName;
            row.Q<Label>("id").text = encounter.EncounterId;

            string poolLabel = encounter.SelectionMode == EncounterSelectionMode.Pool
                ? encounter.PoolId
                : "Прямой";
            string occurrences = encounter.UnlimitedOccurrences ? "∞" : "0/" + encounter.MaxOccurrencesPerGame;
            row.Q<Label>("meta").text =
                "[" + DurationBadge(encounter.DurationClass) + "] " +
                poolLabel + "  ·  " + encounter.DiscoveryChancePercent + "%  ·  W" +
                encounter.SelectionWeight + "  ·  " + occurrences;
        }

        private static string DurationBadge(EncounterDurationClass durationClass)
        {
            switch (durationClass)
            {
                case EncounterDurationClass.Reaction: return "R";
                case EncounterDurationClass.Micro: return "M";
                case EncounterDurationClass.Short: return "S";
                case EncounterDurationClass.Standard: return "E";
                case EncounterDurationClass.Complex: return "C";
                case EncounterDurationClass.QuestSeed: return "Q";
                default: return "?";
            }
        }

        private static bool MatchesDurationFilter(EncounterDurationClass durationClass, DurationFilter filter)
        {
            switch (filter)
            {
                case DurationFilter.Reaction: return durationClass == EncounterDurationClass.Reaction;
                case DurationFilter.Micro: return durationClass == EncounterDurationClass.Micro;
                case DurationFilter.Short: return durationClass == EncounterDurationClass.Short;
                case DurationFilter.Standard: return durationClass == EncounterDurationClass.Standard;
                case DurationFilter.Complex: return durationClass == EncounterDurationClass.Complex;
                case DurationFilter.QuestSeed: return durationClass == EncounterDurationClass.QuestSeed;
                default: return true;
            }
        }

        private static bool MatchesStatusFilter(EncounterStatus status, StatusFilter filter)
        {
            switch (filter)
            {
                case StatusFilter.Draft: return status == EncounterStatus.Draft;
                case StatusFilter.Production: return status == EncounterStatus.Production;
                case StatusFilter.Disabled: return status == EncounterStatus.Disabled;
                case StatusFilter.Deprecated: return status == EncounterStatus.Deprecated;
                default: return true;
            }
        }

        private static string StatusDot(EncounterStatus status)
        {
            switch (status)
            {
                case EncounterStatus.Production: return "●";
                case EncounterStatus.Draft: return "○";
                case EncounterStatus.Disabled: return "◐";
                case EncounterStatus.Deprecated: return "✕";
                default: return "?";
            }
        }

        private void RefreshEncounterList()
        {
            if (database == null || encounterList == null)
                return;

            serializedDatabase.Update();
            visibleEncounterIndices.Clear();

            string query = encounterSearch != null ? encounterSearch.value.Trim() : string.Empty;
            StatusFilter filter = encounterStatusFilter != null
                ? (StatusFilter)encounterStatusFilter.value
                : StatusFilter.All;
            DurationFilter durationFilter = encounterDurationFilter != null
                ? (DurationFilter)encounterDurationFilter.value
                : DurationFilter.All;

            for (int i = 0; i < database.Encounters.Count; i++)
            {
                EncounterDefinition encounter = database.Encounters[i];
                if (encounter == null)
                    continue;

                if (filter != StatusFilter.All && !MatchesStatusFilter(encounter.Status, filter))
                    continue;

                if (durationFilter != DurationFilter.All && !MatchesDurationFilter(encounter.DurationClass, durationFilter))
                    continue;

                if (!string.IsNullOrEmpty(query) &&
                    (encounter.EncounterId == null || encounter.EncounterId.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) &&
                    (encounter.DisplayName == null || encounter.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;

                visibleEncounterIndices.Add(i);
            }

            encounterList.itemsSource = visibleEncounterIndices;
            encounterList.Rebuild();
            int visible = visibleEncounterIndices.IndexOf(selectedEncounterIndex);
            if (visible >= 0)
                encounterList.SetSelectionWithoutNotify(new[] { visible });
        }

        private void RestoreEncounterSelection()
        {
            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= database.Encounters.Count)
                selectedEncounterIndex = database.Encounters.Count > 0 ? 0 : -1;
            ShowSelectedEncounter();
        }

        private void SelectVisibleEncounter(int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleEncounterIndices.Count)
                return;
            selectedEncounterIndex = visibleEncounterIndices[visibleIndex];
            ShowSelectedEncounter();
        }

        // ---------------------------------------------------------------
        // Инспектор (средняя колонка)
        // ---------------------------------------------------------------

        private VisualElement BuildEncounterDetailPane()
        {
            VisualElement pane = new VisualElement();
            pane.style.flexGrow = 1f;

            encounterEmptyHint = new Label("Выберите Encounter слева.");
            encounterEmptyHint.style.flexGrow = 1f;
            encounterEmptyHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            pane.Add(encounterEmptyHint);

            encounterDetails = new ScrollView();
            encounterDetails.style.display = DisplayStyle.None;
            encounterDetails.style.flexGrow = 1f;
            encounterDetails.style.paddingLeft = 16f;
            encounterDetails.style.paddingRight = 16f;
            encounterDetails.style.paddingTop = 12f;
            encounterDetails.style.paddingBottom = 16f;
            encounterDetails.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                serializedDatabase.ApplyModifiedProperties();
                EditorUtility.SetDirty(database);
                encounterList.RefreshItems();
            });
            pane.Add(encounterDetails);
            return pane;
        }

        private void ShowSelectedEncounter()
        {
            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= encountersProperty.arraySize)
            {
                encounterEmptyHint.style.display = DisplayStyle.Flex;
                encounterDetails.style.display = DisplayStyle.None;
                ClearDiagnosticsResult();
                return;
            }

            encounterEmptyHint.style.display = DisplayStyle.None;
            encounterDetails.style.display = DisplayStyle.Flex;
            encounterDetails.Clear();

            SerializedProperty e = encountersProperty.GetArrayElementAtIndex(selectedEncounterIndex);

            AddHeader(encounterDetails, "ИДЕНТИФИКАЦИЯ");
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("EncounterId"), "ID энкаунтера"));
            encounterDetails.Add(MakeMutedLabel("Production ID неизменяем после выхода в Production (§6) — переименовывать вручную только Draft."));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("DisplayName"), "Название"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("Description"), "Описание"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("Status"), "Статус"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("Category"), "Категория"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("Tags"), "Теги"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("DurationClass"), "Класс длительности"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("MemoryClass"), "Класс памяти"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("Functions"), "Функции"));

            AddHeader(encounterDetails, "КОНТЕНТ");
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("DialogueId"), "ID диалога"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("ResolutionMode"), "Режим разрешения"));

            AddHeader(encounterDetails, "ВЫБОР");
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("SelectionMode"), "Режим выбора"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("PoolId"), "ID пула"));
            SerializedProperty chanceProp = e.FindPropertyRelative("DiscoveryChancePercent");
            PropertyField chanceField = new PropertyField(chanceProp, "Шанс обнаружения %");
            encounterDetails.Add(chanceField);
            encounterDetails.Add(MakeMutedLabel("Базовый шанс пройти в список кандидатов — НЕ итоговая вероятность увидеть сцену (§13)."));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("SelectionWeight"), "Вес выбора"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("UnlimitedOccurrences"), "Без ограничения появлений"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("MaxOccurrencesPerGame"), "Макс. появлений"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("CooldownHours"), "Кулдаун (часы)"));

            AddHeader(encounterDetails, "МЕСТО");
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("AllowedRegionIds"), "Разрешённые регионы"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("RequiredLocationTags"), "Обязательные теги"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("ForbiddenLocationTags"), "Запрещённые теги"));

            AddHeader(encounterDetails, "УСЛОВИЯ");
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("RequiredConditions"), "Обязательные условия"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("RequiredFlagsAll"), "Обязательные флаги (все)"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("RequiredFlagsAny"), "Обязательные флаги (любой)"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("ForbiddenFlags"), "Запрещённые флаги"));

            AddHeader(encounterDetails, "ПАМЯТЬ");
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("FlagsSetOnStart"), "Флаги при старте"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("FlagsSetOnComplete"), "Флаги при завершении"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("ClearFlagsOnComplete"), "Очистить флаги при завершении"));

            AddHeader(encounterDetails, "ДОКУМЕНТАЦИЯ");
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("DesignerNotes"), "Заметки дизайнера"));
            encounterDetails.Add(new PropertyField(e.FindPropertyRelative("FutureHooksNotes"), "Заметки о будущих зацепках"));

            encounterDetails.Bind(serializedDatabase);
            ClearDiagnosticsResult();
        }

        // ---------------------------------------------------------------
        // Diagnostics (правая колонка) — Eligibility Preview, без Play Mode.
        // ---------------------------------------------------------------

        private VisualElement BuildDiagnosticsPane()
        {
            diagnosticsPane = new ScrollView();
            diagnosticsPane.style.flexGrow = 1f;
            diagnosticsPane.style.paddingLeft = 12f;
            diagnosticsPane.style.paddingRight = 12f;
            diagnosticsPane.style.paddingTop = 12f;

            AddHeader(diagnosticsPane, "КОНТЕКСТ ПРЕДПРОСМОТРА");
            diagnosticsPane.Add(MakeMutedLabel(
                "В Edit Mode нет реального GameState — контекст задаётся вручную (§48). " +
                "Проверяет только выбранный слева Encounter, не весь пул."));

            previewRegion = new TextField("Регион") { value = "road" };
            diagnosticsPane.Add(previewRegion);

            previewLocationTags = new TextField("Теги локации (через запятую)");
            diagnosticsPane.Add(previewLocationTags);

            previewWorldHour = new FloatField("Мировые часы") { value = 0f };
            diagnosticsPane.Add(previewWorldHour);

            previewPartySize = new IntegerField("Размер отряда") { value = 1 };
            diagnosticsPane.Add(previewPartySize);

            previewFlags = new TextField("Установленные флаги (по одному на строку)") { multiline = true };
            previewFlags.style.minHeight = 44f;
            diagnosticsPane.Add(previewFlags);

            Foldout qualitiesFoldout = new Foldout { text = "Качества героя" };
            previewQualities.Clear();
            foreach (HeroQuality quality in Enum.GetValues(typeof(HeroQuality)))
            {
                IntegerField field = new IntegerField(quality.ToString()) { value = HeroProfileData.DefaultQualityValue };
                previewQualities[quality] = field;
                qualitiesFoldout.Add(field);
            }
            diagnosticsPane.Add(qualitiesFoldout);

            Button checkButton = new Button(RunEligibilityPreview) { text = "ПРОВЕРИТЬ ДОСТУПНОСТЬ" };
            checkButton.style.marginTop = 8f;
            checkButton.style.marginBottom = 8f;
            diagnosticsPane.Add(checkButton);

            diagnosticsResult = new VisualElement();
            diagnosticsPane.Add(diagnosticsResult);

            BuildMonteCarloSection(diagnosticsPane);

            return diagnosticsPane;
        }

        private void ClearDiagnosticsResult()
        {
            diagnosticsResult?.Clear();
        }

        private void RunEligibilityPreview()
        {
            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= database.Encounters.Count)
                return;

            serializedDatabase.ApplyModifiedProperties();
            EncounterDefinition encounter = database.Encounters[selectedEncounterIndex];

            HeroProfileData hero = new HeroProfileData();
            foreach (KeyValuePair<HeroQuality, IntegerField> entry in previewQualities)
                hero.SetQuality(entry.Key, entry.Value.value);

            NarrativeStateData state = new NarrativeStateData();
            foreach (string flag in SplitLines(previewFlags.value))
                state.SetFlag(flag);

            NarrativeEvaluationContext context = new NarrativeEvaluationContext(
                hero, state, partySize: Mathf.Max(1, previewPartySize.value));

            List<string> tags = SplitCsv(previewLocationTags.value);

            EncounterEligibilityResult result = EncounterEligibilityEvaluator.Evaluate(
                encounter, context, new EncounterRuntimeStateData(),
                previewRegion.value, tags, previewWorldHour.value);

            diagnosticsResult.Clear();
            AddHeader(diagnosticsResult, result.Eligible ? "ДОСТУПНО" : "ЗАБЛОКИРОВАНО");
            Label verdict = new Label(result.Eligible ? "✓ Все условия выполнены." : "✗ Заблокирован:");
            verdict.style.color = result.Eligible ? GoodColor : BadColor;
            verdict.style.unityFontStyleAndWeight = FontStyle.Bold;
            diagnosticsResult.Add(verdict);

            foreach (string reason in result.BlockReasons)
            {
                Label reasonLabel = new Label("✗ " + reason);
                reasonLabel.style.whiteSpace = WhiteSpace.Normal;
                reasonLabel.style.color = BadColor;
                reasonLabel.style.marginLeft = 8f;
                diagnosticsResult.Add(reasonLabel);
            }

            diagnosticsResult.Add(MakeMutedLabel(
                "Не учитывает Pool Trigger Chance и Discovery Roll — только чистую Eligibility (§25)."));
        }

        private static List<string> SplitLines(string text)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    result.Add(trimmed);
            }
            return result;
        }

        private static List<string> SplitCsv(string text)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;
            foreach (string part in text.Split(','))
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    result.Add(trimmed);
            }
            return result;
        }

        // ---------------------------------------------------------------
        // Toolbar actions
        // ---------------------------------------------------------------

        private void AddEncounter()
        {
            serializedDatabase.Update();
            int index = encountersProperty.arraySize++;
            SerializedProperty e = encountersProperty.GetArrayElementAtIndex(index);
            e.FindPropertyRelative("EncounterId").stringValue = MakeUniqueEncounterId("NEW_ENCOUNTER");
            e.FindPropertyRelative("DisplayName").stringValue = "Новый Encounter";
            e.FindPropertyRelative("Status").enumValueIndex = (int)EncounterStatus.Draft;
            e.FindPropertyRelative("SelectionMode").enumValueIndex = (int)EncounterSelectionMode.Pool;
            e.FindPropertyRelative("SelectionWeight").intValue = 1;
            e.FindPropertyRelative("MaxOccurrencesPerGame").intValue = 1;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            selectedEncounterIndex = index;
            RefreshEncounterList();
            ShowSelectedEncounter();
        }

        private void DuplicateEncounter()
        {
            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= encountersProperty.arraySize)
                return;
            serializedDatabase.Update();
            encountersProperty.InsertArrayElementAtIndex(selectedEncounterIndex);
            selectedEncounterIndex++;
            SerializedProperty e = encountersProperty.GetArrayElementAtIndex(selectedEncounterIndex);
            string sourceId = e.FindPropertyRelative("EncounterId").stringValue;
            e.FindPropertyRelative("EncounterId").stringValue = MakeUniqueEncounterId(sourceId + "_COPY");
            e.FindPropertyRelative("Status").enumValueIndex = (int)EncounterStatus.Draft;
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            RefreshEncounterList();
            ShowSelectedEncounter();
        }

        // §7/§44: Production Encounter физически не удаляется — переводится
        // в Disabled/Deprecated, чтобы не сломать старые сохранения,
        // помнящие этот EncounterId. Draft можно удалить сразу.
        private void DeprecateEncounter()
        {
            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= encountersProperty.arraySize)
                return;

            serializedDatabase.Update();
            SerializedProperty e = encountersProperty.GetArrayElementAtIndex(selectedEncounterIndex);
            EncounterStatus status = (EncounterStatus)e.FindPropertyRelative("Status").enumValueIndex;
            string id = e.FindPropertyRelative("EncounterId").stringValue;

            if (status == EncounterStatus.Draft)
            {
                if (!EditorUtility.DisplayDialog("Удалить Encounter", "Удалить черновик «" + id + "»?", "Удалить", "Отмена"))
                    return;
                encountersProperty.DeleteArrayElementAtIndex(selectedEncounterIndex);
                selectedEncounterIndex = Mathf.Clamp(selectedEncounterIndex - 1, -1, database.Encounters.Count - 1);
            }
            else
            {
                e.FindPropertyRelative("Status").enumValueIndex = (int)EncounterStatus.Deprecated;
            }

            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            RefreshEncounterList();
            ShowSelectedEncounter();
        }

        private void ValidateEncounterDatabase()
        {
            serializedDatabase.ApplyModifiedProperties();
            List<string> issues = new List<string>();
            database.CollectValidationIssues(issues);
            validationLabel.text = issues.Count == 0 ? "Ошибок не найдено" : "Ошибок: " + issues.Count;
            validationLabel.style.color = issues.Count == 0 ? GoodColor : BadColor;
            if (issues.Count > 0)
                Debug.LogWarning("База энкаунтеров:\n- " + string.Join("\n- ", issues));
        }

        private string MakeUniqueEncounterId(string baseId)
        {
            string candidate = baseId;
            int suffix = 2;
            while (database.FindById(candidate) != null)
                candidate = baseId + "_" + suffix++;
            return candidate;
        }
    }
}
