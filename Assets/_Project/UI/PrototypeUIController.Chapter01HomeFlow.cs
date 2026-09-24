using KingdomSurvival.Chapter01;
using UnityEngine;

public partial class PrototypeUIController
{
    // ПР-02 (простой вариант): домашняя часть главы N01–N10 без Debug.
    // Как только экран свободен, через короткую паузу открывается следующая
    // сцена — сначала N01 в новой партии, затем по порядку до сбора отряда.
    // Какую сцену открыть, решает Chapter01StoryDirector.GetAutoOpenHomeDialogueId;
    // здесь только «когда»: не поверх другого окна и не мгновенно после
    // закрытия предыдущей сцены. Вызывается из единственного LateUpdate.
    private const float Chapter01HomeFlowDelaySeconds = 1f;

    private float chapter01HomeFlowReadyAt = -1f;

    private void RefreshChapter01HomeFlow()
    {
        if (gameState == null || isGameOver || HasBlockingModalWork())
        {
            chapter01HomeFlowReadyAt = -1f;
            return;
        }

        string dialogueId = Chapter01StoryDirector.GetAutoOpenHomeDialogueId(gameState);
        if (string.IsNullOrEmpty(dialogueId))
        {
            chapter01HomeFlowReadyAt = -1f;
            return;
        }

        if (chapter01HomeFlowReadyAt < 0f)
        {
            chapter01HomeFlowReadyAt = Time.unscaledTime + Chapter01HomeFlowDelaySeconds;
            return;
        }

        if (Time.unscaledTime < chapter01HomeFlowReadyAt)
            return;

        chapter01HomeFlowReadyAt = -1f;
        TryOpenNarrativeDialogueById(dialogueId);
    }
}
