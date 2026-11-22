using System;
using System.Collections.Generic;
using System.Linq;

namespace KingdomSurvival.BattleSandbox
{
    public enum SandboxTeam
    {
        Player,
        Enemy
    }

    public enum SandboxUnitRole
    {
        Guard,
        Archer,
        Healer,
        Spearman,
        Scout,
        Militia,
        Beast
    }

    public static class SandboxUnitTags
    {
        public const string Beast = "species.beast";
    }

    public enum SandboxTerrain
    {
        Normal,
        Difficult,
        Impassable
    }

    public enum SandboxBattlePhase
    {
        Preparing,
        InProgress,
        PlayerVictory,
        EnemyVictory
    }

    public sealed class SandboxUnitDefinition
    {
        private readonly HashSet<string> tagIds;
        private readonly HashSet<string> perkIds;

        public string Id { get; }
        public string RoleLabel { get; }
        public SandboxUnitRole Role { get; }
        public int MaxHitPoints { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int Damage { get; }
        public int Movement { get; }
        public int Initiative { get; }
        public int AttackRange { get; }
        public IReadOnlyCollection<string> TagIds => tagIds;

        // 12Е-6: боевые правила особенностей человека (SandboxPerks).
        public IReadOnlyCollection<string> PerkIds => perkIds;

        // ПР-16: способности существа из Базы существ (активные, пассивные,
        // профили ИИ). Пусто — обычный боец.
        public IReadOnlyList<SandboxAbilityDefinition> Abilities { get; }

        // Бриф завершения, этап 4: чародейский запас боевого профиля (0 — заклинания
        // ограничены только действием и КД). Задаётся в Базе существ.
        public int MagicReserve { get; }

        // Этап 11: может ли участник лечь «При смерти» вместо окончательной смерти.
        // Только явная настройка Базы существ; по умолчанию нет.
        public bool CanBeRescuedInBattle { get; }

        public SandboxUnitDefinition(
            string id,
            string roleLabel,
            SandboxUnitRole role,
            int maxHitPoints,
            int attack,
            int defense,
            int damage,
            int movement,
            int initiative,
            int attackRange,
            IEnumerable<string> tags = null,
            IEnumerable<string> perks = null,
            IEnumerable<SandboxAbilityDefinition> abilities = null,
            int magicReserve = 0,
            bool canBeRescuedInBattle = false)
        {
            MagicReserve = Math.Max(0, magicReserve);
            CanBeRescuedInBattle = canBeRescuedInBattle;
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ID типа бойца не может быть пустым.", nameof(id));

            Id = id;
            RoleLabel = string.IsNullOrWhiteSpace(roleLabel) ? id : roleLabel;
            Role = role;
            MaxHitPoints = Math.Max(1, maxHitPoints);
            Attack = Math.Max(0, attack);
            Defense = Math.Max(0, defense);
            Damage = Math.Max(1, damage);
            Movement = Math.Max(1, movement);
            Initiative = Math.Max(0, initiative);
            AttackRange = Math.Max(1, attackRange);
            tagIds = tags != null
                ? new HashSet<string>(
                    tags.Where(tag => !string.IsNullOrWhiteSpace(tag)),
                    StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            perkIds = perks != null
                ? new HashSet<string>(perks.Where(perk => !string.IsNullOrWhiteSpace(perk)), StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            Abilities = abilities != null
                ? abilities.Where(ability => ability != null && ability.Kind != SandboxAbilityKind.None).ToList()
                : new List<SandboxAbilityDefinition>();
        }

        public SandboxAbilityDefinition FindAbility(SandboxAbilityKind kind)
        {
            return Abilities.FirstOrDefault(ability => ability.Kind == kind);
        }

        public bool HasAbility(SandboxAbilityKind kind)
        {
            return FindAbility(kind) != null;
        }

        // Копия с другими числами (уровень противника) и теми же способностями.
        public SandboxUnitDefinition WithStats(int maxHitPoints, int attack, int defense, int damage, int movement, int initiative, int attackRange)
        {
            return new SandboxUnitDefinition(Id, RoleLabel, Role, maxHitPoints, attack, defense, damage, movement, initiative, attackRange, tagIds, perkIds, Abilities, MagicReserve, CanBeRescuedInBattle);
        }

        public bool HasTag(string tagId)
        {
            // Пакет тегов: legacy alias (tactic.pack_hunter → combat.pack и т. п.)
            // ведёт к тому же правилу, исполняется один раз.
            return !string.IsNullOrWhiteSpace(tagId) && (tagIds.Contains(tagId) || SandboxPassiveCatalog.IsAliased(tagIds, tagId));
        }

        public bool HasPerk(string perkId)
        {
            return !string.IsNullOrWhiteSpace(perkId) && perkIds.Contains(perkId);
        }
    }

    public sealed class SandboxUnitState
    {
        public const int ActionsPerActivation = 1;

        private readonly string instanceId;

        public SandboxUnitDefinition Definition { get; }
        public SandboxTeam Team { get; }
        public HexCoord Position { get; internal set; }
        public int HitPoints { get; internal set; }
        public int ActionPoints { get; internal set; }
        public int RemainingMovement { get; internal set; }
        public bool HasAttacked { get; internal set; }
        public bool IsGuarding { get; internal set; }
        // 12Е-6: ответов за раунд (Контрудар даёт два), атак по бойцу за раунд
        // (Основная защита), двигался ли в эту активацию (Холодный глаз) и
        // сработало ли «Ещё на ногах».
        public int RetaliationsThisRound { get; internal set; }
        public int TimesAttackedThisRound { get; internal set; }
        public bool MovedThisActivation { get; internal set; }
        public bool StillStandingUsed { get; internal set; }

        // 12Е-8: за весь бой — для следов развития в кампании.
        public int TimesAttacked { get; internal set; }
        public int RetaliationsMade { get; internal set; }
        public bool ShotFromPlace { get; internal set; }
        public int RetaliationsPerRound => Math.Max(HasPerk(SandboxPerks.Counterstrike) ? 2 : 1, SandboxBattle.PassiveRetaliations(this));
        // Пакет тегов: счётчики пассивов именно этого бойца.
        internal readonly SandboxPassiveRuntime passive = new SandboxPassiveRuntime();
        public SandboxPassiveRuntime Passive => passive;
        public bool HasRetaliatedThisRound => RetaliationsThisRound >= RetaliationsPerRound;

        // Реальный вклад в бой (канон v1.48 §27.3): урон, действительно
        // снятый с противников, и урон, не пропущенный защитной стойкой.
        // Полученный урон вкладом не считается.
        public int DamageDealt { get; internal set; }
        public int DamagePrevented { get; internal set; }
        public bool UsedRangedAttack { get; internal set; }
        public bool UsedMeleeAttack { get; internal set; }

        // ПР-16: состояния способностей. Длительности считаются по собственным
        // активациям бойца, а не по времени анимации.
        public int ActivationCount { get; internal set; }
        internal readonly Dictionary<string, int> AbilityReadyAt = new Dictionary<string, int>(StringComparer.Ordinal);
        public bool TryGetAbilityReadyAt(string abilityId, out int readyAt) => AbilityReadyAt.TryGetValue(abilityId ?? string.Empty, out readyAt);
        // Пакет состояний: временные состояния бойца (один источник истины).
        // Прежние флаги — вычисляемые фасады: «Ошеломлён» плевка Трещотки =
        // status.026, «Оцепенение» укуса Многонога = status.027.
        internal readonly List<SandboxStatusInstance> statuses = new List<SandboxStatusInstance>();
        public IReadOnlyList<SandboxStatusInstance> Statuses => statuses;
        public bool HasStatus(string statusId) => statuses.Any(item => item.StatusId == statusId);
        public bool IsStunned => HasStatus(SandboxStatusCatalog.Dazed);
        public bool IsNumbed => HasStatus(SandboxStatusCatalog.Numbed);
        // Защита от повторного сильного контроля (общая группа) и её снятие
        // в конце первого хода, начавшегося без такого контроля.
        public bool StrongControlGuard { get; internal set; }
        internal bool StrongControlReleaseAtEnd;
        // «Прилив сил»: дополнительное действие только на помощь/очищение.
        public int SupportActions { get; internal set; }
        public int NumbImmunity { get; internal set; }
        internal bool NumbImmunityCounted;
        // Ход без движения (оцепенение, захват, остановка нитью, выход из-под
        // земли): действие с места разрешено, хотя движение 0.
        public bool ActsWithoutMovement { get; internal set; }
        public string AttachedToId { get; internal set; }
        public bool AwaitingPlacement { get; internal set; }
        public bool IsUnderground { get; internal set; }
        public bool IsStuckInTunnel { get; internal set; }
        public bool IsHidden { get; internal set; }
        public string SpawnedBySourceId { get; internal set; }
        public bool CorpseConsumed { get; internal set; }
        public bool CanStrikeAfterExit { get; internal set; }
        public HexCoord BurrowOrigin { get; internal set; }
        public int PitPhase { get; internal set; }

        public bool IsAttached => !string.IsNullOrEmpty(AttachedToId);
        // Не занимает гекс поля: сидит на бойце, под землёй или ждёт места.
        public bool IsOffBoard => IsAttached || AwaitingPlacement || IsUnderground || IsStuckInTunnel;

        public string Id => instanceId;
        public string TypeId => Definition.Id;
        public string DisplayLabel => IsHidden ? "Неизвестная угроза" : Definition.RoleLabel;
        public SandboxUnitRole Role => Definition.Role;
        // Временное изменение MaxHP состояниями — не урон; возврат не лечит.
        public int MaxHitPoints => Math.Max(1, Definition.MaxHitPoints + passive.BonusMaxHitPoints + SandboxBattle.StatusMaxHitPointsMod(this));
        public int Attack => Definition.Attack;
        public int Defense => Definition.Defense;
        public int Damage => Definition.Damage;
        public int Movement => Definition.Movement;
        public int Initiative => Definition.Initiative;
        public int AttackRange => SandboxTerrainRules.GetEffectiveAttackRange(this);
        public bool IsDefeated => HitPoints <= 0;
        // Этап 11: лежит «При смерти» (0 HP, ещё не окончательная смерть).
        public bool IsDying { get; internal set; }
        // Призыв вступает в бой не раньше этого раунда.
        public int ActsFromRound { get; internal set; }
        public int DamageTaken => Math.Max(0, MaxHitPoints - HitPoints);
        public bool IsDamaged => DamageTaken > 0;
        public bool CanRetaliate => !IsDefeated && !HasRetaliatedThisRound && !IsOffBoard && !IsHidden &&
                                    !SandboxBattle.StatusHas(this, SandboxStatusOpKind.NoRetaliation) &&
                                    !SandboxBattle.StatusHas(this, SandboxStatusOpKind.SkipActivation) &&
                                    !SandboxBattle.PassiveBlocksRetaliation(this);

        // ПР-06А: человек кампании входит в бой с тем здоровьем, что у него
        // есть (1..максимум шаблона), а не всегда полным.
        public void SetStartingHitPoints(int hitPoints)
        {
            HitPoints = Math.Max(1, Math.Min(MaxHitPoints, hitPoints));
        }

        public bool HasTag(string tagId)
        {
            return Definition.HasTag(tagId);
        }

        public bool HasPerk(string perkId)
        {
            return Definition.HasPerk(perkId);
        }

        public SandboxUnitState(
            SandboxUnitDefinition definition,
            SandboxTeam team,
            HexCoord position)
            : this(definition != null ? definition.Id : null, definition, team, position)
        {
        }

        public SandboxUnitState(
            string instanceId,
            SandboxUnitDefinition definition,
            SandboxTeam team,
            HexCoord position)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("ID экземпляра бойца не может быть пустым.", nameof(instanceId));

            this.instanceId = instanceId;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Team = team;
            Position = position;
            HitPoints = definition.MaxHitPoints;
        }

        // Был ли в стойке в момент начала собственной активации (теги «в стойке на старте хода»).
        public bool WasGuardingAtStart { get; internal set; }

        internal void BeginActivation()
        {
            WasGuardingAtStart = IsGuarding;
            ActionPoints = ActionsPerActivation;
            RemainingMovement = Movement;
            HasAttacked = false;
            IsGuarding = false;
            MovedThisActivation = false;
            ActsWithoutMovement = false;
            CanStrikeAfterExit = false;
            ActivationCount++;
        }

        internal void ReceiveDamage(int damage)
        {
            // «Ещё на ногах»: 1/бой урон, который вывел бы из строя, оставляет 1 HP.
            if (HasPerk(SandboxPerks.StillStanding) && !StillStandingUsed && HitPoints > 0 && HitPoints - Math.Max(0, damage) <= 0)
            {
                StillStandingUsed = true;
                HitPoints = 1;
                return;
            }
            HitPoints = Math.Max(0, HitPoints - Math.Max(0, damage));
            if (IsDefeated)
            {
                ActionPoints = 0;
                RemainingMovement = 0;
                IsGuarding = false;
            }
        }

        internal void Heal(int amount)
        {
            if (!IsDefeated)
                HitPoints = Math.Min(MaxHitPoints, HitPoints + Math.Max(0, amount));
        }
    }

    // ПР-12З: вид удара в записи результата.
    public enum SandboxHitKind
    {
        Attack,
        // «Упреждающий удар» защитника до атаки противника.
        FirstStrike,
        Retaliation,
        // ПР-16: удар способности (прыжок, разгон, плевок и т.п.).
        Ability,
        // Бриф «Поверхности арены»: урон поверхности (огонь, колючки, яд, ток).
        Environment,
        // Пакет состояний: фиксированный урон состояния (тик, взрыв, перенос).
        Status,
        // Бриф завершения, этап 5: удар-реакция (за выход, месть за соседа).
        Reaction
    }

    // ПР-12З: запись одного удара. Исход уже применён моделью; представление
    // только показывает его по порядку и ничего не пересчитывает.
    public sealed class SandboxHitRecord
    {
        public SandboxHitKind Kind { get; }
        public string StrikerId { get; }
        public string TargetId { get; }
        public HexCoord StrikerPosition { get; }
        public HexCoord TargetPosition { get; }
        public int Damage { get; }
        public int TargetHitPointsBefore { get; }
        public int TargetHitPointsAfter { get; }
        public bool TargetDefeated => TargetHitPointsAfter <= 0;
        public bool IsRanged => StrikerPosition.DistanceTo(TargetPosition) > 1;

        public SandboxHitRecord(
            SandboxHitKind kind,
            string strikerId,
            string targetId,
            HexCoord strikerPosition,
            HexCoord targetPosition,
            int damage,
            int targetHitPointsBefore,
            int targetHitPointsAfter)
        {
            Kind = kind;
            StrikerId = strikerId;
            TargetId = targetId;
            StrikerPosition = strikerPosition;
            TargetPosition = targetPosition;
            Damage = damage;
            TargetHitPointsBefore = targetHitPointsBefore;
            TargetHitPointsAfter = targetHitPointsAfter;
        }
    }

    public sealed class SandboxAttackPreview
    {
        public static SandboxAttackPreview Invalid(string reason)
        {
            return new SandboxAttackPreview(false, reason, 0, 0);
        }

        public bool IsValid { get; }
        public string Reason { get; }
        // Прямой урон удара (после Атаки/Защиты, состояний и тегов).
        public int Damage { get; }
        // Пакет состояний: сколько HP реально снимется (щит, неуязвимость,
        // перенос ран, временные HP) и что ещё произойдёт — отдельными строками.
        public int HpDamage { get; internal set; }
        public int TargetHitPointsAfter { get; internal set; }
        public List<string> Notes { get; } = new List<string>();
        public List<string> StatusNotes { get; } = new List<string>();
        // Новые тики цели в начале её хода — прогноз, не гарантированный урон удара.
        public int UpcomingTicks { get; internal set; }

        public string Details
        {
            get
            {
                List<string> parts = new List<string>();
                if (HpDamage != Damage)
                    parts.Add("по HP " + HpDamage);
                parts.AddRange(Notes);
                parts.AddRange(StatusNotes.Select(note => "наложит " + note));
                if (UpcomingTicks > 0)
                    parts.Add("в начале хода цели ещё " + UpcomingTicks + " урона от состояний");
                return string.Join(" · ", parts);
            }
        }

        public SandboxAttackPreview(
            bool isValid,
            string reason,
            int damage,
            int targetHitPointsAfter)
        {
            IsValid = isValid;
            Reason = reason;
            Damage = damage;
            HpDamage = damage;
            TargetHitPointsAfter = targetHitPointsAfter;
        }
    }

    public sealed partial class SandboxBattle
    {
        private readonly List<SandboxUnitState> units;
        private readonly Dictionary<HexCoord, SandboxTerrain> terrain;
        private readonly bool usesCompactArenaShape;
        // Гексы, отключённые на конкретном поле (База полей боя): их нет в бою.
        private readonly HashSet<HexCoord> disabledCells = new HashSet<HexCoord>();
        private readonly List<string> turnOrderIds = new List<string>();
        private int currentTurnIndex = -1;
        private string pendingRetaliationDefenderId;
        private string pendingRetaliationAttackerId;
        private readonly List<SandboxHitRecord> hitRecords = new List<SandboxHitRecord>();

        // ПР-12З: все удары боя по порядку. Представление берёт новые записи
        // после действия, а не ищет изменения здоровья по всей модели.
        public IReadOnlyList<SandboxHitRecord> HitRecords => hitRecords;

        public List<SandboxHitRecord> GetHitRecordsSince(int index)
        {
            int start = Math.Max(0, index);
            return start >= hitRecords.Count
                ? new List<SandboxHitRecord>()
                : hitRecords.GetRange(start, hitRecords.Count - start);
        }

        public int Width { get; }
        public int Height { get; }
        public int Round { get; private set; }
        public SandboxBattlePhase Phase { get; private set; } = SandboxBattlePhase.Preparing;
        public IReadOnlyList<SandboxUnitState> Units => units;
        public IReadOnlyList<string> TurnOrderIds => turnOrderIds;
        public int CurrentTurnIndex => currentTurnIndex;
        public bool HasPendingRetaliation =>
            !string.IsNullOrEmpty(pendingRetaliationDefenderId) &&
            !string.IsNullOrEmpty(pendingRetaliationAttackerId);
        public string PendingRetaliationDefenderId => pendingRetaliationDefenderId;
        public string PendingRetaliationAttackerId => pendingRetaliationAttackerId;

        public SandboxUnitState CurrentUnit
        {
            get
            {
                if (Phase != SandboxBattlePhase.InProgress ||
                    currentTurnIndex < 0 || currentTurnIndex >= turnOrderIds.Count)
                {
                    return null;
                }

                return GetUnit(turnOrderIds[currentTurnIndex]);
            }
        }

        public SandboxBattle(
            int width,
            int height,
            IEnumerable<SandboxUnitState> units,
            IDictionary<HexCoord, SandboxTerrain> terrain = null,
            IEnumerable<HexCoord> disabledCells = null)
        {
            if (width < 2 || height < 2)
                throw new ArgumentOutOfRangeException(nameof(width));

            Width = width;
            Height = height;
            this.units = units != null
                ? new List<SandboxUnitState>(units)
                : throw new ArgumentNullException(nameof(units));
            this.terrain = terrain != null
                ? new Dictionary<HexCoord, SandboxTerrain>(terrain)
                : new Dictionary<HexCoord, SandboxTerrain>();
            usesCompactArenaShape = SandboxArenaShape.MatchesDimensions(width, height) &&
                                    SandboxArenaShape.InactiveCells().All(coord =>
                                        this.terrain.TryGetValue(coord, out SandboxTerrain value) &&
                                        value == SandboxTerrain.Impassable);
            if (disabledCells != null)
                this.disabledCells.UnionWith(disabledCells);
            // Поверхности арены: штраф топи и укрытие читают свой бой.
            SandboxTerrainRules.AttachBattle(this.units, this);

            ValidateInitialState();
        }

        public void Start()
        {
            if (Phase != SandboxBattlePhase.Preparing)
                return;

            Phase = SandboxBattlePhase.InProgress;
            Round = 1;
            PrepareStartingWater();
            SyncGroundWater();
            InitializeAbilityState();
            InitializeStatusPassives();
            ResetRoundRetaliations();
            BuildRoundOrder();
            currentTurnIndex = 0;
            BeginCurrentActivationOrAdvance();
        }

        public SandboxUnitState GetUnit(string unitId)
        {
            return units.FirstOrDefault(unit => unit.Id == unitId);
        }

        public SandboxUnitState GetUnitAt(HexCoord position)
        {
            return units.FirstOrDefault(unit => !unit.IsDefeated && !unit.IsOffBoard && unit.Position == position);
        }

        public SandboxTerrain GetTerrain(HexCoord position)
        {
            SandboxTerrain value;
            return terrain.TryGetValue(position, out value) ? value : SandboxTerrain.Normal;
        }

        public bool IsInside(HexCoord position)
        {
            bool insideRectangle =
                position.Q >= 0 && position.Q < Width &&
                position.R >= 0 && position.R < Height;
            if (!insideRectangle || disabledCells.Contains(position))
                return false;

            return !usesCompactArenaShape || SandboxArenaShape.Contains(position);
        }

        public IReadOnlyDictionary<HexCoord, int> GetReachable(string unitId)
        {
            SandboxUnitState unit = GetUnit(unitId);
            Dictionary<HexCoord, int> result = new Dictionary<HexCoord, int>();
            if (HasPendingRetaliation || unit == null || unit.IsDefeated ||
                (unit.ActionPoints <= 0 && unit.passive.MoveAfterActionBudget <= 0) || unit.RemainingMovement <= 0 || unit.IsOffBoard)
                return result;
            result = BuildReachable(unit, unit.RemainingMovement);
            FilterStatusReachable(unit, result);
            return result;
        }

        // ПР-16: достижимые клетки при заданном бюджете (отход Шиповика
        // после выстрела — из того же запаса движения).
        public IReadOnlyDictionary<HexCoord, int> GetReachableWithBudget(string unitId, int budget)
        {
            SandboxUnitState unit = GetUnit(unitId);
            if (unit == null || unit.IsDefeated || unit.IsOffBoard || budget <= 0)
                return new Dictionary<HexCoord, int>();
            Dictionary<HexCoord, int> reachable = BuildReachable(unit, budget);
            FilterStatusReachable(unit, reachable);
            return reachable;
        }

        private Dictionary<HexCoord, int> BuildReachable(SandboxUnitState unit, int budget)
        {
            Dictionary<HexCoord, int> result = new Dictionary<HexCoord, int>();

            Dictionary<HexCoord, int> costs = new Dictionary<HexCoord, int>
            {
                { unit.Position, 0 }
            };
            List<HexCoord> frontier = new List<HexCoord> { unit.Position };

            while (frontier.Count > 0)
            {
                int bestIndex = 0;
                for (int i = 1; i < frontier.Count; i++)
                {
                    if (costs[frontier[i]] < costs[frontier[bestIndex]])
                        bestIndex = i;
                }

                HexCoord current = frontier[bestIndex];
                frontier.RemoveAt(bestIndex);

                foreach (HexCoord next in current.Neighbors())
                {
                    if (!IsInside(next) || !IsEnterableFor(unit, next))
                        continue;
                    if (GetUnitAt(next) != null && next != unit.Position)
                        continue;
                    // Нить останавливает движение на своём гексе: дальше этой
                    // клетки маршрут не продолжается.
                    if (current != unit.Position && IsMovementStopCell(unit, current))
                        continue;

                    int stepCost = GetStepCost(unit, next) + PassiveExtraStepCost(unit, current, next);
                    int newCost = costs[current] + stepCost;
                    if (newCost > budget)
                        continue;

                    int oldCost;
                    if (costs.TryGetValue(next, out oldCost) && oldCost <= newCost)
                        continue;

                    costs[next] = newCost;
                    frontier.Add(next);
                }
            }

            foreach (KeyValuePair<HexCoord, int> pair in costs)
            {
                if (pair.Key != unit.Position)
                    result[pair.Key] = pair.Value;
            }

            return result;
        }

        public bool TryMove(string unitId, HexCoord destination, out string message)
        {
            BeginRootAction();
            message = "Перемещение недоступно.";
            if (HasPendingRetaliation)
            {
                message = "Сначала должен завершиться ответный удар.";
                return false;
            }

            SandboxUnitState unit = CurrentUnit;
            if (Phase != SandboxBattlePhase.InProgress || unit == null || unit.Id != unitId)
                return false;
            if (unit.ActionPoints <= 0 && unit.passive.MoveAfterActionBudget <= 0)
            {
                message = "У бойца не осталось действий.";
                return false;
            }
            if (unit.RemainingMovement <= 0)
            {
                message = "У бойца не осталось очков движения.";
                return false;
            }
            if (GetUnitAt(destination) != null)
            {
                message = "Гекс уже занят.";
                return false;
            }

            IReadOnlyDictionary<HexCoord, int> reachable = GetReachable(unitId);
            if (!reachable.ContainsKey(destination))
            {
                message = "Гекс находится вне доступного маршрута.";
                return false;
            }

            HexCoord origin = unit.Position;
            int movementCost = reachable[destination];
            // Бриф «Поверхности арены»: шаг за шагом — опасность клеток пути
            // один раз на вид за цикл; павший останавливается на своей клетке.
            bool leaveInterrupted = false;
            if (SandboxMovementPath.TryBuild(this, unitId, destination, out IReadOnlyList<HexCoord> steps, out _) && steps.Count > 1)
            {
                for (int i = 1; i < steps.Count && !unit.IsDefeated; i++)
                {
                    // Бриф завершения, этап 5: реакции на уход — до пересечения ребра.
                    if (!ResolveLeaveReactions(unit, steps[i - 1], steps[i]))
                    {
                        destination = unit.Position;
                        movementCost = destination == origin ? 0 : reachable.TryGetValue(destination, out int traveled) ? traveled : movementCost;
                        leaveInterrupted = true;
                        break;
                    }
                    SetUnitPosition(unit, steps[i]);
                    ApplyHazardsAt(unit, steps[i]);
                    if (!unit.IsDefeated)
                    {
                        AfterStatusStep(unit, steps[i]);
                        if (!unit.IsDefeated)
                            AfterPassiveStep(unit, steps[i - 1], steps[i]);
                    }
                }
                if (unit.IsDefeated)
                {
                    message = unit.DisplayLabel + " падает на пути." + string.Join(string.Empty, TakeEventLog().Select(entry => " " + entry));
                    EvaluateBattleOutcome();
                    if (Phase == SandboxBattlePhase.InProgress)
                        EndActivation();
                    return true;
                }
            }
            SetUnitPosition(unit, destination);
            if (unit.passive.MoveAfterActionBudget > 0 && unit.ActionPoints <= 0)
                unit.passive.MoveAfterActionBudget = 0;
            unit.MovedThisActivation = unit.MovedThisActivation || destination != origin;
            if (destination != origin)
                ResolvePursuers(unit, origin);
            if (destination != origin)
            {
                // Пакет состояний/тегов: темп, липкий отход, маскировка в движении.
                AfterStatusVoluntaryMove(unit, origin.DistanceTo(destination));
                AfterPassiveVoluntaryMove(unit, origin);
            }
            unit.RemainingMovement = Math.Max(0, unit.RemainingMovement - movementCost);
            message = unit.DisplayLabel + " перемещается " + origin + " → " + destination +
                      ". Осталось движения: " + unit.RemainingMovement + ".";
            message += AfterVoluntaryMove(unit);
            message += string.Join(string.Empty, TakeEventLog().Select(entry => " " + entry));
            if (IsMovementStopCell(unit, destination) && unit.ActionPoints > 0)
            {
                // Нить: движение обнулено, действие с места осталось.
                unit.RemainingMovement = 0;
                unit.ActsWithoutMovement = true;
                message += " Нить останавливает движение — можно ударить с места, встать в стойку или закончить ход.";
                return true;
            }
            if (leaveInterrupted && !unit.IsDefeated && unit.ActionPoints > 0)
            {
                unit.RemainingMovement = 0;
                unit.ActsWithoutMovement = true;
                message += " Движение прервано реакцией — можно действовать с места.";
                return true;
            }
            if (unit.RemainingMovement == 0)
            {
                message += " Движение исчерпано — активация завершена.";
                EndActivation();
            }
            return true;
        }

        public bool TryGetMeleeAttackPosition(
            string attackerId,
            string targetId,
            HexCoord requestedPosition,
            out int movementCost)
        {
            movementCost = 0;
            SandboxUnitState attacker = GetUnit(attackerId);
            SandboxUnitState target = GetUnit(targetId);
            if (!CanPrepareAttack(attacker, target) || attacker.AttackRange != 1 ||
                requestedPosition.DistanceTo(target.Position) != 1 ||
                !IsInside(requestedPosition) ||
                !IsEnterable(requestedPosition))
            {
                return false;
            }

            SandboxUnitState occupant = GetUnitAt(requestedPosition);
            if (occupant != null && occupant.Id != attacker.Id)
                return false;

            int routeCost = 0;
            if (requestedPosition != attacker.Position)
            {
                IReadOnlyDictionary<HexCoord, int> reachable = GetReachable(attackerId);
                if (!reachable.TryGetValue(requestedPosition, out routeCost))
                    return false;
            }

            int strikeStepCost = GetMovementCost(attacker, target.Position);
            bool fromPlaceWithoutMovement = attacker.ActsWithoutMovement && requestedPosition == attacker.Position;
            if (!fromPlaceWithoutMovement &&
                (strikeStepCost == int.MaxValue ||
                 routeCost + strikeStepCost > attacker.RemainingMovement))
            {
                return false;
            }

            movementCost = routeCost;
            return true;
        }

        public bool TryFindAttackPosition(
            string attackerId,
            string targetId,
            out HexCoord position,
            out int movementCost)
        {
            position = default;
            movementCost = 0;

            SandboxUnitState attacker = GetUnit(attackerId);
            SandboxUnitState target = GetUnit(targetId);
            if (!CanPrepareAttack(attacker, target))
                return false;

            position = attacker.Position;
            if (attacker.AttackRange == 1)
            {
                bool meleeFound = false;
                int bestMeleeCost = int.MaxValue;
                HexCoord bestMeleePosition = attacker.Position;
                foreach (HexCoord neighbor in target.Position.Neighbors())
                {
                    int candidateCost;
                    if (!TryGetMeleeAttackPosition(
                            attackerId,
                            targetId,
                            neighbor,
                            out candidateCost))
                    {
                        continue;
                    }

                    if (!meleeFound || candidateCost < bestMeleeCost ||
                        (candidateCost == bestMeleeCost && neighbor.CompareTo(bestMeleePosition) < 0))
                    {
                        meleeFound = true;
                        bestMeleeCost = candidateCost;
                        bestMeleePosition = neighbor;
                    }
                }

                if (!meleeFound)
                    return false;

                position = bestMeleePosition;
                movementCost = bestMeleeCost;
                return true;
            }

            // Ослепление / Забывчивость стрелка: дальний выстрел только вплотную.
            bool rangedBlocked = !string.IsNullOrEmpty(GetStatusTargetBlock(attacker, target, ranged: true));
            if (attacker.Position.DistanceTo(target.Position) <= attacker.AttackRange &&
                CanShootFrom(attacker.Position, target.Position) &&
                (!rangedBlocked || attacker.Position.DistanceTo(target.Position) <= 1))
                return true;

            bool found = false;
            int bestCost = int.MaxValue;
            HexCoord best = attacker.Position;
            foreach (KeyValuePair<HexCoord, int> pair in GetReachable(attackerId))
            {
                if (pair.Value >= attacker.RemainingMovement ||
                    pair.Key.DistanceTo(target.Position) > attacker.AttackRange ||
                    (rangedBlocked && pair.Key.DistanceTo(target.Position) > 1) ||
                    !CanShootFrom(pair.Key, target.Position))
                    continue;

                if (!found || pair.Value < bestCost ||
                    (pair.Value == bestCost && pair.Key.CompareTo(best) < 0))
                {
                    found = true;
                    best = pair.Key;
                    bestCost = pair.Value;
                }
            }

            if (!found)
                return false;

            position = best;
            movementCost = bestCost;
            return true;
        }

        public SandboxAttackPreview PreviewReachableAttack(string attackerId, string targetId)
        {
            HexCoord attackPosition;
            int movementCost;
            if (!TryFindAttackPosition(
                    attackerId,
                    targetId,
                    out attackPosition,
                    out movementCost))
            {
                return SandboxAttackPreview.Invalid("Цель находится вне доступной зоны атаки.");
            }

            return BuildAttackPreview(GetUnit(attackerId), GetUnit(targetId), attackPosition);
        }

        public SandboxAttackPreview PreviewAttack(string attackerId, string targetId)
        {
            if (HasPendingRetaliation)
                return SandboxAttackPreview.Invalid("Сначала должен завершиться ответный удар.");
            if (Phase != SandboxBattlePhase.InProgress)
                return SandboxAttackPreview.Invalid("Бой уже завершён.");

            SandboxUnitState attacker = GetUnit(attackerId);
            SandboxUnitState target = GetUnit(targetId);
            if (attacker == null || target == null || attacker.IsDefeated || target.IsDefeated)
                return SandboxAttackPreview.Invalid("Цель недоступна.");
            if (CurrentUnit == null || CurrentUnit.Id != attackerId)
                return SandboxAttackPreview.Invalid("Сейчас ход другого участника.");
            if (attacker.Team == target.Team)
                return SandboxAttackPreview.Invalid("Нельзя атаковать союзника.");
            if (attacker.ActionPoints <= 0)
                return SandboxAttackPreview.Invalid("Не осталось действий.");
            if (attacker.RemainingMovement <= 0 && !attacker.ActsWithoutMovement)
                return SandboxAttackPreview.Invalid("Движение исчерпано, активация завершена.");
            if (attacker.IsOffBoard)
                return SandboxAttackPreview.Invalid("Сейчас не может атаковать отдельно.");
            if (target.IsUnderground || target.AwaitingPlacement)
                return SandboxAttackPreview.Invalid("Цель под землёй — по ней не ударить.");
            if (attacker.HasAttacked)
                return SandboxAttackPreview.Invalid("Обычная атака уже использована.");
            int distance = attacker.Position.DistanceTo(target.Position);
            if (distance > attacker.AttackRange)
                return SandboxAttackPreview.Invalid("Цель вне дальности.");
            if (distance == 0 && !target.IsStuckInTunnel)
                return SandboxAttackPreview.Invalid("Цель сидит на этом бойце — снимите её действием.");
            // Бриф «Поверхности арены»: дальний выстрел — по общей линии огня.
            if (distance > 1)
            {
                SandboxLineOfFire line = GetLineOfFire(attacker.Position, target.Position);
                if (!line.Clear)
                    return SandboxAttackPreview.Invalid("Нет линии огня: " + line.Reason + ".");
            }
            // Пакет состояний: тот же запрос допустимости, что у ИИ и способностей.
            string statusBlock = GetStatusTargetBlock(attacker, target, distance > 1);
            if (!string.IsNullOrEmpty(statusBlock))
                return SandboxAttackPreview.Invalid(statusBlock);

            return BuildAttackPreview(attacker, target, attacker.Position);
        }

        public bool TryAttack(string attackerId, string targetId, out string message)
        {
            BeginRootAction();
            SandboxAttackPreview preview = PreviewAttack(attackerId, targetId);
            if (!preview.IsValid)
            {
                message = preview.Reason;
                return false;
            }

            SandboxUnitState attacker = GetUnit(attackerId);
            SandboxUnitState target = GetUnit(targetId);
            ClearPendingRetaliation();
            // Этап 10: «Под защитой союзника» — удар уходит защитнику до расчёта урона.
            SandboxUnitState intercepted = InterceptTarget(attacker, target);
            string interceptNote = string.Empty;
            if (intercepted != target)
            {
                interceptNote = intercepted.DisplayLabel + " закрывает " + target.DisplayLabel + " собой. ";
                target = intercepted;
                preview = BuildAttackPreview(attacker, target, attacker.Position);
            }
            string revealNote = interceptNote + RevealAttacker(attacker);

            // «Упреждающий удар»: носитель в защитной стойке отвечает на ближнюю
            // атаку до удара противника.
            string firstStrike = string.Empty;
            if (((target.IsGuarding && target.HasPerk(SandboxPerks.FirstStrike)) || HasPassive(target, SandboxPassiveKind.RetaliateFirst)) && target.CanRetaliate &&
                attacker.AttackRange == 1 && attacker.Position.DistanceTo(target.Position) == 1)
            {
                int counter = BuildAttackPreview(target, attacker, target.Position, retaliation: true).Damage;
                target.RetaliationsThisRound++;
                target.RetaliationsMade++;
                RecordHit(target, attacker, counter, target.Position);
                target.UsedMeleeAttack = true;
                bool attackerStanding = attacker.StillStandingUsed;
                ApplyHit(SandboxHitKind.FirstStrike, target, attacker, counter);
                firstStrike = target.DisplayLabel + " бьёт первым («Упреждающий удар») и наносит " + counter + " урона" +
                              (attacker.IsDefeated ? " — " + attacker.DisplayLabel + " выведен из строя." : StillStandingNote(attacker, attackerStanding) + ".") + " ";
                if (attacker.IsDefeated)
                {
                    attacker.HasAttacked = true;
                    attacker.ActionPoints = 0;
                    attacker.RemainingMovement = 0;
                    message = firstStrike.Trim();
                    EvaluateBattleOutcome();
                    return true;
                }
                preview = BuildAttackPreview(attacker, target, attacker.Position);
            }

            RecordHit(attacker, target, preview.Damage, attacker.Position);
            if (attacker.Position.DistanceTo(target.Position) > 1)
            {
                attacker.UsedRangedAttack = true;
                attacker.ShotFromPlace = attacker.ShotFromPlace || !attacker.MovedThisActivation;
            }
            else
            {
                attacker.UsedMeleeAttack = true;
            }
            bool targetStanding = target.StillStandingUsed;
            string targetLabel = target.DisplayLabel;
            string packNote = PackNote(attacker, target, attacker.Position);
            ApplyHit(SandboxHitKind.Attack, attacker, target, preview.Damage);
            target.TimesAttackedThisRound++;
            target.TimesAttacked++;
            attacker.ActionPoints--;
            attacker.RemainingMovement = 0;
            attacker.HasAttacked = true;

            message = revealNote + firstStrike + attacker.DisplayLabel + " наносит " + targetLabel + " " +
                      preview.Damage + " урона" + packNote +
                      (target.IsDefeated ? " и выводит цель из строя." : StillStandingNote(target, targetStanding) + ".");
            message += RevealTarget(target);

            EvaluateBattleOutcome();
            bool rangedAttack = attacker.Position.DistanceTo(target.Position) > 1;
            if (!rangedAttack)
                attacker.passive.HitAndRunTargets.Add(target.Id);
            // Бриф завершения, этапы 5–6: месть соседа, второй выстрел сразу.
            ResolveAvengers(attacker, target);
            if (rangedAttack)
                message += ResolveFollowUpHit(attacker, GetUnit(targetId) ?? target, true);
            EvaluateBattleOutcome();
            if (Phase == SandboxBattlePhase.InProgress &&
                attacker.AttackRange == 1 &&
                !target.IsDefeated && !attacker.IsDefeated &&
                !HasPassive(attacker, SandboxPassiveKind.NoProvokedRetaliation) &&
                target.CanRetaliate &&
                attacker.Position.DistanceTo(target.Position) == 1)
            {
                pendingRetaliationDefenderId = target.Id;
                pendingRetaliationAttackerId = attacker.Id;
                pendingSequenceAttackerId = attacker.Id;
                pendingSequenceTargetId = target.Id;
            }
            else if (Phase != SandboxBattlePhase.InProgress)
            {
                ClearPendingRetaliation();
            }
            else if (!rangedAttack)
            {
                message += FinishMeleeSequence(attacker, target);
            }

            return true;
        }

        private string pendingSequenceAttackerId;
        private string pendingSequenceTargetId;

        // После ответа (или без него): второе ближнее попадание и «Удар и возврат».
        private string FinishMeleeSequence(SandboxUnitState attacker, SandboxUnitState target)
        {
            pendingSequenceAttackerId = null;
            pendingSequenceTargetId = null;
            if (attacker == null || attacker.IsDefeated || Phase != SandboxBattlePhase.InProgress)
                return string.Empty;
            string note = ResolveFollowUpHit(attacker, target, false);
            if (HasPassive(attacker, SandboxPassiveKind.StrikeAndReturn) && CurrentUnit == attacker &&
                attacker.Position != attacker.passive.ActivationStartPosition && IsFreeCell(attacker.passive.ActivationStartPosition) &&
                attacker.MovedThisActivation)
            {
                HexCoord back = attacker.passive.ActivationStartPosition;
                SetUnitPosition(attacker, back);
                note += " " + attacker.DisplayLabel + " возвращается на " + back + " («Удар и возврат»).";
            }
            EvaluateBattleOutcome();
            return note;
        }

        public SandboxAttackPreview PreviewPendingRetaliation()
        {
            if (!HasPendingRetaliation)
                return SandboxAttackPreview.Invalid("Ответный удар не ожидается.");
            if (Phase != SandboxBattlePhase.InProgress)
                return SandboxAttackPreview.Invalid("Бой уже завершён.");

            SandboxUnitState defender = GetUnit(pendingRetaliationDefenderId);
            SandboxUnitState attacker = GetUnit(pendingRetaliationAttackerId);
            if (defender == null || attacker == null ||
                defender.IsDefeated || attacker.IsDefeated ||
                defender.Team == attacker.Team ||
                defender.HasRetaliatedThisRound ||
                defender.Position.DistanceTo(attacker.Position) != 1)
            {
                return SandboxAttackPreview.Invalid("Ответный удар больше недоступен.");
            }

            return BuildAttackPreview(defender, attacker, defender.Position, retaliation: true);
        }

        public bool TryResolvePendingRetaliation(out string message)
        {
            SandboxAttackPreview preview = PreviewPendingRetaliation();
            if (!preview.IsValid)
            {
                message = preview.Reason;
                ClearPendingRetaliation();
                string after = ResolveAfterReaction();
                if (!string.IsNullOrEmpty(after))
                    message = after.Trim();
                return !string.IsNullOrEmpty(after);
            }

            SandboxUnitState defender = GetUnit(pendingRetaliationDefenderId);
            SandboxUnitState attacker = GetUnit(pendingRetaliationAttackerId);
            defender.RetaliationsThisRound++;
            defender.RetaliationsMade++;
            RecordHit(defender, attacker, preview.Damage, defender.Position);
            defender.UsedMeleeAttack = true;
            bool attackerStanding = attacker.StillStandingUsed;
            ApplyHit(SandboxHitKind.Retaliation, defender, attacker, preview.Damage);
            ClearPendingRetaliation();

            message = defender.DisplayLabel + " отвечает " + attacker.DisplayLabel + " и наносит " +
                      preview.Damage + " урона" +
                      (attacker.IsDefeated ? " и выводит атакующего из строя." : StillStandingNote(attacker, attackerStanding) + ".");
            message += ResolveAfterReaction();
            if (pendingSequenceAttackerId == attacker.Id)
                message += FinishMeleeSequence(attacker, GetUnit(pendingSequenceTargetId));
            EvaluateBattleOutcome();
            return true;
        }

        public bool TryGuard(string unitId, out string message)
        {
            message = "Защитная стойка недоступна.";
            if (HasPendingRetaliation)
            {
                message = "Сначала должен завершиться ответный удар.";
                return false;
            }

            SandboxUnitState unit = CurrentUnit;
            if (Phase != SandboxBattlePhase.InProgress || unit == null || unit.Id != unitId)
                return false;
            if (unit.ActionPoints <= 0 || unit.IsGuarding || unit.IsOffBoard)
                return false;

            unit.ActionPoints--;
            unit.RemainingMovement = 0;
            unit.IsGuarding = true;
            message = unit.DisplayLabel + " занимает защитную стойку: защита +" +
                      SandboxCombatTagRules.GetGuardDefensePercent(unit) +
                      "% до следующей активации.";
            return true;
        }

        public void EndActivation()
        {
            if (HasPendingRetaliation ||
                Phase != SandboxBattlePhase.InProgress || CurrentUnit == null)
            {
                return;
            }

            CurrentUnit.ActionPoints = 0;
            CurrentUnit.RemainingMovement = 0;
            AdvanceTurnIndex();
        }

        public SandboxUnitState FindClosestOpponent(SandboxUnitState unit)
        {
            if (unit == null)
                return null;

            SandboxUnitState attackable = FindBestReachableAttackTarget(unit);
            if (attackable != null)
                return attackable;

            return units
                .Where(candidate => !candidate.IsDefeated && candidate.Team != unit.Team && !candidate.IsUnderground && !candidate.AwaitingPlacement)
                .OrderBy(candidate => unit.Position.DistanceTo(candidate.Position))
                .ThenBy(candidate => candidate.HitPoints)
                .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        public HexCoord FindBestMoveToward(SandboxUnitState unit, HexCoord target)
        {
            SandboxUnitState targetUnit = GetUnitAt(target);
            if (unit != null && targetUnit != null && targetUnit.Team != unit.Team)
            {
                HexCoord plannedAttackPosition;
                int plannedMovementCost;
                if (TryFindAttackPosition(
                        unit.Id,
                        targetUnit.Id,
                        out plannedAttackPosition,
                        out plannedMovementCost))
                {
                    return plannedAttackPosition;
                }
            }

            IReadOnlyDictionary<HexCoord, int> reachable = GetReachable(unit.Id);
            // ПР-12К: на большом поле (вся локация) — по длине пути в обход
            // стен, а не по прямой: иначе противник упирается в стену.
            // Бриф «Поверхности арены», этап 4: на компактной арене с
            // местностью — по цене пути этого бойца (болотный ходок не
            // обходит топь), с штрафом за опасную клетку.
            Dictionary<HexCoord, int> path = !usesCompactArenaShape ? PathDistances(target)
                : UsesTerrainPathing ? TravelCosts(unit, target) : null;
            int Distance(HexCoord cell) =>
                (path != null && path.TryGetValue(cell, out int steps) ? steps : path != null ? 100000 + cell.DistanceTo(target) : cell.DistanceTo(target)) +
                HazardAt(unit, cell) * 3;
            HexCoord best = unit.Position;
            int bestDistance = Distance(best);
            int bestCost = 0;

            foreach (KeyValuePair<HexCoord, int> pair in reachable)
            {
                int distance = Distance(pair.Key);
                if (distance < bestDistance ||
                    (distance == bestDistance && pair.Value < bestCost) ||
                    (distance == bestDistance && pair.Value == bestCost && pair.Key.CompareTo(best) < 0))
                {
                    best = pair.Key;
                    bestDistance = distance;
                    bestCost = pair.Value;
                }
            }

            return best;
        }

        // Длина пути до клетки target по проходимым клеткам (без учёта фигур).
        private Dictionary<HexCoord, int> PathDistances(HexCoord target)
        {
            Dictionary<HexCoord, int> distances = new Dictionary<HexCoord, int> { { target, 0 } };
            Queue<HexCoord> frontier = new Queue<HexCoord>();
            frontier.Enqueue(target);
            while (frontier.Count > 0)
            {
                HexCoord current = frontier.Dequeue();
                int next = distances[current] + 1;
                foreach (HexCoord neighbor in current.Neighbors())
                {
                    if (distances.ContainsKey(neighbor) || !IsInside(neighbor) || !IsEnterable(neighbor))
                        continue;
                    distances[neighbor] = next;
                    frontier.Enqueue(neighbor);
                }
            }
            return distances;
        }

        private SandboxUnitState FindBestReachableAttackTarget(SandboxUnitState attacker)
        {
            SandboxUnitState best = null;
            int bestHitPoints = int.MaxValue;
            int bestDamage = int.MinValue;
            int bestMovementCost = int.MaxValue;

            foreach (SandboxUnitState candidate in units)
            {
                if (candidate.IsDefeated || candidate.Team == attacker.Team)
                    continue;

                HexCoord position;
                int movementCost;
                if (!TryFindAttackPosition(
                        attacker.Id,
                        candidate.Id,
                        out position,
                        out movementCost))
                {
                    continue;
                }

                SandboxAttackPreview preview = PreviewReachableAttack(attacker.Id, candidate.Id);
                if (!preview.IsValid)
                    continue;

                bool better =
                    best == null ||
                    candidate.HitPoints < bestHitPoints ||
                    (candidate.HitPoints == bestHitPoints && preview.Damage > bestDamage) ||
                    (candidate.HitPoints == bestHitPoints && preview.Damage == bestDamage &&
                     movementCost < bestMovementCost) ||
                    (candidate.HitPoints == bestHitPoints && preview.Damage == bestDamage &&
                     movementCost == bestMovementCost &&
                     string.CompareOrdinal(candidate.Id, best.Id) < 0);

                if (!better)
                    continue;

                best = candidate;
                bestHitPoints = candidate.HitPoints;
                bestDamage = preview.Damage;
                bestMovementCost = movementCost;
            }

            return best;
        }

        private void ValidateInitialState()
        {
            HashSet<string> ids = new HashSet<string>();
            HashSet<HexCoord> occupied = new HashSet<HexCoord>();
            foreach (SandboxUnitState unit in units)
            {
                if (!ids.Add(unit.Id))
                    throw new ArgumentException("ID участников боя должны быть уникальными.");
                if (!IsInside(unit.Position))
                    throw new ArgumentException("Участник размещён вне поля: " + unit.Id);
                if (GetTerrain(unit.Position) == SandboxTerrain.Impassable)
                    throw new ArgumentException("Участник размещён на непроходимом гексе: " + unit.Id);
                if (!occupied.Add(unit.Position))
                    throw new ArgumentException("Два участника занимают один стартовый гекс.");
            }

            if (!units.Any(unit => unit.Team == SandboxTeam.Player) ||
                !units.Any(unit => unit.Team == SandboxTeam.Enemy))
            {
                throw new ArgumentException("Для боя нужны обе стороны.");
            }
        }

        private void BuildRoundOrder()
        {
            turnOrderIds.Clear();
            turnOrderIds.AddRange(
                units
                    .Where(unit => (!unit.IsDefeated || unit.IsDying) && unit.ActsFromRound <= Round)
                    .OrderByDescending(unit => GetRoundInitiative(unit))
                    .ThenBy(unit => unit.Team)
                    .ThenBy(unit => unit.Id, StringComparer.Ordinal)
                    .Select(unit => unit.Id));
        }

        private void AdvanceTurnIndex()
        {
            SandboxUnitState ending = CurrentUnit;
            if (ending != null)
                ProcessActivationEnd(ending);
            EvaluateBattleOutcome();
            if (Phase != SandboxBattlePhase.InProgress)
                return;

            currentTurnIndex++;
            BeginCurrentActivationOrAdvance();
        }

        private void BeginCurrentActivationOrAdvance()
        {
            while (Phase == SandboxBattlePhase.InProgress)
            {
                if (currentTurnIndex >= turnOrderIds.Count)
                {
                    Round++;
                    ResetRoundRetaliations();
                    ProcessRoundStart();
                    BuildRoundOrder();
                    currentTurnIndex = 0;
                }

                if (turnOrderIds.Count == 0)
                {
                    EvaluateBattleOutcome();
                    return;
                }

                SandboxUnitState candidate = GetUnit(turnOrderIds[currentTurnIndex]);
                if (candidate != null && candidate.IsDying)
                {
                    // Окно спасения существует в часах: пропущенная активация лежащего.
                    ProcessDownedActivation(candidate);
                    EvaluateBattleOutcome();
                    currentTurnIndex++;
                    continue;
                }
                if (candidate != null && !candidate.IsDefeated)
                {
                    candidate.BeginActivation();
                    BeginRootAction();
                    // Отложенные события, тики, регенерация и ограничения
                    // состояний — до выбора действия (бриф §15.2).
                    bool keepsTurn = ProcessActivationStart(candidate);
                    EvaluateBattleOutcome();
                    if (Phase != SandboxBattlePhase.InProgress)
                        return;
                    if (keepsTurn && !candidate.IsDefeated)
                        return;
                    // Активация закончилась сама (клещень после тика) или
                    // боец пал от отложенного события.
                    if (!candidate.IsDefeated)
                        ProcessActivationEnd(candidate);
                    EvaluateBattleOutcome();
                    currentTurnIndex++;
                    continue;
                }

                currentTurnIndex++;
            }
        }

        // ПР-10: бой кампании — пал вождь (герой), бой проигран, даже если
        // другие ещё стоят.
        public string LeaderUnitId { get; set; }

        // 12Е-6, «Засада»: бой начат подготовленно — отряд игрока получает
        // прибавку к инициативе в первом раунде.
        public int PlayerFirstRoundInitiativeBonus { get; set; }

        // Бриф «Поверхности арены», этап 4: отчёт генератора темы (null — бой
        // без темы: прежний генератор или авторская раскладка).
        public SandboxArenaReport ArenaReport { get; set; }

        public int GetRoundInitiative(SandboxUnitState unit)
        {
            return unit.Initiative + StatusInitiativeMod(unit) + PassiveInitiativeMod(unit) + (Round <= 1 && unit.Team == SandboxTeam.Player ? PlayerFirstRoundInitiativeBonus : 0);
        }

        private void EvaluateBattleOutcome()
        {
            // ПР-16: погибшие от тиков и событий — та же очистка связей,
            // объявленных угроз и объектов, что и после удара.
            foreach (SandboxUnitState fallen in units.Where(unit => unit.IsDefeated && !defeatProcessed.Contains(unit.Id)).ToList())
                OnUnitDefeated(fallen);

            bool playersAlive = units.Any(unit => unit.Team == SandboxTeam.Player && !unit.IsDefeated);
            bool enemiesAlive = units.Any(unit => unit.Team == SandboxTeam.Enemy && !unit.IsDefeated);
            // Этап 12: ожидающий враждебный кокон — видимая оставшаяся угроза.
            enemiesAlive |= objects.Any(o => o.Kind == SandboxObjectKind.Cocoon && !o.IsDestroyed && o.SnapshotTeam == SandboxTeam.Enemy);
            bool leaderFell = !string.IsNullOrEmpty(LeaderUnitId) &&
                              units.Any(unit => unit.Id == LeaderUnitId && unit.IsDefeated);

            if (leaderFell)
                Phase = SandboxBattlePhase.EnemyVictory;
            else if (!enemiesAlive)
                Phase = SandboxBattlePhase.PlayerVictory;
            else if (!playersAlive)
                Phase = SandboxBattlePhase.EnemyVictory;

            if (Phase != SandboxBattlePhase.InProgress)
            {
                ClearPendingRetaliation();
                ClearStatusesAtBattleEnd();
            }
        }

        private void ResetRoundRetaliations()
        {
            ClearPendingRetaliation();
            foreach (SandboxUnitState unit in units)
            {
                unit.RetaliationsThisRound = 0;
                unit.TimesAttackedThisRound = 0;
            }
        }

        private void ClearPendingRetaliation()
        {
            pendingRetaliationDefenderId = null;
            pendingRetaliationAttackerId = null;
        }

        // Урон применяется один раз, здесь же пишется запись удара (ПР-12З).
        private void ApplyHit(SandboxHitKind kind, SandboxUnitState striker, SandboxUnitState target, int damage)
        {
            int before = target.HitPoints;
            // Пакет состояний: щит, неуязвимость, перенос, временные HP, расход зарядов.
            int toHp = ResolveDirectHitBeforeHp(kind, striker, target, damage, out int temporaryAbsorbed);
            toHp = PassiveCheatDeath(kind, target, toHp);
            // Этап 11: положительный остаток повреждения по лежащему — окончательная смерть.
            if (target.IsDying)
            {
                if (toHp > 0)
                    FinalizeDeath(target, striker.DisplayLabel + " добивает");
                return;
            }
            target.ReceiveDamage(toHp);
            ResolveDirectHitAfterHp(kind, striker, target, before - target.HitPoints, temporaryAbsorbed);
            OnPassiveDirectHit(kind, striker, target, before, before - target.HitPoints);
            if (IsDirectHit(kind))
                AfterTypedHit(striker, target, before - target.HitPoints, hitDamageType);
            int afterMitigation = toHp + temporaryAbsorbed;
            Emit(SandboxCombatEventKind.HitResolved, striker, target, e =>
            {
                e.Requested = damage; e.AfterMitigation = afterMitigation; e.AbsorbedTemporary = temporaryAbsorbed;
                e.ActualHp = before - target.HitPoints; e.IsRetaliation = kind == SandboxHitKind.Retaliation || kind == SandboxHitKind.FirstStrike;
                e.Delivery = kind == SandboxHitKind.Environment ? SandboxDeliveryKind.Surface
                    : hitMagical ? SandboxDeliveryKind.Spell
                    : striker.Position.DistanceTo(target.Position) > 1 ? SandboxDeliveryKind.Ranged : SandboxDeliveryKind.Melee;
                e.DamageType = hitDamageType;
                e.IsMagical = hitMagical;
            });
            hitRecords.Add(new SandboxHitRecord(
                kind,
                striker.Id,
                target.Id,
                striker.Position,
                target.Position,
                damage,
                before,
                target.HitPoints));
            if (target.IsDefeated)
                OnUnitDefeated(target);
        }

        // Учёт вклада до нанесения удара: сколько HP реально снято и сколько
        // урона не пропустила защитная стойка цели.
        private void RecordHit(SandboxUnitState striker, SandboxUnitState target, int damage, HexCoord attackPosition)
        {
            int before = target.HitPoints;
            striker.DamageDealt += Math.Min(Math.Max(0, damage), before);
            if (!target.IsGuarding)
                return;

            target.IsGuarding = false;
            int unguarded = BuildAttackPreview(striker, target, attackPosition).Damage;
            target.IsGuarding = true;
            int prevented = Math.Min(unguarded, before) - Math.Min(damage, before);
            if (prevented > 0)
                target.DamagePrevented += prevented;
        }

        private SandboxAttackPreview BuildAttackPreview(
            SandboxUnitState attacker,
            SandboxUnitState target,
            HexCoord attackPosition,
            bool retaliation = false,
            decimal abilityMultiplier = 1m)
        {
            decimal effectiveAttack = SandboxCombatTagRules.GetEffectiveAttack(
                attacker,
                target,
                attackPosition);
            // ПР-16, тег «Стая»: +1 Атака при контактной атаке, если рядом с
            // целью стоит другой живой носитель тега.
            if (HasPackBonus(attacker, target, attackPosition))
                effectiveAttack += SandboxCombatTagRules.PackAttackBonus;
            bool rangedHit = attackPosition.DistanceTo(target.Position) > 1;
            if (halfAttackHit)
                effectiveAttack *= 0.5m;
            // Пакет состояний: временные модификаторы Атаки (предел ±2).
            effectiveAttack = Math.Max(0m, effectiveAttack + StatusAttackMod(attacker, target, rangedHit, retaliation: retaliation));
            // Пакет тегов: постоянные условия — отдельно от предела состояний.
            effectiveAttack = Math.Max(0m, effectiveAttack + PassiveAttackMod(attacker, target, rangedHit, attackPosition));
            decimal effectiveDefense = SandboxCombatTagRules.GetEffectiveDefense(target);
            effectiveDefense = Math.Max(0m, effectiveDefense + PassiveDefenseMod(target, attacker, rangedHit) - PassiveIgnoreDefense(attacker, target, rangedHit));
            // Бриф «Поверхности арены»: укрытие леса/зарослей — только от
            // обычного дальнего выстрела (не ответный удар, не способность).
            if (!retaliation && abilityMultiplier == 1m)
                effectiveDefense += GetCoverBonus(target, attackPosition, projectile: true);

            // 12Е-6: «Основная защита» — первая атака по носителю за раунд −1 Атака.
            if (!retaliation && target.HasPerk(SandboxPerks.BasicDefense) && target.TimesAttackedThisRound == 0)
                effectiveAttack = Math.Max(0m, effectiveAttack - 1m);
            // «Холодный глаз» — стрелок, не двигавшийся в этот ход, игнорирует 1 Защиту цели.
            if (attacker.HasPerk(SandboxPerks.ColdEye) && !attacker.MovedThisActivation && attackPosition == attacker.Position &&
                attackPosition.DistanceTo(target.Position) > 1)
                effectiveDefense = Math.Max(0m, effectiveDefense - 1m);
            decimal statDifference = effectiveAttack - effectiveDefense;
            decimal damageMultiplier;

            if (statDifference > 0m)
            {
                damageMultiplier = Math.Min(
                    5m,
                    1m + statDifference * 0.25m);
            }
            else if (statDifference < 0m)
            {
                damageMultiplier = Math.Max(
                    0.3m,
                    1m - (-statDifference * 0.125m));
            }
            else
            {
                damageMultiplier = 1m;
            }

            int baseDamage = StatusBaseDamage(attacker, rangedHit);
            int damage = Math.Max(
                1,
                (int)Math.Floor(baseDamage * damageMultiplier));
            if (abilityMultiplier != 1m)
            {
                // Модификатор способности — ровно один раз, к базовому Урону;
                // усиление не съедается округлением (минимум +1 к обычному).
                int normal = damage;
                damage = Math.Max(1, (int)Math.Floor(baseDamage * damageMultiplier * abilityMultiplier));
                if (abilityMultiplier > 1m && damage <= normal)
                    damage = normal + 1;
            }
            // «Контрудар II» — первый ответный удар за раунд +1 Урон.
            if (retaliation && attacker.HasPerk(SandboxPerks.CounterstrikeDamage) && attacker.RetaliationsThisRound == 0)
                damage++;
            // Пакет состояний: плоские изменения итогового урона (+2 / −1, минимум 1).
            damage = Math.Max(1, damage + StatusFlatDamage(attacker, target, rangedHit, hitDamageType, consume: false));
            damage = PassiveDamage(attacker, target, rangedHit, damage, consume: false);

            SandboxAttackPreview result = new SandboxAttackPreview(
                true,
                string.Empty,
                damage,
                Math.Max(0, target.HitPoints - damage));
            EnrichPreview(result, attacker, target, rangedHit);
            return result;
        }

        private static string StillStandingNote(SandboxUnitState unit, bool usedBefore)
        {
            return !usedBefore && unit.StillStandingUsed ? " — " + unit.DisplayLabel + " остаётся на ногах («Ещё на ногах»)" : string.Empty;
        }

        private bool CanPrepareAttack(
            SandboxUnitState attacker,
            SandboxUnitState target)
        {
            return !HasPendingRetaliation &&
                   Phase == SandboxBattlePhase.InProgress &&
                   attacker != null && target != null &&
                   !attacker.IsDefeated && !target.IsDefeated &&
                   CurrentUnit != null && CurrentUnit.Id == attacker.Id &&
                   attacker.Team != target.Team &&
                   attacker.ActionPoints > 0 &&
                   (attacker.RemainingMovement > 0 || attacker.ActsWithoutMovement) &&
                   !attacker.HasAttacked && !attacker.IsOffBoard &&
                   !target.IsUnderground && !target.AwaitingPlacement &&
                   string.IsNullOrEmpty(GetStatusTargetBlock(attacker, target, ranged: false));
        }

        private int GetMovementCost(SandboxUnitState unit, HexCoord position)
        {
            return IsEnterableFor(unit, position) ? GetStepCost(unit, position) : int.MaxValue;
        }
    }

    public static class SandboxEnemyPlanner
    {
        public static IReadOnlyList<string> TakeCurrentTurn(SandboxBattle battle)
        {
            List<string> events = new List<string>();
            SandboxUnitState enemy = battle != null ? battle.CurrentUnit : null;
            if (enemy == null || enemy.Team != SandboxTeam.Enemy)
                return events;
            // ПР-16: единый планировщик и исполнитель (способности, профили).
            return SandboxAiExecutor.TakeTurn(battle);
#pragma warning disable CS0162

            SandboxUnitState target = SelectAttackTarget(battle, enemy);
            if (target == null)
            {
                SandboxUnitState closest = battle.FindClosestOpponent(enemy);
                if (closest != null)
                {
                    HexCoord destination = battle.FindBestMoveToward(enemy, closest.Position);
                    if (destination != enemy.Position)
                    {
                        string moveMessage;
                        if (battle.TryMove(enemy.Id, destination, out moveMessage))
                            events.Add(moveMessage);
                    }
                }
            }

            if (battle.Phase == SandboxBattlePhase.InProgress)
            {
                target = SelectAttackTarget(battle, enemy);
                if (target != null)
                {
                    string attackMessage;
                    if (battle.TryAttack(enemy.Id, target.Id, out attackMessage))
                    {
                        events.Add(attackMessage);
                        if (battle.HasPendingRetaliation)
                        {
                            string retaliationMessage;
                            if (battle.TryResolvePendingRetaliation(out retaliationMessage))
                                events.Add(retaliationMessage);
                        }
                    }
                }
            }

            if (battle.Phase == SandboxBattlePhase.InProgress &&
                battle.CurrentUnit != null && battle.CurrentUnit.Id == enemy.Id &&
                enemy.ActionPoints > 0)
            {
                string guardMessage;
                if (battle.TryGuard(enemy.Id, out guardMessage))
                    events.Add(guardMessage);
            }

            if (battle.Phase == SandboxBattlePhase.InProgress &&
                battle.CurrentUnit != null && battle.CurrentUnit.Id == enemy.Id)
            {
                battle.EndActivation();
            }

            return events;
#pragma warning restore CS0162
        }

        private static SandboxUnitState SelectAttackTarget(
            SandboxBattle battle,
            SandboxUnitState attacker)
        {
            return battle.Units
                .Where(unit => !unit.IsDefeated && unit.Team != attacker.Team)
                .Select(unit => new
                {
                    Unit = unit,
                    Preview = battle.PreviewAttack(attacker.Id, unit.Id)
                })
                .Where(candidate => candidate.Preview.IsValid)
                .OrderBy(candidate => candidate.Unit.HitPoints)
                .ThenByDescending(candidate => candidate.Preview.Damage)
                .ThenBy(candidate => candidate.Unit.Id, StringComparer.Ordinal)
                .Select(candidate => candidate.Unit)
                .FirstOrDefault();
        }
    }
}
