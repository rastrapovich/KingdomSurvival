using System.Collections.Generic;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.SettlementDatabase;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// 12Т (SETTLEMENTS_TRADE_TAVERN_SPEC.md §7): экран поселения. Показывается,
// пока командир внутри поселения (SettlementCampService — единственный
// источник присутствия, переживает сохранение): большая иллюстрация,
// короткая левая панель с действиями, полоса людей, строка отряда и выход.
// Корчма — тот же экран в режиме корчмы; торговля и доска — дочерние
// страницы (Settlement.Trade.cs, Settlement.Board.cs). Разговор идёт
// существующим диалогом: экран прячется и возвращается после обычного
// конца сцены, если не начался бой и ничего обязательного не встало сверху.
// Просмотр и выбор времени не двигают (блокирующий модальный слой);
// ночлег — общим временем, экран на это время уходит.
public partial class PrototypeUIController
{
    private enum SettlementMode
    {
        Main,
        Tavern,
        // Бриф исправлений п. 14: все, с кем сейчас можно говорить здесь.
        People
    }

    private VisualElement settlementScreen;
    private VisualElement settlementArt;
    private Image settlementImage;
    private Label settlementTitle;
    private Label settlementSubtitle;
    private Label settlementDescription;
    private Button settlementMoreButton;
    private ScrollView settlementActions;
    private Label settlementMessage;
    private VisualElement settlementPeople;
    private Label settlementPeopleTitle;
    private Label settlementParty;
    private Button settlementExitButton;
    private VisualElement settlementConfirm;
    private Label settlementConfirmText;
    private Button settlementConfirmYes;
    private Button settlementConfirmNo;
    private bool settlementUiBound;

    private string settlementLocationId;
    private SettlementMode settlementMode;
    private bool settlementLongText;
    private string settlementMessageText = string.Empty;
    private System.Action settlementConfirmAction;
    // Экран ушёл на время разговора: вернуть его в том же режиме.
    private string settlementSuspendedLocationId;
    private SettlementMode settlementSuspendedMode;

    private bool IsSettlementScreenOpen => settlementUiBound && !string.IsNullOrEmpty(settlementLocationId);

    // Бриф исправлений п. 13: пока отряд спит в корчме, экран остаётся, но
    // не держит время — ночь идёт общим временем, утром часы снова стоят.
    private bool IsSettlementResting => IsSettlementScreenOpen && gameState != null && CampRest.IsResting(gameState);
    private bool IsSettlementScreenBlocking => IsSettlementScreenOpen && !IsSettlementResting;
    private float settlementRestRefreshTimer;
    private bool settlementWasResting;

    private void BindSettlementUi()
    {
        if (settlementUiBound || interfaceRoot == null)
            return;
        settlementScreen = interfaceRoot.Q<VisualElement>("settlement-screen");
        if (settlementScreen == null)
            return;
        const string screen = "Поселение";
        settlementArt = BindRequiredElement<VisualElement>(settlementScreen, screen, "settlement-art");
        settlementImage = BindRequiredElement<Image>(settlementScreen, screen, "settlement-image");
        settlementTitle = BindRequiredElement<Label>(settlementScreen, screen, "settlement-title");
        settlementSubtitle = BindRequiredElement<Label>(settlementScreen, screen, "settlement-subtitle");
        settlementDescription = BindRequiredElement<Label>(settlementScreen, screen, "settlement-description");
        settlementMoreButton = BindRequiredElement<Button>(settlementScreen, screen, "settlement-more-button");
        settlementActions = BindRequiredElement<ScrollView>(settlementScreen, screen, "settlement-actions");
        settlementMessage = BindRequiredElement<Label>(settlementScreen, screen, "settlement-message");
        settlementPeople = BindRequiredElement<VisualElement>(settlementScreen, screen, "settlement-people");
        settlementPeopleTitle = BindRequiredElement<Label>(settlementScreen, screen, "settlement-people-title");
        settlementParty = BindRequiredElement<Label>(settlementScreen, screen, "settlement-party");
        settlementExitButton = BindRequiredElement<Button>(settlementScreen, screen, "settlement-exit-button");
        // Бриф правок п. 11: журнал открывается и из поселения — поверх него.
        Button journalButton = BindRequiredElement<Button>(settlementScreen, screen, "settlement-journal-button");
        if (journalButton != null)
            journalButton.clicked += OpenJournal;
        settlementConfirm = BindRequiredElement<VisualElement>(settlementScreen, screen, "settlement-confirm");
        settlementConfirmText = BindRequiredElement<Label>(settlementScreen, screen, "settlement-confirm-text");
        settlementConfirmYes = BindRequiredElement<Button>(settlementScreen, screen, "settlement-confirm-yes");
        settlementConfirmNo = BindRequiredElement<Button>(settlementScreen, screen, "settlement-confirm-no");
        if (settlementArt == null || settlementImage == null || settlementTitle == null || settlementActions == null ||
            settlementPeople == null || settlementExitButton == null || settlementConfirm == null)
            return;

        settlementImage.scaleMode = ScaleMode.StretchToFill;
        settlementArt.RegisterCallback<GeometryChangedEvent>(_ => ApplySettlementImage());
        settlementMoreButton.clicked += () =>
        {
            settlementLongText = !settlementLongText;
            RenderSettlement();
        };
        settlementExitButton.clicked += OnSettlementExitClicked;
        settlementConfirmYes.clicked += () =>
        {
            System.Action action = settlementConfirmAction;
            CloseSettlementConfirm();
            action?.Invoke();
        };
        settlementConfirmNo.clicked += CloseSettlementConfirm;
        BindSettlementTradeUi();
        BindSettlementBoardUi();
        settlementUiBound = true;
        settlementScreen.style.display = DisplayStyle.None;
    }

