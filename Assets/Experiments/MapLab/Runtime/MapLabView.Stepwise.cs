using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // 12У-G: пошаговая сборка карты для игры. Тяжёлые чистые расчёты (данные
    // карты, пиксели масок, покраска, тени объектов, геометрия пера) идут в
    // фоновом потоке, а Unity-объекты (текстуры, меши, материалы) создаются
    // в главном между кадрами — игра не «зависает» на десятки секунд.
    // Синхронная сборка (предпросмотр, пакетные проверки) идёт теми же шагами
    // в вызывающем потоке, поэтому результат у обеих одинаков.
    public sealed partial class MapLabView
    {
        private Color32[] pendingMaskA, pendingMaskB, pendingShade;
        private bool deferObjectShadows;

        // Текущий шаг пошаговой сборки (для подписи «Рисуем карту…»).
        private string buildStage = string.Empty;
        public string BuildStage
        {
            get => buildStage;
            private set
            {
                buildStage = value ?? string.Empty;
                int index = System.Array.IndexOf(StageNames, buildStage);
                if (index >= 0)
                {
                    stageIndex = index;
                    stageWatch.Restart();
                }
            }
        }

        // Доля готовности сборки 0..1 для полоски загрузки: шаги весят по
        // обычной длительности, внутри шага доля растёт по времени (не
        // дальше 95% шага, пока он не закончится).
        private static readonly string[] StageNames =
            { "земля, вода, дороги и поселения", "подготовка рельефа", "покраска", "рельеф, вода и скалы", "деревья, дома и мелочи", "тени", "перо", "трава и материалы" };
        private static readonly float[] StageSeconds = { 12f, 3f, 18f, 0.5f, 1.5f, 13f, 6f, 0.5f };
        private int stageIndex = -1;
        private readonly Stopwatch stageWatch = new Stopwatch();

        public float BuildProgress
        {
            get
            {
                if (stageIndex < 0)
                    return 0f;
                float total = 0f, done = 0f;
                for (int i = 0; i < StageSeconds.Length; i++)
                {
                    total += StageSeconds[i];
                    if (i < stageIndex)
                        done += StageSeconds[i];
                }
                float inStage = Mathf.Min((float)stageWatch.Elapsed.TotalSeconds / StageSeconds[stageIndex], 0.95f) * StageSeconds[stageIndex];
                return Mathf.Clamp01((done + inStage) / total);
            }
        }

        // Генерация по шагам: перечислитель продвигают раз в кадр (MoveNext);
        // ошибка расчёта выбрасывается из MoveNext. Пока шаг ждёт фонового
        // расчёта, MoveNext возвращает управление сразу.
        public IEnumerator GenerateStepwise()
        {
            if (!Validate())
                throw new System.InvalidOperationException(LastError);
            Stopwatch watch = Stopwatch.StartNew();
            Settings.Stage05.Art = ResolvedArtSet05;
            // Наборы рисунков в редакторе подгружаются из AssetDatabase — до фона.
            _ = ResolvedArtSet06;
            // Сведения о системе Unity отдаёт только главному потоку.
            MapLabPaintFields.WarmUp();
            BuildStage = "земля, вода, дороги и поселения";
            MapLabData fresh = null;
            Task task = Task.Run(() => fresh = MapLabGenerator.Generate(Settings, null));
            while (!task.IsCompleted)
                yield return null;
            Rethrow(task);
            if (GetComponent<MapLabMarker>() == null)
                gameObject.AddComponent<MapLabMarker>();
            Clear();
            data = fresh;
            yield return null;
            IEnumerator steps = BuildSteps(null, true);
            while (steps.MoveNext())
                yield return null;
            MapLabStats stats = data.Stats;
            stats.GenerateMs = (float)watch.Elapsed.TotalMilliseconds;
            data.Stats = stats;
            LastStats = stats;
            BuildStage = string.Empty;
            stageIndex = StageNames.Length - 1;
        }

        private static void Rethrow(Task task)
        {
            if (task.IsFaulted && task.Exception != null)
            {
                System.Exception inner = task.Exception.InnerExceptions.Count == 1 ? task.Exception.InnerException : task.Exception;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
            }
        }

        // Расчёт work — в фоне (background) или сразу; null — уже выполнен.
        private static Task Offload(bool background, System.Action work)
        {
            if (background)
                return Task.Run(work);
            work();
            return null;
        }

        // Шаги построения по готовым данным. background — тяжёлые расчёты в
        // фоновом потоке (шаг уступает кадр, пока расчёт идёт).
        private IEnumerator BuildSteps(MapLabGenerator.Progress progress, bool background)
        {
            Stopwatch watch = Stopwatch.StartNew();
            Phases.Clear();
            phaseWatch.Restart();
            EnsureRoot();
            CreateSharedResources();
            MapLabGenerator.Report(progress, "Покраска поверхности", 0.94f);
            BuildStage = "подготовка рельефа";
            if (background)
            {
                bool drawnCliffs = WillDrawCliffs06;
                bool shade = data.ShadeResolution > 0 && data.ShadeLight != null;
                Task prepare = Offload(true, () =>
                {
                    MaskPixels(data, out pendingMaskA, out pendingMaskB);
                    if (shade)
                        pendingShade = ShadePixels(drawnCliffs);
                });
                while (!prepare.IsCompleted)
                    yield return null;
                Rethrow(prepare);
            }
            UploadDataTextures();
            UploadShade();
            Phase("текстуры данных");
            if (background)
                yield return null;

            BuildStage = "покраска";
            MapLabTiledPainter.Session session = null;
            if (background && Settings.IsStage03)
            {
                // То же, что RepaintTiled, но расчёт — в фоне; подгрузка
                // подробных тайлов включается в конце сборки.
                if (paletteTexture != null)
                    MapLabArt.WritePalette(paletteTexture, Palette);
                bool washes = WantsWashes;
                if (washes)
                    EnsureWashSources();
                MapLabTiledPainter.Options options = TiledOptions(false);
                bool streaming = WantsStreaming();
                DestroyStreaming();
                float low = Mathf.Clamp(Settings.Stage05.OverviewTexels, 1f, 8f);
                MapLabTiledPainter.Result painted = null;
                Task paint = Offload(true, () =>
                {
                    if (washes)
                    {
                        washLayer = MapLabWash.Place(data, Settings, washSources);
                        options.Washes = washLayer;
                    }
                    if (streaming)
                    {
                        session = MapLabTiledPainter.Prepare(data, Settings, Palette, tileWorld, options);
                        painted = MapLabTiledPainter.PaintAll(session, low);
                    }
                    else
                        painted = MapLabTiledPainter.Paint(data, Settings, Palette, tileWorld, options);
                });
                while (!paint.IsCompleted)
                    yield return null;
                Rethrow(paint);
                UploadTiles(painted);
                if (streaming)
                {
                    lowTiles.Clear();
                    lowRects.Clear();
                    lowTiles.AddRange(paintTiles);
                    lowRects.AddRange(paintRects);
                    streamRevision++;
                }
            }
            else
                Repaint();
            Phase("покраска");
            if (background)
                yield return null;

            MapLabGenerator.Report(progress, "Меши", 0.97f);
            BuildStage = "рельеф, вода и скалы";
            BuildTerrain();
            Phase("рельеф");
            BuildWater();
            BuildCliffs();
            Phase("вода и скалы");
            if (background)
                yield return null;

            BuildStage = "деревья, дома и мелочи";
            deferObjectShadows = background;
            try
            {
                BuildObjects();
            }
            finally
            {
                deferObjectShadows = false;
            }
            if (background)
            {
                yield return null;
                BuildStage = "тени";
                if (PrepareObjectShadows(out List<MapLabObjectShadows.Caster> casters))
                {
                    MapLabObjectShadows.Result shadows = null;
                    Task cast = Offload(true, () => shadows = MapLabObjectShadows.Compute(data, Settings, casters));
                    while (!cast.IsCompleted)
                        yield return null;
                    Rethrow(cast);
                    UploadObjectShadows(shadows);
                }
                Phase("тени объектов");
                yield return null;
            }

            BuildStage = "перо";
            InkWork ink = null;
            Task inkTask = Offload(background, () => ink = ComputeInk());
            while (inkTask != null && !inkTask.IsCompleted)
                yield return null;
            if (inkTask != null)
                Rethrow(inkTask);
            ApplyInk(ink);
            Phase("перо");
            if (background)
                yield return null;

            BuildStage = "трава и материалы";
            BuildGrass();
            BuildWireframe();
            ApplyMaterialSettings();
            Phase("трава и материалы");
            if (session != null)
                paintSession = session;
            if (CameraRig != null)
                CameraRig.Bind(this, true);
            ApplyVisibility();
            PumpStreaming(false);
            MapLabMarker marker = Marker;
            if (marker != null)
                marker.OnMapBuilt(this);
            MapLabStats stats = data.Stats;
            stats.PaintMs += 0f;
            CountStats(ref stats);
            stats.ObjectsMs += (float)watch.Elapsed.TotalMilliseconds - stats.PaintMs;
            data.Stats = stats;
        }
    }
}
