using System;
using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.DialogueDatabase;
using KingdomSurvival.ItemDatabase;
using KingdomSurvival.ItemDatabase.Editor;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.SettlementDatabase.Editor
{
    // 12Т: «База поселений». Слева — поиск и список профилей (создать,
    // дублировать с новым ID, удалить с показом мест использования); справа —
    // разделы: Основное, Изображение (предпросмотр кадра 1920×1080 / 1280×720),
    // Люди и действия, Торговля, Корчма, Доска объявлений, Состояния, Привязка
    // и «Где используется»; отдельная страница — общие Поручения. Диалоги,
    // говорящие, вещи и торговцы выбираются из баз с поиском. Все правки —
    // через SerializedObject (Undo/Redo, отметка изменений). Проверка ссылок —
    // SettlementCatalogValidator, строки видны рядом с записью.
    public sealed class SettlementDatabaseWindow : EditorWindow
    {
        private static readonly string[] Tabs =
            { "Основное", "Изображение", "Люди и действия", "Торговля", "Корчма", "Доска объявлений", "Состояния", "Привязка" };

        [SerializeField] private string selectedId = string.Empty;
        [SerializeField] private int tab;
        [SerializeField] private bool errandsPage;
        [SerializeField] private string search = string.Empty;
        [SerializeField] private bool preview720;

        private SettlementDatabaseAsset database;
        private SerializedObject serialized;
        private DialogueDatabaseAsset dialogues;
        private Vector2 listScroll;
        private Vector2 pageScroll;

        [MenuItem("Kingdom Survival/База поселений")]
        public static void OpenWindow()
        {
            SettlementDatabaseWindow window = GetWindow<SettlementDatabaseWindow>();
            window.titleContent = new GUIContent("База поселений");
            window.minSize = new Vector2(1100f, 640f);
        }

        private void OnEnable()
        {
            database = SettlementDatabaseBootstrap.LoadOrCreate();
            serialized = database != null ? new SerializedObject(database) : null;
            dialogues = AssetDatabase.LoadAssetAtPath<DialogueDatabaseAsset>(SettlementDatabaseBootstrap.DialogueDatabasePath);
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
                EditorGUILayout.HelpBox("База поселений не найдена: " + SettlementDatabaseAsset.AssetPath, MessageType.Error);
                if (GUILayout.Button("Создать базу"))
                    OnEnable();
                return;
            }
            serialized.Update();
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawList();
            EditorGUILayout.BeginVertical();
            pageScroll = EditorGUILayout.BeginScrollView(pageScroll);
            if (errandsPage)
                DrawErrands();
            else
                DrawProfile();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            if (serialized.ApplyModifiedProperties() && EditorApplication.isPlaying)
                SettlementDatabaseRuntime.Apply(database);
        }

        // ------------------------------------------------------------------
        // Каркас
        // ------------------------------------------------------------------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Поиск:", GUILayout.Width(46f));
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.Width(180f));
            if (GUILayout.Button("+ Поселение", EditorStyles.toolbarButton, GUILayout.Width(95f)))
                CreateProfile(null);
            using (new EditorGUI.DisabledScope(SelectedIndex() < 0))
            {
                if (GUILayout.Button("Дублировать", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                    CreateProfile(database.FindEntry(selectedId));
                if (GUILayout.Button("Удалить…", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    DeleteSelected();
            }
            if (GUILayout.Button("+ Рабочий пример", EditorStyles.toolbarButton, GUILayout.Width(120f)))
            {
                Undo.RecordObject(database, "Рабочий пример поселений");
                List<string> added = SettlementDatabaseBootstrap.AddMissingPilot(database);
                EditorUtility.SetDirty(database);
                serialized.Update();
                ShowNotification(new GUIContent(added.Count > 0 ? "Добавлено: " + string.Join(", ", added) : "Пример уже в базе"));
            }
            if (GUILayout.Button("+ Сцены примера в Базу диалогов", EditorStyles.toolbarButton, GUILayout.Width(200f)))
            {
                List<string> added = SettlementDatabaseBootstrap.SeedDialogues(database);
                dialogues = AssetDatabase.LoadAssetAtPath<DialogueDatabaseAsset>(SettlementDatabaseBootstrap.DialogueDatabasePath);
                ShowNotification(new GUIContent(added.Count > 0 ? "В Базу диалогов: " + added.Count : "Сцены уже засеяны"));
            }
            if (GUILayout.Button("База предметов", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                ItemDatabaseWindow.OpenWindow();
            GUILayout.FlexibleSpace();
            List<string> problems = ValidateAll();
            GUILayout.Label(problems.Count == 0 ? "Ошибок нет" : "⚠ Проблем: " + problems.Count);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(260f));
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            for (int i = 0; i < database.profiles.Count; i++)
            {
                SettlementProfileEntry entry = database.profiles[i];
                if (entry?.Data == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(search) &&
                    (entry.Data.Name ?? string.Empty).IndexOf(search.Trim(), StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    (entry.Data.Id ?? string.Empty).IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                bool selected = !errandsPage && entry.Data.Id == selectedId;
                int problems = SettlementCatalogValidator.ValidateProfile(entry.Data, ValidatorContext()).Count;
                Rect row = GUILayoutUtility.GetRect(240f, 36f);
                if (selected)
                    EditorGUI.DrawRect(row, new Color(0.24f, 0.33f, 0.45f, 1f));
                GUI.Label(new Rect(row.x + 6f, row.y + 2f, row.width - 8f, 18f),
                    (problems > 0 ? "⚠ " : string.Empty) + (string.IsNullOrEmpty(entry.Data.Name) ? "(без названия)" : entry.Data.Name), EditorStyles.boldLabel);
                GUI.Label(new Rect(row.x + 6f, row.y + 18f, row.width - 8f, 16f), KindLabel(entry.Data.Kind) + " · " + entry.Data.Id, EditorStyles.miniLabel);
                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
                {
                    selectedId = entry.Data.Id;
                    errandsPage = false;
                    GUI.FocusControl(null);
                    Event.current.Use();
                }
            }
            EditorGUILayout.Space(8f);
            if (GUILayout.Toggle(errandsPage, "Поручения (общие для доски, корчмы и разговоров)", "Button"))
            {
                if (!errandsPage)
                    GUI.FocusControl(null);
                errandsPage = true;
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawProfile()
        {
            int index = SelectedIndex();
            if (index < 0)
            {
                EditorGUILayout.HelpBox("Выберите поселение слева или создайте новое.", MessageType.Info);
                return;
            }
            SettlementProfileEntry entry = database.profiles[index];
            SerializedProperty entryProperty = serialized.FindProperty("profiles").GetArrayElementAtIndex(index);
            SerializedProperty data = entryProperty.FindPropertyRelative("Data");
            SerializedProperty art = entryProperty.FindPropertyRelative("Art");

            EditorGUILayout.LabelField(entry.Data.Name + "  ·  " + KindLabel(entry.Data.Kind), EditorStyles.largeLabel);
            foreach (string problem in SettlementCatalogValidator.ValidateProfile(entry.Data, ValidatorContext()))
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
            tab = GUILayout.Toolbar(tab, Tabs);
            EditorGUILayout.Space(6f);
            switch (tab)
            {
                case 0: DrawBasics(data, entryProperty, entry); break;
                case 1: DrawImage(art, entry); break;
                case 2: DrawPeopleAndActions(data, entry); break;
                case 3: DrawMerchants(data); break;
                case 4: DrawTavern(data, art, entry); break;
                case 5: DrawBoard(data, art, entry); break;
                case 6: DrawVariants(data, art); break;
                case 7: DrawBinding(data, entry); break;
            }
        }

        // ------------------------------------------------------------------
        // Разделы профиля
        // ------------------------------------------------------------------

        private void DrawBasics(SerializedProperty data, SerializedProperty entryProperty, SettlementProfileEntry entry)
        {
            EditorGUILayout.PropertyField(data.FindPropertyRelative("Name"), new GUIContent("Название по умолчанию", "Название конкретного места карты приоритетнее."));
            EditorGUILayout.PropertyField(data.FindPropertyRelative("Kind"), new GUIContent("Тип"));
            TextArea(data.FindPropertyRelative("ShortDescription"), "Короткое описание (1–3 предложения на экране)", 3);
            TextArea(data.FindPropertyRelative("LongDescription"), "Подробнее (открывается по кнопке)", 3);
            EditorGUILayout.PropertyField(data.FindPropertyRelative("Tags"), new GUIContent("Авторские метки"), true);
            TextArea(entryProperty.FindPropertyRelative("AuthorNotes"), "Заметки автора (игроку не видны)", 2);
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("ID", entry.Data.Id);
            EditorGUILayout.LabelField("ID хранится в сохранениях и не меняется при переименовании.", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawImage(SerializedProperty art, SettlementProfileEntry entry)
        {
            EditorGUILayout.LabelField("Основное изображение экрана поселения", EditorStyles.boldLabel);
            DrawFraming(art.FindPropertyRelative("Screen"));
            EditorGUILayout.PropertyField(art.FindPropertyRelative("Accent"), new GUIContent("Цвет акцента", "Рамки и заголовки — в пределах общей темы."));
            EditorGUILayout.Space(6f);
            preview720 = GUILayout.Toolbar(preview720 ? 1 : 0, new[] { "1920×1080", "1280×720" }, GUILayout.Width(240f)) == 1;
            DrawScreenPreview(entry, entry.Art.Screen, entry.Data.ShortDescription);
        }

        private void DrawFraming(SerializedProperty framing)
        {
            SerializedProperty image = framing.FindPropertyRelative("Image");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(image, new GUIContent("Изображение (перетащите сюда)"));
            if (image.objectReferenceValue != null && GUILayout.Button("Очистить", GUILayout.Width(72f)))
                image.objectReferenceValue = null;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.PropertyField(framing.FindPropertyRelative("Fit"), new GUIContent("Вписывание", "Cover — заполнить кадр; Contain — вписать целиком."));
            SerializedProperty focus = framing.FindPropertyRelative("Focus");
            Vector2 f = focus.vector2Value;
            float x = EditorGUILayout.Slider(new GUIContent("Фокус по горизонтали"), f.x, 0f, 1f);
            float y = EditorGUILayout.Slider(new GUIContent("Фокус по вертикали"), f.y, 0f, 1f);
            if (!Mathf.Approximately(x, f.x) || !Mathf.Approximately(y, f.y))
                focus.vector2Value = new Vector2(x, y);
            EditorGUILayout.PropertyField(framing.FindPropertyRelative("Zoom"), new GUIContent("Масштаб (1–2)"));
            EditorGUILayout.PropertyField(framing.FindPropertyRelative("PanelDim"), new GUIContent("Затемнение под текстом"));
        }

        // Предпросмотр расположения экрана: фон с кадрированием, левая панель
        // ~31% ширины, строка названия, полоса людей, нижняя строка отряда.
        private void DrawScreenPreview(SettlementProfileEntry entry, SettlementImageFraming framing, string description)
        {
            float width = Mathf.Min(position.width - 300f, preview720 ? 640f : 820f);
            Rect frame = GUILayoutUtility.GetRect(width, width * 9f / 16f, GUILayout.Width(width), GUILayout.Height(width * 9f / 16f));
            SettlementPreview.DrawImage(frame, framing);
            Rect panel = new Rect(frame.x, frame.y, frame.width * 0.31f, frame.height);
            EditorGUI.DrawRect(panel, new Color(0f, 0f, 0f, framing != null ? framing.PanelDim : 0.55f));
            float scale = preview720 ? 0.85f : 1f;
            GUIStyle title = new GUIStyle(EditorStyles.boldLabel) { fontSize = Mathf.RoundToInt(16 * scale), normal = { textColor = entry.Art.Accent }, wordWrap = true };
            GUIStyle body = new GUIStyle(EditorStyles.label) { fontSize = Mathf.RoundToInt(11 * scale), normal = { textColor = new Color(0.92f, 0.88f, 0.8f) }, wordWrap = true };
            GUI.Label(new Rect(panel.x + 10f, panel.y + 10f, panel.width - 20f, 24f), entry.Data.Name, title);
            GUI.Label(new Rect(panel.x + 10f, panel.y + 38f, panel.width - 20f, 80f), description, body);
            float buttonY = panel.y + 124f;
            foreach (SettlementActionDefinition action in entry.Data.Actions)
            {
                if (action == null || buttonY > panel.yMax - 60f)
                    continue;
                GUI.Box(new Rect(panel.x + 10f, buttonY, panel.width - 20f, 20f), action.Label);
                buttonY += 24f;
            }
            Rect people = new Rect(panel.xMax + 10f, frame.yMax - 78f, frame.width - panel.width - 20f, 44f);
            float px = people.x;
            foreach (SettlementPersonDefinition person in entry.Data.People)
            {
                if (person == null)
                    continue;
                GUI.Box(new Rect(px, people.y, 90f, 44f), person.DisplayName + "\n" + person.Role);
                px += 96f;
            }
            EditorGUI.DrawRect(new Rect(frame.x, frame.yMax - 26f, frame.width, 26f), new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(frame.x + 10f, frame.yMax - 24f, frame.width - 20f, 22f), "С вами: Командир, … · В лагере: … · [Выйти]", body);
        }

        private void DrawPeopleAndActions(SerializedProperty data, SettlementProfileEntry entry)
        {
            EditorGUILayout.LabelField("Люди (полоса портретов; клик — разговор или торговля)", EditorStyles.boldLabel);
            DrawList(data.FindPropertyRelative("People"), (person, i) => DrawPerson(person, entry), "+ Человек", p =>
            {
                p.FindPropertyRelative("Id").stringValue = "person." + Guid.NewGuid().ToString("N").Substring(0, 6);
                p.FindPropertyRelative("DisplayName").stringValue = "Новый человек";
            });

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Действия на экране поселения", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Выход и лагерь добавляются сами. Кнопка с невыполненным «Условием показа» не видна (не выдаёт тайну); с невыполненным «Условием доступности» видна, но недоступна — игрок читает причину.", MessageType.None);
            DrawList(data.FindPropertyRelative("Actions"), (action, i) => DrawAction(action, entry), "+ Действие", a =>
            {
                a.FindPropertyRelative("Id").stringValue = "action." + Guid.NewGuid().ToString("N").Substring(0, 6);
                a.FindPropertyRelative("Label").stringValue = "Новое действие";
            });
        }

        private void DrawPerson(SerializedProperty person, SettlementProfileEntry entry)
        {
            EditorGUILayout.PropertyField(person.FindPropertyRelative("DisplayName"), new GUIContent("Имя"));
            EditorGUILayout.PropertyField(person.FindPropertyRelative("Role"), new GUIContent("Роль (видна игроку)"));
            EditorGUILayout.PropertyField(person.FindPropertyRelative("Activity"), new GUIContent("Чем занят"));
            IdPickerDropdown.Field("Говорящий (портрет)", person.FindPropertyRelative("SpeakerId"), "Говорящие Базы диалогов", SpeakerOptions, SpeakerExists);
            IdPickerDropdown.Field("Диалог по нажатию", person.FindPropertyRelative("DialogueId"), "Диалоги", DialogueOptions, DialogueExists, OpenDialogue);
            IdPickerDropdown.Field("Торгует (торговец)", person.FindPropertyRelative("MerchantId"), "Торговцы поселения", () => MerchantOptions(entry), id => entry.Data.FindMerchant(id) != null);
            EditorGUILayout.PropertyField(person.FindPropertyRelative("Conditions"), new GUIContent("Условия присутствия (пусто — всегда)"), true);
            EditorGUILayout.PropertyField(person.FindPropertyRelative("Id"), new GUIContent("ID"));
        }

        private void DrawAction(SerializedProperty action, SettlementProfileEntry entry)
        {
            SerializedProperty type = action.FindPropertyRelative("Type");
            EditorGUILayout.PropertyField(type, new GUIContent("Тип"));
            EditorGUILayout.PropertyField(action.FindPropertyRelative("Label"), new GUIContent("Подпись кнопки"));
            EditorGUILayout.PropertyField(action.FindPropertyRelative("Hint"), new GUIContent("Пояснение"));
            SerializedProperty target = action.FindPropertyRelative("TargetId");
            switch ((SettlementActionType)type.enumValueIndex)
            {
                case SettlementActionType.Dialogue:
                    IdPickerDropdown.Field("Диалог", target, "Диалоги", DialogueOptions, DialogueExists, OpenDialogue);
                    break;
                case SettlementActionType.Trade:
                    IdPickerDropdown.Field("Торговец", target, "Торговцы поселения", () => MerchantOptions(entry), id => entry.Data.FindMerchant(id) != null);
                    break;
                case SettlementActionType.LocalMap:
                    IdPickerDropdown.Field("Исследуемое место", target, "База локаций", LocalOptions, LocalExists);
                    EditorGUILayout.LabelField("Кнопка «Прогуляться» видна, только если место есть в Базе локаций.", EditorStyles.miniLabel);
                    break;
                default:
                    EditorGUILayout.LabelField("Цель — корчма/доска этого поселения.", EditorStyles.miniLabel);
                    break;
            }
            EditorGUILayout.PropertyField(action.FindPropertyRelative("ShowConditions"), new GUIContent("Условие показа"), true);
            EditorGUILayout.PropertyField(action.FindPropertyRelative("EnableConditions"), new GUIContent("Условие доступности"), true);
            EditorGUILayout.PropertyField(action.FindPropertyRelative("DisabledReason"), new GUIContent("Причина отказа (видна игроку)"));
            EditorGUILayout.PropertyField(action.FindPropertyRelative("Id"), new GUIContent("ID"));
        }

        private void DrawMerchants(SerializedProperty data)
        {
            EditorGUILayout.HelpBox("Цена покупки у торговца = базовая цена вещи × «процент покупки» (вверх); продажа ему = базовая × «процент продажи» (вниз). " +
                                    "Продажа должна быть дешевле покупки. Запас обновляется по игровым часам до целевого уровня, деньги — до «денег после обновления». " +
                                    "Уже начатые торговцы в сохранённых партиях не сбрасываются при правке.", MessageType.None);
            DrawList(data.FindPropertyRelative("Merchants"), (merchant, i) =>
            {
                EditorGUILayout.PropertyField(merchant.FindPropertyRelative("DisplayName"), new GUIContent("Имя"));
                EditorGUILayout.PropertyField(merchant.FindPropertyRelative("Role"), new GUIContent("Роль"));
                IdPickerDropdown.Field("Говорящий (портрет)", merchant.FindPropertyRelative("SpeakerId"), "Говорящие", SpeakerOptions, SpeakerExists);
                EditorGUILayout.PropertyField(merchant.FindPropertyRelative("BuyPercent"), new GUIContent("Процент покупки у него, %"));
                EditorGUILayout.PropertyField(merchant.FindPropertyRelative("SellPercent"), new GUIContent("Процент продажи ему, %"));
                EditorGUILayout.PropertyField(merchant.FindPropertyRelative("StartMoney"), new GUIContent("Деньги в начале, зол."));
                EditorGUILayout.PropertyField(merchant.FindPropertyRelative("MoneyTarget"), new GUIContent("Деньги после обновления, зол."));
                EditorGUILayout.PropertyField(merchant.FindPropertyRelative("RefreshHours"), new GUIContent("Обновление каждые, игровых часов"));
                EditorGUILayout.LabelField("Ассортимент", EditorStyles.boldLabel);
                DrawList(merchant.FindPropertyRelative("Stock"), (line, j) =>
                {
                    SerializedProperty supplies = line.FindPropertyRelative("IsSupplies");
                    EditorGUILayout.PropertyField(supplies, new GUIContent("Припасы отряда", "Абстрактные припасы похода, не вещь в сумке."));
                    if (supplies.boolValue)
                        EditorGUILayout.PropertyField(line.FindPropertyRelative("SupplyUnitPrice"), new GUIContent("Цена за единицу, зол."));
                    else
                    {
                        IdPickerDropdown.Field("Вещь", line.FindPropertyRelative("ItemId"), "База предметов", IdPickerDropdown.ItemOptions,
                            IdPickerDropdown.ItemExists, ItemDatabaseWindow.OpenAt);
                        EditorGUILayout.PropertyField(line.FindPropertyRelative("Unique"), new GUIContent("Не обновлять (одна на партию)"));
                        string itemId = line.FindPropertyRelative("ItemId").stringValue;
                        ItemDatabaseEntry item = AssetDatabase.LoadAssetAtPath<ItemDatabaseAsset>(ItemDatabaseAsset.AssetPath)?.Find(itemId);
                        if (item != null)
                        {
                            MerchantDefinition preview = new MerchantDefinition
                            {
                                BuyPercent = merchant.FindPropertyRelative("BuyPercent").intValue,
                                SellPercent = merchant.FindPropertyRelative("SellPercent").intValue
                            };
                            EditorGUILayout.LabelField("Цена у торговца: купить " + TradeService.BuyPrice(item.Data, preview) +
                                                       " · продать ему " + TradeService.SellPrice(item.Data, preview) + " зол. за штуку", EditorStyles.miniLabel);
                        }
                    }
                    EditorGUILayout.PropertyField(line.FindPropertyRelative("TargetCount"), new GUIContent("Целевой запас, шт."));
                }, "+ Строка ассортимента", l => l.FindPropertyRelative("TargetCount").intValue = 1);
                EditorGUILayout.PropertyField(merchant.FindPropertyRelative("Id"), new GUIContent("ID торговца"));
            }, "+ Торговец", m =>
            {
                m.FindPropertyRelative("Id").stringValue = "merchant." + Guid.NewGuid().ToString("N").Substring(0, 6);
                m.FindPropertyRelative("DisplayName").stringValue = "Торговец";
                m.FindPropertyRelative("BuyPercent").intValue = 100;
                m.FindPropertyRelative("SellPercent").intValue = 50;
                m.FindPropertyRelative("RefreshHours").doubleValue = 72;
            });
        }

        private void DrawTavern(SerializedProperty data, SerializedProperty art, SettlementProfileEntry entry)
        {
            SerializedProperty tavern = data.FindPropertyRelative("Tavern");
            EditorGUILayout.PropertyField(tavern.FindPropertyRelative("Enabled"), new GUIContent("Корчма есть"));
            if (!tavern.FindPropertyRelative("Enabled").boolValue)
                return;
            EditorGUILayout.PropertyField(tavern.FindPropertyRelative("Name"), new GUIContent("Название"));
            TextArea(tavern.FindPropertyRelative("Description"), "Обстановка (1–3 строки)", 3);
            EditorGUILayout.LabelField("Фон корчмы", EditorStyles.boldLabel);
            DrawFraming(art.FindPropertyRelative("Tavern"));
            EditorGUILayout.LabelField("Хозяин", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawPerson(tavern.FindPropertyRelative("Keeper"), entry);
            EditorGUILayout.EndVertical();
            EditorGUILayout.LabelField("Посетители", EditorStyles.boldLabel);
            DrawList(tavern.FindPropertyRelative("Visitors"), (visitor, i) => DrawPerson(visitor, entry), "+ Посетитель", v =>
            {
                v.FindPropertyRelative("Id").stringValue = "visitor." + Guid.NewGuid().ToString("N").Substring(0, 6);
                v.FindPropertyRelative("DisplayName").stringValue = "Посетитель";
            });
            EditorGUILayout.LabelField("Предложения людей корчмы (сведения, поручения)", EditorStyles.boldLabel);
            DrawList(tavern.FindPropertyRelative("Offers"), (offer, i) =>
            {
                EditorGUILayout.PropertyField(offer.FindPropertyRelative("Label"), new GUIContent("Подпись"));
                TextArea(offer.FindPropertyRelative("Description"), "Пояснение", 2);
                IdPickerDropdown.Field("Кто предлагает", offer.FindPropertyRelative("PersonId"), "Люди корчмы", () => PersonOptions(entry), id => entry.Data.FindPerson(id) != null);
                EditorGUILayout.PropertyField(offer.FindPropertyRelative("Price"), new GUIContent("Цена, зол. (0 — даром)"));
                EditorGUILayout.PropertyField(offer.FindPropertyRelative("Once"), new GUIContent("Однократно"));
                if (!offer.FindPropertyRelative("Once").boolValue)
                    EditorGUILayout.PropertyField(offer.FindPropertyRelative("CooldownHours"), new GUIContent("Повтор не раньше, ч"));
                EditorGUILayout.PropertyField(offer.FindPropertyRelative("KnowledgeId"), new GUIContent("Даёт знание (ID)"));
                TextArea(offer.FindPropertyRelative("KnowledgeText"), "Что узнаёт герой (Хроника)", 2);
                EditorGUILayout.PropertyField(offer.FindPropertyRelative("StageFlagId"), new GUIContent("Флаг этапа поиска"));
                EditorGUILayout.PropertyField(offer.FindPropertyRelative("SetFlagId"), new GUIContent("Ставит флаг"));
                IdPickerDropdown.Field("Принять поручение", offer.FindPropertyRelative("ErrandId"), "Поручения", ErrandOptions, ErrandExists);
                IdPickerDropdown.Field("Сцена рассказа", offer.FindPropertyRelative("DialogueId"), "Диалоги", DialogueOptions, DialogueExists, OpenDialogue);
                EditorGUILayout.PropertyField(offer.FindPropertyRelative("Conditions"), new GUIContent("Условия появления"), true);
                EditorGUILayout.PropertyField(offer.FindPropertyRelative("Id"), new GUIContent("ID"));
            }, "+ Предложение", o =>
            {
                o.FindPropertyRelative("Id").stringValue = "offer." + Guid.NewGuid().ToString("N").Substring(0, 6);
                o.FindPropertyRelative("Label").stringValue = "Новое предложение";
                o.FindPropertyRelative("Once").boolValue = true;
            });
            EditorGUILayout.LabelField("Ночлег", EditorStyles.boldLabel);
            SerializedProperty lodging = tavern.FindPropertyRelative("Lodging");
            EditorGUILayout.PropertyField(lodging.FindPropertyRelative("Enabled"), new GUIContent("Можно переночевать"));
            if (lodging.FindPropertyRelative("Enabled").boolValue)
            {
                EditorGUILayout.PropertyField(lodging.FindPropertyRelative("Price"), new GUIContent("Цена, зол."));
                EditorGUILayout.PropertyField(lodging.FindPropertyRelative("Hours"), new GUIContent("Длительность, игровых часов"));
                EditorGUILayout.PropertyField(lodging.FindPropertyRelative("HealHitPoints"), new GUIContent("Вернёт вошедшим до, HP", "Сверх обычного сна. Тяжёлую рану не лечит."));
                TextArea(lodging.FindPropertyRelative("Description"), "Описание ночлега", 2);
                EditorGUILayout.LabelField("Ночлег идёт общим временем и припасами похода; оставшиеся в лагере спят у костра без улучшенного отдыха.", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.Space(6f);
            preview720 = GUILayout.Toolbar(preview720 ? 1 : 0, new[] { "1920×1080", "1280×720" }, GUILayout.Width(240f)) == 1;
            DrawScreenPreview(entry, entry.Art.Tavern, entry.Data.Tavern.Description);
        }

        private void DrawBoard(SerializedProperty data, SerializedProperty art, SettlementProfileEntry entry)
        {
            SerializedProperty board = data.FindPropertyRelative("Board");
            EditorGUILayout.PropertyField(board.FindPropertyRelative("Enabled"), new GUIContent("Доска объявлений есть"));
            if (!board.FindPropertyRelative("Enabled").boolValue)
                return;
            EditorGUILayout.HelpBox("Письмо редкое: береста, дощечки, знаки, а то и грамотей читает вслух. Не делайте доску в каждой деревне. " +
                                    "Чтение объявления само ничего не принимает; игрок видит только подключённые действия.", MessageType.None);
            EditorGUILayout.PropertyField(board.FindPropertyRelative("Name"), new GUIContent("Название"));
            TextArea(board.FindPropertyRelative("Description"), "Как выглядит", 2);
            EditorGUILayout.PropertyField(board.FindPropertyRelative("ReaderNote"), new GUIContent("Кто читает неграмотным"));
            EditorGUILayout.LabelField("Изображение доски", EditorStyles.boldLabel);
            DrawFraming(art.FindPropertyRelative("Board"));
            EditorGUILayout.LabelField("Объявления", EditorStyles.boldLabel);
            DrawList(board.FindPropertyRelative("Notices"), (notice, i) =>
            {
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("Title"), new GUIContent("Заголовок"));
                TextArea(notice.FindPropertyRelative("Text"), "Текст", 3);
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("Type"), new GUIContent("Тип"));
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("Medium"), new GUIContent("Как записано"));
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("AuthorName"), new GUIContent("Автор объявления"));
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("ContactName"), new GUIContent("К кому обратиться"));
                IdPickerDropdown.Field("Заказчик здесь", notice.FindPropertyRelative("ContactPersonId"), "Люди поселения", () => PersonOptions(entry), id => entry.Data.FindPerson(id) != null);
                IdPickerDropdown.Field("Поручение", notice.FindPropertyRelative("ErrandId"), "Поручения", ErrandOptions, ErrandExists);
                IdPickerDropdown.Field("Диалог заказчика", notice.FindPropertyRelative("DialogueId"), "Диалоги", DialogueOptions, DialogueExists, OpenDialogue);
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("KnowledgeId"), new GUIContent("Знание (ID)"));
                TextArea(notice.FindPropertyRelative("KnowledgeText"), "Что отметить в Хронике", 2);
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("LocationId"), new GUIContent("Место на карте (ID)", "Кнопка «Показать на карте» — только если место уже известно."));
                IdPickerDropdown.Field("Торговец", notice.FindPropertyRelative("MerchantId"), "Торговцы поселения", () => MerchantOptions(entry), id => entry.Data.FindMerchant(id) != null);
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("RewardText"), new GUIContent("Награда (если известна)"));
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("LifetimeHours"), new GUIContent("Висит, игровых часов (0 — всегда)"));
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("RepostAfterHours"), new GUIContent("Вывесят снова через, ч (0 — однократно)"));
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("ShowConditions"), new GUIContent("Условия появления"), true);
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("HideConditions"), new GUIContent("Условия исчезновения"), true);
                EditorGUILayout.PropertyField(notice.FindPropertyRelative("Id"), new GUIContent("ID"));
            }, "+ Объявление", n =>
            {
                n.FindPropertyRelative("Id").stringValue = "notice." + Guid.NewGuid().ToString("N").Substring(0, 6);
                n.FindPropertyRelative("Title").stringValue = "Новое объявление";
            });
        }

        private void DrawVariants(SerializedProperty data, SerializedProperty art)
        {
            EditorGUILayout.HelpBox("Вариант описания (и изображения) по условиям: побеждает наибольший приоритет, при равенстве — порядок записи. Без совпадений — основное описание.", MessageType.None);
            DrawList(data.FindPropertyRelative("Variants"), (variant, i) =>
            {
                EditorGUILayout.PropertyField(variant.FindPropertyRelative("Priority"), new GUIContent("Приоритет"));
                TextArea(variant.FindPropertyRelative("Description"), "Описание", 3);
                EditorGUILayout.PropertyField(variant.FindPropertyRelative("Conditions"), new GUIContent("Условия"), true);
                SerializedProperty id = variant.FindPropertyRelative("Id");
                EditorGUILayout.PropertyField(id, new GUIContent("ID"));
                DrawVariantImage(art, id.stringValue);
            }, "+ Вариант", v => v.FindPropertyRelative("Id").stringValue = "variant." + Guid.NewGuid().ToString("N").Substring(0, 6));
        }

        private void DrawVariantImage(SerializedProperty art, string variantId)
        {
            SerializedProperty variants = art.FindPropertyRelative("Variants");
            for (int i = 0; i < variants.arraySize; i++)
            {
                SerializedProperty item = variants.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative("VariantId").stringValue == variantId)
                {
                    EditorGUILayout.PropertyField(item.FindPropertyRelative("Image"), new GUIContent("Изображение варианта"));
                    return;
                }
            }
            if (!string.IsNullOrEmpty(variantId) && GUILayout.Button("+ Своё изображение варианта", GUILayout.Width(220f)))
            {
                variants.InsertArrayElementAtIndex(variants.arraySize);
                SerializedProperty added = variants.GetArrayElementAtIndex(variants.arraySize - 1);
                added.FindPropertyRelative("VariantId").stringValue = variantId;
                added.FindPropertyRelative("Image").objectReferenceValue = null;
            }
        }

        private void DrawBinding(SerializedProperty data, SettlementProfileEntry entry)
        {
            EditorGUILayout.LabelField("Явные места (ID мест авторской карты или истории)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(data.FindPropertyRelative("BoundLocationIds"), new GUIContent("Места"), true);
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Деревни генератора свободной игры", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(data.FindPropertyRelative("AllowGenerated"), new GUIContent("Шаблон для деревень генератора"));
            EditorGUILayout.PropertyField(data.FindPropertyRelative("GenerationWeight"), new GUIContent("Вес выбора"));
            EditorGUILayout.PropertyField(data.FindPropertyRelative("GuaranteeInGenerated"), new GUIContent("Хотя бы одно в каждой партии", "Достаётся одной из ближайших к стоянке деревень."));
            EditorGUILayout.HelpBox("Выбор профиля деревни фиксируется в сохранении при начале партии. Правка базы меняет тексты и изображения уже начатых партий, " +
                                    "но не меняет выбранный профиль, кошельки, остатки и прочитанное. Новые шаблоны появятся в новых партиях.", MessageType.None);
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Где используется", EditorStyles.boldLabel);
            foreach (string usage in Usages(entry.Data))
                EditorGUILayout.LabelField("• " + usage, EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawErrands()
        {
            EditorGUILayout.LabelField("Поручения", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Поручение — одно задание с общим состоянием во флагах сюжета. Принять его можно на доске, в корчме или в диалоге " +
                                    "(эффект «Поставить флаг» с тем же флагом принятия) — состояние одно. Шаги и награды делают диалоги; «Дело» в журнале строится отсюда. " +
                                    "Срок выполнения игрок видит до принятия; исчезновение объявления принятое поручение не отменяет.", MessageType.None);
            foreach (string problem in ValidateAll())
            {
                if (problem.StartsWith("Поручение", StringComparison.Ordinal))
                    EditorGUILayout.HelpBox(problem, MessageType.Warning);
            }
            DrawList(serialized.FindProperty("errands"), (errand, i) =>
            {
                EditorGUILayout.PropertyField(errand.FindPropertyRelative("Title"), new GUIContent("Название"));
                TextArea(errand.FindPropertyRelative("Summary"), "Суть", 2);
                EditorGUILayout.PropertyField(errand.FindPropertyRelative("GiverName"), new GUIContent("Заказчик"));
                EditorGUILayout.PropertyField(errand.FindPropertyRelative("AcceptedFlag"), new GUIContent("Флаг принятия"));
                EditorGUILayout.PropertyField(errand.FindPropertyRelative("CompletedFlag"), new GUIContent("Флаг выполнения"));
                EditorGUILayout.PropertyField(errand.FindPropertyRelative("FailedFlag"), new GUIContent("Флаг провала (необязательно)"));
                TextArea(errand.FindPropertyRelative("AcceptedStepText"), "Шаг после принятия", 2);
                EditorGUILayout.PropertyField(errand.FindPropertyRelative("Stages"), new GUIContent("Этапы (флаг → текст «Дела»)"), true);
                TextArea(errand.FindPropertyRelative("CompletedText"), "Итог при выполнении", 2);
                EditorGUILayout.PropertyField(errand.FindPropertyRelative("CompletionLimitHours"), new GUIContent("Срок выполнения, игровых часов (0 — без срока)"));
                EditorGUILayout.PropertyField(errand.FindPropertyRelative("RewardText"), new GUIContent("Награда (текст)"));
                EditorGUILayout.PropertyField(errand.FindPropertyRelative("Id"), new GUIContent("ID"));
            }, "+ Поручение", e =>
            {
                string id = "errand." + Guid.NewGuid().ToString("N").Substring(0, 6);
                e.FindPropertyRelative("Id").stringValue = id;
                e.FindPropertyRelative("Title").stringValue = "Новое поручение";
                e.FindPropertyRelative("AcceptedFlag").stringValue = id + ".accepted";
                e.FindPropertyRelative("CompletedFlag").stringValue = id + ".done";
            });
        }

        // ------------------------------------------------------------------
        // Помощники
        // ------------------------------------------------------------------

        private static void DrawList(SerializedProperty list, Action<SerializedProperty, int> drawItem, string addLabel, Action<SerializedProperty> init)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty item = list.GetArrayElementAtIndex(i);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                item.isExpanded = EditorGUILayout.Foldout(item.isExpanded, ItemTitle(item, i), true);
                using (new EditorGUI.DisabledScope(i == 0))
                {
                    if (GUILayout.Button("↑", GUILayout.Width(22f)))
                        list.MoveArrayElement(i, i - 1);
                }
                using (new EditorGUI.DisabledScope(i == list.arraySize - 1))
                {
                    if (GUILayout.Button("↓", GUILayout.Width(22f)))
                        list.MoveArrayElement(i, i + 1);
                }
                bool remove = GUILayout.Button("×", GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();
                if (remove)
                {
                    list.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndVertical();
                    break;
                }
                if (item.isExpanded)
                    drawItem(item, i);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button(addLabel, GUILayout.Width(200f)))
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                SerializedProperty added = list.GetArrayElementAtIndex(list.arraySize - 1);
                ResetStrings(added);
                init?.Invoke(added);
                added.isExpanded = true;
            }
        }

        // Новый элемент копирует последний — очищаем ID и подписи, чтобы не
        // получить дубль ID.
        private static void ResetStrings(SerializedProperty item)
        {
            SerializedProperty copy = item.Copy();
            SerializedProperty end = item.GetEndProperty();
            bool enter = true;
            while (copy.NextVisible(enter) && !SerializedProperty.EqualContents(copy, end))
            {
                enter = copy.propertyType == SerializedPropertyType.Generic;
                if (copy.propertyType == SerializedPropertyType.String)
                    copy.stringValue = string.Empty;
                else if (copy.isArray && copy.propertyType == SerializedPropertyType.Generic)
                {
                    copy.ClearArray();
                    enter = false;
                }
            }
        }

        private static string ItemTitle(SerializedProperty item, int index)
        {
            foreach (string field in new[] { "Title", "Label", "DisplayName", "Description", "ItemId", "Id" })
            {
                SerializedProperty property = item.FindPropertyRelative(field);
                if (property != null && property.propertyType == SerializedPropertyType.String && !string.IsNullOrEmpty(property.stringValue))
                {
                    string text = property.stringValue;
                    return (index + 1) + ". " + (text.Length > 60 ? text.Substring(0, 60) + "…" : text);
                }
            }
            SerializedProperty supplies = item.FindPropertyRelative("IsSupplies");
            if (supplies != null && supplies.boolValue)
                return (index + 1) + ". Припасы отряда";
            return (index + 1) + ".";
        }

        private static void TextArea(SerializedProperty property, string label, int lines)
        {
            if (property == null)
                return;
            EditorGUILayout.LabelField(label);
            string current = property.stringValue ?? string.Empty;
            string next = EditorGUILayout.TextArea(current, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.MinHeight(16f * lines));
            if (next != current)
                property.stringValue = next;
        }

        private int SelectedIndex()
        {
            if (database?.profiles == null || string.IsNullOrEmpty(selectedId))
                return -1;
            return database.profiles.FindIndex(p => p?.Data != null && p.Data.Id == selectedId);
        }

        private void CreateProfile(SettlementProfileEntry source)
        {
            Undo.RecordObject(database, source != null ? "Дублировать поселение" : "Новое поселение");
            string baseId = source != null ? source.Data.Id + "_copy" : "settlement.new";
            string id = baseId;
            for (int n = 2; database.FindEntry(id) != null; n++)
                id = baseId + "_" + n;
            SettlementProfileEntry entry = source != null
                ? JsonUtility.FromJson<SettlementProfileEntry>(JsonUtility.ToJson(source))
                : new SettlementProfileEntry { Data = new SettlementProfileDefinition { Name = "Новое поселение" } };
            entry.Data.Id = id;
            if (source != null)
            {
                entry.Data.Name = source.Data.Name + " (копия)";
                entry.Art = source.Art; // ссылки на изображения копируются ниже через сериализацию
                entry.Data.BoundLocationIds = new List<string>();
            }
            database.profiles.Add(entry);
            EditorUtility.SetDirty(database);
            serialized.Update();
            if (source != null)
            {
                // Изображения: JsonUtility не переносит ссылки на ассеты — копируем явно.
                SerializedProperty from = serialized.FindProperty("profiles").GetArrayElementAtIndex(database.profiles.IndexOf(source)).FindPropertyRelative("Art");
                SerializedProperty to = serialized.FindProperty("profiles").GetArrayElementAtIndex(database.profiles.Count - 1).FindPropertyRelative("Art");
                CopyArt(from, to);
                serialized.ApplyModifiedProperties();
            }
            selectedId = id;
            errandsPage = false;
        }

        private static void CopyArt(SerializedProperty from, SerializedProperty to)
        {
            foreach (string framing in new[] { "Screen", "Tavern", "Board" })
                to.FindPropertyRelative(framing).FindPropertyRelative("Image").objectReferenceValue =
                    from.FindPropertyRelative(framing).FindPropertyRelative("Image").objectReferenceValue;
        }

        private void DeleteSelected()
        {
            int index = SelectedIndex();
            if (index < 0)
                return;
            SettlementProfileEntry entry = database.profiles[index];
            List<string> usages = Usages(entry.Data);
            string message = "Удалить «" + entry.Data.Name + "»?" + (usages.Count > 0 ? "\n\nИспользуется:\n" + string.Join("\n", usages) : string.Empty) +
                             "\n\nСохранённые партии, где поселение уже выбрано, сохранят его состояние, но покажут поселение без услуг.";
            if (!EditorUtility.DisplayDialog("Удалить поселение", message, "Удалить", "Отмена"))
                return;
            Undo.RecordObject(database, "Удалить поселение");
            database.profiles.RemoveAt(index);
            EditorUtility.SetDirty(database);
            serialized.Update();
            selectedId = string.Empty;
        }

        private List<string> Usages(SettlementProfileDefinition profile)
        {
            List<string> result = new List<string>();
            if (profile.BoundLocationIds != null)
            {
                foreach (string locationId in profile.BoundLocationIds)
                {
                    if (!string.IsNullOrWhiteSpace(locationId))
                        result.Add("место карты «" + locationId + "»");
                }
            }
            if (profile.AllowGenerated)
                result.Add("шаблон деревень генератора (вес " + profile.GenerationWeight + (profile.GuaranteeInGenerated ? ", в каждой партии" : string.Empty) + ")");
            if (profile.Id == SettlementPilotContent.TradingPostId || profile.Kind == SettlementKind.TradingPost)
                result.Add("история «Тайник у старого русла» ищет торговое село (FreePlayCacheStory)");
            if (result.Count == 0)
                result.Add("не используется: нет привязки к местам и не шаблон генератора");
            return result;
        }

        private List<string> ValidateAll()
        {
            SettlementCatalogData catalog = database.ToCatalog();
            List<SettlementProfileDefinition> profiles = new List<SettlementProfileDefinition>(catalog.Profiles);
            return SettlementCatalogValidator.ValidateAll(profiles, database.errands, ValidatorContext());
        }

        private SettlementCatalogValidator.Context ValidatorContext()
        {
            return new SettlementCatalogValidator.Context
            {
                DialogueExists = DialogueExists,
                SpeakerExists = SpeakerExists,
                LocalLocationExists = LocalExists,
                ItemExists = IdPickerDropdown.ItemExists,
                FindErrand = id => database.errands?.Find(e => e != null && e.Id == id)
            };
        }

        private bool DialogueExists(string id) => dialogues != null && dialogues.FindDialogue(id) != null;
        private bool SpeakerExists(string id) => dialogues != null && dialogues.FindSpeaker(id) != null;
        private bool ErrandExists(string id) => database.errands != null && database.errands.Exists(e => e != null && e.Id == id);

        private static bool LocalExists(string id)
        {
            LocalLocationDatabaseAsset local = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            return local?.locations != null && local.locations.Exists(l => l != null && l.Id == id);
        }

        private List<(string Id, string Label)> DialogueOptions()
        {
            List<(string, string)> result = new List<(string, string)>();
            if (dialogues == null)
                return result;
            foreach (DialogueDefinitionData dialogue in dialogues.Dialogues)
            {
                if (dialogue != null && !string.IsNullOrEmpty(dialogue.Id))
                    result.Add((dialogue.Id, dialogue.Title));
            }
            result.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.CurrentCulture));
            return result;
        }

        private List<(string Id, string Label)> SpeakerOptions()
        {
            List<(string, string)> result = new List<(string, string)>();
            if (dialogues == null)
                return result;
            foreach (DialogueSpeakerData speaker in dialogues.Speakers)
            {
                if (speaker != null && !string.IsNullOrEmpty(speaker.Id))
                    result.Add((speaker.Id, speaker.DisplayName + (string.IsNullOrEmpty(speaker.Role) ? string.Empty : " — " + speaker.Role)));
            }
            return result;
        }

        private static List<(string Id, string Label)> MerchantOptions(SettlementProfileEntry entry)
        {
            List<(string, string)> result = new List<(string, string)>();
            foreach (MerchantDefinition merchant in entry.Data.Merchants)
            {
                if (merchant != null && !string.IsNullOrEmpty(merchant.Id))
                    result.Add((merchant.Id, merchant.DisplayName));
            }
            return result;
        }

        private static List<(string Id, string Label)> PersonOptions(SettlementProfileEntry entry)
        {
            List<(string, string)> result = new List<(string, string)>();
            void Add(SettlementPersonDefinition person)
            {
                if (person != null && !string.IsNullOrEmpty(person.Id))
                    result.Add((person.Id, person.DisplayName + " — " + person.Role));
            }
            foreach (SettlementPersonDefinition person in entry.Data.People)
                Add(person);
            if (entry.Data.Tavern != null)
            {
                Add(entry.Data.Tavern.Keeper);
                foreach (SettlementPersonDefinition visitor in entry.Data.Tavern.Visitors)
                    Add(visitor);
            }
            return result;
        }

        private List<(string Id, string Label)> ErrandOptions()
        {
            List<(string, string)> result = new List<(string, string)>();
            foreach (ErrandDefinition errand in database.errands)
            {
                if (errand != null && !string.IsNullOrEmpty(errand.Id))
                    result.Add((errand.Id, errand.Title));
            }
            return result;
        }

        private static List<(string Id, string Label)> LocalOptions()
        {
            List<(string, string)> result = new List<(string, string)>();
            LocalLocationDatabaseAsset local = AssetDatabase.LoadAssetAtPath<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.AssetPath);
            if (local?.locations == null)
                return result;
            foreach (LocalLocationDefinition location in local.locations)
            {
                if (location != null && !string.IsNullOrEmpty(location.Id))
                    result.Add((location.Id, location.DisplayName));
            }
            return result;
        }

        private static void OpenDialogue(string dialogueId)
        {
            EditorApplication.ExecuteMenuItem("Kingdom Survival/База диалогов");
            EditorGUIUtility.systemCopyBuffer = dialogueId;
        }

        public static string KindLabel(SettlementKind kind)
        {
            switch (kind)
            {
                case SettlementKind.Village: return "деревня";
                case SettlementKind.TradingPost: return "торговое поселение";
                default: return "укреплённый городок";
            }
        }
    }

    // Рисование кадра с тем же кадрированием, что в игре (Cover/Contain,
    // фокус, масштаб) — один расчёт для окна и экрана поселения.
    public static class SettlementPreview
    {
        public static void DrawImage(Rect frame, SettlementImageFraming framing)
        {
            EditorGUI.DrawRect(frame, new Color(0.17f, 0.15f, 0.13f, 1f));
            Sprite sprite = framing?.Image;
            if (sprite == null || sprite.texture == null)
            {
                GUI.Label(frame, "Изображение не назначено — в игре нейтральный фон", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true });
                return;
            }
            Rect tr = sprite.textureRect;
            Rect uv = SettlementImageMath.VisibleUv(tr.width, tr.height, frame.width, frame.height, framing.Fit == SettlementImageFit.Cover,
                framing.Focus, framing.Zoom, out Rect drawRectNormalized);
            Texture2D texture = sprite.texture;
            Rect textureUv = SettlementImageMath.TextureUv(sprite, uv);
            Rect draw = new Rect(frame.x + drawRectNormalized.x * frame.width, frame.y + drawRectNormalized.y * frame.height,
                drawRectNormalized.width * frame.width, drawRectNormalized.height * frame.height);
            GUI.DrawTextureWithTexCoords(draw, texture, textureUv, true);
        }
    }
}
