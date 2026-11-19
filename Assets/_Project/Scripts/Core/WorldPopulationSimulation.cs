using System;
using System.Collections.Generic;
using System.Linq;

// Бриф «Живое население глобальной карты» (19.11.2026), [РАБОЧЕЕ — ПРОТОТИП].
// Жизнь групп по общему игровому времени: такты фиксированной длины
// (TickHours) от последнего обработанного часа, поэтому итог зависит от
// игрового времени и действий, а не от FPS и множителя скорости. На маршруте
// положение — аналитическая функция времени (WorldMapActorMotion); при смене
// занятия (ожидание, наблюдение, погоня, отход, возвращение) задача
// начинается из фактической текущей позиции, без скачка к началу маршрута.
// Контакт проверяется по пройденным за такт отрезкам (сближение по ходу,
// а не только расстояние в конце). Обязательный контакт останавливает
// обработку на своём такте: дальше часы стоят на паузе до решения игрока.
public static partial class WorldPopulationService
{
    public const double TickHours = 0.05;
    // Сколько тактов обрабатывается за один вызов; дальше — догон без ИИ
    // (защита от бесконечного догоняющего расчёта после долгой паузы).
    private const int MaxTicksPerCall = 4000;
    private const double SightBonusHexes = 1.0;

    private sealed class TickContext
    {
        public GameState State;
        public WorldPopulationStateData Data;
        public WorldMapHexGrid Grid;
        public WorldPopulationSettings Settings;
        public bool HeroOnMap;
        public float HeroX, HeroY, HeroPrevX, HeroPrevY;
        public double PartyStrength;
        public List<string> Messages;
        public bool Raised;
        // Позиции групп в начале такта (для сближения по отрезкам).
        public readonly Dictionary<PopulationGroupState, (float X, float Y)> Prev = new Dictionary<PopulationGroupState, (float, float)>();
    }

    // Подшаг часов не длиннее этого: рядом с отрядом враждебная группа —
    // мелкие шаги, чтобы контакт ловился у фактической точки сближения.
    public static double MaxStepHours(GameState state)
    {
        WorldPopulationStateData data = state?.Population;
        if (data == null || !state.HasGeneratedMap || !HeroOnMap(state))
            return double.MaxValue;
        WorldMapHexGrid grid = state.GeneratedMap.CreateGrid();
        ExpeditionData e = state.ActiveExpedition;
        foreach (PopulationGroupState g in data.Groups)
        {
            if (g == null || !g.IsActive || g.Behavior == WorldPopulationBehavior.Peaceful || g.Behavior == WorldPopulationBehavior.Patrol)
                continue;
            if (grid.DistanceHexes(e.CurrentMapXPercent, e.CurrentMapYPercent, g.X, g.Y) < 5.0)
                return TickHours;
        }
        return double.MaxValue;
    }

    public static bool HeroOnMap(GameState state) =>
        state != null && state.HasActiveExpedition && !LocalExplorationService.IsActive(state);

