using System.Reflection;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>代码里按名字取的那几档色，与代码仓 <c>assets/self-drawn/palette.gpl</c> 逐项对得上。</summary>
/// <remarks>
/// 色值抄在代码里（色板不进发行包，理由在 <see cref="PaletteEntry"/>），所以这里是那两个家之间唯一的
/// 对照：色板改了值而代码没跟、或者名字拼错，都不报编译错，表现只是屏幕上的颜色悄悄不在色板里。
///
/// 读那份文件只读不写，仓库根见 <see cref="RepoFiles"/>。反证都在内存里改，不碰那份文件。
/// </remarks>
public class PaletteTests
{
    [Fact]
    public void 按名字取的每一档都在色板里而且值对得上()
    {
        var problems = PaletteFile.Mismatches(NamedEntries(), PaletteFile.Read());

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void 色板里被引用的那一档改了值就点名报出()
    {
        // 反证：在内存里把正文那一档的值改掉一点，上面那条判据必须红、而且说得出是哪一档。
        var palette = new Dictionary<string, PixelColor>(PaletteFile.Read());
        var target = HudPalette.Ink;
        var old = palette[target.Name];
        palette[target.Name] = new PixelColor((byte)(old.R ^ 1), old.G, old.B);

        var problems = PaletteFile.Mismatches(NamedEntries(), palette);

        Assert.Contains(problems, p => p.Contains($"「{target.Name}」") && p.Contains("色板里是"));
    }

    [Fact]
    public void 名字拼错的那一档报不在色板里()
    {
        var misspelled = new[] { ("拼错的那一档", new PaletteEntry("sky 99", HudPalette.Ink.Color)) };

        var problems = PaletteFile.Mismatches(misspelled, PaletteFile.Read());

        Assert.Contains(problems, p => p.Contains("「sky 99」") && p.Contains("不在色板里"));
    }

    [Fact]
    public void 代码设的界面色每一个都带着色板里的项名()
    {
        // 不带项名的颜色上面那几条判不到：它不在色板里也不会红。代码设的颜色只在 HudPalette 一处，那里只许有按名字取的那种。
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var bare = typeof(HudPalette).GetProperties(all).Where(p => p.PropertyType == typeof(PixelColor)).Select(p => p.Name)
            .Concat(typeof(HudPalette).GetFields(all).Where(f => f.FieldType == typeof(PixelColor)).Select(f => f.Name))
            .ToList();

        Assert.Empty(bare);
    }

    [Fact]
    public void 反射找得到按名字取的那几档()
    {
        // 反射一项都没找到时，第一条会空跑通过 —— 不报错的空跑比没有这条检查更坏。
        var found = NamedEntries().Select(e => e.Entry).ToList();

        Assert.Contains(HudPalette.Ink, found);
        Assert.Contains(HudPalette.Cooldown, found);
    }

    [Fact]
    public void 色板解析不出来的行当场报错()
    {
        // 解析器静默跳过一行，那一档就像是「不在色板里」，报出来的原因是错的。
        Assert.Throws<InvalidDataException>(
            () => PaletteFile.Parse(["GIMP Palette", " 25  21\tash 1"]));
        Assert.Throws<InvalidDataException>(
            () => PaletteFile.Parse([" 25  21  39\tash 1", " 48  45  60\tash 1"]));
        Assert.Throws<InvalidDataException>(() => PaletteFile.Parse(["GIMP Palette", "#"]));
    }

    /// <summary>规则层里每一个静态的 <see cref="PaletteEntry"/> 属性，连同它属于谁。</summary>
    /// <remarks>
    /// 扫整个程序集而不是只扫 <see cref="HudPalette"/>：哪天别处也按名字取色板，不必记得回来改这里。
    /// </remarks>
    private static List<(string Owner, PaletteEntry Entry)> NamedEntries() =>
        typeof(PaletteEntry).Assembly.GetTypes()
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(property => property.PropertyType == typeof(PaletteEntry))
                .Select(property => ($"{type.Name}.{property.Name}", (PaletteEntry)property.GetValue(null)!)))
            .ToList();
}

/// <summary>读代码仓的 <c>assets/self-drawn/palette.gpl</c>：项名 → 色。只读不写。</summary>
/// <remarks>
/// 口径与代码仓 <c>tools/palettelib.py</c> 的 <c>read_palette</c> 相同：跳过注释与表头，每行是
/// 「R G B」加一个制表符再加项名。两份解析各写一份是因为两边不同语言；口径不同时这里先红。
/// </remarks>
internal static class PaletteFile
{
    /// <summary>读那份色板。找不到仓库根或文件就抛，不当成「色板是空的」。</summary>
    public static IReadOnlyDictionary<string, PixelColor> Read() =>
        Parse(File.ReadAllLines(Path.Combine(RepoFiles.Root(), "assets", "self-drawn", "palette.gpl")));

    /// <summary>解析色板文本。一行解析不出来、同名出现两次、一项都没有，都抛。</summary>
    public static IReadOnlyDictionary<string, PixelColor> Parse(IEnumerable<string> lines)
    {
        var entries = new Dictionary<string, PixelColor>();
        string[] header = ["#", "GIMP", "Name", "Columns"];
        foreach (var line in lines)
        {
            if (line.Length == 0 || header.Any(h => line.StartsWith(h, StringComparison.Ordinal)))
            {
                continue;
            }

            var parts = line.Split('\t');
            var rgb = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || rgb.Length != 3 || !rgb.All(v => byte.TryParse(v, out _)))
            {
                throw new InvalidDataException($"色板里这一行解析不出来：{line}");
            }

            var name = parts[1].Trim();
            if (!entries.TryAdd(name, new PixelColor(byte.Parse(rgb[0]), byte.Parse(rgb[1]), byte.Parse(rgb[2]))))
            {
                throw new InvalidDataException($"色板里「{name}」出现了两次");
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("色板里一项都没解析到 —— 那等于这条检查根本没有执行，不是通过");
        }

        return entries;
    }

    /// <summary>逐项对：名字不在色板里、或值对不上的，各报一句，说清是谁取的哪一档。</summary>
    public static List<string> Mismatches(
        IEnumerable<(string Owner, PaletteEntry Entry)> entries,
        IReadOnlyDictionary<string, PixelColor> palette) =>
        entries.SelectMany(e => ProblemsOf(e.Owner, e.Entry, palette)).ToList();

    private static IEnumerable<string> ProblemsOf(
        string owner, PaletteEntry entry, IReadOnlyDictionary<string, PixelColor> palette)
    {
        if (!palette.TryGetValue(entry.Name, out var actual))
        {
            yield return $"{owner} 取的「{entry.Name}」不在色板里";
        }
        else if (actual != entry.Color)
        {
            yield return $"{owner} 取的「{entry.Name}」：色板里是 {actual}，代码里是 {entry.Color}";
        }
    }
}
