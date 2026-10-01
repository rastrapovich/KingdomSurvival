using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.Encounters.Editor
{
    // Окно дорожных и прочих встреч для автора. Три вкладки:
    // «Встречи» — список и карточка встречи (сцена, когда выпадает, что
    // остаётся после, заметки); «Проверка пула» — тестовые условия,
    // Монте-Карло, путешествия и настройки пулов; «Флаги» — реестр флагов.
    // Технические поля (ID, пул, вес, режимы) — за «⚙ Производство».
    public sealed partial class EncounterDatabaseWindow : EditorWindow
    {
        private const string EncountersAssetPath = "Assets/_Project/Encounters/Resources/Encounters/KingdomSurvivalEncounters.asset";
        private const string FlagsAssetPath = "Assets/_Project/Encounters/Resources/Encounters/KingdomSurvivalEncounterFlags.asset";
        private const string EncountersFolder = "Assets/_Project/Encounters/Resources/Encounters";
        private const string ShowProductionPrefKey = "KingdomSurvival.Encounters.ShowProduction";

        private static readonly Color GoodColor = new Color(0.42f, 0.72f, 0.45f, 1f);
        private static readonly Color BadColor = new Color(0.90f, 0.48f, 0.38f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
        private static readonly Color HeaderColor = new Color(0.80f, 0.66f, 0.34f, 1f);

        private enum Tab { Encounters, PoolCheck, Flags }

        // Тестовые условия: общие для проверки одной встречи и всего пула.
        [Serializable]
        private sealed class PreviewContext
        {
            public string Region = "road";
            public string LocationTags = string.Empty;
            public float WorldHour;
            public int PartySize = 1;
            public string Flags = string.Empty;
            public List<int> Qualities = new List<int>();

            public int GetQuality(HeroQuality quality)
            {
                int index = (int)quality;
                return index < Qualities.Count ? Qualities[index] : HeroProfileData.DefaultQualityValue;
            }

            public void SetQuality(HeroQuality quality, int value)
            {
                int index = (int)quality;
                while (Qualities.Count <= index)
                    Qualities.Add(HeroProfileData.DefaultQualityValue);
                Qualities[index] = value;
            }

            public HeroProfileData BuildHero()
            {
                HeroProfileData hero = new HeroProfileData();
                foreach (HeroQuality quality in Enum.GetValues(typeof(HeroQuality)))
                    hero.SetQuality(quality, GetQuality(quality));
                return hero;
            }
        }

        [SerializeField] private Tab activeTab = Tab.Encounters;
        [SerializeField] private int selectedEncounterIndex = -1;
        [SerializeField] private int selectedFlagIndex = -1;
        [SerializeField] private PreviewContext previewContext = new PreviewContext();

        private bool showProduction;

        private EncounterDatabaseAsset database;
        private SerializedObject serializedDatabase;
        private SerializedProperty poolsProperty;
        private SerializedProperty encountersProperty;

        private EncounterFlagRegistryAsset flagRegistry;
        private SerializedObject serializedFlagRegistry;
        private SerializedProperty flagsProperty;

        private VisualElement tabBar;
        private VisualElement content;

        [MenuItem("Kingdom Survival/Энкаунтеры")]
        public static void OpenWindow()
        {
            EncounterDatabaseWindow window = GetWindow<EncounterDatabaseWindow>();
            window.titleContent = new GUIContent("Встречи");
            window.minSize = new Vector2(720f, 480f);
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            showProduction = EditorPrefs.GetBool(ShowProductionPrefKey, false);
            if (previewContext == null)
                previewContext = new PreviewContext();

            database = LoadOrCreate<EncounterDatabaseAsset>(EncountersAssetPath);
            flagRegistry = LoadOrCreate<EncounterFlagRegistryAsset>(FlagsAssetPath);

            serializedDatabase = new SerializedObject(database);
            poolsProperty = serializedDatabase.FindProperty("pools");
            encountersProperty = serializedDatabase.FindProperty("encounters");

            serializedFlagRegistry = new SerializedObject(flagRegistry);
            flagsProperty = serializedFlagRegistry.FindProperty("flags");

            tabBar = new VisualElement();
            tabBar.style.flexDirection = FlexDirection.Row;
            tabBar.style.height = 28f;
            tabBar.style.paddingLeft = 6f;
            tabBar.style.paddingRight = 6f;
            tabBar.style.paddingTop = 3f;
            tabBar.style.borderBottomWidth = 1f;
            tabBar.style.borderBottomColor = new Color(0f, 0f, 0f, 0.3f);
            rootVisualElement.Add(tabBar);

            content = new VisualElement();
            content.style.flexGrow = 1f;
            rootVisualElement.Add(content);

            ShowActiveTab();
        }

        private void RebuildTabBar()
        {
            tabBar.Clear();
            tabBar.Add(MakeTabButton("Встречи", Tab.Encounters));
            tabBar.Add(MakeTabButton("Проверка пула", Tab.PoolCheck));
            tabBar.Add(MakeTabButton("Флаги", Tab.Flags));

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            tabBar.Add(spacer);

            ToolbarToggle production = new ToolbarToggle { text = "⚙ Производство", value = showProduction };
            production.tooltip = "Показать технические поля: ID, пул, вес, режимы выбора, теги";
            production.RegisterValueChangedCallback(evt =>
            {
                showProduction = evt.newValue;
                EditorPrefs.SetBool(ShowProductionPrefKey, showProduction);
                ShowActiveTab();
            });
            tabBar.Add(production);
        }

        private Button MakeTabButton(string text, Tab tab)
        {
            Button button = new Button(() => { activeTab = tab; ShowActiveTab(); }) { text = text };
            button.style.height = 22f;
            button.style.marginRight = 4f;
            if (activeTab == tab)
            {
                button.style.unityFontStyleAndWeight = FontStyle.Bold;
                button.style.borderBottomWidth = 2f;
                button.style.borderBottomColor = HeaderColor;
            }
            return button;
        }

        private void ShowActiveTab()
        {
            RebuildTabBar();
            content.Clear();
            switch (activeTab)
            {
                case Tab.Encounters:
                    BuildEncountersTab(content);
                    break;
                case Tab.PoolCheck:
                    BuildPoolCheckTab(content);
                    break;
                default:
                    BuildFlagsTab(content);
                    break;
            }
        }

        // ---------------------------------------------------------------
        // Тестовые условия (общие поля для карточки и вкладки «Проверка пула»).
        // ---------------------------------------------------------------

        private void BuildPreviewContextFields(VisualElement parent)
        {
            TextField region = new TextField("Регион") { value = previewContext.Region };
            region.RegisterValueChangedCallback(evt => previewContext.Region = evt.newValue);
            parent.Add(region);

            TextField tags = new TextField("Приметы места") { value = previewContext.LocationTags, tooltip = "Теги места через запятую" };
            tags.RegisterValueChangedCallback(evt => previewContext.LocationTags = evt.newValue);
            parent.Add(tags);

            FloatField hour = new FloatField("Час мира") { value = previewContext.WorldHour };
            hour.RegisterValueChangedCallback(evt => previewContext.WorldHour = evt.newValue);
            parent.Add(hour);

            IntegerField party = new IntegerField("В отряде") { value = previewContext.PartySize };
            party.RegisterValueChangedCallback(evt => previewContext.PartySize = Mathf.Max(1, evt.newValue));
            parent.Add(party);

            TextField flags = new TextField("Флаги мира") { value = previewContext.Flags, multiline = true, tooltip = "По одному на строку" };
            flags.style.minHeight = 38f;
            flags.RegisterValueChangedCallback(evt => previewContext.Flags = evt.newValue);
            parent.Add(flags);

            Foldout qualities = new Foldout { text = "Качества героя", value = false };
            foreach (HeroQuality quality in Enum.GetValues(typeof(HeroQuality)))
            {
                HeroQuality captured = quality;
                SliderInt field = new SliderInt(NarrativeQualityLabels.GetLabel(quality),
                    HeroProfileData.MinQualityValue, HeroProfileData.MaxQualityValue) { value = previewContext.GetQuality(quality), showInputField = true };
                field.RegisterValueChangedCallback(evt => previewContext.SetQuality(captured, evt.newValue));
                qualities.Add(field);
            }
            parent.Add(qualities);
        }

        private NarrativeEvaluationContext BuildEvaluationContext(NarrativeStateData state, int worldSeed = 0)
        {
            return new NarrativeEvaluationContext(previewContext.BuildHero(), state,
                partySize: Mathf.Max(1, previewContext.PartySize), worldSeed: worldSeed);
        }

        private NarrativeStateData BuildPreviewState()
        {
            NarrativeStateData state = new NarrativeStateData();
            foreach (string flag in SplitLines(previewContext.Flags))
                state.SetFlag(flag);
            return state;
        }

        // ---------------------------------------------------------------
        // Общие мелкие помощники.
        // ---------------------------------------------------------------

        // ---------------------------------------------------------------
        // Карточки разделов: свой фон, рамка, цветная полоса слева и
        // заголовок того же цвета — разделы не сливаются в общий серый.
        // ---------------------------------------------------------------

        private static readonly Color PaneBackground = new Color(0.135f, 0.14f, 0.155f, 1f);
        private static readonly Color CardBackground = new Color(0.20f, 0.205f, 0.225f, 1f);
        private static readonly Color CardBorder = new Color(0.10f, 0.10f, 0.11f, 1f);
        internal static readonly Color SceneAccent = new Color(0.86f, 0.70f, 0.38f, 1f);
        internal static readonly Color WhenAccent = new Color(0.40f, 0.62f, 0.90f, 1f);
        internal static readonly Color AfterAccent = new Color(0.45f, 0.75f, 0.50f, 1f);
        internal static readonly Color NotesAccent = new Color(0.62f, 0.62f, 0.66f, 1f);
        internal static readonly Color ProductionAccent = new Color(0.68f, 0.52f, 0.88f, 1f);

        // Карточка с заголовком. Ширина: растягивается, но не уже minWidth —
        // на широком окне карточки встают в две колонки.
        private static VisualElement SectionCard(string title, Color accent, float minWidth = 380f)
        {
            VisualElement card = new VisualElement();
            card.style.flexGrow = 1f;
            card.style.flexBasis = minWidth;
            card.style.minWidth = minWidth;
            card.style.marginRight = 10f;
            card.style.marginBottom = 10f;
            card.style.paddingLeft = 12f;
            card.style.paddingRight = 12f;
            card.style.paddingTop = 8f;
            card.style.paddingBottom = 10f;
            card.style.backgroundColor = CardBackground;
            card.style.borderTopWidth = 1f;
            card.style.borderRightWidth = 1f;
            card.style.borderBottomWidth = 1f;
            card.style.borderLeftWidth = 3f;
            card.style.borderTopColor = CardBorder;
            card.style.borderRightColor = CardBorder;
            card.style.borderBottomColor = CardBorder;
            card.style.borderLeftColor = accent;
            card.style.borderTopLeftRadius = 5f;
            card.style.borderTopRightRadius = 5f;
            card.style.borderBottomLeftRadius = 5f;
            card.style.borderBottomRightRadius = 5f;
            if (!string.IsNullOrEmpty(title))
            {
                Label header = new Label(title);
                header.style.unityFontStyleAndWeight = FontStyle.Bold;
                header.style.fontSize = 11f;
                header.style.letterSpacing = 1f;
                header.style.color = accent;
                header.style.marginBottom = 6f;
                card.Add(header);
            }
            return card;
        }

        // Сетка карточек с переносом по ширине окна.
        private static VisualElement CardGrid()
        {
            VisualElement grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.FlexStart;
            return grid;
        }

        // Тёмный фон области карточек: на нём карточки читаются отдельно.
        private static void StyleCardPane(VisualElement pane)
        {
            pane.style.backgroundColor = PaneBackground;
        }

        private static void AddToolbarButton(VisualElement parent, string text, Action action)
        {
            Button button = new Button(action) { text = text };
            button.style.height = 22f;
            button.style.marginRight = 6f;
            parent.Add(button);
        }

        private static void AddHeader(VisualElement parent, string text)
        {
            Label label = new Label(text);
            label.style.marginTop = 12f;
            label.style.marginBottom = 4f;
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

        private static List<string> SplitLines(string text)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    result.Add(trimmed);
            }
            return result;
        }

        private static List<string> SplitCsv(string text)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;
            foreach (string part in text.Split(','))
            {
                string trimmed = part.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    result.Add(trimmed);
            }
            return result;
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
