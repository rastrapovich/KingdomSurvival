using System;
using System.Globalization;
using System.IO;
using System.Text;
using KingdomSurvival.MapLab;
using KingdomSurvival.MapLab.Editor;
using KingdomSurvival.WorldMapVisual;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KingdomSurvival.MapLabBridge.Editor
{
    // 12У-F: подготовка рисунка MapLab для игры — ассет MapLabGameAssets в
    // Resources (профиль, наборы арта, шейдеры по прямым ссылкам: в сборке нет
    // AssetDatabase) и пакетная проверка снимка карты партии.
    //   -executeMethod KingdomSurvival.MapLabBridge.Editor.MapLabGameSetup.BakeBatch
    //     -mapLabOut <папка> [-mapLabSeeds 11] [-mapLabHexes 100]
    public static class MapLabGameSetup
    {
        public const string Folder = "Assets/_Project/MapLabBridge/Resources";
        public const string AssetPath = Folder + "/" + MapLabGameAssets.ResourcesPath + ".asset";
        public const string ProfilePath = Folder + "/MapLabGameProfile.asset";

        // Вызывается из окна «Генератор карты» → «Сервис» (отдельного пункта меню нет).
        public static void PrepareMenu()
        {
            MapLabGameAssets assets = Prepare();
            EditorGUIUtility.PingObject(assets);
            Debug.Log("MapLab: ресурсы рисунка карты для игры обновлены — " + AssetPath);
        }

        public static MapLabGameAssets Prepare()
        {
            Directory.CreateDirectory(Folder);
            MapLabGameAssets assets = AssetDatabase.LoadAssetAtPath<MapLabGameAssets>(AssetPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<MapLabGameAssets>();
                AssetDatabase.CreateAsset(assets, AssetPath);
            }
            MapLabSettings profile = AssetDatabase.LoadAssetAtPath<MapLabSettings>(ProfilePath);
            if (profile == null)
            {
                MapLabAssets.EnsureAllPresets();
                profile = UnityEngine.Object.Instantiate(MapLabAssets.EnsurePreset(MapLabPresetKind.RiverValley));
                profile.Stage03.Look = MapLabLook.Stage05;
                profile.Author.UseGamePlan = true;
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            assets.Profile = profile;
            assets.ArtSet02 = MapLabArtImport.LoadArtSet();
            assets.MarkerSet02 = MapLabArtImport.LoadMarkerSet();
            assets.ArtSet03 = MapLabArtImport.LoadStage03Set();
            assets.MarkerSet04 = MapLabArtImport.LoadStage04Markers();
            assets.ArtSet05 = MapLabArtImport.LoadStage05Set();
            assets.MarkerSet05 = MapLabArtImport.LoadStage05Markers();
            assets.ArtSet06 = MapLabArtImport.LoadCliffs06Set();
            assets.ArtSetPoi = MapLabPoiImport.LoadSet();
            assets.Terrain = MapLabAssets.LoadShader("MapLabTerrain.shader");
            assets.Water = MapLabAssets.LoadShader("MapLabWater.shader");
            assets.Card = MapLabAssets.LoadShader("MapLabCard.shader");
            assets.Solid = MapLabAssets.LoadShader("MapLabSolid.shader");
            assets.Lines = MapLabAssets.LoadShader("MapLabLines.shader");
            assets.ArtCard = MapLabAssets.LoadShader("MapLabArtCard.shader");
            assets.Contact = MapLabAssets.LoadShader("MapLabContact.shader");
            assets.Ink = MapLabAssets.LoadShader("MapLabInk.shader");
            assets.Grass = MapLabAssets.LoadShader("MapLabGrass.shader");
            assets.Module = MapLabAssets.LoadShader("MapLabModule.shader");
            assets.Cliff = MapLabAssets.LoadShader("MapLabCliff.shader");
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
            return assets;
        }

        public static void BakeBatch()
        {
            int code = 0;
            StringBuilder report = new StringBuilder();
            string output = MapLabStage03Batch.Argument("-mapLabOut", Path.GetFullPath("MapLabExports/Game"));
            try
            {
                Directory.CreateDirectory(output);
                MapLabGameAssets assets = Prepare();
                WorldMapGeneratorAsset generator = AssetDatabase.LoadAssetAtPath<WorldMapGeneratorAsset>(WorldMapGeneratorAsset.AssetPath);
                WorldMapGenerationSettings settings = generator != null ? generator.Settings.Clone() : WorldMapGenerationSettings.CreateDefault();
                string hexes = MapLabStage03Batch.Argument("-mapLabHexes", "");
                if (hexes.Length > 0)
                    settings.HexesAcross = int.Parse(hexes, CultureInfo.InvariantCulture);
                foreach (string token in MapLabStage03Batch.Argument("-mapLabSeeds", "11").Split(','))
                {
                    int seed = int.Parse(token.Trim(), CultureInfo.InvariantCulture);
                    GeneratedWorldMap plan = WorldMapGenerator.Generate(settings, seed);
                    // Проверка зданий услуг: у каждой деревни — корчма, лавка и столб.
                    System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> services = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>>();
                    for (int v = 1; v < plan.Villages.Count; v++)
                        services["village-" + v] = new System.Collections.Generic.List<string> { "tavern", "merchant", "board", "healer", "elder" };
                    MapLabGameMap.Result result = MapLabGameMap.Bake(plan, assets, services);
                    report.AppendLine($"      зданий услуг на снимке: {result.Buildings.Count}");
                    report.AppendLine($"seed {seed} ({plan.HexesAcross} клеток, {plan.Passport.KindTitle}): {(string.IsNullOrEmpty(result.Error) ? "готово" : result.Error)}, снимок {result.PixelWidth}×{result.PixelHeight}, плиток {result.Tiles.Count}, сжатие глубины {result.HeightScale:0.###}, {result.BakeMs / 1000.0:0.0} с");
                    if (result.Tiles.Count > 0)
                    {
                        // Сборка плиток в один кадр для проверки стыков.
                        Texture2D whole = new Texture2D(result.PixelWidth, result.PixelHeight, TextureFormat.RGBA32, false);
                        foreach ((Texture2D t, Rect r) in result.Tiles)
                        {
                            int x = Mathf.RoundToInt(r.x / 100f * result.PixelWidth);
                            int top = Mathf.RoundToInt(r.y / 100f * result.PixelHeight);
                            whole.SetPixels32(x, result.PixelHeight - top - t.height, t.width, t.height, t.GetPixels32());
                        }
                        // Рамки зданий услуг — проверка совпадения с рисунком.
                        foreach ((string _, string role, Rect r) in result.Buildings)
                        {
                            int x0 = Mathf.RoundToInt(r.xMin / 100f * result.PixelWidth), x1 = Mathf.RoundToInt(r.xMax / 100f * result.PixelWidth);
                            int y0 = Mathf.RoundToInt(r.yMin / 100f * result.PixelHeight), y1 = Mathf.RoundToInt(r.yMax / 100f * result.PixelHeight);
                            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(result.PixelWidth - 1, x1); x++)
                                foreach (int y in new[] { y0, y1 })
                                    if (y >= 0 && y < result.PixelHeight)
                                        whole.SetPixel(x, result.PixelHeight - 1 - y, Color.red);
                            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(result.PixelHeight - 1, y1); y++)
                                foreach (int x in new[] { x0, x1 })
                                    if (x >= 0 && x < result.PixelWidth)
                                        whole.SetPixel(x, result.PixelHeight - 1 - y, Color.red);
                        }
                        whole.Apply();
                        File.WriteAllBytes(Path.Combine(output, $"game_map_seed{seed}.png"), whole.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(whole);
                    }
                    result.Destroy();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                report.AppendLine("Ошибка: " + exception.Message);
                code = 1;
            }
            File.WriteAllText(Path.Combine(output, "game_map_report.txt"), report.ToString());
            Debug.Log(report.ToString());
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }
    }
}
