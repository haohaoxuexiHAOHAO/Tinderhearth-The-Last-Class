using System.Text.Json.Nodes;
using Tinderhearth.Rules.Foundation.Save;
using Xunit;

namespace Tinderhearth.Rules.Tests.Foundation;

/// <summary>读回一份存档：先判兼容，再按版本补或拒缺字段，丢多出来的字段，拒认不出的分片。</summary>
/// <remarks>
/// 规则出自设计仓 decisions/ADR-0015-存档的版本与缺字段口径.md 与 decisions/ADR-0017-版本更新对旧存档的两种后果.md。
/// 现在当前版本与最低兼容版本相等，演练不出中间那一段，所以那几条用带版本界限的那个读法、按「当前第 3 版、最低兼容第 2 版」演。
/// </remarks>
public class SaveVersionTests
{
    private const int DrillCurrent = 3;
    private const int DrillMinimum = 2;

    private static int _defaultCalls;

    private static SaveShardRegistry Registry()
    {
        var registry = new SaveShardRegistry();
        registry.Register(new(Id: "alpha", Owner: "测试用的系统甲", Side: SaveSide.FormalSave,
            Default: () => { _defaultCalls++; return new JsonObject { ["count"] = 0, ["name"] = "默认" }; }));
        registry.Register(new(Id: "beta", Owner: "测试用的系统乙", Side: SaveSide.FormalSave,
            Default: () => new JsonObject { ["flag"] = false }));
        return registry;
    }

    private static JsonObject FullShards() => new()
    {
        ["alpha"] = new JsonObject { ["count"] = 5, ["name"] = "存下来的" },
        ["beta"] = new JsonObject { ["flag"] = true },
    };

    private static string SaveText(int version, JsonObject shards) =>
        new JsonObject { [SaveFile.VersionKey] = version, [SaveFile.ShardsKey] = shards }.ToJsonString();

    private static SaveRejectedException Rejected(string text) =>
        Assert.Throws<SaveRejectedException>(() => SaveFile.ReadFormalSave(Registry(), text));

    [Fact]
    public void 写出去再读回来完全一样()
    {
        var registry = Registry();
        var shards = new Dictionary<string, JsonObject>
        {
            ["alpha"] = new() { ["count"] = 5, ["name"] = "存下来的" },
            ["beta"] = new() { ["flag"] = true },
        };

        var back = SaveFile.ReadFormalSave(registry, SaveFile.WriteFormalSave(registry, shards));

        Assert.Equal(shards.Keys, back.Keys);
        Assert.All(shards, p => Assert.True(JsonNode.DeepEquals(p.Value, back[p.Key])));
    }

    [Fact]
    public void 比当前旧的存档缺一个字段按存档默认值补上()
    {
        var shards = FullShards();
        shards["alpha"]!.AsObject().Remove("name");

        var back = SaveFile.ReadFormalSave(Registry(), SaveText(DrillMinimum, shards), DrillCurrent, DrillMinimum);

        Assert.Equal(5, back["alpha"]["count"]!.GetValue<int>());
        Assert.Equal("默认", back["alpha"]["name"]!.GetValue<string>());
    }

    [Fact]
    public void 比当前旧的存档缺整个分片也按存档默认值补上()
    {
        var shards = FullShards();
        shards.Remove("beta");

        var back = SaveFile.ReadFormalSave(Registry(), SaveText(DrillMinimum, shards), DrillCurrent, DrillMinimum);

        Assert.False(back["beta"]["flag"]!.GetValue<bool>());
    }

    /// <summary>这一条是承重项：同一版缺字段只能是写坏了，补上默认值就是替一份坏档兜底。</summary>
    [Fact]
    public void 与当前同版本的存档缺一个字段被拒并说清是哪个分片缺哪一项()
    {
        var shards = FullShards();
        shards["alpha"]!.AsObject().Remove("name");

        var e = Rejected(SaveText(SaveFile.CurrentVersion, shards));

        Assert.Equal(SaveRejection.MissingField, e.Reason);
        Assert.Contains("alpha", e.Message);
        Assert.Contains("name", e.Message);
    }

    [Fact]
    public void 与当前同版本的存档缺整个分片被拒()
    {
        var shards = FullShards();
        shards.Remove("beta");

        var e = Rejected(SaveText(SaveFile.CurrentVersion, shards));

        Assert.Equal(SaveRejection.MissingField, e.Reason);
        Assert.Contains("beta", e.Message);
    }

    [Fact]
    public void 版本比当前新的存档被拒()
    {
        var e = Rejected(SaveText(SaveFile.CurrentVersion + 1, FullShards()));

        Assert.Equal(SaveRejection.NewerVersion, e.Reason);
    }

    /// <summary>被拒发生在碰任何一个分片之前：连存档默认值都还没取。</summary>
    [Fact]
    public void 版本低于最低兼容版本的存档被拒_一个分片都没碰()
    {
        var registry = Registry();
        var callsBefore = _defaultCalls;

        var e = Assert.Throws<SaveRejectedException>(
            () => SaveFile.ReadFormalSave(registry, SaveText(SaveFile.MinimumCompatibleVersion - 1, FullShards())));

        Assert.Equal(SaveRejection.BelowMinimumVersion, e.Reason);
        Assert.Equal(callsBefore, _defaultCalls);
    }

    /// <summary>钉住最低兼容版本真的在被读：同一份存档，界限一抬就从补得上转为被拒。</summary>
    [Fact]
    public void 把最低兼容版本调到等于当前之后原先补得上的那一份转为被拒()
    {
        var shards = FullShards();
        shards["alpha"]!.AsObject().Remove("name");
        var text = SaveText(DrillMinimum, shards);

        SaveFile.ReadFormalSave(Registry(), text, DrillCurrent, DrillMinimum);
        var e = Assert.Throws<SaveRejectedException>(
            () => SaveFile.ReadFormalSave(Registry(), text, DrillCurrent, minimumCompatibleVersion: DrillCurrent));

        Assert.Equal(SaveRejection.BelowMinimumVersion, e.Reason);
    }

    [Fact]
    public void 分片里多出来的字段被丢弃_再写回时不带它()
    {
        var registry = Registry();
        var shards = FullShards();
        shards["alpha"]!["leftover"] = "旧版本没有的这一格";

        var back = SaveFile.ReadFormalSave(registry, SaveText(SaveFile.CurrentVersion, shards));
        var written = SaveFile.WriteFormalSave(registry, back);

        Assert.False(back["alpha"].ContainsKey("leftover"));
        Assert.DoesNotContain("leftover", written);
    }

    [Fact]
    public void 认不出的分片不丢_载入被拒并点名它()
    {
        var shards = FullShards();
        shards["from_a_mod"] = new JsonObject { ["anything"] = 1 };

        var e = Rejected(SaveText(SaveFile.CurrentVersion, shards));

        Assert.Equal(SaveRejection.UnknownShard, e.Reason);
        Assert.Contains("from_a_mod", e.Message);
    }

    [Theory]
    [InlineData("不是 JSON")]
    [InlineData("[1, 2]")]
    [InlineData("{\"version\": \"1\", \"shards\": {}}")]
    [InlineData("{\"version\": 1, \"shards\": []}")]
    [InlineData("{\"version\": 1, \"shards\": {\"alpha\": 3, \"beta\": {\"flag\": true}}}")]
    [InlineData("{\"version\": 1, \"shards\": {}, }")]
    [InlineData("{\"version\": 1, /* 注释 */ \"shards\": {}}")]
    public void 读不出来的存档被拒(string text)
    {
        Assert.Equal(SaveRejection.Unreadable, Rejected(text).Reason);
    }
}
