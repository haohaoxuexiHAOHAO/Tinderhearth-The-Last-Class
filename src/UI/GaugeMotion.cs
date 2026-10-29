using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>资源条与精力管怎么动的那几个量，作者在检查器里调：回复时追多快、扣减那一截停多久、中段流多快、气泡浮多快。</summary>
/// <remarks>
/// 都没有能用的默认值：没填就是 0，启动时当场报错（<see cref="Require"/>），不悄悄按某个数动。怎么追、怎么停、怎么挪
/// 在 <see cref="GaugeChase"/> 与 <see cref="GaugeFlow"/>，这里只把检查器里的数交过去。填多少实机试。
/// </remarks>
[GlobalClass]
public partial class GaugeMotion : Resource
{
    /// <summary>显示的长度朝数值走多快，界面像素每秒。回复时填充按它追上去，扣减那一截停够了按它缩回来。要大于零。</summary>
    [Export]
    public float ChasePixelsPerSecond { get; set; }

    /// <summary>扣减时掉下去那一截留成浅色、停多久再缩，秒。要大于零。</summary>
    [Export]
    public float LagHoldSeconds { get; set; }

    /// <summary>横条往上追的那段时间里，填充中段往右流多快，界面像素每秒。要大于零。</summary>
    [Export]
    public float FlowPixelsPerSecond { get; set; }

    /// <summary>精力管液面中段一直往上浮多快，界面像素每秒。要大于零。</summary>
    [Export]
    public float BubblePixelsPerSecond { get; set; }

    /// <summary>核一份：没接、或哪一格没填，一次说全。</summary>
    /// <param name="motion">检查器里接的那一份。</param>
    /// <param name="owner">谁接的，报错时说清是哪个节点。</param>
    public static GaugeMotionSettings Require(GaugeMotion? motion, string owner)
    {
        if (motion is null)
        {
            throw new InvalidOperationException(
                $"{owner}没接资源条怎么动那一份（{nameof(GaugeMotion)}）—— 在检查器里新建一份，"
                    + "填回复时追多快、扣减那一截停多久、中段流多快、气泡浮多快");
        }

        var problems = new List<string>();
        Check(problems, nameof(ChasePixelsPerSecond), motion.ChasePixelsPerSecond, "追多快，界面像素每秒");
        Check(problems, nameof(LagHoldSeconds), motion.LagHoldSeconds, "那一截停多久，秒");
        Check(problems, nameof(FlowPixelsPerSecond), motion.FlowPixelsPerSecond, "横条中段流多快，界面像素每秒");
        Check(problems, nameof(BubblePixelsPerSecond), motion.BubblePixelsPerSecond, "精力管气泡浮多快，界面像素每秒");

        return problems.Count == 0
            ? new GaugeMotionSettings(
                new GaugeChaseSettings(motion.ChasePixelsPerSecond, motion.LagHoldSeconds),
                motion.FlowPixelsPerSecond,
                motion.BubblePixelsPerSecond)
            : throw new InvalidOperationException(
                $"{owner}的 {nameof(GaugeMotion)} 里 {string.Join("、", problems)} —— 这几个量没有默认值，"
                    + "要在检查器里各填一个大于零的数（填多少实机试）");
    }

    private static void Check(List<string> problems, string name, float value, string what)
    {
        if (!(value > 0))
        {
            problems.Add($"{name} 是 {value}（{what}）");
        }
    }
}
