using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.ArtAssets.Editor
{
    // ПР-12Н: окно «База ассетов». Слева — категории и фильтры, в центре —
    // холст (объекты рядом в игровом размере), галерея или карточка ассета
    // с шестью ракурсами, справа — свойства. Загрузка — перетаскиванием
    // файлов и папок из Проводника или Project. Места ссылаются на записи по
    // ID; изменения каталога сразу видны во всех экземплярах.
    public sealed partial class ArtAssetDatabaseWindow : EditorWindow
    {
        private ArtAssetDatabaseAsset catalog;
        private ArtAssetViewState state;
        private IMGUIContainer center;
        private VisualElement categories, filterPanel;
        private ScrollView properties;
        private Label status, hint;
        private VisualElement modeBar, canvasBar;
        private PopupField<string> viewField;
        private readonly List<ArtAssetDefinition> visible = new List<ArtAssetDefinition>();
        private int visibleRevision = -1;
        private bool visibleDirty = true;
        private string query = "";
        private Dictionary<string, int> usageCounts;
        private double saveAt = -1, stateSaveAt = -1;

        private ArtAssetDefinition Selected => catalog != null ? catalog.Find(state?.SelectedId) : null;

        [MenuItem("Kingdom Survival/База ассетов")]
        public static void OpenWindow() => Open(null);

        public static ArtAssetDatabaseWindow Open(string assetId)
        {
            ArtAssetDatabaseWindow window = GetWindow<ArtAssetDatabaseWindow>();
            window.titleContent = new GUIContent("База ассетов");
            window.minSize = new Vector2(1100, 640);
            window.Show();
            if (!string.IsNullOrEmpty(assetId)) window.SelectAsset(assetId, true);
            return window;
        }

        private void OnEnable()
        {
            state ??= ArtAssetViewState.Load();
            Undo.undoRedoPerformed += OnUndo;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayChanged;
            AssemblyReloadEvents.beforeAssemblyReload += SaveAll;
            ArtAssetDatabaseAsset.Changed += CatalogChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndo;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= SaveAll;
            ArtAssetDatabaseAsset.Changed -= CatalogChanged;
            ReleaseLit();
            SaveAll();
        }

        private void PlayChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) { SaveAll(); ReleaseLit(); }
        }

        private void SaveAll()
        {
            if (catalog != null) AssetDatabase.SaveAssetIfDirty(catalog);
            state?.Save();
            saveAt = stateSaveAt = -1;
        }

        private void OnUndo()
        {
            catalog?.MarkChanged();
            BuildProperties();
            BuildCategories();
            center?.MarkDirtyRepaint();
        }

        private bool catalogDirtyFromOutside;
        private void CatalogChanged()
        {
            visibleDirty = true;
            catalogDirtyFromOutside = true;
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (saveAt > 0 && now >= saveAt && !draggingTool) { saveAt = -1; if (catalog != null) AssetDatabase.SaveAssetIfDirty(catalog); }
            if (stateSaveAt > 0 && now >= stateSaveAt) { stateSaveAt = -1; state.Save(); }
            if (catalogDirtyFromOutside)
            {
                catalogDirtyFromOutside = false;
                litDirty = true;
            }
            // Под светом и у анимированного ассета карточка живёт — перерисовка каждый кадр.
            if (state?.Mode == ArtAssetCenterMode.Card && (state.CardDisplay == ArtAssetCardDisplay.Lit || (Selected?.IsAnimated ?? false)))
                center?.MarkDirtyRepaint();
        }

        private void ScheduleStateSave() => stateSaveAt = EditorApplication.timeSinceStartup + 1;

        // Правка записи: Undo, отметка, автосохранение с задержкой.
        private void Edit(string undoName, Action action, bool rebuild = false)
        {
            if (catalog == null) return;
            Undo.RecordObject(catalog, undoName);
            action();
            EditorUtility.SetDirty(catalog);
            catalog.MarkChanged();
            saveAt = EditorApplication.timeSinceStartup + .6;
            if (rebuild) BuildProperties();
            center?.MarkDirtyRepaint();
        }

        // ------------------------------------------------------------------
        // Разметка окна
        // ------------------------------------------------------------------

        public void CreateGUI()
        {
            catalog = ArtAssetDatabaseAsset.Override != null ? ArtAssetDatabaseAsset.Override : ArtAssetImporter.LoadOrCreateCatalog();
            state ??= ArtAssetViewState.Load();
            VisualElement root = rootVisualElement;
            root.Clear();
            root.style.backgroundColor = new Color(.12f, .14f, .135f);

            Toolbar toolbar = new Toolbar();
            ToolbarSearchField search = new ToolbarSearchField { value = query };
            search.tooltip = "Поиск по русскому названию, ID и тегам";
            search.style.width = 220;
            search.RegisterValueChangedCallback(evt => { query = evt.newValue; visibleDirty = true; center?.MarkDirtyRepaint(); });
            toolbar.Add(search);
            toolbar.Add(new ToolbarButton(AddEmptyAsset) { text = "+ Ассет", tooltip = "Пустая запись; рисунки — перетаскиванием в ячейки ракурсов" });
            toolbar.Add(new ToolbarButton(LoadFiles) { text = "Загрузить файлы", tooltip = "PNG рисунка или нормали; без ракурса в имени — в выбранную ячейку карточки" });
            toolbar.Add(new ToolbarButton(LoadFolder) { text = "Загрузить папку", tooltip = "Папка объекта с ракурсами или родительская папка с несколькими объектами" });
            toolbar.Add(new ToolbarSpacer());
            modeBar = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            toolbar.Add(modeBar);
            toolbar.Add(new ToolbarSpacer());
            viewField = new PopupField<string>("Ракурс", ArtAssetLabels.Views.Select(ArtAssetLabels.ViewTitle).ToList(), (int)state.View)
            { tooltip = "Общий ракурс: все объекты холста и галереи показывают его" };
            viewField.style.width = 210;
            viewField.labelElement.style.minWidth = 50;
            viewField.RegisterValueChangedCallback(evt => { state.View = (ArtAssetView)viewField.index; ScheduleStateSave(); center?.MarkDirtyRepaint(); });
            toolbar.Add(viewField);
            toolbar.Add(new ToolbarSpacer { flex = true });
            toolbar.Add(new ToolbarButton(ValidateAll) { text = "Проверить" });
            toolbar.Add(new ToolbarButton(() => { SaveAll(); status.text = "Сохранено."; }) { text = "Сохранить" });
            ToolbarMenu more = new ToolbarMenu { text = "Ещё" };
            more.menu.AppendAction("Создать технический набор (заглушки)", _ => { ArtAssetTechnicalSet.Ensure(catalog); visibleDirty = true; BuildCategories(); status.text = "Технический набор готов."; });
            more.menu.AppendAction("Открыть Базу локаций", _ => EditorApplication.ExecuteMenuItem("Kingdom Survival/База локаций"));
            toolbar.Add(more);
            root.Add(toolbar);

            TwoPaneSplitView outer = new TwoPaneSplitView(0, 210, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = "ks-art-assets-left" };
            outer.style.flexGrow = 1;
            VisualElement leftPanel = new ScrollView();
            leftPanel.style.paddingLeft = leftPanel.style.paddingRight = 6;
            categories = new VisualElement();
            leftPanel.Add(SectionTitle("Категории"));
            leftPanel.Add(categories);
            leftPanel.Add(SectionTitle("Фильтры"));
            filterPanel = new VisualElement();
            leftPanel.Add(filterPanel);
            outer.Add(leftPanel);

            TwoPaneSplitView inner = new TwoPaneSplitView(1, 360, TwoPaneSplitViewOrientation.Horizontal) { viewDataKey = "ks-art-assets-right" };
            VisualElement middle = new VisualElement { style = { flexGrow = 1 } };
            canvasBar = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, minHeight = 24 } };
            middle.Add(canvasBar);
            center = new IMGUIContainer(DrawCenter) { focusable = true };
            center.style.flexGrow = 1;
            middle.Add(center);
            hint = new Label();
            hint.style.color = new Color(.68f, .74f, .68f);
            hint.style.paddingLeft = 8;
            hint.style.whiteSpace = WhiteSpace.Normal;
            middle.Add(hint);
            inner.Add(middle);
            properties = new ScrollView();
            properties.style.paddingLeft = properties.style.paddingRight = 10;
            inner.Add(properties);
            outer.Add(inner);
            root.Add(outer);
            status = new Label("Перетащите PNG или папки из Проводника либо Sprite из Project прямо в окно.");
            status.style.paddingLeft = 8;
            status.style.minHeight = 24;
            status.style.whiteSpace = WhiteSpace.Normal;
            root.Add(status);

            RegisterDrop();
            RefreshUsage();
            BuildModeBar();
            BuildCategories();
            BuildFilters();
            BuildProperties();
        }

        private static Label SectionTitle(string text)
        {
            Label label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new Color(.88f, .78f, .53f);
            label.style.marginTop = 10;
            label.style.marginBottom = 4;
            return label;
        }

        private void BuildModeBar()
        {
            if (modeBar == null) return;
            modeBar.Clear();
            foreach ((ArtAssetCenterMode mode, string title) in new[]
                     { (ArtAssetCenterMode.Canvas, "Холст"), (ArtAssetCenterMode.Gallery, "Галерея"), (ArtAssetCenterMode.Card, "Карточка") })
            {
                ToolbarToggle toggle = new ToolbarToggle { text = title, value = state.Mode == mode };
                toggle.RegisterValueChangedCallback(evt => SetMode(mode));
                modeBar.Add(toggle);
            }
            BuildCanvasBar();
        }

        // Показать режим (для снимков окна и переходов из других окон).
        public void ShowMode(ArtAssetCenterMode mode, ArtAssetCardDisplay display = ArtAssetCardDisplay.Color)
        {
            state.CardDisplay = display;
            litDirty = true;
            SetMode(mode);
            if (mode == ArtAssetCenterMode.Canvas) FrameAll();
        }

        private void SetMode(ArtAssetCenterMode mode)
        {
            state.Mode = mode;
            ScheduleStateSave();
            BuildModeBar();
            BuildProperties();
            center?.MarkDirtyRepaint();
        }

        private void BuildCanvasBar()
        {
            canvasBar.Clear();
            void Button(string title, Action action, string tip = null) => canvasBar.Add(new Button(action) { text = title, tooltip = tip });
            switch (state.Mode)
            {
                case ArtAssetCenterMode.Canvas:
                    PopupField<string> scale = new PopupField<string>(new List<string> { "Игровой размер", "Уместить в ячейки" }, (int)state.Scale)
                    { tooltip = "Игровой размер — сравнение с человеком; ячейки — обзор" };
                    scale.RegisterValueChangedCallback(evt => { state.Scale = (ArtAssetCanvasScale)scale.index; ScheduleStateSave(); FrameAll(); });
                    canvasBar.Add(scale);
                    AddBackgroundChoice();
                    Button("Показать все", FrameAll);
                    Button("К выбранному", FrameSelected);
                    Button("Разложить автоматически", () => { state.Order.Clear(); ScheduleStateSave(); center.MarkDirtyRepaint(); },
                        "Сбросить свою раскладку холста; игровые данные не меняются");
                    hint.text = "Колесо — масштаб · ПКМ или средняя кнопка — панорама · ЛКМ — выбор · перетащить объект — поменять порядок на холсте или перенести в Базу локаций · перетащить файлы — загрузить";
                    break;
                case ArtAssetCenterMode.Gallery:
                    AddBackgroundChoice();
                    Slider size = new Slider("Размер карточек", 80, 260) { value = state.CardSize };
                    size.style.width = 260;
                    size.RegisterValueChangedCallback(evt => { state.CardSize = evt.newValue; ScheduleStateSave(); center.MarkDirtyRepaint(); });
                    canvasBar.Add(size);
                    hint.text = "Колесо — прокрутка · ЛКМ — выбор · двойной клик — карточка · перетащить карточку — в Базу локаций · перетащить файлы — загрузить";
                    break;
                default:
                    foreach ((ArtAssetCardDisplay display, string title) in new[]
                             { (ArtAssetCardDisplay.Color, "Рисунок"), (ArtAssetCardDisplay.Normal, "Нормали"), (ArtAssetCardDisplay.Lit, "Под светом") })
                    {
                        ToolbarToggle toggle = new ToolbarToggle { text = title, value = state.CardDisplay == display };
                        toggle.RegisterValueChangedCallback(evt => { state.CardDisplay = display; ScheduleStateSave(); litDirty = true; BuildCanvasBar(); BuildProperties(); center.MarkDirtyRepaint(); });
                        canvasBar.Add(toggle);
                    }
                    ToolbarToggle all = new ToolbarToggle { text = "Все шесть рядом", value = state.AllViews };
                    all.RegisterValueChangedCallback(evt => { state.AllViews = evt.newValue; ScheduleStateSave(); center.MarkDirtyRepaint(); });
                    canvasBar.Add(all);
                    canvasBar.Add(new Label("  Инструмент:") { style = { unityTextAlign = TextAnchor.MiddleLeft } });
                    foreach ((CardTool tool, string title, string tip) in new[]
                             {
                                 (CardTool.Pivot, "Опора", "Клик по точке касания земли на рисунке"),
                                 (CardTool.Footprint, "Основание", "Протянуть прямоугольник занятой земли или перетащить его"),
                                 (CardTool.PartOffset, "Сдвиг части", "Перетащить выбранную часть (её смещение в этом ракурсе)"),
                                 (CardTool.Light, "Свет", "Перетащить контрольный источник (режим «Под светом»)")
                             })
                    {
                        ToolbarToggle toggle = new ToolbarToggle { text = title, tooltip = tip, value = cardTool == tool };
                        toggle.RegisterValueChangedCallback(evt => { cardTool = tool; BuildCanvasBar(); });
                        canvasBar.Add(toggle);
                    }
                    hint.text = "Ячейки ракурсов: перетащите PNG на левую половину — рисунок, на правую — нормаль; несколько файлов — разбор по именам · " +
                                "Колесо — масштаб предпросмотра · ПКМ — панорама";
                    break;
            }
        }

        private void AddBackgroundChoice()
        {
            PopupField<string> background = new PopupField<string>(new List<string> { "Светлый фон", "Тёмный фон", "Шахматный фон" }, (int)state.Background);
            background.RegisterValueChangedCallback(evt => { state.Background = (ArtAssetBackground)background.index; ScheduleStateSave(); center.MarkDirtyRepaint(); });
            canvasBar.Add(background);
        }

        // ------------------------------------------------------------------
        // Категории и фильтры
        // ------------------------------------------------------------------

        private void BuildCategories()
        {
            if (categories == null || catalog == null) return;
            categories.Clear();
            void Add(int index, string title, int count)
            {
                Button button = new Button(() => { state.Category = index; visibleDirty = true; ScheduleStateSave(); BuildCategories(); center.MarkDirtyRepaint(); })
                { text = title + " (" + count + ")" };
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                if (state.Category == index) { button.style.unityFontStyleAndWeight = FontStyle.Bold; button.style.color = new Color(.95f, .82f, .5f); }
                categories.Add(button);
            }
            Add(-1, "Все", catalog.assets.Count(item => item != null));
            for (int i = 0; i < ArtAssetLabels.Categories.Length; i++)
            {
                ArtAssetCategory category = ArtAssetLabels.Categories[i];
                Add(i, ArtAssetLabels.CategoryTitle(category), catalog.assets.Count(item => item != null && item.Category == category));
            }
        }

        private void BuildFilters()
        {
            filterPanel.Clear();
            void Filter(string title, Func<bool> get, Action<bool> set, string tip = null)
            {
                Toggle toggle = new Toggle(title) { value = get(), tooltip = tip };
                toggle.labelElement.style.minWidth = 150;
                toggle.RegisterValueChangedCallback(evt => { set(evt.newValue); visibleDirty = true; ScheduleStateSave(); center.MarkDirtyRepaint(); });
                filterPanel.Add(toggle);
            }
            Filter("Избранное", () => state.Favorites, value => state.Favorites = value);
            Filter("Неполные ракурсы", () => state.IncompleteViews, value => state.IncompleteViews = value);
            Filter("Без нормалей", () => state.MissingNormals, value => state.MissingNormals = value, "Есть ракурсы без карты нормалей");
            Filter("Используемые", () => state.Used, value => state.Used = value, "Стоят хотя бы в одном месте");
            Filter("Неиспользуемые", () => state.Unused, value => state.Unused = value);
            filterPanel.Add(new Button(() => { RefreshUsage(); visibleDirty = true; center.MarkDirtyRepaint(); }) { text = "Пересчитать использование" });
        }

        private void RefreshUsage() => usageCounts = ArtAssetUsages.Counts();

        private int UsageCount(string id) => usageCounts != null && usageCounts.TryGetValue(id, out int count) ? count : 0;

        private List<ArtAssetDefinition> Visible()
        {
            if (catalog == null) return visible;
            if (!visibleDirty && visibleRevision == catalog.Revision) return visible;
            visible.Clear();
            foreach (ArtAssetDefinition asset in catalog.assets)
            {
                if (asset == null || !asset.MatchesQuery(query)) continue;
                if (state.Category >= 0 && asset.Category != ArtAssetLabels.Categories[state.Category]) continue;
                if (state.Favorites && !asset.Favorite) continue;
                if (state.IncompleteViews && asset.ViewCount >= ArtAssetLabels.ViewCount) continue;
                if (state.MissingNormals && asset.NormalCount >= asset.ViewCount && asset.ViewCount > 0) continue;
                int used = UsageCount(asset.Id);
                if (state.Used && !state.Unused && used == 0) continue;
                if (state.Unused && !state.Used && used > 0) continue;
                visible.Add(asset);
            }
            // Порядок холста: своя раскладка, затем категория и название.
            Dictionary<string, int> custom = new Dictionary<string, int>();
            for (int i = 0; i < state.Order.Count; i++) custom[state.Order[i]] = i;
            visible.Sort((a, b) =>
            {
                bool ca = custom.TryGetValue(a.Id, out int ia), cb = custom.TryGetValue(b.Id, out int ib);
                if (ca && cb) return ia.CompareTo(ib);
                if (ca != cb) return ca ? -1 : 1;
                int category = Array.IndexOf(ArtAssetLabels.Categories, a.Category).CompareTo(Array.IndexOf(ArtAssetLabels.Categories, b.Category));
                return category != 0 ? category : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            visibleDirty = false;
            visibleRevision = catalog.Revision;
            layoutDirty = true;
            return visible;
        }

        // ------------------------------------------------------------------
        // Выбор и записи
        // ------------------------------------------------------------------

        public void SelectAsset(string id, bool openCard = false)
        {
            state.SelectedId = id ?? "";
            cardPart = 0;
            litDirty = true;
            ScheduleStateSave();
            if (openCard) { state.Mode = ArtAssetCenterMode.Card; BuildModeBar(); }
            BuildProperties();
            center?.MarkDirtyRepaint();
        }

        private void AddEmptyAsset()
        {
            ArtAssetDefinition asset = new ArtAssetDefinition { Name = "Новый ассет", Category = state.Category >= 0 ? ArtAssetLabels.Categories[state.Category] : ArtAssetCategory.None };
            Edit("Создать ассет", () => catalog.assets.Add(asset));
            visibleDirty = true;
            BuildCategories();
            SelectAsset(asset.Id, true);
            status.text = "Создан «" + asset.Name + "». Перетащите рисунки в ячейки ракурсов.";
        }

        private void DeleteAsset(ArtAssetDefinition asset)
        {
            List<ArtAssetUsage> usages = ArtAssetUsages.Find(asset.Id);
            if (usages.Count > 0)
            {
                string list = string.Join("\n", usages.Take(12).Select(item => "• " + item.Title)) + (usages.Count > 12 ? "\n… ещё " + (usages.Count - 12) : "");
                int choice = EditorUtility.DisplayDialogComplex("Ассет используется",
                    "«" + asset.Name + "» стоит в местах (" + usages.Count + "):\n" + list +
                    "\n\nУдалить нельзя, пока есть ссылки. Можно выбрать замену — экземпляры сохранят ID, опору и ракурс.",
                    "Выбрать замену…", "Отмена", "Показать первое");
                if (choice == 0)
                    ArtAssetPicker.Show("Замена для «" + asset.Name + "»", replacement =>
                    {
                        if (replacement == asset.Id) return;
                        int count = ArtAssetUsages.ReplaceEverywhere(asset.Id, replacement);
                        RefreshUsage();
                        status.text = "Заменено экземпляров: " + count + ". Теперь «" + asset.Name + "» можно удалить.";
                        if (ArtAssetUsages.Find(asset.Id).Count == 0) DeleteAsset(asset);
                    });
                else if (choice == 2) usages[0].Open?.Invoke();
                return;
            }
            if (!EditorUtility.DisplayDialog("Удалить ассет?", "«" + asset.Name + "» будет удалён из каталога.\nФайлы рисунков и нормалей останутся в проекте.", "Удалить", "Отмена"))
                return;
            if (!ArtAssetUsages.TryDelete(catalog, asset, out _)) { status.text = "Ассет используется — удаление отменено."; return; }
            saveAt = EditorApplication.timeSinceStartup + .3;
            state.Order.Remove(asset.Id);
            visibleDirty = true;
            BuildCategories();
            SelectAsset(null);
            status.text = "Удалено. Файлы не тронуты.";
        }

        private void ValidateAll()
        {
            RefreshUsage();
            int warnings = 0, errors = 0;
            List<string> lines = new List<string>();
            foreach (ArtAssetDefinition asset in catalog.assets.Where(item => item != null))
            {
                foreach (ArtAssetIssue issue in ArtAssetValidator.Validate(catalog, asset))
                {
                    if (issue.Level == ArtAssetIssueLevel.Info) continue;
                    if (issue.Level == ArtAssetIssueLevel.Error) errors++; else warnings++;
                    if (lines.Count < 30) lines.Add(asset.Name + ": " + issue);
                }
            }
            status.text = errors + warnings == 0 ? "Проверка пройдена: " + catalog.assets.Count + " записей." : "Ошибок: " + errors + ", предупреждений: " + warnings + ".";
            if (errors + warnings > 0) EditorUtility.DisplayDialog("Проверка Базы ассетов", string.Join("\n", lines), "Понятно");
            BuildProperties();
        }
    }
}
