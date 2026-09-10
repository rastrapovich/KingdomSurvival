using System;
using System.Collections.Generic;

namespace KingdomSurvival.Chapter01
{
    // Одноразовые внешние эффекты Главы 01, которых пока нет среди
    // стандартных NarrativeEffect (раздел 6.4 инструкции): ресурсы, время,
    // предметы. Каждое применение защищено стабильным execution ID через
    // тот же механизм, что уже используют NarrativeEffect
    // (NarrativeStateData.AppliedEffectExecutionIds) — повторный вызов с тем
    // же ID не повторяет эффект.
    public static class Chapter01OutcomeApplier
    {
        public static bool ApplyResourceDelta(
            GameState gameState,
            string executionId,
            int foodDelta = 0,
            int goldDelta = 0,
            int armySupplyDelta = 0)
        {
            return Apply(gameState, executionId, state =>
            {
                gameState.Food += foodDelta;
                gameState.Gold += goldDelta;
                gameState.ArmySupply += armySupplyDelta;
            });
        }

        public static bool ApplyTimeAdvance(GameState gameState, string executionId, int days)
        {
            if (days <= 0)
                throw new ArgumentOutOfRangeException(nameof(days), days, "Продвижение времени должно быть положительным.");

            return Apply(gameState, executionId, state => gameState.Day += days);
        }

        public static bool GrantItem(GameState gameState, string executionId, string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("Item id cannot be empty.", nameof(itemId));

            return Apply(gameState, executionId, state => state.GrantItem(itemId));
        }

        // До появления постоянной системы HP/травм (раздел 6.4) травма
        // героя — нарративный флаг с видимым последствием, а не отдельное
        // механическое поле.
        public static bool MarkHeroInjured(GameState gameState, string executionId, string injuryFlagId)
        {
            if (string.IsNullOrWhiteSpace(injuryFlagId))
                throw new ArgumentException("Injury flag id cannot be empty.", nameof(injuryFlagId));

            return Apply(gameState, executionId, state => state.SetFlag(injuryFlagId));
        }

