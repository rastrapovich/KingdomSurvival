using UnityEngine;

namespace KingdomSurvival.MapLab
{
    // Примыкание тропы к главной дороге (этап 04). Одна и та же функция задаёт
    // и заливку (MapLabTiledPainter), и кромку пера (MapLabInk): на коротком
    // отрезке у узла тропа плавно получает цвет, плотность и вес линии дороги,
    // устье немного расширяется. Вне узла тропа прежняя. На этапах 02–03 —
    // нейтральна (нулевая длина и устье).
    public readonly struct MapLabRoadJunction
    {
        public readonly float Length;
        public readonly float Mouth;

        public MapLabRoadJunction(float length, float mouth)
        {
            Length = Mathf.Max(0f, length);
            Mouth = Mathf.Max(0f, mouth);
        }

        public static MapLabRoadJunction From(MapLabSettings settings)
        {
            return settings != null && settings.IsStage04
                ? new MapLabRoadJunction(settings.Stage04.JunctionLength, settings.Stage04.JunctionMouth)
                : new MapLabRoadJunction(0f, 0f);
        }

        // 1 у кромки главной дороги → 0 на расстоянии Length (до кромки, ед.).
        public float BlendAt(float distanceToMainEdge)
        {
            if (Length <= 0f || distanceToMainEdge >= Length)
                return 0f;
            return 1f - MapLabGeometry.SmoothStep(0f, Length, Mathf.Max(0f, distanceToMainEdge));
        }

        // Добавка к полуширине тропы у узла: плавная воронка устья.
        public float MouthAt(float distanceToMainEdge)
        {
            if (Mouth <= 0f || Length <= 0f || distanceToMainEdge >= Length * 0.6f)
                return 0f;
            float t = 1f - MapLabGeometry.SmoothStep(0f, Length * 0.6f, Mathf.Max(0f, distanceToMainEdge));
            return Mouth * t * t;
        }
    }
}
