using System;
using System.Collections.Generic;
using UnityEngine;

namespace KingdomSurvival.BattlefieldDatabase
{
    // ПР-12О: земля места из Blender (KS Ground Renderer 1.0.0, manifest
    // schema 1). Одна локация — один ID, один корень и общий набор объектов;
    // участок — дочерний фрагмент её земли с индексами X/Y и своими Color,
    // Normal, Height. Прежний «Рисунок места» (Background) — режим «Один
    // участок» через совместимый адаптер (LocationGroundLayout), без переноса.
    public enum LocationGroundMode { Single, Tiles }

    // Чем ограничена камера: весь рисунок, полезная область экспорта
    // (requested_bounds, без добивки до целых участков) или своя рамка.
    public enum LocationCameraBounds { Canvas, UsefulArea, Custom }

    [Serializable]
    public sealed class LocationGroundTile
    {
        public int X;
        public int Y;
        public Sprite Color;
        // Карта нормалей участка; подключена к Color второй текстурой _NormalMap.
        public Texture2D Normal;
        // Высота: двоичные данные LocationHeightTileData (*.bytes), не текстура.
        public TextAsset Height;
        // Происхождение проходов (manifest): revision_id и sha256 исходного PNG.
        public string ColorRevision = string.Empty;
        public string NormalRevision = string.Empty;
        public string HeightRevision = string.Empty;
        public string ColorSha256 = string.Empty;
        public string NormalSha256 = string.Empty;
        public string HeightSha256 = string.Empty;

        public string Key => KeyOf(X, Y);
        public static string KeyOf(int x, int y) => "X" + x.ToString("000") + "_Y" + y.ToString("000");
    }

    [Serializable]
    public sealed class LocationGroundDefinition
    {
        // 1 — первая версия данных земли из участков.
        public const int CurrentVersion = 1;
        public int Version;
        public LocationGroundMode Mode = LocationGroundMode.Single;

        // Идентичность экспортного набора (manifest).
        public string MapId = string.Empty;
        public string RevisionId = string.Empty;
        public string ExporterVersion = string.Empty;
        public string ConfigurationHash = string.Empty;
        public string ExportStatus = string.Empty;
        public string SurfaceContract = string.Empty;
        public string ImportedUtc = string.Empty;
        // Копия manifest (диагностика; игра от неё не зависит).
        public TextAsset Manifest;
        public List<string> Warnings = new List<string>();

        // Сетка: nx × ny участков по W × H пикселей; индексы — снизу слева,
        // X вправо, Y вверх (tile_indices). Шаг — W/H без overscan.
        public int Columns = 1;
        public int Rows = 1;
        public int TileWidth;
        public int TileHeight;
        // PPU, заложенный при экспорте (config.units.unity_ppu); в игре
        // действует единый PPU мест (LocationVisualGeometry.PixelsPerUnit):
        // пиксель рисунка = пиксель рисунка места.
        public float ExportPpu;

        // Плоскость проекции Blender (диагностика и восстановление точки).
        public Vector2 Origin;
        public float ProjectionQ;
        public Vector3 Reference;
        public Vector3 BasisRight = Vector3.right;
        public Vector3 BasisUp = Vector3.up;
        public Vector3 BasisBack = Vector3.forward;
        // Полезная область (requested_bounds) в пикселях виртуальной карты
        // снизу слева: x, y, ширина, высота.
        public Rect UsefulPixels;

        // Высота: абсолютная мировая Z Blender, общий диапазон всех участков.
        public bool HeightEnabled;
        public double HeightMin;
        public double HeightMax = 1;
        public int HeightBitDepth = 16;
        public double MetersPerBlenderUnit = 1;
        // Высота задана не на всей карте: участки без неё дают «нет данных»
        // (явное ограничение автора, иначе — ошибка проверки).
        public bool HeightPartialAllowed;
        // Разрыв поверхности (метры): соседние пиксели расходятся сильнее —
        // интерполяция не смешивает их, берётся ближайший.
        public float SeamlessMeters = .5f;

        // Нормали: зелёный канал экспорта инвертирован (DirectX); импорт
        // переворачивает его ровно один раз.
        public bool NormalGreenInverted;
        public float NormalLevelDegrees;

        // Неполный пакет: доступен как частичный предпросмотр в редакторе,
        // в игру не пропускается.
        public bool Incomplete;

        public List<LocationGroundTile> Tiles = new List<LocationGroundTile>();

        public bool IsTiled => Mode == LocationGroundMode.Tiles;
        public Vector2Int VirtualSize => new Vector2Int(Mathf.Max(1, Columns) * Mathf.Max(1, TileWidth), Mathf.Max(1, Rows) * Mathf.Max(1, TileHeight));
        public LocationGroundTile Find(int x, int y) => Tiles.Find(tile => tile != null && tile.X == x && tile.Y == y);
        public bool HasAnyHeight => Tiles.Exists(tile => tile?.Height != null);
    }

