using UnityEngine;
using UnityEngine.InputSystem;

namespace KingdomSurvival.MapLab
{
    // Ортографическая камера MapLab с фиксированным азимутом. Наклон — режим
    // камеры из настроек (Атлас 60°, Объёмная карта 50°, Ближняя диорама 40°,
    // Custom) или, по выбору, зависит от приближения. orthographicSize =
    // fitSize / Zoom; fitSize — «вся карта в кадре» для текущего наклона.
    //
    // Target — точка фокуса НА ЗЕМЛЕ (центр кадра). Смена наклона вращает камеру
    // вокруг неё, поэтому выбранное место остаётся в центре. Полный Fit (перенос
    // к центру карты) — только явной командой.
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class MapLabCameraRig : MonoBehaviour
    {
        [Tooltip("Точка фокуса на рельефе (мир).")]
        public Vector3 Target = new Vector3(128f, 0f, 128f);
        [Tooltip("Скорость панорамирования клавишами, доля кадра в секунду.")]
        public float PanSpeed = 0.6f;
        [Tooltip("Пределы приближения (1 — вся карта).")]
        public Vector2 ZoomLimits = new Vector2(0.6f, 8f);

        private MapLabView view;
        private Camera cam;
        private float fitSize = 100f;
        private float fitAspect = -1f;
        private float zoom = 1f;
        private float zoomGoal = 1f;
        private bool anchored;
        private Vector3 anchorWorld;
        private Vector2 anchorViewport;
        private Vector3 lastGround;
        private bool hasGround;
        private bool initialized;
        private float initializedSize;
        private string diagnostics;

        public Camera Camera => cam != null ? cam : cam = GetComponent<Camera>();

        public float Zoom
        {
            get => zoom;
            set
            {
                zoom = Mathf.Clamp(value, ZoomLimits.x, ZoomLimits.y);
                zoomGoal = zoom;
                anchored = false;
            }
        }

        public float FitSize => fitSize;

        public float Pitch => view != null && view.Settings != null ? view.Settings.Camera.PitchFor(zoom, ZoomLimits.y) : 60f;

        public float Azimuth => view != null && view.Settings != null ? view.Settings.Camera.AzimuthDegrees : 0f;

        public MapLabCameraMode Mode => view != null && view.Settings != null ? view.Settings.Camera.Mode : MapLabCameraMode.Custom;

        public bool PitchByZoom => view != null && view.Settings != null && view.Settings.Camera.PitchByZoom;

        public void Bind(MapLabView mapView, bool refit)
        {
            view = mapView;
            if (view == null || view.Settings == null)
                return;
            float size = view.Data != null ? view.Data.Size : view.Settings.General.MapSize;
            // После перегенерации той же по размеру карты вид сохраняется.
            if (refit && (!initialized || Mathf.Abs(initializedSize - size) > 0.01f))
            {
                ResetView();
                initialized = true;
                initializedSize = size;
                return;
            }
            RecomputeFitSize();
            Apply();
        }

        // Явная команда: приближение из настроек и вся карта в кадре.
        public void ResetView()
        {
            if (view == null || view.Settings == null)
                return;
            Zoom = view.Settings.Camera.Zoom;
            Fit();
        }

        // Вписать всю карту: проекция углов объёма карты на оси камеры. Центр
        // кадра затем опускается вдоль луча зрения на рельеф (кадр не меняется).
        public void Fit()
        {
            MapLabData data = view != null ? view.Data : null;
            float size = data != null ? data.Size : (view != null && view.Settings != null ? view.Settings.General.MapSize : 256f);
            float min = data != null ? data.MinHeight : 0f;
            float max = data != null ? data.MaxHeight + 6f : 20f;
            Target = new Vector3(size * 0.5f, (min + max) * 0.5f, size * 0.5f);
            Measure(Target, out Vector2 center, out Vector2 extent);
            Quaternion rotation = Rotation();
            Target += rotation * Vector3.right * center.x + rotation * Vector3.up * center.y;
            Target = ToGround(Target);
            fitSize = FitFromExtent(extent);
            fitAspect = Camera.aspect;
            anchored = false;
            Apply();
        }

        // «Вся карта» для текущего наклона и соотношения сторон — без переноса фокуса.
        public void RecomputeFitSize()
        {
            Measure(Target, out _, out Vector2 extent);
            fitSize = FitFromExtent(extent);
            fitAspect = Camera.aspect;
        }

        private void Measure(Vector3 origin, out Vector2 center, out Vector2 extent)
        {
            MapLabData data = view != null ? view.Data : null;
            float size = data != null ? data.Size : (view != null && view.Settings != null ? view.Settings.General.MapSize : 256f);
            float min = data != null ? data.MinHeight : 0f;
            float max = data != null ? data.MaxHeight + 6f : 20f;
            Quaternion rotation = Rotation();
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? 0f : size, (i & 2) == 0 ? min : max, (i & 4) == 0 ? 0f : size) - origin;
                float x = Vector3.Dot(corner, right);
                float y = Vector3.Dot(corner, up);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            center = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            extent = new Vector2(maxX - minX, maxY - minY);
        }

