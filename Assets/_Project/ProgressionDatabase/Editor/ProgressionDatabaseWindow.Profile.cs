using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.UnitDatabase;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.ProgressionDatabase.Editor
{
    // Страница карты развития одного типа: кто он и что умеет, инструменты
    // заполнения, график опыта и таблица всех 100 уровней — опыт до
    // следующего, накопленный опыт, уровень выбора, прибавки характеристик,
    // итоговые характеристики, цена в бою и заметка.
    public sealed partial class ProgressionDatabaseWindow
    {
        private static readonly string[] BonusFields = { "hitPoints", "attack", "defense", "damage", "movement", "initiative" };
        private static readonly string[] BonusTitles = { "+HP", "+Атака", "+Защита", "+Урон", "+Ход", "+Иниц." };

        private int profileIndex = -1;
        private MultiColumnListView levelTable;
        private VisualElement experienceChart;
        private Label profileSummary;
        private readonly long[] totalExperience = new long[ProgressionProfile.LevelCount];
        private readonly UnitCombatStats[] statsAtLevel = new UnitCombatStats[ProgressionProfile.LevelCount];
        private readonly int[] battleValue = new int[ProgressionProfile.LevelCount];
        private int profileSignature;
        private bool tableRefreshScheduled;

        private ProgressionProfileRecord CurrentProfile =>
            profileIndex >= 0 && profileIndex < database.profiles.Count ? database.profiles[profileIndex] : null;

        private SerializedProperty ProfileProperty =>
            serializedDatabase.FindProperty("profiles").GetArrayElementAtIndex(profileIndex);

        private VisualElement BuildProfilePage(int index)
        {
            profileIndex = index;
            ProgressionProfileRecord profile = CurrentProfile;
            SerializedProperty property = ProfileProperty;
            bool isHero = profile.id == ProgressionRules.HeroProfileId;
            UnitDefinitionData unit = BaseUnit(profile.id);

            VisualElement root = new VisualElement();
            root.style.flexGrow = 1f;
            root.RegisterCallback<SerializedPropertyChangeEvent>(_ =>
            {
                OnDatabaseChanged();
                ScheduleTableRefresh();
            });

            ScrollView top = new ScrollView(ScrollViewMode.Vertical);
            top.style.maxHeight = new Length(40f, LengthUnit.Percent);
            top.style.flexShrink = 0f;
            top.style.paddingLeft = 16f;
            top.style.paddingRight = 16f;
            top.style.paddingTop = 10f;
            root.Add(top);

            AddTitle(top, (isHero ? "Командир" : string.IsNullOrWhiteSpace(profile.displayName) ? profile.id : profile.displayName) +
                          " — карта развития 1–100");
            AddNote(top, DescribeUnit(profile.id, unit, isHero));

            VisualElement columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            top.Add(columns);

            VisualElement left = new VisualElement();
            left.style.width = new Length(50f, LengthUnit.Percent);
            left.style.paddingRight = 12f;
            columns.Add(left);
            VisualElement right = new VisualElement();
            right.style.flexGrow = 1f;
            columns.Add(right);

            AddSection(left, "КТО ЭТО");
            AddProperty(left, property.FindPropertyRelative("displayName"), "Название");
            Toggle progresses = new Toggle("Копит опыт и растёт")
            {
                value = profile.progresses,
                tooltip = "Командир и постоянные бойцы — да. Существа имеют уровень как силу противника, но опыт не копят."
            };
            progresses.RegisterValueChangedCallback(evt => ModifyProfile("Копит опыт", record => record.progresses = evt.newValue));
            left.Add(progresses);

            if (profile.progresses)
            {
                AddProperty(left, property.FindPropertyRelative("startingLevel"), "Стартовый уровень",
                    "С каким уровнем приходит новый человек этого типа (§27.8).");
                AddCompetencyPopup(left, property.FindPropertyRelative("meleeCompetencyId"), "Удар вблизи развивает");
                AddCompetencyPopup(left, property.FindPropertyRelative("rangedCompetencyId"), "Удар издалека развивает");
                BuildStartingCompetencies(left, property);

                AddSection(right, "ВЫБОР РАЗВИТИЯ");
                BuildChoiceCatalog(right, property, isHero);
            }
            else
            {
                AddNote(left, "Противник или существо: уровень задаёт его силу в бою (прибавки по уровням) и цену в опыте " +
                              "для отряда. Опыт, умения и выборы развития у него не копятся. Уровень противника задаёт бой.");
            }
            BuildTools(right, isHero);

            // Сводка и график — всегда на виду над таблицей.
            VisualElement overview = new VisualElement();
            overview.style.flexDirection = FlexDirection.Row;
            overview.style.flexShrink = 0f;
            overview.style.marginLeft = 16f;
            overview.style.marginRight = 16f;
            overview.style.marginTop = 6f;
            overview.style.marginBottom = 6f;
            root.Add(overview);

            profileSummary = new Label();
            profileSummary.style.whiteSpace = WhiteSpace.Normal;
            profileSummary.style.flexGrow = 1f;
            profileSummary.style.flexBasis = 0f;
            profileSummary.style.marginRight = 12f;
            overview.Add(profileSummary);

            VisualElement chartBox = new VisualElement();
            chartBox.style.flexGrow = 1f;
            chartBox.style.flexBasis = 0f;
            overview.Add(chartBox);
            Label chartTitle = new Label(profile.progresses
                ? "Накопленный опыт по уровням (золотые отметки внизу — уровни выбора, линии — каждые 10)"
                : "Цена в бою по уровням (линии — каждые 10)");
            chartTitle.style.fontSize = 10f;
            chartTitle.style.color = MutedColor;
            chartBox.Add(chartTitle);
            experienceChart = new VisualElement();
            experienceChart.style.height = 64f;
            experienceChart.style.backgroundColor = new Color(0.13f, 0.13f, 0.13f, 1f);
            experienceChart.generateVisualContent += DrawExperienceChart;
            chartBox.Add(experienceChart);

            levelTable = BuildLevelTable(isHero);
            root.Add(levelTable);

            Recompute();
            top.Bind(serializedDatabase);
            return root;
        }

        private UnitDefinitionData BaseUnit(string profileId)
        {
            if (units == null)
                return null;
            string unitId = profileId == ProgressionRules.HeroProfileId ? CampaignBattleBridge.HeroFallbackUnitTypeId : profileId;
            return units.FindById(unitId);
        }

        private static string DescribeUnit(string id, UnitDefinitionData unit, bool isHero)
        {
            string head = isHero
                ? "ID «hero». Боевая основа героя пока — ополчение (militia); облик героя не утверждён."
                : "ID «" + id + "»" + (unit != null ? " · " + CategoryName(unit.Category) : " · нет в Базе существ");
            if (unit == null)
                return head;
            string stats = "База: HP " + unit.MaxHitPoints + " · атака " + unit.Attack + " · защита " + unit.Defense + " · урон " +
                           unit.Damage + " · ход " + unit.Movement + " · инициатива " + unit.Initiative + " · дальность " + unit.AttackRange + ".";
            return head + "\n" + stats + "\nУровень сам характеристики не меняет (канон §27.5): всё, что растёт, — в колонках прибавок ниже.";
        }

        private static string CategoryName(UnitCategory category)
        {
            switch (category)
            {
                case UnitCategory.Fighter: return "боец";
                case UnitCategory.Creature: return "противник / существо";
                case UnitCategory.Commander: return "командир";
                default: return "прочее";
            }
        }

        // ------------------------------------------------------------------
        // Умения
        // ------------------------------------------------------------------

        private void AddCompetencyPopup(VisualElement parent, SerializedProperty property, string label)
        {
            List<string> choices = new List<string> { string.Empty };
            choices.AddRange(CompetencyIds());
            string current = property.stringValue ?? string.Empty;
            if (!choices.Contains(current))
                choices.Add(current);
            PopupField<string> popup = new PopupField<string>(label, choices, current, CompetencyName, CompetencyName);
            popup.RegisterValueChangedCallback(evt =>
            {
                property.serializedObject.Update();
                property.stringValue = evt.newValue ?? string.Empty;
                property.serializedObject.ApplyModifiedProperties();
                OnDatabaseChanged();
            });
            parent.Add(popup);
        }

        private void BuildStartingCompetencies(VisualElement parent, SerializedProperty profile)
        {
            AddSection(parent, "СТАРТОВЫЕ КОМПЕТЕНЦИИ");
            AddNote(parent, "Что умеет новый человек этого типа (ступень 0–5). Ступень 1 боевых чисел не меняет.");
            SerializedProperty list = profile.FindPropertyRelative("startingCompetencies");
            for (int i = 0; i < list.arraySize; i++)
            {
                int index = i;
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                AddCompetencyPopup(row, entry.FindPropertyRelative("competencyId"), string.Empty);
                row[0].style.flexGrow = 1f;
                IntegerField rank = new IntegerField("ступень") { bindingPath = entry.FindPropertyRelative("rank").propertyPath };
                rank.style.width = 130f;
                row.Add(rank);
                Button remove = new Button(() => ModifyProfile("Убрать стартовую компетенцию", record => record.startingCompetencies.RemoveAt(index))) { text = "×" };
                row.Add(remove);
                parent.Add(row);
            }
            parent.Add(new Button(() => ModifyProfile("Добавить стартовую компетенцию", record =>
                record.startingCompetencies.Add(new CompetencyRankRecord { competencyId = NarrativeCompetencyIds.ChoppingWeapons, rank = 1 })))
            {
                text = "+ КОМПЕТЕНЦИЯ"
            });
        }

        private void BuildChoiceCatalog(VisualElement parent, SerializedProperty profile, bool isHero)
        {
            ProgressionProfileRecord record = CurrentProfile;
            bool usesDefault = record.choiceCompetencies.Count == 0;
            AddNote(parent, "Из каких компетенций строятся варианты «Углубить» и «Новое дело». " +
                            (usesDefault
                                ? "Сейчас — по умолчанию: " + (isHero ? "весь каталог компетенций." : "каталог бойца (отмечен в перечне компетенций).")
                                : "Задан свой список."));
            Foldout foldout = new Foldout { text = "Компетенции для выбора (" + (usesDefault ? "по умолчанию" : record.choiceCompetencies.Count.ToString()) + ")", value = false };
            VisualElement grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            foreach (string id in CompetencyIds())
            {
                string competencyId = id;
                bool enabled = usesDefault ? DefaultChoice(competencyId, isHero) : record.choiceCompetencies.Contains(competencyId);
                Toggle toggle = new Toggle(CompetencyName(competencyId)) { value = enabled };
                toggle.style.width = 210f;
                toggle.RegisterValueChangedCallback(evt => ModifyProfile("Каталог выбора", target =>
                {
                    if (target.choiceCompetencies.Count == 0)
                        target.choiceCompetencies.AddRange(CompetencyIds().Where(candidate => DefaultChoice(candidate, isHero)));
                    target.choiceCompetencies.Remove(competencyId);
                    if (evt.newValue)
                        target.choiceCompetencies.Add(competencyId);
                }, rebuild: false));
                grid.Add(toggle);
            }
            foldout.Add(grid);
            foldout.Add(new Button(() => ModifyProfile("Каталог выбора по умолчанию", target => target.choiceCompetencies.Clear()))
            {
                text = "ВЕРНУТЬ ПО УМОЛЧАНИЮ"
            });
            parent.Add(foldout);
        }

        private bool DefaultChoice(string competencyId, bool isHero)
        {
            if (isHero)
                return true;
            CompetencyRecord entry = database.competencies.Find(candidate => candidate != null && candidate.id == competencyId);
            return entry != null && entry.fighterCatalog;
        }

        // ------------------------------------------------------------------
        // Инструменты заполнения карты
        // ------------------------------------------------------------------

        private void BuildTools(VisualElement parent, bool isHero)
        {
            AddSection(parent, "ИНСТРУМЕНТЫ КАРТЫ");

            if (CurrentProfile.progresses)
                BuildCurveTool(parent);

            BuildRestTools(parent, isHero);
        }

        private void BuildCurveTool(VisualElement parent)
        {
            VisualElement curve = ToolRow(parent);
            IntegerField curveBase = new IntegerField("Опыт: 1-й уровень") { value = 100 };
            IntegerField curveStep = new IntegerField("+ за уровень") { value = 25 };
            FloatField curveGrowth = new FloatField("× рост, %") { value = 0f, tooltip = "Дополнительный рост на каждом уровне (0 — линейная кривая)." };
            curve.Add(curveBase);
            curve.Add(curveStep);
            curve.Add(curveGrowth);
            curve.Add(new Button(() => ModifyProfile("Кривая опыта", record =>
            {
                for (int level = 1; level < ProgressionProfile.LevelCount; level++)
                {
                    double value = (curveBase.value + (double)curveStep.value * (level - 1)) * Math.Pow(1.0 + curveGrowth.value / 100.0, level - 1);
                    record.levels[level - 1].experienceToNext = (int)Math.Max(1, Math.Min(int.MaxValue / 200, Math.Round(value)));
                }
                record.levels[ProgressionProfile.LevelCount - 1].experienceToNext = 0;
            })) { text = "ЗАПОЛНИТЬ" });
        }

        private void BuildRestTools(VisualElement parent, bool isHero)
        {
            if (CurrentProfile.progresses)
            {
                VisualElement choices = ToolRow(parent);
                IntegerField every = new IntegerField("Выбор каждые") { value = 3 };
                choices.Add(every);
                choices.Add(new Button(() => ModifyProfile("Уровни выбора", record =>
                {
                    int step = Math.Max(1, every.value);
                    for (int level = 1; level <= ProgressionProfile.LevelCount; level++)
                        record.levels[level - 1].choice = level % step == 0;
                })) { text = "РАССТАВИТЬ" });
            }

            VisualElement bonus = ToolRow(parent);
            PopupField<string> stat = new PopupField<string>("Прибавка", BonusFields.ToList(), 0, field => BonusTitles[Array.IndexOf(BonusFields, field)], field => BonusTitles[Array.IndexOf(BonusFields, field)]);
            IntegerField amount = new IntegerField("на") { value = 1 };
            IntegerField period = new IntegerField("каждые, ур.") { value = 5 };
            bonus.Add(stat);
            bonus.Add(amount);
            bonus.Add(period);
            bonus.Add(new Button(() => ModifyProfile("Прибавка по уровням", record =>
            {
                int step = Math.Max(1, period.value);
                for (int level = step; level <= ProgressionProfile.LevelCount; level += step)
                    AddBonus(record.levels[level - 1], stat.value, amount.value);
            })) { text = "ДОБАВИТЬ" });
            bonus.Add(new Button(() => ModifyProfile("Обнулить прибавки", record =>
            {
                foreach (ProgressionLevelRecord level in record.levels)
                {
                    level.hitPoints = level.attack = level.defense = level.damage = level.movement = level.initiative = 0;
                }
            })) { text = "ОБНУЛИТЬ ВСЕ" });

            // Командир — не противник: цена в бою ему не нужна.
            if (!isHero)
            {
                VisualElement value = ToolRow(parent);
                value.Add(new Button(() => ModifyProfile("Цена в бою по формуле", record =>
                {
                    Recompute();
                    ProgressionRules rules = database.ToRules();
                    for (int i = 0; i < ProgressionProfile.LevelCount; i++)
                    {
                        UnitCombatStats stats = statsAtLevel[i];
                        record.levels[i].battleExperience = Math.Max(0,
                            rules.EnemyHitPointWeight * stats.MaxHitPoints + rules.EnemyStatWeight * (stats.Attack + stats.Defense + stats.Damage));
                    }
                })) { text = "ЦЕНА В БОЮ ПО ФОРМУЛЕ" });
                value.Add(new Button(() => ModifyProfile("Цена в бою — формулой", record =>
                {
                    foreach (ProgressionLevelRecord level in record.levels)
                        level.battleExperience = 0;
                })) { text = "ОЧИСТИТЬ (0 = ФОРМУЛА)" });
            }

            VisualElement copy = ToolRow(parent);
            List<string> others = database.profiles.Where(profile => profile != null && profile != CurrentProfile).Select(profile => profile.id).ToList();
            if (others.Count > 0)
            {
                PopupField<string> source = new PopupField<string>("Скопировать карту из", others, 0, ProfileName, ProfileName);
                copy.Add(source);
                copy.Add(new Button(() => ModifyProfile("Скопировать карту", record =>
                {
                    ProgressionProfileRecord from = database.FindProfile(source.value);
                    if (from == null)
                        return;
                    for (int i = 0; i < ProgressionProfile.LevelCount && i < from.levels.Count; i++)
                        record.levels[i] = JsonUtility.FromJson<ProgressionLevelRecord>(JsonUtility.ToJson(from.levels[i]));
                })) { text = "КОПИРОВАТЬ" });
            }
        }

        private string ProfileName(string id)
        {
            ProgressionProfileRecord profile = database.FindProfile(id);
            return profile != null && !string.IsNullOrWhiteSpace(profile.displayName) ? profile.displayName + " (" + id + ")" : id;
        }

        private static VisualElement ToolRow(VisualElement parent)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2f;
            // Подпись вплотную к числу: поля инструментов компактные.
            row.RegisterCallback<AttachToPanelEvent>(_ => CompactFields(row));
            parent.Add(row);
            return row;
        }

        private static void CompactFields(VisualElement row)
        {
            foreach (VisualElement child in row.Children())
            {
                Label label = child.Q<Label>(className: "unity-base-field__label");
                VisualElement input = child.Q(className: "unity-base-field__input");
                if (label == null || input == null)
                    continue;
                label.style.minWidth = 0f;
                label.style.width = StyleKeyword.Auto;
                label.style.paddingRight = 4f;
                child.style.flexGrow = 0f;
                child.style.marginRight = 10f;
                if (child is PopupField<string>)
                {
                    input.style.minWidth = 170f;
                    continue;
                }
                input.style.width = 52f;
                input.style.flexGrow = 0f;
            }
        }

        private static void AddBonus(ProgressionLevelRecord level, string field, int amount)
        {
            switch (field)
            {
                case "hitPoints": level.hitPoints += amount; break;
                case "attack": level.attack += amount; break;
                case "defense": level.defense += amount; break;
                case "damage": level.damage += amount; break;
                case "movement": level.movement += amount; break;
                case "initiative": level.initiative += amount; break;
            }
        }

        // Правка профиля кодом: с отменой (Undo), сохранением и перерисовкой.
        private void ModifyProfile(string undoName, Action<ProgressionProfileRecord> change, bool rebuild = true)
        {
            ProgressionProfileRecord record = CurrentProfile;
            if (record == null)
                return;
            Undo.RecordObject(database, "База развития: " + undoName);
            EnsureLevelCount(record);
            change(record);
            EditorUtility.SetDirty(database);
            serializedDatabase.Update();
            OnDatabaseChanged();
            if (rebuild)
                ShowPage(selectedPage);
            else
                ScheduleTableRefresh();
        }

        private static void EnsureLevelCount(ProgressionProfileRecord record)
        {
            while (record.levels.Count < ProgressionProfile.LevelCount)
            {
                ProgressionLevel level = ProgressionProfile.DefaultLevel(record.levels.Count + 1);
                record.levels.Add(new ProgressionLevelRecord { experienceToNext = level.ExperienceToNext, choice = level.Choice });
            }
            if (record.levels.Count > ProgressionProfile.LevelCount)
                record.levels.RemoveRange(ProgressionProfile.LevelCount, record.levels.Count - ProgressionProfile.LevelCount);
        }

        // ------------------------------------------------------------------
        // Расчёт: накопленный опыт, характеристики и цена на каждом уровне
        // ------------------------------------------------------------------

        private void Recompute()
        {
            ProgressionProfileRecord record = CurrentProfile;
            if (record == null)
                return;
            EnsureLevelCount(record);
            ProgressionProfile profile = ProgressionDatabaseAsset.ToProfile(record);
            UnitDefinitionData unit = BaseUnit(record.id);
            UnitCombatStats baseStats = unit != null
                ? new UnitCombatStats
                {
                    MaxHitPoints = unit.MaxHitPoints, Attack = unit.Attack, Defense = unit.Defense, Damage = unit.Damage,
                    Movement = unit.Movement, Initiative = unit.Initiative, AttackRange = unit.AttackRange
                }
                : default;

            ProgressionRules rules = database.ToRules();
            long total = 0;
            StatModifier bonus = default;
            int choiceCount = 0;
            List<int> choiceLevels = new List<int>();
            for (int level = 1; level <= ProgressionProfile.LevelCount; level++)
            {
                totalExperience[level - 1] = total;
                total += profile.ExperienceToNextLevel(level);

                ProgressionLevel data = profile.GetLevel(level);
                bonus.MaxHitPoints += data.Bonus.MaxHitPoints;
                bonus.Attack += data.Bonus.Attack;
                bonus.Defense += data.Bonus.Defense;
                bonus.Damage += data.Bonus.Damage;
                bonus.Movement += data.Bonus.Movement;
                bonus.Initiative += data.Bonus.Initiative;
                UnitCombatStats stats = baseStats;
                stats.MaxHitPoints += bonus.MaxHitPoints;
                stats.Attack += bonus.Attack;
                stats.Defense += bonus.Defense;
                stats.Damage += bonus.Damage;
                stats.Movement += bonus.Movement;
                stats.Initiative += bonus.Initiative;
                statsAtLevel[level - 1] = stats;
                battleValue[level - 1] = data.BattleExperience > 0
                    ? data.BattleExperience
                    : Math.Max(0, rules.EnemyHitPointWeight * stats.MaxHitPoints + rules.EnemyStatWeight * (stats.Attack + stats.Defense + stats.Damage));
                if (data.Choice)
                {
                    choiceCount++;
                    choiceLevels.Add(level);
                }
            }

            profileSignature = Signature(record);
            if (profileSummary != null)
            {
                UnitCombatStats top = statsAtLevel[ProgressionProfile.LevelCount - 1];
                bool isHero = record.id == ProgressionRules.HeroProfileId;
                profileSummary.text =
                    (record.progresses ? "До 100-го уровня: " + totalExperience[ProgressionProfile.LevelCount - 1].ToString("N0") + " опыта · " +
                    "до 10-го: " + totalExperience[9].ToString("N0") + " · до 50-го: " + totalExperience[49].ToString("N0") + ".\n" : string.Empty) +
                    (record.progresses
                        ? "Уровней выбора: " + choiceCount + (choiceLevels.Count > 0 ? " (" + CompressLevels(choiceLevels) + ")" : string.Empty) + ".\n"
                        : "Опыт не копит: уровень задаёт силу противника и цену в опыте.\n") +
                    "Прибавки к 100-му уровню: " + (bonus.IsZero ? "нет" : bonus.Describe()) +
                    (unit != null ? " → HP " + top.MaxHitPoints + " · атака " + top.Attack + " · защита " + top.Defense + " · урон " + top.Damage : string.Empty) + "." +
                    (isHero ? string.Empty
                        : "\nЦена в бою: ур. 1 — " + battleValue[0] + " · ур. 10 — " + battleValue[9] + " · ур. 50 — " + battleValue[49] + " · ур. 100 — " + battleValue[99] + ".");
            }
            experienceChart?.MarkDirtyRepaint();
        }

        private static string CompressLevels(List<int> levels)
        {
            if (levels.Count <= 8)
                return string.Join(", ", levels);
            return string.Join(", ", levels.Take(4)) + " … " + string.Join(", ", levels.Skip(levels.Count - 2));
        }

        private static int Signature(ProgressionProfileRecord record)
        {
            unchecked
            {
                int hash = 17;
                foreach (ProgressionLevelRecord level in record.levels)
                {
                    hash = hash * 31 + level.experienceToNext;
                    hash = hash * 31 + (level.choice ? 1 : 0);
                    hash = hash * 31 + level.hitPoints + 3 * level.attack + 5 * level.defense + 7 * level.damage + 11 * level.movement + 13 * level.initiative;
                    hash = hash * 31 + level.battleExperience;
                }
                return hash;
            }
        }

        private void ScheduleTableRefresh()
        {
            if (tableRefreshScheduled || levelTable == null)
                return;
            tableRefreshScheduled = true;
            levelTable.schedule.Execute(() =>
            {
                tableRefreshScheduled = false;
                ProgressionProfileRecord record = CurrentProfile;
                if (record == null || Signature(record) == profileSignature)
                    return;
                serializedDatabase.Update();
                Recompute();
                levelTable.RefreshItems();
            });
        }

        private void DrawExperienceChart(MeshGenerationContext context)
        {
            Rect rect = experienceChart.contentRect;
            bool progresses = CurrentProfile == null || CurrentProfile.progresses;
            long max = 0;
            for (int i = 0; i < ProgressionProfile.LevelCount; i++)
                max = Math.Max(max, progresses ? totalExperience[i] : battleValue[i]);
            if (rect.width <= 2f || rect.height <= 2f || max <= 0)
                return;

            Painter2D painter = context.painter2D;
            painter.lineWidth = 1f;
            painter.strokeColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            for (int level = 10; level < ProgressionProfile.LevelCount; level += 10)
            {
                float x = rect.width * (level - 1) / (ProgressionProfile.LevelCount - 1);
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, rect.height));
                painter.Stroke();
            }

            // Отметки уровней выбора.
            ProgressionProfileRecord record = CurrentProfile;
            painter.strokeColor = new Color(0.87f, 0.71f, 0.39f, 0.35f);
            for (int level = 1; record != null && level <= ProgressionProfile.LevelCount && level <= record.levels.Count; level++)
            {
                if (!record.levels[level - 1].choice)
                    continue;
                float x = rect.width * (level - 1) / (ProgressionProfile.LevelCount - 1);
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, rect.height - 6f));
                painter.LineTo(new Vector2(x, rect.height));
                painter.Stroke();
            }

            // Накопленный опыт до уровня.
            painter.lineWidth = 2f;
            painter.strokeColor = new Color(0.87f, 0.71f, 0.39f, 1f);
            painter.BeginPath();
            for (int level = 1; level <= ProgressionProfile.LevelCount; level++)
            {
                float x = rect.width * (level - 1) / (ProgressionProfile.LevelCount - 1);
                long point = progresses ? totalExperience[level - 1] : battleValue[level - 1];
                float y = rect.height - 4f - (rect.height - 8f) * point / (float)max;
                if (level == 1)
                    painter.MoveTo(new Vector2(x, y));
                else
                    painter.LineTo(new Vector2(x, y));
            }
            painter.Stroke();
        }

        // ------------------------------------------------------------------
        // Таблица 100 уровней
        // ------------------------------------------------------------------

        private MultiColumnListView BuildLevelTable(bool isHero)
        {
            MultiColumnListView table = new MultiColumnListView
            {
                itemsSource = CurrentProfile.levels,
                fixedItemHeight = 22f,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                reorderable = false
            };
            table.style.flexGrow = 1f;
            table.style.marginLeft = 8f;
            table.style.marginRight = 8f;
            table.style.marginBottom = 8f;

            table.columns.Add(LabelColumn("Ур.", 40f, index => (index + 1).ToString(), index => (index + 1) % 10 == 0));
            // Существа опыт не копят — у них карта про силу и цену в бою.
            if (CurrentProfile.progresses)
            {
            table.columns.Add(IntColumn("Опыт до след.", 95f, "experienceToNext",
                "Сколько опыта от этого уровня до следующего. На 100-м — 0.", index => index < ProgressionProfile.LevelCount - 1));
            table.columns.Add(LabelColumn("Всего опыта", 95f, index => totalExperience[index].ToString("N0"), _ => false,
                "Накопленный опыт, нужный, чтобы достичь этого уровня."));
            }
            if (CurrentProfile.progresses)
                table.columns.Add(ToggleColumn("Выбор", 55f, "choice", "На этом уровне — значимый выбор развития (канон: каждые 3)."));
            for (int i = 0; i < BonusFields.Length; i++)
                table.columns.Add(IntColumn(BonusTitles[i], 62f, BonusFields[i], "Прибавка при достижении уровня, накопительно.", _ => true));
            table.columns.Add(LabelColumn("Итог на уровне", 250f, index =>
            {
                UnitCombatStats stats = statsAtLevel[index];
                return "HP " + stats.MaxHitPoints + " · А " + stats.Attack + " · З " + stats.Defense + " · У " + stats.Damage +
                       " · Х " + stats.Movement + " · И " + stats.Initiative;
            }, _ => false, "База типа + накопленные прибавки. Вещи, качества, владение и состояния добавляются в игре."));
            // Командир — не противник: цена в бою ему не нужна.
            if (!isHero)
            {
                table.columns.Add(IntColumn("Цена в бою", 80f, "battleExperience",
                    "Сколько опыта даёт победа над этим типом на этом уровне. 0 — по формуле общих правил.", _ => true));
                table.columns.Add(LabelColumn("= итого", 65f, index => battleValue[index].ToString(), _ => false,
                    "Действующая цена: заданная или по формуле."));
            }

            Column note = new Column
            {
                title = "Заметка",
                stretchable = true,
                minWidth = 120f,
                makeCell = () => new TextField(),
                bindCell = (element, index) => BindCell((TextField)element, index, "note"),
                unbindCell = (element, _) => ((TextField)element).Unbind()
            };
            table.columns.Add(note);
            return table;
        }

        private Column LabelColumn(string title, float width, Func<int, string> text, Func<int, bool> bold, string tooltip = null)
        {
            return new Column
            {
                title = title,
                width = width,
                makeCell = () =>
                {
                    Label label = new Label();
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    label.style.paddingLeft = 4f;
                    if (!string.IsNullOrEmpty(tooltip))
                        label.tooltip = tooltip;
                    return label;
                },
                bindCell = (element, index) =>
                {
                    Label label = (Label)element;
                    label.text = text(index);
                    label.style.unityFontStyleAndWeight = bold(index) ? FontStyle.Bold : FontStyle.Normal;
                    bool choice = CurrentProfile != null && CurrentProfile.progresses && index < CurrentProfile.levels.Count && CurrentProfile.levels[index].choice;
                    label.style.color = choice && title == "Ур." ? new StyleColor(GoldColor) : new StyleColor(StyleKeyword.Null);
                }
            };
        }

        private Column IntColumn(string title, float width, string field, string tooltip, Func<int, bool> editable)
        {
            return new Column
            {
                title = title,
                width = width,
                makeCell = () => new IntegerField { tooltip = tooltip },
                bindCell = (element, index) =>
                {
                    IntegerField cell = (IntegerField)element;
                    BindCell(cell, index, field);
                    cell.SetEnabled(editable(index));
                },
                unbindCell = (element, _) => ((IntegerField)element).Unbind()
            };
        }

        private Column ToggleColumn(string title, float width, string field, string tooltip)
        {
            return new Column
            {
                title = title,
                width = width,
                makeCell = () => new Toggle { tooltip = tooltip },
                bindCell = (element, index) => BindCell((Toggle)element, index, field),
                unbindCell = (element, _) => ((Toggle)element).Unbind()
            };
        }

        private void BindCell(IBindable cell, int index, string field)
        {
            if (profileIndex < 0)
                return;
            SerializedProperty levels = ProfileProperty.FindPropertyRelative("levels");
            if (index >= levels.arraySize)
                return;
            SerializedProperty property = levels.GetArrayElementAtIndex(index).FindPropertyRelative(field);
            ((VisualElement)cell).Unbind();
            cell.BindProperty(property);
        }
    }
}
