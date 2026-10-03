using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>
/// `UI-34` 随身栏那条六格：格数从哪来、选中位怎么动、内容换了之后它动不动。
/// </summary>
/// <remarks>
/// 这几条为什么要测：它们的失效方式**都不报错**。格数与技能位分叉只在拉宽窗口时看得出来；
/// 「空格选不中」表现为按了没反应；「换内容把选中位重置了」表现为玩家排一次背包就得重新选一次种子。
///
/// **这里一个玩法数值都没有。** 随身栏只答「手上是哪一格」，那一格能不能拿去播种归 `GP-128`、
/// 能不能吃归 `GP-129`。
/// </remarks>
public class CarrySlotTests
{
    private static CarrySlot Seed(string id, int count = 1) => new(id, count);

    private static CarrySlotBar Filled() => new(
        [.. Enumerable.Range(0, CarrySlotBar.SlotCount).Select(i => Seed($"seed_{i}", i + 1))]);

    // ── 格数：一条链到底，中间不许有抄出来的 6 ──────────────────────

    [Fact]
    public void 格数与技能位同一个数()
    {
        // 承重项。HUD 那一块的宽度按「几个 16px 图标横排」算，两种内容同宽这件事**由算式保证** ——
        // 抄一个 6 的后果是改格数时两块宽度悄悄分叉，而分叉只在拉宽窗口时看得出来。
        Assert.Equal(InputActions.Skills.Count, CarrySlotBar.SlotCount);
        Assert.Equal(CarrySlotBar.SlotCount, HudLayout.CarrySlotCount);
    }

    [Fact]
    public void 每一格各有一个输入动作()
    {
        // 动作条数与格数对不上的表现是「最后一格按不动」，而那看起来像键位没绑。
        Assert.Equal(CarrySlotBar.SlotCount, InputActions.CarrySlots.Count);
        foreach (var action in InputActions.CarrySlots)
        {
            Assert.Contains(action, InputActions.All);
        }
    }

    [Fact]
    public void 随身栏那一行放得进动作条那一块()
    {
        // 随身栏沿用技能位那一块的框（`HudBlock.ActionBar`），所以六格紧邻横排必须装得下。
        // 装不下的表现是最后一格被面板边框切掉半个图标。
        var block = HudLayout.ContentSizeOf(HudBlock.ActionBar);

        Assert.True(HudLayout.CarryRowWidth <= block.Width,
            $"随身栏 {HudLayout.CarryRowWidth}px 宽，装不进 {block.Width}px 的块");
        Assert.Equal(UIMetrics.IconSmall, block.Height);   // 一行 16px 图标，高度两种内容一致
    }

    [Fact]
    public void 没有为随身栏新开HUD块()
    {
        // PRD 的硬判据之一：取值个数不变。新开一块就要重算占屏与可读区那两笔账。
        Assert.Equal(4, Enum.GetValues<HudBlock>().Length);
    }

    [Theory]
    [InlineData(UIMetrics.BaseWidth, UIMetrics.BaseHeight)]
    [InlineData(649, UIMetrics.BaseHeight)]     // UI-3 实测的那个宽窗口逻辑尺寸
    [InlineData(1280, UIMetrics.BaseHeight)]
    public void 随身栏进来之后没有哪一块压到角色可读区(int width, int height)
    {
        // 既有的硬判据，本需求不许让它变红。它现在自动仍然成立，**因为随身栏没有新开块、
        // 也没有改那一块的尺寸算式** —— 输入一个字没变，所以结论一个字没变。
        Assert.Empty(HudLayout.BlocksOverActorBand(width, height));
    }

    // ── 选中位 ──────────────────────────────────────────────────────

    [Fact]
    public void 空栏也恰好有一格选中()
    {
        // FR-2。「一格都没选中」这个状态不存在 —— 那会让按下动作键时无从取值，
        // 而调用方就得各自处理一个本不该出现的情况。
        var bar = CarrySlotBar.Empty();

        Assert.Equal(0, bar.SelectedIndex);
        Assert.Equal(CarrySlot.Empty, bar.Selected);
        Assert.True(bar.Selected.IsEmpty);
    }

    [Fact]
    public void 六格都选得中且序号一一对应()
    {
        var bar = Filled();
        for (var i = 0; i < CarrySlotBar.SlotCount; i++)
        {
            Assert.True(bar.Select(i));
            Assert.Equal(i, bar.SelectedIndex);
            Assert.Equal($"seed_{i}", bar.Selected.ItemId);
        }
    }

