# 火种：最后一届 · 代码仓

2D 像素风废土题材的**经营建造 + 关卡制动作 RPG**。Godot 4.7.2（.NET 版）+ C# `net10.0`。

设计与需求**不在本仓**，在并列的设计仓 `Tinderhearth-The-Last-Class-Docs/`。两个仓库是工作区下的
兄弟目录，本文提到设计仓的文件时都写它的仓内相对路径（不做跨仓链接 —— 那种链接在 GitHub 上解析不了）。

## 恢复上下文的顺序

1. 设计仓 `README.md` → `spec/` 下的 `prd-*.md`（当前需求）→ `spec/issues/README.md`（待办与进度）
2. 本文 → [ARCHITECTURE.md](./ARCHITECTURE.md)（分层与边界）→ `CONVENTIONS.md`
3. 以代码和测试为准

命名与风格规范见 [CONVENTIONS.md](./CONVENTIONS.md)（`ENG-4`，2026-09-02 建立）。

## 怎么验收

```
python tools/verify.py
```

**门禁只调这一条。** 它把行尾 → 构建 → 测试 → 导出 → 跑产物五步串起来，逐步日志落到
`logs/verify/<时间戳>/`（不入库），同目录写一份带起止时间戳的 `summary.md`。失败即停 ——
后一步依赖前一步的产物，硬跑下去只会产出误导性的失败。

**标准输出每步只有一行**，详细的东西都在日志里。不做成整轮一行是因为那样看不出是哪一步炸的，
必须先打开日志才能定位，而这条命令的用途正是快速判断这一轮能不能收。

**入口住在代码仓而不是设计仓**：被验收的对象全在这里，底层命令的工作目录也都是本仓根。
放设计仓就得跨目录去找兄弟仓，等于让守卫依赖工作区的目录布局。它启动时会确认同级有
`project.godot` 与 `.sln`，不在就拒绝执行 —— 两仓各有一个 `tools/`，靠命名区分依赖记性，
靠落点自检才能自动检出。

重点不在省几次敲键盘，在于**每步都另找一个量具核对产物**，因为这五步的退出码都骗过人：

| 步骤 | 除退出码之外还核对什么 |
| --- | --- |
| 行尾 | git 管的文本文件行尾必须符合 `.gitattributes`。认不出 `check_eol.py` 输出形状就判失败（`ENG-11`） |
| 构建 | 自己数错误行；认不出 `dotnet build` 的输出形状就判失败，不闭眼签字 |
| 测试 | 运行器报的条数**等于**从测试源码静态数出来的条数，两个来源互相独立（踩坑记录 29） |
| 导出 | 先清空 `export/` 再导，于是「文件存在」＝「本轮生成」；再**解开 `.pck` 逐条看清单**查泄漏（踩坑记录 33） |
| 跑产物 | 真启动导出的 exe，从引擎 `--log-file` 的日志确认 C# 侧跑到了内容载入完成 |

**行尾那一步排在最前面**：它最快（纯 Python，不编译不起引擎），坏行尾不该有机会被带进包。

两个附带用法：`--upto eol|build|test|export` 只跑到某步（前置步骤一定跟着跑，所以跑不出旧
产物；范围不完整时摘要会写明「不能当一次验收」）；`--manifest [某个.pck]` 不跑任何步骤，
只把包内清单打出来。

**本脚本没有自证入口了**（`selfcheck_verify.py` 随 `ADR-0009` 一起删除，「改了守卫必须自证」那条
纪律同时取消）。代价要写明：**一个什么都不检的脚本也会全绿**，所以它坏了没有东西能发现，只能靠
改它的人自己复核。复核的省事办法是临时弄坏一样东西（改反一个文件的行尾、往包里塞一个 `.cs`），
确认它真的报出来再改回去。

### 曾经有过的一批专项守卫，已经全删

