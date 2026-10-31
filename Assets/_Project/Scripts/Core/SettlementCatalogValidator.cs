using System;
using System.Collections.Generic;

// 12Т: проверка «Базы поселений» до запуска: дубли ID, битые ссылки на
// диалоги, вещи, торговцев, поручения и места, несовместимые действия и
// цены с немедленной прибылью. Строки показываются рядом с записью.
public static class SettlementCatalogValidator
{
    public sealed class Context
    {
        public Func<string, bool> DialogueExists = _ => true;
        public Func<string, bool> SpeakerExists = _ => true;
        public Func<string, bool> LocalLocationExists = _ => true;
        public Func<string, bool> ItemExists = id => ItemCatalog.Find(id) != null;
        public Func<string, ErrandDefinition> FindErrand = _ => null;
    }

    public static List<string> ValidateAll(IEnumerable<SettlementProfileDefinition> profiles, IEnumerable<ErrandDefinition> errands, Context context)
    {
        List<string> problems = new List<string>();
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string> boundBy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (profiles != null)
        {
            foreach (SettlementProfileDefinition profile in profiles)
            {
                if (profile == null)
                    continue;
                if (!string.IsNullOrEmpty(profile.Id) && !ids.Add(profile.Id))
                    problems.Add("Профиль «" + profile.Id + "» повторяется.");
                foreach (string problem in ValidateProfile(profile, context))
                    problems.Add((string.IsNullOrEmpty(profile.Name) ? profile.Id : profile.Name) + ": " + problem);
                if (profile.BoundLocationIds == null)
                    continue;
                foreach (string locationId in profile.BoundLocationIds)
                {
                    if (string.IsNullOrWhiteSpace(locationId))
                        continue;
                    if (boundBy.TryGetValue(locationId, out string other))
                        problems.Add("Место «" + locationId + "» привязано и к «" + other + "», и к «" + profile.Id + "».");
                    else
                        boundBy[locationId] = profile.Id;
                }
            }
        }
        ids.Clear();
        if (errands != null)
        {
            foreach (ErrandDefinition errand in errands)
            {
                if (errand == null)
                    continue;
                if (string.IsNullOrWhiteSpace(errand.Id))
                    problems.Add("Поручение без ID.");
                else if (!ids.Add(errand.Id))
                    problems.Add("Поручение «" + errand.Id + "» повторяется.");
                if (string.IsNullOrWhiteSpace(errand.AcceptedFlag))
                    problems.Add("Поручение «" + errand.Id + "»: нет флага принятия.");
                if (string.IsNullOrWhiteSpace(errand.CompletedFlag))
                    problems.Add("Поручение «" + errand.Id + "»: нет флага выполнения — его нельзя закончить.");
                if (string.IsNullOrWhiteSpace(errand.Title))
                    problems.Add("Поручение «" + errand.Id + "»: нет названия.");
                if (errand.CompletionLimitHours < 0)
                    problems.Add("Поручение «" + errand.Id + "»: срок меньше нуля.");
            }
        }
        return problems;
    }