    // ------------------------------------------------------------------
    // Открытие и закрытие
    // ------------------------------------------------------------------

    // Внутри поселения экран показывается всегда, если поверх ничего нет:
    // после входа, после загрузки, после разговора, ночлега и прогулки.
    private void TickSettlementScreen()
    {
        BindSettlementUi();
        if (!settlementUiBound)
            return;

        string inside = gameState != null && !isGameOver ? SettlementCampService.InsideLocationId(gameState) : string.Empty;
        if (IsSettlementScreenOpen && settlementLocationId != inside)
            CloseSettlementScreen();

        // Бой после сцены важнее: устаревшую страницу поверх него не вернуть.
        // Командир уже не там — возвращать некуда.
        if (pendingStoryBattle != null || (!string.IsNullOrEmpty(settlementSuspendedLocationId) && settlementSuspendedLocationId != inside))
        {
            settlementSuspendedLocationId = null;
            if (!IsSettlementScreenOpen)
                HideSettlementScreenVisual();
        }

        if (!IsSettlementScreenOpen && !string.IsNullOrEmpty(inside) && CanAutoOpenSettlement())
        {
            bool restoring = settlementSuspendedLocationId == inside;
            SettlementMode mode = restoring ? settlementSuspendedMode : SettlementMode.Main;
            settlementSuspendedLocationId = null;
            OpenSettlementScreen(inside, mode, restoring);
            // Разговор закончился обычным путём — один шаг часов (п. 14).
            if (restoring && settlementDialogueTimePending)
            {
                settlementDialogueTimePending = false;
                SpendSettlementActionTime();
            }
        }
        if (pendingStoryBattle != null)
            settlementDialogueTimePending = false;

        if (!IsSettlementScreenOpen)
            return;
        HandleSettlementKeys();
        // Экран держит время, пока отряд не спит (просмотр и выбор часов не
        // двигают); утро после ночлега останавливает часы здесь же.
        if (IsSettlementScreenBlocking && !ContinuousSimulationSystem.IsPaused(gameState))
            ContinuousSimulationSystem.SetPaused(gameState, true);
        if (IsSettlementResting || settlementWasResting)
        {
            settlementRestRefreshTimer += Time.unscaledDeltaTime;
            if (settlementRestRefreshTimer >= 0.5f || settlementWasResting != IsSettlementResting)
            {
                settlementRestRefreshTimer = 0f;
                settlementWasResting = IsSettlementResting;
                RenderSettlement();
            }
        }
    }

    private bool CanAutoOpenSettlement()
    {
        if (gameState == null || isGameOver || !narrativeUiBound || pendingStoryBattle != null)
            return false;
        if (IsNarrativeDialogueActive || IsLocationInteractionActive || IsLocalScreenOpen || IsCampScreenOpen || IsGameMenuOpen)
            return false;
        if (gameState.HasPendingExpeditionDecision || openedIncident != null || openedDecision != null ||
            activeQueuedModal != null || queuedModals.Count > 0)
            return false;
        // Ночлег в поселении — экран остаётся (время идёт); прочие дела похода
        // идут общим временем без экрана.
        return gameState.HasActiveExpedition &&
               (!gameState.ActiveExpedition.HasTimedActivity || CampRest.IsResting(gameState)) &&
               !LocalExplorationService.IsActive(gameState);
    }

    // restoring — возвращение после разговора: та же страница (корчма,
    // торговля, доска) и то же сообщение.
    private void OpenSettlementScreen(string locationId, SettlementMode mode = SettlementMode.Main, bool restoring = false)
    {
        BindSettlementUi();
        if (!settlementUiBound || gameState == null)
            return;
        settlementLocationId = locationId;
        settlementMode = mode;
        SettlementService.MarkVisited(gameState, locationId);
        CloseJournalForMandatoryEvent();
        if (!CampRest.IsResting(gameState))
            PauseForBlockingModal();
        if (!restoring)
        {
            settlementLongText = false;
            settlementMessageText = string.Empty;
            HideSettlementPages();
        }
        narrativeDialogueOverlay?.RemoveFromClassList(DialogueOverPlaceClass);
        settlementScreen.style.display = DisplayStyle.Flex;
        settlementScreen.BringToFront();
        settlementScreen.Focus();
        if (timeToggleButton != null)
        {
            timeToggleButton.SetEnabled(false);
            timeToggleButton.tooltip = "Сначала выйдите из поселения";
        }
        RenderSettlement();
        if (IsSettlementTradeOpen)
            RenderSettlementTrade();
        if (IsSettlementBoardOpen)
            RenderSettlementBoard();
    }

    private void HideSettlementScreenVisual()
    {
        HideSettlementPages();
        CloseSettlementConfirm();
        narrativeDialogueOverlay?.RemoveFromClassList(DialogueOverPlaceClass);
        if (settlementScreen == null)
            return;
        settlementScreen.style.display = DisplayStyle.None;
    }

