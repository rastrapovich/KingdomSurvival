using System.Collections.Generic;
using System.Text;
using KingdomSurvival.Chapter01;
using UnityEngine;
using UnityEngine.UIElements;

public partial class PrototypeUIController
{
    // ПР-02: домашняя часть главы N01–N10 без Debug — через Дом, людей и
    // место. Что доступно, решает Chapter01HomeActivities:
    //   - сцены, которые открываются сами (N01, ночные N04/N06, N10), —
    //     здесь, через короткую паузу, когда экран свободен;
    //   - дела-карточки на экране Дома — игрок открывает их сам.
    // Пока глава ждёт ночи, время идёт (RefreshAutoTimeState).
    // Вызывается из единственного LateUpdate.
    private const string HomeActivitiesScreenName = "Дом";
    private const float Chapter01HomeFlowDelaySeconds = 1f;
    private const string HomeActivityUrgentClass = "home-activity-card--urgent";

    private float chapter01HomeFlowReadyAt = -1f;

    private VisualElement homeActivitiesList;
    private Label homeActivitiesEmptyLabel;
    private VisualTreeAsset homeActivityCardTemplate;
    private string homeActivitiesSignature;
    private bool homeActivitiesBindAttempted;

    private void RefreshChapter01HomeFlow()
    {
        RefreshHomeActivitiesIfChanged();

        if (gameState == null || isGameOver || HasBlockingModalWork())
        {
            chapter01HomeFlowReadyAt = -1f;
            return;
        }

        string dialogueId = Chapter01StoryDirector.GetAutoOpenHomeDialogueId(gameState);
        if (string.IsNullOrEmpty(dialogueId))
        {
            chapter01HomeFlowReadyAt = -1f;
            return;
        }

        if (chapter01HomeFlowReadyAt < 0f)
        {
            chapter01HomeFlowReadyAt = Time.unscaledTime + Chapter01HomeFlowDelaySeconds;
            return;
        }

        if (Time.unscaledTime < chapter01HomeFlowReadyAt)
            return;

        chapter01HomeFlowReadyAt = -1f;
        TryOpenNarrativeDialogueById(dialogueId);
    }

    // Список дел пересобирается только при изменении: подпись — ID сцен и
    // признак «ждём ночи», а не каждый кадр.
    private void RefreshHomeActivitiesIfChanged()
    {
        if (gameState == null || interfaceRoot == null)
            return;

        if (homeActivitiesList == null)
        {
            // Один раз: при отсутствии элемента ошибка в консоли не повторяется каждый кадр.
            if (homeActivitiesBindAttempted)
                return;
            homeActivitiesBindAttempted = true;
            homeActivitiesList = BindRequiredElement<VisualElement>(interfaceRoot, HomeActivitiesScreenName, "home-activities-list");
            homeActivitiesEmptyLabel = BindRequiredElement<Label>(interfaceRoot, HomeActivitiesScreenName, "home-activities-empty");
            if (homeActivitiesList == null || homeActivitiesEmptyLabel == null)
                return;
        }

        IReadOnlyList<Chapter01HomeActivity> activities = Chapter01HomeActivities.GetAvailable(gameState);
        bool waitingForNight = Chapter01HomeActivities.IsWaitingForNight(gameState);

        StringBuilder signature = new StringBuilder(waitingForNight ? "night|" : "day|");
        for (int i = 0; i < activities.Count; i++)
            signature.Append(activities[i].DialogueId).Append('|');
        string current = signature.ToString();
        if (current == homeActivitiesSignature)
            return;

        homeActivitiesSignature = current;
        RebuildHomeActivities(activities, waitingForNight);
    }

    private void RebuildHomeActivities(IReadOnlyList<Chapter01HomeActivity> activities, bool waitingForNight)
    {
        homeActivitiesList.Clear();

        VisualTreeAsset template = LoadHomeActivityCardTemplate();
        if (template != null)
        {
            for (int i = 0; i < activities.Count; i++)
            {
                Chapter01HomeActivity activity = activities[i];
                TemplateContainer instance = template.Instantiate();
                Button card = instance.Q<Button>("home-activity-card");
                if (card == null)
                    continue;

                card.Q<Label>("home-activity-title").text = activity.Title;
                card.Q<Label>("home-activity-where").text = activity.Where;
                card.EnableInClassList(HomeActivityUrgentClass, activity.Urgent);

                string dialogueId = activity.DialogueId;
                card.clicked += () => OnHomeActivityClicked(dialogueId);
                homeActivitiesList.Add(instance);
            }
        }

        bool empty = activities.Count == 0;
        homeActivitiesEmptyLabel.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
        homeActivitiesEmptyLabel.text = waitingForNight
            ? "День идёт своим чередом. Ближе к ночи что-то изменится."
            : "Сейчас никто не ждёт тебя.";
    }

    private void OnHomeActivityClicked(string dialogueId)
    {
        if (!TryOpenNarrativeDialogueById(dialogueId))
            AddReport("Сейчас к этому делу не подступиться: закройте открытое окно.");
    }

    private VisualTreeAsset LoadHomeActivityCardTemplate()
    {
        if (homeActivityCardTemplate == null)
            homeActivityCardTemplate = Resources.Load<VisualTreeAsset>("Templates/HomeActivityCard");
        return homeActivityCardTemplate;
    }
}
