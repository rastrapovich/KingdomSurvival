using System;
using System.Collections.Generic;

// Бриф исправлений 29.10.2026 (п. 6, 9): что фактически получено — опыт,
// золото, припасы, вещи — записывается здесь в момент выдачи (после
// успешного применения, а не по обещанию). Из этих же записей UI показывает
// зелёные уведомления «Опыт +…», «Золото +…», а завершённое поручение пишет
// в Хронику свою награду. Повторный показ сцены награду не повторяет: эффекты
// идемпотентны по своим ID, а сюда попадает только реально применённое.
[Serializable]
public sealed class RewardEntryData
{
    public string SceneId = string.Empty;
    public string Text = string.Empty;
    public int Day;
    public double Hour;
}

[Serializable]
public sealed class RewardLogData
{
    // Последние выдачи (журнал ограничен по длине).
    public List<RewardEntryData> Entries = new List<RewardEntryData>();
    // В какой сцене поставлен флаг выполнения поручения: её награда — его.
    public List<string> ErrandCompletionScenes = new List<string>();
}

public static class RewardFeed
{
    public const int MaxEntries = 200;

    // Ещё не показанные уведомления (не сохраняются: показ — дело UI).
    private static readonly List<string> Pending = new List<string>();

    public static RewardLogData Get(GameState state)
    {
        if (state.Rewards == null)
            state.Rewards = new RewardLogData();
        state.Rewards.Entries ??= new List<RewardEntryData>();
        state.Rewards.ErrandCompletionScenes ??= new List<string>();
        return state.Rewards;
    }

    public static void Add(GameState state, string text, string sceneId = null)
    {
        if (state == null || string.IsNullOrWhiteSpace(text))
            return;
        RewardLogData log = Get(state);
        log.Entries.Add(new RewardEntryData
        {
            SceneId = sceneId ?? string.Empty,
            Text = text,
            Day = state.Day,
            Hour = ContinuousSimulationSystem.GetClock(state).HourOfDay
        });
        if (log.Entries.Count > MaxEntries)
            log.Entries.RemoveRange(0, log.Entries.Count - MaxEntries);
        Pending.Add(text);
    }

    // Забрать накопленные уведомления для показа.
    public static List<string> TakePending()
    {
        List<string> result = new List<string>(Pending);
        Pending.Clear();
        return result;
    }

    public static void ClearPending()
    {
        Pending.Clear();
    }

    // Вызывается эффектом диалога после успешного применения.
    public static void OnEffectApplied(NarrativeEvaluationContext context, NarrativeEffect effect, int actualAmount)
    {
        GameState state = context?.GameState;
        if (state == null || effect == null)
            return;
        switch (effect.Type)
        {
            case NarrativeEffectType.GrantExperience:
                if (actualAmount > 0)
                    Add(state, "Опыт +" + actualAmount, context.SceneId);
                break;
            case NarrativeEffectType.ChangeArmyGold:
                if (actualAmount > 0)
                    Add(state, "Золото +" + actualAmount, context.SceneId);
                break;
            case NarrativeEffectType.ChangeSupplies:
                if (actualAmount > 0)
                    Add(state, "Припасы +" + actualAmount, context.SceneId);
                break;
            case NarrativeEffectType.GrantItem:
                ItemDefinition item = ItemCatalog.Find(effect.StringParam);
                if (item != null && !item.IsStory)
                    Add(state, item.Name + " ×1", context.SceneId);
                break;
            case NarrativeEffectType.SetFlag:
                MarkErrandCompletionScene(state, effect.StringParam, context.SceneId);
                break;
        }
    }

    private static void MarkErrandCompletionScene(GameState state, string flag, string sceneId)
    {
        if (string.IsNullOrEmpty(sceneId) || string.IsNullOrEmpty(flag))
            return;
        foreach (ErrandDefinition errand in SettlementCatalog.Current.Errands)
        {
            if (errand.CompletedFlag != flag)
                continue;
            string key = errand.Id + "|" + sceneId;
            List<string> scenes = Get(state).ErrandCompletionScenes;
            scenes.RemoveAll(s => s.StartsWith(errand.Id + "|", StringComparison.Ordinal));
            scenes.Add(key);
        }
    }

    // Что получено в сцене, где поручение выполнено, — строка для Хроники.
    public static string DescribeErrandReward(GameState state, string errandId)
    {
        if (state?.Rewards == null)
            return string.Empty;
        string prefix = errandId + "|";
        string entry = state.Rewards.ErrandCompletionScenes.Find(s => s.StartsWith(prefix, StringComparison.Ordinal));
        if (entry == null)
            return string.Empty;
        string sceneId = entry.Substring(prefix.Length);
        List<string> parts = new List<string>();
        foreach (RewardEntryData reward in state.Rewards.Entries)
        {
            if (reward.SceneId == sceneId)
                parts.Add(reward.Text);
        }
        return parts.Count > 0 ? "Получено: " + string.Join(", ", parts) + "." : string.Empty;
    }
}
