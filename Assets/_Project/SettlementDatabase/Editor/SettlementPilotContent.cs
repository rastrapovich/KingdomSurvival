using System.Collections.Generic;

namespace KingdomSurvival.SettlementDatabase.Editor
{
    // 12Т: рабочий пример для проверки (SETTLEMENTS_TRADE_TAVERN_SPEC.md §14) —
    // три профиля с разными возможностями и два общих поручения. Это
    // [РАБОЧЕЕ] содержание: имена, цены и тексты не утверждают ни географии,
    // ни государства, ни фактов мира. Засев только добавляет недостающие
    // записи; после засева всё правится в «Базе поселений».
    public static class SettlementPilotContent
    {
        public const string VillageId = "settlement.village.road";
        public const string TradingPostId = "settlement.trading.post";
        public const string TownId = "settlement.town.fort";
        public const string CarterErrandId = "settlement.errand.carter_knife";
        public const string TownErrandId = "settlement.errand.town_message";

        private const string LeaderKilled = "freeplay.coal.leader_killed";

        // Говорящие пилота (ID, имя, роль) — добавляются в Базу диалогов.
        public static readonly (string Id, string Name, string Role)[] Speakers =
        {
            ("village_elder", "Вышата", "Староста деревни у дороги"),
            ("village_hostess", "Мокрина", "Хозяйка избы у дороги"),
            ("trade_keeper", "Стоян", "Корчмарь у торга"),
            ("carter", "Ждан", "Возчик"),
            ("peddler", "Гостята", "Проезжий торговец"),
            ("town_hostess", "Ярина", "Хозяйка корчмы у ворот"),
            ("town_milana", "Милана", "Жена сторожа")
        };

        public static List<SettlementProfileEntry> Profiles()
        {
            return new List<SettlementProfileEntry> { Village(), TradingPost(), Town() };
        }

        public static List<ErrandDefinition> Errands()
        {
            return new List<ErrandDefinition>
            {
                new ErrandDefinition
                {
                    Id = CarterErrandId,
                    Title = "Нож деда",
                    Summary = "Возчик Ждан из торгового села просит найти тайник деда у старого русла и принести нож с медной насечкой.",
                    GiverName = "возчик Ждан",
                    AcceptedFlag = "freeplay.cache.errand.accepted",
                    CompletedFlag = "freeplay.cache.errand.done",
                    AcceptedStepText = "Узнать у Ждана, где ива, найти тайник у старого русла и принести нож.",
                    Stages = new List<ErrandStageDefinition>
                    {
                        new ErrandStageDefinition
                        {
                            Flag = ItemCatalog.OldBedCacheStageFlag,
                            Text = "Ива — у старого русла: область поиска отмечена на карте. Найти расколотую иву с тремя зарубками."
                        },
                        new ErrandStageDefinition
                        {
                            Flag = "freeplay.cache.found",
                            Text = "Тайник найден. Отнести нож Ждану в корчму торгового села."
                        }
                    },
                    CompletedText = "Ждан получил дедов нож и заплатил от себя.",
                    RewardText = "серебро из тайника и благодарность Ждана"
                },
                new ErrandDefinition
                {
                    Id = TownErrandId,
                    Title = "Весть старосте",
                    Summary = "Милана, жена сторожа городка, просит передать старосте деревни у дороги весть о сыне.",
                    GiverName = "Милана, жена сторожа",
                    AcceptedFlag = "settlement.town.message.accepted",
                    CompletedFlag = "settlement.town.message.done",
                    FailedFlag = "settlement.town.message.failed",
                    AcceptedStepText = "Дойти до деревни у дороги и передать весть старосте Вышате.",
                    Stages = new List<ErrandStageDefinition>
                    {
                        new ErrandStageDefinition
                        {
                            Flag = "settlement.town.message.delivered",
                            Text = "Весть передана. Вернуться к Милане в корчму городка за платой."
                        }
                    },
                    CompletionLimitHours = 168,
                    CompletedText = "Милана заплатила за весть, как обещала.",
                    RewardText = "8 зол. по возвращении"
                }
            };
        }

