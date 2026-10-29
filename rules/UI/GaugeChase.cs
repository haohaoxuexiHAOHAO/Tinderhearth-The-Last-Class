namespace Tinderhearth.Rules.UI;

/// <summary>资源条显示的长度怎么动的两个量：朝数值走多快（界面像素每秒），扣减时掉下去那一截停多久（秒）。</summary>
/// <remarks>
/// 两个量都由作者在检查器里调，规则层不持有它们的值，填多少实机试。不大于零就抛：速度为零时条永远到不了数值，
/// 而那不报错。
/// </remarks>
public sealed record GaugeChaseSettings
{
    /// <summary>核两个量，不大于零直接抛。</summary>
    /// <param name="pixelsPerSecond">显示的长度朝数值走多快，界面像素每秒。回复时填充按它追，那一截停够之后按它缩。</param>
    /// <param name="holdSeconds">扣减时掉下去那一截留多久再缩，秒。</param>
    public GaugeChaseSettings(double pixelsPerSecond, double holdSeconds)
    {
        PixelsPerSecond = Positive(pixelsPerSecond, nameof(pixelsPerSecond), "追的速度");
        HoldSeconds = Positive(holdSeconds, nameof(holdSeconds), "那一截停的时长");
    }

    /// <summary>显示的长度朝数值走多快，界面像素每秒。</summary>
    public double PixelsPerSecond { get; }

    /// <summary>扣减时掉下去那一截留多久再缩，秒。</summary>
    public double HoldSeconds { get; }

    private static double Positive(double value, string name, string what) =>
        double.IsFinite(value) && value > 0
            ? value
            : throw new ArgumentOutOfRangeException(name, value, $"资源条{what}要大于零，实际是 {value}");
}

/// <summary>
/// 一根资源条显示出来的两个长度：填充画多长，扣减时浅色那一截画到哪。回复时填充按整像素逐帧追上数值；扣减时填充
/// 当场落下去，那一截留在原处，停一段再缩回数值。
/// </summary>
/// <remarks>
/// 长度是界面像素、整数，从放填充那一点量起；时间是秒。那一截不比填充短，它比填充长出来的那一段就是这几下一共掉了多少；
/// 填充当场落下去，是因为慢慢落的话这一下的轻重读不出来。
///
/// 每挨一下，那一截停的时间从这一下算起：连着挨几下，它留到最后一下之后才缩，这一串一共掉了多少看得全。缩的过程中又挨
/// 一下，它停在当时的位置、不跳回原来那么长。回复到它之上它就没了：它说的是掉了多少，回到那么多之后它没有意思。
/// </remarks>
public sealed class GaugeChase
{
    private readonly GaugeChaseSettings _settings;

    // 不到一像素的那点攒着，攒够一像素才走，所以慢的时候也是整像素地走。
    private double _fillCarry;
    private double _lagCarry;

    // 那一截还要停多久，秒。
    private double _hold;

    /// <summary>从一个长度开始，不在动。</summary>
    /// <param name="settings">追多快、停多久。</param>
    /// <param name="length">开始时的长度，界面像素。</param>
    public GaugeChase(GaugeChaseSettings settings, int length)
    {
        _settings = settings;
        Target = Fill = Lag = RequireLength(length);
    }

    /// <summary>数值对应的长度，界面像素。</summary>
    public int Target { get; private set; }

    /// <summary>填充现在画多长，界面像素。</summary>
    public int Fill { get; private set; }

    /// <summary>浅色那一截现在画到哪，界面像素。不比 <see cref="Fill"/> 短，一样长就是没有那一截。</summary>
    public int Lag { get; private set; }

    /// <summary>填充正朝数值往上追。</summary>
    public bool Rising => Fill < Target;

    /// <summary>填充与那一截都停在数值上，不用再走。</summary>
    public bool Settled => Fill == Target && Lag == Target;

    /// <summary>数值变了，换成它对应的长度。</summary>
    public void Retarget(int length)
    {
        RequireLength(length);
        if (length < Fill)
        {
            // 那一截从现在显示的位置留下，不跳回去；停的时间从这一下算起。
            Lag = Math.Max(Lag, Fill);
            Fill = length;
            _fillCarry = 0;
            _lagCarry = 0;
            _hold = _settings.HoldSeconds;
        }
        else if (length >= Lag)
        {
            Lag = Fill;
            _lagCarry = 0;
            _hold = 0;
        }

        Target = length;
    }

    /// <summary>过了这么多秒：填充往上追，那一截停够了往回缩。都是整像素、都不越过数值。</summary>
    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, $"过去的时间不能是负数，实际是 {seconds}");
        }

        if (Fill < Target)
        {
            Fill = Toward(Fill, Target, seconds, ref _fillCarry);
        }

        Lag = Math.Max(Lag, Fill);
        if (Lag <= Target)
        {
            return;
        }

        var held = Math.Min(_hold, seconds);
        _hold -= held;
        if (seconds > held)
        {
            Lag = Toward(Lag, Target, seconds - held, ref _lagCarry);
        }
    }

    // 从 from 朝 to 走 seconds 秒，整像素、不越过 to；不到一像素的那点留在 carry 里，走到了就清掉。
    private int Toward(int from, int to, double seconds, ref double carry)
    {
        carry += _settings.PixelsPerSecond * seconds;
        var steps = (int)Math.Min(Math.Floor(carry), Math.Abs(to - from));
        carry -= steps;
        var next = from + (Math.Sign(to - from) * steps);
        if (next == to)
        {
            carry = 0;
        }

        return next;
    }

    private static int RequireLength(int length) =>
        length >= 0
            ? length
            : throw new ArgumentOutOfRangeException(nameof(length), length, $"资源条的长度不能是负数，实际是 {length}");
}
