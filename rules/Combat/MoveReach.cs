using Tinderhearth.Rules.Economy;

namespace Tinderhearth.Rules.Combat;

/// <summary>轮到的那个人此刻能走到哪：用剩下的行动力走得到的，与要把那一件占回合的事换成再走满一次才走得到的。</summary>
/// <remarks>
/// 两种格子不重叠：剩下的行动力走得到的，就不算换了才走得到的，界面把两种画成不同的形状。指着一格时画的路，
/// 与点下去走的路是同一条。
/// </remarks>
public sealed class MoveReach
{
    private readonly MovementMap _withRemaining;
    private readonly MovementMap? _ifConverted;

    internal MoveReach(MovementMap withRemaining, MovementMap? ifConverted)
    {
        _withRemaining = withRemaining;
        _ifConverted = ifConverted;
        WithRemaining = withRemaining.Reachable.Keys.ToHashSet();
        OnlyByConverting = ifConverted is null
            ? new HashSet<GridCell>()
            : ifConverted.Reachable.Keys.Where(cell => !WithRemaining.Contains(cell)).ToHashSet();
    }

    /// <summary>用剩下的行动力走得到、停得下的格子。</summary>
    public IReadOnlySet<GridCell> WithRemaining { get; }

    /// <summary>要把那一件换成再走满一次才走得到、停得下的格子；那一件已经用掉了就一格都没有。</summary>
    public IReadOnlySet<GridCell> OnlyByConverting { get; }

    /// <summary>走到这一格的那一条路，不含起点、含终点；走不到、停不下的是 <c>null</c>。</summary>
    public IReadOnlyList<GridCell>? PathTo(GridCell cell) => MapFor(cell)?.PathTo(cell);

    /// <summary>走到这一格一共花多少行动力，换了才走得到的也从此刻这一格算起；走不到、停不下的是 <c>null</c>。</summary>
    public int? CostTo(GridCell cell) => MapFor(cell)?.CostTo(cell);

    private MovementMap? MapFor(GridCell cell) => cell switch
    {
        _ when WithRemaining.Contains(cell) => _withRemaining,
        _ when OnlyByConverting.Contains(cell) => _ifConverted,
        _ => null,
    };
}
