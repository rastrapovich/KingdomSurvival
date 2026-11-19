using System.Collections.Generic;

// Метки карты мира (бриф «Метки мест и доступных квестов», 19.11.2026):
//   ? — место, о котором игрок уже знает (видно на карте), но ещё не исследовал;
//   ! — в известном месте сейчас можно взять хотя бы одно новое поручение.
// Одно вычисление для значка, подсказки и карточки. Знание, исследование и
// поручения принадлежат существующим системам: видимость — LocationData
// (туман, слух, события уже решили IsVisibleOnMap), исследование — флаги
// места / поселения / исследуемого места, предложения — те же фильтры, что у
// корчмы (TavernService.Offers/CanTake) и доски (NoticeBoardService.Notices с
// действием «Принять задание»). Своего состояния сервис не хранит и ничего не
// меняет: доска читается без «вывешивания», корчма — без требования «отряд
// внутри» (метка видна издалека; само действие по-прежнему внутри поселения).
public enum WorldMapMarkerKind
{
    None,
    UnknownPlace,
    QuestAvailable
}

public readonly struct WorldMapMarkerInfo
{
    public readonly WorldMapMarkerKind Kind;
    // Место не исследовано (у «!» тоже — для подсказки).
    public readonly bool Unexplored;
    // Сколько новых поручений можно взять (0 — нет).
    public readonly int QuestOffers;

    public WorldMapMarkerInfo(WorldMapMarkerKind kind, bool unexplored, int questOffers)
    {
        Kind = kind;
        Unexplored = unexplored;
        QuestOffers = questOffers;
    }

    public static readonly WorldMapMarkerInfo None = new WorldMapMarkerInfo(WorldMapMarkerKind.None, false, 0);

    // Подсказка метки: без скрытых имён и деталей поручений.
    public string Hint
    {
        get
        {
            List<string> lines = new List<string>();
            if (QuestOffers > 0)
                lines.Add(QuestOffers > 1 ? "Можно взять задание (" + QuestOffers + ")." : "Можно взять задание.");
            if (Unexplored)
                lines.Add("Место не исследовано.");
            return string.Join(" ", lines);
        }
    }
}

public static class WorldMapMarkerService
{
    public static WorldMapMarkerInfo For(GameState state, LocationData location)
    {
        if (state == null || location == null || location.IsWaypoint || !location.IsVisibleOnMap)
            return WorldMapMarkerInfo.None;
        bool unexplored = IsUnexplored(state, location);
        int offers = AvailableQuestOffers(state, location.Id);
        // Оба основания — «!» с приоритетом, подсказка говорит и о неисследованном.
        WorldMapMarkerKind kind = offers > 0 ? WorldMapMarkerKind.QuestAvailable
            : unexplored ? WorldMapMarkerKind.UnknownPlace
            : WorldMapMarkerKind.None;
        return new WorldMapMarkerInfo(kind, unexplored, offers);
    }

    // Известное игроку и ещё не исследованное место: у него есть действие
    // исследования, и оно не выполнено. Поселение — по своему признаку
    // посещения (вход в поселение), исследуемое место — по посещению места.
    public static bool IsUnexplored(GameState state, LocationData location)
    {
        if (state == null || location == null || location.IsWaypoint || !location.IsVisibleOnMap)
            return false;
        if (location.IsSettlement)
        {
            SettlementInstanceData instance = SettlementService.FindInstance(state, location.Id);
            if (instance != null)
                return !instance.Visited;
            return !location.IsExplored && location.ExplorationHours > 0;
        }
        if (location.IsExplored)
            return false;
        LocalLocationDefinition local = LocalLocationCatalog.ForWorldLocation(state, location.Id);
        if (local != null)
            return LocalExplorationService.Find(state, local.Id)?.Visited != true;
        return location.ExplorationHours > 0;
    }

    // Новые поручения, которые можно взять здесь сейчас: корчма и доска,
    // каждое поручение — один раз. Скрытые условиями предложения не видны.
    public static int AvailableQuestOffers(GameState state, string locationId)
    {
        HashSet<string> errands = TavernQuestErrands(state, locationId);
        errands.UnionWith(BoardQuestErrands(state, locationId));
        return errands.Count;
    }

    // Поручения, которые сейчас можно взять в корчме поселения — метка «!»
    // над зданием корчмы на живой карте (общего входа в поселение там нет).
    public static HashSet<string> TavernQuestErrands(GameState state, string locationId)
    {
        HashSet<string> errands = new HashSet<string>();
        if (!IsQuestSettlement(state, locationId))
            return errands;
        foreach (TavernOfferView view in TavernService.Offers(state, locationId))
        {
            TavernOfferDefinition offer = view?.Offer;
            if (offer == null || string.IsNullOrEmpty(offer.ErrandId))
                continue;
            if (TavernService.CanTake(state, locationId, offer, out _, ignorePresence: true))
                errands.Add(offer.ErrandId);
        }
        return errands;
    }

    // Поручения, которые сейчас можно принять с доски объявлений — метка «!»
    // над столбом объявлений. Доска читается без вывешивания.
    public static HashSet<string> BoardQuestErrands(GameState state, string locationId)
    {
        HashSet<string> errands = new HashSet<string>();
        if (!IsQuestSettlement(state, locationId))
            return errands;
        foreach (NoticeView view in NoticeBoardService.Notices(state, locationId, readOnly: true))
        {
            if (view?.Notice == null || string.IsNullOrEmpty(view.Notice.ErrandId))
                continue;
            if (view.Actions.Exists(a => a.Kind == NoticeActionKind.AcceptErrand && a.Enabled))
                errands.Add(view.Notice.ErrandId);
        }
        return errands;
    }

    private static bool IsQuestSettlement(GameState state, string locationId)
    {
        if (state == null || string.IsNullOrEmpty(locationId))
            return false;
        LocationData location = state.FindLocation(locationId);
        return location != null && location.IsSettlement && location.IsVisibleOnMap &&
               SettlementService.Profile(state, locationId) != null;
    }
}
