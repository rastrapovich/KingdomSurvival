using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// Камера исследуемого места и боя на месте: приближение и отдаление
// (колесо, кнопки «+ / −», клавиши + / −, «0» — сброс) и фиксация
// («Закрепить», клавиша F): закреплённая камера стоит на месте и сохраняет
// масштаб — не едет за командиром, колесо и кнопки масштаба не действуют.
// В бою на месте приближается само поле (гексы и фигуры), рисунок места
// следует за ним; средняя кнопка мыши — сдвиг поля.
public partial class PrototypeUIController
{
    private const float LocalZoomMin = 0.5f, LocalZoomMax = 2.5f;
    private const float LocalBattleZoomMin = 0.6f, LocalBattleZoomMax = 2.5f;
    private const float LocalZoomStep = 1.15f;

    // Закреплена ли камера: общая для места и боя на месте, живёт до выхода из игры.
    private bool localCameraLocked;
    private float localBattleZoom = 1f;
    private Vector2 localBattlePan;
    private bool localBattlePanning;
    private Vector2 localBattlePanStart;
    private Vector2 localBattlePanOrigin;
    private BattlefieldView localBattleSurface;

    private VisualElement localCameraPanel;
    private Label localCameraZoomLabel;
    private Button localCameraLockButton;
    private Button localCameraZoomInButton;
    private Button localCameraZoomOutButton;
    private Button localCameraResetButton;

    private bool LocalCameraControlsActive => IsLocalScreenOpen && !HasBlockingModalWork() && !IsNarrativeDialogueActive;

    // Панель камеры — поверх поля и боя (в экране места, после HUD).
    private void BuildLocalCameraPanel()
    {
        if (localScreen == null)
            return;
        localCameraPanel?.RemoveFromHierarchy();
        localCameraPanel = new VisualElement { name = "local-camera-panel" };
        localCameraPanel.AddToClassList("local-camera-panel");
        localCameraPanel.AddToClassList("ks-panel-deep");

        localCameraZoomOutButton = CameraButton("−", "Отдалить (колесо мыши, клавиша −)", () => ZoomLocalCamera(1f / LocalZoomStep));
        localCameraZoomLabel = new Label { name = "local-camera-zoom" };
        localCameraZoomLabel.AddToClassList("local-camera-zoom");
        localCameraZoomInButton = CameraButton("+", "Приблизить (колесо мыши, клавиша +)", () => ZoomLocalCamera(LocalZoomStep));
        localCameraResetButton = CameraButton("1:1", "Обычный масштаб (клавиша 0)", ResetLocalZoom);
        localCameraLockButton = CameraButton("ЗАКРЕПИТЬ", "Закрепить камеру: стоит на месте и держит масштаб (клавиша F)", ToggleLocalCameraLock);
        localCameraLockButton.AddToClassList("local-camera-lock");

        localCameraPanel.Add(localCameraZoomOutButton);
        localCameraPanel.Add(localCameraZoomLabel);
        localCameraPanel.Add(localCameraZoomInButton);
        localCameraPanel.Add(localCameraResetButton);
        localCameraPanel.Add(localCameraLockButton);
        localScreen.Add(localCameraPanel);
        RefreshLocalCameraPanel();
    }

    private static Button CameraButton(string text, string tooltip, System.Action action)
    {
        Button button = new Button(action) { text = text, tooltip = tooltip };
        button.AddToClassList("ks-button");
        button.AddToClassList("local-camera-button");
        return button;
    }

    private float CurrentLocalZoom => IsLocalBattleRunning ? localBattleZoom : localZoom;

    private void RefreshLocalCameraPanel()
    {
        if (localCameraPanel == null)
            return;
        localCameraPanel.BringToFront();
        localCameraZoomLabel.text = Mathf.RoundToInt(CurrentLocalZoom * 100f) + "%";
        localCameraLockButton.text = localCameraLocked ? "ЗАКРЕПЛЕНО" : "ЗАКРЕПИТЬ";
        localCameraLockButton.tooltip = localCameraLocked
            ? "Камера закреплена: стоит на месте и держит масштаб. Нажмите, чтобы отпустить (клавиша F)."
            : "Закрепить камеру: стоит на месте и держит масштаб (клавиша F)";
        localCameraLockButton.EnableInClassList("local-camera-lock--on", localCameraLocked);
        localCameraZoomInButton.SetEnabled(!localCameraLocked);
        localCameraZoomOutButton.SetEnabled(!localCameraLocked);
        localCameraResetButton.SetEnabled(!localCameraLocked);
    }

