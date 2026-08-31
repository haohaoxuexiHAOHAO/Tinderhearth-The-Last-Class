using Tinderhearth.Rules.Ui;
using Xunit;

namespace Tinderhearth.Rules.Tests.Ui;

/// <summary>
/// `UI-5` 相机五项行为的守卫：跟随死区、缩放与视野、边界钳制、震动、演出接管，加建造滚动与推镜。
/// </summary>
/// <remarks>
/// 这几条为什么必须有测试：相机的失效**全都不报错**。死区写成 0 只表现为「镜头有点抖」；钳制少
/// 一边只表现为「地图边上偶尔露白」；演出忘了归还只表现为「后面镜头不动了」；震动开关没关死只
/// 表现为「关了还是有点晃」。这些在实机上都要凑巧遇到才看得见，而断言当场就判。
///
/// **不测手感数值本身，只测量纲与区间。** 死区取 48 还是 40 屏幕像素只能实机收敛（`UI-12`），
/// 所以这里钉的是「能被侧视缩放整除」「落在算得出来的区间里」这类关系 —— 改值不会让测试失败，
/// 改坏关系会。
/// </remarks>
public class CameraTests
{
    private static CameraRig Rig(CameraView view, int logicalWidth = UiMetrics.BaseWidth)
    {
        var rig = new CameraRig(view);
        rig.SetLogicalViewport(logicalWidth, UiMetrics.BaseHeight);
        return rig;
    }

    // ── 缩放与视野 ──────────────────────────────────────────────────────

    [Fact]
    public void 只有两种视角且缩放取自正典而不是相机自己定的常量()
    {
        // 正典写明「战斗与出征一律侧视、基地与城区一律俯视。无例外」—— 多出第三种视角就不成立了。
        Assert.Equal(2, Enum.GetValues<CameraView>().Length);

        Assert.Equal(1, Rig(CameraView.TopDown).Zoom);
        Assert.Equal(UiMetrics.SideViewZoom, Rig(CameraView.SideView).Zoom);
    }

    [Fact]
    public void 侧视有效视野正好是逻辑分辨率的一半()
    {
        var rig = Rig(CameraView.SideView);

        Assert.Equal(UiMetrics.SideViewWorldWidth, rig.VisibleWidth);
        Assert.Equal(UiMetrics.SideViewWorldHeight, rig.VisibleHeight);
        Assert.Equal(320, rig.VisibleWidth);
        Assert.Equal(180, rig.VisibleHeight);
    }

    [Fact]
    public void 逻辑宽度撑开后视野跟着变而不是按六百四十算()
    {
        // aspect="expand" 下逻辑宽度是下限不是定值（UI-3 实测 3840×2130 得到 649×360）。
        // 按 640 算钳制范围会让宽窗口上的镜头停得太早，地图边缘露白。
        var wide = Rig(CameraView.SideView, logicalWidth: 649);

        Assert.Equal(325, wide.VisibleWidth);       // 向上取整：往大取只会让钳制更保守
        Assert.True(wide.VisibleWidth > UiMetrics.SideViewWorldWidth);
    }

    // ── 跟随死区 ────────────────────────────────────────────────────────

    [Fact]
    public void 角色在死区内移动镜头不动出了死区才跟()
    {
        var rig = Rig(CameraView.SideView);
        rig.SnapTo(100, 100);
        var half = rig.DeadzoneHalfWidth;

        Assert.False(rig.Follow(100 + half, 100));      // 正好贴在死区边上：不动
        Assert.Equal(100, rig.CenterX);

        Assert.True(rig.Follow(100 + half + 1, 100));   // 出了一像素：跟一像素
        Assert.Equal(101, rig.CenterX);

        // 反方向同理，且镜头只挪到「目标刚好贴在死区边上」，不多不少。
        Assert.True(rig.Follow(0, 100));
        Assert.Equal(half, rig.CenterX);
    }

    [Fact]
    public void 死区在两种视角下取景一致()
    {
        // 死区写成屏幕像素、除以缩放得世界像素，于是同一个数服务两种视角 —— 这就是「共用同一份
        // 实现」在数值上的形态。若哪天有人给某一种视角单独设一个死区，这条会当场失败。
        foreach (var view in Enum.GetValues<CameraView>())
        {
            var rig = Rig(view);
            Assert.Equal(CameraFeel.DeadzoneHalfWidthScreenPx, rig.DeadzoneHalfWidth * rig.Zoom);
            Assert.Equal(CameraFeel.DeadzoneHalfHeightScreenPx, rig.DeadzoneHalfHeight * rig.Zoom);
        }
    }

