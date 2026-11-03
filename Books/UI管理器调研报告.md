# TEngine UI 管理器全面调研报告

> 调研日期：2026-11-04。目标：对照商业引擎与主流开源框架，为 TEngine UIModule 的功能完善（模态/非模态/队列窗口、屏幕适配、其它 UI 基础能力）提供依据。
> 方法：源码直读（GitHub API/zread + 本地克隆）、官方文档、issues、实战项目（LjdmwMiniClient）对照验证。

---

## 0. 调研对象总览

| 对象 | 状态 | 技术栈 | UI 管理器入口 | 定位 |
|---|---|---|---|---|
| **TEngine 本仓库** | 本项目 | UGUI + UniTask + YooAsset | `GameScripts/HotFix/GameLogic/Module/UIModule/` | 轻量列表窗口栈（UIModule 768 行） |
| **LjdmwMiniClient** | 实战项目（微信小游戏） | 同上 | 本地改造版 UIModule（~2845 行） | 已自建 10+ 补丁组件，反向验证原版缺口 |
| **F8Framework** | 887★ / 2026-09 活跃 | 纯 UGUI，零依赖 | `Runtime/UI/UIManager.cs` + 6 层 LayerUI | 已上架 3 款真实小游戏的全家桶 |
| **GameFramework / UGF**（Ellan Jiang） | 6918★ / 2023 停更 | UGUI | `UIComponent` → 纯 C# `UIManager` | TEngine 直系祖宗，11 钩子生命周期 |
| **GameFramework-Next**（GFN） | 599★ / 2024-10 | UGUI + YooAsset + UniTask | 同 GF（UI 层原样） | GF 现代分支对 UI 层零改造 |
| **ET 9/10**（内嵌 YIUI） | 9957★ / 2026-08 活跃 | UGUI | `YIUIMgrComponent` | 数据驱动 + Option 位 + 栈回退最完整 |
| **QFramework UIKit** | 5573★ / 2026-10 活跃 | UGUI | 静态门面 `UIKit` | 四层 Canvas + 面板表 + 栈导航 |
| Unity UI Toolkit | 官方 | UITK | 无管理器 | 无官方方案，社区无事实标准 |
| 商业参照 | — | — | Unreal UMG（DPI Curve）、Cocos（fitWidth/fitHeight + SafeArea 组件） | 适配策略参照系 |

> 名称考据：用户提到的 "UFG" 经查证不存在公开仓库（`github.com/ALEXTANGXZ/ufg` 404），应为 **UGF（EllanJiang/UnityGameFramework）** 的笔误，已并入 GameFramework 系调研。"Shiyu UI Framework"/"LoongUI" 无公开仓库，未能调研。

---

## 1. 核心结论（TL;DR）

1. **模态遮罩是整个开源生态的最大共同缺口**——GF/GFN/QF/F8 全都没有自动遮罩生成与点击拦截管理（YIUI 也只到"全局点击 Block + 背景模糊"）。TEngine 在窗口 Option 上做 `Modal + 遮罩自动管理` 即可直接超越全部调研对象。
2. **LjdmwMiniClient 已在实战中把 TEngine 的缺口补了一半**（多实例键、生命周期广播、等待遮罩、CanvasGroup 显隐、CTS 竞态防护、SafeAreaPanel 约定、栈顶判定），这些已被线上验证，是上游化的首选素材。
3. **微信小游戏下 `Screen.safeArea` 不可靠**，必须把安全区来源抽象为可覆写接口，注入 `WX.GetWindowInfo().safeArea × pixelRatio`（左上原点、需翻转 Y 轴）。这是当前 Ljdmw 适配代码尚未彻底解决的换源问题。
4. **队列窗口与返回键栈回退**在所有调研框架中只有 YIUI 做得完整（StackOption + 回退事件 + await 弹窗结果）；F8 的 LayerDialog 队列语义（同 uiId 排队、一次只显最老）是 216 行轻量实现，值得直接借鉴。
5. 异步竞态防护（加载中关闭/重复打开）各家方案趋同：GF 双集合、F8 双计数器（removeVersion/loadVersion）、Ljdmw CTS + 身份式释放——**重写任何异步 UI 都必须保留此机制**。

