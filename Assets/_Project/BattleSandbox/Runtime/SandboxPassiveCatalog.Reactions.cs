using System.Linq;
using K = KingdomSurvival.BattleSandbox.SandboxPassiveKind;
using C = KingdomSurvival.BattleSandbox.SandboxPassiveCondition;
using L = KingdomSurvival.BattleSandbox.SandboxPassiveLimit;
using E = KingdomSurvival.BattleSandbox.SandboxReactionEvent;
using X = KingdomSurvival.BattleSandbox.SandboxReactionEffect;
using S = KingdomSurvival.BattleSandbox.SandboxReactionScope;
using R = KingdomSurvival.BattleSandbox.SandboxStatusEndReason;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф завершения, этап 2: теги-реакции на состояния. Тексты и лимиты —
    // passive_tags_effective.json (поле effect); профили — реестр пакета.
    public static partial class SandboxPassiveCatalog
    {
        private static string[] Ids(params string[] numbers) => numbers.Select(n => "status." + n).ToArray();

        private static void RegisterStatusReactions()
        {
            const string p = "passive.status.20261120.";
            T(p + "003", "Противоядие наготове").React(E.Attempt, Ids("001"), X.Cancel, S.AllyNear, L.PerBattle, range: 1);
            T(p + "005", "Пьёт из открытой раны").React(E.Tick, Ids("002"), X.HealSelf, S.OwnerIsSource, L.PerRound, value: 1, range: 3);
            T(p + "006", "След крови").React(E.Applied, Ids("002"), X.ApplyToEventTarget, S.AppliedByOwner, L.PerBattle, effectStatus: "status.101");
            T(p + "007", "Затягивает рваные края").R(K.StatusExtraDamageReduceOnce, 1, limit: L.PerRound, statuses: Ids("193", "194"));
            T(p + "010", "Сбивает пламя с товарища").R(K.AllyGuardExtinguish, limit: L.PerRound, status: SandboxStatusCatalog.Burning);
            T(p + "011", "Огонь будит ярость").React(E.Tick, Ids("003"), X.ApplyToSelf, S.Self, L.PerBattle, effectStatus: "status.113", effectProfile: P("fury", duration: 2));
            T(p + "013", "Лёд сковывает слабого")
                .React(E.Applied, Ids("015"), X.AddCharge, S.AppliedByOwner, L.PerRound, targetRequiresAny: Ids("071"))
                .React(E.ChargeAdded, Ids("015"), X.AddCharge, S.AppliedByOwner, L.PerRound, targetRequiresAny: Ids("071"));
            T(p + "014", "Осколок освобождения").React(E.Ended, Ids("033"), X.DamageEventActor, S.Self, L.PerBattle, value: 1, reasons: new[] { R.BrokenByDamage });
            T(p + "017", "Скользкая хватка").R(K.GrappleBreakOnce, limit: L.PerBattle);
            T(p + "018", "Удерживает с усилием").R(K.HolderImmobilizedView, status: "status.043");
            T(p + "020", "Память о мороке").React(E.Ended, Ids("059", "060", "061"), X.ImmuneSameStatus, S.Self, L.PerBattle);
            T(p + "021", "Слышит родной голос").React(E.Ended, Ids("059", "060", "061"), X.HealSelf, S.Self, L.PerBattle, value: 1, reasons: new[] { R.CleansedByAction });
            T(p + "023", "Чуткий часовой").R(K.WakeAdjacentSleeper, limit: L.PerRound, status: "status.030");
            T(p + "024", "Трудно устрашить повторно").React(E.Ended, Ids("055", "057", "058"), X.ImmuneSameStatusSameSource, S.Self, reasons: new[] { R.Expired });
            T(p + "025", "Крепнет рядом с вожаком").React(E.Attempt, Ids("055", "066"), X.Cancel, S.AllyNear, L.PerBattle, range: 1, ownerRequires: SandboxStatusCatalog.Inspired);
            T(p + "027", "Не видит страшного").React(E.Attempt, Ids("055", "057"), X.Cancel, S.Self, ownerRequires: "status.168");
            T(p + "028", "Страх ослабляет хватку").R(K.FearBreaksGrapple, limit: L.PerBattle, status: SandboxStatusCatalog.Fear);
            T(p + "030", "Вызов держит строй").React(E.Applied, Ids("106"), X.ApplyToNearestAlly, S.Self, L.PerBattle, effectStatus: SandboxStatusCatalog.Inspired, range: 2);
            T(p + "031", "Охотник на дурман").R(K.OutgoingFlat, 1, C.TargetHasStatus, limit: L.PerOwnTurn, statuses: Ids("078", "079", "070"));
            T(p + "034", "Снимает тяжесть удара").R(K.BasePenaltyIgnoreOnce, limit: L.PerBattle, statuses: Ids("088", "099"));
            T(p + "035", "Клинок находит метку").R(K.MarkConsumeOncePerAction, limit: L.PerBattle, statuses: Ids("098", "102"));
            T(p + "036", "Метка не прячется").React(E.Attempt, Ids("171"), X.Cancel, S.Anyone, L.PerBattle, effectStatus: "status.101|status.102", value: 2,
                targetRequiresAny: Ids("101", "102"));
            T(p + "037", "Запоминает сорванную добычу").React(E.Ended, Ids("101"), X.ApplyToSelf, S.OwnerIsSource, L.PerBattle, effectStatus: "status.156",
                reasons: new[] { R.CleansedByAction });
            T(p + "038", "Оберег от приговора").R(K.DelayedTriggerReduceOnce, 1, limit: L.PerBattle, statuses: Ids("105", "010"));
            T(p + "039", "Сбивает отсчёт порчи").React(E.Ended, Ids("105", "010"), X.HealSelf, S.Self, L.PerBattle, value: 1, reasons: new[] { R.CleansedByAction });
            T(p + "040", "Не кормит болевое эхо").R(K.PainEchoReduceOnce, 1, limit: L.PerBattle);
            T(p + "041", "Рвёт мучительную связь").R(K.DrainLinkShortRange, 2);
            T(p + "042", "Связь укрепляет стража").R(K.AttackMod, 1, C.SelfIsLinkPartner, limit: L.PerRound);
            T(p + "043", "Разрывает связь на исходе").R(K.SharedWoundsSurviveOnce, limit: L.PerBattle);
            T(p + "044", "Зависть к чужому покрову").R(K.ShortenTargetBuffOnHit, 1, limit: L.PerBattle, statuses: Ids("135", "136", "148"));
            T(p + "045", "Берегущий заговор").R(K.Policy);
            T(p + "046", "Зеркало не обманешь дважды").R(K.ReflectImmunityOnce, limit: L.PerBattle);
            T(p + "047", "Заслон от пылающей кожи").R(K.RetortReduceOncePerRound, 1, limit: L.PerRound);
            T(p + "048", "Щит любит ожидание").R(K.ShieldExtendIfWaited, 1, limit: L.PerBattle, status: "status.132");
            T(p + "049", "Второе дыхание покрова").R(K.TempPoolExhaustedRegen, limit: L.PerBattle, status: "status.146");
            T(p + "050", "Обрывает чужую подпитку").R(K.ShortenTargetBuffOnHit, 1, limit: L.PerBattle, statuses: Ids("146"));
            T(p + "051", "Жизнь не впрок").R(K.NoVampirismWhileStatus, statuses: Ids("199"));
            T(p + "052", "Силу ран бережёт").R(K.WoundStrengthExtend, 1, limit: L.PerBattle, value2: 4, status: "status.199");
            T(p + "053", "Кровожадность выдаёт")
                .React(E.Applied, Ids("117"), X.RemoveFromSelf, S.Self, effectStatus: SandboxStatusCatalog.Invisible)
                .React(E.Attempt, Ids("171"), X.Cancel, S.Self, ownerRequires: "status.117");
            T(p + "054", "Завершает бой хладнокровно").React(E.Applied, Ids("119"), X.RemoveFromSelf, S.Self, L.PerBattle, effectStatus: "status.067");
            T(p + "055", "Сдерживает удачу").React(E.Attempt, Ids("188"), X.Cancel, S.Self, L.PerBattle, ownerLacks: SandboxStatusCatalog.GoodLuck);
            T(p + "056", "Удача утешает").React(E.Applied, Ids("189"), X.RemoveFromSelf, S.Self, L.PerBattle, effectStatus: "status.066");
            T(p + "057", "Шаг вопреки слабости").React(E.Attempt, Ids("041"), X.Cancel, S.Self, L.PerBattle, ownerRequires: "status.156", effectStatus: "status.156");
            T(p + "058", "Сбитый темп отрезвляет").React(E.Attempt, Ids("059"), X.Cancel, S.Self, L.PerBattle, ownerRequires: SandboxStatusCatalog.Momentum,
                effectStatus: SandboxStatusCatalog.Momentum, range: 2);
            T(p + "059", "Не уронит инициативу").React(E.Attempt, Ids("087"), X.Cancel, S.Self, L.PerBattle, ownerRequires: "status.158", effectStatus: "status.158", value: 1);
            T(p + "060", "Помощь возвращает шаг").R(K.SupportRefundMovement, 1, limit: L.PerBattle, statuses: Ids("185"));
            T(p + "061", "Не бросает оружие от боли").React(E.Attempt, Ids("088"), X.Cancel, S.Self, L.PerBattle, ownerRequires: "status.186");
            T(p + "062", "Опора при контузии").R(K.RetaliationIgnoresStatus, statuses: Ids("187"));
            T(p + "065", "Выдавливает паразита").React(E.Ended, Ids("008", "203"), X.RemoveOneChargeFromSelf, S.Self, L.PerBattle,
                effectStatus: SandboxStatusCatalog.Bleeding, reasons: new[] { R.CleansedByAction });
            T(p + "066", "Носитель заразы слабеет").R(K.AttackMod, -1, C.SelfHasStatus, statuses: Ids("008", "203"));
            T(p + "067", "Не терпит чужих корней").R(K.RootsApplyStatusOnce, limit: L.PerBattle, status: SandboxStatusCatalog.Tarred);
            T(p + "068", "Очищение оставляет оберег").React(E.Ended, Ids("006", "012", "188"), X.ApplyToEventTarget, S.AppliedByOwner, L.PerBattle,
                effectStatus: "status.137", reasons: new[] { R.CleansedByAction });
            T(p + "069", "Проклятие цепляется за ослабленного").React(E.Applied, Ids("006"), X.SetDuration, S.AppliedByOwner, L.PerBattle, value: 3,
                targetRequiresAny: Ids("071"));
            T(p + "070", "Обет держит удар").R(K.VowReduceWhileStatus, 1, statuses: Ids("012"));
            T(p + "071", "Цена чужого воодушевления").React(E.Applied, Ids("190"), X.ApplyToNearestAlly, S.Self, L.PerBattle, effectStatus: SandboxStatusCatalog.Fear,
                range: 1, effectProfile: P("price_of_inspiration", duration: 2));
            T(p + "072", "Порча ищет прореху").React(E.Applied, Ids("019"), X.ApplyToEventTarget, S.AppliedByOwner, L.PerBattle, effectStatus: SandboxStatusCatalog.BadLuck,
                targetRequiresAny: Ids("102"));
            T(p + "073", "Точный ответ на ослепление").React(E.Applied, Ids("168"), X.ApplyToEventActor, S.Self, L.PerBattle, effectStatus: "status.102");
            T(p + "074", "Стойка удерживает силы").R(K.GuardStartShortenStatus, 1, limit: L.PerBattle, status: "status.072");
            T(p + "075", "Собирается после встряски").React(E.Ended, Ids("026"), X.ApplyToSelf, S.Self, L.PerBattle, effectStatus: "status.158");
            T(p + "076", "В оцепенении бережётся стрел").React(E.Applied, Ids("027"), X.ApplyToSelf, S.Self, L.PerBattle, effectStatus: "status.136",
                effectProfile: P("numb_arrows", duration: 2));
            T(p + "078", "Последняя искра надежды").React(E.Attempt, Ids("190"), X.SetDuration, S.Self, L.PerBattle, value: 1, ownerRequires: "status.068",
                effectStatus: "status.068");
            T(p + "079", "Руки помнят выстрел").React(E.Attempt, Ids("084"), X.Replace, S.Self, L.PerBattle, effectStatus: "status.088",
                effectProfile: P("hands_remember", duration: 1));
            T(p + "080", "Покров оставляет брешь").React(E.Ended, Ids("142"), X.ApplyToSelf, S.Self, L.PerBattle, effectStatus: "status.071");

            T("passive.20261119.023", "Сбрасывает порчу").R(K.StartRemoveMagicalStatus,
                statuses: Ids("006", "012", "099", "188", "009", "055", "057", "058", "059", "060", "061"));
            string[] negative = SandboxStatusCatalog.All.Where(d => d.IsHarmful && d.Clock != SandboxStatusClock.LegacyFacade).Select(d => d.Id).ToArray();
            T("passive.20261119b.021", "Порча ведёт клинок")
                .React(E.Applied, Ids("006", "012", "019", "041", "055", "067", "070", "071", "072", "078", "087", "098", "099", "101", "102", "185", "186", "187", "188"),
                    X.GrantAttackCharge, S.AppliedByOwner, L.PerOwnTurn, value: 1);
            T("passive.20261119b.036", "Иней поверх слабости")
                .React(E.Applied, negative, X.ApplyToEventTarget, S.AppliedByOwner, L.PerRound, effectStatus: SandboxStatusCatalog.Frost,
                    effectProfile: P("thin_rime", duration: 1, mode: "thin_rime"));

            RegisterSupportAndMagic();
            RegisterMovementAndSequences();
            RegisterLifecycle();
        }

        // Этапы 7–12: окружение, спасение, призывы и гибель.
        private static void RegisterLifecycle()
        {
            const string p = "passive.status.20261120.";
            const string q = "passive.20261119.";
            T(p + "077", "Разгон разрывает кольцо").R(K.MomentumBreaksRing, limit: L.PerBattle);
            T(p + "063", "Не даст захватить разум умирающего").React(E.Attempt, Ids("059", "060", "061"), X.Cancel, S.Self, ownerRequires: "status.150");
            T(p + "064", "Спасённый держится крепче").R(K.RescuedTemporaryPool, limit: L.PerBattle,
                profile: new SandboxStatusProfile { ProfileId = "rescued", Duration = 2, Value = 1 });
            T(q + "170", "Растёт от призыва").R(K.GrowOnAllySummon, 1, limit: L.PerRound, value2: 3);
            T(q + "165", "Распадается на выводок").R(K.SplitIntoBrood, 2);
            T(q + "260", "Взрывоопасная гибель").R(K.DelayedDeathBlast, 3);
            T("ecology.dangerous_remains", "Опасные останки").R(K.LeaveRemains, 1);
            T(q + "213", "Поглощает останки").R(K.ConsumeRemains, 2, limit: L.PerOwnTurn);
            T("myth.shared_fate", "Делит связь").R(K.SharedFateLink);
        }

        // Этапы 5–6: реакции на движение и несколько попаданий одного действия.
        private static void RegisterMovementAndSequences()
        {
            const string q = "passive.20261119.";
            T(q + "249", "Оппортунист").R(K.LeaveAdjacencyStrike, 0, limit: L.PerRound);
            T(q + "103", "Бьёт уходящего по ногам").R(K.LeaveAdjacencyStrike, 1, limit: L.PerRound);
            T("passive.20261119b.008", "Бьёт и отступает").R(K.HitAndRun);
            T("tactic.pursuer", "Идёт следом").R(K.FollowRetreatingEnemy, limit: L.PerRound);
            T("passive.20261119b.010", "Мстит за соседнего").R(K.AvengeAdjacentAlly, limit: L.PerRound);
            T(q + "112", "Автоматический укол").R(K.BlockRetort, 1, limit: L.PerRound);
            T(q + "207", "Ответ прежде удара").R(K.RetaliateFirst);
            T(q + "020", "Удар и возврат").R(K.StrikeAndReturn);
            T(q + "086", "Победа окрыляет").R(K.MoveAfterKill, 2, limit: L.PerOwnTurn);
            T(q + "226", "Два выстрела").R(K.SecondShot);
            T(q + "227", "Два ближних удара").R(K.SecondMeleeHit);
        }

        // Этапы 3–4: теги лечения, заклинаний и типов урона.
        private static void RegisterSupportAndMagic()
        {
            const string p = "passive.status.20261120.";
            const string q = "passive.20261119.";
            const string b = "passive.20261119b.";
            T(q + "136", "Умелые руки лекаря").R(K.HealOtherBonus, 2, limit: L.PerOwnTurn);
            T(q + "098", "Хороший пациент").R(K.HealReceivedBonus, 1, limit: L.PerRound);
            T(p + "032", "Хвори мешают лечению").R(K.HealReceivedPenaltyWhileStatus, 1, limit: L.PerRound, statuses: Ids("070"));
            T(p + "002", "Вытягивает отраву").Heal(K.OnHealOther, X.RemoveFromEventTarget, SandboxStatusCatalog.Poison, L.PerBattle);
            T(p + "004", "Чистая перевязь").Heal(K.OnHealOther, X.RemoveFromEventTarget, "status.002|status.193", L.PerOwnTurn);
            T(p + "016", "Подрезает путы").Heal(K.OnHealOther, X.RemoveFromEventTarget, "status.046|status.022|status.047", L.PerOwnTurn, range: 1);
            T(b + "026", "Лекарь крепнет от помощи").Heal(K.OnHealOther, X.HealSelf, limit: L.PerOwnTurn, value: 1);
            T(b + "027", "Целебное напутствие").Heal(K.OnHealOther, X.GrantAttackCharge, limit: L.PerOwnTurn, value: 1);
            T(b + "028", "Перевязь с оберегом").Heal(K.OnHealOther, X.ApplyToEventTarget, "status.135", L.PerOwnTurn,
                profile: new SandboxStatusProfile { ProfileId = "healing_ward", Duration = 1, Value = -2, Uses = 1 });
            T(b + "029", "Сила после исцеления").Heal(K.OnHealOther, X.ApplyToEventTarget, "status.148", L.PerTargetPerRound,
                profile: new SandboxStatusProfile { ProfileId = "heal_buffer", Duration = 1, Value = 2 });
            T(q + "128", "Сифон чужой силы").R(K.HealOnDispelMagical, 2, limit: L.PerBattle);
            T(b + "043", "Оберег ближних чар").R(K.SpellCastSelfStatus, limit: L.PerOwnTurn, status: "status.148",
                profile: new SandboxStatusProfile { ProfileId = "close_ward", Duration = 1, Value = 2 });
            T(b + "024", "Бережёт своих внутри чар").R(K.AreaSpellSparesAllies);
            T(b + "025", "Жатва чужой жизни").R(K.OnKillHealSelf, 2, limit: L.PerOwnTurn);
            T(b + "044", "Копит грозовую искру").R(K.SparkCharge, 2, limit: L.PerOwnTurn);
            T(q + "127", "Крадёт запас сил").R(K.MagicRestoreOnHit, 1, limit: L.PerOwnTurn);
            T(q + "235", "Отнимает чародейский запас").R(K.MagicStealAtStart, 1, value2: 2);
            T(q + "024", "Жаростойкая шкура").R(K.IncomingPercent, 40, C.FireDamage);
            T(q + "056", "Легко воспламеняется").R(K.IncomingPercent, -50, C.FireDamage);
            T(q + "129", "Непроницаемый для обычного").R(K.IncomingFlat, -2, C.NonMagical);
            T(q + "132", "Пробивает поглощение").R(K.PierceFlatReduction, 1);
            T(p + "008", "Копоть в глазах").R(K.OnHitApplyStatus, status: "status.168", when: C.TargetHasStatus, when2: C.FireDamage, limit: L.PerBattle,
                statuses: Ids("168", "003"), profile: P("soot", duration: 1));
            T(b + "034", "От удара стынет земля").R(K.ColdHitRimeCell, limit: L.PerOwnTurn);
            T(b + "035", "Промораживает насквозь").R(K.ColdHitMarkNext, 1, limit: L.PerOwnTurn);
            T(b + "045", "Не бьёт током от воды").R(K.ElectricWaterImmune);
        }
    }
}
