using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>一道带「确认」与「不」两个按钮的弹窗：压在面板上，经导航栈开关，按返回等于不。</summary>
/// <remarks>
/// 由代码搭、登记到界面根上（<see cref="UIRoot.Register"/>），不由作者摆：它装的只是一句问话与两个按钮，排版从排版单位与
/// 主题来，与背包页同一个做法。醒来那两个窗只有一个按钮，所以不复用它们（设计仓 spec/prd-items-in-bag.md 的「技术考虑」一节）。
///
/// 它铺满一屏、吃掉鼠标，开着时点不到下面那一页。焦点关在两个按钮之间，方向键走不出这个窗；开的那一下焦点落在「不」上，
/// 确认键按下去是不丢，误按一下什么都不损失。
///
/// 按下确认才交出去：先关窗、再调确认那一下。返回键关窗由界面根收，与按「不」一样什么都不做。它不在面板下面，
/// 主题接不到手环那一份，所以由搭它的那一方给。
/// </remarks>
public sealed partial class ConfirmDialog : Control
{
    private readonly UIRoot _ui;
    private readonly Label _question;
    private readonly Button _yes;
    private readonly Button _no;
    private Action? _onYes;

    /// <param name="surface">它是哪一个界面：挂在弹窗那一层。</param>
    /// <param name="ui">界面根：开关经它的导航栈。</param>
    /// <param name="theme">像素主题，与它压着的那一页同一份。</param>
    public ConfirmDialog(UISurface surface, UIRoot ui, Theme theme)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.Layer != UILayer.Dialog)
        {
            throw new ArgumentException($"确认窗挂在弹窗那一层，「{surface.Id}」却在 {surface.Layer}", nameof(surface));
        }

        Surface = surface;
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        Name = $"Confirm_{surface.Id.Replace('.', '_')}";     // 节点名里不许有点
        MouseFilter = MouseFilterEnum.Stop;

        var center = new CenterContainer { Name = "Center", MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var box = new PanelContainer
        {
            Name = "Box",
            ThemeTypeVariation = PixelTheme.DialogFrame,
            CustomMinimumSize = new Vector2(BagPageLayout.ConfirmWidth, 0),
        };
        center.AddChild(box);

        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", UIMetrics.Grid);
        box.AddChild(column);

        _question = new Label
        {
            Name = "Question",
            ThemeTypeVariation = PixelTheme.PaperLabel,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_question);

        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", UIMetrics.ItemGap);
        column.AddChild(buttons);

        _no = new Button { Name = "No", FocusMode = FocusModeEnum.All };
        _yes = new Button { Name = "Yes", FocusMode = FocusModeEnum.All };
        CursorSkin.ApplyButtonShape(_no);
        CursorSkin.ApplyButtonShape(_yes);
        buttons.AddChild(_no);
        buttons.AddChild(_yes);
        KeepFocusBetween(_no, "../Yes");
        KeepFocusBetween(_yes, "../No");

        _no.Pressed += () => _ui.Close(Surface);
        _yes.Pressed += Confirm;
        VisibilityChanged += OnVisibilityChanged;
    }

    /// <summary>它是哪一个界面。</summary>
    public UISurface Surface { get; }

    /// <summary>关上了，不管是按了哪个按钮还是按了返回。打开它的那一方在这里把焦点放回去。</summary>
    public event Action? Closed;

    /// <summary>问一句：经导航栈打开，焦点落在「不」上。按下确认才调 <paramref name="onYes"/>，而且只调一次。</summary>
    public void Ask(string question, string yes, string no, Action onYes)
    {
        _question.Text = question ?? throw new ArgumentNullException(nameof(question));
        _yes.Text = yes ?? throw new ArgumentNullException(nameof(yes));
        _no.Text = no ?? throw new ArgumentNullException(nameof(no));
        _onYes = onYes ?? throw new ArgumentNullException(nameof(onYes));

        _ui.Open(Surface);

        // 推迟到这一帧末尾：窗刚由导航栈显出来，这一刻排版还没跟上，抢焦点也可能被当成看不见（与醒来那个窗同一个做法）。
        Callable.From(() => _no.GrabFocus()).CallDeferred();
    }

    private void Confirm()
    {
        var onYes = _onYes;
        _ui.Close(Surface);
        onYes?.Invoke();
    }

    private void OnVisibilityChanged()
    {
        if (IsVisibleInTree())
        {
            return;
        }

        _onYes = null;
        Closed?.Invoke();
    }

    /// <summary>四个方向与前后都指到另一个按钮：焦点在这两个之间来回，走不到下面那一页去。</summary>
    private static void KeepFocusBetween(Button button, string other)
    {
        var path = new NodePath(other);
        button.FocusNeighborLeft = path;
        button.FocusNeighborRight = path;
        button.FocusNeighborTop = path;
        button.FocusNeighborBottom = path;
        button.FocusNext = path;
        button.FocusPrevious = path;
    }
}
