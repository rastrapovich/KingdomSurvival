using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // Рабочее окно «Генератор карты» (мастер-бриф перехода на MapLab, §§4–7):
    // три вкладки — Карта, Ассеты, Проверка. Слева настройки текущей вкладки,
    // в центре предпросмотр, справа свойства выбранного ассета. Все поля
    // пишут в существующие MapLabSettings / MapLabArtEntry (второй копии чисел
    // нет); структурные правки применяются отдельной командой, цвет и
    // внешний вид — сразу без новой карты. Undo — через Unity Undo.
    public sealed class MapLabGeneratorWindow : EditorWindow
    {
        private enum Tab { Map = 0, Assets = 1, Check = 2 }

        private static readonly string[] TabNames = { "Карта", "Ассеты", "Проверка" };

        private static readonly string[] Categories =
        {
            "Все", "Деревья и кусты", "Трава и камыш", "Камни", "Скалы", "Дома и постройки", "Реквизит", "Ограды", "Мосты", "Фигуры", "Пятна и поверхность", "Процедурные",
        };

        private static readonly string[] RegionNames = { "Холм и уступ", "Лесное озеро", "Речная долина" };
        private static readonly string[] SizeNames = { "Малый · 192", "Средний · 256", "Большой · 384", "Очень большой · 512 (проверочный)" };
        private static readonly string[] QualityNames = { "Быстро", "Стандарт", "Высокое" };
        private static readonly string[] DiscoveryNames = { "Редко", "Обычно", "Насыщенно" };
        private static readonly string[] VillageNames = { "Авто (от освоенности)", "Малая · 3–6 дворов", "Средняя · 7–12", "Крупная · 12–18, с переулками" };

        private Tab tab;
        private MapLabView view;
        private MapLabSettings snapshotOf;
        private string snapshot;
        private float leftWidth = 330f, rightWidth = 300f;
        private Rect lastBody;
        private int dragging;
        private Vector2 leftScroll, gridScroll, rightScroll, checkScroll;
        private bool lookOpen = true, fineOpen, landingOpen, extraOpen;
        private RenderTexture preview;
        private bool previewDirty = true;
        private string status = "";
        private MapLabPlanReport planReport;
        private Vector2 diagramScroll;
        private bool objectsPending;

        // Ассеты
        private string search = "";
        private int category;
        private bool usedOnly;
        private readonly List<Card> cards = new List<Card>();
        private readonly HashSet<MapLabArtEntry> selected = new HashSet<MapLabArtEntry>();
        private Card focus;
        private int focusView;
        private float groupScale = 1f, groupBrightness = 1f, groupHue, groupSaturation = 1f;
        private bool groundBackground;

        private sealed class Card
        {
            public string Key, Title, Category, Role;
            public readonly List<(MapLabArtSet Set, MapLabArtEntry Entry)> Views = new List<(MapLabArtSet, MapLabArtEntry)>();
        }

        // Единственный пункт меню генератора карты. Всё служебное — пакеты
        // арта, калибровка, подробное окно MapLab, правила генератора игры,
        // подготовка рисунка для игры — в кнопке «Сервис» этого окна.
        [MenuItem("Kingdom Survival/Генератор карты", priority = 300)]
        public static void Open()
        {
            // Сцена генератора открывается сама (с вопросом о несохранённой сцене).
            if (MapLabAssets.FindView() == null && System.IO.File.Exists(MapLabAssets.ScenePath) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(MapLabAssets.ScenePath, OpenSceneMode.Single);
            MapLabGeneratorWindow window = GetWindow<MapLabGeneratorWindow>("Генератор карты");
            window.minSize = new Vector2(760f, 460f);
            window.Show();
        }

        // Окна других сборок (генератор игры, подготовка рисунка для игры) —
        // по имени типа: прямой ссылки между сборками нет.
        private static void RunExternal(string typeName, string assembly, string method)
        {
            Type type = Type.GetType(typeName + ", " + assembly);
            System.Reflection.MethodInfo info = type?.GetMethod(method, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (info == null)
            {
                EditorUtility.DisplayDialog("Генератор карты", "Не найдено: " + typeName + "." + method, "OK");
                return;
            }
            info.Invoke(null, null);
        }

        private void ShowServiceMenu(Rect at)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Подключить арт/Этап 02 (основа)…"), false, () => MapLabArtImport.ImportMenu());
            menu.AddItem(new GUIContent("Подключить арт/Этап 03 (деревья)…"), false, () => MapLabArtImport.ImportStage03Menu());
            menu.AddItem(new GUIContent("Подключить арт/Этап 04 (герой)…"), false, () => MapLabArtImport.ImportStage04Menu());
            menu.AddItem(new GUIContent("Подключить арт/Этап 05 (дома, трава, мосты)…"), false, () => MapLabArtImport.ImportStage05Menu());
            menu.AddItem(new GUIContent("Подключить арт/Скалы 06…"), false, () => MapLabArtImport.ImportCliffs06Menu());
            menu.AddItem(new GUIContent("Подключить арт/Выбранные места и фракции…"), false, () => MapLabPoiImport.ImportMenu());
            menu.AddItem(new GUIContent("Переподключить последние пакеты/Этап 02"), false, () => MapLabArtImport.ReimportMenu());
            menu.AddItem(new GUIContent("Переподключить последние пакеты/Этап 03"), false, () => MapLabArtImport.ReimportStage03Menu());
            menu.AddItem(new GUIContent("Переподключить последние пакеты/Этап 04"), false, () => MapLabArtImport.ReimportStage04Menu());
            menu.AddItem(new GUIContent("Переподключить последние пакеты/Этап 05"), false, () => MapLabArtImport.ReimportStage05Menu());
            menu.AddItem(new GUIContent("Переподключить последние пакеты/Скалы 06"), false, () => MapLabArtImport.ReimportCliffs06Menu());
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Калибровка арта (опоры, высоты)"), false, () => MapLabArtCalibrationWindow.Open());
            menu.AddItem(new GUIContent("Подробные настройки MapLab (все поля профиля)"), false, () => MapLabWindow.Open());
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Правила генератора игры (места, детали, фракции)"), false,
                () => RunExternal("KingdomSurvival.WorldMapVisual.Editor.WorldMapGeneratorWindow", "KingdomSurvival.WorldMapVisual.Editor", "Open"));
            menu.AddItem(new GUIContent("Тушь прежнего рисунка карты"), false,
                () => RunExternal("KingdomSurvival.WorldMapVisual.Editor.WorldMapInkEditorWindow", "KingdomSurvival.WorldMapVisual.Editor", "Open"));
            menu.AddItem(new GUIContent("Подготовить рисунок карты для игры"), false,
                () => RunExternal("KingdomSurvival.MapLabBridge.Editor.MapLabGameSetup", "KingdomSurvival.MapLabBridge.Editor", "PrepareMenu"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Пересоздать сцену генератора"), false, () => Guard(() => { MapLabAssets.CreateTestSceneMenu(); view = MapLabAssets.FindView(); }));
            menu.DropDown(at);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            if (preview != null)
            {
                preview.Release();
                DestroyImmediate(preview);
                preview = null;
            }
            EditorUtility.ClearProgressBar();
        }

        private void OnUndoRedo()
        {
            if (view != null)
            {
                view.ApplyMaterialSettings();
                previewDirty = true;
            }
            Repaint();
        }

        private void OnGUI()
        {
            if (view == null)
                view = MapLabAssets.FindView();
            if (view == null)
            {
                EditorGUILayout.Space(8);
                bool exists = System.IO.File.Exists(MapLabAssets.ScenePath);
                EditorGUILayout.HelpBox(exists ? "Генератор работает в своей сцене. Откройте её (текущая сцена закроется, несохранённое будет предложено сохранить)." : "Сцены генератора ещё нет — создайте её.", MessageType.Info);
                if (exists && GUILayout.Button("Открыть сцену генератора", GUILayout.Height(28)) && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    Guard(() => { EditorSceneManager.OpenScene(MapLabAssets.ScenePath, OpenSceneMode.Single); view = MapLabAssets.FindView(); });
                if (GUILayout.Button(exists ? "Пересоздать сцену генератора" : "Создать сцену генератора", GUILayout.Height(24)))
                    Guard(() => { MapLabAssets.CreateTestSceneMenu(); view = MapLabAssets.FindView(); });
                return;
            }
            DrawTopBar();
            if (view.Settings == null)
            {
                EditorGUILayout.HelpBox("Выберите профиль (MapLabSettings).", MessageType.Warning);
                return;
            }
            if (snapshotOf != view.Settings)
                TakeSnapshot();
            MapLabSettings s = view.Settings;
            if (!s.Author.Captured)
            {
                Undo.RecordObject(s, "Генератор карты: база профиля");
                MapLabAuthor.Capture(s);
                EditorUtility.SetDirty(s);
            }

            tab = (Tab)GUILayout.Toolbar((int)tab, TabNames, GUILayout.Height(24));
            Rect body = GUILayoutUtility.GetRect(10f, 10000f, 10f, 10000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            // В фазе разметки GetRect даёт пустой прямоугольник: левая панель
            // получала ширину меньше пикселя и её поля раскладывались в
            // невидимую область. Берём прямоугольник последней отрисовки.
            if (Event.current.type == EventType.Layout)
                body = lastBody.width > 10f ? lastBody : new Rect(0f, 52f, position.width, Mathf.Max(100f, position.height - 72f));
            else if (Event.current.type == EventType.Repaint)
                lastBody = body;
            bool right = tab == Tab.Assets;
            float lw = Mathf.Clamp(leftWidth, 240f, body.width * 0.45f);
            float rw = right ? Mathf.Clamp(rightWidth, 240f, body.width * 0.4f) : 0f;
            Rect left = new Rect(body.x, body.y, lw, body.height);
            Rect splitL = new Rect(left.xMax, body.y, 4f, body.height);
            Rect rightRect = new Rect(body.xMax - rw, body.y, rw, body.height);
            Rect splitR = new Rect(rightRect.x - 4f, body.y, 4f, body.height);
            Rect center = new Rect(splitL.xMax, body.y, (right ? splitR.x : body.xMax) - splitL.xMax, body.height);
            Splitter(splitL, 1);
            if (right)
                Splitter(splitR, 2);

            GUILayout.BeginArea(left);
            switch (tab)
            {
                case Tab.Map: DrawMapSettings(s); break;
                case Tab.Assets: DrawAssetFilters(); break;
                case Tab.Check: DrawCheck(s); break;
            }
            GUILayout.EndArea();

            if (tab == Tab.Assets)
                DrawAssetGrid(center);
            else
                DrawPreview(center);

            if (right)
            {
                GUILayout.BeginArea(rightRect);
                DrawAssetProperties();
                GUILayout.EndArea();
            }

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
        }

        // ---------- Верхняя строка: профиль ----------

        private void DrawTopBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                MapLabSettings next = (MapLabSettings)EditorGUILayout.ObjectField(view.Settings, typeof(MapLabSettings), false, GUILayout.Width(240f));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(view, "Генератор карты: профиль");
                    view.Settings = next;
                    snapshotOf = null;
                }
                bool dirty = view.Settings != null && EditorUtility.IsDirty(view.Settings);
                GUILayout.Label(dirty ? "● есть несохранённые изменения" : "сохранено", dirty ? EditorStyles.boldLabel : EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                GUIContent service = new GUIContent("Сервис ▾", "Подключить пакеты арта, калибровка, подробные настройки, правила генератора игры, подготовка рисунка для игры.");
                Rect serviceRect = GUILayoutUtility.GetRect(service, EditorStyles.toolbarDropDown);
                if (EditorGUI.DropdownButton(serviceRect, service, FocusType.Passive, EditorStyles.toolbarDropDown))
                    ShowServiceMenu(serviceRect);
                using (new EditorGUI.DisabledScope(view.Settings == null))
                {
                    if (GUILayout.Button("Сохранить", EditorStyles.toolbarButton))
                        Save();
                    if (GUILayout.Button("Сохранить как…", EditorStyles.toolbarButton))
                        SaveAs();
                    if (GUILayout.Button(new GUIContent("Сбросить изменения", "Вернуть профиль к последнему сохранённому виду (Undo доступен)."), EditorStyles.toolbarButton))
                        ResetChanges();
                }
            }
        }

        private void TakeSnapshot()
        {
            snapshotOf = view.Settings;
            snapshot = view.Settings != null ? EditorJsonUtility.ToJson(view.Settings) : null;
        }

        private void Save()
        {
            Save(view.Settings);
            foreach (MapLabArtSet set in ActiveSets())
                if (EditorUtility.IsDirty(set))
                    Save(set);
            TakeSnapshot();
            status = "Профиль и каталог сохранены.";
        }

        private static void Save(UnityEngine.Object asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        private void SaveAs()
        {
            string path = EditorUtility.SaveFilePanelInProject("Сохранить профиль как", view.Settings.name + " копия", "asset", "Новый профиль генератора карты");
            if (string.IsNullOrEmpty(path))
                return;
            MapLabSettings copy = Instantiate(view.Settings);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            Undo.RecordObject(view, "Генератор карты: профиль");
            view.Settings = copy;
            TakeSnapshot();
            status = "Сохранено как " + path;
        }

        private void ResetChanges()
        {
            if (string.IsNullOrEmpty(snapshot))
                return;
            Undo.RecordObject(view.Settings, "Генератор карты: сброс изменений");
            EditorJsonUtility.FromJsonOverwrite(snapshot, view.Settings);
            EditorUtility.ClearDirty(view.Settings);
            view.ApplyMaterialSettings();
            previewDirty = true;
            status = "Изменения профиля сброшены. Для новой карты — «Сгенерировать предпросмотр».";
        }

        // ---------- Вкладка «Карта» ----------

        private void DrawMapSettings(MapLabSettings s)
        {
            MapLabAuthorProfile a = s.Author;
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            EditorGUILayout.LabelField("Основные настройки", EditorStyles.boldLabel);
            bool structure = false, look = false;

            bool planMode = a.UseGamePlan && MapLabPlanSource.Available;
            EditorGUI.BeginChangeCheck();
            bool usePlan = EditorGUILayout.ToggleLeft(new GUIContent("Регион из генератора игры", "Структуру строит генератор свободной игры (районы, деревни, места, маршруты, проверки); MapLab рисует тот же план. Выключено — три пресета MapLab для предпросмотра рисунка."), a.UseGamePlan);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(s, "Генератор карты: источник региона");
                a.UseGamePlan = usePlan;
                structure = true;
                planMode = a.UseGamePlan && MapLabPlanSource.Available;
            }
            if (a.UseGamePlan && !MapLabPlanSource.Available)
                EditorGUILayout.HelpBox("Мост MapLabBridge не загружен — используются пресеты MapLab.", MessageType.Warning);
            EditorGUI.BeginChangeCheck();
            int planRegion = a.PlanRegion;
            int region = (int)s.General.Preset;
            if (planMode)
                planRegion = EditorGUILayout.Popup(new GUIContent("Тип региона", "По seed — тип выбирается по seed. Поправки рельефа, воды, леса и освоенности с разбросом: один тип даёт разные схемы."), a.PlanRegion, MapLabPlanSource.RegionNames);
            else
                region = EditorGUILayout.Popup(new GUIContent("Тип региона", "Композиционное семейство: холм и уступ, лесное озеро, речная долина."), (int)s.General.Preset, RegionNames);
            using (new EditorGUILayout.HorizontalScope())
            {
                int seed = EditorGUILayout.IntField(new GUIContent("Seed", "Ключ воспроизведения; меняется только явно."), s.General.Seed);
                if (GUILayout.Button(new GUIContent("Случайный", "Новый seed"), GUILayout.Width(74f)))
                    seed = UnityEngine.Random.Range(1, 999999);
                if (GUILayout.Button(new GUIContent("Ключ", "Скопировать ключ карты (тип, seed, размер)"), GUILayout.Width(44f)))
                    EditorGUIUtility.systemCopyBuffer = MapKey(s);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(s, "Генератор карты: основные");
                    s.General.Preset = (MapLabPresetKind)region;
                    a.PlanRegion = planRegion;
                    s.General.Seed = seed;
                    structure = true;
                }
            }
            EditorGUI.BeginChangeCheck();
            a.WorldSize = (MapLabWorldSize)EditorGUILayout.Popup(new GUIContent("Размер мира", "Сторона мира в условных единицах. Больше 512 — после подгрузки участков (этап C)."), (int)a.WorldSize, SizeNames);
            if (!MapLabAuthor.Verified(a.WorldSize))
                EditorGUILayout.HelpBox("Проверочный размер: полная карта держится в памяти целиком, для игры не проверен.", MessageType.Warning);
            a.Relief = Slider("Выраженность рельефа", "Спокойный ← → пересечённый: высоты, холмы и уступы.", a.Relief);
            a.Water = Slider("Количество воды", "Мало ← → много: охват озёр, уровень и ширина реки.", a.Water);
            a.Forest = Slider("Лесистость", "Открытая земля ← → преимущественно лес; подходы и просветы сохраняются.", a.Forest);
            a.Settlement = Slider("Освоенность", "Дикая ← → обжитая земля: поля, их охват и размер деревни.", a.Settlement);
            using (new EditorGUI.DisabledScope(!planMode))
            {
                a.Discoveries = (MapLabDiscoveryRate)EditorGUILayout.Popup(new GUIContent("Насыщенность открытиями", "Бюджет мест относительно площади: редко ×0,6, обычно ×1, насыщенно ×1,5. Места с содержанием ставятся всегда. Только в режиме плана игры."), (int)a.Discoveries, DiscoveryNames);
                a.Connectivity = Slider("Связанность маршрутов", "Влево — больше хозяйственных троп-ответвлений; вправо — больше петель между деревнями и обходов через места. Только в режиме плана игры.", a.Connectivity);
            }
            a.Quality = (MapLabDisplayQuality)EditorGUILayout.Popup(new GUIContent("Качество отображения", "Плотность рисунка и сетки; игровую структуру не меняет."), (int)a.Quality, QualityNames);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(s, "Генератор карты: основные");
                structure = true;
            }

            EditorGUILayout.Space(6);
            lookOpen = EditorGUILayout.BeginFoldoutHeaderGroup(lookOpen, "Внешний вид · применяется сразу, мир не меняет");
            if (lookOpen)
            {
                EditorGUI.BeginChangeCheck();
                MapLabPalette palette = (MapLabPalette)EditorGUILayout.ObjectField(new GUIContent("Палитра", "Палитра среды."), s.PainterlyStyle.Palette, typeof(MapLabPalette), false);
                float saturation = EditorGUILayout.Slider(new GUIContent("Насыщенность", "Общая насыщенность карты (отдельно от коррекции ассета)."), s.PainterlyStyle.Saturation, 0.5f, 1.5f);
                a.StrokeStrength = Slider("Сила мазков", "Заметность живописной обработки поверхности.", a.StrokeStrength);
                a.StrokeSize = Slider("Размер мазков", "Масштаб мазков в мире.", a.StrokeSize);
                a.InkWeight = Slider("Выраженность туши", "Общий вес согласованного профиля линий.", a.InkWeight);
                a.ShadowStrength = Slider("Сила теней", "Падающие и контактные тени объектов.", a.ShadowStrength);
                a.Details = Slider("Мелкие детали", "Трава, камыш, мелкие камни, сено.", a.Details);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(s, "Генератор карты: внешний вид");
                    s.PainterlyStyle.Palette = palette;
                    s.PainterlyStyle.Saturation = saturation;
                    look = true;
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            fineOpen = EditorGUILayout.BeginFoldoutHeaderGroup(fineOpen, "Точная настройка · переопределения");
            if (fineOpen)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.LabelField("Реки и пути", EditorStyles.miniBoldLabel);
                Override("Ширина реки", ref a.RiverWidthSet, ref a.RiverWidth, 3f, 30f, s.Water.RiverWidth);
                Override("Ширина основной дороги", ref a.RoadWidthSet, ref a.RoadWidth, 1f, 8f, s.RoadsAndFields.RoadWidth);
                EditorGUILayout.LabelField("Растительность", EditorStyles.miniBoldLabel);
                Override("Размер деревьев", ref a.TreeSizeSet, ref a.TreeSize, 1.5f, 10f, s.Vegetation.TreeSize);
                Override("Доля хвойных", ref a.ConiferShareSet, ref a.ConiferShare, 0f, 1f, s.Vegetation.ConiferShare);
                EditorGUILayout.LabelField("Деревня", EditorStyles.miniBoldLabel);
                a.VillageSize = (MapLabVillageSize)EditorGUILayout.Popup(new GUIContent("Размер деревни", "Диапазон дворов; авто — от освоенности."), (int)a.VillageSize, VillageNames);
                Override("Доля ограждённых дворов", ref a.FencedShareSet, ref a.FencedShare, 0f, 0.5f, s.Stage05.FencedYardShare);
                EditorGUILayout.LabelField("Поверхность", EditorStyles.miniBoldLabel);
                Override("Плотность полей", ref a.FieldDensitySet, ref a.FieldDensity, 0f, 1f, s.RoadsAndFields.FieldDensity);
                Override("Высота уступов", ref a.CliffHeightSet, ref a.CliffHeight, 0f, 20f, s.Cliffs.CliffHeight);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(s, "Генератор карты: точная настройка");
                    structure = true;
                }
                if (GUILayout.Button(new GUIContent("Сделать текущие значения базой", "Снять снимок рабочих значений как нейтральное положение регуляторов.")))
                {
                    Undo.RecordObject(s, "Генератор карты: база профиля");
                    MapLabAuthor.Capture(s);
                    a.Relief = a.Water = a.Forest = a.Settlement = 0.5f;
                    a.StrokeStrength = a.StrokeSize = a.InkWeight = a.ShadowStrength = a.Details = 0.5f;
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            if (structure)
            {
                MapLabAuthor.ApplyStructure(s);
                EditorUtility.SetDirty(s);
                status = "Структурные настройки изменены — нажмите «Сгенерировать предпросмотр».";
            }
            if (look)
            {
                MapLabAuthor.ApplyLook(s);
                EditorUtility.SetDirty(s);
                view.ApplyMaterialSettings();
                previewDirty = true;
                status = "Внешний вид: цвет применён; мазки, тушь и детали — «Обновить внешний вид».";
            }

            EditorGUILayout.Space(8);
            if (GUILayout.Button(new GUIContent("Сгенерировать предпросмотр", "Новая карта по показанным настройкам: рельеф, вода, дороги, деревня, объекты. Текущая партия игры не меняется."), GUILayout.Height(28)))
                Generate();
            if (GUILayout.Button(new GUIContent("Обновить внешний вид", "Перекрасить поверхность, перо и тени без новой карты.")))
                Guard(() =>
                {
                    MapLabAuthor.ApplyLook(s);
                    view.RebuildStage03Layers();
                    previewDirty = true;
                    status = "Внешний вид обновлён.";
                });
            using (new EditorGUI.DisabledScope(MapLabPlanSource.ApplyToGame == null))
            {
                if (GUILayout.Button(new GUIContent("Применить к новой партии", "Записать эти настройки в генератор игры и профиль рисунка игры: новая партия (меню игры → Новая игра) будет такой же картой по виду и устройству; seed у партии свой. Текущая партия не меняется."), GUILayout.Height(26)))
                    Guard(() =>
                    {
                        MapLabAuthor.ApplyStructure(s);
                        MapLabAuthor.ApplyLook(s);
                        status = MapLabPlanSource.ApplyToGame(s);
                    });
            }
            if (GUILayout.Button(new GUIContent("Запустить тест на этой карте", "Play Mode в сцене генератора с этой картой и героем (без основной игры).")))
                EditorApplication.isPlaying = true;
            EditorGUILayout.HelpBox("Ключ карты: " + MapKey(s), MessageType.None);
            if (MapLabPlanSource.DrawGameSettings != null)
            {
                EditorGUILayout.Space(8);
                MapLabPlanSource.DrawGameSettings();
            }
            EditorGUILayout.EndScrollView();
        }

        private static float Slider(string label, string tip, float value)
        {
            return EditorGUILayout.Slider(new GUIContent(label, tip), value, 0f, 1f);
        }

        private static void Override(string label, ref bool set, ref float value, float min, float max, float effective)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                set = EditorGUILayout.ToggleLeft(new GUIContent(label, set ? "Переопределено вручную" : "Значение профиля: " + effective.ToString("0.##")), set, GUILayout.Width(170f));
                using (new EditorGUI.DisabledScope(!set))
                    value = EditorGUILayout.Slider(set ? value : effective, min, max);
            }
        }

        private static string MapKey(MapLabSettings s)
        {
            if (s.Author.UseGamePlan && MapLabPlanSource.Available)
                return $"plan:{MapLabPlanSource.RegionNames[Mathf.Clamp(s.Author.PlanRegion, 0, MapLabPlanSource.RegionNames.Length - 1)]}:{s.General.Seed}:{s.Author.WorldSize}:o{(int)s.Author.Discoveries}:c{s.Author.Connectivity:0.##}:a{MapLabData.AlgorithmVersion}";
            return $"{s.General.Preset}:{s.General.Seed}:{s.General.MapSize:0}:a{MapLabData.AlgorithmVersion}";
        }

        private void Generate()
        {
            MapLabSettings s = view.Settings;
            Guard(() =>
            {
                MapLabAuthor.ApplyStructure(s);
                MapLabAuthor.ApplyLook(s);
                DateTime start = DateTime.Now;
                if (s.Author.UseGamePlan && MapLabPlanSource.Available)
                {
                    if (planReport?.Diagram != null)
                        DestroyImmediate(planReport.Diagram);
                    planReport = MapLabPlanSource.Build(s);
                }
                else
                {
                    s.RegionPlan = null;
                    planReport = null;
                }
                bool ok = view.Generate((stage, fraction) => EditorUtility.DisplayCancelableProgressBar("Генератор карты", stage, fraction));
                if (!ok)
                    throw new Exception("генерация не выполнена (проверьте профиль и шейдеры)");
                if (view.CameraRig != null)
                    view.CameraRig.Fit();
                objectsPending = false;
                previewDirty = true;
                status = $"Карта готова: {(DateTime.Now - start).TotalSeconds:0.0} с · {MapKey(s)}";
            });
        }

        // ---------- Предпросмотр ----------

        private void DrawPreview(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
            MapLabCameraRig rig = view.CameraRig;
            if (rig == null || view.Data == null)
            {
                GUI.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, 40f), "Карта ещё не построена — «Сгенерировать предпросмотр».", EditorStyles.whiteLabel);
                return;
            }
            int w = Mathf.Max(64, Mathf.RoundToInt(rect.width)), h = Mathf.Max(64, Mathf.RoundToInt(rect.height - 22f));
            if (preview == null || preview.width != w || preview.height != h)
            {
                if (preview != null)
                {
                    preview.Release();
                    DestroyImmediate(preview);
                }
                preview = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "Генератор карты · предпросмотр", antiAliasing = 1 };
                previewDirty = true;
            }
            Rect image = new Rect(rect.x, rect.y, w, h);
            Event e = Event.current;
            if (image.Contains(e.mousePosition))
            {
                if (e.type == EventType.ScrollWheel)
                {
                    Vector2 vp = new Vector2((e.mousePosition.x - image.x) / image.width, 1f - (e.mousePosition.y - image.y) / image.height);
                    rig.ZoomAround(rig.Zoom * (e.delta.y > 0f ? 1f / 1.15f : 1.15f), vp, false);
                    previewDirty = true;
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2 || e.button == 0))
                {
                    Camera cam = rig.Camera;
                    float unitsPerPixel = cam != null && cam.orthographic ? cam.orthographicSize * 2f / image.height : 0.5f;
                    Vector3 right = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;
                    Vector3 forward = cam != null ? Vector3.ProjectOnPlane(cam.transform.up, Vector3.up).normalized : Vector3.forward;
                    rig.Target -= (right * e.delta.x - forward * e.delta.y) * unitsPerPixel;
                    rig.ClampTarget();
                    rig.Apply();
                    previewDirty = true;
                    e.Use();
                }
            }
            if (previewDirty && e.type == EventType.Repaint)
            {
                Camera cam = rig.Camera;
                RenderTexture keep = cam.targetTexture;
                cam.targetTexture = preview;
                cam.Render();
                cam.targetTexture = keep;
                previewDirty = false;
            }
            GUI.DrawTexture(image, preview, ScaleMode.StretchToFill, false);
            Rect bar = new Rect(rect.x + 6f, image.yMax + 2f, rect.width - 12f, 18f);
            GUI.Label(new Rect(bar.x, bar.y, bar.width - 150f, 18f), $"Zoom {rig.Zoom:0.##} · наклон {rig.Pitch:0}° · колесо — приближение, перетаскивание — сдвиг", EditorStyles.miniLabel);
            if (GUI.Button(new Rect(bar.xMax - 140f, bar.y, 66f, 18f), "Вся карта", EditorStyles.miniButton))
            {
                rig.Fit();
                previewDirty = true;
            }
            if (GUI.Button(new Rect(bar.xMax - 70f, bar.y, 70f, 18f), "Обновить", EditorStyles.miniButton))
                previewDirty = true;
        }

        // ---------- Вкладка «Ассеты» ----------

        private IEnumerable<MapLabArtSet> ActiveSets()
        {
            HashSet<MapLabArtSet> sets = new HashSet<MapLabArtSet>();
            foreach (MapLabArtSet set in new[] { view.ObjectArtSet, view.ResolvedArtSet05, view.ResolvedArtSet06, view.ArtSet })
                if (set != null)
                    sets.Add(set);
            return sets;
        }

        private static string CategoryOf(MapLabArtEntry e)
        {
            switch (e.Category)
            {
                case MapLabArtCategory.Vegetation:
                    return e.Role == "grass_tuft" || e.Role == "grass_ink_tuft" || e.Role == "reed_clump" ? "Трава и камыш" : "Деревья и кусты";
                case MapLabArtCategory.Rock: return "Камни";
                case MapLabArtCategory.Cliff:
                case MapLabArtCategory.CliffModule: return "Скалы";
                case MapLabArtCategory.Building: return "Дома и постройки";
                case MapLabArtCategory.Prop: return e.Role == "pebble_cluster" ? "Камни" : "Реквизит";
                case MapLabArtCategory.FenceModule: return "Ограды";
                case MapLabArtCategory.BridgeModule: return "Мосты";
                case MapLabArtCategory.Figure:
                case MapLabArtCategory.FigureCalibration: return "Фигуры";
                default: return "Пятна и поверхность";
            }
        }

        private void RebuildCards()
        {
            cards.Clear();
            Dictionary<string, Card> byKey = new Dictionary<string, Card>();
            foreach (MapLabArtSet set in ActiveSets())
            {
                foreach (MapLabArtEntry e in set.Entries)
                {
                    if (e == null)
                        continue;
                    // Кадры одного дома / героя — одна карточка с переключением вида.
                    string key = !string.IsNullOrEmpty(e.VariantGroup) ? set.name + "/" + e.VariantGroup : set.name + "/" + e.Id;
                    if (!byKey.TryGetValue(key, out Card card))
                    {
                        card = new Card
                        {
                            Key = key,
                            Title = !string.IsNullOrEmpty(e.VariantGroup) ? e.VariantGroup : e.Id,
                            Category = CategoryOf(e),
                            Role = e.Role,
                        };
                        byKey[key] = card;
                        cards.Add(card);
                    }
                    card.Views.Add((set, e));
                }
            }
            cards.Sort((x, y) => string.CompareOrdinal(x.Category + x.Title, y.Category + y.Title));
        }

        private bool Visible(Card c)
        {
            if (category == Categories.Length - 1)
                return false;
            if (category > 0 && c.Category != Categories[category])
                return false;
            if (usedOnly && !c.Views.Any(v => view.Usage.ContainsKey(v.Entry)))
                return false;
            if (string.IsNullOrEmpty(search))
                return true;
            string q = search.Trim().ToLowerInvariant();
            return c.Title.ToLowerInvariant().Contains(q) || (c.Role ?? "").ToLowerInvariant().Contains(q) || c.Category.ToLowerInvariant().Contains(q);
        }

        private void DrawAssetFilters()
        {
            if (cards.Count == 0)
                RebuildCards();
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);
            search = EditorGUILayout.TextField(new GUIContent("Поиск", "По имени, роли или категории"), search);
            usedOnly = EditorGUILayout.ToggleLeft("Только используемые на этой карте", usedOnly);
            EditorGUILayout.Space(4);
            for (int i = 0; i < Categories.Length; i++)
            {
                int count = i == Categories.Length - 1 ? 4 : cards.Count(c => i == 0 || c.Category == Categories[i]);
                if (GUILayout.Toggle(category == i, $"{Categories[i]}  ({count})", "Button"))
                    category = i;
            }
            EditorGUILayout.Space(6);
            if (GUILayout.Button(new GUIContent("Обновить список", "Перечитать подключённые наборы.")))
                RebuildCards();
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Групповая правка выбранных: " + selected.Count, EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(selected.Count == 0))
            {
                groupScale = EditorGUILayout.Slider(new GUIContent("Размер ×", "Множитель: различия размеров сохраняются."), groupScale, 0.5f, 2f);
                groupBrightness = EditorGUILayout.Slider(new GUIContent("Яркость ×"), groupBrightness, 0.5f, 1.5f);
                groupHue = EditorGUILayout.Slider(new GUIContent("Тон +°"), groupHue, -60f, 60f);
                groupSaturation = EditorGUILayout.Slider(new GUIContent("Насыщенность ×"), groupSaturation, 0.3f, 1.7f);
                if (GUILayout.Button("Применить к выбранным"))
                {
                    RecordSets("Генератор карты: групповая правка");
                    foreach (MapLabArtEntry e in selected)
                    {
                        e.SizeScale = Mathf.Clamp(e.SizeScale * groupScale, 0.25f, 4f);
                        e.Brightness = Mathf.Clamp(e.Brightness * groupBrightness, 0.3f, 2f);
                        e.HueShift = Mathf.Repeat(e.HueShift + groupHue + 180f, 360f) - 180f;
                        e.Saturation = Mathf.Clamp(e.Saturation * groupSaturation, 0f, 2f);
                    }
                    if (Mathf.Abs(groupScale - 1f) > 1e-4f)
                        objectsPending = true;
                    groupScale = groupBrightness = groupSaturation = 1f;
                    groupHue = 0f;
                    AfterAssetEdit();
                }
                if (GUILayout.Button("Снять выбор"))
                    selected.Clear();
            }
            if (objectsPending)
            {
                EditorGUILayout.HelpBox("Размер, вес или включение изменены: объекты на карте пересобираются явной командой.", MessageType.Info);
                if (GUILayout.Button("Пересобрать объекты карты"))
                    Guard(() =>
                    {
                        view.RebuildObjects((stage, fraction) => EditorUtility.DisplayCancelableProgressBar("Генератор карты", stage, fraction));
                        objectsPending = false;
                        previewDirty = true;
                        status = "Объекты пересобраны с новым выбором и размерами.";
                    });
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawAssetGrid(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.19f, 0.19f, 0.19f));
            if (category == Categories.Length - 1)
            {
                GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 10f, rect.width - 20f, rect.height - 20f));
                EditorGUILayout.HelpBox("Процедурные элементы рисуются кодом по профилю, у них нет PNG.", MessageType.None);
                ProceduralRow("Вода и берег", "Цвет воды, штрихи и перо берега — вкладка «Карта» → «Внешний вид» и профиль Stage05.");
                ProceduralRow("Дороги и тропы", "Ширина — «Точная настройка»; тушь — «Выраженность туши».");
                ProceduralRow("Поля и межи", "Плотность — «Освоенность» / «Плотность полей»; заливка — палитра.");
                ProceduralRow("Поверхность (луг, мазки)", "«Сила мазков», «Размер мазков», палитра.");
                GUILayout.EndArea();
                return;
            }
            List<Card> visible = cards.Where(Visible).ToList();
            const float cell = 118f;
            int columns = Mathf.Max(1, Mathf.FloorToInt((rect.width - 16f) / cell));
            int rows = Mathf.CeilToInt(visible.Count / (float)columns);
            Rect content = new Rect(0f, 0f, rect.width - 16f, rows * (cell + 18f) + 8f);
            gridScroll = GUI.BeginScrollView(rect, gridScroll, content);
            for (int i = 0; i < visible.Count; i++)
            {
                Card c = visible[i];
                Rect r = new Rect(4f + (i % columns) * cell, 4f + (i / columns) * (cell + 18f), cell - 8f, cell + 12f);
                MapLabArtEntry e = c.Views[0].Entry;
                bool isFocus = focus == c, isSelected = c.Views.Any(v => selected.Contains(v.Entry));
                EditorGUI.DrawRect(r, isFocus ? new Color(0.24f, 0.37f, 0.55f) : isSelected ? new Color(0.27f, 0.33f, 0.4f) : new Color(0.23f, 0.23f, 0.23f));
                if (e.Texture != null)
                    GUI.DrawTexture(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.width - 14f), e.Texture, ScaleMode.ScaleToFit, true);
                bool off = c.Views.All(v => !v.Entry.Enabled);
                bool missing = c.Views.Any(v => !v.Entry.HasTexture);
                int used = c.Views.Sum(v => view.Usage.TryGetValue(v.Entry, out int n) ? n : 0);
                string mark = missing ? "⚠ " : off ? "⦸ " : "";
                GUI.Label(new Rect(r.x + 2f, r.yMax - 28f, r.width - 4f, 14f), mark + c.Title, EditorStyles.miniLabel);
                GUI.Label(new Rect(r.x + 2f, r.yMax - 15f, r.width - 4f, 14f), (c.Views.Count > 1 ? c.Views.Count + " вида · " : "") + (used > 0 ? "на карте " + used : "не на карте"), EditorStyles.centeredGreyMiniLabel);
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    if (Event.current.control || Event.current.command)
                    {
                        foreach (var v in c.Views)
                            if (!selected.Remove(v.Entry))
                                selected.Add(v.Entry);
                    }
                    else
                    {
                        focus = c;
                        focusView = 0;
                    }
                    Event.current.Use();
                    Repaint();
                }
            }
            GUI.EndScrollView();
        }

        private static void ProceduralRow(string title, string where)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Процедурный элемент · " + where, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);
        }

        private void DrawAssetProperties()
        {
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll);
            if (focus == null)
            {
                EditorGUILayout.HelpBox("Выберите карточку. Ctrl+клик — добавить в групповую правку.", MessageType.None);
                EditorGUILayout.EndScrollView();
                return;
            }
            if (focus.Views.Count > 1)
                focusView = EditorGUILayout.Popup("Вид", Mathf.Clamp(focusView, 0, focus.Views.Count - 1), focus.Views.Select(v => string.IsNullOrEmpty(v.Entry.Direction) ? v.Entry.Id : v.Entry.Direction + " · " + v.Entry.Id).ToArray());
            focusView = Mathf.Clamp(focusView, 0, focus.Views.Count - 1);
            (MapLabArtSet set, MapLabArtEntry e) = focus.Views[focusView];
            EditorGUILayout.LabelField(e.Id, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"{CategoryOf(e)} · роль {(string.IsNullOrEmpty(e.Role) ? "—" : e.Role)} · набор {set.name}", EditorStyles.wordWrappedMiniLabel);
            groundBackground = EditorGUILayout.ToggleLeft("Фон — цвет земли", groundBackground);
            Rect pr = GUILayoutUtility.GetRect(200f, 200f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(pr, groundBackground ? new Color(0.62f, 0.62f, 0.33f) : new Color(0.5f, 0.5f, 0.5f));
            if (e.Texture != null)
                GUI.DrawTexture(pr, e.Texture, ScaleMode.ScaleToFit, true);

            EditorGUI.BeginChangeCheck();
            Texture2D texture = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Изображение", "«Заменить…»: выбрать другой PNG; ID, вес, цвет и калибровка сохраняются."), e.Texture, typeof(Texture2D), false);
            bool enabled = EditorGUILayout.Toggle(new GUIContent("Использовать в генерации", "Выключенный не выбирается в новых картах; файл и сохранённые объекты не трогаются."), e.Enabled);
            float weight = EditorGUILayout.Slider(new GUIContent("Частота появления", "Относительный вес среди совместимых вариантов."), e.Weight, 0f, 5f);
            float size = EditorGUILayout.Slider(new GUIContent("Размер", "Равномерный масштаб рисунка; основание и проходимость не меняются."), e.SizeScale, 0.25f, 4f);
            EditorGUILayout.LabelField("Итоговая высота в мире", (e.WorldHeight * size).ToString("0.00") + " ед.", EditorStyles.miniLabel);
            float brightness = EditorGUILayout.Slider(new GUIContent("Яркость", "1 — без изменений"), e.Brightness, 0.3f, 2f);
            float hue = EditorGUILayout.Slider(new GUIContent("Цветовой тон, °", "0 — без изменений; альфа не меняется"), e.HueShift, -180f, 180f);
            float saturation = EditorGUILayout.Slider(new GUIContent("Насыщенность", "1 — без изменений"), e.Saturation, 0f, 2f);
            if (EditorGUI.EndChangeCheck())
            {
                if (!enabled && e.Enabled && IsLastEnabled(set, e))
                    EditorUtility.DisplayDialog("Генератор карты", $"«{e.Id}» — последний включённый вариант роли «{e.Role}». Без него генерация этой роли останется без рисунка; верните вариант или назначьте замену.", "Понятно");
                Undo.RecordObject(set, "Генератор карты: ассет " + e.Id);
                bool structural = texture != e.Texture || enabled != e.Enabled || Mathf.Abs(weight - e.Weight) > 1e-5f || Mathf.Abs(size - e.SizeScale) > 1e-5f;
                if (texture != e.Texture && texture != null)
                    e.Notes = (e.Notes ?? "") + $" [замена {DateTime.Now:yyyy-MM-dd}: проверить опору]";
                e.Texture = texture;
                e.Enabled = enabled;
                e.Weight = weight;
                e.SizeScale = size;
                e.Brightness = brightness;
                e.HueShift = hue;
                e.Saturation = saturation;
                EditorUtility.SetDirty(set);
                if (structural)
                    objectsPending = true;
                AfterAssetEdit();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Показать источник"))
                    EditorGUIUtility.PingObject(e.Texture != null ? (UnityEngine.Object)e.Texture : set);
                if (GUILayout.Button(new GUIContent("Сбросить коррекцию", "Яркость, тон, насыщенность и размер — к нейтральным; PNG не меняется.")))
                {
                    Undo.RecordObject(set, "Генератор карты: сброс коррекции");
                    if (Mathf.Abs(e.SizeScale - 1f) > 1e-5f)
                        objectsPending = true;
                    e.SizeScale = e.Brightness = e.Saturation = 1f;
                    e.HueShift = 0f;
                    EditorUtility.SetDirty(set);
                    AfterAssetEdit();
                }
            }
            if (GUILayout.Button(new GUIContent("Найти на карте", "Навести предпросмотр на один экземпляр (вкладка «Карта»).")))
                FindOnMap(e);

            extraOpen = EditorGUILayout.Foldout(extraOpen, "Дополнительно", true);
            if (extraOpen)
            {
                EditorGUI.BeginChangeCheck();
                Vector2 range = EditorGUILayout.Vector2Field(new GUIContent("Разброс масштаба"), e.ScaleRange);
                float slope = EditorGUILayout.Slider(new GUIContent("Допустимый уклон, °"), e.MaxSlope, 0f, 60f);
                float cutoff = EditorGUILayout.Slider(new GUIContent("Порог альфы"), e.Cutoff, 0.05f, 0.95f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(set, "Генератор карты: ассет " + e.Id);
                    e.ScaleRange = new Vector2(Mathf.Max(0.1f, range.x), Mathf.Max(range.x, range.y));
                    e.MaxSlope = slope;
                    e.Cutoff = cutoff;
                    EditorUtility.SetDirty(set);
                    objectsPending = true;
                    AfterAssetEdit();
                }
            }
            landingOpen = EditorGUILayout.Foldout(landingOpen, "Посадка на карту", true);
            if (landingOpen)
            {
                EditorGUILayout.LabelField("Опора (холст)", $"{e.Pivot.x:0.000}, {e.Pivot.y:0.000}" + (e.PivotEdited ? " · правлена" : ""));
                EditorGUILayout.LabelField("Основание в мире", $"{e.Footprint.x:0.0} × {e.Footprint.y:0.0} ед.");
                EditorGUILayout.LabelField("Ракурс / направление", $"{e.PitchDownDegrees:0}° · {(string.IsNullOrEmpty(e.Direction) ? "—" : e.Direction)}");
                if (e.Category == MapLabArtCategory.CliffModule)
                    EditorGUILayout.LabelField("Скала: верх камня / подошва", $"{e.RockTopPx:0} / {e.BasePx:0} px");
                EditorGUILayout.HelpBox("Изменение основания меняет дворы, подходы и клики — правится в калибровке с проверкой подходов.", MessageType.None);
                if (GUILayout.Button("Открыть калибровку арта"))
                    MapLabArtCalibrationWindow.Open();
            }
            EditorGUILayout.EndScrollView();
        }

        private static bool IsLastEnabled(MapLabArtSet set, MapLabArtEntry e)
        {
            if (string.IsNullOrEmpty(e.Role))
                return false;
            return !set.Entries.Any(o => o != null && o != e && o.Enabled && o.Role == e.Role && o.HasTexture);
        }

        private void RecordSets(string label)
        {
            foreach (MapLabArtSet set in ActiveSets())
                Undo.RecordObject(set, label);
        }

        private void AfterAssetEdit()
        {
            foreach (MapLabArtSet set in ActiveSets())
                EditorUtility.SetDirty(set);
            view.ApplyMaterialSettings();
            previewDirty = true;
            Repaint();
        }

        private void FindOnMap(MapLabArtEntry e)
        {
            if (view.CameraRig == null || view.Data == null)
                return;
            foreach (Transform child in view.GetComponentsInChildren<Transform>(true))
            {
                MeshRenderer r = child.GetComponent<MeshRenderer>();
                if (r == null || r.sharedMaterial == null || r.sharedMaterial.GetTexture("_MainTex") != e.Texture)
                    continue;
                Vector3 c = r.bounds.center;
                view.CameraRig.Target = new Vector3(c.x, 0f, c.z);
                view.CameraRig.Zoom = view.CameraRig.MaxZoom;
                view.CameraRig.Apply();
                tab = Tab.Map;
                previewDirty = true;
                status = "Показан участок с «" + e.Id + "».";
                return;
            }
            status = "«" + e.Id + "» на текущей карте не найден.";
        }

        // ---------- Вкладка «Проверка» ----------

        private void DrawCheck(MapLabSettings s)
        {
            checkScroll = EditorGUILayout.BeginScrollView(checkScroll);
            List<(MessageType Type, string Text)> items = Validate(s);
            int errors = items.Count(i => i.Type == MessageType.Error), warnings = items.Count(i => i.Type == MessageType.Warning);
            MessageType total = errors > 0 ? MessageType.Error : warnings > 0 ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(errors > 0 ? $"Есть ошибки: {errors}" : warnings > 0 ? $"Есть предупреждения: {warnings}" : "Карта готова", total);
            foreach ((MessageType type, string text) in items)
                EditorGUILayout.HelpBox(text, type);
            if (planReport != null)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Паспорт региона", EditorStyles.boldLabel);
                foreach (string line in planReport.Passport)
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);
                if (planReport.Diagram != null)
                {
                    EditorGUILayout.LabelField("Схема: районы, пути, деревни (цвет — семейство), места (цвет — число оснований обнаружить: красный 0–1, жёлтый 2, зелёный 3+), площадки, нарушения.", EditorStyles.wordWrappedMiniLabel);
                    float w = Mathf.Max(100f, leftWidth - 24f);
                    float h = w * planReport.Diagram.height / Mathf.Max(1f, planReport.Diagram.width);
                    Rect r = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
                    GUI.DrawTexture(r, planReport.Diagram, ScaleMode.ScaleToFit);
                }
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Подробности", EditorStyles.boldLabel);
            MapLabStats st = view.LastStats;
            MapLabData d = view.Data;
            EditorGUILayout.LabelField("Ключ", MapKey(s));
            if (d != null)
            {
                EditorGUILayout.LabelField("Размер мира", $"{d.Size:0} × {d.Size:0} ед. · сетка {d.Resolution}");
                EditorGUILayout.LabelField("Генерация", $"{st.GenerateMs:0} мс (рельеф {st.TerrainMs:0}, покраска {st.PaintMs:0}, объекты {st.ObjectsMs:0})");
                EditorGUILayout.LabelField("Сцена", $"вершин {st.Vertices:N0}, рендереров {st.Renderers}, материалов {st.Materials}");
                int houses = d.Objects.Count(o => o.Kind == MapLabObjectKind.House);
                EditorGUILayout.LabelField("Деревня", $"дворов {d.Yards.Count}, домов {houses}, дорог {d.Roads.Count}, тропинок {d.YardPaths.Count}");
                EditorGUILayout.LabelField("Перо", $"дороги {view.InkStats.Polylines} линий · поля {view.FieldInkStats.Polylines} · берег {view.ShoreInkStats.Polylines}; окончаний {view.InkStats.Endings + view.FieldInkStats.Endings + view.ShoreInkStats.Endings}");
                EditorGUILayout.LabelField("Ассетов на карте", $"{view.Usage.Count} рисунков, {view.Usage.Values.Sum():N0} экземпляров");
            }
            EditorGUILayout.EndScrollView();
        }

        private List<(MessageType, string)> Validate(MapLabSettings s)
        {
            List<(MessageType, string)> items = new List<(MessageType, string)>();
            if (view.Data == null)
                items.Add((MessageType.Warning, "Карта не построена — «Сгенерировать предпросмотр»."));
            if (!MapLabAuthor.Verified(s.Author.WorldSize))
                items.Add((MessageType.Warning, "Размер «Очень большой» — проверочный: для игры не подтверждён (этап C)."));
            if (planReport != null)
                foreach ((bool error, string text) in planReport.Issues)
                    items.Add((error ? MessageType.Error : MessageType.Warning, "План: " + text));
            if (!s.IsStage05)
                items.Add((MessageType.Warning, "Профиль не на виде Stage05: рабочий генератор рассчитан на принятый вид этапа 05."));
            foreach (MapLabArtSet set in ActiveSets())
            {
                foreach (MapLabArtEntry e in set.Entries)
                {
                    if (e == null)
                        continue;
                    if (!e.HasTexture)
                        items.Add((MessageType.Error, $"Нет файла рисунка: {e.Id} ({set.name})."));
                }
                foreach (IGrouping<string, MapLabArtEntry> role in set.Entries.Where(x => x != null && !string.IsNullOrEmpty(x.Role)).GroupBy(x => x.Role))
                    if (role.All(x => !x.Enabled))
                        items.Add((MessageType.Warning, $"Роль «{role.Key}» ({set.name}): все варианты отключены — в новых картах её не будет."));
            }
            if (view.ModuleStats != null && view.ModuleStats.MissingIds.Count > 0)
                items.Add((MessageType.Warning, "Нет модулей: " + string.Join(", ", view.ModuleStats.MissingIds)));
            if (objectsPending)
                items.Add((MessageType.Info, "Изменения ассетов ждут пересборки объектов (вкладка «Ассеты»)."));
            return items;
        }

        // ---------- Служебное ----------

        private void Splitter(Rect r, int id)
        {
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);
            EditorGUI.DrawRect(r, new Color(0.1f, 0.1f, 0.1f));
            Event e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                dragging = id;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && dragging == id)
            {
                if (id == 1)
                    leftWidth += e.delta.x;
                else
                    rightWidth -= e.delta.x;
                Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && dragging == id)
                dragging = 0;
        }

        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (MapLabGenerator.CancelledException)
            {
                status = "Отменено; прежний вид сохранён.";
            }
            catch (Exception exception)
            {
                status = "Ошибка: " + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
