using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Нормализованный каталог 89 состояний пакета 20.11.2026
    // (ProjectDocs/CombatStatusPack/Data/combat_statuses.json): ID и имена —
    // точно из источника, числа — из полей balance.primary/duration/stack/
    // cleanse, переведённые в операции общих семейств. Вторичные варианты
    // карточек — профили, по умолчанию не включены. Соответствие проверяет
    // EditMode-валидатор StatusCatalogDataTests.
    public static class SandboxStatusCatalog
    {
        private static readonly Dictionary<string, SandboxStatusDefinition> byId = new Dictionary<string, SandboxStatusDefinition>(StringComparer.Ordinal);
        private static readonly List<SandboxStatusDefinition> all = new List<SandboxStatusDefinition>();

        public static IReadOnlyList<SandboxStatusDefinition> All => all;

        public static SandboxStatusDefinition Get(string id)
        {
            return id != null && byId.TryGetValue(id, out SandboxStatusDefinition def) ? def : null;
        }

        // Ключевые ID, на которые опирается код боя.
        public const string Poison = "status.001";
        public const string Bleeding = "status.002";
        public const string Burning = "status.003";
        public const string Parasite = "status.008";
        public const string Tarred = "status.018";
        public const string Rooted = "status.023";
        public const string Stunned = "status.025";
        public const string Dazed = "status.026";
        public const string Numbed = "status.027";
        public const string Frozen = "status.033";
        public const string Frost = "status.015";
        public const string Grappled = "status.044";
        public const string Fear = "status.055";
        public const string Demoralized = "status.066";
        public const string SharedWounds = "status.109";
        public const string Guard = "status.130";
        public const string Invulnerable = "status.142";
        public const string Momentum = "status.161";
        public const string Invisible = "status.171";
        public const string BadLuck = "status.188";
        public const string GoodLuck = "status.189";
        public const string Inspired = "status.190";
        public const string ExtraSupport = "status.159";
        public const string Bloodlust = "status.119";

        private const SandboxStatusCleanse SA = SandboxStatusCleanse.Self | SandboxStatusCleanse.Ally;
        private const SandboxStatusCleanse A = SandboxStatusCleanse.Ally;
        private const SandboxStatusCleanse S = SandboxStatusCleanse.Self;

        static SandboxStatusCatalog()
        {
            // ---------- Урон со временем и отсроченные угрозы ----------
            Def("status.001", "Отравление", "dot", Bad()).Own(3).Op(Tick(1, SandboxDamageType.Poison)).Clean(SA, "очистить");
            Def("status.002", "Кровотечение", "dot", Bad()).Own(2).Charges(2).Op(Tick(1)).Clean(SA, "перевязать");
            Def("status.003", "Горение", "dot", Bad()).Own(2).Op(Tick(1, SandboxDamageType.Fire)).Clean(S, "потушиться");
            Def("status.006", "Тлеющее проклятие", "dot", Bad()).Own(2).Op(Tick(1, SandboxDamageType.Supernatural)).Clean(SA, "снять порчу").Magic();
            Def("status.007", "Мучительная связь", "dot", Bad()).Source(2).Op(Special("DrainLink", 1)).Clean(SA, "порвать связь").SourceBound().Magic();
            Def("status.008", "Паразит на теле", "dot", Bad()).Facade().Op(Special("AttachLink"));
            Def("status.009", "Ужас, нарастающий со временем", "dot", Bad()).Own(3).Op(Special("GrowingDread", -1)).Clean(SA, "ободрить").SourceBound();
            Def("status.010", "Отложенный взрыв", "dot", Bad()).Rounds(2).Op(Special("DelayedBurst", 3)).Clean(SA, "снять заряд");
            Def("status.011", "Болевое эхо", "dot", Bad()).Own(2).Op(Special("PainEcho", 2)).Clean(SA, "очистить").Magic();
            Def("status.012", "Кровавая порча", "dot", Bad()).Own(1).Op(Op(SandboxStatusOpKind.NoVoluntaryMove)).Clean(SA, "освободиться").Magic();
            Def("status.203", "Зародыш в теле", "dot", Bad()).Own(2).Op(Tick(1)).Op(Special("IncubationCocoon"))
                .Clean(SA, "очистить заражение")
                .Pending("Кокон при гибели носителя: нужен объект-кокон 3 HP и вылупление выводка (временная заготовка 3/1/0/1/2/0/1) — тики работают, кокон не создаётся.");

            // ---------- Стихии, вещества и наложения ----------
            Def("status.014", "Озябший", "coat", Bad()).Own(1).Op(Mod(SandboxStatusOpKind.MovementMod, -1)).Clean(SA, "согреться");
            Def("status.015", "Покрыт инеем", "coat", Bad()).Own(2).Charges(3).Op(Mod(SandboxStatusOpKind.MovementMod, -1, minCharges: 2)).Op(Special("FrostToFreeze", 3)).Clean(SA, "согреться");
            Def("status.018", "Облит смолой", "coat", Bad()).Own(2).Uses(1).Op(new SandboxStatusOp(SandboxStatusOpKind.IncomingFlat, 1, SandboxStatusFilter.Fire, consumesUse: true)).Clean(SA, "смыть смолу");
            Def("status.019", "Кислотное разъедание брони", "coat", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.DefenseMod, -1)).Clean(SA, "очистить");
            Def("status.022", "Покрыт паутиной", "coat", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.MovementMod, -1)).Clean(SA, "сорвать паутину");
            Def("status.023", "Опутан корнями", "coat", Bad()).Facade().Op(Special("VineLink"));

            // ---------- Ограничение действий ----------
            Def("status.025", "Оглушение", "control", Bad()).Own(1).Strong().Op(Op(SandboxStatusOpKind.SkipActivation)).Op(Op(SandboxStatusOpKind.NoRetaliation)).Clean(A, "привести в чувство");
            Def("status.026", "Ошеломление", "control", Bad()).Own(1).Op(Op(SandboxStatusOpKind.NoRetaliation)).Op(Mod(SandboxStatusOpKind.MovementMod, -1)).Clean(SA, "прийти в себя");
            Def("status.027", "Оцепенение", "control", Bad()).Own(1).Op(Op(SandboxStatusOpKind.NoVoluntaryMove)).Op(Op(SandboxStatusOpKind.NoRetaliation)).Clean(SA, "снять оцепенение");
            Def("status.028", "Паралич", "control", Bad()).Own(1).Strong().Op(Op(SandboxStatusOpKind.SkipActivation)).Op(Op(SandboxStatusOpKind.NoRetaliation)).Clean(A, "снять паралич");
            Def("status.030", "Сон", "control", Bad()).Own(1).Strong().Op(Op(SandboxStatusOpKind.SkipActivation)).Op(Op(SandboxStatusOpKind.NoRetaliation)).Op(Op(SandboxStatusOpKind.RemoveOnHpDamage)).Clean(A, "разбудить").Magic();
            Def("status.032", "Окаменение", "control", Bad()).Own(1).Strong().Op(Op(SandboxStatusOpKind.SkipActivation)).Op(Op(SandboxStatusOpKind.NoRetaliation))
                .Op(new SandboxStatusOp(SandboxStatusOpKind.DefenseMod, 2, SandboxStatusFilter.Physical)).Clean(A, "развеять окаменение").Magic();
            Def("status.033", "Заморожен", "control", Bad()).Own(1).Strong().Op(Op(SandboxStatusOpKind.SkipActivation)).Op(Op(SandboxStatusOpKind.NoRetaliation)).Op(Op(SandboxStatusOpKind.RemoveOnDirectHpHit)).Clean(A, "освободить ото льда");

            // ---------- Движение и удержание ----------
            Def("status.041", "Замедление", "move", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.MovementMod, -1)).Clean(SA, "снять путы");
            Def("status.043", "Обездвиживание", "move", Bad()).Own(1).Op(Op(SandboxStatusOpKind.NoVoluntaryMove)).Clean(SA, "освободиться");
            Def("status.044", "Захват", "move", Bad()).Facade().Op(Special("EngulfLink"));
            Def("status.046", "В сети", "move", Bad()).Own(2).Op(Op(SandboxStatusOpKind.NoVoluntaryMove)).Clean(SA, "снять сеть");
            Def("status.047", "Запутан", "move", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.MovementMod, -1)).Clean(SA, "распутаться");
            Def("status.054", "Окружён", "move", Bad()).Facade().Op(Special("Surrounded", 1))
                .Pending("Пересчитываемая расстановка: +1 цена первого выхода из соседства 2 врагов без соседнего союзника — нужен учёт в цене пути BuildReachable.");
            Def("status.194", "Ранящее перемещение", "move", Bad()).Own(2).Op(Special("PainfulMovement", 2)).Clean(SA, "перевязать");

            // ---------- Страх, воля и управление ----------
            Def("status.055", "Страх", "mind", Bad()).Own(2).Op(new SandboxStatusOp(SandboxStatusOpKind.AttackMod, -1, SandboxStatusFilter.WhileSourceNear))
                .Op(Op(SandboxStatusOpKind.CannotApproachSource)).Clean(SA, "ободрить").SourceBound();
            Def("status.056", "Паника", "mind", Bad()).Own(1).Strong().Op(Special("FleeFromSource", 2)).Op(Op(SandboxStatusOpKind.NoOffense)).Clean(A, "ободрить").SourceBound();
            Def("status.057", "Ужас", "mind", Bad()).Own(1).Op(Op(SandboxStatusOpKind.CannotTargetSource)).Clean(SA, "ободрить").SourceBound();
            Def("status.058", "Скованный страхом", "mind", Bad()).Own(1).Strong().Op(Op(SandboxStatusOpKind.NoVoluntaryMove)).Op(Op(SandboxStatusOpKind.NoOffense)).Clean(A, "ободрить").SourceBound();
            Def("status.059", "Смятение", "mind", Bad()).Own(1).Strong().Op(Special("Confusion")).Op(Op(SandboxStatusOpKind.RemoveOnDamageFromSourceSide)).Clean(A, "привести в чувство")
                .Pending("Принудительная атака ближайшей живой цели любой стороны: нужен исполнитель хода вне выбора игрока/ИИ и разрешение атаки по своим.");
            Def("status.060", "Очарование", "mind", Bad()).Own(2).Op(Op(SandboxStatusOpKind.CannotTargetSource)).Op(Op(SandboxStatusOpKind.RemoveOnDamageFromSourceSide)).Clean(SA, "очистить").SourceBound().Magic();
            Def("status.061", "Подчинение", "mind", Bad()).Own(1).Strong().Op(Special("Domination")).Op(Op(SandboxStatusOpKind.RemoveOnDamageFromSourceSide)).Clean(A, "очистить").SourceBound().Magic()
                .Pending("Передача управления на 1 активацию стороне источника без смены принадлежности/наград: нужен режим управления в ходе ИИ и запрет самоубийственных действий.");
            Def("status.065", "Спровоцирован", "mind", Bad()).Own(1).Op(Op(SandboxStatusOpKind.MustTargetSource)).Clean(SA, "очистить").SourceBound();
            Def("status.066", "Деморализация", "mind", Bad()).Own(1).Op(new SandboxStatusOp(SandboxStatusOpKind.AttackMod, -2, SandboxStatusFilter.Ranged)).Clean(SA, "ободрить");
            Def("status.067", "Сломленный дух", "mind", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.AttackMod, -1)).Clean(SA, "ободрить");
            Def("status.068", "Отчаяние", "mind", Bad()).Own(2).Op(Op(SandboxStatusOpKind.NoPositiveAttack)).Clean(SA, "ободрить");

            // ---------- Ослабление характеристик и приёмов ----------
            Def("status.070", "Чума", "weak", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.AttackMod, -1)).Clean(SA, "очистить болезнь");
            Def("status.071", "Слабость", "weak", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.AttackMod, -1)).Clean(SA, "снять ослабление");
            Def("status.072", "Обессиленный", "weak", Bad()).Own(2).Op(new SandboxStatusOp(SandboxStatusOpKind.BaseDamageMod, -1, SandboxStatusFilter.Melee)).Clean(SA, "снять ослабление");
            Def("status.077", "Высасывание жизненной силы", "weak", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.MaxHitPointsMod, -2)).Clean(SA, "снять порчу").Magic();
            Def("status.078", "Дурнота", "weak", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.AttackMod, -1)).Clean(SA, "собраться");
            Def("status.079", "Тошнота", "weak", Bad()).Own(1).Strong().Op(Op(SandboxStatusOpKind.NoOffense)).Clean(SA, "собраться");
            Def("status.084", "Забывчивость стрелка", "weak", Bad()).Own(1).Op(Op(SandboxStatusOpKind.NoRangedAttack)).Clean(SA, "снять воздействие").Magic();
            Def("status.087", "Отяжеление", "weak", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.InitiativeMod, -2)).Clean(SA, "очистить");
            Def("status.088", "Ослаблен ударом", "weak", Bad()).Own(1).Op(Mod(SandboxStatusOpKind.OutgoingFlat, -1)).Clean(SA, "помочь");

            // ---------- Открытая защита и уязвимости ----------
            Def("status.098", "Роковая метка", "vuln", Bad()).Own(2).Uses(2).Op(new SandboxStatusOp(SandboxStatusOpKind.IncomingFlat, 1, consumesUse: true)).Clean(SA, "снять порчу").Magic();
            Def("status.099", "Проклятие слабого удара", "vuln", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.BaseDamageMod, -1)).Clean(SA, "снять порчу").Magic();
            Def("status.193", "Рваная рана", "vuln", Bad()).Own(2).Op(new SandboxStatusOp(SandboxStatusOpKind.IncomingFlat, 1, SandboxStatusFilter.FirstBetweenOwnTurns)).Clean(SA, "перевязать");

            // ---------- Метки и связи ----------
            Def("status.101", "Метка добычи", "mark", Bad()).Source(2).Op(new SandboxStatusOp(SandboxStatusOpKind.IncomingFlat, 1, SandboxStatusFilter.SourceOncePerSourceTurn)).Clean(SA, "очистить").SourceBound();
            Def("status.102", "Отмеченная цель", "mark", Bad()).Own(2).Uses(2).Op(new SandboxStatusOp(SandboxStatusOpKind.IncomingAttackMod, 1, consumesUse: true)).Clean(SA, "очистить");
            Def("status.105", "Приговор", "mark", Bad()).Rounds(2).Op(Special("DelayedSentence", 3)).Clean(SA, "очистить").SourceBound().Magic();
            Def("status.106", "Вызов на поединок", "mark", Bad()).Source(2).Op(new SandboxStatusOp(SandboxStatusOpKind.AttackMod, 1, SandboxStatusFilter.VsSource)).Op(Special("DuelMirror", 1)).Clean(SA, "очистить связь").SourceBound();
            Def("status.107", "Под защитой союзника", "mark", Good()).Own(2).Uses(1).Op(Special("Bodyguard")).SourceBound()
                .Pending("Перехват направленной атаки соседним защитником с расчётом по его Защите: нужна подмена цели в TryAttack/способностях до формулы.");
            Def("status.109", "Связанные раны", "mark", Good()).Own(2).Op(Special("SharedWounds")).Clean(SA, "разорвать связь").SourceBound();
            Def("status.110", "Привязка к источнику", "mark", Bad()).Facade().Op(Special("SourceBinding"));

            // ---------- Временное усиление нападения ----------
            Def("status.113", "Свирепость", "attack", Good()).Own(2).Op(new SandboxStatusOp(SandboxStatusOpKind.AttackMod, 2, SandboxStatusFilter.Melee));
            Def("status.114", "Временный вампиризм", "attack", Good()).Own(2).Op(Mod(SandboxStatusOpKind.Vampirism, 2));
            Def("status.117", "Кровожадность", "attack", Good()).Own(2).Op(new SandboxStatusOp(SandboxStatusOpKind.AttackMod, 1, SandboxStatusFilter.Melee));
            Def("status.119", "Боевой подъём", "attack", Good()).Own(1).Uses(1).Op(new SandboxStatusOp(SandboxStatusOpKind.OutgoingFlat, 1, consumesUse: true));
            Def("status.199", "Сила от ран", "attack", Good()).Own(3).Op(new SandboxStatusOp(SandboxStatusOpKind.AttackMod, 1, SandboxStatusFilter.WoundThreshold));

            // ---------- Временная защита ----------
            Def("status.130", "Защитная стойка", "defense", Good()).Facade().Op(Special("GuardFacade"));
            Def("status.132", "Щит одного попадания", "defense", Good()).Own(2).Uses(1).Op(new SandboxStatusOp(SandboxStatusOpKind.AbsorbHit, consumesUse: true));
            Def("status.135", "Каменная кожа", "defense", Good()).Own(2).Op(new SandboxStatusOp(SandboxStatusOpKind.IncomingFlat, -1, SandboxStatusFilter.Physical));
            Def("status.136", "Защита от стрел", "defense", Good()).Own(2).Op(new SandboxStatusOp(SandboxStatusOpKind.IncomingFlat, -1, SandboxStatusFilter.Ranged));
            Def("status.137", "Защита от чар", "defense", Good()).Own(1).Uses(1).Op(Special("WardSupernatural")).Magic();
            Def("status.138", "Огненный покров", "defense", Good()).Own(2).Op(new SandboxStatusOp(SandboxStatusOpKind.ContactRetort, 1, SandboxStatusFilter.Melee, damageType: SandboxDamageType.Fire));
            Def("status.139", "Зеркальная защита", "defense", Good()).Own(2).Uses(1).Op(Special("MirrorSupernatural")).Magic();
            Def("status.142", "Краткая неуязвимость", "defense", Good()).UntilStart().Op(Op(SandboxStatusOpKind.Invulnerable));

            // ---------- Лечение и сохранение жизни ----------
            Def("status.146", "Постепенное восстановление", "heal", Good()).Own(3).Op(Mod(SandboxStatusOpKind.TickHeal, 1));
            Def("status.148", "Запас временной жизни", "heal", Good()).Own(2).Op(Mod(SandboxStatusOpKind.TemporaryHitPoints, 3));
            Def("status.150", "При смерти", "heal", Bad()).Own(1).Op(Special("Dying")).Clean(A, "спасти")
                .Pending("Нужна явная поддержка спасения у участника (флаг Базы существ/героя) и состояние «0 HP, но не погиб» в OnUnitDefeated; без неё бой сохраняет прежнее поражение.");

            // ---------- Темп и мобильность ----------
            Def("status.156", "Ускоренное перемещение", "tempo", Good()).Own(2).Op(Mod(SandboxStatusOpKind.MovementMod, 1));
            Def("status.158", "Перехват инициативы", "tempo", Good()).Own(2).Op(Mod(SandboxStatusOpKind.InitiativeMod, 2));
            Def("status.159", "Прилив сил", "tempo", Good()).Own(1).Uses(1).Op(Special("ExtraSupportAction"));
            Def("status.161", "Набранный темп", "tempo", Good()).Own(2).Charges(2).Op(Special("MomentumMovement", 1));

            // ---------- Скрытие и обнаружение ----------
            Def("status.168", "Ослепление", "vision", Bad()).Own(1).Op(Mod(SandboxStatusOpKind.AttackMod, -2)).Op(Op(SandboxStatusOpKind.RangedAdjacentOnly)).Clean(SA, "промыть глаза");
            Def("status.171", "Невидимость", "vision", Good()).Own(2).Op(Op(SandboxStatusOpKind.CannotBeTargeted)).Op(Op(SandboxStatusOpKind.RemoveOnHpDamage)).Magic();

            // ---------- Накопительные и необычные ----------
            Def("status.185", "Повреждена нога", "stack", Bad()).Forever().Op(Mod(SandboxStatusOpKind.MovementMod, -1)).Clean(SA, "обработать рану");
            Def("status.186", "Повреждена рука", "stack", Bad()).Forever().Op(Mod(SandboxStatusOpKind.AttackMod, -1)).Clean(SA, "обработать рану");
            Def("status.187", "Контузия головы", "stack", Bad()).Own(2).Op(Mod(SandboxStatusOpKind.AttackMod, -1)).Clean(A, "помочь");
            Def("status.188", "Порча невезения", "stack", Bad()).Own(2).Uses(1).Op(new SandboxStatusOp(SandboxStatusOpKind.OutgoingFlat, -1, consumesUse: true)).Clean(SA, "очистить").Magic();
            Def("status.189", "Боевая удача", "stack", Good()).Own(2).Uses(1).Op(new SandboxStatusOp(SandboxStatusOpKind.OutgoingFlat, 1, consumesUse: true));
            Def("status.190", "Воодушевление", "stack", Good()).Own(2).Immune(Fear, Demoralized);

            // Проверка полноты: каждое определение имеет операции, а Special —
            // зарегистрированный исполнитель (иначе это ошибка покрытия).
            foreach (SandboxStatusDefinition def in all)
            {
                if (def.Ops.Count == 0 && def.ImmuneTo.Count == 0)
                    throw new InvalidOperationException("Состояние без операций: " + def.Id);
                foreach (SandboxStatusOp op in def.Ops.Where(op => op.Kind == SandboxStatusOpKind.Special))
                {
                    if (!SpecialExecutors.Contains(op.Executor))
                        throw new InvalidOperationException("Нет исполнителя " + op.Executor + " у " + def.Id);
                }
            }
        }

        // Зарегистрированные именованные исполнители (SandboxBattle.Statuses).
        public static readonly HashSet<string> SpecialExecutors = new HashSet<string>(StringComparer.Ordinal)
        {
            "DrainLink", "AttachLink", "GrowingDread", "DelayedBurst", "PainEcho", "IncubationCocoon", "FrostToFreeze",
            "VineLink", "EngulfLink", "Surrounded", "PainfulMovement", "FleeFromSource", "Confusion", "Domination",
            "DelayedSentence", "DuelMirror", "Bodyguard", "SharedWounds", "SourceBinding", "GuardFacade", "WardSupernatural",
            "MirrorSupernatural", "Dying", "ExtraSupportAction", "MomentumMovement"
        };

        // ---------- Построители (данные, не логика) ----------

        private static int sequence;

        private sealed class Builder
        {
            public SandboxStatusDefinition Def;
            public Builder Own(int turns) { Def.Clock = SandboxStatusClock.OwnActivations; Def.Duration = turns; return this; }
            public Builder Source(int turns) { Def.Clock = SandboxStatusClock.SourceActivations; Def.Duration = turns; return this; }
            public Builder Rounds(int rounds) { Def.Clock = SandboxStatusClock.RoundTrigger; Def.Duration = rounds; return this; }
            public Builder UntilStart() { Def.Clock = SandboxStatusClock.UntilOwnActivationStart; Def.Duration = 1; return this; }
            public Builder Forever() { Def.Clock = SandboxStatusClock.UntilRemoved; Def.Duration = 0; return this; }
            public Builder Facade() { Def.Clock = SandboxStatusClock.LegacyFacade; Def.Duration = 0; return this; }
            public Builder Charges(int max) { Def.Stacking = SandboxStatusStacking.Charges; Def.MaxCharges = max; return this; }
            public Builder Uses(int uses) { Def.Uses = uses; return this; }
            public Builder Strong() { Def.StrongControl = true; return this; }
            public Builder SourceBound() { Def.RemovedOnSourceDeath = true; return this; }
            public Builder Magic() { Def.Supernatural = true; return this; }
            public Builder Clean(SandboxStatusCleanse who, string verb) { Def.Cleanse = who; Def.CleanseVerb = verb; return this; }
            public Builder Op(SandboxStatusOp op) { Def.Add(op); return this; }
            public Builder Immune(params string[] ids) { foreach (string id in ids) Def.AddImmunity(id); return this; }
            public Builder Pending(string reason) { Def.PendingReason = reason; return this; }
        }

        private static bool Bad() => true;
        private static bool Good() => false;

        private static Builder Def(string id, string name, string category, bool harmful)
        {
            SandboxStatusDefinition def = new SandboxStatusDefinition(id, name, category,
                harmful ? SandboxStatusPolarity.Harmful : SandboxStatusPolarity.Beneficial) { IsHarmful = harmful };
            if (byId.ContainsKey(id))
                throw new InvalidOperationException("Повтор ID состояния: " + id);
            byId[id] = def;
            all.Add(def);
            sequence++;
            return new Builder { Def = def };
        }

        private static SandboxStatusOp Op(SandboxStatusOpKind kind) => new SandboxStatusOp(kind);
        private static SandboxStatusOp Mod(SandboxStatusOpKind kind, int value, int minCharges = 0) => new SandboxStatusOp(kind, value, minCharges: minCharges);
        private static SandboxStatusOp Tick(int value, SandboxDamageType type = SandboxDamageType.Physical) => new SandboxStatusOp(SandboxStatusOpKind.TickDamage, value, damageType: type);
        private static SandboxStatusOp Special(string executor, int value = 0) => new SandboxStatusOp(SandboxStatusOpKind.Special, value, executor: executor);

        // Порядок показа над бойцом (§10.1 пакета): При смерти → сильный
        // контроль → взрыв/Приговор → прочие вредные → полезные.
        public static int DisplayPriority(SandboxStatusDefinition def)
        {
            if (def == null)
                return 9;
            if (def.Id == "status.150")
                return 0;
            if (def.StrongControl)
                return 1;
            if (def.Id == "status.010" || def.Id == "status.105")
                return 2;
            return def.IsHarmful ? 3 : 4;
        }
    }
}
