using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.Encounters.Editor
{
    // §53: Monte Carlo Simulator. Прогоняет pure EncounterSelector N раз с
    // ОДНИМ и тем же Preview Context (см. EncounterDatabaseWindow.Encounters.cs),
    // меняя только OpportunityId — т.е. отвечает на вопрос "если эта
    // Opportunity происходила бы N раз подряд из одного и того же
    // состояния мира, как распределился бы выбор". Каждая итерация получает
    // свежий EncounterRuntimeStateData: Simulation не меняет ни database, ни
    // настоящий рантайм — только считает (§53 "Simulation не меняет GameState").
    public sealed partial class EncounterDatabaseWindow
    {
        private const int DefaultSimulationTrials = 10000;

        private IntegerField simulationTrialsField;
        private VisualElement simulationResult;

        private void BuildMonteCarloSection(VisualElement parent)
        {
            AddHeader(parent, "МОНТЕ-КАРЛО");
            parent.Add(MakeMutedLabel(
                "Прогоняет пул выбранного энкаунтера N раз с текущим контекстом предпросмотра выше " +
                "(меняется только ID возможности). Не меняет базу и не требует Play Mode."));

            simulationTrialsField = new IntegerField("Прогонов") { value = DefaultSimulationTrials };
            parent.Add(simulationTrialsField);

            Button simulateButton = new Button(RunMonteCarloSimulation) { text = "СИМУЛИРОВАТЬ" };
            simulateButton.style.marginTop = 6f;
            simulateButton.style.marginBottom = 8f;
            parent.Add(simulateButton);

            simulationResult = new VisualElement();
            parent.Add(simulationResult);
        }

        private void RunMonteCarloSimulation()
        {
            simulationResult.Clear();

            if (selectedEncounterIndex < 0 || selectedEncounterIndex >= database.Encounters.Count)
            {
                simulationResult.Add(MakeMutedLabel("Выберите энкаунтер слева — симуляция прогоняет его пул."));
                return;
            }

            serializedDatabase.ApplyModifiedProperties();
            EncounterDefinition selected = database.Encounters[selectedEncounterIndex];

            if (selected.SelectionMode != EncounterSelectionMode.Pool || string.IsNullOrWhiteSpace(selected.PoolId))
            {
                simulationResult.Add(MakeMutedLabel("Выбранный энкаунтер не в пуле (Direct) — Монте-Карло применим только к пулам."));
                return;
            }

            int trials = Mathf.Clamp(simulationTrialsField.value, 1, 200000);
            string poolId = selected.PoolId;

            HeroProfileData hero = new HeroProfileData();
            foreach (KeyValuePair<HeroQuality, IntegerField> entry in previewQualities)
                hero.SetQuality(entry.Key, entry.Value.value);

            List<string> tags = SplitCsv(previewLocationTags.value);
            List<string> flags = SplitLines(previewFlags.value);
            string region = previewRegion.value;
            double worldHour = previewWorldHour.value;
            int partySize = Mathf.Max(1, previewPartySize.value);

            Dictionary<string, int> counts = new Dictionary<string, int>();
            Dictionary<EncounterDurationClass, int> durationCounts = new Dictionary<EncounterDurationClass, int>();
            const string noneKey = "— Нет —";

            for (int i = 0; i < trials; i++)
            {
                NarrativeStateData state = new NarrativeStateData();
                foreach (string flag in flags)
                    state.SetFlag(flag);

                NarrativeEvaluationContext context = new NarrativeEvaluationContext(
                    hero, state, partySize: partySize, worldSeed: 987654321);

                EncounterOpportunity opportunity = new EncounterOpportunity
                {
                    OpportunityId = "MC_" + i,
                    PoolId = poolId,
                    WorldHour = worldHour,
                    RegionId = region,
                    LocationTags = tags
                };

                EncounterSelectionResult result = EncounterSelector.Select(
                    opportunity, database, context, new EncounterRuntimeStateData());

                string key = result.HasSelection ? result.SelectedEncounter.EncounterId : noneKey;
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;

                if (result.HasSelection)
                {
                    durationCounts.TryGetValue(result.SelectedEncounter.DurationClass, out int durationCount);
                    durationCounts[result.SelectedEncounter.DurationClass] = durationCount + 1;
                }
            }

            AddHeader(simulationResult, "РАСПРЕДЕЛЕНИЕ (" + trials + " прогонов, пул " + poolId + ")");

            List<KeyValuePair<string, int>> sorted = new List<KeyValuePair<string, int>>(counts);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (KeyValuePair<string, int> entry in sorted)
            {
                double percent = 100.0 * entry.Value / trials;
                Label row = new Label(entry.Key + "  —  " + entry.Value + "  (" + percent.ToString("0.0") + "%)");
                if (entry.Key != noneKey)
                    row.style.unityFontStyleAndWeight = FontStyle.Bold;
                simulationResult.Add(row);
            }

            if (durationCounts.Count > 0)
            {
                AddHeader(simulationResult, "ПО КЛАССУ ДЛИТЕЛЬНОСТИ");
                List<KeyValuePair<EncounterDurationClass, int>> durationSorted = new List<KeyValuePair<EncounterDurationClass, int>>(durationCounts);
                durationSorted.Sort((a, b) => b.Value.CompareTo(a.Value));
                foreach (KeyValuePair<EncounterDurationClass, int> entry in durationSorted)
                {
                    double percent = 100.0 * entry.Value / trials;
                    simulationResult.Add(new Label(entry.Key + "  —  " + percent.ToString("0.0") + "%"));
                }
            }
        }
    }
}