    // Главный вход из часов мира: продвигает население от последнего
    // обработанного часа до toHour. Позиция героя на отрезке подшага
    // интерполируется. True — открыт обязательный контакт (пауза).
    public static bool Advance(GameState state, double toHour, float heroFromX, float heroFromY, float heroToX, float heroToY, double stepFromHour, List<string> messages)
    {
        if (state == null || !state.HasGeneratedMap)
            return false;
        WorldPopulationStateData data = EnsureState(state, toHour);
        if (data == null)
            return false;
        if (data.LastSimHour < 0 || toHour - data.LastSimHour > 24.0 * 10)
            data.LastSimHour = toHour - TickHours;
        if (PendingContact(state) is PopulationContactState open && open.Status == PopulationContactState.Pending && state.HasPendingExpeditionDecision)
            return false;

        TickContext ctx = new TickContext
        {
            State = state,
            Data = data,
            Grid = state.GeneratedMap.CreateGrid(),
            Settings = Settings,
            HeroOnMap = HeroOnMap(state),
            Messages = messages,
            PartyStrength = PartyStrength(state)
        };
        float prevX = heroFromX, prevY = heroFromY;
        int ticks = 0;
        double span = Math.Max(1e-9, toHour - stepFromHour);
        while (data.LastSimHour + TickHours <= toHour + 1e-9)
        {
            double t = data.LastSimHour + TickHours;
            if (++ticks > MaxTicksPerCall)
            {
                data.LastSimHour = toHour;
                break;
            }
            double f = Math.Max(0.0, Math.Min(1.0, (t - stepFromHour) / span));
            ctx.HeroPrevX = prevX;
            ctx.HeroPrevY = prevY;
            ctx.HeroX = (float)(heroFromX + (heroToX - heroFromX) * f);
            ctx.HeroY = (float)(heroFromY + (heroToY - heroFromY) * f);
            Tick(ctx, t);
            prevX = ctx.HeroX;
            prevY = ctx.HeroY;
            data.LastSimHour = t;
            if (ctx.Raised)
                return true;
        }
        return false;
    }

    private static void Tick(TickContext ctx, double t)
    {
        WorldPopulationStateData data = ctx.Data;
        GameState state = ctx.State;
        List<PopulationGroupState> groups = data.Groups;

        // 1. Движение по занятиям.
        ctx.Prev.Clear();
        foreach (PopulationGroupState g in groups)
        {
            if (g == null || !g.IsActive)
                continue;
            ctx.Prev[g] = (g.X, g.Y);
            Move(ctx, g, t);
        }

        // 2. Признаки источников и лагерь у героя.
        if (ctx.HeroOnMap && PendingContact(state) == null)
            SourceInteractions(ctx, t);

        // 3. Реакции на героя и друг на друга, контакты.
        foreach (PopulationGroupState g in groups)
        {
            if (g == null || !g.IsActive || g.Activity == PopulationGroupState.Conflict)
                continue;
            if (ctx.HeroOnMap)
                HeroInteractions(ctx, g, t);
            if (ctx.Raised)
                return;
            if (ctx.Settings.NpcConflicts)
                GroupInteractions(ctx, g, t);
        }

        // 4. Столкновения групп: окно вмешательства и фоновый итог.
        foreach (PopulationConflictState c in data.Conflicts)
        {
            if (c.Status != PopulationConflictState.Open)
                continue;
            if (ctx.HeroOnMap && !c.HeroOffered && PendingContact(state) == null &&
                ctx.Grid.DistanceHexes(ctx.HeroX, ctx.HeroY, c.X, c.Y) <= 2.5)
            {
                c.HeroOffered = true;
                PopulationContactState contact = new PopulationContactState
                {
                    Kind = PopulationContactState.ConflictHelp, ConflictId = c.ConflictId, GroupId = c.AttackerId, Hour = t, X = c.X, Y = c.Y
                };
                if (RaiseContact(state, contact, ctx.Messages))
                {
                    ctx.Raised = true;
                    return;
                }
            }
            if (t >= c.ResolveHour && !data.Contacts.Any(x => x.ConflictId == c.ConflictId && x.Status != PopulationContactState.Resolved))
                ResolveConflict(ctx, c, t);
        }

        // Временные встречи: срок, уход, появление.
        TemporaryStep(ctx, t);
        if (ctx.Raised)
            return;

        // 5. Раз в игровой час — пополнение источников.
        if (t >= data.NextUpkeepHour)
        {
            data.NextUpkeepHour = Math.Floor(t) + 1.0;
            Upkeep(state, t, ctx.HeroX, ctx.HeroY, ctx.HeroOnMap, ctx.Grid);
        }
    }

    // ------------------------------------------------------------------
    // Движение

    private static double SpeedHexesPerHour(PopulationGroupState g) =>
        WorldMapMovementRules.Current.GroupHexesPerGameHour(WorldMapActorMotion.SpeedHexesPerHour(g.Role));

