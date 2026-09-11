using System;
using System.Collections.Generic;
using System.Linq;

// Один совершённый бросок/расчёт проверки. У пассивной проверки кубики
// отсутствуют (DieOne == DieTwo == 0). См. §5, §8 инструкции.
[Serializable]
public sealed class NarrativeCheckResult
{
    public string CheckId;
    public int AttemptNumber;
    public bool Success;
    public int DieOne;
    public int DieTwo;
    public int QualityValue;
    public int CompetencyValue;
    public int RawContextModifier;
    public int AppliedContextModifier;
    public int Total;
    public int Difficulty;

    // Проверка была разрешена принудительным исходом Preview, а не броском.
    // Игровой runtime такой результат никогда не создаёт.
    public bool IsForcedByPreview;

    // Снимок реально сработавших контекстных модификаторов в момент
    // расчёта (§8 инструкции по визуализации проверок). Список правил
    // (NarrativeContextModifierRule) сюда не попадает — только неизменяемая
    // копия SourceId/Label/Value, чтобы tooltip не пересчитывал математику
    // заново по изменившемуся состоянию мира.
    public List<NarrativeAppliedModifierSnapshot> AppliedModifiers = new List<NarrativeAppliedModifierSnapshot>();

    public bool HasDice => DieOne > 0 && DieTwo > 0;

    public NarrativeCheckResult()
    {
    }

    public NarrativeCheckResult(
        string checkId,
        int attemptNumber,
        bool success,
        int dieOne,
        int dieTwo,
        int qualityValue,
        int competencyValue,
        int rawContextModifier,
        int appliedContextModifier,
        int total,
        int difficulty,
        bool isForcedByPreview = false,
        IReadOnlyList<NarrativeAppliedModifierSnapshot> appliedModifiers = null)
    {
        CheckId = checkId ?? string.Empty;
        AttemptNumber = attemptNumber;
        Success = success;
        DieOne = dieOne;
        DieTwo = dieTwo;
        QualityValue = qualityValue;
        CompetencyValue = competencyValue;
        RawContextModifier = rawContextModifier;
        AppliedContextModifier = appliedContextModifier;
        Total = total;
        Difficulty = difficulty;
        IsForcedByPreview = isForcedByPreview;
        AppliedModifiers = appliedModifiers != null
            ? new List<NarrativeAppliedModifierSnapshot>(appliedModifiers)
            : new List<NarrativeAppliedModifierSnapshot>();
    }
}

// История попыток одной проверки. Возвратная проверка блокируется после
// провала (IsLocked); решающая — после первого разрешения, независимо от
// исхода. См. §6 инструкции.
[Serializable]
public sealed class NarrativeCheckHistoryEntry
{
    public string CheckId = string.Empty;
    public NarrativeCheckKind Kind;
    public bool IsLocked;
    public List<NarrativeCheckResult> Attempts = new List<NarrativeCheckResult>();

    public NarrativeCheckResult LatestResult =>
        Attempts != null && Attempts.Count > 0 ? Attempts[Attempts.Count - 1] : null;
}

[Serializable]
public sealed class NarrativeRelationEntry
{
    public string SubjectId = string.Empty;
    public int Value;
}

// Флаги, знания, отношения и история совершённых проверок — постоянное
// состояние мира диалогов/Encounter. Хранится в GameState, а не только
// в открытом окне диалога. См. §8, §18 инструкции.
[Serializable]
public sealed class NarrativeStateData
{
    public List<string> Flags = new List<string>();
    public List<string> Knowledge = new List<string>();
    public List<NarrativeRelationEntry> Relations = new List<NarrativeRelationEntry>();
    public List<NarrativeCheckHistoryEntry> CheckHistory = new List<NarrativeCheckHistoryEntry>();
    public List<string> AppliedEffectExecutionIds = new List<string>();

