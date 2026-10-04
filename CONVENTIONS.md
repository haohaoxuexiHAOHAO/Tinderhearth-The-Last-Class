# 编码规范

本文写**怎么写代码**。分层与边界的理由在 `ARCHITECTURE.md`，语法说明在设计仓
`reference/学习CSharp-Java程序员向.md`，本文不重复它们。

两个快速定位：看不懂某个语法去查 C# 学习文档；想知道某条约束为什么存在去查 `ARCHITECTURE.md`
或注释里的踩坑记录引用。

## 命名

| 元素 | 形式 | 例子 |
| --- | --- | --- |
| 命名空间、类型、方法、属性、常量、枚举值 | PascalCase | `HudLayout`、`ContentSizeOf`、`MaxTeammates` |
| 私有字段 | `_camelCase` | `_router`、`_roots`、`_eol_backup` |
| 局部变量、参数 | camelCase | `viewportWidth`、`block`、`rel` |
| 接口 | `I` 前缀 + PascalCase | `IActorController`、`IContentSource` |
| 布尔属性/局部变量 | 动词开头，读起来像断言 | `IsFull`、`IsShown`、`bad_eol` |
| **两字母缩写** | 该用 PascalCase 的位置**全大写**，否则全小写 | `UIMetrics`、`WorldUILayout`、`Tinderhearth.Rules.UI`；局部变量写 `ui` |

两字母缩写那条来自 Godot 的 C# 风格指南，它举的例子就是 `UI`。三字母及以上**不适用**，
照常 PascalCase（`Hud` 而不是 `HUD`，`Vfx` 而不是 `VFX`）。`Id` 不是缩写，当普通标识符写。
**引擎自己的写法优先**：`Error.Ok` 是 Godot 的 API，跟着它写 `Ok`，不要为这条规则把它改成 `OK`。

命名空间与目录对应（`Tinderhearth.Rules.Foundation` ↔ `rules/Foundation/`），
是约定而不是语言强制——但**必须一致**，否则「东西在哪」需要靠记忆而不是路径。
目录只在有代码时建，命名空间跟着走。

## 文件与目录命名

Godot 官方的建议是：文件夹与文件名用 snake_case，**C# 脚本例外**（按类名走 PascalCase）；
节点名用 PascalCase，因为内置节点就是这个形状。

**理由不是好看**：导出后的 `.pck` 虚拟文件系统**区分大小写**，而 Windows 与 macOS 的文件系统
默认不区分。路径大小写写错，在编辑器里一切正常，只在导出后或 Linux 上才找不到文件 ——
**而且不报编译错**，表现是某张图没了、某份配置读不到。

| 元素 | 形式 | 例子 |
| --- | --- | --- |
| 场景文件 | 小写 snake_case | `scenes/main.tscn`、`scenes/training_room.tscn` |
| 资源、素材与数据文件 | 小写 | `data/config/game.json`、`assets/self-drawn/tiles/tilled.png` |
| 节点名 | PascalCase | 场景里的 `Main`、`TrainingRoom`；代码里设的 `LevelHud`、`CastRing` |
| C# 文件 | PascalCase，与类名一致 | `UIMetrics.cs`、`PlayerActor.cs` |
| C# 目录 | PascalCase，与命名空间一致 | `rules/UI/`、`src/World/Farm/` |

有理由的偏离写在这里，**不是漏改**：

- **C# 目录用 PascalCase**。官方那条例外只给 C# 文件，没给文件夹 —— 严格读，`src/UI/` 本该是
  `src/ui/`。但本仓要求命名空间与目录对应（见上面「命名」一节），而命名空间是 PascalCase，
  于是目录只能跟着走；官方那页自己也留了口子：风格指南另有规定时可以破 snake_case，只要一致。
  **这条偏离不是免疫，是被守卫兜着的**：`src/` 在 `res://` 下，`.cs` 以 1 字节占位的形式进包
  （实测包内有 `src/Main.cs` 这样的条目），而 `scenes/main.tscn` 就是按 `res://src/Main.cs`
  这个路径引脚本的 —— 大小写一样要对。保护它的不是「不在资源路径里」，是两件事：这类路径由
  编辑器写而不是人手敲，以及 `check_names.py` 把每一处 `res://` 字面量逐段比过大小写。
- **素材文件名用中划线**（`bar-fill.png`、`self-drawn/`）而不是下划线。全小写，同样吃不到那个坑；
  而改名要动一批写死的路径常量，并且**必须在 Godot 编辑器里做** —— 在外面改，`.import` 附属物
  与引用得手工同步。**只约束新增的**：新素材用下划线。