    private void ToggleLocalCameraLock()
    {
        localCameraLocked = !localCameraLocked;
        localBattlePanning = false;
        RefreshLocalCameraPanel();
        ShowLocalNotice(localCameraLocked ? "Камера закреплена." : IsLocalBattleRunning ? "Камера отпущена." : "Камера снова следует за командиром.");
    }

    // ------------------------------------------------------------------
    // Масштаб
    // ------------------------------------------------------------------

    private void ZoomLocalCamera(float factor) => StepLocalZoom(factor, null);

    // anchorPanel — точка экрана, которая при приближении остаётся на месте
    // (курсор); null — центр поля.
    private void StepLocalZoom(float factor, Vector2? anchorPanel)
    {
        if (localCameraLocked || !IsLocalScreenOpen)
            return;
        if (IsLocalBattleRunning)
            SetLocalBattleZoom(localBattleZoom * factor, anchorPanel);
        else
            localZoom = Mathf.Clamp(localZoom * factor, LocalZoomMin, LocalZoomMax);
        RefreshLocalCameraPanel();
    }

    private void ResetLocalZoom()
    {
        if (localCameraLocked || !IsLocalScreenOpen)
            return;
        if (IsLocalBattleRunning)
        {
            localBattleZoom = 1f;
            localBattlePan = Vector2.zero;
            localBattleCentered = false;
            ApplyLocalBattleCamera();
        }
        else
        {
            localZoom = 1f;
        }
        RefreshLocalCameraPanel();
    }

    private void OnLocalWheel(WheelEvent evt)
    {
        evt.StopPropagation();
        if (localCameraLocked)
            return;
        ZoomLocalCamera(evt.delta.y > 0 ? 1f / LocalZoomStep : LocalZoomStep);
    }

