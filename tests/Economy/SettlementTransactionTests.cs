using Tinderhearth.Rules.Economy;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>整串是一次事务：在副本上跑，写档成功那一刻才换上去；摘要在提交之前产出、提交之后交出。</summary>
/// <remarks>
/// 规则在设计仓 design/每日结算系统.md 的「整串加自动存档是一次事务」一节。用的那片地是换季那天，好让日期、
/// 换季与作物三步都真的改了副本：跑一半的副本若漏进当前那一份，比一比就看得出来。
/// </remarks>
public class SettlementTransactionTests
{
    private const int FallowDays = 7;

    private static readonly GameDate SeasonEnd = new(1, Season.Spring, Calendar.DaysPerSeason);

    private static IReadOnlyDictionary<string, CropDefinition> Crops => DailySettlementTests.FieldCrops;

    private static IEnumerable<(string Name, SettlementStep Run)> Steps(
        Action<WorldState> writeSave, Action<SettlementSummary> onWake) =>
        StandardSettlement.Steps(Crops, FallowDays, () => true, writeSave, onWake);

    private static SettlementOutcome EndDay(
        CurrentWorld world, IEnumerable<(string Name, SettlementStep Run)> steps) =>
        new DailySettlement(steps, _ => { }).EndDay(world, DayEndSource.Sleep);

    [Theory]
    [InlineData(SettlementSteps.DateAdvance)]
    [InlineData(SettlementSteps.SeasonChange)]
    [InlineData(SettlementSteps.CropGrowth)]
    [InlineData(SettlementSteps.AssignmentOutput)]
    [InlineData(SettlementSteps.ProcessingProgress)]
    [InlineData(SettlementSteps.ConstructionProgress)]
    [InlineData(SettlementSteps.ReadingProgress)]
    [InlineData(SettlementSteps.StudentRecovery)]
    [InlineData(SettlementSteps.ExamSettlement)]
    [InlineData(SettlementSteps.DebtInterest)]
    [InlineData(SettlementSteps.QuestExpiry)]
    [InlineData(SettlementSteps.QuestBoardRefill)]
    [InlineData(SettlementSteps.EnergyCapRecompute)]
    [InlineData(SettlementSteps.EnergyRestore)]
    [InlineData(SettlementSteps.Summary)]
    public void 写档之前任何一步出错这一天没有推进(string failing)
    {
        var world = new CurrentWorld(DailySettlementTests.FieldOn(SeasonEnd));
        var before = world.State;
        var snapshot = PlotSnapshot.Of(before);
        var saves = 0;
        var wakes = 0;
        var steps = Steps(_ => saves++, _ => wakes++).Select(s => s.Name == failing
            ? (s.Name, (SettlementStep)(_ => throw new InvalidOperationException("故意让这一步出错")))
            : s);

        var outcome = EndDay(world, steps);

        Assert.False(outcome.Advanced);
        Assert.Equal(failing, outcome.FailedStep);
        Assert.Contains("故意让这一步出错", outcome.Reason);
        Assert.Same(before, world.State);
        Assert.Equal(SeasonEnd, world.State.Date);
        Assert.Equal(snapshot, PlotSnapshot.Of(world.State));
        Assert.Equal(0, saves);
        Assert.Equal(0, wakes);
    }

    [Fact]
    public void 写档失败这一天没有推进并说清原因_摘要一次都不交出()
    {
        var world = new CurrentWorld(DailySettlementTests.FieldOn(SeasonEnd));
        var before = world.State;
        var snapshot = PlotSnapshot.Of(before);
        var wakes = 0;

        var outcome = EndDay(world, Steps(_ => throw new UnauthorizedAccessException("存档文件拒绝写入"), _ => wakes++));

        Assert.False(outcome.Advanced);
        Assert.Equal(SettlementSteps.AutoSave, outcome.FailedStep);
        Assert.Contains("存档文件拒绝写入", outcome.Reason);
        Assert.Null(outcome.Summary);
        Assert.Same(before, world.State);
        Assert.Equal(snapshot, PlotSnapshot.Of(world.State));
        Assert.Equal(0, wakes);
    }

