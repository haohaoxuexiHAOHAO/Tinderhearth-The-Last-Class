using System.Reflection;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>代码里按名字取的那几档色，与代码仓 <c>assets/self-drawn/palette.gpl</c> 逐项对得上。</summary>
/// <remarks>
/// 色值抄在代码里（理由在 <see cref="PaletteEntry"/>），所以这里是那两个家之间唯一的
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
        Assert.Empty(BareColors());
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

    /// <summary><see cref="HudPalette"/> 里不带项名的颜色（类型是 <see cref="PixelColor"/> 的属性与字段）。必须是空的。</summary>
    internal static List<string> BareColors()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        return typeof(HudPalette).GetProperties(all).Where(p => p.PropertyType == typeof(PixelColor)).Select(p => p.Name)
            .Concat(typeof(HudPalette).GetFields(all).Where(f => f.FieldType == typeof(PixelColor)).Select(f => f.Name))
            .ToList();
    }

    /// <summary>规则层里每一个静态的 <see cref="PaletteEntry"/> 属性，连同它属于谁。</summary>
    /// <remarks>
    /// 扫整个程序集而不是只扫 <see cref="HudPalette"/>：哪天别处也按名字取色板，不必记得回来改这里。
    /// </remarks>
    internal static List<(string Owner, PaletteEntry Entry)> NamedEntries() =>
        typeof(PaletteEntry).Assembly.GetTypes()
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(property => property.PropertyType == typeof(PaletteEntry))
                .Select(property => ($"{type.Name}.{property.Name}", (PaletteEntry)property.GetValue(null)!)))
            .ToList();
}

/// <summary>读代码仓的 <c>assets/self-drawn/palette.gpl</c>：项名 → 色。只读不写。</summary>
/// <remarks>
/// 解析走规则层那一份（<see cref="Palette.Parse"/>），游戏运行时读发行包里的色板用的也是它，所以测试与游戏不会对同一份
/// 色板读出两样东西。它与代码仓 <c>tools/palettelib.py</c> 口径相同，两边语言不同所以各写一份；口径不同时这里先红。
/// </remarks>
internal static class PaletteFile
{
    /// <summary>色板在仓库里的位置，与 <see cref="Palette.ResourcePath"/> 是同一份文件。</summary>
    public static string PathInRepo() => Path.Combine(RepoFiles.Root(), "assets", "self-drawn", "palette.gpl");

    /// <summary>读那份色板。找不到仓库根或文件就抛，不当成「色板是空的」。</summary>
    public static Palette ReadPalette() => Palette.Parse(File.ReadAllLines(PathInRepo()), PathInRepo());

    /// <summary>读那份色板，只要「项名 → 色」。</summary>
    public static IReadOnlyDictionary<string, PixelColor> Read() => ReadPalette().Entries;

    /// <summary>解析色板文本，只要「项名 → 色」。一行解析不出来、同名出现两次、一项都没有，都抛。</summary>
    public static IReadOnlyDictionary<string, PixelColor> Parse(IEnumerable<string> lines) =>
        Palette.Parse(lines, "测试里的色板").Entries;

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
