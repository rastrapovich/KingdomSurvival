using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Поверхности поверх луга (SurfacePaint) и перекраска участка после их
    // смены (боевая арена: лёд растаял, разлили смолу). Тайлы, задевающие
    // участок, красятся заново в фоне той же сессией покраски и подменяются
    // на главном потоке (PumpRepaint). Устаревший результат (тайл заказан
    // снова) отбрасывается. Только для покраски без подгрузки.
    public sealed partial class MapLabView
    {
        // Источник поверхностей для покраски; задаётся до сборки.
        public IMapLabSurfacePaint SurfacePaint { get; set; }

        private MapLabTiledPainter.Session repaintSession;
        private readonly Dictionary<int, int> repaintOrder = new Dictionary<int, int>();
        private readonly ConcurrentQueue<(int Index, int Order, MapLabTiledPainter.Tile Tile)> repaintReady = new ConcurrentQueue<(int, int, MapLabTiledPainter.Tile)>();
        private volatile int repaintEpoch;
        private int repaintCounter;

        public bool RepaintPending => repaintOrder.Count > 0;

        // Заказать перекраску тайлов, задевающих участок мира (x, z). Число заказанных тайлов.
        public int RepaintArea(Rect world)
        {
            if (data == null || Settings == null || !Settings.IsStage03 || paintTiles.Count == 0 || StreamingActive)
                return 0;
            if (repaintSession == null)
            {
                MapLabTiledPainter.Options options = TiledOptions(false);
                options.Washes = washLayer;
                repaintSession = MapLabTiledPainter.Prepare(data, Settings, Palette, tileWorld, options, paintFields);
            }
            MapLabTiledPainter.Session session = repaintSession;
            int n = session.TilesPerSide;
            float reach = session.MarginUnits + 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((world.xMin - reach) / tileWorld), 0, n - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((world.xMax + reach) / tileWorld), 0, n - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((world.yMin - reach) / tileWorld), 0, n - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((world.yMax + reach) / tileWorld), 0, n - 1);
            float texels = Settings.Stage03.PaintTexelsPerUnit;
            int epoch = repaintEpoch, count = 0;
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int index = z * n + x;
                if (index >= paintTiles.Count)
                    continue;
                int order = ++repaintCounter;
                repaintOrder[index] = order;
                int tx = x, tz = z;
                count++;
                Task.Run(() =>
                {
                    MapLabTiledPainter.Tile tile = MapLabTiledPainter.MakeTile(session, tx, tz, texels);
                    tile.Pixels = MapLabTiledPainter.PaintTile(session, tile);
                    if (epoch == repaintEpoch)
                        repaintReady.Enqueue((index, order, tile));
                });
            }
            return count;
        }

        // Подменить готовые тайлы (главный поток). true — краска изменилась.
        public bool PumpRepaint()
        {
            bool changed = false;
            while (repaintReady.TryDequeue(out (int Index, int Order, MapLabTiledPainter.Tile Tile) item))
            {
                if (!repaintOrder.TryGetValue(item.Index, out int latest) || latest != item.Order || item.Index >= paintTiles.Count)
                    continue;
                repaintOrder.Remove(item.Index);
                Texture2D old = paintTiles[item.Index];
                paintTiles[item.Index] = MakeTileTexture(item.Tile);
                paintRects[item.Index] = item.Tile.Rect;
                item.Tile.Pixels = null;
                if (old != null)
                {
                    owned.Remove(old);
                    DestroySafe(old);
                }
                changed = true;
            }
            if (changed)
                ApplyPaintBlocks();
            return changed;
        }

        // Покраска заменена целиком: прежняя сессия и заказы устарели.
        private void ResetRepaint()
        {
            repaintEpoch++;
            repaintSession = null;
            repaintOrder.Clear();
            while (repaintReady.TryDequeue(out _)) { }
        }
    }
}
