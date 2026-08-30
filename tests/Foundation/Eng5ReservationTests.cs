using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Foundation.Config;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Foundation;

/// <summary>
/// `ENG-5` 那四条零成本预留的守卫。
/// </summary>
/// <remarks>
/// 为什么这几条要有测试而不只是写在文档里：这四条的失效方式都是**静默的** —— 有人图省事
/// 写死一个数字或一句中文，代码照样能跑、游戏照样能玩，等到做 mod 或联机才发现要翻遍代码。
/// 这里的断言就是让那种图省事当场失败。
///
/// 这些测试**刻意不含任何玩法数值**：`GP-2` 尚未设计，测出来的数字只会是猜的。它们测的是
/// 「数字从配置来」这个结构，而不是「数字应该是几」。所以 `GP-2` 落地后它们不需要改。
/// </remarks>
public class Eng5ReservationTests
{
    [Fact]
    public void 名册容量来自配置而不是代码里的常量()
    {
        // 两份不同的配置数据，同一份代码 —— 容量跟着数据走才算真外置。
        Assert.Equal(3, new Roster(GameConfig.Parse("""{ "rosterCapacity": 3 }""").RosterCapacity).Capacity);
        Assert.Equal(12, new Roster(GameConfig.Parse("""{ "rosterCapacity": 12 }""").RosterCapacity).Capacity);
    }

    [Fact]
    public void 名册满了之后拒绝收人且不抛异常()
    {
        var roster = new Roster(capacity: 2);

        Assert.True(roster.TryAdd("ember"));
        Assert.True(roster.TryAdd("student-a"));
        Assert.False(roster.TryAdd("student-b"));   // 满了
        Assert.False(roster.TryAdd("ember"));       // 重复
        Assert.Equal(2, roster.ActorIds.Count);
    }

    [Fact]
    public void 角色定义从数据文件来且显示名是文本键不是名字()
    {
        const string json = """
            {
              "id": "ember",
              "displayNameKey": "character.ember.name",
              "traitIds": ["trait.example"]
            }
            """;

        var definition = CharacterDefinition.Parse(json, "测试用角色定义");

        Assert.Equal("ember", definition.Id);
        Assert.Equal("character.ember.name", definition.DisplayNameKey);
        Assert.Single(definition.TraitIds);

        // 显示名必须经文本表解析出来，而不是定义里直接写着中文。
        var text = new TextCatalog(new Dictionary<string, string>
        {
            ["character.ember.name"] = "烬",
        });
        Assert.Equal("烬", text[definition.DisplayNameKey]);
    }

    [Fact]
    public void 缺文本时给显眼占位而不是崩掉也不是空串()
    {
        var text = TextCatalog.Empty;

        var shown = text["ui.nonexistent"];

        Assert.Contains("缺文本", shown);          // 看得见
        Assert.Contains("ui.nonexistent", shown);  // 且说得出缺的是哪个键
        Assert.NotEmpty(shown);                    // 不是静默的空串
    }

    [Fact]
    public void 能列出缺失的文本键让漏翻成为一个可观察的数字()
    {
        var text = new TextCatalog(new Dictionary<string, string> { ["a"] = "甲" });

        Assert.Equal(["b", "c"], text.MissingKeys(["a", "b", "c"]));
    }

    [Fact]
    public void 主角可以被换成AI驱动_证明玩家没有写死成主角()
    {
        // 这条是四条预留里最容易被写死的一条，所以直接拿主角来试。
        const string protagonistId = "ember";
        var registry = new ActorControllerRegistry();

        registry.Assign(protagonistId, new StubController(ActorControllerKind.LocalPlayer));
        Assert.Equal(ActorControllerKind.LocalPlayer, registry.Require(protagonistId).Kind);

        // 换成 AI —— 如果哪天有人把「主角必须由玩家控制」写进代码，这一行会失败。
        registry.Assign(protagonistId, new StubController(ActorControllerKind.Ai));
        Assert.Equal(ActorControllerKind.Ai, registry.Require(protagonistId).Kind);

        // 反过来：学员也能交给本地玩家驱动，联机时玩家用自定义角色靠的就是这条。
        registry.Assign("student-a", new StubController(ActorControllerKind.LocalPlayer));
        Assert.Equal(ActorControllerKind.LocalPlayer, registry.Require("student-a").Kind);
    }

