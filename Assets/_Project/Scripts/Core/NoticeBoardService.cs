using System;
using System.Collections.Generic;

// 12Т (дополнение «Доска объявлений»): доска поселения поверх существующих
// систем — поручения (ErrandService), диалоги, знания, карта и Хроника. Чтение
// само ничего не принимает и не выдаёт; действия — только подключённые.
// Прочитанное, сроки размещения и одноразовые выдачи живут в состоянии
// экземпляра поселения; вход и загрузка ничего не обновляют сами.
public enum NoticeActionKind
{
    AcceptErrand,
    RecordKnowledge,
    ShowOnMap,
    ContactPerson,
    OpenTrade
}

public sealed class NoticeActionView
{
    public NoticeActionKind Kind;
    public string Label = string.Empty;
    public bool Enabled = true;
    public string Reason = string.Empty;
}

public sealed class NoticeView
{
    public NoticeDefinition Notice;
    public bool Read;
    public ErrandDefinition Errand;
    public ErrandStatus ErrandStatus;
    public string StatusText = string.Empty;
    public double HoursLeft = -1.0;
    public readonly List<NoticeActionView> Actions = new List<NoticeActionView>();
}

public static class NoticeBoardService
{
    public static NoticeBoardDefinition Board(GameState state, string locationId)
    {
        NoticeBoardDefinition board = SettlementService.Profile(state, locationId)?.Board;
        return board != null && board.Enabled ? board : null;
    }

    public static string TypeLabel(NoticeType type)
    {
        switch (type)
        {
            case NoticeType.Request: return "просьба";
            case NoticeType.Order: return "заказ";
            case NoticeType.Warning: return "предупреждение";
            case NoticeType.Wanted: return "розыск";
            case NoticeType.TradeOffer: return "торг";
            default: return "новость";
        }
    }

    public static string MediumLabel(NoticeMedium medium)
    {
        switch (medium)
        {
            case NoticeMedium.BirchBark: return "береста";
            case NoticeMedium.WoodenTablet: return "дощечка";
            case NoticeMedium.Sign: return "знак";
            default: return "со слов";
        }
    }

    private static NoticeStateData Record(GameState state, string locationId, string noticeId, bool create)
    {
        SettlementInstanceData instance = SettlementService.FindInstance(state, locationId);
        if (instance == null)
            return null;
        NoticeStateData record = instance.Notices.Find(n => n != null && n.NoticeId == noticeId);
        if (record == null && create)
        {
            record = new NoticeStateData { NoticeId = noticeId };
            instance.Notices.Add(record);
        }
        return record;
    }

    // Висит ли объявление сейчас. Первый показ отмечает начало размещения
    // (это и есть «вывесили, когда отряд увидел»), дальше — только часы.
    private static bool IsPosted(GameState state, string locationId, NoticeDefinition notice, double now,
        NarrativeEvaluationContext context, out double hoursLeft)
    {
        hoursLeft = -1.0;
        if (!(notice.ShowConditions?.Evaluate(context) ?? true))
            return false;
        if (notice.HideConditions != null && notice.HideConditions.Conditions != null &&
            notice.HideConditions.Conditions.Count > 0 && notice.HideConditions.Evaluate(context))
            return false;
        if (notice.LifetimeHours <= 0)
            return true;

        NoticeStateData record = Record(state, locationId, notice.Id, true);
        if (record.PostedHour < 0)
        {
            record.PostedHour = now;
            record.Postings = 1;
        }
        double end = record.PostedHour + notice.LifetimeHours;
        if (now < end)
        {
            hoursLeft = end - now;
            return true;
        }
        if (notice.RepostAfterHours > 0 && now >= end + notice.RepostAfterHours)
        {
            // Новое размещение: прежнее прочитанное — уже другое объявление.
            record.PostedHour = now;
            record.Postings++;
            record.Read = false;
            hoursLeft = notice.LifetimeHours;
            return true;
        }
        return false;
    }

    public static List<NoticeView> Notices(GameState state, string locationId)
    {
        List<NoticeView> result = new List<NoticeView>();
        NoticeBoardDefinition board = Board(state, locationId);
        if (board?.Notices == null)
            return result;
        double now = ContinuousSimulationSystem.GetAbsoluteHours(state);
        NarrativeEvaluationContext context = SettlementService.Context(state);
        SettlementProfileDefinition profile = SettlementService.Profile(state, locationId);
        foreach (NoticeDefinition notice in board.Notices)
        {
            if (notice == null || string.IsNullOrEmpty(notice.Id))
                continue;
            if (!IsPosted(state, locationId, notice, now, context, out double hoursLeft))
                continue;
            // Бриф правок пп. 16, 22: объявление поручения — только там, где
            // оно закреплено, и пока поручение не выполнено и не провалено.
            if (!string.IsNullOrEmpty(notice.ErrandId))
            {
                ErrandStatus errandStatus = ErrandService.Status(state, SettlementCatalog.Current.FindErrand(notice.ErrandId));
                if (!ErrandClaims.AvailableAt(state, notice.ErrandId, locationId) ||
                    errandStatus == ErrandStatus.Completed || errandStatus == ErrandStatus.Failed)
                    continue;
            }
            NoticeView view = new NoticeView
            {
                Notice = notice,
                Read = Record(state, locationId, notice.Id, false)?.Read ?? false,
                HoursLeft = hoursLeft,
                Errand = SettlementCatalog.Current.FindErrand(notice.ErrandId)
            };
            view.ErrandStatus = ErrandService.Status(state, view.Errand);
            if (view.Errand != null)
                view.StatusText = "Поручение " + ErrandService.StatusLabel(view.ErrandStatus);
            AddActions(state, locationId, profile, notice, view, context);
            result.Add(view);
        }
        return result;
    }

