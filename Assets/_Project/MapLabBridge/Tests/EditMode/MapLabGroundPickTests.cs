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

        // Наклон от приближения: базовый на общем виде, 35° на наибольшем, между — плавно и монотонно.
        [Test]
        public void PitchForZoom_GoesFromBaseToThirtyFiveSmoothly()
        {
            Assert.That(MapLabGameLive.PitchForZoom(0.2f, 0.2f, 3f, 50f), Is.EqualTo(50f).Within(0.01f));
            Assert.That(MapLabGameLive.PitchForZoom(3f, 0.2f, 3f, 50f), Is.EqualTo(35f).Within(0.01f));
            float previous = 50.01f;
            for (float zoom = 0.2f; zoom <= 3f; zoom *= 1.05f)
            {
                float pitch = MapLabGameLive.PitchForZoom(zoom, 0.2f, 3f, 50f);
                Assert.LessOrEqual(pitch, previous + 1e-4f, "наклон не растёт с приближением");
                Assert.That(previous - pitch, Is.LessThan(1.5f), "ступенька наклона за шаг приближения 5% не больше 1,5°");
                previous = pitch;
            }
            Assert.That(MapLabGameLive.PitchForZoom(1f, 1f, 1f, 50f), Is.EqualTo(50f), "нет диапазона — базовый");
        }

        // Зум колесом через смену наклона: точка земли под курсором остаётся под курсором —
        // земля, найденная по старому наклону, после смены стоит на том же экранном месте.
        [TestCase(50f, 35f)]
        [TestCase(50f, 42f)]
        [TestCase(42f, 35f)]
        [TestCase(35f, 50f)]
        public void GroundUnderCursorStaysPut_WhenPitchChanges(float from, float to)
        {
            const float mean = 3f, playHeight = 100f;
            float Height(float y) => y < 50f ? 6f : 0f; // площадка на севере
            float Screen(float y, float pitch) => y + MapLabGameLive.ShiftPercent(Height(y), mean, Mathf.Sin(pitch * Mathf.Deg2Rad), Mathf.Cos(pitch * Mathf.Deg2Rad), playHeight);
            for (float ground = 8f; ground <= 92f; ground += 3f)
            {
                if (Mathf.Abs(ground - 50f) < 0.5f)
                    continue;
                float cursor = Screen(ground, from);
                // Курсор указывает на землю по старому наклону…
                float found = MapLabGameLive.SolveGroundY(y => Screen(y, from), cursor, 30f, 0.25f);
                Assert.That(found, Is.EqualTo(ground).Within(0.05f));
                // …после смены наклона земля там же, а её экранное место пересчитано (полотно перестроено),
                // обратный пересчёт по новому наклону возвращает ту же землю.
                float after = Screen(found, to);
                float back = MapLabGameLive.SolveGroundY(y => Screen(y, to), after, 30f, 0.25f);
                Assert.That(back, Is.EqualTo(ground).Within(0.05f), $"{from}° → {to}°, земля {ground}");
            }
        }

        // Карточка (здание, деталь места) стоит на земле в той же точке при любом наклоне:
        // расстояние от верха полотна до её основания в пикселях не зависит от того, как
        // полотно сжато наклоном (высота полотна = плоская высота × sin наклона).
        [TestCase(50f)]
        [TestCase(42f)]
        [TestCase(35f)]
        public void CardBase_ProjectsToTheSameGroundPointAtAnyPitch(float pitch)
        {
            const float canvasWidth = 1000f;
            MapLabGameLive.World w = new MapLabGameLive.World { Play = new Rect(10f, 20f, 200f, 200f), Mean = 3f };
            MapLabGameLive.CardSource card = new MapLabGameLive.CardSource { Base = new Vector3(110f, 5f, 90f), Width = 6f, Height = 8f, Pivot = new Vector2(0.5f, 0f) };
            // Эталон: проекция ортокамеры — сдвиг вверх на (h − средняя) · cos и глубина · sin.
            float Reference(float p)
            {
                float sin = Mathf.Sin(p * Mathf.Deg2Rad), cos = Mathf.Cos(p * Mathf.Deg2Rad);
                return -((card.Base.y - w.Mean) * cos + (card.Base.z - w.Play.yMax) * sin) * canvasWidth / w.Play.width;
            }
            w.Pitch = pitch;
            w.Sin = Mathf.Sin(pitch * Mathf.Deg2Rad);
            w.Cos = Mathf.Cos(pitch * Mathf.Deg2Rad);
            Rect rect = MapLabGameLive.CardRect(w, card);
            float flatHeight = canvasWidth; // квадратная плоская карта
            float canvasHeight = flatHeight * w.Sin;
            // Основание карточки: низ прямоугольника (опора y = 0).
            float baseY = rect.yMax / 100f * canvasHeight;
            Assert.That(baseY, Is.EqualTo(Reference(pitch)).Within(0.05f));
            // Ширина и высота карточки в пикселях не зависят от наклона (не растягиваются).
            Assert.That(rect.width / 100f * canvasWidth, Is.EqualTo(card.Width * canvasWidth / w.Play.width).Within(0.01f));
            Assert.That(rect.height / 100f * canvasHeight, Is.EqualTo(card.Height * canvasWidth / w.Play.width).Within(0.01f));
        }
    }
}
