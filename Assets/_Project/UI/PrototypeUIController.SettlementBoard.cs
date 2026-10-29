using System.Collections.Generic;
using UnityEngine.UIElements;

// 12Т (дополнение «Доска объявлений»): страница доски поселения. Слева —
// читаемый список (новые выделены, фильтр по типу), справа — текст
// выбранного объявления, состояние задания и только подключённые действия
// (NoticeBoardService). Чтение само ничего не принимает. Изображение доски —
// фон страницы; текст — средствами UI Toolkit.
public partial class PrototypeUIController
{
    private static readonly string[] BoardFilterChoices =
        { "Все объявления", "Просьбы", "Заказы", "Предупреждения", "Розыск", "Торг", "Новости" };

    private VisualElement boardPage;
    private Label boardTitle;
    private Label boardSubtitle;
    private DropdownField boardFilter;
    private ScrollView boardList;
    private Label boardDetailTitle;
    private Label boardDetailMeta;
    private Label boardDetailText;
    private Label boardDetailStatus;
    private VisualElement boardDetailActions;
    private Label boardMessage;
    private string boardSelectedId;
    private string boardMessageText = string.Empty;

    private bool IsSettlementBoardOpen => boardPage != null && boardPage.style.display == DisplayStyle.Flex;

    private void BindSettlementBoardUi()
    {
        const string screen = "Доска объявлений";
        boardPage = BindRequiredElement<VisualElement>(settlementScreen, screen, "settlement-board-page");
        boardTitle = BindRequiredElement<Label>(settlementScreen, screen, "board-title");
        boardSubtitle = BindRequiredElement<Label>(settlementScreen, screen, "board-subtitle");
        boardFilter = BindRequiredElement<DropdownField>(settlementScreen, screen, "board-filter");
        boardList = BindRequiredElement<ScrollView>(settlementScreen, screen, "board-list");
        boardDetailTitle = BindRequiredElement<Label>(settlementScreen, screen, "board-detail-title");
        boardDetailMeta = BindRequiredElement<Label>(settlementScreen, screen, "board-detail-meta");
        boardDetailText = BindRequiredElement<Label>(settlementScreen, screen, "board-detail-text");
        boardDetailStatus = BindRequiredElement<Label>(settlementScreen, screen, "board-detail-status");
        boardDetailActions = BindRequiredElement<VisualElement>(settlementScreen, screen, "board-detail-actions");
        boardMessage = BindRequiredElement<Label>(settlementScreen, screen, "board-message");
        Button back = BindRequiredElement<Button>(settlementScreen, screen, "board-back-button");
        if (boardPage == null || boardList == null || boardFilter == null || back == null)
            return;
        boardFilter.choices = new List<string>(BoardFilterChoices);
        boardFilter.index = 0;
        boardFilter.RegisterValueChangedCallback(_ => RenderSettlementBoard());
        back.clicked += CloseSettlementBoard;
        boardPage.style.display = DisplayStyle.None;
    }

    private void OpenSettlementBoard()
    {
        if (!IsSettlementScreenOpen || boardPage == null || NoticeBoardService.Board(gameState, settlementLocationId) == null)
            return;
        HideSettlementTrade();
        boardSelectedId = null;
        boardMessageText = string.Empty;
        boardPage.style.display = DisplayStyle.Flex;
        boardPage.BringToFront();
        SyncSettlementPageClass();
        ApplySettlementImage();
        RenderSettlementBoard();
    }

    private void CloseSettlementBoard()
    {
        HideSettlementBoard();
        RenderSettlement();
    }

    private void HideSettlementBoard()
    {
        if (boardPage != null)
            boardPage.style.display = DisplayStyle.None;
        SyncSettlementPageClass();
    }

    private void RenderSettlementBoard()
    {
        if (!IsSettlementBoardOpen || gameState == null)
            return;
        NoticeBoardDefinition board = NoticeBoardService.Board(gameState, settlementLocationId);
        if (board == null)
        {
            CloseSettlementBoard();
            return;
        }
        boardTitle.text = board.Name;
        boardSubtitle.text = board.Description + (string.IsNullOrWhiteSpace(board.ReaderNote) ? string.Empty : " " + board.ReaderNote);

        List<NoticeView> notices = NoticeBoardService.Notices(gameState, settlementLocationId);
        boardList.Clear();
        NoticeView selected = null;
        foreach (NoticeView view in notices)
        {
            if (boardFilter.index > 0 && (int)view.Notice.Type != boardFilter.index - 1)
                continue;
            if (view.Notice.Id == boardSelectedId)
                selected = view;
            boardList.Add(CreateBoardRow(view));
        }
        if (boardList.childCount == 0)
        {
            Label empty = new Label(notices.Count == 0 ? "Сейчас ничего не вывешено." : "Таких объявлений нет.");
            empty.AddToClassList("settlement-action-hint");
            boardList.Add(empty);
        }
        RenderBoardDetail(selected);
        boardMessage.text = boardMessageText;
    }

