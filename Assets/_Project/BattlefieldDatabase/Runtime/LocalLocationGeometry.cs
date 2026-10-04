using System;
using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattleSandbox;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12К (канон v1.53 §28.3): геометрия исследуемого места — одна для
    // исследования и боя. Проходимость = клетка арены, не отключённая на
    // поле (стена) и не занятая объектом места.
    public sealed class LocalLocationGeometry
    {
        public LocalLocationDefinition Definition { get; }
        public BattlefieldDefinitionData Battlefield { get; }
        private readonly HashSet<HexCoord> blocked = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> difficult = new HashSet<HexCoord>();

        public LocalLocationGeometry(LocalLocationDefinition definition, BattlefieldDefinitionData battlefield, IEnumerable<HexCoord> visualBlockedCells = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Battlefield = battlefield;
            if (visualBlockedCells != null)
                foreach (HexCoord cell in visualBlockedCells)
                    blocked.Add(cell);
            if (battlefield != null)
            {
                foreach (HexCoord cell in BattlefieldFrame.DisabledCells(battlefield))
                    blocked.Add(cell);
            }
            foreach (LocalObjectDefinition item in definition.Objects)
            {
                if (item != null && item.Cell != null)
                    blocked.Add(Cell(item.Cell));
            }
            foreach (LocalCellData cell in definition.DifficultCells)
            {
                if (cell != null)
                    difficult.Add(Cell(cell));
            }
        }

        public static HexCoord Cell(LocalCellData cell) => new HexCoord(cell.Q, cell.R);

        public bool IsPassable(HexCoord cell) => SandboxArenaShape.Contains(cell) && !blocked.Contains(cell);

        public int StepCost(HexCoord cell) => difficult.Contains(cell) ? 2 : 1;

        public IReadOnlyCollection<HexCoord> BlockedCells => blocked;

        // Клетки, с которых можно взаимодействовать с объектом: проходимые соседи.
        public List<HexCoord> InteractionCells(LocalObjectDefinition item)
        {
            return Cell(item.Cell).Neighbors().Where(IsPassable).ToList();
        }

        public List<HexCoord> FindPath(HexCoord from, HexCoord to)
        {
            return SandboxLocalNavigation.FindPath(from, to, IsPassable, StepCost);
        }

        public HashSet<HexCoord> Region(HexCoord start) => SandboxLocalNavigation.Region(start, IsPassable);
    }

    // ПР-12К: проверка конфигурации исследуемого места до того, как партия
    // окажется в неверном состоянии. Пустой список — место годится.
    public static class LocalLocationValidator
    {
        // Сколько участников отряда должно поместиться рядом с каждой зоной
        // угрозы: командир + 4 бойца.
        public const int PartyCapacity = 5;

        public static List<string> Validate(
            LocalLocationDefinition definition,
            BattlefieldDatabaseAsset battlefields,
            Func<string, bool> dialogueExists = null,
            Func<string, bool> creatureExists = null,
            IEnumerable<HexCoord> visualBlockedCells = null)
        {
            List<string> errors = new List<string>();
            if (definition == null)
            {
                errors.Add("Место не задано.");
                return errors;
            }
            string prefix = string.IsNullOrWhiteSpace(definition.Id) ? "Место без ID" : definition.Id;
            if (string.IsNullOrWhiteSpace(definition.Id))
                errors.Add(prefix + ": пустой ID.");
            if (string.IsNullOrWhiteSpace(definition.WorldLocationId))
                errors.Add(prefix + ": не задано место глобальной карты.");

            BattlefieldDefinitionData field = battlefields != null ? battlefields.FindById(definition.BattlefieldId) : null;
            if (field == null)
            {
                errors.Add(prefix + ": нет поля '" + definition.BattlefieldId + "' в Базе полей боя.");
                return errors;
            }

            // Основания предметов художественной сборки — тоже препятствия.
            LocalLocationGeometry geometry = new LocalLocationGeometry(definition, field, visualBlockedCells);
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            void CheckId(string kind, string id)
            {
                if (string.IsNullOrWhiteSpace(id))
                    errors.Add(prefix + ": " + kind + " без ID.");
                else if (!ids.Add(id))
                    errors.Add(prefix + ": повторный ID '" + id + "'.");
            }

            if (definition.Entrances.Count == 0)
                errors.Add(prefix + ": нет входа.");
            HexCoord? start = null;
            foreach (LocalEntranceDefinition entrance in definition.Entrances)
            {
                CheckId("вход", entrance?.Id);
                if (entrance == null)
                    continue;
                HexCoord cell = LocalLocationGeometry.Cell(entrance.Cell);
                if (!geometry.IsPassable(cell))
                    errors.Add(prefix + ": вход '" + entrance.Id + "' стоит в стене или вне поля " + cell + ".");
                else if (start == null)
                    start = cell;
            }
            if (start == null)
                return errors;
            HashSet<HexCoord> region = geometry.Region(start.Value);

            foreach (LocalCellData cell in definition.DifficultCells)
            {
                if (cell == null || !geometry.IsPassable(LocalLocationGeometry.Cell(cell)))
                    errors.Add(prefix + ": трудная клетка " + cell + " в стене или вне поля.");
            }

            HashSet<HexCoord> objectCells = new HashSet<HexCoord>();
            foreach (LocalObjectDefinition item in definition.Objects)
            {
                CheckId("объект", item?.Id);
                if (item == null)
                    continue;
                HexCoord cell = LocalLocationGeometry.Cell(item.Cell);
                if (!SandboxArenaShape.Contains(cell))
                    errors.Add(prefix + ": объект '" + item.Id + "' вне поля.");
                if (!objectCells.Add(cell))
                    errors.Add(prefix + ": два объекта на клетке " + cell + ".");
                if (!geometry.InteractionCells(item).Any(region.Contains))
                    errors.Add(prefix + ": к объекту '" + item.Id + "' нельзя подойти от входа.");
                if (item.Kind == LocalObjectKind.Dialogue)
                {
                    if (string.IsNullOrWhiteSpace(item.DialogueId))
                        errors.Add(prefix + ": у объекта '" + item.Id + "' нет диалога.");
                    else if (dialogueExists != null && !dialogueExists(item.DialogueId))
                        errors.Add(prefix + ": диалога '" + item.DialogueId + "' нет в Базе диалогов.");
                }
                else if (string.IsNullOrWhiteSpace(item.Text))
                {
                    errors.Add(prefix + ": у объекта '" + item.Id + "' нет текста.");
                }
            }

            HashSet<HexCoord> enemyCells = new HashSet<HexCoord>();
            foreach (LocalEnemyDefinition enemy in definition.Enemies)
            {
                CheckId("противник", enemy?.InstanceId);
                if (enemy == null)
                    continue;
                HexCoord cell = LocalLocationGeometry.Cell(enemy.Cell);
                if (!geometry.IsPassable(cell) || !region.Contains(cell))
                    errors.Add(prefix + ": противник '" + enemy.InstanceId + "' стоит в стене или отрезан " + cell + ".");
                if (!enemyCells.Add(cell))
                    errors.Add(prefix + ": два противника на клетке " + cell + ".");
                if (creatureExists != null && !creatureExists(enemy.UnitTypeId))
                    errors.Add(prefix + ": нет существа '" + enemy.UnitTypeId + "' в Базе существ.");
                if (definition.FindEncounter(enemy.EncounterId) == null)
                    errors.Add(prefix + ": противник '" + enemy.InstanceId + "' не входит ни в одно столкновение.");
            }

            foreach (LocalEncounterDefinition encounter in definition.Encounters)
            {
                CheckId("столкновение", encounter?.Id);
                if (encounter == null)
                    continue;
                if (string.IsNullOrWhiteSpace(encounter.BattleIdPrefix))
                    errors.Add(prefix + ": у столкновения '" + encounter.Id + "' нет префикса ID боя.");
                if (encounter.TriggerCells.Count == 0)
                    errors.Add(prefix + ": у столкновения '" + encounter.Id + "' нет зоны угрозы.");
                foreach (LocalCellData trigger in encounter.TriggerCells)
                {
                    HexCoord cell = LocalLocationGeometry.Cell(trigger);
                    if (!geometry.IsPassable(cell) || !region.Contains(cell))
                        errors.Add(prefix + ": клетка угрозы " + cell + " недоступна.");
                }
                if (!string.IsNullOrWhiteSpace(encounter.IntroDialogueId) && dialogueExists != null &&
                    !dialogueExists(encounter.IntroDialogueId))
                    errors.Add(prefix + ": диалога '" + encounter.IntroDialogueId + "' нет в Базе диалогов.");
                if (encounter.AllowRetreat)
                {
                    HexCoord retreat = LocalLocationGeometry.Cell(encounter.RetreatCell);
                    if (!geometry.IsPassable(retreat) || !region.Contains(retreat))
                        errors.Add(prefix + ": безопасная точка отхода " + retreat + " недоступна.");
                    else if (encounter.TriggerCells.Exists(cell => cell.Is(retreat.Q, retreat.R)))
                        errors.Add(prefix + ": безопасная точка отхода лежит в зоне угрозы — бой зациклится.");
                }
                int enemies = definition.Enemies.Count(enemy => enemy != null && enemy.EncounterId == encounter.Id);
                if (enemies == 0)
                    errors.Add(prefix + ": в столкновении '" + encounter.Id + "' нет противников.");
                if (region.Count < PartyCapacity + enemies)
                    errors.Add(prefix + ": для боя '" + encounter.Id + "' не хватает свободных клеток.");
            }

            return errors;
        }
    }
}
