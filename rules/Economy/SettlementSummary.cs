namespace Tinderhearth.Rules.Economy;

/// <summary>结算的那一天的摘要：哪一天，这一夜每一格出了什么事。简版弹窗与完整明细读的都是这一份。</summary>
/// <remarks>
/// 它是各步结果的读口，不是第二份账：从这一天记下的变化算出来，不另存（设计仓 design/每日结算系统.md 的
/// 「摘要：简版是弹窗，完整明细是一个读口」一节）。它也不进存档。
/// </remarks>
/// <param name="SettledDate">结算的是哪一天：推进之前的那一天。</param>
/// <param name="CropChanges">这一夜各格地上的变化，按记下的先后。</param>
public sealed record SettlementSummary(GameDate SettledDate, IReadOnlyList<CropChange> CropChanges)
{
    /// <summary>这一夜枯死了几格。</summary>
    public int WitheredCount => CropChanges.Count(c => c.Kind == CropChangeKind.Withered);

    /// <summary>这一夜变异了几格。</summary>
    public int MutatedCount => CropChanges.Count(c => c.Kind == CropChangeKind.Mutated);

    /// <summary>这一夜有没有要紧的变化。没有的那天照样有一份摘要，只是没有项。</summary>
    public bool HasChanges => CropChanges.Count > 0;
}

/// <summary>推进一天的结果：推进了没有；推进了就带着摘要，没推进就带着停在哪一步、为什么。</summary>
/// <param name="Advanced">这一天推进了没有。</param>
/// <param name="SettledDate">结算的是哪一天。没推进时，现在仍是这一天。</param>
/// <param name="Summary">推进了的那一天的摘要；没推进时为 <c>null</c>。</param>
/// <param name="FailedStep">没推进时停在哪一步；推进了为 <c>null</c>。</param>
/// <param name="Reason">没推进的原因，原样带着出错那一下的说法；推进了为 <c>null</c>。</param>
public sealed record SettlementOutcome(
    bool Advanced, GameDate SettledDate, SettlementSummary? Summary, string? FailedStep, string? Reason)
{
    /// <summary>推进了。</summary>
    public static SettlementOutcome Done(GameDate settledDate, SettlementSummary? summary) =>
        new(Advanced: true, SettledDate: settledDate, Summary: summary, FailedStep: null, Reason: null);

    /// <summary>这一天没有推进。</summary>
    public static SettlementOutcome NotAdvanced(GameDate settledDate, string failedStep, string reason) =>
        new(Advanced: false, SettledDate: settledDate, Summary: null, FailedStep: failedStep, Reason: reason);
}