**允许带大写字母的文件只有下面这些**，想往这张表添一行之前先问「它的大小写会不会被某个路径
字符串写死」—— 会，就别添。

| 文件 | 为什么 |
| --- | --- |
| `assets/fonts/LICENSE-OFL.txt` | 授权文件的通行名。它的大小写同时写在磁盘、`rules/UI/PixelFont.cs` 与 `export_presets.cfg` 的 `include_filter` 里，三处必须一致 |
| `assets/downloaded/fists-of-fury/LICENSE.txt`、`assets/downloaded/samurai/License.txt` | 上游原名。改了就断了与来源的对应，而两份来源本来就不同名 |
| `data/text/zh-CN.json` | 语言标签的标准写法。`src/Main.cs` 拿它的全名精确匹配，改名要同时改那一处 |
| 仓库根的 `README.md`、`ARCHITECTURE.md`、`CONVENTIONS.md`、`.csproj`、`.sln` | 不是引擎资源，不进发行包 |

这一节由 `tools/check_names.py` 静态核，它是 `verify.py` 第一步的一部分。
**守卫判得到的只有文件名、目录名与场景里的节点名**；代码里 `Name = "..."` 设的节点名
与两字母缩写那条判不到，写的时候自己守。

## 大括号与缩进

Allman 风格：大括号各占一行，与 `if`、`for`、方法等**对齐**，而不是行尾。

```csharp
// 对
public static HudRect SizeOf(HudBlock block)
{
    var content = ContentSizeOf(block);
    return new HudRect(0, 0, content.Width, content.Height);
}

// 错：大括号不对齐
public static HudRect SizeOf(HudBlock block) {
    ...
}
```

`switch` 表达式（`x switch { ... }`）、集合表达式（`[.. ...]`）、对象初始化器用各自的缩进约定，
不强制 Allman —— 那是语句级的大括号规则。

## 属性与字段

优先用属性，不写 getter/setter 方法。

```csharp
// 对
public int Count { get; private set; }

// 错
private int _count;
public int GetCount() => _count;
```

私有字段只留**有理由的**：自动属性能做到的事不用手写字段。常见有理由的场景：
需要在访问器里加验证逻辑（用 C# 14 的 `field` 关键字，不新开 `_backing`），
或者字段被多个属性共用。

## 局部变量与 `var`

局部变量在**右侧类型一眼可知**时用 `var`；类型不显然时写全类型名。`var` 省的是重复，不是信息。

右侧是 `new T(...)`、字面量、或名字已说清返回什么的方法时，类型就在眼前，`var` 去掉一次复述：

```csharp
// 对：右侧已经写了类型
var rig = new CameraRig(view);
for (var i = 0; i < count; i++) { ... }
```

右侧是**裸属性或裸方法**、返回类型看不出来时，`var` 把类型藏了——读者得跳去看定义才知道后面能拿它做什么：

```csharp
// 错：SkillGroup？Vector2？得跳去看定义才知道
var before = _modifiers.Active;
var move = router.MoveDirection();

// 对：类型写出来，就近可读
SkillGroup before = _modifiers.Active;
Vector2 move = router.MoveDirection();
```

这是**就近可读性**，不是禁用 `var`：判据是「读者能不能在本行看出类型」，而不是「有没有用 `var`」。
成串构造对象的探针与脚手架里 `var x = new Xxx()` 仍照旧——那里类型全在右边。

## 表达式体成员

单行的取值、转发与简单计算用 `=>`，不写 `{ return ...; }`。

```csharp
public int Right => X + Width;
public bool IsFull => UsedSlots >= Capacity;
public static TalentDef? Get(string id) => _all.Find(t => t.Id == id);
```

超过一行、有副作用、或有早返回时写完整方法体。

## 三元与多路分派

三元 `?:` 只用**一层**。条件里、或真分支里再套一个 `?:`（嵌套三元）不许——分支与条件的对应关系
得靠数缩进才读得对。三个以上分支、或按枚举／状态选值，用 `switch` 表达式，一行一个「情形 → 结果」：

```csharp
// 错：嵌套三元，哪个 : 配哪个 ? 要数着看
var action = motor.Phase == MotorPhase.Hurt
    ? _hurtHeavy ? "heavy_hit" : "light_hit"
    : combo.IsAttacking
    ? combo.Kind == ComboKind.Heavy ? "heavy" : LightSheet(combo.Step)
    : motor.Phase == MotorPhase.Dodge ? "dodge" : ...;

// 对：switch 表达式，情形与结果对齐；分支内仍可用单层三元
var action = motor.Phase switch
{
    MotorPhase.Hurt => _hurtHeavy ? "heavy_hit" : "light_hit",
    _ when combo.IsAttacking => combo.Kind == ComboKind.Heavy ? "heavy" : LightSheet(combo.Step),
    MotorPhase.Dodge => "dodge",
    _ => "idle",
};
```