    private static void AddActions(GameState state, string locationId, SettlementProfileDefinition profile,
        NoticeDefinition notice, NoticeView view, NarrativeEvaluationContext context)
    {
        if (view.Errand != null && view.ErrandStatus == ErrandStatus.NotTaken)
            view.Actions.Add(new NoticeActionView { Kind = NoticeActionKind.AcceptErrand, Label = "Принять задание" });

        if (!string.IsNullOrEmpty(notice.KnowledgeId))
        {
            bool known = state.Narrative != null && state.Narrative.HasKnowledge(notice.KnowledgeId);
            view.Actions.Add(new NoticeActionView
            {
                Kind = NoticeActionKind.RecordKnowledge,
                Label = "Отметить сведения в Хронике",
                Enabled = !known,
                Reason = known ? "Уже отмечено." : string.Empty
            });
        }

        LocationData location = string.IsNullOrEmpty(notice.LocationId) ? null : state.FindLocation(notice.LocationId);
        if (location != null && location.IsVisibleOnMap && location.IsDiscovered)
            view.Actions.Add(new NoticeActionView { Kind = NoticeActionKind.ShowOnMap, Label = "Показать на карте" });

        // Заказчик в этом поселении и с ним есть разговор.
        SettlementPersonDefinition contact = profile?.FindPerson(notice.ContactPersonId);
        string dialogueId = !string.IsNullOrEmpty(notice.DialogueId) ? notice.DialogueId : contact?.DialogueId;
        bool contactHere = contact == null || (contact.Conditions?.Evaluate(context) ?? true);
        if (!string.IsNullOrEmpty(dialogueId) && contactHere)
        {
            view.Actions.Add(new NoticeActionView
            {
                Kind = NoticeActionKind.ContactPerson,
                Label = "Обратиться к заказчику"
            });
        }

        if (!string.IsNullOrEmpty(notice.MerchantId) && profile?.FindMerchant(notice.MerchantId) != null)
            view.Actions.Add(new NoticeActionView { Kind = NoticeActionKind.OpenTrade, Label = "К торговцу" });
    }

    public static void MarkRead(GameState state, string locationId, string noticeId)
    {
        NoticeStateData record = Record(state, locationId, noticeId, true);
        if (record != null)
            record.Read = true;
    }

    public static int UnreadCount(GameState state, string locationId)
    {
        int count = 0;
        foreach (NoticeView view in Notices(state, locationId))
        {
            if (!view.Read)
                count++;
        }
        return count;
    }

    // Диалог заказчика для действия «Обратиться».
    public static string ContactDialogueId(GameState state, string locationId, NoticeDefinition notice)
    {
        if (notice == null)
            return string.Empty;
        if (!string.IsNullOrEmpty(notice.DialogueId))
            return notice.DialogueId;
        SettlementPersonDefinition contact = SettlementService.Profile(state, locationId)?.FindPerson(notice.ContactPersonId);
        return contact?.DialogueId ?? string.Empty;
    }

    // Принять задание с доски — то же поручение, что в корчме и в разговоре.
    public static bool TryAccept(GameState state, string locationId, string noticeId, out string message)
    {
        NoticeDefinition notice = FindVisible(state, locationId, noticeId, out message);
        if (notice == null)
            return false;
        LocationData location = state.FindLocation(locationId);
        return ErrandService.TryAccept(state, notice.ErrandId,
            "объявление «" + notice.Title + "»" + (location != null ? ", " + location.Name : string.Empty), out message);
    }

    // Отметить сведения: знание и одна запись Хроники с источником.
    public static bool TryRecordKnowledge(GameState state, string locationId, string noticeId, out string message)
    {
        NoticeDefinition notice = FindVisible(state, locationId, noticeId, out message);
        if (notice == null)
            return false;
        if (string.IsNullOrEmpty(notice.KnowledgeId))
        {
            message = "В объявлении нет сведений.";
            return false;
        }
        state.Narrative ??= new NarrativeStateData();
        if (state.Narrative.HasKnowledge(notice.KnowledgeId))
        {
            message = "Это уже отмечено.";
            return false;
        }
        CommanderData commander = state.GetSelectedCommander();
        if (commander != null)
        {
            commander.HeroProfile ??= new HeroProfileData();
            new NarrativeEffect
            {
                EffectExecutionId = "notice.knowledge." + locationId + "." + notice.Id,
                Type = NarrativeEffectType.AddKnowledge,
                StringParam = notice.KnowledgeId
            }.Apply(new NarrativeEvaluationContext(commander.HeroProfile, state.Narrative, gameState: state));
        }
        state.Narrative.AddKnowledge(notice.KnowledgeId);
        LocationData location = state.FindLocation(locationId);
        string text = string.IsNullOrWhiteSpace(notice.KnowledgeText) ? notice.Text : notice.KnowledgeText;
        Chronicle.Record(state, "knowledge.source." + notice.KnowledgeId, notice.Title,
            text + " (" + MediumLabel(notice.Medium) + (location != null ? ", " + location.Name : string.Empty) + ")",
            string.IsNullOrEmpty(notice.LocationId) ? null : notice.LocationId);
        message = "Отмечено в Хронике: " + notice.Title + ".";
        return true;
    }

    private static NoticeDefinition FindVisible(GameState state, string locationId, string noticeId, out string message)
    {
        if (!SettlementService.CanActInside(state, locationId, out message))
            return null;
        foreach (NoticeView view in Notices(state, locationId))
        {
            if (view.Notice.Id == noticeId)
                return view.Notice;
        }
        message = "Этого объявления уже нет.";
        return null;
    }
}
