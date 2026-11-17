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
                    MapLabGameLive.World world = MapLabGameLive.Matches(plan, string.Empty) ? MapLabGameLive.Current : MapLabGameLive.Build(plan, null);
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
