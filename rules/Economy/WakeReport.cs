using System.Globalization;
using Tinderhearth.Rules.Foundation.Text;

namespace Tinderhearth.Rules.Economy;

/// <summary>醒来那一刻弹的那一个窗写什么：最上面一行，下面一行一项。</summary>
/// <remarks>
/// 推进了就是简版摘要：最上面一行写结算的是哪一天，下面每个有变化的项一行，项名照正典那份清单的写法；一项变化
/// 都没有时只有一行「没有要紧的变化」。没推进就是「这一天没有推进」那一个：第一句就说没有推进，后面跟原因、存档
/// 路径与「处理好之后再睡一次」。字全在文本表里，这里只挑哪几句、填进哪几个数。
///
/// 弹窗只把那句话说出来，不自己判断：推进了没有、停在哪一步、为什么，全是结算交出来的
/// （设计仓 design/界面系统.md 的「主菜单那一侧：读档位列表、更新公告、设置」一节里「存档写入失败」那一段）。
/// </remarks>
/// <param name="Advanced">这一天推进了没有。</param>
/// <param name="Title">最上面那一行。</param>
/// <param name="Lines">下面那几行，一行一项。</param>
public sealed record WakeReport(bool Advanced, string Title, IReadOnlyList<string> Lines)
{
    /// <summary>日期的写法，依次填年、季节名、这一季的第几天。</summary>
    public const string DateKey = "ui.wake.date";

    /// <summary>简版里枯死那一项，填格数。</summary>
    public const string WitheredKey = "ui.wake.withered";

    /// <summary>简版里变异那一项，填格数。</summary>
    public const string MutatedKey = "ui.wake.mutated";

    /// <summary>一项变化都没有的那天，简版里唯一那一行。</summary>
    public const string NothingKey = "ui.wake.nothing";

    /// <summary>停在自动存档那一步时的第一句。</summary>
    public const string SaveFailedKey = "ui.wake.save_failed";

    /// <summary>停在别的步时的第一句，填步名。</summary>
    public const string StepFailedKey = "ui.wake.step_failed";

    /// <summary>原因那一行，填结算交出来的那句原因。</summary>
    public const string ReasonKey = "ui.wake.reason";

    /// <summary>存档路径那一行，填存档文件的绝对路径。</summary>
    public const string SavePathKey = "ui.wake.save_path";

    /// <summary>「这一天没有推进」那个窗的最后一行。</summary>
    public const string RetryKey = "ui.wake.retry";

    /// <summary>完整明细里枯死的一格，依次填哪一格、作物名。</summary>
    public const string DetailWitheredKey = "ui.wake.detail.withered";

    /// <summary>完整明细里变异的一格，依次填哪一格、作物名。</summary>
    public const string DetailMutatedKey = "ui.wake.detail.mutated";

    /// <summary>窗上那个按钮在最后一页时的字：按下去关窗。</summary>
    public const string CloseKey = "ui.wake.close";

    /// <summary>窗上那个按钮在后面还有页时的字：按下去翻页。</summary>
    public const string NextPageKey = "ui.wake.next_page";

    /// <summary>醒来那一刻要弹的那一份。</summary>
    /// <param name="outcome">推进一天的结果。</param>
    /// <param name="text">文本表。</param>
    /// <param name="savePath">存档文件的绝对路径，停在自动存档那一步时写进去。</param>
    public static WakeReport For(SettlementOutcome outcome, TextCatalog text, string savePath)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(savePath);

        if (outcome.Advanced)
        {
            var summary = outcome.Summary
                ?? throw new ArgumentException("推进了的那一天没带摘要：醒来那一步之前就该有它", nameof(outcome));
            return Summary(summary, text);
        }

        var step = outcome.FailedStep
            ?? throw new ArgumentException("没推进的结果没写停在哪一步", nameof(outcome));
        var savedFailed = step == SettlementSteps.AutoSave;

        List<string> lines = [Format(text, ReasonKey, outcome.Reason ?? "")];
        if (savedFailed)
        {
            lines.Add(Format(text, SavePathKey, savePath));
        }

        lines.Add(text[RetryKey]);
        return new WakeReport(
            Advanced: false,
            Title: savedFailed ? text[SaveFailedKey] : Format(text, StepFailedKey, step),
            Lines: lines);
    }

    /// <summary>完整明细：每一格一行，写哪一格、哪种作物、枯死还是变异，按记下的先后。</summary>
    /// <remarks>与简版读的是同一份摘要，不另算。手环那一页还没有，归设计仓台账 UI-20，现在打在输出面板上。</remarks>
    /// <param name="summary">结算交出来的摘要。</param>
    /// <param name="text">文本表。</param>
    /// <param name="cropNameKeyOf">
    /// 一种作物的名字在文本表里是哪一条：作物没有自己的名字，读它产出的那一样物品（<see cref="CropDefinition.NameKeyIn"/>）。
    /// </param>
    public static IReadOnlyList<string> DetailLines(
        SettlementSummary summary, TextCatalog text, Func<string, string> cropNameKeyOf)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(cropNameKeyOf);

        return [.. summary.CropChanges.Select(change => Format(
            text,
            change.Kind == CropChangeKind.Mutated ? DetailMutatedKey : DetailWitheredKey,
            change.Cell,
            text[cropNameKeyOf(change.CropId)]))];
    }

    /// <summary>一个日期写成文本表里的那种写法。</summary>
    public static string DateText(GameDate date, TextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Format(text, DateKey, date.Year, text[SeasonKey(date.Season)], date.Day);
    }

    /// <summary>季节名在文本表里的那一条。</summary>
    public static string SeasonKey(Season season) => season switch
    {
        Season.Spring => "season.spring",
        Season.Summer => "season.summer",
        Season.Autumn => "season.autumn",
        Season.Winter => "season.winter",
        _ => throw new ArgumentOutOfRangeException(nameof(season), season, "这个季节在文本表里没有名字那一条"),
    };

    /// <summary>这两个窗与完整明细要在文本表里有的那几条，季节名一季一条。启动时核一次，测试也核一次。</summary>
    public static IReadOnlyList<string> TextKeys =>
    [
        DateKey, WitheredKey, MutatedKey, NothingKey,
        SaveFailedKey, StepFailedKey, ReasonKey, SavePathKey, RetryKey,
        DetailWitheredKey, DetailMutatedKey, CloseKey, NextPageKey,
        .. Calendar.SeasonOrder.Select(SeasonKey),
    ];

    private static WakeReport Summary(SettlementSummary summary, TextCatalog text)
    {
        // 先后照正典那份清单：作物枯死在作物变异前面。
        List<string> lines = [];
        if (summary.WitheredCount > 0)
        {
            lines.Add(Format(text, WitheredKey, summary.WitheredCount));
        }

        if (summary.MutatedCount > 0)
        {
            lines.Add(Format(text, MutatedKey, summary.MutatedCount));
        }

        if (lines.Count == 0)
        {
            lines.Add(text[NothingKey]);
        }

        return new WakeReport(Advanced: true, Title: DateText(summary.SettledDate, text), Lines: lines);
    }

    /// <summary>按文本表里那一条的写法填数。那一条的占位写错了是内容的错，报出是哪一条。</summary>
    private static string Format(TextCatalog text, string key, params object[] values)
    {
        var template = text[key];
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, values);
        }
        catch (FormatException e)
        {
            throw new InvalidDataException(
                $"文本表里「{key}」那一条的占位写错了（「{template}」，要填 {values.Length} 个）：{e.Message}", e);
        }
    }
}
