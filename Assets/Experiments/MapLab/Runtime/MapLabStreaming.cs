using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Этап 12У-C: подгрузка подробностей карты. Общие сведения режима и
    // оценки памяти до запуска (бриф подгрузки §4): поля покраски — общий
    // массив 19 каналов × 4 отсчёта/ед.; покраска RGBA32 с mip при плотности P.
    public static class MapLabStreaming
    {
        // Режим подгрузки для пакетных замеров и окна (по умолчанию — из профиля).
        public static bool Enabled;

        public static double EstimateFieldsMb(float side)
        {
            double n = Mathf.Ceil(side * MapLabPaintFields.SamplesPerUnit) + 1;
            return n * n * MapLabPaintFields.Channels * 4.0 / 1048576.0;
        }

        public static double EstimatePaintMb(float side, float texelsPerUnit)
        {
            double t = side * texelsPerUnit;
            return t * t * 4.0 * 4.0 / 3.0 / 1048576.0;
        }
    }
}
