using System.Collections.Generic;
using KingdomSurvival.BattlefieldDatabase;
using UnityEngine;

namespace KingdomSurvival.LocationRendering.Editor
{
    // ПР-12О: перенос раскладки места при смене кадра земли (обрезка,
    // переимпорт с другой областью экспорта). Всё остаётся на своём месте
    // земли: новая точка = старая − offset (пиксели рисунка, Y вниз), холст —
    // новый размер. Это сдвиг, а не растяжение: масштаб земли не меняется.
    public static class LocationRebase
    {
        public sealed class Result
        {
            public int Outside;
            // Экземпляры раскидки за новым краем — удаляются вместе с краем.
            public int ScatterRemoved;
            public readonly List<string> Names = new List<string>();
        }

        public static Result Translate(LocalLocationDefinition location, LocationVisualDefinition visual, Vector2 offset, Vector2 canvas)
        {
            Result result = new Result();
            Vector2 old = LocationVisualGeometry.CanvasSize(location);
            canvas = Vector2.Max(canvas, Vector2.one * 16);

            // Разметка местности: та же плотность клеток, тип — из-под центра.
            WorldMapTerrainLayer previous = location.CreateTerrainLayer();
            int hexes = WorldMapHexGrid.SanitizeHexesAcross(Mathf.Max(1, Mathf.RoundToInt(location.HexesAcross * canvas.x / Mathf.Max(1, old.x))));
            WorldMapHexGrid grid = new WorldMapHexGrid(canvas.x, canvas.y, hexes);
            string cells = string.Empty;
            if (!previous.IsEmpty)
            {
                WorldMapTerrainLayer layer = new WorldMapTerrainLayer(grid);
                for (int index = 0; index < grid.CellCount; index++)
                {
                    WorldMapHexCell cell = grid.CellAt(index);
                    grid.CellCenter(cell, out double x, out double y);
                    double ox = x + offset.x, oy = y + offset.y;
                    if (ox >= 0 && oy >= 0 && ox <= old.x && oy <= old.y) layer.Set(cell, previous.GetAtPixel(ox, oy));
                }
                cells = layer.Encode();
            }
            location.CanvasWidth = canvas.x;
            location.CanvasHeight = canvas.y;
            location.HexesAcross = hexes;
            location.TerrainCells = cells;

            void Move(LocalPointData point, string name)
            {
                if (point == null) return;
                point.X -= offset.x;
                point.Y -= offset.y;
                if (point.X < 0 || point.Y < 0 || point.X > canvas.x || point.Y > canvas.y) { result.Outside++; result.Names.Add(name); }
            }
            location.Entrances.ForEach(item => Move(item.Point, "вход «" + item.Label + "»"));
            location.Objects.ForEach(item => Move(item.Point, "объект «" + item.Label + "»"));
            location.Enemies.ForEach(item => Move(item.Point, "противник " + item.InstanceId));
            foreach (LocalEncounterDefinition encounter in location.Encounters)
            {
                Move(encounter.RetreatPoint, "точка отхода " + encounter.Id);
                if (encounter.HasArenaCenter) Move(encounter.ArenaCenter, "кадр боя " + encounter.Id);
                encounter.TriggerArea.X -= offset.x;
                encounter.TriggerArea.Y -= offset.y;
            }

            if (visual != null)
            {
                Vector2 Normalized(Vector2 value, string name)
                {
                    Vector2 pixel = Vector2.Scale(value, old) - offset;
                    if (pixel.x < 0 || pixel.y < 0 || pixel.x > canvas.x || pixel.y > canvas.y) { result.Outside++; result.Names.Add(name); }
                    return new Vector2(pixel.x / canvas.x, pixel.y / canvas.y);
                }
                foreach (LocationVisualObject item in visual.Objects)
                    if (item != null) item.Position = Normalized(item.Position, "предмет «" + item.Name + "»");
                visual.TestStartPoint = Normalized(visual.TestStartPoint, "старт теста");
                if (visual.ScatterLayers != null)
                {
                    foreach (LocationScatterLayer layer in visual.ScatterLayers)
                    {
                        if (layer?.Instances == null) continue;
                        result.ScatterRemoved += layer.Instances.RemoveAll(instance =>
                        {
                            if (instance == null) return true;
                            Vector2 pixel = Vector2.Scale(instance.Position, old) - offset;
                            if (pixel.x < 0 || pixel.y < 0 || pixel.x > canvas.x || pixel.y > canvas.y) return true;
                            instance.Position = new Vector2(pixel.x / canvas.x, pixel.y / canvas.y);
                            return false;
                        });
                    }
                }
                if (visual.Camera != null)
                {
                    Rect bounds = visual.Camera.CustomBounds;
                    Vector2 min = Vector2.Scale(bounds.min, old) - offset, max = Vector2.Scale(bounds.max, old) - offset;
                    visual.Camera.CustomBounds = Rect.MinMaxRect(min.x / canvas.x, min.y / canvas.y, max.x / canvas.x, max.y / canvas.y);
                }
            }
            return result;
        }
    }
}
