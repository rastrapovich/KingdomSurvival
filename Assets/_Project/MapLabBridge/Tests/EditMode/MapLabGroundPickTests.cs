using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.MapLabBridge
{
    // 12И/12У: клик по экрану → точка земли с учётом сдвига рисунка по высоте.
    public sealed class MapLabGroundPickTests
    {
        [Test]
        public void ConstantShiftIsInverted()
        {
            float y = MapLabGameLive.SolveGroundY(g => g - 5f, 50f, 12f, 0.25f);
            Assert.That(y, Is.EqualTo(55f).Within(0.01f));
        }

        [Test]
        public void SlopedShiftIsInverted()
        {
            // Экранный y = g + 0.1·(g − 40): подставляем найденный корень обратно.
            float y = MapLabGameLive.SolveGroundY(g => g + 0.1f * (g - 40f), 52f, 12f, 0.25f);
            Assert.That(y + 0.1f * (y - 40f), Is.EqualTo(52f).Within(0.01f));
        }

        [Test]
        public void ClosestToCameraRootIsChosenAtLedge()
        {
            // Уступ: за g = 50 земля поднимается и рисуется на 4 выше. Цель 48 видна
            // и до уступа (g = 48 → 48), и на верхней площадке (g = 52 → 48).
            float y = MapLabGameLive.SolveGroundY(g => g < 50f ? g : g - 4f, 48f, 12f, 0.25f);
            Assert.That(y, Is.EqualTo(52f).Within(0.01f));
        }

        [Test]
        public void NoRootFallsBackWithoutThrowing()
        {
            float y = MapLabGameLive.SolveGroundY(g => g < 50f ? g : g + 20f, 55f, 12f, 0.25f);
            Assert.That(y, Is.InRange(43f, 67f));
        }

        // Наклон камеры (50° — базовый, 35° — наибольшее приближение MapLab
        // Stage03/04): сдвиг рисунка по высоте зависит от cos/sin наклона, и
        // обратный пересчёт «экран → земля» возвращает ту же точку.
        [TestCase(50f)]
        [TestCase(35f)]
        public void GroundPickRoundTrips_AtPitch(float pitch)
        {
            float sin = Mathf.Sin(pitch * Mathf.Deg2Rad), cos = Mathf.Cos(pitch * Mathf.Deg2Rad);
            const float mean = 3f, playHeight = 100f;
            // Площадка на севере (y < 50) выше низа на 6 ед.; в процентах пути y растёт к югу.
            float Height(float y) => y < 50f ? 6f : 0f;
            float Screen(float y) => y + MapLabGameLive.ShiftPercent(Height(y), mean, sin, cos, playHeight);
            for (float y = 5f; y <= 95f; y += 2.5f)
            {
                if (Mathf.Abs(y - 50f) < 0.01f)
                    continue;
                float root = MapLabGameLive.SolveGroundY(Screen, Screen(y), 25f, 0.25f);
                Assert.That(root, Is.EqualTo(y).Within(0.05f), $"наклон {pitch}°, y = {y}");
            }
        }

        [Test]
        public void SteeperTiltShiftsHeightsMore()
        {
            float shift50 = MapLabGameLive.ShiftPercent(6f, 3f, Mathf.Sin(50f * Mathf.Deg2Rad), Mathf.Cos(50f * Mathf.Deg2Rad), 100f);
            float shift35 = MapLabGameLive.ShiftPercent(6f, 3f, Mathf.Sin(35f * Mathf.Deg2Rad), Mathf.Cos(35f * Mathf.Deg2Rad), 100f);
            Assert.Less(shift35, shift50, "при 35° тот же перепад высот сдвигает рисунок сильнее (сдвиг вверх — отрицательный)");
        }
    }
}
