using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // ПР-16 (бриф 19.11.2026 и просьба пользователя): панель способностей —
    // до 4 ячеек-иконок внизу по центру поля. Нажатие активной включает выбор
    // цели (подсвеченные клетки, предпросмотр по наведению), повторное —
    // отменяет; ПКМ по полю — тоже отмена. Пассивные правила и профили ИИ
    // показаны там же неактивными ячейками. Иконки — программные (буквы),
    // без PNG. Ход ИИ идёт через тот же исполнитель и планировщик.
    public sealed partial class BattleSandboxController
    {
        public const int AbilitySlotCount = 4;

        private bool sandboxHiddenScouted;
        private VisualElement contextPanel;
        private VisualElement abilityBar;
        private readonly List<Button> abilitySlots = new List<Button>();
        private string selectedAbilityId;
        private HexCoord? threadAnchor;
        private List<SandboxBattle.AbilityCandidate> selectedCandidates = new List<SandboxBattle.AbilityCandidate>();

        private void ConfigureAbilityBattle()
        {
            if (battle == null || unitContent == null)
                return;
            battle.ResolveUnitType = typeId =>
                unitContent.CreaturesById.TryGetValue(typeId, out SandboxUnitDefinition definition) ? definition : unitContent.FindUnit(typeId);
        }

        // ---------- Панель ----------

        private VisualElement BuildAbilityBar()
        {
            abilitySlots.Clear();
            abilityBar = new VisualElement { name = "battle-ability-bar", pickingMode = PickingMode.Ignore };
            abilityBar.style.position = Position.Absolute;
            abilityBar.style.left = 0f;
            abilityBar.style.right = 0f;
            abilityBar.style.bottom = 150f;
            abilityBar.style.flexDirection = FlexDirection.Row;
            abilityBar.style.justifyContent = Justify.Center;
            abilityBar.style.display = DisplayStyle.None;
            for (int i = 0; i < AbilitySlotCount; i++)
            {
                Button slot = new Button { name = "battle-ability-slot-" + i };
                slot.style.width = 92f;
                slot.style.height = 92f;
                slot.style.marginLeft = 5f;
                slot.style.marginRight = 5f;
                slot.style.paddingLeft = 4f;
                slot.style.paddingRight = 4f;
                slot.style.paddingTop = 6f;
                slot.style.paddingBottom = 4f;
                slot.style.flexDirection = FlexDirection.Column;
                slot.style.alignItems = Align.Center;
                slot.style.justifyContent = Justify.FlexStart;
                slot.style.whiteSpace = WhiteSpace.Normal;
                SetRadius(slot, 6f);

                VisualElement icon = new VisualElement { name = "icon", pickingMode = PickingMode.Ignore };
                icon.style.width = 42f;
                icon.style.height = 42f;
                icon.style.alignItems = Align.Center;
                icon.style.justifyContent = Justify.Center;
                SetRadius(icon, 21f);
                Label glyph = new Label { name = "glyph", pickingMode = PickingMode.Ignore };
                glyph.style.fontSize = 16f;
                glyph.style.unityFontStyleAndWeight = FontStyle.Bold;
                icon.Add(glyph);
                slot.Add(icon);

                Label title = new Label { name = "title", pickingMode = PickingMode.Ignore };
                title.style.fontSize = 10f;
                title.style.marginTop = 4f;
                title.style.whiteSpace = WhiteSpace.Normal;
                title.style.unityTextAlign = TextAnchor.UpperCenter;
                slot.Add(title);

                Label badge = new Label { name = "badge", pickingMode = PickingMode.Ignore };
                badge.style.position = Position.Absolute;
                badge.style.right = 4f;
                badge.style.top = 3f;
                badge.style.fontSize = 10f;
                badge.style.unityFontStyleAndWeight = FontStyle.Bold;
                slot.Add(badge);

                int index = i;
                slot.clicked += () => OnAbilitySlotClicked(index);
                abilitySlots.Add(slot);
                abilityBar.Add(slot);
            }
            return abilityBar;
        }

        private static string Glyph(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return "?";
            string[] words = title.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            return (words.Length >= 2 ? words[0].Substring(0, 1) + words[1].Substring(0, 1) : words[0].Substring(0, Math.Min(2, words[0].Length))).ToUpperInvariant();
        }

        private List<object> SlotItems(SandboxUnitState unit)
        {
            List<object> items = new List<object>();
            items.AddRange(battle.GetAbilityStates(unit.Id).Cast<object>());
            foreach (SandboxAbilityDefinition passive in unit.Definition.Abilities.Where(item => !item.IsActive))
                items.Add(passive);
            if (unit.HasTag(SandboxCombatTagRules.Pack))
                items.Add("pack");
            return items.Take(AbilitySlotCount).ToList();
        }

        private void RefreshAbilityUi(bool playerInteractionAvailable, SandboxUnitState current)
        {
            RefreshContextActions(playerInteractionAvailable, current);
            if (abilityBar == null)
                return;
            bool show = current != null && current.Team == SandboxTeam.Player && battle.Phase == SandboxBattlePhase.InProgress &&
                        (current.Definition.Abilities.Count > 0 || current.HasTag(SandboxCombatTagRules.Pack));
            abilityBar.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show)
            {
                if (selectedAbilityId != null)
                    ClearAbilitySelection();
                return;
            }
            List<object> items = SlotItems(current);
            for (int i = 0; i < abilitySlots.Count; i++)
            {
                Button slot = abilitySlots[i];
                Label glyph = slot.Q<Label>("glyph");
                Label title = slot.Q<Label>("title");
                Label badge = slot.Q<Label>("badge");
                VisualElement icon = slot.Q<VisualElement>("icon");
                object item = i < items.Count ? items[i] : null;
                slot.userData = item;
                slot.style.display = item != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (item == null)
                    continue;

                if (item is SandboxAbilityState state)
                {
                    bool selected = state.Ability.Id == selectedAbilityId;
                    bool usable = state.Available && playerInteractionAvailable;
                    glyph.text = Glyph(state.Label);
                    title.text = state.Label;
                    badge.text = state.CooldownLeft > 0 ? "КД " + state.CooldownLeft : string.Empty;
                    slot.style.backgroundColor = selected ? new Color(0.42f, 0.31f, 0.13f, 0.96f) : new Color(0.10f, 0.11f, 0.12f, 0.92f);
                    SetBorder(slot, selected ? new Color(0.98f, 0.84f, 0.45f, 1f) : usable ? new Color(0.70f, 0.56f, 0.27f, 1f) : new Color(0.30f, 0.30f, 0.30f, 1f), selected ? 2f : 1f);
                    icon.style.backgroundColor = usable ? new Color(0.62f, 0.40f, 0.17f, 1f) : new Color(0.24f, 0.24f, 0.24f, 1f);
                    glyph.style.color = usable ? new Color(0.99f, 0.92f, 0.74f, 1f) : new Color(0.55f, 0.55f, 0.52f, 1f);
                    title.style.color = usable ? new Color(0.94f, 0.86f, 0.68f, 1f) : new Color(0.55f, 0.55f, 0.52f, 1f);
                    slot.tooltip = state.Label + " · активная · 1 действие" + (state.Ability.Cooldown > 0 ? " · КД " + state.Ability.Cooldown : string.Empty) +
                                   "\n" + SandboxAbilityDefaults.Explain(state.Ability) +
                                   (state.Available ? "\nЛКМ — выбрать цель; повторно — отменить." : "\nНедоступно: " + state.Reason);
                    if (selected && state.NoTarget && selectedCandidates.Count > 0)
                        slot.tooltip = selectedCandidates[0].Preview.Summary + "\nНажмите ячейку ещё раз, чтобы применить.";
                    slot.SetEnabled(usable || selected);
                }
                else
                {
                    SandboxAbilityDefinition passive = item as SandboxAbilityDefinition;
                    string label = passive != null ? passive.Title : "Стая";
                    glyph.text = Glyph(label);
                    title.text = label;
                    badge.text = passive != null && passive.TypeLabel == "поведение ИИ" ? "ИИ" : "ПАС";
                    slot.style.backgroundColor = new Color(0.08f, 0.09f, 0.10f, 0.85f);
                    SetBorder(slot, new Color(0.26f, 0.30f, 0.32f, 1f));
                    icon.style.backgroundColor = new Color(0.20f, 0.27f, 0.30f, 1f);
                    glyph.style.color = new Color(0.72f, 0.80f, 0.82f, 1f);
                    title.style.color = new Color(0.62f, 0.66f, 0.66f, 1f);
                    slot.tooltip = passive != null
                        ? label + " · " + passive.TypeLabel + "\n" + SandboxAbilityDefaults.Explain(passive)
                        : "Стая · тег\n+" + SandboxCombatTagRules.PackAttackBonus + " Атака при контактной атаке, если рядом с той же целью стоит другой зверь стаи.";
                    slot.SetEnabled(true);
                }
            }

            if (selectedAbilityId != null)
            {
                SandboxAbilityState selected = battle.GetAbilityStates(current.Id).FirstOrDefault(item => item.Ability.Id == selectedAbilityId);
                if (selected == null || !selected.Available || !playerInteractionAvailable)
                {
                    ClearAbilitySelection();
                }
                else
                {
                    SandboxAbilityPreview preview = selected.NoTarget && selectedCandidates.Count > 0 ? selectedCandidates[0].Preview : null;
                    targetLabel.text = selected.Label.ToUpperInvariant() + "\n" +
                                       (preview != null
                                           ? preview.Summary + "\nНажмите ячейку ещё раз, чтобы применить."
                                           : selected.NeedsSecondCell && threadAnchor == null
                                               ? "Выберите первую опору нити (подсвечено)."
                                               : selected.NeedsSecondCell
                                                   ? "Выберите вторую опору. Первая: " + threadAnchor.Value
                                                   : "Выберите подсвеченную клетку. Наведение показывает итог.") +
                                       "\nПКМ или повторное нажатие — отмена.";
                }
            }
        }

        private void RefreshContextActions(bool playerInteractionAvailable, SandboxUnitState current)
        {
            if (contextPanel == null)
                return;
            contextPanel.Clear();
            if (!playerInteractionAvailable || current == null)
                return;
            foreach (SandboxContextAction action in battle.GetContextActions(current.Id))
            {
                SandboxContextAction captured = action;
                Button button = new Button(() => PerformContextAction(captured)) { text = action.Label.ToUpperInvariant() };
                StyleSecondaryButton(button);
                button.style.marginTop = 4f;
                button.style.height = 32f;
                button.tooltip = "1 действие. Завершает ход.";
                contextPanel.Add(button);
            }
            // Выбранный паразит на союзнике или зажатый в тоннеле — обычная атака.
            SandboxUnitState target = !string.IsNullOrEmpty(selectedTargetId) ? battle.GetUnit(selectedTargetId) : null;
            if (target != null && !target.IsDefeated && (target.IsAttached || target.IsStuckInTunnel) && target.Team != current.Team)
            {
                Button attack = new Button(() =>
                {
                    if (!TryBeginDirectAttack(current, target, FindMeleeSide(current, target)))
                        AddBattleLog("До " + target.DisplayLabel + " не дотянуться с этой позиции.");
                    RefreshBattleScreen();
                }) { text = "АТАКОВАТЬ: " + target.DisplayLabel.ToUpperInvariant() };
                StylePrimaryButton(attack);
                attack.style.marginTop = 4f;
                attack.style.height = 34f;
                contextPanel.Add(attack);
            }
        }

        private HexCoord? FindMeleeSide(SandboxUnitState attacker, SandboxUnitState target)
        {
            return battle.TryFindAttackPosition(attacker.Id, target.Id, out HexCoord position, out _) ? position : (HexCoord?)null;
        }

        private void PerformContextAction(SandboxContextAction action)
        {
            SandboxUnitState current = battle?.CurrentUnit;
            if (combatAnimationRunning || current == null || current.Team != SandboxTeam.Player)
                return;
            if (battle.TryContextAction(current.Id, action, out string message))
            {
                AddBattleLog(message);
                ClearAbilitySelection();
                FinishActivationIfEmpty();
            }
            RefreshBattleScreen();
        }

        private void OnAbilitySlotClicked(int index)
        {
            SandboxUnitState current = battle?.CurrentUnit;
            if (combatAnimationRunning || current == null || current.Team != SandboxTeam.Player || index >= abilitySlots.Count)
                return;
            if (!(abilitySlots[index].userData is SandboxAbilityState state))
                return;
            if (state.Ability.Id == selectedAbilityId)
            {
                if (state.NoTarget && selectedCandidates.Count > 0)
                {
                    SandboxAbilityCommand command = selectedCandidates[0].Command;
                    ClearAbilitySelection();
                    ExecutePlayerAbility(current, command);
                    return;
                }
                CancelAbilityTargeting();
                return;
            }
            if (!state.Available)
                return;
            selectedAbilityId = state.Ability.Id;
            threadAnchor = null;
            selectedTargetId = null;
            selectedCandidates = battle.GetAbilityCandidates(current.Id, state.Ability.Id);
            board.AbilityTargeting = state.NoTarget ? null : BuildTargeting(state);
            RefreshBattleScreen();
        }

        private BoardAbilityTargeting BuildTargeting(SandboxAbilityState state)
        {
            BoardAbilityTargeting targeting = new BoardAbilityTargeting { FirstAnchor = threadAnchor };
            if (state.NeedsSecondCell)
            {
                foreach (SandboxBattle.AbilityCandidate candidate in selectedCandidates)
                {
                    HexCoord a = candidate.Command.TargetCell.Value, b = candidate.Command.SecondCell.Value;
                    if (!threadAnchor.HasValue)
                    {
                        targeting.Cells.Add(a);
                        targeting.Cells.Add(b);
                    }
                    else if (a == threadAnchor.Value)
                        targeting.Cells.Add(b);
                    else if (b == threadAnchor.Value)
                        targeting.Cells.Add(a);
                }
            }
            else
            {
                foreach (SandboxBattle.AbilityCandidate candidate in selectedCandidates)
                    targeting.Cells.Add(candidate.Command.KeyCell);
            }
            targeting.Preview = cell =>
            {
                SandboxBattle.AbilityCandidate match = MatchCandidate(cell, state.NeedsSecondCell);
                if (match != null)
                    return match.Preview;
                if (state.NeedsSecondCell && !threadAnchor.HasValue && targeting.Cells.Contains(cell))
                    return new SandboxAbilityPreview { IsValid = true, Summary = "Первая опора нити: " + cell };
                return SandboxAbilityPreview.Invalid("Сюда нельзя.");
            };
            return targeting;
        }

        private SandboxBattle.AbilityCandidate MatchCandidate(HexCoord cell, bool twoCells)
        {
            if (twoCells)
            {
                if (!threadAnchor.HasValue)
                    return null;
                HexCoord anchor = threadAnchor.Value;
                return selectedCandidates.FirstOrDefault(item =>
                    (item.Command.TargetCell == anchor && item.Command.SecondCell == cell) ||
                    (item.Command.SecondCell == anchor && item.Command.TargetCell == cell));
            }
            return selectedCandidates.FirstOrDefault(item => item.Command.KeyCell == cell);
        }

        private void OnAbilityCellClicked(HexCoord cell)
        {
            SandboxUnitState current = battle?.CurrentUnit;
            if (combatAnimationRunning || current == null || selectedAbilityId == null)
                return;
            SandboxAbilityState state = battle.GetAbilityStates(current.Id).FirstOrDefault(item => item.Ability.Id == selectedAbilityId);
            if (state == null)
            {
                CancelAbilityTargeting();
                return;
            }
            if (state.NeedsSecondCell && !threadAnchor.HasValue)
            {
                if (board.AbilityTargeting != null && board.AbilityTargeting.Cells.Contains(cell))
                {
                    threadAnchor = cell;
                    board.AbilityTargeting = BuildTargeting(state);
                    RefreshBattleScreen();
                }
                return;
            }
            SandboxBattle.AbilityCandidate match = MatchCandidate(cell, state.NeedsSecondCell);
            if (match == null)
                return;
            ClearAbilitySelection();
            ExecutePlayerAbility(current, match.Command);
        }

        private void CancelAbilityTargeting()
        {
            ClearAbilitySelection();
            RefreshBattleScreen();
        }

        private void ClearAbilitySelection()
        {
            selectedAbilityId = null;
            threadAnchor = null;
            selectedCandidates = new List<SandboxBattle.AbilityCandidate>();
            if (board != null)
                board.AbilityTargeting = null;
        }

        private void ExecutePlayerAbility(SandboxUnitState unit, SandboxAbilityCommand command)
        {
            string unitId = unit.Id;
            if (command.ApproachCell.HasValue && command.ApproachCell.Value != unit.Position)
            {
                if (BeginMoveAnimation(unitId, command.ApproachCell.Value, moved =>
                    {
                        if (moved)
                            ExecuteAbilityCommand(unitId, command, _ => FinishActivationIfEmpty());
                    }))
                    return;
            }
            ExecuteAbilityCommand(unitId, command, _ => FinishActivationIfEmpty());
        }

        // Исход считает модель один раз; поле показывает записи ударов.
        private void ExecuteAbilityCommand(string unitId, SandboxAbilityCommand command, Action<bool> onComplete)
        {
            if (battle == null || board == null || combatAnimationRunning)
            {
                onComplete?.Invoke(false);
                return;
            }
            int firstRecord = battle.HitRecords.Count;
            bool applied = battle.TryUseAbility(unitId, command, out string message);
            AddBattleLog(message);
            if (applied && battle.HasPendingRetaliation && battle.TryResolvePendingRetaliation(out string retaliation))
                AddBattleLog(retaliation);
            PlayRecordsThen(firstRecord, applied, onComplete);
        }

        private void PlayRecordsThen(int firstRecord, bool applied, Action<bool> onComplete)
        {
            combatAnimationRunning = true;
            RefreshBattleScreen();
            List<SandboxHitRecord> records = battle.GetHitRecordsSince(firstRecord);
            int generation = battleGeneration;
            if (applied && records.Count > 0 && board.PlayHitSequence(records, () =>
                {
                    if (generation == battleGeneration)
                        FinishAttackSequence(true, onComplete);
                }))
                return;
            FinishAttackSequence(applied, onComplete);
        }

        // ---------- Ход ИИ ----------

        private void RunEnemyDecision(SandboxUnitState enemy, int pass)
        {
            if (battle == null || enemy == null || battle.CurrentUnit != enemy || battle.Phase != SandboxBattlePhase.InProgress)
            {
                if (enemy != null)
                    CompleteEnemyActivation(enemy.Id);
                RefreshBattleScreen();
                return;
            }
            string enemyId = enemy.Id;
            SandboxAiDecision decision = SandboxAiPlanner.Plan(battle, enemy, pass == 0);
            if (decision.MoveTo.HasValue && decision.MoveTo.Value != enemy.Position && pass == 0)
            {
                if (BeginMoveAnimation(enemyId, decision.MoveTo.Value, moved =>
                    {
                        SandboxUnitState after = battle?.GetUnit(enemyId);
                        if (!moved || after == null || battle.CurrentUnit != after)
                        {
                            CompleteEnemyActivation(enemyId);
                            RefreshBattleScreen();
                            return;
                        }
                        if (decision.Action == SandboxAiActionKind.None)
                            RunEnemyDecision(after, 1);
                        else
                            PerformEnemyAction(after, decision);
                    }))
                    return;
            }
            PerformEnemyAction(enemy, decision);
        }

        private void PerformEnemyAction(SandboxUnitState enemy, SandboxAiDecision decision)
        {
            string enemyId = enemy.Id;
            Action<bool> done = _ => CompleteEnemyActivation(enemyId);
            switch (decision.Action)
            {
                case SandboxAiActionKind.Attack:
                    SandboxUnitState target = battle.GetUnit(decision.TargetId);
                    if (target != null && battle.PreviewAttack(enemyId, target.Id).IsValid)
                    {
                        BeginEnemyAttack(enemy, target);
                        return;
                    }
                    break;
                case SandboxAiActionKind.ShootAndRetreat:
                {
                    int first = battle.HitRecords.Count;
                    bool applied = battle.TryShootAndRetreat(enemyId, decision.TargetId, out string message);
                    AddBattleLog(message);
                    PlayRecordsThen(first, applied, done);
                    return;
                }
                case SandboxAiActionKind.Ability:
                    ExecuteAbilityCommand(enemyId, decision.Command, done);
                    return;
                case SandboxAiActionKind.Context:
                    if (battle.TryContextAction(enemyId, decision.Context, out string contextMessage))
                        AddBattleLog(contextMessage);
                    break;
                case SandboxAiActionKind.Wait:
                    AddBattleLog(enemy.DisplayLabel + ": " + decision.Reason);
                    break;
            }
            CompleteEnemyActivation(enemyId);
            RefreshBattleScreen();
        }
    }
}
