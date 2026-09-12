using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.Encounters.Editor
{
    // Phase 2 (E01-T08/T09/T10 по производственной инструкции Encounter-
    // системы), урезано под правило CLAUDE.md "реалистично для малого
    // производства": список + инспектор + Validate All + минимальный Eligibility
    // Preview для энкаунтеров, список + инспектор + dependency viewer для
    // флагов. Monte Carlo Simulator, Live GameState (Available Now в Play
    // Mode), граф диалога — сознательно не в этом проходе.
    //
    // Построено по образцу BattlefieldDatabase/Editor/BattlefieldDatabaseWindow.cs:
    // UI Toolkit, SerializedObject/SerializedProperty + PropertyField (не
    // custom IMGUI), TwoPaneSplitView, ListView с search+filter.
    public sealed partial class EncounterDatabaseWindow : EditorWindow
    {
        private const string EncountersAssetPath = "Assets/_Project/Encounters/Resources/Encounters/KingdomSurvivalEncounters.asset";
        private const string FlagsAssetPath = "Assets/_Project/Encounters/Resources/Encounters/KingdomSurvivalEncounterFlags.asset";
        private const string EncountersFolder = "Assets/_Project/Encounters/Resources/Encounters";

        private static readonly Color GoodColor = new Color(0.42f, 0.72f, 0.45f, 1f);
        private static readonly Color BadColor = new Color(0.90f, 0.48f, 0.38f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Color HeaderColor = new Color(0.80f, 0.66f, 0.34f, 1f);

        private enum Tab { Encounters, Flags }

        [SerializeField] private Tab activeTab = Tab.Encounters;
        [SerializeField] private int selectedEncounterIndex = -1;
        [SerializeField] private int selectedFlagIndex = -1;

        private EncounterDatabaseAsset database;
        private SerializedObject serializedDatabase;
        private SerializedProperty poolsProperty;
        private SerializedProperty encountersProperty;

        private EncounterFlagRegistryAsset flagRegistry;
        private SerializedObject serializedFlagRegistry;
        private SerializedProperty flagsProperty;

        private VisualElement tabBar;
        private VisualElement content;
        private Label validationLabel;

        [MenuItem("Kingdom Survival/Энкаунтеры")]
        public static void OpenWindow()
        {
            EncounterDatabaseWindow window = GetWindow<EncounterDatabaseWindow>();
            window.titleContent = new GUIContent("Энкаунтеры");
            window.minSize = new Vector2(1080f, 640f);
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();

            database = LoadOrCreate<EncounterDatabaseAsset>(EncountersAssetPath);
            flagRegistry = LoadOrCreate<EncounterFlagRegistryAsset>(FlagsAssetPath);

            serializedDatabase = new SerializedObject(database);
            poolsProperty = serializedDatabase.FindProperty("pools");
            encountersProperty = serializedDatabase.FindProperty("encounters");

            serializedFlagRegistry = new SerializedObject(flagRegistry);
            flagsProperty = serializedFlagRegistry.FindProperty("flags");

            BuildTabBar();
            content = new VisualElement();
            content.style.flexGrow = 1f;
            rootVisualElement.Add(content);

            ShowActiveTab();
        }

        private void BuildTabBar()
        {
            tabBar = new VisualElement();
            tabBar.style.flexDirection = FlexDirection.Row;
            tabBar.style.height = 30f;
            tabBar.style.paddingLeft = 8f;
            tabBar.style.paddingTop = 4f;
            tabBar.style.borderBottomWidth = 1f;
            tabBar.style.borderBottomColor = new Color(0f, 0f, 0f, 0.3f);

            tabBar.Add(MakeTabButton("ЭНКАУНТЕРЫ", Tab.Encounters));
            tabBar.Add(MakeTabButton("ФЛАГИ", Tab.Flags));
            rootVisualElement.Add(tabBar);
        }

        private Button MakeTabButton(string text, Tab tab)
        {
            Button button = new Button(() => { activeTab = tab; ShowActiveTab(); }) { text = text };
            button.style.height = 24f;
            button.style.marginRight = 4f;
            if (activeTab == tab)
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
            return button;
        }

        private void ShowActiveTab()
        {
            if (tabBar != null)
            {
                tabBar.Clear();
                tabBar.Add(MakeTabButton("ЭНКАУНТЕРЫ", Tab.Encounters));
                tabBar.Add(MakeTabButton("ФЛАГИ", Tab.Flags));
            }

            content.Clear();
            if (activeTab == Tab.Encounters)
                BuildEncountersTab(content);
            else
                BuildFlagsTab(content);
        }

        // ---------------------------------------------------------------
        // Общие мелкие помощники, используемые обеими вкладками.
        // ---------------------------------------------------------------

        private static void AddToolbarButton(VisualElement parent, string text, Action action)
        {
            Button button = new Button(action) { text = text };
            button.style.height = 26f;
            button.style.marginRight = 6f;
            parent.Add(button);
        }

        private static void AddHeader(VisualElement parent, string text)
        {
            Label label = new Label(text);
            label.style.marginTop = 12f;
            label.style.marginBottom = 5f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = HeaderColor;
            parent.Add(label);
        }

        private static Label MakeMutedLabel(string text)
        {
            Label label = new Label(text);
            label.style.fontSize = 10f;
            label.style.color = MutedColor;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static T LoadOrCreate<T>(string assetPath) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
                return asset;

            EnsureFolderExists();
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static void EnsureFolderExists()
        {
            if (AssetDatabase.IsValidFolder(EncountersFolder))
                return;

            if (!AssetDatabase.IsValidFolder("Assets/_Project/Encounters"))
                AssetDatabase.CreateFolder("Assets/_Project", "Encounters");
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Encounters/Resources"))
                AssetDatabase.CreateFolder("Assets/_Project/Encounters", "Resources");
            AssetDatabase.CreateFolder("Assets/_Project/Encounters/Resources", "Encounters");
        }
    }
}
