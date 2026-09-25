using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

// ПР-07А-2 (PR07_HOME_SPEC §8 с правками §0.4): закреплённая панель
// «Подготовка похода» на экране Дома. Командир, четыре места бойцов и место
// свиты; рядом короткий список кандидатов (бойцы, Тихон, Остафий, Лада).
// Всё меняется только командами ExpeditionPreparation — перетаскиванием
// или теми же действиями кнопками. Перенос применяется один раз при
// отпускании на допустимой цели; Escape, потеря захвата указателя,
// открытие сюжетного окна и отпускание мимо цели отменяют его без изменений.
public partial class PrototypeUIController
{
    private const float PrepDragThresholdPixels = 7f;

    private enum PrepFilter
    {
        All,
        Fighters,
        Retinue
    }

    private sealed class PrepDrag
    {
        public string PersonId;
        public int FromSlot = -1;
        public bool FromRetinue;
        public bool FromCandidates;
        public VisualElement Source;
        public int PointerId;
        public Vector2 StartPosition;
        public bool Active;
        public Label Ghost;
    }

    private VisualElement homePrepPanel;
    private Label homePrepTitle;
    private Label homePrepMessage;
    private Button homePrepRouteButton;
    private VisualElement homePrepSlots;
    private VisualElement homePrepCandidates;
    private Label homePrepForecast;
    private bool homePrepBound;
    private string homePrepSignature;

    private readonly List<VisualElement> homePrepFighterSlots = new List<VisualElement>();
    private VisualElement homePrepRetinueSlot;
    private VisualElement homePrepCandidateZone;
    private PrepFilter homePrepFilter = PrepFilter.All;
    private string homePrepReplacingId;
    private PrepDrag homePrepDrag;

    private void BindHomePrep()
    {
        homePrepPanel = BindRequiredElement<VisualElement>(interfaceRoot, HomeScreenName, "home-prep-panel");
        homePrepTitle = BindRequiredElement<Label>(interfaceRoot, HomeScreenName, "home-prep-title");
        homePrepMessage = BindRequiredElement<Label>(interfaceRoot, HomeScreenName, "home-prep-message");
        homePrepRouteButton = BindRequiredElement<Button>(interfaceRoot, HomeScreenName, "home-prep-route-button");
        homePrepSlots = BindRequiredElement<VisualElement>(interfaceRoot, HomeScreenName, "home-prep-slots");
        homePrepCandidates = BindRequiredElement<VisualElement>(interfaceRoot, HomeScreenName, "home-prep-candidates");
        homePrepForecast = BindRequiredElement<Label>(interfaceRoot, HomeScreenName, "home-prep-forecast");

        homePrepBound = homePrepPanel != null && homePrepTitle != null && homePrepMessage != null &&
                        homePrepRouteButton != null && homePrepSlots != null && homePrepCandidates != null &&
                        homePrepForecast != null;
        if (!homePrepBound)
            return;

        homePrepRouteButton.clicked += OnHomePrepRouteClicked;
        interfaceRoot.RegisterCallback<KeyDownEvent>(OnHomePrepKeyDown, TrickleDown.TrickleDown);
    }

    private void RefreshHomePrepIfChanged()
    {
        if (!homePrepBound || gameState == null)
            return;

        // Сюжетное окно поверх — перенос отменяется.
        if (homePrepDrag != null && IsNarrativeDialogueActive)
            CancelHomePrepDrag();

        // Идущие часы не пересоздают карточки посреди переноса (§8.3).
        if (homePrepDrag != null)
            return;

        bool editable = ExpeditionPreparation.CanEdit(gameState);
        string signature = homePeopleSignature + "|" + editable + "|" + homePrepFilter + "|" + homePrepReplacingId +
                           "|" + gameState.ArmySupply + "|" + gameState.Food;
        if (signature == homePrepSignature)
            return;
        homePrepSignature = signature;

        RebuildHomePrep(editable);
    }

