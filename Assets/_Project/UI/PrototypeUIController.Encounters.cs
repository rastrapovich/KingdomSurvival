using KingdomSurvival.Encounters;
using UnityEngine;

// Склейка дорожной симуляции (ExpeditionIncidentSystem.SurfaceRoadEncounterOpportunity
// в Scripts/Core, который сам не может ссылаться на модуль Encounters — у
// Core нет зависимостей) и уже существующего единого канала показа диалогов
// (TryOpenNarrativeDialogueById, PrototypeUIController.Narrative.cs). Отдельный
// файл, а не правка PrototypeUIController.ContinuousTime.cs — по тому же
// принципу разделения ответственности, что и Narrative.cs/HeroScreen.cs и т.д.
public partial class PrototypeUIController
{
    private EncounterDatabaseAsset encounterDatabase;

    private EncounterDatabaseAsset LoadEncounterDatabase()
    {
        if (encounterDatabase == null)
            encounterDatabase = Resources.Load<EncounterDatabaseAsset>(EncounterDatabaseAsset.ResourcesPath);
        return encounterDatabase;
    }

    // Вызывается из ProcessContinuousSimulationBatch только когда ни один
    // более приоритетный модальный интерфейс не претендует на внимание в
    // этом же пакете (см. вызов в PrototypeUIController.ContinuousTime.cs).
    // EncounterRuntimeService.SelectEncounter — pure (EncounterSelector
    // ничего не мутирует); occurrence и FlagsSetOnStart применяются через
    // RecordEncounterStarted СТРОГО после успешного TryOpenNarrativeDialogueById —
    // именно открытие диалога, а не сам выбор, считается тем, что "игрок
    // увидел Encounter" (§17 Encounter-инструкции).
    private void TryResolveRoadEncounterOpportunity(StrategicSimulationResult result)
    {
        if (result == null || !result.HasRoadEncounterOpportunity || gameState == null || isGameOver)
            return;

        EncounterRuntimeService.EnsureState(gameState);
        if (gameState.Encounters.WasOpportunityProcessed(result.RoadEncounterOpportunityId))
            return;

        EncounterDatabaseAsset database = LoadEncounterDatabase();
        if (database == null)
        {
            // База ещё не засеяна (Kingdom Survival → Seed → Тёплая овца) —
            // не ошибка выполнения, просто в проекте пока нет ни одного
            // Production Encounter. Помечаем Opportunity обработанной, чтобы
            // не проверять Resources.Load каждый вызов впустую.
            gameState.Encounters.MarkOpportunityProcessed(result.RoadEncounterOpportunityId);
            return;
        }

        CommanderData commander = gameState.GetSelectedCommander();
        if (commander == null)
            return;
        if (commander.HeroProfile == null)
            commander.HeroProfile = new HeroProfileData();

        EncounterOpportunity opportunity = new EncounterOpportunity
        {
            OpportunityId = result.RoadEncounterOpportunityId,
            PoolId = RoadEncounterIds.FirstRegionPoolId,
            WorldHour = result.RoadEncounterWorldHour,
            RegionId = result.RoadEncounterRegionId
        };

        EncounterSelectionResult selection = EncounterRuntimeService.SelectEncounter(
            gameState, commander.HeroProfile, opportunity, database);
        gameState.Encounters.MarkOpportunityProcessed(opportunity.OpportunityId);

        if (!selection.HasSelection)
            return;

        if (TryOpenNarrativeDialogueById(selection.SelectedEncounter.DialogueId))
            EncounterRuntimeService.RecordEncounterStarted(gameState, selection.SelectedEncounter, opportunity.WorldHour);
    }
}
