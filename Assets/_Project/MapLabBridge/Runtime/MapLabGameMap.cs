using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using KingdomSurvival.MapLab;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace KingdomSurvival.MapLabBridge
{
    // 12У-F: рисунок карты партии MapLab для игрового экрана карты. План
    // партии (GeneratedWorldMap) → MapLab (MapLabRegionPlan) → снимок
    // ортографической камерой под рабочим наклоном плитками → текстуры.
    // Земля игровой области проецируется линейно: x — как на холсте, глубина —
    // со сжатием sin(наклона). Поэтому элемент карты UI получает высоту
    // CanvasHeight·sin(наклона) (HeightScale), а все проценты — значки, герой,
    // туман, области поиска — совпадают с рисунком без второй системы
    // координат. Высота рельефа даёт небольшой сдвиг вверх (параллакс);
    // плоскость снимка — средняя высота земли, поэтому сдвиг в обе стороны мал.
    // Снимок кэшируется в памяти (на карту партии) и на диске (по ключу плана
    // и версии алгоритма): загрузка партии не пересобирает рисунок.
    public static class MapLabGameMap
    {
        public sealed class Result
        {
            public readonly List<(Texture2D Texture, Rect Percent)> Tiles = new List<(Texture2D, Rect)>();
            // 12У-G: здания услуг на снимке — поселение (ID места, «village-N»),
            // роль и прямоугольник рисунка в процентах полотна.
            public readonly List<(string SettlementId, string Role, Rect Percent)> Buildings = new List<(string, string, Rect)>();
            public float HeightScale = 1f;
            public float Pitch = 50f;
            public int PixelWidth, PixelHeight;
            public double BakeMs;
            public bool FromDiskCache;
            public string Error = string.Empty;

            public void Destroy()
            {
                foreach ((Texture2D t, Rect _) in Tiles)
                    if (t != null)
                    {
                        if (Application.isPlaying) Object.Destroy(t); else Object.DestroyImmediate(t);
                    }
                Tiles.Clear();
            }
        }

        private static GeneratedWorldMap cachedMap;
        private static Result cached;

        // В пакетном режиме (автотесты) игра рисует карту прежним способом:
        // снимок MapLab — минута работы на партию. Пакетные команды снимка
        // вызывают Bake напрямую.
        public static bool Enabled
        {
            get
            {
                MapLabGameAssets assets = MapLabGameAssets.Load();
                return assets != null && assets.UseInGame && assets.Profile != null && !Application.isBatchMode;
            }
        }

        // Рисунок для карты партии (из памяти, с диска или новый снимок).
        // services — услуги поселений (ID места → роли по порядку): их
        // здания рисуются отдельными рисунками и возвращаются как цели клика.
        public static Result Get(GeneratedWorldMap plan, IDictionary<string, List<string>> services = null)
        {
            if (plan == null || !plan.IsValid || !Enabled)
                return null;
            string servicesKey = ServicesKey(services);
            if (ReferenceEquals(plan, cachedMap) && cached != null && cachedServices == servicesKey)
                return cached;
            Clear();
            MapLabGameAssets assets = MapLabGameAssets.Load();
            Result result = TryLoadDisk(plan, assets, servicesKey) ?? Bake(plan, assets, services);
            if (result != null && string.IsNullOrEmpty(result.Error) && !result.FromDiskCache)
                SaveDisk(plan, assets, result, servicesKey);
            cachedMap = plan;
            cached = result;
            cachedServices = servicesKey;
            return result;
        }

        private static string cachedServices;

        // Кадр фигуры группы для ракурса (набор выбранных мест и фракций).
        public static Texture2D FigureTexture(string figureSet, int direction)
        {
            MapLabGameAssets assets = MapLabGameAssets.Load();
            if (assets == null || assets.ArtSetPoi == null || string.IsNullOrEmpty(figureSet))
                return null;
            MapLabArtEntry entry = assets.ArtSetPoi.Find(figureSet + "_" + WorldMapActorMotion.Directions[((direction % 6) + 6) % 6]);
            return entry != null && entry.HasTexture ? entry.Texture : null;
        }

        // Рисунок здания услуги (для подсветки на экране карты).
        public static Texture2D ServiceTexture(string role)
        {
            MapLabGameAssets assets = MapLabGameAssets.Load();
            string id = MapLabPlacer.ServiceArt(role);
            if (assets == null || assets.ArtSetPoi == null || id == null)
                return null;
            MapLabArtEntry entry = assets.ArtSetPoi.Find(id);
            return entry != null ? entry.Texture : null;
        }

        public static string ServicesKey(IDictionary<string, List<string>> services)
        {
            if (services == null || services.Count == 0)
                return string.Empty;
            List<string> keys = new List<string>(services.Keys);
            keys.Sort(StringComparer.Ordinal);
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            foreach (string k in keys)
                b.Append(k).Append('=').Append(string.Join(",", services[k])).Append(';');
            return b.ToString();
        }

        public static void Clear()
        {
            cached?.Destroy();
            cached = null;
            cachedMap = null;
        }

        public static string CacheKey(GeneratedWorldMap plan, MapLabGameAssets assets)
        {
            string print = plan.Passport != null ? plan.Passport.Fingerprint : string.Empty;
            uint hash = 2166136261;
            foreach (char c in plan.TerrainCells + "|" + print + "|" + plan.HexesAcross)
                hash = (hash ^ c) * 16777619;
            // r — версия рисунка снимка (палитра, покраска): старый кэш не показывается.
            return $"s{plan.Seed}_h{hash:x8}_a{MapLabData.AlgorithmVersion}_r2_t{assets.TexelsPerUnit:0.#}_p{assets.PitchDegrees:0}";
        }

        private static string CacheFolder => Path.Combine(Application.persistentDataPath, "MapLabMapCache");

        public static Result Bake(GeneratedWorldMap plan, MapLabGameAssets assets, IDictionary<string, List<string>> services = null)
        {
            Result result = new Result();
            Stopwatch watch = Stopwatch.StartNew();
            GameObject cameraObject = null, mapObject = null;
            MapLabSettings settings = null;
            try
            {
                settings = Object.Instantiate(assets.Profile);
                settings.name = "MapLab Game Profile";
                MapLabComposition composition = MapLabRegionPlan.Apply(plan, settings);
                if (services != null)
                    foreach (MapLabComposition.Settlement st in composition.Settlements)
                        if (services.TryGetValue(st.Id, out List<string> roles) && roles != null)
                            st.Services.AddRange(roles);
                // Снимок для игры: сетка 257 и покраска чуть плотнее снимка —
                // подробнее экран всё равно не покажет, а генерация в разы короче.
                settings.General.Quality = MapLabQuality.Samples257;
                settings.Stage03.PaintTexelsPerUnit = Mathf.Max(6f, assets.TexelsPerUnit * 1.4f);

                cameraObject = new GameObject("MapLab Game Bake Camera") { hideFlags = HideFlags.HideAndDontSave };
                Camera cam = cameraObject.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.allowMSAA = true;
                cam.allowHDR = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                MapLabCameraRig rig = cameraObject.AddComponent<MapLabCameraRig>();
                rig.enabled = false;

                mapObject = new GameObject("MapLab Game Bake") { hideFlags = HideFlags.HideAndDontSave };
                MapLabView view = mapObject.AddComponent<MapLabView>();
                view.GenerateOnLoad = false;
                MapLabMarker marker = mapObject.AddComponent<MapLabMarker>();
                marker.AutoPlace = false;
                marker.enabled = false;
                assets.ApplyTo(view);
                view.CameraRig = rig;
                view.Settings = settings;
                if (!view.Generate(null))
                {
                    result.Error = "MapLab: генерация не выполнена — " + view.LastError;
                    return result;
                }
                Transform markerRoot = mapObject.transform.Find(MapLabMarker.RootName);
                if (markerRoot != null)
                    markerRoot.gameObject.SetActive(false);

                MapLabData data = view.Data;
                Rect play = data.PlayRect.width > 0f ? data.PlayRect : new Rect(0f, 0f, data.Size, data.Size);
                rig.Zoom = 1f;
                float pitch = Mathf.Clamp(assets.PitchDegrees, 20f, 80f);
                Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
                float sin = Mathf.Sin(pitch * Mathf.Deg2Rad);
                result.Pitch = pitch;
                result.HeightScale = sin;
                float texels = Mathf.Clamp(assets.TexelsPerUnit, 1f, 16f);
                int width = Mathf.CeilToInt(play.width * texels / 4f) * 4;
                int height = Mathf.CeilToInt(play.height * sin * texels / 4f) * 4;
                result.PixelWidth = width;
                result.PixelHeight = height;
                // Плоскость снимка — средняя высота игровой области.
                float mean = 0f;
                for (int i = 0; i < 16; i++)
                    for (int j = 0; j < 16; j++)
                        mean += data.SampleHeight(play.xMin + (i + 0.5f) / 16f * play.width, play.yMin + (j + 0.5f) / 16f * play.height);
                mean /= 256f;
                // Здания услуг: проекция опоры и карточки полной высоты (как в
                // шейдере карточки) — прямоугольник рисунка на снимке.
                float cos = Mathf.Cos(pitch * Mathf.Deg2Rad);
                foreach (MapLabServiceBuilding b in data.ServiceBuildings)
                {
                    float v = (b.Base.y - mean) * cos + (b.Base.z - play.yMax) * sin;
                    float bx = (b.Base.x - play.xMin) * texels, by = -v * texels;
                    float left = bx - b.Pivot.x * b.Width * texels;
                    float top = by - (1f - b.Pivot.y) * b.Height * texels;
                    result.Buildings.Add((b.SettlementId, b.Role, new Rect(left * 100f / width, top * 100f / height, b.Width * texels * 100f / width, b.Height * texels * 100f / height)));
                }
                cam.backgroundColor = view.Palette.Paper;
                float distance = data.Size * 2f + 200f;
                cam.nearClipPlane = Mathf.Max(0.3f, distance - data.Size * 1.6f - 60f);
                cam.farClipPlane = distance + data.Size * 1.6f + 60f;
                int tile = Mathf.Clamp(assets.TileSize, 256, 4096) / 4 * 4;
                for (int top = 0; top < height; top += tile)
                {
                    for (int left = 0; left < width; left += tile)
                    {
                        int tw = Mathf.Min(tile, width - left), th = Mathf.Min(tile, height - top);
                        float uCenter = (left + tw * 0.5f) / texels;
                        float vCenter = (top + th * 0.5f) / texels;
                        Vector3 target = new Vector3(play.xMin + uCenter, mean, play.yMax - vCenter / sin);
                        rig.Target = target;
                        cam.aspect = tw / (float)th;
                        cam.orthographicSize = th / texels * 0.5f;
                        cameraObject.transform.SetPositionAndRotation(target - rotation * Vector3.forward * distance, rotation);
                        view.OnCameraChanged();
                        view.PumpStreaming(true);
                        Texture2D texture = RenderTile(cam, tw, th);
                        result.Tiles.Add((texture, new Rect(left * 100f / width, top * 100f / height, tw * 100f / width, th * 100f / height)));
                    }
                }
            }
            catch (Exception exception)
            {
                result.Error = "MapLab: " + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                if (mapObject != null)
                {
                    MapLabView view = mapObject.GetComponent<MapLabView>();
                    view?.Clear();
                    Object.DestroyImmediate(mapObject);
                }
                if (cameraObject != null)
                    Object.DestroyImmediate(cameraObject);
                if (settings != null)
                    Object.DestroyImmediate(settings);
                result.BakeMs = watch.Elapsed.TotalMilliseconds;
            }
            if (!string.IsNullOrEmpty(result.Error))
                result.Destroy();
            else
                Debug.Log($"MapLab: рисунок карты партии {result.PixelWidth}×{result.PixelHeight} ({result.Tiles.Count} плиток) за {result.BakeMs / 1000.0:0.0} с.");
            return result;
        }

        private static Texture2D RenderTile(Camera cam, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = new RenderTexture(width, height, 24, GraphicsFormat.R8G8B8A8_SRGB)
            {
                name = "MapLab Game Tile",
                antiAliasing = 4,
                hideFlags = HideFlags.DontSave,
            };
            try
            {
                rt.Create();
                cam.targetTexture = rt;
                Shader.SetGlobalFloat("_MapLabPixelScale", 1f);
                cam.Render();
                RenderTexture.active = rt;
                Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                {
                    name = "MapLab Map Tile",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, false);
                return texture;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = previous;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        // ---------- кэш на диске ----------

        private static string Folder(GeneratedWorldMap plan, MapLabGameAssets assets, string servicesKey)
        {
            uint hash = 2166136261;
            foreach (char c in servicesKey ?? string.Empty)
                hash = (hash ^ c) * 16777619;
            return Path.Combine(CacheFolder, CacheKey(plan, assets) + $"_v{hash:x8}");
        }

        private static Result TryLoadDisk(GeneratedWorldMap plan, MapLabGameAssets assets, string servicesKey)
        {
            try
            {
                string folder = Folder(plan, assets, servicesKey);
                string index = Path.Combine(folder, "index.txt");
                if (!File.Exists(index))
                    return null;
                string[] lines = File.ReadAllLines(index);
                Result result = new Result { FromDiskCache = true };
                string[] head = lines[0].Split(' ');
                result.PixelWidth = int.Parse(head[0]);
                result.PixelHeight = int.Parse(head[1]);
                result.Pitch = float.Parse(head[2], System.Globalization.CultureInfo.InvariantCulture);
                result.HeightScale = Mathf.Sin(result.Pitch * Mathf.Deg2Rad);
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split(' ');
                    if (p.Length == 7 && p[0] == "B")
                    {
                        result.Buildings.Add((p[1], p[2], new Rect(F(p[3]), F(p[4]), F(p[5]), F(p[6]))));
                        continue;
                    }
                    if (p.Length < 5)
                        continue;
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                    if (!texture.LoadImage(File.ReadAllBytes(Path.Combine(folder, p[0]))))
                    {
                        result.Destroy();
                        return null;
                    }
                    Rect rect = new Rect(F(p[1]), F(p[2]), F(p[3]), F(p[4]));
                    result.Tiles.Add((texture, rect));
                }
                return result.Tiles.Count > 0 ? result : null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MapLab: кэш рисунка карты не прочитан — " + exception.Message);
                return null;
            }
        }

        private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        private static void SaveDisk(GeneratedWorldMap plan, MapLabGameAssets assets, Result result, string servicesKey)
        {
            try
            {
                string folder = Folder(plan, assets, servicesKey);
                Directory.CreateDirectory(folder);
                List<string> lines = new List<string> { $"{result.PixelWidth} {result.PixelHeight} {result.Pitch.ToString(System.Globalization.CultureInfo.InvariantCulture)}" };
                for (int i = 0; i < result.Tiles.Count; i++)
                {
                    (Texture2D t, Rect r) = result.Tiles[i];
                    string name = $"tile{i}.png";
                    File.WriteAllBytes(Path.Combine(folder, name), t.EncodeToPNG());
                    lines.Add(string.Join(" ", name, r.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), r.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        r.width.ToString("R", System.Globalization.CultureInfo.InvariantCulture), r.height.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                }
                foreach ((string id, string role, Rect r) in result.Buildings)
                    lines.Add(string.Join(" ", "B", id, role, r.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture), r.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        r.width.ToString("R", System.Globalization.CultureInfo.InvariantCulture), r.height.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
                File.WriteAllLines(Path.Combine(folder, "index.txt"), lines);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("MapLab: кэш рисунка карты не записан — " + exception.Message);
            }
        }
    }
}
