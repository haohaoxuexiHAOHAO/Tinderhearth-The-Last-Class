using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>
/// 画在角色身上那一层界面：几何关系、目标报的头顶与体型档，以及读条、精英血条、伤害数字这三个视图模型的约定。
/// </summary>
/// <remarks>写法与 <c>HudTests</c> 一致：测量之间的关系、拿坏值反证、核对边界钳制。</remarks>
public class WorldSpaceTests
{
    // ── 层级 ──────────────────────────────────────────────────────────
    [Fact]
    public void 世界空间层在世界之上而在HUD之下()
    {
        Assert.True(UILayer.World < UILayer.WorldSpace, "世界空间 UI 要盖在世界内容之上");
        Assert.True(UILayer.WorldSpace < UILayer.Hud, "读条画在执行者身上，混进 HUD 会固定在屏幕角落");
    }

    // ── 几何关系 ──────────────────────────────────────────────────────
    [Fact]
    public void 读条圆环居中贴在执行者身上时不落半格()
    {
        // 圆环往左上各挪半张，边长是奇数就挪到半个像素上，画面发虚。它与画好的那张一样大另有一条测试
        // （UIArtTests 里按排版单位核尺寸那一条）。
        Assert.Equal(0, WorldUILayout.RingWidth % 2);
        Assert.Equal(0, WorldUILayout.RingHeight % 2);
        Assert.Equal(UIMetrics.IconSmall, WorldUILayout.RingHeight);
    }

    [Fact]
    public void 精英血条与HUD资源条一样高()
    {
        // 一样高才共用得了那一套槽与填充：画好的槽就是这么高（UIArtTests 里按排版单位核尺寸那一条）。
        Assert.Equal(HudLayout.GaugeBarHeight, WorldUILayout.EliteBarHeight);
    }

    [Fact]
    public void 精英血条宽按体型档给小与人形一档大体型一档()
    {
        // 人形对齐剪影宽的上限；大体型与 HUD 资源条一样长，一下掉多少比得出来。
        Assert.Equal(UIMetrics.IconLarge, WorldUILayout.EliteBarWidthOf(BodyTier.Small));
        Assert.Equal(UIMetrics.IconLarge, WorldUILayout.EliteBarWidthOf(BodyTier.Human));
        Assert.Equal(HudLayout.GaugeBarWidth, WorldUILayout.EliteBarWidthOf(BodyTier.Large));
    }

