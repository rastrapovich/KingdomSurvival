using System;
using KingdomSurvival.DialogueDatabase;

namespace KingdomSurvival.Encounters
{
    // Единственное место с побочными эффектами в системе энкаунтеров (§61,
    // §62): выбор через pure EncounterSelector, запуск диалога, инкремент
    // occurrence СТРОГО после успешного открытия (§17), простановка
    // Flags*/Clear* памяти. Не управляет наградами/ресурсами — это остаётся
    // задачей самого диалога через существующие NarrativeEffect (§62).
    public static class EncounterRuntimeService
    {
        public static void EnsureState(GameState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (state.Encounters == null)
                state.Encounters = new EncounterRuntimeStateData();
            state.Encounters.EnsureInitialized();
        }

        public static EncounterSelectionResult SelectEncounter(
            GameState state,
            HeroProfileData hero,
            EncounterOpportunity opportunity,
            EncounterDatabaseAsset database)
        {
            EnsureState(state);
            NarrativeEvaluationContext context = EncounterEvaluationContextBuilder.Build(state, hero);
            return EncounterSelector.Select(opportunity, database, context, state.Encounters);
        }

        // Композиция SelectEncounter + StartEncounter с защитой от повторной
        // обработки одной и той же Opportunity (§56): если Opportunity уже
        // была разрешена (успешно или нет), повторный вызов не бросает
        // Selector заново и не открывает диалог повторно.
        public static EncounterRunResult TryResolveOpportunity(
            GameState state,
            HeroProfileData hero,
            EncounterOpportunity opportunity,
            EncounterDatabaseAsset database,
            DialogueDatabaseAsset dialogueDatabase,
            out NarrativeDialogueRuntimeSession session)
        {
            session = null;
            EnsureState(state);

            if (opportunity == null)
                return EncounterRunResult.Failed("opportunity_is_null");

            if (state.Encounters.WasOpportunityProcessed(opportunity.OpportunityId))
                return EncounterRunResult.Failed("opportunity_already_processed");

            EncounterSelectionResult selection = SelectEncounter(state, hero, opportunity, database);
            state.Encounters.MarkOpportunityProcessed(opportunity.OpportunityId);

            if (!selection.HasSelection)
                return EncounterRunResult.Failed("no_selection:" + selection.NoSelectionReason);

            if (!string.IsNullOrWhiteSpace(selection.PoolId))
                state.Encounters.MarkPoolTriggered(selection.PoolId, opportunity.WorldHour);

            return StartEncounter(state, hero, selection.SelectedEncounter, opportunity, dialogueDatabase, out session);
        }

        public static EncounterRunResult StartEncounter(
            GameState state,
            HeroProfileData hero,
            EncounterDefinition encounter,
            EncounterOpportunity opportunity,
            DialogueDatabaseAsset dialogueDatabase,
            out NarrativeDialogueRuntimeSession session)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (encounter == null)
                throw new ArgumentNullException(nameof(encounter));
            if (opportunity == null)
                throw new ArgumentNullException(nameof(opportunity));

            EnsureState(state);

            session = new NarrativeDialogueRuntimeSession();
            bool started = session.Start(
                dialogueDatabase,
                encounter.DialogueId,
                hero,
                state.Narrative,
                out NarrativeDialogueView view,
                out string error,
                EncounterEvaluationContextBuilder.GetPresentCompanionIds(state),
                EncounterEvaluationContextBuilder.GetPresentItemIds(state),
                state.WorldSeed,
                EncounterEvaluationContextBuilder.GetPartySize(state));

            if (!started)
                return EncounterRunResult.Failed(error);

            RecordEncounterStarted(state, encounter, opportunity.WorldHour);
            return EncounterRunResult.Ok(encounter.EncounterId, view);
        }

        // Раздельно от StartEncounter (§53 обновлённой архитектуры — единый
        // канал показа через уже существующий Narrative Dialogue): игровой UI
        // (PrototypeUIController.TryOpenNarrativeDialogueById) владеет
        // собственной NarrativeDialogueRuntimeSession и сам решает, когда
        // открывать диалог поверх карты/модальной очереди. Оркестрирующий
        // код (сейчас — точка встраивания в ExpeditionIncidentSystem)
        // обязан вызывать SelectEncounter для чистого выбора, затем — после
        // того как TryOpenNarrativeDialogueById вернул true — этот метод,
        // чтобы occurrence и FlagsSetOnStart применились СТРОГО после
        // реального открытия диалога (§17), а не раньше.
        public static void RecordEncounterStarted(GameState state, EncounterDefinition encounter, double worldHour)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (encounter == null)
                throw new ArgumentNullException(nameof(encounter));

            EnsureState(state);

            EncounterRuntimeEntry entry = state.Encounters.FindOrCreateEntry(encounter.EncounterId);
            entry.TimesStarted++;
            entry.LastStartedWorldHour = worldHour;

            if (encounter.FlagsSetOnStart != null)
            {
                foreach (string flagId in encounter.FlagsSetOnStart)
                    state.Narrative.SetFlag(flagId);
            }
        }

        // Вызывается вызывающей стороной, когда диалог реально дошёл до
        // терминального узла — модуль Encounters не отслеживает завершение
        // диалога автоматически (NarrativeDialogueRuntimeSession этого
        // наружу не сигналит).
        public static void CompleteEncounter(GameState state, EncounterDefinition encounter, double worldHour)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (encounter == null)
                throw new ArgumentNullException(nameof(encounter));

            EnsureState(state);

            EncounterRuntimeEntry entry = state.Encounters.FindOrCreateEntry(encounter.EncounterId);
            entry.TimesCompleted++;
            entry.LastCompletedWorldHour = worldHour;

            if (encounter.FlagsSetOnComplete != null)
            {
                foreach (string flagId in encounter.FlagsSetOnComplete)
                    state.Narrative.SetFlag(flagId);
            }

            if (encounter.ClearFlagsOnComplete != null)
            {
                foreach (string flagId in encounter.ClearFlagsOnComplete)
                    state.Narrative.ClearFlag(flagId);
            }
        }
    }
}
