using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 作者在编辑器里摆进场景的界面节点：它声明自己挂哪一层，<see cref="UIRoot"/> 就绪时把它挂上去。
/// </summary>
/// <remarks>
/// 层号只有 <see cref="UIRoot"/> 一个家（理由在那个类上）。作者把这样的节点摆成界面根的子节点，
/// 界面根就绪时把它挪到它声明的那一层上 —— 所以场景里不用、也不该自己摆一个 <c>CanvasLayer</c>。
///
/// 层由节点的类型声明，不由作者在检查器里选：一个界面属于哪一层是它是什么决定的（常驻显示还是
/// 面板），不是摆的人决定的。放进检查器就多一个能填错、而填错只表现为「被别的东西挡住」的格子。
///
/// 被挪过去的节点会离开再进一次场景树，而它的 <c>_Ready</c> 不会再跑一遍。所以它订阅的事件要在
/// <c>_EnterTree</c> 订、<c>_ExitTree</c> 解，成对跟着进出树走；写在 <c>_Ready</c> 里的订阅挪完就丢了。
/// </remarks>
public interface IPlacedUI
{
    /// <summary>这个界面挂在哪一层。</summary>
    UILayer Layer { get; }
}
