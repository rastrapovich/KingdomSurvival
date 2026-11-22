using KingdomSurvival.BattleSandbox;
using KingdomSurvival.MapLabBridge;
using NUnit.Framework;
using UnityEngine;

namespace KingdomSurvival.BattleSandbox.Tests
{
    // Свои настройки темы: у темы с профилем генератор берёт его, у остальных — стандартные.
    public sealed class ArenaThemeProfileTests
    {
        private SandboxArenaGenerationSettings saved;

        [SetUp]
        public void SetUp() => saved = SandboxArenaGenerationSettings.Current;

        [TearDown]
        public void TearDown()
        {
            ArenaGeneratorAsset.PreviewStandard = false;
            SandboxArenaGenerationSettings.Current = saved;
        }

        [Test]
        public void ThemeWithProfile_UsesOwnSettings_OthersStandard()
        {
            ArenaGeneratorAsset asset = ScriptableObject.CreateInstance<ArenaGeneratorAsset>();
            try
            {
                asset.Tactics.HillAmount = 1f;
                asset.ThemeProfiles.Add(new ArenaThemeProfile { ThemeId = SandboxArenaThemes.Id(SandboxArenaTheme.Ford), Tactics = new SandboxArenaGenerationSettings { HillAmount = 0f } });
                asset.Activate();
                Assert.AreEqual(0f, SandboxArenaGenerationSettings.For(SandboxArenaTheme.Ford).HillAmount, "у брода свои");
                Assert.AreEqual(1f, SandboxArenaGenerationSettings.For(SandboxArenaTheme.DryGlade).HillAmount, "у поляны стандартные");
                ArenaGeneratorAsset.PreviewStandard = true;
                Assert.AreEqual(1f, SandboxArenaGenerationSettings.For(SandboxArenaTheme.Ford).HillAmount, "предпросмотр стандартной темы");
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