    [Fact]
    public void 空格子也选得中()
    {
        // 承重项。拒绝选中空格看着更聪明，代价是玩家按下去没反应而他不知道为什么 ——
        // 而「手上什么都不拿」本来就是一个他可能想要的状态。
        var bar = CarrySlotBar.Empty();

        Assert.True(bar.Select(CarrySlotBar.SlotCount - 1));
        Assert.Equal(CarrySlotBar.SlotCount - 1, bar.SelectedIndex);
        Assert.True(bar.Selected.IsEmpty);
    }

    [Fact]
    public void 越界选不中而且选中位不动()
    {
        // **不静默钳到两端**：钳了的话「按了第 7 格」会看起来像「按了第 6 格」，
        // 而那正是键位绑错时最需要看出来的差别。
        //
        // 上界用 SlotCount 算出来、不写字面量 —— 写了的话改格数时这条会变成测一个不相干的数。
        var bar = Filled();
        bar.Select(2);

        foreach (var index in (int[])[-1, CarrySlotBar.SlotCount, CarrySlotBar.SlotCount + 93])
        {
            Assert.False(bar.Select(index), $"{index} 不该选得中");
            Assert.Equal(2, bar.SelectedIndex);
        }
    }

    [Fact]
    public void 往后挪一圈回到原处且六格都到得了()
    {
        // 手柄那一侧就靠这个遍历六格。**绕回而不是钳住** —— 钳住的话玩家在末尾继续按会
        // 「没反应」，而他分不清是到头了还是键坏了。
        var bar = Filled();
        var seen = new List<int>();

        for (var i = 0; i < CarrySlotBar.SlotCount; i++)
        {
            seen.Add(bar.SelectedIndex);
            bar.SelectNext();
        }

        Assert.Equal(Enumerable.Range(0, CarrySlotBar.SlotCount), seen);
        Assert.Equal(0, bar.SelectedIndex);     // 绕完一圈回到起点
    }

    [Fact]
    public void 往前挪一圈同样绕得回来()
    {
        var bar = Filled();
        bar.SelectPrevious();
        Assert.Equal(CarrySlotBar.SlotCount - 1, bar.SelectedIndex);   // 从第一格往前绕到末格

        for (var i = 1; i < CarrySlotBar.SlotCount; i++)
        {
            bar.SelectPrevious();
        }

        Assert.Equal(0, bar.SelectedIndex);
    }

    // ── 内容是投影，不是快照 ────────────────────────────────────────

    [Fact]
    public void 换一份内容选中位不动()
    {
        // 承重项，而且是 `UI-35` 换来源时不许改的行为之一：随身栏是投影。背包里那一堆用完了、
        // 或者玩家排了一次序，变的是格子里装什么，不是「他手上是第几格」。
        var bar = Filled();
        bar.Select(4);

        bar.Fill([.. Enumerable.Repeat(CarrySlot.Empty, CarrySlotBar.SlotCount)]);

        Assert.Equal(4, bar.SelectedIndex);
        Assert.True(bar.Selected.IsEmpty);
    }

    [Fact]
    public void 那一堆用完之后对应那一格当场变空()
    {
        var bar = Filled();
        bar.Select(0);
        Assert.False(bar.Selected.IsEmpty);

        var next = bar.Slots.ToList();
        next[0] = CarrySlot.Empty;
        bar.Fill(next);

        Assert.True(bar.Selected.IsEmpty);
    }

    [Fact]
    public void 格数对不上一律抛()
    {
        // 静默补齐的后果是「投影少给了一格」看起来像「那一格是空的」，
        // 而这两件事的补救办法完全不同。
        Assert.Throws<ArgumentException>(() => new CarrySlotBar([CarrySlot.Empty]));
        Assert.Throws<ArgumentException>(() => CarrySlotBar.Empty().Fill([CarrySlot.Empty]));
    }

    // ── 一格里显示什么 ──────────────────────────────────────────────

    [Fact]
    public void 件数为一时不显示数字()
    {
        // 画了的话一屏六格全是「1」，那一行字不携带任何信息，却占掉 16px 图标本来就不多的面积。
        Assert.False(Seed("bread").ShowsCount);
        Assert.True(Seed("bread", 2).ShowsCount);
        Assert.False(CarrySlot.Empty.ShowsCount);
    }

    [Theory]
    [InlineData("", 3)]            // 没标识
    [InlineData("bread", 0)]       // 有标识没件数 —— 刚用完，玩家看到的是空格
    [InlineData("bread", -1)]
    public void 这几种都算空格(string id, int count) =>
        Assert.True(new CarrySlot(id, count).IsEmpty);