    private void CloseSettlementScreen()
    {
        if (!settlementUiBound)
            return;
        CloseSettlementTalk();
        tradeReturnPersonId = null;
        HideSettlementPages();
        CloseSettlementConfirm();
        settlementLocationId = null;
        settlementScreen.style.display = DisplayStyle.None;
        RefreshInterface();
        RefreshTimeControlAvailability();
        ResumeAfterBlockingModalIfReady();
    }

    // Экран уходит на время разговора/дела и вернётся сам (TickSettlementScreen).
    private void SuspendSettlementScreen()
    {
        if (!IsSettlementScreenOpen)
            return;
        // Бриф исправлений п. 12: экран остаётся фоном под разговором (та же
        // страница), но не считается открытым — его кнопки не нажимаются, а
        // клик не уходит на карту. Вернётся сам после конца сцены.
        settlementSuspendedLocationId = settlementLocationId;
        settlementSuspendedMode = settlementMode;
        CloseSettlementConfirm();
        settlementLocationId = null;
        // Бриф правок п. 2: экран — непрозрачный фон под разговором.
        // SetEnabled(false) делал его полупрозрачным (состояние :disabled),
        // и сквозь город просвечивала карта. Клики забирает слой диалога
        // поверх; клавиши экрана при приостановке не обрабатываются. Над
        // местом слой диалога затемняет мягче — город виден.
        narrativeDialogueOverlay?.AddToClassList(DialogueOverPlaceClass);
        narrativeDialogueOverlay?.BringToFront();
    }

    private const string DialogueOverPlaceClass = "narrative-dialogue-overlay--over-place";

    private void OnSettlementExitClicked()
    {
        if (!IsSettlementScreenOpen || gameState == null)
            return;
        if (TradeHasUnconfirmedCart())
        {
            AskSettlementConfirm("Сделка не подтверждена. Отменить её и выйти?", () =>
            {
                ResetTradeCart();
                OnSettlementExitClicked();
            });
            return;
        }
        string locationId = settlementLocationId;
        bool hadCamp = SettlementCampService.HasCamp(gameState);
        SettlementCampService.ReturnToCamp(gameState);
        CloseSettlementScreen();
        Autosave();
        // С лагерем — к лагерю у входа (ночлег, сбор, новая группа); без
        // лагеря — отряд просто стоит у поселения на карте.
        if (hadCamp)
            TryOpenLocationInteraction(locationId);
        else
            AddReport("Отряд вышел из поселения.");
    }

    // ------------------------------------------------------------------
    // Показ
    // ------------------------------------------------------------------

    private void RenderSettlement()
    {
        if (!IsSettlementScreenOpen || gameState == null)
            return;
        LocationData location = gameState.FindLocation(settlementLocationId);
        SettlementProfileDefinition profile = SettlementService.Profile(gameState, settlementLocationId);
        TavernDefinition tavern = TavernService.Tavern(gameState, settlementLocationId);
        if (settlementMode == SettlementMode.Tavern && tavern == null)
            settlementMode = SettlementMode.Main;

        ContinuousClockSnapshot clock = ContinuousSimulationSystem.GetClock(gameState);
        string time = "День " + clock.Day + ", " + ContinuousSimulationSystem.FormatClock(clock.HourOfDay);
        string place = location != null ? location.Name : (profile != null ? profile.Name : "Поселение");
        string text;
        if (settlementMode == SettlementMode.People)
        {
            settlementTitle.text = "Люди поселения";
            settlementSubtitle.text = place + " · " + time;
            text = "С кем здесь сейчас можно поговорить. Разговор откроется поверх этого экрана.";
            settlementMoreButton.style.display = DisplayStyle.None;
        }
        else if (settlementMode == SettlementMode.Tavern)
        {
            settlementTitle.text = tavern.Name;
            settlementSubtitle.text = place + " · " + time;
            text = tavern.Description;
            settlementMoreButton.style.display = DisplayStyle.None;
        }
        else
        {
            settlementTitle.text = place;
            settlementSubtitle.text = (profile != null ? KindLabel(profile.Kind) + " · " : string.Empty) + time;
            text = SettlementService.Describe(gameState, location, profile);
            bool hasLong = profile != null && !string.IsNullOrWhiteSpace(profile.LongDescription);
            if (settlementLongText && hasLong)
                text += "\n\n" + profile.LongDescription;
            settlementMoreButton.text = settlementLongText ? "Короче" : "Подробнее";
            settlementMoreButton.style.display = hasLong ? DisplayStyle.Flex : DisplayStyle.None;
        }
        settlementDescription.text = text ?? string.Empty;

        settlementActions.Clear();
        HideSettlementGallery();
        if (settlementMode == SettlementMode.People)
            RenderSettlementPeopleList(profile, tavern);
        else if (settlementMode == SettlementMode.Tavern)
            RenderTavernActions(tavern);
        else
            RenderSettlementActions(profile);
        if (profile == null)
            AddSettlementHint("Здесь живут, но для проезжих ничего не устроено.", false);

        // Местные собеседники — внизу левой колонки: в корчме её люди, на
        // площади — люди поселения. Тот же источник, что общий список.
        settlementPeople.Clear();
        if (settlementMode == SettlementMode.Tavern)
        {
            foreach (SettlementPersonDefinition person in SettlementService.VisiblePeople(gameState, TavernPeople(tavern), settlementLocationId))
                settlementPeople.Add(CreateSettlementPerson(person, true));
        }
        else if (settlementMode == SettlementMode.Main && profile != null)
        {
            foreach (SettlementPersonDefinition person in SettlementService.VisiblePeople(gameState, profile.People, settlementLocationId))
                settlementPeople.Add(CreateSettlementPerson(person, false));
        }
        bool showPeople = settlementPeople.childCount > 0;
        settlementPeople.style.display = showPeople ? DisplayStyle.Flex : DisplayStyle.None;
        settlementPeopleTitle.style.display = showPeople ? DisplayStyle.Flex : DisplayStyle.None;

        settlementParty.text = SettlementPartyLine();
        settlementExitButton.text = SettlementCampService.HasCamp(gameState) ? "ВЕРНУТЬСЯ К ЛАГЕРЮ" : "ВЫЙТИ ИЗ ПОСЕЛЕНИЯ";
        settlementMessage.text = settlementMessageText;
        // Ночлег: экран остаётся, время идёт до утра; уйти посреди ночи нельзя.
        bool resting = CampRest.IsResting(gameState);
        settlementExitButton.SetEnabled(!resting);
        if (resting)
        {
            double left = gameState.ActiveExpedition.ActiveActivity != null ? gameState.ActiveExpedition.ActiveActivity.RemainingHours : 0.0;
            settlementMessage.text = "Отряд спит. До утра: " + ContinuousExpeditionCommands.FormatHours(left) + ".";
        }
        ApplySettlementImage();
    }

