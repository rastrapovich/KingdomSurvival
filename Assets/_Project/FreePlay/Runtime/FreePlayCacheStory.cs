using System.Collections.Generic;

namespace KingdomSurvival.FreePlay
{
    // 12Т, пилот «Тайник у старого русла» [РАБОЧЕЕ] (SETTLEMENTS_TRADE_TAVERN_SPEC.md
    // §12). Сведения о тайнике покупаются в корчме торгового села у возчика
    // Ждана (устный рассказ) или берестой у торговца («Изучить»). Узнав, отряд
    // видит на карте область поиска — не точку; сам тайник — скрытое место,
    // которое находят по пути (радиус обнаружения) или осмотром округи на
    // ночлеге. Внутри — нож деда с медной насечкой и немного серебра;
    // поручение «Нож деда» (общее для доски, корчмы и разговора) закрывает
    // разговор с Жданом. Тексты, награды и поручение — данные Базы диалогов и
    // Базы поселений; здесь — только место, область поиска и передача ножа.
    //
    // Возчик, его дед и тайник — рабочий контент, не факты мира: кто и от кого
    // прятал — не утверждено.
    public static class FreePlayCacheStory
    {
        public const string LocationId = "freeplay.cache.old_bed";
        public const string LocationName = "Расколотая ива у старого русла";
        public const string FindDialogueId = "freeplay_cache_find";
        public const string SearchId = "freeplay.cache.search";

        public static class Flags
        {
            public const string Found = "freeplay.cache.found";
            public const string ErrandAccepted = "freeplay.cache.errand.accepted";
            public const string TokenReturned = "freeplay.cache.token_returned";
            public const string ErrandDone = "freeplay.cache.errand.done";
        }

        private const string ChronicleFound = "freeplay.cache.chronicle.found";
        private const string TokenGivenGrant = "freeplay.cache.grant.token_given";
        private const string FoundKnifeGrant = "freeplay.cache.grant.knife";

        private static bool Has(GameState state, string flag) => state?.Narrative != null && state.Narrative.HasFlag(flag);

        public static bool Knows(GameState state) =>
            state?.Narrative != null &&
            (state.Narrative.HasKnowledge(ItemCatalog.OldBedCacheKnowledge) || Has(state, ItemCatalog.OldBedCacheStageFlag));

        public static List<string> Refresh(GameState state)
        {
            List<string> reports = new List<string>();
            if (state?.Narrative == null)
                return reports;

            // Узнали о тайнике — место появляется в мире (скрытым) один раз.
            if (Knows(state))
            {
                if (!Has(state, ItemCatalog.OldBedCacheStageFlag))
                    state.Narrative.SetFlag(ItemCatalog.OldBedCacheStageFlag);
                EnsureCache(state);
            }

            LocationData cache = state.FindLocation(LocationId);
            // Находка — настоящая вещь в сумке героя, выдаётся один раз
            // (метка NarrativeState.Items не нужна: «вещь рядом» — по сумке).
            if (Has(state, Flags.Found))
            {
                CommanderData commander = state.GetSelectedCommander();
                if (commander != null)
                    ItemService.GrantOnce(state, FoundKnifeGrant, ItemCatalog.CarterTokenKnife, commander.Id);
            }
            if (Has(state, Flags.Found) && cache != null)
            {
                cache.IsVisibleOnMap = true;
                cache.IsDiscovered = true;
                cache.IsExplored = true;
                if (Chronicle.Record(state, ChronicleFound, "Тайник у расколотой ивы",
                        "Под корнями расколотой ивы у старого русла нашли горшок: серебро и нож с медной насечкой в три витка.",
                        LocationId, "knowledge.source." + ItemCatalog.OldBedCacheKnowledge))
                    reports.Add("Тайник у старого русла найден.");
            }

            // Нож отдан Ждану — тот же экземпляр уходит из сумки один раз.
            if (Has(state, Flags.TokenReturned) && !ItemService.HasGrant(state, TokenGivenGrant))
            {
                ItemService.Get(state).AppliedGrants.Add(TokenGivenGrant);
                ItemInstanceData knife = FindReachable(state, ItemCatalog.CarterTokenKnife);
                if (knife != null)
                    ItemService.Detach(state, knife);
            }
            return reports;
        }

        private static ItemInstanceData FindReachable(GameState state, string itemId)
        {
            foreach (ItemInstanceData item in ItemService.Get(state).Items)
            {
                if (item.ItemId == itemId && ItemService.IsReachable(state, item))
                    return item;
            }
            return null;
        }

