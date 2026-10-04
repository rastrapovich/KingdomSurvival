using System.Collections;
using System.Collections.Generic;
using System.IO;
using KingdomSurvival.BattlefieldDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Tests
{
    public sealed class LocationLightingPlayModeTests
    {
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator UniversalSceneStartsWithPartyAndWorkingTimeControl()
        {
            const string path = "Assets/_Project/LocationRendering/LocationLightingTest.unity";
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path,
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            Scene scene = SceneManager.GetSceneByPath(path);
            LocationLightingTest test = scene.GetRootGameObjects()[0].GetComponent<LocationLightingTest>();
            Assert.That(test.Renderer, Is.Not.Null);
            LocationVisualDefinition visual = Resources.Load<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.ResourcesPath).FindVisual(test.LocationId);
            Assert.That(test.Mover.Members.Count, Is.EqualTo(Mathf.Clamp(visual.TestFollowers + 1, 1, 5)));
            UIDocument hud = test.GetComponent<UIDocument>();
            Assert.That(hud.rootVisualElement.Q("location-lighting-playfield"), Is.Not.Null);
            Slider slider = hud.rootVisualElement.Q<Slider>();
            slider.value = 1;
            yield return null;
            Assert.That(test.Hour, Is.EqualTo(1));
            Assert.That(test.Renderer.Hour, Is.EqualTo(1));
            yield return SceneManager.UnloadSceneAsync(scene);
        }
#endif

        [UnityTest]
        public IEnumerator CampRendersDayAndNightAndPartyUsesSameGeometry()
        {
            LocalLocationDatabaseAsset database = Resources.Load<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.ResourcesPath);
            BattlefieldDatabaseAsset fields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
            LocalLocationDefinition location = database.locations.Find(item => item.Id == "technical_lighting_camp");
            LocationVisualDefinition visual = database.FindVisual(location.Id);
            Assert.That(LocationVisualGeometry.Validate(location, visual, fields.FindById(location.BattlefieldId)), Is.Empty);
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, fields.FindById(location.BattlefieldId)))
            {
                RenderTexture target = new RenderTexture(1280, 720, 24);
                target.Create(); renderer.Camera.targetTexture = target;
                renderer.SetTime(13, 0);
                float day = renderer.GlobalLight.intensity;
                Vector2 start = LocationVisualGeometry.ToPixel(location, visual.TestStartPoint);
                float spacing = renderer.HexSizePixels * 1.2f;
                List<LocalPointData> points = renderer.Geometry.SpreadAround(new LocalPointData(start.x, start.y), 3, spacing);
                List<KeyValuePair<string, LocalPointData>> members = new List<KeyValuePair<string, LocalPointData>>();
                for (int i = 0; i < 3; i++) members.Add(new KeyValuePair<string, LocalPointData>("test_" + i, points[i]));
                LocalFreeMover mover = new LocalFreeMover(renderer.Geometry.Layer, renderer.Geometry.Rules, members, spacing);
                Render(renderer, mover, visual, 0);
                Assert.That(renderer.HasActor("test_2"), Is.True);
                yield return null; yield return null;
                Color dayPixel = SaveFrame(target, "camp-day");
                renderer.SetTime(1, 0);
                Assert.That(renderer.GlobalLight.intensity, Is.LessThan(day));
                yield return null; yield return null;
                Color nightPixel = SaveFrame(target, "camp-night");
                Assert.That(dayPixel.grayscale, Is.GreaterThan(nightPixel.grayscale + .03f), "Фон должен действительно темнеть в отрендеренном кадре.");
                // Через весь лагерь, в обход палаток и костра.
                Vector2 destination = new Vector2(location.CanvasWidth * .85f, location.CanvasHeight * .5f);
                Assert.That(renderer.Geometry.IsPassable(destination.x, destination.y), Is.True);
                Assert.That(mover.MoveLeaderTo(destination.x, destination.y), Is.True);
                for (int i = 0; i < 800 && (!mover.IsIdle || !mover.IsGathered); i++)
                {
                    mover.Tick(.1f, out _); Render(renderer, mover, visual, i * .1f);
                    foreach (LocalFreeMover.Member member in mover.Members)
                        Assert.That(renderer.Geometry.IsPassable(member.X, member.Y), Is.True);
                }
                Assert.That(Vector2.Distance(destination, new Vector2((float)mover.Leader.X, (float)mover.Leader.Y)), Is.LessThan(1f));
                renderer.Camera.targetTexture = null;
                Object.Destroy(target);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ObjectStateSwapKeepsTheSceneAndPlacement()
        {
            LocalLocationDefinition location = new LocalLocationDefinition { DisplayName = "Тест" };
            LocationVisualObject item = new LocationVisualObject { Placeholder = LocationPlaceholder.Crate };
            LocationVisualDefinition visual = new LocationVisualDefinition { Objects = new List<LocationVisualObject> { item } };
            Texture2D texture = new Texture2D(2, 2);
            Sprite burned = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            item.Variants.Add(new LocationVisualVariant { Id = "burned", Sprite = burned });
            using (LocationWorldRenderer renderer = new LocationWorldRenderer(location, visual, null))
            {
                SpriteRenderer image = renderer.FindObject(item.Id);
                Transform anchor = image.transform.parent;
                Vector3 position = anchor.position;
                Assert.That(renderer.SetObjectVariant(item.Id, "burned"), Is.True);
                Assert.That(renderer.FindObject(item.Id), Is.SameAs(image));
                Assert.That(image.sprite, Is.SameAs(burned));
                Assert.That(anchor.position, Is.EqualTo(position));
                Assert.That(item.DefaultVariantId, Is.Empty, "Runtime не должен менять авторские данные.");
            }
            Object.Destroy(burned); Object.Destroy(texture);
            yield return null;
        }

        private static void Render(LocationWorldRenderer renderer, LocalFreeMover mover, LocationVisualDefinition visual, float seconds)
        {
            List<LocationWorldRenderer.ActorFrame> frames = new List<LocationWorldRenderer.ActorFrame>();
            foreach (LocalFreeMover.Member member in mover.Members)
                frames.Add(new LocationWorldRenderer.ActorFrame { Id = member.Id, UnitTypeId = visual.TestUnitId,
                    Pixel = new Vector2((float)member.X, (float)member.Y), Direction = new Vector2((float)member.DirectionX, (float)member.DirectionY), Walking = member.Walking });
            renderer.SetActors(frames, seconds);
        }

        private static Color SaveFrame(RenderTexture target, string name)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            Texture2D texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            Color pixel = texture.GetPixel(100, 100);
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/LocationLighting"));
            Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
            Object.Destroy(texture); RenderTexture.active = previous;
            return pixel;
        }
    }
}
