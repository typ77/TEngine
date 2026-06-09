# TEngine 深度学习报告

> 分析日期：2026-05-26  
> 项目来源：https://github.com/typ77/TEngine  
> 分析工具：OpenClaw / 小丽

---

## 一、项目名片

| 属性 | 内容 |
|------|------|
| **项目名称** | TEngine |
| **定位** | Unity 全平台框架解决方案 |
| **核心标签** | 开箱即用 · 高性能 · 商业级 · 热更新 · 全平台 |
| **编程语言** | C#（.NET 4.x） |
| **总文件数** | ~2735 |
| **许可证** | MIT |
| **开发 IDE** | Visual Studio 2019+ / Rider |

---

## 二、核心依赖与 Package 栈

### 2.1 四大基石

```
┌─────────────────────────────────────────────┐
│              TEngine 四大基石                  │
├──────────────┬──────────────────────────────┤
│  HybridCLR   │  全平台原生 C# 热更新方案        │
│  YooAsset    │  商业级资源管理系统（百万DAU验证） │
│  UniTask     │  零GC异步框架（替代 Coroutine）  │
│  Luban       │  最佳游戏配置表解决方案          │
└──────────────┴──────────────────────────────┘
```

### 2.2 完整依赖清单

| 包名 | 版本/来源 | 用途 |
|------|----------|------|
| `com.code-philosophy.hybridclr` | Git (Gitee) | C# 热更新运行时 |
| `com.cysharp.unitask` | 本地嵌入 | 异步编程框架 |
| `com.tuyoogame.yooasset` | 本地嵌入 | 资源管理系统 |
| `com.unity.scriptablebuildpipeline` | 1.21.25 | 可脚本化构建管线（YooAsset 依赖） |
| `com.unity.ai.navigation` | 1.1.6 | 导航寻路 |
| `com.unity.nuget.newtonsoft-json` | 3.2.1 | JSON 序列化 |
| `com.unity.textmeshpro` | 3.0.9 | 文字渲染 |
| `com.unity.ugui` | 2.0.0 | uGUI 系统 |
| `com.unity.ide.rider` | 3.0.40 | Rider IDE 支持 |
| `com.unity.ide.vscode` | 1.2.5 | VS Code 支持 |
| `com.unity.ide.visualstudio` | 2.0.23 | Visual Studio 支持 |
| `com.unity.test-framework` | 1.1.33 | 测试框架 |

> **额外 DLL**：`System.Buffers`、`System.Runtime.CompilerServices.Unsafe`（内存池依赖）

---

## 三、框架自研模块体系

### 3.1 模块总览

```
TEngine/Runtime/Module/
├── AudioModule          # 音频管理（分类/分组/配置）
├── DebugerModule        # 运行时调试器（FPS/内存/输入/环境/图形）
├── FsmModule            # 通用有限状态机
├── LocalizationModule   # 多语言本地化（类 I2 Localization）
├── ObjectPoolModule     # 游戏对象池
├── ProcedureModule      # 商业化启动流程
├── ResourceModule       # 资源管理（基于 YooAsset）
├── SceneModule          # 场景加载管理
├── TimerModule          # 计时器（基于最小堆+对象池）
└── UpdataDriver         # 驱动Update生命周期
```

### 3.2 TEngine/Runtime/Core 核心基础设施

```
TEngine/Runtime/Core/
├── Module.cs / ModuleSystem.cs    # 模块基类与管理系统
├── GameEvent/                     # 零GC事件系统
│   ├── GameEvent.cs               #   全局事件静态API
│   ├── GameEventMgr.cs            #   事件管理器
│   ├── EventDispatcher.cs         #   事件分发器
│   ├── EventMgr.cs                #   事件管理容器
│   ├── EventInterfaceAttribute.cs #   事件接口标记
│   └── RuntimeId.cs               #   运行时ID
├── MemoryPool/                    # 内存池（减少GC）
├── GameTime/                      # 自定义时间系统
├── Log/                           # 日志系统
├── Utility/                       # 工具类（文本/文件/JSON/转换/路径等）
└── DataStruct/                    # 数据结构（链表/多字典等）
```

### 3.3 热更程序集架构

