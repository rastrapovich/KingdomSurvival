using KingdomSurvival.ArtAssets;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // Прогрев арены MapLab заранее (меню полигона, карта партии): первая
    // сборка в сеансе в 2–3 раза дольше следующих — загрузка каталога Базы
    // ассетов с текстурами и холодный старт генератора и покраски. Прогрев
    // делает это фоном до боя: каталог грузится асинхронно, маленькая арена
    // строится по шагам (без рендера) и освобождается. Один раз за сеанс;
    // настоящая арена отменяет незаконченный прогрев.
    public sealed class MapLabArenaPrewarm : MonoBehaviour
    {
        private const double StepBudgetMs = 4.0;
        private static MapLabArenaPrewarm running;
        private static bool done;

        private ResourceRequest catalogRequest;
        private MapLabArena arena;

        public static bool Done => done;

        public static void Begin()
        {
            if (done || running != null || !MapLabArena.Available || !Application.isPlaying)
                return;
            GameObject host = new GameObject("MapLab Arena Prewarm") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(host);
            running = host.AddComponent<MapLabArenaPrewarm>();
            running.catalogRequest = Resources.LoadAsync<ArtAssetDatabaseAsset>(ArtAssetDatabaseAsset.ResourcesPath);
        }

        // Настоящая арена начинается: прогрев больше не нужен (и не отнимает
        // время у её сборки).
        public static void Cancel()
        {
            if (running != null)
                Destroy(running.gameObject);
        }

        private void Update()
        {
            if (catalogRequest != null && !catalogRequest.isDone)
                return;
            if (arena == null)
            {
                arena = MapLabArena.BeginWarmup(WarmupSpec());
                return;
            }
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            while (arena.Building && watch.Elapsed.TotalMilliseconds < StepBudgetMs)
                arena.Advance();
            if (arena.Building)
                return;
            Debug.Log($"MapLab: прогрев арены за {arena.BuildMs / 1000.0:0.0} с.");
            done = true;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            arena?.Dispose();
            arena = null;
            if (running == this)
                running = null;
        }

        // Небольшая раскладка обычной арены: ряды клеток, пара холмов и камень.
        private static MapLabArenaSpec WarmupSpec()
        {
            MapLabArenaSpec spec = new MapLabArenaSpec { Seed = 1, HexNominal = 0.08f };
            float width = Mathf.Sqrt(3f) * spec.HexNominal;
            for (int r = 0; r < 7; r++)
                for (int q = 0; q < 10; q++)
                {
                    Vector2 center = new Vector2(0.2f + width * (q + 0.5f * (r & 1)), 0.18f + 1.5f * spec.HexNominal * spec.VerticalScale * r);
                    MapLabArenaCellKind kind = (q == 3 && r == 3) || (q == 4 && r == 3) ? MapLabArenaCellKind.Hill
                        : q == 7 && r == 2 ? MapLabArenaCellKind.Blocked : MapLabArenaCellKind.Open;
                    spec.Cells.Add((center, kind));
                }
            return spec;
        }
    }
}
