using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace KingdomSurvival.MapLab.Editor
{
    // Этап 12У-C: замер масштаба карты MapLab на одной машине и одном профиле.
    // Для каждой стороны мира — оценка памяти до запуска (поля покраски и
    // тайлы), отказ от размера сверх бюджета, затем генерация с временем
    // стадий, памятью после сборки и временем кадра в RenderTexture.
    //   -executeMethod KingdomSurvival.MapLab.Editor.MapLabScaleBatch.Measure
    //     -mapLabOut <папка> [-mapLabSizes 256,512,1024,2048] [-mapLabBudgetMb 12000]
    public static class MapLabScaleBatch
    {
        // Рендереры в кадре камеры против всех: то, что рисуется на ближнем виде.
        public static string Visible(MapLabView view)
        {
            Camera cam = view.CameraRig != null ? view.CameraRig.Camera : null;
            if (cam == null)
                return "кадр: нет камеры";
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int total = 0, visible = 0; long verts = 0, visibleVerts = 0;
            System.Collections.Generic.Dictionary<string, int> heavy = new System.Collections.Generic.Dictionary<string, int>();
            foreach (MeshRenderer r in view.GetComponentsInChildren<MeshRenderer>(false))
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                int v = f != null && f.sharedMesh != null ? f.sharedMesh.vertexCount : 0;
                total++; verts += v;
                if (!GeometryUtility.TestPlanesAABB(planes, r.bounds))
                    continue;
                visible++; visibleVerts += v;
                string g = r.transform.parent != null ? r.transform.parent.name : r.name;
                heavy[g] = (heavy.TryGetValue(g, out int n) ? n : 0) + v;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(string.Format(CultureInfo.InvariantCulture, "кадр: рендереров {0} из {1}, вершин {2:N0} из {3:N0};", visible, total, visibleVerts, verts));
            foreach (var p in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(heavy, x => x.Value), 6))
                sb.Append(string.Format(CultureInfo.InvariantCulture, " {0} {1:N0}", p.Key, p.Value));
            return sb.ToString();
        }

        public static void Measure()
        {
            int code = 0;
            StringBuilder report = new StringBuilder();
            string output = MapLabStage03Batch.Argument("-mapLabOut", Path.GetFullPath("MapLabExports/Scale"));
            try
            {
                Directory.CreateDirectory(output);
                string[] sizes = MapLabStage03Batch.Argument("-mapLabSizes", "256,512,1024,2048").Split(',');
                float budgetMb = float.Parse(MapLabStage03Batch.Argument("-mapLabBudgetMb", "12000"), CultureInfo.InvariantCulture);
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
                report.AppendLine("MapLab · замер масштаба · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                report.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType + " · CPU: " + SystemInfo.processorType + " · потоков " + SystemInfo.processorCount + " · RAM " + SystemInfo.systemMemorySize + " МБ");
                report.AppendLine("Профиль: пресет В (речная долина), seed 304, Stage05, сетка 257, 16 текселей/ед.; режим подгрузки: " + (MapLabStreaming.Enabled ? "вкл." : "выкл."));
                foreach (string token in sizes)
                {
                    float side = float.Parse(token.Trim(), CultureInfo.InvariantCulture);
                    MapLabSettings s = UnityEngine.Object.Instantiate(MapLabAssets.EnsurePreset(MapLabPresetKind.RiverValley));
                    s.Stage03.Look = MapLabLook.Stage05;
                    s.General.Seed = 304;
                    s.General.MapSize = side;
                    s.General.Quality = MapLabQuality.Samples257;
                    s.Stage03.PaintTexelsPerUnit = 16f;
                    double fieldsMb = MapLabStreaming.EstimateFieldsMb(side);
                    double paintMb = MapLabStreaming.EstimatePaintMb(side, s.Stage03.PaintTexelsPerUnit);
                    string estimate = string.Format(CultureInfo.InvariantCulture, "оценка: поля покраски {0:0} МБ, тайлы {1:0} МБ", fieldsMb, paintMb);
                    if (fieldsMb + paintMb > budgetMb)
                    {
                        report.AppendLine($"{side:0}²: отказ до запуска — {estimate} больше бюджета {budgetMb:0} МБ");
                        UnityEngine.Object.DestroyImmediate(s);
                        continue;
                    }
                    view.Settings = s;
                    GC.Collect();
                    long before = Profiler.GetTotalAllocatedMemoryLong();
                    long managedBefore = GC.GetTotalMemory(true);
                    Stopwatch watch = Stopwatch.StartNew();
                    bool ok = view.Generate(null);
                    double ms = watch.Elapsed.TotalMilliseconds;
                    long after = Profiler.GetTotalAllocatedMemoryLong();
                    long managed = GC.GetTotalMemory(true);
                    MapLabStats st = view.LastStats;
                    float frame = 0f;
                    if (ok && view.CameraRig != null)
                    {
                        view.CameraRig.Fit();
                        view.CameraRig.Zoom = view.CameraRig.MaxZoom;
                        view.CameraRig.Target = new Vector3(side * 0.5f, 0f, side * 0.5f);
                        view.CameraRig.Apply();
                        view.OnCameraChanged();
                        frame = MapLabStage03Batch.MeasureFrame(view.CameraRig, 1920, 1080);
                    }
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:0}²: {1}; генерация {2:0} мс (рельеф {3:0}, покраска {4:0}, объекты {5:0}); управляемая память {6:0} МБ (+{7:0}), Unity {8:0} МБ (+{9:0}); тайлов покраски {10}, подробных {11}; кадр 1920×1080 на ближнем Zoom {12:0.0} мс; {13}",
                        side, ok ? "готово" : "ошибка", ms, st.TerrainMs, st.PaintMs, st.ObjectsMs,
                        managed / 1048576.0, (managed - managedBefore) / 1048576.0, after / 1048576.0, (after - before) / 1048576.0,
                        view.PaintTileCount, view.DetailTileCount, frame, estimate));
                    if (ok && view.CameraRig != null && MapLabStage03Batch.Argument("-mapLabShots", "") == "1")
                    {
                        MapLabCameraRig rig = view.CameraRig;
                        MapLabExport.LinearDownsample = true;
                        MapLabExport.Msaa = s.Stage05.ExportMsaa;
                        void Shot(string name)
                        {
                            string file = Path.Combine(output, $"{side:0}", name + ".png");
                            Directory.CreateDirectory(Path.GetDirectoryName(file));
                            File.WriteAllBytes(file, MapLabExport.RenderPng(rig, 1920, 1080, 2));
                        }
                        rig.Camera.aspect = 1920f / 1080f;
                        rig.Zoom = 1f;
                        rig.Fit();
                        view.OnCameraChanged();
                        Shot("fit");
                        float d = view.Data.Size;
                        string[] names = { "corner_sw", "side_s", "corner_se", "side_w", "side_e", "corner_nw", "side_n", "corner_ne" };
                        Vector2[] at = { new Vector2(0f, 0f), new Vector2(d * 0.5f, 0f), new Vector2(d, 0f), new Vector2(0f, d * 0.5f), new Vector2(d, d * 0.5f), new Vector2(0f, d), new Vector2(d * 0.5f, d), new Vector2(d, d) };
                        foreach (float z in new[] { rig.MaxZoom, rig.MaxZoom * 0.6f })
                        {
                            for (int k = 0; k < names.Length; k++)
                            {
                                rig.Camera.aspect = 1920f / 1080f;
                                rig.Zoom = z;
                                rig.Target = new Vector3(at[k].x, 0f, at[k].y);
                                rig.ClampTarget();
                                rig.Target = rig.ToGround(rig.Target);
                                rig.Apply();
                                view.OnCameraChanged();
                                Vector3 lp = rig.Camera.ViewportToWorldPoint(new Vector3(0f, 0.5f, 0f)), rp = rig.Camera.ViewportToWorldPoint(new Vector3(1f, 0.5f, 0f));
                                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "      {0} z{1:0.#}: цель ({2:0.0}, {3:0.0}), орто {4:0.00}, аспект {5:0.000}, край кадра x {6:0.0}…{7:0.0}, данные 0…{8:0.0}", names[k], z, rig.Target.x, rig.Target.z, rig.Camera.orthographicSize, rig.Camera.aspect, lp.x, rp.x, d));
                                Shot($"{names[k]}_z{z:0.#}");
                            }
                        }
                    }
                    StringBuilder phases = new StringBuilder("      подшаги:");
                    foreach (var p in view.Phases)
                        phases.Append(string.Format(CultureInfo.InvariantCulture, " {0} {1:0}", p.Key, p.Value));
                    report.AppendLine(phases.ToString());
                    report.AppendLine("      " + MapLabScaleBatch.Visible(view));
                    view.Clear();
                    UnityEngine.Object.DestroyImmediate(s);
                    GC.Collect();
                    Resources.UnloadUnusedAssets();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                report.AppendLine("Ошибка: " + exception.Message);
                code = 1;
            }
            File.WriteAllText(Path.Combine(output, "scale_report.txt"), report.ToString());
            Debug.Log(report.ToString());
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }
    }
}