---

## 2. TEngine 现状盘点（主仓库）

### 2.1 代码结构

| 文件（`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/`） | 职责 |
|---|---|
| `UIModule.cs`（768 行） | 管理器单例：`_uiStack` 有序 List、Show/Close/Hide/CloseAll、层级深度排序、全屏遮挡级联、安全区静态方法 |
| `UIWindow.cs`（547 行） | 窗口基类：资源加载、Canvas/Raycaster 管理、Visible（切 layer）/Depth/Interactable、HideTimeToClose 躺尸定时 |
| `UIWidget.cs`（323 行） | 组件基类：嵌套 Widget 更新链、OwnerWindow 回溯、子 Canvas sortingOrder 修正 |
| `UIBase.cs`（742 行） | 公共基类：FindChild、AddUIEvent、CreateWidget、DataBinding（BindText/BindSprite 等） |
| `WindowAttribute.cs` | 5 层 `UILayer`（Bottom/UI/Top/Tips/System）+ `[Window]` 特性（layer/location/fullScreen/fromResources/hideTimeToClose） |
| `IUIResourceLoader.cs` | 资源加载抽象（转发 IResourceModule） |
| `SetUISafeFitHelper.cs` | 刘海/挖孔手工偏移适配（Screen.cutouts + safeArea） |
| `DataBinding/`、`ErrorLogger/`、`UIBindComponent/`、`FancyScrollView/` | 数据绑定、报错窗、组件绑定代码生成、循环列表 |

生命周期全集：`InternalLoad → Handle_Completed → OnWindowPrepare(DataContext) → InternalCreate(Inject→ScriptGenerator→RegisterEvent→OnCreate) → InternalRefresh → 每帧 OnUpdate → OnSetVisible/OnSortDepth → InternalDestroy`。深度 = `layer*2000 + 层内序*100`（UIModule.cs:26-27）。

### 2.2 已有 vs 缺失

已有：异步/同步双形态打开（`ShowUIAsyncAwait` 返回 `UniTask<T>`）、全屏窗口互斥隐藏底层、重复打开=置顶+刷新、事件随窗口自动注销、M2V DataBinding、加载失败兜底销毁、加载超时 30s 保护。

**完全没有（12 项）**：

1. UI 对象池 / 窗口实例复用（Close 即 `Object.Destroy`，UIWindow.cs:467）
2. 开窗/关窗过渡动画钩子（无 DOTween 引用）
3. **模态遮罩 / 半透明 blocker**（全屏窗只有"隐藏下层"语义）
4. **返回键 / ESC / Android Back 拦截分发**
5. **多实例窗口**（类型全名做唯一键，重复 Push 抛异常，UIModule.cs:707）
6. **打开队列 / 排队开窗**（公告连弹、奖励弹窗风暴无收敛机制）
7. 窗口生命周期事件广播（打开/关闭完成通知第三方）
8. UI 预热 API（Preload 高频窗口）
9. Canvas 级 SafeArea 自动组件（只有手动 helper）
10. 常驻窗口豁免批量关闭（CloseAll 全杀）
11. 未加载完成的 ShowUI 竞态合并
12. Toast / 轻提示组件（Tips 层只是层级不是组件）

### 2.3 已知坑（最重的 8 个）

| # | 坑 | 证据 |
|---|---|---|
| 1 | 窗口查找/插入全线性遍历 O(n)，字符串比对 | UIModule.cs:196-203, 674-746 |
| 2 | 每帧全栈遍历（含隐藏/加载中窗口） | UIModule.cs:748-766 |
| 3 | 重复 ShowUIAsync 未完成窗口 → `_prepareCallback` 被覆盖丢失中间态 | UIWindow.cs:310-322 |
| 4 | 快速开关竞态：await 端 `WaitUntil(IsLoadDone)` 不感知销毁，跑满 30s | UIModule.cs:358-361 |
| 5 | Visible 用切 GameObject layer 实现（依赖 UIRoot 相机 culling mask），无法做 alpha 过渡，childCanvas 缓存不响应运行时新增 | UIWindow.cs:148-193 |
| 6 | Depth 子 Canvas 相对偏移 hack + 取模拼接 | UIWindow.cs:127, UIWidget.cs:269 |
| 7 | 加载失败只打日志，无重试/占位 | UIWindow.cs:485-491 |
| 8 | 同步打开路径每次反射取 Attribute + CreateInstance，无缓存 | UIModule.cs:544-585 |

