using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KingdomSurvival.MapLab.Editor
{
    // Ассеты эксперимента: палитра, три проверочных пресета и тестовая сцена.
    // Build Settings и стартовая сцена игры не меняются.
    public static class MapLabAssets
    {
        public const string Root = "Assets/Experiments/MapLab";
        public const string PresetFolder = Root + "/Presets";
        public const string SceneFolder = Root + "/Scenes";
        public const string ScenePath = SceneFolder + "/MapLab_Painterly_Test.unity";
        public const string PalettePath = PresetFolder + "/MapLab_Palette_Variant04.asset";
        public const string ShaderFolder = Root + "/Shaders";

        public static string PresetPath(MapLabPresetKind kind)
        {
            switch (kind)
            {
                case MapLabPresetKind.ForestLake: return PresetFolder + "/MapLab_B_ForestLake.asset";
                case MapLabPresetKind.RiverValley: return PresetFolder + "/MapLab_C_RiverValley.asset";
                default: return PresetFolder + "/MapLab_A_HillAndLedge.asset";
            }
        }

        public static MapLabPalette EnsurePalette()
        {
            MapLabPalette palette = AssetDatabase.LoadAssetAtPath<MapLabPalette>(PalettePath);
            if (palette != null)
                return palette;
            Directory.CreateDirectory(PresetFolder);
            palette = ScriptableObject.CreateInstance<MapLabPalette>();
            AssetDatabase.CreateAsset(palette, PalettePath);
            AssetDatabase.SaveAssets();
            return palette;
        }

        public static MapLabSettings EnsurePreset(MapLabPresetKind kind)
        {
            string path = PresetPath(kind);
            MapLabSettings settings = AssetDatabase.LoadAssetAtPath<MapLabSettings>(path);
            if (settings != null)
                return settings;
            Directory.CreateDirectory(PresetFolder);
            settings = ScriptableObject.CreateInstance<MapLabSettings>();
            MapLabSettings.ApplyPresetDefaults(settings, kind);
            settings.PainterlyStyle.Palette = EnsurePalette();
            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();
            return settings;
        }

        public static void EnsureAllPresets()
        {
            EnsurePalette();
            EnsurePreset(MapLabPresetKind.HillAndLedge);
            EnsurePreset(MapLabPresetKind.ForestLake);
            EnsurePreset(MapLabPresetKind.RiverValley);
        }

        public static Shader LoadShader(string file)
        {
            return AssetDatabase.LoadAssetAtPath<Shader>(ShaderFolder + "/" + file);
        }

        [MenuItem("Kingdom Survival/MapLab (эксперимент)/Создать тестовую сцену", priority = 401)]
        public static void CreateTestSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            CreateTestScene(true);
        }

        // Создаёт (или перезаписывает по подтверждению) сцену MapLab_Painterly_Test.
        public static Scene CreateTestScene(bool interactive)
        {
            EnsureAllPresets();
            if (interactive && File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("MapLab", "Тестовая сцена уже есть. Пересоздать её?", "Пересоздать", "Открыть имеющуюся"))
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject cameraObject = new GameObject("MapLabCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowMSAA = true;
            camera.allowHDR = false;
            cameraObject.tag = "MainCamera";
            MapLabCameraRig rig = cameraObject.AddComponent<MapLabCameraRig>();

            GameObject mapObject = new GameObject("MapLab");
            MapLabView view = mapObject.AddComponent<MapLabView>();
            view.Settings = EnsurePreset(MapLabPresetKind.HillAndLedge);
            view.CameraRig = rig;
            view.TerrainShader = LoadShader("MapLabTerrain.shader");
            view.WaterShader = LoadShader("MapLabWater.shader");
            view.CardShader = LoadShader("MapLabCard.shader");
            view.SolidShader = LoadShader("MapLabSolid.shader");
            view.LinesShader = LoadShader("MapLabLines.shader");
            view.ArtCardShader = LoadShader("MapLabArtCard.shader");
            view.ContactShader = LoadShader("MapLabContact.shader");
            view.InkShader = LoadShader("MapLabInk.shader");
            view.GrassShader = LoadShader("MapLabGrass.shader");
            view.MarkerSet04 = MapLabArtImport.LoadStage04Markers();
            view.ModuleShader = LoadShader("MapLabModule.shader");
            view.ArtSet05 = MapLabArtImport.LoadStage05Set();
            view.MarkerSet05 = MapLabArtImport.LoadStage05Markers();
            view.ArtSet = MapLabArtImport.LoadArtSet();
            view.MarkerSet = MapLabArtImport.LoadMarkerSet();
            view.ArtSet03 = MapLabArtImport.LoadStage03Set();
            mapObject.AddComponent<MapLabMarker>().View = view;

            Directory.CreateDirectory(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            return scene;
        }

        // Дополнить существующую сцену этапа 1 тем, что нужно этапу 02: шейдеры
        // карточек, набор арта, фишка. Сгенерированное в сцену не сохраняется.
        //   -executeMethod KingdomSurvival.MapLab.Editor.MapLabAssets.UpgradeSceneBatch
        public static void UpgradeSceneBatch()
        {
            int code = 0;
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                MapLabView view = FindView();
                view.ArtCardShader = LoadShader("MapLabArtCard.shader");
                view.ContactShader = LoadShader("MapLabContact.shader");
                view.InkShader = LoadShader("MapLabInk.shader");
                view.GrassShader = LoadShader("MapLabGrass.shader");
                view.MarkerSet04 = MapLabArtImport.LoadStage04Markers();
                view.ModuleShader = LoadShader("MapLabModule.shader");
                view.ArtSet05 = MapLabArtImport.LoadStage05Set();
                view.MarkerSet05 = MapLabArtImport.LoadStage05Markers();
                view.ArtSet = MapLabArtImport.LoadArtSet();
                view.MarkerSet = MapLabArtImport.LoadMarkerSet();
                view.ArtSet03 = MapLabArtImport.LoadStage03Set();
                MapLabMarker marker = view.GetComponent<MapLabMarker>();
                if (marker == null)
                    marker = view.gameObject.AddComponent<MapLabMarker>();
                marker.View = view;
                view.Clear();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                code = 1;
            }
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        public static MapLabView FindView()
        {
            return Object.FindAnyObjectByType<MapLabView>();
        }
    }
}
