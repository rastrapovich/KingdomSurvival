using KingdomSurvival.Chapter01;

public partial class PrototypeUIController
{
    // P11/P12: отдельный узкий poller первого возвращения. Он намеренно не
    // расширяет общий dispatcher сюжетных сцен: N14-N16 нужны только этой
    // главе и опираются на уже существующие Dialogue Database, карту и время.
    // Вызывается из единственного PrototypeUIController.LateUpdate() после
    // Update(), чтобы увидеть физическое завершение возвращения в тот же кадр.
    private void RefreshChapter01ReturnFlow()
    {
        if (gameState == null || isGameOver || gameState.Narrative == null)
            return;

        NarrativeStateData state = gameState.Narrative;
        Chapter01ReturnFlow.EnsureAgreementKnowledge(state);

        // Техническое окно «ЭКСПЕДИЦИЯ ВЕРНУЛАСЬ» не должно подменять N16:
        // в первой главе игрок сначала видит последствия в Доме, а не отчёт
        // системы. Сам отчёт остаётся в Хронике/логах и для других походов
        // поведение ModalQueue не меняется.
        if (Chapter01ReturnFlow.IsHomecomingReady(gameState) &&
            activeQueuedModal != null &&
            activeQueuedModal.Title == "ЭКСПЕДИЦИЯ ВЕРНУЛАСЬ")
        {
            FinishActiveQueuedModal();
        }

        if (IsNarrativeDialogueActive || HasBlockingModalWorkExceptCamp())
            return;

        // P13: после закрытия N16 следующий кадр открывает обязательный
        // Совет через тот же poller и тот же Dialogue Database runtime.
        // Отдельный Update/LateUpdate и отдельная council-система не нужны.
        if (Chapter01StoryDirector.CanOpenFinalCouncil(state))
        {
            TryOpenNarrativeDialogueById(Chapter01Ids.Dialogues.D17);
            return;
        }

        // N13 -> N14: причинная сборка фактов происходит там же, у людей
        // ниже по течению. Это не телепорт и не новый маршрутный шаг.
        if (state.HasFlag(Chapter01Ids.Flags.DownstreamContact) &&
            !state.HasFlag(Chapter01Ids.Flags.AgreementRevealed))
        {
            TryOpenNarrativeDialogueById(Chapter01Ids.Dialogues.D14);
            return;
        }

        // После N14 игрок обязан явно решить цену дальнейшего поиска.
        if (state.HasFlag(Chapter01Ids.Flags.AgreementRevealed) &&
            !state.HasFlag(Chapter01Ids.Flags.ReturnStarted))
        {
            TryOpenNarrativeDialogueById(Chapter01Ids.Dialogues.D14Half);
            return;
        }

        if (!state.HasFlag(Chapter01Ids.Flags.ReturnStarted))
            return;

        // Выбор N14½ переводит существующую экспедицию в штатный физический
        // ReturningToCastle. Ветка «идти дальше» сначала тратит 4 часа на
        // свежий след; затем тот же маршрут продолжает работать автоматически.
        Chapter01ReturnFlow.EnsurePhysicalReturn(gameState, out _);

        if (Chapter01ReturnFlow.IsReturnRoadSceneReady(gameState))
        {
            if (Chapter01ReturnFlow.TryMarkRoadEchoReported(state))
            {
                Chapter01ReturnEcho echo = Chapter01ReturnFlow.ResolveEcho(state);
                AddReport("[ОБРАТНАЯ ДОРОГА]\n" + echo.RoadText);
            }

            TryOpenNarrativeDialogueById(Chapter01Ids.Dialogues.D15);
            return;
        }

        // Флаг D15 ставится самим диалогом при раскрытии. После закрытия
        // сцены добавляем реальную цену во времени ровно один раз.
        if (state.HasFlag(Chapter01Ids.Flags.ReturnRoadTraveled))
            Chapter01ReturnFlow.TryApplyReturnRoadConsequence(gameState);

        if (!Chapter01ReturnFlow.IsHomecomingReady(gameState))
            return;

        if (Chapter01ReturnFlow.TryMarkHomeEchoReported(state))
        {
            Chapter01ReturnEcho echo = Chapter01ReturnFlow.ResolveEcho(state);
            AddReport("[ВОЗВРАЩЕНИЕ В ДОМ]\n" + echo.HomeText);
        }

        TryOpenNarrativeDialogueById(Chapter01Ids.Dialogues.D16);
    }
}
