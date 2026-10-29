using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.SettlementDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.ItemDatabase.Editor
{
    // 12Т: «База предметов» — единый каталог вещей игры. Слева поиск по
    // названию и фильтр по виду со списком миниатюр; справа карточка вещи:
    // изображение (перетащить в поле), описание и история отдельно от
    // механики, торговля, «пока надето», эффекты «при использовании»,
    // рассчитанная подпись механики, проверка и «где используется».
    // Правки — через SerializedObject: Undo/Redo и сохранение как обычно.
    public sealed class ItemDatabaseWindow : EditorWindow
    {
        private const float ListWidth = 300f;
        private static readonly string[] KindFilterLabels =
            { "Все виды", "Оружие", "Защита", "Особые", "Сюжетные", "Расходуемые", "Товары", "Артефакты", "С ошибками" };

        [SerializeField] private string selectedId = string.Empty;
        [SerializeField] private string search = string.Empty;
        [SerializeField] private int kindFilter;

        private ItemDatabaseAsset database;
        private SerializedObject serialized;
        private Vector2 listScroll;
        private Vector2 cardScroll;
        private List<string> usages;
        private string usagesFor;

        [MenuItem("Kingdom Survival/База предметов")]
        public static void OpenWindow()
        {
            ItemDatabaseWindow window = GetWindow<ItemDatabaseWindow>();
            window.titleContent = new GUIContent("База предметов");
            window.minSize = new Vector2(980f, 560f);
        }

        public static void OpenAt(string itemId)
        {
            OpenWindow();
            ItemDatabaseWindow window = GetWindow<ItemDatabaseWindow>();
            window.selectedId = itemId ?? string.Empty;
            window.Repaint();
        }

        private void OnEnable()
        {
            database = ItemDatabaseBootstrap.LoadOrCreate();
            serialized = database != null ? new SerializedObject(database) : null;
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
            if (database != null)
                AssetDatabase.SaveAssetIfDirty(database);
        }

        private void OnLostFocus()
        {
            if (database != null)
                AssetDatabase.SaveAssetIfDirty(database);
        }

        private void OnGUI()
        {
            if (database == null || serialized == null)
            {
                EditorGUILayout.HelpBox("База предметов не найдена: " + ItemDatabaseAsset.AssetPath, MessageType.Error);
                if (GUILayout.Button("Создать базу"))
                    OnEnable();
                return;
            }
            serialized.Update();
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawList();
            DrawCard();
            EditorGUILayout.EndHorizontal();
            if (serialized.ApplyModifiedProperties() && EditorApplication.isPlaying)
                ItemDatabaseRuntime.Apply(database);
        }

        // ------------------------------------------------------------------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Поиск:", GUILayout.Width(46f));
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.Width(200f));
            kindFilter = EditorGUILayout.Popup(kindFilter, KindFilterLabels, EditorStyles.toolbarPopup, GUILayout.Width(130f));
            GUILayout.Space(8f);
            if (GUILayout.Button("+ Новая вещь", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                CreateItem(null);
            using (new EditorGUI.DisabledScope(FindIndex(selectedId) < 0))
            {
                if (GUILayout.Button("Дублировать", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                    CreateItem(database.Find(selectedId));
                if (GUILayout.Button("Удалить…", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    DeleteSelected();
            }
            if (GUILayout.Button("+ Недостающие исходные", EditorStyles.toolbarButton, GUILayout.Width(160f)))
            {
                Undo.RecordObject(database, "Исходные вещи");
                List<string> added = ItemDatabaseBootstrap.AddMissingDefaults(database);
                EditorUtility.SetDirty(database);
                serialized.Update();
                ShowNotification(new GUIContent(added.Count > 0 ? "Добавлено: " + added.Count : "Все исходные вещи уже в базе"));
            }
            GUILayout.FlexibleSpace();
            int problems = 0;
            foreach (ItemDatabaseEntry entry in database.items)
                problems += ItemCatalogValidator.Validate(entry?.Data, AllDefinitions()).Count > 0 ? 1 : 0;
            GUILayout.Label("Вещей: " + database.items.Count + (problems > 0 ? "   ⚠ с ошибками: " + problems : "   ошибок нет"));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            for (int i = 0; i < database.items.Count; i++)
            {
                ItemDatabaseEntry entry = database.items[i];
                if (entry?.Data == null || !PassesFilter(entry))
                    continue;
                bool selected = entry.Data.Id == selectedId;
                Rect row = GUILayoutUtility.GetRect(ListWidth - 20f, 40f);
                if (selected)
                    EditorGUI.DrawRect(row, new Color(0.24f, 0.33f, 0.45f, 1f));
                Rect icon = new Rect(row.x + 4f, row.y + 4f, 32f, 32f);
                DrawSprite(icon, entry.Icon);
                bool broken = ItemCatalogValidator.Validate(entry.Data, AllDefinitions()).Count > 0;
                GUI.Label(new Rect(icon.xMax + 6f, row.y + 3f, row.width - 48f, 18f),
                    (broken ? "⚠ " : string.Empty) + (string.IsNullOrEmpty(entry.Data.Name) ? "(без названия)" : entry.Data.Name), EditorStyles.boldLabel);
                GUI.Label(new Rect(icon.xMax + 6f, row.y + 20f, row.width - 48f, 16f),
                    KindLabel(entry.Data) + (entry.Data.Price > 0 ? " · " + entry.Data.Price + " зол." : string.Empty), EditorStyles.miniLabel);
                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
                {
                    selectedId = entry.Data.Id;
                    GUI.FocusControl(null);
                    Event.current.Use();
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private bool PassesFilter(ItemDatabaseEntry entry)
        {
            ItemDefinition data = entry.Data;
            if (!string.IsNullOrWhiteSpace(search))
            {
                string s = search.Trim();
                bool match = (data.Name ?? string.Empty).IndexOf(s, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                             (data.Id ?? string.Empty).IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match)
                    return false;
            }
            switch (kindFilter)
            {
                case 0: return true;
                case 7: return data.Artifact;
                case 8: return ItemCatalogValidator.Validate(data, AllDefinitions()).Count > 0;
                default: return (int)data.Kind == kindFilter - 1;
            }
        }

        private void DrawCard()
        {
            EditorGUILayout.BeginVertical();
            int index = FindIndex(selectedId);
            if (index < 0)
            {
                EditorGUILayout.HelpBox("Выберите вещь слева или создайте новую.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }
            ItemDatabaseEntry entry = database.items[index];
            SerializedProperty entryProperty = serialized.FindProperty("items").GetArrayElementAtIndex(index);
            SerializedProperty data = entryProperty.FindPropertyRelative("Data");
            cardScroll = EditorGUILayout.BeginScrollView(cardScroll);

            EditorGUILayout.BeginHorizontal();
            SerializedProperty icon = entryProperty.FindPropertyRelative("Icon");
            EditorGUILayout.BeginVertical(GUILayout.Width(132f));
            Rect iconRect = GUILayoutUtility.GetRect(128f, 128f, GUILayout.Width(128f), GUILayout.Height(128f));
            DrawSprite(iconRect, entry.Icon);
            HandleSpriteDrop(iconRect, icon);
            EditorGUILayout.PropertyField(icon, GUIContent.none);
            if (icon.objectReferenceValue != null && GUILayout.Button("Очистить"))
                icon.objectReferenceValue = null;
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            EditorGUILayout.PropertyField(data.FindPropertyRelative("Name"), new GUIContent("Название"));
            EditorGUILayout.PropertyField(data.FindPropertyRelative("Kind"), new GUIContent("Вид", "Оружие/защита/особое надеваются в свой слот; расходуемое используют; товар только продают."));
            EditorGUILayout.PropertyField(data.FindPropertyRelative("Artifact"), new GUIContent("Артефакт", "Авторская категория особой вещи со своей историей. Слота не добавляет."));
            DrawLabeledText(data.FindPropertyRelative("Description"), "Описание (видит игрок)", 3);
            DrawLabeledText(data.FindPropertyRelative("Origin"), "Происхождение (видит игрок)", 2);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            Section("Механика — так её увидит игрок");
            ItemDefinition definition = entry.Data;
            string mechanics = ItemUseService.Describe(definition);
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(mechanics) ? "Без механики: вещь для истории или проверок «вещь рядом»." : mechanics, MessageType.None);
            List<string> problems = ItemCatalogValidator.Validate(definition, AllDefinitions());
            foreach (string problem in problems)
                EditorGUILayout.HelpBox(problem, MessageType.Warning);

            Section("Торговля");
            EditorGUILayout.PropertyField(data.FindPropertyRelative("Price"), new GUIContent("Базовая цена, зол. за штуку", "Торговец умножает на свой процент покупки/продажи."));
            EditorGUILayout.PropertyField(data.FindPropertyRelative("CanBuy"), new GUIContent("Можно купить у торговца"));
            EditorGUILayout.PropertyField(data.FindPropertyRelative("CanSell"), new GUIContent("Можно продать торговцу"));
            EditorGUILayout.PropertyField(data.FindPropertyRelative("Unique"), new GUIContent("Одна на мир", "Купленная однажды больше нигде не появится; продажа и смена дня её не восстанавливают."));
            EditorGUILayout.PropertyField(data.FindPropertyRelative("Protected"), new GUIContent("Защищена", "Нельзя продать или отдать (сюжетные вещи защищены всегда)."));

            if (definition.Slot != ItemSlot.None)
            {
                Section("Пока надето (слот: " + SlotLabel(definition.Slot) + ")");
                SerializedProperty modifier = data.FindPropertyRelative("Modifier");
                DrawModifier(modifier);
                DrawLabeledText(data.FindPropertyRelative("UnitTypes"), null, 0, "Кому подходит (ID типов Базы существ; пусто — любому бойцу)");
            }

            if (definition.Kind == ItemKind.Consumable)
            {
                Section("При использовании");
                EditorGUILayout.PropertyField(data.FindPropertyRelative("Uses"), new GUIContent("Применений у новой вещи"));
                EditorGUILayout.PropertyField(data.FindPropertyRelative("UseVerb"), new GUIContent("Подпись кнопки", "«Использовать», «Изучить», «Выпить»… Пусто — «Использовать»."));
                SerializedProperty effects = data.FindPropertyRelative("UseEffects");
                for (int i = 0; i < effects.arraySize; i++)
                {
                    SerializedProperty effect = effects.GetArrayElementAtIndex(i);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(effect.FindPropertyRelative("Type"), new GUIContent("Эффект " + (i + 1)));
                    if (GUILayout.Button("×", GUILayout.Width(22f)))
                    {
                        effects.DeleteArrayElementAtIndex(i);
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                    ItemUseEffectType type = (ItemUseEffectType)effect.FindPropertyRelative("Type").enumValueIndex;
                    switch (type)
                    {
                        case ItemUseEffectType.RestoreHitPoints:
                            EditorGUILayout.PropertyField(effect.FindPropertyRelative("Amount"), new GUIContent("Сколько HP вернуть", "Живому присутствующему; не выше максимума; тяжёлую рану не лечит."));
                            break;
                        case ItemUseEffectType.AddSupplies:
                            EditorGUILayout.PropertyField(effect.FindPropertyRelative("Amount"), new GUIContent("Сколько припасов", "В общие припасы активного похода."));
                            break;
                        case ItemUseEffectType.RemoveExhaustion:
                            EditorGUILayout.LabelField("Снимает изнеможение с выбранного присутствующего человека.", EditorStyles.wordWrappedMiniLabel);
                            break;
                        case ItemUseEffectType.GrantKnowledge:
                            EditorGUILayout.PropertyField(effect.FindPropertyRelative("KnowledgeId"), new GUIContent("ID знания"));
                            EditorGUILayout.PropertyField(effect.FindPropertyRelative("StageFlagId"), new GUIContent("Флаг этапа поиска", "Открывает область поиска на карте (необязательно)."));
                            DrawLabeledText(effect.FindPropertyRelative("KnowledgeText"), "Что узнаёт герой (строка итога и Хроники)", 2);
                            break;
                    }
                    EditorGUILayout.EndVertical();
                }
                if (GUILayout.Button("+ Эффект при использовании", GUILayout.Width(220f)))
                {
                    effects.InsertArrayElementAtIndex(effects.arraySize);
                    SerializedProperty added = effects.GetArrayElementAtIndex(effects.arraySize - 1);
                    added.FindPropertyRelative("Amount").intValue = 1;
                    added.FindPropertyRelative("KnowledgeId").stringValue = string.Empty;
                }
                EditorGUILayout.HelpBox("Поддерживаются: вернуть HP, снять изнеможение, добавить припасы, дать сведения. " +
                                        "Иммунитеты, отражение проклятий и шансы оглушения не работают без нового кода — не обещайте их в описании.",
                    MessageType.None);
            }

            Section("Для авторских проверок");
            DrawLabeledText(data.FindPropertyRelative("Tags"), null, 0, "Метки (технические)");
            EditorGUILayout.LabelField("В диалогах вещь проверяется условием «Предмет рядом» по ID ниже.", EditorStyles.wordWrappedMiniLabel);
            DrawLabeledText(entryProperty.FindPropertyRelative("AuthorNotes"), "Заметки автора (игроку не видны)", 2);

            Section("Технические данные");
            DrawIdField(data.FindPropertyRelative("Id"), entry);

            Section("Где используется");
            // Поиск — по кнопке: файлы проекта не сканируются каждый кадр.
            if (GUILayout.Button("Найти использование", GUILayout.Width(200f)))
            {
                usages = FindUsages(definition.Id);
                usagesFor = definition.Id;
            }
            if (usagesFor == definition.Id && usages != null)
            {
                if (usages.Count == 0)
                    EditorGUILayout.LabelField("Ссылок не найдено.", EditorStyles.miniLabel);
                foreach (string usage in usages)
                    EditorGUILayout.LabelField("• " + usage, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ------------------------------------------------------------------

        private static void Section(string title)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static void DrawLabeledText(SerializedProperty property, string label, int lines, string arrayLabel = null)
        {
            if (property == null)
                return;
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(arrayLabel ?? label), true);
                return;
            }
            EditorGUILayout.LabelField(label);
            string current = property.stringValue ?? string.Empty;
            string next = EditorGUILayout.TextArea(current, new GUIStyle(EditorStyles.textArea) { wordWrap = true },
                GUILayout.MinHeight(16f * Mathf.Max(1, lines)));
            if (next != current)
                property.stringValue = next;
        }

        private static void DrawModifier(SerializedProperty modifier)
        {
            (string Field, string Label)[] fields =
            {
                ("MaxHitPoints", "HP (максимум)"), ("Attack", "Атака"), ("Defense", "Защита"), ("Damage", "Урон"),
                ("Movement", "Движение"), ("Initiative", "Инициатива"), ("AttackRange", "Дальность")
            };
            foreach ((string field, string label) in fields)
                EditorGUILayout.PropertyField(modifier.FindPropertyRelative(field), new GUIContent(label));
        }

        private void DrawIdField(SerializedProperty idProperty, ItemDatabaseEntry entry)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ID", entry.Data.Id);
            if (GUILayout.Button("Изменить ID…", GUILayout.Width(110f)))
            {
                List<string> used = FindUsages(entry.Data.Id);
                if (used.Count == 0 || EditorUtility.DisplayDialog("Изменить ID",
                        "На этот ID ссылаются:\n" + string.Join("\n", used.GetRange(0, Math.Min(8, used.Count))) +
                        "\n\nСсылки и уже сохранённые партии со старым ID не обновятся сами. Название меняется без смены ID. Продолжить?",
                        "Изменить", "Отмена"))
                    ItemIdPrompt.Show(entry.Data.Id, newId =>
                    {
                        if (string.IsNullOrWhiteSpace(newId) || database.Find(newId) != null)
                        {
                            EditorUtility.DisplayDialog("Изменить ID", "ID пустой или уже занят.", "OK");
                            return;
                        }
                        Undo.RecordObject(database, "Изменить ID вещи");
                        entry.Data.Id = newId.Trim();
                        selectedId = entry.Data.Id;
                        EditorUtility.SetDirty(database);
                        serialized.Update();
                    });
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("ID не меняется при переименовании: по нему вещь хранится в сохранениях.", EditorStyles.miniLabel);
        }

        private static void DrawSprite(Rect rect, Sprite sprite)
        {
            EditorGUI.DrawRect(rect, new Color(0.16f, 0.15f, 0.14f, 1f));
            if (sprite == null || sprite.texture == null)
            {
                GUI.Label(rect, "нет\nрисунка", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true });
                return;
            }
            Rect tr = sprite.textureRect;
            Texture2D texture = sprite.texture;
            Rect uv = new Rect(tr.x / texture.width, tr.y / texture.height, tr.width / texture.width, tr.height / texture.height);
            float aspect = tr.width / Mathf.Max(1f, tr.height);
            Rect fit = rect;
            if (aspect > 1f)
                fit.height = rect.width / aspect;
            else
                fit.width = rect.height * aspect;
            fit.center = rect.center;
            GUI.DrawTextureWithTexCoords(fit, texture, uv, true);
        }

        private static void HandleSpriteDrop(Rect rect, SerializedProperty property)
        {
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition) || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform))
                return;
            Sprite sprite = null;
            foreach (UnityEngine.Object dragged in DragAndDrop.objectReferences)
            {
                sprite = dragged as Sprite;
                if (sprite == null && dragged is Texture2D texture)
                    sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GetAssetPath(texture));
                if (sprite != null)
                    break;
            }
            if (sprite == null)
                return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                property.objectReferenceValue = sprite;
            }
            e.Use();
        }

        private int FindIndex(string itemId)
        {
            if (database?.items == null || string.IsNullOrEmpty(itemId))
                return -1;
            return database.items.FindIndex(entry => entry?.Data != null && entry.Data.Id == itemId);
        }

        private List<ItemDefinition> AllDefinitions()
        {
            List<ItemDefinition> all = new List<ItemDefinition>();
            foreach (ItemDatabaseEntry entry in database.items)
            {
                if (entry?.Data != null)
                    all.Add(entry.Data);
            }
            return all;
        }

        private void CreateItem(ItemDatabaseEntry source)
        {
            Undo.RecordObject(database, source != null ? "Дублировать вещь" : "Новая вещь");
            string baseId = source != null ? source.Data.Id + "_copy" : "item.new";
            string id = baseId;
            for (int n = 2; database.Find(id) != null; n++)
                id = baseId + "_" + n;
            ItemDatabaseEntry entry = new ItemDatabaseEntry
            {
                Data = source != null ? source.Data.Clone() : new ItemDefinition { Name = "Новая вещь", Kind = ItemKind.TradeGood, Price = 1 },
                Icon = source?.Icon,
                AuthorNotes = source?.AuthorNotes ?? string.Empty
            };
            entry.Data.Id = id;
            if (source != null)
                entry.Data.Name = source.Data.Name + " (копия)";
            database.items.Add(entry);
            EditorUtility.SetDirty(database);
            serialized.Update();
            selectedId = id;
        }

        private void DeleteSelected()
        {
            int index = FindIndex(selectedId);
            if (index < 0)
                return;
            List<string> used = FindUsages(selectedId);
            string message = used.Count == 0
                ? "Удалить «" + database.items[index].Data.Name + "»?"
                : "На вещь есть ссылки (" + used.Count + "):\n" + string.Join("\n", used.GetRange(0, Math.Min(10, used.Count))) +
                  "\n\nУже сохранённые партии сохранят экземпляры с этим ID, но игра покажет их как неизвестные. Удалить всё равно?";
            if (!EditorUtility.DisplayDialog("Удалить вещь", message, "Удалить", "Отмена"))
                return;
            Undo.RecordObject(database, "Удалить вещь");
            database.items.RemoveAt(index);
            EditorUtility.SetDirty(database);
            serialized.Update();
            selectedId = string.Empty;
        }

        // Реальные ссылки: ассортимент и объявления поселений, эффекты и
        // условия диалогов, код и сиды проекта (по ID и имени константы).
        public static List<string> FindUsages(string itemId)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(itemId))
                return result;

            SettlementDatabaseAsset settlements = AssetDatabase.LoadAssetAtPath<SettlementDatabaseAsset>(SettlementDatabaseAsset.AssetPath);
            if (settlements?.profiles != null)
            {
                foreach (SettlementProfileEntry profile in settlements.profiles)
                {
                    if (profile?.Data?.Merchants == null)
                        continue;
                    foreach (MerchantDefinition merchant in profile.Data.Merchants)
                    {
                        if (merchant?.Stock != null && merchant.Stock.Exists(s => s != null && !s.IsSupplies && s.ItemId == itemId))
                            result.Add("База поселений: «" + profile.Data.Name + "», торговец «" + merchant.DisplayName + "»");
                    }
                }
            }

            DialogueDatabaseAsset dialogues = Resources.Load<DialogueDatabaseAsset>(DialogueDatabaseAsset.ResourcesPath);
            if (dialogues != null)
            {
                foreach (DialogueDefinitionData dialogue in dialogues.Dialogues)
                {
                    if (dialogue != null && DialogueMentions(dialogue, itemId))
                        result.Add("База диалогов: " + dialogue.Id + " (" + dialogue.Title + ")");
                }
            }

            List<string> needles = new List<string> { "\"" + itemId + "\"" };
            foreach (FieldInfo field in typeof(ItemCatalog).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.IsLiteral && field.FieldType == typeof(string) && (string)field.GetRawConstantValue() == itemId)
                    needles.Add("ItemCatalog." + field.Name);
            }
            foreach (string file in Directory.GetFiles("Assets/_Project", "*.*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".cs", StringComparison.Ordinal) && !file.EndsWith(".json", StringComparison.Ordinal))
                    continue;
                if (file.Replace('\\', '/').EndsWith("Scripts/Core/Items.cs", StringComparison.Ordinal))
                    continue;
                string text = File.ReadAllText(file);
                foreach (string needle in needles)
                {
                    if (text.Contains(needle))
                    {
                        result.Add("Код/данные: " + file.Replace('\\', '/').Replace("Assets/_Project/", string.Empty));
                        break;
                    }
                }
            }
            return result;
        }

        private static bool DialogueMentions(DialogueDefinitionData dialogue, string itemId)
        {
            foreach (DialogueNodeData node in dialogue.Nodes)
            {
                if (node == null)
                    continue;
                foreach (DialogueTextBlockData block in node.TextBlocks)
                {
                    if (block != null && (EffectsMention(block.OnRevealEffects, itemId) || ConditionsMention(block.Conditions, itemId)))
                        return true;
                }
                foreach (DialogueChoiceData choice in node.Choices)
                {
                    if (choice != null && (EffectsMention(choice.SuccessEffects, itemId) || EffectsMention(choice.FailureEffects, itemId) ||
                                           ConditionsMention(choice.Conditions, itemId)))
                        return true;
                }
            }
            return false;
        }

        private static bool EffectsMention(IReadOnlyList<NarrativeEffect> effects, string itemId)
        {
            if (effects == null)
                return false;
            foreach (NarrativeEffect effect in effects)
            {
                if (effect != null && (effect.Type == NarrativeEffectType.GrantItem || effect.Type == NarrativeEffectType.RemoveItem) &&
                    effect.StringParam == itemId)
                    return true;
            }
            return false;
        }

        private static bool ConditionsMention(NarrativeConditionGroup group, string itemId)
        {
            if (group?.Conditions == null)
                return false;
            foreach (NarrativeCondition condition in group.Conditions)
            {
                if (condition != null && condition.Type == NarrativeConditionType.ItemPresent && condition.StringParam == itemId)
                    return true;
            }
            return false;
        }

        public static string KindLabel(ItemDefinition item)
        {
            string kind;
            switch (item.Kind)
            {
                case ItemKind.Weapon: kind = "оружие"; break;
                case ItemKind.Protection: kind = "защита"; break;
                case ItemKind.Special: kind = "особое"; break;
                case ItemKind.Story: kind = "сюжетное"; break;
                case ItemKind.Consumable: kind = "расходуемое"; break;
                default: kind = "товар"; break;
            }
            return item.Artifact ? kind + ", артефакт" : kind;
        }

        private static string SlotLabel(ItemSlot slot)
        {
            switch (slot)
            {
                case ItemSlot.Weapon: return "оружие";
                case ItemSlot.Protection: return "защита/одежда";
                case ItemSlot.Special: return "особый предмет";
                default: return "нет";
            }
        }
    }

    // Небольшое окно ввода нового ID.
    internal sealed class ItemIdPrompt : EditorWindow
    {
        private string value;
        private Action<string> onDone;

        public static void Show(string current, Action<string> done)
        {
            ItemIdPrompt window = CreateInstance<ItemIdPrompt>();
            window.titleContent = new GUIContent("Новый ID");
            window.value = current;
            window.onDone = done;
            window.minSize = window.maxSize = new Vector2(380f, 80f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            value = EditorGUILayout.TextField("ID", value);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Готово"))
            {
                onDone?.Invoke(value);
                Close();
            }
            if (GUILayout.Button("Отмена"))
                Close();
            EditorGUILayout.EndHorizontal();
        }
    }
}
