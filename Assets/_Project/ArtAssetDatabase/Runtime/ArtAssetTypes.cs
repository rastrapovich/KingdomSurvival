using System;

namespace KingdomSurvival.ArtAssets
{
    // ПР-12Н: шесть ракурсов объекта — те же и в том же порядке, что у Базы
    // анимаций (CreatureAnimationDirection); соседние отличаются на 60°.
    // Значения сериализуются — порядок не менять.
    public enum ArtAssetView
    {
        Front,
        FrontRight,
        BackRight,
        Back,
        BackLeft,
        FrontLeft
    }

    // Категория — фильтр каталога, а не игровое правило. Новые — только в конец.
    public enum ArtAssetCategory
    {
        None,
        Buildings,
        Trees,
        Vegetation,
        Rocks,
        Items,
        Ground,
        Parts
    }

    // Слои места: тот же порядок, что LocationVisualBand.
    public enum ArtAssetLayer
    {
        Ground,
        GroundDetail,
        World,
        Foreground
    }

    public static class ArtAssetLabels
    {
        public const int ViewCount = 6;

        public static readonly ArtAssetView[] Views =
        {
            ArtAssetView.Front, ArtAssetView.FrontRight, ArtAssetView.BackRight,
            ArtAssetView.Back, ArtAssetView.BackLeft, ArtAssetView.FrontLeft
        };

        public static readonly ArtAssetCategory[] Categories =
        {
            ArtAssetCategory.Buildings, ArtAssetCategory.Trees, ArtAssetCategory.Vegetation, ArtAssetCategory.Rocks,
            ArtAssetCategory.Items, ArtAssetCategory.Ground, ArtAssetCategory.Parts, ArtAssetCategory.None
        };

        public static readonly ArtAssetLayer[] Layers =
        {
            ArtAssetLayer.Ground, ArtAssetLayer.GroundDetail, ArtAssetLayer.World, ArtAssetLayer.Foreground
        };

        public static string ViewTitle(ArtAssetView view)
        {
            switch (view)
            {
                case ArtAssetView.Front: return "Спереди";
                case ArtAssetView.FrontRight: return "Спереди справа";
                case ArtAssetView.BackRight: return "Сзади справа";
                case ArtAssetView.Back: return "Сзади";
                case ArtAssetView.BackLeft: return "Сзади слева";
                case ArtAssetView.FrontLeft: return "Спереди слева";
                default: return view.ToString();
            }
        }

        // Имя папки экспорта (как у Базы анимаций).
        public static string ViewFolder(ArtAssetView view)
        {
            switch (view)
            {
                case ArtAssetView.Front: return "Front";
                case ArtAssetView.FrontRight: return "Front_Right";
                case ArtAssetView.BackRight: return "Back_Right";
                case ArtAssetView.Back: return "Back";
                case ArtAssetView.BackLeft: return "Back_Left";
                case ArtAssetView.FrontLeft: return "Front_Left";
                default: return view.ToString();
            }
        }

        public static string CategoryTitle(ArtAssetCategory category)
        {
            switch (category)
            {
                case ArtAssetCategory.Buildings: return "Постройки";
                case ArtAssetCategory.Trees: return "Деревья";
                case ArtAssetCategory.Vegetation: return "Растительность";
                case ArtAssetCategory.Rocks: return "Камни";
                case ArtAssetCategory.Items: return "Предметы";
                case ArtAssetCategory.Ground: return "Земля и поверхности";
                case ArtAssetCategory.Parts: return "Части объектов";
                default: return "Без категории";
            }
        }

        public static string LayerTitle(ArtAssetLayer layer)
        {
            switch (layer)
            {
                case ArtAssetLayer.Ground: return "Земля";
                case ArtAssetLayer.GroundDetail: return "Детали земли";
                case ArtAssetLayer.Foreground: return "Передний план";
                default: return "Объекты и персонажи";
            }
        }

        // Расстояние по кругу ракурсов: 0…3.
        public static int RingDistance(ArtAssetView first, ArtAssetView second)
        {
            int difference = Math.Abs((int)first - (int)second) % ViewCount;
            return Math.Min(difference, ViewCount - difference);
        }

        // Запасной порядок для отсутствующего ракурса: сам ракурс, затем
        // ближайшие по кругу, при равенстве — следующий по часовой стрелке
        // (по порядку Спереди → Спереди справа → …). Зеркальных видов нет.
        public static ArtAssetView[] FallbackOrder(ArtAssetView view)
        {
            int start = (int)view;
            ArtAssetView[] result = new ArtAssetView[ViewCount];
            result[0] = view;
            int index = 1;
            for (int step = 1; step <= ViewCount / 2; step++)
            {
                result[index++] = (ArtAssetView)((start + step) % ViewCount);
                if (step * 2 != ViewCount) result[index++] = (ArtAssetView)((start - step + ViewCount) % ViewCount);
            }
            return result;
        }
    }
}