    private static List<SettlementPersonDefinition> TavernPeople(TavernDefinition tavern)
    {
        List<SettlementPersonDefinition> people = new List<SettlementPersonDefinition>();
        if (tavern == null)
            return people;
        if (tavern.Keeper != null && !string.IsNullOrEmpty(tavern.Keeper.Id))
            people.Add(tavern.Keeper);
        people.AddRange(tavern.Visitors);
        return people;
    }

    // Все, кто сейчас здесь и с кем можно говорить или торговать: люди
    // поселения и корчмы по их условиям присутствия — без людей других мест
    // и без скрытых.
    private List<SettlementPersonDefinition> SettlementAllPeople(SettlementProfileDefinition profile, TavernDefinition tavern)
    {
        List<SettlementPersonDefinition> all = new List<SettlementPersonDefinition>();
        List<SettlementPersonDefinition> source = new List<SettlementPersonDefinition>();
        if (profile?.People != null)
            source.AddRange(profile.People);
        source.AddRange(TavernPeople(tavern));
        foreach (SettlementPersonDefinition person in SettlementService.VisiblePeople(gameState, source, settlementLocationId))
        {
            bool talk = !string.IsNullOrEmpty(person.DialogueId) && SettlementDialogueExists(person.DialogueId);
            bool trades = !string.IsNullOrEmpty(person.MerchantId) && profile?.FindMerchant(person.MerchantId) != null;
            if ((talk || trades) && !all.Exists(p => p.Id == person.Id))
                all.Add(person);
        }
        return all;
    }

    // Бриф правок п. 2: общий список — в основной области, где изображение
    // города, плашками с портретами; в левом столбце — только «Назад».
    private VisualElement settlementGallery;

    private void RenderSettlementPeopleList(SettlementProfileDefinition profile, TavernDefinition tavern)
    {
        EnsureSettlementGallery();
        settlementGallery.Clear();
        foreach (SettlementPersonDefinition person in SettlementAllPeople(profile, tavern))
        {
            VisualElement card = CreateSettlementPerson(person, false);
            card.AddToClassList("settlement-person--gallery");
            if (!string.IsNullOrEmpty(person.Activity))
            {
                Label activity = new Label(person.Activity);
                activity.AddToClassList("settlement-person-activity");
                (card.Q<VisualElement>("settlement-person-text") ?? card).Add(activity);
            }
            settlementGallery.Add(card);
        }
        settlementGallery.style.display = DisplayStyle.Flex;
        AddSettlementAction("Назад", null, true, () =>
        {
            settlementMode = SettlementMode.Main;
            RenderSettlement();
        }, false);
    }

    private void RenderSettlementActions(SettlementProfileDefinition profile)
    {
        if (profile == null)
            return;
        RenderSettlementProfileActions(profile);
        // Бриф правок п. 2: «Люди поселения» — в самом низу столбца
        // действий; сам список — плашками в основной области над городом.
        int people = SettlementAllPeople(profile, TavernService.Tavern(gameState, settlementLocationId)).Count;
        if (people > 0)
        {
            AddSettlementSection(string.Empty);
            AddSettlementAction("Люди поселения (" + people + ")", "Все, с кем здесь можно поговорить.", true, () =>
            {
                settlementMode = SettlementMode.People;
                settlementMessageText = string.Empty;
                RenderSettlement();
            }, false);
        }
    }