    private void InvalidateHomePrep()
    {
        homePrepSignature = null;
        homePeopleSignature = null;
        homeScreenSignature = null;
    }

    // ------------------------------------------------------------------
    // Построение панели
    // ------------------------------------------------------------------

    private void RebuildHomePrep(bool editable)
    {
        bool away = HomePeopleService.HasDeparted(gameState);
        homePrepTitle.text = away ? "СОСТАВ В ПОХОДЕ" : "ПОДГОТОВКА ПОХОДА";
        homePrepRouteButton.style.display = away ? DisplayStyle.None : DisplayStyle.Flex;
        homePrepRouteButton.SetEnabled(!away && !isGameOver);

        homePrepSlots.Clear();
        homePrepFighterSlots.Clear();

        CommanderData commander = gameState.GetSelectedCommander();
        ResidentState hero = commander != null ? HomePeopleService.Find(gameState, commander.Id) : null;
        VisualElement commanderSlot = CreatePrepSlot("Командир", hero, commander != null ? commander.Name : "Командир",
            "ведёт поход", "home-prep-slot--commander");
        homePrepSlots.Add(commanderSlot);

        IReadOnlyList<string> fighters = ExpeditionPreparation.GetFighterIds(gameState);
        for (int i = 0; i < ExpeditionPreparation.FighterSlots; i++)
        {
            VisualElement slot;
            if (i < fighters.Count)
            {
                ResidentState resident = HomePeopleService.Find(gameState, fighters[i]);
                FighterData fighter = gameState.FindFighter(fighters[i]);
                slot = CreatePrepSlot("Боец " + (i + 1), resident,
                    fighter != null ? fighter.Name : fighters[i],
                    fighter != null ? fighter.Role.ToLowerInvariant() : string.Empty, "home-prep-slot--fighter");
                if (editable)
                    AddFighterSlotButtons(slot, i, fighters.Count, fighters[i]);
                AttachPrepDrag(slot, fighters[i], i, false, false, editable);
            }
            else
            {
                slot = CreateEmptyPrepSlot("Боец " + (i + 1), "Добавить бойца", editable, PrepFilter.Fighters);
            }

            int slotIndex = i;
            slot.userData = slotIndex;
            slot.RegisterCallback<ClickEvent>(_ => OnFighterSlotClicked(slotIndex));
            homePrepFighterSlots.Add(slot);
            homePrepSlots.Add(slot);
        }

        string retinueId = ExpeditionPreparation.GetRetinueId(gameState);
        if (!string.IsNullOrEmpty(retinueId))
        {
            ResidentState resident = HomePeopleService.Find(gameState, retinueId);
            homePrepRetinueSlot = CreatePrepSlot("Свита", resident, resident != null ? resident.DisplayName : retinueId,
                resident != null ? resident.RoleLabel : string.Empty, "home-prep-slot--retinue");
            if (editable)
                AddSlotButton(homePrepRetinueSlot, "Оставить дома", () => RunPrepCommand(
                    ExpeditionPreparation.TryRemove(gameState, retinueId, out string message), message));
            AttachPrepDrag(homePrepRetinueSlot, retinueId, -1, true, false, editable);
        }
        else
        {
            homePrepRetinueSlot = CreateEmptyPrepSlot("Свита", "Взять специалиста", editable, PrepFilter.Retinue);
            homePrepRetinueSlot.AddToClassList("home-prep-slot--retinue");
        }
        homePrepSlots.Add(homePrepRetinueSlot);

        RebuildPrepCandidates(editable, away);

        homePrepForecast.style.display = away ? DisplayStyle.None : DisplayStyle.Flex;
        if (!away)
            homePrepForecast.text = "После выхода:\n" + string.Join("\n", HomeOverview.DescribeDeparture(gameState));

        if (homePrepReplacingId != null)
        {
            ResidentState replacing = HomePeopleService.Find(gameState, homePrepReplacingId);
            SetPrepMessage("Выберите место, где " + (replacing != null ? replacing.DisplayName : "новый боец") +
                           " заменит бойца. Escape — отмена.");
        }
        else if (away)
        {
            SetPrepMessage("Поход идёт — состав не меняется. Нужно вернуться в Дом.");
        }
        else if (!editable)
        {
            SetPrepMessage("Состав закреплён: поход начался.");
        }
    }