        // ------------------------------------------------------------------

        private static SettlementProfileEntry Village()
        {
            SettlementProfileDefinition p = new SettlementProfileDefinition
            {
                Id = VillageId,
                Name = "Деревня у дороги",
                Kind = SettlementKind.Village,
                ShortDescription = "Десяток дворов за плетнём, колодец с журавлём, у околицы — изба для проезжих. Собаки лают больше для порядка.",
                LongDescription = "Деревня живёт с дороги: проезжим продают щи, сено и советы, причём советы дороже всего.",
                Tags = new List<string> { "деревня", "дорога" },
                AllowGenerated = true,
                GenerationWeight = 3,
                GuaranteeInGenerated = true
            };
            p.Variants.Add(new SettlementVariantDefinition
            {
                Id = "village.wolf_gone",
                Priority = 10,
                Conditions = Flag(LeaderKilled),
                Description = "Возчики снова ночуют у околицы, и собаки лают уже не на лес, а на них."
            });
            p.People.Add(Person("village_elder", "village_elder", "Вышата", "староста", "settlement_village_elder", "Чинит хомут на завалинке"));

            MerchantDefinition shop = new MerchantDefinition
            {
                Id = "village.shop", DisplayName = "Мокрина", SpeakerId = "village_hostess", Role = "хозяйка избы у дороги",
                BuyPercent = 110, SellPercent = 45, StartMoney = 30, MoneyTarget = 30, RefreshHours = 48
            };
            shop.Stock.Add(new MerchantStockDefinition { IsSupplies = true, TargetCount = 12, SupplyUnitPrice = 2 });
            shop.Stock.Add(Stock(ItemCatalog.DriedFish, 2));
            shop.Stock.Add(Stock(ItemCatalog.TrailRusks, 2));
            shop.Stock.Add(Stock(ItemCatalog.BitterInfusion, 1));
            p.Merchants.Add(shop);

            p.Tavern = new TavernDefinition
            {
                Enabled = true,
                Id = "village.inn",
                Name = "Изба у дороги",
                Description = "Низкая изба с печью по-чёрному. Дым уходит в волоковое окошко, тепло остаётся. На лавках — двое возчиков и чья-то коза.",
                Keeper = Person("village_hostess", "village_hostess", "Мокрина", "хозяйка", "settlement_village_hostess", "Мешает щи в горшке", "village.shop"),
                Lodging = new TavernLodgingDefinition
                {
                    Enabled = true, Price = 3, Hours = 8, HealHitPoints = 2,
                    Description = "Лавка у тёплой стены, щи и хлеб. Утром Мокрина будит всех одним словом."
                }
            };

            p.Board = new NoticeBoardDefinition
            {
                Enabled = true,
                Name = "Столб у колодца",
                Description = "На столбе у колодца — зарубки, лоскуты бересты на гвоздях и пучок перьев неизвестного назначения.",
                ReaderNote = "Читать умеет только Вышата — он и пересказывает, не торопясь."
            };
            p.Board.Notices.Add(new NoticeDefinition
            {
                Id = "notice.village.ford", Type = NoticeType.Warning, Medium = NoticeMedium.Sign,
                Title = "Знак у брода",
                Text = "У столба воткнута палка с тряпицей, вымоченной в грязи по самую верхушку. Вышата поясняет: так здесь помечают брод, где телеги садятся. Через старое русло после дождя — объезжать выше, по камням.",
                AuthorName = "староста Вышата", ContactName = "староста Вышата", ContactPersonId = "village_elder",
                KnowledgeId = "settlement.knowledge.ford_mud",
                KnowledgeText = "Брод через старое русло после дождя вязкий: телеги объезжают выше, по камням."
            });
            p.Board.Notices.Add(new NoticeDefinition
            {
                Id = "notice.village.wolf", Type = NoticeType.Warning, Medium = NoticeMedium.BirchBark,
                Title = "Волк у дороги",
                Text = "На бересте нацарапан волк и три овцы, две из них перечёркнуты. Пастух просит: по темноте к лесу скот не гонять и детей не пускать.",
                AuthorName = "пастух",
                KnowledgeId = "settlement.knowledge.wolf_road",
                KnowledgeText = "У лесной дороги ходит старый волк: режет овец и не боится огня.",
                HideConditions = Flag(LeaderKilled)
            });
            p.Board.Notices.Add(new NoticeDefinition
            {
                Id = "notice.village.wolf_gone", Type = NoticeType.LocalNews, Medium = NoticeMedium.Spoken,
                Title = "Волка больше нет",
                Text = "Вышата, не дожидаясь вопроса: «Волка у дороги уложили. Кто — толком не знают, но возчики теперь ночуют у нас, а не в лесу. И торг бойчее пошёл».",
                AuthorName = "староста Вышата",
                ShowConditions = Flag(LeaderKilled)
            });

            p.Actions.Add(Action("village.trade", SettlementActionType.Trade, "Лавка Мокрины", "Припасы, сухари, отвар.", "village.shop"));
            p.Actions.Add(Action("village.inn", SettlementActionType.Tavern, "Зайти в избу у дороги", "Хозяйка, щи и разговоры."));
            p.Actions.Add(Action("village.board", SettlementActionType.NoticeBoard, "Столб у колодца", "Знаки, береста и то, что перескажет староста."));
            p.Actions.Add(Action("village.rest", SettlementActionType.Rest, "Переночевать в избе", "Ночлег под крышей для тех, кто вошёл."));
            return new SettlementProfileEntry { Data = p };
        }