        private float FitFromExtent(Vector2 extent)
        {
            float aspect = Mathf.Max(0.1f, Camera.aspect);
            // «Заполнить кадр» — картина без полей бумаги, как в эталоне;
            // иначе вся карта целиком с полями.
            float byHeight = extent.y * 0.5f;
            float byWidth = extent.x * 0.5f / aspect;
            bool fill = view == null || view.Settings == null || view.Settings.Camera.FillFrame;
            return Mathf.Max(1f, fill ? Mathf.Min(byHeight, byWidth) * 0.985f : Mathf.Max(byHeight, byWidth) * 1.02f);
        }

        public Quaternion Rotation()
        {
            return Quaternion.Euler(Mathf.Clamp(Pitch, 5f, 90f), Azimuth, 0f);
        }

        public void Apply()
        {
            Camera c = Camera;
            if (fitAspect > 0f && Mathf.Abs(c.aspect - fitAspect) > 1e-3f)
                RecomputeFitSize();
            c.orthographic = true;
            c.orthographicSize = fitSize / Mathf.Max(0.05f, zoom);
            Quaternion rotation = Rotation();
            float size = view != null && view.Data != null ? view.Data.Size : 256f;
            float distance = size * 2f + 200f;
            transform.SetPositionAndRotation(Target - rotation * Vector3.forward * distance, rotation);
            c.nearClipPlane = Mathf.Max(0.3f, distance - size * 1.6f - 60f);
            c.farClipPlane = distance + size * 1.6f + 60f;
            if (view != null && view.Settings != null)
                c.backgroundColor = view.Palette.Paper;
            c.clearFlags = CameraClearFlags.SolidColor;
        }

        // Режим наклона: фокус (точка на земле в центре кадра) сохраняется.
        public void SetMode(MapLabCameraMode mode)
        {
            if (view == null || view.Settings == null)
                return;
            view.Settings.Camera.Mode = mode;
            view.Settings.Camera.PitchByZoom = false;
            if (view.Data == null || Mathf.Abs(Target.y - view.Data.SampleHeight(Target.x, Target.z)) > 0.05f)
                Target = ToGround(Target);
            RecomputeFitSize();
            Apply();
            view.OnCameraChanged();
        }

        // Приближение вокруг точки экрана (0..1): точка рельефа под ней остаётся
        // на месте. smooth — цель для плавного приближения (Play Mode).
        public void ZoomAround(float newZoom, Vector2 viewport, bool smooth)
        {
            newZoom = Mathf.Clamp(newZoom, ZoomLimits.x, ZoomLimits.y);
            anchorWorld = PickGround(viewport);
            anchorViewport = viewport;
            if (smooth && Application.isPlaying)
            {
                zoomGoal = newZoom;
                anchored = true;
                return;
            }
            zoom = zoomGoal = newZoom;
            ApplyAnchored();
            anchored = false;
            if (view != null)
                view.OnCameraChanged();
        }

        // Точка рельефа под точкой экрана; при промахе — последняя попавшая или
        // плоскость на высоте фокуса (не ноль мира).
        public Vector3 PickGround(Vector2 viewport)
        {
            Ray ray = Camera.ViewportPointToRay(new Vector3(viewport.x, viewport.y, 0f));
            if (view != null && view.Raycast(ray, out Vector3 hit))
            {
                lastGround = hit;
                hasGround = true;
                return hit;
            }
            float planeY = hasGround ? lastGround.y : Target.y;
            if (Mathf.Abs(ray.direction.y) > 1e-4f)
            {
                float t = (planeY - ray.origin.y) / ray.direction.y;
                if (t > 0f)
                    return ray.origin + ray.direction * t;
            }
            return hasGround ? lastGround : Target;
        }

        private void ApplyAnchored()
        {
            Apply();
            Camera c = Camera;
            Vector3 vp = c.WorldToViewportPoint(anchorWorld);
            float h = c.orthographicSize * 2f, w = h * c.aspect;
            Quaternion rotation = Rotation();
            Target += rotation * Vector3.right * ((vp.x - anchorViewport.x) * w) + rotation * Vector3.up * ((vp.y - anchorViewport.y) * h);
            Target = ToGround(Target);
            ClampTarget();
            Apply();
        }

        // Сдвиг вдоль луча зрения до рельефа: кадр не меняется.
        public Vector3 ToGround(Vector3 point)
        {
            if (view == null || view.Data == null)
                return point;
            Vector3 forward = Rotation() * Vector3.forward;
            Ray ray = new Ray(point - forward * (view.Data.Size * 2f + 100f), forward);
            if (view.Raycast(ray, out Vector3 hit))
            {
                lastGround = hit;
                hasGround = true;
                return hit;
            }
            return point;
        }

