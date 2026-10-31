using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>即时提示那一行的屏幕部分：作者在界面根下面摆一块、块里摆一行字，代码只往里填字、停够了收起。</summary>
/// <remarks>
/// 出不出、顶不顶掉、什么时候收判在规则层（<see cref="InstantNotice"/>），写哪一句由被拒的那一方挑，这里不挑字。
/// 它不接管输入、不进导航栈，所以出它的那一帧世界照常推进；鼠标过滤是忽略，铺满一屏的这一块不吃点击。
///
/// 作者把它摆成 <see cref="UIRoot"/> 的子节点：界面根就绪时把它挪到即时提示那一层（<see cref="IPlacedUI"/>），并把这一块
/// 铺满一屏，所以那一行摆在哪由块里那个 <see cref="Label"/> 自己的锚点定，块里还垫不垫别的由作者定。字体、字色从这里
/// 装的像素主题来，那一行的 Theme Type Variation 由作者挑。停多久填在检查器里，没有默认值。
/// </remarks>
public sealed partial class InstantNoticeView : Control, IPlacedUI
{
    private InstantNotice _notice = null!;
    private Label _line = null!;

    /// <summary>那一行字。摆在这一块下面，位置与样子由作者定；字由代码填。</summary>
    [Export]
    public Label? Line { get; set; }

    /// <summary>素材资源。像素主题从它装，缺了或缺格当场报错并一次列全。</summary>
    /// <remarks>摆进场景时接的是 <c>assets/self-drawn/ui/ui_art.tres</c>，与随身栏接的是同一份。</remarks>
    [Export]
    public UIArt? Art { get; set; }

    /// <summary>一条停多久，秒。没有默认值：缺了或不大于零当场报错。</summary>
    /// <remarks>填多少实机试 —— 要停得够读完一行，又不能挂到玩家下一个动作之后。值归设计仓数值模型那张尚未给值的表。</remarks>
    [Export]
    public double SecondsShown { get; set; }

    /// <summary>这一行挂在即时提示那一层。</summary>
    public UILayer Layer => UILayer.Notice;

    /// <summary>现在显示的那一行；没有在显示的时候是 <c>null</c>。</summary>
    public string? Shown => _notice.Line;

    public override void _Ready()
    {
        // 先停掉每帧回调，校验过了、有一条要显示时再开。_Ready 抛出之后引擎并不会停掉这个节点，
        // _Process 照旧每帧跑而规则层那一份还是 null —— 说清缺了什么的那一条会被成百条空引用刷走。
        SetProcess(false);

        // 子节点先于父节点就绪，所以这一刻它还在作者摆的地方：界面根就绪时才把它挪走。摆在界面根外面的话，
        // 它不会被挪到自己那一层，那一行画在世界里、跟着相机走，而那不报错。
        if (GetParent() is not UIRoot)
        {
            throw new InvalidOperationException(
                $"{nameof(InstantNoticeView)}（节点 {Name}）不在界面根下面 —— 把它摆成挂了 {nameof(UIRoot)} 的那个节点的子节点");
        }

        _line = Line ?? throw new InvalidOperationException(
            $"{nameof(InstantNoticeView)}（节点 {Name}）的 {nameof(Line)} 没接上 —— 它要的是那一行字（一个 Label）");

        // 摆在这一块外面的话，收起时藏的是这一块，那一行却还挂在屏上。
        if (!IsAncestorOf(_line))
        {
            throw new InvalidOperationException(
                $"{nameof(InstantNoticeView)}（节点 {Name}）的 {nameof(Line)}（{_line.Name}）不在它下面 —— 把那一行摆成它的子节点");
        }

        if (!double.IsFinite(SecondsShown) || SecondsShown <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(InstantNoticeView)}（节点 {Name}）的 {nameof(SecondsShown)} 是 {SecondsShown} —— "
                    + "一条提示停多久没有默认值，要在检查器里填一个大于零的秒数（填多少实机试）");
        }

        _notice = new InstantNotice(SecondsShown);

        // 农场不经过主场景，这里不装的话，那一行的字退回引擎默认字体，而那不报错。
        Theme = PixelTheme.Install(UIArtGuard.Require(Art, $"{nameof(InstantNoticeView)}（节点 {Name}）"), out _);
        MouseFilter = MouseFilterEnum.Ignore;
        _line.MouseFilter = MouseFilterEnum.Ignore;
        Refresh();
    }

    /// <summary>出一条：正在显示的那一条被顶掉，停留从头算。</summary>
    public void Show(string line)
    {
        _notice.Show(line);
        Refresh();
        SetProcess(true);
    }

    /// <summary>
    /// 每帧往前走一点，停够了收起。
    /// </summary>
    /// <remarks>
    /// 用 <c>_Process</c>：它不是玩法推进，不该被顿帧那个闸门管着。面板开着、世界停着时它照走，那由它所在那一层定。
    /// 收起之后停掉每帧回调，下一条进来再开。
    /// </remarks>
    public override void _Process(double delta)
    {
        _notice.Advance(delta);
        if (_notice.Line is null)
        {
            Refresh();
            SetProcess(false);
        }
    }

    /// <summary>藏的是整一块，不只是那一行：作者给那一行垫了底框的话，框也跟着收。</summary>
    private void Refresh()
    {
        _line.Text = _notice.Line ?? string.Empty;
        Visible = _notice.Line is not null;
    }
}