```
GameScripts/
├── Main/               # 主程序（不热更）启动器 + 流程
└── HotFix/             # 热更程序集（HybridCLR 热更新）
    ├── GameBase/       # 基础框架
    ├── GameProto/      # 配置 + 协议（Luban Lib）
    └── GameLogic/      # 业务逻辑
        ├── UIModule/   # UI 框架（UIWindow/UIWidget/UIBindComponent）
        ├── SingletonSystem/  # 单例系统
        ├── UI/         # 具体UI页面（LoginUI/BattleMainUI）
        └── ...
```

### 3.4 UI 框架分层

```
UIBase
├── UIWindow    # 全屏/弹出窗口（有生命周期管理）
│   ├── LoginUI
│   ├── BattleMainUI
│   └── LogUI
└── UIWidget    # 可复用UI组件（内嵌于UIWindow）
```

### 3.5 商业化启动流程（14个节点）

```
ProcedureLaunch
  → ProcedureSplash
    → ProcedureInitPackage
      → ProcedurePreload
        → ProcedureInitResources
          → ProcedureUpdateVersion
            → ProcedureUpdateManifest
              → ProcedureCreateDownloader
                → ProcedureDownloadFile
                  → ProcedureDownloadOver
                    → ProcedureClearCache
                      → ProcedureLoadAssembly
                        → ProcedureStartGame
```

---

## 四、编辑器工具链

| 工具 | 位置 | 功能 |
|------|------|------|
| **UI 脚本自动生成器** | `Editor/UIScriptGenerator/` | 从 UI Prefab 生成绑定代码 |
| **图集制作工具** | `Editor/AtlasMakerEditor/` | 自动精灵图集生成 |
| **HybridCLR 构建工具** | `Editor/HybridCLR/` | 一键构建热更新 DLL |
| **Luban 集成工具** | `Editor/LubanTools/` | 配置表生成 |
| **编辑器扩展工具栏** | `Editor/ToolbarExtender/` | 自定义 Unity 顶部工具栏 |
| **引用查找器** | `Editor/ReferenceFinder/` | 资源引用关系查找 |
| **DefineSymbols 管理** | `Editor/DefineSymbols/` | 脚本宏定义管理 |
| **YooAsset 编辑器扩展** | `Packages/YooAsset/EditorExtension/` | 构建/对比/导入/Shader收集 |
| **构建命令行 (BuildCLI)** | `BuildCLI/` | Android/Windows 批量化构建 |
| **文件服务器** | `Tools/FileServer/` | 基于 Node.js 的本地资源服务器 |
| **事件源码生成器** | `Tools/GameEventSourceGenerator/` | 源码级自动生成事件代码 |

---

## 五、平台支持矩阵

| 平台 | 支持状态 |
|------|---------|
| ✅ Windows (Standalone) | 已验证，有项目上架 |
| ✅ macOS | 已验证 |
| ✅ Android | 已验证 |
| ✅ iOS | 已验证，有项目上架 App Store |
| ✅ WebGL | 已验证 |
| ✅ 微信小游戏 | 已验证（YooAsset MiniGame 支持） |
| ✅ Steam | 已有项目上架 |

---

## 六、AI 开发工作流深度解析

TEngine 最大的特色之一是其**深度嵌入的 AI 辅助开发体系**，针对 Claude Code 设计了一套完整的规范驱动开发工作流。

### 6.1 AI 工作流架构图