    [Fact]
    public void 写档成功之后世界换成推进过的那一份_写出去的正是它()
    {
        var world = new CurrentWorld(DailySettlementTests.FieldOn(SeasonEnd));
        var before = world.State;
        WorldState? written = null;

        var outcome = EndDay(world, Steps(w => written = w, _ => { }));

        Assert.True(outcome.Advanced);
        Assert.NotNull(written);
        Assert.Same(written, world.State);
        Assert.NotSame(before, world.State);
        Assert.Equal(SeasonEnd.Next(), world.State.Date);
        Assert.Equal(SeasonEnd, before.Date);
    }

    /// <summary>摘要在写档之前就已经在了，交出去却在写档之后；摘要那一步之前它还不存在。</summary>
    [Fact]
    public void 摘要在生成结算摘要那一步产出_在醒来那一步交出()
    {
        var world = new CurrentWorld(DailySettlementTests.FieldOn(SeasonEnd));
        var order = new List<string>();
        var steps = Steps(_ => order.Add("写档"), _ => order.Add("交出摘要")).Select(s => s.Name switch
        {
            SettlementSteps.EnergyRestore => (s.Name, (SettlementStep)(day => order.Add(day.Summary is null ? "摘要之前还没有" : "摘要之前已经有了"))),
            SettlementSteps.AutoSave => (s.Name, (SettlementStep)(day =>
            {
                order.Add(day.Summary is null ? "存档时还没有摘要" : "存档时已有摘要");
                s.Run(day);
            })),
            _ => s,
        });
        string[] expected = ["摘要之前还没有", "存档时已有摘要", "写档", "交出摘要"];

        EndDay(world, steps);

        Assert.Equal(expected, order);
    }

    [Fact]
    public void 摘要写着结算的是哪一天与这一夜每一格的变化()
    {
        var world = new CurrentWorld(DailySettlementTests.FieldOn(SeasonEnd));
        var delivered = new List<SettlementSummary>();

        var outcome = EndDay(world, Steps(_ => { }, delivered.Add));

        var summary = Assert.Single(delivered);
        Assert.Same(summary, outcome.Summary);
        Assert.Equal(SeasonEnd, summary.SettledDate);
        Assert.Equal(1, summary.WitheredCount);
        Assert.Equal(1, summary.MutatedCount);
        Assert.True(summary.HasChanges);
        Assert.Contains(summary.CropChanges, c => c is { Kind: CropChangeKind.Withered, Cell.X: 0, CropId: "spring_only" });
        Assert.Contains(summary.CropChanges, c => c is { Kind: CropChangeKind.Mutated, Cell.X: 1, CropId: "spring_summer" });
    }

    [Fact]
    public void 一项变化都没有的那天也交出一份摘要_只是没有项()
    {
        var quiet = new WorldState(
            new GameDate(1, Season.Spring, 3), PlotGrid.Rectangle(new GridCell(0, 0), 2, 1, PlotState.Tilled), TestBags.Empty());
        var world = new CurrentWorld(quiet);
        var delivered = new List<SettlementSummary>();

        EndDay(world, Steps(_ => { }, delivered.Add));

        var summary = Assert.Single(delivered);
        Assert.False(summary.HasChanges);
        Assert.Equal(new GameDate(1, Season.Spring, 3), summary.SettledDate);
    }

    /// <summary>提交之后出错不是回滚：存档已经写上、世界已经换过，接住它只会让人以为这一天没过。</summary>
    [Fact]
    public void 提交之后那一步出错照常抛出去_世界已经换过()
    {
        var world = new CurrentWorld(DailySettlementTests.FieldOn(SeasonEnd));
        WorldState? written = null;

        Assert.Throws<InvalidOperationException>(
            () => EndDay(world, Steps(w => written = w, _ => throw new InvalidOperationException("弹窗那一侧出错"))));

        Assert.Same(written, world.State);
    }
}