    private static void Move(TickContext ctx, PopulationGroupState g, double t)
    {
        GeneratedMapActor def = DefinitionOf(ctx.State, g);
        WorldMapHexGrid grid = ctx.Grid;
        if (!g.HasPosition && def != null)
        {
            MapPointData start = WorldMapActorMotion.PositionAt(def, t, grid, out float sx, out float sy, out _);
            SetPosition(g, start.XPercent, start.YPercent, sx, sy);
        }
        bool hasRoute = def != null && def.State == "Travel";
        switch (g.Activity)
        {
            case PopulationGroupState.Route:
            {
                if (!hasRoute)
                    return;
                MapPointData at = WorldMapActorMotion.PositionAt(def, t, grid, out float dx, out float dy, out _);
                SetPosition(g, at.XPercent, at.YPercent, dx, dy);
                return;
            }
            case PopulationGroupState.Chase:
            {
                if (!TargetPosition(ctx, g, t, out float tx, out float ty) || ChaseOver(ctx, g, t, tx, ty))
                {
                    StartActivity(g, PopulationGroupState.Return, string.Empty, t, 0);
                    return;
                }
                Step(ctx, g, tx, ty, SpeedHexesPerHour(g) * 1.15 * TickHours, false);
                return;
            }
            case PopulationGroupState.Avoid:
            {
                if (t >= g.ActivityUntilHour)
                {
                    StartActivity(g, PopulationGroupState.Return, string.Empty, t, 0);
                    return;
                }
                if (TargetPosition(ctx, g, t, out float ax, out float ay))
                    Step(ctx, g, ax, ay, SpeedHexesPerHour(g) * TickHours, true);
                return;
            }
            case PopulationGroupState.Leave:
                Step(ctx, g, g.ExitX, g.ExitY, SpeedHexesPerHour(g) * TickHours, false);
                return;
            case PopulationGroupState.Wait:
            case PopulationGroupState.Observe:
                if (g.ActivityUntilHour > 0 && t >= g.ActivityUntilHour)
                    StartActivity(g, PopulationGroupState.Return, string.Empty, t, 0);
                return;
            case PopulationGroupState.Return:
            {
                if (!hasRoute)
                {
                    g.Activity = PopulationGroupState.Route;
                    return;
                }
                MapPointData home = WorldMapActorMotion.PositionAt(def, t, grid, out _, out _, out _);
                double d = grid.DistanceHexes(g.X, g.Y, home.XPercent, home.YPercent);
                double step = SpeedHexesPerHour(g) * 1.3 * TickHours;
                if (d <= Math.Max(0.25, step))
                {
                    g.Activity = PopulationGroupState.Route;
                    g.TargetId = string.Empty;
                    SetPosition(g, home.XPercent, home.YPercent, home.XPercent - g.X, home.YPercent - g.Y);
                    return;
                }
                Step(ctx, g, home.XPercent, home.YPercent, step, false);
                return;
            }
            default:
                return;
        }
    }

    private static void Step(TickContext ctx, PopulationGroupState g, float tx, float ty, double hexes, bool away)
    {
        WorldMapHexGrid grid = ctx.Grid;
        double dxPx = grid.PercentToPixelX(tx) - grid.PercentToPixelX(g.X);
        double dyPx = grid.PercentToPixelY(ty) - grid.PercentToPixelY(g.Y);
        double length = Math.Sqrt(dxPx * dxPx + dyPx * dyPx);
        if (length < 1e-6)
            return;
        if (away)
        {
            dxPx = -dxPx;
            dyPx = -dyPx;
        }
        double move = Math.Min(away ? hexes * grid.HexWidth : length, hexes * grid.HexWidth);
        double nx = grid.PercentToPixelX(g.X) + dxPx / length * move;
        double ny = grid.PercentToPixelY(g.Y) + dyPx / length * move;
        float px = grid.PixelToPercentX(nx), py = grid.PixelToPercentY(ny);
        // Группа не уходит в воду, на скалы и за край карты: стоит на месте.
        WorldMapHexCell cell = grid.CellAtPercent(px, py);
        if (!grid.IsInside(cell) || !Passable(ctx.State, px, py))
            return;
        SetPosition(g, px, py, px - g.X, py - g.Y);
    }

