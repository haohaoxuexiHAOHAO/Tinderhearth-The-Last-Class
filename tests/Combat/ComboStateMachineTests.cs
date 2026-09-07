using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>
/// `GP-10` 连段状态机的守卫：三相时序、判定框只在命中相开、后摇取消窗续段、窗外回落、
/// 末段不再续、空中落地打断、轻重不混连。这些错了在实机上只表现为「手感怪」，不报错。
/// </summary>
public class ComboStateMachineTests
{
    private static ComboStateMachine StartLight()
    {
        var m = new ComboStateMachine();
        m.Tick(lightPressed: true, heavyPressed: false, isOnFloor: true);
        return m;
    }

    private static void AdvanceToComboWindow(ComboStateMachine m)
    {
        var guard = 0;
        while (!m.IsComboWindowOpen && guard++ < 200)
        {
            m.Tick(false, false, true);
        }
    }

    [Fact]
    public void 空闲时按轻攻击进入前摇且判定框不开()
    {
        var m = StartLight();
        Assert.True(m.IsAttacking);
        Assert.Equal(ComboKind.Light, m.Kind);
        Assert.Equal(AttackPhase.Startup, m.Phase);
        Assert.Equal(0, m.Step);
        Assert.False(m.IsHitActive);
    }

    [Fact]
    public void 两键同帧按下时轻攻击优先()
    {
        var m = new ComboStateMachine();
        m.Tick(lightPressed: true, heavyPressed: true, isOnFloor: true);
        Assert.Equal(ComboKind.Light, m.Kind);
    }

    [Fact]
    public void 判定框只在命中相开且恰好持续登记的帧数()
    {
        var m = StartLight();
        var active = m.IsHitActive ? 1 : 0;
        for (var i = 0; i < 40; i++)
        {
            m.Tick(false, false, true);
            if (m.IsHitActive)
            {
                active++;
            }
        }

        Assert.Equal(CombatFeel.LightActiveFrames, active);
    }

    [Fact]
    public void 后摇取消窗内按同键续下一段()
    {
        var m = StartLight();
        AdvanceToComboWindow(m);

        Assert.True(m.IsComboWindowOpen);
        Assert.Equal(0, m.Step);

        m.Tick(lightPressed: true, heavyPressed: false, isOnFloor: true);

        Assert.Equal(1, m.Step);
        Assert.Equal(AttackPhase.Startup, m.Phase);
    }

    [Fact]
    public void 窗外不按键就回到待机()
    {
        var m = StartLight();
        for (var i = 0; i < 80; i++)
        {
            m.Tick(false, false, true);
        }

        Assert.False(m.IsAttacking);
        Assert.Equal(ComboKind.None, m.Kind);
    }

    [Fact]
    public void 连段打到末段后取消窗不再开且走完回到待机()
    {
        var m = StartLight();
        for (var step = 0; step < CombatFeel.LightChainLength - 1; step++)
        {
            AdvanceToComboWindow(m);
            m.Tick(true, false, true);
        }

        Assert.Equal(CombatFeel.LightChainLength - 1, m.Step);

        var everOpen = false;
        for (var i = 0; i < 80 && m.IsAttacking; i++)
        {
            if (m.IsComboWindowOpen)
            {
                everOpen = true;
            }

            m.Tick(false, false, true);
        }

        Assert.False(everOpen);
        Assert.False(m.IsAttacking);
    }

    [Fact]
    public void 轻连段里按重攻击不续段也不改类型()
    {
        var m = StartLight();
        AdvanceToComboWindow(m);

        m.Tick(lightPressed: false, heavyPressed: true, isOnFloor: true);

        Assert.Equal(0, m.Step);
        Assert.Equal(ComboKind.Light, m.Kind);
    }

    [Fact]
    public void 空中起手的连段落地打断()
    {
        var m = new ComboStateMachine();
        m.Tick(lightPressed: true, heavyPressed: false, isOnFloor: false);
        Assert.True(m.IsAttacking);

        m.Tick(false, false, isOnFloor: false);
        m.Tick(false, false, isOnFloor: true);

        Assert.False(m.IsAttacking);
        Assert.Equal(ComboKind.None, m.Kind);
    }

    [Fact]
    public void 地面起手的连段不因踩在地面而被打断()
    {
        var m = StartLight();
        m.Tick(false, false, isOnFloor: true);
        Assert.True(m.IsAttacking);
    }
}
