using System.Globalization;
using Tinderhearth.Rules.Foundation.Text;
using Xunit;

namespace Tinderhearth.Rules.Tests.Foundation;

/// <summary>改区域设置的用例不与别的用例并行：区域设置有一半是整个进程共用的。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessCultureCollection
{
    public const string Name = "进程区域设置";
}

/// <summary>
/// 界面上的数字照游戏的语言印，不照玩家系统的区域设置印。理由在 <see cref="GameLanguage"/>。
/// </summary>
[Collection(ProcessCultureCollection.Name)]
public class GameLanguageTests
{
    [Fact]
    public void 系统区域的负号是U2212时_钉过之后仍印半角负号与小数点()
    {
        var saved = Snapshot();
        try
        {
            // 照瑞典语那种区域造一份：负号 U+2212、小数点是逗号。自己造而不直接用 sv-SE，
            // 用例就不跟着本机装的区域数据走。
            var swedishLike = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            swedishLike.NumberFormat.NegativeSign = "\u2212";
            swedishLike.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = swedishLike;

            // 不钉就是这个样子 —— 先确认这条用例真的碰到了那个问题。
            Assert.Equal("\u22125", (-5).ToString());
            Assert.Equal("1,5", 1.5.ToString());

            GameLanguage.PinCulture();

            Assert.Equal("-5", (-5).ToString());
            Assert.Equal("1.5", 1.5.ToString());
        }
        finally
        {
            Restore(saved);
        }
    }

    [Fact]
    public void 钉上的区域不带系统里的自定义格式_印数字只用得到字体审计核过的符号()
    {
        var culture = GameLanguage.Culture;
        Assert.False(culture.UseUserOverride);

        var format = culture.NumberFormat;
        string[] symbols =
        [
            format.NegativeSign, format.PositiveSign,
            format.NumberDecimalSeparator, format.NumberGroupSeparator,
            format.PercentSymbol, format.PercentDecimalSeparator, format.PercentGroupSeparator,
        ];
        foreach (var symbol in symbols)
        {
            Assert.All(symbol, ch => Assert.Contains(ch, GameLanguage.NumberSymbols));
        }
    }

    private static (CultureInfo Current, CultureInfo CurrentUI, CultureInfo? Default, CultureInfo? DefaultUI)
        Snapshot() =>
        (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture,
         CultureInfo.DefaultThreadCurrentCulture, CultureInfo.DefaultThreadCurrentUICulture);

    private static void Restore(
        (CultureInfo Current, CultureInfo CurrentUI, CultureInfo? Default, CultureInfo? DefaultUI) saved)
    {
        CultureInfo.CurrentCulture = saved.Current;
        CultureInfo.CurrentUICulture = saved.CurrentUI;
        CultureInfo.DefaultThreadCurrentCulture = saved.Default;
        CultureInfo.DefaultThreadCurrentUICulture = saved.DefaultUI;
    }
}
