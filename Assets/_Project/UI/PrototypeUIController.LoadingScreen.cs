using KingdomSurvival.MapLabBridge;
using UnityEngine;
using UnityEngine.UIElements;

// 12У-G (запрос пользователя 08.11.2026): экран загрузки с полоской. По
// «Начать игру» (и при продолжении партии) сразу закрывает экран целиком:
// игрок видит, что игра грузится, а не зависла, и не видит полупустую
// карту с прежней фигурой героя. Полоска: создание мира, затем шаги сборки
// живой карты MapLab (доля по шагам). Экран уходит, когда карта готова
// (или когда живой карты нет — прежний рисунок, пакетный режим).
public partial class PrototypeUIController
{
    private VisualElement loadingScreen;
    private VisualElement loadingFill;
    private Label loadingStage;
    private Label loadingPercent;
    private bool loadingCampaignReady;
    private float loadingShownAt;
    private float loadingShown;

    public bool IsLoadingScreenOpen => loadingScreen != null && loadingScreen.style.display == DisplayStyle.Flex;

    private VisualElement rootVisualElementForLoading() => GetComponent<UIDocument>()?.rootVisualElement;

    private void EnsureLoadingScreen()
    {
        if (loadingScreen != null)
            return;
        VisualElement root = rootVisualElementForLoading();
        if (root == null)
            return;
        loadingScreen = new VisualElement { name = "loading-screen", pickingMode = PickingMode.Position };
        loadingScreen.style.position = Position.Absolute;
        loadingScreen.style.left = loadingScreen.style.top = loadingScreen.style.right = loadingScreen.style.bottom = 0;
        loadingScreen.style.backgroundColor = new Color(0.11f, 0.1f, 0.085f, 1f);
        loadingScreen.style.alignItems = Align.Center;
        loadingScreen.style.justifyContent = Justify.Center;

        Label title = new Label("KINGDOM SURVIVAL") { pickingMode = PickingMode.Ignore };
        title.style.fontSize = 34;
        title.style.color = new Color(0.9f, 0.84f, 0.7f);
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 28;
        loadingScreen.Add(title);

        VisualElement track = new VisualElement { name = "loading-track", pickingMode = PickingMode.Ignore };
        track.style.width = 520;
        track.style.height = 14;
        track.style.backgroundColor = new Color(0.22f, 0.19f, 0.15f, 1f);
        track.style.borderTopLeftRadius = track.style.borderTopRightRadius = track.style.borderBottomLeftRadius = track.style.borderBottomRightRadius = 7;
        track.style.overflow = Overflow.Hidden;
        loadingFill = new VisualElement { name = "loading-fill", pickingMode = PickingMode.Ignore };
        loadingFill.style.height = Length.Percent(100);
        loadingFill.style.width = Length.Percent(0);
        loadingFill.style.backgroundColor = new Color(0.78f, 0.62f, 0.34f, 1f);
        track.Add(loadingFill);
        loadingScreen.Add(track);

        VisualElement row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.width = 520;
        row.style.marginTop = 10;
        loadingStage = new Label { pickingMode = PickingMode.Ignore };
        loadingStage.style.color = new Color(0.78f, 0.72f, 0.6f);
        loadingStage.style.fontSize = 16;
        loadingPercent = new Label { pickingMode = PickingMode.Ignore };
        loadingPercent.style.color = new Color(0.78f, 0.72f, 0.6f);
        loadingPercent.style.fontSize = 16;
        row.Add(loadingStage);
        row.Add(loadingPercent);
        loadingScreen.Add(row);
        loadingScreen.style.display = DisplayStyle.None;
        root.Add(loadingScreen);
    }

    private void ShowLoadingScreen(string stage, float progress)
    {
        EnsureLoadingScreen();
        if (loadingScreen == null)
            return;
        loadingCampaignReady = false;
        loadingShownAt = Time.realtimeSinceStartup;
        loadingShown = 0f;
        loadingScreen.style.display = DisplayStyle.Flex;
        loadingScreen.BringToFront();
        SetLoadingProgress(stage, progress);
    }

    private void SetLoadingProgress(string stage, float progress)
    {
        // Полоска не отступает назад.
        loadingShown = Mathf.Max(loadingShown, Mathf.Clamp01(progress));
        if (loadingFill != null)
            loadingFill.style.width = Length.Percent(loadingShown * 100f);
        if (loadingStage != null)
            loadingStage.text = stage;
        if (loadingPercent != null)
            loadingPercent.text = Mathf.RoundToInt(loadingShown * 100f) + "%";
    }

    private void HideLoadingScreen()
    {
        if (loadingScreen != null)
            loadingScreen.style.display = DisplayStyle.None;
        loadingCampaignReady = false;
    }

    // Каждый кадр, пока экран открыт.
    private void TickLoadingScreen()
    {
        if (!IsLoadingScreenOpen)
            return;
        loadingScreen.BringToFront();
        if (!loadingCampaignReady || gameState == null)
            return;
        // Живой карты нет — грузить нечего.
        if (!WorldMapLiveWanted())
        {
            HideLoadingScreen();
            return;
        }
        MapLabGameLive.World world = MapLabGameLive.Current;
        bool ours = world != null && ReferenceEquals(world.Plan, gameState.GeneratedMap);
        if (ours && world.Ready && worldMapMapLab != null)
        {
            SetLoadingProgress("Готово", 1f);
            HideLoadingScreen();
            return;
        }
        if (ours && !string.IsNullOrEmpty(world.Error))
        {
            HideLoadingScreen();
            return;
        }
        // Карта ещё не начала строиться (экран карты строит её при показе).
        if (!ours)
        {
            if (!IsWorldMapScreenOpen && Time.realtimeSinceStartup - loadingShownAt > 2f && !worldMapLiveScheduled && worldMapLiveBuilding == null)
                ScheduleWorldMapLiveBuild();
            SetLoadingProgress("Готовим карту…", 0.05f);
            return;
        }
        string stage = world.Stage;
        SetLoadingProgress(string.IsNullOrEmpty(stage) ? "Рисуем карту…" : "Рисуем карту: " + stage + "…", 0.05f + 0.95f * world.Progress);
    }
}
