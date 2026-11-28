using System.Text.Json.Nodes;
using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>面板照「属性换算成面板」那张表从属性算出，换算参数都从配置读。</summary>
public class CombatPanelTests
{
    private static readonly string[] Items =
    [
        "hpMax", "mpMax", "hpRegen", "mpRegen", "physicalAttack", "magicAttack", "physicalDefense", "magicDefense",
        "toughnessMax", "hitRate", "dodgeRate", "physicalCritChance", "magicCritChance", "physicalCritMultiplier",
        "magicCritMultiplier",
    ];

    /// <summary>四项属性取不同的量级，每一项算出来是哪个量级，就是看的哪一项属性。</summary>
    [Fact]
    public void 每一项看的是那张表里写的那一项属性()
    {
        var config = Config(perPoint: 1, baseValue: 0);
        var attributes = new CharacterAttributes { Strength = 1, Agility = 10, Constitution = 100, Intellect = 1000 };

        var panel = CombatPanel.From(attributes, config.Panel);

        Assert.Equal(
            new CombatPanel(
                HpMax: 100, MpMax: 1000, HpRegen: 100, MpRegen: 1000, PhysicalAttack: 1, MagicAttack: 1000,
                PhysicalDefense: 100, MagicDefense: 100, ToughnessMax: 100, HitRate: 10, DodgeRate: 10,
                PhysicalCritChance: 10, MagicCritChance: 10, PhysicalCritMultiplier: 1, MagicCritMultiplier: 1000),
            panel);
    }

    [Fact]
    public void 初始值加每点给的乘属性_点数类向下取整_几率与倍率留小数()
    {
        var config = Config(perPoint: 0.3, baseValue: 0.5);
        var attributes = new CharacterAttributes { Strength = 3, Agility = 3, Constitution = 3, Intellect = 3 };

        var panel = CombatPanel.From(attributes, config.Panel);

        Assert.Equal(1, panel.HpMax);
        Assert.Equal(1, panel.PhysicalAttack);
        Assert.Equal(1.4, panel.HitRate, precision: 10);
        Assert.Equal(1.4, panel.PhysicalCritMultiplier, precision: 10);
    }

    /// <summary>1.15 × 100 在浮点里是 114.99999999999999，直接取整会少 1 点。</summary>
    [Fact]
    public void 本该是整数的不因浮点误差少1点()
    {
        var config = Config(perPoint: 1.15, baseValue: 0);
        var attributes = new CharacterAttributes { Strength = 100, Agility = 100, Constitution = 100, Intellect = 100 };

        var panel = CombatPanel.From(attributes, config.Panel);

        Assert.Equal(115, panel.HpMax);
        Assert.Equal(115, panel.PhysicalAttack);
    }

    /// <summary>配置里每一项都要写：缺了不许悄悄落成 0。</summary>
    [Fact]
    public void 配置里缺任一项载入被拒并点名那一项()
    {
        foreach (var item in Items)
        {
            var json = ConfigJson(perPoint: 1, baseValue: 0);
            json["panel"]!.AsObject().Remove(item);

            var error = Record.Exception(() => CombatConfig.Parse(json.ToJsonString()));

            Assert.NotNull(error);
            Assert.Contains(item, error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("base")]
    [InlineData("perPoint")]
    public void 换算缺初始值或每点给多少载入被拒(string key)
    {
        var json = ConfigJson(perPoint: 1, baseValue: 0);
        json["panel"]!["hpMax"]!.AsObject().Remove(key);

        var error = Record.Exception(() => CombatConfig.Parse(json.ToJsonString()));

        Assert.NotNull(error);
        Assert.Contains(key, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 换算写负数载入被拒()
    {
        var json = ConfigJson(perPoint: 1, baseValue: 0);
        json["panel"]!["dodgeRate"]!["perPoint"] = -0.1;

        Assert.NotNull(Record.Exception(() => CombatConfig.Parse(json.ToJsonString())));
    }

    private static CombatConfig Config(double perPoint, double baseValue) =>
        CombatConfig.Parse(ConfigJson(perPoint, baseValue).ToJsonString());

    private static JsonObject ConfigJson(double perPoint, double baseValue)
    {
        var panel = new JsonObject();
        foreach (var item in Items)
        {
            panel[item] = new JsonObject { ["base"] = baseValue, ["perPoint"] = perPoint };
        }

        return new JsonObject
        {
            ["panel"] = panel,
            ["movement"] = new JsonObject { ["stepCost"] = 1, ["extraBesideEnemy"] = 1 },
            ["hit"] = new JsonObject { ["minChance"] = 0.05, ["maxChance"] = 0.95, ["dodgeByDistance"] = new JsonArray(0.5, 1.0) },
            ["damage"] = new JsonObject { ["defenseK"] = 60, ["blockReduction"] = 0.5 },
        };
    }
}
