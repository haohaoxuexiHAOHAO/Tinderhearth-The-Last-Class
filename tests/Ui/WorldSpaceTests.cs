using Tinderhearth.Rules.Ui;
using Xunit;

namespace Tinderhearth.Rules.Tests.Ui;

/// <summary>
/// 世界空间 UI 的规则层测试（`UI-9`）：几何关系、读条/血条/伤害数字的视图模型契约。
/// 风格照 <c>HudTests</c> —— 钉住关系、坏值反证、边界钳制。
/// </summary>
public class WorldSpaceTests
{
    // ── 层级（UI-9 依赖 UI-6 的世界空间层）────────────────────────────
    [Fact]
    public void 世界空间层在世界之上而在HUD之下()
    {
        Assert.True(UiLayer.World < UiLayer.WorldSpace, "世界空间 UI 要盖在世界内容之上");
        Assert.True(UiLayer.WorldSpace < UiLayer.Hud, "读条画在执行者身上，混进 HUD 会固定在屏幕角落");
    }

    // ── 几何关系 ──────────────────────────────────────────────────────
    [Fact]
    public void 读条圆环直径等于小图标档且是半径两倍()
    {
        Assert.Equal(UiMetrics.IconSmall, WorldUiLayout.RingDiameter);
        Assert.Equal(WorldUiLayout.RingDiameter, WorldUiLayout.RingRadius * 2);
    }

    [Fact]
    public void 进度弧半径落在墨环内侧()
    {
        Assert.True(WorldUiLayout.ArcRadius < WorldUiLayout.RingRadius, "弧要压在底环内侧，不超出圆外");
        Assert.True(WorldUiLayout.ArcRadius > 0, "弧半径要是正数");
    }

    [Fact]
    public void 精英血条宽对齐剪影而高为内边距量级()
    {
        Assert.Equal(UiMetrics.IconLarge, WorldUiLayout.EliteBarWidth);
        Assert.Equal(UiMetrics.PanelPadding, WorldUiLayout.EliteBarHeight);
    }

    [Fact]
    public void 精英血条浮在剪影头顶以上()
    {
        Assert.True(WorldUiLayout.EliteBarOffsetY < 0, "向上为负：血条要在剪影上方");
        Assert.True(WorldUiLayout.EliteBarOffsetY <= -(UiMetrics.IconLarge / 2), "血条要完全在头顶以上、不压脸");
    }

    [Fact]
    public void 伤害数字从头顶起向上飘一个基础单位()
    {
        Assert.Equal(-(UiMetrics.IconLarge / 2), WorldUiLayout.DamageStartOffsetY);
        Assert.Equal(UiMetrics.BaseUnit, WorldUiLayout.DamageFloatDistancePx);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void 伤害数字飘升在两端对齐起点与终点(double life)
    {
        double expected = WorldUiLayout.DamageStartOffsetY - WorldUiLayout.DamageFloatDistancePx * life;
        Assert.Equal(expected, WorldUiLayout.DamageRiseAt(life), 3);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(1.5, 1.0)]
    public void 伤害数字飘升比例越界被钳(double life, double clampedLife)
    {
        double expected = WorldUiLayout.DamageStartOffsetY - WorldUiLayout.DamageFloatDistancePx * clampedLife;
        Assert.Equal(expected, WorldUiLayout.DamageRiseAt(life), 3);
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
        Assert.False(WorldUiOptions.Default.ShowDamageNumbers, "正典：伤害数字默认关闭，设置里可开");
        Assert.True(new WorldUiOptions(ShowDamageNumbers: true).ShowDamageNumbers);
    }
}
