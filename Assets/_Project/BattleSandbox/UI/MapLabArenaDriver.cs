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

        // Арена текущего экрана боя ещё строится (экран загрузки на месте).
        public static bool Loading => current != null && !current.disposed && current.shown == null;

        // Обычная арена 10 × 7 без боя на месте и без сетки локации.
        // onReady — арена показана (или сборка не удалась): бой может идти.
        public static MapLabArenaDriver TryCreate(BattlefieldDefinitionData field, BattlefieldView surface, HexBoardElement board, SandboxBattle battle,
            Action onReady = null)
        {
            if (surface == null || board == null || battle == null || !MapLabArena.Available)
                return null;
            if (!SandboxArenaShape.MatchesDimensions(battle.Width, battle.Height))
                return null;
            return new MapLabArenaDriver(BuildSpec(field, battle), surface, board, onReady);
        }

        // Раскладка боя → данные рисунка. Seed — от самой раскладки и поля:
        // та же раскладка даёт тот же рисунок.
        public static MapLabArenaSpec BuildSpec(BattlefieldDefinitionData field, SandboxBattle battle)
        {
            Rect gridArea = BattlefieldFrame.GetGridArea(field);
            BattlefieldGridLayout layout = BattlefieldFrame.ComputeLayout(new Rect(0f, 0f, BattlefieldFrame.Aspect, 1f), gridArea);
            MapLabArenaSpec spec = new MapLabArenaSpec
            {
                Aspect = BattlefieldFrame.Aspect,
                VerticalScale = layout.VerticalScale,
                HexNominal = layout.Size,
            };
            StringBuilder key = new StringBuilder(field != null ? field.Id : string.Empty);
            foreach (HexCoord cell in BattlefieldFrame.ActiveCells(field))
            {
                SandboxTerrain terrain = battle.GetTerrain(cell);
                MapLabArenaCellKind kind = terrain == SandboxTerrain.Difficult ? MapLabArenaCellKind.Hill
                    : terrain == SandboxTerrain.Impassable ? MapLabArenaCellKind.Blocked
                    : MapLabArenaCellKind.Open;
                spec.Cells.Add((layout.GetCenter(cell.Q, cell.R), kind));
                key.Append((int)kind);
            }
            // Стартовые клетки бойцов — без травы и мелочи поверх.
            foreach (SandboxUnitState unit in battle.Units)
                if (unit != null)
                    spec.KeepClear.Add(layout.GetCenter(unit.Position.Q, unit.Position.R));
            spec.Seed = StableHash(key.ToString());
            return spec;
        }

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

        private MapLabArenaDriver(MapLabArenaSpec spec, BattlefieldView surface, HexBoardElement board, Action onReady)
        {
            this.surface = surface;
            this.board = board;
            this.onReady = onReady;
            // На экране один бой — одна арена: прежняя (экран боя закрыт или
            // сцена выгружена без события отсоединения панели) освобождается.
            if (current != null && !current.disposed)
                current.Dispose();
            current = this;
            arena = MapLabArena.Begin(spec);
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
            Rect frame = surface.FrameRect;
            if (frame.width < 2f || frame.height < 2f)
                return;
            float scale = surface.panel != null ? surface.panel.scaledPixelsPerPoint : 1f;
            Vector2Int size = new Vector2Int(Mathf.RoundToInt(frame.width * scale), Mathf.RoundToInt(frame.height * scale));
            // Тот же размер: после готовности кадр догружается (не чаще 10 раз
            // в секунду), дальше не перерисовывается.
            if (shown != null && size == lastSize && (Time.unscaledTime - readyAt > SettleSeconds || Time.unscaledTime - renderedAt < 0.1f))
                return;
            renderedAt = Time.unscaledTime;
            lastSize = size;
            // Сам кадр рисуется в LateUpdate (ArenaRenderPump), а не во время
            // обновления панелей UI: на D3D12 рендер камеры посреди обновления
            // панели, которая рисует ту же текстуру, вешал устройство.
            requestedSize = size;
            ArenaRenderPump.Request(this);
        }

        private Vector2Int requestedSize;

        // Из LateUpdate: нарисовать запрошенный кадр и показать его.
        internal void RenderRequested()
        {
            if (disposed || !arena.Ready || requestedSize.x < 8)
                return;
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
                readyAt = Time.unscaledTime;
                expectedSeconds = Mathf.Clamp(Time.realtimeSinceStartup - loadingStarted, 1f, 15f);
                surface.Relief = this;
                board.Relief = this;
                RemoveLoadingScreen();
            }
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
                board.Relief = null;
            if (shown != null)
                surface.SetSceneTexture(null);
            RemoveLoadingScreen();
            arena.Dispose();
        }
    }
}