        private static SettlementProfileEntry TradingPost()
        {
            SettlementProfileDefinition p = new SettlementProfileDefinition
            {
                Id = TradingPostId,
                Name = "Торговое село",
                Kind = SettlementKind.TradingPost,
                ShortDescription = "Торг на площади, где в торговый день больше телег, чем людей. Пахнет дёгтем, рыбой и свежим тёсом.",
                LongDescription = "Сюда свозят воск, шкурки и рыбу, а увозят соль, железо и слухи. Слухи — бесплатно, но с наценкой.",
                Tags = new List<string> { "торг", "дорога" },
                AllowGenerated = true,
                GenerationWeight = 2,
                GuaranteeInGenerated = true
            };
            p.Variants.Add(new SettlementVariantDefinition
            {
                Id = "trade.busy",
                Priority = 10,
                Conditions = Flag(LeaderKilled),
                Description = "Возы стоят до самой дороги: обозы снова ходят без опаски, и торг шумит с рассвета."
            });
            p.People.Add(Person("store_keeper", string.Empty, "Ратша", "лавочник", string.Empty, "Считает связки воска", "trade.store"));

            MerchantDefinition store = new MerchantDefinition
            {
                Id = "trade.store", DisplayName = "Ратша", Role = "лавочник",
                BuyPercent = 100, SellPercent = 50, StartMoney = 60, MoneyTarget = 60, RefreshHours = 72
            };
            store.Stock.Add(new MerchantStockDefinition { IsSupplies = true, TargetCount = 20, SupplyUnitPrice = 2 });
            store.Stock.Add(Stock(ItemCatalog.SalveJar, 2));
            store.Stock.Add(Stock(ItemCatalog.TrailRusks, 3));
            store.Stock.Add(Stock(ItemCatalog.BitterInfusion, 2));
            store.Stock.Add(Stock(ItemCatalog.Knife, 2));
            store.Stock.Add(Stock(ItemCatalog.Axe, 1));
            store.Stock.Add(Stock(ItemCatalog.PaddedCoat, 1));
            store.Stock.Add(Stock(ItemCatalog.Jacket, 1));
            store.Stock.Add(Stock(ItemCatalog.RopeWithHooks, 1));
            p.Merchants.Add(store);

            MerchantDefinition peddler = new MerchantDefinition
            {
                Id = "peddler", DisplayName = "Гостята", SpeakerId = "peddler", Role = "проезжий торговец",
                BuyPercent = 120, SellPercent = 55, StartMoney = 80, MoneyTarget = 80, RefreshHours = 120
            };
            peddler.Stock.Add(new MerchantStockDefinition { ItemId = ItemCatalog.AurochsHorn, TargetCount = 1, Unique = true });
            peddler.Stock.Add(new MerchantStockDefinition { ItemId = ItemCatalog.BirchBarkScheme, TargetCount = 1, Unique = true });
            peddler.Stock.Add(Stock(ItemCatalog.BoarSpear, 1));
            peddler.Stock.Add(Stock(ItemCatalog.ScaleVest, 1));
            p.Merchants.Add(peddler);

            p.Tavern = new TavernDefinition
            {
                Enabled = true,
                Id = "trade.tavern",
                Name = "Корчма у торга",
                Description = "Длинная изба с двумя печами. У одной греются, у другой спорят о цене на воск — спор идёт с прошлой осени.",
                Keeper = Person("trade_keeper", "trade_keeper", "Стоян", "корчмарь", "settlement_trade_keeper", "Протирает кружку рукавом"),
                Lodging = new TavernLodgingDefinition
                {
                    Enabled = true, Price = 5, Hours = 8, HealHitPoints = 3,
                    Description = "Полати над печью, каша с салом. Шумно до полуночи, потом — храп."
                }
            };
            p.Tavern.Visitors.Add(Person("carter", "carter", "Ждан", "возчик", "freeplay_carter", "Сидит боком к столу, бережёт спину"));
            p.Tavern.Visitors.Add(Person("peddler", "peddler", "Гостята", "проезжий торговец", "settlement_trade_peddler", "Перебирает короб", "peddler"));
            p.Tavern.Offers.Add(new TavernOfferDefinition
            {
                Id = "offer.carter.where",
                Label = "Где дед прятал",
                Description = "Ждан расскажет, где у старого русла ива с тремя зарубками.",
                PersonId = "carter",
                Price = 12,
                KnowledgeId = ItemCatalog.OldBedCacheKnowledge,
                StageFlagId = ItemCatalog.OldBedCacheStageFlag,
                KnowledgeText = "Тайник у старого русла, в полудне пути от торгового села: расколотая ива с тремя зарубками.",
                DialogueId = "freeplay_carter_tells"
            });
            p.Tavern.Offers.Add(new TavernOfferDefinition
            {
                Id = "offer.carter.errand",
                Label = "Взяться за поручение Ждана",
                Description = "Найти тайник и принести Ждану дедов нож.",
                PersonId = "carter",
                ErrandId = CarterErrandId
            });

            p.Board = new NoticeBoardDefinition
            {
                Enabled = true,
                Name = "Доска у торга",
                Description = "Дощечки на гвоздях у ворот торга. Грамотных тут двое: дьячок и тот, кто дьячку должен.",
                ReaderNote = "За медяк дьячок прочтёт вслух; за два — с выражением."
            };
            p.Board.Notices.Add(new NoticeDefinition
            {
                Id = "notice.trade.carter", Type = NoticeType.Request, Medium = NoticeMedium.WoodenTablet,
                Title = "Ищу дедов нож",
                Text = "На дощечке выжжены нож с тремя витками на рукояти и ива. Дьячок читает: «Кто найдёт под ивой у старого русла дедово добро — серебро себе, нож мне. Спросить Ждана-возчика в корчме».",
                AuthorName = "возчик Ждан", ContactName = "Ждан, в корчме у торга", ContactPersonId = "carter",
                ErrandId = CarterErrandId,
                RewardText = "серебро из тайника; от Ждана — благодарность",
                HideConditions = Flag("freeplay.cache.errand.done")
            });
            p.Board.Notices.Add(new NoticeDefinition
            {
                Id = "notice.trade.peddler", Type = NoticeType.TradeOffer, Medium = NoticeMedium.BirchBark,
                Title = "Вещи с историей",
                Text = "«Гостята, вещи с историей. Сижу в корчме до…» — дальше береста оторвана. Дьячок предполагает, что до холодов.",
                AuthorName = "проезжий торговец Гостята",
                MerchantId = "peddler",
                LifetimeHours = 120,
                RepostAfterHours = 72
            });
            p.Board.Notices.Add(new NoticeDefinition
            {
                Id = "notice.trade.road_open", Type = NoticeType.LocalNews, Medium = NoticeMedium.Sign,
                Title = "Обозы пошли",
                Text = "На воротах торга снова висит связка колокольцев — знак, что обозы ходят. Говорят, волка у лесной дороги больше нет.",
                ShowConditions = Flag(LeaderKilled)
            });

            p.Actions.Add(Action("trade.store", SettlementActionType.Trade, "Лавка у торга", "Ратша: припасы, мазь, простое снаряжение.", "trade.store"));
            p.Actions.Add(Action("trade.tavern", SettlementActionType.Tavern, "Корчма у торга", "Корчмарь, возчик Ждан, проезжий торговец."));
            p.Actions.Add(Action("trade.board", SettlementActionType.NoticeBoard, "Доска у торга", "Просьбы и предложения; дьячок прочтёт."));
            return new SettlementProfileEntry { Data = p };
        }

