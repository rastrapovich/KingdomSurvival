using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using KingdomSurvival.LocationRendering;
using UnityEngine;
using UnityEngine.UIElements;

namespace KingdomSurvival.BattleSandbox
{
    // Поле боя, собранное в Базе локаций (место вида «Поле боя» с тем же
    // BattlefieldId): вместо картинки поля под доской — живой кадр места:
    // земля, предметы, раскидка, свет и время суток, огонь, анимации, трава
    // гнётся от фигур. Камера места снимает ровно кадр поля на холсте
    // (BattlefieldFrame.FrameOnPicture), фигуры доски рисует рендерер места
    // под своим светом и с тенями — как в бою на месте. Поле без такого
    // места — прежняя картинка.
    internal sealed class BattlefieldSceneDriver
    {
        private readonly LocationWorldRenderer renderer;
        private readonly BattlefieldView surface;
        private readonly HexBoardElement board;
        private readonly Rect frameOnCanvas;
        private readonly List<LocationWorldRenderer.BattleFigureFrame> frames = new List<LocationWorldRenderer.BattleFigureFrame>();
        private Texture shown;
        private bool disposed;

        public LocationWorldRenderer Renderer => renderer;

        // Место вида «Поле боя» для поля; нет — null.
        public static LocalLocationDefinition FindScene(BattlefieldDefinitionData field, out LocationVisualDefinition visual)
        {
            visual = null;
            if (field == null || string.IsNullOrEmpty(field.Id)) return null;
            LocalLocationDatabaseAsset database = Resources.Load<LocalLocationDatabaseAsset>(LocalLocationDatabaseAsset.ResourcesPath);
            LocalLocationDefinition location = database?.locations?.Find(item => item != null && item.IsBattlefield && item.BattlefieldId == field.Id);
            if (location == null) return null;
            visual = database.FindVisual(location.Id);
            return visual != null ? location : null;
        }

        public static BattlefieldSceneDriver TryCreate(BattlefieldDefinitionData field, BattlefieldView surface, HexBoardElement board)
        {
            LocalLocationDefinition location = FindScene(field, out LocationVisualDefinition visual);
            if (location == null || surface == null || board == null) return null;
            return new BattlefieldSceneDriver(location, visual, field, surface, board);
        }

        private BattlefieldSceneDriver(LocalLocationDefinition location, LocationVisualDefinition visual, BattlefieldDefinitionData field,
            BattlefieldView surface, HexBoardElement board)
        {
            this.surface = surface;
            this.board = board;
            renderer = new LocationWorldRenderer(location, visual, field) { VegetationReacts = true };
            // Мир поля — вдали от камеры сцены боя: её кадр его не видит.
            renderer.Root.transform.position = new Vector3(20000f, 20000f, 0f);
            renderer.SuppressOtherGlobalLights();
            frameOnCanvas = BattlefieldFrame.FrameOnPicture(renderer.CanvasSize, field);
            board.ExternalFigures = true;
            surface.schedule.Execute(Tick).Every(0);
            surface.RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
        }

        private void Tick()
        {
            if (disposed) return;
            Rect frame = surface.FrameRect;
            if (frame.width < 2f || frame.height < 2f) return;
            float scale = surface.panel != null ? surface.panel.scaledPixelsPerPoint : 1f;
            RenderTexture target = renderer.EnsureTarget(Mathf.RoundToInt(frame.width * scale), Mathf.RoundToInt(frame.height * scale));
            if (shown != target)
            {
                shown = target;
                surface.SetSceneTexture(target);
            }
            renderer.SetView(frameOnCanvas.center, frameOnCanvas.height);
            renderer.SetTime(Hour(), Time.unscaledTime);
            frames.Clear();
            foreach (BoardFigure figure in board.Figures)
            {
                Vector2 min = ToCanvas(figure.Rect.min, frame), max = ToCanvas(figure.Rect.max, frame);
                frames.Add(new LocationWorldRenderer.BattleFigureFrame
                {
                    Id = figure.UnitId, Sprite = figure.Sprite, Rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y),
                    Ground = ToCanvas(figure.Ground, frame), Mirrored = figure.Mirrored, FitInside = figure.FitInside,
                    Tint = figure.Tint, Corpse = figure.Corpse
                });
            }
            renderer.SetBattleFigures(frames);
        }

        // Точка доски → пиксели холста места: кадр поля на экране ↔ кадр поля на холсте.
        private Vector2 ToCanvas(Vector2 boardPoint, Rect frame)
        {
            Vector2 local = surface.WorldToLocal(board.LocalToWorld(boardPoint));
            Vector2 share = new Vector2((local.x - frame.x) / frame.width, (local.y - frame.y) / frame.height);
            return frameOnCanvas.position + Vector2.Scale(share, frameOnCanvas.size);
        }

        // Время суток кампании; без кампании (полигон) — день.
        private static float Hour() =>
            CampaignSession.HasActive ? (float)ContinuousSimulationSystem.GetClock(CampaignSession.Current).HourOfDay : 13f;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            surface.SetSceneTexture(null);
            renderer.Dispose();
        }
    }
}
