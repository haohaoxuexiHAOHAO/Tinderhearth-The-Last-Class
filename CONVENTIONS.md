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

命名空间与目录对应（`Tinderhearth.Rules.Foundation` ↔ `rules/Foundation/`），
是约定而不是语言强制——但**必须一致**，否则「东西在哪」需要靠记忆而不是路径。
目录只在有代码时建，命名空间跟着走。

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

## 表达式体成员

单行的取值、转发与简单计算用 `=>`，不写 `{ return ...; }`。

```csharp
public int Right => X + Width;
public bool IsFull => UsedSlots >= Capacity;
public static TalentDef? Get(string id) => _all.Find(t => t.Id == id);
```

超过一行、有副作用、或有早返回时写完整方法体。

## 记录类型

轻量的数据载体优先用 `record` 或 `record struct`。

- **值语义、不可变**：`readonly record struct`（如 `HudRect`、`ActorView`）
- **引用语义、需继承**：`record class`
- **内部实现细节**（只在一个类里用的小数据包）：`private sealed record`（如 `GaugeRow`、`SkillCell`）

`sealed` 对内部 record 是强制的——它们不是设计为扩展点的类型，明说出来，读者就不会试着去继承。

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

公开 API（`public`、`internal`）写 XML 文档注释：

```csharp
/// <summary>一句话说这个东西是什么，不说它怎么实现。</summary>
/// <remarks>
/// 约束、实测结论、「为什么不用另一种明显的写法」放这里。
/// 引用踩坑记录编号或 issue 编号让原始来源可查。
/// </remarks>
public static HudRect SizeOf(HudBlock block) { ... }
```

行内注释（`//`）只写**代码本身无法表达的约束**——不写"下一行做什么"，不写"我改了什么"：

```csharp
// 对：说明约束
// 条比行矮，靠 ShrinkCenter 在行里居中 —— 不用纵向偏移，就不会有半格。

// 错：复述代码
// 创建一个新的 HBoxContainer 并设置间距
var row = new HBoxContainer();
row.AddThemeConstantOverride("separation", UiMetrics.ItemGap);
```

注释语言：与代码仓一致，使用中文。

## 架构层边界

**规则层（`rules/`）不引用 GodotSharp。** 这条由编译器强制——`rules/` 的 `.csproj`
不引用 `GodotSharp` 程序集。违反时编译失败，而不是运行时崩溃。

**引擎层（`src/`）查询输入必须通过 `InputRouter`**，不许直接调 `Input.IsActionPressed`。
`check_input_map.py` 静态扫 `src/` 下的直接轮询，发现即门禁失败。

**界面里除 0 与 1 之外无数字字面量。** 排版量从 `UiMetrics` 和 `HudLayout` 取，
颜色从 `HudPalette` 取，字体参数从 `PixelFont` 取。`check_hud.py` 静态扫这一点。

**不覆盖 `TextureFilter`。** 项目级最近邻纹理过滤在 `project.godot` 里统一设置，
代码和场景里不许覆盖它（`ENG-13`）。`check_hud.py` 与 `check_texture_filter` 静态扫。

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
（踩坑记录 29）。如果数据量大到需要 `[MemberData]`，在 `selfcheck_verify.py` 里显式登记条数。

每加一条新测试，先跑 `dotnet run --project tests` 确认条数多了那一条。
