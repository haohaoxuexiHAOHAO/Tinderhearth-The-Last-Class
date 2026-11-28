using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>一招打一个人：命中几率、伤害与暴击照 desc 的公式算，算完向下取整、最少 1 点。</summary>
/// <remarks>配置照 <see cref="TestBattles.Config"/>：命中 5%–95%，闪避的距离倍率 0.5、0.75、1，K 为 60，「格挡」少一半。</remarks>
public class StrikeOddsTests
{
    private static readonly CombatConfig Config = TestBattles.Config();

    [Theory]
    [InlineData(1, 0.8)]    // 0.9 − 0.2 × 0.5
    [InlineData(2, 0.75)]   // 0.9 − 0.2 × 0.75
    [InlineData(3, 0.7)]    // 0.9 − 0.2 × 1
    [InlineData(9, 0.7)]    // 比那一串还远的取最后一个
    public void 命中几率是攻方命中率减守方面板闪避率乘距离倍率(int distance, double expected)
    {
        var odds = Compute(Panel(hitRate: 0.9), Panel(dodgeRate: 0.2), distance: distance);

        Assert.Equal(expected, odds.HitChance, precision: 10);
    }

    [Theory]
    [InlineData(2.0, 0.0, 0.95)]
    [InlineData(0.1, 1.0, 0.05)]   // 0.1 − 1 × 0.5 是负的
    public void 命中几率限制在5到95之间(double hitRate, double dodgeRate, double expected)
    {
        var odds = Compute(Panel(hitRate: hitRate), Panel(dodgeRate: dodgeRate));

        Assert.Equal(expected, odds.HitChance, precision: 10);
    }

    [Fact]
    public void 伤害是攻击乘技能倍率乘一减防御减免_暴击时再乘暴击倍率()
    {
        // 防御减免 = 20 ÷ (20 + 60) = 0.25。
        var odds = Compute(Panel(attack: 40, critMultiplier: 1.5), Panel(defense: 20));
        var stronger = Compute(Panel(attack: 40, critMultiplier: 1.5), Panel(defense: 20), skill: TestBattles.BasicAttack with { Multiplier = 1.2 });

        Assert.Equal(30, odds.Damage);
        Assert.Equal(45, odds.CriticalDamage);
        Assert.Equal(36, stronger.Damage);
    }

    [Fact]
    public void 算完才向下取整()
    {
        // 40 × (1 − 8 ÷ 68) = 35.29…；暴击 × 1.45 是 51.17…
        var odds = Compute(Panel(attack: 40, critMultiplier: 1.45), Panel(defense: 8));

        Assert.Equal(35, odds.Damage);
        Assert.Equal(51, odds.CriticalDamage);
    }

    /// <summary>100 × 1.15 在浮点里是 114.99999999999999，直接取整会少 1 点。</summary>
    [Fact]
    public void 本该是整数的不因浮点误差少1点()
    {
        var odds = Compute(Panel(attack: 100), Panel(defense: 0), skill: TestBattles.BasicAttack with { Multiplier = 1.15 });

        Assert.Equal(115, odds.Damage);
    }

    [Theory]
    [InlineData(5, 32)]    // 有效防御 15：40 × (1 − 15 ÷ 75)
    [InlineData(20, 40)]   // 扣到 0
    [InlineData(50, 40)]   // 扣过头也只到 0，不放大伤害
    public void 穿透先从防御里扣_最少扣到0(int penetration, int expected)
    {
        var odds = Compute(Panel(attack: 40), Panel(defense: 20), skill: TestBattles.BasicAttack with { Penetration = penetration });

        Assert.Equal(expected, odds.Damage);
    }

    [Fact]
    public void 物理招读物理那一套_魔法招读魔法那一套()
    {
        var attacker = Panel(attack: 40, critChance: 0.1, critMultiplier: 1.5) with
        {
            MagicAttack = 80,
            MagicCritChance = 0.3,
            MagicCritMultiplier = 2.0,
        };
        var defender = Panel(defense: 20) with { MagicDefense = 60 };

        var physical = Compute(attacker, defender);
        var magic = Compute(attacker, defender, skill: TestBattles.BasicAttack with { DamageKind = DamageKind.Magic });

        Assert.Equal((30, 45, 0.1), (physical.Damage, physical.CriticalDamage, physical.CriticalChance));
        Assert.Equal((40, 80, 0.3), (magic.Damage, magic.CriticalDamage, magic.CriticalChance));   // 80 × (1 − 60 ÷ 120)
    }

    [Fact]
    public void 挂着格挡受到的伤害降低_降低多少从配置读()
    {
        var quarter = TestBattles.Config(damage: TestBattles.Damage with { BlockReduction = 0.25 });

        var half = Compute(Panel(attack: 40, critMultiplier: 1.5), Panel(defense: 20), blocking: true);
        var lighter = Compute(Panel(attack: 40, critMultiplier: 1.5), Panel(defense: 20), blocking: true, config: quarter);

        Assert.Equal((15, 22), (half.Damage, half.CriticalDamage));         // 30 × 0.5；45 × 0.5 = 22.5
        Assert.Equal((22, 33), (lighter.Damage, lighter.CriticalDamage));   // 30 × 0.75 = 22.5；45 × 0.75 = 33.75
    }

    [Fact]
    public void 伤害最少1点_挂着格挡与暴击也一样()
    {
        var odds = Compute(Panel(attack: 1, critMultiplier: 1.5), Panel(defense: 1000), blocking: true);

        Assert.Equal((1, 1), (odds.Damage, odds.CriticalDamage));
    }

    [Fact]
    public void 暴击几率是攻方的那一项_限制在0到1之间()
    {
        Assert.Equal(0.25, Compute(Panel(critChance: 0.25), Panel()).CriticalChance, precision: 10);
        Assert.Equal(1.0, Compute(Panel(critChance: 1.5), Panel()).CriticalChance, precision: 10);
    }

    [Fact]
    public void 不打人的招算不出_隔0格当场报错()
    {
        Assert.Throws<ArgumentException>(() => Compute(Panel(), Panel(), skill: TestBattles.Defend));
        Assert.Throws<ArgumentOutOfRangeException>(() => Compute(Panel(), Panel(), distance: 0));
    }

    private static StrikeOdds Compute(
        CombatPanel attacker,
        CombatPanel defender,
        bool blocking = false,
        SkillDefinition? skill = null,
        int distance = 1,
        CombatConfig? config = null) => StrikeOdds.Compute(
            attacker: attacker,
            defender: defender,
            defenderBlocking: blocking,
            skill: skill ?? TestBattles.BasicAttack,
            distance: distance,
            config: config ?? Config);

    /// <summary>一份面板：给的那几项物理与魔法同一个数，其余是不碍事的数。</summary>
    private static CombatPanel Panel(
        int attack = 10,
        int defense = 0,
        double hitRate = 1,
        double dodgeRate = 0,
        double critChance = 0,
        double critMultiplier = 1) => new(
            HpMax: 100,
            MpMax: 10,
            HpRegen: 1,
            MpRegen: 1,
            PhysicalAttack: attack,
            MagicAttack: attack,
            PhysicalDefense: defense,
            MagicDefense: defense,
            ToughnessMax: 10,
            HitRate: hitRate,
            DodgeRate: dodgeRate,
            PhysicalCritChance: critChance,
            MagicCritChance: critChance,
            PhysicalCritMultiplier: critMultiplier,
            MagicCritMultiplier: critMultiplier);
}
