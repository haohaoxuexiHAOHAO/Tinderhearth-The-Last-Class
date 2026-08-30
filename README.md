# 火种：最后一届 · 代码仓

2D 像素风废土题材的**经营建造 + 关卡制动作 RPG**。Godot 4.7.2（.NET 版）+ C# `net10.0`。

设计与需求**不在本仓**，在并列的设计仓 `Tinderhearth-The-Last-Class-Docs/`。两个仓库是工作区下的
兄弟目录，本文提到设计仓的文件时都写它的仓内相对路径（不做跨仓链接 —— 那种链接在 GitHub 上解析不了）。

## 恢复上下文的顺序

1. 设计仓 `README.md` → `spec/` 下的 `prd-*.md`（当前需求）→ `spec/issues/README.md`（待办与进度）
2. 本文 → [ARCHITECTURE.md](./ARCHITECTURE.md)（分层与边界）→ `CONVENTIONS.md`
3. 以代码和测试为准

`CONVENTIONS.md` **尚未建立**，归待办 `ENG-4`。在它到位前，命名与风格参照设计仓
`reference/学习CSharp-Java程序员向.md`（本项目用 Allman 大括号、接口 `I` 前缀）。

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
| `src/` | 场景、节点、输入、显示与文件 I/O |
| `rules/` | 判定与结算规则，**不引用 Godot** |
| `tests/` | 规则层测试 |
| `data/` | 外置内容：配置、文本、角色定义 |
| `scenes/` | 场景文件 |

分层的理由、mod 加载路径与各系统的模块边界都在 [ARCHITECTURE.md](./ARCHITECTURE.md)。

## 现在有什么、没有什么

**有**：工程骨架、规则层与测试底座、内容与 mod 的加载链路、`ENG-5` 四条零成本预留。

**没有**：任何玩法实现。**数值模型尚未设计**（设计仓待办 `GP-2`）—— 属性公式、成长曲线、判定公式、
价格与消耗量都还不存在，代码里也**刻意没有**这类数字。逻辑分辨率与界面结构同样未定（`UI-1`）。
