using System;
using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering
{
    // Универсальная служебная сцена: те же renderer/анимации/движение,
    // отдельное состояние без CampaignSession, расходов, событий и save/load.
    // Движение — как на глобальной карте (LocalFreeMover), камера следует
    // за командиром.
    public sealed class LocationLightingTest : MonoBehaviour
    {
        public PanelSettings Panel;
        public string LocationId = "technical_lighting_camp";
        public LocationWorldRenderer Renderer { get; private set; }
        public LocalFreeMover Mover { get; private set; }
        public float Hour = 13;
        public bool Cycle;
        public float CycleSeconds = 30;
        private Label clock, notice, heightLabel;
        private Slider timeSlider;
        private UIDocument document;
        private PanelSettings panelInstance;

        private void Start()
        {
            string id = LocationId;
#if UNITY_EDITOR
            id = UnityEditor.SessionState.GetString("KS.LocationLighting.Id", id);
            Hour = UnityEditor.SessionState.GetFloat("KS.LocationLighting.Hour", Hour);
#endif
            try
            {
                Initialize(Resources.Load<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.ResourcesPath),
                    Resources.Load<BattlefieldDatabaseAsset>(BattlefieldDatabaseAsset.ResourcesPath), id);
                BuildHud();
            }
            catch (Exception error) { Debug.LogException(error); enabled = false; }
        }

        public void Initialize(LocalLocationDatabaseAsset database, BattlefieldDatabaseAsset fields, string id)
        {
            LocalLocationDefinition location = database?.locations.Find(item => item.Id == id);
            LocationVisualDefinition visual = database?.FindVisual(id);
            BattlefieldDefinitionData field = location != null ? fields?.FindById(location.BattlefieldId) : null;
            List<string> errors = LocationVisualGeometry.Validate(location, visual, field);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
            Renderer?.Dispose();
            Renderer = new LocationWorldRenderer(location, visual, field, transform);
            Vector2 start = LocationVisualGeometry.ToPixel(location, visual.TestStartPoint);
            int count = Mathf.Clamp(visual.TestFollowers + 1, 1, 5);
            float spacing = Renderer.HexSizePixels * 1.2f;
            List<LocalPointData> points = Renderer.Geometry.SpreadAround(new LocalPointData(start.x, start.y), count, spacing);
            List<KeyValuePair<string, LocalPointData>> members = new List<KeyValuePair<string, LocalPointData>>();
            for (int i = 0; i < count; i++) members.Add(new KeyValuePair<string, LocalPointData>("test_" + i, points[i]));
            Mover = new LocalFreeMover(Renderer.Geometry.Layer, Renderer.Geometry.Rules, members, spacing);
            ViewHeight = LocationCameraFollow.DefaultViewHeight(visual, location);
            RenderActors(0, 0, true);
            Renderer.SetTime(Hour, 0);
        }

        // Высота видимой части рисунка (пиксели) — камера следует за командиром.
        public float ViewHeight { get; set; } = 1080;

        // Высота земли под ногами командира (если у места есть карта высот).
        public HeightSample LeaderHeight { get; private set; }

        private void RenderActors(float seconds, float deltaTime, bool snap)
        {
            List<LocationWorldRenderer.ActorFrame> frames = new List<LocationWorldRenderer.ActorFrame>();
            for (int i = 0; i < Mover.Members.Count; i++)
            {
                LocalFreeMover.Member member = Mover.Members[i];
                frames.Add(new LocationWorldRenderer.ActorFrame
                {
                    Id = member.Id, UnitTypeId = Renderer.Definition.TestUnitId,
                    Kind = i == 0 ? LocationWorldRenderer.ActorKind.Party : LocationWorldRenderer.ActorKind.Retinue,
                    Pixel = new Vector2((float)member.X, (float)member.Y),
                    Direction = new Vector2((float)member.DirectionX, (float)member.DirectionY),
                    Walking = member.Walking
                });
            }
            Renderer.SetActors(frames, seconds);
            LocalFreeMover.Member leader = Mover.Leader;
            Renderer.Follow(new Vector2((float)leader.X, (float)leader.Y), ViewHeight, deltaTime, snap);
            Renderer.TrySampleActorHeight(leader.Id, out HeightSample height);
            LeaderHeight = height;
        }

        private void BuildHud()
        {
            document = gameObject.AddComponent<UIDocument>();
            panelInstance = Panel != null ? Instantiate(Panel) : ScriptableObject.CreateInstance<PanelSettings>();
            document.panelSettings = panelInstance;
            VisualElement root = document.rootVisualElement;
            root.style.color = new Color(.90f, .91f, .86f);
            root.style.position = Position.Absolute;
            root.style.left = root.style.right = root.style.top = root.style.bottom = 0;
            VisualElement field = new VisualElement { name = "location-lighting-playfield" };
            field.style.flexGrow = 1;
            root.Add(field);
            field.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || !TryPointAt(root, evt.position, out Vector2 point)) return;
                if (!Mover.MoveLeaderTo(point.x, point.y))
                    notice.text = "Туда не пройти.";
                else notice.text = "Командир идёт к точке; спутники следуют за ним. Зажатая кнопка — идти за курсором.";
                // Зажатая кнопка — командир идёт за курсором, как на глобальной карте.
                heldPoint = point;
                holding = true;
                field.CapturePointer(evt.pointerId);
            });
            field.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!holding) return;
                if ((evt.pressedButtons & 1) == 0) { holding = false; field.ReleasePointer(evt.pointerId); return; }
                if (TryPointAt(root, evt.position, out Vector2 point) && (point - heldPoint).sqrMagnitude > 64)
                {
                    heldPoint = point;
                    if (Mover.IsPassable(point.x, point.y)) Mover.MoveLeaderTo(point.x, point.y);
                }
            });
            field.RegisterCallback<PointerUpEvent>(evt =>
            {
                holding = false;
                if (field.HasPointerCapture(evt.pointerId)) field.ReleasePointer(evt.pointerId);
            });
            VisualElement bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.left = bar.style.right = bar.style.top = 0;
            bar.style.backgroundColor = new Color(.08f, .10f, .09f, .94f);
            bar.style.paddingTop = bar.style.paddingBottom = 8;
            bar.style.paddingLeft = bar.style.paddingRight = 14;
            root.Add(bar);
            Label title = new Label("ЛАГЕРЬ · ПРОВЕРКА СВЕТА");
            title.style.color = new Color(.91f, .81f, .57f);
            title.style.fontSize = 18;
            bar.Add(title);
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            bar.Add(row);
            clock = new Label(); clock.style.width = 60; row.Add(clock);
            timeSlider = new Slider("Время суток", 0, 24) { value = Hour };
            timeSlider.style.width = 350;
            timeSlider.RegisterValueChangedCallback(evt => { Hour = Mathf.Repeat(evt.newValue, 24); Cycle = false; });
            row.Add(timeSlider);
            foreach ((string titleText, float hour) in new[] { ("Рассвет", 6f), ("День", 13f), ("Вечер", 19f), ("Ночь", 1f) })
            {
                Button button = new Button(() => { Hour = hour; Cycle = false; }) { text = titleText };
                row.Add(button);
            }
            row.Add(new Button(() => Cycle = !Cycle) { text = "Сутки / пауза" });
            row.Add(new Button(() =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }) { text = "Вернуться в редактор" });
            notice = new Label("Кликните по земле. Время теста не затрагивает кампанию.");
            notice.style.color = new Color(.75f, .77f, .72f);
            bar.Add(notice);
            heightLabel = new Label { name = "location-lighting-height" };
            heightLabel.style.color = new Color(.70f, .82f, .90f);
            bar.Add(heightLabel);
        }

        private bool holding;
        private Vector2 heldPoint;

        // Точка панели → точка рисунка места через кадр камеры (весь экран).
        private bool TryPointAt(VisualElement root, Vector2 panelPosition, out Vector2 point)
        {
            Vector2 viewport = new Vector2(panelPosition.x / Mathf.Max(1, root.resolvedStyle.width),
                panelPosition.y / Mathf.Max(1, root.resolvedStyle.height));
            point = Renderer.ViewportToPixel(viewport);
            return point.x >= 0 && point.y >= 0 && point.x <= Renderer.CanvasSize.x && point.y <= Renderer.CanvasSize.y;
        }

        private void Update()
        {
            if (Renderer == null || Mover == null) return;
            if (Cycle) Hour = Mathf.Repeat(Hour + Time.unscaledDeltaTime * 24 / Mathf.Max(1, CycleSeconds), 24);
            Mover.Tick(Time.unscaledDeltaTime, out _);
            Renderer.Camera.aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            RenderActors(Time.unscaledTime, Time.unscaledDeltaTime, false);
            Renderer.SetTime(Hour, Time.unscaledTime);
            if (clock != null) clock.text = FormatHour(Hour);
            if (heightLabel != null)
                heightLabel.text = Renderer.Height == null ? string.Empty
                    : LeaderHeight.Valid ? "Высота под ногами: " + LeaderHeight.Meters.ToString("0.000") + " м · участок " +
                                           LocationGroundTile.KeyOf(LeaderHeight.Column, LeaderHeight.Row) + (LeaderHeight.Coverage < .999f ? " · у края" : "")
                    : "Высота: " + LeaderHeight.Reason;
            timeSlider?.SetValueWithoutNotify(Hour);
        }

        public static string FormatHour(float hours)
        {
            int minutes = Mathf.FloorToInt(Mathf.Repeat(hours, 24) * 60);
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        private void OnDestroy()
        {
            Renderer?.Dispose();
            if (panelInstance != null) Destroy(panelInstance);
        }
    }
}
