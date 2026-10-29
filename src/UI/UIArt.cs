// 本文件由 tools/ui_art_resource.py 照生成器清单（tools/ui_art.py）写出，别手改：改清单之后重跑那个入口。

using Godot;

namespace Tinderhearth.UI;

/// <summary>
/// 素材资源：界面件一律从这一份取。一件一格，格名就是文件名，按 <c>assets/self-drawn/ui/</c> 下的目录分组。
/// </summary>
/// <remarks>
/// 九宫格件那一格是一个 <see cref="NinePiece"/>：图与它四边的边距放在一起。每一格该多大、用在哪，在
/// <see cref="Tinderhearth.Rules.UI.UIArtManifest"/>；载入时缺哪几格、哪张尺寸不对，由
/// <see cref="UIArtGuard"/> 一次全报。按文件名填好的那一份在 <c>assets/self-drawn/ui/ui_art.tres</c>，
/// 作者在检查器里换图，换上去的那张要保持原尺寸。
/// </remarks>
[GlobalClass]
public partial class UIArt : Resource
{
    /// <summary>面板外壳（个人界面那一套顶层页签就搭在它顶边上）、背包、弹窗</summary>
    /// <remarks>40×44；九宫格 左 18 上 18 右 18 下 22；来源：切（按模板轮廓重画）。</remarks>
    [ExportGroup("kit", "Kit")]
    [Export]
    public NinePiece? KitFrameThickDark { get; set; }

    /// <summary>浅色那一族的厚框：压在面板上的弹窗、对话框</summary>
    /// <remarks>40×44；九宫格 左 18 上 18 右 18 下 22；放一行字至少高 56；来源：派生。</remarks>
    [Export]
    public NinePiece? KitFrameThickLight { get; set; }

    /// <summary>面板里的内容区：子页签下面那一大块；浅底，字用深色</summary>
    /// <remarks>32×31；九宫格 左 14 上 13 右 14 下 14；放一行字至少高 43；来源：派生。</remarks>
    [Export]
    public NinePiece? KitInset { get; set; }

    /// <summary>左栏那种小盒子：个人介绍、数值区；浅底，字用深色</summary>
    /// <remarks>7×7；九宫格 左 3 上 3 右 3 下 3；放一行字至少高 22；来源：派生。</remarks>
    [Export]
    public NinePiece? KitBox { get; set; }

    /// <summary>盒子顶上那条亮色名字带：角色名、小标题</summary>
    /// <remarks>7×7；九宫格 左 3 上 3 右 3 下 3；放一行字至少高 22；来源：切（按模板重画）。</remarks>
    [Export]
    public NinePiece? KitBoxTitle { get; set; }

    /// <summary>说明框：物品、技能、天赋、状态的详情与各种长文本；分区用 divider_h_light／divider_v_light</summary>
    /// <remarks>14×14；九宫格 左 5 上 5 右 5 下 5；放一行字至少高 26；来源：派生。</remarks>
    [Export]
    public NinePiece? KitFrameThinLight { get; set; }

    /// <summary>HUD 各块的底、小弹层；画法照随身栏那条栏的边，底取最深那档，压在杂乱的场景上字也读得清</summary>
    /// <remarks>12×12；九宫格 左 4 上 4 右 4 下 4；放一行字至少高 24；来源：手画。</remarks>
    [Export]
    public NinePiece? KitFrameThinDark { get; set; }

    /// <summary>标题绸带：页标题、升档提示</summary>
    /// <remarks>34×26；九宫格 左 15 上 0 右 15 下 0；来源：切。</remarks>
    [Export]
    public NinePiece? KitRibbon { get; set; }

    /// <summary>深色框里的横分隔线</summary>
    /// <remarks>8×2；九宫格 左 2 上 0 右 2 下 0；来源：手画。</remarks>
    [Export]
    public NinePiece? KitDividerH { get; set; }

    /// <summary>深色框里的竖分隔线</summary>
    /// <remarks>2×8；九宫格 左 0 上 2 右 0 下 2；来源：手画。</remarks>
    [Export]
    public NinePiece? KitDividerV { get; set; }

    /// <summary>说明框里的横分隔线</summary>
    /// <remarks>8×2；九宫格 左 2 上 0 右 2 下 0；来源：手画。</remarks>
    [Export]
    public NinePiece? KitDividerHLight { get; set; }

    /// <summary>说明框里的竖分隔线</summary>
    /// <remarks>2×8；九宫格 左 0 上 2 右 0 下 2；来源：手画。</remarks>
    [Export]
    public NinePiece? KitDividerVLight { get; set; }

    /// <summary>带字的按钮；焦点与鼠标悬停同一张；字由字体画</summary>
    /// <remarks>12×12；九宫格 左 4 上 4 右 4 下 4；放一行字至少高 24；同一件的几态：normal、focus、pressed、disabled；来源：手画。</remarks>
    [Export]
    public NinePiece? KitButtonNormal { get; set; }

    /// <summary>带字的按钮</summary>
    /// <remarks>12×12；九宫格 左 4 上 4 右 4 下 4；放一行字至少高 24；同一件的几态：normal、focus、pressed、disabled；来源：手画。</remarks>
    [Export]
    public NinePiece? KitButtonFocus { get; set; }

    /// <summary>带字的按钮</summary>
    /// <remarks>12×12；九宫格 左 4 上 4 右 4 下 4；放一行字至少高 24；同一件的几态：normal、focus、pressed、disabled；来源：手画。</remarks>
    [Export]
    public NinePiece? KitButtonPressed { get; set; }

    /// <summary>带字的按钮</summary>
    /// <remarks>12×12；九宫格 左 4 上 4 右 4 下 4；放一行字至少高 24；同一件的几态：normal、focus、pressed、disabled；来源：手画。</remarks>
    [Export]
    public NinePiece? KitButtonDisabled { get; set; }

    /// <summary>图标按钮的底（作者那批八角按钮）</summary>
    /// <remarks>16×16；同一件的几态：normal、focus、pressed、disabled；来源：切。</remarks>
    [Export]
    public Texture2D? KitIconbuttonNormal { get; set; }

    /// <summary>图标按钮：焦点／悬停</summary>
    /// <remarks>16×16；同一件的几态：normal、focus、pressed、disabled；来源：派生。</remarks>
    [Export]
    public Texture2D? KitIconbuttonFocus { get; set; }