    private VisualElement CreateBoardRow(NoticeView view)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("board-row");
        row.EnableInClassList("board-row--unread", !view.Read);
        row.EnableInClassList("board-row--selected", view.Notice.Id == boardSelectedId);
        Label title = new Label((view.Read ? string.Empty : "• ") + view.Notice.Title);
        title.AddToClassList("board-row-title");
        row.Add(title);
        string meta = NoticeBoardService.TypeLabel(view.Notice.Type) + " · " + NoticeBoardService.MediumLabel(view.Notice.Medium) +
                      (string.IsNullOrEmpty(view.StatusText) ? string.Empty : " · " + view.StatusText) +
                      (view.Read ? string.Empty : " · новое");
        Label metaLabel = new Label(meta);
        metaLabel.AddToClassList("board-row-meta");
        row.Add(metaLabel);
        string noticeId = view.Notice.Id;
        row.RegisterCallback<ClickEvent>(evt =>
        {
            evt.StopPropagation();
            boardSelectedId = noticeId;
            boardMessageText = string.Empty;
            // Прочитано — только отметка; ничего не принимается и не выдаётся.
            NoticeBoardService.MarkRead(gameState, settlementLocationId, noticeId);
            RenderSettlementBoard();
        });
        return row;
    }

    private void RenderBoardDetail(NoticeView view)
    {
        boardDetailActions.Clear();
        if (view == null)
        {
            boardDetailTitle.text = "Выберите объявление";
            boardDetailMeta.text = string.Empty;
            boardDetailText.text = "Береста, дощечки и знаки. Что написано — перескажут.";
            boardDetailStatus.text = string.Empty;
            return;
        }
        NoticeDefinition notice = view.Notice;
        boardDetailTitle.text = notice.Title;
        List<string> meta = new List<string> { NoticeBoardService.TypeLabel(notice.Type), NoticeBoardService.MediumLabel(notice.Medium) };
        if (!string.IsNullOrWhiteSpace(notice.AuthorName))
            meta.Add("от: " + notice.AuthorName);
        if (!string.IsNullOrWhiteSpace(notice.ContactName))
            meta.Add("обращаться: " + notice.ContactName);
        if (view.HoursLeft > 0)
            meta.Add("провисит ещё ~" + System.Math.Max(1, (int)System.Math.Ceiling(view.HoursLeft / 24.0)) + " дн.");
        boardDetailMeta.text = string.Join(" · ", meta);
        boardDetailText.text = notice.Text;

        List<string> status = new List<string>();
        if (!string.IsNullOrWhiteSpace(notice.RewardText))
            status.Add("Награда: " + notice.RewardText + ".");
        if (view.Errand != null)
        {
            status.Add(view.StatusText + ".");
            string limit = ErrandService.DescribeLimit(view.Errand);
            if (view.ErrandStatus == ErrandStatus.NotTaken && !string.IsNullOrEmpty(limit))
                status.Add(limit + " — считается с момента, как возьмётесь.");
        }
        boardDetailStatus.text = string.Join(" ", status);

        foreach (NoticeActionView action in view.Actions)
        {
            NoticeActionView captured = action;
            Button button = new Button(() => RunNoticeAction(notice, captured.Kind)) { text = action.Label };
            button.AddToClassList("ks-button");
            button.AddToClassList("board-action");
            button.SetEnabled(action.Enabled);
            if (!action.Enabled && !string.IsNullOrEmpty(action.Reason))
                button.tooltip = action.Reason;
            boardDetailActions.Add(button);
        }
    }

    private void RunNoticeAction(NoticeDefinition notice, NoticeActionKind kind)
    {
        if (!IsSettlementBoardOpen || gameState == null)
            return;
        string message;
        switch (kind)
        {
            case NoticeActionKind.AcceptErrand:
                ErrandDefinition errand = SettlementCatalog.Current.FindErrand(notice.ErrandId);
                string limit = ErrandService.DescribeLimit(errand);
                AskSettlementConfirm("Взяться: «" + (errand != null ? errand.Title : notice.Title) + "»?" +
                                     (string.IsNullOrEmpty(limit) ? string.Empty : " " + limit + "."), () =>
                {
                    bool ok = NoticeBoardService.TryAccept(gameState, settlementLocationId, notice.Id, out string accepted);
                    boardMessageText = accepted;
                    if (ok)
                    {
                        AddReport(accepted);
                        Autosave();
                    }
                    RenderSettlementBoard();
                });
                return;
            case NoticeActionKind.RecordKnowledge:
                if (NoticeBoardService.TryRecordKnowledge(gameState, settlementLocationId, notice.Id, out message))
                {
                    AddReport(message);
                    Autosave();
                }
                boardMessageText = message;
                RenderSettlementBoard();
                return;
            case NoticeActionKind.ShowOnMap:
                LocationData location = gameState.FindLocation(notice.LocationId);
                boardMessageText = location != null
                    ? "«" + location.Name + "» отмечено на карте — посмотрите, когда выйдете из поселения."
                    : "Это место пока неизвестно.";
                if (location != null)
                {
                    float x = location.MapXPercent;
                    float y = location.MapYPercent;
                    interfaceRoot.schedule.Execute(() => CenterWorldMapOn(x, y)).ExecuteLater(1);
                }
                RenderSettlementBoard();
                return;
            case NoticeActionKind.ContactPerson:
                // Разговор — поверх доски; после него та же доска.
                string dialogueId = NoticeBoardService.ContactDialogueId(gameState, settlementLocationId, notice);
                StartSettlementDialogue(dialogueId);
                return;
            case NoticeActionKind.OpenTrade:
                OpenSettlementTrade(notice.MerchantId);
                return;
        }
    }
}