    private VisualElement CreatePrepSlot(string slotTitle, ResidentState resident, string name, string role, string slotClass)
    {
        VisualElement slot = new VisualElement();
        slot.AddToClassList("home-prep-slot");
        slot.AddToClassList(slotClass);

        Label caption = new Label(slotTitle);
        caption.AddToClassList("home-prep-slot-caption");
        Label nameLabel = new Label(name);
        nameLabel.AddToClassList("home-prep-slot-name");
        Label roleLabel = new Label(role);
        roleLabel.AddToClassList("home-prep-slot-role");
        slot.Add(caption);
        slot.Add(nameLabel);
        slot.Add(roleLabel);

        if (resident != null)
        {
            string state = resident.HasCombatState
                ? "HP " + resident.CurrentHitPoints + "/" + resident.MaxHitPoints
                : string.Empty;
            if (resident.Injury == ResidentInjury.Recovering)
            {
                state += (state.Length > 0 ? " · " : string.Empty) + "восстанавливается — перед выходом замените";
                slot.AddToClassList("home-prep-slot--blocked");
            }
            if (state.Length > 0)
            {
                Label stateLabel = new Label(state);
                stateLabel.AddToClassList("home-prep-slot-state");
                slot.Add(stateLabel);
            }
        }

        return slot;
    }

    private VisualElement CreateEmptyPrepSlot(string slotTitle, string actionText, bool editable, PrepFilter filter)
    {
        VisualElement slot = new VisualElement();
        slot.AddToClassList("home-prep-slot");
        slot.AddToClassList("home-prep-slot--empty");

        Label caption = new Label(slotTitle);
        caption.AddToClassList("home-prep-slot-caption");
        slot.Add(caption);

        if (editable)
        {
            Button action = new Button(() =>
            {
                homePrepFilter = filter;
                InvalidateHomePrep();
                RefreshHomePrepIfChanged();
            }) { text = actionText };
            action.AddToClassList("home-prep-slot-button");
            slot.Add(action);
        }
        else
        {
            Label empty = new Label("пусто");
            empty.AddToClassList("home-prep-slot-role");
            slot.Add(empty);
        }

        return slot;
    }