    public static List<string> ValidateProfile(SettlementProfileDefinition profile, Context context)
    {
        List<string> problems = new List<string>();
        if (string.IsNullOrWhiteSpace(profile.Id))
            problems.Add("нет ID.");
        if (string.IsNullOrWhiteSpace(profile.Name))
            problems.Add("нет названия.");
        if (profile.AllowGenerated && profile.GenerationWeight <= 0)
            problems.Add("шаблон генератора с весом 0 — не выпадет никогда.");

        HashSet<string> personIds = new HashSet<string>(StringComparer.Ordinal);
        void CheckPerson(SettlementPersonDefinition person, string where)
        {
            if (person == null)
                return;
            if (string.IsNullOrWhiteSpace(person.Id))
            {
                problems.Add(where + ": человек без ID.");
                return;
            }
            if (!personIds.Add(person.Id))
                problems.Add(where + ": человек «" + person.Id + "» повторяется.");
            if (!string.IsNullOrEmpty(person.DialogueId) && !context.DialogueExists(person.DialogueId))
                problems.Add(where + " «" + person.DisplayName + "»: нет диалога «" + person.DialogueId + "».");
            if (!string.IsNullOrEmpty(person.SpeakerId) && !context.SpeakerExists(person.SpeakerId))
                problems.Add(where + " «" + person.DisplayName + "»: нет говорящего «" + person.SpeakerId + "».");
            if (!string.IsNullOrEmpty(person.MerchantId) && profile.FindMerchant(person.MerchantId) == null)
                problems.Add(where + " «" + person.DisplayName + "»: нет торговца «" + person.MerchantId + "».");
        }

        if (profile.People != null)
        {
            foreach (SettlementPersonDefinition person in profile.People)
                CheckPerson(person, "Люди");
        }

        HashSet<string> merchantIds = new HashSet<string>(StringComparer.Ordinal);
        if (profile.Merchants != null)
        {
            foreach (MerchantDefinition merchant in profile.Merchants)
            {
                if (merchant == null)
                    continue;
                if (string.IsNullOrWhiteSpace(merchant.Id) || !merchantIds.Add(merchant.Id))
                    problems.Add("торговец без ID или с повторяющимся ID «" + merchant.Id + "».");
                if (merchant.SellPercent >= merchant.BuyPercent)
                    problems.Add("торговец «" + merchant.DisplayName + "»: продажа ему (" + merchant.SellPercent +
                                 "%) не дешевле покупки у него (" + merchant.BuyPercent + "%) — перепродажа даст прибыль.");
                if (merchant.BuyPercent <= 0 || merchant.SellPercent < 0)
                    problems.Add("торговец «" + merchant.DisplayName + "»: проценты цен должны быть положительными.");
                if (merchant.StartMoney < 0 || merchant.MoneyTarget < 0)
                    problems.Add("торговец «" + merchant.DisplayName + "»: деньги меньше нуля.");
                if (merchant.RefreshHours < 0)
                    problems.Add("торговец «" + merchant.DisplayName + "»: интервал обновления меньше нуля.");
                if (merchant.Stock == null)
                    continue;
                int supplies = 0;
                HashSet<string> stockIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (MerchantStockDefinition line in merchant.Stock)
                {
                    if (line == null)
                        continue;
                    if (line.IsSupplies)
                    {
                        supplies++;
                        if (line.SupplyUnitPrice <= 0)
                            problems.Add("торговец «" + merchant.DisplayName + "»: цена припасов должна быть больше нуля.");
                    }
                    else if (string.IsNullOrEmpty(line.ItemId) || !context.ItemExists(line.ItemId))
                        problems.Add("торговец «" + merchant.DisplayName + "»: нет вещи «" + line.ItemId + "» в Базе предметов.");
                    else if (!stockIds.Add(line.ItemId))
                        problems.Add("торговец «" + merchant.DisplayName + "»: вещь «" + line.ItemId + "» указана дважды.");
                    else
                    {
                        ItemDefinition item = ItemCatalog.Find(line.ItemId);
                        if (item != null && (item.IsTradeProtected || !item.CanBuy))
                            problems.Add("торговец «" + merchant.DisplayName + "»: «" + item.Name + "» не продаётся (сюжетная/защищена/нельзя купить).");
                        if (item != null && item.Price <= 0)
                            problems.Add("торговец «" + merchant.DisplayName + "»: у «" + item.Name + "» нет цены.");
                    }
                    if (line.TargetCount < 0)
                        problems.Add("торговец «" + merchant.DisplayName + "»: запас меньше нуля.");
                }
                if (supplies > 1)
                    problems.Add("торговец «" + merchant.DisplayName + "»: строк припасов больше одной.");
            }
        }

        TavernDefinition tavern = profile.Tavern;
        if (tavern != null && tavern.Enabled)
        {
            if (string.IsNullOrWhiteSpace(tavern.Name))
                problems.Add("корчма без названия.");
            CheckPerson(tavern.Keeper, "Хозяин корчмы");
            if (tavern.Visitors != null)
            {
                foreach (SettlementPersonDefinition visitor in tavern.Visitors)
                    CheckPerson(visitor, "Посетитель");
            }
            if (tavern.Offers != null)
            {
                HashSet<string> offerIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (TavernOfferDefinition offer in tavern.Offers)
                {
                    if (offer == null)
                        continue;
                    if (string.IsNullOrWhiteSpace(offer.Id) || !offerIds.Add(offer.Id))
                        problems.Add("предложение корчмы без ID или с повтором «" + offer.Id + "».");
                    if (offer.Price < 0)
                        problems.Add("предложение «" + offer.Label + "»: цена меньше нуля.");
                    if (!string.IsNullOrEmpty(offer.PersonId) && profile.FindPerson(offer.PersonId) == null)
                        problems.Add("предложение «" + offer.Label + "»: нет человека «" + offer.PersonId + "».");
                    if (!string.IsNullOrEmpty(offer.DialogueId) && !context.DialogueExists(offer.DialogueId))
                        problems.Add("предложение «" + offer.Label + "»: нет диалога «" + offer.DialogueId + "».");
                    if (!string.IsNullOrEmpty(offer.ErrandId) && context.FindErrand(offer.ErrandId) == null)
                        problems.Add("предложение «" + offer.Label + "»: нет поручения «" + offer.ErrandId + "».");
                    if (string.IsNullOrEmpty(offer.KnowledgeId) && string.IsNullOrEmpty(offer.ErrandId) &&
                        string.IsNullOrEmpty(offer.SetFlagId) && string.IsNullOrEmpty(offer.DialogueId))
                        problems.Add("предложение «" + offer.Label + "» ничего не даёт.");
                    if (!offer.Once && offer.CooldownHours <= 0 && offer.Price > 0)
                        problems.Add("предложение «" + offer.Label + "»: повторяемое платное без перерыва.");
                }
            }
            if (tavern.Lodging != null && tavern.Lodging.Enabled)
            {
                if (tavern.Lodging.Hours <= 0)
                    problems.Add("ночлег: длительность должна быть больше нуля.");
                if (tavern.Lodging.Price < 0 || tavern.Lodging.HealHitPoints < 0)
                    problems.Add("ночлег: цена и лечение не могут быть меньше нуля.");
            }
        }

        NoticeBoardDefinition board = profile.Board;
        if (board != null && board.Enabled && board.Notices != null)
        {
            HashSet<string> noticeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (NoticeDefinition notice in board.Notices)
            {
                if (notice == null)
                    continue;
                string name = "объявление «" + (string.IsNullOrEmpty(notice.Title) ? notice.Id : notice.Title) + "»";
                if (string.IsNullOrWhiteSpace(notice.Id) || !noticeIds.Add(notice.Id))
                    problems.Add(name + ": нет ID или ID повторяется.");
                if (string.IsNullOrWhiteSpace(notice.Title) || string.IsNullOrWhiteSpace(notice.Text))
                    problems.Add(name + ": нужны заголовок и текст.");
                if (!string.IsNullOrEmpty(notice.ErrandId) && context.FindErrand(notice.ErrandId) == null)
                    problems.Add(name + ": нет поручения «" + notice.ErrandId + "».");
                if (!string.IsNullOrEmpty(notice.DialogueId) && !context.DialogueExists(notice.DialogueId))
                    problems.Add(name + ": нет диалога «" + notice.DialogueId + "».");
                if (!string.IsNullOrEmpty(notice.ContactPersonId) && profile.FindPerson(notice.ContactPersonId) == null)
                    problems.Add(name + ": нет человека «" + notice.ContactPersonId + "» в этом поселении.");
                if (!string.IsNullOrEmpty(notice.MerchantId) && profile.FindMerchant(notice.MerchantId) == null)
                    problems.Add(name + ": нет торговца «" + notice.MerchantId + "».");
                if (notice.LifetimeHours < 0 || notice.RepostAfterHours < 0)
                    problems.Add(name + ": сроки не могут быть меньше нуля.");
                if (notice.RepostAfterHours > 0 && notice.LifetimeHours <= 0)
                    problems.Add(name + ": повтор без срока размещения не имеет смысла.");
            }
        }

        if (profile.Actions != null)
        {
            HashSet<string> actionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SettlementActionDefinition action in profile.Actions)
            {
                if (action == null)
                    continue;
                string name = "действие «" + (string.IsNullOrEmpty(action.Label) ? action.Id : action.Label) + "»";
                if (string.IsNullOrWhiteSpace(action.Id) || !actionIds.Add(action.Id))
                    problems.Add(name + ": нет ID или ID повторяется.");
                if (string.IsNullOrWhiteSpace(action.Label))
                    problems.Add(name + ": нет подписи кнопки.");
                switch (action.Type)
                {
                    case SettlementActionType.Dialogue:
                        if (string.IsNullOrEmpty(action.TargetId) || !context.DialogueExists(action.TargetId))
                            problems.Add(name + ": нет диалога «" + action.TargetId + "».");
                        break;
                    case SettlementActionType.Trade:
                        if (profile.FindMerchant(action.TargetId) == null)
                            problems.Add(name + ": нет торговца «" + action.TargetId + "».");
                        break;
                    case SettlementActionType.Tavern:
                        if (tavern == null || !tavern.Enabled)
                            problems.Add(name + ": корчма не включена.");
                        break;
                    case SettlementActionType.Rest:
                        if (tavern == null || !tavern.Enabled || tavern.Lodging == null || !tavern.Lodging.Enabled)
                            problems.Add(name + ": ночлег корчмы не включён.");
                        break;
                    case SettlementActionType.Healer:
                        if (action.Price < 0)
                            problems.Add(name + ": отрицательная цена знахаря.");
                        break;
                    case SettlementActionType.LocalMap:
                        if (string.IsNullOrEmpty(action.TargetId) || !context.LocalLocationExists(action.TargetId))
                            problems.Add(name + ": нет исследуемого места «" + action.TargetId + "».");
                        break;
                    case SettlementActionType.NoticeBoard:
                        if (board == null || !board.Enabled)
                            problems.Add(name + ": доска не включена.");
                        break;
                }
            }
        }

        if (profile.Variants != null)
        {
            HashSet<string> variantIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SettlementVariantDefinition variant in profile.Variants)
            {
                if (variant != null && (string.IsNullOrWhiteSpace(variant.Id) || !variantIds.Add(variant.Id)))
                    problems.Add("вариант без ID или с повтором «" + variant.Id + "».");
            }
        }
        return problems;
    }
}
