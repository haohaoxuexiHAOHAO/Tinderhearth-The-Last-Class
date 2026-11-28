using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>图标还是「暂无」时格子里写的那一个字。</summary>
/// <remarks>写错的表现不报错：截到半个字画出来是乱码，取成空串那一格就空着、看着像没东西。</remarks>
public class IconPlaceholderTests
{
    [Theory]
    [InlineData("作物1", "作")]
    [InlineData("萝卜种子", "萝")]
    [InlineData("Turnip", "T")]
    [InlineData("  土豆", "土")]
    [InlineData("7号零件", "7")]
    public void 写名字的第一个字(string name, string expected)
    {
        Assert.Equal(expected, IconPlaceholder.TextFor(name));
    }

    [Fact]
    public void 拼成一个字的那几种整个取出来_不截半个()
    {
        // 「𠀀」在 UTF-16 里是一对代理项，「é」这里写成 e 加一个组合重音：按 char 取都只拿到一半。
        Assert.Equal("𠀀", IconPlaceholder.TextFor("𠀀字"));
        Assert.Equal("e\u0301", IconPlaceholder.TextFor("e\u0301clair"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 名字没有可写的字时写问号_不让那一格空着(string? name)
    {
        Assert.Equal(IconPlaceholder.Unnamed, IconPlaceholder.TextFor(name));
    }
}
