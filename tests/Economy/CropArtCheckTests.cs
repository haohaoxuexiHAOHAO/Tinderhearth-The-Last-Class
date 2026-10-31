using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>作物素材载入时那一次核对：缺哪几张、配给了谁、哪几种没有素材，一次全报。</summary>
/// <remarks>素材本身在引擎层，这里交进去的是「每一格配没配」，所以不用引擎就测得到。</remarks>
public class CropArtCheckTests
{
    private static CropDefinition Crop(string id) => new()
    {
        Id = id,
        StageDays = [1, 1, 1],
        RegrowFromStage = null,
        Seasons = [Season.Spring],
        SeedItemId = $"{id}_seed",
        YieldItemId = $"{id}_harvest",
    };

    /// <summary>配齐的一份：阶段数照那条作物声明的来，变异成熟与枯死都有。</summary>
    private static CropArtEntry Full(CropDefinition crop) =>
        new(crop.Id, [.. Enumerable.Repeat(true, crop.StageCount)], HasMutatedRipe: true, HasWithered: true);

    [Fact]
    public void 全部配齐时没有问题()
    {
        var crops = new[] { Crop("crop_01"), Crop("crop_02") };

        Assert.Empty(CropArtCheck.Problems(crops, [.. crops.Select(Full)]));
    }

    /// <summary>几种作物各错一处、再加一种没有素材，一次全报出来，不停在第一处。</summary>
    [Fact]
    public void 几处不对一次全报()
    {
        var a = Crop("crop_01");
        var b = Crop("crop_02");
        var c = Crop("crop_03");
        var arts = new CropArtEntry?[]
        {
            Full(a) with { HasMutatedRipe = false },
            Full(b) with { HasWithered = false },
        };

        var problems = CropArtCheck.Problems([a, b, c], arts);

        Assert.Equal(3, problems.Count);
        Assert.Contains("crop_01", problems[0]);
        Assert.Contains("变异成熟", problems[0]);
        Assert.Contains("crop_02", problems[1]);
        Assert.Contains("枯死", problems[1]);
        Assert.Contains("crop_03", problems[2]);
        Assert.Contains("没有素材", problems[2]);
    }

    /// <summary>一份素材里几格同时空着，每一格各报一条。</summary>
    [Fact]
    public void 同一份里缺的几格各报一条()
    {
        var crop = Crop("crop_01");
        var art = new CropArtEntry(crop.Id, [true, false, true, false], HasMutatedRipe: false, HasWithered: false);

        var problems = CropArtCheck.Problems([crop], [art]);

        Assert.Equal(4, problems.Count);
        Assert.Contains("第 1 阶段", problems[0]);
        Assert.Contains("第 3 阶段", problems[1]);
        Assert.Contains("变异成熟", problems[2]);
        Assert.Contains("枯死", problems[3]);
    }

    [Fact]
    public void 生长阶段的张数与声明的阶段数对不上就报()
    {
        var crop = Crop("crop_01");
        var art = Full(crop) with { StagesFilled = [true, true, true] };

        var problem = Assert.Single(CropArtCheck.Problems([crop], [art]));

        Assert.Contains($"要有 {crop.StageCount} 张，实际 3 张", problem);
    }

    /// <summary>复制一份忘了改标识：两份配给同一种，单独报一条，说出是哪两项。</summary>
    /// <remarks>不报的话后一份顶掉前一份，表现只是另一种作物「有定义但没有素材」。</remarks>
    [Fact]
    public void 两份配给同一种作物时点名是哪两项()
    {
        var a = Crop("crop_01");
        var b = Crop("crop_02");

        var problems = CropArtCheck.Problems([a, b], [Full(a), Full(a)]);

        Assert.Equal(2, problems.Count);
        Assert.Contains("第 0 项与第 1 项", problems[0]);
        Assert.Contains("crop_01", problems[0]);
        Assert.Contains("crop_02", problems[1]);
        Assert.Contains("没有素材", problems[1]);
    }

    [Fact]
    public void 配给不存在的作物与空着的那一项都报()
    {
        var crop = Crop("crop_01");
        var arts = new CropArtEntry?[] { Full(crop), null, Full(crop) with { CropId = "crop_99" } };

        var problems = CropArtCheck.Problems([crop], arts);

        Assert.Equal(2, problems.Count);
        Assert.Contains("第 1 项", problems[0]);
        Assert.Contains("crop_99", problems[1]);
        Assert.Contains("没有对应的作物定义", problems[1]);
    }

    /// <summary>没有素材的那几种按标识排，同一份内容每次报得都一样。</summary>
    [Fact]
    public void 没有素材的那几种按标识排()
    {
        var crops = new[] { Crop("crop_03"), Crop("crop_01"), Crop("crop_02") };

        var problem = Assert.Single(CropArtCheck.Problems(crops, []));

        Assert.Contains("crop_01、crop_02、crop_03", problem);
    }
}