    [Fact]
    public void 两种视角对同样的屏幕位移给出相同的镜头位移()
    {
        // 这条是「两种视角共用同一份实现」的行为判据：目标在屏幕上走同样多的像素，镜头在屏幕上
        // 就该走同样多的像素。各写一套的话两边迟早分叉，而分叉在实机上只表现为「侧视手感不一样」。
        var moved = new List<int>();
        foreach (var view in Enum.GetValues<CameraView>())
        {
            var rig = Rig(view);
            rig.SnapTo(0, 0);
            rig.Follow(200 / rig.Zoom, 0);              // 屏幕上向右 200 像素
            moved.Add(rig.CenterX * rig.Zoom);          // 换算回屏幕像素
        }

        Assert.Equal(moved[0], moved[1]);
        Assert.Equal(200 - CameraFeel.DeadzoneHalfWidthScreenPx, moved[0]);
    }

    // ── 边界钳制 ────────────────────────────────────────────────────────

    [Fact]
    public void 相机被钳在地图内视口不越出边界()
    {
        var rig = Rig(CameraView.SideView);
        rig.SetWorldBounds(0, 0, 1000, 600);
        Assert.True(rig.ClampsHorizontally);
        Assert.True(rig.ClampsVertically);

        rig.SnapTo(-9999, -9999);
        Assert.Equal(rig.VisibleWidth / 2, rig.CenterX);         // 左上角贴边，视口左边缘正好在 0
        Assert.Equal(rig.VisibleHeight / 2, rig.CenterY);

        rig.SnapTo(9999, 9999);
        Assert.Equal(1000 - (rig.VisibleWidth / 2), rig.CenterX);
        Assert.Equal(600 - (rig.VisibleHeight / 2), rig.CenterY);

        // 判据不是「相机在边界内」而是「视口在边界内」—— 前者也会露白。
        Assert.True(rig.CenterX + (rig.VisibleWidth / 2) <= 1000);
        Assert.True(rig.CenterY + (rig.VisibleHeight / 2) <= 600);
    }

    [Fact]
    public void 视野比地图宽时居中而不是顶到一边()
    {
        // 这不是补丁：俯视 1 倍缩放下逻辑视野宽度已经是 640 的下限，而可建造区只有 640 世界像素
        // 宽 —— expand 撑开的宽窗口上视野真的会比地图宽。硬钳会把白边全挤到一侧。
        var rig = Rig(CameraView.TopDown, logicalWidth: 800);
        rig.SetWorldBounds(0, 0, 640, 480);

        Assert.False(rig.ClampsHorizontally);        // 横向没得钳
        Assert.True(rig.ClampsVertically);          // 纵向 360 < 480，仍要钳

        rig.SnapTo(0, 0);
        Assert.Equal(320, rig.CenterX);             // 居中，白边左右对称
        rig.SnapTo(9999, 9999);
        Assert.Equal(320, rig.CenterX);
        Assert.Equal(480 - (rig.VisibleHeight / 2), rig.CenterY);
    }

    [Fact]
    public void 没设地图边界时不钳制_测试与演出场景可以不设()
    {
        var rig = Rig(CameraView.SideView);

        Assert.False(rig.HasWorldBounds);
        rig.SnapTo(-5000, 7000);
        Assert.Equal(-5000, rig.CenterX);
        Assert.Equal(7000, rig.CenterY);
    }

    // ── 屏幕震动 ────────────────────────────────────────────────────────

    [Fact]
    public void 震动位移是整数世界像素且幅度不超过登记值()
    {
        var rig = Rig(CameraView.SideView);
        rig.Shake();

        Assert.True(rig.IsShaking);
        var maxScreen = 0;
        for (var i = 0; i < 8; i++)
        {
            maxScreen = Math.Max(maxScreen,
                Math.Max(Math.Abs(rig.ShakeOffsetX), Math.Abs(rig.ShakeOffsetY)) * rig.Zoom);
            rig.Advance(CameraFeel.ShakeSeconds / 8);
        }

        Assert.Equal(CameraFeel.ShakeAmplitudeScreenPx, maxScreen);
        Assert.False(rig.IsShaking);                // 到时自己停
        Assert.Equal(0, rig.ShakeOffsetX);
        Assert.Equal(0, rig.ShakeOffsetY);
    }

