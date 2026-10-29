namespace Tinderhearth.Rules.UI;

/// <summary>资源条与精力管怎么动的那几个量：追与留一截（<see cref="GaugeChaseSettings"/>），加中段挪多快。</summary>
/// <remarks>
/// 都由作者在检查器里调，规则层不持有它们的值，填多少实机试。两个挪的速度单位是界面像素每秒，不大于零就抛：
/// 速度为零时它永远不动，而那不报错。
/// </remarks>
public sealed record GaugeMotionSettings
{
    /// <summary>核三样，不对就抛。</summary>
    /// <param name="chase">回复时追多快、扣减那一截停多久。</param>
    /// <param name="flowPixelsPerSecond">横条往上追的时候中段往右流多快，界面像素每秒。</param>
    /// <param name="bubblePixelsPerSecond">精力管液面中段往上浮多快，界面像素每秒。</param>
    public GaugeMotionSettings(GaugeChaseSettings chase, double flowPixelsPerSecond, double bubblePixelsPerSecond)
    {
        Chase = chase;
        FlowPixelsPerSecond = Positive(flowPixelsPerSecond, nameof(flowPixelsPerSecond), "横条中段流的速度");
        BubblePixelsPerSecond = Positive(bubblePixelsPerSecond, nameof(bubblePixelsPerSecond), "精力管气泡浮的速度");
    }

    /// <summary>回复时追多快、扣减那一截停多久。</summary>
    public GaugeChaseSettings Chase { get; }

    /// <summary>横条往上追的时候中段往右流多快，界面像素每秒。</summary>
    public double FlowPixelsPerSecond { get; }

    /// <summary>精力管液面中段往上浮多快，界面像素每秒。</summary>
    public double BubblePixelsPerSecond { get; }

    private static double Positive(double value, string name, string what) =>
        double.IsFinite(value) && value > 0
            ? value
            : throw new ArgumentOutOfRangeException(name, value, $"{what}要大于零，实际是 {value}");
}