    [Serializable]
    public sealed class LocationCameraSettings
    {
        // 0 — данные прежнего формата: камера как раньше (жёстко за командиром).
        public int Version;
        public bool FollowCommander = true;
        // Смещение центра кадра от командира (пиксели рисунка, Y вниз).
        public Vector2 Offset;
        // Время сглаживания (секунды); 0 — кадр сразу на командире (прежнее).
        public float SmoothTime;
        // Мёртвая зона: доли половины кадра, внутри которых камера стоит.
        public Vector2 DeadZone;
        // Видимая высота рисунка (пиксели); 0 — как прежде: min(высота места, 1080).
        public float ViewHeight;
        public LocationCameraBounds Bounds = LocationCameraBounds.Canvas;
        // Своя рамка (доли рисунка, Y вниз).
        public Rect CustomBounds = new Rect(0, 0, 1, 1);
        // Скачок дальше этой доли высоты кадра — телепорт: кадр сразу на месте.
        public float SnapDistance = 1;

        // Новые большие карты: плавное следование.
        public static LocationCameraSettings ForLargeMap() => new LocationCameraSettings
        {
            Version = 1, FollowCommander = true, SmoothTime = .25f, DeadZone = new Vector2(.08f, .08f)
        };
    }

    // Одна функция координат земли для показа, кликов, высоты, предпросмотра
    // и камеры. Рисунок места: пиксели, начало сверху слева, Y вниз (как все
    // точки места). Виртуальная карта экспорта: пиксели, начало снизу слева,
    // Y вверх; участок (i, j) занимает [i·W, (i+1)·W) × [j·H, (j+1)·H).
    // Переворот строк PNG (сверху вниз) делается один раз — в данных высоты
    // (LocationHeightTileData хранит строки снизу вверх).
    public readonly struct LocationGroundGrid
    {
        public readonly int Columns, Rows, TileWidth, TileHeight;
        public readonly float CanvasWidth, CanvasHeight;

        public LocationGroundGrid(int columns, int rows, int tileWidth, int tileHeight, float canvasWidth, float canvasHeight)
        {
            Columns = Mathf.Max(1, columns);
            Rows = Mathf.Max(1, rows);
            TileWidth = Mathf.Max(1, tileWidth);
            TileHeight = Mathf.Max(1, tileHeight);
            CanvasWidth = Mathf.Max(1, canvasWidth);
            CanvasHeight = Mathf.Max(1, canvasHeight);
        }

        public static LocationGroundGrid For(LocationGroundDefinition ground, LocalLocationDefinition location)
        {
            Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
            return new LocationGroundGrid(ground.Columns, ground.Rows, ground.TileWidth, ground.TileHeight, canvas.x, canvas.y);
        }

        public int VirtualWidth => Columns * TileWidth;
        public int VirtualHeight => Rows * TileHeight;
        // Пикселей виртуальной карты на пиксель рисунка места (1 — без растяжения).
        public double ScaleX => VirtualWidth / (double)CanvasWidth;
        public double ScaleY => VirtualHeight / (double)CanvasHeight;
        public bool IsOneToOne => Math.Abs(ScaleX - 1) < 1e-6 && Math.Abs(ScaleY - 1) < 1e-6;

        public Vector2d CanvasToVirtual(double x, double y) => new Vector2d(x * ScaleX, (CanvasHeight - y) * ScaleY);
        public Vector2 VirtualToCanvas(double x, double y) => new Vector2((float)(x / ScaleX), (float)(CanvasHeight - y / ScaleY));

        public bool InsideVirtual(double x, double y) => x >= 0 && y >= 0 && x <= VirtualWidth && y <= VirtualHeight;

        // Участок точки виртуальной карты: полуоткрытые интервалы; точка на
        // правой/верхней внешней границе — последний участок.
        public bool TryTileOf(double x, double y, out int column, out int row)
        {
            column = row = -1;
            if (double.IsNaN(x) || double.IsNaN(y) || !InsideVirtual(x, y)) return false;
            column = Math.Min(Columns - 1, (int)Math.Floor(x / TileWidth));
            row = Math.Min(Rows - 1, (int)Math.Floor(y / TileHeight));
            return true;
        }

        // Прямоугольник участка на рисунке места (пиксели, Y вниз).
        public Rect TileCanvasRect(int column, int row)
        {
            Vector2 topLeft = VirtualToCanvas((double)column * TileWidth, (double)(row + 1) * TileHeight);
            Vector2 bottomRight = VirtualToCanvas((double)(column + 1) * TileWidth, (double)row * TileHeight);
            return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
        }

        // Прямоугольник области виртуальной карты (снизу слева) на рисунке места.
        public Rect VirtualRectToCanvas(Rect area)
        {
            Vector2 topLeft = VirtualToCanvas(area.xMin, area.yMax);
            Vector2 bottomRight = VirtualToCanvas(area.xMax, area.yMin);
            return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
        }
    }

