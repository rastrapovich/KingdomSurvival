using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.AnimationDatabase.Tests
{
    // Снаряд набора анимаций: значения по умолчанию, клампы, геометрия полёта
    // и правило «Атака или Выстрел».
    public sealed class CreatureAnimationProjectileTests
    {
        private Texture2D texture;
        private Sprite sprite;

        [SetUp]
        public void SetUp()
        {
            texture = new Texture2D(4, 4);
            sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
        }

        [TearDown]
        public void TearDown()
        {
            if (sprite != null)
                Object.DestroyImmediate(sprite);
            if (texture != null)
                Object.DestroyImmediate(texture);
        }

        [Test]
        public void Defaults_AreExpected()
        {
            var data = new CreatureAnimationProjectileData();
            Assert.IsFalse(data.HasSprite);
            Assert.AreEqual(10f, data.SpeedHexesPerSecond, 1e-5f);
            Assert.AreEqual(0.75f, data.LengthHexes, 1e-5f);
            Assert.AreEqual(0.08f, data.ArcHeight, 1e-5f);
            Assert.AreEqual(0.8f, data.HitHeightHexes, 1e-5f);
            Assert.AreEqual(new Vector2(0.14f, 0.235f), data.LaunchPoint);
        }

        [Test]
        public void Setters_ClampAndFallBackToDefaults()
        {
            var data = new CreatureAnimationProjectileData();
            data.SetSpeedHexesPerSecond(500f);
            Assert.AreEqual(60f, data.SpeedHexesPerSecond, 1e-5f);
            data.SetSpeedHexesPerSecond(0.2f);
            Assert.AreEqual(1f, data.SpeedHexesPerSecond, 1e-5f);
            data.SetSpeedHexesPerSecond(0f);
            Assert.AreEqual(10f, data.SpeedHexesPerSecond, 1e-5f);
            data.SetSpeedHexesPerSecond(float.NaN);
            Assert.AreEqual(10f, data.SpeedHexesPerSecond, 1e-5f);

            data.SetLengthHexes(50f);
            Assert.AreEqual(3f, data.LengthHexes, 1e-5f);
            data.SetLengthHexes(0.01f);
            Assert.AreEqual(0.1f, data.LengthHexes, 1e-5f);
            data.SetLengthHexes(-1f);
            Assert.AreEqual(0.75f, data.LengthHexes, 1e-5f);

            data.SetArcHeight(2f);
            Assert.AreEqual(0.5f, data.ArcHeight, 1e-5f);
            data.SetArcHeight(0f);
            Assert.AreEqual(0f, data.ArcHeight, 1e-5f);
            data.SetArcHeight(-1f);
            Assert.AreEqual(0f, data.ArcHeight, 1e-5f);

            data.SetHitHeightHexes(9f);
            Assert.AreEqual(3f, data.HitHeightHexes, 1e-5f);
            data.SetHitHeightHexes(0f);
            Assert.AreEqual(0f, data.HitHeightHexes, 1e-5f);
            data.SetHitHeightHexes(-1f);
            Assert.AreEqual(0.8f, data.HitHeightHexes, 1e-5f);
        }

        [Test]
        public void SetSprite_SetsHasSprite()
        {
            var data = new CreatureAnimationProjectileData();
            data.SetSprite(sprite);
            Assert.IsTrue(data.HasSprite);
            Assert.AreSame(sprite, data.Sprite);
        }

        [Test]
        public void LaunchPosition_GoesTowardTarget()
        {
            var data = new CreatureAnimationProjectileData();
            Vector2 ground = new Vector2(100f, 200f);
            Vector2 size = new Vector2(100f, 200f);
            Vector2 right = CreatureAnimationProjectileMath.LaunchPosition(ground, size, new Vector2(300f, 200f), data, 1f);
            Vector2 left = CreatureAnimationProjectileMath.LaunchPosition(ground, size, new Vector2(-50f, 200f), data, 1f);
            Assert.AreEqual(100f + 0.14f * 100f, right.x, 1e-3f);
            Assert.AreEqual(100f - 0.14f * 100f, left.x, 1e-3f);
            Assert.AreEqual(200f - 0.235f * 200f, right.y, 1e-3f);
            Assert.AreEqual(right.y, left.y, 1e-5f);
        }

        [Test]
        public void LaunchPosition_UsesFallbackSignWhenTargetIsAbove()
        {
            var data = new CreatureAnimationProjectileData();
            Vector2 ground = new Vector2(100f, 200f);
            Vector2 size = new Vector2(100f, 200f);
            Vector2 target = new Vector2(100.0005f, 50f);
            Assert.Greater(CreatureAnimationProjectileMath.LaunchPosition(ground, size, target, data, 1f).x, ground.x);
            Assert.Less(CreatureAnimationProjectileMath.LaunchPosition(ground, size, target, data, -1f).x, ground.x);
        }

        [Test]
        public void Math_WorksWithNullData()
        {
            Vector2 launch = CreatureAnimationProjectileMath.LaunchPosition(
                new Vector2(0f, 100f), new Vector2(100f, 100f), new Vector2(50f, 100f), null, 1f);
            Assert.AreEqual(14f, launch.x, 1e-3f);
            Assert.AreEqual(100f - 23.5f, launch.y, 1e-3f);
            Vector2 hit = CreatureAnimationProjectileMath.HitPosition(new Vector2(10f, 100f), 50f, null);
            Assert.AreEqual(100f - 40f, hit.y, 1e-3f);
            Assert.AreEqual(0.4f, CreatureAnimationProjectileMath.FlightSeconds(Vector2.zero, new Vector2(40f, 0f), 10f, null), 1e-4f);
        }

        [Test]
        public void HitPosition_IsAboveTargetCenter()
        {
            var data = new CreatureAnimationProjectileData();
            data.SetHitHeightHexes(1f);
            Vector2 hit = CreatureAnimationProjectileMath.HitPosition(new Vector2(30f, 100f), 40f, data);
            Assert.AreEqual(30f, hit.x, 1e-5f);
            Assert.AreEqual(60f, hit.y, 1e-5f);
        }

        [Test]
        public void FlightSeconds_FollowsSpeedAndHasMinimum()
        {
            var data = new CreatureAnimationProjectileData();
            Assert.AreEqual(0.4f, CreatureAnimationProjectileMath.FlightSeconds(Vector2.zero, new Vector2(160f, 0f), 40f, data), 1e-4f);
            Assert.AreEqual(0.05f, CreatureAnimationProjectileMath.FlightSeconds(Vector2.zero, new Vector2(1f, 0f), 40f, data), 1e-5f);
            // Шаг гекса <= 0 считается единицей.
            Assert.AreEqual(1f, CreatureAnimationProjectileMath.FlightSeconds(Vector2.zero, new Vector2(10f, 0f), 0f, data), 1e-4f);
        }

        [Test]
        public void Evaluate_EndpointsAndArc()
        {
            Vector2 from = new Vector2(0f, 100f);
            Vector2 to = new Vector2(200f, 100f);
            Vector2 start = CreatureAnimationProjectileMath.Evaluate(from, to, 0.1f, 0f, out _);
            Vector2 end = CreatureAnimationProjectileMath.Evaluate(from, to, 0.1f, 1f, out _);
            Assert.AreEqual(from.x, start.x, 1e-4f);
            Assert.AreEqual(from.y, start.y, 1e-4f);
            Assert.AreEqual(to.x, end.x, 1e-4f);
            Assert.AreEqual(to.y, end.y, 1e-4f);

            Vector2 middle = CreatureAnimationProjectileMath.Evaluate(from, to, 0.1f, 0.5f, out _);
            Assert.Less(middle.y, 100f);
            Assert.AreEqual(100f - 4f * 0.1f * 200f * 0.25f, middle.y, 1e-3f);

            Vector2 clamped = CreatureAnimationProjectileMath.Evaluate(from, to, 0.1f, 5f, out _);
            Assert.AreEqual(to.x, clamped.x, 1e-4f);
        }

        [Test]
        public void Evaluate_Angles()
        {
            float angle;
            CreatureAnimationProjectileMath.Evaluate(new Vector2(0f, 0f), new Vector2(100f, 0f), 0f, 0.5f, out angle);
            Assert.AreEqual(0f, angle, 1e-3f);

            CreatureAnimationProjectileMath.Evaluate(new Vector2(0f, 0f), new Vector2(100f, 100f), 0f, 0.5f, out angle);
            Assert.AreEqual(45f, angle, 1e-3f);

            // С дугой в начале стрела летит вверх (угол < 0), в конце — вниз.
            CreatureAnimationProjectileMath.Evaluate(new Vector2(0f, 0f), new Vector2(100f, 0f), 0.2f, 0f, out angle);
            Assert.Less(angle, 0f);
            CreatureAnimationProjectileMath.Evaluate(new Vector2(0f, 0f), new Vector2(100f, 0f), 0.2f, 1f, out angle);
            Assert.Greater(angle, 0f);
        }

        [Test]
        public void SetData_ProjectileIsNeverNullAndEmptyByDefault()
        {
            var set = new CreatureAnimationSetData("zz_archer", "Лучник");
            Assert.IsNotNull(set.Projectile);
            Assert.IsFalse(set.Projectile.HasSprite);
        }

        [Test]
        public void FiresProjectile_FollowsAttackShootRule()
        {
            var set = new CreatureAnimationSetData("zz_archer", "Лучник");
            set.GetOrAddClip(CreatureAnimationAction.Attack);

            // Без картинки снаряда не бывает.
            Assert.IsFalse(set.FiresProjectile(CreatureAnimationAction.Attack));
            Assert.IsFalse(set.FiresProjectile(CreatureAnimationAction.Shoot));

            set.Projectile.SetSprite(sprite);
            Assert.IsTrue(set.FiresProjectile(CreatureAnimationAction.Attack));
            Assert.IsTrue(set.FiresProjectile(CreatureAnimationAction.Shoot));
            Assert.IsFalse(set.FiresProjectile(CreatureAnimationAction.Idle));

            // Появился отдельный «Выстрел»: стрела только из него, «Атака» — ближний удар.
            set.GetOrAddClip(CreatureAnimationAction.Shoot);
            Assert.IsFalse(set.FiresProjectile(CreatureAnimationAction.Attack));
            Assert.IsTrue(set.FiresProjectile(CreatureAnimationAction.Shoot));
            Assert.IsFalse(set.FiresProjectile(CreatureAnimationAction.Idle));
        }
    }
}
