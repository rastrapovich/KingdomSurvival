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
    // Этап 03: фиксированный участок (развилка дороги, луг, поле, опушка, дом)
    // речной долины — замеры резкости и раздельные кадры без коллажей.
    //   -executeMethod KingdomSurvival.MapLab.Editor.MapLabStage03Batch.Render
    //     -mapLabOut <папка> [-mapLabSize 2048x1365] [-mapLabOnly префиксы] [-mapLabLook 02|03]
    public static class MapLabStage03Batch
    {
        public static void Render()
        {
            int code = 0;
            try
            {
                string output = Argument("-mapLabOut", Path.GetFullPath("MapLabExports/Stage03"));
                Vector2Int size = ParseSize(Argument("-mapLabSize", "2048x1365"));
                string only = Argument("-mapLabOnly", "");
                bool stage02 = Argument("-mapLabLook", "03") == "02";
                Directory.CreateDirectory(output);
                MapLabAssets.EnsureAllPresets();
                EditorSceneManager.OpenScene(MapLabAssets.ScenePath, OpenSceneMode.Single);
                MapLabView view = MapLabAssets.FindView();
                view.ArtSet = MapLabArtImport.LoadArtSet();
                view.MarkerSet = MapLabArtImport.LoadMarkerSet();
                view.ArtSet03 = MapLabArtImport.LoadStage03Set();
                if (view.InkShader == null)
                    view.InkShader = MapLabAssets.LoadShader("MapLabInk.shader");
                StringBuilder report = new StringBuilder();
                MapLabSettings settings = UnityEngine.Object.Instantiate(MapLabAssets.EnsurePreset(MapLabPresetKind.RiverValley));
                settings.Stage03.Look = stage02 ? MapLabLook.Stage02 : MapLabLook.Stage03;
                view.Settings = settings;
                long before = Profiler.GetTotalAllocatedMemoryLong();
                Stopwatch watch = Stopwatch.StartNew();
                view.Generate(null);
                float generate = (float)watch.Elapsed.TotalMilliseconds;
                MapLabCameraRig rig = view.CameraRig;
                report.AppendLine($"Язык {settings.Stage03.Look}; пресет C seed {settings.General.Seed}: генерация {generate:0} мс; {Paint(view)}; {Counts(view)}");
                report.AppendLine($"Память: выделено Unity {Profiler.GetTotalAllocatedMemoryLong() / 1048576} МБ (до генерации {before / 1048576} МБ), пик управляемой кучи {GC.GetTotalMemory(false) / 1048576} МБ; текстуры покраски {PaintTextureMb(view):0.#} МБ");
                if (Argument("-mapLabProfile", "") == "1")
                {
                    File.WriteAllText(Path.Combine(output, "report_stage03.txt"), report.ToString());
                    Debug.Log("MapLab stage 03:\n" + report);
                    view.Clear();
                    if (Application.isBatchMode)
                        EditorApplication.Exit(0);
                    return;
                }
                Vector2 plot = FindPlot(view.Data, view.DemoFocus);
                Vector2 edge = FindForestEdge(view.Data, plot);
                Vector2 mid = Vector2.Lerp(plot, edge, 0.55f);
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "Участок (развилка): {0:0.0}, {1:0.0}; опушка: {2:0.0}, {3:0.0}", plot.x, plot.y, edge.x, edge.y));

                bool Want(string name) => string.IsNullOrEmpty(only) || only.Split(',').Any(o => name.StartsWith(o.Trim(), StringComparison.Ordinal));
                void Shot(string name, int supersample = 2)
                {
                    if (!Want(name))
                        return;
                    string file = Path.Combine(output, name + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllBytes(file, MapLabExport.RenderPng(rig, size.x, size.y, supersample));
                    MapLabExportInfo.Write(view, file, size.x, size.y, supersample);
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
                void ByZoom(float zoom, Vector2 at)
                {
                    settings.Camera.PitchByZoom = true;
                    rig.Zoom = zoom;
                    rig.Target = new Vector3(at.x, view.Data.SampleHeight(at.x, at.y), at.y);
                    rig.RecomputeFitSize();
                    rig.Apply();
                    rig.Target = rig.ToGround(rig.Target);
                    rig.Apply();
                    view.OnCameraChanged();
                }
                void Restyle()
                {
                    view.RebuildStage03Layers();
                    view.ApplyMaterialSettings();
                    view.ApplyVisibility();
                }

                float maxZoom = rig.ZoomLimits.y;
                // 1. Тот же участок, тот же масштаб, что в 00_baseline.
                foreach (float pitch in new[] { 50f, 35f })
                {
                    View(pitch, maxZoom, plot);
                    report.AppendLine(Density(view, rig, size, pitch));
                    Shot($"plot_{pitch:0}_zmax");
                    Shot($"plot_{pitch:0}_zmax_ss1", 1);
                }
                View(50f, 2f, plot);
                Shot("plot_50_z2");
                View(50f, 4f, plot);
                Shot("plot_50_z4");
                // 2. Крупный вид 35°: дорога, поле, лес и дом; наклон от приближения.
                View(35f, 4f, mid);
                Shot("close_35_z4");
                ByZoom(maxZoom, mid);
                report.AppendLine($"Наклон от приближения: Zoom {rig.Zoom:0.##} (предел рига {maxZoom:0.##}) → {rig.Pitch:0.##}°; Zoom 1 → {settings.Camera.PitchFor(1f, maxZoom):0.##}°, Zoom 2 → {settings.Camera.PitchFor(2f, maxZoom):0.##}°, Zoom 4 → {settings.Camera.PitchFor(4f, maxZoom):0.##}°; кадр домов {view.HouseView}°");
                Shot("close_byzoom_zmax");
                // 3. Лес: акценты вкл/выкл и диагностика групп.
                View(50f, 4f, edge);
                Shot("forest_50_z4_accents_on");
                View(35f, 6f, edge);
                Shot("forest_35_z6_accents_on");
                settings.Stage03.ForestAccents = false;
                view.ApplyMaterialSettings();
                Shot("forest_35_z6_accents_off");
                View(50f, 4f, edge);
                Shot("forest_50_z4_accents_off");
                settings.Stage03.ForestAccents = true;
                settings.Diagnostics.Mode = MapLabDiagnosticMode.ForestGroups;
                view.ApplyMaterialSettings();
                view.ApplyVisibility();
                Shot("layers/forest_groups");
                settings.Diagnostics.Mode = MapLabDiagnosticMode.ForestAccents;
                view.ApplyMaterialSettings();
                Shot("layers/forest_accent_mask");
                settings.Diagnostics.Mode = MapLabDiagnosticMode.Final;
                view.ApplyMaterialSettings();
                view.ApplyVisibility();
                // 4. Тени вкл/выкл и маска (тот же участок, тот же масштаб).
                View(50f, 4f, mid);
                Shot("shadows_on");
                settings.Diagnostics.Mode = MapLabDiagnosticMode.ShadowMask;
                view.ApplyMaterialSettings();
                view.ApplyVisibility();
                Shot("layers/shadow_mask");
                settings.Diagnostics.Mode = MapLabDiagnosticMode.Final;
                settings.Stage03.ShadowStrength = 0f;
                settings.Stage03.ContactStrength = 0f;
                view.ApplyMaterialSettings();
                view.ApplyVisibility();
                Shot("shadows_off");
                MapLabSettings reference = MapLabAssets.EnsurePreset(MapLabPresetKind.RiverValley);
                settings.Stage03.ShadowStrength = reference.Stage03.ShadowStrength;
                settings.Stage03.ContactStrength = reference.Stage03.ContactStrength;
                view.ApplyMaterialSettings();

                // 5. Слои на максимальном приближении 50°: исходная краска, вклад
                // мазков, без мазков, без подложки, маска дорог, итог.
                View(50f, maxZoom, plot);
                MapLabDiagnosticsSettings d = settings.Diagnostics;
                d.Mode = MapLabDiagnosticMode.SourcePaint;
                view.ApplyMaterialSettings();
                view.ApplyVisibility();
                Shot("layers/source_paint");
                d.Mode = MapLabDiagnosticMode.StrokeContribution;
                view.ApplyMaterialSettings();
                view.ApplyVisibility();
                Shot("layers/stroke_contribution");
                d.Mode = MapLabDiagnosticMode.RoadGraph;
                view.ApplyMaterialSettings();
                view.ApplyVisibility();
                Shot("layers/road_mask_with_ink");
                d.Mode = MapLabDiagnosticMode.Final;
                view.ApplyMaterialSettings();
                view.ApplyVisibility();
                Shot("layers/final");
                settings.Art.GroundTextures = false;
                view.ApplyMaterialSettings();
                Shot("layers/final_no_undercoat");
                settings.Art.GroundTextures = true;
                d.ShowStrokes = false;
                Restyle();
                Shot("layers/final_no_strokes");
                d.ShowStrokes = true;
                settings.Stage03.Ink = false;
                Restyle();
                Shot("layers/final_no_ink");
                settings.Stage03.Ink = true;
                Restyle();

                // 6. Замеры кадра (не FPS игры: offscreen-рендер в RenderTexture).
                foreach (float z in new[] { 1f, 4f, maxZoom })
                {
                    View(z >= maxZoom ? 35f : 50f, z, plot);
                    report.AppendLine($"Кадр {rig.Pitch:0}° Zoom {z:0.#}: {MeasureFrame(rig, size.x, size.y):0.0} мс (среднее 20 рендеров {size.x}×{size.y} в RenderTexture)");
                }

                // 7. Обзор трёх пресетов (каждый — свой seed пресета).
                if (Want("presets"))
                {
                    foreach (MapLabPresetKind kind in new[] { MapLabPresetKind.HillAndLedge, MapLabPresetKind.ForestLake, MapLabPresetKind.RiverValley })
                    {
                        MapLabSettings preset = UnityEngine.Object.Instantiate(MapLabAssets.EnsurePreset(kind));
                        preset.Stage03.Look = settings.Stage03.Look;
                        view.Settings = preset;
                        watch.Restart();
                        view.Generate(null);
                        float ms = (float)watch.Elapsed.TotalMilliseconds;
                        preset.Camera.PitchByZoom = false;
                        rig.SetMode(MapLabCameraMode.Volume50);
                        rig.Zoom = 1f;
                        rig.Fit();
                        view.OnCameraChanged();
                        Shot($"presets/{kind}_{preset.General.Seed}_50_fit");
                        Vector2 focus = kind == MapLabPresetKind.RiverValley ? mid : FindForestEdge(view.Data, view.DemoFocus);
                        rig.Zoom = 3f;
                        rig.Target = rig.ToGround(new Vector3(focus.x, view.Data.SampleHeight(focus.x, focus.y), focus.y));
                        rig.Apply();
                        view.OnCameraChanged();
                        Shot($"presets/{kind}_{preset.General.Seed}_50_z3");
                        report.AppendLine($"{kind} seed {preset.General.Seed}: генерация {ms:0} мс; {Paint(view)}; {Counts(view)}");
                        view.Settings = settings;
                        UnityEngine.Object.DestroyImmediate(preset);
                    }
                }

                report.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType + " · CPU: " + SystemInfo.processorType + " · потоков " + SystemInfo.processorCount);
                File.WriteAllText(Path.Combine(output, "report_stage03.txt"), report.ToString());
                Debug.Log("MapLab stage 03:\n" + report);
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

        private static string Paint(MapLabView view)
        {
            MapLabTiledPainter.Result p = view.LastTiledPaint;
            MapLabStats st = view.Data.Stats;
            if (view.Settings.IsStage03 && p != null)
                return string.Format(CultureInfo.InvariantCulture,
                    "покраска {0:0} мс (поля {1:0} мс; потоки: дороги {12:0}, основа {13:0}, мазки {14:0}, упаковка {15:0} мс), {2:0.#} текс./ед., тайлов {3} × {4}², мазков {5}, проходов мазка на тексель {6:0.00}; перо {7} линий {8:0} ед.; теней {9} ({10}² , {11:0} мс)",
                    st.PaintMs, p.FieldsMs, p.TexelsPerUnit, p.Tiles.Count, p.Tiles.Count > 0 ? p.Tiles[0].Size : 0, p.Strokes, p.StampedTexels / (double)Math.Max(1, p.Texels),
                    view.InkStats.Polylines, view.InkStats.Length, view.ShadowShapes, view.ShadowResolution, view.ShadowMs,
                    p.RoadsThreadMs, p.BaseThreadMs, p.StrokesThreadMs, p.PackThreadMs);
            return $"покраска {st.PaintMs:0} мс, {view.Settings.PaintResolution}²";
        }

        private static float PaintTextureMb(MapLabView view)
        {
            MapLabTiledPainter.Result p = view.LastTiledPaint;
            if (!view.Settings.IsStage03 || p == null || p.Tiles.Count == 0)
                return view.Settings.PaintResolution * (float)view.Settings.PaintResolution * 4f * 1.333f / 1048576f;
            float tile = p.Tiles[0].Size * (float)p.Tiles[0].Size * 4f * 1.333f;
            float overview = p.OverviewResolution * (float)p.OverviewResolution * 4f * 1.333f;
            return (tile * p.Tiles.Count + overview) / 1048576f;
        }

        private static string Counts(MapLabView view)
        {
            MapLabData d = view.Data;
            MapLabStats s = d.Stats;
            MapLabArtBuilder.ForestStats f = MapLabArtBuilder.LastForest;
            return $"рендереров {s.Renderers}, материалов {s.Materials}, вершин {s.Vertices}, карточек {(view.ArtPlan != null ? view.ArtPlan.Count : 0)}, " +
                   $"групп крон {f.Groups}, с акцентом {f.AccentGroups} (цепочек {f.Chains}, крон {f.AccentTrees}), домов {d.CountObjects(MapLabObjectKind.House)}";
        }

        // Плотность покраски против экрана: сколько экранных пикселей приходится
        // на тексель. Камера ортографическая: по горизонтали масштаб одинаков во
        // всём кадре, по глубине экран сжимает землю в sin(наклона) раз.
        public static string Density(MapLabView view, MapLabCameraRig rig, Vector2Int size, float pitch)
        {
            float texelsPerUnit = view.PaintTexelsPerUnit;
            float pxPerUnitX = size.x / (rig.Camera.orthographicSize * 2f * size.x / size.y);
            float pxPerUnitZ = pxPerUnitX * Mathf.Sin(pitch * Mathf.Deg2Rad);
            float mip = Mathf.Max(0f, Mathf.Log(texelsPerUnit / pxPerUnitZ, 2f));
            return string.Format(CultureInfo.InvariantCulture,
                "Наклон {0:0}° Zoom {1:0.##}: покраска {2:0.#} тексель/ед.; экран {3:0.#} пикс/ед. по X и {4:0.#} по глубине; " +
                "увеличение текстуры ×{5:0.#} (X) и ×{6:0.#} (глубина); оценка mip по глубине {7:0.##}; ортография — одинаково в центре и у дальнего края",
                pitch, rig.Zoom, texelsPerUnit, pxPerUnitX, pxPerUnitZ, pxPerUnitX / texelsPerUnit, pxPerUnitZ / texelsPerUnit, mip);
        }

        // Развилка: конец одной дороги, лежащий на другой, ближайший к поселению.
        public static Vector2 FindPlot(MapLabData data, Vector2 focus)
        {
            Vector2 best = focus;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < data.Roads.Count; i++)
            {
                List<Vector2> points = data.Roads[i].Points;
                if (points.Count < 2)
                    continue;
                foreach (Vector2 end in new[] { points[0], points[points.Count - 1] })
                {
                    for (int j = 0; j < data.Roads.Count; j++)
                    {
                        if (j == i)
                            continue;
                        List<Vector2> other = data.Roads[j].Points;
                        for (int s = 0; s < other.Count - 1; s++)
                        {
                            if (MapLabGeometry.DistanceToSegment(end, other[s], other[s + 1], out _) > 1.5f)
                                continue;
                            float dist = Vector2.Distance(end, focus);
                            if (dist < bestDistance && !data.IsUnderWater(end.x, end.y))
                            {
                                bestDistance = dist;
                                best = end;
                            }
                        }
                    }
                }
            }
            return best;
        }

        // Ближайшая опушка: точка с плотностью леса около половины.
        public static Vector2 FindForestEdge(MapLabData data, Vector2 from)
        {
            Vector2 best = from;
            float bestDistance = float.MaxValue;
            for (float z = 4f; z < data.Size - 4f; z += 2f)
            for (float x = 4f; x < data.Size - 4f; x += 2f)
            {
                float f = data.SampleMask(data.Forest, x, z);
                if (f < 0.42f || f > 0.6f || data.IsUnderWater(x, z) || data.SampleMask(data.Forest, x, z + 8f) < 0.7f)
                    continue;
                float dist = Vector2.Distance(new Vector2(x, z), from);
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    best = new Vector2(x, z);
                }
            }
            return best;
        }

        private static float MeasureFrame(MapLabCameraRig rig, int width, int height)
        {
            Camera cam = rig.Camera;
            RenderTexture rt = new RenderTexture(width, height, 24, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB);
            RenderTexture previous = cam.targetTexture;
            Texture2D probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                Stopwatch watch = Stopwatch.StartNew();
                const int frames = 20;
                for (int i = 0; i < frames; i++)
                {
                    cam.Render();
                    RenderTexture.active = rt;
                    probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                }
                return (float)watch.Elapsed.TotalMilliseconds / frames;
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = previous;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        private static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name)
                    return args[i + 1];
            return fallback;
        }

        private static Vector2Int ParseSize(string text)
        {
            string[] parts = text.ToLowerInvariant().Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                return new Vector2Int(w, h);
            return new Vector2Int(2048, 1365);
        }
    }
}