    // Минимальный реестр сюжетных предметов героя (§6.4 инструкции по
    // Главе 01): не инвентарная система, только присутствие/отсутствие
    // для условия ItemPresent. Расширять до полной инвентарной системы
    // только когда это докажет свою необходимость минимум в трёх сценах.
    public List<string> Items = new List<string>();

    public bool HasItem(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) && Items != null && Items.Contains(itemId);
    }

    public void GrantItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return;
        if (Items == null)
            Items = new List<string>();
        if (!Items.Contains(itemId))
            Items.Add(itemId);
    }

    public void RemoveItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId) || Items == null)
            return;
        Items.Remove(itemId);
    }

    public bool HasFlag(string flagId)
    {
        return !string.IsNullOrWhiteSpace(flagId) && Flags != null && Flags.Contains(flagId);
    }

    public void SetFlag(string flagId)
    {
        if (string.IsNullOrWhiteSpace(flagId))
            return;
        if (Flags == null)
            Flags = new List<string>();
        if (!Flags.Contains(flagId))
            Flags.Add(flagId);
    }

    public void ClearFlag(string flagId)
    {
        if (string.IsNullOrWhiteSpace(flagId) || Flags == null)
            return;
        Flags.Remove(flagId);
    }

    public bool HasKnowledge(string knowledgeId)
    {
        return !string.IsNullOrWhiteSpace(knowledgeId) && Knowledge != null && Knowledge.Contains(knowledgeId);
    }

    public void AddKnowledge(string knowledgeId)
    {
        if (string.IsNullOrWhiteSpace(knowledgeId))
            return;
        if (Knowledge == null)
            Knowledge = new List<string>();
        if (!Knowledge.Contains(knowledgeId))
            Knowledge.Add(knowledgeId);
    }

    public int GetRelation(string subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId) || Relations == null)
            return 0;

        for (int i = 0; i < Relations.Count; i++)
        {
            NarrativeRelationEntry entry = Relations[i];
            if (entry != null && string.Equals(entry.SubjectId, subjectId, StringComparison.Ordinal))
                return entry.Value;
        }

        return 0;
    }

    public void ChangeRelation(string subjectId, int delta)
    {
        if (string.IsNullOrWhiteSpace(subjectId) || delta == 0)
            return;

        if (Relations == null)
            Relations = new List<NarrativeRelationEntry>();

        for (int i = 0; i < Relations.Count; i++)
        {
            NarrativeRelationEntry entry = Relations[i];
            if (entry != null && string.Equals(entry.SubjectId, subjectId, StringComparison.Ordinal))
            {
                entry.Value += delta;
                return;
            }
        }

        Relations.Add(new NarrativeRelationEntry { SubjectId = subjectId, Value = delta });
    }

    public NarrativeCheckHistoryEntry FindHistory(string checkId)
    {
        if (string.IsNullOrWhiteSpace(checkId) || CheckHistory == null)
            return null;

        for (int i = 0; i < CheckHistory.Count; i++)
        {
            NarrativeCheckHistoryEntry entry = CheckHistory[i];
            if (entry != null && string.Equals(entry.CheckId, checkId, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }

    public bool IsCheckLocked(string checkId)
    {
        NarrativeCheckHistoryEntry entry = FindHistory(checkId);
        return entry != null && entry.IsLocked;
    }

    // true = успех, false = провал, null = проверка ещё не совершалась.
    public bool? GetLastOutcome(string checkId)
    {
        NarrativeCheckHistoryEntry entry = FindHistory(checkId);
        NarrativeCheckResult result = entry?.LatestResult;
        return result?.Success;
    }

    public NarrativeCheckHistoryEntry RecordCheckResult(NarrativeCheckKind kind, NarrativeCheckResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        if (string.IsNullOrWhiteSpace(result.CheckId))
            throw new ArgumentException("Check result must have a CheckId.", nameof(result));

        if (CheckHistory == null)
            CheckHistory = new List<NarrativeCheckHistoryEntry>();

        NarrativeCheckHistoryEntry entry = FindHistory(result.CheckId);
        if (entry == null)
        {
            entry = new NarrativeCheckHistoryEntry { CheckId = result.CheckId, Kind = kind };
            CheckHistory.Add(entry);
        }

        if (kind == NarrativeCheckKind.Passive)
        {
            // Пассивная проверка не является "попыткой": она пересчитывается
            // заново при каждом входе в узел и не блокируется.
            entry.Attempts.Clear();
            entry.Attempts.Add(result);
            return entry;
        }

        entry.Attempts.Add(result);
        if (kind == NarrativeCheckKind.ActiveReturnable && !result.Success)
            entry.IsLocked = true;
        else if (kind == NarrativeCheckKind.ActiveDecisive)
            entry.IsLocked = true;

        return entry;
    }

    // Разблокирует возвратную проверку. Решающую проверку разблокировать
    // нельзя — вызов молча игнорируется (§6: "решающая проверка не
    // повторяется").
    public void UnlockCheck(string checkId)
    {
        NarrativeCheckHistoryEntry entry = FindHistory(checkId);
        if (entry != null && entry.Kind == NarrativeCheckKind.ActiveReturnable)
            entry.IsLocked = false;
    }

    public bool HasEffectApplied(string effectExecutionId)
    {
        return !string.IsNullOrWhiteSpace(effectExecutionId) &&
               AppliedEffectExecutionIds != null &&
               AppliedEffectExecutionIds.Contains(effectExecutionId);
    }

    public void MarkEffectApplied(string effectExecutionId)
    {
        if (string.IsNullOrWhiteSpace(effectExecutionId))
            return;
        if (AppliedEffectExecutionIds == null)
            AppliedEffectExecutionIds = new List<string>();
        if (!AppliedEffectExecutionIds.Contains(effectExecutionId))
            AppliedEffectExecutionIds.Add(effectExecutionId);
    }
}

// Не сохраняется напрямую: связывает воедино профиль героя, постоянное
// состояние мира и сиюминутный контекст сцены (кто из спутников/предметов
// присутствует), необходимый для условий и контекстных модификаторов.
public sealed class NarrativeEvaluationContext
{
    public HeroProfileData Hero { get; }
    public NarrativeStateData State { get; }
    public IReadOnlyCollection<string> PresentCompanionIds { get; }
    public IReadOnlyCollection<string> PresentItemIds { get; }
    public int WorldSeed { get; }

    // Размер боевой экспедиции (герой + бойцы), 1..5. Отдельное понятие от
    // PresentCompanionIds: тот перечисляет конкретных спутников/бойцов,
    // а PartySize — их число как контекстное значение (P10-T04). Параметр
    // добавлен в конец конструктора с безопасным default, вычисленным из
    // PresentCompanionIds, чтобы не менять существующие вызовы.
    public int PartySize { get; }

    public NarrativeEvaluationContext(
        HeroProfileData hero,
        NarrativeStateData state,
        IReadOnlyCollection<string> presentCompanionIds = null,
        IReadOnlyCollection<string> presentItemIds = null,
        int worldSeed = 0,
        int? partySize = null)
    {
        Hero = hero ?? throw new ArgumentNullException(nameof(hero));
        State = state ?? throw new ArgumentNullException(nameof(state));
        PresentCompanionIds = presentCompanionIds ?? Array.Empty<string>();
        PresentItemIds = presentItemIds ?? Array.Empty<string>();
        WorldSeed = worldSeed;
        PartySize = partySize ?? (PresentCompanionIds.Count + 1);
    }

    public bool IsCompanionPresent(string companionId)
    {
        return !string.IsNullOrWhiteSpace(companionId) &&
               PresentCompanionIds != null &&
               PresentCompanionIds.Contains(companionId);
    }

    public bool IsItemPresent(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) &&
               PresentItemIds != null &&
               PresentItemIds.Contains(itemId);
    }
}
