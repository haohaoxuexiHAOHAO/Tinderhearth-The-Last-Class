using System.Text.Json.Nodes;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Progression;

/// <summary>技能定义：一招一份，缺一格、几格对不上都在载入时被拒。</summary>
public class SkillDefinitionTests
{
    [Fact]
    public void 读得出打人的招()
    {
        var skill = SkillDefinition.Parse(Attack().ToJsonString(), "测试");

        Assert.Equal("basic_attack", skill.Id);
        Assert.True(skill.TakesTurn);
        Assert.Equal(SkillTarget.Enemy, skill.Target);
        Assert.Equal(1, skill.Range);
        Assert.Equal(1, skill.Cooldown);
        Assert.Equal(DamageKind.Physical, skill.DamageKind);
        Assert.Equal(1.0, skill.Multiplier);
        Assert.Equal(0, skill.Penetration);
        Assert.Null(skill.SelfStatusId);
        Assert.Equal("sword", skill.WeaponKind);
        Assert.Equal(["skill.basic_attack.name", "skill.basic_attack.description"], skill.TextKeys);
    }

    [Fact]
    public void 读得出对自己放的招_伤害那几格写null()
    {
        var skill = SkillDefinition.Parse(Defend().ToJsonString(), "测试");

        Assert.Equal(SkillTarget.Self, skill.Target);
        Assert.Equal(0, skill.Range);
        Assert.Null(skill.DamageKind);
        Assert.Null(skill.Multiplier);
        Assert.Null(skill.Penetration);
        Assert.Equal("block", skill.SelfStatusId);
        Assert.Null(skill.WeaponKind);
    }

    /// <summary>写 null 是合法值的那几格也一样：没写这个键与写了 null 是两回事。</summary>
    [Theory]
    [InlineData("id")]
    [InlineData("nameKey")]
    [InlineData("descriptionKey")]
    [InlineData("takesTurn")]
    [InlineData("target")]
    [InlineData("range")]
    [InlineData("cooldown")]
    [InlineData("damageKind")]
    [InlineData("multiplier")]
    [InlineData("penetration")]
    [InlineData("selfStatusId")]
    [InlineData("weaponKind")]
    public void 缺任一格载入被拒并点名那一格(string key)
    {
        var json = Defend();
        json.Remove(key);

        var error = Record.Exception(() => SkillDefinition.Parse(json.ToJsonString(), "测试"));

        Assert.NotNull(error);
        Assert.Contains(key, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cooldown", "0")]
    [InlineData("range", "-1")]
    [InlineData("multiplier", "0")]
    [InlineData("penetration", "-1")]
    [InlineData("nameKey", "\"普通攻击\"")]
    [InlineData("weaponKind", "\"\"")]
    [InlineData("target", "\"Ally\"")]
    public void 写得不对的一格载入被拒(string key, string value)
    {
        var json = Attack();
        json[key] = JsonNode.Parse(value);

        Assert.NotNull(Record.Exception(() => SkillDefinition.Parse(json.ToJsonString(), "测试")));
    }

    [Theory]
    [InlineData("multiplier", "技能倍率")]
    [InlineData("penetration", "穿透")]
    public void 打人的招缺了倍率或穿透被拒(string key, string expected)
    {
        var json = Attack();
        json[key] = null;

        var error = Assert.Throws<InvalidDataException>(() => SkillDefinition.Parse(json.ToJsonString(), "测试"));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 不打人的招写了倍率被拒()
    {
        var json = Defend();
        json["multiplier"] = 1.0;

        var error = Assert.Throws<InvalidDataException>(() => SkillDefinition.Parse(json.ToJsonString(), "测试"));

        Assert.Contains("技能倍率写 null", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 对自己放的招施放距离不是0被拒_几处对不上一次全报()
    {
        var json = Defend();
        json["range"] = 2;
        json["damageKind"] = "Magic";
        json["multiplier"] = 1.0;
        json["penetration"] = 0;

        var error = Assert.Throws<InvalidDataException>(() => SkillDefinition.Parse(json.ToJsonString(), "测试"));

        Assert.Contains("施放距离写 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("对自己放的招不打人", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 指向对面的招施放距离为0被拒()
    {
        var json = Attack();
        json["range"] = 0;

        var error = Assert.Throws<InvalidDataException>(() => SkillDefinition.Parse(json.ToJsonString(), "测试"));

        Assert.Contains("施放距离至少 1", error.Message, StringComparison.Ordinal);
    }

    private static JsonObject Attack() => JsonNode.Parse("""
        {
          "id": "basic_attack",
          "nameKey": "skill.basic_attack.name",
          "descriptionKey": "skill.basic_attack.description",
          "takesTurn": true,
          "target": "Enemy",
          "range": 1,
          "cooldown": 1,
          "damageKind": "Physical",
          "multiplier": 1.0,
          "penetration": 0,
          "selfStatusId": null,
          "weaponKind": "sword"
        }
        """)!.AsObject();

    private static JsonObject Defend() => JsonNode.Parse("""
        {
          "id": "defend",
          "nameKey": "skill.defend.name",
          "descriptionKey": "skill.defend.description",
          "takesTurn": true,
          "target": "Self",
          "range": 0,
          "cooldown": 1,
          "damageKind": null,
          "multiplier": null,
          "penetration": null,
          "selfStatusId": "block",
          "weaponKind": null
        }
        """)!.AsObject();
}
