using System;
using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
    public enum Chapter01RepairChoice
    {
        None,
        Old,
        New
    }

    // Узкий контроллер позвоночника N01-N17 (раздел 6.1/6.2 инструкции).
    // Не универсальный QuestManager: читает NarrativeState, не хранит вторую
    // копию прогресса, не перемещает отряд по карте — только определяет,
    // какой диалог открыть следующим.
    //
    // Текущая последовательность — черновой линейный каркас P03 ("Готово
    // когда" раздела 8: граф проходим от N01 до N17 в обеих ветках ремонта
    // без тупика). Свободный порядок N07A/B/C и осмысленные комбинации
    // знаний для перехода к N09 (раздел 14) — отдельные задачи P07-T05 и
    // P08-T02, которые заменят линейную часть этой последовательности, не
    // меняя публичный контракт GetNextDialogueId/TryAdvance.
    public static class Chapter01StoryDirector
    {
        private readonly struct NodeStep
        {
            public readonly string NodeId;
            public readonly string DialogueId;
            public readonly string[] CompletionFlags;

            // P08-T02: узкий необязательный "готов ли шаг к открытию" сверх
            // завершённости предыдущих узлов — нужен только N09 (см.
            // CanOpenDepartureCouncil). Null для всех остальных шагов.
            public readonly Func<NarrativeStateData, bool> ReadyGate;

            public NodeStep(
                string nodeId,
                string dialogueId,
                Func<NarrativeStateData, bool> readyGate,
                params string[] completionFlags)
            {
                NodeId = nodeId;
                DialogueId = dialogueId;
                ReadyGate = readyGate;
                CompletionFlags = completionFlags ?? Array.Empty<string>();
            }

            public NodeStep(string nodeId, string dialogueId, params string[] completionFlags)
                : this(nodeId, dialogueId, null, completionFlags)
            {
            }
        }

        private static readonly NodeStep[] Sequence =
        {
            new NodeStep(Chapter01Ids.Nodes.N01, Chapter01Ids.Dialogues.D01, Chapter01Ids.Flags.HomeIntroSeen),
            new NodeStep(Chapter01Ids.Nodes.N02, Chapter01Ids.Dialogues.D02, Chapter01Ids.Flags.HousePeopleMet),
            new NodeStep(Chapter01Ids.Nodes.N03, Chapter01Ids.Dialogues.D03, Chapter01Ids.Flags.FirstPressureSeen),
            new NodeStep(Chapter01Ids.Nodes.N04, Chapter01Ids.Dialogues.D04, Chapter01Ids.Flags.FloodHappened),
            new NodeStep(Chapter01Ids.Nodes.N05, Chapter01Ids.Dialogues.D05, Chapter01Ids.Flags.RepairOld, Chapter01Ids.Flags.RepairNew),
            new NodeStep(Chapter01Ids.Nodes.N06, Chapter01Ids.Dialogues.D06, Chapter01Ids.Flags.WaterWrongActive),
            new NodeStep(Chapter01Ids.Nodes.N07A, Chapter01Ids.Dialogues.D07A, Chapter01Ids.Flags.InvestigatedMill),
            new NodeStep(Chapter01Ids.Nodes.N07B, Chapter01Ids.Dialogues.D07B, Chapter01Ids.Flags.InvestigatedCattle),
            new NodeStep(Chapter01Ids.Nodes.N07C, Chapter01Ids.Dialogues.D07C, Chapter01Ids.Flags.InvestigatedRiver),
            new NodeStep(Chapter01Ids.Nodes.N08, Chapter01Ids.Dialogues.D08, Chapter01Ids.Flags.OldTraceFound),
            new NodeStep(Chapter01Ids.Nodes.N09, Chapter01Ids.Dialogues.D09, CanOpenDepartureCouncil, Chapter01Ids.Flags.FarRouteUnlocked),
            new NodeStep(Chapter01Ids.Nodes.N10, Chapter01Ids.Dialogues.D10, Chapter01Ids.Flags.ExpeditionStarted),
            new NodeStep(Chapter01Ids.Nodes.N11, Chapter01Ids.Dialogues.D11, Chapter01Ids.Flags.LongRoadStarted),
            new NodeStep(Chapter01Ids.Nodes.N12, Chapter01Ids.Dialogues.D12, Chapter01Ids.Flags.OldFordFound),
            new NodeStep(Chapter01Ids.Nodes.N13, Chapter01Ids.Dialogues.D13, Chapter01Ids.Flags.DownstreamContact),
            new NodeStep(Chapter01Ids.Nodes.N14, Chapter01Ids.Dialogues.D14, Chapter01Ids.Flags.AgreementRevealed),
            new NodeStep(Chapter01Ids.Nodes.N14Half, Chapter01Ids.Dialogues.D14Half, Chapter01Ids.Flags.ReturnStarted),
            new NodeStep(Chapter01Ids.Nodes.N15, Chapter01Ids.Dialogues.D15, Chapter01Ids.Flags.ReturnRoadTraveled),
            new NodeStep(Chapter01Ids.Nodes.N16, Chapter01Ids.Dialogues.D16, Chapter01Ids.Flags.ReturnedHome),
            new NodeStep(Chapter01Ids.Nodes.N17, Chapter01Ids.Dialogues.D17, Chapter01Ids.Flags.CouncilCompleted)
        };

        // Первый незавершённый узел последовательности, либо null, если
        // глава завершена или state ещё не создан. Идемпотентно: повторный
        // вызов без изменения флагов между вызовами возвращает тот же узел,
        // а не открывает его заново с нуля — решение "не запускать заново
        // завершённый узел" целиком определяется тем, что флаг завершения
        // уже выставлен и узел пропускается.
        public static string GetNextDialogueId(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (state.HasFlag(Chapter01Ids.Flags.Completed))
                return null;

            for (int i = 0; i < Sequence.Length; i++)
            {
                NodeStep step = Sequence[i];
                if (IsStepCompleted(state, step))
                    continue;
                if (step.ReadyGate != null && !step.ReadyGate(state))
                    return null;
                return step.DialogueId;
            }

            return null;
        }

        public static string GetNextNodeId(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (state.HasFlag(Chapter01Ids.Flags.Completed))
                return null;

            for (int i = 0; i < Sequence.Length; i++)
            {
                NodeStep step = Sequence[i];
                if (IsStepCompleted(state, step))
                    continue;
                if (step.ReadyGate != null && !step.ReadyGate(state))
                    return null;
                return step.NodeId;
            }

            return null;
        }

        public static bool IsChapterComplete(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            return state.HasFlag(Chapter01Ids.Flags.Completed);
        }

        public static Chapter01RepairChoice GetRepairChoice(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            bool old = state.HasFlag(Chapter01Ids.Flags.RepairOld);
            bool @new = state.HasFlag(Chapter01Ids.Flags.RepairNew);

            if (old && @new)
                throw new InvalidOperationException("repair_old и repair_new не могут быть установлены одновременно.");

            if (old)
                return Chapter01RepairChoice.Old;
            if (@new)
                return Chapter01RepairChoice.New;
            return Chapter01RepairChoice.None;
        }

        // P07-T05: свободный порядок N07A/N07B/N07C без счётчика улик —
        // возвращает конкретные ещё не пройденные расследования, а не
        // число. Пустой список означает либо "фаза расследования ещё не
        // началась" (WaterWrongActive == false), либо "все три уже
        // завершены" (пора в N08) — вызывающая сторона различает эти
        // случаи через сами InvestigatedMill/Cattle/River, если нужно.
        // GetNextDialogueId ниже продолжает возвращать один линейный ID
        // (первый ещё не пройденный, в фиксированном порядке A/B/C) —
        // этого достаточно для TryAdvance и не меняет его публичный
        // контракт; реальный свободный выбор получает список отсюда.
        public static IReadOnlyList<string> GetAvailableInvestigationDialogueIds(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            List<string> available = new List<string>();
            if (!state.HasFlag(Chapter01Ids.Flags.WaterWrongActive))
                return available;

            if (!state.HasFlag(Chapter01Ids.Flags.InvestigatedMill))
                available.Add(Chapter01Ids.Dialogues.D07A);
            if (!state.HasFlag(Chapter01Ids.Flags.InvestigatedCattle))
                available.Add(Chapter01Ids.Dialogues.D07B);
            if (!state.HasFlag(Chapter01Ids.Flags.InvestigatedRiver))
                available.Add(Chapter01Ids.Dialogues.D07C);

            return available;
        }

        // P08-T02: N09 открывается не по факту прохождения N08 самого по
        // себе, а по осмысленному сочетанию конкретных знаний — раздел
        // "P08-T02" инструкции ("имеет значение сочетание конкретных
        // фактов, а не количество очков расследования"). Три допустимых
        // комбинации привязаны к реально реализованным в P07 знаниям (в
        // репозитории от исходного раздела 14 остался только словесный
        // принцип "варианты A/B/C", буквальный текст утерян):
        //   A = память Мирона о седьмом рукаве + подтверждённый старый обычай (N08);
        //   B = два независимых природных свидетеля — скот и рыба;
        //   C = память о седьмом рукаве + понятый материальный предмет (N08).
        // OldTraceFound (сама находка N08) обязателен во всех случаях.
        public static bool CanOpenDepartureCouncil(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (!state.HasFlag(Chapter01Ids.Flags.OldTraceFound))
                return false;

            bool combinationA =
                state.HasKnowledge(Chapter01Ids.Knowledge.OldSeventhChannel) &&
                state.HasKnowledge(Chapter01Ids.Knowledge.OldCustom);
            bool combinationB =
                state.HasKnowledge(Chapter01Ids.Knowledge.CattleAvoidOldBranch) &&
                state.HasKnowledge(Chapter01Ids.Knowledge.FishPatternChanged);
            bool combinationC =
                state.HasKnowledge(Chapter01Ids.Knowledge.OldSeventhChannel) &&
                state.HasKnowledge(Chapter01Ids.Knowledge.SevenToothObject);

            return combinationA || combinationB || combinationC;
        }

        // P08-T01: N09 открывает ровно одну обязательную дальнюю цель и до
        // двух дополнительных — каждая появляется, только если игрок
        // действительно получил соответствующие сведения раньше (не
        // отдельный счётчик, а прямая проверка тех же знаний P07). Список
        // читает сам диалог D09 через свои Conditions; этот метод — только
        // для внешних потребителей (тесты, будущий UI похода), не для
        // ветвления самого диалога.
        public static IReadOnlyList<string> GetDepartureOptionalGoals(NarrativeStateData state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            List<string> goals = new List<string>();

            if (state.HasKnowledge(Chapter01Ids.Knowledge.SecondLoafIsRation) &&
                state.HasKnowledge(Chapter01Ids.Knowledge.OldCustom))
            {
                goals.Add(Chapter01Ids.Knowledge.SecondLoafIsRation);
            }

            if (state.HasKnowledge(Chapter01Ids.Knowledge.SevenToothObject))
                goals.Add(Chapter01Ids.Knowledge.SevenToothObject);

            return goals;
        }

        // P08-T03: узкая точка завершения сбора отряда — вызывается только
        // ПОСЛЕ того, как реальная экспедиция уже создана
        // (GameState.TryStartExpedition/TryStartExpeditionToMapPoint
        // вернули успех), никогда из текста диалога N10 самого по себе.
        // ExpeditionStarted должен означать совершившееся действие, а не
        // просмотр сцены сбора.
        public static void HandleStoryExpeditionStarted(GameState gameState)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));

            if (gameState.Narrative == null)
                gameState.Narrative = new NarrativeStateData();

            gameState.Narrative.SetFlag(Chapter01Ids.Flags.ExpeditionStarted);
        }

        // Тонкая точка интеграции с UI (раздел 6.2: "запускает диалог через
        // существующий TryOpenNarrativeDialogueById"), без прямой ссылки на
        // PrototypeUIController — вызывающий код передаёт открыватель
        // диалога делегатом. Возвращает false, если открывать нечего или
        // открыватель отказал (например, уже идёт другой диалог).
        public static bool TryAdvance(GameState gameState, Func<string, bool> openDialogueById)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));
            if (openDialogueById == null)
                throw new ArgumentNullException(nameof(openDialogueById));

            if (gameState.Narrative == null)
                gameState.Narrative = new NarrativeStateData();

            string nextDialogueId = GetNextDialogueId(gameState.Narrative);
            if (string.IsNullOrEmpty(nextDialogueId))
                return false;

            return openDialogueById(nextDialogueId);
        }

        // P05-T03 (раздел 17 инструкции): узкая точка завершения сцены,
        // отдельная от TryAdvance/GetNextDialogueId. Читает завершившийся
        // dialogueId и применяет внешние системные последствия конкретной
        // главы, не расширяя универсальный NarrativeEffectType новыми
        // типами ради одной сцены. UI вызывает это перед закрытием
        // завершившегося диалога (PrototypeUIController.Narrative.cs).
        public static void HandleDialogueCompleted(GameState gameState, string dialogueId)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));

            if (string.Equals(dialogueId, Chapter01Ids.Dialogues.D04, StringComparison.Ordinal))
                Chapter01OutcomeApplier.ApplyFloodConsequences(gameState);
            else if (string.Equals(dialogueId, Chapter01Ids.Dialogues.D08, StringComparison.Ordinal))
                Chapter01OutcomeApplier.ApplySevenTeethInvestigationConsequences(gameState);
            else if (string.Equals(dialogueId, Chapter01Ids.Dialogues.D09, StringComparison.Ordinal))
                Chapter01OutcomeApplier.ApplyDepartureConsequences(gameState);
            else if (string.Equals(dialogueId, Chapter01Ids.Dialogues.D11, StringComparison.Ordinal))
                Chapter01OutcomeApplier.ApplyLongRoadRouteConsequences(gameState);
            else if (string.Equals(dialogueId, Chapter01Ids.Dialogues.D11B, StringComparison.Ordinal))
                Chapter01OutcomeApplier.ApplyCartConsequences(gameState);
            else if (string.Equals(dialogueId, Chapter01Ids.Dialogues.D12, StringComparison.Ordinal))
                Chapter01OutcomeApplier.ApplyDownstreamLocationReveal(gameState);
        }

        // P09-T02/T03: доля пройденных клеток текущего маршрута, [0..1].
        // Единственная величина, по которой триггерятся дорожные встречи —
        // не время суток и не случайный бросок (раздел "Обязательная
        // дорожная встреча" инструкции: "не случайный RNG, гарантированное
        // обучение"). RouteLengthCells == 0 (нет активного маршрута или
        // герой уже стоит) считается 0, а не делением на ноль.
        private static double GetRouteProgress(ExpeditionData expedition)
        {
            if (expedition == null || expedition.RouteLengthCells <= 0)
                return 0.0;

            int traveled = expedition.RouteLengthCells - expedition.RemainingRouteCells;
            double progress = traveled / (double)expedition.RouteLengthCells;
            if (progress < 0.0) return 0.0;
            if (progress > 1.0) return 1.0;
            return progress;
        }

        // Рабочие числа раздела "Что требует решения" производственной
        // инструкции ("N11 ≈ 30% пути", «Трое под телегой» ≈ 60%) — намеренно
        // не канонизированы, просто константы места вызова.
        private const double RoadEventProgressThreshold = 0.30;
        private const double CartEventProgressThreshold = 0.60;

        // P09-T02/T03: единственное намеренное исключение из общего правила
        // "сюжетный диалог открывается только по клику игрока/дебагу" —
        // обязательная и необязательная дорожные встречи первого похода
        // триггерятся физическим прогрессом ТЕКУЩЕГО маршрута во время
        // движения, не добавляясь в линейный Sequence (у них нет своего
        // NodeStep/ReadyGate). Вызывающая сторона (PrototypeUIController.
        // RefreshAutoTimeState) обязана вызывать это КАЖДЫЙ кадр до расчёта
        // паузы и, получив непустой ID, открыть диалог через
        // TryOpenNarrativeDialogueById — тот уже сам ставит
        // PauseForBlockingModal() и не даст открыть диалог повторно, пока
        // предыдущий не закрыт (идемпотентно по построению, не по кэшу
        // здесь). Гейты по LongRoadStarted/CartResolved делают срабатывание
        // одноразовым без отдельного технического флага "уже показывали".
        public static string GetPendingRoadEventDialogueId(GameState gameState)
        {
            if (gameState == null || !gameState.HasActiveExpedition)
                return null;

            NarrativeStateData state = gameState.Narrative;
            if (state == null || !state.HasFlag(Chapter01Ids.Flags.ExpeditionStarted))
                return null;

            ExpeditionData expedition = gameState.ActiveExpedition;
            if (expedition.Phase != CommanderState.TravellingToLocation)
                return null;

            double progress = GetRouteProgress(expedition);

            if (!state.HasFlag(Chapter01Ids.Flags.LongRoadStarted) && progress >= RoadEventProgressThreshold)
                return Chapter01Ids.Dialogues.D11;

            if (state.HasFlag(Chapter01Ids.Flags.LongRoadStarted) &&
                !state.HasFlag(Chapter01Ids.Flags.CartResolved) &&
                progress >= CartEventProgressThreshold)
            {
                return Chapter01Ids.Dialogues.D11B;
            }

            return null;
        }

        // P10-LocInt: узкий story-gate для автоматического открытия N12 после
        // завершения сюжетного Location Research в OldWaterSearch (раздел
        // "Связка с N12" инструкции про Location Interaction). Завершение
        // исследования ("мы осмотрели область") и OldFordFound ("мы поняли
        // конкретный старый брод") — разные состояния: последний по-прежнему
        // выставляется только внутри самого N12. Вызывающая сторона
        // (PrototypeUIController.RefreshAutoTimeState) обязана опрашивать
        // это каждый кадр наравне с GetPendingRoadEventDialogueId и открывать
        // через TryOpenNarrativeDialogueById — идемпотентно за счёт условия
        // "!OldFordFound", без отдельного технического флага.
        public static string GetPendingLocationNarrativeDialogueId(GameState gameState)
        {
            if (gameState == null || gameState.Narrative == null || !gameState.HasActiveExpedition)
                return null;

            NarrativeStateData state = gameState.Narrative;
            ExpeditionData expedition = gameState.ActiveExpedition;

            if (expedition.Phase != CommanderState.AtLocation ||
                !string.Equals(expedition.LocationId, Chapter01Ids.Locations.OldWaterSearch, StringComparison.Ordinal))
            {
                return null;
            }

            if (!state.HasFlag(Chapter01Ids.Flags.RoadDestinationReached) ||
                state.HasFlag(Chapter01Ids.Flags.OldFordFound))
            {
                return null;
            }

            LocationData location = gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch);
            if (location == null || !location.IsExplored)
                return null;

            return Chapter01Ids.Dialogues.D12;
        }

        // P10-T05: сюжетное действие при осознанном входе игрока в уже
        // достигнутую локацию. В отличие от GetPendingLocationNarrativeDialogueId
        // этот gate не опрашивается для автозапуска: UI Location Interaction
        // использует возвращённый ID только после нажатия основной кнопки.
        public static string GetLocationEntryDialogueId(GameState gameState, string locationId)
        {
            if (gameState == null ||
                gameState.Narrative == null ||
                !gameState.HasActiveExpedition ||
                string.IsNullOrEmpty(locationId))
            {
                return null;
            }

            if (!string.Equals(
                    locationId,
                    Chapter01Ids.Locations.DownstreamSettlement,
                    StringComparison.Ordinal))
            {
                return null;
            }

            ExpeditionData expedition = gameState.ActiveExpedition;
            if (expedition.Phase != CommanderState.AtLocation ||
                !string.Equals(expedition.LocationId, locationId, StringComparison.Ordinal))
            {
                return null;
            }

            NarrativeStateData state = gameState.Narrative;
            if (!state.HasFlag(Chapter01Ids.Flags.OldFordFound) ||
                state.HasFlag(Chapter01Ids.Flags.DownstreamContact))
            {
                return null;
            }

            return gameState.FindLocation(locationId) != null
                ? Chapter01Ids.Dialogues.D13
                : null;
        }

        // P09-T01: продолжение маршрута после временной точки крюка
        // "Пойти старым путём" (раздел "Решение у старой дороги" —
        // "отряд физически идёт к ней, затем продолжает к прежней цели").
        // OldRoadDetourInProgress ставит Chapter01OutcomeApplier.
        // ApplyLongRoadRouteConsequences в момент выбора; здесь только читаем
        // факт прибытия к этой точке (Phase стал AtLocation) и перенаправляем
        // экспедицию к настоящей цели тем же публичным API, каким игрок сам
        // меняет маршрут кликом по карте — это не отдельный маршрутный
        // движок, а обычный TryChangeExpeditionRoute. Возвращает true, если
        // действительно перенаправила (вызывающая сторона может просто
        // продолжить пересчёт паузы тем же кадром — маршрут уже валиден).
        public static bool TryContinueOldRoadDetourIfArrived(GameState gameState)
        {
            if (gameState == null || gameState.Narrative == null || !gameState.HasActiveExpedition)
                return false;

            if (!gameState.Narrative.HasFlag(Chapter01Ids.Flags.OldRoadDetourInProgress))
                return false;

            if (gameState.ActiveExpedition.Phase != CommanderState.AtLocation)
                return false;

            LocationData target = gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch);
            if (target == null)
                return false;

            gameState.Narrative.ClearFlag(Chapter01Ids.Flags.OldRoadDetourInProgress);
            gameState.TryChangeExpeditionRoute(target.MapXPercent, target.MapYPercent, target.Id, out _);
            return true;
        }

        // P09: физическое прибытие в область поиска (не открытие точки на
        // карте в P08 — раскрытие места и приход туда разные события).
        // Ставит только технический флаг; Журнал сам решает, как показать
        // переход :travel -> :search_area (Chapter01JournalProvider), P10 —
        // отдельная задача, OldFordFound здесь не выставляется (раздел
        // "Достижение OldWaterSearch" инструкции: "брод ещё надо реально
        // обнаружить").
        private static void RefreshRoadArrivalState(GameState gameState)
        {
            if (gameState?.Narrative == null || !gameState.HasActiveExpedition)
                return;

            ExpeditionData expedition = gameState.ActiveExpedition;
            if (expedition.Phase == CommanderState.AtLocation &&
                string.Equals(expedition.LocationId, Chapter01Ids.Locations.OldWaterSearch, StringComparison.Ordinal) &&
                !gameState.Narrative.HasFlag(Chapter01Ids.Flags.RoadDestinationReached))
            {
                gameState.Narrative.SetFlag(Chapter01Ids.Flags.RoadDestinationReached);
            }
        }

        // Единая точка входа для PrototypeUIController.RefreshAutoTimeState:
        // и продолжение крюка, и фиксацию прибытия нужно проверять каждый
        // кадр наравне с GetPendingRoadEventDialogueId, чтобы не размазывать
        // три отдельных вызова по UI-коду.
        public static void RefreshRoadState(GameState gameState)
        {
            TryContinueOldRoadDetourIfArrived(gameState);
            RefreshRoadArrivalState(gameState);
        }

        private static bool IsStepCompleted(NarrativeStateData state, NodeStep step)
        {
            for (int i = 0; i < step.CompletionFlags.Length; i++)
            {
                if (state.HasFlag(step.CompletionFlags[i]))
                    return true;
            }
            return false;
        }
    }
}
