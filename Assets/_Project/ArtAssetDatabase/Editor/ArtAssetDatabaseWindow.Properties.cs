using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Правая панель: свойства выбранного ассета, ракурса и части.
    public sealed partial class ArtAssetDatabaseWindow
    {
        private void Heading(string text)
        {
            Label label = SectionTitle(text);
            label.style.fontSize = 13;
            label.style.marginTop = 12;
            properties.Add(label);
        }

        private void Help(string text, HelpBoxMessageType type = HelpBoxMessageType.Info) => properties.Add(new HelpBox(text, type));

        private void Row(params (string title, Action action)[] buttons)
        {
            VisualElement row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            foreach ((string title, Action action) in buttons) row.Add(new Button(action) { text = title });
            properties.Add(row);
        }

        private void TextProperty(string label, string value, Action<string> set, string undo = "Изменить ассет")
        {
            TextField field = new TextField(label) { value = value ?? string.Empty, isDelayed = true };
            field.RegisterValueChangedCallback(evt => Edit(undo, () => set(evt.newValue)));
            properties.Add(field);
        }

        private void BoolProperty(string label, bool value, Action<bool> set, string tip = null)
        {
            Toggle field = new Toggle(label) { value = value, tooltip = tip };
            field.RegisterValueChangedCallback(evt => Edit("Изменить ассет", () => set(evt.newValue)));
            properties.Add(field);
        }

        private void FloatProperty(string label, float value, Action<float> set, string tip = null, bool rebuild = false)
        {
            FloatField field = new FloatField(label) { value = value, isDelayed = true, tooltip = tip };
            field.RegisterValueChangedCallback(evt => Edit("Изменить ассет", () => set(evt.newValue), rebuild));
            properties.Add(field);
        }

        private void IntProperty(string label, int value, Action<int> set)
        {
            IntegerField field = new IntegerField(label) { value = value, isDelayed = true };
            field.RegisterValueChangedCallback(evt => Edit("Изменить ассет", () => set(evt.newValue)));
            properties.Add(field);
        }

        private void VectorProperty(string label, Vector2 value, Action<Vector2> set, string tip = null)
        {
            Vector2Field field = new Vector2Field(label) { value = value, tooltip = tip };
            field.RegisterValueChangedCallback(evt => Edit("Изменить ракурс", () => set(evt.newValue)));
            properties.Add(field);
        }

        private void ChoiceProperty<T>(string label, IList<T> options, Func<T, string> name, T current, Action<T> set)
        {
            List<string> names = options.Select(name).ToList();
            PopupField<string> field = new PopupField<string>(label, names, Mathf.Max(0, options.IndexOf(current)));
            field.RegisterValueChangedCallback(evt => Edit("Изменить ассет", () => set(options[field.index]), true));
            properties.Add(field);
        }

        private void BuildProperties()
        {
            if (properties == null || catalog == null) return;
            properties.Clear();
            ArtAssetDefinition asset = Selected;
            if (asset == null)
            {
                Heading("Ассет не выбран");
                Help("Выберите объект на холсте или в галерее. Новый ассет — «+ Ассет», «Загрузить папку» или перетаскивание файлов и папок в окно.");
                Help("Папка объекта: Телега/Front/color.png и Телега/Front/normal.png … для шести ракурсов (Front, Front_Right, Back_Right, Back, Back_Left, Front_Left). " +
                     "Также распознаются пары cart_Front.png / cart_Front_normal.png и русские имена ракурсов («Спереди», «Сзади слева»…).");
                return;
            }
            Heading(asset.Name);
            Label summary = new Label(ArtAssetDrawing.Completeness(asset) + " · используется: " + UsageCount(asset.Id));
            summary.style.color = new Color(.75f, .8f, .75f);
            properties.Add(summary);
            Row((state.Mode == ArtAssetCenterMode.Card ? "К холсту" : "Открыть карточку",
                    () => SetMode(state.Mode == ArtAssetCenterMode.Card ? ArtAssetCenterMode.Canvas : ArtAssetCenterMode.Card)),
                ("Удалить…", () => DeleteAsset(asset)));

            Heading("Общее");
            TextProperty("Название", asset.Name, value => { asset.Name = value; visibleDirty = true; });
            VisualElement idRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            idRow.Add(new Label("ID: " + asset.Id) { style = { flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft } });
            idRow.Add(new Button(() => { EditorGUIUtility.systemCopyBuffer = asset.Id; status.text = "ID скопирован."; }) { text = "Копировать" });
            properties.Add(idRow);
            ChoiceProperty("Категория", ArtAssetLabels.Categories, ArtAssetLabels.CategoryTitle, asset.Category,
                value => { asset.Category = value; visibleDirty = true; BuildCategories(); });
            TextProperty("Теги (через запятую)", asset.TagsText, value => { asset.TagsText = value; visibleDirty = true; });
            BoolProperty("Избранное", asset.Favorite, value => { asset.Favorite = value; visibleDirty = true; });
            TextProperty("Подпись / автор", asset.Credit, value => asset.Credit = value);
            TextProperty("Источник / лицензия", asset.SourceUrl, value => asset.SourceUrl = value);

            Heading("Игровой размер");
            bool hasReference = asset.TryReferenceView(out ArtAssetView reference);
            if (hasReference)
            {
                FloatProperty("Высота («" + ArtAssetLabels.ViewTitle(reference) + "»)", asset.Height, value => asset.Height = Mathf.Max(.05f, value),
                    "Единицы мира. Масштаб общий для всех ракурсов и частей.", true);
                Label person = new Label("Человек в месте ≈ " + PersonHeight().ToString("0.##") + " ед. мира · 108 пикселей рисунка места = 1 ед.");
                person.style.color = new Color(.7f, .75f, .7f);
                person.style.whiteSpace = WhiteSpace.Normal;
                properties.Add(person);
            }
            else Help("Размер задаётся после загрузки первого рисунка основы.");
            FloatProperty("Пикселей рисунка на единицу мира", asset.PixelsPerUnit, value => asset.PixelsPerUnit = Mathf.Max(.5f, value),
                "Чем больше, тем меньше объект в месте. Прозрачные поля PNG масштаб не меняют.", true);

            Heading("Рекомендации для мест");
            Help("Экземпляр в месте берёт эти значения, пока для него не включено «Настроить для этого экземпляра».", HelpBoxMessageType.None);
            BoolProperty("Блокирует проход (по основанию)", asset.BlocksMovement, value => asset.BlocksMovement = value);
            BoolProperty("Перекрывает свет местных источников", asset.OccludesLight, value => asset.OccludesLight = value);
            FloatProperty("Длина тени (множитель)", asset.ShadowLength, value => asset.ShadowLength = Mathf.Max(0, value));

            ArtAssetView view = CardView;
            Heading("Ракурс «" + ArtAssetLabels.ViewTitle(view) + "»");
            if (state.Mode != ArtAssetCenterMode.Card) Help("Опору и основание удобно ставить мышью в карточке (инструменты «Опора» и «Основание»).", HelpBoxMessageType.None);
            ArtAssetViewSettings settings = asset.Settings(view);
            VectorProperty("Опора (доля рисунка, Y вверх)", settings.Pivot, value => settings.Pivot = value, "Точка касания земли: 0 — левый/нижний край, 1 — правый/верхний");
            VectorProperty("Основание: размер", settings.FootprintSize, value => settings.FootprintSize = Vector2.Max(Vector2.zero, value), "Занятая земля, единицы мира");
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

            BuildPartProperties(asset, view);
            if (state.Mode == ArtAssetCenterMode.Card && state.CardDisplay == ArtAssetCardDisplay.Lit) BuildLightProperties();

            Heading("Проверка");
            List<ArtAssetIssue> issues = ArtAssetValidator.Validate(catalog, asset);
            if (issues.Count == 0) Help("Замечаний нет.", HelpBoxMessageType.None);
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

            Heading("Где используется");
            List<ArtAssetUsage> usages = ArtAssetUsages.Find(asset.Id);
            if (usages.Count == 0) Help("Нигде. Перетащите ассет в окно Базы локаций или выберите его там кнопкой «+ Из Базы ассетов».", HelpBoxMessageType.None);
            foreach (ArtAssetUsage usage in usages.Take(40))
            {
                Button button = new Button(() => usage.Open?.Invoke()) { text = usage.Title, tooltip = "Перейти к записи" };
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                properties.Add(button);
            }
        }

        private void BuildPartProperties(ArtAssetDefinition asset, ArtAssetView view)
        {
            Heading("Части объекта");
            Help("Обычному предмету хватает основы. Дому — «Основа» и «Крыша», дереву — «Ствол» и «Крона». Части меняют ракурс вместе и переносятся в место одним ассетом.",
                HelpBoxMessageType.None);
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
            properties.Add(list);
            Row(("+ Крыша", () => AddPart(asset, "Крыша")), ("+ Крона", () => AddPart(asset, "Крона")), ("+ Ствол", () => AddPart(asset, "Ствол")),
                ("+ Своя часть", () => AddPart(asset, "Часть " + asset.Parts.Count)));
            cardPart = Mathf.Clamp(cardPart, 0, asset.Parts.Count - 1);
            ArtAssetPart part = asset.Parts[cardPart];
            if (part == null) return;
            TextProperty("Название части", part.Name, value => part.Name = string.IsNullOrWhiteSpace(value) ? part.Name : value.Trim());
            ChoiceProperty("Слой", ArtAssetLabels.Layers, ArtAssetLabels.LayerTitle, part.Layer, value => part.Layer = value);
            if (part.Layer == ArtAssetLayer.Foreground)
                Help("Передний план всегда перекрывает героя. Если перекрытие должно зависеть от положения героя — оставьте «Объекты и персонажи».", HelpBoxMessageType.None);
            IntProperty("Порядок внутри слоя", part.OrderOffset, value => part.OrderOffset = value);
            BoolProperty("Отбрасывает тень-силуэт", part.ProjectsShadow, value => part.ProjectsShadow = value);
            ArtAssetPartView slot = part.View(view);
            if (cardPart > 0)
                VectorProperty("Смещение в ракурсе «" + ArtAssetLabels.ViewTitle(view) + "»", slot.Offset, value => slot.Offset = value,
                    "Левый нижний угол рисунка части от угла рисунка основы, единицы мира. Слои одного кадра — 0.");
            ObjectField shadow = new ObjectField("Свой силуэт тени («" + ArtAssetLabels.ViewTitle(view) + "»)") { objectType = typeof(Sprite), allowSceneObjects = false, value = slot.ShadowSprite };
            shadow.RegisterValueChangedCallback(evt => Edit("Силуэт тени", () => slot.ShadowSprite = evt.newValue as Sprite));
            properties.Add(shadow);
            if (cardPart > 0)
                Row(("Удалить часть «" + part.Name + "»", () =>
                {
                    if (!EditorUtility.DisplayDialog("Удалить часть?", "Часть «" + part.Name + "» будет снята со всех ракурсов. Файлы останутся.", "Удалить", "Отмена")) return;
                    Edit("Удалить часть", () => asset.Parts.Remove(part), true);
                    cardPart = 0;
                    BuildProperties();
                }));
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
            Heading("Под светом");
            Help("Контрольный источник — общими средствами рендерера мест. Нормали действуют только от местного источника: " +
                 "общий дневной свет освещает рисунок ровно и их заглушает — смотрите ночью. Включите «Свет по кругу» или тащите " +
                 "источник инструментом «Свет» и переключайте «Учитывать карты нормалей»: с нормалями проявляется объём.", HelpBoxMessageType.None);
            Toggle orbit = new Toggle("Свет по кругу") { value = state.LightOrbit, tooltip = "Источник ходит вокруг объекта — видно, как нормали лепят объём" };
            orbit.RegisterValueChangedCallback(evt => { state.LightOrbit = evt.newValue; ScheduleStateSave(); });
            properties.Add(orbit);
            void Number(string label, float value, float min, float max, Action<float> set)
            {
                Slider slider = new Slider(label, min, max) { value = value, showInputField = true };
                slider.RegisterValueChangedCallback(evt => { set(evt.newValue); litDirty = true; ScheduleStateSave(); });
                properties.Add(slider);
            }
            Number("Яркость", state.LightIntensity, 0, 4, value => state.LightIntensity = value);
            Number("Высота над землёй", state.LightHeight, 0, 6, value => state.LightHeight = value);
            Number("Расстояние для нормалей", state.LightNormalDistance, .2f, 6, value => state.LightNormalDistance = value);
            Label distanceHint = new Label("Меньше расстояние — резче рельеф и сильнее светотень; больше — мягче, ровнее.");
            distanceHint.style.whiteSpace = WhiteSpace.Normal;
            distanceHint.style.fontSize = 10;
            distanceHint.style.color = new Color(.65f, .7f, .65f);
            properties.Add(distanceHint);
            Toggle normals = new Toggle("Учитывать карты нормалей") { value = state.LightNormals };
            normals.RegisterValueChangedCallback(evt => { state.LightNormals = evt.newValue; litDirty = true; ScheduleStateSave(); });
            properties.Add(normals);
            Row(("День", () => { state.LightNight = false; litDirty = true; ScheduleStateSave(); }), ("Ночь", () => { state.LightNight = true; litDirty = true; ScheduleStateSave(); }));
        }
    }
}
