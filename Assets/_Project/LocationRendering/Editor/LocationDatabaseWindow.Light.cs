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
    // ПР-12М: вкладка «Свет» окна «База локаций» — общий свет (улица / под
    // крышей), солнце и тени, тени людей, все источники места с полным
    // набором 2D-света Unity, обработка кадра, готовые настройки.
    public sealed partial class LocationDatabaseWindow
    {
        private void BuildLightSettings(LocalLocationDefinition location)
        {
            LocationVisualDefinition visual = Visual;
            if (visual == null)
            {
                Help("Для света нужна художественная сборка места.");
                AddButton(settings, "Добавить сборку и свет", () => Change(() => database.visuals.Add(new LocationVisualDefinition { LocationId = selectedId }), true));
                return;
            }

            LocationSky sky = new LocationSky(visual, database.worldLighting);
            Heading("Готовые настройки");
            VisualElement presets = Row();
            foreach (LocationLightPreset preset in LocationLightPresets.All)
                AddButton(presets, preset.Name, () => Change(() => preset.Apply(visual, sky), true));
            Help(sky.IsWorld
                ? "Готовая настройка меняет «улица / под крышей» этого места и ОБЩЕЕ небо мира (все места с общим светом). Источники и предметы не трогает."
                : "Готовая настройка меняет «улица / под крышей» и своё небо этого места. Источники и предметы не трогает.");

            Heading("Предпросмотр");
            UnityEngine.UIElements.Toggle compare = new UnityEngine.UIElements.Toggle("Сравнить сутки (утро, день, вечер, ночь)") { value = compareDay };
            compare.RegisterValueChangedCallback(evt => compareDay = evt.newValue); settings.Add(compare);
            UnityEngine.UIElements.Toggle lights = new UnityEngine.UIElements.Toggle("Показывать источники и солнце") { value = showLights };
            lights.RegisterValueChangedCallback(evt => showLights = evt.newValue); settings.Add(lights);

            Heading("Общий свет");
            Choice("Где место", new List<LocationLightingMode> { LocationLightingMode.Outdoor, LocationLightingMode.Indoor },
                mode => mode == LocationLightingMode.Outdoor ? "Улица — свет по суткам, солнце" : "Под крышей — постоянный свет",
                visual.Lighting, mode => visual.Lighting = mode);
            if (visual.Lighting == LocationLightingMode.Indoor)
            {
                ColorField("Цвет", visual.IndoorColor, value => visual.IndoorColor = value);
                Number("Яркость", visual.IndoorIntensity, 0, 2, value => visual.IndoorIntensity = value);
                Help("Под крышей нет солнечных теней и смены суток; свет дают источники (огонь, вход, щели).");
            }
            Heading("Небо: сутки, солнце, тени, обработка кадра");
            Choice("Чьё небо", new List<bool> { true, false }, world => world ? "Общий свет мира (все места)" : "Свой для этого места",
                visual.UseWorldLighting, world =>
                {
                    // Своё небо начинается с копии общего: вид места не прыгает.
                    if (!world && visual.UseWorldLighting) LocationSky.CopyWorldToOwn(database.worldLighting, visual);
                    visual.UseWorldLighting = world;
                });
            if (sky.IsWorld)
                Help("Правки неба ниже меняют все места с общим светом мира. Солнце в мире одно — тени во всех местах совпадают по времени.");
            else
                AddButton(settings, "Скопировать общий свет мира сюда", () => Change(() => LocationSky.CopyWorldToOwn(database.worldLighting, visual), true));
            if (visual.Lighting == LocationLightingMode.Outdoor)
            {
                LocationDaylight daylight = sky.Daylight;
                Number("Общая яркость", daylight.Intensity, 0, 2, value => daylight.Intensity = value);
                CurveField curve = new CurveField("Яркость за сутки") { value = daylight.Brightness };
                curve.RegisterValueChangedCallback(evt => Change(() => daylight.Brightness = evt.newValue)); settings.Add(curve);
                GradientField gradient = new GradientField("Цвет за сутки") { value = daylight.Color };
                gradient.RegisterValueChangedCallback(evt => Change(() => daylight.Color = evt.newValue)); settings.Add(gradient);

                Heading("Солнце и тени");
                LocationSunDefinition sun = sky.Sun;
                Toggle("Солнечные тени", sun.Enabled, value => sun.Enabled = value);
                Number("Восход (час)", sun.Sunrise, 0, 24, value => sun.Sunrise = value);
                Number("Закат (час)", sun.Sunset, 0, 24, value => sun.Sunset = value);
                Number("Тень утром (угол)", sun.MorningAngle, 0, 360, value => sun.MorningAngle = value,
                    "Куда ложится тень на восходе: 0 — вправо, 90 — вверх (от зрителя), 180 — влево, 270 — вниз.");
                Number("Тень вечером (угол)", sun.EveningAngle, 0, 360, value => sun.EveningAngle = value);
                Number("Длина в полдень", sun.NoonLength, 0, 3, value => sun.NoonLength = value, "В долях высоты предмета.");
                Number("Длина на восходе и закате", sun.LowLength, 0, 5, value => sun.LowLength = value);
                Number("Темнота тени", sun.Opacity, 0, 1, value => sun.Opacity = value);
                Number("Мягкость края", sun.Softness, 0, 1, value => sun.Softness = value);
                ColorField("Цвет тени", sun.Color, value => sun.Color = value);
                Toggle("Лунные тени ночью", sun.MoonShadows, value => sun.MoonShadows = value, true);
                if (sun.MoonShadows)
                {
                    Number("Луна: угол тени", sun.MoonAngle, 0, 360, value => sun.MoonAngle = value);
                    Number("Луна: длина", sun.MoonLength, 0, 3, value => sun.MoonLength = value);
                    Number("Луна: темнота", sun.MoonOpacity, 0, 1, value => sun.MoonOpacity = value);
                }
                Help("Направление и длина тени в текущий час видны стрелкой в левом верхнем углу предпросмотра.");
            }

            Heading("Стиль теней");
            Toggle("Люди отбрасывают тени (солнце и огонь)", sky.PeopleCastShadows, value => sky.PeopleCastShadows = value);
            Number("Длина тени людей (множитель)", sky.PeopleShadowLength, 0, 3, value => sky.PeopleShadowLength = value);
            Number("Наклон тени к земле (наименьший)", sky.ShadowMinLean, 0, 1.5f, value => sky.ShadowMinLean = value,
                "Тень всегда немного уходит «от зрителя», даже если огонь строго сбоку или солнце низко. 0 — честная проекция (сбоку — линия).");

            Heading("Источники света");
            VisualElement add = Row();
            AddButton(add, "+ Источник", () => AddLightSource("Источник света", LocationLightShape.Point));
            AddButton(add, "+ Конус", () => AddLightSource("Конус света", LocationLightShape.Spot));
            AddButton(add, "+ Свет формы", () => AddLightSource("Свет формы", LocationLightShape.Freeform));
            AddButton(add, "+ Свет-рисунок", () => AddLightSource("Свет-рисунок", LocationLightShape.Sprite));
            AddButton(add, "+ Костёр", () => AddArtObject(null, LocationPlaceholder.Fire));
            foreach (LocationVisualObject item in visual.Objects.Where(item => item.Light.Enabled || item.LightOnly))
                SelectButton(Kind.Art, item.Id, (item.LightOnly ? "☀ " : "▣ ") + item.Name + " · " + ShapeName(item.Light.Shape));
            LocationVisualObject selected = ArtObject;
            if (selected != null && (selected.Light.Enabled || selected.LightOnly))
            {
                Heading(selected.Name);
                Text("Название", selected.Name, value => selected.Name = value);
                LightEditor(selected);
                AddButton(settings, "Удалить", () => Change(() => { visual.Objects.Remove(selected); selectedKind = Kind.None; }, true));
            }

            BuildPostSettings(sky.Post);
        }

        private VisualElement Row()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            settings.Add(row);
            return row;
        }

        private static string ShapeName(LocationLightShape shape)
        {
            switch (shape)
            {
                case LocationLightShape.Spot: return "конус";
                case LocationLightShape.Freeform: return "своя форма";
                case LocationLightShape.Sprite: return "свет-рисунок";
                default: return "точка";
            }
        }

        private void AddLightSource(string name, LocationLightShape shape)
        {
            LocalLocationDefinition location = Location;
            if (Visual == null) return;
            Change(() =>
            {
                LocationVisualObject item = new LocationVisualObject
                {
                    Name = name, LightOnly = true, ProjectsShadow = false,
                    Position = LocationVisualGeometry.ToNormalized(location, viewCenter)
                };
                item.Light.Enabled = true;
                item.Light.Shape = shape;
                item.Light.Color = new Color(1f, .9f, .75f);
                item.Light.Animation = LocationLightAnimation.None;
                Visual.Objects.Add(item);
                selectedKind = Kind.Art; selectedElementId = item.Id;
            }, true);
        }

        // Все настройки источника — для предмета и для «только света».
        private void LightEditor(LocationVisualObject owner)
        {
            LocationLightDefinition light = owner.Light;
            Toggle("Источник включён", light.Enabled, value => light.Enabled = value, true);
            if (!light.Enabled) return;
            Choice("Вид", new List<LocationLightShape> { LocationLightShape.Point, LocationLightShape.Spot, LocationLightShape.Freeform, LocationLightShape.Sprite },
                ShapeName, light.Shape, value => light.Shape = value);
            ColorField("Цвет", light.Color, value => light.Color = value);
            Number("Яркость", light.Intensity, 0, 5, value => light.Intensity = value);
            switch (light.Shape)
            {
                case LocationLightShape.Spot:
                    Number("Радиус", light.Radius, .1f, 15, value => light.Radius = value);
                    Number("Мягкость к краю", light.Softness, 0, 1, value => light.Softness = value);
                    Number("Направление (угол)", light.Direction, 0, 360, value => light.Direction = value, "0 — вправо, 90 — вверх, 270 — вниз.");
                    Number("Ширина конуса", light.OuterAngle, 1, 360, value => light.OuterAngle = value);
                    Number("Яркая середина", light.InnerAngle, 0, 360, value => light.InnerAngle = value);
                    break;
                case LocationLightShape.Freeform:
                    Number("Мягкость края", light.FreeformFalloff, 0, 3, value => light.FreeformFalloff = value);
                    AddButton(settings, "Править форму мышью", () => SetTool(Tool.LightShape));
                    AddButton(settings, "Сбросить форму", () => Change(() => light.FreeformPoints = new LocationLightDefinition().FreeformPoints));
                    Help("Инструмент «Форма света»: тянуть точку — переместить, клик у края — добавить точку, Shift+клик — удалить точку.");
                    break;
                case LocationLightShape.Sprite:
                    SpriteField("Рисунок света", light.Cookie, value => light.Cookie = value);
                    Vector2Field size = new Vector2Field("Размер (единицы мира)") { value = light.CookieSize };
                    size.RegisterValueChangedCallback(evt => Change(() => light.CookieSize = Vector2.Max(new Vector2(.1f, .1f), evt.newValue))); settings.Add(size);
                    Help("Свет-рисунок: пятна сквозь листву, решётка, свет из окна. Светлое — свет, тёмное — тень.");
                    break;
                default:
                    Number("Радиус", light.Radius, .1f, 15, value => light.Radius = value);
                    Number("Мягкость к краю", light.Softness, 0, 1, value => light.Softness = value);
                    break;
            }
            Number("Резкость спада", light.Falloff, 0, 1, value => light.Falloff = value);
            Vector2Field offset = new Vector2Field("Смещение источника") { value = light.Offset };
            offset.RegisterValueChangedCallback(evt => Change(() => light.Offset = evt.newValue)); settings.Add(offset);

            Heading("Жизнь огня");
            Choice("Как меняется", new List<LocationLightAnimation> { LocationLightAnimation.None, LocationLightAnimation.Flicker, LocationLightAnimation.Pulse, LocationLightAnimation.Strobe },
                AnimationName, light.Animation, value => light.Animation = value);
            if (light.Animation != LocationLightAnimation.None)
            {
                Number("Сила", light.Flicker, 0, .6f, value => light.Flicker = value);
                Number("Скорость", light.AnimationSpeed, .05f, 10, value => light.AnimationSpeed = value);
            }
            Toggle("По расписанию", light.NightOnly, value => light.NightOnly = value, true);
            if (light.NightOnly)
            {
                Number("Включить в", light.StartsAt, 0, 24, value => light.StartsAt = value);
                Number("Выключить в", light.EndsAt, 0, 24, value => light.EndsAt = value);
            }

            Heading("Тени от этого света");
            Toggle("Тени-силуэты предметов и людей", light.ProjectsShadows, value => light.ProjectsShadows = value, true);
            if (light.ProjectsShadows)
            {
                Number("Высота источника", light.Height, .2f, 6, value => light.Height = value, "Ниже — тени длиннее. Единицы мира.");
                Number("Темнота", light.ProjectedShadowOpacity, 0, 1, value => light.ProjectedShadowOpacity = value);
                Number("Наибольшая длина", light.ProjectedShadowMaxLength, .2f, 6, value => light.ProjectedShadowMaxLength = value);
            }
            Toggle("Предметы перекрывают свет", light.Shadows, value => light.Shadows = value, true);
            if (light.Shadows)
            {
                Number("Темнота перекрытия", light.ShadowStrength, 0, 1, value => light.ShadowStrength = value);
                Number("Мягкость перекрытия", light.ShadowSoftness, 0, 1, value => light.ShadowSoftness = value);
                Number("Мягкость вдали", light.ShadowSoftnessFalloff, 0, 1, value => light.ShadowSoftnessFalloff = value);
            }

            Heading("Тонкая настройка");
            Toggle("Свечение воздуха", light.Volumetric, value => light.Volumetric = value, true);
            if (light.Volumetric)
            {
                Number("Сила свечения", light.VolumeIntensity, 0, 1, value => light.VolumeIntensity = value);
                Toggle("Тени в свечении", light.VolumetricShadows, value => light.VolumetricShadows = value);
                Number("Темнота теней в свечении", light.VolumeShadowIntensity, 0, 1, value => light.VolumeShadowIntensity = value);
            }
            Choice("Смешивание (стиль рендерера 2D)", new List<int> { 0, 1, 2, 3 }, index => index == 0 ? "0 — обычный" : index.ToString(),
                light.BlendStyle, value => light.BlendStyle = value);
            Toggle("Перекрывать другие источники (не складывать)", light.AlphaOverlap, value => light.AlphaOverlap = value);
            Integer("Порядок источника", light.Order, value => light.Order = value);
            Toggle("Карты нормалей", light.NormalMaps, value => light.NormalMaps = value, true);
            if (light.NormalMaps)
            {
                Toggle("Точный расчёт нормалей", light.NormalMapsAccurate, value => light.NormalMapsAccurate = value);
                Number("Высота для нормалей", light.NormalMapDistance, .1f, 10, value => light.NormalMapDistance = value);
            }
        }

        private static string AnimationName(LocationLightAnimation value)
        {
            switch (value)
            {
                case LocationLightAnimation.Flicker: return "Мерцает";
                case LocationLightAnimation.Pulse: return "Пульсирует";
                case LocationLightAnimation.Strobe: return "Вспыхивает";
                default: return "Ровно";
            }
        }

        private void BuildPostSettings(LocationPostEffects post)
        {
            Heading("Обработка кадра");
            Toggle("Включена", post.Enabled, value => post.Enabled = value, true);
            if (!post.Enabled) return;
            Help("Днём и ночью — свои значения цвета; между ними — по яркости суток. Под крышей — дневные.");
            GradeEditor("День", post.Day);
            GradeEditor("Ночь", post.Night);
            Toggle("Свечение ярких мест (bloom)", post.Bloom, value => post.Bloom = value, true);
            if (post.Bloom)
            {
                Number("Сила свечения", post.BloomIntensity, 0, 5, value => post.BloomIntensity = value);
                Number("Порог яркости", post.BloomThreshold, 0, 2, value => post.BloomThreshold = value);
                Number("Разлёт", post.BloomScatter, 0, 1, value => post.BloomScatter = value);
                ColorField("Оттенок свечения", post.BloomTint, value => post.BloomTint = value);
            }
            Toggle("Затемнение краёв", post.Vignette, value => post.Vignette = value, true);
            if (post.Vignette)
            {
                Number("Сила", post.VignetteIntensity, 0, 1, value => post.VignetteIntensity = value);
                Number("Мягкость", post.VignetteSmoothness, .01f, 1, value => post.VignetteSmoothness = value);
                ColorField("Цвет краёв", post.VignetteColor, value => post.VignetteColor = value);
            }
            Number("Зерно", post.Grain, 0, 1, value => post.Grain = value);
            Choice("Тональная кривая", new List<LocationTonemapping> { LocationTonemapping.None, LocationTonemapping.Neutral, LocationTonemapping.ACES },
                value => value == LocationTonemapping.None ? "Нет" : value == LocationTonemapping.Neutral ? "Нейтральная" : "Кино (ACES)",
                post.Tonemapping, value => post.Tonemapping = value);
        }

        private void GradeEditor(string title, LocationColorGrade grade)
        {
            Label label = new Label(title); label.style.unityFontStyleAndWeight = FontStyle.Bold; label.style.marginTop = 6; settings.Add(label);
            Number(title + ": экспозиция", grade.Exposure, -3, 3, value => grade.Exposure = value);
            Number(title + ": контраст", grade.Contrast, -100, 100, value => grade.Contrast = value);
            Number(title + ": насыщенность", grade.Saturation, -100, 100, value => grade.Saturation = value);
            Number(title + ": тепло / холод", grade.Temperature, -100, 100, value => grade.Temperature = value);
            Number(title + ": зелень / пурпур", grade.Tint, -100, 100, value => grade.Tint = value);
            ColorField(title + ": цветной фильтр", grade.Filter, value => grade.Filter = value);
        }

        // Карта нормалей подключается к рисунку как вторая текстура
        // (_NormalMap) в настройках импорта — так её видит 2D-свет Unity.
        private void NormalMapField(string label, Sprite sprite, Texture2D value, Action<Texture2D> set)
        {
            ObjectField field = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false, value = value };
            field.RegisterValueChangedCallback(evt =>
            {
                Texture2D normal = evt.newValue as Texture2D;
                Change(() => set(normal));
                status.text = LocationNormalMaps.Assign(sprite, normal, out string message)
                    ? (normal != null ? "Карта нормалей подключена к рисунку." : "Карта нормалей снята.")
                    : message;
            });
            settings.Add(field);
            if (sprite == null)
                settings.Add(new HelpBox("Нормали подключаются к рисунку-спрайту; у технической заглушки их нет.", HelpBoxMessageType.None));
        }

        // ------------------------------------------------------------------
        // Предпросмотр света: источники, конусы, формы, стрелка солнца
        // ------------------------------------------------------------------

        private void DrawLightOverlays(Rect frame, Vector2 shift)
        {
            if (!showLights || Visual == null || renderer == null) return;
            LocalLocationDefinition location = Location;
            float scale = frame.height / ViewHeight * LocationVisualGeometry.PixelsPerUnit;
            foreach (LocationVisualObject item in Visual.Objects)
            {
                if (item.Hidden || !item.Light.Enabled) continue;
                LocationLightDefinition light = item.Light;
                Vector2 anchor = ToGui(frame, LocationVisualGeometry.ToPixel(location, item.Position)) + shift;
                Vector2 source = anchor + new Vector2(light.Offset.x, -light.Offset.y) * scale;
                bool selected = selectedKind == Kind.Art && selectedElementId == item.Id;
                Color color = new Color(light.Color.r, light.Color.g, light.Color.b, selected ? .95f : .55f);
                Handles.color = color;
                switch (light.Shape)
                {
                    case LocationLightShape.Spot:
                        float from = light.Direction - light.OuterAngle / 2;
                        Vector3 start = new Vector3(Mathf.Cos(from * Mathf.Deg2Rad), -Mathf.Sin(from * Mathf.Deg2Rad));
                        Handles.DrawWireArc(source, Vector3.back, start, light.OuterAngle, light.Radius * scale);
                        Handles.DrawLine(source, source + (Vector2)(start * light.Radius * scale));
                        float to = (light.Direction + light.OuterAngle / 2) * Mathf.Deg2Rad;
                        Handles.DrawLine(source, source + new Vector2(Mathf.Cos(to), -Mathf.Sin(to)) * light.Radius * scale);
                        break;
                    case LocationLightShape.Freeform:
                        List<Vector3> points = light.FreeformPoints.Select(point => (Vector3)(source + new Vector2(point.x, -point.y) * scale)).ToList();
                        if (points.Count > 1)
                        {
                            points.Add(points[0]);
                            Handles.DrawPolyLine(points.ToArray());
                            if (selected && tool == Tool.LightShape)
                                foreach (Vector3 point in points) Handles.DrawSolidDisc(point, Vector3.forward, 4);
                        }
                        break;
                    case LocationLightShape.Sprite:
                        Vector2 size = light.CookieSize * scale;
                        Handles.DrawSolidRectangleWithOutline(new Rect(source - size / 2, size), Color.clear, color);
                        break;
                    default:
                        Handles.DrawWireDisc(source, Vector3.forward, light.Radius * scale);
                        Handles.DrawWireDisc(source, Vector3.forward, light.Radius * (1 - light.Softness) * scale);
                        break;
                }
                Handles.DrawSolidDisc(source, Vector3.forward, item.LightOnly ? 6 : 3);
                if (item.LightOnly)
                    GUI.Label(new Rect(source.x + 8, source.y - 9, 200, 18), item.Name, EditorStyles.miniBoldLabel);
            }

            // Стрелка солнца: куда и насколько длинно ложится тень в этот час.
            Rect compass = new Rect(frame.x + shift.x + 10, frame.y + shift.y + 10, 70, 70);
            EditorGUI.DrawRect(compass, new Color(0, 0, 0, .45f));
            Vector2 center = compass.center;
            Handles.color = new Color(1, 1, 1, .35f);
            Handles.DrawWireDisc(center, Vector3.forward, 30);
            string text;
            if (renderer.Indoor) text = "под крышей";
            else if (renderer.SunShadowOpacity <= .001f) text = "ночь";
            else
            {
                Vector2 vector = renderer.SunShadowVector;
                Handles.color = new Color(1, .85f, .4f);
                Vector2 tip = center + new Vector2(vector.x, -vector.y).normalized * Mathf.Min(30, 12 + vector.magnitude * 10);
                Handles.DrawLine(center, tip, 2);
                Handles.DrawSolidDisc(tip, Vector3.forward, 3);
                text = "тень ×" + vector.magnitude.ToString("0.0");
            }
            GUI.Label(new Rect(compass.x, compass.yMax - 16, compass.width, 16), text, EditorStyles.centeredGreyMiniLabel);
        }

        // Инструмент «Форма света»: перемещение, добавление и удаление точек.
        private void LightShapeMouseDown(LocalLocationDefinition location, Rect frame, Vector2 mouse, bool shift)
        {
            LocationVisualObject item = ArtObject;
            if (item == null || item.Light.Shape != LocationLightShape.Freeform) { status.text = "Выберите источник «своей формы»."; return; }
            List<Vector2> points = item.Light.FreeformPoints;
            Vector2 source = LightSourceGui(location, frame, item);
            float scale = frame.height / ViewHeight * LocationVisualGeometry.PixelsPerUnit;
            Vector2 Gui(Vector2 point) => source + new Vector2(point.x, -point.y) * scale;
            int nearest = -1; float best = 12;
            for (int i = 0; i < points.Count; i++)
            {
                float distance = Vector2.Distance(Gui(points[i]), mouse);
                if (distance < best) { best = distance; nearest = i; }
            }
            Undo.RecordObject(database, "Форма света");
            if (shift)
            {
                if (nearest >= 0 && points.Count > 3) points.RemoveAt(nearest);
                EditorUtility.SetDirty(database); rebuildRequested = true;
                return;
            }
            if (nearest < 0)
            {
                // Новая точка — в разрыв ближайшего края.
                int edge = 0; float edgeBest = float.MaxValue;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 a = Gui(points[i]), b = Gui(points[(i + 1) % points.Count]);
                    float distance = HandleUtility.DistancePointToLineSegment(mouse, a, b);
                    if (distance < edgeBest) { edgeBest = distance; edge = i; }
                }
                Vector2 local = (mouse - source) / scale;
                points.Insert(edge + 1, new Vector2(local.x, -local.y));
                nearest = edge + 1;
            }
            dragVertex = nearest;
            dragging = true;
            EditorUtility.SetDirty(database);
        }

        private void LightShapeMouseDrag(LocalLocationDefinition location, Rect frame, Vector2 mouse)
        {
            LocationVisualObject item = ArtObject;
            if (item == null || dragVertex < 0 || dragVertex >= item.Light.FreeformPoints.Count) return;
            Vector2 source = LightSourceGui(location, frame, item);
            float scale = frame.height / ViewHeight * LocationVisualGeometry.PixelsPerUnit;
            Vector2 local = (mouse - source) / scale;
            item.Light.FreeformPoints[dragVertex] = new Vector2(local.x, -local.y);
            EditorUtility.SetDirty(database);
        }

        private Vector2 LightSourceGui(LocalLocationDefinition location, Rect frame, LocationVisualObject item)
        {
            float scale = frame.height / ViewHeight * LocationVisualGeometry.PixelsPerUnit;
            Vector2 anchor = ToGui(frame, LocationVisualGeometry.ToPixel(location, item.Position));
            return anchor + new Vector2(item.Light.Offset.x, -item.Light.Offset.y) * scale;
        }

        // Сравнение суток: четыре кадра одного места в разные часы.
        private void DrawDayComparison(Rect area)
        {
            float[] hours = { 6, 13, 19, 1 };
            string[] names = { "Утро 06:00", "День 13:00", "Вечер 19:00", "Ночь 01:00" };
            Vector2 half = new Vector2(area.width / 2, area.height / 2);
            for (int i = 0; i < 4; i++)
            {
                Rect cell = new Rect(area.x + (i % 2) * half.x, area.y + (i / 2) * half.y, half.x, half.y);
                Rect frame = CanvasFrame(new Rect(cell.x + 4, cell.y + 4, cell.width - 8, cell.height - 8));
                renderer.SetTime(hours[i], (float)EditorApplication.timeSinceStartup);
                RenderPreviewActors();
                preview.camera.orthographicSize = CanvasSize.y / LocationVisualGeometry.PixelsPerUnit / 2;
                preview.camera.transform.position = new Vector3(0, 0, -10);
                preview.BeginPreview(frame, GUIStyle.none);
                preview.Render(true);
                GUI.DrawTexture(frame, preview.EndPreview(), ScaleMode.StretchToFill);
                GUI.Label(new Rect(frame.x + 6, frame.y + 4, 160, 18), names[i], EditorStyles.whiteBoldLabel);
            }
            renderer.SetTime(hour, (float)EditorApplication.timeSinceStartup);
        }
    }
}
