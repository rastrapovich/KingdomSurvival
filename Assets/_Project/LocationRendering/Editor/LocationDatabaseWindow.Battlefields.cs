using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattlefieldDatabase.Editor;
using KingdomSurvival.BattleSandbox;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Поля боя — только здесь, в Базе локаций (своего окна у них больше нет).
    // Поле собирается как место (земля, предметы, раскидка, свет); его запись
    // в данных полей боя (тот же BattlefieldId) — сетка, отключённые гексы,
    // вид гекса и теги — правится здесь же, как и общее для всех полей:
    // скорость ходьбы, общий вид гекса, справочник тегов. Кадр поля 16:9 на
    // холсте — рамка (масштаб и сдвиг кадра); в бою камера места снимает
    // ровно её (BattlefieldSceneDriver). Поле без сборки при открытии окна
    // собирается само: его рисунок — земля.
    public sealed partial class LocationDatabaseWindow
    {
        private enum ListKind { Places, Battlefields }

        [SerializeField] private ListKind listKind = ListKind.Places;
        private readonly List<Button> listKindButtons = new List<Button>();
        // Кисть гексов поля: протяжка ставит то же состояние, что первый клик.
        private bool fieldCellPaintDisabled;
        private readonly HashSet<HexCoord> fieldCellsStroke = new HashSet<HexCoord>();

        private bool ShowsBattlefields => listKind == ListKind.Battlefields;

        // Фильтр списка: места или поля боя; перенос старых полей.
        private void BuildListKind(VisualElement left)
        {
            // Открыть ту часть списка, где выбранная запись.
            if (Location != null) listKind = Location.IsBattlefield ? ListKind.Battlefields : ListKind.Places;
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            listKindButtons.Clear();
            foreach ((ListKind kind, string title) in new[] { (ListKind.Places, "Места"), (ListKind.Battlefields, "Поля боя") })
            {
                ListKind value = kind;
                Button button = new Button(() => { listKind = value; RefreshList(); RefreshListKind(); }) { text = title };
                button.style.flexGrow = 1;
                listKindButtons.Add(button);
                row.Add(button);
            }
            left.Add(row);
            RefreshListKind();
        }

        private void RefreshListKind()
        {
            for (int i = 0; i < listKindButtons.Count; i++)
            {
                bool active = (int)listKind == i;
                listKindButtons[i].style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
                listKindButtons[i].style.color = active ? new Color(.95f, .82f, .5f) : new StyleColor(StyleKeyword.Null);
            }
        }

        private bool MatchesListKind(LocalLocationDefinition location) => location.IsBattlefield == ShowsBattlefields;

        // Поля, у которых ещё нет сборки в Базе локаций.
        public static List<BattlefieldDefinitionData> UnassembledBattlefields(LocalLocationDatabaseAsset locations, BattlefieldDatabaseAsset battlefields)
        {
            if (battlefields == null || locations == null) return new List<BattlefieldDefinitionData>();
            HashSet<string> assembled = new HashSet<string>(locations.locations.Where(item => item != null && item.IsBattlefield).Select(item => item.BattlefieldId));
            return battlefields.Battlefields.Where(item => item != null && !string.IsNullOrEmpty(item.Id) && !assembled.Contains(item.Id)).ToList();
        }

        // Перенос: каждое поле без сборки становится полем-местом — рисунок
        // поля — земля (холст его размера), кадрирование фона остаётся у поля
        // и задаёт рамку кадра; гексы, теги и вид гекса — тоже у поля.
        // Возвращает число перенесённых. Меню: Kingdom Survival → служебное.
        public static int AssembleAllBattlefields(LocalLocationDatabaseAsset locations, BattlefieldDatabaseAsset battlefields)
        {
            List<BattlefieldDefinitionData> missing = UnassembledBattlefields(locations, battlefields);
            if (missing.Count == 0) return 0;
            Undo.RecordObject(locations, "Поля боя → База локаций");
            foreach (BattlefieldDefinitionData field in missing)
            {
                Vector2 canvas = field.Background != null ? field.Background.rect.size : new Vector2(1920, 1080);
                AddSceneFor(locations, field.Id, string.IsNullOrEmpty(field.DisplayLabel) ? field.Id : field.DisplayLabel, field.Background, canvas);
            }
            EditorUtility.SetDirty(locations);
            AssetDatabase.SaveAssetIfDirty(locations);
            return missing.Count;
        }

        // Для запуска без окна (batchmode): перенести поля рабочей базы.
        public static void AssembleAllBattlefieldsInProject()
        {
            LocalLocationDatabaseAsset locations = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            BattlefieldDatabaseAsset battlefields = Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath);
            Debug.Log("Поля боя перенесены в Базу локаций: " + AssembleAllBattlefields(locations, battlefields));
        }

        // Новое поле: запись поля боя и место вида «Поле боя» 16:9.
        private void AddBattlefieldScene()
        {
            if (fields == null) return;
            SerializedObject serialized = new SerializedObject(fields);
            SerializedProperty list = serialized.FindProperty("battlefields");
            string id = UniqueBattlefieldId("field");
            list.arraySize++;
            SerializedProperty field = list.GetArrayElementAtIndex(list.arraySize - 1);
            field.FindPropertyRelative("id").stringValue = id;
            field.FindPropertyRelative("displayLabel").stringValue = "Новое поле боя";
            field.FindPropertyRelative("background").objectReferenceValue = null;
            field.FindPropertyRelative("backgroundScale").floatValue = 1f;
            field.FindPropertyRelative("backgroundOffset").vector2Value = Vector2.zero;
            field.FindPropertyRelative("awaitingArt").boolValue = false;
            field.FindPropertyRelative("gridScale").floatValue = 1f;
            field.FindPropertyRelative("gridOffset").vector2Value = Vector2.zero;
            field.FindPropertyRelative("disabledCells").ClearArray();
            field.FindPropertyRelative("useOwnHexStyle").boolValue = false;
            field.FindPropertyRelative("tagIds").ClearArray();
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(fields);
            Change(() =>
            {
                selectedId = AddSceneFor(database, id, "Новое поле боя", null, new Vector2(1920, 1080));
                listKind = ListKind.Battlefields;
                tab = Tab.Place;
                RefreshList();
            }, true);
            RefreshListKind();
            status.text = "Поле боя добавлено: соберите его кистями и ассетами, как место; гексы и теги — вкладка «Место» → «Поле боя».";
        }

        private string UniqueBattlefieldId(string prefix)
        {
            string id;
            do id = prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            while (fields.FindById(id) != null);
            return id;
        }

        // Место вида «Поле боя» для поля: холст — размер рисунка поля (или 16:9).
        private static string AddSceneFor(LocalLocationDatabaseAsset locations, string battlefieldId, string label, Sprite picture, Vector2 canvas)
        {
            string id = "field." + battlefieldId;
            int suffix = 1;
            while (locations.locations.Any(item => item.Id == id)) id = "field." + battlefieldId + "_" + ++suffix;
            locations.locations.Add(new LocalLocationDefinition
            {
                Id = id, DisplayName = label, Kind = LocalLocationKind.Battlefield, BattlefieldId = battlefieldId,
                CanvasWidth = canvas.x, CanvasHeight = canvas.y, BattleFrameWidth = canvas.x
            });
            locations.visuals.Add(new LocationVisualDefinition { LocationId = id, Background = picture });
            return id;
        }

        // Открыть сборку поля (из места: «Открыть это поле боя»).
        private void OpenBattlefieldScene(string battlefieldId)
        {
            LocalLocationDefinition scene = database.locations.Find(item => item != null && item.IsBattlefield && item.BattlefieldId == battlefieldId);
            if (scene == null) { status.text = "У поля «" + battlefieldId + "» нет сборки."; return; }
            listKind = ListKind.Battlefields;
            OpenRequested(scene.Id, null);
        }

        // Удалить поле боя: сборку и запись поля, если на него не ссылаются места и полигон.
        private bool DeleteBattlefieldScene(LocalLocationDefinition location)
        {
            if (!location.IsBattlefield) return false;
            string fieldId = location.BattlefieldId;
            List<string> users = database.locations.Where(item => item != null && !item.IsBattlefield && item.BattlefieldId == fieldId)
                .Select(item => item.DisplayName).ToList();
            if (fields.SandboxBattlefieldId == fieldId) users.Add("полигон");
            if (users.Count > 0)
            {
                EditorUtility.DisplayDialog("Поле используется", "На поле «" + location.DisplayName + "» ссылаются: " + string.Join(", ", users) +
                                                                 ".\nСначала выберите им другое поле.", "Понятно");
                return true;
            }
            if (!EditorUtility.DisplayDialog("Удалить поле боя?", location.DisplayName + "\nСборка и запись поля (гексы, теги, вид) будут удалены. Ctrl+Z — вернуть.",
                    "Удалить", "Отмена"))
                return true;
            LocationVisualDefinition visual = database.FindVisual(location.Id);
            int index = fields.Battlefields.ToList().FindIndex(item => item != null && item.Id == fieldId);
            if (index >= 0)
            {
                SerializedObject serialized = new SerializedObject(fields);
                serialized.FindProperty("battlefields").DeleteArrayElementAtIndex(index);
                serialized.ApplyModifiedProperties();
            }
            Change(() =>
            {
                database.locations.Remove(location);
                if (visual != null) database.visuals.Remove(visual);
                selectedId = database.locations.FirstOrDefault(item => item.IsBattlefield)?.Id ?? LocationLightingTestBootstrap.CampId;
                selectedKind = Kind.None;
                RefreshList();
            }, true);
            return true;
        }

        // Дублировать поле боя: сборку и запись поля (свои гексы, теги, вид).
        private bool DuplicateBattlefieldScene(LocalLocationDefinition location)
        {
            if (!location.IsBattlefield) return false;
            int index = fields.Battlefields.ToList().FindIndex(item => item != null && item.Id == location.BattlefieldId);
            string fieldId = UniqueBattlefieldId("field");
            SerializedObject serialized = new SerializedObject(fields);
            SerializedProperty list = serialized.FindProperty("battlefields");
            if (index >= 0)
            {
                list.InsertArrayElementAtIndex(index);
                SerializedProperty copy = list.GetArrayElementAtIndex(index + 1);
                copy.FindPropertyRelative("id").stringValue = fieldId;
                copy.FindPropertyRelative("displayLabel").stringValue = location.DisplayName + " · копия";
                serialized.ApplyModifiedProperties();
            }
            LocalLocationDefinition scene = JsonUtility.FromJson<LocalLocationDefinition>(JsonUtility.ToJson(location));
            LocationVisualDefinition visual = Visual != null ? JsonUtility.FromJson<LocationVisualDefinition>(JsonUtility.ToJson(Visual)) : new LocationVisualDefinition();
            Change(() =>
            {
                scene.Id = "field." + fieldId;
                scene.DisplayName = location.DisplayName + " · копия";
                scene.BattlefieldId = index >= 0 ? fieldId : location.BattlefieldId;
                visual.LocationId = scene.Id;
                database.locations.Add(scene);
                database.visuals.Add(visual);
                selectedId = scene.Id;
                RefreshList();
            }, true);
            return true;
        }

        // Общее для всех полей: скорость ходьбы и справочник тегов (общий вид
        // гекса — карточка «Вид гекса» без «Свой вид у этого поля»).
        private void BuildCommonBattlefieldSettings()
        {
            Heading("Общее для всех полей боя");
            SerializedObject serialized = new SerializedObject(fields);
            VisualElement common = new VisualElement();
            Slider walk = BattlefieldHexEditorKit.BoundSlider("Ходьба, гексов/с", serialized.FindProperty("walkHexesPerSecond"),
                BattlefieldDatabaseAsset.MinWalkHexesPerSecond, BattlefieldDatabaseAsset.MaxWalkHexesPerSecond);
            walk.tooltip = "Одна скорость на бой и исследование мест: бойцы на поле и отряд в месте. Темп шага анимации подстраивается.";
            common.Add(walk);
            Foldout tags = new Foldout { text = "Справочник тегов", value = false };
            tags.Add(new PropertyField(serialized.FindProperty("tags"), "Теги"));
            tags.Add(new Button(() =>
            {
                serialized.Update();
                SerializedProperty list = serialized.FindProperty("tags");
                list.arraySize++;
                SerializedProperty tag = list.GetArrayElementAtIndex(list.arraySize - 1);
                string id = "new_tag";
                for (int i = 2; fields.FindTag(id) != null; i++) id = "new_tag_" + i;
                tag.FindPropertyRelative("id").stringValue = id;
                tag.FindPropertyRelative("displayLabel").stringValue = "Новый тег";
                serialized.ApplyModifiedProperties();
                BuildSettings();
            }) { text = "+ Тег" });
            common.Add(tags);
            common.Bind(serialized);
            common.TrackSerializedObjectValue(serialized, _ => EditorUtility.SetDirty(fields));
            settings.Add(common);
            Help("Общий вид гекса правится в карточке «Вид гекса» любого поля без своего вида.");
        }

        // Вкладка «Место» у поля боя: вместо «Перемещения» и «Боя на месте».
        private void BuildBattlefieldSceneSettings(LocalLocationDefinition location)
        {
            BattlefieldDefinitionData field = Field;
            Heading("Поле боя");
            if (field == null)
            {
                Help("Нет записи поля «" + location.BattlefieldId + "» — поле боя повреждено: удалите его и создайте заново.");
                return;
            }
            Help("Поле собирается как место: земля, предметы, раскидка и свет — на других вкладках. В бою (на дороге и в полигоне) " +
                 "камера снимает рамку кадра поля 16:9 на холсте — живьём: время суток, огонь, анимация, трава гнётся от бойцов. " +
                 "Ниже — теги, рамка кадра, гексы и вид гекса этого поля, в конце — общее для всех полей.");
            int index = fields.Battlefields.ToList().IndexOf(field);
            SerializedObject serialized = new SerializedObject(fields);
            SerializedProperty property = BattlefieldHexEditorKit.FieldProperty(serialized, index);

            TextField label = new TextField("Название поля") { value = field.DisplayLabel, isDelayed = true };
            label.RegisterValueChangedCallback(evt =>
            {
                serialized.Update();
                property.FindPropertyRelative("displayLabel").stringValue = evt.newValue;
                serialized.ApplyModifiedProperties();
                Change(() => location.DisplayName = evt.newValue);
                RefreshList();
            });
            settings.Add(label);

            Label tagsTitle = new Label("Теги (подбор поля для дорожного боя по местности):");
            tagsTitle.style.marginTop = 4;
            settings.Add(tagsTitle);
            VisualElement chips = new VisualElement();
            chips.style.flexDirection = FlexDirection.Row;
            chips.style.flexWrap = Wrap.Wrap;
            foreach (BattlefieldTagDefinition tag in fields.Tags)
            {
                if (tag == null || string.IsNullOrEmpty(tag.Id)) continue;
                string tagId = tag.Id;
                bool on = field.HasTag(tagId);
                Button chip = new Button(() =>
                {
                    serialized.Update();
                    SerializedProperty tagIds = property.FindPropertyRelative("tagIds");
                    int found = -1;
                    for (int i = 0; i < tagIds.arraySize; i++)
                        if (tagIds.GetArrayElementAtIndex(i).stringValue == tagId) found = i;
                    if (found >= 0) tagIds.DeleteArrayElementAtIndex(found);
                    else
                    {
                        tagIds.arraySize++;
                        tagIds.GetArrayElementAtIndex(tagIds.arraySize - 1).stringValue = tagId;
                    }
                    serialized.ApplyModifiedProperties();
                    BuildSettings();
                })
                {
                    text = string.IsNullOrEmpty(tag.DisplayLabel) ? tagId : tag.DisplayLabel,
                    tooltip = tagId + (string.IsNullOrEmpty(tag.Description) ? string.Empty : "\n" + tag.Description)
                };
                Color color = tag.Color;
                chip.style.backgroundColor = on ? new Color(color.r, color.g, color.b, .85f) : new Color(.16f, .16f, .17f, 1f);
                chip.style.color = on ? Color.white : new Color(.62f, .62f, .62f);
                chips.Add(chip);
            }
            settings.Add(chips);
            bool sandbox = fields.SandboxBattlefieldId == field.Id;
            Button use = new Button(() =>
            {
                serialized.Update();
                serialized.FindProperty("sandboxBattlefieldId").stringValue = field.Id;
                serialized.ApplyModifiedProperties();
                BuildSettings();
            }) { text = sandbox ? "● Полигон идёт на этом поле" : "Сделать полем полигона" };
            use.SetEnabled(!sandbox);
            settings.Add(use);

            Heading("Кадр поля на холсте");
            Help("Рамка 16:9 на холсте — то, что видно в бою. 1 и 0 — весь холст (у холста 16:9 рамка совпадает с ним).");
            VisualElement frameCard = new VisualElement();
            frameCard.Add(BattlefieldHexEditorKit.BoundSlider("Масштаб", property.FindPropertyRelative("backgroundScale"), .5f, 3f));
            SerializedProperty offset = property.FindPropertyRelative("backgroundOffset");
            frameCard.Add(BattlefieldHexEditorKit.BoundSlider("Сдвиг X", offset.FindPropertyRelative("x"), -.5f, .5f));
            frameCard.Add(BattlefieldHexEditorKit.BoundSlider("Сдвиг Y", offset.FindPropertyRelative("y"), -.5f, .5f));
            frameCard.Bind(serialized);
            frameCard.TrackSerializedObjectValue(serialized, _ => FieldChanged());
            settings.Add(frameCard);

            Heading("Гексы поля");
            Help("Отключённые гексы — стены: туда нельзя пойти, в бою их нет. Кисть: ЛКМ по гексу на холсте, протяжка — тем же состоянием.");
            VisualElement row = Row();
            AddButton(row, tool == Tool.FieldHexes ? "● Кисть гексов" : "Кисть гексов", () => SetTool(tool == Tool.FieldHexes ? Tool.Select : Tool.FieldHexes));
            AddButton(row, "Включить все", () => EditFieldCells(field, cells => cells.Clear()));
            AddButton(row, "Инвертировать", () => EditFieldCells(field, cells =>
            {
                List<Vector2Int> inverted = SandboxArenaShape.Cells().Select(cell => new Vector2Int(cell.Q, cell.R))
                    .Where(cell => !cells.Contains(cell)).ToList();
                cells.Clear();
                cells.AddRange(inverted);
            }));
            BuildFieldHexCards();
            BuildCommonBattlefieldSettings();
        }

        // Правка отключённых гексов поля (одна запись Undo).
        private void EditFieldCells(BattlefieldDefinitionData field, Action<List<Vector2Int>> edit)
        {
            int index = fields.Battlefields.ToList().IndexOf(field);
            if (index < 0) return;
            SerializedObject serialized = new SerializedObject(fields);
            SerializedProperty array = BattlefieldHexEditorKit.FieldProperty(serialized, index).FindPropertyRelative("disabledCells");
            List<Vector2Int> cells = new List<Vector2Int>();
            for (int i = 0; i < array.arraySize; i++) cells.Add(array.GetArrayElementAtIndex(i).vector2IntValue);
            edit(cells);
            array.ClearArray();
            for (int i = 0; i < cells.Count; i++)
            {
                array.arraySize++;
                array.GetArrayElementAtIndex(i).vector2IntValue = cells[i];
            }
            serialized.ApplyModifiedProperties();
            FieldChanged();
            arenaField = null;
            SyncArenaView();
        }

        // Кисть гексов поля на холсте.
        private bool HandleFieldHexInput(Event evt, Rect frame, Vector2 pixel)
        {
            if (tool != Tool.FieldHexes || Location == null || !Location.IsBattlefield || Field == null) return false;
            bool down = evt.type == EventType.MouseDown && evt.button == 0 && frame.Contains(evt.mousePosition);
            bool drag = evt.type == EventType.MouseDrag && evt.button == 0;
            if (evt.type == EventType.MouseUp && evt.button == 0) { fieldCellsStroke.Clear(); return false; }
            if (!down && !drag) return false;
            BattlefieldDefinitionData field = Field;
            BattlefieldGridLayout layout = FieldLayoutOnCanvas(field);
            if (!layout.TryGetCell(pixel, out int q, out int r)) { evt.Use(); return true; }
            Vector2 delta = pixel - layout.GetCenter(q, r);
            delta.y /= layout.VerticalScale;
            if (delta.magnitude > layout.Size) { evt.Use(); return true; }
            HexCoord cell = new HexCoord(q, r);
            if (down)
            {
                fieldCellsStroke.Clear();
                fieldCellPaintDisabled = !field.IsCellDisabled(q, r);
            }
            if (fieldCellsStroke.Add(cell))
                EditFieldCells(field, cells =>
                {
                    Vector2Int value = new Vector2Int(q, r);
                    cells.Remove(value);
                    if (fieldCellPaintDisabled) cells.Add(value);
                });
            evt.Use();
            return true;
        }

        // Рамка кадра поля и сетка арены в пикселях холста.
        private Rect FieldFrameOnCanvas(BattlefieldDefinitionData field) => BattlefieldFrame.FrameOnPicture(CanvasSize, field);

        private BattlefieldGridLayout FieldLayoutOnCanvas(BattlefieldDefinitionData field) =>
            BattlefieldFrame.ComputeLayout(FieldFrameOnCanvas(field), BattlefieldFrame.GetGridArea(field));
    }
}
