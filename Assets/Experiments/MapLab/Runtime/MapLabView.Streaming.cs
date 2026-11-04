using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // 12У-C: подгрузка покраски. При генерации вся карта красится облегчённым
    // обзором (несколько текселей на единицу); подробные тайлы рабочей
    // плотности красятся в фоне только для участков в кадре и рядом, когда
    // экранная плотность выше обзорной. Готовые применяются по нескольку за
    // кадр; сверх бюджета вытесняются давно не нужные (вместо них — обзор).
    // Устаревшие результаты (после перекраски) отбрасываются по ревизии.
    public sealed partial class MapLabView
    {
        private MapLabTiledPainter.Session paintSession;
        private readonly List<Texture2D> lowTiles = new List<Texture2D>();
        private readonly List<Vector4> lowRects = new List<Vector4>();
        private readonly Dictionary<int, double> detailUsed = new Dictionary<int, double>();
        private readonly HashSet<int> detailPending = new HashSet<int>();
        private readonly ConcurrentQueue<(int Index, int Revision, MapLabTiledPainter.Tile Tile)> detailReady = new ConcurrentQueue<(int, int, MapLabTiledPainter.Tile)>();
        private int streamRevision;
        private double streamClock;

        public bool StreamingActive => paintSession != null;

        // Решение о подгрузке: режим профиля (авто — сторона данных больше 600 ед.).
        private bool WantsStreaming()
        {
            if (Settings == null || !Settings.IsStage05)
                return false;
            switch (Settings.Stage05.PaintStreaming)
            {
                case MapLabPaintStreaming.On: return true;
                case MapLabPaintStreaming.Off: return false;
                default: return data != null && data.Size > 600f;
            }
        }

        private void RepaintStreaming(MapLabTiledPainter.Options options)
        {
            DestroyStreaming();
            paintSession = MapLabTiledPainter.Prepare(data, Settings, Palette, tileWorld, options);
            float low = Mathf.Clamp(Settings.Stage05.OverviewTexels, 1f, 8f);
            MapLabTiledPainter.Result result = MapLabTiledPainter.PaintAll(paintSession, low);
            UploadTiles(result);
            lowTiles.Clear();
            lowRects.Clear();
            lowTiles.AddRange(paintTiles);
            lowRects.AddRange(paintRects);
            streamRevision++;
        }

        // Снять подробные тайлы и сессию (перед перекраской и очисткой).
        private void DestroyStreaming()
        {
            streamRevision++;
            foreach (int index in detailUsed.Keys)
            {
                if (index < paintTiles.Count && index < lowTiles.Count && paintTiles[index] != lowTiles[index])
                {
                    owned.Remove(paintTiles[index]);
                    DestroySafe(paintTiles[index]);
                    paintTiles[index] = lowTiles[index];
                    paintRects[index] = lowRects[index];
                }
            }
            detailUsed.Clear();
            detailPending.Clear();
            while (detailReady.TryDequeue(out _)) { }
            lowTiles.Clear();
            lowRects.Clear();
            paintSession = null;
        }

        private void Update()
        {
            if (paintSession != null)
                PumpStreaming(false);
        }

        // Нужные подробные тайлы, заказ покраски, применение и вытеснение.
        // blocking — дождаться покраски всех нужных (замеры и экспорт).
        public void PumpStreaming(bool blocking)
        {
            if (paintSession == null || CameraRig == null || data == null)
                return;
            streamClock += 1.0;
            MapLabStage05Settings s5 = Settings.Stage05;
            float detail = Settings.Stage03.PaintTexelsPerUnit;
            float low = Mathf.Clamp(s5.OverviewTexels, 1f, 8f);
            Camera cam = CameraRig.Camera;
            List<int> want = new List<int>();
            float pxPerUnit = cam.pixelHeight / Mathf.Max(0.01f, 2f * cam.orthographicSize);
            if (pxPerUnit > low * 1.3f)
            {
                float half = cam.orthographicSize;
                float halfWidth = half * Mathf.Max(0.1f, cam.aspect);
                float halfDepth = half / Mathf.Max(0.2f, Mathf.Sin(Mathf.Clamp(CameraRig.Pitch, 5f, 90f) * Mathf.Deg2Rad));
                Quaternion rotation = CameraRig.Rotation();
                Vector3 right = rotation * Vector3.right;
                Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up).normalized;
                float ex = Mathf.Abs(right.x) * halfWidth + Mathf.Abs(forward.x) * halfDepth + tileWorld * 0.5f;
                float ez = Mathf.Abs(right.z) * halfWidth + Mathf.Abs(forward.z) * halfDepth + tileWorld * 0.5f;
                Vector3 t = CameraRig.Target;
                int n = paintSession.TilesPerSide;
                int x0 = Mathf.Clamp(Mathf.FloorToInt((t.x - ex) / tileWorld), 0, n - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((t.x + ex) / tileWorld), 0, n - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((t.z - ez) / tileWorld), 0, n - 1), z1 = Mathf.Clamp(Mathf.FloorToInt((t.z + ez) / tileWorld), 0, n - 1);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        want.Add(z * n + x);
                Vector2 c = new Vector2(t.x, t.z);
                want.Sort((a, b) => Distance(a, c).CompareTo(Distance(b, c)));
            }
            foreach (int index in want)
                if (detailUsed.ContainsKey(index))
                    detailUsed[index] = streamClock;

            // Заказ: ближние первыми; в фоне — не больше нескольких задач сразу.
            int revision = streamRevision;
            MapLabTiledPainter.Session session = paintSession;
            List<int> missing = want.FindAll(i => !detailUsed.ContainsKey(i) && !detailPending.Contains(i));
            if (blocking)
            {
                MapLabTiledPainter.Tile[] tiles = new MapLabTiledPainter.Tile[missing.Count];
                Parallel.For(0, missing.Count, k =>
                {
                    int n = session.TilesPerSide;
                    MapLabTiledPainter.Tile tile = MapLabTiledPainter.MakeTile(session, missing[k] % n, missing[k] / n, detail);
                    tile.Pixels = MapLabTiledPainter.PaintTile(session, tile);
                    tiles[k] = tile;
                });
                for (int k = 0; k < missing.Count; k++)
                    detailReady.Enqueue((missing[k], revision, tiles[k]));
            }
            else
            {
                int inFlight = Mathf.Clamp(System.Environment.ProcessorCount - 2, 1, 6);
                foreach (int index in missing)
                {
                    if (detailPending.Count >= inFlight)
                        break;
                    detailPending.Add(index);
                    int n = session.TilesPerSide;
                    Task.Run(() =>
                    {
                        MapLabTiledPainter.Tile tile = MapLabTiledPainter.MakeTile(session, index % n, index / n, detail);
                        tile.Pixels = MapLabTiledPainter.PaintTile(session, tile);
                        detailReady.Enqueue((index, revision, tile));
                    });
                }
            }

            // Применение: по нескольку за кадр (загрузка текстур — на главном потоке).
            int apply = blocking ? int.MaxValue : Mathf.Max(1, s5.DetailTilesPerFrame);
            bool changed = false;
            while (apply-- > 0 && detailReady.TryDequeue(out (int Index, int Revision, MapLabTiledPainter.Tile Tile) item))
            {
                detailPending.Remove(item.Index);
                if (item.Revision != streamRevision || item.Index >= paintTiles.Count)
                    continue;
                if (detailUsed.ContainsKey(item.Index))
                    continue;
                paintTiles[item.Index] = MakeTileTexture(item.Tile);
                paintRects[item.Index] = item.Tile.Rect;
                item.Tile.Pixels = null;
                detailUsed[item.Index] = streamClock;
                changed = true;
            }

            // Вытеснение сверх бюджета: давно не нужные, кроме тех, что в кадре.
            int budget = Mathf.Max(want.Count, s5.DetailTileBudget);
            if (detailUsed.Count > budget)
            {
                HashSet<int> keep = new HashSet<int>(want);
                List<KeyValuePair<int, double>> order = new List<KeyValuePair<int, double>>(detailUsed);
                order.Sort((a, b) => a.Value.CompareTo(b.Value));
                foreach (KeyValuePair<int, double> pair in order)
                {
                    if (detailUsed.Count <= budget)
                        break;
                    if (keep.Contains(pair.Key))
                        continue;
                    owned.Remove(paintTiles[pair.Key]);
                    DestroySafe(paintTiles[pair.Key]);
                    paintTiles[pair.Key] = lowTiles[pair.Key];
                    paintRects[pair.Key] = lowRects[pair.Key];
                    detailUsed.Remove(pair.Key);
                    changed = true;
                }
            }
            if (changed)
                ApplyPaintBlocks();
        }

        private float Distance(int index, Vector2 c)
        {
            int n = paintSession.TilesPerSide;
            Vector2 center = new Vector2((index % n + 0.5f) * tileWorld, (index / n + 0.5f) * tileWorld);
            return (center - c).sqrMagnitude;
        }

        private Texture2D MakeTileTexture(MapLabTiledPainter.Tile tile)
        {
            Texture2D t = Own(new Texture2D(tile.Size, tile.Size, TextureFormat.RGBA32, true, false)
            {
                name = $"MapLab Paint {tile.X},{tile.Z}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = Settings.IsStage05 ? Mathf.Clamp(Settings.Stage05.GroundAniso, 1, 16) : 8,
            });
            t.SetPixels32(tile.Pixels);
            t.Apply(true, true);
            return t;
        }
    }
}