        // Торговое село, от которого считается тайник: ближайшее к стоянке
        // поселение типа «торговое» в этой партии.
        public static LocationData FindTradingPost(GameState state)
        {
            if (state?.Settlements?.Instances == null)
                return null;
            LocationData best = null;
            double bestDistance = double.MaxValue;
            foreach (SettlementInstanceData instance in state.Settlements.Instances)
            {
                SettlementProfileDefinition profile = SettlementCatalog.Current.FindProfile(instance.ProfileId);
                if (profile == null || profile.Kind != SettlementKind.TradingPost)
                    continue;
                LocationData location = state.FindLocation(instance.LocationId);
                if (location == null)
                    continue;
                double dx = location.MapXPercent - WorldMapNavigation.CapitalXPercent;
                double dy = location.MapYPercent - WorldMapNavigation.CapitalYPercent;
                double distance = dx * dx + dy * dy;
                if (distance < bestDistance || (distance == bestDistance && string.CompareOrdinal(location.Id, best?.Id) < 0))
                {
                    best = location;
                    bestDistance = distance;
                }
            }
            return best;
        }

        // Тайник — в полудне пути от торгового села, в достижимой точке
        // (конец найденного маршрута: не в воде и не в непроходимой клетке).
        public static LocationData EnsureCache(GameState state)
        {
            LocationData existing = state.FindLocation(LocationId);
            if (existing != null)
                return existing;
            LocationData post = FindTradingPost(state);
            float fromX = post != null ? post.MapXPercent : WorldMapNavigation.CapitalXPercent;
            float fromY = post != null ? post.MapYPercent : WorldMapNavigation.CapitalYPercent;

            // Направление — устойчиво по seed партии, четыре попытки.
            uint hash = SettlementService.StableHash(state.WorldSeed + ":" + LocationId);
            float[,] directions = { { 7f, 1.5f }, { -7f, 1.5f }, { 1.5f, 7f }, { 1.5f, -7f } };
            float finalX = fromX;
            float finalY = fromY;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int d = (int)((hash + attempt) % 4);
                float candidateX = Clamp(fromX + directions[d, 0]);
                float candidateY = Clamp(fromY + directions[d, 1]);
                List<MapPointData> route = WorldMapNavigation.FindPath(fromX, fromY, candidateX, candidateY);
                if (route.Count < 2)
                    continue;
                finalX = route[route.Count - 1].XPercent;
                finalY = route[route.Count - 1].YPercent;
                if (WorldMapNavigation.DistanceHexes(fromX, fromY, finalX, finalY) >= 2.0)
                    break;
            }

            List<MapPointData> fromHome = WorldMapNavigation.FindPath(
                WorldMapNavigation.CapitalXPercent, WorldMapNavigation.CapitalYPercent, finalX, finalY);
            LocationData cache = new LocationData(LocationId, LocationName,
                ContinuousSimulationSystem.CalculateTravelHours(fromHome), "низкая")
            {
                RegionId = "freeplay-cache",
                RegionName = GameState.GetRegionName(finalX, finalY),
                MapSlotIndex = state.Locations.Count,
                MapXPercent = finalX,
                MapYPercent = finalY,
                IsDiscovered = false,
                IsVisibleOnMap = false,
                InteractionDescription = "Старое русло заросло осокой. Ива у края расколота надвое, и на одной половине — три зарубки, затянутые корой.",
                ResearchResultText = "Под ивой пусто: горшок вынули."
            };
            state.Locations.Add(cache);
            return cache;
        }

        private static float Clamp(float value) => value < 3f ? 3f : value > 97f ? 97f : value;

        // Область поиска: прямоугольник вокруг тайника, сдвинутый так, чтобы
        // тайник не был в центре (по приметам, а не по точке).
        public static IEnumerable<WorldMapSearchAreaDefinition> SearchAreas(GameState state)
        {
            LocationData cache = state?.FindLocation(LocationId);
            if (cache == null)
                yield break;
            uint hash = SettlementService.StableHash(state.WorldSeed + ":" + SearchId);
            float shiftX = (hash % 5) - 2f;
            float shiftY = ((hash / 5) % 5) - 2f;
            float cx = cache.MapXPercent + shiftX;
            float cy = cache.MapYPercent + shiftY;
            WorldMapSearchAreaDefinition search = new WorldMapSearchAreaDefinition
            {
                SearchId = SearchId,
                TargetLocationId = LocationId
            };
            search.Stages.Add(new WorldMapSearchStageDefinition
            {
                StageId = SearchId + ".area",
                RequiredKnowledgeFlags = new List<string> { ItemCatalog.OldBedCacheStageFlag },
                MinXPercent = cx - 5f,
                MaxXPercent = cx + 5f,
                MinYPercent = cy - 5f,
                MaxYPercent = cy + 5f,
                NeutralLabel = "старое русло: ищите расколотую иву"
            });
            yield return search;
        }

        public static LocationEntryView LocationEntry(GameState state, string locationId)
        {
            if (locationId != LocationId || Has(state, Flags.Found))
                return null;
            return new LocationEntryView
            {
                DialogueId = FindDialogueId,
                ButtonText = "РАЗРЫТЬ ПОД ИВОЙ",
                Hint = "Три зарубки на коре — здесь прятали."
            };
        }
    }
}
