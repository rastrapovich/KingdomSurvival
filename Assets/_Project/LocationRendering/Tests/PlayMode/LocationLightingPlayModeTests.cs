using System.Collections;
using System.Collections.Generic;
using System.IO;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
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
            Assert.That(test.Mover.Members.Count, Is.EqualTo(3));
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
                List<HexCoord> cells = new List<HexCoord>(renderer.Geometry.Region(new HexCoord(4, 4)));
                Assert.That(cells.Count, Is.GreaterThan(5));
                List<KeyValuePair<string, HexCoord>> members = new List<KeyValuePair<string, HexCoord>>();
                for (int i = 0; i < 3; i++) members.Add(new KeyValuePair<string, HexCoord>("test_" + i, cells[i]));
                LocalPartyMover mover = new LocalPartyMover(renderer.Geometry.IsPassable, renderer.Geometry.StepCost, members);
                renderer.AddTestActors(3); renderer.RenderActors(mover.Members, 0);
                yield return null; yield return null;
                Color dayPixel = SaveFrame(target, "camp-day");
                renderer.SetTime(1, 0);
                Assert.That(renderer.GlobalLight.intensity, Is.LessThan(day));
                yield return null; yield return null;
                Color nightPixel = SaveFrame(target, "camp-night");
                Assert.That(dayPixel.grayscale, Is.GreaterThan(nightPixel.grayscale + .03f), "Фон должен действительно темнеть в отрендеренном кадре.");
                HexCoord destination = cells[cells.Count - 1];
                Assert.That(mover.MoveLeaderTo(destination), Is.True);
                for (int i = 0; i < 800 && (!mover.IsIdle || !mover.IsGathered); i++)
                {
                    mover.Tick(.1f); renderer.RenderActors(mover.Members, i * .1f);
                    foreach (LocalPartyMover.Member member in mover.Members)
                        Assert.That(renderer.Geometry.IsPassable(member.SettledCell), Is.True);
                }
                Assert.That(mover.Leader.Cell, Is.EqualTo(destination));
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
