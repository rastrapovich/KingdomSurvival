using System;
using System.Collections.Generic;

// AM-07 (канон v1.33, §9.9, раздел 12 инструкции по миграции): авторские
// сужающиеся стадии слуха о конкретном месте. Не вводит отдельную систему
// знаний — привязана к уже существующим флагам NarrativeStateData.Flags,
// которые и так сохраняются (CampaignSaveService) и идемпотентно
// проверяются во всём остальном нарративе.
[Serializable]
public sealed class WorldMapSearchStageDefinition
{
    public string StageId = string.Empty;

    // Все перечисленные флаги должны быть известны, чтобы стадия считалась
    // достигнутой. Пустой список — стадия доступна всегда (например,
    // "общеизвестная область", раздел 12 инструкции: "крупная дорога может
    // быть общеизвестной").
    public List<string> RequiredKnowledgeFlags = new List<string>();

    public float MinXPercent;
    public float MaxXPercent;
    public float MinYPercent;
    public float MaxYPercent;

    // Нейтральная подпись области ("где-то в холмах"), а не имя цели —
    // раскрывающий текст относится к самой локации, не к поиску.
    public string NeutralLabel = string.Empty;

    public bool Contains(float xPercent, float yPercent) =>
        xPercent >= MinXPercent && xPercent <= MaxXPercent &&
        yPercent >= MinYPercent && yPercent <= MaxYPercent;

    public double Area =>
        Math.Max(0.0, MaxXPercent - MinXPercent) * Math.Max(0.0, MaxYPercent - MinYPercent);
}

[Serializable]
public sealed class WorldMapSearchAreaDefinition
{
    public string SearchId = string.Empty;
    public string TargetLocationId = string.Empty;

    // Порядок важен: стадии перечисляются от самой широкой к самой узкой.
    // GetActiveStage побеждает последней достигнутой в порядке списка.
    public List<WorldMapSearchStageDefinition> Stages =
        new List<WorldMapSearchStageDefinition>();
}