**单层**三元（`a ? x : y`）是「取值版 if/else」，仍然首选，不要为它写四行 `if`：

```csharp
public UISurface? Top => _stack.Count > 0 ? _stack[^1] : null;
```

带 `when` 守卫的 `switch` 表达式**保序**：情形自上而下匹配、第一个成立的赢，与原来的 `?:` 链一致——
把嵌套三元改成 `switch` 时照原顺序摆臂，行为不变。

## 记录类型

轻量的数据载体优先用 `record` 或 `record struct`。

- **值语义、不可变**：`readonly record struct`（如 `HudRect`、`ActorView`）
- **引用语义、需继承**：`record class`
- **内部实现细节**（只在一个类里用的小数据包）：`private sealed record`（如 `GaugeRow`、`SkillCell`）

`sealed` 对内部 record 是强制的——它们不是设计为扩展点的类型，明说出来，读者就不会试着去继承。

## 构造与 `new`

目标类型 `new`（写 `new(...)`、省掉类型名）用在**类型可还原**处：左边有显式类型、或在有返回类型的
`return`／表达式体成员里。**不要在 `var` 后面用**——那样等号两头都没有类型：

```csharp
private readonly SkillModifierState _modifiers = new();       // 类型在左边
public ActorIntent Decide(in ActorView view) => new("idle");  // 返回类型在签名里
```

构造**参数 ≥4 个、或有多个同类型参数**时用具名实参。位置一多，`new(1, 0, true, false, true, false, true)`
里每个值是谁全靠数位置，而且改了记录字段顺序还不报错：

```csharp
// 错：五个 bool 靠位置区分，读的人得对着定义数
return new(Math.Sign(move.X), Math.Sign(move.Y),
    router.IsJustPressed(InputActions.Jump), router.IsJustPressed(InputActions.AttackLight), ...);

// 对：具名实参，每个值标着自己是谁
return new(
    HorizontalSign: Math.Sign(move.X),
    DepthSign: Math.Sign(move.Y),
    JumpPressed: router.IsJustPressed(InputActions.Jump),
    LightPressed: router.IsJustPressed(InputActions.AttackLight),
    ...);
```

全默认值的构造（如 `CombatInput.None => new(0, 0, false, false, false, false, false)`）没有可混淆的信息，
位置写法可留。

## 可空引用类型

项目开了 `<Nullable>enable</Nullable>`，默认引用类型不可为空。

- 允许 null 的地方**必须**在类型上加 `?`（`string?`、`TalentDef?`）
- 确定不为 null 但编译器看不出来时用 `!` 压制警告，并在旁边写原因

```csharp
// 单例在 _EnterTree() 里赋值，声明时先占位
public static EventBus Instance { get; private set; } = null!;
```

`?.` 和 `??` 是首选的空值处理方式，比显式判空嵌套更清晰：

```csharp
// 找不到就返回 0
public int CountOf(string id) => _stacks.FirstOrDefault(s => s.Def.Id == id)?.Count ?? 0;
```

## 集合与 LINQ

优先用 LINQ（`.Where`、`.Select`、`.FirstOrDefault` 等），而不是手写 `for` 循环，
除非性能测量显示有问题。

`List<T>` 的 `.Find` 等同于 LINQ 的 `.FirstOrDefault`，两者都可以，在同一文件里保持一致。

字符串比较直接用 `==`，不用 `.Equals()`。C# 的字符串 `==` 比较内容，不比引用。

集合表达式（C# 12+）优先于 `new List<...> { ... }` 形式：

```csharp
// 对
return [.. Enum.GetValues<HudBlock>().Select(b => (b, RectOf(b, w, h)))];

// 也对，当类型推断需要帮助时
IReadOnlyList<HudBlock> result = [HudBlock.Resources, HudBlock.Skills];
```

## 注释与文档

注释写给读代码的人，包括三个月后的自己。不写给评审看，所以不用在注释里论证取舍是对的。

公开 API（`public`、`internal`）写 XML 文档注释。`<summary>` 一句话说这个东西是什么、量纲是什么；
`<remarks>` 只在有代码读不出来的约束时才写：

