using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    // Бриф завершения, этап 13: теги, которым нужны «факты способностей».
    // Факты — данные: классификаторы тела и оружия (теги-классификаторы
    // в каталоге), снаряды (Ammo в Базе существ), местность (лес, заросли,
    // низкие преграды) и условия боя (темнота, фланговая доплата, усталость).
    // Условия боя по умолчанию выключены: прежний баланс не меняется, пока
    // автор их не включит.
    public static class SandboxCapabilityFacts
    {
        // Классификаторы (назначаются как обычные теги в Базе существ).
        public const string PlantBody = "body.plant";
        public const string LargeBody = "body.large";
        public const string NaturalWeapon = "weapon.natural";
        public const string Evasive = "fact.evasive";
        // Боевой полёт — сам тег «полёт».
        public const string Flying = "passive.20261119.040";
        public const string Undead = "nature.returned";

        // Классификаторы-факты: назначаются в tagIds как теги, но в банк тегов
        // не входят (каталог исполнителя — только активные боевые теги банка).
        public static readonly (string Id, string Name, string Description)[] Classifiers =
        {
            (PlantBody, "Растительное тело", "Факт тела: читают «Слаженность рощи», «Хранитель поросли». Своего эффекта нет."),
            (LargeBody, "Крупное тело", "Факт тела: читает «Великанобой». Не равен высокому спрайту. Своего эффекта нет."),
            (NaturalWeapon, "Природное оружие", "Факт оружия: «Разбивает оружие» не действует. Зверь считается с природным оружием и без факта."),
            (Evasive, "Уклончивый", "Уклонение: +" + 2 + " Защиты как вклад уклонения; «Точность против уклонения» уменьшает вклад, но не до нуля.")
        };

        public static bool IsClassifier(string id) => Classifiers.Any(item => item.Id == id);

        // Вклад уклонения в Защиту у носителя «Уклончивый».
        public const int EvasiveDefense = 2;
        // Темнота: штраф Атаки обычного дальнего удара.
        public const int DarkRangedAttackPenalty = 1;

        // Вооружён ли боец (не природное оружие): зверь и носитель «Природного оружия» — нет.
        public static bool IsArmed(SandboxUnitState unit) =>
            unit != null && unit.Role != SandboxUnitRole.Beast && !unit.HasTag(NaturalWeapon);

        // Стрелок с конечным запасом снарядов.
        public static bool UsesAmmo(SandboxUnitState unit) => unit != null && unit.Definition.Ammo > 0;
    }

    public sealed partial class SandboxBattle
    {
        // ---------- Условия боя (настраиваются сценой/полигоном) ----------

        // Ночь или темнота на поле: дальний обычный удар −1 Атака.
        public bool IsDark { get; set; }
        // Доплата урона за фланговый ближний удар (союзник нападающего напротив цели); 0 — выключено.
        public int FlankBonusDamage { get; set; }
        // С какого раунда копится боевая усталость (−1 движение в начале хода); 0 — выключено.
        public int FatigueFromRound { get; set; }

        // ---------- Постоянные расчёты ----------

        internal static int PassiveMaxHpPenalty(SandboxUnitState unit)
        {
            int percent = 0;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.MaxHitPointsPercent))
                percent = Math.Max(percent, rule.Value);
            if (percent <= 0)
                return 0;
            int max = unit.Definition.MaxHitPoints;
            return Math.Min(max - 1, max * percent / 100);
        }

        // Процент к исходящему урону: сильнейшее усиление и сильнейшее ослабление.
        private int OutgoingPercentAdjust(SandboxUnitState attacker, SandboxUnitState target, bool ranged, int damage)
        {
            int up = 0, down = 0;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(attacker, SandboxPassiveKind.OutgoingPercent))
            {
                if (!Both(rule, attacker, target, ranged))
                    continue;
                if (rule.Value > 0) up = Math.Max(up, rule.Value);
                else down = Math.Max(down, -rule.Value);
            }
            if (up > 0) damage = damage * (100 + up) / 100;
            if (down > 0) damage = damage * (100 - down) / 100;
            return damage;
        }

        // Уклонение цели и точность нападающего: вклад уменьшается, но не до нуля.
        private int DodgeDefense(SandboxUnitState target, SandboxUnitState attacker)
        {
            int dodge = target.HasTag(SandboxCapabilityFacts.Evasive) ? SandboxCapabilityFacts.EvasiveDefense : 0;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.DodgeDefense))
                dodge = Math.Max(dodge, rule.Value);
            if (dodge <= 0 || attacker == null)
                return dodge;
            int reduce = 0;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(attacker, SandboxPassiveKind.DodgeReduction))
                reduce = Math.Max(reduce, rule.Value);
            return Math.Max(1, dodge - reduce);
        }

        // Силовой покров: прямое физическое попадание теряет по 1 урону за заряд.
        private int ShellReduction(SandboxUnitState target)
        {
            if (target == null || hitMagical || hitDamageType != SandboxDamageType.Physical || !HasPassive(target, SandboxPassiveKind.ForceShell))
                return 0;
            return target.passive.ShellCharges;
        }

        // Ослабленный ответ («Страшный укус»): −N урона в ответных ударах.
        private int RetortPenalty(SandboxUnitState attacker)
        {
            SandboxPassiveRuntime rt = attacker.passive;
            return rt.RetortPenalty > 0 && rt.RetortPenaltyUntilActivation >= attacker.ActivationCount ? rt.RetortPenalty : 0;
        }

        // Фланговая доплата: ближний удар, союзник нападающего стоит напротив.
        private int FlankBonus(SandboxUnitState attacker, SandboxUnitState target, bool ranged)
        {
            if (FlankBonusDamage <= 0 || ranged || !IsFlankAttack(attacker, target))
                return 0;
            int reduce = 0;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.FlankBonusReduction))
                reduce = Math.Max(reduce, rule.Value);
            return Math.Max(0, FlankBonusDamage - reduce);
        }

        internal bool IsFlankAttack(SandboxUnitState attacker, SandboxUnitState target)
        {
            if (attacker == null || target == null)
                return false;
            int dir = target.Position.GetNeighborIndex(attacker.Position);
            if (dir < 0)
                return false;
            SandboxUnitState ally = GetUnitAt(SandboxHexLine.Step(target.Position, (dir + 3) % 6));
            return ally != null && ally != attacker && ally.Team == attacker.Team && !ally.IsDefeated;
        }

        // Темнота: штраф Атаки обычного дальнего удара (тег «Ночная стрельба» снимает).
        private int DarknessAttackPenalty(SandboxUnitState attacker, bool ranged, bool normalAttack)
        {
            if (!IsDark || !ranged || !normalAttack || HasPassive(attacker, SandboxPassiveKind.IgnoreDarkness))
                return 0;
            return SandboxCapabilityFacts.DarkRangedAttackPenalty;
        }

        // Штраф Атаки от состояния не действует (раны, страх ночью).
        private bool StatusPenaltyIgnored(SandboxUnitState unit, SandboxStatusInstance instance, int value)
        {
            if (value >= 0)
                return false;
            foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.StatusAttackPenaltyIgnore))
            {
                if (rule.StatusIds.Contains(instance.StatusId) && Both(rule, unit, null, false))
                    return true;
            }
            return false;
        }

        // Аура защиты союзников: растения рощи, опора соседей против фронтального ближнего.
        private int AllyDefenseAura(SandboxUnitState target, SandboxUnitState attacker, bool ranged)
        {
            int best = 0;
            foreach (SandboxUnitState bearer in units)
            {
                if (bearer == target || bearer.IsDefeated || bearer.IsOffBoard || bearer.Team != target.Team ||
                    bearer.Position.DistanceTo(target.Position) != 1 || StrongControlled(bearer))
                    continue;
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(bearer, SandboxPassiveKind.AuraAllyDefense))
                {
                    if (rule.Condition == SandboxPassiveCondition.TargetTag && !target.HasTag(rule.ConditionParam))
                        continue;
                    // Value2 = 1: только фронтальный ближний удар (не фланг).
                    if (rule.Value2 == 1 && (ranged || attacker == null || IsFlankAttack(attacker, target)))
                        continue;
                    best = Math.Max(best, rule.Value);
                }
            }
            return best;
        }

        // ---------- Начало и конец хода ----------

        private void CapabilityActivationStart(SandboxUnitState unit)
        {
            // «Хранитель поросли»: соседний союзник-растение восстанавливает 1 HP за раунд.
            foreach (SandboxUnitState bearer in units)
            {
                if (bearer == unit || bearer.IsDefeated || bearer.IsOffBoard || bearer.Team != unit.Team ||
                    bearer.Position.DistanceTo(unit.Position) != 1 || StrongControlled(bearer))
                    continue;
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(bearer, SandboxPassiveKind.AuraAllyRegen))
                {
                    if (rule.Condition == SandboxPassiveCondition.TargetTag && !unit.HasTag(rule.ConditionParam))
                        continue;
                    if (unit.HitPoints >= unit.MaxHitPoints || !LimitReady(bearer, rule, unit.Id))
                        continue;
                    MarkUsed(bearer, rule, unit.Id);
                    HealLog(unit, rule.Value, def.Name + " (" + bearer.DisplayLabel + ")");
                }
            }
        }

        private int CapabilityMovementStart(SandboxUnitState unit)
        {
            int bonus = 0;
            if (unit.ActivationCount <= 1)
            {
                foreach ((SandboxPassiveDefinition _, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.AmbushFirstActivation))
                    bonus = Math.Max(bonus, rule.Value);
            }
            if (FatigueFromRound > 0 && Round >= FatigueFromRound)
            {
                if (HasPassive(unit, SandboxPassiveKind.IgnoreFatigue))
                    Log(unit.DisplayLabel + ": «" + Rules(unit, SandboxPassiveKind.IgnoreFatigue).First().Def.Name + "» — боевая усталость не отнимает движение.");
                else
                    bonus -= 1;
            }
            return bonus;
        }

        private void CapabilityActivationEnd(SandboxUnitState unit, bool still)
        {
            SandboxPassiveRuntime rt = unit.passive;
            if (rt.RetortPenaltyUntilActivation >= 0 && unit.ActivationCount >= rt.RetortPenaltyUntilActivation)
            {
                rt.RetortPenalty = 0;
                rt.RetortPenaltyUntilActivation = -1;
            }
            if (!still)
                return;
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.ForceShell))
            {
                if (rt.ShellCharges >= rule.Value2)
                    continue;
                rt.ShellCharges++;
                Log(unit.DisplayLabel + ": «" + def.Name + "» — заряд " + rt.ShellCharges + "/" + rule.Value2 + ".");
            }
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.QuietPhase))
            {
                SandboxStatusApplyResult applied = ApplyStatus(SandboxStatusCatalog.Invisible, unit, unit, rule.Profile);
                if (applied.Applied)
                    Log(unit.DisplayLabel + ": «" + def.Name + "» — естественная маскировка.");
            }
        }

        // ---------- Попадание ----------

        private void CapabilityOnDirectHit(SandboxHitKind kind, SandboxUnitState striker, SandboxUnitState target, bool ranged, int hpTaken)
        {
            if (striker == null || target == null || striker.Team == target.Team)
                return;
            bool own = IsOwnAttack(kind);
            // Расход боевого снаряда стрелком.
            if (own && ranged && kind == SandboxHitKind.Attack && SandboxCapabilityFacts.UsesAmmo(striker) && striker.AmmoLeft > 0)
                striker.AmmoLeft--;
            if (own && !striker.IsDefeated)
            {
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(striker, SandboxPassiveKind.RetortWeakenOnHit))
                {
                    if (hpTaken <= 0 || target.IsDefeated || !LimitReady(striker, rule, target.Id))
                        continue;
                    MarkUsed(striker, rule, target.Id);
                    target.passive.RetortPenalty = rule.Value;
                    target.passive.RetortPenaltyUntilActivation = target.ActivationCount + 1;
                    Log(striker.DisplayLabel + ": «" + def.Name + "» — " + target.DisplayLabel + " слабее отвечает.");
                }
                if (!ranged)
                {
                    foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(striker, SandboxPassiveKind.BreakWeapon))
                    {
                        if (target.IsDefeated || !SandboxCapabilityFacts.IsArmed(target) || !LimitReady(striker, rule, target.Id))
                            continue;
                        string key = "breakweapon:" + target.Id;
                        int count = striker.passive.Counter(key) + 1;
                        striker.passive.Counters[key] = count;
                        if (count < rule.Value2)
                            continue;
                        MarkUsed(striker, rule, target.Id);
                        target.passive.Stacks["attack.permanent"] = target.passive.Stack("attack.permanent") - rule.Value;
                        Log(striker.DisplayLabel + ": «" + def.Name + "» — оружие " + target.DisplayLabel + " повреждено, Атака −" + rule.Value + " до конца боя.");
                    }
                    foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(striker, SandboxPassiveKind.DestroyAmmo))
                    {
                        if (!SandboxCapabilityFacts.UsesAmmo(target) || target.AmmoLeft <= 0 || !LimitReady(striker, rule, target.Id))
                            continue;
                        MarkUsed(striker, rule, target.Id);
                        target.AmmoLeft = Math.Max(0, target.AmmoLeft - rule.Value);
                        Log(striker.DisplayLabel + ": «" + def.Name + "» — у " + target.DisplayLabel + " осталось снарядов: " + target.AmmoLeft + ".");
                    }
                    foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(striker, SandboxPassiveKind.RootBindOnHit))
                    {
                        if (target.IsDefeated || !string.IsNullOrEmpty(striker.passive.RootBindTargetId) || !LimitReady(striker, rule, target.Id))
                            continue;
                        SandboxStatusApplyResult applied = ApplyStatus(rule.StatusIds[0], target, striker, rule.Profile);
                        if (!applied.Applied)
                            continue;
                        MarkUsed(striker, rule, target.Id);
                        striker.passive.RootBindTargetId = target.Id;
                        Log(striker.DisplayLabel + ": «" + def.Name + "» держат " + target.DisplayLabel + ".");
                    }
                }
            }
            if (!IsDirectHit(kind) || target.IsDefeated)
                return;
            // «Отражающий покров»: в чётные раунды прямой HP-урон возвращает нападавшему 1.
            if (hpTaken > 0 && Round % 2 == 0)
            {
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.ReflectEvenRounds))
                {
                    if (striker.IsDefeated || !LimitReady(target, rule, null))
                        continue;
                    MarkUsed(target, rule, null);
                    DealStatusDamage(striker, rule.Value, target, "«" + def.Name + "» (" + target.DisplayLabel + ")", SandboxDamageType.Physical);
                }
            }
            // «Силовой покров»: при полном заряде ближний нападающий получает 1, заряды сгорают.
            if (!ranged && !hitMagical && hitDamageType == SandboxDamageType.Physical)
            {
                foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(target, SandboxPassiveKind.ForceShell))
                {
                    if (target.passive.ShellCharges < rule.Value2)
                        continue;
                    target.passive.ShellCharges = 0;
                    if (!striker.IsDefeated)
                        DealStatusDamage(striker, rule.Value, target, "«" + def.Name + "» разряжается (" + target.DisplayLabel + ")", SandboxDamageType.Physical);
                }
            }
        }

        // ---------- Связь корней ----------

        // Держит, пока носитель соседствует и сам не двигается.
        private void CheckRootBinds(SandboxUnitState moved = null)
        {
            foreach (SandboxUnitState bearer in units)
            {
                string targetId = bearer.passive.RootBindTargetId;
                if (string.IsNullOrEmpty(targetId))
                    continue;
                SandboxUnitState target = GetUnit(targetId);
                bool keep = !bearer.IsDefeated && !bearer.IsOffBoard && bearer != moved && target != null && !target.IsDefeated &&
                            bearer.Position.DistanceTo(target.Position) == 1;
                SandboxStatusInstance bind = target?.statuses.FirstOrDefault(item => item.SourceUnitId == bearer.Id && item.Mode == "root_bind");
                if (keep && bind != null)
                    continue;
                bearer.passive.RootBindTargetId = null;
                if (bind != null)
                    RemoveStatusInstance(target, bind, "корни отпустили");
            }
        }

        // ---------- Липкий след ----------

        private void AfterForcedShift(SandboxUnitState unit, HexCoord firstCell)
        {
            foreach ((SandboxPassiveDefinition def, SandboxPassiveRule rule) in Rules(unit, SandboxPassiveKind.StickyTrailAfterShift))
            {
                if (AddEffect(firstCell, SandboxEffectKind.Sticky, Math.Max(1, rule.Value), 0, unit.Id) != null)
                    Log(unit.DisplayLabel + ": «" + def.Name + "» — клетка " + firstCell + " липкая.");
            }
            CheckRootBinds();
        }

        // ---------- Полёт ----------

        // Пролёт над низкой преградой: шаг по обычной цене, остановиться нельзя.
        internal bool CanFlyOver(SandboxUnitState unit, HexCoord cell)
        {
            if (unit == null || !HasPassive(unit, SandboxPassiveKind.FlyOverLowObstacles) || !IsInside(cell) ||
                GetTerrain(cell) == SandboxTerrain.Impassable || IsGroundBlocking(cell) || HasCellState(cell, SandboxCellState.Pit))
                return false;
            SandboxBattleObject item = TerrainObjectAt(cell);
            return item != null && SandboxTerrainObjects.Get(item.Kind).Height == SandboxObstacleHeight.Low;
        }

        // Все клетки маршрута, включая пролётные (для восстановления пути).
        internal IReadOnlyDictionary<HexCoord, int> GetTransitCosts(string unitId)
        {
            SandboxUnitState unit = GetUnit(unitId);
            if (unit == null || !HasPassive(unit, SandboxPassiveKind.FlyOverLowObstacles))
                return GetReachable(unitId);
            Dictionary<HexCoord, int> all = BuildReachable(unit, unit.RemainingMovement, includeTransit: true);
            IReadOnlyDictionary<HexCoord, int> stops = GetReachable(unitId);
            // Конечные клетки — после фильтра состояний; пролётные — как есть.
            Dictionary<HexCoord, int> result = new Dictionary<HexCoord, int>();
            foreach (KeyValuePair<HexCoord, int> pair in all)
            {
                if (stops.ContainsKey(pair.Key) || CanFlyOver(unit, pair.Key))
                    result[pair.Key] = pair.Value;
            }
            return result;
        }

        internal int GetTransitStepCost(SandboxUnitState unit, HexCoord cell) =>
            CanFlyOver(unit, cell) ? 1 : GetStepCost(unit, cell);
    }
}