    private static bool Passable(GameState state, float x, float y)
    {
        WorldMapGameplayTerrainType terrain = WorldMapNavigation.GetTerrainAtPercent(x, y);
        return terrain != WorldMapGameplayTerrainType.Water && terrain != WorldMapGameplayTerrainType.Cliffs &&
               terrain != WorldMapGameplayTerrainType.Mountains;
    }

    private static bool TargetPosition(TickContext ctx, PopulationGroupState g, double t, out float x, out float y)
    {
        x = y = 0f;
        if (g.TargetId == HeroTarget)
        {
            if (!ctx.HeroOnMap)
                return false;
            x = ctx.HeroX;
            y = ctx.HeroY;
            return true;
        }
        PopulationGroupState target = FindGroup(ctx.State, g.TargetId);
        if (target == null || !target.IsActive)
            return false;
        x = target.X;
        y = target.Y;
        return true;
    }

    // Погоня заканчивается по времени, по дальности от точки начала и при
    // потере цели; за пределы своего участка группа не идёт.
    private static bool ChaseOver(TickContext ctx, PopulationGroupState g, double t, float tx, float ty)
    {
        WorldPopulationPreset preset = Preset(g);
        WorldMapHexGrid grid = ctx.Grid;
        if (t - g.ActivityStartHour > ctx.Settings.ChaseMaxHours)
            return true;
        if (grid.DistanceHexes(g.ActivityOriginX, g.ActivityOriginY, g.X, g.Y) > preset.ChaseHexes + 0.5)
            return true;
        if (grid.DistanceHexes(g.X, g.Y, tx, ty) > preset.DetectHexes * 1.6)
            return true;
        if (g.TargetId == HeroTarget && HeroNearService(ctx, tx, ty))
            return true;
        return false;
    }

    private static bool HeroNearService(TickContext ctx, float x, float y)
    {
        GeneratedWorldMap map = ctx.State.GeneratedMap;
        double safe = ctx.Settings.ServiceSafeRadiusHexes + 0.5;
        foreach (GeneratedMapSite v in map.Villages)
            if (ctx.Grid.DistanceHexes(x, y, v.XPercent, v.YPercent) <= safe)
                return true;
        return ctx.Grid.DistanceHexes(x, y, map.StartXPercent, map.StartYPercent) <= ctx.Settings.StartSafeRadiusHexes * 0.5;
    }

    // Ближайшее сближение двух точек, равномерно идущих по своим отрезкам
    // за такт (клетки).
    private static double ClosestApproach(WorldMapHexGrid grid, float a0x, float a0y, float a1x, float a1y, float b0x, float b0y, float b1x, float b1y)
    {
        double rx = grid.PercentToPixelX(a0x) - grid.PercentToPixelX(b0x);
        double ry = grid.PercentToPixelY(a0y) - grid.PercentToPixelY(b0y);
        double vx = (grid.PercentToPixelX(a1x) - grid.PercentToPixelX(a0x)) - (grid.PercentToPixelX(b1x) - grid.PercentToPixelX(b0x));
        double vy = (grid.PercentToPixelY(a1y) - grid.PercentToPixelY(a0y)) - (grid.PercentToPixelY(b1y) - grid.PercentToPixelY(b0y));
        double vv = vx * vx + vy * vy;
        double s = vv < 1e-9 ? 0.0 : Math.Max(0.0, Math.Min(1.0, -(rx * vx + ry * vy) / vv));
        double cx = rx + vx * s, cy = ry + vy * s;
        return Math.Sqrt(cx * cx + cy * cy) / grid.HexWidth;
    }

    // ------------------------------------------------------------------
    // Герой

