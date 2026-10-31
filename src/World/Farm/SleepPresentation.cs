using Godot;
using Tinderhearth.Rules.Economy;
using Tinderhearth.Rules.Foundation.Text;
using Tinderhearth.Rules.UI;
using Tinderhearth.UI;

namespace Tinderhearth.World.Farm;

/// <summary>睡下到醒来那一段的表现：淡出、结算、淡入、弹窗。作者在农场里摆一个节点挂它，接上遮罩、两个窗与界面根。</summary>
/// <remarks>
/// 推进一天的入口仍在那片地上（<see cref="FarmField"/>），这里只管那一段怎么走：关上输入、播淡出；播完结算、播淡入；
/// 播完交回输入、弹窗。走到哪儿、第二次推进被不被拒，判在规则层的 <see cref="SleepSequence"/>；窗里写什么判在
/// <see cref="WakeReport"/>。窗一弹出，世界照导航栈那一套暂停，按什么都推不动一天，所以这一段到弹窗那一刻就交回了。
/// 这一段不碰 <c>SceneTree.Paused</c>（设计仓 design/每日结算系统.md 的「结算期间没有输入入口」一节）。
///
/// 它在 _Ready 里把自己登记到那片地上。没摆它的农场按 N 照样推进一天，摘要与失败只打在输出面板上；摆了床就必须有它，
/// 那一条由那片地在启动之后核。缺了哪一格，当场报错说清缺谁。
/// </remarks>
public partial class SleepPresentation : Node
{
    /// <summary>关输入那道闸时报的名字。</summary>
    private const string GateHolder = "sleep";

    private readonly SleepSequence _sequence = new();
    private UIRoot _ui = null!;
    private InputRouter _router = null!;
    private SleepCurtain _curtain = null!;
    private WakeDialog _summary = null!;
    private WakeDialog _notAdvanced = null!;
    private FarmField? _registeredTo;
    private bool _subscribed;

    /// <summary>睡下去推进的是哪一片地的那一天。</summary>
    [Export]
    public FarmField? Field { get; set; }

    /// <summary>界面根。两个窗经它开关，世界暂不暂停照它那一套。</summary>
    [Export]
    public UIRoot? UI { get; set; }

    /// <summary>输入门面。睡下那一刻关上，淡入播完那一刻交回。</summary>
    [Export]
    public InputRouter? Router { get; set; }

    /// <summary>盖住画面的那一块，摆在界面根下面。</summary>
    [Export]
    public SleepCurtain? Curtain { get; set; }

    /// <summary>推进了的那一天弹的简版摘要，摆在界面根下面。</summary>
    [Export]
    public WakeDialog? Summary { get; set; }

    /// <summary>没推进的那一天弹的「这一天没有推进」，摆在界面根下面。</summary>
    [Export]
    public WakeDialog? NotAdvanced { get; set; }

    /// <summary>素材资源。两个窗的字体、字色与按钮的样子由它组装成像素主题，缺了或缺格当场报错并一次列全。</summary>
    /// <remarks>接的是 <c>assets/self-drawn/ui/ui_art.tres</c>，与随身栏接的是同一份。</remarks>
    [Export]
    public UIArt? Art { get; set; }

    /// <summary>睡下到醒来那一段里：输入关着，调试键也不该响应。</summary>
    public bool InputClosed => _sequence.InputClosed;

