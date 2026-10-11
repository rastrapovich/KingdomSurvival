using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace KingdomSurvival.LocationRendering.Tests
{
    // Замер кадра настоящего места с настоящими ассетами (тестовый лагерь):
    // фигуры, время суток с анимациями и реакцией травы, отрисовка камеры.
    // Явный — зависит от рабочей базы; запускается фильтром по имени.
    [Explicit, Category("Performance")]
    public sealed class LocationPerformancePlayModeTests
    {
        [UnityTest]
        public IEnumerator TechnicalCamp_FrameCost()
        {
            LocalLocationDatabaseAsset database = Resources.Load<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.ResourcesPath);
            BattlefieldDatabaseAsset fields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
            LocalLocationDefinition location = database.locations.Find(item => item.Id == "technical_lighting_camp");
            LocationVisualDefinition visual = database.FindVisual(location.Id);
            BattlefieldDefinitionData field = fields?.FindById(location.BattlefieldId);
            RenderTexture target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            target.Create();
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, field) { VegetationReacts = true })
            {
                renderer.Camera.targetTexture = target;
                renderer.Camera.aspect = 16 / 9f;
                Vector2 start = LocationVisualGeometry.ToPixel(location, visual.TestStartPoint);
                Stopwatch watch = new Stopwatch();
                double actorsMs = 0, timeMs = 0, renderMs = 0, frameMs = 0, worstFrame = 0;
                const int warm = 30, frames = 240;
                ProfilerRecorder garbage = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                long garbageBytes = 0, gActors = 0, gTime = 0, gRender = 0;
                LocationWorldRenderer.ActorFrame[] actors = new LocationWorldRenderer.ActorFrame[3];
                for (int i = 0; i < warm + frames; i++)
                {
                    float t = i / 60f;
                    for (int a = 0; a < actors.Length; a++)
                        actors[a] = new LocationWorldRenderer.ActorFrame
                        {
                            Id = "walker" + a, UnitTypeId = visual.TestUnitId, Walking = true, Direction = Vector2.right,
                            Pixel = start + new Vector2(t * 120 - a * 60, a * 30)
                        };
                    watch.Restart();
                    long g0 = System.GC.GetTotalMemory(false);
                    renderer.SetActors(actors, t);
                    long g1 = System.GC.GetTotalMemory(false);
                    double setActors = watch.Elapsed.TotalMilliseconds;
                    watch.Restart();
                    renderer.SetTime(21, t);
                    long g2 = System.GC.GetTotalMemory(false);
                    double setTime = watch.Elapsed.TotalMilliseconds;
                    renderer.Follow(start + new Vector2(t * 120, 0), 1080, 1 / 60f, i == 0);
                    watch.Restart();
                    renderer.Camera.Render();
                    long g3 = System.GC.GetTotalMemory(false);
                    double render = watch.Elapsed.TotalMilliseconds;
                    yield return null;
                    if (i < warm) continue;
                    actorsMs += setActors;
                    timeMs += setTime;
                    renderMs += render;
                    frameMs += Time.unscaledDeltaTime * 1000;
                    garbageBytes += garbage.LastValue;
                    gActors += System.Math.Max(0, g1 - g0); gTime += System.Math.Max(0, g2 - g1); gRender += System.Math.Max(0, g3 - g2);
                    worstFrame = System.Math.Max(worstFrame, setActors + setTime + render);
                }
                UnityEngine.Debug.Log("Кадр места «" + location.DisplayName + "»: фигуры " + (actorsMs / frames).ToString("0.00") + " мс, время/анимации/свет " +
                                      (timeMs / frames).ToString("0.00") + " мс, отрисовка " + (renderMs / frames).ToString("0.00") + " мс, худший CPU " +
                                      worstFrame.ToString("0.00") + " мс, кадр игрока " + (frameMs / frames).ToString("0.00") + " мс; предметов " +
                                      renderer.Root.GetComponentsInChildren<SpriteRenderer>(true).Length + " рисунков, теней " + renderer.VisibleShadowCount +
                                      ", пучков с реакцией " + renderer.BendingPlantCount + ", мусор " + (garbageBytes / frames / 1024f).ToString("0.0") + " КБ/кадр");
                garbage.Dispose();
                UnityEngine.Debug.Log("Мусор по частям, байт/кадр: фигуры " + gActors / frames + ", время " + gTime / frames + ", отрисовка " + gRender / frames);
                UnityEngine.Debug.Log("Тени: " + (renderer.ShadowMilliseconds / (warm + frames)).ToString("0.00") + " мс/кадр в среднем с прогревом");
                renderer.Camera.targetTexture = null;
            }
            target.Release();
            Object.Destroy(target);
        }
    }
}