```
┌──────────────────────────────────────────────────────────────┐
│                    用户发起任务                                │
└──────────────────┬───────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────┐
│            Step 0: 判断任务等级                               │
├──────────┬──────────┬──────────┬────────────────────────────┤
│  L1 简单  │  L2 调用 │  L3 功能 │       L4 架构              │
│ typo/注释 │  单一API  │ 新功能/  │  系统设计/重构              │
│ /日志     │  局部修改  │ 跨文件   │                           │
├──────────┴──────────┴──────────┴────────────────────────────┤
│   ↓直接编码          ↓触发 tengine-dev skill 获取规范          │
└──────────────────────────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────┐
│            Step 1: 查询规范（tengine-dev skill）              │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  14 份 AI 精炼参考文档 (references/)                 │    │
│  │  ┌──────────────┬──────────────┬──────────────┐    │    │
│  │  │ architecture │   modules    │ ui-lifecycle │    │    │
│  │  │ event-system │ resource-api │ hotfix-work..│    │    │
│  │  │ luban-config │ naming-rules │ mcp-tools    │    │    │
│  │  │ ui-patterns  │ event-anti.. │ resource-p.. │    │    │
│  │  │ mcp-visual   │ trouble..    │              │    │    │
│  │  └──────────────┴──────────────┴──────────────┘    │    │
│  └─────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────┐
│            Step 2: 会话内缓存机制                             │
│  同一会话内相同主题只查一次，后续直接复用                      │
│  L4 架构任务可并行查询多个主题                               │
└──────────────────────────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────┐
│            Step 3: 规范冲突检测                               │
│  AI 自动比对 references 文档与实际代码 API                    │
│  发现不一致 → 标注冲突 → 记录到 .claude/memory/              │
│  以代码实现为最终依据                                        │
└──────────────────────────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────┐
│            Step 4: 输出符合规范的代码                          │
│  核心红线（AI 编码必须遵守）：                                │
│  • 异步优先：IO 操作用 UniTask，禁止同步/Coroutine           │
│  • 模块访问：通过 GameModule.XXX                             │
│  • 资源必须释放：LoadAssetAsync → UnloadAsset                │
│  • 热更边界：Main 不热更，HotFix 全部热更                    │
│  • 事件解耦：模块间 GameEvent，UI 内 AddUIEvent              │
└──────────────────────────────────────────────────────────────┘
```

### 6.2 AI 工具总览（`.claude/` 目录下共 25+ 技能）

#### 核心 AI 技能

| 技能/工具 | 文件位置 | 说明 |
|-----------|---------|------|
| **tengine-dev** | `.claude/skills/tengine-dev/` | TEngine 开发核心技能，含 14 份 references 精炼文档，AI 按需查询框架规范 |
| **Unity-MCP** | `.claude/skills/mcp-builder/` | 通过 MCP 协议直接操控 Unity Editor（场景/GameObject/UI/脚本/材质/Shader/动画/VFX） |
| **openspec** (4件套) | `.claude/skills/openspec-*` | 规范驱动的变更管理：提案 → 查询 → 应用 → 归档 |

#### 开发流程技能

| 技能 | 说明 |
|------|------|
| subagent-driven-development | 多子代理协作开发 |
| dispatching-parallel-agents | 并行代理分发（L4 架构任务） |
| test-driven-development | 测试驱动开发 |
| systematic-debugging | 系统化调试 |
| requesting/receiving-code-review | 代码审查双向流程 |
| chinese-code-review | 中文代码审查 |
| writing-plans / executing-plans | 方案编写与执行 |
| verification-before-completion | 完成前验证 |
| finishing-a-development-branch | 分支收尾流程 |

#### 专项技能

| 技能 | 说明 |
|------|------|
| luban-dev | Luban 配置表开发 |
| html-to-ugui | HTML 转 UGUI 布局 |
| wiki-synchelper | Wiki 文档同步助手 |
| workflow-runner | 工作流运行调度 |
| brainstorming | 头脑风暴 |
| using-git-worktrees | Git Worktree 使用 |
| using-superpowers | 超级能力集成 |

#### AI Agent

| Agent | 说明 |
|-------|------|
| **wiki-query-agent** | Wiki 查询代理，独立运行，按需查询项目知识库 |

### 6.3 AI 编码红线（核心原则）

```
┌─────────────────────────────────────────────────────────┐
│                AI 编码红线（严格执行）                    │
├─────────────────────────────────────────────────────────┤
│  ① 异步优先                                           │
│     ✅ UniTask → IO操作                                │
│     ❌ 同步加载、Coroutine                             │
├─────────────────────────────────────────────────────────┤
│  ② 模块访问                                           │
│     ✅ GameModule.Resource / GameModule.UI / ...       │
│     ❌ ModuleSystem.GetModule<T>()                     │
├─────────────────────────────────────────────────────────┤
│  ③ 资源生命周期                                        │
│     ✅ LoadAssetAsync → UnloadAsset                    │
│     ✅ LoadGameObjectAsync → 自动释放                  │
│     ❌ 只加载不释放 → 内存泄漏                         │
├─────────────────────────────────────────────────────────┤
│  ④ 热更边界                                           │
│     ✅ 业务代码放 HotFix/GameLogic/                    │
│     ❌ 不要往 Main/ 写热更代码                         │
├─────────────────────────────────────────────────────────┤
│  ⑤ 事件解耦                                           │
│     ✅ 模块间通信 → GameEvent                          │
│     ✅ UI 内部事件 → AddUIEvent                        │
│     ❌ 模块间直接引用 → 耦合                           │
└─────────────────────────────────────────────────────────┘
```