    public override void _Ready()
    {
        var field = Field ?? throw Missing(nameof(Field), $"睡下去推进的那一片地（挂了 {nameof(FarmField)} 的节点）");
        _ui = UI ?? throw Missing(nameof(UI), $"界面根（挂了 {nameof(UIRoot)} 的节点）");
        _router = Router ?? throw Missing(nameof(Router), $"输入门面（挂了 {nameof(InputRouter)} 的节点）");
        _curtain = Curtain ?? throw Missing(nameof(Curtain), $"盖住画面的那一块（挂了 {nameof(SleepCurtain)} 的节点）");
        _summary = Summary ?? throw Missing(nameof(Summary), $"简版摘要那个窗（挂了 {nameof(WakeDialog)} 的节点）");
        _notAdvanced = NotAdvanced ?? throw Missing(nameof(NotAdvanced), $"「这一天没有推进」那个窗（挂了 {nameof(WakeDialog)} 的节点）");

        if (ReferenceEquals(_summary, _notAdvanced))
        {
            throw new InvalidOperationException(
                $"{nameof(SleepPresentation)}（节点 {Name}）的 {nameof(Summary)} 与 {nameof(NotAdvanced)} 接的是同一块"
                    + $"（{_summary.Name}）—— 两个窗各摆一块");
        }

        // 摆在界面根外面的话，它不会被挪到自己那一层、也不会登记：窗打开时世界停了，窗却不显出来。
        foreach (var (exportName, node) in new (string, Node)[]
                 {
                     (nameof(Curtain), _curtain), (nameof(Summary), _summary), (nameof(NotAdvanced), _notAdvanced),
                 })
        {
            if (!_ui.IsAncestorOf(node))
            {
                throw new InvalidOperationException(
                    $"{nameof(SleepPresentation)}（节点 {Name}）的 {exportName}（{node.Name}）不在界面根（{_ui.Name}）下面 —— "
                        + "把它摆成界面根的子节点，界面根才会把它挪到它那一层");
            }
        }

        // 两个窗的字体、字色与按钮的样子从这一份主题来，窗自己不载字体（与主场景给手环挂主题同一个做法）。
        // 农场不经过主场景，这里不装的话，窗上的字退回引擎默认字体，而那不报错。
        var theme = PixelTheme.Install(UIArtGuard.Require(Art, $"{nameof(SleepPresentation)}（节点 {Name}）"), out var checks);
        _summary.Theme = theme;
        _notAdvanced.Theme = theme;
        GD.Print("[界面] 像素字体 ", PixelFont.ResourcePath, " ｜ 导入属性核对 ",
                 checks.Count(c => c.Ok), "/", checks.Count, " 一致");

        // 本节点不会被挪动，_Ready 只跑一次，所以订阅写在这里、校验过了之后；离开场景树时解掉。
        _summary.CloseRequested += CloseSummary;
        _notAdvanced.CloseRequested += CloseNotAdvanced;
        _subscribed = true;

        field.RegisterWake(this);
        _registeredTo = field;
    }

    public override void _ExitTree()
    {
        if (_subscribed)
        {
            _summary.CloseRequested -= CloseSummary;
            _notAdvanced.CloseRequested -= CloseNotAdvanced;
            _subscribed = false;
        }

        _registeredTo?.UnregisterWake(this);
        _registeredTo = null;
    }

    /// <summary>睡下去。这一段还没走完时这一下不算。</summary>
    /// <param name="settle">推进一天，在淡出播完、整屏盖住的那一刻调一次。</param>
    /// <param name="text">文本表。</param>
    /// <param name="savePath">存档文件的绝对路径，写不上时写进窗里。</param>
    internal void Begin(Func<SettlementOutcome> settle, TextCatalog text, string savePath)
    {
        if (!_sequence.TryBegin())
        {
            GD.Print("[结算] 还在睡下到醒来那一段里，这一下不算");
            return;
        }

        _router.Gate.Close(GateHolder);
        Guarded(() => _curtain.PlayFadeOut(() => OnFadedOut(settle, text, savePath)));
    }

    private void OnFadedOut(Func<SettlementOutcome> settle, TextCatalog text, string savePath) => Guarded(() =>
    {
        _sequence.FadeOutFinished();
        var report = WakeReport.For(settle(), text, savePath);
        _curtain.PlayFadeIn(() => OnFadedIn(report, text));
    });

    private void OnFadedIn(WakeReport report, TextCatalog text) => Guarded(() =>
    {
        _sequence.FadeInFinished();
        _router.Gate.Reopen(GateHolder);

        var dialog = report.Advanced ? _summary : _notAdvanced;
        dialog.Fill(report.Title, report.Lines, text);
        _ui.Open(dialog.Surface);
        dialog.OnOpened();
    });

    /// <summary>这一段里哪一下出了缺陷：撤掉遮罩、交回输入、回到醒着，再照常抛出去。</summary>
    /// <remarks>不撤的话画面一直盖着、输入一直关着，而报错那一条被盖在下面，看起来像卡死。</remarks>
    private void Guarded(Action step)
    {
        try
        {
            step();
        }
        catch
        {
            _sequence.Abort();
            if (_router.Gate.Holders.Contains(GateHolder))
            {
                _router.Gate.Reopen(GateHolder);
            }

            _curtain.Abort();
            throw;
        }
    }

    private void CloseSummary() => _ui.Close(_summary.Surface);

    private void CloseNotAdvanced() => _ui.Close(_notAdvanced.Surface);

    private InvalidOperationException Missing(string exportName, string what) =>
        new($"{nameof(SleepPresentation)}（节点 {Name}）的 {exportName} 没接上 —— 它要的是{what}");
}
