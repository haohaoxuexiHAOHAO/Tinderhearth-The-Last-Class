using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Actors;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Combat;

/// <summary>一场仗开打时，谁站在哪一格、是哪一边的。</summary>
/// <param name="Cell">站在哪一格。</param>
/// <param name="CharacterId">站的是谁，指一份角色定义。</param>
/// <param name="Side">站在哪一边：我方或敌方。</param>
public sealed record StartingPosition(GridCell Cell, string CharacterId, CombatSide Side);

/// <summary>一场仗打的那一间屋：哪几格是地面、哪几格是墙，以及谁站在哪。</summary>
/// <remarks>
/// 屋子由作者在 Godot 里摆，这里只收摆好的结果，并在载入时一次全报摆错的地方，点名是哪一格。地面以外的格子
/// （墙与屋子外面）都走不进去。站位的先后就是场景里的先后：敏捷一样时按它排（工作区根 desc/战斗规则.md 的「轮与回合」）。
///
/// 我方要有烬、而且只有一个，因为我方阶段永远是他第一个动；还要至少一名学生，因为学生全部倒下判失败
/// （工作区根 desc/战斗规则.md 的「失败判定」），一名学生都没有的屋子开打就是一个说不通的局面。
/// </remarks>
public sealed class BattleRoom
{
    private BattleRoom(IReadOnlySet<GridCell> floor, IReadOnlySet<GridCell> walls, IReadOnlyList<StartingPosition> positions)
    {
        Floor = floor;
        Walls = walls;
        Positions = positions;
    }

    /// <summary>走得进去的格子。</summary>
    public IReadOnlySet<GridCell> Floor { get; }

    /// <summary>墙。只用来画；走不走得进去只看 <see cref="Floor"/>。</summary>
    public IReadOnlySet<GridCell> Walls { get; }

    /// <summary>开打时谁站在哪，照场景里的先后。</summary>
    public IReadOnlyList<StartingPosition> Positions { get; }

    /// <summary>这一格是不是地面。</summary>
    public bool IsFloor(GridCell cell) => Floor.Contains(cell);

    /// <summary>核摆好的那一间屋；摆错的地方一次全报，一行一处。</summary>
    public static BattleRoom Create(
        IEnumerable<GridCell> floor,
        IEnumerable<GridCell> walls,
        IEnumerable<StartingPosition> positions,
        IReadOnlyDictionary<string, CharacterDefinition> characters)
    {
        ArgumentNullException.ThrowIfNull(floor);
        ArgumentNullException.ThrowIfNull(walls);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(characters);

        var floorSet = floor.ToHashSet();
        var wallSet = walls.ToHashSet();
        var list = positions.ToList();
        var problems = new List<string>();

        if (floorSet.Count == 0)
        {
            problems.Add("这一间屋一格地面都没有");
        }

        foreach (var cell in floorSet.Intersect(wallSet).OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            problems.Add($"{cell} 既是地面又是墙");
        }

        foreach (var position in list)
        {
            if (!floorSet.Contains(position.Cell))
            {
                problems.Add($"站位 {position.Cell}（{position.CharacterId}）不在地面上：是墙或在屋子外面");
            }

            if (!characters.ContainsKey(position.CharacterId))
            {
                problems.Add($"站位 {position.Cell} 的角色「{position.CharacterId}」不在 {CharacterDefinition.ContentDirectory}/ 里");
            }

            if (position.Side == CombatSide.Neutral)
            {
                problems.Add($"站位 {position.Cell}（{position.CharacterId}）的阵营只能是我方或敌方");
            }
        }

        foreach (var crowded in list.GroupBy(p => p.Cell).Where(g => g.Count() > 1))
        {
            problems.Add($"{crowded.Key} 站了不止一个人：{string.Join("、", crowded.Select(p => p.CharacterId))}");
        }

        var allies = list.Where(p => p.Side == CombatSide.Ally).ToList();
        var protagonists = allies.Count(p => p.CharacterId == CharacterDefinition.ProtagonistId);
        if (protagonists != 1)
        {
            problems.Add($"我方要有烬（{CharacterDefinition.ProtagonistId}），而且只有一个，实际 {protagonists} 个");
        }

        if (list.Any(p => p.Side == CombatSide.Enemy && p.CharacterId == CharacterDefinition.ProtagonistId))
        {
            problems.Add($"烬（{CharacterDefinition.ProtagonistId}）只能站在我方");
        }

        if (allies.All(p => p.CharacterId == CharacterDefinition.ProtagonistId))
        {
            problems.Add("我方除了烬至少要有一名学生");
        }

        if (list.All(p => p.Side != CombatSide.Enemy))
        {
            problems.Add("敌方一个人都没有");
        }

        return problems.Count == 0
            ? new BattleRoom(floorSet, wallSet, list)
            : throw new InvalidDataException($"这一间屋有 {problems.Count} 处摆得不对：\n{string.Join("\n", problems)}");
    }
}
