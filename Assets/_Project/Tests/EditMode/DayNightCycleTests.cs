using NUnit.Framework;

// Смена дня и ночи (бриф 19.11.2026): фазы, поворот диска и свет карты.
public class DayNightCycleTests
{
    private static readonly DayNightSettings Defaults = DayNightSettings.Default;

    [TestCase(8.0, DayPhase.Day)]
    [TestCase(12.0, DayPhase.Day)]
    [TestCase(6.0, DayPhase.Dawn)]
    [TestCase(18.0, DayPhase.Dusk)]
    [TestCase(0.0, DayPhase.Night)]
    [TestCase(23.99, DayPhase.Night)]
    public void PhaseMatchesBrief(double hour, DayPhase expected)
    {
        Assert.AreEqual(expected, DayNightCycle.GetPhase(hour, Defaults));
    }

    [TestCase(8.0, "Утро")]
    [TestCase(12.0, "День")]
    [TestCase(18.0, "Закат")]
    [TestCase(0.0, "Ночь")]
    public void PhaseNameForTooltip(double hour, string expected)
    {
        Assert.AreEqual(expected, DayNightCycle.GetPhaseName(hour, Defaults));
    }

    [Test]
    public void DiscMakesExactlyOneTurnPer24Hours()
    {
        double a = DayNightCycle.GetDiscRotationDegrees(10.0);
        double b = DayNightCycle.GetDiscRotationDegrees(34.0);
        Assert.AreEqual(360.0, a - b, 1e-9);
    }

    [Test]
    public void SunUnderPointerAtNoonAndMoonAtMidnight()
    {
        // Под указателем (верх экрана) — точка диска под углом -поворот.
        double noonAngle = -DayNightCycle.GetDiscRotationDegrees(DayNightCycle.GetAbsoluteHours(3, 12.0));
        double midnightAngle = -DayNightCycle.GetDiscRotationDegrees(DayNightCycle.GetAbsoluteHours(3, 0.0));
        Assert.AreEqual(12.0, DayNightCycle.HourAtDiscAngle(noonAngle), 1e-9);
        Assert.AreEqual(0.0, DayNightCycle.HourAtDiscAngle(midnightAngle), 1e-9);
        Assert.AreEqual(0.0, DayNightCycle.GetNightWeight(12.0, Defaults), 1e-9);
        Assert.AreEqual(1.0, DayNightCycle.GetNightWeight(0.0, Defaults), 1e-9);
    }

    [Test]
    public void MidnightHasNoJumpOrReverse()
    {
        double before = DayNightCycle.GetDiscRotationDegrees(DayNightCycle.GetAbsoluteHours(1, 23.99));
        double after = DayNightCycle.GetDiscRotationDegrees(DayNightCycle.GetAbsoluteHours(2, 0.0));
        Assert.Less(after, before);
        Assert.Less(before - after, 0.2);
    }

    [Test]
    public void WrappedRotationStaysInRangeAndMatchesModulo()
    {
        for (double h = 0; h < 24 * 400; h += 7.3)
        {
            double wrapped = DayNightCycle.GetDiscRotationWrapped(h);
            Assert.LessOrEqual(wrapped, 0.0);
            Assert.Greater(wrapped, -360.0);
            double diff = (DayNightCycle.GetDiscRotationDegrees(h) - wrapped) % 360.0;
            Assert.AreEqual(0.0, System.Math.Min(System.Math.Abs(diff), 360.0 - System.Math.Abs(diff)), 1e-6);
        }
    }

    [Test]
    public void NoonIsUnchangedAndNightIsDimButNotBlack()
    {
        DayNightLight noon = DayNightCycle.GetLight(12.0, Defaults);
        Assert.AreEqual(1f, noon.R, 1e-4f);
        Assert.AreEqual(1f, noon.G, 1e-4f);
        Assert.AreEqual(1f, noon.B, 1e-4f);
        DayNightLight night = DayNightCycle.GetLight(0.0, Defaults);
        Assert.Less(night.R, 0.8f);
        Assert.Greater(night.R, 0.45f);
        Assert.Greater(night.B, night.R);
    }

    [Test]
    public void DawnIsWarmAndDuskIsOchre()
    {
        DayNightLight dawn = DayNightCycle.GetLight(6.0, Defaults);
        DayNightLight dusk = DayNightCycle.GetLight(18.0, Defaults);
        Assert.Greater(dawn.R, dawn.B);
        Assert.Greater(dusk.R, dusk.B);
    }

    [Test]
    public void LightChangesSmoothly()
    {
        DayNightLight previous = DayNightCycle.GetLight(0.0, Defaults);
        for (double h = 0.05; h <= 24.0; h += 0.05)
        {
            DayNightLight light = DayNightCycle.GetLight(h, Defaults);
            Assert.Less(System.Math.Abs(light.R - previous.R), 0.03f, "час " + h);
            Assert.Less(System.Math.Abs(light.B - previous.B), 0.03f, "час " + h);
            previous = light;
        }
    }

    [Test]
    public void DisabledOrZeroStrengthKeepsDayPalette()
    {
        DayNightSettings off = new DayNightSettings { Enabled = false };
        DayNightSettings weak = new DayNightSettings { Strength = 0.0 };
        Assert.AreEqual(1f, DayNightCycle.GetLight(0.0, off).R, 1e-6f);
        Assert.AreEqual(1f, DayNightCycle.GetLight(0.0, weak).B, 1e-6f);
    }
}