    [Fact]
    public void 关掉震动后重击不产生任何位移()
    {
        // 判据是**恒零**而不是「幅度调小」。对晕动敏感的玩家要的是不动，调小仍然会动。
        var rig = Rig(CameraView.SideView);
        rig.ShakeEnabled = false;
        rig.Shake();

        Assert.False(rig.IsShaking);
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(0, rig.ShakeOffsetX);
            Assert.Equal(0, rig.ShakeOffsetY);
            rig.Advance(CameraFeel.ShakeSeconds / 8);
        }
    }

    [Fact]
    public void 震动幅度除不尽缩放时抛而不是产生半像素()
    {
        // 侧视缩放 2 下，奇数屏幕像素的幅度换算成世界像素就是半像素，而半像素会让最近邻采样把
        // 像素块切成宽窄不一的条（UI-5 实测）。那看起来只是「画面有点脏」，不报错。
        var side = Rig(CameraView.SideView);
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => side.Shake(3, CameraFeel.ShakeSeconds));
        Assert.Contains("除不尽", ex.Message);

        // 俯视 1 倍缩放下任何整数都除得尽，所以同一个值在那边合法 —— 抛的理由是缩放而不是奇偶。
        Rig(CameraView.TopDown).Shake(3, CameraFeel.ShakeSeconds);
    }

    // ── 演出接管 ────────────────────────────────────────────────────────

    [Fact]
    public void 演出接管期间跟随空转归还后跟随行为与接管前一致()
    {
        var rig = Rig(CameraView.SideView);
        rig.SetWorldBounds(0, 0, 1000, 600);
        rig.SnapTo(500, 300);
        var zoomBefore = rig.Zoom;
        var halfBefore = rig.DeadzoneHalfWidth;

        var lease = rig.TakeOver("开场：主角混在难民里等安全扫描");
        Assert.True(rig.IsUnderCutsceneControl);
        Assert.Equal("开场：主角混在难民里等安全扫描", rig.CutsceneReason);

        // 演出把能改的都改一遍
        lease.OverrideZoom(4);
        rig.ShakeEnabled = false;
        rig.SetWorldBounds(0, 0, 4000, 4000);
        lease.MoveTo(3000, 3000);

        Assert.False(rig.Follow(700, 300));         // 接管期间跟随空转，不排队
        Assert.Equal(3000, rig.CenterX);

        lease.Release();

        Assert.False(rig.IsUnderCutsceneControl);
        Assert.Equal("", rig.CutsceneReason);
        Assert.Equal(zoomBefore, rig.Zoom);         // 缩放还原
        Assert.True(rig.ShakeEnabled);              // 震动开关还原
        Assert.Equal(halfBefore, rig.DeadzoneHalfWidth);

        // 归还后第一次跟随直接对准目标（不补那段横移），且钳制回到接管前那张地图。
        rig.Follow(700, 300);
        Assert.Equal(700, rig.CenterX);
        rig.SnapTo(9999, 9999);
        Assert.Equal(1000 - (rig.VisibleWidth / 2), rig.CenterX);

        // 死区仍是接管前那个：贴边不动，出一像素跟一像素。
        rig.SnapTo(500, 300);
        Assert.False(rig.Follow(500 + halfBefore, 300));
        Assert.True(rig.Follow(500 + halfBefore + 1, 300));
    }

    [Fact]
    public void 重复接管抛而不是让第二段演出静默顶掉第一段()
    {
        var rig = Rig(CameraView.SideView);
        using var first = rig.TakeOver("教学关开场");

        var ex = Assert.Throws<InvalidOperationException>(() => rig.TakeOver("对话特写"));
        Assert.Contains("教学关开场", ex.Message);      // 说得出是谁还握着
        Assert.Contains("对话特写", ex.Message);
    }

    [Fact]
    public void 接管期间绕过凭据驱动相机会抛()
    {
        var rig = Rig(CameraView.SideView);
        using var lease = rig.TakeOver("对话特写");

        var ex = Assert.Throws<InvalidOperationException>(() => rig.SnapTo(0, 0));
        Assert.Contains("凭据", ex.Message);
    }

    [Fact]
    public void 归还幂等且using能让抛异常的演出也归还()
    {
        var rig = Rig(CameraView.SideView);

        var lease = rig.TakeOver("演出脚本中途抛异常");
        lease.Release();
        lease.Release();                                    // 再还一次不抛
        Assert.False(rig.IsUnderCutsceneControl);
        Assert.Throws<InvalidOperationException>(() => lease.MoveTo(0, 0));

        // 写成局部函数而不是 lambda：只含 throw 的块 lambda 会被重载解析挑去
        // Assert.Throws<T>(Func<Task>)，而那个重载已标记 obsolete，编译当场失败。
        void 中途抛异常的演出()
        {
            using (rig.TakeOver("这段演出会抛"))
            {
                throw new InvalidDataException("演出脚本炸了");
            }
        }

        Assert.Throws<InvalidDataException>(中途抛异常的演出);
        Assert.False(rig.IsUnderCutsceneControl);           // using 仍然归还了
    }

    [Fact]
    public void 演出缩放只接受正整数()
    {
        var rig = Rig(CameraView.SideView);
        using var lease = rig.TakeOver("特写");

        lease.OverrideZoom(4);
        Assert.Equal(4, rig.Zoom);
        Assert.Equal(160, rig.VisibleWidth);                // 640 / 4

        Assert.Throws<ArgumentOutOfRangeException>(() => lease.OverrideZoom(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => lease.OverrideZoom(-2));
    }

    [Fact]
    public void 接管必须写明用途()
    {
        // 没有用途的接管在卡住时无法指认责任方 —— 而「忘了归还」正是这套接口要防的头号故障。
        Assert.Throws<ArgumentException>(() => Rig(CameraView.SideView).TakeOver("  "));
    }

    // ── 建造：滚动与边缘推镜 ────────────────────────────────────────────

    [Fact]
    public void 慢速滚动不会卡住不动_不足一像素攒着()
    {
        // 位置只暴露整数世界像素，所以慢速滚动若直接取整就永远是 0 —— 表现为「推方向没反应」。
        var rig = Rig(CameraView.TopDown);
        rig.SnapTo(0, 0);

        Assert.False(rig.Scroll(0.4, 0));
        Assert.False(rig.Scroll(0.4, 0));
        Assert.True(rig.Scroll(0.4, 0));
        Assert.Equal(1, rig.CenterX);
    }

    [Fact]
    public void 角色靠近可建造区边缘时朝那条边推镜()
    {
        var rig = Rig(CameraView.TopDown);
        rig.SetWorldBounds(-320, -240, 1280, 960);      // 地图比可建造区大，留出推镜的余地
        rig.SetBuildableArea(0, 0, 640, 480);
        rig.SnapTo(320, 240);

        // 角色贴到可建造区左边缘之内 margin 格
        Assert.True(rig.PushFromEdge(actorX: 20, actorY: 240, seconds: 1.0));
        Assert.Equal(320 - CameraFeel.EdgePushPixelsPerSecond, rig.CenterX);
        Assert.Equal(240, rig.CenterY);                 // 纵向没靠近，不动

        // 角色在正中：不推。贴边继续推也不该有反馈，见 GridCursor.Move 的注释。
        rig.SnapTo(320, 240);
        Assert.False(rig.PushFromEdge(320, 240, 1.0));
        Assert.Equal(320, rig.CenterX);

        // 右下角同时靠近两条边，两轴一起推
        rig.SnapTo(320, 240);
        Assert.True(rig.PushFromEdge(640 - 1, 480 - 1, 1.0));
        Assert.Equal(320 + CameraFeel.EdgePushPixelsPerSecond, rig.CenterX);
        Assert.Equal(240 + CameraFeel.EdgePushPixelsPerSecond, rig.CenterY);
    }

    [Fact]
    public void 没设可建造区就推镜会抛而不是静默不动()
    {
        // 静默不动会让「忘了从配置读尺寸」和「角色确实不在边上」长得一模一样。
        var rig = Rig(CameraView.TopDown);
        var ex = Assert.Throws<InvalidOperationException>(() => rig.PushFromEdge(0, 0, 1.0));
        Assert.Contains("可建造区", ex.Message);
    }

    [Fact]
    public void 建造靠滚动与推镜而不是缩放()
    {
        // 作者 2026-08-30 定：建造不做缩放。所以除演出接管之外没有任何改缩放的入口 ——
        // 这条测的是接口形状：不握凭据就改不了缩放。
        var rig = Rig(CameraView.TopDown);
        Assert.Equal(1, rig.Zoom);
        rig.SetBuildableArea(0, 0, 640, 480);
        rig.Scroll(CameraFeel.ScrollPixelsPerSecond * 0.5, 0);
        rig.PushFromEdge(10, 240, 0.1);
        Assert.Equal(1, rig.Zoom);                      // 滚动与推镜都不碰缩放
    }

    [Fact]
    public void 演出接管期间建造滚动与推镜都空转()
    {
        var rig = Rig(CameraView.TopDown);
        rig.SetBuildableArea(0, 0, 640, 480);
        using var lease = rig.TakeOver("建造完成的展示镜头");
        lease.MoveTo(100, 100);

        Assert.False(rig.Scroll(50, 50));
        Assert.False(rig.PushFromEdge(0, 0, 1.0));
        Assert.Equal(100, rig.CenterX);
    }

    // ── 手感初值的量纲与区间 ────────────────────────────────────────────

    [Fact]
    public void 手感初值能被侧视缩放整除_否则换算出半像素()
    {
        Assert.Equal(0, CameraFeel.DeadzoneHalfWidthScreenPx % UiMetrics.SideViewZoom);
        Assert.Equal(0, CameraFeel.DeadzoneHalfHeightScreenPx % UiMetrics.SideViewZoom);
        Assert.Equal(0, CameraFeel.ShakeAmplitudeScreenPx % UiMetrics.SideViewZoom);
    }

    [Fact]
    public void 死区落在算得出来的区间里()
    {
        // 上限：角色始终留在画面中间那一半，即死区半宽 ≤ 视野半宽的一半。
        var halfViewX = UiMetrics.BaseWidth / 2;
        var halfViewY = UiMetrics.BaseHeight / 2;
        Assert.InRange(CameraFeel.DeadzoneHalfWidthScreenPx, UiMetrics.Grid, halfViewX / 2);
        Assert.InRange(CameraFeel.DeadzoneHalfHeightScreenPx, UiMetrics.Grid, halfViewY / 2);

        // 竖向视野本来就只有 180，所以竖向死区不该比横向宽。
        Assert.True(CameraFeel.DeadzoneHalfHeightScreenPx <= CameraFeel.DeadzoneHalfWidthScreenPx);
    }

    [Fact]
    public void 震动幅度与时长落在算得出来的区间里()
    {
        // 幅度下限是「侧视下表达得出来的最小位移」，上限是「别把角色晃出死区」。
        Assert.InRange(CameraFeel.ShakeAmplitudeScreenPx,
            UiMetrics.SideViewZoom, CameraFeel.DeadzoneHalfHeightScreenPx);

        // 时长：短于一个震动周期只会看到一次跳动；长过四分之一秒会盖住下一次输入的反馈
        // （正典对顿帧那条约束的邻居：不能长到打断连段输入节奏）。
        Assert.InRange(CameraFeel.ShakeSeconds, 0.05, 0.25);
        Assert.True(CameraFeel.ShakeSeconds * CameraFeel.ShakeHertz >= 2,
            "时长内装不下一个完整震动周期，玩家看到的是一次跳动而不是震动");
    }

    [Fact]
    public void 推镜与滚动速度落在算得出来的区间里()
    {
        // 触发余量必须小于视野半宽，否则一进场就在推镜。
        Assert.True(CameraFeel.EdgePushMarginPixels < UiMetrics.SideViewWorldWidth / 2);
        Assert.Equal(CameraFeel.EdgePushMarginCells * UiMetrics.BaseUnit,
            CameraFeel.EdgePushMarginPixels);

        // 推镜是提示不是操作，所以比手动滚动慢。
        Assert.True(CameraFeel.EdgePushPixelsPerSecond < CameraFeel.ScrollPixelsPerSecond);

        // 横穿一屏不该久到让人不耐烦：按这个速度走完 640 世界像素不超过 4 秒。
        Assert.True(CameraFeel.ScrollPixelsPerSecond >= UiMetrics.BaseWidth / 4);
    }
}
