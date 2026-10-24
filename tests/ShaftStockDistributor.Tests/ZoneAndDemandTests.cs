using ShaftStockDistributor.Core;
using ShaftStockDistributor.Optimizer;

namespace ShaftStockDistributor.Tests;

public class ZoneAndDemandTests
{
    [Theory]
    [InlineData(12, 4, 4, 4)]
    [InlineData(10, 3, 4, 3)]
    [InlineData(11, 3, 5, 3)]
    [InlineData(1, 0, 1, 0)]
    [InlineData(2, 0, 2, 0)]
    public void Thirds_put_the_remainder_in_the_middle(int segments, int left, int shared, int right)
    {
        var zones = ZoneMap.FromSegmentCount(segments);

        Assert.Equal(left, zones.Shuttle1OnlyCount);
        Assert.Equal(shared, zones.SharedCount);
        Assert.Equal(right, zones.Shuttle2OnlyCount);
        Assert.Equal(segments, zones.Shuttle1OnlyCount + zones.SharedCount + zones.Shuttle2OnlyCount);
    }

    [Fact]
    public void Segment_zones_follow_the_thirds()
    {
        var zones = ZoneMap.FromSegmentCount(12);

        Assert.Equal(Zone.Shuttle1Only, zones.ZoneOf(0));
        Assert.Equal(Zone.Shuttle1Only, zones.ZoneOf(3));
        Assert.Equal(Zone.Shared, zones.ZoneOf(4));
        Assert.Equal(Zone.Shared, zones.ZoneOf(7));
        Assert.Equal(Zone.Shuttle2Only, zones.ZoneOf(8));
        Assert.Equal(Zone.Shuttle2Only, zones.ZoneOf(11));
    }

    [Fact]
    public void Layout_shafts_inherit_the_segment_zone()
    {
        var layout = SampleWarehouse.CreateLayout();

        Assert.Equal(540, layout.Shafts.Count);
        Assert.All(layout.Shafts, shaft => Assert.Equal(layout.Zones.ZoneOf(shaft.Location.Segment), shaft.Zone));
        Assert.Equal(new ShuttlePose(0, 0, 0), layout.Shuttle1Home);
        Assert.Equal(new ShuttlePose(11, 0, 8), layout.Shuttle2Home);
    }

    [Fact]
    public void Shaft_count_covers_arrivals_during_the_pull_wait()
    {
        var shafts = ShaftDemandCalculator.RequiredShafts(
            cratesPerHour: 360,
            cratesPerPallet: 40,
            offset: 4,
            shaftCapacity: 48,
            expectedPullWaitSeconds: 300,
            transferDurationSeconds: 30);

        Assert.Equal(9, shafts);
    }

    [Theory]
    [InlineData(0, 300, 30, 1)]
    [InlineData(360, 0, 0, 1)]
    public void Shaft_count_is_one_when_nothing_arrives_or_the_wait_is_zero(
        double inflow,
        double wait,
        double transfer,
        int expected)
    {
        var shafts = ShaftDemandCalculator.RequiredShafts(inflow, 40, 4, 48, wait, transfer);
        Assert.Equal(expected, shafts);
    }

    [Fact]
    public void Missing_buffer_falls_back_to_the_whole_shaft()
    {
        var shafts = ShaftDemandCalculator.RequiredShafts(
            cratesPerHour: 2000,
            cratesPerPallet: 40,
            offset: 4,
            shaftCapacity: 44,
            expectedPullWaitSeconds: 300,
            transferDurationSeconds: 30);

        Assert.Equal(5, shafts);
    }

    [Fact]
    public void Unspecified_inflow_is_split_from_the_class_share()
    {
        var articles = new ArticleSet
        {
            AbcDefaults = new AbcDefaults { TotalCratesPerHour = 1000, ShareA = 0.5, ShareB = 0.2, ShareC = 0.3 }
        };
        articles.Items.Add(new Article { Id = "a1", AbcClass = AbcClass.A });
        articles.Items.Add(new Article { Id = "a2", AbcClass = AbcClass.A });
        articles.Items.Add(new Article { Id = "b1", AbcClass = AbcClass.B, CratesPerHour = 12 });

        var rates = InflowResolver.Resolve(articles);

        Assert.Equal(250, rates["a1"]);
        Assert.Equal(250, rates["a2"]);
        Assert.Equal(12, rates["b1"]);
    }
}