`ADR-0009`（设计仓 `decisions/`）把表现层的验证交还给作者在 Godot 里实机看，于是这些入口
连同它们的自证一起删除了：`check_scaling`／`check_camera`／`check_hud`／`check_worldui`／
`check_input_map`／`check_assets`／`gen_placeholders`／`import_role_sheets`／`inspect_art_inbox`／
`harness_shot`、素材登记表 `asset-registry.json`、五个图形探针（`player_dev`／`hit_feedback_dev`／
`depth_dev`／`combat_debug_dev`／`blocking_dev`）与它们的 dev 场景，以及全部 `selfcheck_*`。

**写在这里是为了让下一次「这条规则的执行体在哪」有个答案。** 它们守过的约定（界面不写死坐标、
输入只走 `InputRouter`、两种视角共用一份相机、世界空间 UI 不挂到 Hud 层、不覆盖 `TextureFilter`、
像素不用半透明）**规则全部仍然有效**，见 `CONVENTIONS.md` 与 `ARCHITECTURE.md`；变的是它们现在
**只有约定、没有门禁** —— 违反的表现是画面上看得出来的东西，由作者实机验收。

判断一个新入口该不该存在，用同一把尺子：**它判的东西在 Godot 里看不看得出来。** 看得出来的交给
作者，看不出来的（规则层逻辑、发行包泄漏、字体授权、数值平衡）才写成入口。

### 工具链依赖

现存四个入口（`verify.py`、`check_eol.py`、`loglib.py`、`run_local_check.py`）全是**纯标准库**，
clone 完直接能跑，不装任何东西就能验收。

`tools/requirements.txt` 里钉着 Pillow，但**当前没有使用者** —— 需要它的是「读别人的 PNG」那一类
素材守卫，那条链已删。先留着不删的理由写在那个文件里（下次真要读图时会撞上同一个坑）。

下面四节是构建、测试、导出与行尾四步各自的原始命令，单独调试时用得上。行尾那一步是
`python tools/check_eol.py`（修复行尾：`python tools/check_eol.py --fix`）。

## 像素字体怎么进来的

主字体是 [ADR-0008] 选定的**缝合像素字体 12px 比例模式 简体版**，落在
`assets/fonts/fusion-pixel-12px-proportional-zh_hans.ttf`。原先有登记表逐次核 SHA256，
那条链随 `ADR-0009` 删除；**授权侧仍有两道执行体**：设计仓 `python tools/audit_fonts.py`
核授权原文与上游来源，代码仓 `verify.py` 解包时核「包里有字体数据就必须有许可证」（`ART-3`）。
旁边的 `LICENSE-OFL.txt` 必须随发行 —— OFL 第 2 条要求每份拷贝都带许可证与版权声明。
这两条留着的理由是**法律合规在实机里看不出来**。

**换版本是三步，刻意不做成一条命令** —— 它一年也不会跑第二次，而每次都必须重读 ADR：

1. 设计仓 `python tools/audit_fonts.py --only fusion-12px`：下载、核授权原文与上游七环、
   量字形覆盖与度量。下载物落 `temp/font-audit/`。
2. 把 `.ttf` 与 `LICENSE-OFL` 复制进 `assets/fonts/`。
3. 在 Godot 里重新导入这个 `.ttf`（或跑一次 `<Godot> --headless --path . --import`），
   让 `.ttf.import` 的参数真的生效，然后**实机看一眼中文有没有脏边**。

**第 3 步不能省。** 2026-08-31 实测：改了 `.ttf.import` 的 `[params]` 却没重新导入，
游戏读到的还是上一次烘出来的 `.fontdata` —— 抗锯齿仍是灰度，12px 中文多一圈半透明脏边，
**一句报错都没有**。那十项渲染属性的期望值在 `rules/Ui/PixelFont.cs`；原先有守卫拿引擎自报值
逐项比对，守卫已删，现在靠这一步的实机确认 —— 脏边是肉眼看得见的东西。

## 怎么构建

```
dotnet build
```

三个工程一起编：Godot 工程本体、规则层、规则层测试。

## 怎么跑测试

```
dotnet run --project tests
```

