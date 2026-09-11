using UnityEngine.UIElements;

// Location Interaction — системное окно "мы вошли в локацию", которое
// заменяет старый decision-модал ("АРМИЯ ПРИБЫЛА" -> PendingDecision
// "Исследовать/Отменить"). Не третий тип fullscreen/modal UI: переиспользует
// тот же VisualElement-оверлей, что и Narrative Dialogue (narrative-dialogue-
// overlay/speaker/role/portrait/history/choices), но питается не из
// DialogueDatabase, а прямо из GameState+LocationData — локация не NPC и не
// заслуживает записи в сюжетной базе диалогов. Narrative Dialogue и Location
// Interaction никогда не показываются одновременно (см. IsLocationInteractionActive
// в HasBlockingModalWorkExceptCamp/HasBlockingModalWork, ModalQueue.cs).
public partial class PrototypeUIController
{
    private bool locationInteractionActive;
    private string locationInteractionLocationId;

    private bool IsLocationInteractionActive =>
        locationInteractionActive && !string.IsNullOrEmpty(locationInteractionLocationId);

    // Единая точка входа: и автоматическое открытие при прибытии
    // (PrototypeUIController.ModalQueue.cs, QueueNotice), и ручной вход по
    // кнопке "ВОЙТИ В ЛОКАЦИЮ" (WorldMapLocationActions.cs) вызывают именно
    // этот метод — не создавать вторую точку открытия того же окна.
    public bool TryOpenLocationInteraction(string locationId)
    {
        if (!narrativeUiBound || string.IsNullOrEmpty(locationId))
            return false;

        if (gameState == null || isGameOver)
            return false;

        if (IsNarrativeDialogueActive || IsLocationInteractionActive || HasBlockingModalWorkExceptCamp())
            return false;

        if (!gameState.HasActiveExpedition)
            return false;

        ExpeditionData expedition = gameState.ActiveExpedition;
        if (expedition.Phase != CommanderState.AtLocation ||
            !string.Equals(expedition.LocationId, locationId, System.StringComparison.Ordinal))
        {
            return false;
        }

        LocationData location = gameState.FindLocation(locationId);
        if (location == null || location.IsWaypoint)
            return false;

        locationInteractionActive = true;
        locationInteractionLocationId = location.Id;

        PauseForBlockingModal();

        // Локация — не говорящий персонаж: портретный конвейер 5:7 здесь не
        // участвует (раздел "Не использовать портрет NPC для локации"
        // инструкции). Восстанавливается в CloseLocationInteraction, чтобы
        // следующий настоящий Narrative Dialogue не остался без портрета.
        if (narrativePortrait != null)
            narrativePortrait.style.display = DisplayStyle.None;

        if (narrativeSpeakerLabel != null)
            narrativeSpeakerLabel.text = location.Name;
        if (narrativeRoleLabel != null)
            narrativeRoleLabel.text = "ЛОКАЦИЯ";

        RenderLocationInteractionDescription(location);
        RenderLocationInteractionChoices(location);

        if (narrativeDialogueOverlay != null)
        {
            narrativeDialogueOverlay.style.display = DisplayStyle.Flex;
            // Тот же инвариант, что у Narrative Dialogue: верхний блокирующий
            // gameplay-слой обязан возвращаться на вершину sibling stack при
            // каждом открытии (Camp/Hero/Journal не имеют права оказаться выше).
            narrativeDialogueOverlay.BringToFront();
        }

        if (timeToggleButton != null)
        {
            timeToggleButton.SetEnabled(false);
            timeToggleButton.tooltip = "Сначала закройте окно локации";
        }

        return true;
    }

    private void RenderLocationInteractionDescription(LocationData location)
    {
        if (narrativeHistoryContainer == null)
            return;

        narrativeHistoryContainer.Clear();

        VisualElement block = new VisualElement();
        block.AddToClassList("narrative-dialogue-history-entry");

        string description = !string.IsNullOrWhiteSpace(location.InteractionDescription)
            ? location.InteractionDescription
            : "Отряд находится внутри локации «" + location.Name + "».";

        Label text = new Label(description);
        text.AddToClassList("narrative-dialogue-history-text");
        block.Add(text);

        narrativeHistoryContainer.Add(block);
    }