```csharp
/// <summary>某一块 HUD 在屏幕上占多大，单位是界面像素。</summary>
/// <remarks>
/// 条比行矮，靠 ShrinkCenter 在行里居中。不用纵向偏移就不会出现半格。
/// </remarks>
public static HudRect SizeOf(HudBlock block) { ... }
```

`<remarks>` 里该写什么，判据是「改这段代码的人需不需要看到它」：

| 这一类 | 写哪 |
| --- | --- |
| 引擎行为陷阱、实测踩出来的结论 | 写在注释里，一两句 |
| 某个常量为什么取这个数 | 写在那个常量上，两三句 |
| 设计层面的理由（为什么是这套玩法形状） | 设计仓有家，注释里只写一行指向「设计仓 design/X.md 的某一节」 |

四条禁止：

- **不写编号。** `GP-6`、`ADR-0009`、`UI-27` 这类只有查过台账的人读得懂，读到那儿就断了。
  要说「这个值还没定、得实机试」就直接这么说。
- **不写 markdown 粗体。** 一段话里一半的词加粗等于没加粗，而且在 Godot 自带脚本编辑器里
  就是字面的 `**`。
- **不写方括号引用**（`[场景绘制约定 · 前后遮挡按脚底位置排序]`）。它在代码里点不开也搜不到。
  指向设计仓就写清文件名和节名。
- **不用自造词。** 读口、落成、承重的、代价写明、判据，这套词在设计仓里统一，混进代码注释里
  只增加解码成本。换成大白话。

篇幅上限：`<remarks>` 最多三段，每段不超过三行。超了就说明里面混进了设计论证或复述。

行内注释（`//`）一到两行，只写代码本身无法表达的约束——不写「下一行做什么」，不写「我改了什么」：

```csharp
// 对：说明约束
// 只在格子真的变了才重画。这一格多数帧都不动，每帧重画是白算的。

// 错：复述代码
// 创建一个新的 HBoxContainer 并设置间距
var row = new HBoxContainer();
row.AddThemeConstantOverride("separation", UIMetrics.ItemGap);
```

`throw` 里的那句话按同一套标准写：说清缺的是哪一样、要填什么样的值，不带编号和粗体。
它是运行时打给作者看的，所以更要能一眼读懂。

注释语言：与代码仓一致，使用中文。

参考范本：`src/World/Farm/FarmCellCursor.cs`。

## 架构层边界

**规则层（`rules/`）不引用 GodotSharp。** 这条由编译器强制——`rules/` 的 `.csproj`
不引用 `GodotSharp` 程序集。违反时编译失败，而不是运行时崩溃。

下面三条原先各有一个静态扫描守卫（`check_input_map.py`／`check_hud.py`），
守卫随设计仓 `ADR-0009` 删除，**规则本身不变，但现在只有约定、没有门禁**。
写的时候自己守，评审时按 `review-it` 固定问一遍。

**引擎层（`src/`）查询输入必须通过 `InputRouter`**，不许直接调 `Input.IsActionPressed`。
理由是绕过门面会让「面板打开时屏蔽玩法动作」这类门控在某一处失效，且**不报错**。

**界面里除 0 与 1 之外无数字字面量。** 排版量从 `UIMetrics` 和 `HudLayout` 取，
颜色从 `HudPalette` 取，字体参数从 `PixelFont` 取。

**不覆盖 `TextureFilter`。** 项目级最近邻纹理过滤在 `project.godot` 里统一设置，
代码和场景里不许覆盖它（`ENG-13`）——覆盖成线性过滤会让像素糊掉，这一条作者实机看得出来。

## 事件订阅

订阅事件必须在 `_ExitTree` 里解绑，与订阅成对出现：

```csharp
public override void _Ready()
{
    _router.SkillGroupChanged += OnSkillGroupChanged;
    Resized += AnchorBlocks;
}

public override void _ExitTree()
{
    _router.SkillGroupChanged -= OnSkillGroupChanged;
    Resized -= AnchorBlocks;
}
```

解绑用**具名方法**，而不是 lambda——lambda 解绑时需要保存同一个委托实例，
用具名方法就不需要。同一个节点里的订阅用 `_ExitTree` 统一解绑。

## 测试

测试只在 `tests/` 里，只测规则层（`rules/`）。`tests/` 不引用 `src/`。

用 `[Fact]` 标注单条测试，用 `[InlineData]` 标注参数化测试。**不用 `[MemberData]`**——
`verify.py` 静态数测试条数的方式不能数出 `[MemberData]` 的用例，会导致门禁报「条数对不上」
（踩坑记录 29）。

每加一条新测试，先跑 `dotnet run --project tests` 确认条数多了那一条。