    private static void SourceInteractions(TickContext ctx, double t)
    {
        WorldMapHexGrid grid = ctx.Grid;
        foreach (PopulationSourceState source in ctx.Data.Sources)
        {
            if (source.Kind == GeneratedPopulationSource.Village)
                continue;
            double d = grid.DistanceHexes(ctx.HeroX, ctx.HeroY, source.X, source.Y);
            bool live = source.Status != PopulationSourceState.Eliminated &&
                        ctx.Data.Groups.Any(g => g.IsActive && g.SourceId == source.SourceId);
            // Следы: район и тип риска без точного положения группы.
            if (source.Knowledge == 0 && live && d <= Math.Max(ctx.Settings.TraceHexes, source.RadiusHexes + 1.5) && !string.IsNullOrEmpty(source.Signs))
            {
                source.Knowledge = 1;
                ctx.Messages?.Add("Следы: " + source.Signs);
            }
            if (source.Kind == GeneratedPopulationSource.BandCamp && source.Status != PopulationSourceState.Eliminated &&
                d <= ctx.Settings.CampApproachHexes && t >= source.CampCooldownUntil)
            {
                source.CampCooldownUntil = t + ctx.Settings.RepeatContactHours;
                source.Knowledge = 2;
                PopulationContactState contact = new PopulationContactState
                {
                    Kind = PopulationContactState.CampApproach, SourceId = source.SourceId, Hour = t, X = source.X, Y = source.Y
                };
                if (RaiseContact(ctx.State, contact, ctx.Messages))
                {
                    ctx.Raised = true;
                    return;
                }
            }
        }
    }

    private static void HeroInteractions(TickContext ctx, PopulationGroupState g, double t)
    {
        WorldMapHexGrid grid = ctx.Grid;
        WorldPopulationSettings s = ctx.Settings;
        WorldPopulationPreset preset = Preset(g);
        double d = grid.DistanceHexes(ctx.HeroX, ctx.HeroY, g.X, g.Y);
        double sight = WorldMapMovementRules.Current.FogRevealRadiusHexes + SightBonusHexes;
        if (d <= sight)
        {
            g.Knowledge = 3;
            g.LastKnownX = g.X;
            g.LastKnownY = g.Y;
            g.LastKnownHour = t;
        }
        if (g.Behavior == WorldPopulationBehavior.Peaceful || g.Behavior == WorldPopulationBehavior.Patrol)
            return;
        if (!g.Members.Any(m => m.Alive))
            return;
        if (PendingContact(ctx.State) != null)
            return;
        bool calm = t < g.CalmUntilHour;
        bool respite = t < ctx.Data.RespiteUntilHour;
        (float X, float Y) before = ctx.Prev.TryGetValue(g, out (float X, float Y) p) ? p : (g.X, g.Y);
        bool contact = ClosestApproach(grid, before.X, before.Y, g.X, g.Y,
            ctx.HeroPrevX, ctx.HeroPrevY, ctx.HeroX, ctx.HeroY) <= s.ContactHexes;

        switch (g.Behavior)
        {
            case WorldPopulationBehavior.Territorial:
                if (d > preset.WarnHexes + 1.5)
                    g.Warned = false;
                if (calm)
                    return;
                if (!g.Warned && d <= preset.WarnHexes)
                {
                    g.Warned = true;
                    ctx.Messages?.Add(Text(preset.WarningText, g.Name + " предупреждает: дальше — его участок."));
                    if (g.Activity == PopulationGroupState.Route)
                        StartActivity(g, PopulationGroupState.Wait, HeroTarget, t, 0.75);
                }
                if (contact)
                    Raise(ctx, g, PopulationContactState.BeastAttack, t);
                return;
            case WorldPopulationBehavior.Shy:
                if (calm)
                    return;
                if (d <= preset.DetectHexes && g.Activity != PopulationGroupState.Avoid)
                {
                    if (!g.ObserveReported)
                    {
                        g.ObserveReported = true;
                        ctx.Messages?.Add(Text(preset.WarningText, g.Name + " уходит от отряда."));
                    }
                    StartActivity(g, PopulationGroupState.Avoid, HeroTarget, t, 1.0);
                }
                if (contact)
                    Raise(ctx, g, PopulationContactState.BeastAttack, t);
                return;
            case WorldPopulationBehavior.Predator:
                if (calm || respite || g.Activity == PopulationGroupState.Leave)
                    return;
                if (g.Activity == PopulationGroupState.Chase && g.TargetId == HeroTarget)
                {
                    if (contact)
                        Raise(ctx, g, PopulationContactState.BeastAttack, t);
                    return;
                }
                if (g.Activity == PopulationGroupState.Observe)
                {
                    if (d > preset.DetectHexes * 1.3)
                    {
                        StartActivity(g, PopulationGroupState.Return, string.Empty, t, 0);
                        g.ObserveReported = false;
                        return;
                    }
                    if (t - g.ActivityStartHour >= s.PredatorObserveHours && GroupStrength(g) >= ctx.PartyStrength && !HeroNearService(ctx, ctx.HeroX, ctx.HeroY))
                        StartActivity(g, PopulationGroupState.Chase, HeroTarget, t, 0);
                    else if (contact)
                        Raise(ctx, g, PopulationContactState.BeastAttack, t);
                    return;
                }
                if (d <= preset.DetectHexes && (g.Activity == PopulationGroupState.Route || g.Activity == PopulationGroupState.Return))
                {
                    StartActivity(g, PopulationGroupState.Observe, HeroTarget, t, 0);
                    if (!g.ObserveReported)
                    {
                        g.ObserveReported = true;
                        ctx.Messages?.Add(Text(preset.WarningText, g.Name + " следит за отрядом."));
                    }
                }
                else if (contact)
                    Raise(ctx, g, PopulationContactState.BeastAttack, t);
                return;
            case WorldPopulationBehavior.Bandit:
                if (g.Activity == PopulationGroupState.Chase && g.TargetId == HeroTarget)
                {
                    if (contact)
                        Raise(ctx, g, PopulationContactState.BandDemand, t);
                    return;
                }
                if (calm || respite || g.Activity == PopulationGroupState.Chase || g.Activity == PopulationGroupState.Avoid || g.Activity == PopulationGroupState.Leave)
                    return;
                if (d <= preset.DetectHexes && !HeroNearService(ctx, ctx.HeroX, ctx.HeroY))
                {
                    if (GroupStrength(g) >= ctx.PartyStrength * 0.9)
                        StartActivity(g, PopulationGroupState.Chase, HeroTarget, t, 0);
                    else if (d <= preset.DetectHexes * 0.8)
                        StartActivity(g, PopulationGroupState.Avoid, HeroTarget, t, 1.0);
                }
                return;
        }
    }

