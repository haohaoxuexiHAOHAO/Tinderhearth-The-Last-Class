using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Foundation;

/// <summary>「一个文件一个实体」的内容按标识建表：一个标识只许对应一份定义，角色的标识只许一种写法。</summary>
/// <remarks>
/// 角色的标识取名字的拼音，同音的名字会撞上。不拦的话，撞上之后后读到的那份顶掉前一份、或者进不了
/// 名册，而这不报错 —— 表现只是某个角色不见了。
/// </remarks>
public class ContentByIdTests
{
    [Fact]
    public void 按标识建表_键是标识不是文件路径()
    {
        var catalog = Catalog(Source("base",
            ("characters/jin.json", Character("jin")),
            ("characters/someone.json", Character("tiejun"))));

        var byId = Characters(catalog);

        Assert.Equal(["jin", "tiejun"], byId.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("character.tiejun.name", byId["tiejun"].DisplayNameKey);
    }

    [Fact]
    public void 两份文件用了同一个标识当场报错并点名两份()
    {
        // 反证：云漪和另一个同音的人都按拼音取成 yunyi。
        var catalog = Catalog(Source("base",
            ("characters/yunyi.json", Character("yunyi")),
            ("characters/yunyi_new.json", Character("yunyi"))));

        var error = Assert.Throws<InvalidDataException>(() => Characters(catalog));

        Assert.Contains("「yunyi」", error.Message);
        Assert.Contains("characters/yunyi.json", error.Message);
        Assert.Contains("characters/yunyi_new.json", error.Message);
    }

    [Fact]
    public void mod的另一份文件撞了基础内容的标识也报_并说出各自来自哪里()
    {
        var catalog = Catalog(
            Source("base", ("characters/jin.json", Character("jin"))),
            Source("mod:example", ("characters/jin_gold.json", Character("jin"))));

        var error = Assert.Throws<InvalidDataException>(() => Characters(catalog));

        Assert.Contains("（来自 base）", error.Message);
        Assert.Contains("（来自 mod:example）", error.Message);
    }

    [Fact]
    public void 同一路径是mod有意整份替换_不算撞()
    {
        var catalog = Catalog(
            Source("base", ("characters/jin.json", Character("jin"))),
            Source("mod:example", ("characters/jin.json",
                """{ "id": "jin", "displayNameKey": "mod.jin.name", "traitIds": [] }""")));

        var byId = Characters(catalog);

        Assert.Single(byId);
        Assert.Equal("mod.jin.name", byId["jin"].DisplayNameKey);
    }

    [Fact]
    public void 撞上的几组一次全报()
    {
        // 一次只报一组的话，改完一组重跑才看得见下一组。
        var catalog = Catalog(Source("base",
            ("characters/a.json", Character("yunyi")),
            ("characters/b.json", Character("yunyi")),
            ("characters/c.json", Character("jin")),
            ("characters/d.json", Character("jin"))));

        var error = Assert.Throws<InvalidDataException>(() => Characters(catalog));

        Assert.Contains("「yunyi」", error.Message);
        Assert.Contains("「jin」", error.Message);
    }

    [Theory]
    [InlineData("jin")]
    [InlineData("tiejun")]
    [InlineData("yunyi2")]
    public void 角色标识是小写字母_末尾可接数字(string id)
    {
        Assert.Equal(id, CharacterDefinition.Parse(Character(id), "测试").Id);
    }

    /// <summary>这几种写法各自都和一种合规的写法同音，放过它们，撞名检查就拦不住那一对。</summary>
    [Theory]
    [InlineData("Jin")]       // 大写：与 jin 只差大小写
    [InlineData("tie_jun")]   // 分隔：与 tiejun 同音
    [InlineData("tie-jun")]
    [InlineData("jìn")]       // 声调
    [InlineData("yun2yi")]    // 数字只许在末尾
    [InlineData("2jin")]
    [InlineData("")]
    public void 写法不对的角色标识载入被拒并指出取法在哪(string id)
    {
        var error = Assert.Throws<InvalidDataException>(() => CharacterDefinition.Parse(Character(id), "测试"));

        Assert.Contains("界面绘制约定", error.Message);
    }

    [Fact]
    public void 没写标识的角色定义载入被拒()
    {
        Assert.Throws<InvalidDataException>(() => CharacterDefinition.Parse(
            """{ "displayNameKey": "character.x.name", "traitIds": [] }""", "测试"));
    }

    private static ContentCatalog Catalog(params InMemoryContentSource[] sources)
    {
        var catalog = new ContentCatalog();
        foreach (var source in sources)
        {
            catalog.AddSource(source);
        }

        return catalog;
    }

    private static InMemoryContentSource Source(string name, params (string Path, string Json)[] files) =>
        new(name, files.ToDictionary(f => f.Path, f => f.Json));

    private static string Character(string id) =>
        $$"""{ "id": "{{id}}", "displayNameKey": "character.{{id}}.name", "traitIds": [] }""";

    private static IReadOnlyDictionary<string, CharacterDefinition> Characters(ContentCatalog catalog) =>
        catalog.ResolveById(CharacterDefinition.ContentDirectory,
            e => CharacterDefinition.Parse(e.Text, e.RelativePath), c => c.Id);
}