---

## 七、体系架构总图

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        TEngine 体系架构总览                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                        外部集成层 (Dependencies)                      │  │
│  │  HybridCLR · YooAsset · UniTask · Luban · Newtonsoft · TMP · SBP    │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                    │                                         │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                      框架核心层 (TEngine/Runtime/Core)                │  │
│  │  ModuleSystem · GameEvent · MemoryPool · GameTime · Log · Utility   │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                    │                                         │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                      模块系统层 (Runtime/Module)                      │  │
│  │  Resource · Fsm · Procedure · Audio · Timer · Scene · ObjectPool    │  │
│  │  Debugger · Localization · UpdataDriver                             │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                    │                                         │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                      游戏逻辑层 (GameScripts)                        │  │
│  │  ┌─────────────┐  ┌─────────────────────────────────────────────┐  │  │
│  │  │  Main (不热更)│  │         HotFix (热更域)                     │  │  │
│  │  │  · 启动器     │  │  GameBase · GameProto · GameLogic           │  │  │
│  │  │  · 流程控制   │  │  UIModule · UIWindow · UIWidget            │  │  │
│  │  │  · 入口场景   │  │  SingletonSystem · UI（BattleMain/Login）   │  │  │
│  │  └─────────────┘  └─────────────────────────────────────────────┘  │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                    │                                         │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                      编辑器工具层 (Editor Tools)                     │  │
│  │  UI自动生成 · 图集制作 · HybridCLR构建 · Luban集成 · 工具栏扩展     │  │
│  │  引用查找 · 构建管线 · 文件服务器 · YooAsset扩展 · 编译宏管理       │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                    │                                         │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                      AI 开发工作流层 (.claude)                       │  │
│  │  tengine-dev · 25+ Skills · MCP Server · openspec · 14 References  │  │
│  │  任务分级 · 缓存复用 · 冲突检测 · 并行查询 · 规范驱动世代          │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                    │                                         │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                         平台支持层                                   │  │
│  │  Windows · macOS · Android · iOS · WebGL · 微信小游戏 · Steam       │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 八、学习要点总结

### 8.1 值得学习的设计

1. **模块化架构**：所有模块通过 `ModuleSystem` 统一管理，接口与实现分离（`IResourceModule` / `ResourceModule`），高内聚低耦合
2. **异步化编程**：全面拥抱 UniTask，整个框架从资源加载到 UI 弹出全部异步化，性能优秀
3. **资源生命周期管理**：`AssetReference` + LRU/ARC 缓存策略 + 自动释放，解决了 Unity 项目最大的痛点——内存泄漏
4. **商业化流程设计**：14 步启动流程覆盖了从版本更新 → 资源下载 → 热更加载的全链路
5. **UI 框架脱离 Mono**：纯 C# 实现 UIWindow/UIWidget，完全解除了 Unity Mono 生命周期的约束
6. **事件系统的零 GC 设计**：通过接口绑定、泛型分发等技术实现零分配事件系统
7. **AI-first 开发文化**：将 AI 辅助开发作为一等公民设计，有一套从任务分级到规范查询到代码输出的完整工作流

### 8.2 适合的场景

- 中小团队需要一个**开箱即用的 Unity 商业框架**
- 需要**全平台 + 热更新**的项目
- 希望引入 **AI 辅助开发**的 Unity 团队
- **独立开发者**快速搭建项目原型

### 8.3 技术亮点

- HybridCLR（零成本热更，不是 ILRuntime 那种解释执行）
- YooAsset（百万 DAU 验证的资源系统，比 Unity 自带 Addressable 更成熟）
- UniTask（比 Coroutine 性能更好，比 Async/Await 更轻量）
- 完善的 AI 开发工作流设计（这是目前同类框架中极少见的）

---

> 此报告由小丽生成。项目路径：`TEngine/`