    private static void Raise(TickContext ctx, PopulationGroupState g, string kind, double t)
    {
        PopulationContactState contact = new PopulationContactState
        {
            Kind = kind, GroupId = g.GroupId, SourceId = g.SourceId, Hour = t, X = g.X, Y = g.Y
        };
        if (!RaiseContact(ctx.State, contact, ctx.Messages))
            return;
        g.LastContactHour = t;
        g.ContactCount++;
        g.Knowledge = 3;
        StartActivity(g, PopulationGroupState.Wait, HeroTarget, t, 0);
        ctx.Raised = true;
    }

    // ------------------------------------------------------------------
    // Группы между собой (без героя)

    private static void GroupInteractions(TickContext ctx, PopulationGroupState g, double t)
    {
        WorldMapHexGrid grid = ctx.Grid;
        WorldPopulationPreset preset = Preset(g);
        if (!g.Members.Any(m => m.Alive) || !string.IsNullOrEmpty(g.ConflictId))
            return;
        List<PopulationGroupState> others = ctx.Data.Groups;

        // Погоня за группой: контакт — один зарегистрированный конфликт пары.
        if (g.Activity == PopulationGroupState.Chase && g.TargetId != HeroTarget)
        {
            PopulationGroupState target = FindGroup(ctx.State, g.TargetId);
            if (target != null && target.IsActive && string.IsNullOrEmpty(target.ConflictId) &&
                grid.DistanceHexes(g.X, g.Y, target.X, target.Y) <= ctx.Settings.ContactHexes)
                StartConflict(ctx, g, target, t);
            return;
        }
        bool free = g.Activity == PopulationGroupState.Route || g.Activity == PopulationGroupState.Return;
        switch (g.Behavior)
        {
            case WorldPopulationBehavior.Bandit:
            {
                if (!free || t < g.CalmUntilHour)
                    return;
                double mine = GroupStrength(g);
                PopulationGroupState patrol = others.FirstOrDefault(o => o.IsActive && o.Behavior == WorldPopulationBehavior.Patrol &&
                                                                         grid.DistanceHexes(g.X, g.Y, o.X, o.Y) <= preset.DetectHexes);
                if (patrol != null && GroupStrength(patrol) >= mine)
                {
                    StartActivity(g, PopulationGroupState.Avoid, patrol.GroupId, t, 1.0);
                    return;
                }
                PopulationGroupState prey = others
                    .Where(o => o.IsActive && o != g && (o.Role == WorldMapGroupRule.Caravan || o.Role == WorldMapGroupRule.Master) &&
                                string.IsNullOrEmpty(o.ConflictId) && grid.DistanceHexes(g.X, g.Y, o.X, o.Y) <= preset.DetectHexes)
                    .OrderBy(o => grid.DistanceHexes(g.X, g.Y, o.X, o.Y)).FirstOrDefault();
                if (prey != null && mine >= GroupStrength(prey) * 0.8)
                    StartActivity(g, PopulationGroupState.Chase, prey.GroupId, t, 0);
                return;
            }
            case WorldPopulationBehavior.Patrol:
            {
                if (!free)
                    return;
                PopulationGroupState band = others
                    .Where(o => o.IsActive && o.Behavior == WorldPopulationBehavior.Bandit && string.IsNullOrEmpty(o.ConflictId) &&
                                o.Members.Any(m => m.Alive) && grid.DistanceHexes(g.X, g.Y, o.X, o.Y) <= preset.DetectHexes)
                    .OrderBy(o => grid.DistanceHexes(g.X, g.Y, o.X, o.Y)).FirstOrDefault();
                if (band != null && g.Members.Any(m => m.Alive))
                    StartActivity(g, PopulationGroupState.Chase, band.GroupId, t, 0);
                return;
            }
            case WorldPopulationBehavior.Peaceful:
            {
                // Обоз замечает угрозу — встаёт и выжидает; угроза ушла — дальше.
                PopulationGroupState threat = others.FirstOrDefault(o => o.IsActive && o.Behavior == WorldPopulationBehavior.Bandit &&
                                                                         grid.DistanceHexes(g.X, g.Y, o.X, o.Y) <= preset.DetectHexes);
                if (threat != null && g.Activity == PopulationGroupState.Route)
                    StartActivity(g, PopulationGroupState.Wait, threat.GroupId, t, 4.0);
                else if (threat == null && g.Activity == PopulationGroupState.Wait && !string.IsNullOrEmpty(g.TargetId) && g.TargetId != HeroTarget)
                    StartActivity(g, PopulationGroupState.Return, string.Empty, t, 0);
                return;
            }
        }
    }

