using System;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattlefieldDatabase.Editor;
using KingdomSurvival.BattleSandbox;
using KingdomSurvival.MapLab;
using KingdomSurvival.MapLabBridge;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Левая панель «Генератора арен»: настройки генерации (все — в ассете,
    // их берёт игра) и обработка кадра арены (одна общая).
    public sealed partial class ArenaGeneratorWindow
    {
        private VisualElement section;

        private void BuildLeft()
        {
            if (left == null || asset == null) return;
            left.Clear();
            section = null;
            serialized ??= new SerializedObject(asset);
            serialized.Update();
            // Привязка полей шлёт первое «изменение» — не пересобирать из-за него.
            ignoreUntil = EditorApplication.timeSinceStartup + .4;
            if (CurrentMode == Mode.Frame) BuildFrame();
            else { BuildGeneration(); LockIfNoProfile(); }
        }

        // ------------------------------------------------------------------
        // Генерация
        // ------------------------------------------------------------------

        private void BuildGeneration()
        {
            BuildThemeBanner();
            Help("Общие настройки генерации обычной арены боя (дорожный бой, полигон). Игра их запоминает: ассет " +
                 ArenaGeneratorAsset.AssetPath + ". Правка пересобирает предпросмотр справа; клетки и рисунок те же, что в бою. " +
                 "По умолчанию — прежние арены (те же seed дают те же клетки и декор).");
            VisualElement presets = Row();
            AddButton(presets, "Много травы", () => ApplyLook(look =>
            {
                look.GrassPatchesMin = 30; look.GrassPatchesMax = 42;
                look.GrassPerPatchMin = 8; look.GrassPerPatchMax = 16;
                look.GrassPatchSizeMin = 1f; look.GrassPatchSizeMax = 2.4f;
                look.GrassSpacing = .45f; look.GrassClearOfStart = .4f; look.GrassOutside = 1.5f;
                look.GrassCarpet = 10f; look.GrassCarpetScale = 1.2f; look.GrassSingles = 1.5f; look.GrassScale = 1.3f;
            }), "Пятен травы втрое больше, травяной ковёр и россыпь травинок по всему полю");
            AddButton(presets, "Поляна", () => ApplyLook(look =>
            {
                look.GrassCarpet = 10f; look.GrassCarpetScale = 1.2f; look.GrassSingles = 1.5f;
                look.GrassPatchesMin = 24; look.GrassPatchesMax = 32; look.GrassPerPatchMin = 6; look.GrassPerPatchMax = 12;
                look.GrassInField = 2.5f; look.GrassOutsideField = .6f; look.GrassFieldEdge = 1.5f;
            }), "Поле клеток — сплошная травяная поляна, вокруг травы меньше");
            AddButton(presets, "Гуще за полем", () => ApplyLook(look =>
            {
                look.GrassCarpet = Mathf.Max(look.GrassCarpet, 6f); look.GrassSingles = Mathf.Max(look.GrassSingles, 1f);
                look.GrassInField = .5f; look.GrassOutsideField = 2f; look.GrassFieldEdge = 1.5f;
            }), "На клетках трава реже, за полем — гуще, переход плавный");
            AddButton(presets, "Густой лес", () => ApplyLook(look =>
            {
                look.TreeGroupsMin = 14; look.TreeGroupsMax = 20;
                look.TreesPerGroupMin = 6; look.TreesPerGroupMax = 12;
                look.TreeGroupGap = 2.4f; look.TreeGroupSpread = 1.2f;
            }), "Больше и плотнее групп деревьев по краям");
            AddButton(presets, "Голое поле", () => ApplyLook(look =>
            {
                look.TreeGroupsMin = 3; look.TreeGroupsMax = 5;
                look.GrassPatchesMin = 2; look.GrassPatchesMax = 4;
                look.GrassCarpet = 0f; look.GrassSingles = 0f;
            }), "Мало деревьев и травы");
            AddButton(presets, "Сбросить всё", () =>
            {
                if (!EditorUtility.DisplayDialog("Сбросить настройки генератора?", "Тактика клеток и рисунок арены этой темы вернутся к прежним значениям. Обработка кадра не меняется. Ctrl+Z — вернуть.", "Сбросить", "Отмена")) return;
                Undo.RecordObject(asset, "Сбросить генератор арен");
                ActiveTactics = new SandboxArenaGenerationSettings();
                ActiveLook = new MapLabArenaLook();
                ignoreUntil = 0;
                MarkChanged(true);
                serialized.Update();
                BuildLeft();
            }, "Прежние значения арены");
            left.Add(presets);

            Section("Клетки боя (тактика)", "Tactics", true);
            Help("Что генератор кладёт на клетки: особая местность и преграды по теме арены. Проверка связности, обхода и высот остаётся: " +
                 "если вариант не проходит — упрощённый, затем без особой местности (отчёт под предпросмотром).");
            F("Tactics.SpecialShare", "Особая местность, доля клеток", 0, .6f, "Холмы, кустарник, лес, топь, грязь, мелководье, щебень, снег, лёд");
            F("Tactics.ObstacleShare", "Преграды, доля клеток", 0, .35f, "Валуны, стволы, завалы, стенки, глубокая вода");
            F("Tactics.HillAmount", "Холмы (объём)", 0, 3, "Множитель холмов темы; 0 — без холмов");
            F("Tactics.VegetationAmount", "Кустарник и лес на клетках (объём)", 0, 3);
            F("Tactics.GroundAmount", "Особое основание (объём)", 0, 3, "Топь, грязь, мелководье, щебень, снег, лёд");
            F("Tactics.ObstacleAmount", "Преграды (объём)", 0, 3);
            SubHeading("Река (темы «Берег и брод», «Берег с мостом»)");
            I("Tactics.RiverBend", "Изгиб русла, клеток", 0, 4, "0 — прямое русло сверху вниз");
            I("Tactics.RiverWidth", "Ширина русла, клеток", 1, 2, "2 — брод и мост во весь ряд");
            I("Tactics.FordCount", "Бродов", 1, 3);
            SubHeading("Проверка");
            I("Tactics.MaxAttempts", "Попыток на вариант", 1, 200);
            F("Tactics.MaxDetourFactor", "Допустимый обход (× прямой)", 1.2f, 5f, "Путь сближения сторон не длиннее прямой × это + 2");

            Section("Деревья", "Trees", true);
            F("Look.TreeGroupsMin", "Групп деревьев, от", 0, 40);
            F("Look.TreeGroupsMax", "Групп деревьев, до", 0, 40);
            I("Look.TreesPerGroupMin", "Деревьев в группе, от", 1, 30);
            I("Look.TreesPerGroupMax", "Деревьев в группе, до", 1, 30);
            F("Look.TreeGroupSpread", "Размах группы", .3f, 3f, "1 — 2,6 × 1,8 гекса");
            F("Look.TreeGroupGap", "Расстояние между группами, гексов", .5f, 8f);
            F("Look.TreeClearance", "Отступ от клеток поля, гексов", .3f, 4f, "Ближе — деревья подходят к полю (могут заслонять край клеток)");
            F("Look.TreeMainSpecies", "Доля главной породы в группе", 0, 1);
            F("Look.TreeYoungAtEdge", "Молодые деревья на краю группы", 0, 1);
            F("Look.TreeScale", "Размер деревьев", .4f, 2.5f);

            Section("Трава", "Grass", true);
            Help("Низкая трава — рисунки Базы ассетов (роль «низкая трава») пятнами с паузами. Травяной ковёр — пучки травы карты мира ровно по полю; россыпь — одиночные травинки Базы ассетов.");
            F("Look.GrassPatchesMin", "Пятен травы, от", 0, 120);
            F("Look.GrassPatchesMax", "Пятен травы, до", 0, 120);
            I("Look.GrassPerPatchMin", "Травинок в пятне, от", 1, 60);
            I("Look.GrassPerPatchMax", "Травинок в пятне, до", 1, 60);
            F("Look.GrassPatchSizeMin", "Размер пятна, от (гексов)", .2f, 5f);
            F("Look.GrassPatchSizeMax", "Размер пятна, до (гексов)", .2f, 5f);
            F("Look.GrassSpacing", "Теснота (меньше — гуще)", .1f, 1.5f);
            F("Look.GrassClearOfStart", "Отступ от стартовых клеток, гексов", 0, 2f, "0 — трава и под бойцами");
            F("Look.GrassOutside", "Пятна за краем поля, гексов", 0, 4f);
            SubHeading("Где гуще: на поле или за ним");
            F("Look.GrassInField", "Густота травы на поле клеток", 0, 4f, "Множитель всех видов травы на клетках боя. 1 и 1 — ровно; больше на поле — поляна");
            F("Look.GrassOutsideField", "Густота травы за полем", 0, 4f, "Множитель травы дальше от клеток. Больше, чем на поле, — за клетками всегда гуще");
            F("Look.GrassFieldEdge", "Переход поле → окраина, гексов", .2f, 5f, "Ширина плавного перехода густоты от края клеток");
            F("Look.GrassScale", "Размер травы", .4f, 3f);
            F("Look.TallGrassShare", "Доля высокой травы (пакет «Grass_*»)", 0, 1, "Высокая луговая трава (от 0,75 м) среди травы пятен и россыпи; на клетки поля не ставится. 0 — только низкая");
            SubHeading("Травяной ковёр и россыпь (по всему полю)");
            F("Look.GrassCarpet", "Ковёр: пучков травы карты на гекс", 0, 40, "Пучки травы карты мира (набор этапа 05) ровно по полю; 0 — нет ковра");
            F("Look.GrassCarpetInk", "Ковёр: доля контурных пучков (при равном приоритете)", 0, 1);
            F("Look.GrassCarpetScale", "Ковёр: размер пучков", .3f, 3f);
            F("Look.GrassCarpetPatchiness", "Ковёр: неровность (плешины)", 0, 1, "0 — ровно, 1 — плешины и густые места");
            F("Look.GrassSingles", "Россыпь травинок Базы ассетов на гекс", 0, 12, "Одиночные травинки ровно по полю, сверх пятен");
            F("Look.ReedDensity", "Камыш у реки", 0, 3f, "0 — без камыша");

            Section("Пни, бурьян, камни", "Props", false);
            F("Look.WeedChance", "Бурьян у группы деревьев, вероятность", 0, 1, "Вероятность каждого растения у края группы");
            I("Look.WeedsPerGroup", "Бурьян у группы деревьев, до штук", 0, 8, "Крапива, лопух, иван-чай… у края группы со стороны поля");
            F("Look.WeedClumps", "Куртины бурьяна по окраине, штук", 0, 30, "Куртина — 1–3 растения бурьяна за краем поля, клеток не закрывает");
            F("Look.StumpChance", "Пень у группы деревьев, вероятность", 0, 1);
            F("Look.WeedScale", "Размер бурьяна", .4f, 3f);
            F("Look.SmallRocksMin", "Мелких камней, от", 0, 40);
            F("Look.SmallRocksMax", "Мелких камней, до", 0, 40);
            I("Look.RubbleStonesMin", "Камней на клетке щебня, от", 0, 8);
            I("Look.RubbleStonesMax", "Камней на клетке щебня, до", 0, 8);
            F("Look.BlockBoulderShare", "Непроходимая клетка: доля валунов (иначе пень)", 0, 1);

            Section("Рельеф, плотность, качество", "Relief", false);
            F("Look.DecorDensity", "Общая плотность декора", 0, 5f, "Множитель групп деревьев, пятен травы и камней");
            F("Look.HillLiftHexes", "Высота холма, радиусов гекса", .1f, 1.2f);
            F("Look.AssetScale", "Масштаб рисунков (0 — из MapLabGameAssets)", 0, 4f);
            F("Look.PaintTexelsPerUnit", "Плотность покраски земли", 8, 48, "Выше — чётче земля, дольше сборка арены");

            Section("Общее для полей боя", "Fields", false);
            BuildCommonFields();
        }

        private void ApplyLook(Action<MapLabArenaLook> change)
        {
            Undo.RecordObject(asset, "Генератор арен: набор");
            change(ActiveLook);
            ignoreUntil = 0;
            MarkChanged(true);
            serialized.Update();
            BuildLeft();
        }

        // Ходьба и сетка/вид гекса поля полигона (бывший раздел «Поля боя» Базы локаций).
        private void BuildCommonFields()
        {
            if (fields == null) { Help("База полей боя не найдена."); return; }
            SerializedObject fieldsSerialized = new SerializedObject(fields);
            VisualElement common = new VisualElement();
            Slider walk = BattlefieldHexEditorKit.BoundSlider("Ходьба, гексов/с", fieldsSerialized.FindProperty("walkHexesPerSecond"),
                BattlefieldDatabaseAsset.MinWalkHexesPerSecond, BattlefieldDatabaseAsset.MaxWalkHexesPerSecond);
            walk.tooltip = "Одна скорость на бой и исследование мест: бойцы на поле и отряд в месте. Темп шага анимации подстраивается.";
            common.Add(walk);
            BattlefieldDefinitionData field = Field;
            int index = field != null ? fields.Battlefields.ToList().IndexOf(field) : -1;
            if (index >= 0)
            {
                Help("Сетка и вид гекса — запись поля «" + field.DisplayLabel + "» (поле полигона и дорожных боёв).");
                SerializedProperty property = BattlefieldHexEditorKit.FieldProperty(fieldsSerialized, index);
                common.Add(BattlefieldHexEditorKit.GridCard(fieldsSerialized, property, "Размер и сдвиг сетки в кадре 16:9.", () => FieldsChanged(true)));
                common.Add(BattlefieldHexEditorKit.HexCard(fields, fieldsSerialized, index, this, false, _ => { }, BuildLeft, () => FieldsChanged(false)));
            }
            common.Bind(fieldsSerialized);
            common.TrackSerializedObjectValue(fieldsSerialized, _ => FieldsChanged(false));
            if (index >= 0)
            {
                SerializedProperty property = BattlefieldHexEditorKit.FieldProperty(fieldsSerialized, index);
                common.TrackPropertyValue(property.FindPropertyRelative("gridScale"), _ => FieldsChanged(true));
                common.TrackPropertyValue(property.FindPropertyRelative("gridOffset"), _ => FieldsChanged(true));
            }
            section.Add(common);
        }

        private void FieldsChanged(bool rebuild)
        {
            if (fields == null || EditorApplication.timeSinceStartup < ignoreUntil) return;
            EditorUtility.SetDirty(fields);
            if (rebuild) RequestRebuild();
            else previewHost?.Query<BattlefieldView>().ForEach(view => view.Refresh());
        }

        // ------------------------------------------------------------------
        // Обработка кадра
        // ------------------------------------------------------------------

        private static string FrameTarget
        {
            get => EditorPrefs.GetString(PrefsKey + "FrameTarget", "");
            set => EditorPrefs.SetString(PrefsKey + "FrameTarget", value ?? "");
        }

        private void BuildFrame()
        {
            asset.Frame.Repair();
            Help("Обработка кадра генерируемой арены боя — те же правила, что у мест: одна общая обработка действует на всех аренах.");
            FrameEffectsEditor editor = new FrameEffectsEditor
            {
                Database = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath),
                DirectPost = () => asset.Frame.Common,
                DirectTitle = "Правится: обработка кадра арен боя",
                Sections = ArenaFrameSections,
                DirectNote = ArenaFrameNote,
                CharactersNote = "Фигуры бойцов — на предпросмотре справа: правка видна сразу. Нет фигур — «Пересобрать» или другой seed.",
                Change = (action, rebuild) =>
                {
                    Undo.RecordObject(asset, "Обработка кадра арены");
                    action();
                    ignoreUntil = 0;
                    MarkChanged(false);
                    if (rebuild) BuildLeft();
                },
                Rebuild = BuildLeft,
                Pulse = strength => ArenaFrameBinding.Current?.Hit(strength),
                Hit = strength => ArenaFrameBinding.Current?.Hit(strength),
            };
            if (editor.Database == null) { Help("Не найдена База локаций (сохранённые стили кадра)."); return; }
            left.Add(editor.Build());
        }

        // Разделы обработки кадра, которые действуют на арене.
        // «Рисунки» на арене — только ветер, «Атмосфера» — тени облаков и лучи.
        public const LocationFrameSection ArenaFrameSections = LocationFrameSection.Color | LocationFrameSection.LightLens | LocationFrameSection.Tone |
                                                                LocationFrameSection.Canvas | LocationFrameSection.Sprites | LocationFrameSection.Characters |
                                                                LocationFrameSection.Atmosphere | LocationFrameSection.Game | LocationFrameSection.Quality;

        public const string ArenaFrameNote = "На арене действуют: цвет, свет и линза (со всплеском, вспышкой и тряской при ударе), тон, холст, " +
                                             "рисунки (ветер качает деревья, кусты и траву), атмосфера (тени облаков, лучи), " +
                                             "игровые состояния (красные края по здоровью вождя, вспышка, затемнение, полосы) и персонажи (подкраска и яркость фигур бойцов). " +
                                             "Частицы атмосферы и прочие эффекты рисунков — свойства мира места: у арены их нет.";

        // ------------------------------------------------------------------
        // Поля
        // ------------------------------------------------------------------

        private void Section(string title, string key, bool open)
        {
            Foldout foldout = new Foldout { text = title, value = EditorPrefs.GetBool(PrefsKey + "Open." + key, open) };
            foldout.RegisterValueChangedCallback(evt => { if (evt.target == foldout) EditorPrefs.SetBool(PrefsKey + "Open." + key, evt.newValue); });
            foldout.style.marginTop = 6;
            foldout.Q<Toggle>().style.unityFontStyleAndWeight = FontStyle.Bold;
            left.Add(foldout);
            section = foldout;
        }

        private void SubHeading(string text)
        {
            Label label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 6;
            label.style.color = new Color(.8f, .78f, .7f);
            section.Add(label);
        }

        private void F(string path, string label, float min, float max, string tooltip = null)
        {
            SerializedProperty property = serialized.FindProperty(ProfilePath(path));
            if (property == null) { section.Add(new Label("нет поля " + path)); return; }
            Slider slider = new Slider(label, min, max) { showInputField = true, tooltip = tooltip };
            slider.labelElement.style.minWidth = 190;
            slider.BindProperty(property);
            slider.RegisterValueChangedCallback(_ => MarkChanged(true));
            section.Add(slider);
        }

        private void I(string path, string label, int min, int max, string tooltip = null)
        {
            SerializedProperty property = serialized.FindProperty(ProfilePath(path));
            if (property == null) { section.Add(new Label("нет поля " + path)); return; }
            SliderInt slider = new SliderInt(label, min, max) { showInputField = true, tooltip = tooltip };
            slider.labelElement.style.minWidth = 190;
            slider.BindProperty(property);
            slider.RegisterValueChangedCallback(_ => MarkChanged(true));
            section.Add(slider);
        }

        private void B(string path, string label, string tooltip = null)
        {
            SerializedProperty property = serialized.FindProperty(ProfilePath(path));
            if (property == null) { section.Add(new Label("нет поля " + path)); return; }
            Toggle toggle = new Toggle(label) { tooltip = tooltip };
            toggle.labelElement.style.minWidth = 190;
            toggle.BindProperty(property);
            toggle.RegisterValueChangedCallback(_ => MarkChanged(true));
            section.Add(toggle);
        }

        private void Help(string text) => (section != null && section.parent != null && CurrentMode == Mode.Generation && section.parent == left ? section : left)
            .Add(new HelpBox(text, HelpBoxMessageType.Info));

        private void Warn(string text) => left.Add(new HelpBox(text, HelpBoxMessageType.Warning));

        private static VisualElement Row()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 4;
            return row;
        }

        private static void AddButton(VisualElement parent, string title, Action action, string tooltip = null) =>
            parent.Add(new Button(action) { text = title, tooltip = tooltip });
    }
}