---

## 3. LjdmwMiniClient 实战补丁对照（上游化候选素材）

实战项目把 UIModule 扩到 ~2845 行并自建组件，逐条对应原版缺口：

| 痛点 | 实战变通 | 位置（LjdmwMiniClient） |
|---|---|---|
| 无多实例窗口 | `IUIWindowInstanceKey`：窗口键 = 类型全名 : 业务键，按实例压栈/寻址 | UIModule/IUIWindowInstanceKey.cs; UIModule.cs:1101-1146 |
| 无生命周期广播 | `UILifecycleEvent.AfterOpenUI/AfterCloseUI/AllWindowsClosed` | UIModule/UILifecycleEvent.cs |
| 加载慢无反馈 | `WaitUIOpenMask` 引用计数等待遮罩（N 个并发只显一次，finally 配对归还） | UIModule.cs:390-421; System/Tip/WaitUIOpenMask.cs |
| 加载/销毁竞态 | InternalLoad 全程 CTS + 身份式释放 + 吞并 continuation 竞态异常 | UIWindow.cs:450-460, 498-540 |
| 切 layer 显隐无法做 alpha 过渡 | 运行时 `_visibilityRoot` + CanvasGroup 显隐容器 | UIWindow.cs:47-57, 737-739 |
| 刘海逐窗手调 | prefab 有 `WxSafeAreaPanel`/`SafeAreaPanel` 子节点即自动 SetUIFit | UIWindow.cs:800-818 |
| 关闭同类型误杀其它实例 | `CloseUI(UIWindow)`/`HideUI(UIWindow)` 实例重载 + 幂等守卫 | UIModule.cs:568-652 |
| 引导需要"是否真栈顶" | `WindowTopState` 结构化栈顶判定 + OcclusionIgnore 豁免 | UIModule.cs:1236-1341 |
| GraphicRaycaster 被显隐误开 | 缓存预制体 raycaster 初始开关，只恢复原本开着的 | UIWindow.cs:786-797 |
| 常驻悬浮窗被批量关闭 | 第 8 层 `UILayer.Suspension` 豁免 CloseAll | WindowAttribute.cs:8-18 |
| 开窗重复播动画 | PlayOpenUIAnimation 防重播守卫 | UIWindow.cs:880-902 |

项目自建周边：AnimationUI 开关窗动画库（Module/AnimationUI/）、FrostGlassMgr 毛玻璃烘焙（Module/Common/UI/）、TaskQueueMgr 全局任务队列监听 `AllWindowsClosed`、LazyWidget 页签懒加载（15 文件）、UIProfiler 开窗链路埋点。**两仓库关键差异**：Ljdmw 8 层（+Guide/Loading/Suspension）、`UniTask<bool> OnCreate` 异步化、已剥离 DataBinding。

---

## 4. 框架横向功能矩阵

