using System.Collections.Generic;
using System.Linq;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.BattlefieldDatabase.Editor;
using KingdomSurvival.BattleSandbox;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.LocationRendering.Editor
{
    // Бой на месте в окне: гексы кадра поля рисует тот же BattlefieldView,
    // что и бой на месте (подложка, картинки гекса и рамки, линия — по виду
    // гекса поля); поверх — слой правки, как в Базе полей боя: трудные клетки
    // цветом состояния, стены разметки — как отключённые гексы. Настройки
    // сетки и вида гекса — те же карточки, что в Базе полей боя.
    public sealed partial class LocationDatabaseWindow
    {
        [SerializeField] private bool showHexSamples;

        private VisualElement arenaClip;
        private BattlefieldView arenaView;
        private VisualElement arenaOverlay;
        private BattlefieldDefinitionData arenaField;
        private readonly HashSet<HexCoord> arenaBlocked = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> arenaDifficult = new HashSet<HexCoord>();
        private SerializedObject fieldsSerialized;

        // Слой над холстом: обрезан по рисунку, мышь не перехватывает.
        private VisualElement BuildArenaLayer()
        {
            arenaClip = new VisualElement { name = "location-arena", pickingMode = PickingMode.Ignore };
            arenaClip.style.position = Position.Absolute;
            arenaClip.style.overflow = Overflow.Hidden;
            arenaClip.style.display = DisplayStyle.None;

            arenaView = new BattlefieldView(false);
            arenaView.style.position = Position.Absolute;
            arenaView.WorldUnderlay = true;
            arenaClip.Add(arenaView);

            arenaOverlay = new VisualElement { name = "location-arena-states", pickingMode = PickingMode.Ignore };
            arenaOverlay.style.position = Position.Absolute;
            arenaOverlay.style.left = arenaOverlay.style.top = arenaOverlay.style.right = arenaOverlay.style.bottom = 0;
            arenaOverlay.generateVisualContent += DrawArenaStates;
            arenaView.Add(arenaOverlay);
            arenaView.LayoutChanged += arenaOverlay.MarkDirtyRepaint;
            // Правки Базы полей боя видны на месте сразу, как в идущем бою.
            arenaView.EnableLiveRefresh(500);
            arenaField = null;
            arenaBlocked.Clear();
            arenaDifficult.Clear();
            return arenaClip;
        }

        private Vector2 ArenaCenterOf(LocalEncounterDefinition encounter)
        {
            LocalAreaData area = encounter.TriggerArea;
            List<LocalPointData> anchor = Location.Enemies.Where(enemy => enemy.EncounterId == encounter.Id).Select(enemy => enemy.Point).ToList();
            anchor.Add(new LocalPointData(area.X + area.Width / 2, area.Y + area.Height / 2));
            return geometry.ArenaCenterFor(encounter, anchor);
        }

        // Кадр боя выбранного столкновения — туда же, где он на рисунке.
        private void SyncArenaView()
        {
            if (arenaClip == null || canvas == null) return;
            BattlefieldDefinitionData field = Field;
            LocalEncounterDefinition encounter = SelectedEncounter;
            Rect area = new Rect(0, 0, canvas.contentRect.width, canvas.contentRect.height);
            bool show = showArena && !compareDay && renderer != null && geometry != null && field != null && encounter != null &&
                        !float.IsNaN(area.width) && area.width >= 10 && area.height >= 10;
            arenaClip.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            Rect frame = CanvasFrame(area);
            Vector2 arenaCenter = ArenaCenterOf(encounter);
            Rect rect = geometry.FrameRect(arenaCenter);
            Vector2 a = ToGui(frame, rect.min), b = ToGui(frame, rect.max);
            arenaClip.style.left = frame.x;
            arenaClip.style.top = frame.y;
            arenaClip.style.width = frame.width;
            arenaClip.style.height = frame.height;
            arenaView.style.left = a.x - frame.x;
            arenaView.style.top = a.y - frame.y;
            arenaView.style.width = b.x - a.x;
            arenaView.style.height = b.y - a.y;

            BattlefieldHexStyle style = fields.GetHexStyle(field);
            bool changed = field != arenaField || style != arenaView.HexStyle;
            if (changed)
            {
                arenaField = field;
                arenaView.Show(field, style);
            }
            HashSet<HexCoord> blocked = geometry.ArenaBlockedCells(arenaCenter);
            if (changed || !blocked.SetEquals(arenaBlocked))
            {
                arenaBlocked.Clear();
                arenaBlocked.UnionWith(blocked);
                arenaView.SetWorldUnderlay(blocked);
            }
            HashSet<HexCoord> difficult = geometry.ArenaDifficultCells(arenaCenter);
            if (!difficult.SetEquals(arenaDifficult))
            {
                arenaDifficult.Clear();
                arenaDifficult.UnionWith(difficult);
                arenaOverlay.MarkDirtyRepaint();
            }
        }

        private void DrawArenaStates(MeshGenerationContext context)
        {
            BattlefieldGridLayout layout = arenaView.Layout;
            if (layout.Size <= 0.01f) return;
            BattlefieldHexStyle style = arenaView.HexStyle;
            Painter2D painter = context.painter2D;
            foreach (HexCoord cell in arenaDifficult)
            {
                if (!arenaBlocked.Contains(cell))
                    BattlefieldHexPainter.FillState(painter, layout, style, cell.Q, cell.R, style.DifficultColor);
            }
            if (showHexSamples)
                BattlefieldHexPainter.DrawStateSamples(painter, layout, style, (q, r) => arenaBlocked.Contains(new HexCoord(q, r)));
            foreach (HexCoord cell in arenaBlocked)
                BattlefieldHexPainter.DrawDisabled(painter, layout, style, cell.Q, cell.R);
        }

        // Карточки «Сетка» и «Вид гекса» поля этого места — те же, что в
        // Базе полей боя, и правят ту же базу.
        private void BuildFieldHexCards()
        {
            BattlefieldDefinitionData field = Field;
            int index = field != null ? fields.Battlefields.ToList().IndexOf(field) : -1;
            if (index < 0) return;
            fieldsSerialized = new SerializedObject(fields);
            SerializedProperty property = BattlefieldHexEditorKit.FieldProperty(fieldsSerialized, index);
            VisualElement cards = new VisualElement();
            cards.style.marginTop = 6;
            cards.style.marginLeft = -4;
            cards.Add(BattlefieldHexEditorKit.GridCard(fieldsSerialized, property,
                "Размер и сдвиг сетки в кадре поля 16:9 — как в Базе полей боя.", FieldChanged));
            cards.Add(BattlefieldHexEditorKit.HexCard(fields, fieldsSerialized, index, this, showHexSamples,
                value =>
                {
                    showHexSamples = value;
                    arenaOverlay?.MarkDirtyRepaint();
                },
                BuildSettings, FieldChanged));
            cards.Bind(fieldsSerialized);
            cards.TrackSerializedObjectValue(fieldsSerialized, _ => FieldChanged());
            // Сетка меняет кадр боя на рисунке и размер фигур.
            cards.TrackPropertyValue(property.FindPropertyRelative("gridScale"), _ => rebuildRequested = true);
            cards.TrackPropertyValue(property.FindPropertyRelative("gridOffset"), _ => rebuildRequested = true);
            settings.Add(cards);
        }

        private void FieldChanged()
        {
            if (fields == null) return;
            EditorUtility.SetDirty(fields);
            fieldsSaveAt = EditorApplication.timeSinceStartup + .5;
            arenaView?.Refresh();
            arenaOverlay?.MarkDirtyRepaint();
            SyncArenaView();
        }
    }
}
