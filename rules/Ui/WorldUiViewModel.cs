namespace Tinderhearth.Rules.Ui;

/// <summary>敌人等级。血条只给精英与 BOSS（[战斗与关卡] 正典），杂兵没有。</summary>
public enum EnemyRank
{
    /// <summary>杂兵：无血条。</summary>
    Trash,

    /// <summary>精英：有血条。</summary>
    Elite,

    /// <summary>BOSS：有血条。</summary>
    Boss,
}

/// <summary>
/// 读条状态（世界空间 UI，`UI-9`）。画在执行者身上、随角色移动。
/// </summary>
/// <remarks>
/// 值由调用方传入（脚手架现在、玩法实现将来），本记录只负责钳制与派生 —— 与 `GP-2` 无关，
/// 读条时长这类数值不在本条。
/// </remarks>
public sealed record CastState(bool Active, double Progress, bool Interrupted)
{
    /// <summary>读条进度，钳在 0..1。</summary>
    public double ClampedProgress => System.Math.Clamp(Progress, 0.0, 1.0);

    /// <summary>
    /// 读条是否显示。受击中断后即不显示 —— 本条只保证**表现能中断**；
    /// 「中断是否消耗物品」属玩法实现，不在本条。
    /// </summary>
    public bool Visible => Active && !Interrupted;

    /// <summary>没有读条时的空状态。</summary>
    public static CastState Idle => new(false, 0.0, false);
}

/// <summary>
/// 一个敌人的血量与等级（`UI-9`）。决定它出不出血条、条有多长。
/// </summary>
public sealed record EliteHealth
{
    /// <summary>坏值直接抛 —— 负血量是调用方的错，静默钳会把 bug 藏起来。</summary>
    public EliteHealth(int current, int max, EnemyRank rank)
    {
        if (max < 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(max), max, "血量上限不能为负");
        }

        if (current < 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(current), current, "当前血量不能为负");
        }

        Current = current;
        Max = max;
        Rank = rank;
    }

    /// <summary>当前血量。</summary>
    public int Current { get; }

    /// <summary>血量上限。</summary>
    public int Max { get; }

    /// <summary>敌人等级。</summary>
    public EnemyRank Rank { get; }

    /// <summary>血量比例，钳在 0..1。上限为 0 时记 0（避免除零）。</summary>
    public double Ratio => Max <= 0 ? 0.0 : System.Math.Clamp((double)Current / Max, 0.0, 1.0);

    /// <summary>血条只对精英与 BOSS 出现，杂兵没有血条（正典）。</summary>
    public bool ShowsBar => Rank is EnemyRank.Elite or EnemyRank.Boss;
}

/// <summary>
/// 世界空间 UI 的呈现开关（`UI-9`）。**伤害数字默认关闭**，设置里可开（正典）。
/// </summary>
/// <remarks>
/// 这是表现规则、不是玩法数值：照 <see cref="CameraFeel"/> 先例留在规则层，既不进 `game.json`
/// （那是 mod 可改的内容，mod 不该改表现），也不进 `design/数值模型`。现在由脚手架注入，
/// 将来由设置系统注入 —— 项目暂无设置系统（`src/Main.cs` 里已注明它待建）。
/// </remarks>
public sealed record WorldUiOptions(bool ShowDamageNumbers = false)
{
    /// <summary>默认呈现：伤害数字关。</summary>
    public static WorldUiOptions Default => new();
}
