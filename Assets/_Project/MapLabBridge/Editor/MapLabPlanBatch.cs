using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KingdomSurvival.MapLab;
using KingdomSurvival.MapLab.Editor;
using KingdomSurvival.WorldMapVisual;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KingdomSurvival.MapLabBridge.Editor
{
    // 12У-D/F: план региона генератора игры → рисунок MapLab. Пакетный показ:
    // для нескольких seed — план (деревни, места, дороги, реки, озёра), рисунок
    // MapLab по этому плану, кадры общего вида и деревень, отчёт.
    //   -executeMethod KingdomSurvival.MapLabBridge.Editor.MapLabPlanBatch.Render
    //     -mapLabOut <папка> [-mapLabSeeds 11,12] [-mapLabHexes 60]
    public static class MapLabPlanBatch
    {
        private static int Count(GeneratedWorldMap plan, WorldMapGameplayTerrainType type)
        {
            WorldMapHexGrid grid = plan.CreateGrid();
            WorldMapTerrainLayer layer = plan.CreateTerrainLayer();
            int n = 0;
            for (int i = 0; i < grid.CellCount; i++)
                if (layer.Get(grid.CellAt(i)) == type)
                    n++;
            return n;
        }

        public static void Render()
        {
            int code = 0;
            StringBuilder report = new StringBuilder();
            string output = MapLabStage03Batch.Argument("-mapLabOut", Path.GetFullPath("MapLabExports/Plan"));
            try
            {
                Directory.CreateDirectory(output);
                string[] seeds = MapLabStage03Batch.Argument("-mapLabSeeds", "11").Split(',');
                int hexes = int.Parse(MapLabStage03Batch.Argument("-mapLabHexes", "60"), CultureInfo.InvariantCulture);
                WorldMapGeneratorAsset generator = AssetDatabase.LoadAssetAtPath<WorldMapGeneratorAsset>(WorldMapGeneratorAsset.AssetPath);
                WorldMapGenerationSettings planSettings = generator != null ? generator.Settings.Clone() : WorldMapGenerationSettings.CreateDefault();
                planSettings.HexesAcross = hexes;
                MapLabAssets.EnsureAllPresets();
                EditorSceneManager.OpenScene(MapLabAssets.ScenePath, OpenSceneMode.Single);
                MapLabView view = MapLabAssets.FindView();
                view.ArtSet = MapLabArtImport.LoadArtSet();
                view.MarkerSet = MapLabArtImport.LoadMarkerSet();
                view.ArtSet03 = MapLabArtImport.LoadStage03Set();
                view.MarkerSet04 = MapLabArtImport.LoadStage04Markers();
                view.ArtSet05 = MapLabArtImport.LoadStage05Set();
                view.MarkerSet05 = MapLabArtImport.LoadStage05Markers();
                view.ArtSet06 = MapLabArtImport.LoadCliffs06Set();
                view.CliffShader = MapLabAssets.LoadShader("MapLabCliff.shader");
                view.InkShader = MapLabAssets.LoadShader("MapLabInk.shader");
                view.GrassShader = MapLabAssets.LoadShader("MapLabGrass.shader");
                view.ModuleShader = MapLabAssets.LoadShader("MapLabModule.shader");
                report.AppendLine("План региона → MapLab · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + $" · клеток по ширине {hexes}");
                foreach (string token in seeds)
                {
                    int seed = int.Parse(token.Trim(), CultureInfo.InvariantCulture);
                    Stopwatch watch = Stopwatch.StartNew();
                    GeneratedWorldMap plan = WorldMapGenerator.Generate(planSettings, seed);
                    double planMs = watch.Elapsed.TotalMilliseconds;
                    MapLabSettings s = UnityEngine.Object.Instantiate(MapLabAssets.EnsurePreset(MapLabPresetKind.RiverValley));
                    s.Stage03.Look = MapLabLook.Stage05;
                    MapLabComposition c = MapLabRegionPlan.Apply(plan, s);
                    s.General.Quality = s.WorldSide > 600f ? MapLabQuality.Samples513 : MapLabQuality.Samples257;
                    view.Settings = s;
                    watch.Restart();
                    bool ok = view.Generate(null);
                    double mapMs = watch.Elapsed.TotalMilliseconds;
                    MapLabData d = view.Data;
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "seed {0}: план {1:0} мс — деревень {2}, мест {3}, дорог {4}, рек {5}, клеток озёр {6}, клеток полей {18}, клеток леса {19}; MapLab {7} {8:0} мс — сторона {9:0} ед., игровая область {10:0}×{11:0}, поселений {12}, дворов {13}, домов {14}, мостов {15}, холмов {16}, озёр {17}",
                        seed, planMs, plan.Villages.Count, plan.Landmarks.Count, plan.Roads.Count, plan.Rivers.Count, plan.LakeCells.Count,
                        ok ? "готово" : "ошибка: " + view.LastError, mapMs, d != null ? d.Size : 0f, c.PlayRect.width, c.PlayRect.height,
                        c.Settlements.Count, d != null ? d.Yards.Count : 0, d != null ? d.CountObjects(MapLabObjectKind.House) : 0, d != null ? d.Bridges.Count : 0, c.Hills.Count, c.Lakes.Count,
                        Count(plan, WorldMapGameplayTerrainType.Field), Count(plan, WorldMapGameplayTerrainType.Forest)));
                    if (d != null)
                    {
                        int mask = 0, planHits = 0;
                        for (int i = 0; i < d.Field.Length; i++)
                        {
                            if (d.Field[i] > 0.5f) mask++;
                            Vector2 p = new Vector2((i % d.Resolution) * d.Cell, (i / d.Resolution) * d.Cell);
                            if (c.SamplePlan(c.PlanField, p) > 0.5f) planHits++;
                        }
                        report.AppendLine($"      поля: участков {d.FieldBlocks.Count}, обрабатываемых {d.FieldBlocks.Count(b => b.Cultivated)}, отсчётов маски {mask}, отсчётов под полем плана {planHits}");
                    }
                    if (ok && view.CameraRig != null)
                    {
                        MapLabCameraRig rig = view.CameraRig;
                        MapLabExport.LinearDownsample = true;
                        MapLabExport.Msaa = s.Stage05.ExportMsaa;
                        void Shot(string name)
                        {
                            string file = Path.Combine(output, $"seed{seed}", name + ".png");
                            Directory.CreateDirectory(Path.GetDirectoryName(file));
                            view.PumpStreaming(true);
                            File.WriteAllBytes(file, MapLabExport.RenderPng(rig, 1920, 1080, 2));
                        }
                        rig.Camera.aspect = 1920f / 1080f;
                        rig.Zoom = 1f;
                        rig.Fit();
                        view.OnCameraChanged();
                        Shot("fit");
                        for (int v = 0; v < Mathf.Min(3, c.Settlements.Count); v++)
                        {
                            Vector2 at = c.Settlements[v].Center;
                            foreach (float z in new[] { rig.MaxZoom * 0.45f, rig.MaxZoom })
                            {
                                rig.Camera.aspect = 1920f / 1080f;
                                rig.Zoom = z;
                                rig.Target = new Vector3(at.x, 0f, at.y);
                                rig.ClampTarget();
                                rig.Target = rig.ToGround(rig.Target);
                                rig.Apply();
                                view.OnCameraChanged();
                                Shot($"village{v}_z{z:0.#}");
                            }
                        }
                    }
                    view.Clear();
                    UnityEngine.Object.DestroyImmediate(s);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                report.AppendLine("Ошибка: " + exception.Message);
                code = 1;
            }
            File.WriteAllText(Path.Combine(output, "plan_report.txt"), report.ToString());
            Debug.Log(report.ToString());
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }
    }
}