    private void RenderLocationInteractionChoices(LocationData location)
    {
        if (narrativeChoicesContainer == null)
            return;

        narrativeChoicesContainer.Clear();

        Button researchButton = InstantiateFlatTemplate<Button>(LoadNarrativeChoiceButtonTemplate(), "narrative-dialogue-choice");
        if (researchButton != null)
        {
            string researchButtonText;
            string researchHint;
            bool researchEnabled = GetLocationResearchAvailability(location, out researchButtonText, out researchHint);
            researchButton.text = researchButtonText;
            researchButton.SetEnabled(researchEnabled);
            researchButton.clicked += OnLocationInteractionResearchClicked;
            narrativeChoicesContainer.Add(researchButton);

            if (!string.IsNullOrWhiteSpace(researchHint))
            {
                Label hint = InstantiateFlatTemplate<Label>(LoadNarrativeChoiceSecondaryTemplate(), "narrative-dialogue-choice-secondary");
                if (hint != null)
                {
                    hint.text = researchHint;
                    hint.AddToClassList("narrative-dialogue-choice-hint");
                    narrativeChoicesContainer.Add(hint);
                }
            }
        }

        Button cancelButton = InstantiateFlatTemplate<Button>(LoadNarrativeChoiceButtonTemplate(), "narrative-dialogue-choice");
        if (cancelButton != null)
        {
            cancelButton.text = "ОТМЕНИТЬ";
            cancelButton.clicked += OnLocationInteractionCancelClicked;
            narrativeChoicesContainer.Add(cancelButton);

            Label hint = InstantiateFlatTemplate<Label>(LoadNarrativeChoiceSecondaryTemplate(), "narrative-dialogue-choice-secondary");
            if (hint != null)
            {
                hint.text = "Остаться в локации, ничего не предпринимая.";
                hint.AddToClassList("narrative-dialogue-choice-hint");
                narrativeChoicesContainer.Add(hint);
            }
        }
    }

    // Вся содержательная логика (снабжение/уже исследовано/не реализовано)
    // остаётся в GameState.CanResearchActiveLocation — здесь только читаем
    // её и переводим в текст/подсказку кнопки (раздел "Реализовать кнопку
    // ИССЛЕДОВАТЬ" инструкции), без дублирования бизнес-правил в UI.
    private bool GetLocationResearchAvailability(LocationData location, out string buttonText, out string hint)
    {
        if (location.IsExplored)
        {
            buttonText = "ИССЛЕДОВАНО";
            hint = null;
            return false;
        }

        if (gameState.HasActiveExpedition && gameState.ActiveExpedition.IsLocationResearchInProgress)
        {
            buttonText = "ИССЛЕДОВАНИЕ...";
            hint = "Исследование уже идёт.";
            return false;
        }

        if (location.ExplorationHours <= 0.0)
        {
            buttonText = "ИССЛЕДОВАТЬ";
            hint = "Исследование этой локации пока не реализовано.";
            return false;
        }

        if (gameState.ArmySupply < gameState.ExpeditionSupplyConsumption)
        {
            buttonText = "ИССЛЕДОВАТЬ";
            hint = "Недостаточно снабжения.";
            return false;
        }

        buttonText = "ИССЛЕДОВАТЬ";
        hint = "Время: " + ContinuousExpeditionCommands.FormatHours(location.ExplorationHours) + ".";
        return gameState.CanResearchActiveLocation;
    }

    private void OnLocationInteractionResearchClicked()
    {
        if (!IsLocationInteractionActive || gameState == null || isGameOver)
            return;

        if (!gameState.CanResearchActiveLocation)
            return;

        string resultMessage;
        bool started = gameState.TryStartLocationResearch(out resultMessage);
        if (!started)
        {
            AddReport(NormalizeContinuousReportText(resultMessage));
            CloseLocationInteraction();
            RefreshContinuousTimeUi(true);
            return;
        }

        LocationData location = gameState.FindLocation(gameState.ActiveExpedition.LocationId);
        double hours = location != null ? location.ExplorationHours : 0.0;
        AddReport("Исследование начато. Расчётное время: " + ContinuousExpeditionCommands.FormatHours(hours) + ".");

        // Закрываем окно; время само продолжит идти — ActiveActivity=
        // LocationResearch уже стоит, RefreshAutoTimeState увидит это
        // следующим кадром (модель "движение/активность = течение времени").
        // Вручную SetPaused(false) здесь не вызывается.
        CloseLocationInteraction();
        RefreshContinuousTimeUi(true);
    }

    private void OnLocationInteractionCancelClicked()
    {
        if (!IsLocationInteractionActive)
            return;

        // Отменить не создаёт Activity, не меняет LocationId и не отдаёт
        // приказ на возврат — отряд просто остаётся AtLocation, и
        // RefreshAutoTimeState естественным образом оставит время
        // остановленным (нет движения/активности).
        CloseLocationInteraction();
    }

    private void CloseLocationInteraction()
    {
        locationInteractionActive = false;
        locationInteractionLocationId = null;

        if (narrativeDialogueOverlay != null)
            narrativeDialogueOverlay.style.display = DisplayStyle.None;
        if (narrativeChoicesContainer != null)
            narrativeChoicesContainer.Clear();
        if (narrativeHistoryContainer != null)
            narrativeHistoryContainer.Clear();
        if (narrativePortrait != null)
            narrativePortrait.style.display = DisplayStyle.Flex;

        RefreshInterface();
        RefreshTimeControlAvailability();
        ResumeAfterBlockingModalIfReady();
    }
}
