using System.Text.Json.Nodes;
using Tinderhearth.Rules.Foundation.Save;
using Xunit;

namespace Tinderhearth.Rules.Tests.Foundation;

/// <summary>存档分片的声明与注册，以及一份正式存档的外形。</summary>
/// <remarks>
/// 分片的内容在这里只是几格测试用的数，世界那几个真分片（日历、地块、背包）的用例在 <c>tests/Economy</c> 下。
/// </remarks>
public class SaveShardTests
{
    private static SaveShardDeclaration Shard(string id, SaveSide side = SaveSide.FormalSave) =>
        new(Id: id, Owner: "测试用的系统", Side: side, Default: () => new JsonObject { ["count"] = 0 });

    private static SaveShardRegistry Registry(params SaveShardDeclaration[] shards)
    {
        var registry = new SaveShardRegistry();
        foreach (var shard in shards)
        {
            registry.Register(shard);
        }

        return registry;
    }

    [Theory]
    [InlineData("标识")]
    [InlineData("持有它的系统")]
    [InlineData("进哪一侧")]
    [InlineData("存档默认值")]
    public void 声明缺一格注册被拒并说清缺哪一格(string missing)
    {
        var whole = Shard("calendar");
        var broken = missing switch
        {
            "标识" => whole with { Id = " " },
            "持有它的系统" => whole with { Owner = "" },
            "进哪一侧" => whole with { Side = default },
            "存档默认值" => whole with { Default = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(missing)),
        };

        var e = Assert.Throws<ArgumentException>(() => new SaveShardRegistry().Register(broken));

        Assert.Contains(missing, e.Message);
    }

    [Fact]
    public void 存档默认值给不出一份时与没填一样被拒()
    {
        var e = Assert.Throws<ArgumentException>(
            () => new SaveShardRegistry().Register(Shard("calendar") with { Default = () => null! }));

        Assert.Contains("存档默认值", e.Message);
    }

    [Fact]
    public void 同一个标识注册两次被拒()
    {
        var registry = Registry(Shard("calendar"));

        var e = Assert.Throws<ArgumentException>(() => registry.Register(Shard("calendar", SaveSide.ResumePoint)));

        Assert.Contains("calendar", e.Message);
        Assert.Single(registry.Shards);
    }

    /// <summary>没有「两边都进」这个取值；枚举里多出一个、或者交进来一个枚举里没有的数，都过不去。</summary>
    [Fact]
    public void 进哪一侧只有正式存档与临时续玩点两个取值()
    {
        SaveSide[] both = [SaveSide.FormalSave, SaveSide.ResumePoint];

        Assert.Equal(both, Enum.GetValues<SaveSide>());
        Assert.Throws<ArgumentException>(() => new SaveShardRegistry().Register(Shard("calendar") with { Side = (SaveSide)3 }));
    }

    /// <summary>新建的那一份是空的：别处声明过的分片不注册就不在里面。</summary>
    [Fact]
    public void 读口列出的正是注册过的那几个()
    {
        var registry = new SaveShardRegistry();
        var declaredButNotRegistered = Shard("codex");
        string[] registered = ["calendar", "plots"];

        Assert.Empty(registry.Shards);
        registry.Register(Shard("calendar"));
        registry.Register(Shard("plots", SaveSide.ResumePoint));

        Assert.Equal(registered, registry.Shards.Select(s => s.Id));
        Assert.False(registry.TryGet(declaredButNotRegistered.Id, out _));
        Assert.True(registry.TryGet("plots", out var plots));
        Assert.Equal(SaveSide.ResumePoint, plots.Side);
    }

    [Fact]
    public void 正式存档最外层只有一个版本号与各分片()
    {
        var registry = Registry(Shard("calendar"), Shard("plots"), Shard("resume_only", SaveSide.ResumePoint));
        string[] outer = [SaveFile.VersionKey, SaveFile.ShardsKey];
        string[] formalOnly = ["calendar", "plots"];

        var text = SaveFile.WriteFormalSave(registry, new Dictionary<string, JsonObject>
        {
            ["plots"] = new() { ["count"] = 2 },
            ["calendar"] = new() { ["count"] = 1 },
        });

        var root = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(outer, root.Select(p => p.Key));
        Assert.Equal(SaveFile.CurrentVersion, root[SaveFile.VersionKey]!.GetValue<int>());
        Assert.Equal(formalOnly, root[SaveFile.ShardsKey]!.AsObject().Select(p => p.Key));
        Assert.Equal(2, root[SaveFile.ShardsKey]!["plots"]!["count"]!.GetValue<int>());
    }

    [Fact]
    public void 分片原样写出去()
    {
        var registry = Registry(Shard("calendar"));
        var calendar = new JsonObject { ["year"] = 2, ["season"] = "Summer", ["day"] = 17, ["names"] = new JsonArray("金", "云舒") };

        var text = SaveFile.WriteFormalSave(registry, new Dictionary<string, JsonObject> { ["calendar"] = calendar });

        Assert.True(JsonNode.DeepEquals(calendar, JsonNode.Parse(text)![SaveFile.ShardsKey]!["calendar"]));
    }

    [Fact]
    public void 明文缩进排好_换行一律是LF_中文不转义()
    {
        var registry = Registry(Shard("calendar"));
        var text = SaveFile.WriteFormalSave(
            registry, new Dictionary<string, JsonObject> { ["calendar"] = new() { ["name"] = "云舒" } });

        Assert.Contains("\n  \"" + SaveFile.ShardsKey + "\"", text);
        Assert.DoesNotContain("\r", text);
        Assert.Contains("云舒", text);
    }

    [Fact]
    public void 缺一个进正式存档的分片被拒()
    {
        var registry = Registry(Shard("calendar"), Shard("plots"));

        var e = Assert.Throws<ArgumentException>(() => SaveFile.WriteFormalSave(
            registry, new Dictionary<string, JsonObject> { ["calendar"] = new() }));

        Assert.Contains("plots", e.Message);
    }

    [Fact]
    public void 给了一份没注册的分片被拒()
    {
        var registry = Registry(Shard("calendar"));

        var e = Assert.Throws<ArgumentException>(() => SaveFile.WriteFormalSave(
            registry, new Dictionary<string, JsonObject> { ["calendar"] = new(), ["stray"] = new() }));

        Assert.Contains("stray", e.Message);
    }

    [Fact]
    public void 进续玩点的分片不进正式存档()
    {
        var registry = Registry(Shard("calendar"), Shard("resume_only", SaveSide.ResumePoint));

        var e = Assert.Throws<ArgumentException>(() => SaveFile.WriteFormalSave(
            registry, new Dictionary<string, JsonObject> { ["calendar"] = new(), ["resume_only"] = new() }));

        Assert.Contains("resume_only", e.Message);
    }

    /// <summary>交进去的那一份已经挂在别的节点上也写得出去，写完它还在原处。</summary>
    [Fact]
    public void 已经挂在别处的分片也写得出去()
    {
        var registry = Registry(Shard("calendar"));
        var calendar = new JsonObject { ["day"] = 3 };
        var elsewhere = new JsonObject { ["held"] = calendar };

        SaveFile.WriteFormalSave(registry, new Dictionary<string, JsonObject> { ["calendar"] = calendar });

        Assert.Same(calendar, elsewhere["held"]);
    }
}
