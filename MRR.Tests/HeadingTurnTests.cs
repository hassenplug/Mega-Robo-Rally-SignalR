using MRR.Devices;

namespace MRR.Tests;

/// <summary>
/// RobotConnection.PlanHeadingTurn: the turn_for angle for a game turn with heading hold
/// (ROBOT_MOVEMENT_DESIGN.md Phase 1). Pure math; whether a real robot turns by the
/// fractional angle it is sent was checked on the board (Phase 0 abs-turn), not here.
/// </summary>
public class HeadingTurnTests
{
    private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 1);

    [Fact]
    public void FirstTurnHasNoTarget_TurnsNominalAndAnchors()
    {
        var p = RobotConnection.PlanHeadingTurn(null, 3.2, 90);
        Near(90, p.Angle);
        Near(93.2, p.NewTarget);
        Assert.True(p.Reanchored);
    }

    [Fact]
    public void OvershootOnLastTurn_IsTakenOutOfThisOne()
    {
        // Target 90, robot overshot to 90.8: the next right turn is 89.2 and aims at 180.
        var p = RobotConnection.PlanHeadingTurn(90, 90.8, 90);
        Near(89.2, p.Angle);
        Near(180, p.NewTarget);
        Assert.False(p.Reanchored);
    }

    [Fact]
    public void LeftTurn_CorrectsUndershoot()
    {
        var p = RobotConnection.PlanHeadingTurn(180, 181.5, -90);
        Near(-91.5, p.Angle);
        Near(90, p.NewTarget);
    }

    [Theory]
    [InlineData(0, 359.4, 90, 90.6, 90)]   // target 0, robot just short of 360
    [InlineData(0, 0.7, -90, -90.7, 270)]  // target 0, robot just past 0, turning left across 0
    [InlineData(270, 271.0, 90, 89.0, 0)]  // aiming at 360 lands on 0
    public void WrapsAcrossZero(double target, double heading, double nominal, double angle, double newTarget)
    {
        var p = RobotConnection.PlanHeadingTurn(target, heading, nominal);
        Near(angle, p.Angle);
        Near(newTarget, p.NewTarget);
        Assert.False(p.Reanchored);
    }

    [Theory]
    [InlineData(180, 179.0, 2, 181.0)]
    [InlineData(180, 181.0, 2, 179.0)]
    [InlineData(180, 181.0, -2, -181.0)]
    public void UTurnKeepsItsDirection(double target, double heading, double nominal, double angle)
    {
        var p = RobotConnection.PlanHeadingTurn(target, heading, nominal * 90);
        Near(angle, p.Angle);
        Near(0, p.NewTarget);
    }

    [Theory]
    [InlineData(90, 180.5)] // placed back by hand facing another way
    [InlineData(90, 75)]    // knocked or placed 15 degrees off
    public void BigGap_ReanchorsInsteadOfCorrecting(double target, double heading)
    {
        var p = RobotConnection.PlanHeadingTurn(target, heading, 90);
        Near(90, p.Angle);
        Near((heading + 90) % 360, p.NewTarget);
        Assert.True(p.Reanchored);
    }

    [Fact]
    public void GapAtTheLimit_IsStillCorrected()
    {
        var p = RobotConnection.PlanHeadingTurn(90, 80.5, 90);
        Near(99.5, p.Angle);
        Assert.False(p.Reanchored);
    }

    [Fact]
    public void FourRightTurnsWithSteadyOvershoot_DoNotBuildUp()
    {
        // Robot always turns 0.8 degrees more than asked, as measured at turn_rate 200.
        double? target = null;
        double heading = 0;
        for (int i = 0; i < 4; i++)
        {
            var p = RobotConnection.PlanHeadingTurn(target, heading, 90);
            heading = (heading + p.Angle + 0.8) % 360;
            target = p.NewTarget;
        }
        // Without correction: 3.2 degrees off. With it, only the last turn's 0.8.
        Near(0.8, ((heading - target!.Value) % 360 + 540) % 360 - 180);
    }
}
