using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KingdomSurvival.BattleSandbox;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.UnitDatabase.Editor
{
    // Бриф завершения, этап 14: каталог всего, что умеет бой, прямо в карточке
    // существа. Теги — весь банк (457: активные, корзина, архив) и факты
    // движка; назначаются кнопкой без ручного ввода ID. Способности — все виды
    // боевого движка с параметрами по умолчанию. Состояния — все 89 с
    // значком и описанием. Корзина и архив только для чтения.
    public sealed partial class UnitDatabaseWindow
    {
        private const string TagBankPath = "ProjectDocs/CombatStatusPack/Data/passive_tags_effective.json";

        [SerializeField] private int catalogTab;
        [SerializeField] private string catalogSearch = string.Empty;
        [SerializeField] private bool catalogFavoritesOnly;
        [SerializeField] private bool catalogShowTrash;
        [SerializeField] private bool catalogExecutableOnly;
        [SerializeField] private bool catalogAssignedOnly;
        [SerializeField] private string catalogCategory = string.Empty;

        private static List<CatalogTag> catalogTags;
        private static Dictionary<string, string> catalogCategoryLabels;
        private ListView catalogList;
        private Label catalogCount;
        private readonly List<object> catalogRows = new List<object>();

        [Serializable]
        private sealed class BankDetails
        {
            public string effect;
            public string activation;
        }

        [Serializable]
        private sealed class BankRecord
        {
            public string id;
            public string name;
            public string effect;
            public string[] categories;
            public string battleCategory;
            public string passiveType;
            public bool favorite;
            public bool deleted;
            public bool archived;
            public string[] statusIds;
            public BankDetails details;
        }

        [Serializable]
        private sealed class Bank
        {
            public BankRecord[] records;
        }

        private sealed class CatalogTag
        {
            public string Id;
            public string Name;
            public string Effect;
            public string Activation;
            public string Category;
            public string PassiveType;
            public bool Favorite;
            public bool Trash;
            public bool Archived;
            public bool EngineOnly;
            public string[] StatusIds = Array.Empty<string>();
            public bool Executable;
            public string Haystack;
            public bool ReadOnly => Trash || Archived;
        }

        private sealed class CatalogAbility
        {
            public SandboxAbilityKind Kind;
            public SandboxAbilityDefinition Defaults;
            public bool Assignable;
            public string Haystack;
        }

        // ---------- Данные ----------

        private static string Normalize(string text) =>
            (text ?? string.Empty).ToLowerInvariant().Replace('ё', 'е');

        private static List<CatalogTag> LoadCatalogTags()
        {
            if (catalogTags != null)
                return catalogTags;
            catalogTags = new List<CatalogTag>();
            catalogCategoryLabels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "attack", "Атака и урон" }, { "defense", "Защита и покров" }, { "reaction", "Ответы и перехват" },
                { "movement", "Движение в бою" }, { "control", "Контроль и ослабление" }, { "healing", "Живучесть и лечение" },
                { "support", "Ауры и поддержка" }, { "initiative", "Инициатива и темп" }, { "morale", "Страх и боевой дух" },
                { "environment", "Условия поля боя" }, { "cleanse", "Снятие состояний" }, { "death", "Гибель и возвращение" },
                { "weakness", "Недостатки и уязвимости" }, { "saved", "Сохранено вне боя" }, { "status", "Взаимодействие с состояниями" },
                { "engine", "Факты и правила движка" }
            };
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, TagBankPath);
            if (File.Exists(path))
            {
                Bank bank = JsonUtility.FromJson<Bank>(File.ReadAllText(path));
                foreach (BankRecord record in bank?.records ?? Array.Empty<BankRecord>())
                {
                    if (record == null || string.IsNullOrEmpty(record.id) || !seen.Add(record.id))
                        continue;
                    catalogTags.Add(new CatalogTag
                    {
                        Id = record.id,
                        Name = record.name,
                        Effect = !string.IsNullOrEmpty(record.effect) ? record.effect : record.details?.effect,
                        Activation = record.details?.activation ?? string.Empty,
                        Category = !string.IsNullOrEmpty(record.battleCategory) ? record.battleCategory : record.categories?.FirstOrDefault() ?? string.Empty,
                        PassiveType = record.passiveType ?? string.Empty,
                        Favorite = record.favorite,
                        Trash = record.deleted,
                        Archived = record.archived,
                        StatusIds = record.statusIds ?? Array.Empty<string>(),
                        Executable = SandboxPassiveCatalog.Get(record.id) != null
                    });
                }
            }
            // Теги движка вне банка: классификаторы-факты и прежние ID.
            foreach (SandboxPassiveDefinition def in SandboxPassiveCatalog.All)
            {
                if (!seen.Add(def.TagId))
                    continue;
                catalogTags.Add(new CatalogTag
                {
                    Id = def.TagId,
                    Name = def.Name,
                    Effect = string.IsNullOrEmpty(def.Summary)
                        ? "Правило движка: " + string.Join(", ", def.Rules.Select(rule => rule.Kind.ToString()).Distinct())
                        : def.Summary,
                    Category = "engine",
                    EngineOnly = true,
                    Executable = true
                });
            }
            foreach ((string Id, string Name, string Description) fact in SandboxCapabilityFacts.Classifiers)
            {
                if (!seen.Add(fact.Id))
                    continue;
                catalogTags.Add(new CatalogTag
                {
                    Id = fact.Id, Name = fact.Name, Effect = fact.Description, Category = "engine", EngineOnly = true, Executable = true
                });
            }
            foreach (CatalogTag tag in catalogTags)
            {
                string statuses = string.Join(" ", tag.StatusIds.Select(id => id + " " + (SandboxStatusCatalog.Get(id)?.Name ?? string.Empty)));
                tag.Haystack = Normalize(tag.Name + " " + tag.Id + " " + tag.Effect + " " + tag.Activation + " " + statuses);
            }
            return catalogTags;
        }

        private static List<CatalogAbility> LoadCatalogAbilities()
        {
            List<CatalogAbility> result = new List<CatalogAbility>();
            foreach (SandboxAbilityKind kind in Enum.GetValues(typeof(SandboxAbilityKind)))
            {
                if (kind == SandboxAbilityKind.None)
                    continue;
                SandboxAbilityDefinition defaults = SandboxAbilityDefaults.Create(kind, kind.ToString().ToLowerInvariant());
                string title = string.IsNullOrEmpty(defaults.Title) ? kind.ToString() : defaults.Title;
                result.Add(new CatalogAbility
                {
                    Kind = kind,
                    Defaults = defaults,
                    Assignable = Enum.TryParse(kind.ToString(), out UnitAbilityKind _),
                    Haystack = Normalize(title + " " + kind + " " + SandboxAbilityDefaults.Explain(defaults))
                });
            }
            return result;
        }

        // ---------- Вид ----------

        private VisualElement BuildCatalogCard(SerializedProperty unit, UnitDefinitionData data)
        {
            VisualElement card = Card("КАТАЛОГ БОЯ: ТЕГИ · СПОСОБНОСТИ · СОСТОЯНИЯ", 980f);
            card.style.flexGrow = 1f;
            card.style.maxWidth = 1320f;

            VisualElement tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginBottom = 6f;
            string[] titles = { "Теги", "Способности", "Состояния", "Боевые факты" };
            for (int i = 0; i < titles.Length; i++)
            {
                int captured = i;
                Button tab = new Button(() =>
                {
                    catalogTab = captured;
                    ShowSelectedUnit();
                }) { text = titles[i] };
                if (i == catalogTab)
                {
                    tab.style.backgroundColor = new Color(0.88f, 0.71f, 0.38f, 1f);
                    tab.style.color = new Color(0.1f, 0.08f, 0.05f, 1f);
                    tab.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
                tabs.Add(tab);
            }
            card.Add(tabs);

            switch (catalogTab)
            {
                case 1: BuildAbilityCatalog(card, data); break;
                case 2: BuildStatusCatalog(card); break;
                case 3: BuildCombatFacts(card, unit); break;
                default: BuildTagCatalog(card, data); break;
            }
            return card;
        }

        private void BuildTagCatalog(VisualElement card, UnitDefinitionData data)
        {
            List<CatalogTag> tags = LoadCatalogTags();
            card.Add(Hint("Весь банк тегов: активные назначаются кнопкой, корзина и архив видны только для чтения. " +
                          "Поиск — по названию, ID, эффекту и состояниям (ё = е). ✓ — у тега есть исполнитель в бою."));

            VisualElement filters = new VisualElement();
            filters.style.flexDirection = FlexDirection.Row;
            filters.style.flexWrap = Wrap.Wrap;
            filters.style.alignItems = Align.Center;
            filters.style.marginTop = 4f;
            filters.style.marginBottom = 4f;

            TextField search = new TextField { value = catalogSearch ?? string.Empty };
            search.style.width = 260f;
            search.tooltip = "Название, ID, эффект, состояние";
            search.RegisterValueChangedCallback(evt =>
            {
                catalogSearch = evt.newValue;
                RefreshTagCatalog(data);
            });
            filters.Add(new Label("Поиск") { style = { marginRight = 4f } });
            filters.Add(search);

            filters.Add(FilterToggle("★ избранные", catalogFavoritesOnly, value => { catalogFavoritesOnly = value; RefreshTagCatalog(data); }));
            filters.Add(FilterToggle("корзина и архив", catalogShowTrash, value => { catalogShowTrash = value; RefreshTagCatalog(data); }));
            filters.Add(FilterToggle("исполняются", catalogExecutableOnly, value => { catalogExecutableOnly = value; RefreshTagCatalog(data); }));
            filters.Add(FilterToggle("назначены бойцу", catalogAssignedOnly, value => { catalogAssignedOnly = value; RefreshTagCatalog(data); }));

            List<string> categories = new List<string> { string.Empty };
            categories.AddRange(tags.Select(tag => tag.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(CategoryLabel));
            PopupField<string> category = new PopupField<string>(categories, Math.Max(0, categories.IndexOf(catalogCategory ?? string.Empty)),
                c => string.IsNullOrEmpty(c) ? "Все категории" : CategoryLabel(c), c => string.IsNullOrEmpty(c) ? "Все категории" : CategoryLabel(c));
            category.style.width = 230f;
            category.RegisterValueChangedCallback(evt =>
            {
                catalogCategory = evt.newValue;
                RefreshTagCatalog(data);
            });
            filters.Add(category);
            card.Add(filters);

            catalogCount = new Label();
            catalogCount.style.color = HintColor;
            catalogCount.style.marginBottom = 4f;
            card.Add(catalogCount);

            catalogList = new ListView
            {
                itemsSource = catalogRows,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                selectionType = SelectionType.None,
                makeItem = MakeTagRow,
                bindItem = (element, index) => BindTagRow(element, (CatalogTag)catalogRows[index], data)
            };
            catalogList.style.height = 460f;
            card.Add(catalogList);
            RefreshTagCatalog(data);
        }

        private static string CategoryLabel(string key) =>
            catalogCategoryLabels != null && catalogCategoryLabels.TryGetValue(key ?? string.Empty, out string label) ? label : key;

        private static Toggle FilterToggle(string label, bool value, Action<bool> changed)
        {
            Toggle toggle = new Toggle(label) { value = value };
            toggle.style.marginLeft = 10f;
            toggle.labelElement.style.minWidth = 0f;
            toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
            return toggle;
        }

        private void RefreshTagCatalog(UnitDefinitionData data)
        {
            if (catalogList == null)
                return;
            List<CatalogTag> tags = LoadCatalogTags();
            string[] words = Normalize(catalogSearch).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            catalogRows.Clear();
            foreach (CatalogTag tag in tags)
            {
                if (tag.ReadOnly && !catalogShowTrash) continue;
                if (catalogFavoritesOnly && !tag.Favorite) continue;
                if (catalogExecutableOnly && !tag.Executable) continue;
                if (catalogAssignedOnly && (data == null || !data.HasTag(tag.Id))) continue;
                if (!string.IsNullOrEmpty(catalogCategory) && tag.Category != catalogCategory) continue;
                if (words.Any(word => !tag.Haystack.Contains(word))) continue;
                catalogRows.Add(tag);
            }
            int active = tags.Count(tag => !tag.ReadOnly && !tag.EngineOnly);
            int executable = tags.Count(tag => !tag.ReadOnly && !tag.EngineOnly && tag.Executable);
            catalogCount.text = "Показано " + catalogRows.Count + " из " + tags.Count + " · активных в банке " + active + ", исполняются " + executable +
                                " · корзина " + tags.Count(tag => tag.Trash) + ", архив " + tags.Count(tag => tag.Archived) +
                                " · факты движка " + tags.Count(tag => tag.EngineOnly) +
                                (data != null ? " · у бойца " + data.TagIds.Count : string.Empty);
            catalogList.Rebuild();
        }

        private static VisualElement MakeTagRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.paddingTop = 4f;
            row.style.paddingBottom = 4f;
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = CardBorder;

            Button action = new Button { name = "action" };
            action.style.width = 92f;
            action.style.flexShrink = 0f;
            row.Add(action);

            VisualElement body = new VisualElement { name = "body" };
            body.style.flexGrow = 1f;
            body.style.flexShrink = 1f;
            body.style.marginLeft = 6f;
            Label title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.whiteSpace = WhiteSpace.Normal;
            body.Add(title);
            Label effect = new Label { name = "effect" };
            effect.style.whiteSpace = WhiteSpace.Normal;
            body.Add(effect);
            Label meta = new Label { name = "meta" };
            meta.style.fontSize = 10f;
            meta.style.color = HintColor;
            meta.style.whiteSpace = WhiteSpace.Normal;
            body.Add(meta);
            row.Add(body);
            return row;
        }

        private void BindTagRow(VisualElement row, CatalogTag tag, UnitDefinitionData data)
        {
            bool assigned = data != null && data.HasTag(tag.Id);
            Button action = row.Q<Button>("action");
            action.clickable = null;
            action.SetEnabled(!tag.ReadOnly && data != null);
            action.text = tag.ReadOnly ? (tag.Trash ? "корзина" : "архив") : assigned ? "− снять" : "+ назначить";
            action.style.backgroundColor = assigned ? new Color(0.45f, 0.24f, 0.2f, 1f) : StyleKeyword.Null;
            action.clickable = new Clickable(() => SetUnitTag(tag, !assigned));

            string flags = (tag.Favorite ? "★ " : string.Empty) + (tag.Executable ? "✓ " : "— ");
            row.Q<Label>("title").text = flags + tag.Name + (assigned ? "   [назначен]" : string.Empty);
            row.Q<Label>("title").style.color = tag.ReadOnly ? HintColor : assigned ? HeaderColor : Color.white;
            row.Q<Label>("effect").text = tag.Effect ?? string.Empty;
            string statuses = tag.StatusIds.Length == 0
                ? string.Empty
                : " · состояния: " + string.Join(", ", tag.StatusIds.Select(id => SandboxStatusCatalog.Get(id)?.Name ?? id));
            row.Q<Label>("meta").text = tag.Id + " · " + CategoryLabel(tag.Category) +
                                        (string.IsNullOrEmpty(tag.PassiveType) ? string.Empty : " · " + tag.PassiveType) +
                                        (tag.Executable ? " · исполняется" : " · нет исполнителя") +
                                        (tag.EngineOnly ? " · движок" : string.Empty) + statuses;
        }

        private void SetUnitTag(CatalogTag tag, bool enabled)
        {
            if (selectedUnitIndex < 0 || selectedUnitIndex >= unitsProperty.arraySize)
                return;
            string tagId = tag.Id;
            // Справочник базы: назначенный из каталога тег регистрируется, чтобы
            // проверка базы и чипы карточки его знали.
            if (enabled && database.FindTag(tagId) == null)
            {
                Undo.RecordObject(database, "Тег из каталога");
                database.AddTagIfMissing(UnitTagDefinition.Create(tagId, tag.Name, CategoryLabel(tag.Category),
                    new Color(0.42f, 0.36f, 0.52f, 1f), tag.Effect ?? string.Empty));
                EditorUtility.SetDirty(database);
            }
            serializedDatabase.Update();
            SerializedProperty currentTags = unitsProperty.GetArrayElementAtIndex(selectedUnitIndex).FindPropertyRelative("tagIds");
            SetSerializedListValue(currentTags, tagId, enabled);
            serializedDatabase.ApplyModifiedProperties();
            EditorUtility.SetDirty(database);
            RefreshUnitList();
            RefreshTagCatalog(database.Units[selectedUnitIndex]);
        }

        private void BuildAbilityCatalog(VisualElement card, UnitDefinitionData data)
        {
            card.Add(Hint("Все виды способностей боевого движка с параметрами по умолчанию. «+ добавить» кладёт способность бойцу " +
                          "статусом «Активна»; числа правятся в карточке «Способности»."));
            ScrollView scroll = new ScrollView();
            scroll.style.maxHeight = 520f;
            foreach (CatalogAbility ability in LoadCatalogAbilities())
            {
                int owned = data?.Abilities.Count(item => item != null && item.Kind.ToString() == ability.Kind.ToString()) ?? 0;
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.paddingTop = 4f;
                row.style.paddingBottom = 4f;
                row.style.borderBottomWidth = 1f;
                row.style.borderBottomColor = CardBorder;
                CatalogAbility captured = ability;
                Button add = new Button(() => AddCatalogAbility(captured)) { text = "+ добавить" };
                add.style.width = 92f;
                add.style.flexShrink = 0f;
                add.SetEnabled(ability.Assignable && data != null);
                row.Add(add);
                VisualElement body = new VisualElement();
                body.style.flexGrow = 1f;
                body.style.flexShrink = 1f;
                body.style.marginLeft = 6f;
                string title = string.IsNullOrEmpty(ability.Defaults.Title) ? ability.Kind.ToString() : ability.Defaults.Title;
                Label name = new Label(title + "  (" + ability.Kind + ")" + (owned > 0 ? "   [у бойца: " + owned + "]" : string.Empty));
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                name.style.color = owned > 0 ? HeaderColor : Color.white;
                body.Add(name);
                Label explain = new Label(SandboxAbilityDefaults.Explain(ability.Defaults));
                explain.style.whiteSpace = WhiteSpace.Normal;
                body.Add(explain);
                Label meta = new Label("КД " + ability.Defaults.Cooldown + " · дальность " + ability.Defaults.RangeMin + "–" + ability.Defaults.RangeMax +
                                       " · тип " + ability.Defaults.DamageType + (ability.Defaults.IsMagical ? " · магическая" : string.Empty) +
                                       (ability.Assignable ? string.Empty : " · нет поля в Базе существ"));
                meta.style.fontSize = 10f;
                meta.style.color = HintColor;
                body.Add(meta);
                row.Add(body);
                scroll.Add(row);
            }
            card.Add(scroll);
        }

        private void AddCatalogAbility(CatalogAbility ability)
        {
            if (selectedUnitIndex < 0 || selectedUnitIndex >= database.Units.Count || !Enum.TryParse(ability.Kind.ToString(), out UnitAbilityKind kind))
                return;
            UnitDefinitionData data = database.Units[selectedUnitIndex];
            string baseId = (data.Id + "." + ability.Kind).ToLowerInvariant();
            string id = baseId;
            for (int n = 2; data.Abilities.Any(item => item != null && item.Id == id); n++)
                id = baseId + "_" + n;
            SandboxAbilityDefinition d = ability.Defaults;
            string title = string.IsNullOrEmpty(d.Title) ? ability.Kind.ToString() : d.Title;
            UnitAbilityData created = UnitAbilityData.Create(id, title, UnitCombatBrick.Extension, UnitAbilityStatus.Active, SandboxAbilityDefaults.Explain(d));
            created.SetMechanic(kind, d.Cooldown, d.RangeMin, d.RangeMax, d.PathMin, d.PathMax, (float)d.DamageMultiplier, d.Amount,
                d.ObjectHitPoints, d.Duration, d.Limit, d.Push, string.Empty);
            created.SetDamage((UnitDamageType)(int)d.DamageType, d.IsMagical);
            Undo.RecordObject(database, "Добавить способность из каталога");
            data.AddAbility(created);
            EditorUtility.SetDirty(database);
            serializedDatabase.Update();
            ShowSelectedUnit();
        }

        private void BuildStatusCatalog(VisualElement card)
        {
            IReadOnlyList<SandboxStatusDefinition> all = SandboxStatusCatalog.All;
            card.Add(Hint("Все " + all.Count + " состояний боя: значок, группа, механика. Состояния накладывают теги и способности " +
                          "(поле «ID состояния» способности)."));
            List<CatalogTag> tags = LoadCatalogTags();
            ScrollView scroll = new ScrollView();
            scroll.style.maxHeight = 520f;
            foreach (SandboxStatusDefinition def in all)
            {
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.paddingTop = 4f;
                row.style.paddingBottom = 4f;
                row.style.borderBottomWidth = 1f;
                row.style.borderBottomColor = CardBorder;
                VisualElement icon = new VisualElement();
                icon.style.width = 32f;
                icon.style.height = 32f;
                icon.style.flexShrink = 0f;
                Sprite sprite = Resources.Load<Sprite>("StatusIcons/" + def.IconId.Replace('.', '_'));
                if (sprite != null)
                    icon.style.backgroundImage = new StyleBackground(sprite);
                row.Add(icon);
                VisualElement body = new VisualElement();
                body.style.flexGrow = 1f;
                body.style.flexShrink = 1f;
                body.style.marginLeft = 8f;
                Label name = new Label((def.IsExecutable ? "✓ " : "— ") + def.Name);
                name.style.unityFontStyleAndWeight = FontStyle.Bold;
                name.style.color = def.Polarity == SandboxStatusPolarity.Beneficial ? new Color(0.6f, 0.85f, 0.6f, 1f) : new Color(0.95f, 0.7f, 0.6f, 1f);
                body.Add(name);
                Label text = new Label(SandboxStatusText.Describe(def));
                text.style.whiteSpace = WhiteSpace.Normal;
                body.Add(text);
                int linked = tags.Count(tag => !tag.ReadOnly && tag.StatusIds.Contains(def.Id));
                Label meta = new Label(def.Id + " · " + def.Category + (def.StrongControl ? " · сильный контроль" : string.Empty) +
                                       (def.Supernatural ? " · магическое" : string.Empty) + " · активных тегов со ссылкой: " + linked);
                meta.style.fontSize = 10f;
                meta.style.color = HintColor;
                body.Add(meta);
                row.Add(body);
                scroll.Add(row);
            }
            card.Add(scroll);
        }

        private void BuildCombatFacts(VisualElement card, SerializedProperty unit)
        {
            card.Add(Hint("Факты бойца, которые читают теги и способности. Классификаторы тела и оружия назначаются как теги " +
                          "(вкладка «Теги», категория «Факты и правила движка»: «Растительное тело», «Крупное тело», «Природное оружие», «Уклончивый»)."));
            card.Add(IntSlider(unit, "magicReserve", "Чародейский запас", 0, 20, "0 — заклинания ограничены только действием и КД."));
            card.Add(IntSlider(unit, "ammo", "Боевые снаряды", 0, 40, "0 — бесконечные природные выстрелы; иначе каждый дальний удар тратит снаряд."));
            AddField(card, unit, "canBeRescuedInBattle", "Можно спасти в бою («При смерти»)");
            card.Add(Hint("Условия поля (темнота, фланговая доплата, боевая усталость) задаются боем: SandboxBattle.IsDark, FlankBonusDamage, " +
                          "FatigueFromRound. По умолчанию выключены."));
        }
    }
}