    private void RenderSettlementProfileActions(SettlementProfileDefinition profile)
    {
        if (profile.Actions == null)
            return;
        NarrativeEvaluationContext context = SettlementService.Context(gameState);
        foreach (SettlementActionDefinition action in profile.Actions)
        {
            if (action == null || !(action.ShowConditions?.Evaluate(context) ?? true))
                continue;
            // Невозможное по данным (нет торговца, нет места) — кнопки нет.
            if (!SettlementActionWired(profile, action))
                continue;
            bool enabled = action.EnableConditions?.Evaluate(context) ?? true;
            string reason = enabled ? string.Empty : (string.IsNullOrWhiteSpace(action.DisabledReason) ? "Сейчас недоступно." : action.DisabledReason);
            if (enabled && action.Type == SettlementActionType.Rest && !TavernService.CanLodge(gameState, settlementLocationId, out string lodgeReason))
            {
                enabled = false;
                reason = lodgeReason;
            }
            if (enabled && action.Type == SettlementActionType.Healer && !WoundRecovery.CanVisitHealer(gameState, action.Price, out string healerReason))
            {
                enabled = false;
                reason = healerReason;
            }
            string hint = enabled ? action.Hint : reason;
            if (action.Type == SettlementActionType.Healer && action.Price > 0)
                hint = (string.IsNullOrEmpty(hint) ? string.Empty : hint + " ") + "Цена: " + action.Price + " зол. за раненого.";
            if (action.Type == SettlementActionType.NoticeBoard && enabled)
            {
                int unread = NoticeBoardService.UnreadCount(gameState, settlementLocationId);
                if (unread > 0)
                    hint = (string.IsNullOrEmpty(hint) ? string.Empty : hint + " ") + "Новых: " + unread + ".";
            }
            SettlementActionDefinition captured = action;
            AddSettlementAction(action.Label, hint, enabled, () => RunSettlementAction(captured), action.Type == SettlementActionType.Tavern);
        }
    }

    private bool SettlementActionWired(SettlementProfileDefinition profile, SettlementActionDefinition action)
    {
        switch (action.Type)
        {
            case SettlementActionType.Dialogue:
                return !string.IsNullOrEmpty(action.TargetId) && SettlementDialogueExists(action.TargetId);
            case SettlementActionType.Trade:
                return profile.FindMerchant(action.TargetId) != null;
            case SettlementActionType.Tavern:
                return TavernService.Tavern(gameState, settlementLocationId) != null;
            case SettlementActionType.Rest:
                return TavernService.Tavern(gameState, settlementLocationId)?.Lodging?.Enabled == true;
            case SettlementActionType.LocalMap:
                return LocalLocationCatalog.Find(action.TargetId) != null;
            case SettlementActionType.NoticeBoard:
                return NoticeBoardService.Board(gameState, settlementLocationId) != null;
            case SettlementActionType.Healer:
                return true;
            default:
                return false;
        }
    }

    private void RunSettlementAction(SettlementActionDefinition action)
    {
        if (!IsSettlementScreenOpen || gameState == null)
            return;
        switch (action.Type)
        {
            case SettlementActionType.Dialogue:
                StartSettlementDialogue(action.TargetId);
                break;
            case SettlementActionType.Trade:
                // Бриф правок (19 пунктов) п. 2: кнопка лавки — сразу торговля,
                // «Назад» — на страницу поселения.
                tradeReturnPersonId = null;
                OpenSettlementTrade(action.TargetId);
                break;
            case SettlementActionType.Tavern:
                settlementMode = SettlementMode.Tavern;
                settlementMessageText = string.Empty;
                RenderSettlement();
                break;
            case SettlementActionType.Rest:
                AskLodging();
                break;
            case SettlementActionType.LocalMap:
                StartSettlementWalk(action.TargetId);
                break;
            case SettlementActionType.NoticeBoard:
                OpenSettlementBoard();
                break;
            case SettlementActionType.Healer:
                VisitHealer(action.Price);
                break;
        }
    }

    // Решение пользователя 31.10.2026 (аудит A-02): знахарь снимает тяжёлые
    // раны вошедших за плату — одно действие поселения по времени.
    private void VisitHealer(int pricePerPerson)
    {
        if (!WoundRecovery.TryVisitHealer(gameState, pricePerPerson, out string message))
        {
            ShowSettlementMessage(message);
            return;
        }
        AddReport(message);
        SpendSettlementActionTime();
        ShowSettlementMessage(message);
        RefreshInterface();
    }

