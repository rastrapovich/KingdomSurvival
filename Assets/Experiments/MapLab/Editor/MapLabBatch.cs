using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace KingdomSurvival.MapLab.Editor
{
    // Запуск из командной строки (-batchmode без -nographics):
    //   -executeMethod KingdomSurvival.MapLab.Editor.MapLabBatch.RenderPresets
    //     -mapLabOut <папка> [-mapLabPresets A,B,C] [-mapLabSeeds 104,105] [-mapLabSize 1536x1024]
    //   -executeMethod KingdomSurvival.MapLab.Editor.MapLabBatch.CompatibilityCheck -mapLabOut <папка>
    public static class MapLabBatch
    {
        public static void RenderPresets()
        {
            int code = 0;
            try
            {
                string output = Argument("-mapLabOut", Path.GetFullPath("MapLabExports"));
                Directory.CreateDirectory(output);
                Vector2Int size = ParseSize(Argument("-mapLabSize", "1536x1024"));
                string presets = Argument("-mapLabPresets", "A,B,C");
                string seeds = Argument("-mapLabSeeds", "");
                string mode = Argument("-mapLabMode", "Final");
                float zoom = float.Parse(Argument("-mapLabZoom", "1"), System.Globalization.CultureInfo.InvariantCulture);
                string[] focusParts = Argument("-mapLabFocus", "0,0").Split(',');
                Vector2 focus = new Vector2(float.Parse(focusParts[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(focusParts[1], System.Globalization.CultureInfo.InvariantCulture));
                MapLabAssets.EnsureAllPresets();
                if (!File.Exists(MapLabAssets.ScenePath))
                    MapLabAssets.CreateTestScene(false);
                EditorSceneManager.OpenScene(MapLabAssets.ScenePath, OpenSceneMode.Single);
                MapLabView view = MapLabAssets.FindView();
                StringBuilder report = new StringBuilder();
                foreach (string token in presets.Split(','))
                {
                    MapLabPresetKind kind = token.Trim().ToUpperInvariant() switch
                    {
                        "B" => MapLabPresetKind.ForestLake,
                        "C" => MapLabPresetKind.RiverValley,
                        _ => MapLabPresetKind.HillAndLedge,
                    };
                    MapLabSettings source = MapLabAssets.EnsurePreset(kind);
                    // Работаем с копией: ассет пресета не меняется.
                    MapLabSettings settings = UnityEngine.Object.Instantiate(source);
                    string[] modes = mode.Split(',');
                    settings.Diagnostics.Mode = MapLabDiagnosticMode.Final;
                    List<int> seedList = new List<int>();
                    if (string.IsNullOrEmpty(seeds))
                        seedList.Add(source.General.Seed);
                    else
                        foreach (string s in seeds.Split(','))
                            seedList.Add(int.Parse(s.Trim()) + (s.Trim().StartsWith("+") ? source.General.Seed : 0));
                    foreach (int seed in seedList)
                    {
                        settings.General.Seed = seed;
                        view.Settings = settings;
                        Stopwatch watch = Stopwatch.StartNew();
                        view.Generate(null);
                        float generate = (float)watch.Elapsed.TotalMilliseconds;
                        MapLabData d0 = view.Data;
                        watch.Restart();
                        string file = Path.Combine(output, $"{token.Trim()}_{kind}_{seed}{(zoom != 1f ? "_z" + zoom.ToString(System.Globalization.CultureInfo.InvariantCulture) : "")}.png");
                        if (zoom != 1f || focus != Vector2.zero)
                        {
                            view.CameraRig.Zoom = zoom;
                            view.CameraRig.Fit();
                            view.CameraRig.Target += new Vector3(focus.x * d0.Size, 0f, focus.y * d0.Size);
                            view.CameraRig.Apply();
                        }
                        File.WriteAllBytes(file, MapLabExport.RenderPng(view.CameraRig, size.x, size.y, 2));
                        // Дополнительные режимы диагностики — без перегенерации (только материалы).
                        for (int m = 1; m < modes.Length; m++)
                        {
                            settings.Diagnostics.Mode = (MapLabDiagnosticMode)Enum.Parse(typeof(MapLabDiagnosticMode), modes[m].Trim());
                            view.ApplyMaterialSettings();
                            view.ApplyVisibility();
                            File.WriteAllBytes(Path.ChangeExtension(file, null) + "_" + modes[m].Trim() + ".png", MapLabExport.RenderPng(view.CameraRig, size.x / 2, size.y / 2, 1));
                        }
                        settings.Diagnostics.Mode = MapLabDiagnosticMode.Final;
                        view.ApplyMaterialSettings();
                        view.ApplyVisibility();
                        float render = (float)watch.Elapsed.TotalMilliseconds;
                        float frame = MeasureFrame(view.CameraRig, size.x, size.y);
                        MapLabExportInfo.Write(view, file, size.x, size.y);
                        MapLabData d = view.Data;
                        MapLabStats st = d.Stats;
                        string line = $"{kind} seed {seed}: генерация {generate:0} мс (рельеф {st.TerrainMs:0}, покраска {st.PaintMs:0}), рендер+экспорт {render:0} мс; " +
                                      $"вершин {st.Vertices}, треугольников {st.Triangles}, рендереров {st.Renderers}, материалов {st.Materials}; " +
                                      $"деревьев {d.CountObjects(MapLabObjectKind.Deciduous) + d.CountObjects(MapLabObjectKind.Conifer) + d.CountObjects(MapLabObjectKind.DeciduousGroup)}, " +
                                      $"кустов {d.CountObjects(MapLabObjectKind.Bush)}, валунов {d.CountObjects(MapLabObjectKind.Boulder)}, домов {d.CountObjects(MapLabObjectKind.House)}, " +
                                      $"мостов {d.Bridges.Count}, столбов скал {CountColumns(d)}, полей {d.FieldBlocks.Count}; высоты {d.MinHeight:0.0}–{d.MaxHeight:0.0}; " +
                                      $"кадр {size.x}×{size.y} (среднее 20 рендеров, CPU+GPU) {frame:0.0} мс";
                        report.AppendLine(line);
                        Debug.Log("MapLab: " + line);
                    }
                    view.Clear();
                    UnityEngine.Object.DestroyImmediate(settings);
                }
                report.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType + " · CPU: " + SystemInfo.processorType);
                File.WriteAllText(Path.Combine(output, "report.txt"), report.ToString());
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                code = 1;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        // Этап 02: один и тот же участок речной долины в разных режимах.
        //   -executeMethod KingdomSurvival.MapLab.Editor.MapLabBatch.RenderStage02 -mapLabOut <папка> [-mapLabSize 2048x1365]
        public static void RenderStage02()
        {
            int code = 0;
            try
            {
                string output = Argument("-mapLabOut", Path.GetFullPath("MapLabExports/Stage02"));
                Vector2Int size = ParseSize(Argument("-mapLabSize", "2048x1365"));
                string only = Argument("-mapLabOnly", "");
                Directory.CreateDirectory(output);
                MapLabAssets.EnsureAllPresets();
                if (!File.Exists(MapLabAssets.ScenePath))
                    MapLabAssets.CreateTestScene(false);
                EditorSceneManager.OpenScene(MapLabAssets.ScenePath, OpenSceneMode.Single);
                MapLabView view = MapLabAssets.FindView();
                view.ArtSet = MapLabArtImport.LoadArtSet();
                view.MarkerSet = MapLabArtImport.LoadMarkerSet();
                if (view.ArtSet == null)
                    throw new Exception("Набор арта не подключён — сначала MapLabArtImport.ImportBatch.");
                StringBuilder report = new StringBuilder();
                MapLabSettings settings = UnityEngine.Object.Instantiate(MapLabAssets.EnsurePreset(MapLabPresetKind.RiverValley));
                view.Settings = settings;
                Stopwatch watch = Stopwatch.StartNew();
                view.Generate(null);
                float generate = (float)watch.Elapsed.TotalMilliseconds;
                MapLabCameraRig rig = view.CameraRig;
                MapLabMarker marker = view.Marker;
                Vector2 focus = view.DemoFocus;
                report.AppendLine($"Пресет C seed {settings.General.Seed}: генерация с артом {generate:0} мс; участок {focus}; {Counts(view)}");

                bool Want(string name) => string.IsNullOrEmpty(only) || only.Split(',').Any(o => name.StartsWith(o.Trim(), StringComparison.Ordinal));
                void Shot(string name)
                {
                    if (!Want(name))
                        return;
                    string file = Path.Combine(output, name + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllBytes(file, MapLabExport.RenderPng(rig, size.x, size.y, 2));
                    MapLabExportInfo.Write(view, file, size.x, size.y);
                }
                void Mode(MapLabCameraMode mode, float zoom, Vector2 at)
                {
                    rig.SetMode(mode);
                    rig.Zoom = zoom;
                    rig.Target = rig.ToGround(new Vector3(at.x, view.Data.SampleHeight(at.x, at.y), at.y));
                    rig.Apply();
                    view.OnCameraChanged();
                }
                void Art(bool on)
                {
                    settings.Art.Vegetation = on ? MapLabArtMode.Art : MapLabArtMode.Procedural;
                    settings.Art.Boulders = on ? MapLabArtMode.Art : MapLabArtMode.Procedural;
                    settings.Art.Buildings = on ? MapLabBuildingDisplay.AutoByPitch : MapLabBuildingDisplay.Geometry;
                    settings.Art.CliffDecor = on;
                    settings.Art.GroundTextures = on;
                    settings.Art.ShowMarker = on;
                    watch.Restart();
                    view.RebuildArt();
                    report.AppendLine($"Переключение арта {(on ? "вкл" : "выкл")}: {watch.Elapsed.TotalMilliseconds:0} мс; {Counts(view)}");
                }
                if (marker != null)
                {
                    marker.AutoPlace = true;
                    marker.OnMapBuilt(view);
                }
                Vector2 plot = marker != null && marker.PreviewPath.Count > 0 ? Vector2.Lerp(focus, marker.Position, 0.5f) : focus;

                // Процедурный вид того же участка (A/B при том же seed).
                Art(false);
                Mode(MapLabCameraMode.Volume50, 2f, plot);
                Shot("01_procedural_50_z2");
                Mode(MapLabCameraMode.Atlas60, 1f, plot);
                rig.Fit();
                Shot("01_procedural_60_z1_fit");
                Art(true);
                Mode(MapLabCameraMode.Volume50, 2f, plot);
                Shot("02_art_50_z2");
                Mode(MapLabCameraMode.Diorama40, 2f, plot);
                Shot("03_art_40_z2");
                Mode(MapLabCameraMode.Atlas60, 2f, plot);
                Shot("04_art_60_z2");
                Mode(MapLabCameraMode.Volume50, 4f, plot);
                Shot("05_art_50_z4");
                Mode(MapLabCameraMode.Volume50, 1f, plot);
                rig.Fit();
                Shot("06_art_50_z1_fit");

                // Матрица 3 наклона × 3 приближения (отдельные файлы).
                foreach (MapLabCameraMode m in new[] { MapLabCameraMode.Atlas60, MapLabCameraMode.Volume50, MapLabCameraMode.Diorama40 })
                    foreach (float z in new[] { 1f, 2f, 4f })
                    {
                        Mode(m, z, plot);
                        Shot($"matrix/{(int)MapLabCameraSettings.PitchOf(m)}deg_z{z:0}");
                    }

                // Фишка: шесть направлений на месте, крупно, со стрелкой.
                if (marker != null && view.MarkerSet != null)
                {
                    marker.AutoPlace = false;
                    marker.PreviewMove = false;
                    marker.ShowArrow = true;
                    Vector2 spot = marker.Position;
                    for (int i = 0; i < 6; i++)
                    {
                        marker.Position = spot;
                        marker.FacingDegrees = rig.Azimuth + 180f - i * 60f;
                        Mode(MapLabCameraMode.Volume50, 9f, spot);
                        marker.Refresh();
                        Shot($"figure/{i}_{MapLabMarkerSet.DirectionNames[i]}_50");
                    }
                    marker.FacingDegrees = rig.Azimuth + 180f;
                    foreach (MapLabCameraMode m in new[] { MapLabCameraMode.Diorama40, MapLabCameraMode.Atlas60 })
                    {
                        Mode(m, 9f, spot);
                        marker.Refresh();
                        Shot($"figure/Front_calibration_{(int)MapLabCameraSettings.PitchOf(m)}");
                    }
                    Mode(MapLabCameraMode.Diorama40, 9f, spot);
                    marker.FacingDegrees = rig.Azimuth + 120f;
                    marker.Refresh();
                    Shot("figure/Front_Right_40_approximate");
                    marker.ShowArrow = false;

                    // Перекрытия: перед/за деревом, перед/за домом, у подножия и над уступом, склон.
                    settings.Art.MarkerGhost = true;
                    foreach ((string name, Vector2 at, float facing) in OverlapSpots(view))
                    {
                        marker.Position = at;
                        marker.FacingDegrees = facing;
                        Mode(MapLabCameraMode.Volume50, 7f, at);
                        marker.Refresh();
                        Shot("overlap/" + name);
                    }
                    settings.Art.MarkerGhost = false;
                    marker.AutoPlace = true;
                    marker.OnMapBuilt(view);
                }

                // Альфа: результат AlphaClip на карте и тестовая полоса.
                settings.Art.AlphaView = MapLabAlphaView.ClipResult;
                view.ApplyMaterialSettings();
                Mode(MapLabCameraMode.Volume50, 5f, plot);
                Shot("alpha/clip_result_map_z5");
                settings.Art.AlphaView = MapLabAlphaView.Off;
                view.ApplyMaterialSettings();
                List<MapLabArtEntry> stripEntries = new List<MapLabArtEntry>();
                foreach (string id in new[] { "Tree_Oak_01", "House_Thatch_01", "Figure_Front", "Bush_01", "Tree_Spruce_01" })
                {
                    MapLabArtEntry e = view.ArtSet.Find(id);
                    if (e != null && e.HasTexture)
                        stripEntries.Add(e);
                }
                Vector3 stripCenter = view.BuildArtStrip(stripEntries);
                rig.SetMode(MapLabCameraMode.Volume50);
                rig.Zoom = 5.5f;
                rig.Target = stripCenter + Vector3.up * 2f;
                rig.Apply();
                Shot("alpha/test_strip");
                view.ClearArtStrip();

                // Замеры: кадр в RenderTexture и число отрисовщиков.
                foreach (float z in new[] { 1f, 4f })
                {
                    Mode(MapLabCameraMode.Volume50, z, plot);
                    report.AppendLine($"Кадр Art 50° Zoom {z}: {MeasureFrame(rig, size.x, size.y):0.0} мс (среднее 20 рендеров {size.x}×{size.y})");
                }
                Art(false);
                Mode(MapLabCameraMode.Volume50, 1f, plot);
                report.AppendLine($"Кадр Procedural 50° Zoom 1: {MeasureFrame(rig, size.x, size.y):0.0} мс");
                Art(true);

                // Пресеты A/B/C: три seed — композиционная работоспособность с артом.
                if (Want("seeds"))
                {
                    foreach (MapLabPresetKind kind in new[] { MapLabPresetKind.HillAndLedge, MapLabPresetKind.ForestLake, MapLabPresetKind.RiverValley })
                    {
                        MapLabSettings preset = UnityEngine.Object.Instantiate(MapLabAssets.EnsurePreset(kind));
                        for (int k = 0; k < 3; k++)
                        {
                            preset.General.Seed = MapLabAssets.EnsurePreset(kind).General.Seed + k;
                            view.Settings = preset;
                            watch.Restart();
                            view.Generate(null);
                            float ms = (float)watch.Elapsed.TotalMilliseconds;
                            rig.SetMode(MapLabCameraMode.Volume50);
                            rig.Fit();
                            Shot($"seeds/{kind}_{preset.General.Seed}");
                            report.AppendLine($"{kind} seed {preset.General.Seed}: генерация {ms:0} мс; {Counts(view)}");
                        }
                        UnityEngine.Object.DestroyImmediate(preset);
                    }
                }
                report.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType + " · CPU: " + SystemInfo.processorType);
                File.WriteAllText(Path.Combine(output, "report_stage02.txt"), report.ToString());
                Debug.Log("MapLab stage 02:\n" + report);
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

        private static string Counts(MapLabView view)
        {
            MapLabData d = view.Data;
            if (d == null)
                return "";
            MapLabStats s = d.Stats;
            return $"рендереров {s.Renderers}, материалов {s.Materials}, вершин {s.Vertices}, карточек арта {(view.ArtPlan != null ? view.ArtPlan.Count : 0)}, " +
                   $"деревьев {d.CountObjects(MapLabObjectKind.Deciduous) + d.CountObjects(MapLabObjectKind.Conifer) + d.CountObjects(MapLabObjectKind.DeciduousGroup)}, домов {d.CountObjects(MapLabObjectKind.House)}";
        }

        // Точки проверки перекрытий фишки рядом с участком (камера смотрит в +Z).
        private static List<(string, Vector2, float)> OverlapSpots(MapLabView view)
        {
            MapLabData d = view.Data;
            Vector2 focus = view.DemoFocus;
            List<(string, Vector2, float)> spots = new List<(string, Vector2, float)>();
            float best = float.MaxValue;
            Vector2 tree = Vector2.zero, house = Vector2.zero;
            float bestHouse = float.MaxValue;
            foreach (MapLabPlacement o in d.Objects)
            {
                Vector2 p = new Vector2(o.Position.x, o.Position.z);
                float dist = Vector2.Distance(p, focus);
                if ((o.Kind == MapLabObjectKind.Deciduous) && dist < best && !d.IsUnderWater(p.x, p.y - 1.5f) && d.SampleSlope(p.x, p.y) < 10f)
                {
                    best = dist;
                    tree = p;
                }
                if (o.Kind == MapLabObjectKind.House && dist < bestHouse)
                {
                    bestHouse = dist;
                    house = p;
                }
            }
            if (best < float.MaxValue)
            {
                spots.Add(("tree_front", tree + new Vector2(0.6f, -1.6f), 200f));
                spots.Add(("tree_behind", tree + new Vector2(0.3f, 1.2f), 160f));
            }
            if (bestHouse < float.MaxValue)
            {
                spots.Add(("house_front", house + new Vector2(0.5f, -4.2f), 180f));
                spots.Add(("house_behind", house + new Vector2(0.4f, 2.6f), 0f));
            }
            Vector2 toCamera = new Vector2(0f, -1f);
            MapLabCliffColumn chosen = default;
            float bestCliff = float.MaxValue;
            foreach (MapLabCliffLine line in d.CliffLines)
                foreach (MapLabCliffColumn c in line.Columns)
                {
                    Vector2 mid = (c.Start + c.End) * 0.5f;
                    float dist = Vector2.Distance(mid, focus);
                    if (Vector2.Dot(c.Outward, toCamera) > 0.7f && c.Top - c.Bottom > 3f && dist < bestCliff)
                    {
                        bestCliff = dist;
                        chosen = c;
                    }
                }
            if (bestCliff < float.MaxValue)
            {
                Vector2 mid = (chosen.Start + chosen.End) * 0.5f;
                spots.Add(("cliff_foot_front", mid + chosen.Outward * (d.CliffSharpness * 1.9f + 2.5f), 180f));
                spots.Add(("cliff_top_behind", mid - chosen.Outward * (d.CliffSharpness * 1.5f + 2.2f), 150f));
            }
            // Ровная дорога и пологий склон.
            Vector2 flat = Vector2.zero, slope = Vector2.zero;
            float flatBest = float.MaxValue, slopeBest = 0f;
            foreach (MapLabPolyline road in d.Roads)
                foreach (Vector2 p in MapLabGeometry.Resample(road.Points, 2f))
                {
                    if (!d.Contains(p.x, p.y) || d.IsUnderWater(p.x, p.y))
                        continue;
                    float s = d.SampleSlope(p.x, p.y);
                    float dist = Vector2.Distance(p, focus);
                    if (s < 2f && dist < flatBest)
                    {
                        flatBest = dist;
                        flat = p;
                    }
                    if (s > slopeBest && s < 12f && dist < 90f)
                    {
                        slopeBest = s;
                        slope = p;
                    }
                }
            if (flatBest < float.MaxValue)
                spots.Add(("road_flat", flat, 180f));
            if (slopeBest > 0f)
                spots.Add(($"road_slope_{slopeBest:0}deg", slope, 130f));
            return spots;
        }

        // Среднее время рендера кадра камерой MapLab в RenderTexture с ожиданием GPU
        // (ReadPixels одного пикселя). Это не FPS игрока, а сравнительная мера.
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

        private static int CountColumns(MapLabData d)
        {
            int count = 0;
            foreach (MapLabCliffLine line in d.CliffLines)
                count += line.Columns.Count;
            return count;
        }

        // Этап 0: непрозрачная сетка Solid, плоскость воды и две пересекающиеся
        // alpha-clip карточки под наклонной камерой на действующем Renderer2D.
        public static void CompatibilityCheck()
        {
            int code = 0;
            try
            {
                string output = Argument("-mapLabOut", Path.GetFullPath("MapLabExports"));
                Directory.CreateDirectory(output);
                StringBuilder report = new StringBuilder();
                report.AppendLine("Render pipeline: " + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "Built-in"));
                report.AppendLine("Graphics: " + SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Shader solid = MapLabAssets.LoadShader("MapLabSolid.shader");
                Shader card = MapLabAssets.LoadShader("MapLabCard.shader");
                Shader water = MapLabAssets.LoadShader("MapLabWater.shader");
                Shader terrain = MapLabAssets.LoadShader("MapLabTerrain.shader");
                foreach (Shader s in new[] { solid, card, water, terrain })
                    report.AppendLine($"{(s != null ? s.name : "<нет>")}: supported={(s != null && s.isSupported)}");
                MapLabPalette palette = MapLabPalette.CreateFallback();
                Texture2D paletteTex = MapLabArt.CreatePaletteTexture(palette);
                Texture2D atlas = MapLabArt.CreateVegetationAtlas(7);
                Texture2D detail = MapLabArt.CreateDetailTexture(1);

                Material solidMat = new Material(solid);
                solidMat.SetTexture("_PaletteTex", paletteTex);
                solidMat.SetTexture("_DetailTex", detail);
                MapLabMeshBuilder ground = new MapLabMeshBuilder();
                ground.Quad(new Vector3(-10, 0, -10), new Vector3(10, 0, -10), new Vector3(10, 0, 10), new Vector3(-10, 0, 10), MapLabPaletteIndex.GrassA, MapLabPattern.Plain, 0.5f);
                ground.Box(new Vector3(0, 1.5f, 2), new Vector3(4, 3, 4), 25f, MapLabPaletteIndex.RockLight, MapLabPattern.Rock, 1f);
                Spawn("Solid", ground.ToMesh("Compat Solid"), solidMat);

                Material waterMat = new Material(water);
                waterMat.SetFloat("_MapSize", 20f);
                Mesh plane = new Mesh();
                plane.vertices = new[] { new Vector3(-10, 0.3f, -10), new Vector3(0, 0.3f, -10), new Vector3(0, 0.3f, 0), new Vector3(-10, 0.3f, 0) };
                plane.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                plane.uv2 = new[] { Vector2.right, Vector2.right, Vector2.right, Vector2.right };
                plane.triangles = new[] { 0, 3, 2, 0, 2, 1 };
                Spawn("Water", plane, waterMat);

                Material cardMat = new Material(card);
                cardMat.SetTexture("_MainTex", atlas);
                cardMat.SetTexture("_PaletteTex", paletteTex);
                cardMat.SetTexture("_DetailTex", detail);
                // Две карточки в одной точке опоры и одна позади куба — проверка глубины.
                MapLabData fake = new MapLabData(9, 20f);
                fake.Objects.Add(new MapLabPlacement { Kind = MapLabObjectKind.Deciduous, Position = new Vector3(4, 0, -2), Scale = 4f, Tone = 0.2f, Brightness = 1f });
                fake.Objects.Add(new MapLabPlacement { Kind = MapLabObjectKind.Conifer, Position = new Vector3(5, 0, -2.2f), Scale = 4f, Tone = 0.8f, Brightness = 1f, Variant = 1 });
                fake.Objects.Add(new MapLabPlacement { Kind = MapLabObjectKind.Deciduous, Position = new Vector3(0.5f, 0, 6f), Scale = 5f, Tone = 0.5f, Brightness = 1f, Variant = 3 });
                foreach (Mesh m in MapLabMeshes.BuildVegetationCards(fake, 64f))
                    Spawn("Cards", m, cardMat);

                GameObject cameraObject = new GameObject("Camera");
                Camera cam = cameraObject.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 9f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = palette.Paper;
                cameraObject.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
                cameraObject.transform.position = cameraObject.transform.rotation * new Vector3(0, 0, -60f);
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 200f;
                RenderTexture rt = new RenderTexture(1024, 768, 24, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                Texture2D image = new Texture2D(1024, 768, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, 1024, 768), 0, 0);
                image.Apply();
                RenderTexture.active = null;
                Color32 center = image.GetPixel(512, 384);
                Color32 corner = image.GetPixel(4, 4);
                int magenta = 0;
                foreach (Color32 c in image.GetPixels32())
                    if (c.r > 230 && c.g < 40 && c.b > 230)
                        magenta++;
                report.AppendLine($"Центр кадра: {center}; угол: {corner}; розовых пикселей: {magenta}");
                File.WriteAllBytes(Path.Combine(output, "compat_check.png"), image.EncodeToPNG());
                File.WriteAllText(Path.Combine(output, "compat_check.txt"), report.ToString());
                Debug.Log("MapLab compatibility:\n" + report);
                cam.targetTexture = null;
                rt.Release();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                code = 1;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        private static void Spawn(string name, Mesh mesh, Material material)
        {
            GameObject go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
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
            return new Vector2Int(1536, 1024);
        }
    }
}
