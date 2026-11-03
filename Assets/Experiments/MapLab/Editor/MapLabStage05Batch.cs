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
using Debug = UnityEngine.Debug;

namespace KingdomSurvival.MapLab.Editor
{
    // Этап 05: сравнение Stage04 ↔ Stage05 на одном seed, фокусе, свете и
    // размере кадра при Zoom ≤ 3 (каждый этап — со своей политикой наклона);
    // прежний Zoom 8 — один кадр только как пояснение сменившегося предела.
    // Плюс замер видимости пера, слои, кадры героя P35, мосты, деревня.
    //   -executeMethod KingdomSurvival.MapLab.Editor.MapLabStage05Batch.Render
    //     -mapLabOut <папка> [-mapLabSize 2048x1365] [-mapLabOnly префиксы]
    public static class MapLabStage05Batch
    {
        private struct Spots
        {
            public Vector2 Village, Tree, Junction, Fence, Bridge, Cliff, River, Hero;
            public bool HasBridge, HasCliff, HasRiver;
        }

        public static void Render()
        {
            int code = 0;
            try
            {
                string output = MapLabStage03Batch.Argument("-mapLabOut", Path.GetFullPath("MapLabExports/Stage05"));
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
                view.ArtSet05 = MapLabArtImport.LoadStage05Set();
                view.MarkerSet05 = MapLabArtImport.LoadStage05Markers();
                view.InkShader = MapLabAssets.LoadShader("MapLabInk.shader");
                view.GrassShader = MapLabAssets.LoadShader("MapLabGrass.shader");
                view.ModuleShader = MapLabAssets.LoadShader("MapLabModule.shader");
                StringBuilder report = new StringBuilder();
                report.AppendLine("MapLab этап 05 · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                report.AppendLine(Dependencies(view));
                MapLabSettings settings = UnityEngine.Object.Instantiate(MapLabAssets.EnsurePreset(MapLabPresetKind.RiverValley));
                view.Settings = settings;
                MapLabCameraRig rig = view.CameraRig;
                MapLabMarker marker = view.Marker;
                bool Want(string name) => string.IsNullOrEmpty(only) || only.Split(',').Any(o => name.StartsWith(o.Trim(), StringComparison.Ordinal));
                int SS() => settings.IsStage05 ? settings.Stage05.ExportSupersample : 2;
                void Profile()
                {
                    MapLabExport.LinearDownsample = settings.IsStage05;
                    MapLabExport.Msaa = settings.IsStage05 ? settings.Stage05.ExportMsaa : 1;
                }
                float lastShotMs = 0f;
                void Shot(string name)
                {
                    if (!Want(name))
                        return;
                    Profile();
                    string file = Path.Combine(output, name + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    Stopwatch watch = Stopwatch.StartNew();
                    byte[] png = MapLabExport.RenderPng(rig, size.x, size.y, SS());
                    lastShotMs = (float)watch.Elapsed.TotalMilliseconds;
                    File.WriteAllBytes(file, png);
                    MapLabExportInfo.Write(view, file, size.x, size.y, SS());
                }
                // Наклон — по политике этапа (от приближения); фокус — точка на земле.
                void View(float zoom, Vector2 at, float pitch = -1f)
                {
                    if (pitch > 0f)
                    {
                        settings.Camera.PitchByZoom = false;
                        settings.Camera.Mode = MapLabCameraMode.Custom;
                        settings.Camera.CameraPitchDownDegrees = pitch;
                    }
                    else
                        settings.Camera.PitchByZoom = true;
                    rig.Zoom = zoom;
                    rig.Target = new Vector3(at.x, view.Data.SampleHeight(at.x, at.y), at.y);
                    rig.RecomputeFitSize();
                    rig.Apply();
                    rig.Target = rig.ToGround(rig.Target);
                    rig.Apply();
                    view.OnCameraChanged();
                }

                // ---- Сравнение Stage04 ↔ Stage05 ----
                settings.Stage03.Look = MapLabLook.Stage05;
                view.Generate(null);
                Spots spots = Find(view.Data, view.DemoFocus);
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "Места (по данным Stage05, seed {0}): деревня {1}; дерево {2}; узел {3}; ограда {4}; мост {5}; уступ {6}; изгиб реки {7}",
                    settings.General.Seed, spots.Village, spots.Tree, spots.Junction, spots.Fence, spots.HasBridge ? spots.Bridge.ToString() : "нет", spots.HasCliff ? spots.Cliff.ToString() : "нет", spots.HasRiver ? spots.River.ToString() : "нет"));
                Dictionary<string, float[]> inkDark = new Dictionary<string, float[]>();
                foreach (MapLabLook look in new[] { MapLabLook.Stage04, MapLabLook.Stage05 })
                {
                    settings.Stage03.Look = look;
                    Stopwatch watch = Stopwatch.StartNew();
                    view.Generate(null);
                    float ms = (float)watch.Elapsed.TotalMilliseconds;
                    string tag = look == MapLabLook.Stage04 ? "stage04" : "stage05";
                    report.AppendLine($"{look}: генерация {ms:0} мс; {MapLabStage03Batch.Paint(view)}; {MapLabStage03Batch.Counts(view)}; фактический предел Zoom {rig.MaxZoom:0.##} (предел рига {rig.ZoomLimits.y:0.#}), наклон на пределе {settings.PitchFor(rig.MaxZoom, rig.ZoomLimits.y):0.#}°");
                    if (marker != null)
                    {
                        marker.AutoPlace = false;
                        marker.PreviewMove = false;
                        marker.Position = spots.Hero;
                        marker.FacingDegrees = 150f;
                        marker.Refresh();
                    }
                    float max = Mathf.Min(3f, rig.MaxZoom);
                    View(2f, spots.Village);
                    Shot($"compare/{tag}/village_fields_bridge_z2");
                    View(max, spots.Tree);
                    Shot($"compare/{tag}/tree_meadow_shadow_z3");
                    View(max, spots.Junction);
                    Shot($"compare/{tag}/junction_z3");
                    if (spots.HasCliff)
                    {
                        View(max, spots.Cliff);
                        Shot($"compare/{tag}/cliff_base_z3");
                        View(2f, spots.Cliff);
                        Shot($"compare/{tag}/cliff_medium_z2");
                        View(1.3f, spots.Cliff);
                        Shot($"compare/{tag}/cliff_overview_z1.3");
                    }
                    if (spots.HasRiver)
                    {
                        View(max, spots.River);
                        Shot($"compare/{tag}/river_bend_shore_z3");
                    }
                    View(max, spots.Fence);
                    Shot($"compare/{tag}/fence_joints_z3");
                    if (spots.HasBridge)
                    {
                        View(max, spots.Bridge);
                        Shot($"compare/{tag}/bridge_joints_z3");
                    }
                    View(1f, spots.Village);
                    rig.Fit();
                    Shot($"compare/{tag}/overview_fit");
                    // Видимость пера дорог: потемнение кадра от ленты пера (с пером и без).
                    float[] dark = new float[3];
                    float[] zooms = { 1f, 2f, max };
                    for (int k = 0; k < zooms.Length; k++)
                    {
                        View(zooms[k], spots.Junction);
                        dark[k] = InkDarkening(view, rig, 1024, 683);
                    }
                    inkDark[tag] = dark;
                    if (look == MapLabLook.Stage04)
                    {
                        // Только пояснение: прежний предел Zoom 8 этапа 04.
                        View(8f, spots.Junction);
                        report.AppendLine($"Stage04: прежний предел — Zoom {rig.Zoom:0.#}, наклон {rig.Pitch:0.#}° (кадр old_zoom8_explanation — только пояснение смены предела)");
                        Shot("compare/stage04/old_zoom8_explanation");
                    }
                }
                float[] a4 = inkDark["stage04"], a5 = inkDark["stage05"];
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Видимость пера дорог (сумма потемнения яркости от ленты пера, тот же кадр 1024×683, узел тропы): Zoom 1 — {0:0} → {1:0} (×{2:0.0}); Zoom 2 — {3:0} → {4:0} (×{5:0.0}); Zoom 3 — {6:0} → {7:0} (×{8:0.0})",
                    a4[0], a5[0], a5[0] / Mathf.Max(1f, a4[0]), a4[1], a5[1], a5[1] / Mathf.Max(1f, a4[1]), a4[2], a5[2], a5[2] / Mathf.Max(1f, a4[2])));
                MapLabInkStyle i4 = settings.Stage03.InkStyle;
                MapLabStage05Settings s5 = settings.Stage05;
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Предел пера: Stage04 толщина {0} ед., {1}–{2} пикс., непрозрачность {3}; Stage05 {4} ед., {5}–{6} пикс., плотность {7} (верхний предел поднят вместе с толщиной — рост не съедается clamp)",
                    i4.Width, i4.MinPixels, i4.MaxPixels, i4.Opacity, s5.InkWidth, s5.InkMinPixels, s5.InkMaxPixels, s5.InkDensity));

                // ---- Stage05: слои, деревня, мосты, герой, сглаживание ----
                settings.Stage03.Look = MapLabLook.Stage05;
                view.Generate(null);
                report.AppendLine(Village(view.Data));
                MapLabModules05.Stats ms5 = view.ModuleStats;
                if (ms5 != null)
                    report.AppendLine($"Модули: столбов {ms5.Posts}, заполнений {ms5.Infills}, ворот {ms5.Gates}; мостов {ms5.Bridges}, панелей настила {ms5.DeckPanels}, столбов перил {ms5.BridgePosts}, козел {ms5.Trestles}, съездов {ms5.Ramps}, устоев {ms5.Abutments}; нет модулей: {(ms5.MissingIds.Count == 0 ? "—" : string.Join(", ", ms5.MissingIds))}");
                MapLabArtBuilder.Decor05Stats d5 = MapLabArtBuilder.LastDecor05;
                report.AppendLine($"Спрайты: трава {d5.Grass}, тростник {d5.Reeds}, камни {d5.Rocks}, осыпь (Outcrop_D) {d5.Outcrops}, сено {d5.Hay}, реквизит {d5.Props}, домов {d5.Houses}, сараев {d5.Barns}; нет ролей: {(d5.MissingRoles.Count == 0 ? "—" : string.Join(", ", d5.MissingRoles))}");
                report.AppendLine($"Пятна Wash: {view.WashPatches}; перо берега: {view.ShoreInkStats.Polylines} линий, {view.ShoreInkStats.Length:0} ед.; уступ: плоскостей {MapLabCliffs05.LastStats.Planes} (средняя ширина {MapLabCliffs05.LastStats.MeanPlaneWidth:0.0} ед.), отступов {MapLabCliffs05.LastStats.SetBack}, полок {MapLabCliffs05.LastStats.Ledges}, осыпей {MapLabCliffs05.LastStats.Screes}");
                for (int b = 0; b < view.Data.Bridges.Count; b++)
                {
                    MapLabBridge br = view.Data.Bridges[b];
                    MapLabModules05.BridgeLayout l = MapLabModules05.Layout(view.Data, settings, br);
                    Vector2 axis = (br.EndB - br.EndA).normalized;
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture, "Мост {0}: длина настила {1:0.0} ед., направление {2:0}°, сухие опоры {3}/{4}, панелей {5}, козел {6}",
                        b, Vector2.Distance(l.LandA, l.LandB), Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg, l.DryA, l.DryB, l.Panels, l.Trestles));
                    View(Mathf.Min(3f, rig.MaxZoom), (l.LandA + l.LandB) * 0.5f);
                    Shot($"stage05/bridges/bridge_{b}_z3");
                }
                // Слои: без пятен, без спрайтов, без пера берега.
                if (spots.HasRiver)
                {
                    View(3f, spots.River);
                    float wash = s5.WashStrength;
                    s5.WashStrength = 0f;
                    view.RebuildStage03Layers();
                    Shot("stage05/layers/river_no_wash");
                    s5.WashStrength = wash;
                    view.RebuildStage03Layers();
                    view.SetGroupVisible("ArtDecor05", false);
                    Shot("stage05/layers/river_no_sprites");
                    view.ApplyVisibility();
                }
                View(2f, spots.Tree);
                Shot("stage05/layers/washes_z2");
                settings.Diagnostics.Mode = MapLabDiagnosticMode.ShadowMask;
                view.ApplyMaterialSettings();
                view.ApplyVisibility();
                View(3f, spots.Tree);
                Shot("stage05/layers/shadow_live_edge_mask");
                settings.Diagnostics.Mode = MapLabDiagnosticMode.Final;
                view.ApplyMaterialSettings();
                view.ApplyVisibility();
                // Наклон и набор героя на всём диапазоне приближения (без переключения кадров).
                StringBuilder walk = new StringBuilder("Stage05, проход приближения (Zoom → наклон, набор героя, кадр дома):");
                foreach (float z in new[] { 0.6f, 1f, 1.5f, 2f, 2.5f, 3f, 8f })
                {
                    View(z, spots.Hero);
                    marker?.Refresh();
                    walk.Append($" {z:0.#}→{rig.Zoom:0.##}/{rig.Pitch:0.#}°/{(marker != null ? marker.PitchSetPitch : 0f):0}°");
                }
                report.AppendLine(walk.ToString());
                if (marker != null && view.ActiveMarkerSet != null)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        View(3f, spots.Hero);
                        marker.FacingDegrees = rig.Azimuth + 180f - i * 60f;
                        marker.Refresh();
                        Shot($"stage05/hero/z3_{i}_{MapLabMarkerSet.DirectionNames[i]}");
                        report.AppendLine($"Герой {rig.Pitch:0}° {MapLabMarkerSet.DirectionNames[i]}: кадр {(marker.CurrentEntry != null ? marker.CurrentEntry.Id : "нет")}, набор {marker.PitchSetPitch:0}°, приближённо {marker.Approximate}");
                    }
                }
                // Лесное озеро: общий вид пресета B (тот же seed для обоих этапов).
                if (Want("compare") || Want("forest"))
                {
                    MapLabSettings lake = UnityEngine.Object.Instantiate(MapLabAssets.EnsurePreset(MapLabPresetKind.ForestLake));
                    view.Settings = lake;
                    foreach (MapLabLook look in new[] { MapLabLook.Stage04, MapLabLook.Stage05 })
                    {
                        lake.Stage03.Look = look;
                        lake.Camera.PitchByZoom = true;
                        view.Generate(null);
                        rig.Zoom = 1f;
                        rig.Fit();
                        view.OnCameraChanged();
                        MapLabExport.LinearDownsample = lake.IsStage05;
                        MapLabExport.Msaa = lake.IsStage05 ? lake.Stage05.ExportMsaa : 1;
                        string file = Path.Combine(output, $"compare/{(look == MapLabLook.Stage04 ? "stage04" : "stage05")}/forest_lake_overview.png");
                        Directory.CreateDirectory(Path.GetDirectoryName(file));
                        int ss = lake.IsStage05 ? lake.Stage05.ExportSupersample : 2;
                        File.WriteAllBytes(file, MapLabExport.RenderPng(rig, size.x, size.y, ss));
                        MapLabExportInfo.Write(view, file, size.x, size.y, ss);
                    }
                    view.Settings = settings;
                    UnityEngine.Object.DestroyImmediate(lake);
                    settings.Stage03.Look = MapLabLook.Stage05;
                    view.Generate(null);
                }
                // Сглаживание: стоимость экспорта по профилям (тот же кадр).
                View(3f, spots.Village);
                foreach ((int ss, bool linear, int msaa) in new[] { (1, false, 1), (2, false, 1), (2, true, 1), (2, true, 4) })
                {
                    MapLabExport.LinearDownsample = linear;
                    MapLabExport.Msaa = msaa;
                    MapLabExport.RenderPng(rig, size.x, size.y, ss);
                    Stopwatch watch = Stopwatch.StartNew();
                    MapLabExport.RenderPng(rig, size.x, size.y, ss);
                    report.AppendLine($"Экспорт {size.x}×{size.y}, {MapLabExport.LastProfile}: {watch.Elapsed.TotalMilliseconds:0} мс (с чтением и кодированием PNG)");
                }
                foreach (float z in new[] { 1f, 2f, 3f })
                {
                    View(z, spots.Village);
                    report.AppendLine($"Кадр Stage05 {rig.Pitch:0}° Zoom {rig.Zoom:0.#}: {MapLabStage03Batch.MeasureFrame(rig, size.x, size.y):0.0} мс (среднее 20 рендеров {size.x}×{size.y} в RenderTexture без MSAA, не FPS игры)");
                }
                report.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType + " · CPU: " + SystemInfo.processorType + " · потоков " + SystemInfo.processorCount);
                File.WriteAllText(Path.Combine(output, "report_stage05.txt"), report.ToString());
                Debug.Log("MapLab stage 05:\n" + report);
                view.Clear();
                UnityEngine.Object.DestroyImmediate(settings);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                code = 1;
            }
            MapLabExport.LinearDownsample = false;
            MapLabExport.Msaa = 1;
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        // Сумма потемнения яркости (0..255) от ленты пера дорог в одном кадре.
        private static float InkDarkening(MapLabView view, MapLabCameraRig rig, int w, int h)
        {
            MapLabExport.LinearDownsample = true;
            MapLabExport.Msaa = 1;
            Texture2D on = MapLabExport.Render(rig, w, h, 2);
            view.SetGroupVisible("Ink", false);
            Texture2D off = MapLabExport.Render(rig, w, h, 2);
            view.ApplyVisibility();
            Color32[] a = on.GetPixels32(), b = off.GetPixels32();
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                float la = 0.299f * a[i].r + 0.587f * a[i].g + 0.114f * a[i].b;
                float lb = 0.299f * b[i].r + 0.587f * b[i].g + 0.114f * b[i].b;
                if (lb > la)
                    sum += lb - la;
            }
            UnityEngine.Object.DestroyImmediate(on);
            UnityEngine.Object.DestroyImmediate(off);
            return (float)(sum / 255.0);
        }

        // Зависимости набора: деревья — этап 03, герой — файлы этапа 04 (по SHA).
        private static string Dependencies(MapLabView view)
        {
            StringBuilder sb = new StringBuilder();
            MapLabArtSet s05 = view.ArtSet05, s03 = view.ArtSet03;
            sb.Append("Набор этапа 05: ").Append(s05 != null ? $"{s05.PackageId} · подключение №{s05.ImportVersion} · записей {s05.Entries.Count} · без картинки {s05.MissingTextures}" : "НЕ ПОДКЛЮЧЁН");
            if (s05 != null)
            {
                int reused = s05.Entries.Count(e => e != null && !string.IsNullOrEmpty(e.ReusedFrom));
                sb.Append($" · из набора этапа 04 по SHA: {reused}");
                Dictionary<MapLabArtCategory, int> byCategory = new Dictionary<MapLabArtCategory, int>();
                foreach (MapLabArtEntry e in s05.Entries)
                    if (e != null)
                        byCategory[e.Category] = byCategory.TryGetValue(e.Category, out int n) ? n + 1 : 1;
                sb.Append(" · ").Append(string.Join(", ", byCategory.Select(p => $"{p.Key} {p.Value}")));
                sb.AppendLine();
                foreach (MapLabArtEntry e in s05.Entries.Where(e => e != null).OrderBy(e => e.Id, StringComparer.Ordinal))
                    sb.AppendLine($"  {e.Id} [{e.Category}/{e.Role}{(string.IsNullOrEmpty(e.ViewTag) ? "" : "/" + e.ViewTag)}] {e.Width}×{e.Height} sha256={e.Sha256.Substring(0, 12)}… {(string.IsNullOrEmpty(e.ReusedFrom) ? AssetDatabase.GetAssetPath(e.Texture) : "→ " + AssetDatabase.GetAssetPath(e.Texture))}");
            }
            sb.Append("Зависимость: деревья и кусты этапа 05 — набор этапа 03 (").Append(s03 != null ? $"{s03.PackageId}, записей {s03.Entries.Count}" : "НЕ ПОДКЛЮЧЁН — деревья процедурные").Append(").");
            return sb.ToString();
        }

        private static string Village(MapLabData d)
        {
            StringBuilder sb = new StringBuilder($"Деревня: дворов {d.YardRects.Count}, реквизита {d.Props.Count}");
            if (d.FenceGraph != null)
                sb.Append($", ограда: узлов {d.FenceGraph.Nodes.Count}, рёбер {d.FenceGraph.Edges.Count}, ворот {d.FenceGraph.Edges.Count(e => e.Gate)}, снято общих границ {d.FenceGraph.SharedRemoved}, слито коротких {d.FenceGraph.Merged}");
            foreach (MapLabPlacement o in d.Objects)
            {
                if ((o.Kind != MapLabObjectKind.House && o.Kind != MapLabObjectKind.Barn) || o.Yard <= 0)
                    continue;
                sb.Append($"\n  двор {o.Yard}: {o.Kind} {MapLabHouseViews.Tags[Mathf.Clamp(o.View, 0, 3)]} (вариант {o.Variant}), вход ({o.Entrance.x:0.00}; {o.Entrance.y:0.00}), основание {o.Size.x:0.0}×{o.Size.y:0.0}");
            }
            Dictionary<string, int> roles = new Dictionary<string, int>();
            foreach (MapLabProp p in d.Props)
                roles[p.Role] = roles.TryGetValue(p.Role, out int n) ? n + 1 : 1;
            sb.Append("\n  реквизит: ").Append(string.Join(", ", roles.Select(p => $"{p.Key} {p.Value}")));
            return sb.ToString();
        }

        private static Spots Find(MapLabData d, Vector2 focus)
        {
            Spots s = default;
            s.Village = d.Plaza.z > 0f ? new Vector2(d.Plaza.x, d.Plaza.y) : focus;
            s.Junction = MapLabStage03Batch.FindPlot(d, focus);
            float best = float.MaxValue;
            s.Tree = s.Village;
            foreach (MapLabPlacement o in d.Objects)
            {
                if (o.Kind != MapLabObjectKind.Deciduous)
                    continue;
                Vector2 p = new Vector2(o.Position.x, o.Position.z);
                float dist = Vector2.Distance(p, s.Village);
                if (dist < best && dist > 10f && d.SampleMask(d.Forest, p.x, p.y) < 0.15f && d.SampleSlope(p.x, p.y) < 8f && !d.IsUnderWater(p.x, p.y, 1f))
                {
                    best = dist;
                    s.Tree = p;
                }
            }
            s.Fence = s.Village;
            if (d.FenceGraph != null && d.FenceGraph.Nodes.Count > 0)
            {
                // Узел с воротами ближе всего к площадке.
                best = float.MaxValue;
                foreach (MapLabFenceEdge e in d.FenceGraph.Edges)
                {
                    if (!e.Gate)
                        continue;
                    Vector2 p = (d.FenceGraph.Nodes[e.A] + d.FenceGraph.Nodes[e.B]) * 0.5f;
                    float dist = Vector2.Distance(p, s.Village);
                    if (dist < best)
                    {
                        best = dist;
                        s.Fence = p;
                    }
                }
            }
            if (d.Bridges.Count > 0)
            {
                s.HasBridge = true;
                best = float.MaxValue;
                foreach (MapLabBridge b in d.Bridges)
                {
                    Vector2 mid = (b.EndA + b.EndB) * 0.5f;
                    float dist = Vector2.Distance(mid, s.Village);
                    if (dist < best)
                    {
                        best = dist;
                        s.Bridge = mid;
                    }
                }
            }
            best = float.MaxValue;
            foreach (MapLabCliffLine line in d.CliffLines)
                foreach (MapLabCliffColumn c in line.Columns)
                {
                    Vector2 mid = (c.Start + c.End) * 0.5f;
                    float dist = Vector2.Distance(mid, s.Village);
                    if (Vector2.Dot(c.Outward, Vector2.down) > 0.6f && c.Top - c.Bottom > 3f && dist < best)
                    {
                        best = dist;
                        s.Cliff = mid + c.Outward * 2f;
                        s.HasCliff = true;
                    }
                }
            // Изгиб реки: наибольший поворот касательной на отрезке ~10 ед.
            best = 0f;
            foreach (MapLabRiver river in d.Rivers)
            {
                List<Vector2> p = MapLabGeometry.Resample(river.Points, 2f);
                for (int i = 5; i < p.Count - 5; i++)
                {
                    float turn = Vector2.Angle(p[i] - p[i - 5], p[i + 5] - p[i]);
                    if (turn > best && d.Contains(p[i].x, p[i].y) && p[i].x > 20f && p[i].y > 20f && p[i].x < d.Size - 20f && p[i].y < d.Size - 20f)
                    {
                        best = turn;
                        s.River = p[i];
                        s.HasRiver = true;
                    }
                }
            }
            s.Hero = s.Village;
            best = float.MaxValue;
            foreach (MapLabPolyline road in d.Roads)
                foreach (Vector2 p in MapLabGeometry.Resample(road.Points, 1.5f))
                {
                    float dist = Mathf.Abs(Vector2.Distance(p, s.Village) - 9f);
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
