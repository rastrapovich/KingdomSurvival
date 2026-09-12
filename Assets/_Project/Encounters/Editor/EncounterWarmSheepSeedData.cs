using System;
using System.Collections.Generic;
using System.Reflection;
using KingdomSurvival.DialogueDatabase;
using UnityEditor;
using UnityEngine;

namespace KingdomSurvival.Encounters.Editor
{
    // Заполняет production-содержимое вертикального среза «Тёплая овца»:
    // пул + Encounter в KingdomSurvivalEncounters.asset, флаги в
    // KingdomSurvivalEncounterFlags.asset, диалог в существующем
    // KingdomSurvivalDialogues.asset. Построено по образцу
    // DevelopmentTracker/Editor/DevelopmentPlanSeedData.cs — тот же принцип
    // "код заполняет ассет через API, а не руки правят YAML без компилятора"
    // (см. предупреждение в DialogueDatabaseCheckSystemTests.cs). Идемпотентно:
    // повторный запуск обновляет существующие записи по Id, а не дублирует их.
    //
    // Статус контента: [РАБОЧЕЕ] (сценарная версия пользователя, 2026-09-12).
    // Объективная причина смерти овцы и связь с исчезновением других овец
    // намеренно оставлены [ОТКРЫТО] — ничего не утверждается в LORE.md/
    // BESTIARY.md этим скриптом. Echo-энкаунтер «Чья овца?» (владелец ищет
    // пропавший скот) в этот срез НЕ входит — соответствующие флаги
    // зарегистрированы как Reserved future hooks, сам Encounter не создан.
    public static class EncounterWarmSheepSeedData
    {
        private const string EncountersFolder = "Assets/_Project/Encounters/Resources/Encounters";
        private const string EncountersAssetPath = EncountersFolder + "/KingdomSurvivalEncounters.asset";
        private const string FlagsAssetPath = EncountersFolder + "/KingdomSurvivalEncounterFlags.asset";
        private const string DialogueAssetPath = "Assets/_Project/DialogueDatabase/Resources/DialogueDatabase/KingdomSurvivalDialogues.asset";

        private const string PoolId = RoadEncounterIds.FirstRegionPoolId;
        private const string EncounterId = "ROAD_WARM_SHEEP_01";
        private const string DialogueId = "road_warm_sheep_01";

        private const string FlagSeen = "ROAD_WARM_SHEEP_SEEN";
        private const string FlagInspected = "ROAD_WARM_SHEEP_INSPECTED";
        private const string FlagHumanTraceKnown = "ROAD_WARM_SHEEP_HUMAN_TRACE_KNOWN";
        private const string FlagFollowedTracks = "ROAD_WARM_SHEEP_FOLLOWED_TRACKS";
        private const string FlagManFound = "ROAD_WARM_SHEEP_MAN_FOUND";
        private const string FlagDirectionKnown = "ROAD_WARM_SHEEP_DIRECTION_KNOWN";
        private const string FlagManHelped = "ROAD_WARM_SHEEP_MAN_HELPED";
        private const string FlagManSupplied = "ROAD_WARM_SHEEP_MAN_SUPPLIED";
        private const string FlagMeatTakenFromMan = "ROAD_WARM_SHEEP_MEAT_TAKEN_FROM_MAN";
        private const string FlagLeftManUntouched = "ROAD_WARM_SHEEP_LEFT_MAN_UNTOUCHED";
        private const string FlagMeatTakenRaw = "ROAD_WARM_SHEEP_MEAT_TAKEN_RAW";
        private const string FlagDryLeafClue = "ROAD_WARM_SHEEP_DRY_LEAF_CLUE";
        private const string FlagLeftUntouched = "ROAD_WARM_SHEEP_LEFT_UNTOUCHED";

        private const string CheckInspect = "road_warm_sheep_inspect";
        private const string CheckPress = "road_warm_sheep_press";

        [MenuItem("Kingdom Survival/Seed/Тёплая овца (Encounter)")]
        public static void SeedWarmSheepEncounter()
        {
            SeedFlags();
            SeedEncounter();
            SeedDialogue();
            AssetDatabase.SaveAssets();
            Debug.Log("Kingdom Survival: Encounter «Тёплая овца» (" + EncounterId + ") засеян/обновлён.");
        }

        // ---------------------------------------------------------------
        // Flag Registry
        // ---------------------------------------------------------------

