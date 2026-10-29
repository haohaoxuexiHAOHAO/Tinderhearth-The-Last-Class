using System.Globalization;

namespace Tinderhearth.Rules.Foundation.Text;

/// <summary>
/// 游戏用哪种语言，以及界面上的数字照这种语言印、不照玩家系统的区域设置印。
/// </summary>
/// <remarks>
/// 不钉的话数字跟着系统的区域设置走。本机 .NET 10 实测：瑞典、芬兰、挪威的区域把 -5 印成「−5」，
/// 那个负号是 U+2212，而选定的像素字体里没有这个字形，玩家看到的是豆腐块；德、法、俄的区域把 1.5
/// 印成「1,5」。来源是 .NET 5 起在 Windows 上改取 ICU 的区域数据。它不报错，开发机是简体中文时
/// 永远看不到 —— 所以启动时钉一次，并由单元测试与设计仓的字体审计各盯一头。
/// </remarks>
public static class GameLanguage
{
    /// <summary>
    /// 游戏的语言。现在只发简体中文（设计仓 decisions/ADR-0008-中文像素字体选型.md）；选语言落地时改成读玩家级偏好，那一侧见
    /// 设计仓 design/存档系统.md 的「玩家级偏好：第三样东西，不是分片」一节。
    /// </summary>
    public const string Tag = "zh-CN";

    /// <summary>这种语言的文本表在内容目录里的相对路径。</summary>
    public static string TextTablePath => $"text/{Tag}.json";

    /// <summary>
    /// 钉上的区域印数字时可能用到的全部字符：十个数字、正负号、小数点与千分位、百分号。
    /// </summary>
    /// <remarks>
    /// 两头各有东西盯着它：单元测试核钉上的区域印数字只用得到这几个；设计仓 tools/audit_fonts.py
    /// 读这一行，对选定字体做全覆盖硬要求。改这一行，两边会一起响。
    /// </remarks>
    public const string NumberSymbols = "0123456789+-.,%";

    /// <summary>游戏语言对应的区域设置：只读、不带玩家在系统里自定义过的格式。</summary>
    /// <remarks>
    /// 取 <see cref="CultureInfo.GetCultureInfo(string)"/> 而不是 <c>new CultureInfo(Tag)</c>：后者在
    /// 区域名与系统一致时会带上玩家的自定义格式（例如把负号改成括号），那就又跟着系统走了。
    /// </remarks>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Tag);

    /// <summary>
    /// 把当前线程与之后新建的线程都钉成 <see cref="Culture"/>，返回钉上的那一份。启动时调一次，
    /// 要在任何数字被格式化之前。
    /// </summary>
    public static CultureInfo PinCulture()
    {
        var culture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }
}