    private static void StartConflict(TickContext ctx, PopulationGroupState attacker, PopulationGroupState defender, double t)
    {
        WorldPopulationStateData data = ctx.Data;
        PopulationConflictState conflict = new PopulationConflictState
        {
            ConflictId = "k" + (++data.NextConflictNumber) + "-" + attacker.GroupId + "-" + defender.GroupId,
            AttackerId = attacker.GroupId,
            DefenderId = defender.GroupId,
            X = (attacker.X + defender.X) * 0.5f,
            Y = (attacker.Y + defender.Y) * 0.5f,
            StartHour = t,
            ResolveHour = t + ctx.Settings.ConflictWindowHours
        };
        data.Conflicts.Add(conflict);
        attacker.ConflictId = defender.ConflictId = conflict.ConflictId;
        StartActivity(attacker, PopulationGroupState.Conflict, defender.GroupId, t, 0);
        StartActivity(defender, PopulationGroupState.Conflict, attacker.GroupId, t, 0);
    }

    // Простой изолированный оценщик фонового столкновения: текущие статы,
    // ограниченный разброс по устойчивому seed конфликта, ограниченные
    // потери. Не бой героя и не эквивалент BattleSandbox.
    private static void ResolveConflict(TickContext ctx, PopulationConflictState c, double t)
    {
        GameState state = ctx.State;
        PopulationGroupState a = FindGroup(state, c.AttackerId);
        PopulationGroupState d = FindGroup(state, c.DefenderId);
        c.Status = PopulationConflictState.Resolved;
        if (a == null || d == null || !a.IsActive || !d.IsActive)
        {
            Release(a, t);
            Release(d, t);
            c.Outcome = "dispersed";
            return;
        }
        Random random = new Random(WorldMapGenerator.StreamSeed(state.GeneratedMap.Seed, "population.conflict." + c.ConflictId));
        double sa = GroupStrength(a) * (0.85 + 0.3 * random.NextDouble());
        double sd = GroupStrength(d) * (0.85 + 0.3 * random.NextDouble());
        PopulationGroupState winner = sa >= sd ? a : d;
        PopulationGroupState loser = winner == a ? d : a;
        double ratio = Math.Max(sa, sd) / Math.Max(1.0, Math.Min(sa, sd));
        c.Outcome = winner == a ? "attacker-won" : "defender-won";

        List<PopulationMemberState> loserAlive = loser.Members.Where(m => m.Alive).ToList();
        int casualties = ratio >= 2.0 ? loserAlive.Count : Math.Max(1, (loserAlive.Count + 1) / 2);
        for (int i = 0; i < loserAlive.Count; i++)
        {
            PopulationMemberState m = loserAlive[(i + random.Next(loserAlive.Count)) % loserAlive.Count];
            if (!m.Alive)
                continue;
            if (casualties > 0)
            {
                m.Alive = false;
                m.HitPoints = 0;
                casualties--;
            }
            else
                m.HitPoints = Math.Max(1, (m.HitPoints > 0 ? m.HitPoints : Stats(m.UnitTypeId).MaxHitPoints) / 2);
        }
        List<PopulationMemberState> winnerAlive = winner.Members.Where(m => m.Alive).ToList();
        if (winnerAlive.Count > 0)
        {
            PopulationMemberState hurt = winnerAlive[random.Next(winnerAlive.Count)];
            int max = hurt.HitPoints > 0 ? hurt.HitPoints : Stats(hurt.UnitTypeId).MaxHitPoints;
            hurt.HitPoints = Math.Max(1, (int)Math.Round(max * 0.6));
        }

        Release(winner, t);
        Release(loser, t);
        if (!loser.Members.Any(m => m.Alive) && loser.Role != WorldMapGroupRule.Master)
            Destroy(state, loser, t, winner.Role);
        else
        {
            loser.CalmUntilHour = Math.Max(loser.CalmUntilHour, t + 12.0);
            if (loser.Role == WorldMapGroupRule.Caravan)
                loser.Outcome = "robbed";
        }
        winner.CalmUntilHour = Math.Max(winner.CalmUntilHour, t + 2.0);
        if (ctx.HeroOnMap && ctx.Grid.DistanceHexes(ctx.HeroX, ctx.HeroY, c.X, c.Y) <= WorldMapMovementRules.Current.FogRevealRadiusHexes + SightBonusHexes)
            ctx.Messages?.Add("Неподалёку: " + winner.Name.ToLowerInvariant() + " взяла верх над «" + loser.Name.ToLowerInvariant() + "».");
    }

    private static void Release(PopulationGroupState g, double t)
    {
        if (g == null)
            return;
        g.ConflictId = string.Empty;
        if (g.IsActive)
            StartActivity(g, PopulationGroupState.Return, string.Empty, t, 0);
    }
}
