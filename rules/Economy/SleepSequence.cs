namespace Tinderhearth.Rules.Economy;

/// <summary>从睡下到醒来那一段走到了哪儿。</summary>
public enum SleepPhase
{
    /// <summary>醒着：收输入，睡得下。</summary>
    Awake = 1,

    /// <summary>淡出在播：睡下去了，还没结算。</summary>
    FadingOut,

    /// <summary>结算完了，淡入在播。</summary>
    FadingIn,
}

/// <summary>睡下到醒来那一段：这一段里第二次推进被拒、玩家输入关着；淡入播完、弹窗弹出那一刻交回。</summary>
/// <remarks>
/// 结算本身是同步的，夹在淡出播完与淡入开始之间，所以没有「结算中」这一段。弹窗弹出之后由暂停接手：世界停着，
/// 玩法节点不跑，按什么都推不动一天（设计仓 design/每日结算系统.md 的「结算期间没有输入入口」一节）。
///
/// 次序错了当场抛：那是接线的缺陷，悄悄认下来的话，下一次睡就可能被永远挡在外面。
/// </remarks>
public sealed class SleepSequence
{
    /// <summary>走到了哪儿。</summary>
    public SleepPhase Phase { get; private set; } = SleepPhase.Awake;

    /// <summary>这一段里玩家输入关着：睡下那一刻关上，淡入播完那一刻交回。</summary>
    public bool InputClosed => Phase != SleepPhase.Awake;

    /// <summary>睡下去。已经在这一段里时被拒。</summary>
    /// <returns>睡下了为 <c>true</c>；这一段还没走完，这一下不算，为 <c>false</c>。</returns>
    public bool TryBegin()
    {
        if (Phase != SleepPhase.Awake)
        {
            return false;
        }

        Phase = SleepPhase.FadingOut;
        return true;
    }

    /// <summary>淡出播完：接下来结算、播淡入。</summary>
    public void FadeOutFinished()
    {
        Require(SleepPhase.FadingOut, nameof(FadeOutFinished));
        Phase = SleepPhase.FadingIn;
    }

    /// <summary>淡入播完：醒了，调用方随即弹窗。</summary>
    public void FadeInFinished()
    {
        Require(SleepPhase.FadingIn, nameof(FadeInFinished));
        Phase = SleepPhase.Awake;
    }

    /// <summary>中途出了缺陷时回到醒着，免得输入永远关着。</summary>
    public void Abort() => Phase = SleepPhase.Awake;

    private void Require(SleepPhase expected, string step)
    {
        if (Phase != expected)
        {
            throw new InvalidOperationException($"{step} 只该在 {expected} 那一段里来，现在是 {Phase}");
        }
    }
}
