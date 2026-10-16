using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12М: панель «Обработка кадра» — одна и та же в окне «Обработка
    // кадра» и на вкладке «Кадр» Базы локаций. Сверху — чьё небо правится,
    // главный выключатель и сохранённые стили («Запомнить», «Применить» по
    // разделам); ниже — вкладки разделов: Цвет, Свет и линза, Тон, Холст,
    // Рисунки, Атмосфера, Качество. Каждый эффект — блок со своим
    // выключателем и «Сбросить». Правки идут через хозяина (Undo, запись,
    // пересборка предпросмотра, если сменился набор материалов рисунков).
    internal sealed class FrameEffectsEditor
    {
        private const string PrefsKey = "KS.FrameEffects.";

        public LocalLocationDatabaseAsset Database;
        public LocationVisualDefinition Visual;
        public string PlaceName;
        // Правка: действие, затем — перестроить ли панель.
        public Action<Action, bool> Change;
        // Перестроить панель целиком (стили, смена неба).
        public Action Rebuild;
        // Предпросмотр: всплеск линзы и пробное растворение (может не быть).
        public Action Pulse;
        public Action TestDissolve;
        // Снимок кадра без LUT для покраски (null — в этом окне нет).
        public Func<Texture2D> Snapshot;
        // Открыть отдельное окно (с вкладки Базы локаций).
        public Action OpenWindow;

        private VisualElement parent;

        private static readonly string[] TabTitles = LocationFrameSections.Order.Select(LocationFrameSections.Title).ToArray();

        private static int Tab
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(PrefsKey + "Tab", 0), 0, TabTitles.Length - 1);
            set => EditorPrefs.SetInt(PrefsKey + "Tab", value);
        }

        private static string SelectedStyle
        {
            get => EditorPrefs.GetString(PrefsKey + "Style", "");
            set => EditorPrefs.SetString(PrefsKey + "Style", value ?? "");
        }

        private static LocationFrameSection ApplySections
        {
            get => (LocationFrameSection)EditorPrefs.GetInt(PrefsKey + "Sections", (int)LocationFrameSection.All);
            set => EditorPrefs.SetInt(PrefsKey + "Sections", (int)value);
        }

        private LocationSky Sky => new LocationSky(Visual, Database.worldLighting);
        private LocationPostEffects Post
        {
            get
            {
                LocationSky sky = Sky;
                if (sky.Post == null) sky.Post = new LocationPostEffects();
                return sky.Post.Repair();
            }
        }

        public VisualElement Build()
        {
            VisualElement root = new VisualElement();
            parent = root;
            if (Database == null) { Help("База мест не найдена."); return root; }
            if (Visual == null) { Help("У места нет художественной сборки — обработке кадра нечего показывать."); return root; }
            LocationSky sky = Sky;
            LocationPostEffects post = Post;

            BuildTarget(sky);
            Toggle("Обработка кадра включена", post.Enabled, value => post.Enabled = value, true,
                "Главный выключатель: без него ни один эффект ниже не виден.");
            if (!post.Enabled) Help("Обработка кадра выключена — эффекты не видны (настройки сохраняются).");
            if (OpenWindow != null)
                Buttons(("Открыть в отдельном окне ↗", OpenWindow, "Окно «Обработка кадра»: большой предпросмотр, сравнение «до / после», снимок для LUT"));

            BuildStyles(post);

            VisualElement tabs = Row(true);
            for (int i = 0; i < TabTitles.Length; i++)
            {
                int index = i;
                Button button = new Button(() => { Tab = index; Rebuild?.Invoke(); }) { text = TabTitles[i] };
                if (i == Tab) { button.style.unityFontStyleAndWeight = FontStyle.Bold; button.style.color = new Color(.95f, .82f, .5f); }
                tabs.Add(button);
            }
            tabs.style.marginTop = 8;
            root.Add(tabs);

            VisualElement body = new VisualElement();
            body.SetEnabled(post.Enabled);
            root.Add(body);
            parent = body;
            switch (LocationFrameSections.Order[Tab])
            {
                case LocationFrameSection.Color: BuildColor(post); break;
                case LocationFrameSection.LightLens: BuildLightLens(post); break;
                case LocationFrameSection.Tone: BuildTone(post); break;
                case LocationFrameSection.Canvas: BuildCanvas(post); break;
                case LocationFrameSection.Sprites: BuildSprites(post); break;
                case LocationFrameSection.Atmosphere: BuildAtmosphere(post); break;
                default: BuildQuality(post); break;
            }
            parent = root;
            return root;
        }

        // ------------------------------------------------------------------
        // Чьё небо и стили
        // ------------------------------------------------------------------

        private void BuildTarget(LocationSky sky)
        {
            Label title = new Label(sky.IsWorld ? "Правится: общий свет мира — все места с общим светом" : "Правится: своё небо места «" + PlaceName + "»");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.color = sky.IsWorld ? new Color(.95f, .78f, .45f) : new Color(.6f, .85f, .65f);
            title.style.marginBottom = 4;
            parent.Add(title);
            List<bool> options = new List<bool> { true, false };
            PopupField<string> field = new PopupField<string>("Чьё небо", new List<string> { "Общий свет мира (все места)", "Своё для этого места" },
                Visual.UseWorldLighting ? 0 : 1);
            field.RegisterValueChangedCallback(evt =>
            {
                bool world = options[field.index];
                Change(() =>
                {
                    // Своё небо начинается с копии общего: вид места не прыгает.
                    if (!world && Visual.UseWorldLighting) LocationSky.CopyWorldToOwn(Database.worldLighting, Visual);
                    Visual.UseWorldLighting = world;
                }, true);
            });
            field.tooltip = "Обработка кадра — часть неба места: общего для всех мест или своего.";
            parent.Add(field);
        }

        private void BuildStyles(LocationPostEffects post)
        {
            Database.frameStyles ??= new List<LocationFrameStyle>();
            List<LocationFrameStyle> styles = Database.frameStyles;
            Foldout block = Block("Сохранённые стили (" + styles.Count + ")", "Styles", true);
            VisualElement previous = parent;
            parent = block;

            LocationFrameStyle selected = Database.FindFrameStyle(SelectedStyle);
            ListView list = new ListView(styles, 20, () => new Label(), (element, index) =>
            {
                LocationFrameStyle style = styles[index];
                Label label = (Label)element;
                label.text = (style == selected ? "● " : "   ") + style.Name + (string.IsNullOrEmpty(style.Note) ? "" : "  — " + style.Note);
                label.tooltip = string.IsNullOrEmpty(style.Note) ? "Дважды щёлкните — применить" : style.Note;
            })
            {
                selectionType = SelectionType.Single,
                fixedItemHeight = 20
            };
            list.style.minHeight = 64;
            list.style.maxHeight = 160;
            list.style.height = Mathf.Clamp(styles.Count * 20 + 4, 64, 160);
            list.selectionChanged += items =>
            {
                LocationFrameStyle style = items.OfType<LocationFrameStyle>().FirstOrDefault();
                if (style == null || style.Id == SelectedStyle) return;
                SelectedStyle = style.Id;
                Rebuild?.Invoke();
            };
            list.itemsChosen += items =>
            {
                LocationFrameStyle style = items.OfType<LocationFrameStyle>().FirstOrDefault();
                if (style != null) ApplyStyle(style);
            };
            if (selected != null) list.SetSelectionWithoutNotify(new[] { styles.IndexOf(selected) });
            block.Add(list);
            if (styles.Count == 0) Help("Стилей пока нет. Настройте кадр и нажмите «Запомнить как новый» — потом его можно применять к любым местам целиком или по разделам.");

            TextField name = new TextField("Имя") { value = selected != null ? selected.Name : "Новый стиль" };
            TextField note = new TextField("Заметка") { value = selected != null ? selected.Note : "", tooltip = "Для чего стиль: «пасмурное утро», «страх», «кладбище»…" };
            block.Add(name);
            block.Add(note);
            Buttons(
                ("Запомнить как новый", () => Change(() =>
                {
                    LocationFrameStyle style = new LocationFrameStyle { Name = Unique(name.value), Note = note.value ?? "", Post = post.Clone() };
                    styles.Add(style);
                    SelectedStyle = style.Id;
                }, true), "Сохранить текущие настройки кадра (все разделы) новым стилем"),
                ("Перезаписать выбранный", selected == null ? null : (Action)(() => Change(() =>
                {
                    selected.Post = post.Clone();
                    selected.Name = string.IsNullOrWhiteSpace(name.value) ? selected.Name : name.value.Trim();
                    selected.Note = note.value ?? "";
                }, true)), "Записать текущие настройки в выбранный стиль"));
            Buttons(
                ("Применить", selected == null ? null : (Action)(() => ApplyStyle(selected)), "Применить выбранные разделы стиля к этому небу"),
                ("Переименовать", selected == null ? null : (Action)(() => Change(() =>
                {
                    selected.Name = string.IsNullOrWhiteSpace(name.value) ? selected.Name : name.value.Trim();
                    selected.Note = note.value ?? "";
                }, true)), "Имя и заметку — из полей выше"),
                ("Дублировать", selected == null ? null : (Action)(() => Change(() =>
                {
                    LocationFrameStyle copy = new LocationFrameStyle { Name = Unique(selected.Name + " копия"), Note = selected.Note, Post = selected.Post.Clone() };
                    styles.Insert(styles.IndexOf(selected) + 1, copy);
                    SelectedStyle = copy.Id;
                }, true)), null),
                ("Удалить", selected == null ? null : (Action)(() =>
                {
                    if (!EditorUtility.DisplayDialog("Удалить стиль", "Удалить стиль «" + selected.Name + "»? Места, к которым он применён, не изменятся.", "Удалить", "Отмена")) return;
                    Change(() => { styles.Remove(selected); SelectedStyle = ""; }, true);
                }), null));
            Buttons(
                ("↑", selected == null || styles.IndexOf(selected) <= 0 ? null : (Action)(() => Change(() => Move(styles, selected, -1), true)), "Выше в списке"),
                ("↓", selected == null || styles.IndexOf(selected) >= styles.Count - 1 ? null : (Action)(() => Change(() => Move(styles, selected, 1), true)), "Ниже в списке"));

            Label sectionsLabel = new Label("Что применять из стиля:");
            sectionsLabel.style.marginTop = 4;
            block.Add(sectionsLabel);
            VisualElement sections = Row(true);
            foreach (LocationFrameSection section in LocationFrameSections.Order)
            {
                UnityEngine.UIElements.Toggle toggle = new UnityEngine.UIElements.Toggle(LocationFrameSections.Title(section)) { value = (ApplySections & section) != 0 };
                toggle.style.marginRight = 8;
                toggle.labelElement.style.minWidth = 0;
                toggle.RegisterValueChangedCallback(evt => ApplySections = evt.newValue ? ApplySections | section : ApplySections & ~section);
                sections.Add(toggle);
            }
            block.Add(sections);
            if (Sky.IsWorld)
                Help("Небо места — общий свет мира: «Применить» меняет кадр всех мест с общим светом.");
            parent = previous;
        }

        private void ApplyStyle(LocationFrameStyle style)
        {
            LocationFrameSection sections = ApplySections;
            if (sections == LocationFrameSection.None) { EditorUtility.DisplayDialog("Применить стиль", "Не выбран ни один раздел «Что применять».", "Понятно"); return; }
            Change(() => LocationFrameSections.Copy(style.Post, Post, sections), true);
        }

        private string Unique(string name)
        {
            string baseName = string.IsNullOrWhiteSpace(name) ? "Стиль" : name.Trim();
            string result = baseName;
            for (int i = 2; Database.frameStyles.Any(item => item.Name == result); i++) result = baseName + " " + i;
            return result;
        }

        private static void Move(List<LocationFrameStyle> styles, LocationFrameStyle style, int step)
        {
            int index = styles.IndexOf(style);
            int target = Mathf.Clamp(index + step, 0, styles.Count - 1);
            styles.RemoveAt(index);
            styles.Insert(target, style);
        }

        // ------------------------------------------------------------------
        // Разделы
        // ------------------------------------------------------------------

        private void BuildColor(LocationPostEffects post)
        {
            Help("Днём и ночью — свои значения; между ними — по яркости суток. Под крышей — дневные.");
            Group("День", "Day", null, null, () => Grade(post.Day), () => post.Day = new LocationColorGrade());
            Group("Ночь", "Night", null, null, () => Grade(post.Night), () => post.Night = new LocationPostEffects().Night);
            Buttons(
                ("День → в ночь", (Action)(() => Change(() => post.Night = JsonUtility.FromJson<LocationColorGrade>(JsonUtility.ToJson(post.Day)), true)), "Скопировать дневной цвет в ночной"),
                ("Ночь → в день", (Action)(() => Change(() => post.Day = JsonUtility.FromJson<LocationColorGrade>(JsonUtility.ToJson(post.Night)), true)), null));
        }

        private void Grade(LocationColorGrade grade)
        {
            Slider("Экспозиция", grade.Exposure, -3, 3, value => grade.Exposure = value, "Общая яркость кадра (ступени).");
            Slider("Контраст", grade.Contrast, -100, 100, value => grade.Contrast = value);
            Slider("Насыщенность", grade.Saturation, -100, 100, value => grade.Saturation = value);
            Slider("Тепло / холод", grade.Temperature, -100, 100, value => grade.Temperature = value);
            Slider("Зелень / пурпур", grade.Tint, -100, 100, value => grade.Tint = value);
            ColorField("Цветной фильтр", grade.Filter, value => grade.Filter = value, "Умножение всего кадра на цвет (белый — без изменений).");
        }

        private void BuildLightLens(LocationPostEffects post)
        {
            LocationPostEffects neutral = new LocationPostEffects();
            Group("Свечение ярких мест (bloom)", "Bloom", post.Bloom, value => post.Bloom = value, () =>
            {
                Slider("Сила", post.BloomIntensity, 0, 5, value => post.BloomIntensity = value);
                Slider("Порог яркости", post.BloomThreshold, 0, 2, value => post.BloomThreshold = value, "Что ярче порога — светится: огонь, блики.");
                Slider("Разлёт", post.BloomScatter, 0, 1, value => post.BloomScatter = value);
                ColorField("Оттенок", post.BloomTint, value => post.BloomTint = value);
            }, () =>
            {
                post.BloomIntensity = neutral.BloomIntensity; post.BloomThreshold = neutral.BloomThreshold;
                post.BloomScatter = neutral.BloomScatter; post.BloomTint = neutral.BloomTint;
            });
            Group("Затемнение краёв (виньетка)", "Vignette", post.Vignette, value => post.Vignette = value, () =>
            {
                Slider("Сила", post.VignetteIntensity, 0, 1, value => post.VignetteIntensity = value);
                Slider("Мягкость", post.VignetteSmoothness, .01f, 1, value => post.VignetteSmoothness = value);
                ColorField("Цвет краёв", post.VignetteColor, value => post.VignetteColor = value);
            }, () =>
            {
                post.VignetteIntensity = neutral.VignetteIntensity; post.VignetteSmoothness = neutral.VignetteSmoothness; post.VignetteColor = neutral.VignetteColor;
            });
            Group("Зерно плёнки", "Grain", post.Grain > .001f, value => post.Grain = value ? Mathf.Max(post.Grain, .25f) : 0, () =>
            {
                Slider("Сила", post.Grain, 0, 1, value => post.Grain = value);
                Choice("Размер зерна", new List<LocationGrainType> { LocationGrainType.Thin, LocationGrainType.Medium, LocationGrainType.Large },
                    value => value == LocationGrainType.Thin ? "Мелкое" : value == LocationGrainType.Medium ? "Среднее" : "Крупное", post.GrainType, value => post.GrainType = value);
                Slider("Где видно (0 — везде, 1 — в тенях)", post.GrainResponse, 0, 1, value => post.GrainResponse = value);
            }, () => { post.Grain = 0; post.GrainType = LocationGrainType.Thin; post.GrainResponse = .8f; });
            Group("Тональная кривая", "Tonemapping", null, null, () =>
            {
                Choice("Кривая", new List<LocationTonemapping> { LocationTonemapping.None, LocationTonemapping.Neutral, LocationTonemapping.ACES },
                    value => value == LocationTonemapping.None ? "Нет" : value == LocationTonemapping.Neutral ? "Нейтральная" : "Кино (ACES)",
                    post.Tonemapping, value => post.Tonemapping = value);
                Help("ACES сгущает тени и мягко гасит пересветы — «киношно», но меняет цвета рисунков. Нейтральная — бережнее.");
            }, () => post.Tonemapping = LocationTonemapping.None);
            LocationLensEffects lens = post.Lens;
            Group("Линза", "Lens", null, null, () =>
            {
                Slider("Хроматическая аберрация", lens.ChromaticAberration, 0, 1, value => lens.ChromaticAberration = value,
                    "Цветные края к углам кадра. Постоянно — едва-едва или никак; сильнее — как краткий всплеск.");
                Slider("Искажение линзы", lens.Distortion, -1, 1, value => lens.Distortion = value, "Меньше нуля — «бочка», больше — «подушка».");
                Slider("Масштаб при искажении", lens.DistortionScale, .01f, 5, value => lens.DistortionScale = value, "Подрезать края, ушедшие при искажении.");
            }, () => { lens.ChromaticAberration = 0; lens.Distortion = 0; lens.DistortionScale = 1; });
            Group("Краткий всплеск (удар, страх, нечисть рядом)", "Pulse", null, null, () =>
            {
                Help("Игра вызывает всплеск сама (LocationWorldRenderer.Pulse); здесь — его сила и длительность. На пике к линзе добавляется:");
                Slider("Аберрация на пике", lens.PulseAberration, 0, 1, value => lens.PulseAberration = value);
                Slider("Искажение на пике", lens.PulseDistortion, -1, 1, value => lens.PulseDistortion = value);
                Slider("Длительность (с)", lens.PulseDuration, .05f, 3, value => lens.PulseDuration = value);
                if (Pulse != null) Buttons(("Проверить всплеск", Pulse, "Показать всплеск в предпросмотре"));
            }, () =>
            {
                LocationLensEffects fresh = new LocationLensEffects();
                lens.PulseAberration = fresh.PulseAberration; lens.PulseDistortion = fresh.PulseDistortion; lens.PulseDuration = fresh.PulseDuration;
            });
        }

        private void BuildTone(LocationPostEffects post)
        {
            LocationToneEffects tone = post.Tone;
            Group("LUT — палитра одним рисунком", "Lut", tone.Lut != null && tone.LutContribution > 0, value => tone.LutContribution = value ? Mathf.Max(tone.LutContribution, 1) : 0, () =>
            {
                Help("Как сделать: «Снимок для покраски» → откройте PNG в Photoshop или Krita → красьте корректирующими слоями (кривые, цветовой баланс, оттенок) " +
                     "весь снимок вместе с полоской в левом верхнем углу, размер не меняйте → сохраните PNG → «Извлечь LUT из покрашенного снимка». " +
                     "Unity применит тот же цвет к каждому кадру.");
                TextureField("LUT (1024×32)", tone.Lut, value => tone.Lut = value);
                Slider("Сила", tone.LutContribution, 0, 1, value => tone.LutContribution = value);
                string problem = LutTools.Problem(tone.Lut);
                if (problem != null)
                {
                    parent.Add(new HelpBox(problem, HelpBoxMessageType.Warning));
                    if (LutTools.CanFix(tone.Lut))
                        Buttons(("Исправить импорт LUT", (Action)(() => { LutTools.FixImport(tone.Lut); Rebuild?.Invoke(); }), "Без sRGB, без мип-карт, без сжатия, Clamp"));
                }
                Buttons(
                    ("Снимок для покраски…", (Action)(() => LutTools.ExportSnapshot(Snapshot, OpenWindow)), "PNG кадра (без LUT) с нейтральной полоской LUT"),
                    ("Извлечь LUT из покрашенного снимка…", (Action)(() =>
                    {
                        Texture2D lut = LutTools.ImportPainted();
                        if (lut != null) Change(() => { tone.Lut = lut; tone.LutContribution = Mathf.Max(tone.LutContribution, 1); }, true);
                    }), "Вырезать полоску из PNG и сохранить LUT в проект"));
                Buttons(("Нейтральный LUT…", (Action)(() =>
                {
                    Texture2D lut = LutTools.SaveNeutral();
                    if (lut != null) Change(() => tone.Lut = lut, true);
                }), "Пустая полоска 1024×32 — красить отдельно (для внешних редакторов LUT)"));
            }, () => { tone.Lut = null; tone.LutContribution = 1; });

            Group("Разделение тонов", "Split", tone.SplitToning, value => tone.SplitToning = value, () =>
            {
                Help("Тени — в один цвет, света — в другой. Холодные тени и тёплые света дают кадру «киношное» единство.");
                ColorField("Тени", tone.SplitShadows, value => tone.SplitShadows = value);
                ColorField("Света", tone.SplitHighlights, value => tone.SplitHighlights = value);
                Slider("Баланс (− к теням, + к светам)", tone.SplitBalance, -100, 100, value => tone.SplitBalance = value);
            }, () =>
            {
                LocationToneEffects fresh = new LocationToneEffects();
                tone.SplitShadows = fresh.SplitShadows; tone.SplitHighlights = fresh.SplitHighlights; tone.SplitBalance = 0;
            });
            Group("Тени / полутона / света", "Smh", tone.ShadowsMidtonesHighlights, value => tone.ShadowsMidtonesHighlights = value, () =>
            {
                Help("Цвет — оттенок (белый — без сдвига), сдвиг — светлее или темнее. Ровняет рисунки разных партий по яркости.");
                Trackball("Тени", tone.Shadows);
                Trackball("Полутона", tone.Midtones);
                Trackball("Света", tone.Highlights);
            }, () => { tone.Shadows = new LocationTrackball(); tone.Midtones = new LocationTrackball(); tone.Highlights = new LocationTrackball(); });
            Group("Lift / Gamma / Gain", "Lgg", tone.LiftGammaGain, value => tone.LiftGammaGain = value, () =>
            {
                Help("Lift — самые тёмные места, Gamma — середина, Gain — светлые. Грубее, чем «тени / полутона / света».");
                Trackball("Lift", tone.Lift);
                Trackball("Gamma", tone.Gamma);
                Trackball("Gain", tone.Gain);
            }, () => { tone.Lift = new LocationTrackball(); tone.Gamma = new LocationTrackball(); tone.Gain = new LocationTrackball(); });
            Group("Смешивание каналов", "Mixer", tone.ChannelMixer, value => tone.ChannelMixer = value, () =>
            {
                Help("Из чего собран каждый канал (проценты). 100 / 0 / 0 — без изменений.");
                Mixer("Красный", tone.MixerRed, value => tone.MixerRed = value);
                Mixer("Зелёный", tone.MixerGreen, value => tone.MixerGreen = value);
                Mixer("Синий", tone.MixerBlue, value => tone.MixerBlue = value);
            }, () => { tone.MixerRed = new Vector3(100, 0, 0); tone.MixerGreen = new Vector3(0, 100, 0); tone.MixerBlue = new Vector3(0, 0, 100); });
            Group("Кривые", "Curves", tone.Curves, value => tone.Curves = value, () =>
            {
                Help("Вход слева направо (0..1) → выход снизу вверх. Насыщенность: 0.5 — без изменений, выше — ярче.");
                Rect unit = new Rect(0, 0, 1, 1);
                Curve("Общая", tone.Master, value => tone.Master = value, unit, Color.white);
                Curve("Красный", tone.Red, value => tone.Red = value, unit, new Color(1, .4f, .4f));
                Curve("Зелёный", tone.Green, value => tone.Green = value, unit, new Color(.4f, 1, .4f));
                Curve("Синий", tone.Blue, value => tone.Blue = value, unit, new Color(.45f, .6f, 1));
                Curve("Насыщенность по тону", tone.HueVsSaturation, value => tone.HueVsSaturation = value, unit, new Color(1, .8f, .3f));
                Curve("Насыщенность по насыщенности", tone.SaturationVsSaturation, value => tone.SaturationVsSaturation = value, unit, new Color(1, .8f, .3f));
                Curve("Насыщенность по яркости", tone.LuminanceVsSaturation, value => tone.LuminanceVsSaturation = value, unit, new Color(1, .8f, .3f));
            }, () =>
            {
                tone.Master = LocationToneEffects.Identity(); tone.Red = LocationToneEffects.Identity(); tone.Green = LocationToneEffects.Identity();
                tone.Blue = LocationToneEffects.Identity(); tone.HueVsSaturation = LocationToneEffects.Flat();
                tone.SaturationVsSaturation = LocationToneEffects.Flat(); tone.LuminanceVsSaturation = LocationToneEffects.Flat();
            });
        }

        private void BuildCanvas(LocationPostEffects post)
        {
            LocationCanvasEffects canvas = post.Canvas;
            LocationCanvasEffects fresh = new LocationCanvasEffects();
            Group("Живописность (мазки, фильтр Кувахары)", "Painterly", canvas.Painterly, value => canvas.Painterly = value, () =>
            {
                Help("Мелкие детали сливаются в мазки, края остаются. Дорого для видеокарты: радиус больше 4 — только на сильных машинах.");
                IntSlider("Радиус мазка (пиксели)", canvas.PainterlyRadius, 1, 8, value => canvas.PainterlyRadius = value);
                Slider("Сила", canvas.PainterlyStrength, 0, 1, value => canvas.PainterlyStrength = value);
            }, () => { canvas.PainterlyRadius = fresh.PainterlyRadius; canvas.PainterlyStrength = fresh.PainterlyStrength; });
            Group("Туман по высоте экрана", "Fog", canvas.Fog, value => canvas.Fog = value, () =>
            {
                Choice("Откуда", new List<LocationFogDirection> { LocationFogDirection.FromTop, LocationFogDirection.FromBottom },
                    value => value == LocationFogDirection.FromTop ? "Сверху — даль в дымке" : "Снизу — низовой туман", canvas.FogDirection, value => canvas.FogDirection = value);
                ColorField("Цвет", canvas.FogColor, value => canvas.FogColor = value);
                Slider("Плотность", canvas.FogDensity, 0, 1, value => canvas.FogDensity = value);
                Slider("Начинается (доля высоты от края)", canvas.FogStart, 0, 1, value => canvas.FogStart = value, "Считается от края, откуда идёт туман.");
                Slider("Полный (доля высоты)", canvas.FogEnd, 0, 1, value => canvas.FogEnd = value);
                Slider("Клочья", canvas.FogNoise, 0, 1, value => canvas.FogNoise = value, "0 — ровная дымка, 1 — рваные клочья.");
                Slider("Размер клочьев", canvas.FogNoiseScale, .2f, 12, value => canvas.FogNoiseScale = value);
                Slider("Дрейф", canvas.FogSpeed, 0, .3f, value => canvas.FogSpeed = value);
            }, () =>
            {
                canvas.FogColor = fresh.FogColor; canvas.FogDensity = fresh.FogDensity; canvas.FogStart = fresh.FogStart; canvas.FogEnd = fresh.FogEnd;
                canvas.FogNoise = fresh.FogNoise; canvas.FogNoiseScale = fresh.FogNoiseScale; canvas.FogSpeed = fresh.FogSpeed; canvas.FogDirection = fresh.FogDirection;
            });
            Group("Сведение к палитре", "Palette", canvas.Palette, value => canvas.Palette = value, () =>
            {
                Help("Без рисунка-палитры — по уровням на канал. С рисунком — каждый цвет кадра заменяется ближайшим из палитры (до 64 цветов: каждый пиксель рисунка — цвет).");
                TextureField("Рисунок-палитра", canvas.PaletteTexture, value => canvas.PaletteTexture = value);
                if (canvas.PaletteTexture != null)
                    parent.Add(new Label("Цветов в палитре: " + LocationCanvasPass.PaletteOf(canvas.PaletteTexture).Length) { style = { marginLeft = 4, marginBottom = 2 } });
                IntSlider("Уровней на канал (без палитры)", canvas.PaletteLevels, 2, 32, value => canvas.PaletteLevels = value);
                Slider("Сила", canvas.PaletteStrength, 0, 1, value => canvas.PaletteStrength = value);
                Slider("Дизеринг (сетка)", canvas.PaletteDither, 0, 1, value => canvas.PaletteDither = value, "Мелкая сетка точек вместо полос между цветами.");
            }, () =>
            {
                canvas.PaletteTexture = null; canvas.PaletteLevels = fresh.PaletteLevels; canvas.PaletteStrength = fresh.PaletteStrength; canvas.PaletteDither = fresh.PaletteDither;
            });
            Group("Бумага / холст", "Paper", canvas.Paper, value => canvas.Paper = value, () =>
            {
                Help("Фактура поверх кадра — картинка выглядит нарисованной. Без своей текстуры — встроенная бумага с волокнами. Своя текстура повторяется по кадру.");
                TextureField("Своя текстура (необязательно)", canvas.PaperTexture, value => canvas.PaperTexture = value);
                Slider("Сила", canvas.PaperStrength, 0, 1, value => canvas.PaperStrength = value);
                Slider("Масштаб", canvas.PaperScale, .1f, 6, value => canvas.PaperScale = value);
                Slider("Волокна (встроенная)", canvas.PaperFibers, 0, 1, value => canvas.PaperFibers = value);
            }, () =>
            {
                canvas.PaperTexture = null; canvas.PaperStrength = fresh.PaperStrength; canvas.PaperScale = fresh.PaperScale; canvas.PaperFibers = fresh.PaperFibers;
            });
        }

        private void BuildSprites(LocationPostEffects post)
        {
            LocationSpriteEffects s = post.Sprites;
            LocationSpriteEffects fresh = new LocationSpriteEffects();
            Help("Эффекты общего шейдера рисунков: предметы, раскидка, люди. Включение и выключение ветра, обводки, дальнего плана и растворения пересобирает предпросмотр.");
            Group("Ветер", "Wind", s.Wind, value => s.Wind = value, () =>
            {
                Help("Качает деревья и растительность (категории Базы ассетов) и ковёр раскидки; частицы атмосферы сносит туда же.");
                Slider("Размах (ед. мира)", s.WindStrength, 0, .5f, value => s.WindStrength = value);
                Slider("Скорость", s.WindSpeed, 0, 5, value => s.WindSpeed = value);
                Slider("Порывы", s.WindGusts, 0, 1, value => s.WindGusts = value);
                Slider("Высота полного размаха (ед. мира)", s.WindHeight, .2f, 10, value => s.WindHeight = value, "Ниже — качается меньше: у земли ствол стоит.");
                Slider("Направление (− запад, + восток, 0 — качание)", s.WindDirection, -1, 1, value => s.WindDirection = value);
                Slider("Деревья (доля)", s.WindTrees, 0, 2, value => s.WindTrees = value);
                Slider("Растительность и трава (доля)", s.WindPlants, 0, 2, value => s.WindPlants = value);
            }, () =>
            {
                s.WindStrength = fresh.WindStrength; s.WindSpeed = fresh.WindSpeed; s.WindGusts = fresh.WindGusts; s.WindHeight = fresh.WindHeight;
                s.WindDirection = fresh.WindDirection; s.WindTrees = fresh.WindTrees; s.WindPlants = fresh.WindPlants;
            });
            Group("Обводка", "Outline", s.Outline, value => s.Outline = value, () =>
            {
                Help("Тонкий край по контуру рисунка, изнутри (рисунок не растёт). Сводит рисунки разных художников к одной манере.");
                ColorField("Цвет", s.OutlineColor, value => s.OutlineColor = value);
                Slider("Толщина (пиксели рисунка)", s.OutlineWidth, .5f, 6, value => s.OutlineWidth = value);
                Toggle("У людей", s.OutlinePeople, value => s.OutlinePeople = value);
                Toggle("У предметов", s.OutlineObjects, value => s.OutlineObjects = value);
            }, () => { s.OutlineColor = fresh.OutlineColor; s.OutlineWidth = fresh.OutlineWidth; s.OutlinePeople = fresh.OutlinePeople; s.OutlineObjects = fresh.OutlineObjects; });
            Group("Дальний план", "Distance", s.Distance, value => s.Distance = value, () =>
            {
                Help("Чем выше на экране опора рисунка — тем он дальше: бледнее и в дымке. Земля не меняется (её — туман «Холста»).");
                Slider("Начинается (доля высоты снизу)", s.DistanceStart, 0, 1, value => s.DistanceStart = value);
                Slider("Полная сила (доля высоты)", s.DistanceEnd, 0, 1, value => s.DistanceEnd = value);
                Slider("Обесцвечивание", s.DistanceDesaturate, 0, 1, value => s.DistanceDesaturate = value);
                ColorField("Цвет дымки", s.DistanceHaze, value => s.DistanceHaze = value);
                Slider("Дымка", s.DistanceHazeStrength, 0, 1, value => s.DistanceHazeStrength = value);
            }, () =>
            {
                s.DistanceStart = fresh.DistanceStart; s.DistanceEnd = fresh.DistanceEnd; s.DistanceDesaturate = fresh.DistanceDesaturate;
                s.DistanceHaze = fresh.DistanceHaze; s.DistanceHazeStrength = fresh.DistanceHazeStrength;
            });
            Group("Растворение при гибели", "Dissolve", s.Dissolve, value => s.Dissolve = value, () =>
            {
                Help("Павший в бою на месте рассыпается тлеющим краем.");
                Slider("Длительность (с)", s.DissolveDuration, .1f, 5, value => s.DissolveDuration = value);
                Slider("До какой доли (1 — исчезает)", s.DissolveAmount, 0, 1, value => s.DissolveAmount = value, "Меньше 1 — от тела остаётся след.");
                ColorField("Цвет края", s.DissolveEdge, value => s.DissolveEdge = value);
                Slider("Ширина края", s.DissolveEdgeWidth, .01f, .4f, value => s.DissolveEdgeWidth = value);
                Slider("Мелкость", s.DissolveScale, 2, 120, value => s.DissolveScale = value);
                if (TestDissolve != null) Buttons(("Проверить на фигуре", TestDissolve, "Растворить фигуры предпросмотра и вернуть"));
            }, () =>
            {
                s.DissolveDuration = fresh.DissolveDuration; s.DissolveAmount = fresh.DissolveAmount; s.DissolveEdge = fresh.DissolveEdge;
                s.DissolveEdgeWidth = fresh.DissolveEdgeWidth; s.DissolveScale = fresh.DissolveScale;
            });
        }

        private void BuildAtmosphere(LocationPostEffects post)
        {
            LocationAtmosphereEffects a = post.Atmosphere;
            Help("Частицы поверх места. Плотность — частиц в секунду на 100 кв. единиц мира (искры — на источник огня с мерцанием, пар — на человека). " +
                 "Ветер раздела «Рисунки» сносит частицы по их доле ветра.");
            Particle("Пыль в лучах", "Dust", a.Dust, LocationParticleLayer.DefaultDust, value => a.Dust = value, 0, 40);
            Particle("Мошкара", "Midges", a.Midges, LocationParticleLayer.DefaultMidges, value => a.Midges = value, 0, 120, true);
            Particle("Дождь", "Rain", a.Rain, LocationParticleLayer.DefaultRain, value => a.Rain = value, 0, 400, false, true);
            Particle("Падающие листья", "Leaves", a.Leaves, LocationParticleLayer.DefaultLeaves, value => a.Leaves = value, 0, 10);
            Particle("Искры от огня", "Sparks", a.Sparks, LocationParticleLayer.DefaultSparks, value => a.Sparks = value, 0, 40);
            Particle("Пар изо рта", "Breath", a.Breath, LocationParticleLayer.DefaultBreath, value => a.Breath = value, 0, 4);
        }

        private void Particle(string title, string key, LocationParticleLayer layer, Func<LocationParticleLayer> defaults, Action<LocationParticleLayer> replace,
            float minDensity, float maxDensity, bool groups = false, bool streak = false)
        {
            Group(title, "Particle" + key, layer.Enabled, value => layer.Enabled = value, () =>
            {
                Slider("Плотность", layer.Density, minDensity, maxDensity, value => layer.Density = value);
                if (groups) IntSlider("Роёв", layer.Groups, 1, 12, value => layer.Groups = value);
                Slider("Размер (ед. мира)", layer.Size, .005f, .5f, value => layer.Size = value);
                Slider("Разброс размера", layer.SizeJitter, 0, 1, value => layer.SizeJitter = value);
                Slider("Жизнь (с)", layer.Lifetime, .1f, 20, value => layer.Lifetime = value);
                Slider("Скорость", layer.Speed, 0, 20, value => layer.Speed = value);
                Slider("Направление (°: 90 — вверх, 270 — вниз)", layer.Angle, 0, 360, value => layer.Angle = value);
                Slider("Разброс направления (°)", layer.Spread, 0, 360, value => layer.Spread = value);
                Slider("Тяга (+ вниз, − вверх)", layer.Gravity, -.5f, .5f, value => layer.Gravity = value);
                Slider("Блуждание", layer.Wander, 0, 3, value => layer.Wander = value);
                Slider("Частота блуждания", layer.WanderFrequency, .05f, 6, value => layer.WanderFrequency = value);
                if (streak) Slider("Длина штриха", layer.Stretch, 1, 40, value => layer.Stretch = value);
                ColorField("Цвет", layer.Color, value => layer.Color = value);
                ColorField("Второй цвет (вразброс)", layer.ColorB, value => layer.ColorB = value);
                Slider("Непрозрачность", layer.Opacity, 0, 1, value => layer.Opacity = value);
                Slider("Доля ветра", layer.WindShare, 0, 2, value => layer.WindShare = value);
                TextureField("Своя картинка (необязательно)", layer.Texture, value => layer.Texture = value);
                Choice("Когда видно", new List<LocationParticleTime> { LocationParticleTime.Always, LocationParticleTime.Day, LocationParticleTime.Night, LocationParticleTime.Hours },
                    value => value == LocationParticleTime.Always ? "Всегда" : value == LocationParticleTime.Day ? "Днём" : value == LocationParticleTime.Night ? "Ночью" : "В часы",
                    layer.Time, value => layer.Time = value);
                VisualElement hours = new VisualElement();
                hours.SetEnabled(layer.Time == LocationParticleTime.Hours);
                VisualElement keep = parent;
                parent = hours;
                Slider("С часа", layer.FromHour, 0, 24, value => layer.FromHour = value);
                Slider("До часа", layer.ToHour, 0, 24, value => layer.ToHour = value);
                parent = keep;
                parent.Add(hours);
            }, () =>
            {
                bool enabled = layer.Enabled;
                LocationParticleLayer fresh = defaults();
                fresh.Enabled = enabled;
                replace(fresh);
            });
        }

        private void BuildQuality(LocationPostEffects post)
        {
            Group("Сглаживание", "Antialiasing", null, null, () =>
            {
                Help("Убирает «лесенку» на краях повёрнутых и уменьшенных рисунков. FXAA — дёшево и чуть мылит; SMAA — чище, дороже. Работает при включённой обработке кадра.");
                Choice("Сглаживание", new List<LocationAntialiasing> { LocationAntialiasing.None, LocationAntialiasing.Fxaa, LocationAntialiasing.SmaaLow, LocationAntialiasing.SmaaMedium, LocationAntialiasing.SmaaHigh },
                    value => value == LocationAntialiasing.None ? "Нет" : value == LocationAntialiasing.Fxaa ? "FXAA — быстрое"
                        : value == LocationAntialiasing.SmaaLow ? "SMAA — низкое" : value == LocationAntialiasing.SmaaMedium ? "SMAA — среднее" : "SMAA — высокое",
                    post.Quality.Antialiasing, value => post.Quality.Antialiasing = value);
            }, () => post.Quality.Antialiasing = LocationAntialiasing.None);
            Group("Сбросить разделы", "Reset", null, null, () =>
            {
                Help("Вернуть раздел к нейтральному виду (ничего не меняет в кадре). Отменяется Ctrl+Z.");
                VisualElement row = Row(true);
                foreach (LocationFrameSection section in LocationFrameSections.Order)
                {
                    LocationFrameSection captured = section;
                    row.Add(new Button(() => Change(() => LocationFrameSections.Reset(post, captured), true)) { text = LocationFrameSections.Title(section) });
                }
                parent.Add(row);
            }, null);
        }

        // ------------------------------------------------------------------
        // Поля
        // ------------------------------------------------------------------

        private Foldout Block(string title, string key, bool open)
        {
            string prefs = PrefsKey + "Block." + key;
            Foldout block = new Foldout { text = title, value = EditorPrefs.GetBool(prefs, open) };
            block.RegisterValueChangedCallback(evt => { if (evt.target == block) EditorPrefs.SetBool(prefs, evt.newValue); });
            block.style.marginTop = 6;
            block.style.paddingLeft = block.style.paddingRight = 6;
            block.style.paddingTop = 2;
            block.style.paddingBottom = 4;
            block.style.backgroundColor = new Color(1, 1, 1, .035f);
            block.style.borderTopWidth = block.style.borderBottomWidth = block.style.borderLeftWidth = block.style.borderRightWidth = 1;
            block.style.borderTopColor = block.style.borderBottomColor = block.style.borderLeftColor = block.style.borderRightColor = new Color(1, 1, 1, .09f);
            block.style.borderTopLeftRadius = block.style.borderTopRightRadius = block.style.borderBottomLeftRadius = block.style.borderBottomRightRadius = 4;
            Label label = block.Q<UnityEngine.UIElements.Toggle>()?.Q<Label>();
            if (label != null)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = new Color(.88f, .78f, .53f);
            }
            block.contentContainer.style.marginLeft = 2;
            parent.Add(block);
            return block;
        }

        // Блок эффекта: выключатель (если есть), «Сбросить», поля.
        private void Group(string title, string key, bool? enabled, Action<bool> setEnabled, Action content, Action reset)
        {
            string Title(bool? on) => (on == null ? "" : on.Value ? "● " : "○ ") + title;
            Foldout block = Block(Title(enabled), key, false);
            VisualElement keep = parent;
            parent = block;
            VisualElement header = Row(false);
            VisualElement body = new VisualElement();
            if (enabled != null)
            {
                UnityEngine.UIElements.Toggle toggle = new UnityEngine.UIElements.Toggle("Включено") { value = enabled.Value };
                toggle.style.flexGrow = 1;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    Change(() => setEnabled(evt.newValue), false);
                    body.SetEnabled(evt.newValue);
                    block.text = Title(evt.newValue);
                });
                header.Add(toggle);
                body.SetEnabled(enabled.Value);
            }
            else header.Add(new VisualElement { style = { flexGrow = 1 } });
            if (reset != null)
                header.Add(new Button(() => Change(reset, true)) { text = "Сбросить", tooltip = "Значения блока — по умолчанию (выключатель не трогает)" });
            if (enabled != null || reset != null) block.Add(header);
            block.Add(body);
            parent = body;
            content();
            parent = keep;
        }

        private VisualElement Row(bool wrap)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            if (wrap) row.style.flexWrap = Wrap.Wrap;
            return row;
        }

        private void Help(string text) => parent.Add(new HelpBox(text, HelpBoxMessageType.Info));

        private void Buttons(params (string text, Action action, string tooltip)[] buttons)
        {
            VisualElement row = Row(true);
            foreach ((string text, Action action, string tooltip) in buttons)
            {
                Button button = new Button(action) { text = text, tooltip = tooltip };
                button.SetEnabled(action != null);
                row.Add(button);
            }
            parent.Add(row);
        }

        private void Toggle(string label, bool value, Action<bool> set, bool rebuild = false, string tooltip = null)
        {
            UnityEngine.UIElements.Toggle field = new UnityEngine.UIElements.Toggle(label) { value = value, tooltip = tooltip };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), rebuild));
            parent.Add(field);
        }

        private void Slider(string label, float value, float min, float max, Action<float> set, string tooltip = null)
        {
            UnityEngine.UIElements.Slider field = new UnityEngine.UIElements.Slider(label, min, max) { value = value, showInputField = true, tooltip = tooltip };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), false));
            parent.Add(field);
        }

        private void IntSlider(string label, int value, int min, int max, Action<int> set)
        {
            SliderInt field = new SliderInt(label, min, max) { value = value, showInputField = true };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), false));
            parent.Add(field);
        }

        private void ColorField(string label, Color value, Action<Color> set, string tooltip = null)
        {
            UnityEditor.UIElements.ColorField field = new UnityEditor.UIElements.ColorField(label) { value = value, tooltip = tooltip };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), false));
            parent.Add(field);
        }

        private void TextureField(string label, Texture2D value, Action<Texture2D> set)
        {
            ObjectField field = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false, value = value };
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue as Texture2D), true));
            parent.Add(field);
        }

        private void Curve(string label, AnimationCurve value, Action<AnimationCurve> set, Rect ranges, Color color)
        {
            CurveField field = new CurveField(label) { value = value, ranges = ranges };
            field.style.height = 34;
            field.RegisterValueChangedCallback(evt => Change(() => set(evt.newValue), false));
            parent.Add(field);
        }

        private void Choice<T>(string label, List<T> options, Func<T, string> name, T current, Action<T> set)
        {
            List<string> names = options.Select(name).ToList();
            PopupField<string> field = new PopupField<string>(label, names, Mathf.Max(0, options.IndexOf(current)));
            field.RegisterValueChangedCallback(evt => Change(() => set(options[field.index]), true));
            parent.Add(field);
        }

        private void Trackball(string label, LocationTrackball ball)
        {
            ColorField(label + ": оттенок", ball.Color, value => ball.Color = value);
            Slider(label + ": сдвиг", ball.Offset, -1, 1, value => ball.Offset = value);
        }

        private void Mixer(string label, Vector3 value, Action<Vector3> set)
        {
            Label title = new Label(label) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } };
            parent.Add(title);
            Slider("  из красного", value.x, -200, 200, v => { Vector3 x = value; x.x = v; value = x; set(x); });
            Slider("  из зелёного", value.y, -200, 200, v => { Vector3 x = value; x.y = v; value = x; set(x); });
            Slider("  из синего", value.z, -200, 200, v => { Vector3 x = value; x.z = v; value = x; set(x); });
        }
    }
}
