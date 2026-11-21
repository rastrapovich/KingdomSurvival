using System.Collections.Generic;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Приоритет записи (0–10) — вес выбора рисунка в генераторе арены.
    public sealed class ArenaPriorityTests
    {
        private static MapLabDecorArt Art(string id, float priority) => new MapLabDecorArt { Id = id, Priority = priority };

        [Test]
        public void EqualPriorities_PickUniformly()
        {
            List<MapLabDecorArt> pool = new List<MapLabDecorArt> { Art("a", 1f), Art("b", 1f), Art("c", 1f), Art("d", 1f) };
            for (int i = 0; i < pool.Count; i++)
                Assert.AreSame(pool[i], MapLabArtAssets.Pick(pool, (i + 0.5f) / pool.Count));
        }

        [Test]
        public void Priority_ScalesHowOftenAssetIsPicked()
        {
            List<MapLabDecorArt> pool = new List<MapLabDecorArt> { Art("rare", 0.1f), Art("common", 1f) };
            int rare = 0;
            const int rolls = 1000;
            for (int i = 0; i < rolls; i++)
                if (MapLabArtAssets.Pick(pool, (i + 0.5f) / rolls).Id == "rare") rare++;
            Assert.That(rare, Is.InRange(5, 15), "0,1 против 1: вес 0,01 против 1 — около 1 % выборов.");
        }

        [Test]
        public void ZeroPriority_IsNeverPicked()
        {
            List<MapLabDecorArt> pool = new List<MapLabDecorArt> { Art("off", 0f), Art("on", 1f) };
            for (int i = 0; i < 100; i++)
                Assert.AreEqual("on", MapLabArtAssets.Pick(pool, i / 100f).Id);
        }
    }
}
