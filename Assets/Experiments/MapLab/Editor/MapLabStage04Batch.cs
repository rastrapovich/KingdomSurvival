using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace KingdomSurvival.MapLab.Editor
{
    // Этап 04: те же участки речной долины до (Stage03) и после (Stage04) прохода
    // согласования, крупные планы тени, узла тропы, ограды, моста и уступа,
    // слои, двенадцать кадров героя в сцене, общий вид трёх пресетов.
    //   -executeMethod KingdomSurvival.MapLab.Editor.MapLabStage04Batch.Render
    //     -mapLabOut <папка> [-mapLabSize 2048x1365] [-mapLabOnly префиксы]
    public static class MapLabStage04Batch
    {
        private struct Spots
        {
            public Vector2 Plot, Edge, Tree, Junction, Fence, Bridge, Cliff, Hero;
            public bool HasBridge, HasCliff;
        }

        public static void Render()
        {
            int code = 0;
            try
            {
                string output = MapLabStage03Batch.Argument("-mapLabOut", Path.GetFullPath("MapLabExports/Stage04"));
                Vector2Int size = MapLabStage03Batch.ParseSize(MapLabStage03Batch.Argument("-mapLabSize", "2048x1365"));
                string only = MapLabStage03Batch.Argument("-mapLabOnly", "");
                Directory.CreateDirectory(output);
                MapLabAssets.EnsureAllPresets();
                EditorSceneManager.OpenScene(MapLabAssets.ScenePath, OpenSceneMode.Single);
                MapLabView view = MapLabAssets.FindView();
                view.ArtSet = MapLabArtImport.LoadArtSet();
                view.MarkerSet = MapLabArtImport.LoadMarkerSet();
                view.ArtSet03 = MapLabArtImport.LoadStage03Set();
                view.MarkerSet04 = MapLabArtImport.LoadStage04Markers();
                view.InkShader = MapLabAssets.LoadShader("MapLabInk.shader");
                view.GrassShader = MapLabAssets.LoadShader("MapLabGrass.shader");
                StringBuilder report = new StringBuilder();
                MapLabSettings settings = UnityEngine.Object.Instantiate(MapLabAssets.EnsurePreset(MapLabPresetKind.RiverValley));
                view.Settings = settings;
                MapLabCameraRig rig = view.CameraRig;
                MapLabMarker marker = view.Marker;
                bool Want(string name) => string.IsNullOrEmpty(only) || only.Split(',').Any(o => name.StartsWith(o.Trim(), StringComparison.Ordinal));
                void Shot(string name)
                {
                    if (!Want(name))
                        return;
                    string file = Path.Combine(output, name + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllBytes(file, MapLabExport.RenderPng(rig, size.x, size.y, 2));
                    MapLabExportInfo.Write(view, file, size.x, size.y, 2);
                }
                void View(float pitch, float zoom, Vector2 at)
                {
                    settings.Camera.PitchByZoom = false;
                    settings.Camera.Mode = MapLabCameraMode.Custom;
                    settings.Camera.CameraPitchDownDegrees = pitch;
                    rig.Zoom = zoom;
                    rig.Target = new Vector3(at.x, view.Data.SampleHeight(at.x, at.y), at.y);
                    rig.RecomputeFitSize();
                    rig.Apply();
                    rig.Target = rig.ToGround(rig.Target);
                    rig.Apply();
                    view.OnCameraChanged();
                }
                void Diag(MapLabDiagnosticMode mode)
                {
                    settings.Diagnostics.Mode = mode;
                    view.ApplyMaterialSettings();
                    view.ApplyVisibility();
                }
                float maxZoom = rig.MaxZoom;
                Spots spots = default;

                // До и после: один seed, те же места, масштабы и размер кадра.
                foreach (MapLabLook look in new[] { MapLabLook.Stage03, MapLabLook.Stage04 })
                {
                    settings.Stage03.Look = look;
                    long before = Profiler.GetTotalAllocatedMemoryLong();
                    Stopwatch watch = Stopwatch.StartNew();
                    view.Generate(null);
                    float ms = (float)watch.Elapsed.TotalMilliseconds;
                    report.AppendLine($"{look}: генерация {ms:0} мс; {MapLabStage03Batch.Paint(view)}; пучков травы {view.GrassTufts}; {MapLabStage03Batch.Counts(view)}");
                    report.AppendLine($"  память: выделено Unity {Profiler.GetTotalAllocatedMemoryLong() / 1048576} МБ (до {before / 1048576} МБ); текстуры покраски {MapLabStage03Batch.PaintTextureMb(view):0.#} МБ");
                    if (look == MapLabLook.Stage03)
                    {
                        spots = Find(view.Data, view.DemoFocus);
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture, "Участки: развилка {0}; опушка {1}; дерево {2}; узел тропы {3}; ограда {4}; мост {5}; уступ {6}",
                            spots.Plot, spots.Edge, spots.Tree, spots.Junction, spots.Fence, spots.HasBridge ? spots.Bridge.ToString() : "нет", spots.HasCliff ? spots.Cliff.ToString() : "нет"));
                    }
                    string tag = look == MapLabLook.Stage03 ? "before" : "after";
                    if (marker != null)
                    {
                        marker.AutoPlace = false;
                        marker.PreviewMove = false;
                        marker.Position = spots.Hero;
                        marker.FacingDegrees = 150f;
                        marker.Refresh();
                    }
                    View(50f, 4f, spots.Plot);
                    Shot($"{tag}/plot_50_z4");
                    View(50f, maxZoom, spots.Plot);
                    Shot($"{tag}/plot_50_zmax");
                    View(35f, maxZoom, spots.Plot);
                    Shot($"{tag}/plot_35_zmax");
                    View(50f, maxZoom, spots.Tree);
                    Shot($"{tag}/shadow_single_tree_50_zmax");
                    View(50f, 5f, spots.Edge);
                    Shot($"{tag}/shadow_forest_edge_50_z5");
                    View(35f, maxZoom, spots.Junction);
                    Shot($"{tag}/junction_35_zmax");
                    View(50f, maxZoom, spots.Fence);
                    Shot($"{tag}/fence_50_zmax");
                    if (spots.HasBridge)
                    {
                        View(50f, 6f, spots.Bridge);
                        Shot($"{tag}/bridge_50_z6");
                    }
                    if (spots.HasCliff)
                    {
                        foreach (float p in new[] { 60f, 50f, 35f })
                        {
                            View(p, 5f, spots.Cliff);
                            Shot($"{tag}/cliff_{p:0}_z5");
                        }
                    }
                    View(50f, 1f, spots.Plot);
                    rig.Fit();
                    Shot($"{tag}/overview_50_fit");
                }

                // Слои этапа 04 на тех же местах.
                View(50f, maxZoom, spots.Tree);
                Diag(MapLabDiagnosticMode.ShadowMask);
                Shot("layers/shadow_mask_tree");
                Diag(MapLabDiagnosticMode.ContactGradient);
                Shot("layers/contact_gradient_tree");
                Diag(MapLabDiagnosticMode.Final);
                View(50f, 5f, spots.Edge);
                Diag(MapLabDiagnosticMode.ShadowMask);
                Shot("layers/shadow_mask_forest_edge");
                Diag(MapLabDiagnosticMode.Final);
                View(35f, maxZoom, spots.Plot);
                float grain = settings.Stage04.Grain, blots = settings.Stage04.Blots, grass = settings.Stage04.Grass;
                Shot("layers/surface_with_grain_blots_grass");
                settings.Stage04.Grain = 0f;
                settings.Stage04.Blots = 0f;
                settings.Stage04.Grass = 0f;
                view.RebuildStage03Layers();
                Shot("layers/surface_without_grain_blots_grass");
                settings.Stage04.Grain = grain;
                settings.Stage04.Blots = blots;
                settings.Stage04.Grass = grass;
                view.RebuildStage03Layers();
                View(50f, 3f, spots.Plot);
                Shot("layers/blots_z3");

                // Герой: шесть направлений при 50° и при 35° на одном месте, плюс 60° (приближённо).
                if (marker != null && view.ActiveMarkerSet != null)
                {
                    marker.Position = spots.Hero;
                    foreach (float p in new[] { 50f, 35f })
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            View(p, maxZoom, spots.Hero);
                            marker.FacingDegrees = rig.Azimuth + 180f - i * 60f;
                            marker.Refresh();
                            Shot($"hero/{p:0}_{i}_{MapLabMarkerSet.DirectionNames[i]}");
                            report.AppendLine($"Герой {p:0}° {MapLabMarkerSet.DirectionNames[i]}: кадр {(marker.CurrentEntry != null ? marker.CurrentEntry.Id : "нет")}, набор {marker.PitchSetPitch:0}°, приближённо {marker.Approximate}");
                        }
                    }
                    View(60f, maxZoom, spots.Hero);
                    marker.FacingDegrees = rig.Azimuth + 180f;
                    marker.Refresh();
                    Shot("hero/60_front_approximate");
                    report.AppendLine($"Герой 60°: кадр {(marker.CurrentEntry != null ? marker.CurrentEntry.Id : "нет")}, приближённо {marker.Approximate}");
                    // Порог наборов в обе стороны (гистерезис ±1° около 42.5°).
                    StringBuilder walk = new StringBuilder("Смена набора героя по наклону:");
                    foreach (float p in new[] { 50f, 43f, 42f, 41f, 42f, 43f, 44f, 50f })
                    {
                        View(p, maxZoom, spots.Hero);
                        marker.Refresh();
                        walk.Append($" {p:0}°→{marker.PitchSetPitch:0}");
                    }
                    report.AppendLine(walk.ToString());
                    // У дерева и дома — перекрытия.
                    marker.Position = spots.Tree + new Vector2(0.3f, 1.4f);
                    View(50f, maxZoom, marker.Position);
                    marker.Refresh();
                    Shot("hero/behind_tree_50");
                }

                // Кадр и общий вид пресетов.
                foreach (float z in new[] { 1f, 4f, maxZoom })
                {
                    View(z >= maxZoom ? 35f : 50f, z, spots.Plot);
                    report.AppendLine($"Кадр {rig.Pitch:0}° Zoom {z:0.#}: {MapLabStage03Batch.MeasureFrame(rig, size.x, size.y):0.0} мс (среднее 20 рендеров {size.x}×{size.y} в RenderTexture, не FPS игры)");
                }
                if (Want("presets"))
                {
                    foreach (MapLabPresetKind kind in new[] { MapLabPresetKind.HillAndLedge, MapLabPresetKind.ForestLake, MapLabPresetKind.RiverValley })
                    {
                        MapLabSettings preset = UnityEngine.Object.Instantiate(MapLabAssets.EnsurePreset(kind));
                        view.Settings = preset;
                        Stopwatch watch = Stopwatch.StartNew();
                        view.Generate(null);
                        float ms = (float)watch.Elapsed.TotalMilliseconds;
                        preset.Camera.PitchByZoom = false;
                        rig.SetMode(MapLabCameraMode.Volume50);
                        rig.Zoom = 1f;
                        rig.Fit();
                        view.OnCameraChanged();
                        Shot($"presets/{kind}_{preset.General.Seed}_50_fit");
                        report.AppendLine($"{kind} seed {preset.General.Seed}: генерация {ms:0} мс; {MapLabStage03Batch.Paint(view)}; пучков травы {view.GrassTufts}");
                        view.Settings = settings;
                        UnityEngine.Object.DestroyImmediate(preset);
                    }
                }
                report.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType + " · CPU: " + SystemInfo.processorType + " · потоков " + SystemInfo.processorCount);
                File.WriteAllText(Path.Combine(output, "report_stage04.txt"), report.ToString());
                Debug.Log("MapLab stage 04:\n" + report);
                view.Clear();
                UnityEngine.Object.DestroyImmediate(settings);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                code = 1;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        private static Spots Find(MapLabData d, Vector2 focus)
        {
            Spots s = default;
            s.Plot = MapLabStage03Batch.FindPlot(d, focus);
            s.Edge = MapLabStage03Batch.FindForestEdge(d, s.Plot);
            float best = float.MaxValue;
            s.Tree = s.Plot;
            foreach (MapLabPlacement o in d.Objects)
            {
                if (o.Kind != MapLabObjectKind.Deciduous)
                    continue;
                Vector2 p = new Vector2(o.Position.x, o.Position.z);
                float dist = Vector2.Distance(p, s.Plot);
                if (dist < best && dist > 8f && d.SampleMask(d.Forest, p.x, p.y) < 0.15f && d.SampleSlope(p.x, p.y) < 8f && !d.IsUnderWater(p.x, p.y, 1f))
                {
                    best = dist;
                    s.Tree = p;
                }
            }
            // Узел: конец тропы, лежащий у главной дороги.
            best = float.MaxValue;
            s.Junction = s.Plot;
            List<MapLabPolyline> paths = new List<MapLabPolyline>(d.YardPaths);
            foreach (MapLabPolyline r in d.Roads)
                if (r.Kind == MapLabRoadKind.Path)
                    paths.Add(r);
            foreach (MapLabPolyline path in paths)
            {
                if (path.Points.Count < 2)
                    continue;
                foreach (Vector2 end in new[] { path.Points[0], path.Points[path.Points.Count - 1] })
                    foreach (MapLabPolyline road in d.Roads)
                    {
                        if (road.Kind == MapLabRoadKind.Path)
                            continue;
                        float dist = MapLabGeometry.Nearest(road.Points, end, out _, out _, out _, out _);
                        float score = Vector2.Distance(end, s.Plot) + dist * 10f;
                        if (dist < road.Width && score < best)
                        {
                            best = score;
                            s.Junction = end;
                        }
                    }
            }
            best = float.MaxValue;
            s.Fence = s.Plot;
            foreach (MapLabFence f in d.Fences)
            {
                float dist = Vector2.Distance(f.A, s.Plot);
                if (dist < best)
                {
                    best = dist;
                    s.Fence = f.A;
                }
            }
            if (d.Bridges.Count > 0)
            {
                s.HasBridge = true;
                s.Bridge = (d.Bridges[0].EndA + d.Bridges[0].EndB) * 0.5f;
            }
            best = float.MaxValue;
            foreach (MapLabCliffLine line in d.CliffLines)
                foreach (MapLabCliffColumn c in line.Columns)
                {
                    Vector2 mid = (c.Start + c.End) * 0.5f;
                    float dist = Vector2.Distance(mid, s.Plot);
                    if (Vector2.Dot(c.Outward, Vector2.down) > 0.6f && c.Top - c.Bottom > 3f && dist < best)
                    {
                        best = dist;
                        s.Cliff = mid + c.Outward * 2f;
                        s.HasCliff = true;
                    }
                }
            // Герой — на ровной дороге у развилки, не в узле.
            s.Hero = s.Plot;
            best = float.MaxValue;
            foreach (MapLabPolyline road in d.Roads)
                foreach (Vector2 p in MapLabGeometry.Resample(road.Points, 1.5f))
                {
                    float dist = Mathf.Abs(Vector2.Distance(p, s.Plot) - 9f);
                    if (dist < best && d.SampleSlope(p.x, p.y) < 4f && !d.IsUnderWater(p.x, p.y, 1f))
                    {
                        best = dist;
                        s.Hero = p;
                    }
                }
            return s;
        }
    }
}
