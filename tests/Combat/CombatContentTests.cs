using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Tests.Foundation;
using Tinderhearth.Rules.Tests.Progression;
using Tinderhearth.Rules.Tests.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>打仗要读的那几样内容：角色指的招要在册，工程里那一份读得通。</summary>
public class CombatContentTests
{
    [Fact]
    public void 工程里那一份读得通_每个角色的面板都算得出()
    {
        var content = ShippedContent();

        Assert.Contains("jin", content.Characters.Keys);
        Assert.Equal(CombatWisdom.Unpredictable, content.Characters["jin"].CombatWisdom);
        Assert.All(content.Characters.Values, c => Assert.True(content.PanelOf(c).HpMax > 0, $"{c.Id} 的 HP 上限不为正"));
        Assert.Equal(1, content.Skills["basic_attack"].Cooldown);
        Assert.Equal(1, content.Skills["defend"].Cooldown);
        Assert.True(content.Skills["basic_attack"].TakesTurn);
        Assert.True(content.Skills["defend"].TakesTurn);
    }

    [Fact]
    public void 角色指的普通攻击或防御不在册被拒_几处一次全报()
    {
        var catalog = new ContentCatalog();
        catalog.AddSource(new InMemoryContentSource("base", new Dictionary<string, string>(ShippedFiles())
        {
            ["characters/a.json"] = TestCharacters.Json("aaa", "character.aaa.name").Replace("\"basic_attack\"", "\"no_such_attack\"", StringComparison.Ordinal),
            ["characters/b.json"] = TestCharacters.Json("bbb", "character.bbb.name").Replace("\"defend\"", "\"no_such_defend\"", StringComparison.Ordinal),
        }));

        var error = Assert.Throws<InvalidDataException>(() => CombatContent.Load(catalog));

        Assert.Contains("角色 aaa 的普通攻击「no_such_attack」", error.Message, StringComparison.Ordinal);
        Assert.Contains("角色 bbb 的防御「no_such_defend」", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 招给自己挂的状态打仗时认不得_载入被拒并点名()
    {
        var files = ShippedFiles();
        files["skills/rage.json"] = files["skills/defend.json"]
            .Replace("\"id\": \"defend\"", "\"id\": \"rage\"", StringComparison.Ordinal)
            .Replace("\"block\"", "\"fury\"", StringComparison.Ordinal);
        var catalog = new ContentCatalog();
        catalog.AddSource(new InMemoryContentSource("base", files));

        var error = Assert.Throws<InvalidDataException>(() => CombatContent.Load(catalog));

        Assert.Contains("技能 rage 给自己挂的状态「fury」打仗时认不得", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 读不出来的那一份在报错里点名()
    {
        var catalog = new ContentCatalog();
        catalog.AddSource(new InMemoryContentSource("base", new Dictionary<string, string>(ShippedFiles())
        {
            ["characters/broken.json"] = TestCharacters.Json("broken", "character.broken.name").Replace("\"actionPoints\": 5, ", "", StringComparison.Ordinal),
        }));

        var error = Assert.Throws<InvalidDataException>(() => CombatContent.Load(catalog));

        Assert.Contains("characters/broken.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 缺战斗那一份配置当场报错()
    {
        var files = ShippedFiles();
        files.Remove(CombatConfig.ContentPath);
        var catalog = new ContentCatalog();
        catalog.AddSource(new InMemoryContentSource("base", files));

        var error = Assert.Throws<FileNotFoundException>(() => CombatContent.Load(catalog));

        Assert.Contains(CombatConfig.ContentPath, error.Message, StringComparison.Ordinal);
    }

    private static CombatContent ShippedContent()
    {
        var catalog = new ContentCatalog();
        catalog.AddSource(new InMemoryContentSource("base", ShippedFiles()));
        return CombatContent.Load(catalog);
    }

    /// <summary>工程里的角色、技能与战斗配置，路径写成内容目录里的相对路径。只读不写。</summary>
    private static Dictionary<string, string> ShippedFiles()
    {
        var data = Path.Combine(RepoFiles.Root(), "data");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in (string[])[CharacterDefinition.ContentDirectory, SkillDefinition.ContentDirectory])
        {
            foreach (var path in Directory.GetFiles(Path.Combine(data, directory), "*.json").Order(StringComparer.Ordinal))
            {
                files[$"{directory}/{Path.GetFileName(path)}"] = File.ReadAllText(path);
            }
        }

        files[CombatConfig.ContentPath] = File.ReadAllText(Path.Combine(data, "config", "combat.json"));
        return files;
    }
}
