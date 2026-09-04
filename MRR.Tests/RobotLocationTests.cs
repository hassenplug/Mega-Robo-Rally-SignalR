namespace MRR.Tests;

/// <summary>
/// RobotLocation.X/Y used to clamp any negative value to 0, which silently turned the
/// parameterless constructor's intended "unset" sentinel of (-1,-1) into (0,0) -- a real
/// board square. See CommandItemPositionTests for the resulting bug this caused downstream.
/// </summary>
public class RobotLocationTests
{
    [Fact]
    public void DefaultConstructor_UsesNegativeOneSentinel_NotZero()
    {
        var location = new RobotLocation();

        Assert.Equal(-1, location.X);
        Assert.Equal(-1, location.Y);
    }

    [Fact]
    public void X_and_Y_AllowNegativeValues()
    {
        var location = new RobotLocation(Direction.Left, 0, 0)
        {
            X = -1,
            Y = -1,
        };

        Assert.Equal(-1, location.X);
        Assert.Equal(-1, location.Y);
    }

    [Fact]
    public void CalcNewLocation_MovingLeftFromColumnZero_GoesNegative_NotClampedToZero()
    {
        var current = new RobotLocation(Direction.Left, 0, 3);

        var next = current.CalcNewLocation(1, Direction.Left);

        Assert.Equal(-1, next.X);
        Assert.Equal(3, next.Y);
    }
}