| 功能 | TEngine 原版 | GF/UGF | GFN | ET(YIUI) | QF UIKit | F8 | 商业实践共识 |
|---|---|---|---|---|---|---|---|
| 自动模态遮罩 | ✗ | ✗（仅 OnCover 回调） | ✗ | 部分（Block+模糊相机） | ✗ | **✗（遮罩全靠预制体自放）** | ✓（遮罩计数栈 + 配置透明度/点击行为） |
| 队列窗口 | ✗ | ✗ | ✗ | 部分 | ✗ | ✓（LayerDialog 同 id 排队） | ✓（FIFO+优先级+合并/丢弃） |
| 栈回退/返回键 | ✗ | ✗ | ✗ | ✓（Back 系统+IgnoreBack） | 部分（栈有 NRE 坑） | **✗（OnEscBtn 空壳）** | ✓（Escape 分发栈顶，消费/默认 pop） |
| await 弹窗结果 | — | ✗ | ✗ | ✓（OpenPanelWaitAsync+HashWait） | ✗ | ✗ | — |
| 多实例窗口 | ✗ | ✓（同名多开） | ✓ | ✓（Popup 层共存） | ✓（Multiple+SearchKeys） | ✓（Guid） | ✓（类型+实例 id 双键） |
| 窗口缓存 | 部分（躺尸定时） | ✓（对象池四参数+延迟回收） | ✓ | ✓（Forever/TimeCache 按窗配置） | 部分（Single 复用） | ✓（双态缓存：活跃→Detach 保活） | ✓ |
| 实例对象池 | ✗ | ✓ | ✓ | 部分 | ✗（Close 即 Destroy） | ✓（免 Instantiate/Destroy） | ✓ |
| SafeArea | 部分（手动 helper） | ✗ | ✗ | ✓（Screen.safeArea+横竖屏） | ✗ | ✓（SafeAreaAdapter 按 match 换算） | ✓ |
| 动效钩子 | ✗ | ✗ | ✗ | ✓（EWindowOption 20+ 开关位） | ✗ | ✓（ViewOpen/CloseSequence，动画接管移除） | ✓ |
| 开窗并发防护 | ✗（回调覆盖） | ✓（双集合竞态方案） | ✓ | 部分（Option） | 部分 | ✓（removeVersion/loadVersion） | ✓（Opening 态+节流） |
| 生命周期广播 | ✗ | ✓（C# 事件） | ✓ | ✓ | ✓ | ✓ | ✓ |
| 全屏互斥压屏 | ✓（隐藏底层） | 部分（OnPause） | 部分 | ✓（Panel 层互斥+栈） | ✗ | ✗（无压屏） | ✓（+联动关 3D 相机） |
| Editor 运行时监视 | ✗ | ✓ | ✓ | ✓ | 部分 | ✗ | — |
| 红点对接 | 间接 | ✗ | ✗ | ✓ | ✗ | ✓（红点树+异步节流） | ✓ |

---

## 5. 各框架亮点与缺陷摘录

### 5.1 F8Framework（用户点名，源码克隆至 `.research/F8Framework`）

**值得抄**：
1. **"模态"重定义为队列语义**（LayerDialog.cs:18-71）：同 uiId 请求进 `Dictionary<int, Queue<DialogParam>>`，一次只显最老，OnRemoved 后延迟一帧自动弹下一个，requestId 防错位出队——公告连弹/奖励连弹天然解决，仅 216 行。
2. **双计数器防异步竞态**（DelegateComponent.cs `_removeVersion` + LayerUI.cs `LoadVersion`）：关闭动画中再关、关了又开、关闭打断加载，全部幂等。
3. **BaseView 内建三追踪器**（EventDispatcher/AssetLoadTracker/TimerTracker，BaseView.cs:18-28）：BeforeRemove 自动 Clear，机制性消灭 UI 泄漏三大来源。
4. **双态缓存不接对象池**（LayerUI.cs）：Close = SetActive(false) + Detach 保活，重开免实例化；`Close(id, isDestroy:true)` 才真销毁。
5. **UILoader 一类三栖**：await（GetAwaiter）/ 协程（IEnumerator）/ 回调同一返回值，WebGL 安全。
6. **SafeAreaAdapter 通用换算**（SafeAreaAdapter.cs:28-43）：按 CanvasScaler.matchWidthOrHeight 把 safeArea 像素换算回参考分辨率坐标，任意 match 值都正确。
7. **组件命名约定自动绑定**（ComponentBind.cs）：`Button_Start`/`Image[0]` 编辑期展开为生成代码，零反射零 Find。
8. **红点树异步节流**：每帧最多刷 1 个节点，聚合计数带缓存。

**缺陷**：无自动遮罩；层内无 sortingOrder/动态调层 API（靠 sibling 顺序）；无返回键适配；无全屏压屏；缓存无 LRU 上限；UIConfig 手写 C# 字典；BaseView/Item 生命周期双轨混乱（issue #39/#38 实锤）。