        private void ClampTarget()
        {
            if (view == null || view.Data == null)
                return;
            float size = view.Data.Size;
            Target.x = Mathf.Clamp(Target.x, -size * 0.1f, size * 1.1f);
            Target.z = Mathf.Clamp(Target.z, -size * 0.3f, size * 1.1f);
        }

        private void Update()
        {
            if (!Application.isPlaying || view == null || view.Data == null)
                return;
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            Camera c = Camera;
            Quaternion rotation = Rotation();
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f)
                forward = Vector3.forward;
            bool moved = false;
            if (keyboard != null)
            {
                Vector2 move = Vector2.zero;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
                if (move != Vector2.zero)
                {
                    float speed = c.orthographicSize * 2f * PanSpeed * Time.unscaledDeltaTime;
                    Target += (right * move.x + forward * move.y) * speed;
                    anchored = false;
                    moved = true;
                }
                if (keyboard.fKey.wasPressedThisFrame)
                {
                    Fit();
                    return;
                }
                if (keyboard.rKey.wasPressedThisFrame)
                {
                    ResetView();
                    return;
                }
                if (keyboard.digit1Key.wasPressedThisFrame) SetMode(MapLabCameraMode.Atlas60);
                if (keyboard.digit2Key.wasPressedThisFrame) SetMode(MapLabCameraMode.Volume50);
                if (keyboard.digit3Key.wasPressedThisFrame) SetMode(MapLabCameraMode.Diorama40);
                if (keyboard.digit4Key.wasPressedThisFrame) SetMode(MapLabCameraMode.Low35);
                if (keyboard.pKey.wasPressedThisFrame)
                {
                    view.Settings.Camera.PitchByZoom = !view.Settings.Camera.PitchByZoom;
                    RecomputeFitSize();
                    view.OnCameraChanged();
                    moved = true;
                }
            }
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    Vector2 pointer = mouse.position.ReadValue();
                    Vector2 viewport = new Vector2(pointer.x / Mathf.Max(1, c.pixelWidth), pointer.y / Mathf.Max(1, c.pixelHeight));
                    ZoomAround(zoomGoal * (scroll > 0f ? 1.18f : 1f / 1.18f), viewport, true);
                }
                if (mouse.rightButton.isPressed || mouse.middleButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    float unitsPerPixel = c.orthographicSize * 2f / Mathf.Max(1, c.pixelHeight);
                    Target -= right * delta.x * unitsPerPixel;
                    Target -= forward * delta.y * unitsPerPixel / Mathf.Max(0.2f, Mathf.Sin(Pitch * Mathf.Deg2Rad));
                    if (delta.sqrMagnitude > 0f)
                    {
                        anchored = false;
                        moved = true;
                    }
                }
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    Ray ray = c.ScreenPointToRay(mouse.position.ReadValue());
                    diagnostics = view.Raycast(ray, out Vector3 point) ? view.Describe(point) : "Вне карты.";
                }
            }
            if (Mathf.Abs(zoomGoal - zoom) > 1e-4f)
            {
                // Экспоненциальное сглаживание в логарифме приближения; якорь —
                // точка рельефа под курсором в момент прокрутки.
                float k = 1f - Mathf.Exp(-view.Settings.Camera.ZoomSmoothing * Time.unscaledDeltaTime);
                zoom = Mathf.Exp(Mathf.Lerp(Mathf.Log(zoom), Mathf.Log(zoomGoal), k));
                if (Mathf.Abs(zoomGoal - zoom) < 1e-3f)
                    zoom = zoomGoal;
                if (PitchByZoom)
                    RecomputeFitSize();
                if (anchored)
                    ApplyAnchored();
                else
                    Apply();
                view.OnCameraChanged();
                return;
            }
            if (moved)
            {
                ClampTarget();
                Target = ToGround(Target);
                Apply();
            }
        }

        public string ControlsHint =>
            "Play Mode: WASD/стрелки — сдвиг, ПКМ/СКМ — перетаскивание, колесо — плавное приближение к курсору, " +
            "1/2/3/4 — Атлас 60° / Объёмная 50° / Диорама 40° / Низкая 35°, P — наклон от приближения (до 35° на наибольшем приближении), F — вся карта, R — сброс, ЛКМ — диагностика точки.";

        private void OnGUI()
        {
            if (!Application.isPlaying || view == null)
                return;
            string mode = PitchByZoom ? "наклон от приближения" : Mode.ToString();
            string approx = view.ArtSpritesApproximate ? " · ракурс рисунков приближённый" : "";
            GUI.Label(new Rect(12, 10, 1400, 22), $"{mode} · наклон {Pitch:0.#}° · Zoom {zoom:0.00}{approx}");
            GUI.Label(new Rect(12, 30, 1600, 22), ControlsHint);
            if (!string.IsNullOrEmpty(diagnostics))
                GUI.Label(new Rect(12, 50, 900, 22), diagnostics);
        }
    }
}