    [Fact]
    public void 随身栏不提供任何增减件数的入口()
    {
        // 一条结构约束（**反证**）：它挡的是「随身栏偷偷变成第二个容器」那种实现。
        // 少一件是背包那一侧的事，给这里加一个 Take() 会立刻造出两份存储，
        // 而两份对不上时没有任何东西判得出来。
        var methods = typeof(CarrySlot).GetMethods()
            .Concat(typeof(CarrySlotBar).GetMethods())
            .Select(m => m.Name)
            .ToList();

        Assert.DoesNotContain("Take", methods);
        Assert.DoesNotContain("Consume", methods);
        Assert.DoesNotContain("Add", methods);
        Assert.DoesNotContain("Remove", methods);
    }

    // ── 存档那一个字段 ──────────────────────────────────────────────

    [Fact]
    public void 载入时越界一律退回第一格并且说得出它退过()
    {
        // 与 Select 的越界处理**刻意不同**：那一条的输入来自当场的按键，抛了或拒了都好办；
        // 这一条的输入来自一个没人修得了的文件（旧版本存档的格数可能不同），
        // 而 ADR-0015 对旧版本的口径是补默认值、不是报错 —— 报错等于让玩家的档打不开。
        var bar = Filled();

        Assert.False(bar.Restore(CarrySlotBar.SlotCount));
        Assert.Equal(0, bar.SelectedIndex);

        Assert.False(bar.Restore(-1));
        Assert.Equal(0, bar.SelectedIndex);

        Assert.True(bar.Restore(3));
        Assert.Equal(3, bar.SelectedIndex);
    }

    // ── 键位 ────────────────────────────────────────────────────────

    [Fact]
    public void 键鼠上六格是数字键1到6顺序一一对应()
    {
        var expected = new[]
        {
            InputSymbol.Digit1, InputSymbol.Digit2, InputSymbol.Digit3,
            InputSymbol.Digit4, InputSymbol.Digit5, InputSymbol.Digit6,
        };

        for (var i = 0; i < InputActions.CarrySlots.Count; i++)
        {
            var keys = InputBindings.For(InputActions.CarrySlots[i],
                                        InputDeviceKind.KeyboardMouse);
            Assert.Single(keys);
            Assert.Equal(expected[i], keys[0].Symbol);
        }
    }

    [Fact]
    public void 手柄上靠十字键左右挪选中位而不是六个直选键()
    {
        // 手柄上凑不出六个空位，所以改成挪位。两个键够遍历六格，因为选中位是绕回的。
        Assert.Equal(InputSymbol.PadDpadLeft,
            InputBindings.For(InputActions.CarryPrev, InputDeviceKind.Gamepad)[0].Symbol);
        Assert.Equal(InputSymbol.PadDpadRight,
            InputBindings.For(InputActions.CarryNext, InputDeviceKind.Gamepad)[0].Symbol);

        foreach (var action in InputActions.CarrySlots)
        {
            Assert.Empty(InputBindings.For(action, InputDeviceKind.Gamepad));
        }
    }

    [Fact]
    public void 手柄那套挪位不与两个扳机冲突()
    {
        // 扳机现在切两组技能位。挪位键落在 D-pad 上，与扳机是不同的物理位 ——
        // 所以「按住扳机挑技能」与「换随身那一格」不会互相干扰。
        var triggers = new[] { InputSymbol.PadTriggerLeft, InputSymbol.PadTriggerRight };
        var movers = new[] { InputSymbol.PadDpadLeft, InputSymbol.PadDpadRight };

        Assert.Empty(triggers.Intersect(movers));
    }

    [Fact]
    public void 随身栏那几个动作两侧各缺一半而且都登记了理由()
    {
        // 「要么有绑定、要么有一条带理由的豁免」那条已有测试盯着全部动作；这一条额外钉住
        // **两侧缺的理由是相反的**：键鼠有六个空键所以直选，手柄没有所以挪位。
        // 理由写反或者复制粘贴成同一句，是这类登记最容易出的错。
        foreach (var action in InputActions.CarrySlots)
        {
            Assert.True(InputBindings.Exemptions.TryGetValue(
                (action, InputDeviceKind.Gamepad), out var padWhy));
            Assert.Contains("六个空位", padWhy);
        }

        foreach (var action in (string[])[InputActions.CarryPrev, InputActions.CarryNext])
        {
            Assert.True(InputBindings.Exemptions.TryGetValue(
                (action, InputDeviceKind.KeyboardMouse), out var keyWhy));
            Assert.Contains("数字键", keyWhy);
        }
    }
}