### 5.2 GameFramework / UGF / GFN 系

- **两级深度模型**（组深 + 组内序，UIManager.UIGroup.cs）是标志性设计，层级由管理器统一维护并主动下发 `OnDepthChanged`——共识。
- **对象池四参数**（AutoReleaseInterval/Capacity/ExpireTime/Priority）+ 延迟回收队列（同帧开关防抖）。
- **加载中关闭双集合**（`m_UIFormsBeingLoaded` + `m_UIFormsToReleaseOnLoad`）——异步 UI 竞态鼻祖方案。
- 10 年未变的结论：社区痛点在资源/异步层而非生命周期模型；**模态/队列/SafeArea 全裸奔**。

### 5.3 ET(YIUI)

- **Option 位优于方法重载**：`EPanelOption`（ForeverCache/TimeCache/DisClose/IgnoreBack…）+ `EWindowTweenOption` 20+ 开关位（FirstOpen/LastClose、BanAwaitOpenTween、AllowOptionByTween 动画期锁操作…）——动效控制的关键在"时序契约"而非播放本身。
- **Panel 层（全屏互斥+栈）与 Popup 层（共存）的层级语义**被实践验证好用。
- **OpenPanelWaitAsync**：`await` 一个弹窗直到关闭并拿到结果——"打开弹窗等返回值"的一等公民。
- Back 系统完整：StackOption 选项化 + `YIUIEventPanelCloseBefore` 前置事件 + IgnoreBack 白名单。
- SafeArea 有静态属性封装但作者自嘲"Unity safeArea API 也不太靠谱"，预留扩展点——**正确方向是覆写换源**。

### 5.4 QFramework UIKit

- `PanelSearchKeys` 多键复用检索 + Single/Multiple 显式打开模式。
- UIPanelStack Push/Pop 有场景切换注销 NRE（issue #155）——栈必须随场景重建清理。
- 面板实例 Close 即 Destroy，无实例池。

### 5.5 商业实践（UWA / 腾讯 WeTest / 网易 / Unity 官方）

1. **模态遮罩计数栈**：每个模态窗在自己下方插一层遮罩（或全局遮罩+计数），透明度取各窗口配置，支持四态：全透明不可穿透 / 半透明不可穿透 / 深色不可穿透 / 可穿透（非模态）。
2. **遮罩点击行为是窗口配置项**：点击关闭（默认）/ 不响应（支付、公告）/ 转发为关闭按钮逻辑。
3. **点击吞没**：弹窗打开瞬间的那一次点击不落到弹窗按钮上——延迟 0.1~0.2s 可交互或等下一次 pointer up。
4. **全屏互斥联动**：全屏栈只渲染最顶一个（`Canvas.enabled=false` 只省渲染不省 rebuild，须 SetActive(false)）；背景完全被盖时联动关闭 3D 场景相机。
5. **返回键两种语义**：无限回退 vs 单步关闭整条链——做成窗口配置；Escape 分发给栈顶窗口 `OnBackKey()`（true=已消费，false=框架默认 pop）。
6. **Toast 通行形态**：Tips 层静态函数式 `Toast.Show(msg)` + 对象池 + 同文案合并 + 上限 N 条。
7. **引导镂空**：推荐重写 `OnPopulateMesh` 画镂空 Mesh + `ICanvasRaycastFilter` 控制可点区 + 目标跟随；强度分级（强/中/弱引导）是产品规范不是特效代码。
8. **打开并发防护**：Opening 态 + 关闭动画期锁重入 + 全局连点节流——防重复开窗/重复扣费第一道闸。

---

## 6. 屏幕适配专项结论

### 6.1 通行做法与坑

- 根级方案：Canvas 下全屏 RectTransform 用 `Screen.safeArea` 归一化为 anchor 收缩；背景图留在收缩层外延伸进刘海。**只在根级包一层**，多层包裹双重收缩。
- 坑：①旋转后 safeArea 滞后 1-2 帧且与 Screen.width/height 可能不同帧——Update 轮询比对缓存值，变化才应用；②部分安卓机 portrait 返回错误 safeArea、180° 翻转 Unity 永不重算；③Android 需开 "Render outside safe area"；④分屏/折叠屏中途改分辨率，缓存死值失效；⑤uGUI 2.6+（Unity 6）官方已内置 SafeArea 组件，优先评估。
- CanvasScaler 取舍：竖屏常用 match=1（高度适配），横屏 match=0；进阶做法运行时按宽高比动态切换。中间值 0.5 在极端比例设备两头不准。

