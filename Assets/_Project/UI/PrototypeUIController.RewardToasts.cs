using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Бриф исправлений п. 9: что получено — опыт, золото, припасы, вещи — сразу
// видно зелёными уведомлениями поверх любого экрана (и поверх диалога).
// Источник — RewardFeed (записи после фактической выдачи); те же записи
// попадают в Хронику завершённого поручения. Дублируются строкой донесений.
public partial class PrototypeUIController
{
    private const float RewardToastSeconds = 4.5f;

    private VisualElement rewardToastStack;
    private readonly List<(VisualElement Toast, float Until)> rewardToasts = new List<(VisualElement, float)>();
    private GameState rewardToastState;

    private void TickRewardToasts()
    {
        if (rewardToastStack == null && interfaceRoot != null)
            rewardToastStack = interfaceRoot.Q<VisualElement>("reward-toast-stack");
        if (rewardToastStack == null)
            return;

        // Другая партия (новая игра, загрузка) — прежние уведомления не её.
        if (!ReferenceEquals(rewardToastState, gameState))
        {
            rewardToastState = gameState;
            RewardFeed.ClearPending();
            rewardToastStack.Clear();
            rewardToasts.Clear();
            return;
        }

        float now = Time.unscaledTime;
        List<string> fresh = RewardFeed.TakePending();
        if (fresh.Count > 0)
        {
            foreach (string text in fresh)
            {
                Label toast = new Label(text) { pickingMode = PickingMode.Ignore };
                toast.AddToClassList("reward-toast");
                rewardToastStack.Add(toast);
                rewardToasts.Add((toast, now + RewardToastSeconds));
            }
            AddReport("Получено: " + string.Join(", ", fresh) + ".");
            rewardToastStack.BringToFront();
        }

        for (int i = rewardToasts.Count - 1; i >= 0; i--)
        {
            if (now < rewardToasts[i].Until)
                continue;
            rewardToasts[i].Toast.RemoveFromHierarchy();
            rewardToasts.RemoveAt(i);
        }
    }
}
