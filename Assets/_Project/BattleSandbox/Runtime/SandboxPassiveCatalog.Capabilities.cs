using K = KingdomSurvival.BattleSandbox.SandboxPassiveKind;
using C = KingdomSurvival.BattleSandbox.SandboxPassiveCondition;
using L = KingdomSurvival.BattleSandbox.SandboxPassiveLimit;
using F = KingdomSurvival.BattleSandbox.SandboxCapabilityFacts;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф завершения, этап 13: теги на фактах способностей. Классификаторы
    // (тело, оружие, уклонение) — обычные теги Базы существ без собственного
    // эффекта; их читают условия других тегов. Числа — из текста тега.
    public static partial class SandboxPassiveCatalog
    {
        private static void RegisterCapabilities()
        {
            const string q = "passive.20261119.";
            const string b = "passive.20261119b.";

            // Классификаторы-факты — не теги банка: SandboxCapabilityFacts.Classifiers.
            // Тело и классы целей.
            T(q + "005", "Разгон по прямой").R(K.AttackMod, 2, C.Melee, when2: C.StraightRun, limit: L.PerOwnTurn);
            T(q + "004", "Гроза крылатых").R(K.OutgoingPercent, 25, C.TargetTag, F.Flying);
            T(q + "044", "Великанобой").R(K.IgnoreDefense, 2, C.TargetTag, F.LargeBody);
            T(q + "013", "Слаженность рощи").R(K.AuraAllyDefense, 1, C.TargetTag, F.PlantBody);
            T(q + "014", "Хранитель поросли").R(K.AuraAllyRegen, 1, C.TargetTag, F.PlantBody, L.PerTargetPerRound);
            T(q + "032", "Поводырь мёртвых").R(K.AuraAllyAttack, 1, C.TargetTag, F.Undead);
            T(q + "040", "полёт").R(K.FlyOverLowObstacles);
            T(q + "195", "Хрупкий").R(K.MaxHitPointsPercent, 15);

            // Ответы, оружие, снаряды.
            T(q + "036", "Страшный укус").R(K.RetortWeakenOnHit, 1, limit: L.PerRound);
            T(q + "204", "Разбивает оружие").R(K.BreakWeapon, 1, limit: L.PerTargetPerBattle, value2: 2);
            T(q + "212", "Отнимает боезапас").R(K.DestroyAmmo, 1, limit: L.PerOwnTurn);
            T(q + "057", "Ломает заграждения").R(K.ObstacleDamageMultiplier, 2);
            T(q + "228", "Связывающие корни").R(K.RootBindOnHit, status: "status.043",
                profile: P("root_bind", battle: true, mode: "root_bind"));

            // Состояния цели и свои штрафы.
            T(q + "097", "Пользуется сумятицей").R(K.OutgoingFlat, 1, C.TargetHasStatus,
                statuses: Ids("055", "056", "057", "058", "066", "067", "068"));
            T(q + "208", "Не слабеет от ран").R(K.StatusAttackPenaltyIgnore, statuses: Ids("185", "186", "187", "193"));
            T(q + "101", "Ночная выдержка").R(K.StatusAttackPenaltyIgnore, when: C.Dark, statuses: Ids("055"));
            T(b + "064", "Стреляет в слабое место").R(K.OutgoingFlat, 2, C.Ranged, when2: C.TargetHasStatus, statuses: Ids("102", "098"));

            // Направление, уклонение, свет, усталость.
            T(q + "100", "Фехтовальная защита").R(K.FlankBonusReduction, 1);
            T(q + "130", "Опора соседей").R(K.AuraAllyDefense, 1, value2: 1);
            T(q + "131", "Точность против уклонения").R(K.DodgeReduction, 1);
            T(q + "102", "Ночная стрельба").R(K.IgnoreDarkness);
            T(q + "210", "Не устаёт в бою").R(K.IgnoreFatigue);

            // Местность.
            T(q + "194", "Знает лесную схватку").R(K.AttackMod, 1, C.SelfInForest);
            T(q + "198", "Лесная защита").R(K.DefenseMod, 1, C.SelfInForest).R(K.ForestStride);
            T(b + "039", "Заросли кормят тело").R(K.ActivationStartHeal, 1, C.SelfInThicket);
            T("candidate.ks.0104", "Неуверенность").R(K.AttackMod, -1, C.Ranged, when2: C.BeyondHalfRange);
            T("candidate.ks.0127", "Липкий след").R(K.StickyTrailAfterShift, 1);

            // Тело и раны, раунды, засада, покровы.
            T(q + "220", "Слабеет вместе с телом").R(K.OutgoingPercent, -25, C.SelfHpBelowHalf).R(K.OutgoingPercent, -50, C.SelfHpQuarter);
            T(q + "264", "Отражающий покров").R(K.ReflectEvenRounds, 1, limit: L.PerRound);
            T(b + "016", "Первый бросок из засады").R(K.AmbushFirstActivation, 1).R(K.OutgoingFlat, 2, C.FirstOwnActivation, limit: L.PerBattle);
            T(b + "046", "Силовой покров разряжается").R(K.ForceShell, 1, value2: 3);
            T("candidate.ks.0262", "Тихая фаза").R(K.QuietPhase, profile: P("quiet_phase", duration: 1));
        }
    }
}