    private void HandleLocalCameraHotkeys()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !LocalCameraControlsActive)
            return;
        if (interfaceRoot?.panel?.focusController?.focusedElement is TextField)
            return;
        if (keyboard.fKey.wasPressedThisFrame)
            ToggleLocalCameraLock();
        else if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
            ZoomLocalCamera(LocalZoomStep);
        else if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
            ZoomLocalCamera(1f / LocalZoomStep);
        else if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame)
            ResetLocalZoom();
    }


    // ------------------------------------------------------------------
    // Бой на месте: масштаб самого поля
    // ------------------------------------------------------------------

    // Новый бой: поле принимает колесо и сдвиг средней кнопкой. Поле
    // преобразуется само (масштаб от левого верхнего угла и сдвиг): его
    // место в раскладке боя не меняется, панели боя лежат поверх, рисунок
    // места подстраивается под новый кадр поля (AlignLocalBattleCamera).
    // Закреплённая камера держит прежний масштаб и сдвиг.
    private void BindLocalBattleCamera()
    {
        localBattleSurface = localBattle?.BattlefieldSurface;
        if (!localCameraLocked)
        {
            localBattleZoom = 1f;
            localBattlePan = Vector2.zero;
        }
        localBattlePanning = false;
        localBattleCentered = false;
        if (localBattleSurface == null)
            return;
        localBattleSurface.style.transformOrigin = new TransformOrigin(0f, 0f);
        // Поле — вся локация: окно камеры — родитель поля, лишнее обрезается.
        if (localBattleSurface.HasLocationGrid && localBattleSurface.parent != null)
        {
            localBattleSurface.parent.style.overflow = Overflow.Hidden;
            localBattleSurface.parent.RegisterCallback<GeometryChangedEvent>(_ => ApplyLocalBattleCamera());
        }
        localBattleSurface.RegisterCallback<WheelEvent>(OnLocalBattleWheel);
        localBattleSurface.RegisterCallback<PointerDownEvent>(OnLocalBattlePointerDown, TrickleDown.TrickleDown);
        localBattleSurface.RegisterCallback<PointerMoveEvent>(OnLocalBattlePointerMove, TrickleDown.TrickleDown);
        localBattleSurface.RegisterCallback<PointerUpEvent>(OnLocalBattlePointerUp, TrickleDown.TrickleDown);
        localBattleSurface.RegisterCallback<GeometryChangedEvent>(_ => ApplyLocalBattleCamera());
        ApplyLocalBattleCamera();
        RefreshLocalCameraPanel();
    }

    private void OnLocalBattleWheel(WheelEvent evt)
    {
        evt.StopPropagation();
        if (localCameraLocked)
            return;
        StepLocalZoom(evt.delta.y > 0 ? 1f / LocalZoomStep : LocalZoomStep, evt.mousePosition);
    }

    private void OnLocalBattlePointerDown(PointerDownEvent evt)
    {
        if (evt.button != 2 || localCameraLocked || localBattleSurface == null)
            return;
        localBattlePanning = true;
        localBattlePanStart = evt.position;
        localBattlePanOrigin = localBattlePan;
        localBattleSurface.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    private void OnLocalBattlePointerMove(PointerMoveEvent evt)
    {
        if (!localBattlePanning)
            return;
        localBattlePan = localBattlePanOrigin + (Vector2)evt.position - localBattlePanStart;
        ApplyLocalBattleCamera();
        evt.StopPropagation();
    }

    private void OnLocalBattlePointerUp(PointerUpEvent evt)
    {
        if (!localBattlePanning || evt.button != 2)
            return;
        localBattlePanning = false;
        localBattleSurface?.ReleasePointer(evt.pointerId);
        evt.StopPropagation();
    }

    // Прямоугольник поля в раскладке боя (без масштаба) — «окно» камеры.
    // Поле на всю локацию больше экрана: окно — его родитель.
    private Rect LocalBattleArea => localBattleSurface == null ? default
        : localBattleSurface.HasLocationGrid && localBattleSurface.parent != null
            ? new Rect(Vector2.zero, localBattleSurface.parent.contentRect.size)
            : localBattleSurface.layout;

    // Бой на всей локации стартует кадром у столкновения (как прежняя арена).
    private bool localBattleCentered;

    // Пикселей экрана на пиксель рисунка места при масштабе 1: кадр боя
    // («Ширина кадра боя») — во всю ширину окна, как раньше арена.
    private float LocalBattleBaseScale(Rect view) => view.width / Mathf.Max(1f, localGeometry != null ? localGeometry.FrameWidth : 1920f);

    private float LocalBattleZoomFloor(Rect view)
    {
        if (localBattleSurface == null || !localBattleSurface.HasLocationGrid || localGeometry == null) return LocalBattleZoomMin;
        float scale = LocalBattleBaseScale(view);
        // Отдалить можно до всей локации целиком.
        float whole = Mathf.Min(view.width / (localGeometry.CanvasWidth * scale), view.height / (localGeometry.CanvasHeight * scale));
        return Mathf.Min(LocalBattleZoomMin, whole);
    }

    private void SetLocalBattleZoom(float zoom, Vector2? anchorPanel)
    {
        if (localBattleSurface == null || localBattleSurface.parent == null)
            return;
        Rect area = LocalBattleArea;
        float next = Mathf.Clamp(zoom, LocalBattleZoomFloor(area), LocalBattleZoomMax);
        Vector2 anchor = anchorPanel.HasValue
            ? localBattleSurface.parent.WorldToLocal(anchorPanel.Value) - area.position
            : area.size / 2f;
        // Точка поля под курсором остаётся под курсором.
        localBattlePan = anchor - (anchor - localBattlePan) * (next / Mathf.Max(0.01f, localBattleZoom));
        localBattleZoom = next;
        ApplyLocalBattleCamera();
    }

    // Крупнее своего прямоугольника — без пустых полей по краям; мельче — по центру.
    private void ApplyLocalBattleCamera()
    {
        if (localBattleSurface == null)
            return;
        Rect area = LocalBattleArea;
        if (float.IsNaN(area.width) || area.width < 1f || area.height < 1f)
            return;
        float zoom = localBattleZoom;
        Vector2 size = area.size * zoom;
        if (localBattleSurface.HasLocationGrid && localGeometry != null)
        {
            // Поле — весь рисунок места в масштабе кадра боя.
            float scale = LocalBattleBaseScale(area);
            Vector2 canvas = new Vector2(localGeometry.CanvasWidth, localGeometry.CanvasHeight) * scale;
            if (Mathf.Abs(localBattleSurface.resolvedStyle.width - canvas.x) > .5f || Mathf.Abs(localBattleSurface.resolvedStyle.height - canvas.y) > .5f)
            {
                localBattleSurface.style.width = canvas.x;
                localBattleSurface.style.height = canvas.y;
            }
            size = canvas * zoom;
            if (!localBattleCentered)
            {
                localBattleCentered = true;
                localBattlePan = area.size / 2f - localArenaCenter * scale * zoom;
            }
        }
        localBattlePan = new Vector2(ClampPan(localBattlePan.x, area.width, size.x), ClampPan(localBattlePan.y, area.height, size.y));
        localBattleSurface.style.scale = new Scale(new Vector3(zoom, zoom, 1f));
        localBattleSurface.style.translate = new Translate(localBattlePan.x, localBattlePan.y);
    }

    private static float ClampPan(float pan, float view, float content) =>
        content <= view ? (view - content) / 2f : Mathf.Clamp(pan, view - content, 0f);
}
