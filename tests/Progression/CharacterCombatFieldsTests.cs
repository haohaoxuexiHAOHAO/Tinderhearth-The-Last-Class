using System.Text.Json.Nodes;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Progression;

/// <summary>角色定义里打仗要的那几格：读得出来，缺一格、写错一格都在载入时被拒，并点名是哪一格。</summary>
public class CharacterCombatFieldsTests
{
    [Fact]
    public void 读得出四项属性_行动力_普通攻击_防御与战斗智慧()
    {
        var definition = CharacterDefinition.Parse(Full().ToJsonString(), "测试");

        Assert.Equal(new CharacterAttributes { Strength = 1, Agility = 2, Constitution = 3, Intellect = 4 }, definition.Attributes);
        Assert.Equal(5, definition.ActionPoints);
        Assert.Equal("basic_attack", definition.BasicAttackSkillId);
        Assert.Equal("defend", definition.DefendSkillId);
        Assert.Equal(CombatWisdom.Dull, definition.CombatWisdom);
    }

    /// <summary>每一格都没有能用的默认值：缺了不许悄悄落成 0 或空。</summary>
    [Theory]
    [InlineData("displayNameKey")]
    [InlineData("traitIds")]
    [InlineData("attributes")]
    [InlineData("actionPoints")]
    [InlineData("basicAttackSkillId")]
    [InlineData("defendSkillId")]
    [InlineData("combatWisdom")]
    public void 缺任一格载入被拒并点名那一格(string key)
    {
        var json = Full();
        json.Remove(key);

        var error = Record.Exception(() => CharacterDefinition.Parse(json.ToJsonString(), "测试"));

        Assert.NotNull(error);
        Assert.Contains(key, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>属性写 0 是合法的，所以缺一项也必须被拒，不能落成 0。</summary>
    [Theory]
    [InlineData("strength")]
    [InlineData("agility")]
    [InlineData("constitution")]
    [InlineData("intellect")]
    public void 缺一项属性载入被拒并点名那一项(string key)
    {
        var json = Full();
        json["attributes"]!.AsObject().Remove(key);

        var error = Record.Exception(() => CharacterDefinition.Parse(json.ToJsonString(), "测试"));

        Assert.NotNull(error);
        Assert.Contains(key, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("actionPoints", "0")]
    [InlineData("actionPoints", "-1")]
    [InlineData("basicAttackSkillId", "\"\"")]
    [InlineData("defendSkillId", "\"  \"")]
    [InlineData("combatWisdom", "\"Genius\"")]
    public void 写得不对的一格载入被拒(string key, string value)
    {
        var json = Full();
        json[key] = JsonNode.Parse(value);

        Assert.NotNull(Record.Exception(() => CharacterDefinition.Parse(json.ToJsonString(), "测试")));
    }

    [Fact]
    public void 属性为负载入被拒()
    {
        var json = Full();
        json["attributes"]!["agility"] = -1;

        var error = Record.Exception(() => CharacterDefinition.Parse(json.ToJsonString(), "测试"));

        Assert.NotNull(error);
        Assert.Contains("Agility", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>战斗智慧按名字读：八档加主角那一个，名字以外的写法都不认。</summary>
    [Fact]
    public void 战斗智慧八档从弱到强_主角那一个排在最后()
    {
        Assert.Equal(
            [
                CombatWisdom.Dull, CombatWisdom.Slow, CombatWisdom.Ordinary, CombatWisdom.Alert,
                CombatWisdom.Clever, CombatWisdom.Wise, CombatWisdom.Outstanding, CombatWisdom.Transcendent,
                CombatWisdom.Unpredictable,
            ],
            Enum.GetValues<CombatWisdom>());
    }

    private static JsonObject Full() => JsonNode.Parse("""
        {
          "id": "xuesheng",
          "displayNameKey": "character.xuesheng.name",
          "traitIds": [],
          "attributes": { "strength": 1, "agility": 2, "constitution": 3, "intellect": 4 },
          "actionPoints": 5,
          "basicAttackSkillId": "basic_attack",
          "defendSkillId": "defend",
          "combatWisdom": "Dull"
        }
        """)!.AsObject();
}
