using Tinderhearth.Rules.Combat;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Tests.Progression;
using Xunit;

namespace Tinderhearth.Rules.Tests.Combat;

/// <summary>一间屋：地面、墙与站位，摆错的地方在载入时一次全报并点名哪一格。</summary>
public class BattleRoomTests
{
    /// <summary>6×4 的地面，外面一圈墙。</summary>
    private static readonly GridCell[] Floor =
        [.. Enumerable.Range(1, 6).SelectMany(x => Enumerable.Range(1, 4).Select(y => new GridCell(x, y)))];

    private static readonly GridCell[] Walls =
    [
        .. Enumerable.Range(0, 8).SelectMany(x => (GridCell[])[new(x, 0), new(x, 5)]),
        .. Enumerable.Range(1, 4).SelectMany(y => (GridCell[])[new(0, y), new(7, y)]),
    ];

    private static readonly Dictionary<string, CharacterDefinition> Characters = new()
    {
        ["jin"] = Character("jin"),
        ["xuesheng"] = Character("xuesheng"),
        ["diren1"] = Character("diren1"),
    };

    [Fact]
    public void 摆对了读得出地面_墙与站位_站位照场景里的先后()
    {
        var room = BattleRoom.Create(Floor, Walls, Good(), Characters);

        Assert.True(room.IsFloor(new GridCell(1, 1)));
        Assert.False(room.IsFloor(new GridCell(0, 0)));
        Assert.False(room.IsFloor(new GridCell(20, 20)));
        Assert.Contains(new GridCell(0, 0), room.Walls);
        Assert.Equal(Good(), room.Positions);
    }

    [Fact]
    public void 敌人可以是同一份角色定义的好几个()
    {
        var positions = Good().Append(new StartingPosition(new GridCell(6, 1), "diren1", CombatSide.Enemy));

        Assert.Equal(4, BattleRoom.Create(Floor, Walls, positions, Characters).Positions.Count);
    }

    [Theory]
    [InlineData(0, 0)]   // 墙
    [InlineData(9, 9)]   // 屋子外面
    public void 站位不在地面上被拒并点名那一格(int x, int y)
    {
        var positions = Good().Select(p => p.CharacterId == "diren1" ? p with { Cell = new GridCell(x, y) } : p);

        var error = Assert.Throws<InvalidDataException>(() => BattleRoom.Create(Floor, Walls, positions, Characters));

        Assert.Contains($"站位 ({x}, {y})（diren1）不在地面上", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 两个站位同一格被拒并点名那一格()
    {
        var positions = Good().Select(p => p.CharacterId == "xuesheng" ? p with { Cell = new GridCell(1, 1) } : p);

        var error = Assert.Throws<InvalidDataException>(() => BattleRoom.Create(Floor, Walls, positions, Characters));

        Assert.Contains("(1, 1) 站了不止一个人：jin、xuesheng", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 站位指的角色定义不在册被拒并点名那一格()
    {
        var positions = Good().Append(new StartingPosition(new GridCell(6, 4), "nobody", CombatSide.Enemy));

        var error = Assert.Throws<InvalidDataException>(() => BattleRoom.Create(Floor, Walls, positions, Characters));

        Assert.Contains("站位 (6, 4) 的角色「nobody」不在 characters/ 里", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 我方没有烬被拒()
    {
        var positions = Good().Where(p => p.CharacterId != "jin");

        var error = Assert.Throws<InvalidDataException>(() => BattleRoom.Create(Floor, Walls, positions, Characters));

        Assert.Contains("我方要有烬", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 烬站在敌方或有两个烬被拒()
    {
        var positions = Good().Append(new StartingPosition(new GridCell(6, 2), "jin", CombatSide.Enemy));

        var error = Assert.Throws<InvalidDataException>(() => BattleRoom.Create(Floor, Walls, positions, Characters));

        Assert.Contains("只能站在我方", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 我方没有学生被拒()
    {
        var positions = Good().Where(p => p.CharacterId != "xuesheng");

        var error = Assert.Throws<InvalidDataException>(() => BattleRoom.Create(Floor, Walls, positions, Characters));

        Assert.Contains("至少要有一名学生", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 敌方一个人都没有被拒()
    {
        var positions = Good().Where(p => p.Side != CombatSide.Enemy);

        var error = Assert.Throws<InvalidDataException>(() => BattleRoom.Create(Floor, Walls, positions, Characters));

        Assert.Contains("敌方一个人都没有", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 中立的站位_既是地面又是墙_没有地面都被拒()
    {
        var neutral = Good().Append(new StartingPosition(new GridCell(6, 3), "diren1", CombatSide.Neutral));
        Assert.Contains(
            "阵营只能是我方或敌方",
            Assert.Throws<InvalidDataException>(() => BattleRoom.Create(Floor, Walls, neutral, Characters)).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            "(1, 1) 既是地面又是墙",
            Assert.Throws<InvalidDataException>(() => BattleRoom.Create(Floor, Walls.Append(new GridCell(1, 1)), Good(), Characters)).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            "一格地面都没有",
            Assert.Throws<InvalidDataException>(() => BattleRoom.Create([], Walls, [], Characters)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void 摆错的几处一次全报()
    {
        StartingPosition[] positions =
        [
            new(new GridCell(0, 0), "jin", CombatSide.Ally),
            new(new GridCell(2, 2), "nobody", CombatSide.Enemy),
        ];

        var error = Assert.Throws<InvalidDataException>(() => BattleRoom.Create(Floor, Walls, positions, Characters));

        Assert.Contains("不在地面上", error.Message, StringComparison.Ordinal);
        Assert.Contains("「nobody」", error.Message, StringComparison.Ordinal);
        Assert.Contains("至少要有一名学生", error.Message, StringComparison.Ordinal);
    }

    private static List<StartingPosition> Good() =>
    [
        new(new GridCell(1, 1), "jin", CombatSide.Ally),
        new(new GridCell(1, 2), "xuesheng", CombatSide.Ally),
        new(new GridCell(6, 2), "diren1", CombatSide.Enemy),
    ];

    private static CharacterDefinition Character(string id) =>
        CharacterDefinition.Parse(TestCharacters.Json(id, $"character.{id}.name"), "测试");
}