### 6.2 微信小游戏特有（关键）

| 问题 | 结论 |
|---|---|
| 安全区 API 换源 | **不用 `Screen.safeArea`**，用 `WX.GetWindowInfo().safeArea`（官方口径）；`wx.getSystemInfoSync()` 自 2.20.1 起停止维护 |
| 坐标系换算 | wx 的 safeArea 是**左上角原点的逻辑像素（CSS px）**，Unity 是**左下角原点的物理渲染像素**——需 × `pixelRatio` + 翻转 Y 轴，最常见翻车点 |
| 胶囊按钮 | 顶部（…与⊙）不在系统 safeArea 内，需按胶囊位置接口额外留白 |
| 键盘 | InputField 需 `WXTouchInputOverride` 或 `mobileKeyboardSupport=true`（默认关闭）；键盘是原生浮层盖 UI，输入框上移须自己做 |
| DPR/清晰度 | WebGL 默认按 CSS 像素渲染；DPR 上限压 1.5~2 是小游戏画质/性能折中；Canvas 渲染分辨率直接决定 fillrate |

### 6.3 现有轮子（license 可商用）

| 插件 | License | 可抄点 |
|---|---|---|
| [gilzoide/unity-safe-area-layout](https://github.com/gilzoide/unity-safe-area-layout) | Unlicense（公有领域） | **最值得抄**：变更驱动+仅变化时重建（无 Update/协程）、`ignoreLayout` 排除背景、**可 override `GetSafeArea()` 注入自定义安全区** |
| [sanukin39/UniSafeArea](https://github.com/sanukin39/UniSafeArea) | MIT | 经典 anchor 方案 + 编辑器机型预览 |
| [Doyasu24/SafeAreaRect](https://github.com/doyasu24/SafeAreaRect) | MIT | 轻量 RectTransform 方案 |

> **TEngine/Ljdmw 现状评估**：Ljdmw 的 `WxSafeAreaPanel/SafeAreaPanel` 节点约定方向正确，但 `SetUISafeFitHelper` 仍读 `Screen.safeArea/Screen.cutouts`——小游戏下数值来源不对，正确落点是 **GetSafeArea() 抽象：平台层注入 WX.GetWindowInfo × pixelRatio + Y 翻转，上层组件无感平台差异**（F8 的 match 换算 + gilzoide 的变更驱动可合并借鉴）。

---

## 7. 共性设计模式总结（TEngine 设计依据）

1. **管理器单例 + 窗口基类钩子是公约数**，生命周期全集取并集：`Init → Open → Update → Pause/Resume → Cover/Reveal → DepthChanged → Close → Recycle`。
2. **层级由管理器统一维护并主动下发 OnDepthChanged**，窗口不自改 SiblingIndex；"全屏层互斥 + 弹窗层共存"是层级语义默认值（YIUI Panel/Popup 划分）。
3. **关闭 ≠ 销毁，回收统一池化**；显隐切换廉价、重建昂贵；缓存粒度到每个窗口（TimeCache/ForeverCache/hideTimeToClose 同构）。
4. **异步化是新旧分水岭**：窗口打开返回 `UniTask<T>`，且应支持 **await 弹窗结果**（YIUI OpenPanelWaitAsync）。
5. **堆栈/回退做成框架能力而非业务习惯**："是否进回退栈"是窗口 Option，不是隐式规则；返回键语义（无限回退/单步关闭）可配置。
6. **窗口配置 Option 位（Flags）优于方法重载**（GF 9 个重载已演化到头）。
7. **动效契约**：开/关动画与回调的等待关系、多窗口动画先后、动画期是否锁操作，参照 YIUI `EWindowOption` 语义清单；关闭动画接管真实移除时机（F8）。
8. **异步竞态防护不可省**：版本号/CTS 双计数器方案（GF 双集合 / F8 双版本 / Ljdmw CTS）。
9. **生命周期广播**（AfterOpen/AfterClose/AllWindowsClosed）供引导、任务队列、BGM 调度等第三方挂接（Ljdmw 已验证）。
10. **遮罩自动管理是生态最大空白**，谁做谁领先。

---

## 8. TEngine 补齐功能清单（按优先级）

### P0 — 商业刚需 + 当前最大缺口

| # | 功能 | 方案要点 | 参照 |
|---|---|---|---|
| 1 | **模态遮罩管理** | `WindowAttribute` 加 Modal 类型 + 遮罩配置（透明度/点击是否关闭/是否渐隐）；Manager 级遮罩池 + 计数栈；遮罩跟随窗口排序；点击吞没 | 商业遮罩计数栈；YIUI Block |
| 2 | **返回键/ESC 栈回退** | Update 检测 `KeyCode.Escape` → 栈顶 `OnBackKey()`（消费/默认 pop）；无限回退 vs 单步关闭语义可配置；IgnoreBack 白名单 | YIUI Back 系统 |
| 3 | **队列窗口** | `Enqueue` FIFO + 优先级 + 同类型合并/低优先级丢弃 + 切场景清空；当前弹窗关闭后自动出队；队列窗口请求带 requestId | F8 LayerDialog；商业弹窗风暴收敛 |
| 4 | **打开并发防护** | Opening 态标志 + 关闭动画期锁重入 + 修复 `_prepareCallback` 覆盖丢失 + await 感知销毁（不再跑满 30s） | F8 双计数器；Ljdmw CTS（已验证） |
| 5 | **小游戏 SafeArea 桥接** | 抽象 `GetSafeArea()` 可覆写接口；微信平台注入 `WX.GetWindowInfo().safeArea × pixelRatio` + Y 翻转；变更驱动仅在变化时重建；胶囊留白接口 | gilzoide GetSafeArea 模式；F8 match 换算 |

### P1 — 高价值基础能力

| # | 功能 | 方案要点 | 参照 |
|---|---|---|---|
| 6 | 窗口缓存分级 + 实例复用 | 缓存 Option 化（Never/TimeCache/Forever）；Close 支持保活 Detach；`hideTimeToClose` 保留并统一语义 | F8 双态缓存；YIUI 缓存位；GF 对象池 |
| 7 | 开关窗动效钩子 | `OnPlayShow/OnPlayHide` + 打开动画期锁操作选项 + 关闭动画接管真实移除 + 防重播守卫 | YIUI EWindowOption；F8 ViewSequence；Ljdmw AnimationUI |
| 8 | 多实例窗口 | 窗口键 = 类型 + 实例 id（`IUIWindowInstanceKey` 直搬）；Close/Hide 支持实例重载 | Ljdmw 已验证；QF SearchKeys |
| 9 | 生命周期广播 | AfterOpenUI/AfterCloseUI/AllWindowsClosed 事件（Ljdmw 直搬）；供引导/队列/BGM 挂接 | Ljdmw 已验证 |
| 10 | UI 预热 | `Preload<T>()` 高频窗口 + 图集分帧预载（Loading 期调用） | 进阶主程；小游戏首开 |
| 11 | Toast/轻提示 | Tips 层静态封装 + 对象池 + 同文案合并 + 上限 | 商业通行形态 |
| 12 | 等待遮罩 | `WaitUIOpenMask` 引用计数等待窗（Ljdmw 直搬） | Ljdmw 已验证 |

### P2 — 体验与工具

| # | 功能 | 方案要点 | 参照 |
|---|---|---|---|
| 13 | 引导镂空遮罩组件 | `OnPopulateMesh` 镂空 + `ICanvasRaycastFilter` + 目标跟随 | 网易引导规范；社区方案 |
| 14 | 常驻窗口豁免 | Suspension 层/OcclusionIgnore（Ljdmw 直搬） | Ljdmw 已验证 |
| 15 | 全屏互斥联动相机 | 全屏窗打开时可选关 3D 相机/切 Culling Mask | UWA 商业方案 |
| 16 | Editor 窗口监视器 | 运行时层级/栈状态/缓存诊断面板 | GF Debugger；Ljdmw UIProfiler |
| 17 | 窗口 Freeze 态 | 过场可见不可交互 + 切后台统一挂起 UI 动画 | 刘国柱四态生命周期 |
| 18 | 性能修复 | 窗口查找 Dictionary 化（消灭 O(n) 字符串遍历）；反射 Attribute 缓存；`GameObject.Find("UIRoot")` 改引用注册 | 本报告 2.3 节 |

---

## 9. 分阶段路线图建议

- **阶段一（P0 1/3/4 + 9/12）**：模态遮罩、队列窗口、并发防护、生命周期广播、等待遮罩。不动渲染与显隐实现，风险最低；Ljdmw 已验证的机制直接上游化。
- **阶段二（P0 2/5 + P1 8）**：返回键栈回退、SafeArea 换源桥接、多实例窗口。跨平台行为变更，需真机回归。
- **阶段三（P1 6/7/10/11）**：缓存分级、动效钩子、预热、Toast。涉及生命周期与显隐实现改动，建议与"切 layer 显隐 → CanvasGroup 显隐"的改造（Ljdmw 已验证）一起做，并顺手修性能项（P2 18）。

兼容策略：`[Window]` 特性字段只增不改；新能力全部走 Option 位；`ShowUIAsync` 返回值向后兼容。

---

## 10. 参考来源

**框架源码**
- F8Framework：https://github.com/TippingGame/F8Framework （本地克隆 `.research/F8Framework`，HEAD 3.2.323）
- GameFramework：https://github.com/EllanJiang/GameFramework · UnityGameFramework：https://github.com/EllanJiang/UnityGameFramework
- GFN：https://github.com/Alex-Rachel/GameFramework-Next · ET：https://github.com/egametang/ET · YIUI：https://github.com/LiShengYang-yiyi/YIUI · https://yiui.xyz
- QFramework：https://github.com/liangxiegame/QFramework

**适配与小游戏**
- Unity SafeArea 组件（uGUI 2.6+）：https://docs.unity3d.com/Packages/com.unity.ugui@2.6/manual/script-SafeArea.html
- 微信官方 Unity 适配：https://developers.weixin.qq.com/minigame/dev/guide/game-engine/unity-webgl-transform/Design/fixScreen.html · 输入适配：.../Design/InputAdaptation.html · wx.getWindowInfo：https://developers.weixin.qq.com/minigame/dev/api/base/system/wx.getWindowInfo.html
- WebGL Canvas/DPR：https://docs.unity3d.com/cn/2020.3/Manual/webgl-canvas-size.html
- 安卓大屏适配：https://developer.android.com/games/engines/unity/unity-large-screen?hl=zh-cn
- SafeArea 插件：https://github.com/gilzoide/unity-safe-area-layout · https://github.com/sanukin39/UniSafeArea
- UMA UWA 刘海适配：https://blog.uwa4d.com/archives/USparkle_UGUI-NGUI.html

**UI 模式与性能**
- 模态窗体管理（刘国柱）：https://www.cnblogs.com/LiuGuozhu/p/6517791.html
- UWA 显隐共存优化：https://blog.uwa4d.com/archives/TechSharing_146.html
- Unity 官方 UI 优化：https://unity.com/cn/how-to/unity-ui-optimization-tips
- WeTest UGUI 优化：https://wetest.qq.com/labs/272
- 红点树：https://blog.uwa4d.com/archives/USparkle_RedPoint.html
- 镂空遮罩：https://zhuanlan.zhihu.com/p/1947332840766306173
- 网易引导规范：https://uxtool.netease.com/help/guides/12
- Unity 官方 screen stack：https://discussions.unity.com/t/quizu-managing-menu-screens-in-ui-toolkit-post-2/310272

**Issues 佐证**
- F8 #38（BaseItem 事件泄漏）· #39（生命周期混乱）· #24（多分辨率诉求）· #42（YooAsset 诉求）
- QF #155（场景切换栈 NRE）
