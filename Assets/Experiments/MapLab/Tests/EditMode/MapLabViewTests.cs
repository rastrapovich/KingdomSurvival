using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLab.Tests
{
    // Команды сцены: генерация, повтор, перекраска, пересборка объектов, очистка.
    public sealed class MapLabViewTests
    {
        private GameObject mapObject, cameraObject, userObject;
        private MapLabSettings settings;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<MapLabSettings>();
            MapLabSettings.ApplyPresetDefaults(settings, MapLabPresetKind.RiverValley);
            settings.General.Quality = MapLabQuality.Samples129;
            settings.General.PaintResolution = 512;
            cameraObject = new GameObject("MapLabCamera (test)");
            cameraObject.AddComponent<Camera>();
            MapLabCameraRig rig = cameraObject.AddComponent<MapLabCameraRig>();
            mapObject = new GameObject("MapLab (test)");
            mapObject.SetActive(false);
            MapLabView view = mapObject.AddComponent<MapLabView>();
            view.GenerateOnLoad = false;
            view.Settings = settings;
            view.CameraRig = rig;
            mapObject.SetActive(true);
            // Пользовательский объект рядом с картой и внутри её объекта — не должен пострадать.
            userObject = new GameObject("User Terrain");
            new GameObject("User child").transform.SetParent(mapObject.transform, false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(mapObject);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(userObject);
            Object.DestroyImmediate(settings);
        }

        private MapLabView View => mapObject.GetComponent<MapLabView>();

        [Test]
        public void Generate_Twice_DoesNotDuplicate_AndClearKeepsUserObjects()
        {
            Assert.IsTrue(View.Generate(null), View.LastError);
            int renderers = View.Root.GetComponentsInChildren<MeshRenderer>(true).Length;
            Assert.Greater(renderers, 5);
            Assert.IsTrue(View.Generate(null));
            Assert.AreEqual(renderers, View.Root.GetComponentsInChildren<MeshRenderer>(true).Length);
            Assert.AreEqual(1, CountRoots());
            View.Clear();
            Assert.IsNull(View.Root);
            Assert.AreEqual(0, CountRoots());
            Assert.IsNotNull(GameObject.Find("User Terrain"));
            Assert.IsNotNull(mapObject.transform.Find("User child"));
        }

        [Test]
        public void RepaintOnly_KeepsGeometry_RebuildObjects_KeepsTerrain()
        {
            Assert.IsTrue(View.Generate(null), View.LastError);
            Dictionary<string, Mesh> before = MeshIds();
            settings.PainterlyStyle.BrushStrength = 0.1f;
            settings.Diagnostics.Mode = MapLabDiagnosticMode.Slope;
            Assert.IsTrue(View.RepaintOnly());
            CollectionAssert.AreEquivalent(before, MeshIds(), "Repaint Only пересоздал меши");
            settings.Vegetation.ForestDensity = 0.9f;
            Assert.IsTrue(View.RebuildObjects(null));
            Dictionary<string, Mesh> after = MeshIds();
            foreach (KeyValuePair<string, Mesh> pair in before)
                if (pair.Key.StartsWith("Terrain/") || pair.Key.StartsWith("Water/") || pair.Key.StartsWith("Cliffs/"))
                    Assert.AreSame(pair.Value, after[pair.Key], "Rebuild Objects перестроил " + pair.Key);
        }

        [Test]
        public void EmptyOptionalSlots_FallBackWithoutErrors()
        {
            settings.PainterlyStyle.Palette = null;
            settings.Relief.ExternalHeightmap = null;
            settings.Relief.Source = MapLabHeightSource.ProceduralPlusHeightmap;
            Assert.IsTrue(View.Generate(null), View.LastError);
            Assert.IsNotNull(View.Palette);
        }

        private int CountRoots()
        {
            int count = 0;
            foreach (Transform child in mapObject.transform)
                if (child.name == MapLabView.RootName)
                    count++;
            return count;
        }

        private Dictionary<string, Mesh> MeshIds()
        {
            Dictionary<string, Mesh> ids = new Dictionary<string, Mesh>();
            foreach (MeshFilter filter in View.Root.GetComponentsInChildren<MeshFilter>(true))
                ids[filter.transform.parent.name + "/" + filter.name] = filter.sharedMesh;
            return ids;
        }
    }
}