    [Fact]
    public void 体型档没报就给不出血条宽()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldUILayout.EliteBarWidthOf(BodyTier.Unset));
    }

    [Fact]
    public void 精英血条横着居中在脚底上不落半格()
    {
        // 宽是奇数时挪半根就挪到半个像素上，画面发虚。
        foreach (var tier in Enum.GetValues<BodyTier>().Where(t => t != BodyTier.Unset))
        {
            Assert.Equal(-WorldUILayout.EliteBarWidthOf(tier), WorldUILayout.EliteBarLeftX(tier) * 2);
        }
    }

    [Theory]
    [InlineData(28)]
    [InlineData(48)]
    [InlineData(80)]
    public void 精英血条整根浮在目标报的头顶以上(int head)
    {
        // 向上为负。条的底边离头顶空一个组内间距，所以不压住脸。
        var anchor = WorldUIAnchor.Of("精英", head, BodyTier.Large);
        var bottom = WorldUILayout.EliteBarTopY(anchor) + WorldUILayout.EliteBarHeight;
        Assert.Equal(-(head + UIMetrics.ItemGap), bottom);
    }

    [Fact]
    public void 血条与伤害数字的高度跟着目标报的头顶走()
    {
        // 两个只差身高的目标，偏移正好差那么多：偏移是从报的高度算出来的，不是一个写死的半身高。
        var human = WorldUIAnchor.Of("人形精英", 48, BodyTier.Human);
        var large = WorldUIAnchor.Of("大体型精英", 80, BodyTier.Large);

        Assert.Equal(80 - 48, WorldUILayout.EliteBarTopY(human) - WorldUILayout.EliteBarTopY(large));
        Assert.Equal(80 - 48, WorldUILayout.DamageStartOffsetY(human) - WorldUILayout.DamageStartOffsetY(large));
    }

    [Fact]
    public void 伤害数字从头顶起向上飘一个基础单位()
    {
        var anchor = WorldUIAnchor.Of("精英", 48, BodyTier.Human);
        Assert.Equal(-48, WorldUILayout.DamageStartOffsetY(anchor));
        Assert.Equal(UIMetrics.BaseUnit, WorldUILayout.DamageFloatDistancePx);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    public void 伤害数字飘升两端对齐起点与终点而越界被钳(double life, double clampedLife)
    {
        var anchor = WorldUIAnchor.Of("精英", 48, BodyTier.Human);
        double expected = WorldUILayout.DamageStartOffsetY(anchor) - WorldUILayout.DamageFloatDistancePx * clampedLife;
        Assert.Equal(expected, WorldUILayout.DamageRiseAt(life, anchor), 3);
    }

    // ── 目标报的那两样 ────────────────────────────────────────────────
    [Fact]
    public void 报全了就造得出()
    {
        var anchor = WorldUIAnchor.Of("精英", 64, BodyTier.Large);
        Assert.Equal(64, anchor.HeadHeightWorldPx);
        Assert.Equal(BodyTier.Large, anchor.Tier);
    }

    [Theory]
    [InlineData(0, BodyTier.Human, "头顶高度")]
    [InlineData(-3, BodyTier.Human, "头顶高度")]
    [InlineData(48, BodyTier.Unset, "体型档")]
    [InlineData(48, (BodyTier)99, "体型档")]
    public void 目标缺哪一项就点名是谁缺了哪一项(int head, BodyTier tier, string missing)
    {
        var error = Assert.Throws<ArgumentException>(() => WorldUIAnchor.Of("精英甲", head, tier));
        Assert.Contains("「精英甲」", error.Message);
        Assert.Contains(missing, error.Message);
    }

    [Fact]
    public void 两项都没报就一次说全()
    {
        var error = Assert.Throws<ArgumentException>(() => WorldUIAnchor.Of("精英甲", 0, BodyTier.Unset));
        Assert.Contains("头顶高度", error.Message);
        Assert.Contains("体型档", error.Message);
    }

    // ── 读条状态 ──────────────────────────────────────────────────────
    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.3, 0.3)]
    [InlineData(1.5, 1.0)]
    public void 读条进度钳在零到一之间(double progress, double expected)
    {
        Assert.Equal(expected, new CastState(true, progress, false).ClampedProgress, 3);
    }

    [Fact]
    public void 读条激活且未中断才显示()
    {
        Assert.True(new CastState(true, 0.5, false).Visible);
        Assert.False(new CastState(true, 0.5, true).Visible, "受击中断后不显示");
        Assert.False(new CastState(false, 0.5, false).Visible, "没在读条时不显示");
    }

    [Fact]
    public void 空读条状态不显示()
    {
        Assert.False(CastState.Idle.Visible);
    }

    // ── 精英血条 ──────────────────────────────────────────────────────
    [Theory]
    [InlineData(EnemyRank.Trash, false)]
    [InlineData(EnemyRank.Elite, true)]
    [InlineData(EnemyRank.Boss, true)]
    public void 血条只给精英与BOSS杂兵没有(EnemyRank rank, bool shows)
    {
        Assert.Equal(shows, new EliteHealth(10, 20, rank).ShowsBar);
    }

    [Theory]
    [InlineData(0, 20, 0.0)]
    [InlineData(10, 20, 0.5)]
    [InlineData(20, 20, 1.0)]
    [InlineData(30, 20, 1.0)]
    public void 血量比例钳在零到一之间(int current, int max, double expected)
    {
        Assert.Equal(expected, new EliteHealth(current, max, EnemyRank.Elite).Ratio, 3);
    }

    [Fact]
    public void 血量上限为零时比例记零不除零()
    {
        Assert.Equal(0.0, new EliteHealth(0, 0, EnemyRank.Elite).Ratio, 3);
    }

    [Fact]
    public void 负血量抛异常而不是静默钳()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new EliteHealth(-1, 20, EnemyRank.Elite));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new EliteHealth(10, -1, EnemyRank.Elite));
    }

    // ── 呈现开关 ──────────────────────────────────────────────────────
    [Fact]
    public void 伤害数字默认关闭而可开()
    {
        Assert.False(WorldUIOptions.Default.ShowDamageNumbers, "正典：伤害数字默认关闭，设置里可开");
        Assert.True(new WorldUIOptions(ShowDamageNumbers: true).ShowDamageNumbers);
    }
}