    private void RenderTavernActions(TavernDefinition tavern)
    {
        SettlementPersonDefinition keeper = tavern.Keeper;
        if (keeper != null && !string.IsNullOrEmpty(keeper.DialogueId) && SettlementDialogueExists(keeper.DialogueId) &&
            SettlementService.Check(gameState, keeper.Conditions))
        {
            string dialogueId = keeper.DialogueId;
            AddSettlementAction("Поговорить с хозяином", keeper.DisplayName + (string.IsNullOrEmpty(keeper.Activity) ? string.Empty : " — " + keeper.Activity.ToLowerInvariant()),
                true, () => StartSettlementDialogue(dialogueId), true);
        }

        List<TavernOfferView> offers = TavernService.Offers(gameState, settlementLocationId);
        if (offers.Count > 0)
            AddSettlementSection("Предложения");
        foreach (TavernOfferView view in offers)
        {
            TavernOfferView captured = view;
            string price = view.Offer.Price > 0 ? " — " + view.Offer.Price + " зол." : string.Empty;
            string hint = view.Enabled
                ? (string.IsNullOrEmpty(view.SourceName) ? string.Empty : view.SourceName + ": ") + view.Offer.Description + ErrandLimitSuffix(view.Offer.ErrandId)
                : view.Reason;
            AddSettlementAction(view.Offer.Label + price, hint, view.Enabled, () => AskTavernOffer(captured.Offer), false);
        }

        // Бриф правок (19 пунктов) п. 10: кандидаты в наёмники.
        RenderHireCandidates();

        if (tavern.Lodging != null && tavern.Lodging.Enabled)
        {
            AddSettlementSection("Ночлег");
            bool canLodge = TavernService.CanLodge(gameState, settlementLocationId, out string reason);
            AddSettlementAction("Поесть и переночевать", canLodge ? TavernService.DescribeLodging(gameState, settlementLocationId) : reason,
                canLodge, AskLodging, false);
        }

        AddSettlementSection(string.Empty);
        AddSettlementAction("Вернуться в поселение", null, true, () =>
        {
            settlementMode = SettlementMode.Main;
            settlementMessageText = string.Empty;
            RenderSettlement();
        }, false);
    }

    private string ErrandLimitSuffix(string errandId)
    {
        ErrandDefinition errand = SettlementCatalog.Current.FindErrand(errandId);
        string limit = ErrandService.DescribeLimit(errand);
        return string.IsNullOrEmpty(limit) ? string.Empty : " " + limit;
    }

    private void AskTavernOffer(TavernOfferDefinition offer)
    {
        string limit = ErrandLimitSuffix(offer.ErrandId);
        string question = offer.Price > 0
            ? "«" + offer.Label + "» за " + offer.Price + " зол. " + offer.Description + limit + " Заплатить?"
            : "«" + offer.Label + "». " + offer.Description + limit + " Согласиться?";
        AskSettlementConfirm(question, () =>
        {
            if (!TavernService.TryTakeOffer(gameState, settlementLocationId, offer.Id, out string message, out string dialogueId))
            {
                ShowSettlementMessage(message);
                return;
            }
            AddReport(message);
            Autosave();
            if (!string.IsNullOrEmpty(dialogueId) && SettlementDialogueExists(dialogueId))
                StartSettlementDialogue(dialogueId);
            else
                ShowSettlementMessage(message);
        });
    }

    private void AskLodging()
    {
        if (!TavernService.CanLodge(gameState, settlementLocationId, out string reason))
        {
            ShowSettlementMessage(reason);
            return;
        }
        AskSettlementConfirm("Ночлег: " + TavernService.DescribeLodging(gameState, settlementLocationId) + ". Остаться на ночь?", () =>
        {
            if (!TavernService.TryStartLodging(gameState, settlementLocationId, out string message))
            {
                ShowSettlementMessage(message);
                return;
            }
            AddReport(message);
            Autosave();
            // Ночь идёт общим временем, экран поселения остаётся (п. 13
            // брифа исправлений); утром часы снова стоят.
            settlementWasResting = true;
            RenderSettlement();
            RefreshInterface();
            RefreshContinuousTimeUi(true);
        });
    }

    // «Прогуляться»: назначенная локальная карта поселения.
    private void StartSettlementWalk(string localLocationId)
    {
        LocalLocationDefinition definition = LocalLocationCatalog.Find(localLocationId);
        if (definition == null)
        {
            ShowSettlementMessage("Пройтись здесь пока негде.");
            return;
        }
        if (!LocalExplorationService.Enter(gameState, definition, null, out string reason, settlementLocationId))
        {
            ShowSettlementMessage(reason);
            return;
        }
        CloseSettlementScreen();
        OpenLocalScreen();
        Autosave();
    }

    // Бриф правок п. 14: в поселении часы стоят; целое действие — разговор
    // до конца или подтверждённая сделка — сдвигает их один раз на минуты
    // из Базы поселений. Сдвиг проводит полночь и прочие последствия тем же
    // путём, что шаги в исследуемом месте, и снова оставляет паузу.
    private bool settlementDialogueTimePending;

    private void SpendSettlementActionTime()
    {
        if (gameState == null || isGameOver)
            return;
        int minutes = SettlementCatalog.Current.ActionMinutes;
        if (minutes <= 0)
            return;
        ContinuousSimulationBatch batch = ContinuousSimulationSystem.AdvanceLocalHours(gameState, minutes / 60.0);
        if (batch.HasReportableContent)
            ProcessContinuousSimulationBatch(batch);
        ContinuousSimulationSystem.SetPaused(gameState, true);
        RefreshContinuousClockOnly();
        if (IsSettlementScreenOpen)
            RenderSettlement();
    }

    // Разговор — единственной точкой входа в диалог. После обычного конца
    // сцены экран вернётся в том же режиме (TickSettlementScreen).
    private void StartSettlementDialogue(string dialogueId)
    {
        if (!IsSettlementScreenOpen || string.IsNullOrEmpty(dialogueId))
            return;
        string locationId = settlementLocationId;
        SettlementMode mode = settlementMode;
        SuspendSettlementScreen();
        settlementDialogueTimePending = true;
        if (!TryOpenNarrativeDialogueById(dialogueId))
        {
            settlementDialogueTimePending = false;
            settlementSuspendedLocationId = null;
            OpenSettlementScreen(locationId, mode, true);
            ShowSettlementMessage("Сейчас не поговорить.");
        }
    }

