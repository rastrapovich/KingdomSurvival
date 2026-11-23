using System;
using System.Diagnostics;
using System.Text;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.MapLabBridge;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Обычная арена (дорожный бой, полигон) в стиле карты: под доской —
    // кадр маленького мира MapLab, собранного по принятой раскладке боя.
    // Клетки холма (Difficult) — видимый холм; сетка, отметки, фигуры и
    // выбор мышью поднимаются по той же высоте (IBattlefieldRelief). Пока мир
    // строится, поле закрыто экраном загрузки (прежняя картинка поля не
    // показывается, клики не проходят, ход противника ждёт). Ошибка сборки —
    // экран снимается, остаётся прежняя картинка. Рисунок только читает
    // тактическую раскладку и ничего в ней не меняет.
    public sealed class MapLabArenaDriver : IBattlefieldRelief
    {
        // Сколько секунд после готовности перерисовывать кадр (догружаются
        // материалы), дальше — только при смене размера.
        private const float SettleSeconds = 1.5f;
        // Время на шаги сборки за кадр: больше шагов за кадр — быстрее готово.
        private const double StepBudgetMs = 8.0;

        private readonly MapLabArena arena;
        private readonly BattlefieldView surface;
        private readonly HexBoardElement board;
        private readonly Action onReady;
        private Texture shown;
        private bool attached, disposed;
        private readonly bool preview;
        private int frameVersion = ArenaGeneratorAsset.Version;
        private ArenaFrameBinding frameBinding;
        public ArenaFrameBinding Frame => frameBinding;
        public bool Disposed => disposed;
        public bool Shown => shown != null;
        private float readyAt = -1f;
        private Vector2Int lastSize;
        private float renderedAt = -1f;
        private static MapLabArenaDriver current;

        private VisualElement loading, loadingFill;
        private Label loadingLabel;
        private float shownProgress;
        private float loadingStarted;
        // Время прошлой сборки (первой — типичное после прогрева).
        private static float expectedSeconds = 3f;

        public MapLabArena Arena => arena;

        // Поверхности клеток в краске земли: что заказано и что уже нарисовано.
        private readonly SandboxBattle battle;
        private readonly System.Collections.Generic.List<(HexCoord Cell, Vector2 Center)> cellCenters;
        private readonly System.Collections.Generic.Dictionary<HexCoord, ulong> surfacesSent = new System.Collections.Generic.Dictionary<HexCoord, ulong>();
        private bool surfacesPending;

        private static System.Collections.Generic.List<(HexCoord Cell, Vector2 Center)> ArenaCells(BattlefieldDefinitionData field)
        {
            BattlefieldGridLayout layout = BattlefieldFrame.ComputeLayout(new Rect(0f, 0f, BattlefieldFrame.Aspect, 1f), BattlefieldFrame.GetGridArea(field));
            System.Collections.Generic.List<(HexCoord, Vector2)> cells = new System.Collections.Generic.List<(HexCoord, Vector2)>();
            foreach (HexCoord cell in BattlefieldFrame.ActiveCells(field))
                cells.Add((cell, layout.GetCenter(cell.Q, cell.R)));
            return cells;
        }

        // Состояние боя → краска: сменившиеся поверхности перекрашиваются в
        // фоне; доска не рисует заливку у уже нарисованных краской.
        private void SyncSurfaces()
        {
            bool changed = false;
            foreach ((HexCoord cell, Vector2 _) in cellCenters)
            {
                ulong mask = SurfaceMask(battle, cell);
                surfacesSent.TryGetValue(cell, out ulong sent);
                if (mask != sent)
                {
                    changed = true;
                    break;
                }
            }
            if (changed)
            {
                System.Collections.Generic.List<(Vector2, ulong)> list = new System.Collections.Generic.List<(Vector2, ulong)>();
                foreach ((HexCoord cell, Vector2 center) in cellCenters)
                {
                    ulong mask = SurfaceMask(battle, cell);
                    surfacesSent[cell] = mask;
                    if (mask != 0UL)
                        list.Add((center, mask));
                }
                arena.SetSurfaces(list);
                surfacesPending = true;
            }
            if (arena.PumpSurfaces())
                RequestFrame();
            if (surfacesPending && arena.SurfacesSettled)
            {
                surfacesPending = false;
                board.PaintedSurfaces = VisibleSurfaces();
            }
        }

        // Что из заказанного видно в рисунке (на клетке реки — только вода).
        private System.Collections.Generic.Dictionary<HexCoord, ulong> VisibleSurfaces()
        {
            System.Collections.Generic.Dictionary<HexCoord, ulong> visible = new System.Collections.Generic.Dictionary<HexCoord, ulong>();
            foreach ((HexCoord cell, Vector2 center) in cellCenters)
                if (surfacesSent.TryGetValue(cell, out ulong mask) && mask != 0UL)
                    visible[cell] = arena.VisibleLayers(center, mask);
            return visible;
        }

        // Перерисовать кадр (окно генератора: сменилась обработка кадра).
        public void RequestRedraw() => RequestFrame();

        private void RequestFrame()
        {
            readyAt = Time.realtimeSinceStartup;
            renderedAt = -1f;
        }

        // Арена текущего экрана боя ещё строится (экран загрузки на месте).
        public static bool Loading => current != null && !current.disposed && current.shown == null;

        // Обычная арена 10 × 7 без боя на месте и без сетки локации.
        // onReady — арена показана (или сборка не удалась): бой может идти.
        // preview — предпросмотр окна «Генератор арен»: не занимает место
        // арены экрана боя (идущий бой не освобождается).
        public static MapLabArenaDriver TryCreate(BattlefieldDefinitionData field, BattlefieldView surface, HexBoardElement board, SandboxBattle battle,
            Action onReady = null, bool preview = false)
        {
            if (surface == null || board == null || battle == null || !MapLabArena.Available)
                return null;
            if (!SandboxArenaShape.MatchesDimensions(battle.Width, battle.Height))
                return null;
            board.ArtBackedObjects.Clear();
            foreach (SandboxBattleObject item in battle.TerrainObjects)
                if (item.Kind == SandboxObjectKind.Boulder || (SandboxTerrainObjects.Get(item.Kind).Indestructible && MapLabArtAssets.HasArenaObjectArt(ObjectKindId(item.Kind))))
                    board.ArtBackedObjects.UnionWith(item.Cells);
            return new MapLabArenaDriver(BuildSpec(field, battle), surface, board, battle, ArenaCells(field), onReady, preview);
        }

        // Раскладка боя → данные рисунка. Seed — от самой раскладки и поля:
        // та же раскладка даёт тот же рисунок.
        public static MapLabArenaSpec BuildSpec(BattlefieldDefinitionData field, SandboxBattle battle)
        {
            Rect gridArea = BattlefieldFrame.GetGridArea(field);
            BattlefieldGridLayout layout = BattlefieldFrame.ComputeLayout(new Rect(0f, 0f, BattlefieldFrame.Aspect, 1f), gridArea);
            // Рисунок — по общим настройкам «Генератора арен» (их запоминает игра).
            // У темы раскладки могут быть свои настройки рисунка (иначе стандартные).
            MapLabArenaLook look = (battle?.ArenaReport != null ? ArenaGeneratorAsset.LookFor(battle.ArenaReport.Theme) : ArenaGeneratorAsset.CurrentLook).Clone();
            MapLabArenaSpec spec = new MapLabArenaSpec
            {
                Aspect = BattlefieldFrame.Aspect,
                VerticalScale = layout.VerticalScale,
                HexNominal = layout.Size,
                Look = look,
                HillLiftHexes = look.HillLiftHexes,
                DecorDensity = look.DecorDensity,
                AssetScale = look.AssetScale,
                Frame = ArenaGeneratorAsset.CurrentFrame(),
            };
            StringBuilder key = new StringBuilder(field != null ? field.Id : string.Empty);
            foreach (HexCoord cell in BattlefieldFrame.ActiveCells(field))
            {
                SandboxTerrain terrain = battle.GetTerrain(cell);
                // Бриф «Поверхности арены», этап 5: валун местности — тем же
                // рисунком валуна MapLab, что непроходимая клетка.
                bool boulder = battle.TerrainObjectAt(cell)?.Kind == SandboxObjectKind.Boulder;
                MapLabArenaCellKind kind = terrain == SandboxTerrain.Difficult && !boulder ? MapLabArenaCellKind.Hill
                    : terrain == SandboxTerrain.Impassable || boulder ? MapLabArenaCellKind.Blocked
                    : MapLabArenaCellKind.Open;
                spec.Cells.Add((layout.GetCenter(cell.Q, cell.R), kind));
                key.Append((int)kind);
                ulong surfaces = SurfaceMask(battle, cell);
                if (surfaces != 0UL)
                    spec.Surfaces.Add((layout.GetCenter(cell.Q, cell.R), surfaces));
            }
            // Объекты местности (кроме валуна — он клеткой Blocked): слот рисунка.
            foreach (SandboxBattleObject item in battle.TerrainObjects)
            {
                // Разрушаемые (плетень, гнездо) — только доской: рисунок арены
                // не перестраивается при разрушении.
                if (item.Kind == SandboxObjectKind.Boulder || !SandboxTerrainObjects.Get(item.Kind).Indestructible)
                    continue;
                foreach (HexCoord cell in item.Cells)
                {
                    spec.Objects.Add((layout.GetCenter(cell.Q, cell.R), ObjectKindId(item.Kind)));
                    key.Append(ObjectKindId(item.Kind));
                }
            }
            // Стартовые клетки бойцов — без травы и мелочи поверх.
            foreach (SandboxUnitState unit in battle.Units)
                if (unit != null)
                    spec.KeepClear.Add(layout.GetCenter(unit.Position.Q, unit.Position.R));
            spec.Seed = StableHash(key.ToString());
            return spec;
        }

        // Поверхность основания, рисуемая краской арены (0 — луг как есть).
        public static MapLabArenaSurface GroundSurface(SandboxGround ground)
        {
            switch (ground)
            {
                case SandboxGround.Mud: return MapLabArenaSurface.Mud;
                case SandboxGround.Bog: return MapLabArenaSurface.Bog;
                case SandboxGround.ShallowWater: return MapLabArenaSurface.ShallowWater;
                case SandboxGround.DeepWater: return MapLabArenaSurface.DeepWater;
                case SandboxGround.Rubble: return MapLabArenaSurface.Rubble;
                case SandboxGround.DeepSnow: return MapLabArenaSurface.Snow;
                case SandboxGround.Ice: return MapLabArenaSurface.Ice;
                case SandboxGround.Cliff: return MapLabArenaSurface.Cliff;
                case SandboxGround.Road: return MapLabArenaSurface.Road;
                case SandboxGround.Sand: return MapLabArenaSurface.Sand;
                case SandboxGround.WetGround: return MapLabArenaSurface.WetGround;
                default: return 0;
            }
        }

        // Временная область, рисуемая краской арены (0 — остаётся значком доски).
        public static MapLabArenaSurface EffectSurface(SandboxEffectKind kind)
        {
            switch (kind)
            {
                case SandboxEffectKind.Fire: return MapLabArenaSurface.Fire;
                case SandboxEffectKind.Tar: return MapLabArenaSurface.Tar;
                case SandboxEffectKind.Poison: return MapLabArenaSurface.Poison;
                case SandboxEffectKind.Ice: return MapLabArenaSurface.Frost;
                default: return 0;
            }
        }

        // Все нарисованные краской слои клетки сейчас.
        public static ulong SurfaceMask(SandboxBattle battle, HexCoord cell)
        {
            ulong mask = 0UL;
            MapLabArenaSurface ground = GroundSurface(battle.GetGround(cell));
            if (ground != 0)
                mask |= MapLabArenaSurfaces.Bit(ground);
            if (battle.CellStates.TryGetValue(cell, out SandboxCellState state))
            {
                if ((state & SandboxCellState.ShallowWater) != 0) mask |= MapLabArenaSurfaces.Bit(MapLabArenaSurface.ShallowWater);
                if ((state & SandboxCellState.LooseEarth) != 0) mask |= MapLabArenaSurfaces.Bit(MapLabArenaSurface.LooseEarth);
                if ((state & SandboxCellState.Subsided) != 0) mask |= MapLabArenaSurfaces.Bit(MapLabArenaSurface.Subsided);
                if ((state & SandboxCellState.Pit) != 0) mask |= MapLabArenaSurfaces.Bit(MapLabArenaSurface.Pit);
            }
            foreach (SandboxCellEffect effect in battle.EffectsAt(cell))
            {
                MapLabArenaSurface surface = EffectSurface(effect.Kind);
                if (surface != 0)
                    mask |= MapLabArenaSurfaces.Bit(surface);
            }
            return mask;
        }

        // Вид объекта для слота Базы ассетов: FallenLog → fallen_log.
        public static string ObjectKindId(SandboxObjectKind kind) =>
            System.Text.RegularExpressions.Regex.Replace(kind.ToString(), "(?<=[a-z])([A-Z])", "_$1").ToLowerInvariant();

        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (char c in text)
                    hash = (hash ^ c) * 16777619;
                return hash & 0x7fffffff;
            }
        }

        private MapLabArenaDriver(MapLabArenaSpec spec, BattlefieldView surface, HexBoardElement board, SandboxBattle battle,
            System.Collections.Generic.List<(HexCoord Cell, Vector2 Center)> cells, Action onReady, bool preview)
        {
            this.preview = preview;
            this.surface = surface;
            this.board = board;
            this.onReady = onReady;
            this.battle = battle;
            cellCenters = cells;
            // На экране один бой — одна арена: прежняя (экран боя закрыт или
            // сцена выгружена без события отсоединения панели) освобождается.
            if (!preview)
            {
                if (current != null && !current.disposed)
                    current.Dispose();
                current = this;
            }
            arena = MapLabArena.Begin(spec);
            frameBinding = new ArenaFrameBinding(arena, board);
            CreateLoadingScreen();
            surface.schedule.Execute(Tick).Every(0);
            surface.RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        public float LiftShare(Vector2 frameShare) => attached ? arena.LiftShare(frameShare) : 0f;

        // Экран загрузки поверх поля: закрывает прежнюю картинку и доску,
        // ловит клики, показывает ход сборки.
        private void CreateLoadingScreen()
        {
            loading = new VisualElement { name = "arena-loading", pickingMode = PickingMode.Position };
            loading.style.position = Position.Absolute;
            loading.style.left = 0f;
            loading.style.right = 0f;
            loading.style.top = 0f;
            loading.style.bottom = 0f;
            loading.style.backgroundColor = new Color(0.16f, 0.15f, 0.12f, 1f);
            loading.style.alignItems = Align.Center;
            loading.style.justifyContent = Justify.Center;
            loadingLabel = new Label("Готовим поле боя…");
            loadingLabel.style.color = new Color(0.93f, 0.86f, 0.68f, 1f);
            loadingLabel.style.fontSize = 22f;
            loadingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            loadingLabel.style.marginBottom = 14f;
            loading.Add(loadingLabel);
            VisualElement track = new VisualElement();
            track.style.width = 320f;
            track.style.height = 8f;
            track.style.backgroundColor = new Color(0.30f, 0.27f, 0.21f, 1f);
            track.style.borderTopLeftRadius = track.style.borderTopRightRadius = 4f;
            track.style.borderBottomLeftRadius = track.style.borderBottomRightRadius = 4f;
            loadingFill = new VisualElement();
            loadingFill.style.height = 8f;
            loadingFill.style.width = 0f;
            loadingFill.style.backgroundColor = new Color(0.84f, 0.66f, 0.33f, 1f);
            loadingFill.style.borderTopLeftRadius = loadingFill.style.borderTopRightRadius = 4f;
            loadingFill.style.borderBottomLeftRadius = loadingFill.style.borderBottomRightRadius = 4f;
            track.Add(loadingFill);
            loading.Add(track);
            surface.Add(loading);
            loadingStarted = Time.realtimeSinceStartup;
        }

        private void UpdateLoadingScreen()
        {
            if (loading == null)
                return;
            // Доска добавляется в поле позже драйвера — экран всегда сверху.
            loading.BringToFront();
            // Прогресс — по времени прошлой сборки арены (оценка этапов MapLab
            // рассчитана на большую карту); не откатывается и не доходит до
            // конца раньше готовности.
            float byTime = (Time.realtimeSinceStartup - loadingStarted) / Mathf.Max(0.5f, expectedSeconds) * 0.95f;
            float target = Mathf.Min(0.97f, Mathf.Max(arena.Progress, byTime));
            shownProgress = Mathf.Max(shownProgress, Mathf.Lerp(shownProgress, target, 0.25f));
            loadingFill.style.width = 320f * Mathf.Clamp01(shownProgress);
        }

        private void RemoveLoadingScreen()
        {
            if (loading == null)
                return;
            loading.RemoveFromHierarchy();
            loading = null;
            onReady?.Invoke();
        }

        private void Tick()
        {
            if (disposed)
                return;
            if (arena.Building)
            {
                // Несколько шагов сборки за кадр в пределах бюджета.
                Stopwatch watch = Stopwatch.StartNew();
                while (arena.Building && watch.Elapsed.TotalMilliseconds < StepBudgetMs)
                    arena.Advance();
                UpdateLoadingScreen();
                if (arena.Building)
                    return;
            }
            if (!arena.Ready)
            {
                // Ошибка сборки уже в журнале: экран загрузки снят, остаётся
                // прежняя картинка поля.
                Dispose();
                return;
            }
            UpdateLoadingScreen();
            if (attached)
                SyncSurfaces();
            if (frameVersion != ArenaGeneratorAsset.Version)
            {
                frameVersion = ArenaGeneratorAsset.Version;
                RequestFrame();
            }
            Rect frame = surface.FrameRect;
            if (frame.width < 2f || frame.height < 2f)
                return;
            float scale = surface.panel != null ? surface.panel.scaledPixelsPerPoint : 1f;
            Vector2Int size = new Vector2Int(Mathf.RoundToInt(frame.width * scale), Mathf.RoundToInt(frame.height * scale));
            // Тот же размер: после готовности кадр догружается (не чаще 10 раз
            // в секунду), дальше не перерисовывается.
            // Обработка кадра со временем (вспышка, тряска, плёнка, облака) — кадр
            // каждые ~1/30 с, пока она идёт.
            // Идущий боец гнёт траву: кадр каждые ~1/30 с, пока он идёт и трава возвращается.
            bool walking = UpdateWalkers();
            UpdateBreathSources();
            // Река течёт (штрихи воды плывут и пересобираются): кадр каждые ~1/30 с.
            bool animating = (frameBinding != null && frameBinding.Animating) || walking || arena.HasRiver || arena.ParticlesActive;
            if (shown != null && size == lastSize && (animating ? AnimationWaiting()
                    : Time.realtimeSinceStartup - readyAt > SettleSeconds || Time.realtimeSinceStartup - renderedAt < 0.1f))
                return;
            renderedAt = Time.realtimeSinceStartup;
            lastSize = size;
            // Сам кадр рисуется в LateUpdate (ArenaRenderPump), а не во время
            // обновления панелей UI: на D3D12 рендер камеры посреди обновления
            // панели, которая рисует ту же текстуру, вешал устройство.
            requestedSize = size;
            ArenaRenderPump.Request(this);
        }

        private Vector2Int requestedSize;

        // ---------- нагрузка анимированных кадров ----------
        // Кадр арены — это 3 + 2 × (строк бойцов) проходов камеры; со временем он
        // перерисовывается каждые ~1/30 с (ветер, облака, зерно, река, частицы), и
        // при кадре дороже 33 мс (а он 45–150 мс, на 4K — до 300) игра стояла в
        // очереди за ним весь Play Mode. Теперь: не больше одного кадра в работе у
        // видеокарты (AsyncGPUReadback отвечает, когда она закончила) и пауза после
        // кадра, кратная его реальной цене, — арена занимает не более ~1/3 времени,
        // остальное — интерфейсу.
        private const float AnimationFrameSeconds = 1f / 30f;
        private const float AnimationLoadShare = 3f;
        private const float AnimationPendingLimit = 1f;
        private bool gpuPending;
        private float gpuSubmittedAt;
        private float cpuCostSeconds, gpuCostSeconds;
        // Полная цена кадра: ЦП на отправку + то, что видеокарта доделывает после (без ответа — по ЦП вдвое).
        private float frameCostSeconds => cpuCostSeconds + (gpuCostSeconds > 0f ? gpuCostSeconds : cpuCostSeconds);

        // true — до следующего анимированного кадра ещё рано.
        private bool AnimationWaiting()
        {
            float now = Time.realtimeSinceStartup;
            if (gpuPending && now - gpuSubmittedAt < AnimationPendingLimit)
                return true;
            return now - renderedAt < Mathf.Max(AnimationFrameSeconds, AnimationLoadShare * frameCostSeconds);
        }

        // Цена только что отправленного кадра: ЦП — сразу, видеокарта — когда ответит чтение 1 пикселя.
        private void TrackFrameCost(Texture last, float cpuSeconds)
        {
            cpuCostSeconds = cpuCostSeconds <= 0f ? cpuSeconds : Mathf.Lerp(cpuCostSeconds, cpuSeconds, 0.5f);
            if (last == null || gpuPending || !SystemInfo.supportsAsyncGPUReadback)
                return;
            gpuPending = true;
            float submitted = gpuSubmittedAt = Time.realtimeSinceStartup;
            UnityEngine.Rendering.AsyncGPUReadback.Request(last, 0, 0, 1, 0, 1, 0, 1, request =>
            {
                if (disposed) return;
                gpuPending = false;
                if (request.hasError) return;
                float seconds = Time.realtimeSinceStartup - submitted;
                gpuCostSeconds = gpuCostSeconds <= 0f ? seconds : Mathf.Lerp(gpuCostSeconds, seconds, 0.5f);
            });
        }

        // ---------- объекты впереди бойцов ----------

        private readonly System.Collections.Generic.List<(float KeyY, float ShareY)> frontRowsSorted = new System.Collections.Generic.List<(float, float)>();
        private readonly System.Collections.Generic.List<(float KeyY, Texture Texture)> frontOut = new System.Collections.Generic.List<(float, Texture)>();

        // Для каждой строки земли с бойцами — слой объектов, стоящих ближе к зрителю.
        private void RenderFronts()
        {
            frontOut.Clear();
            frontRowsSorted.Clear();
            frontRowsSorted.AddRange(board.FrontRows);
            frontRowsSorted.Sort((a, b) => a.KeyY.CompareTo(b.KeyY));
            float last = float.NegativeInfinity;
            int slot = 0;
            foreach ((float keyY, float shareY) in frontRowsSorted)
            {
                if (keyY - last < 1.5f) continue;
                last = keyY;
                RenderTexture layer = arena.RenderFront(slot, arena.GroundZ(shareY));
                if (layer == null) break;
                frontOut.Add((keyY, layer));
                slot++;
            }
            board.SetFrontLayers(frontOut);
        }

        // ---------- частицы атмосферы ----------

        private readonly System.Collections.Generic.List<Vector2> headBuffer = new System.Collections.Generic.List<Vector2>();

        // Пар изо рта: головы живых бойцов (кадр поля) — только когда слой включён.
        private void UpdateBreathSources()
        {
            if (!arena.WantsHeads)
            {
                if (headBuffer.Count > 0)
                {
                    headBuffer.Clear();
                    arena.SetHeads(headBuffer);
                }
                return;
            }
            headBuffer.Clear();
            foreach (SandboxUnitState unit in battle.Units)
            {
                if (unit.IsDefeated || unit.IsOffBoard) continue;
                foreach ((HexCoord cell, Vector2 center) in cellCenters)
                    if (cell == unit.Position)
                    {
                        headBuffer.Add(center + new Vector2(0f, -1.3f * arena.Spec.HexNominal));
                        break;
                    }
            }
            arena.SetHeads(headBuffer);
        }

        // ---------- проход по траве ----------

        private const float TrailSeconds = 0.9f;
        private const float TrailStep = 0.1f;
        private const int TrailMax = 5;
        private readonly System.Collections.Generic.List<(Vector2 Nominal, float Time)> trail = new System.Collections.Generic.List<(Vector2, float)>();
        private readonly System.Collections.Generic.List<(Vector2 Nominal, float Strength)> walkerPoints = new System.Collections.Generic.List<(Vector2, float)>();
        private bool walkersWereActive;

        // Ноги идущего бойца и следы за ним → арена (трава отклоняется и
        // возвращается). true — трава сейчас в движении (кадр обновляется часто).
        private bool UpdateWalkers()
        {
            float now = Time.realtimeSinceStartup;
            Vector2 share = default;
            bool walking = board != null && board.TryGetWalkerFrameShare(out share);
            Vector2 current = default;
            if (walking)
            {
                current = new Vector2(share.x * arena.Spec.Aspect, share.y);
                if (trail.Count == 0 || now - trail[trail.Count - 1].Time >= TrailStep)
                {
                    trail.Add((current, now));
                    if (trail.Count > TrailMax)
                        trail.RemoveAt(0);
                }
            }
            for (int i = trail.Count - 1; i >= 0; i--)
                if (now - trail[i].Time > TrailSeconds)
                    trail.RemoveAt(i);

            walkerPoints.Clear();
            if (walking)
                walkerPoints.Add((current, 1f));
            for (int i = trail.Count - 1; i >= 0; i--)
                walkerPoints.Add((trail[i].Nominal, 0.8f * (1f - (now - trail[i].Time) / TrailSeconds)));
            arena.SetWalkers(walkerPoints);

            bool active = walkerPoints.Count > 0;
            if (walkersWereActive && !active)
                RequestFrame();   // трава вернулась — последний кадр без изгиба
            walkersWereActive = active;
            return active;
        }

        // Из LateUpdate: нарисовать запрошенный кадр и показать его.
        internal void RenderRequested()
        {
            if (disposed || !arena.Ready || requestedSize.x < 8)
                return;
            Stopwatch frameWatch = Stopwatch.StartNew();
            // Обработка кадра арены — общая (ArenaGeneratorAsset).
            arena.Spec.Frame = ArenaGeneratorAsset.CurrentFrame();
            frameBinding?.BeforeRender();
            RenderTexture target = arena.Render(requestedSize.x, requestedSize.y);
            if (target != null && shown != target)
            {
                shown = target;
                surface.SetSceneTexture(target);
            }
            if (!attached && shown != null)
            {
                // Первый кадр показан: рельеф доски и поля, экран загрузки снят.
                attached = true;
                readyAt = Time.realtimeSinceStartup;
                expectedSeconds = Mathf.Clamp(Time.realtimeSinceStartup - loadingStarted, 1f, 15f);
                surface.Relief = this;
                board.Relief = this;
                // Краска собрана по раскладке (spec.Surfaces) — она и нарисована.
                foreach ((HexCoord cell, Vector2 _) in cellCenters)
                    surfacesSent[cell] = SurfaceMask(battle, cell);
                board.PaintedSurfaces = VisibleSurfaces();
                RemoveLoadingScreen();
            }
            board.ShadowDirection = arena.ShadowScreenDirection();
            arena.ShadowScreenAxes(out Vector2 shadowAlong, out Vector2 shadowAcross);
            board.ShadowAlong = shadowAlong;
            board.ShadowAcross = shadowAcross;
            RenderFronts();
            board.SetTerrainOverlay(arena.OverlayTexture);
            TrackFrameCost(frontOut.Count > 0 ? frontOut[frontOut.Count - 1].Texture : arena.OverlayTexture != null ? arena.OverlayTexture : shown, (float)frameWatch.Elapsed.TotalSeconds);
            board.MarkDirtyRepaint();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (current == this)
                current = null;
            if (surface.Relief == this)
                surface.Relief = null;
            if (board.Relief == this)
            {
                board.Relief = null;
                board.SetTerrainOverlay(null);
                board.PaintedSurfaces = null;
            }
            if (shown != null)
                surface.SetSceneTexture(null);
            RemoveLoadingScreen();
            frameBinding?.Dispose();
            arena.Dispose();
        }
    }
}
