using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.LocationRendering.Editor
{
    public static class LocationLightingTestBootstrap
    {
        [InitializeOnLoadMethod]
        private static void ObservePlayMode()
        {
            EditorApplication.playModeStateChanged -= RestoreAfterPlay;
            EditorApplication.playModeStateChanged += RestoreAfterPlay;
        }

        private static void RestoreAfterPlay(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) RestorePlayScene();
        }
        public const string CampId = "technical_lighting_camp";
        public const string FieldId = "technical_lighting_camp_field";
        public const string ScenePath = "Assets/_Project/LocationRendering/LocationLightingTest.unity";
        private const string PreviousSceneKey = "KS.LocationLighting.PreviousPlayScene";
        private const string HasPreviousSceneKey = "KS.LocationLighting.HasPreviousPlayScene";

        // Вызывается явно из меню/подготовки. Повторный вызов ничего не пересоздаёт.
        public static void EnsureCamp()
        {
            EnsureShadowPrefab();
            LocalLocationDatabaseAsset database = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            BattlefieldDatabaseAsset fields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
            if (database == null || fields == null) throw new System.InvalidOperationException("Не найдены существующие базы мест и полей.");
            if (fields.FindById(FieldId) == null)
            {
                SerializedObject serialized = new SerializedObject(fields);
                SerializedProperty array = serialized.FindProperty("battlefields");
                int index = array.arraySize; array.InsertArrayElementAtIndex(index);
                SerializedProperty item = array.GetArrayElementAtIndex(index);
                item.FindPropertyRelative("id").stringValue = FieldId;
                item.FindPropertyRelative("displayLabel").stringValue = "Лагерь · тест света";
                item.FindPropertyRelative("background").objectReferenceValue = null;
                item.FindPropertyRelative("backgroundScale").floatValue = 1;
                item.FindPropertyRelative("backgroundOffset").vector2Value = Vector2.zero;
                item.FindPropertyRelative("gridScale").floatValue = 1;
                item.FindPropertyRelative("gridOffset").vector2Value = Vector2.zero;
                item.FindPropertyRelative("disabledCells").ClearArray();
                item.FindPropertyRelative("tagIds").ClearArray();
                item.FindPropertyRelative("awaitingArt").boolValue = true;
                item.FindPropertyRelative("useOwnHexStyle").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(fields);
            }
            if (!database.locations.Any(item => item.Id == CampId))
            {
                database.locations.Add(new LocalLocationDefinition
                {
                    Id = CampId, WorldLocationId = "__technical_lighting_camp", DisplayName = "Лагерь",
                    BattlefieldId = FieldId, PlaceholderArt = true,
                    Entrances = new List<LocalEntranceDefinition>
                    { new LocalEntranceDefinition { Id = "camp_entry", Label = "Вход", Point = new LocalPointData(960, 540) } }
                });
                EditorUtility.SetDirty(database);
            }
            if (database.FindVisual(CampId) == null)
            {
                LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = CampId, TechnicalTest = true };
                UnitDatabaseAsset units = Resources.Load<UnitDatabaseAsset>(UnitDatabaseAsset.ResourcesPath);
                UnitDefinitionData actor = units?.Units.FirstOrDefault(item => item.Category == UnitCategory.Commander) ??
                    units?.Units.FirstOrDefault(item => item.Category == UnitCategory.Fighter);
                if (actor != null) visual.TestUnitId = actor.Id;
                visual.Objects.Add(Placed("Палатка слева", LocationPlaceholder.Tent, .27f, .40f, 2, true));
                visual.Objects.Add(Placed("Палатка справа", LocationPlaceholder.Tent, .72f, .39f, 2.2f, true));
                visual.Objects.Add(Placed("Палатка в глубине", LocationPlaceholder.Tent, .51f, .25f, 1.8f, true));
                // В костёр не встают; тени от своего основания у него нет — иначе
                // он затенял бы собственный свет.
                LocationVisualObject fire = Placed("Костёр", LocationPlaceholder.Fire, .51f, .58f, .8f, true);
                fire.CastsShadow = false;
                fire.Light.Enabled = true; fire.Light.Radius = 3.6f; fire.Light.Intensity = 1.7f;
                visual.Objects.Add(fire);
                visual.Objects.Add(Placed("Ящик", LocationPlaceholder.Crate, .33f, .69f, .7f, true));
                visual.Objects.Add(Placed("Камень", LocationPlaceholder.Rock, .76f, .72f, .6f, true));
                visual.Objects.Add(Placed("Куст", LocationPlaceholder.Bush, .18f, .72f, 1, false));
                database.visuals.Add(visual); EditorUtility.SetDirty(database);
            }
            AssetDatabase.SaveAssetIfDirty(database);
            EnsureScene();
        }

        private static void EnsureShadowPrefab()
        {
            const string path = "Assets/_Project/LocationRendering/Resources/LocationRendering/GroundShadowCaster.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.Refresh();
            GameObject root = new GameObject("Тень · основание");
            root.SetActive(false);
            ShadowCaster2D caster = root.AddComponent<ShadowCaster2D>();
            SerializedObject serialized = new SerializedObject(caster);
            SerializedProperty shape = serialized.FindProperty("m_ShapePath");
            shape.arraySize = 4;
            Vector3[] points = { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
            for (int i = 0; i < 4; i++) shape.GetArrayElementAtIndex(i).vector3Value = points[i];
            serialized.FindProperty("m_ShadowCastingSource").intValue = 1;
            serialized.FindProperty("m_ShapePathHash").intValue = 1;
            serialized.FindProperty("m_CastingOption").intValue = (int)ShadowCaster2D.ShadowCastingOptions.CastShadow;
            SerializedProperty layers = serialized.FindProperty("m_ApplyToSortingLayers");
            layers.arraySize = 1; layers.GetArrayElementAtIndex(0).intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static LocationVisualObject Placed(string name, LocationPlaceholder kind, float x, float y, float height, bool block) =>
            new LocationVisualObject { Name = name, Placeholder = kind, Position = new Vector2(x, y), Height = height,
                BlocksMovement = block, CastsShadow = block, Footprint = new Vector2(kind == LocationPlaceholder.Tent ? 1.5f : .65f, .65f) };

        private static void EnsureScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            Scene previous = SceneManager.GetActiveScene();
            // В batchmode Unity начинает с пустой несохранённой сцены.
            // При обычной работе открытые сцены никогда не заменяем.
            bool batchEmpty = Application.isBatchMode && string.IsNullOrEmpty(previous.path);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batchEmpty ? NewSceneMode.Single : NewSceneMode.Additive);
            GameObject root = new GameObject("Проверка локаций и света");
            SceneManager.MoveGameObjectToScene(root, scene);
            LocationLightingTest test = root.AddComponent<LocationLightingTest>();
            test.Panel = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset");
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }

        public static void Launch(string id, float hour = 13)
        {
            EnsureCamp();
            SessionState.SetString("KS.LocationLighting.Id", id);
            SessionState.SetFloat("KS.LocationLighting.Hour", hour);
            if (!SessionState.GetBool(HasPreviousSceneKey, false))
            {
                SessionState.SetString(PreviousSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
                SessionState.SetBool(HasPreviousSceneKey, true);
            }
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            EditorApplication.isPlaying = true;
        }
        public static void RestorePlayScene()
        {
            if (!SessionState.GetBool(HasPreviousSceneKey, false)) return;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(PreviousSceneKey, ""));
            SessionState.EraseBool(HasPreviousSceneKey);
            SessionState.EraseString(PreviousSceneKey);
        }
    }
}
