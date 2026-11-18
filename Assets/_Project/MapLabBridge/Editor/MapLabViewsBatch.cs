using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KingdomSurvival.MapLab;
using KingdomSurvival.MapLab.Editor;
using KingdomSurvival.WorldMapVisual;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KingdomSurvival.MapLabBridge.Editor
{
    // Виды карты: пакетная проверка игровым путём. Карта партии собирается
    // живой картой (MapLabGameLive, как в игре) один раз, затем каждый
    // включённый вид рисуется двумя кадрами — общий вид и деревня — в PNG.
    // Вид со своей палитрой или рисунками пересобирает мир (как в игре).
    //   -executeMethod KingdomSurvival.MapLabBridge.Editor.MapLabViewsBatch.Render
    //     -mapLabOut <папка> [-mapLabSeeds 11] [-mapLabHexes 40]
    public static class MapLabViewsBatch
    {
        public static void Render()
        {
            int code = 0;
            string output = MapLabStage03Batch.Argument("-mapLabOut", Path.GetFullPath("MapLabExports/Views"));
            string savedId = MapLabMapViews.ActiveId;
            StringBuilder report = new StringBuilder();
            try
            {
                Directory.CreateDirectory(output);
                int seed = int.Parse(MapLabStage03Batch.Argument("-mapLabSeeds", "11").Split(',')[0], CultureInfo.InvariantCulture);
                int hexes = int.Parse(MapLabStage03Batch.Argument("-mapLabHexes", "40"), CultureInfo.InvariantCulture);
                WorldMapGeneratorAsset generator = AssetDatabase.LoadAssetAtPath<WorldMapGeneratorAsset>(WorldMapGeneratorAsset.AssetPath);
                WorldMapGenerationSettings planSettings = generator != null ? generator.Settings.Clone() : WorldMapGenerationSettings.CreateDefault();
                planSettings.HexesAcross = hexes;
                GeneratedWorldMap plan = WorldMapGenerator.Generate(planSettings, seed);
                MapLabMapViews.ResetCache();
                List<MapLabMapView> views = MapLabMapViews.Load().EnabledViews();
                report.AppendLine($"Виды карты · seed {seed}, клеток {hexes}, видов {views.Count}");
                Vector2 village = plan.Villages.Count > 0 ? new Vector2(plan.Villages[0].XPercent, plan.Villages[0].YPercent) : new Vector2(50f, 50f);
                foreach (MapLabMapView v in views)
                {
                    MapLabMapViews.ActiveId = v.Id;
                    System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                    MapLabGameLive.World world = MapLabGameLive.Matches(plan, string.Empty) || (MapLabGameLive.Current != null && ReferenceEquals(MapLabGameLive.Current.Plan, plan) && MapLabGameLive.TryApplyActiveView()) ? MapLabGameLive.Current : MapLabGameLive.Build(plan, null);
                    if (world == null || !world.Ready)
                        throw new Exception("живая карта не собрана: " + world?.Error);
                    double buildMs = watch.Elapsed.TotalMilliseconds;
                    Save(world, new Rect(0f, 0f, 100f, 100f), 1600, 900, Path.Combine(output, $"{v.Id}_overview.png"));
                    float span = 22f;
                    Rect close = new Rect(Mathf.Clamp(village.x - span * 0.5f, 0f, 100f - span), Mathf.Clamp(village.y - span * 0.5f, 0f, 100f - span), span, span * 900f / 1600f / world.Sin);
                    Save(world, close, 1600, 900, Path.Combine(output, $"{v.Id}_village.png"));
                    Save(world, new Rect(62f, 34f, 26f, 26f * 900f / 1600f / world.Sin), 1600, 900, Path.Combine(output, $"{v.Id}_detail.png"));
                    report.AppendLine($"  {v.Id} «{v.Name}»: сборка {buildMs:0} мс (пересборка — {(buildMs > 50 ? "да" : "нет")}), своих рисунков {v.ArtCount}, палитра {(v.Palette != null ? v.Palette.name : "профиля")}");
                }
            }
            catch (Exception exception)
            {
                code = 1;
                report.AppendLine("ОШИБКА: " + exception);
                Debug.LogException(exception);
            }
            finally
            {
                MapLabMapViews.ActiveId = savedId;
                MapLabGameLive.Clear();
                File.WriteAllText(Path.Combine(output, "report.txt"), report.ToString());
                Debug.Log("MAP_VIEWS: " + report);
            }
            EditorApplication.Exit(code);
        }

        // Проверка смены вида в сцене генератора карты (как в окне): карта по
        // плану игры, затем Обычный → Мрачный → Сепия → Мрачный → Обычный без
        // пересборки. После каждого шага у всех объектов карты текстура должна
        // быть из папки выбранного вида (или основная — если своей нет).
        //   -executeMethod KingdomSurvival.MapLabBridge.Editor.MapLabViewsBatch.CheckGeneratorSwap
        public static void CheckGeneratorSwap()
        {
            int code = 0;
            string savedId = MapLabMapViews.ActiveId;
            StringBuilder report = new StringBuilder();
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(MapLabAssets.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
                MapLabView view = MapLabAssets.FindView();
                if (view == null || view.Settings == null)
                    throw new Exception("нет MapLabView со своим профилем в сцене генератора");
                MapLabSettings s = view.Settings;
                if (s.Author.UseGamePlan && MapLabPlanSource.Available)
                    MapLabPlanSource.Build(s);
                MapLabMapViews.ResetCache();
                MapLabMapViews.ActiveId = MapLabMapViews.ClassicId;
                view.SetLook(MapLabMapViews.Active);
                if (!view.Generate(null))
                    throw new Exception("генерация не выполнена: " + view.LastError);
                string[] steps = { MapLabMapViews.GloomId, MapLabMapViews.SepiaId, MapLabMapViews.GloomId, MapLabMapViews.ClassicId };
                report.AppendLine("Смена вида в сцене генератора: " + Count(view, MapLabMapViews.ClassicId, out _));
                foreach (string id in steps)
                {
                    MapLabMapViews.ActiveId = id;
                    bool swapped = view.SwapLook(MapLabMapViews.Active);
                    int wrong = Mismatch(view, id, out string sample);
                    report.AppendLine($"  {id}: подмена {(swapped ? "да" : "НЕТ")}, чужих текстур {wrong}{sample}; {Count(view, id, out _)}");
                    if (!swapped || wrong > 0)
                        code = 1;
                }
            }
            catch (Exception exception)
            {
                code = 1;
                report.AppendLine("ОШИБКА: " + exception);
                Debug.LogException(exception);
            }
            finally
            {
                MapLabMapViews.ActiveId = savedId;
            }
            Debug.Log("MAP_VIEWS_SWAP: " + report);
            EditorApplication.Exit(code);
        }

        // Текстуры объектов карты из папки другого вида.
        private static int Mismatch(MapLabView view, string activeId, out string sample)
        {
            int wrong = 0;
            sample = string.Empty;
            foreach (Renderer r in view.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_MainTex"))
                        continue;
                    Texture t = m.GetTexture("_MainTex");
                    string path = t != null ? AssetDatabase.GetAssetPath(t) : string.Empty;
                    int at = path.IndexOf("/MapViews/", StringComparison.Ordinal);
                    if (at < 0)
                        continue;
                    string folder = path.Substring(at + 10).Split('/')[0];
                    if (folder == activeId)
                        continue;
                    wrong++;
                    if (sample.Length == 0)
                        sample = $" (например {m.name}: {folder})";
                }
            return wrong;
        }

        private static string Count(MapLabView view, string activeId, out int own)
        {
            own = 0;
            int total = 0;
            foreach (Renderer r in view.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_MainTex") || m.GetTexture("_MainTex") == null)
                        continue;
                    total++;
                    if (AssetDatabase.GetAssetPath(m.GetTexture("_MainTex")).Contains("/MapViews/" + activeId + "/"))
                        own++;
                }
            return $"материалов с текстурой {total}, из папки вида {own}";
        }

        private static void Save(MapLabGameLive.World world, Rect visible, int width, int height, string path)
        {
            RenderTexture rt = null;
            // Подробная покраска подгружается по мере рендера — несколько проходов.
            for (int i = 0; i < 8; i++)
            {
                world.View.PumpStreaming(true);
                rt = MapLabGameLive.Render(world, visible, width, height);
            }
            if (rt == null)
                throw new Exception("кадр не нарисован: " + path);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            texture.Apply(false, false);
            RenderTexture.active = previous;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
