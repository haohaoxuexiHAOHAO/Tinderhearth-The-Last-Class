namespace Tinderhearth.Rules.UI;

/// <summary>
/// 色板：代码仓 <c>assets/self-drawn/palette.gpl</c> 解析出来的「项名 → 色」，外加按项名取它属于哪条色带。
/// </summary>
/// <remarks>
/// 游戏运行时也读这一份：物品两条轴的档位按项名声明显示色，载入时要查得到那一项、取得到它的色带（设计仓
/// design/物品系统.md 的「两条轴的档位各是一张在册的声明表」一节），界面再按它染色。所以色板随发行包走 ——
/// 代码仓 <c>export_presets.cfg</c> 的 include_filter 带着它（它不是引擎资源，不点名就不进包）。
///
/// 解析口径与代码仓 <c>tools/palettelib.py</c> 相同：跳过空行、注释与表头，每行是「R G B」加一个制表符再加项名，
/// 项名是「色带名 档号」，档号是整数或带 .5 的半档。两边语言不同所以各写一份；口径分了岔，
/// <c>tests/UI/PaletteTests.cs</c> 拿仓库里那份色板一读就红。
/// </remarks>
public sealed class Palette
{
    /// <summary>色板在工程里的位置。运行时按它读，测试按同一个相对路径从仓库里读。</summary>
    public const string ResourcePath = "res://assets/self-drawn/palette.gpl";

    private static readonly string[] HeaderPrefixes = ["#", "GIMP", "Name", "Columns"];

    private readonly Dictionary<string, PixelColor> _entries;

    private Palette(Dictionary<string, PixelColor> entries) => _entries = entries;

    /// <summary>项名 → 色。</summary>
    public IReadOnlyDictionary<string, PixelColor> Entries => _entries;

    /// <summary>解析色板文本。一行解析不出来、项名不是「色带名 档号」、同名出现两次、一项都没有，都抛。</summary>
    /// <remarks>静默跳过一行的话，那一项就像「不在色板里」，报出来的原因是错的。</remarks>
    /// <param name="lines">色板文件的每一行。</param>
    /// <param name="whatForDiagnostics">报错里写的「哪一份色板」。</param>
    public static Palette Parse(IEnumerable<string> lines, string whatForDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var entries = new Dictionary<string, PixelColor>(StringComparer.Ordinal);
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || HeaderPrefixes.Any(h => line.StartsWith(h, StringComparison.Ordinal)))
            {
                continue;
            }

            var parts = line.Split('\t');
            var rgb = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || rgb.Length != 3 || !rgb.All(v => byte.TryParse(v, out _)))
            {
                throw new InvalidDataException($"{whatForDiagnostics} 里这一行解析不出来：{line}");
            }

            var name = parts[1].Trim();
            if (SplitName(name) is null)
            {
                throw new InvalidDataException(
                    $"{whatForDiagnostics} 里的项名「{name}」不是「色带名 档号」的样子（档号是 1、2…，半档是 1.5、2.5…）");
            }

            if (!entries.TryAdd(name, new PixelColor(byte.Parse(rgb[0]), byte.Parse(rgb[1]), byte.Parse(rgb[2]))))
            {
                throw new InvalidDataException($"{whatForDiagnostics} 里「{name}」出现了两次");
            }
        }

        return entries.Count > 0
            ? new Palette(entries)
            : throw new InvalidDataException(
                $"{whatForDiagnostics} 里一项都没解析到 —— 那等于按色板判的那几条根本没有执行，不是通过");
    }

    /// <summary>这一项属于哪条色带；不在色板里就是 <c>null</c>。</summary>
    /// <example><c>ember 5</c> 属于 <c>ember</c>，半档 <c>gemred 1.5</c> 属于 <c>gemred</c>。</example>
    public string? BandOf(string entryName) =>
        entryName is not null && _entries.ContainsKey(entryName) ? SplitName(entryName)?.Band : null;

    /// <summary>把项名拆成色带名与档号；不是「色带名 档号」的样子就是 <c>null</c>。</summary>
    private static (string Band, string Step)? SplitName(string name)
    {
        var space = name.LastIndexOf(' ');
        if (space <= 0)
        {
            return null;
        }

        var step = name[(space + 1)..];
        var whole = step.EndsWith(".5", StringComparison.Ordinal) ? step[..^2] : step;
        return whole.Length > 0 && whole.All(char.IsAsciiDigit) ? (name[..space], step) : null;
    }
}
