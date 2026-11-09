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
    // строится и при любой ошибке под доской остаётся прежняя картинка поля.
    // Рисунок только читает тактическую раскладку и ничего в ней не меняет.
    public sealed class MapLabArenaDriver : IBattlefieldRelief
    {
        // Сколько секунд после готовности перерисовывать кадр каждый кадр
        // (догружаются материалы и трава), дальше — только при смене размера.
        private const float SettleSeconds = 1.5f;

        private readonly MapLabArena arena;
        private readonly BattlefieldView surface;
        private readonly HexBoardElement board;
        private Texture shown;
        private bool attached, disposed;
        private float readyAt = -1f;
        private Vector2Int lastSize;
        private float renderedAt = -1f;
        private static MapLabArenaDriver current;

        public MapLabArena Arena => arena;

        // Обычная арена 10 × 7 без боя на месте и без сетки локации.
        public static MapLabArenaDriver TryCreate(BattlefieldDefinitionData field, BattlefieldView surface, HexBoardElement board, SandboxBattle battle)
        {
            if (surface == null || board == null || battle == null || !MapLabArena.Available)
                return null;
            if (!SandboxArenaShape.MatchesDimensions(battle.Width, battle.Height))
                return null;
            return new MapLabArenaDriver(BuildSpec(field, battle), surface, board);
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

        private MapLabArenaDriver(MapLabArenaSpec spec, BattlefieldView surface, HexBoardElement board)
        {
            this.surface = surface;
            this.board = board;
            // На экране один бой — одна арена: прежняя (экран боя закрыт или
            // сцена выгружена без события отсоединения панели) освобождается.
            if (current != null && !current.disposed)
                current.Dispose();
            current = this;
            arena = MapLabArena.Begin(spec);
            surface.schedule.Execute(Tick).Every(0);
            surface.RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        public float LiftShare(Vector2 frameShare) => attached ? arena.LiftShare(frameShare) : 0f;

        private void Tick()
        {
            if (disposed)
                return;
            if (arena.Building)
            {
                arena.Advance();
                return;
            }
            if (!arena.Ready)
            {
                // Ошибка сборки уже в журнале: остаётся прежняя картинка поля.
                Dispose();
                return;
            }
            if (!attached)
            {
                attached = true;
                readyAt = Time.unscaledTime;
                surface.Relief = this;
                board.Relief = this;
            }
            Rect frame = surface.FrameRect;
            if (frame.width < 2f || frame.height < 2f)
                return;
            float scale = surface.panel != null ? surface.panel.scaledPixelsPerPoint : 1f;
            Vector2Int size = new Vector2Int(Mathf.RoundToInt(frame.width * scale), Mathf.RoundToInt(frame.height * scale));
            // Тот же размер: после готовности кадр догружается (не чаще 10 раз
            // в секунду), дальше не перерисовывается.
            if (size == lastSize && (Time.unscaledTime - readyAt > SettleSeconds || Time.unscaledTime - renderedAt < 0.1f))
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
            arena.Dispose();
        }
    }
}
