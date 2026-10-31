using System;
using System.Collections.Generic;

// ПР-12А: общие модели «Дел» и вход к содержанию свободной игры. Ядро не
// знает конкретных историй: модуль свободной игры регистрирует свой
// источник (FreePlayContent.Register), UI и ядро читают только этот вход.
// Канон v1.59: режим один — свободная игра; выбора режима по ID нет.

public enum JournalGoalCategory
{
    Main,
    Optional
}

public enum JournalGoalState
{
    Hidden,
    Active,
    Completed,
    Failed
}

public sealed class JournalGoalViewData
{
    public string Id;
    public string Title;
    public string Description;
    public string CurrentStep;
    public string RevisionId;
    public JournalGoalCategory Category;
    public JournalGoalState State;
}

public sealed class CampaignContentProvider
{
    public Func<GameState, IReadOnlyList<JournalGoalViewData>> Goals;
    // Однократная настройка только что созданной партии.
    public Action<GameState> OnNewCampaign;

    // ПР-12Б: авторские истории. Узкие точки входа, не система квестов:
    // сцена при входе в место, конец разговора, бой после сцены, итог
    // такого боя и опрос каждого кадра с донесениями.
    public Func<GameState, string, LocationEntryView> LocationEntry;
    public Action<GameState, string> DialogueCompleted;
    public Func<GameState, string, CampaignBattleRequest> BattleAfterDialogue;
    public Action<GameState, CampaignBattleResult, List<string>> BattleApplied;
    public Func<GameState, List<string>> Refresh;
}

// Сцена при входе в достигнутое место: какой диалог и как подписана кнопка.
public sealed class LocationEntryView
{
    public string DialogueId;
    public string ButtonText;
    public string Hint;
}

public static class CampaignContent
{
    private static CampaignContentProvider provider;

    public static void Register(CampaignContentProvider contentProvider)
    {
        if (contentProvider != null)
            provider = contentProvider;
    }

    public static bool IsRegistered => provider != null;

    public static IReadOnlyList<JournalGoalViewData> BuildGoals(GameState state)
    {
        return state != null && provider?.Goals != null ? provider.Goals(state) : new List<JournalGoalViewData>();
    }

    public static void InitializeNewCampaign(GameState state)
    {
        if (state != null)
            provider?.OnNewCampaign?.Invoke(state);
    }

    public static LocationEntryView LocationEntry(GameState state, string locationId)
    {
        return state != null && provider?.LocationEntry != null ? provider.LocationEntry(state, locationId) : null;
    }

    public static void OnDialogueCompleted(GameState state, string dialogueId)
    {
        if (state != null)
            provider?.DialogueCompleted?.Invoke(state, dialogueId);
    }

    public static CampaignBattleRequest BattleAfterDialogue(GameState state, string dialogueId)
    {
        return state != null && provider?.BattleAfterDialogue != null ? provider.BattleAfterDialogue(state, dialogueId) : null;
    }

    public static void OnBattleApplied(GameState state, CampaignBattleResult result, List<string> reports)
    {
        if (state != null)
            provider?.BattleApplied?.Invoke(state, result, reports);
    }

    // Опрос содержания; возвращает донесения для игрока.
    public static List<string> Refresh(GameState state)
    {
        return state != null && provider?.Refresh != null ? provider.Refresh(state) ?? new List<string>() : new List<string>();
    }
}