    [Fact]
    public void 没登记控制器的角色直接抛而不是静默不动()
    {
        var registry = new ActorControllerRegistry();

        var ex = Assert.Throws<KeyNotFoundException>(() => registry.Require("nobody"));
        Assert.Contains("nobody", ex.Message);
    }

    [Fact]
    public void mod来源覆盖基础内容且记得每条来自哪里()
    {
        var catalog = new ContentCatalog();
        catalog.AddSource(new InMemoryContentSource("base", new Dictionary<string, string>
        {
            ["characters/ember.json"] = "{ \"id\": \"ember\" }",
            ["characters/student-a.json"] = "{ \"id\": \"student-a\" }",
        }));
        catalog.AddSource(new InMemoryContentSource("mod:example", new Dictionary<string, string>
        {
            ["characters/ember.json"] = "{ \"id\": \"ember-modded\" }",
            ["characters/extra.json"] = "{ \"id\": \"extra\" }",
        }));

        var resolved = catalog.Resolve("characters");

        Assert.Equal(3, resolved.Count);
        // 后登记的来源覆盖前面的同名条目
        Assert.Equal("mod:example", resolved["characters/ember.json"].SourceName);
        Assert.Contains("ember-modded", resolved["characters/ember.json"].Text);
        // 没被覆盖的仍来自基础内容
        Assert.Equal("base", resolved["characters/student-a.json"].SourceName);
        // mod 新增的条目也在，且来源可追溯 —— `ENG-7` 的缺失提示要靠这个
        Assert.Equal("mod:example", resolved["characters/extra.json"].SourceName);
    }

    [Fact]
    public void 文本按键合并_mod只改一句不会抹掉其余文本()
    {
        // 这条对着一个实测撞出来的缺陷：最初文本走整文件覆盖，mod 提供 text/zh-CN.json 后
        // 基础文本从 5 条掉到 3 条，boot.title 直接消失。整文件覆盖对角色对、对文本错。
        var merged = TextCatalog.Merge(
        [
            new Dictionary<string, string>
            {
                ["boot.title"] = "火种：最后一届",
                ["character.ember.name"] = "烬",
            },
            new Dictionary<string, string>
            {
                ["character.ember.name"] = "烬（mod 改过）",
            },
        ]);

        Assert.Equal("烬（mod 改过）", merged["character.ember.name"]); // 同键被覆盖
        Assert.Equal("火种：最后一届", merged["boot.title"]);           // 未提到的键仍在
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void 一个文件一个实体的内容仍然整文件覆盖()
    {
        // 与上一条相对：角色是「一个文件一个实体」，mod 想改某个角色就该整份替换。
        // 两种语义并存是有意的，别把它们统一成一种。
        var catalog = new ContentCatalog();
        catalog.AddSource(new InMemoryContentSource("base", new Dictionary<string, string>
        {
            ["characters/ember.json"] = """{ "id": "ember", "displayNameKey": "a", "traitIds": ["t1"] }""",
        }));
        catalog.AddSource(new InMemoryContentSource("mod", new Dictionary<string, string>
        {
            ["characters/ember.json"] = """{ "id": "ember", "displayNameKey": "b", "traitIds": [] }""",
        }));

        var resolved = catalog.Resolve("characters");
        var ember = CharacterDefinition.Parse(resolved["characters/ember.json"].Text, "test");

        Assert.Equal("b", ember.DisplayNameKey);
        Assert.Empty(ember.TraitIds);   // 整份被替换，基础里的 t1 不残留

        // ResolveAll 不去重，两份都在，顺序是先基础后 mod
        var all = catalog.ResolveAll("characters");
        Assert.Equal(2, all.Count);
        Assert.Equal(["base", "mod"], all.Select(e => e.SourceName));
    }

    private sealed class StubController(ActorControllerKind kind) : IActorController
    {
        public ActorControllerKind Kind { get; } = kind;

        public ActorIntent Decide(in ActorView view) => new("idle", null);
    }
}
