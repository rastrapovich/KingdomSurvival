using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.Encounters.Editor
{
    // Вкладка «Проверка пула»: какой пул проверяем, тестовые условия,
    // Монте-Карло (одна возможность N раз), 100 путешествий по 30 дней и
    // настройки пулов. Ничего не меняет в базе, кроме явной правки пулов.
    public sealed partial class EncounterDatabaseWindow
    {
        private const int DefaultSimulationTrials = 10000;

        [SerializeField] private string selectedPoolId = string.Empty;
        private IntegerField simulationTrialsField;
        private VisualElement simulationResult;

        private void BuildPoolCheckTab(VisualElement root)
        {
            ScrollView pane = new ScrollView();
            pane.style.flexGrow = 1f;
            pane.style.paddingLeft = 12f;
            pane.style.paddingRight = 12f;
            pane.style.paddingTop = 8f;
            root.Add(pane);

            serializedDatabase.Update();
            List<string> poolIds = new List<string>();
            foreach (EncounterPoolDefinition pool in database.Pools)
                if (pool != null && !string.IsNullOrEmpty(pool.PoolId))
                    poolIds.Add(pool.PoolId);
            if (poolIds.Count == 0)
            {
                pane.Add(MakeMutedLabel("В базе нет пулов."));
                return;
            }
            if (!poolIds.Contains(selectedPoolId))
                selectedPoolId = poolIds[0];

            string PoolLabel(string id)
            {
                EncounterPoolDefinition pool = database.FindPool(id);
                string name = pool != null && !string.IsNullOrWhiteSpace(pool.DisplayName) ? pool.DisplayName : id;
                return showProduction ? name + "  [" + id + "]" : name;
            }
            PopupField<string> poolField = new PopupField<string>("Пул", poolIds, selectedPoolId, PoolLabel, PoolLabel);
            poolField.RegisterValueChangedCallback(evt => { selectedPoolId = evt.newValue; ShowActiveTab(); });
            pane.Add(poolField);

            int members = 0;
            foreach (EncounterDefinition encounter in database.Encounters)
                if (encounter != null && encounter.PoolId == selectedPoolId && encounter.Status == EncounterStatus.Production)
                    members++;
            pane.Add(MakeMutedLabel("Готовых встреч в пуле: " + members + "."));

            AddHeader(pane, "ТЕСТОВЫЕ УСЛОВИЯ");
            BuildPreviewContextFields(pane);

            AddHeader(pane, "СИМУЛЯЦИЯ");
            simulationTrialsField = new IntegerField("Прогонов одной возможности") { value = DefaultSimulationTrials };
            pane.Add(simulationTrialsField);
            VisualElement buttons = Row();
            buttons.Add(new Button(RunMonteCarloSimulation) { text = "Что выпадает чаще", tooltip = "Одна и та же возможность встречи N раз: распределение результатов" });
            buttons.Add(new Button(RunJourneySimulation) { text = "100 путешествий по 30 дней", tooltip = "Дни идут подряд: учитываются одноразовость, перерывы и флаги" });
            pane.Add(buttons);
            simulationResult = new VisualElement();
            pane.Add(simulationResult);

            AddHeader(pane, "НАСТРОЙКИ ПУЛА");
            BuildPoolSettings(pane, selectedPoolId);
        }

        private void BuildPoolSettings(VisualElement parent, string poolId)
        {
            int index = -1;
            for (int i = 0; i < poolsProperty.arraySize; i++)
                if (poolsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("PoolId").stringValue == poolId)
                    index = i;
            if (index < 0)
                return;

            SerializedProperty pool = poolsProperty.GetArrayElementAtIndex(index);
            void Apply()
            {
                serializedDatabase.ApplyModifiedProperties();
                EditorUtility.SetDirty(database);
            }

            if (showProduction)
            {
                TextField id = new TextField("ID пула") { value = pool.FindPropertyRelative("PoolId").stringValue };
                id.RegisterValueChangedCallback(evt => { pool.FindPropertyRelative("PoolId").stringValue = evt.newValue; Apply(); });
                parent.Add(id);
            }

            TextField name = new TextField("Название") { value = pool.FindPropertyRelative("DisplayName").stringValue };
            name.RegisterValueChangedCallback(evt => { pool.FindPropertyRelative("DisplayName").stringValue = evt.newValue; Apply(); });
            parent.Add(name);

            Toggle enabled = new Toggle("Пул включён") { value = pool.FindPropertyRelative("Enabled").boolValue };
            enabled.RegisterValueChangedCallback(evt => { pool.FindPropertyRelative("Enabled").boolValue = evt.newValue; Apply(); });
            parent.Add(enabled);

            SliderInt trigger = new SliderInt("Шанс срабатывания, %", 0, 100)
            {
                value = pool.FindPropertyRelative("GlobalTriggerChancePercent").intValue,
                showInputField = true,
                tooltip = "Шанс, что при возможности вообще случится встреча из этого пула"
            };
            trigger.RegisterValueChangedCallback(evt => { pool.FindPropertyRelative("GlobalTriggerChancePercent").intValue = evt.newValue; Apply(); });
            parent.Add(trigger);

            IntegerField gap = new IntegerField("Перерыв между встречами, ч") { value = pool.FindPropertyRelative("MinimumHoursBetweenEncounters").intValue };
            gap.RegisterValueChangedCallback(evt => { pool.FindPropertyRelative("MinimumHoursBetweenEncounters").intValue = Mathf.Max(0, evt.newValue); Apply(); });
            parent.Add(gap);

            if (showProduction)
            {
                IntegerField reactiveGap = new IntegerField("Перерыв между реакциями, ч") { value = pool.FindPropertyRelative("MinimumHoursBetweenReactiveEncounters").intValue };
                reactiveGap.RegisterValueChangedCallback(evt => { pool.FindPropertyRelative("MinimumHoursBetweenReactiveEncounters").intValue = Mathf.Max(0, evt.newValue); Apply(); });
                parent.Add(reactiveGap);

                IntegerField reactivePerDay = new IntegerField("Реакций за день пути, макс.") { value = pool.FindPropertyRelative("MaxReactiveEncountersPerTravelDay").intValue };
                reactivePerDay.RegisterValueChangedCallback(evt => { pool.FindPropertyRelative("MaxReactiveEncountersPerTravelDay").intValue = Mathf.Max(0, evt.newValue); Apply(); });
                parent.Add(reactivePerDay);
            }

            TextField notes = new TextField("Заметки") { value = pool.FindPropertyRelative("DesignerNotes").stringValue, multiline = true };
            notes.style.whiteSpace = WhiteSpace.Normal;
            notes.RegisterValueChangedCallback(evt => { pool.FindPropertyRelative("DesignerNotes").stringValue = evt.newValue; Apply(); });
            parent.Add(notes);
        }

        private string DisplayNameOf(string encounterId)
        {
            EncounterDefinition encounter = database.FindById(encounterId);
            if (encounter == null || string.IsNullOrWhiteSpace(encounter.DisplayName))
                return encounterId;
            return showProduction ? encounter.DisplayName + "  [" + encounterId + "]" : encounter.DisplayName;
        }

        // Одна и та же возможность N раз из одного состояния мира.
        private void RunMonteCarloSimulation()
        {
            simulationResult.Clear();
            serializedDatabase.ApplyModifiedProperties();

            int trials = Mathf.Clamp(simulationTrialsField.value, 1, 200000);
            List<string> tags = SplitCsv(previewContext.LocationTags);
            List<string> flags = SplitLines(previewContext.Flags);

            Dictionary<string, int> counts = new Dictionary<string, int>();
            const string noneKey = "— ничего не случилось —";

            for (int i = 0; i < trials; i++)
            {
                NarrativeStateData state = new NarrativeStateData();
                foreach (string flag in flags)
                    state.SetFlag(flag);

                EncounterOpportunity opportunity = new EncounterOpportunity
                {
                    OpportunityId = "MC_" + i,
                    PoolId = selectedPoolId,
                    WorldHour = previewContext.WorldHour,
                    RegionId = previewContext.Region,
                    LocationTags = tags
                };

                EncounterSelectionResult result = EncounterSelector.Select(
                    opportunity, database, BuildEvaluationContext(state, 987654321), new EncounterRuntimeStateData());

                string key = result.HasSelection ? result.SelectedEncounter.EncounterId : noneKey;
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
            }

            AddHeader(simulationResult, "РАСПРЕДЕЛЕНИЕ · " + trials + " прогонов");
            List<KeyValuePair<string, int>> sorted = new List<KeyValuePair<string, int>>(counts);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (KeyValuePair<string, int> entry in sorted)
            {
                double percent = 100.0 * entry.Value / trials;
                Label row = new Label((entry.Key == noneKey ? noneKey : DisplayNameOf(entry.Key)) + " — " + percent.ToString("0.0") + "%");
                if (entry.Key != noneKey)
                    row.style.unityFontStyleAndWeight = FontStyle.Bold;
                simulationResult.Add(row);
            }
        }
    }
}
