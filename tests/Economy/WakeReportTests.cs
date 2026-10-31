using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;
using Xunit;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>醒来那一刻弹的窗写什么：简版摘要、「这一天没有推进」那一个，以及完整明细。</summary>
/// <remarks>
/// 挑哪几句、填哪几个数用一份假文本表测，每一条的写法一眼认得出是哪个键；措辞不在这里钉，作者改字不该让这里红。
/// 真文本表只核一件事：要的那几条一条不缺。
/// </remarks>
public class WakeReportTests
{
    private static readonly GameDate Day3 = new(1, Season.Spring, 3);
    private const string SavePath = "D:/saves/slot_01.json";

    /// <summary>芜菁的名字那一条。作物的名字读它产出的那一样物品，由调用方交给完整明细。</summary>
    private const string TurnipNameKey = "item.turnip.name";

    private static readonly TextCatalog Fake = new(new Dictionary<string, string>
    {
        [WakeReport.DateKey] = "date:{0}/{1}/{2}",
        ["season.spring"] = "春",
        [WakeReport.WitheredKey] = "withered:{0}",
        [WakeReport.MutatedKey] = "mutated:{0}",
        [WakeReport.NothingKey] = "nothing",
        [WakeReport.SaveFailedKey] = "save_failed",
        [WakeReport.StepFailedKey] = "step_failed:{0}",
        [WakeReport.ReasonKey] = "reason:{0}",
        [WakeReport.SavePathKey] = "path:{0}",
        [WakeReport.RetryKey] = "retry",
        [WakeReport.DetailWitheredKey] = "dw:{0}:{1}",
        [WakeReport.DetailMutatedKey] = "dm:{0}:{1}",
        [TurnipNameKey] = "芜菁",
    });

    private static WakeReport Woke(params CropChange[] changes) =>
        WakeReport.For(SettlementOutcome.Done(Day3, new SettlementSummary(Day3, changes)), Fake, SavePath);

    private static CropChange Change(int x, CropChangeKind kind) => new(new GridCell(x, 0), "turnip", kind);

    [Fact]
    public void 文本表里有这两个窗与完整明细要的每一条()
    {
        var path = Path.Combine(RepoFiles.Root(), "data", "text", "zh-CN.json");
        var table = new TextCatalog(ContentJson.Parse<Dictionary<string, string>>(File.ReadAllText(path), path));

        Assert.Empty(table.MissingKeys(WakeReport.TextKeys));
    }

    [Fact]
    public void 简版最上面一行是结算的那一天_日期取自摘要里那一个()
    {
        var report = Woke();

        Assert.True(report.Advanced);
        Assert.Equal("date:1/春/3", report.Title);
    }

    [Fact]
    public void 简版每个有变化的项一行_枯死在变异前面()
    {
        var report = Woke(Change(0, CropChangeKind.Mutated), Change(1, CropChangeKind.Withered), Change(2, CropChangeKind.Withered));

        Assert.Equal(["withered:2", "mutated:1"], report.Lines);
    }

    [Fact]
    public void 简版里没有变化的项不列()
    {
        var report = Woke(Change(0, CropChangeKind.Mutated));

        Assert.Equal(["mutated:1"], report.Lines);
    }

    [Fact]
    public void 一项变化都没有的那天照弹_只有一行没有要紧的变化()
    {
        var report = Woke();

        Assert.Equal(["nothing"], report.Lines);
    }

    [Fact]
    public void 停在自动存档那一步_第一句说存档没写上_后面跟原因存档路径与再睡一次()
    {
        var outcome = SettlementOutcome.NotAdvanced(Day3, SettlementSteps.AutoSave, "写不进去");

        var report = WakeReport.For(outcome, Fake, SavePath);

        Assert.False(report.Advanced);
        Assert.Equal("save_failed", report.Title);
        Assert.Equal(["reason:写不进去", $"path:{SavePath}", "retry"], report.Lines);
    }

    [Fact]
    public void 停在别的步_第一句点名那一步_不写存档路径()
    {
        var outcome = SettlementOutcome.NotAdvanced(Day3, SettlementSteps.CropGrowth, "找不到作物定义");

        var report = WakeReport.For(outcome, Fake, SavePath);

        Assert.Equal($"step_failed:{SettlementSteps.CropGrowth}", report.Title);
        Assert.Equal(["reason:找不到作物定义", "retry"], report.Lines);
    }

    [Fact]
    public void 完整明细每一格一行_写哪一格哪种作物枯死还是变异_按记下的先后()
    {
        var summary = new SettlementSummary(Day3, [Change(0, CropChangeKind.Mutated), Change(1, CropChangeKind.Withered)]);

        var lines = WakeReport.DetailLines(summary, Fake, cropId => cropId == "turnip" ? TurnipNameKey : "");

        Assert.Equal(["dm:(0, 0):芜菁", "dw:(1, 0):芜菁"], lines);
    }

    [Fact]
    public void 文本表里那一条的占位写错了_报出是哪一条()
    {
        var broken = new TextCatalog(new Dictionary<string, string> { [WakeReport.WitheredKey] = "withered:{1}" });
        var outcome = SettlementOutcome.Done(Day3, new SettlementSummary(Day3, [Change(0, CropChangeKind.Withered)]));

        var e = Assert.Throws<InvalidDataException>(() => WakeReport.For(outcome, broken, SavePath));

        Assert.Contains(WakeReport.WitheredKey, e.Message);
    }
}
