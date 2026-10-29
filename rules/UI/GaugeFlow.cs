namespace Tinderhearth.Rules.UI;

/// <summary>
/// 资源条与精力管中段那一段花纹挪到哪了：按整像素往前挪，挪满一段（中段那么长）回到原样。
/// </summary>
/// <remarks>
/// 只挪作者画的中段，两头不动，所以挪出来的每一张都只有原图里的像素。什么时候挪归调用方：横条只在往上追的时候挪，
/// 精力管一直挪；这里只管挪了多少。不挪的时候停在当时那一格，不跳回原样。
/// </remarks>
public sealed class GaugeFlow
{
    // 不到一像素的那点攒着，攒够一像素才挪。
    private double _carry;

    /// <summary>从原样开始。</summary>
    /// <param name="pixelsPerSecond">挪多快，界面像素每秒，要大于零。</param>
    /// <param name="period">中段多长，像素：挪满这么多就回到原样。至少为一。</param>
    public GaugeFlow(double pixelsPerSecond, int period)
    {
        PixelsPerSecond = double.IsFinite(pixelsPerSecond) && pixelsPerSecond > 0
            ? pixelsPerSecond
            : throw new ArgumentOutOfRangeException(nameof(pixelsPerSecond), pixelsPerSecond,
                $"中段挪的速度要大于零，实际是 {pixelsPerSecond}");
        Period = period >= 1
            ? period
            : throw new ArgumentOutOfRangeException(nameof(period), period, $"中段至少要一像素长，实际是 {period}");
    }

    /// <summary>挪多快，界面像素每秒。</summary>
    public double PixelsPerSecond { get; }

    /// <summary>中段多长，像素。</summary>
    public int Period { get; }

    /// <summary>现在挪了几像素，0 到 <see cref="Period"/> 减一；0 就是原样。</summary>
    public int Offset { get; private set; }

    /// <summary>挪了这么多秒。</summary>
    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, $"过去的时间不能是负数，实际是 {seconds}");
        }

        _carry += PixelsPerSecond * seconds;
        var steps = Math.Floor(_carry);
        _carry -= steps;
        Offset = (int)((Offset + (steps % Period)) % Period);
    }

    /// <summary>
    /// 中段第 <paramref name="index"/> 格（沿流动方向数，从 0 起）在挪了 <paramref name="offset"/> 像素之后，
    /// 取原图中段的第几格。
    /// </summary>
    /// <remarks>原图第 j 格挪完落在第 j 加 offset 格，所以每一格取的是它后面 offset 格的那一格，绕回来接上。</remarks>
    public static int SourceIndex(int index, int offset, int period) =>
        (((index - offset) % period) + period) % period;
}
