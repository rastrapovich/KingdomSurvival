using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KingdomSurvival.ArtAssets.Editor
{
    // Карточка ассета: слева — общий предпросмотр выбранного ракурса (рисунок,
    // нормали или под светом рендерера мест), справа — шесть ячеек ракурсов
    // с рисунком и нормалью. Опора ставится кликом, основание — мышью.
    public sealed partial class ArtAssetDatabaseWindow
    {
        private enum CardTool { Pivot, Footprint, FootprintBrush, PartOffset, Light }

        private ArtAssetView cardView = ArtAssetView.Front;
        private int cardPart;
        private CardTool cardTool = CardTool.Pivot;
        private float cardZoom = 1;
        private Vector2 cardPan;
        private bool draggingTool;
        private Vector2 toolStart;
        private Vector2 footprintStartOffset, partStartOffset;
        private bool footprintMove;
        // Мазок кисти основания: закрашивает или стирает (Shift).
        private bool brushErase;

        private PreviewRenderUtility litPreview;
        private LocationWorldRenderer litRenderer;
        private bool litDirty = true;
        private string litKey;
        private LocalLocationDefinition litLocation;

        private const string LitObjectId = "asset", LitLightId = "light";

        private ArtAssetView CardView => cardView;

        private struct CardFrame
        {
            public Rect Preview;
            public Vector2 Anchor;
            public float PixelsPerUnit;
        }

        private void DrawCard(Rect area, Event evt)
        {
            ArtAssetDefinition asset = Selected;
            FillBackground(area);
            if (asset == null)
            {
                if (evt.type == EventType.Repaint)
                    GUI.Label(area, "Выберите ассет на холсте или в галерее.", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = TextColor } });
                return;
            }
            // Один вид: карточка показывает основной рисунок записи, без
            // ракурсов, нормалей и светового предпросмотра.
            if (cardView != asset.PrimaryView) { cardView = asset.PrimaryView; litDirty = true; }
            state.CardDisplay = ArtAssetCardDisplay.Color;
            if (cardTool == CardTool.Light) cardTool = CardTool.Pivot;
            CardLayout(area, out Rect preview, out Rect slots);
            if (DrawSlots(slots, asset, evt)) return;
            DrawBigPreview(preview, asset, evt);
        }

        private CardFrame Frame(Rect preview, ArtAssetDefinition asset)
        {
            Rect bounds = ArtAssetDrawing.Resolve(catalog, asset, cardView).Bounds;
            ArtAssetViewSettings settings = asset.Settings(cardView);
            Rect footprint = settings.UsesFootprintMask ? settings.FootprintMask.Bounds()
                : new Rect(settings.FootprintOffset - settings.FootprintSize / 2, settings.FootprintSize);
            Rect all = Rect.MinMaxRect(Mathf.Min(bounds.xMin, footprint.xMin), Mathf.Min(bounds.yMin, footprint.yMin, -.3f),
                Mathf.Max(bounds.xMax, footprint.xMax), Mathf.Max(bounds.yMax, footprint.yMax));
            float scale = Mathf.Min((preview.width - 60) / Mathf.Max(.1f, all.width), (preview.height - 90) / Mathf.Max(.1f, all.height)) * cardZoom;
            Vector2 anchor = new Vector2(preview.center.x - all.center.x * scale, preview.center.y + all.center.y * scale + 10) + cardPan;
            return new CardFrame { Preview = preview, Anchor = anchor, PixelsPerUnit = Mathf.Max(1, scale) };
        }

        private static Vector2 ToGui(CardFrame frame, Vector2 world) => frame.Anchor + new Vector2(world.x, -world.y) * frame.PixelsPerUnit;
        private static Vector2 ToWorld(CardFrame frame, Vector2 gui) => new Vector2(gui.x - frame.Anchor.x, frame.Anchor.y - gui.y) / frame.PixelsPerUnit;

        private void DrawBigPreview(Rect preview, ArtAssetDefinition asset, Event evt)
        {
            CardFrame frame = Frame(preview, asset);
            if (HandleCardInput(frame, asset, evt)) return;
            if (evt.type != EventType.Repaint) return;
            ArtAssetDrawing.Layout layout = ArtAssetDrawing.Resolve(catalog, asset, cardView);
            if (state.CardDisplay == ArtAssetCardDisplay.Lit) DrawLit(preview, frame);
            GUI.BeginClip(preview);
            Vector2 shift = -preview.position;
            CardFrame local = frame;
            local.Anchor += shift;
            if (state.CardDisplay != ArtAssetCardDisplay.Lit)
            {
                ArtAssetDrawing.DrawAsset(catalog, asset, cardView, local.Anchor, local.PixelsPerUnit,
                    state.CardDisplay == ArtAssetCardDisplay.Normal ? ArtAssetDrawMode.Normal : ArtAssetDrawMode.Color, null,
                    cardTool == CardTool.PartOffset ? cardPart : -1, asset.IsAnimated ? EditorApplication.timeSinceStartup : -1);
            }
            DrawCardOverlays(local, asset, layout, new Rect(0, 0, preview.width, preview.height));
            GUI.EndClip();
        }

        private void DrawCardOverlays(CardFrame frame, ArtAssetDefinition asset, ArtAssetDrawing.Layout layout, Rect clip)
        {
            Handles.BeginGUI();
            Color line = state.Background == ArtAssetBackground.Dark || state.CardDisplay == ArtAssetCardDisplay.Lit ? new Color(1, 1, 1, .35f) : new Color(0, 0, 0, .35f);
            EditorGUI.DrawRect(new Rect(clip.x, frame.Anchor.y, clip.width, 1), line);
            ArtAssetViewSettings settings = asset.Settings(cardView);
            bool brush = cardTool == CardTool.FootprintBrush;
            if (settings.UsesFootprintMask)
            {
                // Основание кистью: закрашенные клетки; прямоугольник не действует.
                // Кистью видно, что закрашено; в остальное время — как прямоугольник:
                // слабая заливка и контур.
                Color fill = new Color(1, .35f, .15f, brush ? .4f : .07f);
                foreach (Rect cell in settings.FootprintMask.Rects())
                {
                    Vector2 min = ToGui(frame, new Vector2(cell.xMin, cell.yMax)), max = ToGui(frame, new Vector2(cell.xMax, cell.yMin));
                    EditorGUI.DrawRect(Rect.MinMaxRect(min.x, min.y, max.x, max.y), fill);
                }
                Handles.color = new Color(1, .45f, .2f, brush ? .95f : .5f);
                foreach (Vector4 edge in settings.FootprintMask.Outline())
                    Handles.DrawLine(ToGui(frame, new Vector2(edge.x, edge.y)), ToGui(frame, new Vector2(edge.z, edge.w)));
            }
            else
            {
                Vector2 fpCenter = ToGui(frame, settings.FootprintOffset);
                Vector2 fpSize = settings.FootprintSize * frame.PixelsPerUnit;
                Handles.DrawSolidRectangleWithOutline(new Rect(fpCenter - fpSize / 2, fpSize), new Color(1, .35f, .15f, cardTool == CardTool.Footprint ? .16f : .07f),
                    new Color(1, .45f, .2f, cardTool == CardTool.Footprint ? .95f : .5f));
            }
            if (brush && clip.Contains(Event.current.mousePosition))
            {
                Handles.color = Event.current.shift ? new Color(1, .45f, .4f, .95f) : new Color(1, .8f, .4f, .95f);
                Handles.DrawWireDisc(Event.current.mousePosition, Vector3.forward, state.FootprintBrush * frame.PixelsPerUnit);
            }
            foreach (LocationResolvedPart part in layout.Resolved.Parts)
            {
                Rect world = ArtAssetDrawing.PartRect(part);
                Vector2 min = ToGui(frame, new Vector2(world.xMin, world.yMax)), max = ToGui(frame, new Vector2(world.xMax, world.yMin));
                bool active = cardPart < asset.Parts.Count && asset.Parts[cardPart] != null && asset.Parts[cardPart].Name == part.Name;
                Handles.DrawSolidRectangleWithOutline(Rect.MinMaxRect(min.x, min.y, max.x, max.y), Color.clear,
                    active ? new Color(.95f, .8f, .35f, .8f) : new Color(.6f, .8f, 1f, .3f));
            }
            Vector2 anchor = frame.Anchor;
            EditorGUI.DrawRect(new Rect(anchor.x - 9, anchor.y - 1, 18, 2), Color.yellow);
            EditorGUI.DrawRect(new Rect(anchor.x - 1, anchor.y - 9, 2, 18), Color.yellow);
            Handles.EndGUI();
            GUIStyle title = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1, .85f, .45f) }, fontSize = 13 };
            GUI.Label(new Rect(clip.x + 10, clip.y + 6, clip.width - 20, 20), asset.Name + " · " + ArtAssetLabels.PurposeTitle(asset.Purpose), title);
            GUIStyle note = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1, .62f, .3f) } };
            if (layout.Resolved.NoArt) GUI.Label(new Rect(clip.x + 10, clip.y + 26, clip.width, 16), "Рисунка нет — перетащите PNG в ячейку справа.", note);
            if (state.CardDisplay == ArtAssetCardDisplay.Lit && cardTool == CardTool.Light && litLocation != null)
            {
                // Контрольный источник.
                Vector2 pixel = LocationVisualGeometry.ToPixel(litLocation, state.LightPosition);
                Vector2 objectPixel = LocationVisualGeometry.ToPixel(litLocation, new Vector2(.5f, .62f));
                Vector2 world = (pixel - objectPixel) / LocationVisualGeometry.PixelsPerUnit;
                Vector2 ground = ToGui(frame, new Vector2(world.x, -world.y));
                Vector2 gui = ToGui(frame, new Vector2(world.x, -world.y + state.LightHeight));
                Handles.BeginGUI();
                Handles.color = new Color(1, .85f, .5f, .95f);
                // Источник — на высоте над точкой земли (крестик); тени — от неё.
                Handles.DrawLine(ground, gui);
                Handles.DrawLine(ground - new Vector2(5, 0), ground + new Vector2(5, 0));
                Handles.DrawWireDisc(gui, Vector3.forward, 10);
                Handles.DrawSolidDisc(gui, Vector3.forward, 4);
                Handles.EndGUI();
            }
            GUIStyle help = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = state.Background == ArtAssetBackground.Dark ? new Color(.7f, .75f, .7f) : new Color(.2f, .2f, .2f) } };
            string tool = cardTool == CardTool.Pivot ? "Опора: клик по точке касания земли — она встанет на опору (жёлтый крест)."
                : cardTool == CardTool.Footprint ? "Основание: протяните прямоугольник занятой земли или перетащите его. Блокирует проход, только если включено «Блокирует проход»."
                : cardTool == CardTool.FootprintBrush ? "Кисть основания: ЛКМ — закрасить занятую землю, Shift+ЛКМ — стереть, [ и ] — радиус. Закрашенное заменяет прямоугольник."
                : cardTool == CardTool.PartOffset ? "Сдвиг части: перетащите выбранную часть («" + (cardPart < asset.Parts.Count ? asset.Parts[cardPart]?.Name : "") + "»)."
                : "Свет: перетащите контрольный источник (режим «Под светом»).";
            GUI.Label(new Rect(clip.x + 10, clip.yMax - 20, clip.width - 20, 16), tool, help);
        }

        private bool HandleCardInput(CardFrame frame, ArtAssetDefinition asset, Event evt)
        {
            Vector2 mouse = evt.mousePosition;
            if (!frame.Preview.Contains(mouse) && !draggingTool && !panning) return false;
            Vector2 world = ToWorld(frame, mouse);
            ArtAssetViewSettings settings = asset.Settings(cardView);
            switch (evt.type)
            {
                case EventType.MouseMove when cardTool == CardTool.FootprintBrush:
                    // Круг кисти идёт за мышью.
                    center.MarkDirtyRepaint();
                    return false;
                case EventType.KeyDown when cardTool == CardTool.FootprintBrush && (evt.keyCode == KeyCode.LeftBracket || evt.keyCode == KeyCode.RightBracket):
                    state.FootprintBrush = Mathf.Clamp(state.FootprintBrush * (evt.keyCode == KeyCode.LeftBracket ? 1 / 1.2f : 1.2f), .01f, 5);
                    ScheduleStateSave();
                    BuildProperties();
                    evt.Use(); center.MarkDirtyRepaint(); return true;
                case EventType.ScrollWheel:
                    cardZoom = Mathf.Clamp(cardZoom * Mathf.Pow(1.1f, -evt.delta.y), .2f, 8);
                    evt.Use(); center.MarkDirtyRepaint(); return true;
                case EventType.MouseDown when evt.button == 1 || evt.button == 2:
                    panning = true; lastMouse = mouse; evt.Use(); return true;
                case EventType.MouseDrag when panning:
                    cardPan += mouse - lastMouse; lastMouse = mouse; evt.Use(); center.MarkDirtyRepaint(); return true;
                case EventType.MouseUp when panning:
                    panning = false; evt.Use(); return true;
                case EventType.MouseDown when evt.button == 0:
                    center.Focus();
                    switch (cardTool)
                    {
                        case CardTool.Pivot:
                            SetPivotAt(asset, world);
                            evt.Use(); return true;
                        case CardTool.Footprint:
                            Undo.RecordObject(catalog, "Основание ракурса");
                            draggingTool = true; toolStart = world;
                            Rect current = new Rect(settings.FootprintOffset - settings.FootprintSize / 2, settings.FootprintSize);
                            footprintMove = current.Contains(world) && settings.FootprintSize.x > .05f;
                            footprintStartOffset = settings.FootprintOffset;
                            evt.Use(); return true;
                        case CardTool.FootprintBrush:
                            Undo.RecordObject(catalog, "Кисть основания");
                            draggingTool = true;
                            brushErase = evt.shift;
                            PaintFootprint(asset, world);
                            evt.Use(); center.MarkDirtyRepaint(); return true;
                        case CardTool.PartOffset:
                            if (cardPart <= 0) { status.text = "Основа задаёт опору; сдвигаются другие части (выберите часть справа)."; evt.Use(); return true; }
                            Undo.RecordObject(catalog, "Сдвиг части");
                            draggingTool = true; toolStart = world;
                            partStartOffset = asset.Parts[cardPart].View(cardView).Offset;
                            evt.Use(); return true;
                        case CardTool.Light:
                            draggingTool = true;
                            MoveLight(world);
                            evt.Use(); return true;
                    }
                    break;
                case EventType.MouseDrag when draggingTool && evt.button == 0:
                    if (cardTool == CardTool.Footprint)
                    {
                        if (footprintMove) settings.FootprintOffset = footprintStartOffset + (world - toolStart);
                        else
                        {
                            Rect rect = Rect.MinMaxRect(Mathf.Min(toolStart.x, world.x), Mathf.Min(toolStart.y, world.y), Mathf.Max(toolStart.x, world.x), Mathf.Max(toolStart.y, world.y));
                            settings.FootprintSize = rect.size;
                            settings.FootprintOffset = rect.center;
                        }
                        catalog.MarkChanged();
                    }
                    else if (cardTool == CardTool.FootprintBrush) PaintFootprint(asset, world);
                    else if (cardTool == CardTool.PartOffset && cardPart > 0)
                    {
                        asset.Parts[cardPart].View(cardView).Offset = partStartOffset + (world - toolStart);
                        catalog.MarkChanged();
                    }
                    else if (cardTool == CardTool.Light) MoveLight(world);
                    evt.Use(); center.MarkDirtyRepaint(); return true;
                case EventType.MouseUp when draggingTool && evt.button == 0:
                    draggingTool = false;
                    if (cardTool != CardTool.Light)
                    {
                        EditorUtility.SetDirty(catalog);
                        catalog.MarkChanged();
                        saveAt = EditorApplication.timeSinceStartup + .3;
                        BuildProperties();
                    }
                    else ScheduleStateSave();
                    evt.Use(); return true;
            }
            return false;
        }

        // Мазок кисти основания в точке world (единицы мира от опоры). Сетка
        // маски заводится при первом мазке — по рисунку ракурса, клетка ≈ 1/96
        // его большей стороны — и растёт, если кисть выходит за край.
        private void PaintFootprint(ArtAssetDefinition asset, Vector2 world)
        {
            ArtAssetViewSettings settings = asset.Settings(cardView);
            if (settings.FootprintMask == null) settings.FootprintMask = new ArtAssetFootprintMask();
            ArtAssetFootprintMask mask = settings.FootprintMask;
            if (!mask.HasGrid)
            {
                if (brushErase) return;
                PrepareMaskGrid(asset, mask);
            }
            if (mask.Paint(world, Mathf.Max(.005f, state.FootprintBrush), !brushErase)) catalog.MarkChanged();
        }

        // Пустая сетка маски по рисунку ракурса (с запасом) и прямоугольнику основания.
        private void PrepareMaskGrid(ArtAssetDefinition asset, ArtAssetFootprintMask mask)
        {
            ArtAssetViewSettings settings = asset.Settings(cardView);
            Rect picture = ArtAssetDrawing.Resolve(catalog, asset, cardView).Bounds;
            Rect footprint = new Rect(settings.FootprintOffset - settings.FootprintSize / 2, settings.FootprintSize);
            Rect area = Rect.MinMaxRect(Mathf.Min(picture.xMin, footprint.xMin), Mathf.Min(picture.yMin, footprint.yMin),
                Mathf.Max(picture.xMax, footprint.xMax), Mathf.Max(picture.yMax, footprint.yMax));
            float cell = Mathf.Clamp(Mathf.Max(picture.width, picture.height) / 96, .005f, .25f);
            area = Rect.MinMaxRect(area.xMin - cell * 4, area.yMin - cell * 4, area.xMax + cell * 4, area.yMax + cell * 4);
            mask.Reset(area, cell);
        }

        // Кликнутая точка рисунка основы становится опорой ракурса.
        private void SetPivotAt(ArtAssetDefinition asset, Vector2 world)
        {
            ArtAssetDrawing.Layout layout = ArtAssetDrawing.Resolve(catalog, asset, cardView);
            LocationResolvedPart main = layout.Resolved.Main;
            if (main == null || main.Sprite == null || layout.Resolved.ViewFallback)
            {
                status.text = "Опора ставится на рисунок основы этого ракурса — сначала загрузите его.";
                return;
            }
            Rect rect = ArtAssetDrawing.PartRect(main);
            Vector2 pivot = new Vector2((world.x - rect.xMin) / rect.width, (world.y - rect.yMin) / rect.height);
            pivot = new Vector2(Mathf.Clamp(pivot.x, -.5f, 1.5f), Mathf.Clamp(pivot.y, -.5f, 1.5f));
            Edit("Опора ракурса", () => asset.Settings(cardView).Pivot = pivot, true);
            status.text = "Опора «" + ArtAssetLabels.ViewTitle(cardView) + "»: " + pivot.x.ToString("0.###") + "; " + pivot.y.ToString("0.###") + ".";
        }

        // ------------------------------------------------------------------
        // Ячейки ракурсов
        // ------------------------------------------------------------------

        // Раскладка карточки: слева предпросмотр, справа — ячейка рисунка.
        private static void CardLayout(Rect area, out Rect preview, out Rect slots)
        {
            float slotsWidth = Mathf.Clamp(area.width * .3f, 220, 380);
            preview = new Rect(area.x, area.y, area.width - slotsWidth - 6, area.height);
            slots = new Rect(preview.xMax + 6, area.y, slotsWidth, area.height);
        }

        // Ячейка рисунка (один вид записи).
        private static Rect PictureCell(Rect slots)
        {
            const float header = 20;
            float height = Mathf.Min(slots.height - header - 12, slots.width * 1.4f);
            return new Rect(slots.x, slots.y + header, slots.width, height);
        }

        private static Rect PictureArea(Rect cell) => new Rect(cell.x + 4, cell.y + 22, cell.width - 8, cell.height - 62);

        private bool DrawSlots(Rect slots, ArtAssetDefinition asset, Event evt)
        {
            cardPart = Mathf.Clamp(cardPart, 0, asset.Parts.Count - 1);
            ArtAssetPart part = asset.Parts[cardPart];
            ArtAssetView view = cardView;
            Rect cell = PictureCell(slots);
            Rect colorRect = PictureArea(cell);
            ArtAssetPartView slot = part.View(view);
            Rect clearColor = new Rect(colorRect.x, cell.yMax - 20, Mathf.Min(140, colorRect.width), 16);
            if (evt.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(cell, new Color(.95f, .75f, .3f, .14f));
                ArtAssetDrawing.Checker(colorRect, 8);
                if (slot.Sprite != null)
                {
                    Rect fit = Fit(colorRect, slot.Sprite.rect.size);
                    ArtAssetDrawing.DrawSprite(fit, slot.Sprite);
                }
                else GUI.Label(colorRect, "рисунок\n(PNG сюда)", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true });
                GUIStyle head = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(1, .85f, .45f) } };
                int frames = slot.FrameCount;
                GUI.Label(new Rect(cell.x + 4, cell.y + 2, cell.width - 8, 18), "Рисунок" + (slot.Sprite != null ? "  ✓" : "  —") +
                                                                            (frames > 1 ? " · кадров: " + frames : ""), head);
                string info = slot.Sprite != null ? SizeText(SpriteNormalMaps.SourceSize(slot.Sprite.texture)) : "";
                GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(.75f, .78f, .74f) }, clipping = TextClipping.Clip };
                GUI.Label(new Rect(cell.x + 4, cell.yMax - 38, cell.width - 8, 16), info, infoStyle);
                GUIStyle note = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = new Color(1, .85f, .45f) } };
                GUI.Label(new Rect(slots.x + 2, slots.y + 2, slots.width - 4, 16), "Часть «" + part.Name + "»" +
                                                                                    (asset.Parts.Count > 1 ? " (другая — справа, «Части объекта»)" : ""), note);
            }
            if (slot.Sprite != null && GUI.Button(clearColor, "✕ рисунок", EditorStyles.miniButton))
            {
                ArtAssetImporter.ClearColor(catalog, part, view);
                status.text = "Рисунок снят (файл не удалён).";
                BuildProperties(); return true;
            }
            return false;
        }

        private static string SizeText(Vector2Int size) => size.x + "×" + size.y;

        private static Rect Fit(Rect area, Vector2 size)
        {
            float scale = Mathf.Min(area.width / Mathf.Max(1, size.x), area.height / Mathf.Max(1, size.y));
            Vector2 fitted = size * scale;
            return new Rect(area.center - fitted / 2, fitted);
        }

        // ------------------------------------------------------------------
        // Под светом: тот же рендерер мест, контрольный источник
        // ------------------------------------------------------------------

        private void ReleaseLit()
        {
            litRenderer?.Dispose();
            litRenderer = null;
            litPreview?.Cleanup();
            litPreview = null;
            litKey = null;
        }

        private void EnsureLit(ArtAssetDefinition asset)
        {
            string key = asset.Id + "|" + cardView + "|" + catalog.Revision + "|" + state.LightIntensity + "|" + state.LightHeight + "|" + state.LightNormalDistance + "|" + state.LightNormals + "|" + state.LightNight + "|" + state.LightEnabled;
            if (!litDirty && litRenderer != null && key == litKey) return;
            ReleaseLit();
            litDirty = false;
            litKey = key;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            litLocation = new LocalLocationDefinition { Id = "zz_asset_preview", DisplayName = "Предпросмотр ассета", CanvasWidth = 1920, CanvasHeight = 1080 };
            LocationVisualDefinition visual = new LocationVisualDefinition { LocationId = litLocation.Id, UseWorldLighting = false, TechnicalTest = true };
            visual.Objects.Add(new LocationVisualObject { Id = LitObjectId, Name = asset.Name, AssetId = asset.Id, View = cardView, Position = new Vector2(.5f, .62f) });
            LocationVisualObject light = new LocationVisualObject { Id = LitLightId, Name = "Контрольный свет", LightOnly = true, Position = state.LightPosition };
            // Выключенный контрольный свет — источника нет вовсе (виден только общий свет).
            light.Light.Enabled = state.LightEnabled;
            light.Light.Color = new Color(1, .86f, .7f);
            Rect opaque = ArtAssetDrawing.OpaqueBounds(ArtAssetDrawing.Resolve(catalog, asset, cardView));
            // Радиус — по видимой части объекта: свет достаёт до всего рисунка.
            light.Light.Radius = Mathf.Max(4, opaque.size.magnitude * 1.6f);
            light.Light.Intensity = state.LightIntensity;
            // Высота над землёй: свечение выше точки на земле, тени — от этой высоты.
            light.Light.Height = Mathf.Max(.1f, state.LightHeight);
            light.Light.NormalMaps = state.LightNormals;
            light.Light.NormalMapsAccurate = true;
            light.Light.NormalMapDistance = Mathf.Max(.1f, state.LightNormalDistance);
            light.Light.Animation = LocationLightAnimation.None;
            light.Light.Falloff = .3f;
            light.Light.Offset = new Vector2(0, state.LightHeight);
            visual.Objects.Add(light);
            litRenderer = new LocationWorldRenderer(litLocation, visual, null);
            litRenderer.Camera.enabled = false;
            litPreview = new PreviewRenderUtility(true);
            litPreview.AddSingleGO(litRenderer.Root);
            litPreview.camera.orthographic = true;
            litPreview.camera.allowHDR = false;
            UniversalAdditionalCameraData data = litPreview.camera.GetUniversalAdditionalCameraData();
            data.SetRenderer(0);
            data.renderPostProcessing = false;
            litPreview.camera.clearFlags = CameraClearFlags.SolidColor;
            litPreview.camera.backgroundColor = new Color(.035f, .045f, .04f);
        }

        private void MoveLight(Vector2 world)
        {
            if (litLocation == null) return;
            Vector2 objectPixel = LocationVisualGeometry.ToPixel(litLocation, new Vector2(.5f, .62f));
            Vector2 pixel = objectPixel + new Vector2(world.x, -world.y) * LocationVisualGeometry.PixelsPerUnit;
            state.LightPosition = LocationVisualGeometry.ToNormalized(litLocation, pixel);
            litRenderer?.MoveObject(LitLightId, state.LightPosition);
        }

        private void DrawLit(Rect area, CardFrame frame)
        {
            ArtAssetDefinition asset = Selected;
            if (asset == null) return;
            EnsureLit(asset);
            if (litRenderer == null || litPreview == null)
            {
                GUI.Label(area, "Предпросмотр под светом недоступен в Play Mode.");
                return;
            }
            if (state.LightOrbit && state.LightEnabled)
            {
                // Источник обходит объект по земле (эллипс вокруг видимой части) на
                // заданной высоте: нормали видны по движению света.
                Rect opaque = ArtAssetDrawing.OpaqueBounds(ArtAssetDrawing.Resolve(catalog, asset, cardView));
                float angle = (float)EditorApplication.timeSinceStartup * .9f;
                float rx = Mathf.Max(.6f, opaque.width * .7f), ry = Mathf.Max(.3f, rx * .45f);
                MoveLight(new Vector2(opaque.center.x + Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry));
            }
            litRenderer.SetTime(state.LightNight ? 1 : 13, (float)EditorApplication.timeSinceStartup);
            // Кадр камеры совпадает с предпросмотром: опора ассета — в frame.Anchor.
            Vector2 objectPixel = LocationVisualGeometry.ToPixel(litLocation, new Vector2(.5f, .62f));
            float heightPixels = area.height / frame.PixelsPerUnit * LocationVisualGeometry.PixelsPerUnit;
            Vector2 centerPixel = objectPixel + (area.center - frame.Anchor) / frame.PixelsPerUnit * LocationVisualGeometry.PixelsPerUnit;
            Vector2 world = LocationVisualGeometry.PixelToWorld(litLocation, centerPixel);
            litPreview.camera.transform.position = new Vector3(world.x, world.y, -10);
            litPreview.camera.transform.rotation = Quaternion.identity;
            litPreview.camera.orthographicSize = heightPixels / LocationVisualGeometry.PixelsPerUnit / 2;
            Rect target = new Rect(area.x, area.y, area.width, area.height);
            litPreview.BeginPreview(target, GUIStyle.none);
            litPreview.Render(true);
            Texture texture = litPreview.EndPreview();
            GUI.DrawTexture(target, texture, ScaleMode.StretchToFill, false);
        }
    }
}
