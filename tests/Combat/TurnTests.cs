using Tinderhearth.Rules.Combat;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>一个人自己的一个回合：行动力回满，一回合一件占回合的事、做完减半，换成再走满一次，用完才算完。</summary>
public class TurnTests
{
    [Fact]
    public void 回合开始行动力是满的_那一件还没用()
    {
        var turn = new Turn(5);

        Assert.Equal(5, turn.ActionPoints);
        Assert.Equal(5, turn.FullActionPoints);
        Assert.False(turn.TurnActionUsed);
        Assert.False(turn.IsOver);
        Assert.Equal(10, turn.BudgetIfConverted);
    }

    [Fact]
    public void 一回合只做得了一件占回合的事_第二件被拒_被拒的那一下什么都不改()
    {
        var turn = new Turn(5);

        Assert.True(turn.TryTakeTurnAction());
        Assert.Equal(2, turn.ActionPoints);

        Assert.False(turn.TryTakeTurnAction());
        Assert.Equal(2, turn.ActionPoints);
        Assert.True(turn.TurnActionUsed);
    }

    [Fact]
    public void 一步没走就做占回合的事_满的行动力减半向下取整()
    {
        var turn = new Turn(5);

        turn.TryTakeTurnAction();

        Assert.Equal(2, turn.ActionPoints);
    }

    /// <summary>工作区根 desc/各类公式计算.md 的「行动力」那个例子：有 5 点，先走 2 点再出手，剩下的 3 点减成 1 点。</summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    public void 走过之后再做占回合的事_剩下的行动力减半向下取整(int walked, int left)
    {
        var turn = new Turn(5);
        turn.Spend(walked);

        turn.TryTakeTurnAction();

        Assert.Equal(left, turn.ActionPoints);
    }

    [Fact]
    public void 行动力用完_还没做占回合的事时_可以换成再走满一次()
    {
        var turn = new Turn(5);
        turn.Spend(5);
        Assert.False(turn.IsOver);
        Assert.Equal(5, turn.BudgetIfConverted);

        Assert.True(turn.TryConvertToMove());

        Assert.Equal(5, turn.ActionPoints);
        Assert.True(turn.TurnActionUsed);
        Assert.Null(turn.BudgetIfConverted);
    }

    [Fact]
    public void 行动力没用完时也换得了_剩下的加上满满一笔()
    {
        var turn = new Turn(5);
        turn.Spend(2);
        Assert.Equal(8, turn.BudgetIfConverted);

        turn.TryConvertToMove();

        Assert.Equal(8, turn.ActionPoints);
    }

    [Fact]
    public void 换过之后占回合的事被拒_做过之后也换不了()
    {
        var converted = new Turn(5);
        converted.TryConvertToMove();
        var acted = new Turn(5);
        acted.TryTakeTurnAction();

        Assert.False(converted.TryTakeTurnAction());
        Assert.Equal(10, converted.ActionPoints);
        Assert.False(converted.TryConvertToMove());
        Assert.False(acted.TryConvertToMove());
        Assert.Equal(2, acted.ActionPoints);
    }

    [Fact]
    public void 行动力用完而且那一件用掉了_这一回合才算完()
    {
        var walkedOut = new Turn(5);
        walkedOut.Spend(5);
        var acted = new Turn(5);
        acted.TryTakeTurnAction();

        Assert.False(walkedOut.IsOver);
        Assert.False(acted.IsOver);

        walkedOut.TryTakeTurnAction();
        acted.Spend(2);

        Assert.True(walkedOut.IsOver);
        Assert.True(acted.IsOver);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(0)]
    [InlineData(-1)]
    public void 花的比剩下的多_或者不是正数_当场报错_行动力不扣(int points)
    {
        var turn = new Turn(5);

        Assert.Throws<ArgumentOutOfRangeException>(() => turn.Spend(points));
        Assert.Equal(5, turn.ActionPoints);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void 满的行动力不是正数_当场报错(int full)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Turn(full));
    }
}
