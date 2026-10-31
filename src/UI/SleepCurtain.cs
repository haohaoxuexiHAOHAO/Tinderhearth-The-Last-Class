using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>睡下与醒来时盖住画面的那一块：作者在 UIRoot 下面摆的，挂在遮罩层。</summary>
/// <remarks>
/// 淡出与淡入是作者做的两段动画，快慢由那两段自己决定，代码里没有时长；代码只放、只等它们播完。像素画面不许用
/// 半透明淡出，要一档一档往暗端走（设计仓 production/像素绘制原则.md），所以这两段怎么画全在作者那一侧。
///
/// 它不进导航栈：栈里有东西世界就暂停，而睡下到醒来这一段不碰暂停（设计仓 design/每日结算系统.md 的
/// 「结算期间没有输入入口」一节）。遮罩层在弹窗那一层之上，所以淡入播完就把自己藏起来，否则会盖住随即弹出的窗。
///
/// 两段都要能播完：不存在的、循环的、长度为零的，启动时当场报错。播不完的话「播完」永远不来，输入就一直关着。
/// </remarks>
public sealed partial class SleepCurtain : Control, IPlacedUI
{
    private AnimationPlayer _animator = null!;
    private Action? _onFinished;
    private StringName? _playing;

    /// <summary>放那两段的动画播放器，作者摆在这一块下面。</summary>
    [Export]
    public AnimationPlayer? Animator { get; set; }

    /// <summary>睡下时播的那一段的名字：从看得见画面播到整屏盖住。</summary>
    [Export]
    public StringName FadeOut { get; set; } = new();

    /// <summary>结算完播的那一段的名字：从整屏盖住播回看得见画面。</summary>
    [Export]
    public StringName FadeIn { get; set; } = new();

    /// <summary>这一块挂在遮罩层。</summary>
    public UILayer Layer => UILayer.Curtain;

    /// <summary>有一段正在播。</summary>
    public bool IsPlaying => _playing is not null;

    public override void _Ready()
    {
        _animator = Animator ?? throw new InvalidOperationException(
            $"{nameof(SleepCurtain)}（节点 {Name}）的 {nameof(Animator)} 没接上 —— 它要的是放淡出与淡入那两段的 AnimationPlayer");

        Require(nameof(FadeOut), FadeOut);
        Require(nameof(FadeIn), FadeIn);

        // 平时不盖着：作者在编辑器里可能让它显着，好对着它画那两段。
        Visible = false;
    }

    // 订阅跟着进出树走：界面根会把本节点挪到遮罩层，挪的时候它离开再进一次树，_Ready 却不再跑（见 IPlacedUI）。
    public override void _EnterTree()
    {
        if (Animator is not null)
        {
            Animator.AnimationFinished += OnAnimationFinished;
        }
    }

    public override void _ExitTree()
    {
        if (Animator is not null)
        {
            Animator.AnimationFinished -= OnAnimationFinished;
        }
    }

    /// <summary>播淡出：先把这一块显出来，播完调一次 <paramref name="onFinished"/>。播完之后它一直盖着，等淡入。</summary>
    public void PlayFadeOut(Action onFinished)
    {
        Visible = true;
        Play(FadeOut, onFinished);
    }

    /// <summary>播淡入：播完把这一块藏起来，再调一次 <paramref name="onFinished"/>。</summary>
    public void PlayFadeIn(Action onFinished) => Play(FadeIn, () =>
    {
        Visible = false;
        onFinished();
    });

    /// <summary>中途出了缺陷时把这一块撤掉，免得画面一直盖着。</summary>
    public void Abort()
    {
        _playing = null;
        _onFinished = null;
        Visible = false;
    }

    private void Play(StringName animation, Action onFinished)
    {
        ArgumentNullException.ThrowIfNull(onFinished);
        if (_playing is not null)
        {
            throw new InvalidOperationException($"{nameof(SleepCurtain)}（节点 {Name}）还在播「{_playing}」，又要播「{animation}」");
        }

        _playing = animation;
        _onFinished = onFinished;
        _animator.Play(animation);
    }

    /// <summary>那一段播完了：推迟到这一帧末尾再告诉等它的那一方。</summary>
    /// <remarks>
    /// 不在这个信号里当场接着放下一段：4.7.2 的播放器先发「播完」，再发一次「当前动画变成空」
    /// （引擎源码 scene/animation/animation_player.cpp 的 _blend_post_process）。当场放的话，刚放上的那一段后面紧跟着
    /// 一句不对的「当前动画变成空」。推迟一下，下一段就不夹在播放器自己收尾的中间。
    /// </remarks>
    private void OnAnimationFinished(StringName animation)
    {
        if (_playing is null || animation != _playing)
        {
            return;
        }

        var onFinished = _onFinished;
        _playing = null;
        _onFinished = null;
        if (onFinished is not null)
        {
            Callable.From(onFinished).CallDeferred();
        }
    }

    private void Require(string exportName, StringName animation)
    {
        var name = animation.ToString();
        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException(
                $"{nameof(SleepCurtain)}（节点 {Name}）的 {exportName} 没填 —— 要填 {_animator.Name} 里那一段动画的名字");
        }

        if (!_animator.HasAnimation(animation))
        {
            throw new InvalidOperationException(
                $"{nameof(SleepCurtain)}（节点 {Name}）的 {exportName} 填的是「{name}」，{_animator.Name} 里没有这一段，"
                    + $"有的是：{string.Join("、", _animator.GetAnimationList())}");
        }

        var clip = _animator.GetAnimation(animation);
        if (clip.LoopMode != Animation.LoopModeEnum.None)
        {
            throw new InvalidOperationException(
                $"{nameof(SleepCurtain)}（节点 {Name}）的「{name}」那一段设成了循环 —— 循环的那一段永远播不完，"
                    + "睡下之后输入就一直关着；在动画面板里把循环关掉");
        }

        if (clip.Length <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(SleepCurtain)}（节点 {Name}）的「{name}」那一段长度是 {clip.Length} 秒 —— 要比零长");
        }
    }
}
