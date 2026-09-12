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
            // В проекте пока нет ни одного Production Encounter в базе —
            // не ошибка выполнения. Помечаем Opportunity обработанной, чтобы
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

        EncounterRuntimeService.RecordSelectionPacing(gameState, selection, opportunity);

        if (TryOpenNarrativeDialogueById(selection.SelectedEncounter.DialogueId))
            EncounterRuntimeService.RecordEncounterStarted(gameState, selection.SelectedEncounter, opportunity.WorldHour);
    }

    // Принудительный запуск для тестирования (P14-T01, "Принудительный запуск
    // для тестирования"): та же цепочка вызовов, что и реальный игровой путь
    // выше (SelectEncounter → RecordSelectionPacing → TryOpenNarrativeDialogueById →
    // RecordEncounterStarted) — единственное отличие от TryResolveRoadEncounterOpportunity
    // в том, что Opportunity здесь синтетическая (свежий GUID на каждую попытку,
    // не завязанная на "день"), чтобы можно было форсировать без ожидания
    // реального scheduled-check и без блокировки "эта Opportunity уже
    // обработана". Discovery Roll/Eligibility всё равно настоящие — при
    // низком шансе или отсутствии доступных Encounter метод честно вернёт
    // false после нескольких попыток, а не подменит логику отбора.
    private bool TryDebugForceRoadEncounter(out string message)
    {
        EncounterRuntimeService.EnsureState(gameState);

        EncounterDatabaseAsset database = LoadEncounterDatabase();
        if (database == null)
        {
            message = "база энкаунтеров не найдена (Resources/" + EncounterDatabaseAsset.ResourcesPath + ").";
            return false;
        }

        CommanderData commander = gameState.GetSelectedCommander();
        if (commander == null)
        {
            message = "нет выбранного командира.";
            return false;
        }
        if (commander.HeroProfile == null)
            commander.HeroProfile = new HeroProfileData();

        const int maxAttempts = 50;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            EncounterOpportunity opportunity = new EncounterOpportunity
            {
                OpportunityId = "debug_force_" + System.Guid.NewGuid().ToString("N"),
                PoolId = RoadEncounterIds.FirstRegionPoolId,
                WorldHour = gameState.Day * 24.0,
                RegionId = RoadEncounterIds.FirstRegionId
            };

            EncounterSelectionResult selection = EncounterRuntimeService.SelectEncounter(
                gameState, commander.HeroProfile, opportunity, database);

            if (!selection.HasSelection)
                continue;

            EncounterRuntimeService.RecordSelectionPacing(gameState, selection, opportunity);

            if (!TryOpenNarrativeDialogueById(selection.SelectedEncounter.DialogueId))
                continue;

            EncounterRuntimeService.RecordEncounterStarted(gameState, selection.SelectedEncounter, opportunity.WorldHour);
            message = "энкаунтер вызван вручную: " + selection.SelectedEncounter.DisplayName +
                       " (" + selection.SelectedEncounter.EncounterId + ").";
            return true;
        }

        message = "не удалось вызвать энкаунтер за " + maxAttempts +
                   " попыток — все доступные Encounter либо исчерпаны (MaxOccurrences), " +
                   "либо не прошли Discovery Roll.";
        return false;
    }
}