    private void AddFighterSlotButtons(VisualElement slot, int index, int count, string personId)
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("home-prep-slot-buttons");
        if (index > 0)
            AddSlotButton(row, "←", () => RunPrepCommand(
                ExpeditionPreparation.TryMoveFighter(gameState, index, index - 1, out string message), message));
        if (index < count - 1)
            AddSlotButton(row, "→", () => RunPrepCommand(
                ExpeditionPreparation.TryMoveFighter(gameState, index, index + 1, out string message), message));
        AddSlotButton(row, "Оставить дома", () => RunPrepCommand(
            ExpeditionPreparation.TryRemove(gameState, personId, out string message), message));
        slot.Add(row);
    }

    private static void AddSlotButton(VisualElement parent, string text, System.Action action)
    {
        Button button = new Button(action) { text = text };
        button.AddToClassList("home-prep-slot-button");
        parent.Add(button);
    }

    // Кандидаты: бойцы и специалисты свиты, которые сейчас не в составе.
    // Фоновые жители и дети в поход не идут и здесь не показываются.
    private void RebuildPrepCandidates(bool editable, bool away)
    {
        homePrepCandidates.Clear();
        homePrepCandidateZone = homePrepCandidates;
        if (away)
            return;

        VisualElement filters = new VisualElement();
        filters.AddToClassList("home-prep-filters");
        AddFilterButton(filters, "Все", PrepFilter.All);
        AddFilterButton(filters, "Бойцы", PrepFilter.Fighters);
        AddFilterButton(filters, "Свита", PrepFilter.Retinue);
        Label hint = new Label("Остаются дома — перетащите сюда, чтобы оставить");
        hint.AddToClassList("home-prep-candidates-hint");
        filters.Add(hint);
        homePrepCandidates.Add(filters);

        VisualElement list = new VisualElement();
        list.AddToClassList("home-prep-candidate-list");
        homePrepCandidates.Add(list);

        int fighterCount = ExpeditionPreparation.GetFighterIds(gameState).Count;
        bool full = fighterCount >= ExpeditionPreparation.FighterSlots;
        int shown = 0;

        foreach (ResidentState resident in HomePeopleService.All(gameState))
        {
            if (!resident.IsHomeMember || ExpeditionPreparation.IsPrepared(gameState, resident.PersonId))
                continue;

            bool isFighter = resident.TravelRole == ResidentTravelRole.Combatant && gameState.FindFighter(resident.PersonId) != null;
            bool isRetinue = resident.TravelRole == ResidentTravelRole.Retinue;
            if (!isFighter && !isRetinue)
                continue;
            if ((homePrepFilter == PrepFilter.Fighters && !isFighter) || (homePrepFilter == PrepFilter.Retinue && !isRetinue))
                continue;

            VisualElement card = new VisualElement();
            card.AddToClassList("home-prep-candidate");
            Label name = new Label(resident.DisplayName);
            name.AddToClassList("home-prep-slot-name");
            Label role = new Label(isRetinue ? resident.RoleLabel + " · свита" : resident.RoleLabel);
            role.AddToClassList("home-prep-slot-role");
            card.Add(name);
            card.Add(role);

            bool recovering = resident.Injury == ResidentInjury.Recovering;
            if (recovering)
            {
                Label state = new Label("восстанавливается");
                state.AddToClassList("home-prep-slot-state");
                card.Add(state);
                card.AddToClassList("home-prep-candidate--blocked");
            }

            if (editable)
            {
                string personId = resident.PersonId;
                if (isFighter)
                {
                    if (full)
                    {
                        Button replace = new Button(() =>
                        {
                            homePrepReplacingId = personId;
                            InvalidateHomePrep();
                            RefreshHomePrepIfChanged();
                        }) { text = "Заменить бойца" };
                        replace.AddToClassList("home-prep-slot-button");
                        replace.SetEnabled(!recovering);
                        card.Add(replace);
                    }
                    else
                    {
                        Button add = new Button(() => RunPrepCommand(
                            ExpeditionPreparation.TryAddFighter(gameState, personId, out string message), message))
                        { text = "Взять в отряд" };
                        add.AddToClassList("home-prep-slot-button");
                        card.Add(add);
                    }
                }
                else
                {
                    Button take = new Button(() => RunPrepCommand(
                        ExpeditionPreparation.TrySetRetinue(gameState, personId, out string message), message))
                    { text = "Взять в свиту" };
                    take.AddToClassList("home-prep-slot-button");
                    card.Add(take);
                }

                AttachPrepDrag(card, personId, -1, false, true, true);
            }

            list.Add(card);
            shown++;
        }

        if (shown == 0)
        {
            Label empty = new Label("Все, кто может идти, уже в составе.");
            empty.AddToClassList("home-prep-candidates-hint");
            list.Add(empty);
        }
    }

    private void AddFilterButton(VisualElement parent, string text, PrepFilter filter)
    {
        Button button = new Button(() =>
        {
            homePrepFilter = filter;
            InvalidateHomePrep();
            RefreshHomePrepIfChanged();
        }) { text = text };
        button.AddToClassList("home-prep-filter");
        button.EnableInClassList("home-prep-filter--selected", homePrepFilter == filter);
        parent.Add(button);
    }

    // ------------------------------------------------------------------
    // Команды
    // ------------------------------------------------------------------

    private void RunPrepCommand(bool ok, string message)
    {
        homePrepReplacingId = null;
        SetPrepMessage(message);
        InvalidateHomePrep();
        RefreshInterface();
        RefreshHomePeopleUi();
        if (stableUiInitialized)
            RefreshStableUiAfterStateChange();
    }

    private void OnFighterSlotClicked(int slotIndex)
    {
        if (homePrepReplacingId == null || homePrepDrag != null)
            return;

        string personId = homePrepReplacingId;
        RunPrepCommand(ExpeditionPreparation.TryPlaceFighter(gameState, personId, slotIndex, out string message), message);
    }

    private void SetPrepMessage(string message)
    {
        if (homePrepMessage != null)
            homePrepMessage.text = message ?? string.Empty;
    }

    private void OnHomePrepRouteClicked()
    {
        if (gameState == null || isGameOver)
            return;
        OnExpeditionsNavigationClicked();
    }

    private void OnHomePrepKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Escape)
            return;

        if (homePrepDrag != null)
        {
            CancelHomePrepDrag();
            evt.StopPropagation();
        }
        else if (homePrepReplacingId != null)
        {
            homePrepReplacingId = null;
            SetPrepMessage(string.Empty);
            InvalidateHomePrep();
            RefreshHomePrepIfChanged();
            evt.StopPropagation();
        }
    }

    // ------------------------------------------------------------------
    // Перетаскивание
    // ------------------------------------------------------------------

    private void AttachPrepDrag(VisualElement element, string personId, int fromSlot, bool fromRetinue, bool fromCandidates, bool editable)
    {
        element.AddToClassList("home-prep-draggable");

        element.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || gameState == null)
                return;
            // Кнопки внутри карточки работают как кнопки, а не как начало переноса.
            if (evt.target is Button && evt.target != element)
                return;

            homePrepDrag = new PrepDrag
            {
                PersonId = personId,
                FromSlot = fromSlot,
                FromRetinue = fromRetinue,
                FromCandidates = fromCandidates,
                Source = element,
                PointerId = evt.pointerId,
                StartPosition = evt.position
            };
            if (!editable)
                homePrepDrag.Active = false;
            element.CapturePointer(evt.pointerId);
        });

        element.RegisterCallback<PointerMoveEvent>(evt =>
        {
            PrepDrag drag = homePrepDrag;
            if (drag == null || drag.Source != element || evt.pointerId != drag.PointerId)
                return;

            if (!drag.Active)
            {
                if (!editable || Vector2.Distance((Vector2)evt.position, drag.StartPosition) < PrepDragThresholdPixels)
                    return;
                BeginHomePrepDrag(drag);
            }

            MoveHomePrepGhost(drag, evt.position);
        });

        element.RegisterCallback<PointerUpEvent>(evt =>
        {
            PrepDrag drag = homePrepDrag;
            if (drag == null || drag.Source != element || evt.pointerId != drag.PointerId)
                return;

            if (element.HasPointerCapture(evt.pointerId))
                element.ReleasePointer(evt.pointerId);

            if (!drag.Active)
            {
                // Нажатие без перемещения — открыть карточку человека.
                homePrepDrag = null;
                ResidentState resident = HomePeopleService.Find(gameState, personId);
                if (resident != null)
                    SetPrepMessage(PersonCardText(resident));
                return;
            }

            DropHomePrepDrag(drag, evt.position);
        });

        element.RegisterCallback<PointerCaptureOutEvent>(_ =>
        {
            if (homePrepDrag != null && homePrepDrag.Source == element)
                CancelHomePrepDrag();
        });
    }

    private void BeginHomePrepDrag(PrepDrag drag)
    {
        drag.Active = true;
        ResidentState resident = HomePeopleService.Find(gameState, drag.PersonId);
        drag.Ghost = new Label(resident != null ? resident.DisplayName : drag.PersonId);
        drag.Ghost.AddToClassList("home-prep-ghost");
        drag.Ghost.pickingMode = PickingMode.Ignore;
        interfaceRoot.Add(drag.Ghost);
        drag.Source.AddToClassList("home-prep-drag-source");

        bool isRetinue = resident != null && resident.TravelRole == ResidentTravelRole.Retinue;
        foreach (VisualElement slot in homePrepFighterSlots)
            slot.EnableInClassList(isRetinue ? "home-prep-drop--invalid" : "home-prep-drop--valid", true);
        homePrepRetinueSlot?.EnableInClassList(isRetinue ? "home-prep-drop--valid" : "home-prep-drop--invalid", true);
        if (!drag.FromCandidates)
            homePrepCandidateZone?.AddToClassList("home-prep-drop--valid");

        SetPrepMessage(isRetinue
            ? "Отпустите на месте свиты. Боевые места не для специалиста."
            : "Отпустите на месте бойца — занятого заменит. Место свиты не для бойца.");
    }

    private void MoveHomePrepGhost(PrepDrag drag, Vector2 pointer)
    {
        if (drag.Ghost == null)
            return;
        Vector2 local = interfaceRoot.WorldToLocal(pointer);
        drag.Ghost.style.left = local.x + 12f;
        drag.Ghost.style.top = local.y + 8f;
    }

    private void DropHomePrepDrag(PrepDrag drag, Vector2 pointer)
    {
        string personId = drag.PersonId;
        EndHomePrepDragVisuals(drag);
        homePrepDrag = null;

        for (int i = 0; i < homePrepFighterSlots.Count; i++)
        {
            if (!homePrepFighterSlots[i].worldBound.Contains(pointer))
                continue;

            if (drag.FromSlot >= 0)
            {
                // Перестановка: в занятое — поменять местами, в пустое — в конец.
                int count = ExpeditionPreparation.GetFighterIds(gameState).Count;
                RunPrepCommand(ExpeditionPreparation.TryMoveFighter(gameState, drag.FromSlot, System.Math.Min(i, count - 1),
                    out string moveMessage), moveMessage);
            }
            else
            {
                RunPrepCommand(ExpeditionPreparation.TryPlaceFighter(gameState, personId, i, out string message), message);
            }
            return;
        }

        if (homePrepRetinueSlot != null && homePrepRetinueSlot.worldBound.Contains(pointer))
        {
            RunPrepCommand(ExpeditionPreparation.TrySetRetinue(gameState, personId, out string message), message);
            return;
        }

        if (!drag.FromCandidates && homePrepCandidateZone != null && homePrepCandidateZone.worldBound.Contains(pointer))
        {
            RunPrepCommand(ExpeditionPreparation.TryRemove(gameState, personId, out string message), message);
            return;
        }

        // Мимо цели — ничего не меняется: это не увольнение и не приказ.
        SetPrepMessage(string.Empty);
        InvalidateHomePrep();
        RefreshHomePrepIfChanged();
    }

    private void CancelHomePrepDrag()
    {
        PrepDrag drag = homePrepDrag;
        if (drag == null)
            return;

        homePrepDrag = null;
        if (drag.Source != null && drag.Source.HasPointerCapture(drag.PointerId))
            drag.Source.ReleasePointer(drag.PointerId);
        EndHomePrepDragVisuals(drag);
        SetPrepMessage(string.Empty);
        InvalidateHomePrep();
        RefreshHomePrepIfChanged();
    }

    private void EndHomePrepDragVisuals(PrepDrag drag)
    {
        drag.Ghost?.RemoveFromHierarchy();
        drag.Source?.RemoveFromClassList("home-prep-drag-source");
        foreach (VisualElement slot in homePrepFighterSlots)
        {
            slot.RemoveFromClassList("home-prep-drop--valid");
            slot.RemoveFromClassList("home-prep-drop--invalid");
        }
        homePrepRetinueSlot?.RemoveFromClassList("home-prep-drop--valid");
        homePrepRetinueSlot?.RemoveFromClassList("home-prep-drop--invalid");
        homePrepCandidateZone?.RemoveFromClassList("home-prep-drop--valid");
    }
}
