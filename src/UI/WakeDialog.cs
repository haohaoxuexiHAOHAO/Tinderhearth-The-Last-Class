using Godot;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>醒来那一刻弹的一个窗：作者在 UIRoot 下面摆的一块，代码只往里填字、翻页、请人关上。</summary>
/// <remarks>
/// 简版摘要与「这一天没有推进」用同一个脚本，各摆一块：两块的形状一样（最上面一行、下面几行、一个按钮），
/// 排版由作者各自定。写什么判在规则层（<see cref="WakeReport"/>），这里不挑字。
///
/// 开关走导航栈（<see cref="IPlacedSurface"/>）：开着时世界暂停、关上交回，用的是既有那一套，本类不碰暂停也不碰
/// <c>Visible</c>。返回键关窗由 <see cref="UIRoot"/> 收；按钮按下时，后面还有页就翻页，最后一页就请人关窗。
///
/// 字多了分页，不滚（设计仓 reference/godot/文字与文本框.md 的「六、字放不下：对话框分页，别处按整行滚」一节）：
/// 一页几行就是正文那一块在检查器里的 Max Lines Visible，作者定；不设就是一页放全部。按钮上的字跟着换成「下一页」
/// 或「知道了」，所以玩家看得出后面还有没有。
/// </remarks>
public sealed partial class WakeDialog : Control, IPlacedSurface
{
    private Label _title = null!;
    private Label _body = null!;
    private Button _button = null!;
    private TextCatalog _text = TextCatalog.Empty;
    private UISurface? _surface;

    /// <summary>最上面那一行。</summary>
    [Export]
    public Label? Title { get; set; }

    /// <summary>下面那几行，一行一项。一页几行看它的 Max Lines Visible。</summary>
    [Export]
    public Label? Body { get; set; }

    /// <summary>那个按钮：后面还有页时翻页，最后一页时关窗。上面的字由代码从文本表填，检查器里不用填。</summary>
    /// <remarks>
    /// 它必须在松开时才算按下（Action Mode 取 Button Release，引擎默认就是这个）。按下就算的话，关窗那一下与
    /// 交互键是同一次按下，窗一关世界恢复，那一下就落到世界里，站在床边会紧接着又睡一次。
    /// </remarks>
    [Export]
    public Button? CloseButton { get; set; }

    /// <summary>这个窗挂在弹窗那一层。</summary>
    public UILayer Layer => UILayer.Dialog;

    /// <summary>它是哪一个界面。标识带着节点名，所以同一个脚本摆两块也分得开。</summary>
    public UISurface Surface => _surface ??= new UISurface($"wake.{Name}", UILayer.Dialog, SurfaceKind.View);

    /// <summary>最后一页上按了按钮：请打开它的那一方关窗。</summary>
    public event Action? CloseRequested;

    /// <summary>后面还有没有页。</summary>
    public bool HasNextPage =>
        _body.MaxLinesVisible > 0 && _body.LinesSkipped + _body.MaxLinesVisible < _body.GetLineCount();

    public override void _Ready()
    {
        _title = Title ?? throw Missing(nameof(Title), "最上面那一行（一个 Label）");
        _body = Body ?? throw Missing(nameof(Body), "下面那几行（一个 Label）");
        _button = CloseButton ?? throw Missing(nameof(CloseButton), "那个按钮（一个 Button）");

        if (_button.ActionMode != BaseButton.ActionModeEnum.Release)
        {
            throw new InvalidOperationException(
                $"{nameof(WakeDialog)}（节点 {Name}）的按钮（{_button.Name}）Action Mode 不是 Button Release —— "
                    + "按下就算的话，关窗那一下会落到世界里，站在床边会紧接着又睡一次");
        }

        if (_button.FocusMode == FocusModeEnum.None)
        {
            throw new InvalidOperationException(
                $"{nameof(WakeDialog)}（节点 {Name}）的按钮（{_button.Name}）Focus Mode 是 None —— "
                    + "拿不到焦点，键盘与手柄就按不到它");
        }
    }

    // 订阅跟着进出树走：界面根会把本节点挪到弹窗那一层，挪的时候它离开再进一次树，_Ready 却不再跑（见 IPlacedUI）。
    public override void _EnterTree()
    {
        if (CloseButton is not null)
        {
            CloseButton.Pressed += OnPressed;
        }
    }

    public override void _ExitTree()
    {
        if (CloseButton is not null)
        {
            CloseButton.Pressed -= OnPressed;
        }
    }

    /// <summary>填字，回到第一页。窗由打开它的那一方经 <see cref="UIRoot.Open"/> 打开，开完调 <see cref="OnOpened"/>。</summary>
    public void Fill(string title, IReadOnlyList<string> lines, TextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(lines);
        _text = text ?? throw new ArgumentNullException(nameof(text));

        _title.Text = title;
        _body.Text = string.Join("\n", lines);
        _body.LinesSkipped = 0;
        RefreshButton();
    }

    /// <summary>窗打开之后：按显出来之后的排版再判一次有没有下一页，再把焦点给按钮，确认键就按得到它。</summary>
    /// <remarks>推迟到这一帧末尾：窗刚由导航栈显出来，这一刻排版还没跟上，抢焦点也可能被当成看不见。</remarks>
    public void OnOpened() => Callable.From(() =>
    {
        RefreshButton();
        _button.GrabFocus();
    }).CallDeferred();

    private void OnPressed()
    {
        if (HasNextPage)
        {
            _body.LinesSkipped += _body.MaxLinesVisible;
            RefreshButton();
            return;
        }

        CloseRequested?.Invoke();
    }

    private void RefreshButton() => _button.Text = _text[HasNextPage ? WakeReport.NextPageKey : WakeReport.CloseKey];

    private InvalidOperationException Missing(string exportName, string what) =>
        new($"{nameof(WakeDialog)}（节点 {Name}）的 {exportName} 没接上 —— 它要的是{what}");
}