        private static void SeedFlags()
        {
            EncounterFlagRegistryAsset registry = LoadOrCreate<EncounterFlagRegistryAsset>(FlagsAssetPath);
            List<EncounterFlagDefinition> flags = GetOrCreateList<EncounterFlagDefinition>(registry, "flags");

            // Active — реально читаются внутри этого же среза.
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagSeen,
                DisplayName = "Тёплая овца увидена",
                Description = "Игрок открыл Encounter «Тёплая овца».",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Active,
                FutureUseNotes = "Потребитель: EncounterDefinition.ForbiddenFlags этого же Encounter (защита от повтора)."
            });
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagHumanTraceKnown,
                DisplayName = "След человека замечен",
                Description = "При осмотре туши герой заметил человеческий след без обуви.",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Active,
                FutureUseNotes = "Потребитель: условный текстовый блок узла follow_tracks (меняет тон описания погони)."
            });

            // Reserved — намеренные закладки без текущего потребителя в этом
            // срезе (§30): для будущего echo-энкаунтера «Чья овца?» и вообще
            // памяти о том, что герой видел/сделал.
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagInspected,
                DisplayName = "Туша осмотрена",
                Description = "Герой попытался осмотреть тушу (независимо от исхода проверки).",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Reserved,
                FutureUseNotes = "Для будущих сцен, которым важно, пытался ли герой вообще разобраться, а не только what he found."
            });
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagFollowedTracks,
                DisplayName = "Прошёл по следам",
                Description = "Герой пошёл по следу от туши и нашёл человека.",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Reserved,
                FutureUseNotes = "Future hook для follow-up сцен о самой находке места."
            });
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagManFound,
                DisplayName = "Человек у костра найден",
                Description = "Герой нашёл раненого человека в низине за дорогой.",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Reserved,
                FutureUseNotes = "Future hook: этот человек может упоминаться или встретиться снова."
            });
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagDirectionKnown,
                DisplayName = "Известно направление взгляда овцы",
                Description = "Герой узнал от человека, что овца перед смертью смотрела в сторону леса.",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Reserved,
                FutureUseNotes = "Ключевой future hook: использовать в будущей сцене, связанной с причиной (регион/существо), когда она будет утверждена в LORE.md/BESTIARY.md. Причина сейчас [ОТКРЫТО]."
            });
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagManHelped,
                DisplayName = "Человеку помогли добраться",
                Description = "Герой довёл раненого человека до безопасного места.",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Reserved,
                FutureUseNotes = "Future hook для отношения/эха: человек помнит помощь."
            });
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagManSupplied,
                DisplayName = "Человеку оставили припасы",
                Description = "Герой оставил человеку часть припасов и ушёл.",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Reserved,
                FutureUseNotes = "Future hook: человек выбрался сам, может всплыть позже."
            });
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagMeatTakenFromMan,
                DisplayName = "Мясо забрано у человека",
                Description = "Герой забрал мясо и ушёл, оставив человека почти ни с чем.",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Reserved,
                FutureUseNotes = "Future hook для будущей встречи владельца скота: как герой обошёлся с находкой."
            });
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagLeftManUntouched,
                DisplayName = "Человека оставили как есть",
                Description = "Герой поговорил с человеком, но не помог и не забрал мясо — просто ушёл.",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Reserved,
                FutureUseNotes = "Future hook: нейтральный исход разговора."
            });
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagMeatTakenRaw,
                DisplayName = "Мясо забрано без расследования",
                Description = "Герой сразу разделал тушу, не осматривая её и не идя по следу.",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Reserved,
                FutureUseNotes = "Future hook: герой прошёл мимо человеческой стороны истории."
            });
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagDryLeafClue,
                DisplayName = "Найден сухой лист не местной породы",
                Description = "При поспешной разделке в шерсти обнаружен сухой лист — растения такого вида у дороги нет.",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Reserved,
                FutureUseNotes = "Маленькая улика даже для чисто ресурсного выбора — future hook для региональной темы."
            });
            Upsert(flags, f => f.FlagId, new EncounterFlagDefinition
            {
                FlagId = FlagLeftUntouched,
                DisplayName = "Овца оставлена нетронутой",
                Description = "Герой прошёл мимо, не тронув тушу и не найдя человека.",
                Category = "road_warm_sheep",
                Status = EncounterFlagStatus.Reserved,
                FutureUseNotes = "Future hook: герой ничего не знает об этом случае, когда позже спросят про пропавших овец."
            });

            EditorUtility.SetDirty(registry);
        }

        // ---------------------------------------------------------------
        // Encounter Database
        // ---------------------------------------------------------------

        private static void SeedEncounter()
        {
            EncounterDatabaseAsset database = LoadOrCreate<EncounterDatabaseAsset>(EncountersAssetPath);
            List<EncounterPoolDefinition> pools = GetOrCreateList<EncounterPoolDefinition>(database, "pools");
            List<EncounterDefinition> encounters = GetOrCreateList<EncounterDefinition>(database, "encounters");

            Upsert(pools, p => p.PoolId, new EncounterPoolDefinition
            {
                PoolId = PoolId,
                DisplayName = "Дорожные встречи (первый регион)",
                Enabled = true,
                GlobalTriggerChancePercent = 45,
                MinimumHoursBetweenEncounters = 24,
                DesignerNotes = "MVP-пул: пока в нём один Production Encounter (ROAD_WARM_SHEEP_01)."
            });

            Upsert(encounters, e => e.EncounterId, new EncounterDefinition
            {
                EncounterId = EncounterId,
                DisplayName = "Тёплая овца",
                Description = "Овца лежит почти посреди дороги, ещё тёплая. Горло цело, задняя нога ободрана до кости.",
                Status = EncounterStatus.Production,
                Category = EncounterCategory.Road,
                Tags = new List<string> { "road", "animal", "mundane", "ambiguous", "forest-edge" },
                DialogueId = DialogueId,
                ResolutionMode = EncounterResolutionMode.DialogueDriven,
                SelectionMode = EncounterSelectionMode.Pool,
                PoolId = PoolId,
                DiscoveryChancePercent = 40,
                SelectionWeight = 20,
                UnlimitedOccurrences = false,
                MaxOccurrencesPerGame = 1,
                CooldownHours = 0,
                AllowedRegionIds = new List<string> { RoadEncounterIds.FirstRegionId },
                RequiredLocationTags = new List<string>(),
                ForbiddenLocationTags = new List<string>(),
                RequiredConditions = new NarrativeConditionGroup(),
                RequiredFlagsAll = new List<string>(),
                RequiredFlagsAny = new List<string>(),
                ForbiddenFlags = new List<string> { FlagSeen },
                FlagsSetOnStart = new List<string> { FlagSeen },
                FlagsSetOnComplete = new List<string>(),
                ClearFlagsOnComplete = new List<string>(),
                DesignerNotes = "[РАБОЧЕЕ] Рабочий эталон Encounter, предложен пользователем 2026-09-12 " +
                                "(источники: [ELDRITCH HORROR], [ГОГОЛЬ], [D&D / TRAVEL REFERENCES], [KINGDOM SURVIVAL]).",
                FutureHooksNotes = "Причина смерти овцы и связь с пропавшим скотом — [ОТКРЫТО]. Echo-энкаунтер " +
                                    "«Чья овца?» (владелец ищет двух других пропавших овец) НЕ реализован в этом " +
                                    "срезе; читает флаги *_MAN_*, *_MEAT_TAKEN_*, " + FlagDirectionKnown + ", " + FlagLeftUntouched + "."
            });

            EditorUtility.SetDirty(database);
        }

        // ---------------------------------------------------------------
        // Dialogue
        // ---------------------------------------------------------------

        private static void SeedDialogue()
        {
            DialogueDatabaseAsset asset = AssetDatabase.LoadAssetAtPath<DialogueDatabaseAsset>(DialogueAssetPath);
            if (asset == null)
            {
                Debug.LogError("Kingdom Survival: не найден " + DialogueAssetPath + " — база диалогов должна уже существовать.");
                return;
            }

            List<DialogueSpeakerData> speakers = GetOrCreateList<DialogueSpeakerData>(asset, "speakers");
            List<DialogueDefinitionData> dialogues = GetOrCreateList<DialogueDefinitionData>(asset, "dialogues");

            Upsert(speakers, s => s.Id, MakeSpeaker("narrator", "Рассказчик"));
            Upsert(speakers, s => s.Id, MakeSpeaker("hero_thought", "Мысль героя"));
            Upsert(speakers, s => s.Id, MakeSpeaker("traveller", "Человек у костра"));

            DialogueDefinitionData dialogue = BuildDialogue();
            Upsert(dialogues, d => d.Id, dialogue);

            EditorUtility.SetDirty(asset);

            List<string> issues = new List<string>();
            asset.CollectValidationIssuesForDialogue(DialogueId, issues);
            if (issues.Count > 0)
                Debug.LogWarning("Kingdom Survival: диалог " + DialogueId + " содержит проблемы валидации:\n" + string.Join("\n", issues));
        }

        private static DialogueDefinitionData BuildDialogue()
        {
            List<DialogueNodeData> nodes = new List<DialogueNodeData>
            {
                BuildStartNode(),
                BuildInspectSuccessNode(),
                BuildInspectFailureNode(),
                BuildFollowTracksNode(),
                BuildManFoundNode(),
                BuildPochtiNode(),
                BuildNogaNode(),
                BuildOtkudaNode(),
                BuildSecondApproachNode(),
                BuildConfessionNode(),
                BuildConfessionPartialNode(),
                BuildFinalChoicesNode(),
                BuildEndingHelpedNode(),
                BuildEndingSuppliedNode(),
                BuildEndingMeatTakenFromManNode(),
                BuildEndingLeftManNode(),
                BuildTakeMeatNowNode(),
                BuildLeaveNowNode()
            };

            return MakeDialogue(DialogueId, "start", nodes);
        }

        // --- start -------------------------------------------------------

        private static DialogueNodeData BuildStartNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_main", DialogueTextBlockKind.MainLine,
                    "Овца лежит почти посреди дороги — так небрежно, будто собиралась перейти на " +
                    "другую сторону и передумала на полпути.\n\nОна ещё тёплая.\n\nГорло цело. Шерсть на " +
                    "шее даже не примята.\n\nЗато одна задняя нога ободрана почти до кости. Мясо снято " +
                    "клочьями, и красная кость белеет между шерстью.\n\nВокруг тихо. Ни пастуха. Ни собак. " +
                    "Ни стада."),

                // Упрощение относительно авторского текста: в текущей модели
                // качества/компетенции хранятся только у самого героя
                // (HeroProfileData), у спутников — нет, поэтому реплика
                // "волк так не ест" привязана к Instinct героя, а не к
                // присутствию конкретного опытного спутника.
                MakeTextBlock("b_instinct", DialogueTextBlockKind.HeroThought,
                    "Волк так не ест.",
                    speakerOverride: "hero_thought",
                    conditions: Conditions(Cond(NarrativeConditionType.QualityAtLeast, quality: HeroQuality.Instinct, intParam: 6)))
            };

            List<DialogueChoiceData> choices = new List<DialogueChoiceData>
            {
                MakeActiveChoice(
                    "c_inspect", "Осмотреть тушу.",
                    DialogueChoiceKind.ActiveDecisive,
                    new NarrativeCheckSpec { CheckId = CheckInspect, Kind = NarrativeCheckKind.ActiveDecisive, Quality = HeroQuality.Instinct, CompetencyId = NarrativeCompetencyIds.Fieldcraft, Difficulty = NarrativeDifficulty.Ordinary },
                    "inspect_success", "inspect_failure",
                    successEffects: Effects(Effect("set_inspected_s", NarrativeEffectType.SetFlag, FlagInspected), Effect("set_trace_known", NarrativeEffectType.SetFlag, FlagHumanTraceKnown)),
                    failureEffects: Effects(Effect("set_inspected_f", NarrativeEffectType.SetFlag, FlagInspected))),
                MakeNormalChoice("c_follow", "Пройти по следам, пока они свежие.", "follow_tracks"),
                MakeNormalChoice("c_take_now", "Мясу всё равно пропадать.", "take_meat_now"),
                MakeNormalChoice("c_leave", "Не наше дело. Идём.", "leave_now")
            };

            return MakeNode("start", "narrator", blocks, choices);
        }

        // --- осмотр туши --------------------------------------------------

        private static DialogueNodeData BuildInspectSuccessNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_success", DialogueTextBlockKind.Observation,
                    "Шерсть вокруг раны почти сухая. Зубы здесь были — но уже после смерти.\n\n" +
                    "На внутренней стороне ноги видны другие следы: короткие прямые надрезы. Нож был " +
                    "тупой. И тот, кто им работал, очень спешил.\n\nКто-то разделывал овцу совсем недавно.\n\n" +
                    "В грязи под брюхом отпечаталась пятка. Человеческая. Без сапога.")
            };

            return MakeNode("inspect_success", "narrator", blocks, AfterInvestigationChoices());
        }

        private static DialogueNodeData BuildInspectFailureNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_failure", DialogueTextBlockKind.Observation,
                    "Чем дольше смотришь, тем меньше становится понятно. Следов слишком много: " +
                    "овечьи копыта, собственная кровь, продавленная трава, грязь с дороги.\n\n" +
                    "Одно ясно — если что-то и произошло здесь, дождь или первый проезжий уничтожат " +
                    "половину ответа. Решать надо сейчас.")
            };

            return MakeNode("inspect_failure", "narrator", blocks, AfterInvestigationChoices());
        }

        private static List<DialogueChoiceData> AfterInvestigationChoices()
        {
            return new List<DialogueChoiceData>
            {
                MakeNormalChoice("c_follow2", "Пройти по следам, пока они свежие.", "follow_tracks"),
                MakeNormalChoice("c_take2", "Мясу всё равно пропадать.", "take_meat_now"),
                MakeNormalChoice("c_leave2", "Не наше дело. Идём.", "leave_now")
            };
        }

        // --- погоня по следу ----------------------------------------------

        private static DialogueNodeData BuildFollowTracksNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_known", DialogueTextBlockKind.Narration,
                    "След найти легко — вот примятая трава, вот клочья шерсти на ветках. Овцу тащили, " +
                    "и тащили недавно.",
                    conditions: Conditions(Cond(NarrativeConditionType.FlagSet, stringParam: FlagHumanTraceKnown))),
                MakeTextBlock("b_unknown", DialogueTextBlockKind.Narration,
                    "За кустами становится видно, что овца сюда не пришла. Её тащили. Трава лежит " +
                    "полосой, на ветках остались белые клочья шерсти.",
                    conditions: Conditions(Cond(NarrativeConditionType.FlagSet, stringParam: FlagHumanTraceKnown, negate: true))),
                MakeTextBlock("b_common", DialogueTextBlockKind.Narration,
                    "Потом появляется человеческий след. Один человек. Босиком на одну ногу.\n\n" +
                    "След заканчивается возле небольшой низины.",
                    onRevealEffects: Effects(Effect("set_followed", NarrativeEffectType.SetFlag, FlagFollowedTracks)))
            };

            List<DialogueChoiceData> choices = new List<DialogueChoiceData>
            {
                MakeNormalChoice("c_go_on", "Подойти ближе.", "man_found")
            };

            return MakeNode("follow_tracks", "narrator", blocks, choices);
        }

        private static DialogueNodeData BuildManFoundNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_scene", DialogueTextBlockKind.MainLine,
                    "Под корнями лежит мужчина. Не разбойник в очевидной форме. Просто грязный, " +
                    "измученный человек.\n\nОдин сапог на нём, второй стоит рядом. Нога распухла.\n\n" +
                    "Рядом — нож, несколько кусков овечьего мяса, тряпка, маленький угасающий костёр.\n\n" +
                    "Он смотрит сначала на героя. Потом на мясо.",
                    onRevealEffects: Effects(Effect("set_man_found", NarrativeEffectType.SetFlag, FlagManFound))),
                MakeTextBlock("b_line", DialogueTextBlockKind.CompanionLine,
                    "— Она уже дохлая была. Почти.",
                    speakerOverride: "traveller")
            };

            List<DialogueChoiceData> choices = new List<DialogueChoiceData>
            {
                MakeNormalChoice("c_ask", "«Что значит «почти»?»", "man_pochti")
            };

            return MakeNode("man_found", "narrator", blocks, choices);
        }

        private static DialogueNodeData BuildPochtiNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_pochti", DialogueTextBlockKind.CompanionLine,
                    "— Ногами ещё била. — Пауза. — А встать не могла.\n\nОн проводит большим пальцем по " +
                    "лезвию ножа.\n\n— Я ей помог.",
                    speakerOverride: "traveller")
            };

            List<DialogueChoiceData> choices = new List<DialogueChoiceData>
            {
                MakeNormalChoice("c_ask_why", "«Почему ты здесь?»", "man_noga")
            };

            return MakeNode("man_pochti", "narrator", blocks, choices);
        }

        private static DialogueNodeData BuildNogaNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_noga", DialogueTextBlockKind.CompanionLine,
                    "— Нога.\n\nОн показывает опухшую ступню.\n\n— Вчера была меньше.\n\nЧерез секунду " +
                    "добавляет:\n\n— Или позавчера.",
                    speakerOverride: "traveller")
            };

            List<DialogueChoiceData> choices = new List<DialogueChoiceData>
            {
                MakeNormalChoice("c_ask_where", "«Откуда овца?»", "man_otkuda")
            };

            return MakeNode("man_noga", "narrator", blocks, choices);
        }

        private static DialogueNodeData BuildOtkudaNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_otkuda", DialogueTextBlockKind.CompanionLine,
                    "— Оттуда.\n\nОн показывает куда-то между дорогой и лесом.\n\n— Пришла сама.\n\n" +
                    "Потом смотрит на героя.\n\n— Ну... до дороги сама.",
                    speakerOverride: "traveller"),
                MakeTextBlock("b_lies", DialogueTextBlockKind.Observation,
                    "Он врёт. Но пока непонятно — о чём именно.")
            };

            List<DialogueChoiceData> choices = new List<DialogueChoiceData>
            {
                MakeContinueChoice("c_decide_approach", "Решить, как продолжить разговор.", "second_approach")
            };

            return MakeNode("man_otkuda", "narrator", blocks, choices);
        }

        private static DialogueNodeData BuildSecondApproachNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_choice", DialogueTextBlockKind.Narration,
                    "Он что-то недоговаривает.")
            };

            List<DialogueChoiceData> choices = new List<DialogueChoiceData>
            {
                MakeNormalChoice("c_help_first", "Помочь ему — перевязать ногу, дать воды.", "confession"),
                MakeActiveChoice(
                    "c_press", "Спросить прямо, что он скрывает.",
                    DialogueChoiceKind.ActiveDecisive,
                    new NarrativeCheckSpec { CheckId = CheckPress, Kind = NarrativeCheckKind.ActiveDecisive, Quality = HeroQuality.Judgment, Difficulty = NarrativeDifficulty.Demanding },
                    "confession", "confession_partial")
            };

            return MakeNode("second_approach", "narrator", blocks, choices);
        }

        private static DialogueNodeData BuildConfessionNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_confess1", DialogueTextBlockKind.CompanionLine,
                    "— Я думал, она больная. — Пауза. — Потом увидел глаза.",
                    speakerOverride: "traveller"),
                MakeTextBlock("b_confess2", DialogueTextBlockKind.CompanionLine,
                    "«Какие глаза?»\n\n— Овечьи. Только смотрела всё время в одну сторону.\n\n«Куда?»\n\n" +
                    "Человек показывает в лес. Не на дорогу. Не туда, откуда пришёл. Просто между двумя " +
                    "деревьями.\n\nТам ничего нет.",
                    speakerOverride: "traveller",
                    onRevealEffects: Effects(Effect("set_direction_known", NarrativeEffectType.SetFlag, FlagDirectionKnown)))
            };

            List<DialogueChoiceData> choices = new List<DialogueChoiceData>
            {
                MakeContinueChoice("c_to_final", "Решить, что делать дальше.", "final_choices")
            };

            return MakeNode("confession", "narrator", blocks, choices);
        }

        private static DialogueNodeData BuildConfessionPartialNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_partial", DialogueTextBlockKind.Observation,
                    "Он замыкается. Что бы ни стояло за этим враньём, силой из него это не вытащить — " +
                    "он просто смотрит в костёр и молчит.")
            };

            List<DialogueChoiceData> choices = new List<DialogueChoiceData>
            {
                MakeNormalChoice("c_help_after_fail", "Всё же помочь ему.", "confession"),
                MakeNormalChoice("c_give_up", "Оставить как есть и решить, что делать.", "final_choices")
            };

            return MakeNode("confession_partial", "narrator", blocks, choices);
        }

        private static DialogueNodeData BuildFinalChoicesNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_final", DialogueTextBlockKind.Narration,
                    "Пора решать, что делать с человеком и с тем, что от овцы осталось.")
            };

            List<DialogueChoiceData> choices = new List<DialogueChoiceData>
            {
                MakeNormalChoice("c_end_help", "Помочь ему добраться до безопасного места.", "ending_helped"),
                MakeNormalChoice("c_end_supply", "Оставить ему часть припасов и уйти.", "ending_supplied"),
                MakeNormalChoice("c_end_meat", "Забрать мясо и уйти.", "ending_meat_taken_from_man"),
                MakeNormalChoice("c_end_leave", "Оставить всё как есть и уйти.", "ending_left_man")
            };

            return MakeNode("final_choices", "narrator", blocks, choices);
        }

        private static DialogueNodeData BuildEndingHelpedNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_end", DialogueTextBlockKind.Narration,
                    "Он держится за плечо героя всю дорогу до ближайшего безопасного места и почти " +
                    "ничего не говорит. У самого края поднимает голову.\n\n— Я запомню.",
                    onRevealEffects: Effects(Effect("set_man_helped", NarrativeEffectType.SetFlag, FlagManHelped)))
            };

            return MakeNode("ending_helped", "narrator", blocks, new List<DialogueChoiceData> { MakeExitChoice("c_exit_helped", "Идти дальше.") });
        }

        private static DialogueNodeData BuildEndingSuppliedNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_end", DialogueTextBlockKind.Narration,
                    "Герой оставляет часть припасов у костра. Человек кивает, не поднимая головы. " +
                    "С такой ногой до жилья он дойдёт нескоро — но, может, дойдёт.",
                    onRevealEffects: Effects(Effect("set_man_supplied", NarrativeEffectType.SetFlag, FlagManSupplied)))
            };

            return MakeNode("ending_supplied", "narrator", blocks, new List<DialogueChoiceData> { MakeExitChoice("c_exit_supplied", "Идти дальше.") });
        }

        private static DialogueNodeData BuildEndingMeatTakenFromManNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_end", DialogueTextBlockKind.Narration,
                    "Герой забирает мясо. Человек не спорит — просто смотрит, как уменьшается то, ради " +
                    "чего он резал тупым ножом и спешил. Он остаётся у костра один.",
                    onRevealEffects: Effects(Effect("set_meat_from_man", NarrativeEffectType.SetFlag, FlagMeatTakenFromMan)))
            };

            return MakeNode("ending_meat_taken_from_man", "narrator", blocks, new List<DialogueChoiceData> { MakeExitChoice("c_exit_meat_man", "Идти дальше.") });
        }

        private static DialogueNodeData BuildEndingLeftManNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_end", DialogueTextBlockKind.Narration,
                    "Герой оставляет его у костра — с ногой, с ножом, с тем немногим мясом, что осталось. " +
                    "Дорога впереди пуста.",
                    onRevealEffects: Effects(Effect("set_left_man", NarrativeEffectType.SetFlag, FlagLeftManUntouched)))
            };

            return MakeNode("ending_left_man", "narrator", blocks, new List<DialogueChoiceData> { MakeExitChoice("c_exit_left_man", "Идти дальше.") });
        }

        // --- прямое разделывание / уход ------------------------------------

        private static DialogueNodeData BuildTakeMeatNowNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_butcher", DialogueTextBlockKind.Narration,
                    "Нож входит тяжело. Шкура ещё держит тепло.\n\nЧерез несколько минут становится ясно, " +
                    "что переднюю половину туши никто не трогал. Для дороги мяса здесь больше, чем можно " +
                    "унести без лишнего веса.\n\nВ шерсти на брюхе застрял длинный сухой лист. Таких возле " +
                    "дороги нет.",
                    onRevealEffects: Effects(
                        Effect("set_meat_raw", NarrativeEffectType.SetFlag, FlagMeatTakenRaw),
                        Effect("set_dry_leaf", NarrativeEffectType.SetFlag, FlagDryLeafClue)))
            };

            return MakeNode("take_meat_now", "narrator", blocks, new List<DialogueChoiceData> { MakeExitChoice("c_exit_meat_now", "Идти дальше.") });
        }

        private static DialogueNodeData BuildLeaveNowNode()
        {
            List<DialogueTextBlockData> blocks = new List<DialogueTextBlockData>
            {
                MakeTextBlock("b_leave", DialogueTextBlockKind.Narration,
                    "Овца остаётся за спиной. Через несколько минут дорога снова становится обычной.\n\n" +
                    "Почти.",
                    onRevealEffects: Effects(Effect("set_left_untouched", NarrativeEffectType.SetFlag, FlagLeftUntouched)))
            };

            return MakeNode("leave_now", "narrator", blocks, new List<DialogueChoiceData> { MakeExitChoice("c_exit_leave", "Идти дальше.") });
        }

        // ---------------------------------------------------------------
        // Низкоуровневые билдеры (тот же паттерн, что и в
        // Tests/EditMode/DialogueDatabaseCheckSystemTests.cs — построение
        // через reflection по приватным [SerializeField]-полям).
        // ---------------------------------------------------------------

        private static NarrativeConditionGroup Conditions(params NarrativeCondition[] conditions)
        {
            return new NarrativeConditionGroup
            {
                Combinator = NarrativeConditionCombinator.All,
                Conditions = new List<NarrativeCondition>(conditions)
            };
        }

        private static NarrativeCondition Cond(
            NarrativeConditionType type,
            string stringParam = null,
            HeroQuality quality = default,
            int intParam = 0,
            bool negate = false)
        {
            return new NarrativeCondition
            {
                Type = type,
                StringParam = stringParam ?? string.Empty,
                QualityParam = quality,
                IntParam = intParam,
                Negate = negate
            };
        }

        private static List<NarrativeEffect> Effects(params NarrativeEffect[] effects)
        {
            return new List<NarrativeEffect>(effects);
        }

        private static NarrativeEffect Effect(string executionIdSuffix, NarrativeEffectType type, string stringParam, int intParam = 0)
        {
            return new NarrativeEffect
            {
                EffectExecutionId = DialogueId + "." + executionIdSuffix,
                Type = type,
                StringParam = stringParam,
                IntParam = intParam
            };
        }

        private static DialogueSpeakerData MakeSpeaker(string id, string displayName)
        {
            DialogueSpeakerData speaker = new DialogueSpeakerData();
            SetField(speaker, "id", id);
            SetField(speaker, "displayName", displayName);
            SetField(speaker, "role", string.Empty);
            return speaker;
        }

        private static DialogueTextBlockData MakeTextBlock(
            string blockId,
            DialogueTextBlockKind kind,
            string text,
            string speakerOverride = null,
            NarrativeConditionGroup conditions = null,
            List<NarrativeEffect> onRevealEffects = null)
        {
            DialogueTextBlockData block = new DialogueTextBlockData();
            SetField(block, "blockId", blockId);
            SetField(block, "kind", kind);
            SetField(block, "speakerIdOverride", speakerOverride ?? string.Empty);
            SetField(block, "text", text);
            SetField(block, "conditions", conditions ?? new NarrativeConditionGroup());
            SetField(block, "hasPassiveCheck", false);
            SetField(block, "passiveCheck", new NarrativeCheckSpec { Kind = NarrativeCheckKind.Passive });
            SetField(block, "onRevealEffects", onRevealEffects ?? new List<NarrativeEffect>());
            return block;
        }

        private static DialogueChoiceData MakeNormalChoice(string choiceId, string text, string nextNodeId)
        {
            DialogueChoiceData choice = new DialogueChoiceData();
            SetField(choice, "text", text);
            SetField(choice, "nextNodeId", nextNodeId ?? string.Empty);
            SetField(choice, "endsDialogue", false);
            SetField(choice, "choiceId", choiceId);
            SetField(choice, "kind", DialogueChoiceKind.Normal);
            SetField(choice, "conditions", new NarrativeConditionGroup());
            SetField(choice, "unavailablePresentation", DialogueChoiceUnavailablePresentation.Hidden);
            SetField(choice, "check", new NarrativeCheckSpec());
            SetField(choice, "successNodeId", string.Empty);
            SetField(choice, "failureNodeId", string.Empty);
            SetField(choice, "successEffects", new List<NarrativeEffect>());
            SetField(choice, "failureEffects", new List<NarrativeEffect>());
            return choice;
        }

        // "Continue" — кнопка "читать дальше" внутри одной сцены, не решение
        // героя (production-правило "одна реплика = один шаг", §15 P06).
        // Эффекты на таком выборе никогда не применяются рантаймом — если
        // нужно поставить флаг при переходе, он должен висеть на blockOnReveal
        // блока целевого узла, а не на самом выборе.
        private static DialogueChoiceData MakeContinueChoice(string choiceId, string text, string nextNodeId)
        {
            DialogueChoiceData choice = MakeNormalChoice(choiceId, text, nextNodeId);
            SetField(choice, "kind", DialogueChoiceKind.Continue);
            return choice;
        }

        private static DialogueChoiceData MakeExitChoice(string choiceId, string text)
        {
            DialogueChoiceData choice = MakeNormalChoice(choiceId, text, null);
            SetField(choice, "endsDialogue", true);
            SetField(choice, "kind", DialogueChoiceKind.Exit);
            return choice;
        }

        private static DialogueChoiceData MakeActiveChoice(
            string choiceId,
            string text,
            DialogueChoiceKind kind,
            NarrativeCheckSpec check,
            string successNodeId,
            string failureNodeId,
            List<NarrativeEffect> successEffects = null,
            List<NarrativeEffect> failureEffects = null)
        {
            DialogueChoiceData choice = new DialogueChoiceData();
            SetField(choice, "text", text);
            SetField(choice, "nextNodeId", string.Empty);
            SetField(choice, "endsDialogue", false);
            SetField(choice, "choiceId", choiceId);
            SetField(choice, "kind", kind);
            SetField(choice, "conditions", new NarrativeConditionGroup());
            SetField(choice, "unavailablePresentation", DialogueChoiceUnavailablePresentation.DisabledWithHint);
            SetField(choice, "check", check);
            SetField(choice, "successNodeId", successNodeId);
            SetField(choice, "failureNodeId", failureNodeId);
            SetField(choice, "successEffects", successEffects ?? new List<NarrativeEffect>());
            SetField(choice, "failureEffects", failureEffects ?? new List<NarrativeEffect>());
            return choice;
        }

        private static DialogueNodeData MakeNode(
            string id,
            string speakerId,
            List<DialogueTextBlockData> textBlocks,
            List<DialogueChoiceData> choices)
        {
            DialogueNodeData node = new DialogueNodeData();
            SetField(node, "id", id);
            SetField(node, "speakerId", speakerId);
            SetField(node, "text", string.Empty);
            SetField(node, "textBlocks", textBlocks);
            SetField(node, "choices", choices);
            SetField(node, "editorPosition", Vector2.zero);
            SetField(node, "hasEditorPosition", false);
            return node;
        }

        private static DialogueDefinitionData MakeDialogue(string id, string startNodeId, List<DialogueNodeData> nodes)
        {
            DialogueDefinitionData dialogue = new DialogueDefinitionData();
            SetField(dialogue, "id", id);
            SetField(dialogue, "title", "Тёплая овца");
            SetField(dialogue, "category", DialogueCategory.RandomEncounter);
            SetField(dialogue, "status", DialogueProductionStatus.Working);
            SetField(dialogue, "developerComment",
                "[РАБОЧЕЕ] Сценарная версия пользователя от 2026-09-12. Причина смерти овцы намеренно не " +
                "раскрыта — см. FutureHooksNotes на EncounterDefinition ROAD_WARM_SHEEP_01.");
            SetField(dialogue, "startNodeId", startNodeId);
            SetField(dialogue, "tags", new List<string> { "road", "warm_sheep" });
            SetField(dialogue, "nodes", nodes);
            SetField(dialogue, "schemaVersion", 1);
            return dialogue;
        }

        // ---------------------------------------------------------------
        // Общая инфраструктура reflection/asset io.
        // ---------------------------------------------------------------

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new InvalidOperationException("Не найдено приватное поле " + target.GetType().Name + "." + fieldName);
            field.SetValue(target, value);
        }

        private static List<T> GetOrCreateList<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null)
                throw new InvalidOperationException("Не найдено приватное поле " + target.GetType().Name + "." + fieldName);

            List<T> list = field.GetValue(target) as List<T>;
            if (list == null)
            {
                list = new List<T>();
                field.SetValue(target, list);
            }

            return list;
        }

        private static void Upsert<T>(List<T> list, Func<T, string> idSelector, T item)
        {
            string id = idSelector(item);
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(idSelector(list[i]), id, StringComparison.Ordinal))
                {
                    list[i] = item;
                    return;
                }
            }

            list.Add(item);
        }

        private static T LoadOrCreate<T>(string assetPath) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
                return asset;

            EnsureFolderExists(EncountersFolder);
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static void EnsureFolderExists(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string parent = "Assets/_Project/Encounters";
            if (!AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder("Assets/_Project", "Encounters");

            AssetDatabase.CreateFolder(parent, "Resources");
            AssetDatabase.CreateFolder(parent + "/Resources", "Encounters");
        }
    }
}
