using K = KingdomSurvival.BattleSandbox.SandboxPassiveKind;
using C = KingdomSurvival.BattleSandbox.SandboxPassiveCondition;
using L = KingdomSurvival.BattleSandbox.SandboxPassiveLimit;

namespace KingdomSurvival.BattleSandbox
{
    // Нормализация текущих текстов тегов (ProjectDocs/CombatStatusPack/Data/
    // passive_tags_effective.json) и реестра решений пакета в правила.
    // Включены только неудалённые боевые теги; что не попало сюда —
    // ProjectDocs/COMBAT_STATUS_IMPLEMENTATION.md §7 с причиной.
    public static partial class SandboxPassiveCatalog
    {
        static SandboxPassiveCatalog()
        {
            const string Poison = SandboxStatusCatalog.Poison;

            // ---------- Атака по условиям ----------
            T("tactic.pack_hunter", "Стая").Alias(SandboxCombatTagRules.Pack);
            T("tactic.isolation_hunter", "Охотник").R(K.AttackMod, 1, C.Melee, when2: C.TargetIsolated);
            T("tactic.wounded_hunter", "Чует слабину").R(K.AttackMod, 1, C.Melee, when2: C.TargetHpHalf);
            T("tactic.last_ally_standing", "Загнанный одиночка").R(K.AttackMod, 2, C.Melee, when2: C.NoLivingAllies);
            T("passive.20261119.002", "Дух ополчения").R(K.AttackMod, 1, C.AlliesWithin2AtLeast3);
            T("passive.20261119.021", "Подавление").R(K.AttackMod, 2, C.TargetMaxHpHalfOfMine);
            T("passive.20261119.043", "Охотник").R(K.AttackMod, 2, C.TargetTag, SandboxCombatTagRules.Beast);
            T("passive.20261119.062", "Запоминает противника").R(K.AttackMod, 2, C.RememberedAttacker);
            T("passive.20261119.066", "Безумие потерь").R(K.AttackMod, 2, C.SelfHpHalf).R(K.DefenseMod, -2, C.SelfHpHalf);
            T("passive.20261119.073", "Добивает раненых").R(K.AttackMod, 2, C.TargetHp30);
            T("passive.20261119.077", "Работает в окружении").R(K.AttackMod, 2, C.TwoOtherAlliesNearTarget);
            T("passive.20261119.084", "Воин-одиночка").R(K.AttackMod, 2, C.NoAlliesWithin2).R(K.DefenseMod, 2, C.NoAlliesWithin2);
            T("passive.20261119.095", "На последнем дыхании").R(K.AttackMod, 3, C.Melee, when2: C.SelfHp20);
            T("passive.20261119.096", "Мститель").R(K.AttackMod, 2, C.AvengedKiller);
            T("passive.20261119.180", "Силен неповреждённым").R(K.OutgoingFlat, 2, C.SelfFullHp);
            T("passive.20261119.181", "Отчаянно увёртлив").R(K.DefenseMod, 2, C.SelfHpQuarter);
            T("passive.20261119.183", "Горяч на старте").R(K.AttackMod, 2, C.FirstRound);
            T("passive.20261119.196", "Дрожит перед зверем").R(K.AttackMod, -1, C.TargetTag, SandboxCombatTagRules.Beast);
            T("passive.20261119.197", "Растерян в первом раунде").R(K.InitiativeMod, -2, C.FirstRound).R(K.DefenseMod, -1, C.FirstRound);
            T("passive.20261119b.047", "Подступ к открытому врагу").R(K.AttackMod, 1, C.FirstAttackOnTarget);
            T("passive.20261119.099", "Ищет стык доспеха").R(K.IgnoreDefense, 1, C.TargetArmored);
            T("passive.20261119b.065", "Кованый наконечник").R(K.IgnoreDefense, 1, C.Ranged);
            T("passive.20261119.107", "Скрытый фланг").R(K.OutgoingFlat, 2, C.Melee, when2: C.AllyOppositeTarget);
            T("passive.status.20261120.026", "Охотится на паникующего")
                .R(K.AttackMod, 1, C.TargetHasStatus, limit: L.PerOwnTurn, statuses: new[] { "status.056", "status.057" });
            T("passive.status.20261120.015", "Корни держат добычу").R(K.OutgoingFlat, 1, C.TargetHeldBySelfVine, limit: L.PerOwnTurn);

            // ---------- Защита по условиям ----------
            T("trait.exposed_flanks", "Открытые бока").R(K.DefenseMod, -2, C.TwoEnemiesAdjacentToSelf);
            T("trait.ranged_resistant", "Прикрыт от обстрела").R(K.DefenseMod, 2, C.Ranged, when2: C.NotArmoredSelf);
            T("passive.20261119.003", "Крепкая кольчуга").R(K.IncomingPercent, 20);
            T("passive.20261119.027", "Пустоты между костями").R(K.IncomingPercent, 50, C.Ranged);
            T("passive.20261119.035", "Сквозистое тело").R(K.IncomingPercent, 30);
            T("passive.20261119.244", "Сопротивляется оружию").R(K.IncomingFlat, -1);
            T("passive.20261119.061", "Учится держать удар").R(K.StackDefensePerHitTaken, 1, value2: 3);
            T("passive.20261119.065", "Привыкает к боли").R(K.StackReductionPerDamageTaken, 1, value2: 3);
            T("passive.20261119.206", "Первое парирование").R(K.FirstMeleeHitPerRoundDefense, 2);
            T("passive.20261119.261", "Маскируется в движении").R(K.IncomingAttackMod, -3, C.Ranged, when2: C.NotAttackedSinceMove);
            T("passive.20261119.010", "Сбивающий облик")
                .R(K.IncomingAttackMod, -2, C.Melee, when2: C.AttackerTag, param: SandboxCombatTagRules.Human, limit: L.PerTargetPerBattle);
            T("passive.20261119.199", "Хозяин холмов").R(K.DefenseMod, 1, C.SelfOnHill);
            T("passive.20261119.200", "Болотная стойка").R(K.DefenseMod, 1, C.SelfOnShallowWater);

            // ---------- Ауры (семья не складывается сама с собой) ----------
            T("passive.20261119.007", "Строевой наставник").R(K.AuraAllyAttack, 1);
            T("passive.20261119.042", "Гнетущее присутствие").R(K.AuraEnemyInitiative, -1);
            T("passive.20261119.060", "Вожак ловчих").R(K.AuraAllyMovementStart, 1, C.TargetTag, SandboxCombatTagRules.Pack);
            T("passive.20261119.236", "Мёртвая тревога").R(K.AuraEnemyDefense, -1, statuses: new[] { SandboxStatusCatalog.Fear });
            T("passive.20261119.242", "Зловонная близость").R(K.AuraEnemyAttack, -1);
            T("passive.status.20261120.019", "Не подпускает палача").R(K.AuraAllyGuardStrongControlled, 1, limit: L.PerRound,
                statuses: new[] { "status.025", "status.028", "status.032", SandboxStatusCatalog.Frozen });

            // ---------- Ответные удары ----------
            T("passive.20261119.019", "Неиссякаемый ответ").R(K.Retaliations, 3);
            T("passive.20261119.050", "Безответный удар").R(K.NoProvokedRetaliation);
            T("tactic.slow_turner", "Долго разворачивается").R(K.NoMeleeRetaliationAfterMove);
            T("tactic.shot_exhausted", "Открывается после выстрела").R(K.NoMeleeRetaliationAfterShot);

            // ---------- Невосприимчивость и сроки состояний ----------
            T("trait.bloodless", "Без кровотока").R(K.StatusImmunity, status: SandboxStatusCatalog.Bleeding);
            T("resist.biological_poison", "Иммунитет к яду").R(K.StatusImmunity, status: Poison);
            T("resist.fearless", "Не поддаётся страху").R(K.StatusImmunity, status: SandboxStatusCatalog.Fear);
            T("passive.20261119.025", "Не загорается").R(K.StatusImmunity, status: SandboxStatusCatalog.Burning);
            T("passive.20261119.046", "Несгибаемый разум").R(K.StatusImmunity, statuses: new[] { "status.060", "status.061" });
            T("resist.short_stun", "Быстро приходит в себя").R(K.Policy);
            T("passive.20261119.076", "Стойкий к состояниям").R(K.StatusMaxDuration, 1, statuses: new[] { Poison, SandboxStatusCatalog.Bleeding, "status.071" });
            T("passive.20261119.177", "Кровь быстро свёртывается").R(K.StatusDurationDelta, -1, limit: L.PerBattle, status: SandboxStatusCatalog.Bleeding);
            T("passive.20261119.178", "Плохо сворачивается кровь").R(K.StatusDurationDelta, 1, status: SandboxStatusCatalog.Bleeding, value2: 3);
            T("passive.status.20261120.022", "Не спит стоя").R(K.StatusCancelOnce, status: "status.030", limit: L.PerBattle);
            T("passive.status.20261120.029", "Не ведётся на вызов").R(K.StatusCancelOnce, statuses: new[] { "status.065", "status.106" }, limit: L.PerBattle);

            // ---------- Наложение состояний попаданием ----------
            T("passive.20261119.052", "Ядовитая атака")
                .R(K.OnHitApplyStatus, status: Poison, limit: L.PerOwnTurn, profile: P("venom_attack", duration: 2, tick: 1, refresh: true));
            T("passive.20261119.149", "Смертельная отрава")
                .R(K.OnHitApplyStatus, status: Poison, limit: L.PerTargetPerBattle, when: C.TargetLiving, profile: P("deadly_venom", duration: 2, tick: 2));
            T("candidate.ks.0292", "Опасность первого укуса").R(K.OnHitApplyStatus, status: Poison, when: C.Melee, limit: L.PerBattle);
            T("passive.20261119.009", "Удар с оцепенением")
                .R(K.OnHitApplyStatus, status: "status.041", limit: L.EveryNth, n: 3, profile: P("short_slow", duration: 1));
            T("passive.20261119.012", "Изнуряющая пыль")
                .R(K.OnHitApplyStatus, status: "status.071", limit: L.PerRound, profile: P("dust", duration: 1, refresh: true));
            T("passive.20261119.068", "Меткое ослабление")
                .R(K.OnHitApplyStatus, status: "status.019", when: C.Ranged, limit: L.EveryNth, n: 3, profile: P("armor_weakening_impact", duration: 2));
            T("passive.20261119.070", "Рвущие удары")
                .R(K.OnHitApplyStatus, status: "status.185", when: C.DamageAtLeastFifthOfTargetMax, limit: L.PerOwnTurn, profile: P("temporary_leg", duration: 2));
            T("passive.20261119.202", "Тяжёлые снаряды").R(K.OnHitApplyStatus, status: "status.019", when: C.Ranged, profile: P("armor_weakening_impact", duration: 2));
            T("passive.20261119.219", "Замедляющий удар")
                .R(K.OnHitApplyStatus, status: "status.041", limit: L.PerRound, profile: P("half_speed", duration: 1, mode: "half_speed"));
            T("passive.20261119.230", "Старящий удар")
                .R(K.OnHitApplyStatus, status: "status.077", when: C.TargetLiving, limit: L.PerTargetPerBattle, profile: P("aging", battle: true, mode: "aging"));
            T("passive.20261119.232", "Разъедающая слюна")
                .R(K.OnHitApplyStatus, status: "status.019", limit: L.EveryNth, n: 3, profile: P("corrosive_spit", value: -2, battle: true));
            T("passive.20261119.083", "Подавляет темпом")
                .R(K.OnHitApplyStatus, status: "status.071", when: C.TargetNotActedThisRound, profile: P("overwhelm", duration: 1));
            T("passive.status.20261120.001", "Едкая смесь")
                .R(K.OnHitApplyStatus, status: "status.019", when: C.TargetHasStatus, limit: L.PerOwnTurn, statuses: new[] { "status.019", Poison });
            T("passive.status.20261120.009", "Смолистый плевок").R(K.OnHitApplyStatus, status: SandboxStatusCatalog.Tarred, when: C.Ranged, limit: L.PerBattle);
            T("trait.venomous_flesh", "Ядовитая плоть")
                .R(K.OnHitTakenApplyStatusToAttacker, status: Poison, when: C.Melee, limit: L.PerRound, profile: P("venomous_flesh", duration: 2, tick: 1));
            T("body.sticky", "Липкая поверхность")
                .R(K.OnHitTakenApplyStatusToAttacker, status: "status.047", when: C.Melee, profile: P("sticky_exit", duration: 1, mode: "sticky_exit"));

            // ---------- Попадание задевает соседей, лечит ----------
            T("passive.20261119.016", "Соседний всплеск").R(K.OnHitSplashAdjacentToTarget, 1, C.Ranged);
            T("passive.20261119.022", "Три пасти").R(K.OnHitSplashFlank, 2, C.Melee);
            T("passive.20261119.041", "Бьёт через строй").R(K.OnHitSplashBehindTarget, 2);
            T("passive.20261119.031", "Пьёт жизненную силу").R(K.OnHitHealSelf, 3, C.TargetLiving, limit: L.PerOwnTurn);

            // ---------- Накопления ----------
            T("passive.20261119.033", "Копит ярость ударов").R(K.StackOutgoingPerAttack, 1, value2: 3);
            T("passive.20261119.069", "Пристреливается").R(K.StackAttackAfterMiss, 2, value2: 6);
            T("passive.20261119.037", "Звереет от боли").R(K.ChargeAttackAfterDamageTaken, 3, value2: 2);
            T("passive.20261119.158", "Считает павших").R(K.StackAttackAfterAlliesFell, 2, value2: 2);

            // ---------- Убийство, смерть, спасение ----------
            T("passive.20261119.092", "Вкус победы").R(K.OnKillOutgoingNextTurn, 2);
            T("passive.20261119.211", "Затаптывает слабого").R(K.OnKillStepIntoCell, 0, C.Melee);
            T("passive.20261119.225", "Питается победой").R(K.OnKillMaxHitPoints, 2, C.TargetLiving, value2: 6);
            T("passive.20261119b.057", "Победа обращает в страх").R(K.OnKillApplyStatusNearestEnemy, 2, status: SandboxStatusCatalog.Fear, limit: L.PerRound);
            T("passive.20261119.256", "Живучий").R(K.CheatDeathOnce);
            T("passive.20261119.071", "Девять жизней").R(K.CheatDeathOnce, statuses: new[] { Poison, SandboxStatusCatalog.Bleeding });
            T("passive.20261119.267", "Второе дыхание").R(K.BelowHalfOnce, 3, value2: 1);
            T("passive.20261119.029", "Чумной распад").R(K.OnDeathApplyStatus, status: Poison, profile: P("rot", duration: 2, tick: 1));
            T("passive.20261119.164", "Последний взрыв").R(K.OnDeathDamage, 2);
            T("passive.20261119.257", "Разлетается осколками").R(K.OnDeathDamage, 1);
            T("passive.20261119.166", "Последний дар защиты").R(K.OnDeathDefenseToAlly, 2, value2: 2);
            T("passive.20261119.167", "Последний целебный выдох").R(K.OnDeathHealAllies, 2);
            T("passive.20261119.168", "Передаёт натиск").R(K.OnDeathAttackToAlly, 2);

            // ---------- Начало и конец хода ----------
            T("candidate.ks.0129", "Регенерация").R(K.ActivationStartHeal, 1);
            T("passive.20261119b.038", "Вода затягивает раны").R(K.ActivationStartHeal, 1, C.SelfOnShallowWater);
            T("candidate.ks.0282", "Лечение по очереди").R(K.ActivationStartHealAlly, 1);
            T("passive.20261119.138", "Сосуд живучести").R(K.ActivationEndHealPercent, 10);
            T("trait.rest_knitter", "Срастается в покое").R(K.ActivationEndRestHeal, 1);
            T("passive.20261119.038", "Боевая спячка").R(K.ActivationEndRestSleep, 2);
            T("passive.status.20261120.033", "Чистит заразу отдыхом").R(K.ActivationEndRestCleanse, limit: L.PerBattle, statuses: new[] { "status.078", "status.070" });
            T("passive.status.20261120.012", "Тепло под кольчугой").R(K.ActivationStartStatusDecay, statuses: new[] { SandboxStatusCatalog.Frost, "status.014" });
            T("candidate.ks.0113", "Рваный темп").R(K.MovementBonusAfterNoAttack, 1);

            // ---------- Движение ----------
            T("body.spiny", "Колючий").R(K.EnterAdjacentDamage, 1);
            T("tactic.zone_holder", "Мешает пройти").R(K.LeaveAdjacentCost, 1);
            T("trait.anchored", "Крепкая опора").R(K.PushReduction, 1);
            T("trait.height_adapted", "Цепкий").Alias(SandboxTerrainTags.Highlander);
            T("movement.wetland_walker", "Ходит по жиже").Alias(SandboxTerrainTags.SwampWalker);
            T("passive.20261119.247", "Не вязнет в паутине").Alias(SandboxTerrainTags.SpiderStep);

            RegisterStatusReactions();
            RegisterCapabilities();
        }
    }
}