    /// <summary>图标按钮：按下（图标由代码下移一像素）</summary>
    /// <remarks>16×16；同一件的几态：normal、focus、pressed、disabled；来源：派生。</remarks>
    [Export]
    public Texture2D? KitIconbuttonPressed { get; set; }

    /// <summary>图标按钮：不可用</summary>
    /// <remarks>16×16；同一件的几态：normal、focus、pressed、disabled；来源：派生。</remarks>
    [Export]
    public Texture2D? KitIconbuttonDisabled { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconArrowLeft { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconArrowRight { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconClose { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconConfirm { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconPause { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconMail { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconMenu { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconSettings { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；同一件的几态：off、on；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconSoundOff { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；同一件的几态：off、on；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconSoundOn { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconHelp { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconMinus { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconPlus { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconLock { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconUnlock { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconStar { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconArrowUp { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconArrowDown { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconAlert { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconPlay { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconRefresh { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconFastForward { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconMusic { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconTrash { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconShop { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconEnergy { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconTrophy { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconCamera { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconGift { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconStats { get; set; }

    /// <summary>叠在图标按钮底上的那一层</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconBattery { get; set; }

    /// <summary>对话的「跳过」；与快进分得开</summary>
    /// <remarks>16×16；来源：誊。</remarks>
    [Export]
    public Texture2D? KitButtonIconSkip { get; set; }

    /// <summary>设置里的勾选框：没勾</summary>
    /// <remarks>16×16；同一件的几态：off、on；来源：手画。</remarks>
    [Export]
    public Texture2D? KitCheckOff { get; set; }

    /// <summary>设置里的勾选框：勾上</summary>
    /// <remarks>16×16；同一件的几态：off、on；来源：手画。</remarks>
    [Export]
    public Texture2D? KitCheckOn { get; set; }

    /// <summary>技能栏、随身栏、背包、仓库、宝物栏共用的格子；图标放在 (3,3)</summary>
    /// <remarks>22×22；同一件的几态：normal、disabled、locked、focus；来源：切。</remarks>
    [Export]
    public Texture2D? KitSlotNormal { get; set; }

    /// <summary>格子：暂时用不了</summary>
    /// <remarks>22×22；同一件的几态：normal、disabled、locked、focus；来源：派生。</remarks>
    [Export]
    public Texture2D? KitSlotDisabled { get; set; }

    /// <summary>格子：还没开的格（背包下一级才开的那几格）</summary>
    /// <remarks>22×22；同一件的几态：normal、disabled、locked、focus；来源：派生。</remarks>
    [Export]
    public Texture2D? KitSlotLocked { get; set; }

    /// <summary>选中框：叠在格子外面一圈，左上角对齐格子左上角往外 2 像素</summary>
    /// <remarks>26×26；来源：手画。</remarks>
    [Export]
    public Texture2D? KitSlotSelect { get; set; }

    /// <summary>焦点／悬停：格子里面一圈亮边，与选中框同时在也看得出</summary>
    /// <remarks>22×22；同一件的几态：normal、disabled、locked、focus；来源：手画。</remarks>
    [Export]
    public Texture2D? KitSlotFocus { get; set; }

    /// <summary>稀有度：紧贴图标那一圈，纯白、代码按档染</summary>
    /// <remarks>22×22；来源：手画。</remarks>
    [Export]
    public Texture2D? KitRarityEdge { get; set; }

    /// <summary>详情框左上那一格：物品、技能、天赋、状态共用；16×16 图标原样放在 (2,2)；不染色，物品的稀有度写在名字下面那一行</summary>
    /// <remarks>20×20；来源：手画。</remarks>
    [Export]
    public Texture2D? KitIconFrame { get; set; }

    /// <summary>宝石孔（两种孔共用）：圆的，与装物品的方格分得开</summary>
    /// <remarks>22×22；同一件的几态：open、locked；来源：手画。</remarks>
    [Export]
    public Texture2D? KitSocketOpen { get; set; }

    /// <summary>宝石孔：还没开</summary>
    /// <remarks>22×22；同一件的几态：open、locked；来源：手画。</remarks>
    [Export]
    public Texture2D? KitSocketLocked { get; set; }

    /// <summary>品质点：纯白、代码染，落在格子左下角</summary>
    /// <remarks>2×2；来源：手画。</remarks>
    [Export]
    public Texture2D? KitPip { get; set; }

    /// <summary>品质点：纯白、代码染，落在格子左下角</summary>
    /// <remarks>3×3；来源：手画。</remarks>
    [Export]
    public Texture2D? KitPipLarge { get; set; }

    /// <summary>品质星的填充：纯白、代码染；取线稿围住的格</summary>
    /// <remarks>8×8；来源：派生。</remarks>
    [Export]
    public Texture2D? KitStarFill { get; set; }

    /// <summary>品质星的线稿：叠在填充上，不染</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitStarLine { get; set; }

    /// <summary>关系爱心／托付／强化点；每一对外轮廓一样</summary>
    /// <remarks>8×8；同一件的几态：empty、full；来源：手画。</remarks>
    [Export]
    public Texture2D? KitHeartEmpty { get; set; }

    /// <summary>关系爱心／托付／强化点；每一对外轮廓一样</summary>
    /// <remarks>8×8；同一件的几态：empty、full；来源：手画。</remarks>
    [Export]
    public Texture2D? KitHeartFull { get; set; }

    /// <summary>关系爱心／托付／强化点；每一对外轮廓一样</summary>
    /// <remarks>8×8；同一件的几态：empty、full；来源：手画。</remarks>
    [Export]
    public Texture2D? KitHeartEntrustEmpty { get; set; }

    /// <summary>关系爱心／托付／强化点；每一对外轮廓一样</summary>
    /// <remarks>8×8；同一件的几态：empty、full；来源：手画。</remarks>
    [Export]
    public Texture2D? KitHeartEntrustFull { get; set; }

    /// <summary>关系爱心／托付／强化点；每一对外轮廓一样</summary>
    /// <remarks>8×8；同一件的几态：empty、full；来源：手画。</remarks>
    [Export]
    public Texture2D? KitPipEnhanceEmpty { get; set; }

    /// <summary>关系爱心／托付／强化点；每一对外轮廓一样</summary>
    /// <remarks>8×8；同一件的几态：empty、full；来源：手画。</remarks>
    [Export]
    public Texture2D? KitPipEnhanceFull { get; set; }

    /// <summary>顶层页签的底（个人界面、学生列表……那一排）；最下面一行压在面板顶边上，底下两角也切掉，像放在面板上的牌子；图标放 (2,2)</summary>
    /// <remarks>20×20；同一件的几态：normal、selected、disabled、focus；来源：切（按模板重画）。</remarks>
    [Export]
    public Texture2D? KitTabNormal { get; set; }

    /// <summary>顶层页签的底（个人界面、学生列表……那一排）；最下面一行压在面板顶边上，底边方的，接进面板顶边；图标放 (2,2)</summary>
    /// <remarks>20×20；同一件的几态：normal、selected、disabled、focus；来源：切（按模板重画）。</remarks>
    [Export]
    public Texture2D? KitTabSelected { get; set; }

    /// <summary>顶层页签的底（个人界面、学生列表……那一排）；最下面一行压在面板顶边上，底下两角也切掉，像放在面板上的牌子；图标放 (2,2)</summary>
    /// <remarks>20×20；同一件的几态：normal、selected、disabled、focus；来源：切（按模板重画）。</remarks>
    [Export]
    public Texture2D? KitTabDisabled { get; set; }

    /// <summary>顶层页签的底（个人界面、学生列表……那一排）；最下面一行压在面板顶边上，焦点与鼠标悬停同一张，只换没选中的那几个；底下两角也切掉；图标放 (2,2)</summary>
    /// <remarks>20×20；同一件的几态：normal、selected、disabled、focus；来源：切（按模板重画）。</remarks>
    [Export]
    public Texture2D? KitTabFocus { get; set; }

    /// <summary>子页签（属性、状态、天赋……）；字由字体画，选中的深字、其余浅字；最下面一行接内容区顶边</summary>
    /// <remarks>8×6；九宫格 左 2 上 2 右 2 下 0；同一件的几态：normal、disabled、focus、selected；来源：派生。</remarks>
    [Export]
    public NinePiece? KitTabtextNormal { get; set; }

    /// <summary>子页签：不可用</summary>
    /// <remarks>8×6；九宫格 左 2 上 2 右 2 下 0；同一件的几态：normal、disabled、focus、selected；来源：派生。</remarks>
    [Export]
    public NinePiece? KitTabtextDisabled { get; set; }

    /// <summary>子页签：焦点／悬停（底色取顶层页签焦点那一态的，字是深字）</summary>
    /// <remarks>8×6；九宫格 左 2 上 2 右 2 下 0；同一件的几态：normal、disabled、focus、selected；来源：派生。</remarks>
    [Export]
    public NinePiece? KitTabtextFocus { get; set; }

    /// <summary>子页签（属性、状态、天赋……）；字由字体画，选中的深字、其余浅字；最下面一行接内容区顶边</summary>
    /// <remarks>8×6；九宫格 左 2 上 2 右 2 下 0；同一件的几态：normal、disabled、focus、selected；来源：切。</remarks>
    [Export]
    public NinePiece? KitTabtextSelected { get; set; }

    /// <summary>列表里选中的那一行；字是深色</summary>
    /// <remarks>12×16；九宫格 左 4 上 0 右 4 下 0；同一件的几态：selected、focus；来源：手画。</remarks>
    [Export]
    public NinePiece? KitRowSelected { get; set; }

    /// <summary>列表里焦点／悬停的那一行：只有一圈亮边，可以叠在选中行上</summary>
    /// <remarks>12×16；九宫格 左 4 上 0 右 4 下 0；同一件的几态：selected、focus；来源：手画。</remarks>
    [Export]
    public NinePiece? KitRowFocus { get; set; }

    /// <summary>设置里的横滑条：槽</summary>
    /// <remarks>12×7；九宫格 左 4 上 0 右 4 下 0；来源：切。</remarks>
    [Export]
    public NinePiece? KitSliderTrack { get; set; }

    /// <summary>横滑条的钮</summary>
    /// <remarks>5×13；来源：切。</remarks>
    [Export]
    public Texture2D? KitSliderKnob { get; set; }

    /// <summary>列表的竖滚动条：槽</summary>
    /// <remarks>7×12；九宫格 左 0 上 4 右 0 下 4；来源：切。</remarks>
    [Export]
    public NinePiece? KitScrollTrack { get; set; }

    /// <summary>竖滚动条的钮</summary>
    /// <remarks>13×5；来源：切。</remarks>
    [Export]
    public Texture2D? KitScrollKnob { get; set; }

    /// <summary>文字区的细滚动条：槽；只在字超出那一块时出现，位置一直留着</summary>
    /// <remarks>6×8；九宫格 左 0 上 3 右 0 下 3；来源：手画。</remarks>
    [Export]
    public NinePiece? KitScrollSlimTrack { get; set; }

    /// <summary>细滚动条的钮：长短按看得见的那一段占全文多少拉</summary>
    /// <remarks>6×8；九宫格 左 0 上 3 右 0 下 3；来源：手画。</remarks>
    [Export]
    public NinePiece? KitScrollSlimKnob { get; set; }

    /// <summary>迷你字：一格 5×7（字形 3×5 加一圈描边），顺序 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ；格子角上的件数与键名</summary>
    /// <remarks>180×7；来源：手画。</remarks>
    [Export]
    public Texture2D? KitFontMini { get; set; }

    /// <summary>面板上的大数字（属性值、等级）：一格 10×12，顺序 0–9</summary>
    /// <remarks>100×12；来源：切。</remarks>
    [Export]
    public Texture2D? KitFontDigits { get; set; }

    /// <summary>鼠标指针，1× 原图，代码按整数倍放大；热点 (4,0)</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? KitCursorArrow { get; set; }

    /// <summary>鼠标指针，1× 原图，代码按整数倍放大；热点 (5,0)</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? KitCursorHand { get; set; }

    /// <summary>鼠标指针，1× 原图，代码按整数倍放大；热点 (7,7)</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? KitCursorBlocked { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniCursor { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniRight { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniLeft { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniUp { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniDown { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniMore { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniPlus { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniMinus { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniLock { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniDone { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniFree { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniUnpaid { get; set; }

    /// <summary>迷你记号</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? KitMiniIrreversible { get; set; }

    /// <summary>技能栏与随身栏的底；格子从 (7,5) 起、每格间距 28；格数由代码给</summary>
    /// <remarks>28×32；九宫格 左 12 上 0 右 12 下 0；来源：切。</remarks>
    [ExportGroup("hud", "Hud")]
    [Export]
    public NinePiece? HudActionbarFrame { get; set; }

    /// <summary>资源条的槽；填充放在 (2,2)，按槽里 ember 3 那片裁</summary>
    /// <remarks>15×10；九宫格 左 5 上 0 右 6 下 0；来源：切。</remarks>
    [Export]
    public NinePiece? HudBarTrack { get; set; }

    /// <summary>生命条的填充；中段用 Tile 铺、不拉伸，右端三列是斜切的头</summary>
    /// <remarks>17×6；九宫格 左 4 上 0 右 3 下 0；来源：切。</remarks>
    [Export]
    public NinePiece? HudBarFillHp { get; set; }

    /// <summary>体力条的填充；中段用 Tile 铺、不拉伸，右端三列是斜切的头</summary>
    /// <remarks>19×6；九宫格 左 4 上 0 右 3 下 0；来源：切。</remarks>
    [Export]
    public NinePiece? HudBarFillSp { get; set; }

    /// <summary>法力条的填充（比原图亮一档）；中段用 Tile 铺、不拉伸，右端三列是斜切的头</summary>
    /// <remarks>19×6；九宫格 左 4 上 0 右 3 下 0；来源：派生。</remarks>
    [Export]
    public NinePiece? HudBarFillMp { get; set; }

    /// <summary>经验条的填充；中段用 Tile 铺、不拉伸，右端三列是斜切的头</summary>
    /// <remarks>19×6；九宫格 左 4 上 0 右 3 下 0；来源：切。</remarks>
    [Export]
    public NinePiece? HudBarFillXp { get; set; }

    /// <summary>扣减时那一截（四条共用）；中段用 Tile 铺、不拉伸，右端三列是斜切的头</summary>
    /// <remarks>19×6；九宫格 左 4 上 0 右 3 下 0；来源：派生。</remarks>
    [Export]
    public NinePiece? HudBarFillLag { get; set; }

    /// <summary>经营侧那条竖着的精力条：空管；中段按行平铺拉长</summary>
    /// <remarks>13×28；九宫格 左 0 上 5 右 0 下 5；来源：切。</remarks>
    [Export]
    public NinePiece? HudVigorTubeTrack { get; set; }

    /// <summary>精力条的液面：叠在空管上，代码从下往上按比例露出（色改走绿）；中段按行平铺拉长</summary>
    /// <remarks>13×28；九宫格 左 0 上 8 右 0 下 8；来源：派生。</remarks>
    [Export]
    public NinePiece? HudVigorTubeFill { get; set; }

    /// <summary>资源条前面的标签</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? HudGaugeIconHp { get; set; }

    /// <summary>资源条前面的标签</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? HudGaugeIconSp { get; set; }

    /// <summary>资源条前面的标签</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? HudGaugeIconMp { get; set; }

    /// <summary>资源条前面的标签</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? HudGaugeIconVigor { get; set; }

    /// <summary>任务目标那一行打头</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? HudObjective { get; set; }

    /// <summary>这一屏清干净了</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? HudScreenClear { get; set; }

    /// <summary>倒地：盖在小头像上</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? HudDownMark { get; set; }

    /// <summary>队友那一块的头像框：中间 16×16 透明放小头像</summary>
    /// <remarks>20×20；来源：手画。</remarks>
    [Export]
    public Texture2D? HudPortraitFrame { get; set; }

    /// <summary>读条底环：打断就没了的那一种</summary>
    /// <remarks>14×16；来源：切。</remarks>
    [ExportGroup("world", "World")]
    [Export]
    public Texture2D? WorldRingCast { get; set; }

    /// <summary>读条那一圈：叠在底环上，代码按角度露出</summary>
    /// <remarks>14×16；来源：切。</remarks>
    [Export]
    public Texture2D? WorldRingCastFill { get; set; }

    /// <summary>转场读条底环：方的，与圆的那张分得开</summary>
    /// <remarks>14×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldRingTransfer { get; set; }

    /// <summary>转场读条那一圈</summary>
    /// <remarks>14×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldRingTransferFill { get; set; }

    /// <summary>当前操作格：够得到；比一格地大一圈，左上角对齐格子往外 1 像素</summary>
    /// <remarks>18×18；来源：切。</remarks>
    [Export]
    public Texture2D? WorldCellUsable { get; set; }

    /// <summary>当前操作格：够不到</summary>
    /// <remarks>18×18；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldCellBlocked { get; set; }

    /// <summary>气泡框（深底浅字）</summary>
    /// <remarks>14×14；九宫格 左 5 上 5 右 5 下 5；放一行字至少高 26；来源：切。</remarks>
    [Export]
    public NinePiece? WorldBubble { get; set; }

    /// <summary>气泡尾巴，模板原样、不染色；最上面一行压在气泡倒数第三行</summary>
    /// <remarks>11×10；来源：切。</remarks>
    [Export]
    public Texture2D? WorldBubbleTail { get; set; }

    /// <summary>引导提示框：浅底、圆角、没有铆钉，与气泡形状分得开</summary>
    /// <remarks>12×12；九宫格 左 4 上 4 右 4 下 4；放一行字至少高 24；来源：手画。</remarks>
    [Export]
    public NinePiece? WorldHintFrame { get; set; }

    /// <summary>引导提示左边那个记号</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldHintIcon { get; set; }

    /// <summary>这一招对它没反应</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldImmune { get; set; }

    /// <summary>设备头上的状态：缺料</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldDeviceNofuel { get; set; }

    /// <summary>设备头上的状态：在产</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldDeviceWorking { get; set; }

    /// <summary>设备头上的状态：产完了</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldDeviceDone { get; set; }

    /// <summary>建造预览，一格一张：可摆；中间透明</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldPlaceOk { get; set; }

    /// <summary>建造预览，一格一张：被拒；中间透明</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldPlaceBlocked { get; set; }

    /// <summary>建造预览，一格一张：门前那一格；中间透明</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldPlaceDoor { get; set; }

    /// <summary>建造预览，一格一张：施工中已被占；中间透明</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldPlaceOccupied { get; set; }

    /// <summary>掉在地上那一件的底：纯白、代码按稀有度染</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? WorldDropBase { get; set; }

    /// <summary>个人界面左上的立绘框：64×64 立绘放在 (6,6)</summary>
    /// <remarks>76×76；来源：切。</remarks>
    [ExportGroup("panel", "Panel")]
    [Export]
    public Texture2D? PanelProfileFrame { get; set; }

    /// <summary>属性页：等级</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? PanelAttrLevel { get; set; }

    /// <summary>属性页：力量</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? PanelAttrStr { get; set; }

    /// <summary>属性页：智力</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? PanelAttrInt { get; set; }

    /// <summary>属性页：敏捷</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? PanelAttrAgi { get; set; }

    /// <summary>属性页：体质</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? PanelAttrCon { get; set; }

    /// <summary>属性页：属性点</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? PanelAttrPoint { get; set; }

    /// <summary>地图页：当前在这张图</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? PanelMapHere { get; set; }

    /// <summary>地图页：进不去的图</summary>
    /// <remarks>8×8；来源：手画。</remarks>
    [Export]
    public Texture2D? PanelMapBlocked { get; set; }

    /// <summary>出征结束那一屏：成功</summary>
    /// <remarks>32×32；来源：手画。</remarks>
    [Export]
    public Texture2D? PanelResultSuccess { get; set; }

    /// <summary>出征结束那一屏：撤退</summary>
    /// <remarks>32×32；来源：手画。</remarks>
    [Export]
    public Texture2D? PanelResultRetreat { get; set; }

    /// <summary>出征结束那一屏：失败</summary>
    /// <remarks>32×32；来源：手画。</remarks>
    [Export]
    public Texture2D? PanelResultFail { get; set; }

    /// <summary>主线页：这个节点派出一条任务</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? PanelNodeQuest { get; set; }

    /// <summary>主线页：这个节点是一段演出</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? PanelNodeCutscene { get; set; }

    /// <summary>图鉴里还没收录的那一条</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? PanelCodexUnknown { get; set; }

    /// <summary>钱（背包里那一栏）</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? PanelCoin { get; set; }

    /// <summary>顶层页签图标：个人界面</summary>
    /// <remarks>16×16；同一件的几态：character、disabled；来源：切。</remarks>
    [ExportGroup("tab", "Tab")]
    [Export]
    public Texture2D? TabCharacter { get; set; }

    /// <summary>顶层页签图标：个人界面（不可用）</summary>
    /// <remarks>16×16；同一件的几态：character、disabled；来源：派生。</remarks>
    [Export]
    public Texture2D? TabCharacterDisabled { get; set; }

    /// <summary>顶层页签图标：学生列表</summary>
    /// <remarks>16×16；同一件的几态：students、disabled；来源：切。</remarks>
    [Export]
    public Texture2D? TabStudents { get; set; }

    /// <summary>顶层页签图标：学生列表（不可用）</summary>
    /// <remarks>16×16；同一件的几态：students、disabled；来源：派生。</remarks>
    [Export]
    public Texture2D? TabStudentsDisabled { get; set; }

    /// <summary>顶层页签图标：背包</summary>
    /// <remarks>16×16；同一件的几态：backpack、disabled；来源：切。</remarks>
    [Export]
    public Texture2D? TabBackpack { get; set; }

    /// <summary>顶层页签图标：背包（不可用）</summary>
    /// <remarks>16×16；同一件的几态：backpack、disabled；来源：派生。</remarks>
    [Export]
    public Texture2D? TabBackpackDisabled { get; set; }

    /// <summary>顶层页签图标：任务列表</summary>
    /// <remarks>16×16；同一件的几态：quests、disabled；来源：切。</remarks>
    [Export]
    public Texture2D? TabQuests { get; set; }

    /// <summary>顶层页签图标：任务列表（不可用）</summary>
    /// <remarks>16×16；同一件的几态：quests、disabled；来源：派生。</remarks>
    [Export]
    public Texture2D? TabQuestsDisabled { get; set; }

    /// <summary>顶层页签图标：地图</summary>
    /// <remarks>16×16；同一件的几态：map、disabled；来源：切。</remarks>
    [Export]
    public Texture2D? TabMap { get; set; }

    /// <summary>顶层页签图标：地图（不可用）</summary>
    /// <remarks>16×16；同一件的几态：map、disabled；来源：派生。</remarks>
    [Export]
    public Texture2D? TabMapDisabled { get; set; }

    /// <summary>顶层页签图标：关系列表</summary>
    /// <remarks>16×16；同一件的几态：relations、disabled；来源：切。</remarks>
    [Export]
    public Texture2D? TabRelations { get; set; }

    /// <summary>顶层页签图标：关系列表（不可用）</summary>
    /// <remarks>16×16；同一件的几态：relations、disabled；来源：派生。</remarks>
    [Export]
    public Texture2D? TabRelationsDisabled { get; set; }

    /// <summary>顶层页签图标：百科全书</summary>
    /// <remarks>16×16；同一件的几态：codex、disabled；来源：切。</remarks>
    [Export]
    public Texture2D? TabCodex { get; set; }

    /// <summary>顶层页签图标：百科全书（不可用）</summary>
    /// <remarks>16×16；同一件的几态：codex、disabled；来源：派生。</remarks>
    [Export]
    public Texture2D? TabCodexDisabled { get; set; }

    /// <summary>顶层页签图标：经营</summary>
    /// <remarks>16×16；同一件的几态：business、disabled；来源：切。</remarks>
    [Export]
    public Texture2D? TabBusiness { get; set; }

    /// <summary>顶层页签图标：经营（不可用）</summary>
    /// <remarks>16×16；同一件的几态：business、disabled；来源：派生。</remarks>
    [Export]
    public Texture2D? TabBusinessDisabled { get; set; }

    /// <summary>主菜单那一列的外框；里面每一项左右各让 4、上下各让 2，项与项之间空 3 行</summary>
    /// <remarks>26×26；九宫格 左 11 上 11 右 11 下 11；来源：切（按模板重画）。</remarks>
    [ExportGroup("menu", "Menu")]
    [Export]
    public NinePiece? MenuMenuFrame { get; set; }

    /// <summary>主菜单的一项：载入游戏、新的游戏、设置；字由字体画</summary>
    /// <remarks>16×16；九宫格 左 6 上 6 右 6 下 6；放一行字至少高 28；同一件的几态：normal、focus、pressed、disabled；来源：切。</remarks>
    [Export]
    public NinePiece? MenuMenuItemNormal { get; set; }

    /// <summary>主菜单的一项：载入游戏、新的游戏、设置；字由字体画</summary>
    /// <remarks>16×16；九宫格 左 6 上 6 右 6 下 6；放一行字至少高 28；同一件的几态：normal、focus、pressed、disabled；来源：派生。</remarks>
    [Export]
    public NinePiece? MenuMenuItemFocus { get; set; }

    /// <summary>主菜单的一项：载入游戏、新的游戏、设置；字由字体画</summary>
    /// <remarks>16×16；九宫格 左 6 上 6 右 6 下 6；放一行字至少高 28；同一件的几态：normal、focus、pressed、disabled；来源：派生。</remarks>
    [Export]
    public NinePiece? MenuMenuItemPressed { get; set; }

    /// <summary>主菜单的一项：载入游戏、新的游戏、设置；字由字体画</summary>
    /// <remarks>16×16；九宫格 左 6 上 6 右 6 下 6；放一行字至少高 28；同一件的几态：normal、focus、pressed、disabled；来源：派生。</remarks>
    [Export]
    public NinePiece? MenuMenuItemDisabled { get; set; }

    /// <summary>键盘上任何一个键：框是这张，键名由字体写在里面</summary>
    /// <remarks>12×16；九宫格 左 4 上 0 右 4 下 0；来源：手画。</remarks>
    [ExportGroup("glyph", "Glyph")]
    [Export]
    public NinePiece? GlyphKeycap { get; set; }

    /// <summary>鼠标键</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? GlyphMouseLeft { get; set; }

    /// <summary>鼠标键</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? GlyphMouseRight { get; set; }

    /// <summary>鼠标键</summary>
    /// <remarks>16×16；来源：手画。</remarks>
    [Export]
    public Texture2D? GlyphMouseMiddle { get; set; }

    /// <summary>数字键（随身栏换格那一排）</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? GlyphKeyDigit0 { get; set; }

    /// <summary>数字键（随身栏换格那一排）</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? GlyphKeyDigit1 { get; set; }

    /// <summary>数字键（随身栏换格那一排）</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? GlyphKeyDigit2 { get; set; }

    /// <summary>数字键（随身栏换格那一排）</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? GlyphKeyDigit3 { get; set; }

    /// <summary>数字键（随身栏换格那一排）</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? GlyphKeyDigit4 { get; set; }

    /// <summary>数字键（随身栏换格那一排）</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? GlyphKeyDigit5 { get; set; }

    /// <summary>数字键（随身栏换格那一排）</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? GlyphKeyDigit6 { get; set; }

    /// <summary>数字键（随身栏换格那一排）</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? GlyphKeyDigit7 { get; set; }

    /// <summary>数字键（随身栏换格那一排）</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? GlyphKeyDigit8 { get; set; }

    /// <summary>数字键（随身栏换格那一排）</summary>
    /// <remarks>16×16；来源：切。</remarks>
    [Export]
    public Texture2D? GlyphKeyDigit9 { get; set; }

    /// <summary>每一格填了什么，给载入核对用：分组、名字、那张图、九宫格那一格（不是九宫格件的是 <c>null</c>）。</summary>
    internal IEnumerable<(string Group, string Name, Texture2D? Texture, NinePiece? Nine)> Cells()
    {
        yield return ("kit", "frame_thick_dark", KitFrameThickDark?.Texture, KitFrameThickDark);
        yield return ("kit", "frame_thick_light", KitFrameThickLight?.Texture, KitFrameThickLight);
        yield return ("kit", "inset", KitInset?.Texture, KitInset);
        yield return ("kit", "box", KitBox?.Texture, KitBox);
        yield return ("kit", "box_title", KitBoxTitle?.Texture, KitBoxTitle);
        yield return ("kit", "frame_thin_light", KitFrameThinLight?.Texture, KitFrameThinLight);
        yield return ("kit", "frame_thin_dark", KitFrameThinDark?.Texture, KitFrameThinDark);
        yield return ("kit", "ribbon", KitRibbon?.Texture, KitRibbon);
        yield return ("kit", "divider_h", KitDividerH?.Texture, KitDividerH);
        yield return ("kit", "divider_v", KitDividerV?.Texture, KitDividerV);
        yield return ("kit", "divider_h_light", KitDividerHLight?.Texture, KitDividerHLight);
        yield return ("kit", "divider_v_light", KitDividerVLight?.Texture, KitDividerVLight);
        yield return ("kit", "button_normal", KitButtonNormal?.Texture, KitButtonNormal);
        yield return ("kit", "button_focus", KitButtonFocus?.Texture, KitButtonFocus);
        yield return ("kit", "button_pressed", KitButtonPressed?.Texture, KitButtonPressed);
        yield return ("kit", "button_disabled", KitButtonDisabled?.Texture, KitButtonDisabled);
        yield return ("kit", "iconbutton_normal", KitIconbuttonNormal, null);
        yield return ("kit", "iconbutton_focus", KitIconbuttonFocus, null);
        yield return ("kit", "iconbutton_pressed", KitIconbuttonPressed, null);
        yield return ("kit", "iconbutton_disabled", KitIconbuttonDisabled, null);
        yield return ("kit", "button_icon_arrow_left", KitButtonIconArrowLeft, null);
        yield return ("kit", "button_icon_arrow_right", KitButtonIconArrowRight, null);
        yield return ("kit", "button_icon_close", KitButtonIconClose, null);
        yield return ("kit", "button_icon_confirm", KitButtonIconConfirm, null);
        yield return ("kit", "button_icon_pause", KitButtonIconPause, null);
        yield return ("kit", "button_icon_mail", KitButtonIconMail, null);
        yield return ("kit", "button_icon_menu", KitButtonIconMenu, null);
        yield return ("kit", "button_icon_settings", KitButtonIconSettings, null);
        yield return ("kit", "button_icon_sound_off", KitButtonIconSoundOff, null);
        yield return ("kit", "button_icon_sound_on", KitButtonIconSoundOn, null);
        yield return ("kit", "button_icon_help", KitButtonIconHelp, null);
        yield return ("kit", "button_icon_minus", KitButtonIconMinus, null);
        yield return ("kit", "button_icon_plus", KitButtonIconPlus, null);
        yield return ("kit", "button_icon_lock", KitButtonIconLock, null);
        yield return ("kit", "button_icon_unlock", KitButtonIconUnlock, null);
        yield return ("kit", "button_icon_star", KitButtonIconStar, null);
        yield return ("kit", "button_icon_arrow_up", KitButtonIconArrowUp, null);
        yield return ("kit", "button_icon_arrow_down", KitButtonIconArrowDown, null);
        yield return ("kit", "button_icon_alert", KitButtonIconAlert, null);
        yield return ("kit", "button_icon_play", KitButtonIconPlay, null);
        yield return ("kit", "button_icon_refresh", KitButtonIconRefresh, null);
        yield return ("kit", "button_icon_fast_forward", KitButtonIconFastForward, null);
        yield return ("kit", "button_icon_music", KitButtonIconMusic, null);
        yield return ("kit", "button_icon_trash", KitButtonIconTrash, null);
        yield return ("kit", "button_icon_shop", KitButtonIconShop, null);
        yield return ("kit", "button_icon_energy", KitButtonIconEnergy, null);
        yield return ("kit", "button_icon_trophy", KitButtonIconTrophy, null);
        yield return ("kit", "button_icon_camera", KitButtonIconCamera, null);
        yield return ("kit", "button_icon_gift", KitButtonIconGift, null);
        yield return ("kit", "button_icon_stats", KitButtonIconStats, null);
        yield return ("kit", "button_icon_battery", KitButtonIconBattery, null);
        yield return ("kit", "button_icon_skip", KitButtonIconSkip, null);
        yield return ("kit", "check_off", KitCheckOff, null);
        yield return ("kit", "check_on", KitCheckOn, null);
        yield return ("kit", "slot_normal", KitSlotNormal, null);
        yield return ("kit", "slot_disabled", KitSlotDisabled, null);
        yield return ("kit", "slot_locked", KitSlotLocked, null);
        yield return ("kit", "slot_select", KitSlotSelect, null);
        yield return ("kit", "slot_focus", KitSlotFocus, null);
        yield return ("kit", "rarity_edge", KitRarityEdge, null);
        yield return ("kit", "icon_frame", KitIconFrame, null);
        yield return ("kit", "socket_open", KitSocketOpen, null);
        yield return ("kit", "socket_locked", KitSocketLocked, null);
        yield return ("kit", "pip", KitPip, null);
        yield return ("kit", "pip_large", KitPipLarge, null);
        yield return ("kit", "star_fill", KitStarFill, null);
        yield return ("kit", "star_line", KitStarLine, null);
        yield return ("kit", "heart_empty", KitHeartEmpty, null);
        yield return ("kit", "heart_full", KitHeartFull, null);
        yield return ("kit", "heart_entrust_empty", KitHeartEntrustEmpty, null);
        yield return ("kit", "heart_entrust_full", KitHeartEntrustFull, null);
        yield return ("kit", "pip_enhance_empty", KitPipEnhanceEmpty, null);
        yield return ("kit", "pip_enhance_full", KitPipEnhanceFull, null);
        yield return ("kit", "tab_normal", KitTabNormal, null);
        yield return ("kit", "tab_selected", KitTabSelected, null);
        yield return ("kit", "tab_disabled", KitTabDisabled, null);
        yield return ("kit", "tab_focus", KitTabFocus, null);
        yield return ("kit", "tabtext_normal", KitTabtextNormal?.Texture, KitTabtextNormal);
        yield return ("kit", "tabtext_disabled", KitTabtextDisabled?.Texture, KitTabtextDisabled);
        yield return ("kit", "tabtext_focus", KitTabtextFocus?.Texture, KitTabtextFocus);
        yield return ("kit", "tabtext_selected", KitTabtextSelected?.Texture, KitTabtextSelected);
        yield return ("kit", "row_selected", KitRowSelected?.Texture, KitRowSelected);
        yield return ("kit", "row_focus", KitRowFocus?.Texture, KitRowFocus);
        yield return ("kit", "slider_track", KitSliderTrack?.Texture, KitSliderTrack);
        yield return ("kit", "slider_knob", KitSliderKnob, null);
        yield return ("kit", "scroll_track", KitScrollTrack?.Texture, KitScrollTrack);
        yield return ("kit", "scroll_knob", KitScrollKnob, null);
        yield return ("kit", "scroll_slim_track", KitScrollSlimTrack?.Texture, KitScrollSlimTrack);
        yield return ("kit", "scroll_slim_knob", KitScrollSlimKnob?.Texture, KitScrollSlimKnob);
        yield return ("kit", "font_mini", KitFontMini, null);
        yield return ("kit", "font_digits", KitFontDigits, null);
        yield return ("kit", "cursor_arrow", KitCursorArrow, null);
        yield return ("kit", "cursor_hand", KitCursorHand, null);
        yield return ("kit", "cursor_blocked", KitCursorBlocked, null);
        yield return ("kit", "mini_cursor", KitMiniCursor, null);
        yield return ("kit", "mini_right", KitMiniRight, null);
        yield return ("kit", "mini_left", KitMiniLeft, null);
        yield return ("kit", "mini_up", KitMiniUp, null);
        yield return ("kit", "mini_down", KitMiniDown, null);
        yield return ("kit", "mini_more", KitMiniMore, null);
        yield return ("kit", "mini_plus", KitMiniPlus, null);
        yield return ("kit", "mini_minus", KitMiniMinus, null);
        yield return ("kit", "mini_lock", KitMiniLock, null);
        yield return ("kit", "mini_done", KitMiniDone, null);
        yield return ("kit", "mini_free", KitMiniFree, null);
        yield return ("kit", "mini_unpaid", KitMiniUnpaid, null);
        yield return ("kit", "mini_irreversible", KitMiniIrreversible, null);
        yield return ("hud", "actionbar_frame", HudActionbarFrame?.Texture, HudActionbarFrame);
        yield return ("hud", "bar_track", HudBarTrack?.Texture, HudBarTrack);
        yield return ("hud", "bar_fill_hp", HudBarFillHp?.Texture, HudBarFillHp);
        yield return ("hud", "bar_fill_sp", HudBarFillSp?.Texture, HudBarFillSp);
        yield return ("hud", "bar_fill_mp", HudBarFillMp?.Texture, HudBarFillMp);
        yield return ("hud", "bar_fill_xp", HudBarFillXp?.Texture, HudBarFillXp);
        yield return ("hud", "bar_fill_lag", HudBarFillLag?.Texture, HudBarFillLag);
        yield return ("hud", "vigor_tube_track", HudVigorTubeTrack?.Texture, HudVigorTubeTrack);
        yield return ("hud", "vigor_tube_fill", HudVigorTubeFill?.Texture, HudVigorTubeFill);
        yield return ("hud", "gauge_icon_hp", HudGaugeIconHp, null);
        yield return ("hud", "gauge_icon_sp", HudGaugeIconSp, null);
        yield return ("hud", "gauge_icon_mp", HudGaugeIconMp, null);
        yield return ("hud", "gauge_icon_vigor", HudGaugeIconVigor, null);
        yield return ("hud", "objective", HudObjective, null);
        yield return ("hud", "screen_clear", HudScreenClear, null);
        yield return ("hud", "down_mark", HudDownMark, null);
        yield return ("hud", "portrait_frame", HudPortraitFrame, null);
        yield return ("world", "ring_cast", WorldRingCast, null);
        yield return ("world", "ring_cast_fill", WorldRingCastFill, null);
        yield return ("world", "ring_transfer", WorldRingTransfer, null);
        yield return ("world", "ring_transfer_fill", WorldRingTransferFill, null);
        yield return ("world", "cell_usable", WorldCellUsable, null);
        yield return ("world", "cell_blocked", WorldCellBlocked, null);
        yield return ("world", "bubble", WorldBubble?.Texture, WorldBubble);
        yield return ("world", "bubble_tail", WorldBubbleTail, null);
        yield return ("world", "hint_frame", WorldHintFrame?.Texture, WorldHintFrame);
        yield return ("world", "hint_icon", WorldHintIcon, null);
        yield return ("world", "immune", WorldImmune, null);
        yield return ("world", "device_nofuel", WorldDeviceNofuel, null);
        yield return ("world", "device_working", WorldDeviceWorking, null);
        yield return ("world", "device_done", WorldDeviceDone, null);
        yield return ("world", "place_ok", WorldPlaceOk, null);
        yield return ("world", "place_blocked", WorldPlaceBlocked, null);
        yield return ("world", "place_door", WorldPlaceDoor, null);
        yield return ("world", "place_occupied", WorldPlaceOccupied, null);
        yield return ("world", "drop_base", WorldDropBase, null);
        yield return ("panel", "profile_frame", PanelProfileFrame, null);
        yield return ("panel", "attr_level", PanelAttrLevel, null);
        yield return ("panel", "attr_str", PanelAttrStr, null);
        yield return ("panel", "attr_int", PanelAttrInt, null);
        yield return ("panel", "attr_agi", PanelAttrAgi, null);
        yield return ("panel", "attr_con", PanelAttrCon, null);
        yield return ("panel", "attr_point", PanelAttrPoint, null);
        yield return ("panel", "map_here", PanelMapHere, null);
        yield return ("panel", "map_blocked", PanelMapBlocked, null);
        yield return ("panel", "result_success", PanelResultSuccess, null);
        yield return ("panel", "result_retreat", PanelResultRetreat, null);
        yield return ("panel", "result_fail", PanelResultFail, null);
        yield return ("panel", "node_quest", PanelNodeQuest, null);
        yield return ("panel", "node_cutscene", PanelNodeCutscene, null);
        yield return ("panel", "codex_unknown", PanelCodexUnknown, null);
        yield return ("panel", "coin", PanelCoin, null);
        yield return ("tab", "character", TabCharacter, null);
        yield return ("tab", "character_disabled", TabCharacterDisabled, null);
        yield return ("tab", "students", TabStudents, null);
        yield return ("tab", "students_disabled", TabStudentsDisabled, null);
        yield return ("tab", "backpack", TabBackpack, null);
        yield return ("tab", "backpack_disabled", TabBackpackDisabled, null);
        yield return ("tab", "quests", TabQuests, null);
        yield return ("tab", "quests_disabled", TabQuestsDisabled, null);
        yield return ("tab", "map", TabMap, null);
        yield return ("tab", "map_disabled", TabMapDisabled, null);
        yield return ("tab", "relations", TabRelations, null);
        yield return ("tab", "relations_disabled", TabRelationsDisabled, null);
        yield return ("tab", "codex", TabCodex, null);
        yield return ("tab", "codex_disabled", TabCodexDisabled, null);
        yield return ("tab", "business", TabBusiness, null);
        yield return ("tab", "business_disabled", TabBusinessDisabled, null);
        yield return ("menu", "menu_frame", MenuMenuFrame?.Texture, MenuMenuFrame);
        yield return ("menu", "menu_item_normal", MenuMenuItemNormal?.Texture, MenuMenuItemNormal);
        yield return ("menu", "menu_item_focus", MenuMenuItemFocus?.Texture, MenuMenuItemFocus);
        yield return ("menu", "menu_item_pressed", MenuMenuItemPressed?.Texture, MenuMenuItemPressed);
        yield return ("menu", "menu_item_disabled", MenuMenuItemDisabled?.Texture, MenuMenuItemDisabled);
        yield return ("glyph", "keycap", GlyphKeycap?.Texture, GlyphKeycap);
        yield return ("glyph", "mouse_left", GlyphMouseLeft, null);
        yield return ("glyph", "mouse_right", GlyphMouseRight, null);
        yield return ("glyph", "mouse_middle", GlyphMouseMiddle, null);
        yield return ("glyph", "key_digit_0", GlyphKeyDigit0, null);
        yield return ("glyph", "key_digit_1", GlyphKeyDigit1, null);
        yield return ("glyph", "key_digit_2", GlyphKeyDigit2, null);
        yield return ("glyph", "key_digit_3", GlyphKeyDigit3, null);
        yield return ("glyph", "key_digit_4", GlyphKeyDigit4, null);
        yield return ("glyph", "key_digit_5", GlyphKeyDigit5, null);
        yield return ("glyph", "key_digit_6", GlyphKeyDigit6, null);
        yield return ("glyph", "key_digit_7", GlyphKeyDigit7, null);
        yield return ("glyph", "key_digit_8", GlyphKeyDigit8, null);
        yield return ("glyph", "key_digit_9", GlyphKeyDigit9, null);
    }
}
