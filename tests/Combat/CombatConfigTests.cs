using System.Text.Json.Nodes;
using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>战斗配置里命中与伤害那两节：工程里那一份读得通，desc 定了的几个数照 desc；缺一格、写错一格载入被拒。</summary>
/// <remarks>反证都在工程里那一份的副本上改，不改那份文件。</remarks>
public class CombatConfigTests
{
    [Fact]
    public void 工程里那一份读得通_desc定了的几个数照desc()
    {
        var config = CombatConfig.Parse(Shipped().ToJsonString());

        Assert.Equal(0.05, config.Hit.MinChance);
        Assert.Equal(0.95, config.Hit.MaxChance);
        Assert.Equal(0.5, config.Hit.DodgeMultiplierAt(1));
        Assert.True(config.Damage.DefenseK > 0);
    }

    [Theory]
    [InlineData("hit")]
    [InlineData("damage")]
    public void 缺命中或伤害那一节载入被拒并点名(string section)
    {
        var json = Shipped();
        json.Remove(section);

        var error = Record.Exception(() => CombatConfig.Parse(json.ToJsonString()));

        Assert.NotNull(error);
        Assert.Contains(section, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("hit", "minChance")]
    [InlineData("hit", "maxChance")]
    [InlineData("hit", "dodgeByDistance")]
    [InlineData("damage", "defenseK")]
    [InlineData("damage", "blockReduction")]
    public void 缺任一格载入被拒并点名那一格(string section, string key)
    {
        var json = Shipped();
        json[section]!.AsObject().Remove(key);

        var error = Record.Exception(() => CombatConfig.Parse(json.ToJsonString()));

        Assert.NotNull(error);
        Assert.Contains(key, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("hit", "minChance", "1.2")]
    [InlineData("hit", "maxChance", "-0.1")]
    [InlineData("hit", "minChance", "0.96")]   // 比最高的还高
    [InlineData("hit", "dodgeByDistance", "[]")]
    [InlineData("hit", "dodgeByDistance", "[-0.5]")]
    [InlineData("damage", "defenseK", "0")]
    [InlineData("damage", "blockReduction", "1.5")]
    [InlineData("damage", "blockReduction", "-0.1")]
    public void 写得不对的一格载入被拒(string section, string key, string value)
    {
        var json = Shipped();
        json[section]![key] = JsonNode.Parse(value);

        Assert.NotNull(Record.Exception(() => CombatConfig.Parse(json.ToJsonString())));
    }

    [Fact]
    public void 闪避的距离倍率随距离降了载入被拒_报错写明哪一格()
    {
        var json = Shipped();
        json["hit"]!["dodgeByDistance"] = JsonNode.Parse("[0.5, 0.8, 0.7]");

        var error = Record.Exception(() => CombatConfig.Parse(json.ToJsonString()));

        Assert.NotNull(error);
        Assert.Contains("只升不降，隔 3 格的 0.7 比隔 2 格的 0.8 小", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(2, 0.75)]
    [InlineData(3, 1.0)]
    [InlineData(40, 1.0)]
    public void 闪避的距离倍率按隔几格取_比那一串还远的取最后一个(int distance, double expected)
    {
        Assert.Equal(expected, TestBattles.Hit.DodgeMultiplierAt(distance));
    }

    [Fact]
    public void 隔0格取闪避的距离倍率当场报错()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestBattles.Hit.DodgeMultiplierAt(0));
    }

    /// <summary>工程里那一份战斗配置。只读不写。</summary>
    private static JsonObject Shipped() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RepoFiles.Root(), "data", "config", "combat.json")))!.AsObject();
}
