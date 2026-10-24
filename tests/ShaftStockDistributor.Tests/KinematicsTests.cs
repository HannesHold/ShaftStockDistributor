using ShaftStockDistributor.Core;

namespace ShaftStockDistributor.Tests;

public class KinematicsTests
{
    [Fact]
    public void Zero_distance_takes_no_time()
    {
        Assert.Equal(0, Kinematics.AxisSeconds(0, 1.5, 0.5));
    }

    [Fact]
    public void Short_move_uses_a_triangular_profile()
    {
        var seconds = Kinematics.AxisSeconds(0.1, speedMetersPerSecond: 1.5, acceleration: 0.5);
        Assert.Equal(2 * Math.Sqrt(0.1 / 0.5), seconds, precision: 9);
    }

    [Fact]
    public void Long_move_cruises_at_full_speed()
    {
        var seconds = Kinematics.AxisSeconds(10, speedMetersPerSecond: 1.5, acceleration: 0.5);
        var expected = (2 * (1.5 / 0.5)) + ((10 - (1.5 * 1.5 / 0.5)) / 1.5);
        Assert.Equal(expected, seconds, precision: 9);
    }

    [Fact]
    public void Travel_time_is_the_slower_axis()
    {
        var kinematics = new Kinematics();
        var horizontal = Kinematics.AxisSeconds(10, kinematics.HorizontalSpeedMetersPerSecond, kinematics.HorizontalAccelerationMetersPerSecondSquared);
        var vertical = Kinematics.AxisSeconds(0.5, kinematics.VerticalSpeedMetersPerSecond, kinematics.VerticalAccelerationMetersPerSecondSquared);

        Assert.True(horizontal > vertical);
        Assert.Equal(horizontal, kinematics.TravelSeconds(10, 0.5), precision: 9);
    }
}
