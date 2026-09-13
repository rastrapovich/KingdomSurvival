using System;

// AM-05 (канон v1.33, §9.9 / раздел 15 инструкции по миграции): контейнер
// сохранения кампании. Чистые данные без UnityEngine — фактическая запись в
// файл (JsonUtility, File I/O) остаётся в Unity-слое (UI), эта структура и
// CampaignSaveService лишь описывают, что и как переносится.
[Serializable]
public sealed class CampaignSaveData
{
    public int SaveFormatVersion = CampaignSaveService.CurrentSaveFormatVersion;

    // Пусто, если партия ещё не переведена на авторский мир (AM-01/AM-04)
    // — тогда география процедурная и WorldSeed остаётся единственным
    // источником. Заполнено — Load обязан проверить совпадение мира/версии
    // перед тем, как считать save совместимым (раздел 15 инструкции).
    public string WorldDefinitionId = string.Empty;
    public int GeographyVersion;

    public GameState State;

    // JsonUtility не умеет сохранять null для полей-ссылок (проверено
    // эмпирически: при сериализации на месте null оказывается фиктивный
    // default-объект) — эти флаги фиксируют настоящую null-ность отдельно,
    // CampaignSaveService.RestoreCampaign восстанавливает её явно.
    public bool HasActiveExpedition;
    public bool HasActiveActivity;
    public bool HasPendingDecision;

    public ContinuousSimulationSnapshotData ClockSnapshot;
    public BuildingSystemSnapshotData BuildingSnapshot;
}
