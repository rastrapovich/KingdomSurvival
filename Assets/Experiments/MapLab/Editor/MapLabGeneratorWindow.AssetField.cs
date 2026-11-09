using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.MapLab.Editor
{
    // Поле ассетов вкладки «Ассеты»: ровная земля MapLab с рекой рядом, все
    // ассеты каталога стоят на ней как детали мест — тем же конвейером, в том
    // же масштабе и с теми же настройками вида, что и на карте. Ассеты
    // перетаскиваются мышью, расстановка — кнопками (квадрат, ряды, по росту,
    // круг); размер ассета меняет все его копии (здесь и на карте).
    // Расстановка поля хранится в настройках редактора (EditorPrefs).
    public sealed partial class MapLabGeneratorWindow
    {
        private const string FieldPrefsKey = "KingdomSurvival.MapLab.AssetField";
        private const string FieldKind = "asset-field";
        private const string HeroKey = "@hero";
        private const float FieldRiverBand = 22f, FieldGap = 1.6f;
        private static readonly string[] ArrangeNames = { "Квадрат", "Ряды по группам", "По росту", "Круг" };

        [Serializable]
        private sealed class FieldSpot
        {
            public string Key;
            public float U, V;
        }

        [Serializable]
        private sealed class FieldLayout
        {
            public List<FieldSpot> Spots = new List<FieldSpot>();
            public bool River = true, Labels = true;
        }

        private sealed class FieldItem
        {
            public string Key, Title, Group;
            public Card Card;
            public MapLabArtEntry Entry;
            public float Width, Height;
        }

        private readonly Dictionary<string, Vector2> fieldSpots = new Dictionary<string, Vector2>();
        private readonly List<FieldItem> fieldItems = new List<FieldItem>();
        private bool fieldLoaded, fieldBuilt, fieldLabels = true, fieldRiver = true;
        private Vector2 fieldOrigin;
        private float fieldSize;
        // Своя копия рисовальщика, профиля и камеры: поле не трогает карту
        // окна и не смешивается с ней (свой слой камеры).
        private const int FieldLayer = 30;
        private MapLabView fieldView;
        private MapLabSettings fieldSettings;
        private Camera fieldCamera;
        private int fieldHover = -1;
        private int fieldDrag = -1;
        private Vector2 fieldDragOffset, fieldDragAt, fieldDragStart;
        private bool fieldDragMoved;

        // ---------- Хранение расстановки ----------

        private void LoadFieldLayout()
        {
            if (fieldLoaded)
                return;
            fieldLoaded = true;
            fieldSpots.Clear();
            try
            {
                string json = EditorPrefs.GetString(FieldPrefsKey, string.Empty);
                if (string.IsNullOrEmpty(json))
                    return;
                FieldLayout layout = JsonUtility.FromJson<FieldLayout>(json);
                if (layout?.Spots == null)
                    return;
                foreach (FieldSpot spot in layout.Spots)
                    if (spot != null && !string.IsNullOrEmpty(spot.Key))
                        fieldSpots[spot.Key] = new Vector2(spot.U, spot.V);
                fieldRiver = layout.River;
                fieldLabels = layout.Labels;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Поле ассетов: расстановка не прочитана — " + exception.Message);
            }
        }

        private void SaveFieldLayout()
        {
            FieldLayout layout = new FieldLayout { River = fieldRiver, Labels = fieldLabels };
            foreach (KeyValuePair<string, Vector2> pair in fieldSpots)
                layout.Spots.Add(new FieldSpot { Key = pair.Key, U = pair.Value.x, V = pair.Value.y });
            EditorPrefs.SetString(FieldPrefsKey, JsonUtility.ToJson(layout));
        }

        // ---------- Состав поля ----------

        private void CollectFieldItems()
        {
            fieldItems.Clear();
            MapLabArtEntry hero = view.ActiveMarkerSet?.View(MapLabMarkerDirection.Front);
            if (hero != null && hero.HasTexture)
                fieldItems.Add(new FieldItem { Key = HeroKey, Title = "Герой", Group = "Герой", Entry = hero });
            // Плоские пятна поверхности на поле не ставятся (это не стоячие
            // рисунки) — только если их категория выбрана явно.
            bool spots = category > 0 && Categories[category] == "Пятна и поверхность";
            foreach (Card c in cards.Where(Visible))
            {
                MapLabArtEntry e = c.Views[0].Entry;
                if (e == null || !e.HasTexture || (!spots && c.Category == "Пятна и поверхность"))
                    continue;
                // Фишка героя уже стоит первой.
                if (hero != null && c.Views.Any(v => v.Entry == hero))
                    continue;
                fieldItems.Add(new FieldItem { Key = c.Key, Title = c.Title, Group = c.Category, Card = c, Entry = e });
            }
            foreach (FieldItem item in fieldItems)
            {
                item.Height = Mathf.Max(0.2f, MapLabArtBuilder.FeatureWorldHeight(item.Entry));
                item.Width = Mathf.Max(0.6f, item.Height * item.Entry.Aspect * Mathf.Max(0.3f, item.Entry.SolidRect.width));
            }
        }

        // ---------- Расстановка ----------

        // Позиции — в осях поля: U вправо, V от реки вглубь (единицы мира).
        private void Arrange(int mode)
        {
            CollectFieldItems();
            if (fieldItems.Count == 0)
                return;
            fieldSpots.Clear();
            List<FieldItem> order = fieldItems.ToList();
            switch (mode)
            {
                case 0:
                {
                    float cell = Mathf.Clamp(order.Max(i => i.Width) + FieldGap, 3f, 14f);
                    int cols = Mathf.CeilToInt(Mathf.Sqrt(order.Count));
                    for (int i = 0; i < order.Count; i++)
                        fieldSpots[order[i].Key] = new Vector2(cell * (i % cols + 0.5f), cell * (i / cols + 0.5f));
                    break;
                }
                case 1:
                    PackRows(order.GroupBy(i => i.Group).Select(g => g.ToList()).ToList());
                    break;
                case 2:
                    PackRows(new List<List<FieldItem>> { order.OrderByDescending(i => i.Height).ToList() });
                    break;
                default:
                {
                    float around = order.Sum(i => i.Width + FieldGap);
                    float radius = Mathf.Max(10f, around / (2f * Mathf.PI));
                    float angle = 0f;
                    foreach (FieldItem item in order)
                    {
                        float step = (item.Width + FieldGap) / radius;
                        angle += step * 0.5f;
                        fieldSpots[item.Key] = new Vector2(radius + 4f + Mathf.Cos(angle) * radius, radius + 4f + Mathf.Sin(angle) * radius);
                        angle += step * 0.5f;
                    }
                    break;
                }
            }
            SaveFieldLayout();
            BuildField();
        }

        // Ряды: элементы идут подряд по своей ширине, ряд переносится по
        // ширине поля; каждая группа начинает новый ряд.
        private void PackRows(List<List<FieldItem>> groups)
        {
            float area = fieldItems.Sum(i => (i.Width + FieldGap) * (i.Width * 0.7f + FieldGap * 2f));
            float limit = Mathf.Max(48f, Mathf.Sqrt(area) * 1.3f);
            float v = 0f;
            foreach (List<FieldItem> group in groups)
            {
                float u = 0f, depth = 0f;
                foreach (FieldItem item in group)
                {
                    if (u > 0f && u + item.Width > limit)
                    {
                        v += depth;
                        u = 0f;
                        depth = 0f;
                    }
                    fieldSpots[item.Key] = new Vector2(u + item.Width * 0.5f, v + item.Width * 0.35f + FieldGap);
                    u += item.Width + FieldGap;
                    depth = Mathf.Max(depth, item.Width * 0.7f + FieldGap * 2f);
                }
                v += depth + FieldGap;
            }
        }

        // ---------- Построение поля ----------

        private void BuildField()
        {
            if (view.Settings == null)
                return;
            Guard(() =>
            {
                MapLabView fv = EnsureFieldView();
                MapLabSettings s = fieldSettings;
                LoadFieldLayout();
                // Каталог перечитывается при каждой постройке: новые ассеты
                // (подключённые пакеты, правки наборов) встают на поле сами.
                RebuildCards();
                CollectFieldItems();
                // Ширина ряда — по общей площади ассетов: поле остаётся
                // примерно квадратным при любом их количестве.
                float area = fieldItems.Sum(i => (i.Width + FieldGap) * (i.Width * 0.7f + FieldGap * 2f));
                float rowLimit = Mathf.Max(48f, Mathf.Sqrt(area) * 1.3f);
                // Новым ассетам (ещё без места) — ряды за уже расставленными.
                List<FieldItem> missing = fieldItems.Where(i => !fieldSpots.ContainsKey(i.Key)).ToList();
                if (missing.Count > 0)
                {
                    float v = fieldSpots.Count > 0 ? fieldSpots.Values.Max(p => p.y) + 8f : 0f;
                    float u = 0f, depth = 0f;
                    foreach (FieldItem item in missing)
                    {
                        if (u > 0f && u + item.Width > rowLimit)
                        {
                            u = 0f;
                            v += depth;
                            depth = 0f;
                        }
                        fieldSpots[item.Key] = new Vector2(u + item.Width * 0.5f, v + item.Width * 0.35f + FieldGap);
                        u += item.Width + FieldGap;
                        depth = Mathf.Max(depth, item.Width * 0.7f + FieldGap * 2f);
                    }
                    SaveFieldLayout();
                }
                float maxU = 10f, maxV = 10f;
                foreach (FieldItem item in fieldItems)
                {
                    Vector2 p = fieldSpots[item.Key];
                    maxU = Mathf.Max(maxU, p.x + item.Width);
                    maxV = Mathf.Max(maxV, p.y + item.Width);
                }
                // Поле растёт под расстановку; предел — проверенный размер мира
                // MapLab (512). Если ассеты не помещаются — вся расстановка
                // ужимается к центру, чтобы все стояли на поле.
                float need = Mathf.Max(maxU, maxV + FieldRiverBand) + 16f;
                if (need > 512f)
                {
                    float k = (512f - 16f - FieldRiverBand) / Mathf.Max(maxU, maxV);
                    foreach (string key in fieldSpots.Keys.ToList())
                        fieldSpots[key] *= k;
                    maxU *= k;
                    maxV *= k;
                    SaveFieldLayout();
                    need = 512f;
                }
                float size = Mathf.Clamp(need, 64f, 512f);
                s.General.MapSize = size;
                float frame = s.FrameWidth;
                fieldSize = size;
                // Ассеты — по центру поля по ширине, над рекой.
                fieldOrigin = new Vector2(frame + (size - maxU) * 0.5f, frame + FieldRiverBand);

                MapLabComposition c = new MapLabComposition
                {
                    FromPlan = true,
                    AssetField = true,
                    PlayRect = new Rect(frame, frame, size, size),
                    PlanRect = new Rect(frame, frame, size, size),
                    PlanCols = 2,
                    PlanRows = 2,
                    PlanForest = new float[4],
                    PlanField = new float[4],
                    MapEdgeForest = 0f,
                    LakeLevelFraction = 0.3f,
                };
                // Ровная земля: одна площадка на всё поле.
                c.Flats.Add(new MapLabComposition.Zone { Center = new Vector2(frame + size * 0.5f, frame + size * 0.5f), Radius = size * 1.2f, Strength = 1f });
                if (fieldRiver)
                {
                    MapLabComposition.River river = new MapLabComposition.River();
                    float total = size + 2f * frame;
                    river.Control.Add(new Vector2(-8f, frame + 9f));
                    river.Control.Add(new Vector2(frame + size * 0.3f, frame + 11f));
                    river.Control.Add(new Vector2(frame + size * 0.65f, frame + 7f));
                    river.Control.Add(new Vector2(total + 8f, frame + 10f));
                    c.Rivers.Add(river);
                }
                foreach (FieldItem item in fieldItems)
                {
                    Vector2 p = fieldOrigin + fieldSpots[item.Key];
                    c.Features.Add((p, item.Entry.Id, FieldKind));
                    c.Reserved.Add(new MapLabComposition.Zone { Center = p, Radius = item.Width * 0.6f + 1.5f, Strength = 1f });
                }
                s.RegionPlan = c;
                fv.FeatureExtraSets = ActiveSets().ToList();
                if (!fv.Generate((stage, fraction) => EditorUtility.DisplayCancelableProgressBar("Поле ассетов", stage, fraction)))
                    throw new Exception("поле не построено (проверьте профиль и шейдеры)");
                SetFieldLayer();
                fv.CameraRig?.Fit();
                fieldBuilt = true;
                objectsPending = false;
                previewDirty = true;
                string missingArt = MapLabArtBuilder.MissingFeatureArt.Count > 0 ? " · без рисунка: " + string.Join(", ", MapLabArtBuilder.MissingFeatureArt.Take(6)) : string.Empty;
                status = $"Поле ассетов: {fieldItems.Count} шт. на {size:0}×{size:0} ед.{missingArt}";
            });
        }

        // Перестановка без нового рельефа: только объекты (ассеты на поле).
        private void RefreshFieldObjects()
        {
            if (!fieldBuilt || fieldView == null || fieldView.Data?.Composition == null)
            {
                BuildField();
                return;
            }
            MapLabComposition c = fieldView.Data.Composition;
            c.Features.Clear();
            c.Reserved.Clear();
            foreach (FieldItem item in fieldItems)
                if (fieldSpots.TryGetValue(item.Key, out Vector2 spot))
                {
                    Vector2 p = fieldOrigin + spot;
                    c.Features.Add((p, item.Entry.Id, FieldKind));
                    c.Reserved.Add(new MapLabComposition.Zone { Center = p, Radius = item.Width * 0.6f + 1.5f, Strength = 1f });
                }
            fieldView.FeatureExtraSets = ActiveSets().ToList();
            Guard(() =>
            {
                fieldView.RebuildObjects((stage, fraction) => EditorUtility.DisplayCancelableProgressBar("Поле ассетов", stage, fraction));
                SetFieldLayer();
            });
            objectsPending = false;
            previewDirty = true;
        }

        // Копия рисовальщика сцены без карты: свой профиль (копия рабочего) и
        // своя камера, видящая только слой поля; камера карты слой поля не видит.
        private MapLabView EnsureFieldView()
        {
            if (fieldSettings == null)
                fieldSettings = Instantiate(view.Settings);
            else
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(view.Settings), fieldSettings);
            fieldSettings.name = "Поле ассетов · профиль";
            fieldSettings.hideFlags = HideFlags.DontSave;
            if (fieldView == null)
            {
                bool generate = view.GenerateOnLoad;
                view.GenerateOnLoad = false;
                GameObject copy;
                try
                {
                    copy = Instantiate(view.gameObject);
                }
                finally
                {
                    view.GenerateOnLoad = generate;
                }
                copy.name = "Поле ассетов";
                copy.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
                fieldView = copy.GetComponent<MapLabView>();
                fieldView.GenerateOnLoad = false;
                GameObject cameraObject = new GameObject("Поле ассетов · камера") { hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy };
                fieldCamera = cameraObject.AddComponent<Camera>();
                Camera main = view.CameraRig != null ? view.CameraRig.Camera : null;
                if (main != null)
                    fieldCamera.CopyFrom(main);
                fieldCamera.enabled = false;
                fieldCamera.cullingMask = 1 << FieldLayer;
                fieldView.CameraRig = cameraObject.AddComponent<MapLabCameraRig>();
            }
            fieldView.Settings = fieldSettings;
            if (view.CameraRig != null && view.CameraRig.Camera != null)
                view.CameraRig.Camera.cullingMask &= ~(1 << FieldLayer);
            return fieldView;
        }

        private void SetFieldLayer()
        {
            if (fieldView == null)
                return;
            foreach (Transform t in fieldView.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = FieldLayer;
        }

        // Поле живёт, пока открыто окно: копия сцены не сохраняется.
        private void DestroyFieldView()
        {
            if (fieldView != null)
            {
                fieldView.Clear();
                DestroyImmediate(fieldView.gameObject);
            }
            if (fieldCamera != null)
                DestroyImmediate(fieldCamera.gameObject);
            if (fieldSettings != null)
                DestroyImmediate(fieldSettings);
            fieldView = null;
            fieldCamera = null;
            fieldSettings = null;
            fieldBuilt = false;
        }

        // Правка вида в окне — в копию поля (размер поля сохраняется).
        private void SyncFieldLook(bool rebuildLayers)
        {
            if (fieldView == null || fieldSettings == null)
                return;
            float size = fieldSettings.General.MapSize;
            MapLabComposition plan = fieldSettings.RegionPlan;
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(view.Settings), fieldSettings);
            fieldSettings.General.MapSize = size;
            fieldSettings.RegionPlan = plan;
            fieldView.ApplyMaterialSettings();
            if (rebuildLayers)
                fieldView.RebuildStage03Layers();
            previewDirty = true;
        }

        // ---------- Левая панель в режиме поля ----------

        private void DrawFieldControls()
        {
            LoadFieldLayout();
            EditorGUILayout.LabelField("Поле ассетов", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Ровная земля карты с рекой: ассеты в мировом масштабе, тем же рисунком, что на карте. Перетаскивание ассета — левой кнопкой; сдвиг поля — правой или средней; колесо — приближение.", MessageType.None);
            if (GUILayout.Button(new GUIContent(fieldBuilt ? "Перестроить поле" : "Построить поле", "Собрать поле из показанных фильтром ассетов."), GUILayout.Height(26)))
                BuildField();
            EditorGUILayout.LabelField("Расставить все", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
                for (int m = 0; m < 2; m++)
                    if (GUILayout.Button(ArrangeNames[m]))
                        Arrange(m);
            using (new EditorGUILayout.HorizontalScope())
                for (int m = 2; m < 4; m++)
                    if (GUILayout.Button(ArrangeNames[m]))
                        Arrange(m);
            EditorGUI.BeginChangeCheck();
            bool river = EditorGUILayout.ToggleLeft(new GUIContent("Река рядом", "Река вдоль ближнего края поля — сравнить цвет и тон ассетов с водой и берегом."), fieldRiver);
            bool labels = EditorGUILayout.ToggleLeft("Подписи и высоты", fieldLabels);
            if (EditorGUI.EndChangeCheck())
            {
                bool rebuild = river != fieldRiver;
                fieldRiver = river;
                fieldLabels = labels;
                SaveFieldLayout();
                previewDirty = true;
                if (rebuild && fieldBuilt)
                    BuildField();
            }
            if (objectsPending && fieldBuilt && GUILayout.Button("Применить размеры на поле"))
                RefreshFieldObjects();
            EditorGUILayout.Space(6);
            MapLabSettings s = view.Settings;
            if (DrawLookGroup(s, s.Author))
            {
                MapLabAuthor.ApplyLook(s);
                EditorUtility.SetDirty(s);
                view.ApplyMaterialSettings();
                SyncFieldLook(false);
            }
            if (GUILayout.Button(new GUIContent("Обновить внешний вид", "Перекрасить поверхность, перо и тени поля без перестройки.")))
                Guard(() =>
                {
                    MapLabAuthor.ApplyLook(s);
                    SyncFieldLook(true);
                });
            EditorGUILayout.Space(6);
        }

        // ---------- Центр: поле в предпросмотре MapLab ----------

        private void DrawAssetField(Rect rect)
        {
            if (!fieldBuilt || fieldView == null || fieldView.Data == null)
            {
                EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f));
                GUI.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, 60f), "Поле ассетов ещё не построено — «Построить поле» слева.\nВсе показанные фильтром ассеты встанут на ровную землю карты рядом с рекой.", EditorStyles.whiteLabel);
                return;
            }
            Rect image = new Rect(rect.x, rect.y, rect.width, rect.height - 22f);
            HandleFieldDrag(image);
            DrawPreview(rect, fieldView);
            Camera cam = fieldView.CameraRig != null ? fieldView.CameraRig.Camera : null;
            if (cam == null)
                return;
            List<(Rect R, FieldItem Item)> marks = FieldScreenRects(image, cam);
            foreach ((Rect r, FieldItem item) in marks)
            {
                bool isFocus = item.Card != null && focus == item.Card;
                bool isDrag = fieldDrag >= 0 && fieldDrag < fieldItems.Count && fieldItems[fieldDrag] == item;
                bool isHover = fieldHover >= 0 && fieldHover < fieldItems.Count && fieldItems[fieldHover] == item;
                if (isFocus || isDrag || isHover)
                {
                    Color frameColor = isDrag ? new Color(1f, 0.85f, 0.3f, 0.9f) : isFocus ? new Color(0.35f, 0.6f, 1f, 0.9f) : new Color(1f, 0.96f, 0.82f, 0.85f);
                    Rect shown = r;
                    if (isDrag && fieldDragMoved)
                        shown.position += fieldDragAt - fieldDragStart;
                    EditorGUI.DrawRect(new Rect(shown.x, shown.yMax, shown.width, 2f), frameColor);
                    EditorGUI.DrawRect(new Rect(shown.x, shown.y, shown.width, 1f), frameColor);
                    EditorGUI.DrawRect(new Rect(shown.x, shown.y, 1f, shown.height), frameColor);
                    EditorGUI.DrawRect(new Rect(shown.xMax - 1f, shown.y, 1f, shown.height), frameColor);
                }
                if (isHover && !fieldLabels)
                    GUI.Label(new Rect(r.center.x - 60f, r.yMax + 1f, 120f, 14f), item.Title, LabelStyle);
                if (fieldLabels && r.width > 6f)
                {
                    GUI.Label(new Rect(r.center.x - 60f, r.yMax + 1f, 120f, 14f), item.Title, LabelStyle);
                    GUI.Label(new Rect(r.center.x - 60f, r.yMax + 13f, 120f, 14f), item.Height.ToString("0.0") + " ед.", LabelStyle);
                }
            }
        }

        private static GUIStyle labelStyle;

        private static GUIStyle LabelStyle => labelStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.UpperCenter,
            normal = { textColor = new Color(0.08f, 0.07f, 0.05f) },
        };

        // Экранные прямоугольники ассетов поля (по опоре и высоте карточки).
        private List<(Rect, FieldItem)> FieldScreenRects(Rect image, Camera cam)
        {
            List<(Rect, FieldItem)> rects = new List<(Rect, FieldItem)>();
            Dictionary<string, FieldItem> byId = new Dictionary<string, FieldItem>();
            Dictionary<Vector2, FieldItem> byPoint = new Dictionary<Vector2, FieldItem>();
            foreach (FieldItem item in fieldItems)
                if (fieldSpots.TryGetValue(item.Key, out Vector2 spot))
                    byPoint[fieldOrigin + spot] = item;
            foreach (MapLabFeatureSprite sprite in fieldView.Data.FeatureSprites)
            {
                if (sprite.Kind != FieldKind || !byPoint.TryGetValue(sprite.Point, out FieldItem item))
                    continue;
                Vector3 baseView = cam.WorldToViewportPoint(sprite.Base);
                Vector3 topView = cam.WorldToViewportPoint(sprite.Base + Vector3.up * sprite.Height);
                if (baseView.z <= 0f)
                    continue;
                Vector2 bottom = new Vector2(image.x + baseView.x * image.width, image.y + (1f - baseView.y) * image.height);
                Vector2 top = new Vector2(image.x + topView.x * image.width, image.y + (1f - topView.y) * image.height);
                float h = Mathf.Max(4f, bottom.y - top.y);
                float w = Mathf.Max(4f, h * sprite.Width / Mathf.Max(0.01f, sprite.Height));
                rects.Add((new Rect(bottom.x - w * sprite.Pivot.x, bottom.y - h, w, h), item));
            }
            return rects;
        }

        // Ассет под курсором: внутри рисунка (ближний к зрителю — первым), иначе
        // ближайшая опора в пределах 18 px — мелкие рисунки ловятся легко.
        private (Rect R, FieldItem Item) FieldHit(List<(Rect R, FieldItem Item)> marks, Vector2 mouse)
        {
            (Rect R, FieldItem Item) hit = marks.Where(m => m.R.Contains(mouse)).OrderByDescending(m => m.R.yMax).FirstOrDefault();
            if (hit.Item != null)
                return hit;
            float best = 18f;
            foreach ((Rect R, FieldItem Item) m in marks)
            {
                float d = Vector2.Distance(mouse, new Vector2(m.R.center.x, m.R.yMax - Mathf.Min(m.R.height * 0.3f, 10f)));
                if (d < best)
                {
                    best = d;
                    hit = m;
                }
            }
            return hit;
        }

        // Наведение — подсветка; левая кнопка на ассете — выбор, а движение
        // дальше 4 px — перетаскивание (по отпусканию ассет встаёт на новое
        // место, пересобираются только объекты).
        private void HandleFieldDrag(Rect image)
        {
            Event e = Event.current;
            Camera cam = fieldView.CameraRig != null ? fieldView.CameraRig.Camera : null;
            if (cam == null || fieldView.Data == null)
                return;
            if (e.type == EventType.MouseMove)
            {
                int hover = -1;
                if (image.Contains(e.mousePosition))
                {
                    (Rect R, FieldItem Item) h = FieldHit(FieldScreenRects(image, cam), e.mousePosition);
                    hover = h.Item != null ? fieldItems.IndexOf(h.Item) : -1;
                }
                if (hover != fieldHover)
                {
                    fieldHover = hover;
                    Repaint();
                }
            }
            if (e.type == EventType.MouseDown && e.button == 0 && image.Contains(e.mousePosition))
            {
                (Rect R, FieldItem Item) hit = FieldHit(FieldScreenRects(image, cam), e.mousePosition);
                if (hit.Item != null)
                {
                    fieldDrag = fieldItems.IndexOf(hit.Item);
                    fieldDragOffset = e.mousePosition - new Vector2(hit.R.x + hit.R.width * hit.Item.Entry.Pivot.x, hit.R.yMax);
                    fieldDragAt = fieldDragStart = e.mousePosition;
                    fieldDragMoved = false;
                    if (hit.Item.Card != null)
                    {
                        focus = hit.Item.Card;
                        focusView = 0;
                    }
                    e.Use();
                    Repaint();
                }
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && fieldDrag >= 0)
            {
                fieldDragAt = e.mousePosition;
                fieldDragMoved |= Vector2.Distance(fieldDragAt, fieldDragStart) > 4f;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && fieldDrag >= 0)
            {
                FieldItem item = fieldDrag < fieldItems.Count ? fieldItems[fieldDrag] : null;
                fieldDrag = -1;
                if (item != null && fieldDragMoved && GroundPoint(image, cam, e.mousePosition - fieldDragOffset, out Vector2 ground))
                {
                    float frame = fieldSettings.FrameWidth;
                    Rect play = new Rect(frame, frame, fieldSize, fieldSize);
                    ground.x = Mathf.Clamp(ground.x, play.xMin + 1f, play.xMax - 1f);
                    ground.y = Mathf.Clamp(ground.y, play.yMin + 1f, play.yMax - 1f);
                    fieldSpots[item.Key] = ground - fieldOrigin;
                    SaveFieldLayout();
                    RefreshFieldObjects();
                }
                e.Use();
                Repaint();
            }
        }

        // Точка земли под экранной точкой: луч камеры до плоскости средней
        // высоты поля (поле ровное).
        private bool GroundPoint(Rect image, Camera cam, Vector2 screen, out Vector2 ground)
        {
            ground = default;
            Vector2 viewport = new Vector2((screen.x - image.x) / image.width, 1f - (screen.y - image.y) / image.height);
            Ray ray = cam.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
            float height = fieldView.Data.SampleHeight(fieldOrigin.x + fieldSize * 0.3f, fieldOrigin.y + fieldSize * 0.3f);
            Plane plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (!plane.Raycast(ray, out float distance))
                return false;
            Vector3 hit = ray.GetPoint(distance);
            ground = new Vector2(hit.x, hit.z);
            return true;
        }
    }
}