        private static SettlementProfileEntry Town()
        {
            SettlementProfileDefinition p = new SettlementProfileDefinition
            {
                Id = TownId,
                Name = "Городок за частоколом",
                Kind = SettlementKind.Town,
                ShortDescription = "Бревенчатый частокол, башня над воротами, в ней — сторож и его кашель. Внутри тесно, зато сухо.",
                LongDescription = "Городок держит кузня и ворота: за железом едут издалека, а у ворот спрашивают, зачем.",
                Tags = new List<string> { "городок", "кузня" },
                AllowGenerated = true,
                GenerationWeight = 1,
                GuaranteeInGenerated = true
            };
            p.People.Add(Person("smith", string.Empty, "Твердята", "кузнец", string.Empty, "Бьёт по железу так, что слышно у ворот", "town.smith"));

            MerchantDefinition smith = new MerchantDefinition
            {
                Id = "town.smith", DisplayName = "Твердята", Role = "кузнец",
                BuyPercent = 105, SellPercent = 50, StartMoney = 100, MoneyTarget = 100, RefreshHours = 96
            };
            smith.Stock.Add(Stock(ItemCatalog.MailShirt, 1));
            smith.Stock.Add(Stock(ItemCatalog.PaddedCoat, 2));
            smith.Stock.Add(Stock(ItemCatalog.Spear, 2));
            smith.Stock.Add(Stock(ItemCatalog.Sword, 1));
            smith.Stock.Add(Stock(ItemCatalog.SwordAndShield, 1));
            smith.Stock.Add(Stock(ItemCatalog.Bow, 1));
            smith.Stock.Add(Stock(ItemCatalog.ScaleVest, 1));
            smith.Stock.Add(Stock(ItemCatalog.SalveJar, 1));
            p.Merchants.Add(smith);

            p.Tavern = new TavernDefinition
            {
                Enabled = true,
                Id = "town.tavern",
                Name = "Корчма у ворот",
                Description = "Сразу за воротами, чтобы стражу было недалеко ходить. На стене — правила, вырезанные ножом; читать их не нужно, хозяйка скажет.",
                Keeper = Person("town_hostess", "town_hostess", "Ярина", "хозяйка", "settlement_town_hostess", "Стоит у печи, руки в боки"),
                Lodging = new TavernLodgingDefinition
                {
                    Enabled = true, Price = 6, Hours = 8, HealHitPoints = 4,
                    Description = "Отдельная клеть с засовом и соломенным тюфяком. За частоколом спится спокойнее."
                }
            };
            p.Tavern.Visitors.Add(Person("milana", "town_milana", "Милана", "жена сторожа", "settlement_town_milana", "Сидит у окна с нетронутой кружкой"));
            p.Tavern.Offers.Add(new TavernOfferDefinition
            {
                Id = "offer.milana.errand",
                Label = "Взяться передать весть",
                Description = "Отнести весть старосте деревни у дороги. Плата — по возвращении.",
                PersonId = "milana",
                ErrandId = TownErrandId
            });

            p.Board = new NoticeBoardDefinition
            {
                Enabled = true,
                Name = "Таблички у ворот",
                Description = "Под воротной башней — доска с табличками. Углём, ножом, а одна — гвоздём: видно, писали в спешке.",
                ReaderNote = "Сторож читает вслух, кашляя на знаках препинания."
            };
            p.Board.Notices.Add(new NoticeDefinition
            {
                Id = "notice.town.message", Type = NoticeType.Order, Medium = NoticeMedium.WoodenTablet,
                Title = "Весть в деревню у дороги",
                Text = "Углём на табличке: «Нужен человек до деревни у дороги — весть старосте. Плата по возвращении. Спросить Милану в корчме». Сторож добавляет от себя: «Жена моя. Не обижайте».",
                AuthorName = "сторож у ворот", ContactName = "Милана, в корчме у ворот", ContactPersonId = "milana",
                ErrandId = TownErrandId,
                RewardText = "8 зол. по возвращении",
                HideConditions = Flag("settlement.town.message.done")
            });
            p.Board.Notices.Add(new NoticeDefinition
            {
                Id = "notice.town.wanted", Type = NoticeType.Wanted, Medium = NoticeMedium.WoodenTablet,
                Title = "Конокрад",
                Text = "Нарисован человек с одним ухом и лошадь — двумя. Сторож поясняет: конокрада ищут с весны, примета — нет левого уха. Кто приведёт живым — пусть говорит с городком.",
                AuthorName = "городок",
                RewardText = "по уговору со сторожем",
                LifetimeHours = 240
            });

            p.Actions.Add(Action("town.smith", SettlementActionType.Trade, "Кузня", "Твердята: кольчуги, стёганки, оружие.", "town.smith"));
            p.Actions.Add(Action("town.tavern", SettlementActionType.Tavern, "Корчма у ворот", "Хозяйка Ярина и те, кто ждёт у окна."));
            p.Actions.Add(Action("town.board", SettlementActionType.NoticeBoard, "Таблички у ворот", "Заказы и розыск; сторож прочтёт."));
            return new SettlementProfileEntry { Data = p };
        }

        // ------------------------------------------------------------------

        private static NarrativeConditionGroup Flag(string flag, bool negate = false)
        {
            return new NarrativeConditionGroup
            {
                Conditions = new List<NarrativeCondition>
                {
                    new NarrativeCondition { Type = NarrativeConditionType.FlagSet, StringParam = flag, Negate = negate }
                }
            };
        }

        private static SettlementPersonDefinition Person(string id, string speakerId, string name, string role, string dialogueId,
            string activity, string merchantId = "")
        {
            return new SettlementPersonDefinition
            {
                Id = id, SpeakerId = speakerId, DisplayName = name, Role = role, DialogueId = dialogueId,
                Activity = activity, MerchantId = merchantId
            };
        }

        private static MerchantStockDefinition Stock(string itemId, int count)
        {
            return new MerchantStockDefinition { ItemId = itemId, TargetCount = count };
        }

        private static SettlementActionDefinition Action(string id, SettlementActionType type, string label, string hint, string target = "")
        {
            return new SettlementActionDefinition { Id = id, Type = type, Label = label, Hint = hint, TargetId = target };
        }
    }
}
