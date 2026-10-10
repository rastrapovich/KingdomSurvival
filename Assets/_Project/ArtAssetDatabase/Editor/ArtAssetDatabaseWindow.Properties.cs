using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Правая панель: свойства выбранного ассета, ракурса и части. Разделы
    // сворачиваются (состояние помнится до перезапуска редактора); длинные
    // объяснения — в подсказках полей.
    public sealed partial class ArtAssetDatabaseWindow
    {
        // Куда добавляются поля: панель или открытый раздел.
        private VisualElement propertyTarget;
        private VisualElement Target => propertyTarget ?? properties;

        private void Add(VisualElement element) => Target.Add(element);

        private void Heading(string text)
        {
            Label label = SectionTitle(text);
            label.style.fontSize = 13;
            label.style.marginTop = 12;
            Add(label);
        }

        // Сворачиваемый раздел; поля до EndSection попадают в него.
        private void Section(string title, bool open, string key = null)
        {
            propertyTarget = null;
            string prefsKey = "KS.ArtAssets.Section." + (key ?? title);
            Foldout foldout = new Foldout { text = title, value = SessionState.GetBool(prefsKey, open) };
            foldout.RegisterValueChangedCallback(evt => { if (evt.target == foldout) SessionState.SetBool(prefsKey, evt.newValue); });
            foldout.style.marginTop = 8;
            Toggle toggle = foldout.Q<Toggle>();
            if (toggle != null)
            {
                toggle.style.unityFontStyleAndWeight = FontStyle.Bold;
                toggle.style.color = new Color(.88f, .78f, .53f);
            }
            properties.Add(foldout);
            propertyTarget = foldout;
        }

        private void EndSection() => propertyTarget = null;

        private void Help(string text, HelpBoxMessageType type = HelpBoxMessageType.Info) => Add(new HelpBox(text, type));

        private void Note(string text, string tip = null)
        {
            Label label = new Label(text) { tooltip = tip };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = new Color(.7f, .75f, .7f);
            label.style.fontSize = 11;
            Add(label);
        }

        private void Row(params (string title, Action action)[] buttons)
        {
            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            foreach ((string title, Action action) in buttons) row.Add(new Button(action) { text = title });
            Add(row);
        }

        private void TextProperty(string label, string value, Action<string> set, string undo = "Изменить ассет")
        {
            TextField field = new TextField(label) { value = value ?? string.Empty, isDelayed = true };
            field.RegisterValueChangedCallback(evt => Edit(undo, () => set(evt.newValue)));
            Add(field);
        }

        private void BoolProperty(string label, bool value, Action<bool> set, string tip = null)
        {
            Toggle field = new Toggle(label) { value = value, tooltip = tip };
            field.RegisterValueChangedCallback(evt => Edit("Изменить ассет", () => set(evt.newValue)));
            Add(field);
        }

        private void FloatProperty(string label, float value, Action<float> set, string tip = null, bool rebuild = false)
        {
            FloatField field = new FloatField(label) { value = value, isDelayed = true, tooltip = tip };
            field.RegisterValueChangedCallback(evt => Edit("Изменить ассет", () => set(evt.newValue), rebuild));
            Add(field);
        }

        private void IntProperty(string label, int value, Action<int> set)
        {
            IntegerField field = new IntegerField(label) { value = value, isDelayed = true };
            field.RegisterValueChangedCallback(evt => Edit("Изменить ассет", () => set(evt.newValue)));
            Add(field);
        }

        private void VectorProperty(string label, Vector2 value, Action<Vector2> set, string tip = null)
        {
            Vector2Field field = new Vector2Field(label) { value = value, tooltip = tip };
            field.RegisterValueChangedCallback(evt => Edit("Изменить ракурс", () => set(evt.newValue)));
            Add(field);
        }

        private void ChoiceProperty<T>(string label, IList<T> options, Func<T, string> name, T current, Action<T> set)
        {
            List<string> names = options.Select(name).ToList();
            PopupField<string> field = new PopupField<string>(label, names, Mathf.Max(0, options.IndexOf(current)));
            field.RegisterValueChangedCallback(evt => Edit("Изменить ассет", () => set(options[field.index]), true));
            Add(field);
        }

        private void BuildProperties()
        {
            if (properties == null || catalog == null) return;
            properties.Clear();
            propertyTarget = null;
            ArtAssetDefinition asset = Selected;
            if (asset == null)
            {
                Heading("Ассет не выбран");
                Note("Выберите объект в галерее или на холсте. Новые ассеты — «Загрузить папку…»: всё подхватится само — ракурсы, кадры и нормали.");
                Help("Папка объекта, как у Базы анимаций:\n" +
                     "• Трава/Front/…png, Трава/Back/…png — ракурсы папками;\n" +
                     "• Трава/Idle/Front/Idle_Front_0001.png — рендер KS Sprite Renderer (номера — кадры);\n" +
                     "• трава_Front.png, трава__Front__20261009-142811-641.png — ракурс в имени (из нескольких рендеров берётся самый свежий).\n" +
                     "Нормаль — рядом с рисунком: …_n.png, …_normal.png или папка Normal. Ракурсы: Front, Front_Right, Back_Right, Back, Back_Left, Front_Left (или по-русски).",
                    HelpBoxMessageType.None);
                return;
            }
            Heading(asset.Name);
            Note(ArtAssetDrawing.Completeness(asset) + " · используется: " + UsageCount(asset.Id));
            Row((state.Mode == ArtAssetCenterMode.Card ? "К галерее" : "Открыть карточку",
                    () => SetMode(state.Mode == ArtAssetCenterMode.Card ? ArtAssetCenterMode.Gallery : ArtAssetCenterMode.Card)),
                ("Удалить…", () => DeleteAsset(asset)));
            BuildSourceRow(asset);

            Section("Общее", true);
            TextProperty("Название", asset.Name, value => { asset.Name = value; visibleDirty = true; });
            ChoiceProperty("Категория", ArtAssetLabels.Categories, ArtAssetLabels.CategoryTitle, asset.Category,
                value => { asset.Category = value; visibleDirty = true; BuildCategories(); BuildCanvasBar(); });
            TextProperty("Теги (через запятую)", asset.TagsText, value => { asset.TagsText = value; visibleDirty = true; });
            BoolProperty("Избранное", asset.Favorite, value => { asset.Favorite = value; visibleDirty = true; });
            bool hasReference = asset.TryReferenceView(out ArtAssetView reference);
            if (hasReference)
                FloatProperty("Высота, ед. мира", asset.Height, value => asset.Height = Mathf.Max(.05f, value),
                    "Высота ракурса «" + ArtAssetLabels.ViewTitle(reference) + "». Масштаб общий для всех ракурсов и частей. Человек в месте ≈ " +
                    PersonHeight().ToString("0.##") + " ед.; 108 пикселей рисунка места = 1 ед.", true);
            FloatProperty("Пикселей на ед. мира", asset.PixelsPerUnit, value => asset.PixelsPerUnit = Mathf.Max(.5f, value),
                "Чем больше, тем меньше объект в месте. Прозрачные поля PNG масштаб не меняют.", true);
            EndSection();

            Section("В местах по умолчанию", false);
            Note("Экземпляр в месте берёт эти значения, пока для него не включено «Настроить для этого экземпляра».");
            BoolProperty("Перекрывает проход (и в бою — стена)", asset.BlocksMovement, value => asset.BlocksMovement = value,
                "Основание предмета непроходимо: в исследовании его обходят, в бою на месте гексы с центром на основании — стены.");
            BoolProperty("Перекрывает свет местных источников", asset.OccludesLight, value => asset.OccludesLight = value,
                "Тень по контуру от огня. Для травы и мелочи выключите — сотни перекрытий дороги.");
            FloatProperty("Длина тени (множитель)", asset.ShadowLength, value => asset.ShadowLength = Mathf.Max(0, value));
            BoolProperty("Освещается солнцем (смена суток)", asset.LitBySun, value => asset.LitBySun = value,
                "Выключено — рисунок не темнеет ночью и не меняет цвет со временем суток: всегда как днём (светящийся гриб, огонь, вывеска).");
            BoolProperty("Освещается огнём (местные источники)", asset.LitByFire, value => asset.LitByFire = value,
                "Выключено — костёр, факел и другие местные источники рисунок не освещают (и его нормали не работают).");
            EndSection();

            ArtAssetView view = CardView;
            BuildAnimationProperties(asset, view);

            Section("Ракурс «" + ArtAssetLabels.ViewTitle(view) + "»", true, "view");
            ArtAssetViewSettings settings = asset.Settings(view);
            VectorProperty("Опора (доля рисунка, Y вверх)", settings.Pivot, value => settings.Pivot = value,
                "Точка касания земли: 0 — левый/нижний край, 1 — правый/верхний. Мышью — в карточке, инструмент «Опора».");
            VectorProperty("Основание: размер", settings.FootprintSize, value => settings.FootprintSize = Vector2.Max(Vector2.zero, value),
                "Занятая земля, единицы мира. Мышью — в карточке, инструмент «Основание».");
            VectorProperty("Основание: смещение", settings.FootprintOffset, value => settings.FootprintOffset = value, "Центр основания от опоры, единицы мира (Y — вглубь)");
            Row(("Опору и основание — во все ракурсы", () => Edit("Опора во все ракурсы", () =>
            {
                foreach (ArtAssetView other in ArtAssetLabels.Views)
                {
                    if (other == view) continue;
                    ArtAssetViewSettings target = asset.Settings(other);
                    target.Pivot = settings.Pivot; target.FootprintSize = settings.FootprintSize; target.FootprintOffset = settings.FootprintOffset;
                }
            })));
            EndSection();

            BuildPartProperties(asset, view);
            if (state.Mode == ArtAssetCenterMode.Card && state.CardDisplay == ArtAssetCardDisplay.Lit) BuildLightProperties();

            Section("Подпись и источник", false);
            VisualElement idRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            idRow.Add(new Label("ID: " + asset.Id) { style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } });
            idRow.Add(new Button(() => { EditorGUIUtility.systemCopyBuffer = asset.Id; status.text = "ID скопирован."; }) { text = "Копировать" });
            Add(idRow);
            TextProperty("Подпись / автор", asset.Credit, value => asset.Credit = value);
            TextProperty("Источник / лицензия", asset.SourceUrl, value => asset.SourceUrl = value);
            EndSection();

            List<ArtAssetIssue> issues = ArtAssetValidator.Validate(catalog, asset);
            int serious = issues.Count(issue => issue.Level != ArtAssetIssueLevel.Info);
            Section(serious > 0 ? "Проверка · замечаний " + serious : "Проверка · в порядке", serious > 0, "check");
            if (issues.Count == 0) Note("Замечаний нет.");
            foreach (ArtAssetIssue issue in issues.Take(20))
                Help(issue.Text, issue.Level == ArtAssetIssueLevel.Error ? HelpBoxMessageType.Error : issue.Level == ArtAssetIssueLevel.Warning ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
            Row(("Исправить подключения нормалей", () =>
            {
                int count = 0;
                Edit("Исправить подключения нормалей", () => count = ArtAssetValidator.RepairNormals(asset));
                status.text = count > 0 ? "Исправлено подключений нормалей: " + count + "." : "Все нормали уже подключены.";
                litDirty = true;
                BuildProperties();
            }));
            if (issues.Any(issue => issue.Text.Contains("гамма-коррекцией")))
                Row(("Исправить гамму нормалей", () =>
                {
                    if (!EditorUtility.DisplayDialog("Исправить гамму нормалей?",
                            "Карты нормалей этого ассета в проекте будут пересчитаны из sRGB в линейные числа (файлы переписываются, GUID сохраняется). " +
                            "Ваши исходные файлы вне проекта не меняются. Для новых рендеров лучше поставить в Blender View Transform = Raw.", "Исправить", "Отмена"))
                        return;
                    int count = ArtAssetValidator.FixNormalGamma(asset);
                    status.text = "Исправлено карт нормалей: " + count + ".";
                    litDirty = true;
                    catalog.MarkChanged();
                    BuildProperties();
                }));
            EndSection();

            List<ArtAssetUsage> usages = ArtAssetUsages.Find(asset.Id);
            Section("Где используется · " + usages.Count, false, "usages");
            if (usages.Count == 0) Note("Нигде. Перетащите ассет в окно Базы локаций или выберите его там кнопкой «+ Из Базы ассетов».");
            foreach (ArtAssetUsage usage in usages.Take(40))
            {
                Button button = new Button(() => usage.Open?.Invoke()) { text = usage.Title, tooltip = "Перейти к записи" };
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                Add(button);
            }
            EndSection();
        }

        // Папка источника: «Обновить из папки» перечитывает её (новые рендеры,
        // кадры, нормали) — ID и размещения в местах сохраняются.
        private void BuildSourceRow(ArtAssetDefinition asset)
        {
            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, alignItems = Align.Center, marginTop = 2 } };
            bool known = !string.IsNullOrEmpty(asset.SourceFolder);
            bool exists = known && Directory.Exists(asset.SourceFolder);
            if (known)
            {
                Label folder = new Label("Папка: " + Path.GetFileName(asset.SourceFolder.TrimEnd('/', '\\')) + (exists ? "" : " (не найдена)"))
                { tooltip = asset.SourceFolder };
                folder.style.color = exists ? new Color(.7f, .75f, .7f) : new Color(.95f, .6f, .4f);
                folder.style.marginRight = 6;
                row.Add(folder);
                Button refresh = new Button(() => RefreshFromFolder(asset)) { text = "Обновить из папки", tooltip = "Перечитать " + asset.SourceFolder + ": рисунки, кадры и нормали заменятся новыми." };
                refresh.SetEnabled(exists);
                row.Add(refresh);
            }
            row.Add(new Button(() =>
            {
                string folder = EditorUtility.OpenFolderPanel("Папка рисунков «" + asset.Name + "»", exists ? asset.SourceFolder : LastFolder, "");
                if (string.IsNullOrEmpty(folder)) return;
                LastFolder = Path.GetDirectoryName(folder) ?? string.Empty;
                ImportPaths(new List<string> { folder }, asset, cardView);
            }) { text = known ? "Другая папка…" : "Загрузить из папки…", tooltip = "Все файлы папки — в этот ассет: ракурсы, кадры и нормали по именам." });
            row.Add(new Button(() => PickNormalFolder(asset))
            {
                text = "Нормали папкой…",
                tooltip = "Как в Базе анимаций: папка нормалей с теми же ракурсами (Front, Back… — папками или в имени) и кадрами; имена любые, суффикс «_normal» не нужен. " +
                          "Можно бросить папку на правую половину ячейки ракурса в карточке."
            });
            Add(row);
        }

        private void PickNormalFolder(ArtAssetDefinition asset)
        {
            string folder = EditorUtility.OpenFolderPanel("Папка нормалей «" + asset.Name + "» (ракурсы и кадры как у рисунков)", LastFolder, "");
            if (string.IsNullOrEmpty(folder)) return;
            LastFolder = Path.GetDirectoryName(folder) ?? string.Empty;
            AttachNormalFolder(asset, new List<string> { folder });
        }

        // Нормали папкой: всё в папке — нормали этого ассета; ракурс — по папке
        // или имени (без ракурса — выбранный в карточке), кадры — по номерам.
        private void AttachNormalFolder(ArtAssetDefinition asset, List<string> paths)
        {
            try
            {
                status.text = ArtAssetImporter.AttachNormalFolder(catalog, asset, paths, cardView, out int attached);
                if (attached > 0) { litDirty = true; visibleDirty = true; saveAt = EditorApplication.timeSinceStartup + .3; }
            }
            catch (Exception exception)
            {
                status.text = "Нормали не подключены: " + exception.Message;
            }
            BuildProperties();
            center?.MarkDirtyRepaint();
        }

        private void RefreshFromFolder(ArtAssetDefinition asset)
        {
            if (string.IsNullOrEmpty(asset.SourceFolder) || !Directory.Exists(asset.SourceFolder)) { status.text = "Папка источника не найдена."; return; }
            // В папке может быть несколько объектов — берётся свой (по имени источника).
            ArtAssetImportPlan plan = ArtAssetImportParser.ParsePaths(new[] { asset.SourceFolder }, null, ArtAssetView.Front);
            ArtAssetImportGroup own = plan.Groups.Count == 1 ? plan.Groups[0]
                : plan.Groups.FirstOrDefault(group => string.Equals(group.Key, asset.ImportKey, StringComparison.OrdinalIgnoreCase) ||
                                                      string.Equals(group.Key, ArtAssetImportParser.NormalizeKey(asset.Name), StringComparison.OrdinalIgnoreCase));
            if (own == null)
            {
                status.text = "В папке нет объекта «" + asset.Name + "» — выберите папку кнопкой «Другая папка…».";
                return;
            }
            ArtAssetImportPlan single = new ArtAssetImportPlan();
            single.Groups.Add(own);
            ArtAssetImportResult result = ArtAssetImporter.Import(catalog, single, _ => asset);
            visibleDirty = true;
            litDirty = true;
            SelectAsset(result.Updated.FirstOrDefault() ?? asset.Id);
            status.text = "Обновлено из папки: " + result.Summary + (own.SkippedOlder > 0 ? " Старых рендеров пропущено: " + own.SkippedOlder + "." : "") +
                          (result.Warnings.Count > 0 ? " ⚠ " + string.Join(" ", result.Warnings.Take(3)) : "") +
                          (result.Errors.Count > 0 ? " Ошибки: " + string.Join("; ", result.Errors.Take(3)) : "");
        }

        private void BuildPartProperties(ArtAssetDefinition asset, ArtAssetView view)
        {
            Section("Части объекта · " + asset.Parts.Count, asset.Parts.Count > 1, "parts");
            Note("Обычному предмету хватает основы. Дому — «Основа» и «Крыша», дереву — «Ствол» и «Крона»: части меняют ракурс вместе и ставятся в место одним ассетом.");
            VisualElement list = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            for (int i = 0; i < asset.Parts.Count; i++)
            {
                int index = i;
                ArtAssetPart item = asset.Parts[i];
                if (item == null) continue;
                Button button = new Button(() => { cardPart = index; BuildProperties(); center.MarkDirtyRepaint(); })
                { text = (index == cardPart ? "● " : "") + item.Name + (index == 0 ? " (основа)" : "") };
                list.Add(button);
            }
            Add(list);
            Row(("+ Крыша", () => AddPart(asset, "Крыша")), ("+ Крона", () => AddPart(asset, "Крона")), ("+ Ствол", () => AddPart(asset, "Ствол")),
                ("+ Своя часть", () => AddPart(asset, "Часть " + asset.Parts.Count)));
            cardPart = Mathf.Clamp(cardPart, 0, asset.Parts.Count - 1);
            ArtAssetPart part = asset.Parts[cardPart];
            if (part == null) { EndSection(); return; }
            TextProperty("Название части", part.Name, value => part.Name = string.IsNullOrWhiteSpace(value) ? part.Name : value.Trim());
            ChoiceProperty("Слой", ArtAssetLabels.Layers, ArtAssetLabels.LayerTitle, part.Layer, value => part.Layer = value);
            if (part.Layer == ArtAssetLayer.Foreground)
                Note("Передний план всегда перекрывает героя. Если перекрытие должно зависеть от положения героя — оставьте «Объекты и персонажи».");
            IntProperty("Порядок внутри слоя", part.OrderOffset, value => part.OrderOffset = value);
            BoolProperty("Отбрасывает тень-силуэт", part.ProjectsShadow, value => part.ProjectsShadow = value);
            ArtAssetPartView slot = part.View(view);
            if (cardPart > 0)
                VectorProperty("Смещение в ракурсе «" + ArtAssetLabels.ViewTitle(view) + "»", slot.Offset, value => slot.Offset = value,
                    "Левый нижний угол рисунка части от угла рисунка основы, единицы мира. Слои одного кадра — 0.");
            ObjectField shadow = new ObjectField("Свой силуэт тени («" + ArtAssetLabels.ViewTitle(view) + "»)") { objectType = typeof(Sprite), allowSceneObjects = false, value = slot.ShadowSprite };
            shadow.RegisterValueChangedCallback(evt => Edit("Силуэт тени", () => slot.ShadowSprite = evt.newValue as Sprite));
            Add(shadow);
            if (cardPart > 0)
                Row(("Удалить часть «" + part.Name + "»", () =>
                {
                    if (!EditorUtility.DisplayDialog("Удалить часть?", "Часть «" + part.Name + "» будет снята со всех ракурсов. Файлы останутся.", "Удалить", "Отмена")) return;
                    Edit("Удалить часть", () => asset.Parts.Remove(part), true);
                    cardPart = 0;
                    BuildProperties();
                }));
            EndSection();
        }

        // ПР-12П: покадровая анимация — кадры приходят импортом (номер в конце
        // имени файла), здесь — скорость, порядок и фаза.
        private void BuildAnimationProperties(ArtAssetDefinition asset, ArtAssetView view)
        {
            if (!asset.IsAnimated)
            {
                Note("Анимации нет — неподвижный рисунок.",
                    "Чтобы трава качалась, загрузите кадры: номер в конце имени файла — Трава/Front/000.png, 001.png … или трава_Front_000.png, трава_Front_001.png " +
                    "(нормали — трава_Front_000_n.png). Рендер KS Sprite Renderer (Трава/Idle/Front/Idle_Front_0001.png) подхватывается папкой целиком.");
                return;
            }
            Section("Анимация", true);
            List<string> counts = new List<string>();
            foreach (ArtAssetView item in ArtAssetLabels.Views)
            {
                int frames = asset.FrameCountIn(item);
                if (frames > 1) counts.Add(ArtAssetLabels.ViewTitle(item) + " — " + frames);
            }
            Note("Кадров: " + string.Join(", ", counts));
            // Лист кадров: места показывают анимацию из него (одна текстура на кадры).
            List<ArtAssetPartView> animated = asset.Parts.Where(item => item != null).SelectMany(item => ArtAssetLabels.Views.Select(item.FindView))
                .Where(slot => slot != null && slot.IsAnimated).ToList();
            int stale = animated.Count(ArtAssetSheets.IsStale);
            int pages = animated.Where(slot => slot.HasSheet).SelectMany(slot => slot.SheetFrames).Select(sprite => sprite.texture).Distinct().Count();
            Note(stale == 0 ? "Листы кадров собраны: страниц " + pages + "." : "Листов кадров не собрано: " + stale + " из " + animated.Count + " — анимация идёт отдельными рисунками.",
                "Кадры ракурса складываются на страницы (до 4096×4096) вместе с нормалями — как в Базе анимаций; в местах кадры одной страницы рисуются одной отрисовкой. " +
                "Лист собирается сам при загрузке и правке кадров; отдельные кадры остаются для правки.");
            Row((stale > 0 ? "Собрать листы кадров" : "Пересобрать листы кадров", () =>
            {
                List<string> warnings = new List<string>();
                int built = ArtAssetSheets.Refresh(catalog, asset, warnings, stale == 0);
                litDirty = true;
                status.text = "Листов собрано: " + built + "." + (warnings.Count > 0 ? " ⚠ " + string.Join(" ", warnings.Take(3)) : "");
                BuildProperties();
            }));
            FloatProperty("Кадров в секунду", asset.FramesPerSecond, value => asset.FramesPerSecond = Mathf.Clamp(value, .1f, 60),
                "Общая скорость для всех частей и ракурсов.");
            ChoiceProperty("Порядок кадров", new[] { ArtAssetPlayback.Loop, ArtAssetPlayback.PingPong }, ArtAssetAnimation.PlaybackTitle, asset.Playback,
                value => asset.Playback = value);
            BoolProperty("Своя фаза у каждого экземпляра", asset.RandomPhase, value => asset.RandomPhase = value,
                "Включено — одинаковые кусты в заросли качаются вразнобой (сдвиг по ID экземпляра, при каждом запуске один и тот же).");
            cardPart = Mathf.Clamp(cardPart, 0, asset.Parts.Count - 1);
            ArtAssetPart part = asset.Parts[cardPart];
            ArtAssetPartView slot = part?.FindView(view);
            if (slot != null && slot.IsAnimated)
                Row(("Снять кадры «" + ArtAssetLabels.ViewTitle(view) + "»" + (asset.Parts.Count > 1 ? " · " + part.Name : "") + " (оставить первый)", () =>
                {
                    ArtAssetImporter.ClearFrames(catalog, part, view);
                    status.text = "Кадры сняты, остался первый рисунок (файлы не удалены).";
                    litDirty = true;
                    BuildProperties();
                }));
            EndSection();
        }

        private void AddPart(ArtAssetDefinition asset, string name)
        {
            Edit("Добавить часть", () =>
            {
                ArtAssetPart part = new ArtAssetPart { Name = name, Layer = ArtAssetLayer.World, OrderOffset = asset.Parts.Count };
                asset.Parts.Add(part);
                cardPart = asset.Parts.Count - 1;
            });
            state.Mode = ArtAssetCenterMode.Card;
            BuildModeBar();
            BuildProperties();
            status.text = "Часть «" + name + "» добавлена. Перетащите её рисунки в ячейки ракурсов.";
        }

        private void BuildLightProperties()
        {
            Section("Под светом", true, "lit");
            Note("Нормали действуют только от местного источника: дневной свет освещает ровно и их заглушает — смотрите ночью.",
                "Включите «Свет по кругу» или тащите источник инструментом «Свет» и переключайте «Учитывать карты нормалей»: с нормалями проявляется объём.");
            Toggle enabled = new Toggle("Контрольный свет (точечный)") { value = state.LightEnabled, tooltip = "Выключено — источника нет: виден только общий свет дня или ночи." };
            enabled.RegisterValueChangedCallback(evt => { state.LightEnabled = evt.newValue; litDirty = true; ScheduleStateSave(); BuildProperties(); });
            Add(enabled);
            Toggle orbit = new Toggle("Свет по кругу") { value = state.LightOrbit, tooltip = "Источник ходит вокруг объекта — видно, как нормали лепят объём" };
            orbit.RegisterValueChangedCallback(evt => { state.LightOrbit = evt.newValue; ScheduleStateSave(); });
            Add(orbit);
            void Number(string label, float value, float min, float max, Action<float> set, string tip = null)
            {
                Slider slider = new Slider(label, min, max) { value = value, showInputField = true, tooltip = tip };
                slider.RegisterValueChangedCallback(evt => { set(evt.newValue); litDirty = true; ScheduleStateSave(); });
                Add(slider);
            }
            Number("Яркость", state.LightIntensity, 0, 4, value => state.LightIntensity = value);
            Number("Высота над землёй", state.LightHeight, 0, 6, value => state.LightHeight = value);
            Number("Расстояние для нормалей", state.LightNormalDistance, .2f, 6, value => state.LightNormalDistance = value,
                "Меньше расстояние — резче рельеф и сильнее светотень; больше — мягче, ровнее.");
            Toggle normals = new Toggle("Учитывать карты нормалей") { value = state.LightNormals };
            normals.RegisterValueChangedCallback(evt => { state.LightNormals = evt.newValue; litDirty = true; ScheduleStateSave(); });
            Add(normals);
            Row(("День", () => { state.LightNight = false; litDirty = true; ScheduleStateSave(); }), ("Ночь", () => { state.LightNight = true; litDirty = true; ScheduleStateSave(); }));
            EndSection();
        }
    }
}
