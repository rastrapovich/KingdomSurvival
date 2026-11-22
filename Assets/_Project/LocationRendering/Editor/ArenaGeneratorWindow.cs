using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.MapLabBridge;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // 20.11.2026, просьба пользователя: «Генератор арен» вместо собранных
    // вручную полей боя Базы локаций. Слева — общие настройки генерации
    // обычной арены боя (клетки, деревья, трава, пни, камни, рельеф, река),
    // их запоминает игра (ArenaGeneratorAsset); вкладка «Обработка кадра» —
    // общая и своя у каждого вида карты. Справа — предпросмотр той же
    // сборкой, что в бою (доска, сетка, кадр арены MapLab), и галерея
    // вариаций: все темы на одном seed или одна тема на разных seed.
    public sealed partial class ArenaGeneratorWindow : EditorWindow
    {
        private const string PrefsKey = "KS.ArenaGenerator.";
        private const double RebuildDelay = .7;

        private ArenaGeneratorAsset asset;
        private SerializedObject serialized;
        private BattlefieldDatabaseAsset fields;

        private VisualElement left, previewHost, galleryHost;
        private Label status, report;
        private MapLabArenaDriver driver;
        private SandboxBattle previewBattle;
        private double rebuildAt = -1, saveAt = -1, ignoreUntil;
        private bool reportedReady, galleryAllThemes;

        private enum Mode { Generation, Frame }

        private static Mode CurrentMode
        {
            get => (Mode)EditorPrefs.GetInt(PrefsKey + "Mode", 0);
            set => EditorPrefs.SetInt(PrefsKey + "Mode", (int)value);
        }

        private static int Seed
        {
            get => EditorPrefs.GetInt(PrefsKey + "Seed", 1);
            set => EditorPrefs.SetInt(PrefsKey + "Seed", value);
        }

        // -2 — «Стандартная тема» (общие настройки, предпросмотр на обычной теме по seed),
        // -1 — тема по seed (правка идёт в стандартные), иначе номер темы.
        private static int ThemeIndex
        {
            get => EditorPrefs.GetInt(PrefsKey + "Theme", -2);
            set => EditorPrefs.SetInt(PrefsKey + "Theme", value);
        }

        private static int Fighters
        {
            get => EditorPrefs.GetInt(PrefsKey + "Fighters", 4);
            set => EditorPrefs.SetInt(PrefsKey + "Fighters", value);
        }

        private static int Enemies
        {
            get => EditorPrefs.GetInt(PrefsKey + "Enemies", 4);
            set => EditorPrefs.SetInt(PrefsKey + "Enemies", value);
        }

        [MenuItem("Kingdom Survival/Генератор арен")]
        public static void Open()
        {
            ArenaGeneratorWindow window = GetWindow<ArenaGeneratorWindow>();
            window.titleContent = new GUIContent("Генератор арен");
            window.minSize = new Vector2(980, 560);
            window.Show();
        }

        // Открыть на вкладке «Обработка кадра» (из окна «Обработка кадра»).
        public static void OpenFrame()
        {
            CurrentMode = Mode.Frame;
            Open();
            GetWindow<ArenaGeneratorWindow>().BuildLeft();
        }

        // Ассет настроек: нет — создаётся с прежними значениями арены.
        public static ArenaGeneratorAsset EnsureAsset()
        {
            ArenaGeneratorAsset existing = AssetDatabase.LoadAssetAtPath<ArenaGeneratorAsset>(ArenaGeneratorAsset.AssetPath);
            if (existing != null) return existing;
            string folder = System.IO.Path.GetDirectoryName(ArenaGeneratorAsset.AssetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/'), System.IO.Path.GetFileName(folder));
            ArenaGeneratorAsset created = CreateInstance<ArenaGeneratorAsset>();
            created.Repair();
            AssetDatabase.CreateAsset(created, ArenaGeneratorAsset.AssetPath);
            AssetDatabase.SaveAssets();
            return created;
        }

        // Для запуска без окна (batchmode): создать ассет настроек.
        public static void EnsureAssetInProject() => Debug.Log("Генератор арен: " + AssetDatabase.GetAssetPath(EnsureAsset()));

        private void OnEnable()
        {
            EditorApplication.update += Pump;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Undo.undoRedoPerformed += OnUndo;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Pump;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Undo.undoRedoPerformed -= OnUndo;
            ArenaGeneratorAsset.PreviewStandard = false;
            ReleasePreview();
            CancelGallery();
            if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
        }

        private void OnPlayMode(PlayModeStateChange change)
        {
            // Арена боя и предпросмотр — миры MapLab на одном слое: вместе нельзя.
            if (change == PlayModeStateChange.ExitingEditMode) ArenaGeneratorAsset.PreviewStandard = false;
            if (change == PlayModeStateChange.ExitingEditMode || change == PlayModeStateChange.ExitingPlayMode)
            {
                ReleasePreview();
                CancelGallery();
            }
            if (change == PlayModeStateChange.EnteredEditMode)
                RequestRebuild(.3);
        }

        private void OnUndo()
        {
            if (asset == null) return;
            asset.Activate();
            serialized?.Update();
            ArenaGeneratorAsset.Touch();
            BuildLeft();
            RequestRebuild();
        }

        public void CreateGUI()
        {
            asset = EnsureAsset();
            asset.Activate();
            serialized = new SerializedObject(asset);
            fields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);

            rootVisualElement.Clear();
            rootVisualElement.style.backgroundColor = new Color(.12f, .135f, .14f);
            Toolbar toolbar = new Toolbar();
            foreach ((Mode mode, string title) in new[] { (Mode.Generation, "Генерация"), (Mode.Frame, "Обработка кадра") })
            {
                Mode captured = mode;
                ToolbarToggle toggle = new ToolbarToggle { text = title, value = CurrentMode == mode };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (!evt.newValue) { toggle.SetValueWithoutNotify(CurrentMode == captured); return; }
                    CurrentMode = captured;
                    foreach (ToolbarToggle other in toolbar.Query<ToolbarToggle>().ToList()) other.SetValueWithoutNotify(other == toggle);
                    BuildLeft();
                });
                toolbar.Add(toggle);
            }
            toolbar.Add(new ToolbarSpacer { flex = true });
            toolbar.Add(new ToolbarButton(() => RequestRebuild(0)) { text = "Пересобрать", tooltip = "Собрать арену заново с нынешними настройками" });
            toolbar.Add(new ToolbarButton(() => FrameStyleWindow.Open(null)) { text = "Обработка кадра мест ↗" });
            toolbar.Add(new ToolbarButton(() => EditorApplication.ExecuteMenuItem("Kingdom Survival/База локаций")) { text = "База локаций ↗" });
            rootVisualElement.Add(toolbar);

            TwoPaneSplitView split = new TwoPaneSplitView(0, 430, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            ScrollView leftScroll = new ScrollView(ScrollViewMode.Vertical);
            leftScroll.style.flexGrow = 1;
            left = new VisualElement();
            left.style.paddingLeft = left.style.paddingRight = 8;
            left.style.paddingTop = 6;
            left.style.paddingBottom = 20;
            leftScroll.Add(left);
            split.Add(leftScroll);

            VisualElement right = new VisualElement();
            right.style.flexGrow = 1;
            right.Add(BuildPreviewControls());
            previewHost = new VisualElement { name = "arena-preview" };
            previewHost.style.flexGrow = 1;
            previewHost.style.minHeight = 300;
            previewHost.style.marginLeft = previewHost.style.marginRight = 6;
            previewHost.style.backgroundColor = new Color(.035f, .043f, .05f);
            right.Add(previewHost);
            galleryHost = new VisualElement { name = "arena-gallery" };
            galleryHost.style.flexDirection = FlexDirection.Row;
            galleryHost.style.flexWrap = Wrap.Wrap;
            galleryHost.style.display = DisplayStyle.None;
            galleryHost.style.flexGrow = 1;
            right.Add(galleryHost);
            report = new Label();
            report.style.whiteSpace = WhiteSpace.Normal;
            report.style.marginLeft = report.style.marginRight = 8;
            report.style.marginTop = 4;
            report.style.color = new Color(.78f, .76f, .7f);
            right.Add(report);
            status = new Label();
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.marginLeft = 8;
            status.style.marginBottom = 4;
            status.style.color = new Color(.6f, .85f, .65f);
            right.Add(status);
            split.Add(right);
            rootVisualElement.Add(split);

            BuildLeft();
            RequestRebuild(.2);
        }

        // ------------------------------------------------------------------
        // Предпросмотр: та же сборка, что на экране боя
        // ------------------------------------------------------------------

        private VisualElement BuildPreviewControls()
        {
            VisualElement bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.flexWrap = Wrap.Wrap;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = bar.style.paddingRight = 6;
            bar.style.paddingTop = bar.style.paddingBottom = 4;

            List<string> themes = new List<string> { "Стандартная тема (общая)", "Тема по seed" };
            themes.AddRange(SandboxArenaThemes.All.Select(SandboxArenaThemes.Label));
            PopupField<string> theme = new PopupField<string>("Тема", themes, Mathf.Clamp(ThemeIndex + 2, 0, themes.Count - 1));
            theme.labelElement.style.minWidth = 40;
            theme.style.width = 260;
            theme.tooltip = "«Стандартная тема» — общие настройки, которые берут все темы без своих (предпросмотр на обычной теме по seed). «Тема по seed» — как подбирает игра; правки идут в стандартные. У каждой темы можно завести свои настройки.";
            theme.RegisterValueChangedCallback(_ => { ThemeIndex = theme.index - 2; BuildLeft(); RequestRebuild(0); });
            bar.Add(theme);

            IntegerField seed = new IntegerField("Seed") { value = Seed, isDelayed = true };
            seed.labelElement.style.minWidth = 36;
            seed.style.width = 140;
            seed.RegisterValueChangedCallback(evt => { Seed = evt.newValue; RequestRebuild(0); });
            bar.Add(seed);
            bar.Add(new Button(() => { Seed--; seed.SetValueWithoutNotify(Seed); RequestRebuild(0); }) { text = "◀", tooltip = "Предыдущий seed" });
            bar.Add(new Button(() => { Seed++; seed.SetValueWithoutNotify(Seed); RequestRebuild(0); }) { text = "▶", tooltip = "Следующий seed" });
            bar.Add(new Button(() => { Seed = UnityEngine.Random.Range(1, 100000); seed.SetValueWithoutNotify(Seed); RequestRebuild(0); }) { text = "Случайный" });

            List<(string Id, string Name)> views = MapLabGameViews.All();
            if (views.Count > 0)
            {
                int current = Mathf.Max(0, views.FindIndex(item => item.Id == MapLabGameViews.ActiveId));
                PopupField<string> view = new PopupField<string>("Вид", views.Select(item => item.Name).ToList(), current);
                view.labelElement.style.minWidth = 30;
                view.style.width = 170;
                view.tooltip = "Вид карты — тот же выбор, что в игре: рисунки вида и его обработка кадра.";
                view.RegisterValueChangedCallback(_ =>
                {
                    MapLabGameViews.Select(views[view.index].Id);
                    driver?.RequestRedraw();
                    if (CurrentMode == Mode.Frame) BuildLeft();
                });
                bar.Add(view);
            }

            SliderInt fighters = new SliderInt("Бойцы", 1, 6) { value = Fighters, showInputField = true };
            fighters.labelElement.style.minWidth = 44;
            fighters.style.width = 170;
            fighters.RegisterValueChangedCallback(evt => { Fighters = evt.newValue; RequestRebuild(); });
            bar.Add(fighters);
            SliderInt enemies = new SliderInt("Враги", 1, 8) { value = Enemies, showInputField = true };
            enemies.labelElement.style.minWidth = 40;
            enemies.style.width = 170;
            enemies.RegisterValueChangedCallback(evt => { Enemies = evt.newValue; RequestRebuild(); });
            bar.Add(enemies);

            bar.Add(new Button(() => StartGallery(true)) { text = "Галерея: все темы", tooltip = "Все темы на нынешнем seed — по одной сборке (несколько секунд каждая)" });
            bar.Add(new Button(() => StartGallery(false)) { text = "Галерея: 9 seed", tooltip = "Нынешняя тема на девяти seed подряд" });
            return bar;
        }

        private SandboxArenaTheme ThemeFor(int seed)
        {
            List<SandboxArenaTheme> all = SandboxArenaThemes.All.ToList();
            if (ThemeIndex >= 0 && ThemeIndex < all.Count) return all[ThemeIndex];
            SandboxArenaTheme[] ordinary = SandboxArenaThemes.Ordinary;
            return ordinary[((seed % ordinary.Length) + ordinary.Length) % ordinary.Length];
        }

        private BattlefieldDefinitionData Field => fields != null ? fields.GetSandboxBattlefield() : null;

        private SandboxBattle CreateBattle(SandboxArenaTheme theme, int seed)
        {
            // Бойцы отряда и случайные существа Базы существ (по seed), а не заглушки.
            // «Стандартная тема» — предпросмотр общих настроек; галерея всех тем — каждая со своими.
            ArenaGeneratorAsset.PreviewStandard = ThemeIndex == -2 && !galleryAllThemes;
            ArenaPreviewContent.Pick(seed, Mathf.Clamp(Fighters, 1, 6), Mathf.Clamp(Enemies, 1, 8),
                out List<SandboxUnitDefinition> ours, out List<SandboxUnitDefinition> theirs);
            asset.Activate();
            return SandboxRoster.CreateBattle(ours, theirs, seed, disabledCells: BattlefieldFrame.DisabledCells(Field),
                theme: theme, themeSource: "Генератор арен");
        }

        private void RequestRebuild(double delay = RebuildDelay)
        {
            rebuildAt = EditorApplication.timeSinceStartup + delay;
        }

        private void MarkChanged(bool rebuild)
        {
            if (asset == null) return;
            if (EditorApplication.timeSinceStartup < ignoreUntil) return;
            asset.Activate();
            EditorUtility.SetDirty(asset);
            ArenaGeneratorAsset.Touch();
            saveAt = EditorApplication.timeSinceStartup + 1;
            if (rebuild) RequestRebuild();
            else driver?.RequestRedraw();
        }

        private void Pump()
        {
            double now = EditorApplication.timeSinceStartup;
            if (saveAt > 0 && now >= saveAt)
            {
                saveAt = -1;
                if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
            }
            if (PumpGallery()) return;
            if (rebuildAt > 0 && now >= rebuildAt)
            {
                rebuildAt = -1;
                BuildPreview();
            }
            if (driver != null && !reportedReady && driver.Shown && driver.Arena != null)
            {
                reportedReady = true;
                MapLabArena arena = driver.Arena;
                status.text = "Арена собрана за " + (arena.BuildMs / 1000.0).ToString("0.0") + " с · элементов декора: " + arena.DecorFeatures +
                              (arena.UsesAssetDatabase ? " (рисунки Базы ассетов)" : " (прежний лес MapLab — в Базе ассетов меньше трёх деревьев для арены)") +
                              (arena.Spec.Frame != null ? " · обработка кадра: включена" : " · обработка кадра: выключена");
            }
        }

        private void BuildPreview()
        {
            if (previewHost == null || asset == null) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ReleasePreview();
                status.text = "Во время игры предпросмотр выключен (арена боя и предпросмотр — миры одного слоя). Настройки сохраняются и действуют в следующем бою; обработка кадра — сразу.";
                return;
            }
            CancelGallery();
            ReleasePreview();
            galleryAllThemes = false;
            galleryHost.style.display = DisplayStyle.None;
            previewHost.style.display = DisplayStyle.Flex;
            BattlefieldDefinitionData field = Field;
            if (field == null)
            {
                status.text = "Нет поля полигона в базе полей боя — сетке арены не на чем строиться.";
                return;
            }
            SandboxArenaTheme theme = ThemeFor(Seed);
            try
            {
                previewBattle = CreateBattle(theme, Seed);
            }
            catch (Exception exception)
            {
                status.text = "Бой не собран: " + exception.Message;
                return;
            }
            BattlefieldHexStyle style = fields.GetHexStyle(field);
            BattlefieldView surface = new BattlefieldView(true) { name = "arena-preview-surface" };
            surface.style.flexGrow = 1;
            surface.style.position = Position.Relative;
            surface.style.backgroundColor = new Color(.035f, .043f, .05f);
            surface.Show(field, style);
            HexBoardElement board = new HexBoardElement();
            board.style.position = Position.Absolute;
            board.style.left = board.style.right = board.style.top = board.style.bottom = 0;
            board.style.minWidth = board.style.minHeight = 0;
            board.style.backgroundColor = Color.clear;
            board.style.borderLeftWidth = board.style.borderRightWidth = board.style.borderTopWidth = board.style.borderBottomWidth = 0;
            board.SetBattlefield(field, style, true);
            ArenaPreviewContent.Apply(board);
            board.SetBattle(previewBattle, null);
            previewHost.Add(surface);
            driver = MapLabArenaDriver.TryCreate(field, surface, board, previewBattle, null, true);
            surface.Add(board);
            surface.LayoutChanged += board.MarkDirtyRepaint;
            reportedReady = false;
            report.text = previewBattle.ArenaReport != null ? previewBattle.ArenaReport.Summary() : SandboxArenaThemes.Label(theme);
            status.text = driver == null
                ? "Арена MapLab недоступна (MapLabGameAssets: «Арена в бою» выключена или нет профиля) — показана картинка поля."
                : "Собирается арена: " + SandboxArenaThemes.Label(theme) + ", seed " + Seed + "…";
        }

        // Для проверок: драйвер предпросмотра, строка состояния и отчёт.
        public MapLabArenaDriver PreviewDriver => driver;
        public string StatusText => status?.text ?? string.Empty;
        public string ReportText => report?.text ?? string.Empty;
        public bool GalleryRunning => thumbIndex >= 0;
        public int GalleryCount => thumbs.Count;
        public void StartGalleryForTest(bool allThemes) => StartGallery(allThemes);

        private void ReleasePreview()
        {
            // Снятие поля с панели освобождает арену (драйвер подписан на отсоединение).
            driver?.Dispose();
            driver = null;
            previewBattle = null;
            previewHost?.Clear();
        }
    }
}