    private bool SettlementDialogueExists(string dialogueId)
    {
        if (narrativeDialogueDatabase == null)
            narrativeDialogueDatabase = DialogueDatabaseRuntime.LoadDefaultDatabase();
        return narrativeDialogueDatabase != null && narrativeDialogueDatabase.FindDialogue(dialogueId) != null;
    }

    private void EnsureSettlementGallery()
    {
        if (settlementGallery != null)
            return;
        settlementGallery = new VisualElement { name = "settlement-people-gallery" };
        settlementGallery.AddToClassList("settlement-people-gallery");
        settlementGallery.style.display = DisplayStyle.None;
        settlementScreen.Add(settlementGallery);
    }

    private void HideSettlementGallery()
    {
        if (settlementGallery != null)
            settlementGallery.style.display = DisplayStyle.None;
    }

    private VisualElement CreateSettlementPerson(SettlementPersonDefinition person, bool tavern)
    {
        VisualElement card = new VisualElement();
        card.AddToClassList("settlement-person");
        VisualElement portrait = new VisualElement();
        portrait.AddToClassList("settlement-person-portrait");
        if (narrativeDialogueDatabase == null)
            narrativeDialogueDatabase = DialogueDatabaseRuntime.LoadDefaultDatabase();
        Sprite sprite = !string.IsNullOrEmpty(person.SpeakerId) ? narrativeDialogueDatabase?.FindSpeaker(person.SpeakerId)?.Portrait : null;
        if (sprite != null)
            portrait.style.backgroundImage = new StyleBackground(sprite);
        else
        {
            Label initial = new Label(string.IsNullOrEmpty(person.DisplayName) ? "?" : person.DisplayName.Substring(0, 1));
            initial.AddToClassList("settlement-person-initial");
            portrait.Add(initial);
        }
        card.Add(portrait);
        // Бриф правок (19 пунктов) п. 3: имя и описание — одним блоком (в
        // общем списке он стоит справа от портрета).
        VisualElement text = new VisualElement { name = "settlement-person-text" };
        text.AddToClassList("settlement-person-text");
        card.Add(text);
        Label name = new Label(person.DisplayName);
        name.AddToClassList("settlement-person-name");
        text.Add(name);
        string role = person.Role;
        if (tavern && !string.IsNullOrEmpty(person.Activity))
            role = string.IsNullOrEmpty(role) ? person.Activity : role + " · " + person.Activity.ToLowerInvariant();
        Label roleLabel = new Label(role);
        roleLabel.AddToClassList("settlement-person-role");
        text.Add(roleLabel);

        bool talk = !string.IsNullOrEmpty(person.DialogueId) && SettlementDialogueExists(person.DialogueId);
        bool trades = !string.IsNullOrEmpty(person.MerchantId) &&
                      SettlementService.Profile(gameState, settlementLocationId)?.FindMerchant(person.MerchantId) != null;
        // Бриф правок (19 пунктов) п. 2: торговец — сначала разговор (кто он,
        // «Покажи свой товар»), а прямая торговля — у кнопки лавки.
        card.tooltip = trades ? "Клик — поговорить с торговцем." : talk ? "Клик — поговорить." : person.DisplayName;
        string dialogueId = person.DialogueId;
        string merchantId = person.MerchantId;
        card.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            if (trades)
                OpenMerchantTalk(person);
            else if (talk)
                StartSettlementDialogue(dialogueId);
        });
        // Торгующий посетитель корчмы — ещё и кнопкой на панели (прямая торговля).
        if (tavern && trades)
        {
            string label = "Торговать: " + person.DisplayName;
            AddSettlementAction(label, person.Role, true, () =>
            {
                tradeReturnPersonId = null;
                OpenSettlementTrade(merchantId);
            }, false);
        }
        return card;
    }

    private string SettlementPartyLine()
    {
        List<string> here = new List<string>();
        foreach (string id in PartyPresence.PresentIds(gameState))
            here.Add(PersonName(id));
        string line = "С вами: " + (here.Count > 0 ? string.Join(", ", here) : "никого");
        if (SettlementCampService.HasCamp(gameState) && gameState.SettlementCamp.WaitingIds.Count > 0)
        {
            List<string> waiting = new List<string>();
            foreach (string id in gameState.SettlementCamp.WaitingIds)
                waiting.Add(PersonName(id));
            line += "   ·   В лагере у входа: " + string.Join(", ", waiting);
        }
        return line + "   ·   Золото: " + gameState.ArmyGold + "   ·   Припасы: " + gameState.ArmySupply;
    }

    private void AddSettlementAction(string text, string hint, bool enabled, System.Action onClick, bool primary)
    {
        Button button = new Button(() =>
        {
            if (IsSettlementScreenOpen)
                onClick?.Invoke();
        }) { text = text };
        button.AddToClassList("ks-button");
        button.AddToClassList("settlement-action");
        button.EnableInClassList("settlement-action--primary", primary);
        button.SetEnabled(enabled);
        settlementActions.Add(button);
        if (!string.IsNullOrWhiteSpace(hint))
            AddSettlementHint(hint, !enabled);
    }

    private void AddSettlementHint(string hint, bool blocked)
    {
        Label label = new Label(hint);
        label.AddToClassList("settlement-action-hint");
        label.EnableInClassList("settlement-action-hint--blocked", blocked);
        settlementActions.Add(label);
    }

    private void AddSettlementSection(string title)
    {
        Label label = new Label(title);
        label.AddToClassList("settlement-section-title");
        settlementActions.Add(label);
    }

    private void ShowSettlementMessage(string message)
    {
        settlementMessageText = message ?? string.Empty;
        if (settlementMessage != null)
            settlementMessage.text = settlementMessageText;
        if (IsSettlementBoardOpen)
            RenderSettlementBoard();
        else if (IsSettlementTradeOpen)
            RenderSettlementTrade();
        else
            RenderSettlement();
    }

    // Изображение: экран, корчма или доска — кадрирование из Базы
    // поселений тем же расчётом, что предпросмотр автора. Без изображения —
    // нейтральный фон экрана.
    private void ApplySettlementImage()
    {
        if (settlementImage == null || !IsSettlementScreenOpen)
            return;
        SettlementDatabaseAsset database = SettlementDatabaseRuntime.Load();
        SettlementInstanceData instance = SettlementService.FindInstance(gameState, settlementLocationId);
        SettlementProfileEntry entry = database != null && instance != null ? database.FindEntry(instance.ProfileId) : null;
        SettlementImageFraming framing = null;
        Sprite sprite = null;
        if (entry?.Art != null)
        {
            if (IsSettlementBoardOpen && entry.Art.Board?.Image != null)
                framing = entry.Art.Board;
            else if (settlementMode == SettlementMode.Tavern && entry.Art.Tavern?.Image != null)
                framing = entry.Art.Tavern;
            else
                framing = entry.Art.Screen;
            sprite = framing == entry.Art.Screen
                ? database.ScreenImage(instance.ProfileId, SettlementService.ActiveVariant(gameState, entry.Data)?.Id)
                : framing?.Image;
        }
        if (sprite == null || sprite.texture == null)
        {
            settlementImage.image = null;
            settlementImage.style.display = DisplayStyle.None;
            return;
        }
        float width = settlementArt.resolvedStyle.width;
        float height = settlementArt.resolvedStyle.height;
        if (float.IsNaN(width) || float.IsNaN(height) || width <= 1f || height <= 1f)
            return;
        Rect tr = sprite.textureRect;
        Rect uv = SettlementImageMath.VisibleUv(tr.width, tr.height, width, height, framing.Fit == SettlementImageFit.Cover,
            framing.Focus, framing.Zoom, out Rect draw);
        settlementImage.image = sprite.texture;
        settlementImage.uv = SettlementImageMath.TextureUv(sprite, uv);
        settlementImage.style.left = Length.Percent(draw.x * 100f);
        settlementImage.style.top = Length.Percent(draw.y * 100f);
        settlementImage.style.width = Length.Percent(draw.width * 100f);
        settlementImage.style.height = Length.Percent(draw.height * 100f);
        settlementImage.style.display = DisplayStyle.Flex;
    }

    // ------------------------------------------------------------------
    // Подтверждение и клавиши
    // ------------------------------------------------------------------

    private void AskSettlementConfirm(string text, System.Action onYes)
    {
        settlementConfirmAction = onYes;
        settlementConfirmText.text = text;
        settlementConfirm.style.display = DisplayStyle.Flex;
        settlementConfirm.BringToFront();
    }

    private void CloseSettlementConfirm()
    {
        settlementConfirmAction = null;
        if (settlementConfirm != null)
            settlementConfirm.style.display = DisplayStyle.None;
    }

    private bool IsSettlementConfirmOpen => settlementConfirm != null && settlementConfirm.style.display == DisplayStyle.Flex;

    // Esc закрывает самый глубокий безопасный слой. Незавершённая сделка
    // сбрасывается только после «Отменить сделку?».
    private void HandleSettlementKeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || IsGameMenuOpen || IsJournalOpen)
            return;
        if (IsSettlementTalkOpen)
        {
            CloseSettlementTalk();
            return;
        }
        if (IsSettlementConfirmOpen)
        {
            CloseSettlementConfirm();
            return;
        }
        if (IsSettlementTradeOpen)
        {
            OnTradeBackClicked();
            return;
        }
        if (IsSettlementBoardOpen)
        {
            CloseSettlementBoard();
            return;
        }
        if (settlementMode != SettlementMode.Main)
        {
            settlementMode = SettlementMode.Main;
            RenderSettlement();
        }
    }

    private void HideSettlementPages()
    {
        HideSettlementTrade();
        HideSettlementBoard();
    }

    // Дочерняя страница закрывает основную панель целиком.
    private void SyncSettlementPageClass()
    {
        settlementScreen?.EnableInClassList("settlement-screen--page", IsSettlementTradeOpen || IsSettlementBoardOpen);
    }

    private static string KindLabel(SettlementKind kind)
    {
        switch (kind)
        {
            case SettlementKind.Village: return "деревня";
            case SettlementKind.TradingPost: return "торговое поселение";
            default: return "укреплённый городок";
        }
    }
}