**刻意不用 `dotnet test`。** xunit.v3 生成的可执行文件是 xunit 自己的 in-process 控制台运行器，
不是 Microsoft.Testing.Platform 测试模块，而 .NET 10 的 `dotnet test` 会用 MTP 协议去问它，
结果是「Zero tests ran」。四种开关都试过，诊断与结论记在设计仓 `reference/踩坑记录.md` 第 30 条。
直接跑可执行文件是 xunit v3 的一等路径：退出码两个方向都可信（全过 0、有失败 1，均已实测）。

看结果时**核对测试数量，不要只看它说没说「通过」** —— 理由见踩坑记录第 29 条。

## 怎么导出

```
<Godot 可执行文件> --headless --export-release "Windows Desktop"
```

产物落在 `export/`（不入库）。**导出命令的退出码不可信** —— 必须另行校验产物文件确实生成、
并实际运行产物看它有没有起来。这些都已经串进 `python tools/verify.py`，日常别单独敲这条。

`rules/`、`tests/` 与 `tools/` 各有一个 `.gdignore`，让 Godot 的资源扫描跳过它们，否则源码与
`obj/`／`bin/` 中间产物会被打进发行包（实测过：包从 8.5 KB 变成 182 KB，其中含带本机绝对
路径的 `project.assets.json`）。`logs/` 的那份由 `verify.py` 现场补，因为该目录不入库。

**新增 `res://` 下不含 Godot 类型的目录时，同时放一个 `.gdignore`。** 这条现在有执行体了：
验收入口会解开 `.pck` 逐条比对，泄漏当场判失败 —— 而泄漏本身是不会报错的。

## 怎么跑游戏

用 Godot 编辑器打开本目录，或者：

```
<Godot 可执行文件> --path . 
```

启动场景是 `scenes/Main.tscn`。它现在是 `ENG-2` 的**临时脚手架**：只把「内容加载 → 规则层」
这条链路走通一遍并打出诊断，好让 `ENG-1` 的导出冒烟有个能观察的落点。教学关与开局流程都还没有。

## 目录

| 路径 | 是什么 |
| --- | --- |
| `project.godot` | Godot 工程配置。`aspect="expand"` 与 mobile 渲染方式是有意的，见 ARCHITECTURE |
| `Tinderhearth-The-Last-Class.csproj` | Godot 工程本体，碰引擎的代码都在这个程序集 |
| `assets/fonts/` | 像素字体与它的 OFL 许可证。渲染参数在同名 `.ttf.import` 里，见下节 |
| `src/` | 场景、节点、输入、显示与文件 I/O |
| `rules/` | 判定与结算规则，**不引用 Godot** |
| `tests/` | 规则层测试 |
| `data/` | 外置内容：配置、文本、角色定义 |
| `scenes/` | 场景文件 |
| `tools/` | 本仓的 Python 入口，共四个：`verify.py` 验收总入口（五步）、`check_eol.py` 行尾守卫（`ENG-11`）、`loglib.py` 日志落盘共用件、`run_local_check.py` 本地一键跑。专项守卫与图形探针那一批已随 `ADR-0009` 全删 |

分层的理由、mod 加载路径与各系统的模块边界都在 [ARCHITECTURE.md](./ARCHITECTURE.md)。

## 现在有什么、没有什么

**有**：工程骨架、规则层与测试底座、内容与 mod 的加载链路、`ENG-5` 四条零成本预留、
验收总入口（`ENG-3`）。`UI-1` 已落地的部分：逻辑分辨率与缩放链路（`UI-3`）、界面层级与导航栈
（`UI-6`）、输入映射（`UI-7`）、相机五项行为（`UI-5`）、像素字体与主题加关卡 HUD 的屏幕空间
部分（`UI-8`）。

**没有**：任何玩法实现。**世界空间 UI 还没有**（`UI-9`：读条、精英血条、伤害数字，它们要挂
`UiLayer.WorldSpace`）。相机只有行为没有场景：基地场景、建造界面与演出
系统本身都不在（`UI-5` 只做到演出「能接管能归还」这个接缝）。玩法数值代码里也**刻意没有**：
设计仓 `design/数值模型.md` 已给出公式与参数表，但把它搬进规则层属各玩法实现需求 —— HUD 显示的
量一律由视图模型传入，演示数据在 `src/World/HudDemoModel.cs` 且明确标为演示。
