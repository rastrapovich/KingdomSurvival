using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Тени глобальной карты: силуэт карточки у основания вместо пятен маски.
    public sealed class MapLabMapShadowTests
    {
        private MapLabSettings settings;
        private GameObject mapObject;
        private Material material;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<MapLabSettings>();
            settings.Stage03.Look = MapLabLook.Stage05;
            settings.Stage03.ObjectShadows = true;
            settings.Stage03.SilhouetteShadows = false;
            mapObject = new GameObject("MapLab shadows (test)");
            mapObject.SetActive(false);
            MapLabView view = mapObject.AddComponent<MapLabView>();
            view.GenerateOnLoad = false;
            view.Settings = settings;
            mapObject.SetActive(true);
            Shader shader = Shader.Find("Kingdom Survival/MapLab/Art Card");
            Assert.IsNotNull(shader, "шейдер карточек не найден");
            material = new Material(shader);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(mapObject);
            Object.DestroyImmediate(settings);
        }

        private MapLabView View => mapObject.GetComponent<MapLabView>();

        [Test]
        public void MapCardShadows_OnForStage05Map_OffForArenaOrDisabledShadows()
        {
            Assert.IsTrue(settings.MapCardShadows);
            settings.Stage03.SilhouetteShadows = true;
            Assert.IsFalse(settings.MapCardShadows, "арена рисует тени сама");
            settings.Stage03.SilhouetteShadows = false;
            settings.Stage03.ObjectShadows = false;
            Assert.IsFalse(settings.MapCardShadows);
            settings.Stage03.ObjectShadows = true;
            settings.Stage05.CardShadows = false;
            Assert.IsFalse(settings.MapCardShadows);
        }

        [Test]
        public void CardGround_GoesWithLightAndAwayFromViewer_NeverTowardsViewer()
        {
            foreach (Vector3 light in new[] { new Vector3(-0.62f, 0.72f, 0.06f), new Vector3(-0.3f, 0.8f, -0.5f), new Vector3(0f, 0.9f, 0f), new Vector3(-0.7f, 0.6f, 0.4f) })
            {
                Vector2 g = MapLabObjectShadows.CardGround(light, 0.6f, 25f);
                Assert.Greater(g.y, 0f, "тень не должна выходить к зрителю: " + light);
                Assert.GreaterOrEqual(g.y, g.magnitude * Mathf.Sin(25f * Mathf.Deg2Rad) - 1e-4f);
                if (light.x < -0.01f)
                    Assert.Greater(g.x, 0f, "тень уходит вправо при свете слева");
            }
            Vector2 near = MapLabObjectShadows.CardGround(new Vector3(-0.62f, 0.72f, 0.06f), 0.6f, 25f);
            Vector2 far = MapLabObjectShadows.CardGround(new Vector3(-0.62f, 0.72f, 0.06f), 1.2f, 25f);
            Assert.AreEqual(near.magnitude * 2f, far.magnitude, 1e-3f, "длина пропорциональна настройке");
        }

        [Test]
        public void Map_CardsDoNotUseArenaPass_ShadowShaderIsFindable_ArenaKeepsItsRule()
        {
            // На карте проход арены (Universal2D) выключен: тень рисует отдельный шейдер.
            Assert.IsNotNull(Shader.Find("Kingdom Survival/MapLab/Card Shadow"), "шейдер тени карточек не найден (Resources)");
            View.ConfigureArtMaterial(material, new MapLabArtEntry { Category = MapLabArtCategory.Vegetation });
            Assert.AreEqual(0f, material.GetFloat("_CastShadow"));
            settings.Stage03.SilhouetteShadows = true;
            View.ConfigureArtMaterial(material, new MapLabArtEntry { Category = MapLabArtCategory.Vegetation });
            Assert.AreEqual(1f, material.GetFloat("_CastShadow"), "арена: прежнее правило");
            View.ConfigureArtMaterial(material, new MapLabArtEntry { Category = MapLabArtCategory.Building });
            Assert.AreEqual(0f, material.GetFloat("_CastShadow"));
        }
    }
}