        // P05-T03: читает уже установленные флаги N04 «Синяя ставня» и
        // применяет единственное внешнее системное последствие паводка —
        // потерю части хозяйственного скота (раздел 15 инструкции). Не
        // решает сюжет и не трогает флаги сама — только реагирует на то,
        // что уже выставили onRevealEffects конечных узлов N04. Injury
        // героя и повреждение настила остаются нарративными флагами без
        // отдельного системного эффекта (раздел 12/16).
        public static void ApplyFloodConsequences(GameState gameState)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));

            if (gameState.Narrative == null)
                gameState.Narrative = new NarrativeStateData();

            if (gameState.Narrative.HasFlag(Chapter01Ids.Flags.FloodLivestockLost))
            {
                ApplyResourceDelta(
                    gameState,
                    Chapter01Ids.Effects.FloodResourceLoss,
                    foodDelta: -12);
            }
        }

        // P07-T04: находка планки в N08 (chapter01.flag.old_trace_found) —
        // физический предмет, найденный независимо от исхода проверки
        // chapter01.check.seven_tooth_object. Провал проверки не отменяет
        // находку — он только не даёт knowledge.seven_tooth_object
        // (это ставит сам диалог через onRevealEffects на успешной ветке).
        public static void ApplySevenTeethInvestigationConsequences(GameState gameState)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));

            if (gameState.Narrative == null)
                gameState.Narrative = new NarrativeStateData();

            if (gameState.Narrative.HasFlag(Chapter01Ids.Flags.OldTraceFound))
            {
                GrantItem(gameState, Chapter01Ids.Effects.SevenToothGaugeGrant, Chapter01Ids.Items.SevenToothGauge);
            }
        }

        // P08-T01/T03: единственное внешнее системное последствие решения
        // "идти дальше" в N09 — раскрытие ОДНОЙ области поиска на карте
        // (раздел про N09/карту инструкции: "область поиска", не точная
        // локация — точный брод игрок находит позже, в P09). Координаты на
        // карте ниже — production-деталь размещения, не лор; сама Locality
        // не знает о главах и не должна: раскрывает её здесь, реагируя на
        // уже выставленный FarRouteUnlocked, а не сама решает сюжет.
        public static void ApplyDepartureConsequences(GameState gameState)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));

            Apply(gameState, Chapter01Ids.Effects.DepartureLocationReveal, state => RevealDepartureSearchLocation(gameState));
        }

        private static void RevealDepartureSearchLocation(GameState gameState)
        {
            if (gameState.Locations == null)
                gameState.Locations = new List<LocationData>();

            if (gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch) != null)
                return;

            float candidateX = WorldMapNavigation.CapitalXPercent;
            float candidateY = WorldMapNavigation.CapitalYPercent - 26f;

            List<MapPointData> route = WorldMapNavigation.FindPath(
                WorldMapNavigation.CapitalXPercent,
                WorldMapNavigation.CapitalYPercent,
                candidateX,
                candidateY);

            float finalX = candidateX;
            float finalY = candidateY;
            if (route.Count > 0)
            {
                finalX = route[route.Count - 1].XPercent;
                finalY = route[route.Count - 1].YPercent;
            }

            LocationData location = new LocationData(
                Chapter01Ids.Locations.OldWaterSearch,
                "След старого русла",
                ContinuousSimulationSystem.CalculateTravelHours(route),
                "неизвестна")
            {
                RegionId = "chapter01-old-water-search",
                RegionName = GameState.GetRegionName(finalX, finalY),
                MapSlotIndex = gameState.Locations.Count,
                MapXPercent = finalX,
                MapYPercent = finalY
            };

            gameState.Locations.Add(location);
        }

        // P09-T01: последствие выбора маршрута в N11, читает флаг, который
        // уже поставил сам диалог (тот же паттерн, что ApplyFloodConsequences
        // читает флаги N04). "Срезать через низину" (CrossedOldRoadBoundary)
        // не требует никакого действия здесь — маршрут и так уже идёт от
        // текущей позиции к прежней цели (раздел "Вариант Б": "Маршрут снова
        // строится от текущей позиции напрямую" — это ОПИСАНИЕ уже
        // действующего маршрута, а не новое действие). Только "Пойти старым
        // путём" (FollowedOldRoad) требует реального крюка.
        public static void ApplyLongRoadRouteConsequences(GameState gameState)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));
            if (gameState.Narrative == null)
                gameState.Narrative = new NarrativeStateData();

            if (gameState.Narrative.HasFlag(Chapter01Ids.Flags.FollowedOldRoad))
                Apply(gameState, Chapter01Ids.Effects.OldRoadDetourStart, _ => StartOldRoadDetour(gameState));
        }

        private static void StartOldRoadDetour(GameState gameState)
        {
            if (!gameState.HasActiveExpedition)
                return;

            ExpeditionData expedition = gameState.ActiveExpedition;
            LocationData target = gameState.FindLocation(Chapter01Ids.Locations.OldWaterSearch);
            if (target == null)
                return;

            float curX = expedition.CurrentMapXPercent;
            float curY = expedition.CurrentMapYPercent;
            float targetX = target.MapXPercent;
            float targetY = target.MapYPercent;

            float midX = (curX + targetX) / 2f;
            float midY = (curY + targetY) / 2f;
            float dx = targetX - curX;
            float dy = targetY - curY;
            double length = Math.Sqrt(dx * dx + dy * dy);

            // Реальный крюк в стороне от прямой линии до цели (раздел
            // "Решение у старой дороги": "отряд физически идёт к ней").
            // Величина смещения не канонизирована (раздел "Что требует
            // решения") — фиксированный отступ в процентах карты достаточен,
            // чтобы гарантированно дать WorldMapNavigation.FindPath более
            // длинный путь, даже на ровной местности.
            const float DetourOffsetPercent = 10f;
            float offsetX;
            float offsetY;
            if (length > 0.001)
            {
                offsetX = (float)(-dy / length) * DetourOffsetPercent;
                offsetY = (float)(dx / length) * DetourOffsetPercent;
            }
            else
            {
                offsetX = DetourOffsetPercent;
                offsetY = 0f;
            }

            float waypointX = WorldMapNavigation.ClampMapX(midX + offsetX);
            float waypointY = WorldMapNavigation.ClampMapY(midY + offsetY);

            // locationId=null — временная точка маршрута
            // (GameState.GetOrCreateRouteWaypoint), не постоянная локация;
            // Chapter01StoryDirector.TryContinueOldRoadDetourIfArrived
            // перенаправит экспедицию к настоящей цели по прибытии сюда.
            if (gameState.TryChangeExpeditionRoute(waypointX, waypointY, null, out _))
                gameState.Narrative.SetFlag(Chapter01Ids.Flags.OldRoadDetourInProgress);
        }

        // P09-T03: единственное внешнее системное последствие "Троих под
        // телегой" — запуск времязатратной остановки на дороге с ценой,
        // зависящей от выбранного исхода (сам NarrativeEffect не умеет
        // стартовать GameState.TryStartRoadActivity). CartOutcomePassedBy —
        // 0 часов, ничего не стартуем: "Не вмешиваться" не должно стоить
        // времени (раздел "Исход 1 — пройти мимо": "Время: +0").
        public static void ApplyCartConsequences(GameState gameState)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));
            if (gameState.Narrative == null)
                gameState.Narrative = new NarrativeStateData();

            NarrativeStateData state = gameState.Narrative;
            double durationHours = 0.0;
            string activityDisplayName = null;

            if (state.HasFlag(Chapter01Ids.Flags.CartOutcomeSavedMan) ||
                state.HasFlag(Chapter01Ids.Flags.CartOutcomeSavedSeed))
            {
                durationHours = 1.0;
                activityDisplayName = "ПОМОЩЬ У ТЕЛЕГИ";
            }
            else if (state.HasFlag(Chapter01Ids.Flags.CartOutcomeSavedBoth))
            {
                int fighterCount = gameState.HasActiveExpedition ? gameState.ActiveExpedition.FighterIds.Count : 0;
                durationHours = fighterCount <= 1 ? 3.0 : 2.0;
                activityDisplayName = "СПАСЕНИЕ ЧЕЛОВЕКА И ЗЕРНА";
            }

            if (durationHours <= 0.0)
                return;

            Apply(gameState, Chapter01Ids.Effects.CartActivityStart, _ =>
                gameState.TryStartRoadActivity(
                    "cart_help",
                    activityDisplayName,
                    durationHours,
                    0,
                    0,
                    out _));
        }

        private static bool Apply(GameState gameState, string executionId, Action<NarrativeStateData> mutation)
        {
            if (gameState == null)
                throw new ArgumentNullException(nameof(gameState));
            if (string.IsNullOrWhiteSpace(executionId))
                throw new ArgumentException("Execution id cannot be empty.", nameof(executionId));
            if (mutation == null)
                throw new ArgumentNullException(nameof(mutation));

            if (gameState.Narrative == null)
                gameState.Narrative = new NarrativeStateData();

            if (gameState.Narrative.HasEffectApplied(executionId))
                return false;

            mutation(gameState.Narrative);
            gameState.Narrative.MarkEffectApplied(executionId);
            return true;
        }
    }
}
