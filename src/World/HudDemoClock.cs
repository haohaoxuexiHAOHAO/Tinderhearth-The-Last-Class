using Godot;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World;

/// <summary>主场景预览关卡那一套 HUD 时，让演示数据按 <see cref="HudDemoModel"/> 那一轮起伏走，好在实机看资源条怎么追、怎么留一截。</summary>
/// <remarks>
/// 只在起落到点时换一份视图模型，不每帧换：换一份就是整块 HUD 刷一遍。它跟着世界暂停，所以手环开着时演示也停着。
/// </remarks>
public sealed partial class HudDemoClock : Node
{
    private readonly LevelHud _hud;
    private readonly HudViewModel _start;
    private double _seconds;
    private int _beats;

    /// <summary>接上那一份 HUD 与它开头的样子。</summary>
    public HudDemoClock(LevelHud hud, HudViewModel start)
    {
        Name = "HudDemoClock";
        _hud = hud;
        _start = start;
    }

    public override void _Process(double delta)
    {
        _seconds += delta;
        var beats = HudDemoModel.BeatsPassed(_seconds);
        if (beats == _beats)
        {
            return;
        }

        _beats = beats;
        _hud.Model = HudDemoModel.At(_start, _seconds);
    }
}
