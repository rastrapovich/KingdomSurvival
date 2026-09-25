using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.Encounters.Editor
{
    public sealed partial class EncounterDatabaseWindow
    {
        // 100 путешествий по 30 дней: дни идут подряд, учитываются
        // одноразовость, перерывы и флаги, которые встречи ставят и снимают.
        private void RunJourneySimulation()
        {
            simulationResult.Clear();
            serializedDatabase.ApplyModifiedProperties();
            EncounterPoolDefinition pool = database.FindPool(selectedPoolId);
            if (pool == null)
            {
                simulationResult.Add(MakeMutedLabel("Выберите пул."));
                return;
            }

            List<string> tags = SplitCsv(previewContext.LocationTags);
            List<string> flags = SplitLines(previewContext.Flags);
            Dictionary<string, int> counts = new Dictionary<string, int>();
            int eventDays = 0, silentDays = 0, longestSilence = 0, allUnique = 0;
            const int journeys = 100, days = 30;
            for (int journey = 0; journey < journeys; journey++)
            {
                EncounterRuntimeStateData history = new EncounterRuntimeStateData();
                NarrativeStateData state = new NarrativeStateData();
                foreach (string flag in flags) state.SetFlag(flag);
                HashSet<string> seen = new HashSet<string>();
                int silence = 0;
                for (int day = 0; day < days; day++)
                {
                    double hour = previewContext.WorldHour + day * 24.0;
                    EncounterOpportunity opportunity = new EncounterOpportunity
                    {
                        OpportunityId = "J_" + journey + "_" + day,
                        PoolId = pool.PoolId,
                        WorldHour = hour,
                        RegionId = previewContext.Region,
                        LocationTags = tags
                    };
                    EncounterSelectionResult result = EncounterSelector.Select(
                        opportunity, database, BuildEvaluationContext(state, 32100 + journey), history);
                    if (!result.HasSelection)
                    {
                        silentDays++;
                        silence++;
                        continue;
                    }
                    eventDays++;
                    longestSilence = Mathf.Max(longestSilence, silence);
                    silence = 0;
                    EncounterDefinition encounter = result.SelectedEncounter;
                    seen.Add(encounter.EncounterId);
                    counts.TryGetValue(encounter.EncounterId, out int count);
                    counts[encounter.EncounterId] = count + 1;
                    history.FindOrCreateEntry(encounter.EncounterId).TimesStarted++;
                    history.MarkPoolTriggered(pool.PoolId, hour);
                    if (encounter.DurationClass == EncounterDurationClass.Reaction ||
                        encounter.DurationClass == EncounterDurationClass.Micro)
                        history.MarkReactiveTriggered(pool.PoolId, hour, (int)(hour / 24.0));
                    if (encounter.FlagsSetOnStart != null)
                        foreach (string flag in encounter.FlagsSetOnStart) state.SetFlag(flag);
                    if (encounter.FlagsSetOnComplete != null)
                        foreach (string flag in encounter.FlagsSetOnComplete) state.SetFlag(flag);
                    if (encounter.ClearFlagsOnComplete != null)
                        foreach (string flag in encounter.ClearFlagsOnComplete) state.ClearFlag(flag);
                }
                longestSilence = Mathf.Max(longestSilence, silence);
                allUnique += seen.Count;
            }

            AddHeader(simulationResult, "100 ПУТЕШЕСТВИЙ ПО 30 ДНЕЙ");
            simulationResult.Add(new Label("Дни со встречей: " + (100f * eventDays / (journeys * days)).ToString("0.0") + "%"));
            simulationResult.Add(new Label("Тихие дни: " + silentDays + " · самая длинная тишина: " + longestSilence + " дн."));
            simulationResult.Add(new Label("Разных встреч за путешествие: " + (allUnique / (float)journeys).ToString("0.0") + " в среднем"));
            List<KeyValuePair<string, int>> sorted = new List<KeyValuePair<string, int>>(counts);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (KeyValuePair<string, int> pair in sorted)
                simulationResult.Add(new Label(DisplayNameOf(pair.Key) + " — выпадала " + pair.Value + " раз за " + journeys + " путешествий"));
        }
    }
}
