using UnityEngine;
using UnityEngine.InputSystem;

namespace KingdomSurvival.MapLab
{
    // Ортографическая камера MapLab с фиксированным азимутом. Наклон —
    // CameraPitchDownDegrees из настроек (90 — строго сверху, 0 — вдоль земли).
    // Fit учитывает проекцию, соотношение сторон и высоту геометрии; near/far
    // считаются от объёма карты, поэтому высокие скалы не обрезаются.
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class MapLabCameraRig : MonoBehaviour
    {
        [Tooltip("Точка, на которую смотрит камера (мир).")]
        public Vector3 Target = new Vector3(128f, 0f, 128f);
        [Tooltip("Скорость панорамирования клавишами, доля кадра в секунду.")]
        public float PanSpeed = 0.6f;
        [Tooltip("Пределы приближения (1 — вся карта).")]
        public Vector2 ZoomLimits = new Vector2(0.6f, 8f);

        private MapLabView view;
        private Camera cam;
        private float fitSize = 100f;
        private float zoom = 1f;
        private string diagnostics;

        public Camera Camera => cam != null ? cam : cam = GetComponent<Camera>();

        public float Zoom
        {
            get => zoom;
            set => zoom = Mathf.Clamp(value, ZoomLimits.x, ZoomLimits.y);
        }

        public void Bind(MapLabView mapView, bool refit)
        {
            view = mapView;
            if (view == null || view.Settings == null)
                return;
            if (refit)
            {
                zoom = Mathf.Clamp(view.Settings.Camera.Zoom, ZoomLimits.x, ZoomLimits.y);
                Fit();
            }
            else
            {
                Apply();
            }
        }

        public void ResetView()
        {
            if (view == null || view.Settings == null)
                return;
            zoom = Mathf.Clamp(view.Settings.Camera.Zoom, ZoomLimits.x, ZoomLimits.y);
            Fit();
        }

        // Вписать всю карту: проекция углов объёма карты на оси камеры.
        public void Fit()
        {
            MapLabData data = view != null ? view.Data : null;
            float size = data != null ? data.Size : (view != null && view.Settings != null ? view.Settings.General.MapSize : 256f);
            float min = data != null ? data.MinHeight : 0f;
            float max = data != null ? data.MaxHeight + 6f : 20f;
            Target = new Vector3(size * 0.5f, (min + max) * 0.5f, size * 0.5f);
            Quaternion rotation = Rotation();
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? 0f : size, (i & 2) == 0 ? min : max, (i & 4) == 0 ? 0f : size) - Target;
                float x = Vector3.Dot(corner, right);
                float y = Vector3.Dot(corner, up);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            Target += right * (minX + maxX) * 0.5f + up * (minY + maxY) * 0.5f;
            float aspect = Mathf.Max(0.1f, Camera.aspect);
            // «Заполнить кадр» — картина без полей бумаги, как в эталоне;
            // иначе вся карта целиком с полями.
            float byHeight = (maxY - minY) * 0.5f;
            float byWidth = (maxX - minX) * 0.5f / aspect;
            bool fill = view == null || view.Settings == null || view.Settings.Camera.FillFrame;
            fitSize = Mathf.Max(1f, fill ? Mathf.Min(byHeight, byWidth) * 0.985f : Mathf.Max(byHeight, byWidth) * 1.02f);
            Apply();
        }

        public Quaternion Rotation()
        {
            float pitch = view != null && view.Settings != null ? view.Settings.Camera.CameraPitchDownDegrees : 60f;
            float azimuth = view != null && view.Settings != null ? view.Settings.Camera.AzimuthDegrees : 0f;
            return Quaternion.Euler(Mathf.Clamp(pitch, 5f, 90f), azimuth, 0f);
        }

        public void Apply()
        {
            Camera c = Camera;
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
            }
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    Zoom = zoom * (scroll > 0f ? 1.12f : 1f / 1.12f);
                    moved = true;
                }
                if (mouse.rightButton.isPressed || mouse.middleButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    float unitsPerPixel = c.orthographicSize * 2f / Mathf.Max(1, c.pixelHeight);
                    Target -= right * delta.x * unitsPerPixel;
                    Target -= forward * delta.y * unitsPerPixel / Mathf.Max(0.2f, Mathf.Sin(rotation.eulerAngles.x * Mathf.Deg2Rad));
                    moved = true;
                }
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    Ray ray = c.ScreenPointToRay(mouse.position.ReadValue());
                    diagnostics = view.Raycast(ray, out Vector3 point) ? view.Describe(point) : "Вне карты.";
                }
            }
            if (moved)
            {
                float size = view.Data.Size;
                Target.x = Mathf.Clamp(Target.x, -size * 0.1f, size * 1.1f);
                Target.z = Mathf.Clamp(Target.z, -size * 0.3f, size * 1.1f);
                Apply();
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || string.IsNullOrEmpty(diagnostics))
                return;
            GUI.Label(new Rect(12, 10, 900, 24), diagnostics);
        }
    }
}
