using System.Collections;
using System.Collections.Generic;
using System.IO;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KingdomSurvival.LocationRendering.Tests
{
    // ПР-12М: тени и нормали на настоящем рендере. Солнце утром кладёт тень
    // предмета влево; ночью солнечной тени нет, тень даёт огонь; под крышей
    // солнца нет; карта нормалей освещает сторону, обращённую к огню, и у
    // отражённого рисунка тоже.
    public sealed class LocationShadowPlayModeTests
    {
        private const int Width = 960, Height = 540;

        private static LocalLocationDefinition Location() => new LocalLocationDefinition { Id = "zz_shadow_test", DisplayName = "Тень" };

        private static LocationVisualObject Crate(float height = 2) => new LocationVisualObject
        {
            Id = "crate", Name = "Ящик", Placeholder = LocationPlaceholder.Crate, Position = new Vector2(.5f, .5f),
            Height = height, Pivot = new Vector2(.5f, .1f), Band = LocationVisualBand.World, ProjectsShadow = true
        };

        private static RenderTexture Target(LocationWorldRenderer renderer)
        {
            RenderTexture target = new RenderTexture(Width, Height, 24);
            target.Create();
            renderer.Camera.targetTexture = target;
            renderer.Camera.aspect = Width / (float)Height;
            renderer.ShowWhole();
            return target;
        }

        // Яркость рисунка места в точке (пиксели рисунка 1920×1080, Y вниз).
        private static float Brightness(RenderTexture target, Vector2 canvasPixel)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            int x = Mathf.RoundToInt(canvasPixel.x / 1920f * Width), y = Mathf.RoundToInt((1 - canvasPixel.y / 1080f) * Height);
            float sum = 0;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    sum += texture.GetPixel(x + dx, y + dy).grayscale;
            Object.Destroy(texture);
            return sum / 25;
        }

        private static void Save(RenderTexture target, string name)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); texture.Apply();
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/LocationLighting"));
            Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
            Object.Destroy(texture); RenderTexture.active = previous;
        }

        [UnityTest]
        public IEnumerator MorningSunLaysTheSilhouetteShadowAway()
        {
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "zz_shadow_test", UseWorldLighting = false };
            visual.Sun = new LocationSunDefinition { MorningAngle = 170, EveningAngle = 10, Opacity = .8f, LowLength = 1.8f, Softness = .1f };
            visual.Objects.Add(Crate());
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), visual, null))
            {
                RenderTexture target = Target(renderer);
                renderer.SetTime(8, 0);
                Assert.That(renderer.VisibleShadowCount, Is.EqualTo(1));
                Assert.That(renderer.SunShadowVector.x, Is.LessThan(0), "Утром тень влево.");
                yield return null; yield return null;
                Save(target, "shadow-morning");
                // Тень ящика на высоте около метра рисунка уходит влево-вверх.
                float left = Brightness(target, new Vector2(800, 470));
                float right = Brightness(target, new Vector2(1120, 470));
                Assert.That(left, Is.LessThan(right - .04f), "Слева от ящика — тень, справа — земля.");

                renderer.SetTime(1, 0);
                Assert.That(renderer.VisibleShadowCount, Is.EqualTo(0), "Ночью солнечной тени нет.");

                // Общий свет мира без солнца — у места с общим небом тени нет.
                visual.UseWorldLighting = true;
                using (LocationWorldRenderer world = new LocationWorldRenderer(Location(), visual, null, null,
                           new LocationWorldLighting { Sun = new LocationSunDefinition { Enabled = false } }))
                {
                    world.SetTime(8, 0);
                    Assert.That(world.VisibleShadowCount, Is.EqualTo(0), "Небо — общее: солнце выключено во всём мире.");
                }
                visual.UseWorldLighting = false;
                renderer.Camera.targetTexture = null;
                Object.Destroy(target);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator FireCastsShadowsAtNight_AndIndoorHasNoSun()
        {
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "zz_shadow_test", UseWorldLighting = false };
            visual.Objects.Add(Crate());
            LocationVisualObject fire = new LocationVisualObject { Id = "fire", Name = "Огонь", LightOnly = true, Position = new Vector2(.3f, .5f) };
            fire.Light.Enabled = true; fire.Light.Radius = 8; fire.Light.Intensity = 2; fire.Light.Height = 1;
            fire.Light.Animation = LocationLightAnimation.None;
            visual.Objects.Add(fire);
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), visual, null))
            {
                RenderTexture target = Target(renderer);
                renderer.SetTime(1, 0);
                Assert.That(renderer.VisibleShadowCount, Is.EqualTo(1), "Ночью тень даёт огонь.");
                yield return null; yield return null;
                Save(target, "shadow-fire-night");
                float behind = Brightness(target, new Vector2(1200, 505));
                float front = Brightness(target, new Vector2(720, 505));
                Assert.That(behind, Is.LessThan(front), "Тень — по другую сторону от огня.");
                renderer.Camera.targetTexture = null;
                Object.Destroy(target);
            }

            visual.Lighting = LocationLightingMode.Indoor;
            visual.IndoorIntensity = .4f;
            fire.Light.Enabled = false;
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), visual, null))
            {
                renderer.SetTime(13, 0);
                Assert.That(renderer.Indoor, Is.True);
                Assert.That(renderer.VisibleShadowCount, Is.EqualTo(0), "Под крышей солнца нет.");
                Assert.That(renderer.GlobalLight.intensity, Is.EqualTo(.4f).Within(.001f));
            }
            yield return null;
        }

        // Карта нормалей «полусфера»: огонь слева освещает левую сторону
        // заметно сильнее правой — и у отражённого рисунка тоже.
        [UnityTest]
        public IEnumerator NormalMapsLightTheSideFacingTheFire()
        {
            Texture2D color = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            Texture2D normal = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            Color32[] white = new Color32[64 * 64];
            Color32[] sphere = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    white[y * 64 + x] = new Color32(200, 200, 200, 255);
                    float nx = (x + .5f) / 32 - 1, ny = (y + .5f) / 32 - 1;
                    float nz = Mathf.Sqrt(Mathf.Max(.05f, 1 - nx * nx - ny * ny));
                    Vector3 n = new Vector3(nx, ny, nz).normalized;
                    sphere[y * 64 + x] = new Color32((byte)((n.x * .5f + .5f) * 255), (byte)((n.y * .5f + .5f) * 255), (byte)((n.z * .5f + .5f) * 255), 255);
                }
            }
            color.SetPixels32(white); color.Apply();
            normal.SetPixels32(sphere); normal.Apply();
            Sprite sprite = Sprite.Create(color, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, Vector4.zero, false,
                new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = normal } });

            float[] results = new float[3];
            for (int run = 0; run < 3; run++)
            {
                bool normals = run != 0;
                bool flip = run == 2;
                LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = "zz_normals", UseWorldLighting = false, Lighting = LocationLightingMode.Indoor, IndoorIntensity = .05f };
                visual.Objects.Add(new LocationVisualObject
                {
                    Id = "ball", Name = "Шар", Sprite = sprite, Position = new Vector2(.5f, .5f), Height = 4, Pivot = new Vector2(.5f, .5f),
                    FlipX = flip, ProjectsShadow = false
                });
                LocationVisualObject fire = new LocationVisualObject { Id = "fire", Name = "Огонь", LightOnly = true, Position = new Vector2(.3f, .5f) };
                fire.Light.Enabled = true; fire.Light.Radius = 12; fire.Light.Intensity = 1.5f; fire.Light.Softness = 0;
                fire.Light.Animation = LocationLightAnimation.None; fire.Light.Offset = Vector2.zero; fire.Light.Falloff = 0;
                fire.Light.NormalMaps = normals; fire.Light.NormalMapsAccurate = true; fire.Light.NormalMapDistance = 1;
                fire.Light.ProjectsShadows = false; fire.Light.Shadows = false;
                visual.Objects.Add(fire);
                using (LocationWorldRenderer renderer = new LocationWorldRenderer(Location(), visual, null))
                {
                    RenderTexture target = Target(renderer);
                    renderer.SetTime(13, 0);
                    yield return null; yield return null;
                    Save(target, "normals-" + run);
                    float left = Brightness(target, new Vector2(960 - 120, 540));
                    float right = Brightness(target, new Vector2(960 + 120, 540));
                    results[run] = left - right;
                    renderer.Camera.targetTexture = null;
                    Object.Destroy(target);
                }
            }
            Assert.That(results[1], Is.GreaterThan(results[0] + .05f), "С нормалями сторона к огню заметно светлее: " + string.Join(", ", results));
            Assert.That(results[2], Is.GreaterThan(results[0] + .05f), "У отражённого рисунка свет тоже со стороны огня: " + string.Join(", ", results));
            Object.Destroy(sprite); Object.Destroy(color); Object.Destroy(normal);
        }
    }
}
