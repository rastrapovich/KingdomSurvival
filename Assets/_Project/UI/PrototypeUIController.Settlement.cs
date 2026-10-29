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
        Tavern
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
        settlementParty = BindRequiredElement<Label>(settlementScreen, screen, "settlement-party");
        settlementExitButton = BindRequiredElement<Button>(settlementScreen, screen, "settlement-exit-button");
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
            settlementSuspendedLocationId = null;

        if (!IsSettlementScreenOpen && !string.IsNullOrEmpty(inside) && CanAutoOpenSettlement())
        {
            SettlementMode mode = settlementSuspendedLocationId == inside ? settlementSuspendedMode : SettlementMode.Main;
            settlementSuspendedLocationId = null;
            OpenSettlementScreen(inside, mode);
        }

        if (IsSettlementScreenOpen)
            HandleSettlementKeys();
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
        // Ночлег и другие дела похода идут общим временем — экран вернётся утром.
        return gameState.HasActiveExpedition && !gameState.ActiveExpedition.HasTimedActivity &&
               !LocalExplorationService.IsActive(gameState);
    }

    private void OpenSettlementScreen(string locationId, SettlementMode mode = SettlementMode.Main)
    {
        BindSettlementUi();
        if (!settlementUiBound || gameState == null)
            return;
        settlementLocationId = locationId;
        settlementMode = mode;
        settlementLongText = false;
        settlementMessageText = string.Empty;
        SettlementService.MarkVisited(gameState, locationId);
        CloseJournalForMandatoryEvent();
        PauseForBlockingModal();
        HideSettlementPages();
        settlementScreen.style.display = DisplayStyle.Flex;
        settlementScreen.BringToFront();
        settlementScreen.Focus();
        if (timeToggleButton != null)
        {
            timeToggleButton.SetEnabled(false);
            timeToggleButton.tooltip = "Сначала выйдите из поселения";
        }
        RenderSettlement();
    }

    private void CloseSettlementScreen()
    {
        if (!settlementUiBound)
            return;
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
        settlementSuspendedLocationId = settlementLocationId;
        settlementSuspendedMode = settlementMode;
        HideSettlementPages();
        CloseSettlementConfirm();
        settlementLocationId = null;
        settlementScreen.style.display = DisplayStyle.None;
    }

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
        if (settlementMode == SettlementMode.Tavern)
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
        if (settlementMode == SettlementMode.Tavern)
            RenderTavernActions(tavern);
        else
            RenderSettlementActions(profile);
        if (profile == null)
            AddSettlementHint("Здесь живут, но для проезжих ничего не устроено.", false);

        settlementPeople.Clear();
        if (settlementMode == SettlementMode.Tavern)
        {
            List<SettlementPersonDefinition> people = new List<SettlementPersonDefinition>();
            if (tavern.Keeper != null && !string.IsNullOrEmpty(tavern.Keeper.Id))
                people.Add(tavern.Keeper);
            people.AddRange(tavern.Visitors);
            foreach (SettlementPersonDefinition person in SettlementService.VisiblePeople(gameState, people))
                settlementPeople.Add(CreateSettlementPerson(person, true));
        }
        else if (profile != null)
        {
            foreach (SettlementPersonDefinition person in SettlementService.VisiblePeople(gameState, profile.People))
                settlementPeople.Add(CreateSettlementPerson(person, false));
        }

        settlementParty.text = SettlementPartyLine();
        settlementExitButton.text = SettlementCampService.HasCamp(gameState) ? "ВЕРНУТЬСЯ К ЛАГЕРЮ" : "ВЫЙТИ ИЗ ПОСЕЛЕНИЯ";
        settlementMessage.text = settlementMessageText;
        ApplySettlementImage();
    }

    private void RenderSettlementActions(SettlementProfileDefinition profile)
    {
        if (profile?.Actions == null)
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
            string hint = enabled ? action.Hint : reason;
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
        }
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
            // Ночь идёт общим временем: экран закрывается и вернётся утром
            // сам (командир по-прежнему внутри, TickSettlementScreen).
            CloseSettlementScreen();
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

    // Разговор — единственной точкой входа в диалог. После обычного конца
    // сцены экран вернётся в том же режиме (TickSettlementScreen).
    private void StartSettlementDialogue(string dialogueId)
    {
        if (!IsSettlementScreenOpen || string.IsNullOrEmpty(dialogueId))
            return;
        string locationId = settlementLocationId;
        SettlementMode mode = settlementMode;
        SuspendSettlementScreen();
        if (!TryOpenNarrativeDialogueById(dialogueId))
        {
            settlementSuspendedLocationId = null;
            OpenSettlementScreen(locationId, mode);
            ShowSettlementMessage("Сейчас не поговорить.");
        }
    }

    private bool SettlementDialogueExists(string dialogueId)
    {
        if (narrativeDialogueDatabase == null)
            narrativeDialogueDatabase = DialogueDatabaseRuntime.LoadDefaultDatabase();
        return narrativeDialogueDatabase != null && narrativeDialogueDatabase.FindDialogue(dialogueId) != null;
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
        Label name = new Label(person.DisplayName);
        name.AddToClassList("settlement-person-name");
        card.Add(name);
        string role = person.Role;
        if (tavern && !string.IsNullOrEmpty(person.Activity))
            role = string.IsNullOrEmpty(role) ? person.Activity : role + " · " + person.Activity.ToLowerInvariant();
        Label roleLabel = new Label(role);
        roleLabel.AddToClassList("settlement-person-role");
        card.Add(roleLabel);

        bool talk = !string.IsNullOrEmpty(person.DialogueId) && SettlementDialogueExists(person.DialogueId);
        bool trades = !string.IsNullOrEmpty(person.MerchantId) &&
                      SettlementService.Profile(gameState, settlementLocationId)?.FindMerchant(person.MerchantId) != null;
        card.tooltip = talk && trades ? "Клик — поговорить; торговля — кнопкой на панели." : talk ? "Клик — поговорить." : trades ? "Клик — торговать." : person.DisplayName;
        string dialogueId = person.DialogueId;
        string merchantId = person.MerchantId;
        card.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            if (talk)
                StartSettlementDialogue(dialogueId);
            else if (trades)
                OpenSettlementTrade(merchantId);
        });
        // Торгующий посетитель корчмы — ещё и кнопкой на панели.
        if (tavern && trades)
        {
            string label = "Торговать: " + person.DisplayName;
            AddSettlementAction(label, person.Role, true, () => OpenSettlementTrade(merchantId), false);
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
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || IsGameMenuOpen)
            return;
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
        if (settlementMode == SettlementMode.Tavern)
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
