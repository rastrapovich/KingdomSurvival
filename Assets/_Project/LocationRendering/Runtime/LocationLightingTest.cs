using System;
using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattleSandbox;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering
{
    // Универсальная служебная сцена: те же renderer/анимации/движение,
    // отдельное состояние без CampaignSession, расходов, событий и save/load.
    public sealed class LocationLightingTest : MonoBehaviour
    {
        public PanelSettings Panel;
        public string LocationId = "technical_lighting_camp";
        public LocationWorldRenderer Renderer { get; private set; }
        public LocalPartyMover Mover { get; private set; }
        public float Hour = 13;
        public bool Cycle;
        public float CycleSeconds = 30;
        private Label clock, notice;
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
            List<HexCoord> cells = new List<HexCoord>(Renderer.Geometry.Region(new HexCoord(visual.TestStart.x, visual.TestStart.y)));
            HexCoord start = new HexCoord(visual.TestStart.x, visual.TestStart.y);
            cells.Sort((a, b) => a.DistanceTo(start).CompareTo(b.DistanceTo(start)));
            int count = Mathf.Clamp(visual.TestFollowers + 1, 1, 5);
            if (cells.Count < count) throw new InvalidOperationException("Рядом с точкой старта мало места для тестового отряда.");
            List<KeyValuePair<string, HexCoord>> members = new List<KeyValuePair<string, HexCoord>>();
            for (int i = 0; i < count; i++) members.Add(new KeyValuePair<string, HexCoord>("test_" + i, cells[i]));
            Mover = new LocalPartyMover(Renderer.Geometry.IsPassable, Renderer.Geometry.StepCost, members);
            Renderer.AddTestActors(count);
            Renderer.RenderActors(Mover.Members, 0);
            Renderer.SetTime(Hour, 0);
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
                if (evt.button != 0) return;
                Vector2 screen = new Vector2(evt.position.x / root.resolvedStyle.width * Screen.width,
                    (1 - evt.position.y / root.resolvedStyle.height) * Screen.height);
                if (!Renderer.Camera.pixelRect.Contains(screen)) return;
                Vector2 world = Renderer.Camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10));
                if (!LocationVisualGeometry.TryCell(Renderer.Field, world, out HexCoord cell) || !Mover.MoveLeaderTo(cell))
                    notice.text = "Туда не пройти.";
                else notice.text = "Командир идёт к точке; спутники следуют за ним.";
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
        }

        private void Update()
        {
            if (Renderer == null || Mover == null) return;
            if (Cycle) Hour = Mathf.Repeat(Hour + Time.unscaledDeltaTime * 24 / Mathf.Max(1, CycleSeconds), 24);
            Mover.Tick(Time.unscaledDeltaTime);
            Renderer.RenderActors(Mover.Members, Time.unscaledTime);
            Renderer.SetTime(Hour, Time.unscaledTime);
            float screenAspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            if (screenAspect > BattlefieldFrame.Aspect)
            {
                float w = BattlefieldFrame.Aspect / screenAspect;
                Renderer.Camera.rect = new Rect((1 - w) / 2, 0, w, 1);
            }
            else
            {
                float h = screenAspect / BattlefieldFrame.Aspect;
                Renderer.Camera.rect = new Rect(0, (1 - h) / 2, 1, h);
            }
            if (clock != null) clock.text = FormatHour(Hour);
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
