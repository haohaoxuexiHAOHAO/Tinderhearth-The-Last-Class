namespace Tinderhearth.Rules.UI;

/// <summary>即时提示那一行：一个动作被拒时说清为什么。一次只有一条，新的一条顶掉旧的；停够那么久就收起。</summary>
/// <remarks>
/// 呈现形状在设计仓 design/界面系统.md 的「即时提示那一行：被拒那一下当场说清，一条，不排队」一节，本类不复述理由，
/// 只落那几条：它不接管输入，所以这里没有任何与暂停或导航栈有关的东西；不排队，新的一条进来，旧的那一条就没了、
/// 停留从头算，所以这里只有「现在那一行」，没有一个队列可读。
///
/// 停多久由作者在检查器里填，值归设计仓 design/数值模型.md 那张尚未给值的表；本类只拒绝不是正数的那一种。
/// </remarks>
public sealed class InstantNotice
{
    private double _left;

    /// <param name="secondsShown">一条停多久，秒。必须是有限的正数：零就是出了等于没出，而那不报错。</param>
    public InstantNotice(double secondsShown)
    {
        if (!double.IsFinite(secondsShown) || secondsShown <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(secondsShown), secondsShown, "一条提示停多久要是一个大于零的秒数");
        }

        SecondsShown = secondsShown;
    }

    /// <summary>一条停多久，秒。</summary>
    public double SecondsShown { get; }

    /// <summary>现在显示的那一行；没有在显示的时候是 <c>null</c>。</summary>
    public string? Line { get; private set; }

    /// <summary>出一条。正在显示的那一条被顶掉，停留从头算；同一句再出一次也从头算。</summary>
    public void Show(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);

        Line = line;
        _left = SecondsShown;
    }

    /// <summary>过了这么久，秒。停够了就收起；没有在显示的时候什么都不做。</summary>
    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "过去的时间要是一个不为负的秒数");
        }

        if (Line is null)
        {
            return;
        }

        _left -= seconds;
        if (_left <= 0)
        {
            Line = null;
            _left = 0;
        }
    }
}