    // Точная пара координат (двойная точность: большие карты и пиксели высоты).
    public readonly struct Vector2d
    {
        public readonly double X, Y;
        public Vector2d(double x, double y) { X = x; Y = y; }
    }

    // Совместимый адаптер: что рисовать землёй места. Прежний рисунок места —
    // один участок на весь рисунок; земля из участков — каждый участок в своём
    // прямоугольнике общей системы координат.
    public static class LocationGroundLayout
    {
        public struct Piece
        {
            public Sprite Sprite;
            public Rect CanvasRect;
            public int Column, Row;
            public bool Missing;
        }

        public static List<Piece> Resolve(LocationVisualDefinition visual, LocalLocationDefinition location)
        {
            List<Piece> result = new List<Piece>();
            Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
            LocationGroundDefinition ground = visual?.Ground;
            if (ground == null || !ground.IsTiled)
            {
                result.Add(new Piece { Sprite = visual?.Background, CanvasRect = new Rect(Vector2.zero, canvas), Missing = visual?.Background == null });
                return result;
            }
            LocationGroundGrid grid = LocationGroundGrid.For(ground, location);
            for (int row = 0; row < grid.Rows; row++)
            {
                for (int column = 0; column < grid.Columns; column++)
                {
                    LocationGroundTile tile = ground.Find(column, row);
                    result.Add(new Piece
                    {
                        Sprite = tile?.Color, CanvasRect = grid.TileCanvasRect(column, row),
                        Column = column, Row = row, Missing = tile?.Color == null
                    });
                }
            }
            return result;
        }

        // Ошибки, при которых землю нельзя пускать в игру (окно базы
        // показывает их же; частичный предпросмотр в редакторе разрешён).
        public static List<string> RuntimeErrors(LocationVisualDefinition visual, LocalLocationDefinition location)
        {
            List<string> errors = new List<string>();
            LocationGroundDefinition ground = visual?.Ground;
            if (ground == null || !ground.IsTiled) return errors;
            if (ground.Columns <= 0 || ground.Rows <= 0 || ground.TileWidth <= 0 || ground.TileHeight <= 0)
            {
                errors.Add("Земля из участков: неверная сетка или размер участка.");
                return errors;
            }
            if (ground.Incomplete)
                errors.Add("Земля из участков: экспорт «" + ground.MapId + "» не завершён — доступен только предпросмотр в Базе локаций.");
            HashSet<string> keys = new HashSet<string>();
            List<string> missingColor = new List<string>(), missingHeight = new List<string>();
            foreach (LocationGroundTile tile in ground.Tiles)
            {
                if (tile == null) { errors.Add("Земля из участков: пустая запись участка."); continue; }
                if (tile.X < 0 || tile.Y < 0 || tile.X >= ground.Columns || tile.Y >= ground.Rows)
                    errors.Add("Участок " + tile.Key + " вне сетки " + ground.Columns + "×" + ground.Rows + ".");
                if (!keys.Add(tile.Key))
                    errors.Add("Участок " + tile.Key + " встречается дважды.");
            }
            for (int row = 0; row < ground.Rows; row++)
            {
                for (int column = 0; column < ground.Columns; column++)
                {
                    LocationGroundTile tile = ground.Find(column, row);
                    if (tile?.Color == null) missingColor.Add(LocationGroundTile.KeyOf(column, row));
                    if (ground.HeightEnabled && tile?.Height == null) missingHeight.Add(LocationGroundTile.KeyOf(column, row));
                }
            }
            if (missingColor.Count > 0)
                errors.Add("Нет Color у участков: " + string.Join(", ", missingColor) + ".");
            if (ground.HeightEnabled)
            {
                if (!(ground.HeightMax > ground.HeightMin) || double.IsNaN(ground.HeightMin) || double.IsInfinity(ground.HeightMax))
                    errors.Add("Неверный диапазон высоты: max должен быть больше min.");
                if (missingHeight.Count > 0 && !ground.HeightPartialAllowed)
                    errors.Add("Высота включена, но нет Height у участков: " + string.Join(", ", missingHeight) +
                               ". Импортируйте их или отметьте «Высота только на части карты».");
            }
            Vector2 canvas = LocationVisualGeometry.CanvasSize(location);
            Vector2Int size = ground.VirtualSize;
            if (Mathf.Abs(canvas.x * size.y - canvas.y * size.x) > Mathf.Max(canvas.x, canvas.y))
                errors.Add("Пропорции рисунка места " + canvas.x + "×" + canvas.y + " не совпадают с землёй " + size.x + "×" + size.y +
                           ": участки растянуты неравномерно. Подгоните размер места в «Земле».");
            return errors;
        }
    }
}
