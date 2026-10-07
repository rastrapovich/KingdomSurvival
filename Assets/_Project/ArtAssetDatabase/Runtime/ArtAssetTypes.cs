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

    // ПР-12П: порядок кадров — по кругу (1-2-3-1-2-3) или туда-обратно
    // (1-2-3-2-1: качание травы без скачка с последнего кадра на первый).
    // Значения сериализуются — новые только в конец.
    public enum ArtAssetPlayback
    {
        Loop,
        PingPong
    }

    public static class ArtAssetAnimation
    {
        public static string PlaybackTitle(ArtAssetPlayback playback) =>
            playback == ArtAssetPlayback.PingPong ? "Туда-обратно" : "По кругу";

        // Кадр в момент seconds: count — кадров, fps — кадров в секунду,
        // phase — сдвиг 0..1 доли полного цикла (свой у экземпляра).
        public static int FrameIndex(int count, float fps, ArtAssetPlayback playback, double seconds, float phase)
        {
            if (count <= 1 || fps <= 0) return 0;
            int cycle = playback == ArtAssetPlayback.PingPong ? count * 2 - 2 : count;
            double position = seconds * fps + phase * cycle;
            int step = (int)(Math.Floor(position) % cycle);
            if (step < 0) step += cycle;
            return step < count ? step : cycle - step;
        }

        // Устойчивый сдвиг 0..1 по ID экземпляра (FNV-1a): один и тот же при
        // каждом запуске и на любой машине — не string.GetHashCode.
        public static float StablePhase(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in id) hash = (hash ^ c) * 16777619;
                return (hash & 0xFFFFFF) / (float)0x1000000;
            }
        }
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
