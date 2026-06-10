# FancyScrollView TEngine 集成 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 将 `setchi/FancyScrollView` 以热更代码方式集成到 TEngine，并提供符合 UIWidget、MVE、YooAsset、事件系统规范的虚拟化列表适配层。

**架构：** FancyScrollView 原始 Runtime 源码复制到 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/`，命名空间统一为 `GameLogic.UI.FancyScrollView`，只在创建边界做最小侵入改造：Cell 池改为 `IFancyCell`、Cell 创建虚方法化、ScrollRect 参数可由 Inspector Proxy 注入。TEngine 适配层以 `FancyCellWidget`、`FancyScrollViewWidget`、`FancyScrollRectWidget`、`FancyGridViewWidget` 组合原始滚动核心，Cell 仍走 UIWidget 生命周期，列表数据刷新通过 `BindData` 推送到 Cell 内部 BindableProperty。

**技术栈：** Unity 2022+ / C# / HybridCLR 热更程序集 / TEngine UIWidget / UniTask / YooAsset AssetHandle / NUnit EditMode Tests / setchi FancyScrollView Runtime

**规格文档：** `docs/superpowers/specs/2026-06-10-fancy-scrollview-design.md`

---

## TEngine 约束摘要

- 本功能属于热更业务代码，Runtime 与适配层放在 `Assets/GameScripts/HotFix/GameLogic/`，不新增主包 Runtime 模块。
- UI 生命周期必须遵守：`ScriptGenerator → BindMemberProperty → RegisterEvent → OnCreate → OnRefresh → OnDestroy`。
- UI 内事件使用 `AddUIEvent` 或 Widget 自己的 C# event；跨模块才由 UIWindow 转发为 `GameEvent`。
- IO/资源加载使用 `UniTask`，禁止 Coroutine 与 `Resources.Load`。
- 标准资源规则禁止裸 `LoadAssetAsync<GameObject> + Instantiate`。本功能为虚拟化列表 Prefab 缓存场景，允许使用 `LoadAssetAsyncHandle<GameObject>` 长持有 Prefab 引用并自行 `Dispose` 句柄；禁止使用不持有句柄的 `LoadAssetAsync<GameObject>`。
- Cell 中图片使用 `SetSprite` / `BindSprite`，不要用 `LoadAssetAsync<Sprite>`。
- ScriptGenerator 前缀来自 `ScriptGenerateRuler.uiElementRegex`，新增长前缀必须放在短前缀前，避免 `m_fscroll_` 被其它规则误匹配。

---

## 文件结构总览

```text
# FancyScrollView 热更 Runtime 源码（新增，来自 setchi/FancyScrollView Runtime，移除 asmdef）
Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/
├── Core/
│   ├── IFancyCell.cs                         T2  新增接口：统一 FancyCell 与 UIWidget Cell
│   ├── FancyCell.cs                          T2  上游源码 + 实现 IFancyCell
│   ├── FancyScrollView.cs                    T3  上游源码 + pool 接口化 + CreateCell 虚方法
│   └── NullContext.cs                        T2  从上游 FancyScrollView.cs 拆出，便于测试
├── Scroller/
│   ├── EasingCore.cs                         T1  上游源码，命名空间迁移
│   ├── MovementDirection.cs                  T1  上游源码，命名空间迁移
│   ├── MovementType.cs                       T1  上游源码，命名空间迁移
│   ├── ScrollDirection.cs                    T1  上游源码，命名空间迁移
│   └── Scroller.cs                           T1  上游源码，命名空间迁移
├── ScrollRect/
│   ├── IFancyScrollRectContext.cs            T1  上游源码，命名空间迁移
│   ├── FancyScrollRectContext.cs             T1  上游源码，命名空间迁移
│   ├── FancyScrollRectCell.cs                T1  上游源码，命名空间迁移
│   └── FancyScrollRect.cs                    T4  上游源码 + CellSize 可配置
├── GridView/
│   ├── IFancyCellGroupContext.cs             T1  上游源码，命名空间迁移
│   ├── IFancyGridViewContext.cs              T1  上游源码，命名空间迁移
│   ├── FancyGridViewContext.cs               T1  上游源码，命名空间迁移
│   ├── FancyGridViewCell.cs                  T1  上游源码，命名空间迁移
│   ├── FancyCellGroup.cs                     T5  上游源码 + Cells 只读访问器
│   └── FancyGridView.cs                      T5  上游源码 + Setup 已是 virtual，补公开桥接
└── Adapters/
    ├── ContentLoadState.cs                   T6  Cell 二级内容加载状态枚举
    ├── ContentLoadMode.cs                    T8  渐进式加载模式枚举
    ├── FancyScrollRectProxy.cs               T7  非泛型 Inspector 配置组件
    ├── FancyCellWidget.cs                    T6  Cell Widget 基类，生命周期、绑定、事件、动画
    ├── ContentLoadScheduler.cs               T8  可见 Cell 的 Cascade/AutoTrigger/Manual 调度
    ├── FancyScrollViewWidget.cs              T9  基础 Carousel/Loop 列表容器 Widget
    ├── FancyScrollRectWidget.cs              T10 ScrollRect 容器 Widget
    ├── CellGroupWrapper.cs                   T11 GridView 行组适配
    └── FancyGridViewWidget.cs                T11 GridView 容器 Widget

# Editor 代码生成扩展（修改）
Assets/Editor/UIScriptGenerator/ScriptGenerateRuler.cs       T12 增加 UIComponentName.FancyScrollRectProxy / Scroller
Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.cs    T12 默认规则插入 m_fscroll / m_scroller
Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.asset T12 追加 m_fscroll / m_scroller 两条规则

# 测试（新增）
Assets/TEngine/Tests/EditMode/FancyScrollView/
├── TEngine.Tests.FancyScrollView.asmdef       T1  测试程序集，引用 GameLogic
├── FancyScrollViewCoreTests.cs                T2-T5 核心池/布局/接口测试
├── FancyCellWidgetTests.cs                    T6  Widget Cell 生命周期与事件测试
├── FancyScrollRectProxyTests.cs               T7  Proxy 参数测试
├── ContentLoadSchedulerTests.cs               T8  渐进式调度测试
├── FancyScrollViewWidgetTests.cs              T9  容器创建/事件/资源句柄测试
├── FancyScrollRectWidgetTests.cs              T10 ScrollRect 桥接测试
├── FancyGridViewWidgetTests.cs                T11 Grid 子 Cell 初始化与事件测试
└── ScriptGeneratorRuleTests.cs                T12 前缀与类型枚举测试

# 文档（修改）
.claude/skills/tengine-dev/references/naming-rules.md        T13 记录 m_fscroll_ / m_scroller_ 前缀
.claude/skills/tengine-dev/references/ui-patterns.md         T13 记录 FancyScrollView Widget 使用模式
.claude/skills/tengine-dev/references/mve-patterns.md        T13 记录 CellWidget MVE 模式
```

---

## 任务 1：导入上游 Runtime 源码与测试程序集骨架

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Scroller/EasingCore.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Scroller/MovementDirection.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Scroller/MovementType.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Scroller/ScrollDirection.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Scroller/Scroller.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/ScrollRect/IFancyScrollRectContext.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/ScrollRect/FancyScrollRectContext.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/ScrollRect/FancyScrollRectCell.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/GridView/IFancyCellGroupContext.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/GridView/IFancyGridViewContext.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/GridView/FancyGridViewContext.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/GridView/FancyGridViewCell.cs`
- 创建：`Assets/TEngine/Tests/EditMode/FancyScrollView/TEngine.Tests.FancyScrollView.asmdef`
- 创建：`Assets/GameScripts/HotFix/GameLogic/Properties/AssemblyInfo.cs`

**上游来源：** `https://github.com/setchi/FancyScrollView/tree/master/Assets/FancyScrollView/Sources/Runtime`

- [ ] **步骤 1：创建测试程序集定义**

创建 `Assets/TEngine/Tests/EditMode/FancyScrollView/TEngine.Tests.FancyScrollView.asmdef`：

```json
{
    "name": "TEngine.Tests.FancyScrollView",
    "rootNamespace": "TEngine.Tests",
    "references": [
        "GameLogic"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false,
    "optionalUnityReferences": ["TestAssemblies"]
}
```

- [ ] **步骤 2：开放 GameLogic internal 给测试程序集**

创建 `Assets/GameScripts/HotFix/GameLogic/Properties/AssemblyInfo.cs`：

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TEngine.Tests.FancyScrollView")]
```

- [ ] **步骤 3：复制上游 Runtime 文件**

从上游 `setchi/FancyScrollView` 复制以下文件内容到目标路径，保留 MIT 版权头：

```text
Assets/FancyScrollView/Sources/Runtime/Scroller/EasingCore.cs
Assets/FancyScrollView/Sources/Runtime/Scroller/MovementDirection.cs
Assets/FancyScrollView/Sources/Runtime/Scroller/MovementType.cs
Assets/FancyScrollView/Sources/Runtime/Scroller/ScrollDirection.cs
Assets/FancyScrollView/Sources/Runtime/Scroller/Scroller.cs
Assets/FancyScrollView/Sources/Runtime/ScrollRect/IFancyScrollRectContext.cs
Assets/FancyScrollView/Sources/Runtime/ScrollRect/FancyScrollRectContext.cs
Assets/FancyScrollView/Sources/Runtime/ScrollRect/FancyScrollRectCell.cs
Assets/FancyScrollView/Sources/Runtime/GridView/IFancyCellGroupContext.cs
Assets/FancyScrollView/Sources/Runtime/GridView/IFancyGridViewContext.cs
Assets/FancyScrollView/Sources/Runtime/GridView/FancyGridViewContext.cs
Assets/FancyScrollView/Sources/Runtime/GridView/FancyGridViewCell.cs
```

复制后做全局命名空间替换：

```text
namespace FancyScrollView          → namespace GameLogic.UI.FancyScrollView
using FancyScrollView;             → using GameLogic.UI.FancyScrollView;
using EasingCore;                  → using GameLogic.UI.FancyScrollView.EasingCore;
namespace EasingCore               → namespace GameLogic.UI.FancyScrollView.EasingCore
```

- [ ] **步骤 4：运行编译验证，确认失败点只来自未导入核心类**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -quit -logFile Logs/FancyScrollView-T1.log
```

预期：编译失败，错误包含 `FancyCell` 或 `FancyScrollView` 未定义；不应出现命名空间 `FancyScrollView` 未迁移导致的 `using EasingCore` 解析错误。

- [ ] **步骤 5：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView Assets/GameScripts/HotFix/GameLogic/Properties/AssemblyInfo.cs Assets/TEngine/Tests/EditMode/FancyScrollView
git commit -m "feat: import FancyScrollView runtime scaffolding"
```

---

## 任务 2：实现 `IFancyCell`、`FancyCell`、`NullContext`

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Core/IFancyCell.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Core/FancyCell.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Core/NullContext.cs`
- 创建：`Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewCoreTests.cs`

- [ ] **步骤 1：编写失败测试**

创建 `Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewCoreTests.cs`：

```csharp
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyScrollViewCoreTests
    {
        private sealed class TestCell : FancyCell<string, NullContext>
        {
            public string LastContent { get; private set; }
            public float LastPosition { get; private set; }

            public override void UpdateContent(string itemData)
            {
                LastContent = itemData;
            }

            public override void UpdatePosition(float position)
            {
                LastPosition = position;
            }
        }

        [Test]
        public void FancyCell_ImplementsIFancyCellContract()
        {
            var go = new GameObject("cell", typeof(RectTransform));
            var cell = go.AddComponent<TestCell>();

            Assert.IsInstanceOf<IFancyCell<string, NullContext>>(cell);
            cell.Index = 7;
            cell.SetContext(new NullContext());
            cell.Initialize();
            cell.SetVisible(false);
            cell.UpdateContent("Sword");
            cell.UpdatePosition(0.25f);

            Assert.AreEqual(7, cell.Index);
            Assert.IsFalse(cell.IsVisible);
            Assert.AreEqual("Sword", cell.LastContent);
            Assert.AreEqual(0.25f, cell.LastPosition);

            Object.DestroyImmediate(go);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollViewCoreTests.FancyCell_ImplementsIFancyCellContract -quit -logFile Logs/FancyScrollView-T2-red.log
```

预期：FAIL 或编译失败，错误包含 `IFancyCell`、`FancyCell` 或 `NullContext` 不存在。

- [ ] **步骤 3：创建 IFancyCell**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Core/IFancyCell.cs`：

```csharp
namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// FancyScrollView Cell 的统一接口。
    /// 原始 FancyCell 与 TEngine FancyCellWidget 都实现此接口，使 Cell 创建管线可被 UIWidget 接管。
    /// </summary>
    public interface IFancyCell<TItemData, TContext> where TContext : class, new()
    {
        int Index { get; set; }
        bool IsVisible { get; }
        void SetVisible(bool visible);
        void UpdateContent(TItemData itemData);
        void UpdatePosition(float position);
        void SetContext(TContext context);
        void Initialize();
    }
}
```

- [ ] **步骤 4：创建 NullContext**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Core/NullContext.cs`：

```csharp
namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// 不需要共享 Context 时使用的空上下文。
    /// </summary>
    public sealed class NullContext
    {
    }
}
```

- [ ] **步骤 5：创建 FancyCell**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Core/FancyCell.cs`，内容基于上游 `Core/FancyCell.cs`，并改为实现 `IFancyCell<TItemData,TContext>`：

```csharp
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// FancyScrollView Cell 抽象基类。
    /// </summary>
    public abstract class FancyCell<TItemData, TContext> : MonoBehaviour, IFancyCell<TItemData, TContext>
        where TContext : class, new()
    {
        public int Index { get; set; } = -1;
        public virtual bool IsVisible => gameObject.activeSelf;
        protected TContext Context { get; private set; }
        public virtual void SetContext(TContext context) => Context = context;
        public virtual void Initialize() { }
        public virtual void SetVisible(bool visible) => gameObject.SetActive(visible);
        public abstract void UpdateContent(TItemData itemData);
        public abstract void UpdatePosition(float position);
    }

    /// <summary>
    /// 无 Context 版本的 Cell 抽象基类。
    /// </summary>
    public abstract class FancyCell<TItemData> : FancyCell<TItemData, NullContext>
    {
        public sealed override void SetContext(NullContext context) => base.SetContext(context);
    }
}
```

- [ ] **步骤 6：运行测试验证通过**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollViewCoreTests.FancyCell_ImplementsIFancyCellContract -quit -logFile Logs/FancyScrollView-T2-green.log
```

预期：PASS。

- [ ] **步骤 7：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Core Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewCoreTests.cs
git commit -m "feat: add FancyScrollView cell contract"
```

---

## 任务 3：实现 `FancyScrollView` 的接口池与虚拟创建边界

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Core/FancyScrollView.cs`
- 修改：`Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewCoreTests.cs`

- [ ] **步骤 1：追加失败测试**

在 `FancyScrollViewCoreTests` 中追加以下测试与辅助类：

```csharp
private sealed class TestScrollView : FancyScrollView<string, NullContext>
{
    public int CreatedCount { get; private set; }
    public TestCell LastCreatedCell { get; private set; }

    public void SetPrefab(GameObject prefab)
    {
        cellPrefab = prefab;
    }

    public void SetContainer(Transform container)
    {
        cellContainer = container;
    }

    public void SetItems(params string[] items)
    {
        UpdateContents(items);
    }

    protected override IFancyCell<string, NullContext> CreateCell(GameObject prefab, Transform container)
    {
        CreatedCount++;
        var go = Object.Instantiate(prefab, container);
        LastCreatedCell = go.GetComponent<TestCell>();
        return LastCreatedCell;
    }
}

[Test]
public void FancyScrollView_UsesVirtualCreateCellAndInterfacePool()
{
    var root = new GameObject("root", typeof(RectTransform));
    var container = new GameObject("container", typeof(RectTransform)).transform;
    container.SetParent(root.transform, false);
    var prefab = new GameObject("cellPrefab", typeof(RectTransform));
    prefab.AddComponent<TestCell>();
    var view = root.AddComponent<TestScrollView>();
    view.SetContainer(container);
    view.SetPrefab(prefab);

    view.SetItems("A", "B", "C");

    Assert.Greater(view.CreatedCount, 0);
    Assert.IsNotNull(view.LastCreatedCell);
    Assert.IsTrue(view.LastCreatedCell.IsVisible);
    Assert.AreEqual("A", view.LastCreatedCell.LastContent);

    Object.DestroyImmediate(prefab);
    Object.DestroyImmediate(root);
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollViewCoreTests.FancyScrollView_UsesVirtualCreateCellAndInterfacePool -quit -logFile Logs/FancyScrollView-T3-red.log
```

预期：编译失败，错误包含 `FancyScrollView<TItemData,TContext>` 不存在或 `CreateCell` 不可 override。

- [ ] **步骤 3：创建 FancyScrollView.cs**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Core/FancyScrollView.cs`，基于上游 `Core/FancyScrollView.cs`，关键差异如下：

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    public abstract class FancyScrollView<TItemData, TContext> : MonoBehaviour where TContext : class, new()
    {
        [SerializeField, Range(1e-2f, 1f)] protected float cellInterval = 0.2f;
        [SerializeField, Range(0f, 1f)] protected float scrollOffset = 0.5f;
        [SerializeField] protected bool loop = false;
        [SerializeField] protected Transform cellContainer = default;
        [SerializeField] protected GameObject cellPrefab = default;

        private readonly IList<IFancyCell<TItemData, TContext>> _pool = new List<IFancyCell<TItemData, TContext>>();

        protected bool initialized;
        protected float currentPosition;
        protected virtual GameObject CellPrefab => cellPrefab;
        protected IList<TItemData> ItemsSource { get; set; } = new List<TItemData>();
        protected TContext Context { get; } = new TContext();

        protected virtual void Initialize() { }

        protected virtual void UpdateContents(IList<TItemData> itemsSource)
        {
            ItemsSource = itemsSource ?? new List<TItemData>();
            Refresh();
        }

        protected virtual void Relayout() => UpdatePosition(currentPosition, false);
        protected virtual void Refresh() => UpdatePosition(currentPosition, true);
        protected virtual void UpdatePosition(float position) => UpdatePosition(position, false);

        protected virtual IFancyCell<TItemData, TContext> CreateCell(GameObject prefab, Transform container)
        {
            var go = Instantiate(prefab, container);
            var cell = go.GetComponent<IFancyCell<TItemData, TContext>>();
            if (cell == null)
            {
                throw new MissingComponentException(string.Format(
                    "IFancyCell<{0}, {1}> component not found in {2}.",
                    typeof(TItemData).FullName,
                    typeof(TContext).FullName,
                    prefab.name));
            }

            return cell;
        }

        private void UpdatePosition(float position, bool forceRefresh)
        {
            if (!initialized)
            {
                Initialize();
                initialized = true;
            }

            currentPosition = position;

            var p = position - scrollOffset / cellInterval;
            var firstIndex = Mathf.CeilToInt(p);
            var firstPosition = (Mathf.Ceil(p) - p) * cellInterval;

            if (firstPosition + _pool.Count * cellInterval < 1f)
            {
                ResizePool(firstPosition);
            }

            UpdateCells(firstPosition, firstIndex, forceRefresh);
        }

        private void ResizePool(float firstPosition)
        {
            Debug.Assert(CellPrefab != null);
            Debug.Assert(cellContainer != null);

            var addCount = Mathf.CeilToInt((1f - firstPosition) / cellInterval) - _pool.Count;
            for (var i = 0; i < addCount; i++)
            {
                var cell = CreateCell(CellPrefab, cellContainer);
                cell.SetContext(Context);
                cell.Initialize();
                cell.SetVisible(false);
                _pool.Add(cell);
            }
        }

        private void UpdateCells(float firstPosition, int firstIndex, bool forceRefresh)
        {
            for (var i = 0; i < _pool.Count; i++)
            {
                var index = firstIndex + i;
                var position = firstPosition + i * cellInterval;
                var cell = _pool[CircularIndex(index, _pool.Count)];

                if (loop)
                {
                    index = CircularIndex(index, ItemsSource.Count);
                }

                if (index < 0 || index >= ItemsSource.Count || position > 1f)
                {
                    cell.SetVisible(false);
                    continue;
                }

                if (forceRefresh || cell.Index != index || !cell.IsVisible)
                {
                    cell.Index = index;
                    cell.SetVisible(true);
                    cell.UpdateContent(ItemsSource[index]);
                }

                cell.UpdatePosition(position);
            }
        }

        private static int CircularIndex(int i, int size)
        {
            return size < 1 ? 0 : i < 0 ? size - 1 + (i + 1) % size : i % size;
        }

#if UNITY_EDITOR
        private bool _cachedLoop;
        private float _cachedCellInterval;
        private float _cachedScrollOffset;

        private void LateUpdate()
        {
            if (_cachedLoop != loop || _cachedCellInterval != cellInterval || _cachedScrollOffset != scrollOffset)
            {
                _cachedLoop = loop;
                _cachedCellInterval = cellInterval;
                _cachedScrollOffset = scrollOffset;
                UpdatePosition(currentPosition);
            }
        }
#endif
    }

    public abstract class FancyScrollView<TItemData> : FancyScrollView<TItemData, NullContext>
    {
    }
}
```

- [ ] **步骤 4：运行核心测试验证通过**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollViewCoreTests -quit -logFile Logs/FancyScrollView-T3-green.log
```

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Core/FancyScrollView.cs Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewCoreTests.cs
git commit -m "feat: virtualize FancyScrollView cell creation"
```

---

## 任务 4：实现 `FancyScrollRect` 可配置 CellSize

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/ScrollRect/FancyScrollRect.cs`
- 修改：`Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewCoreTests.cs`

- [ ] **步骤 1：追加失败测试**

在 `FancyScrollViewCoreTests` 中追加：

```csharp
private sealed class TestScrollRect : FancyScrollRect<string, FancyScrollRectContext>
{
    public float ExposedCellSize => CellSize;
    public void SetCellSize(float value) => cellSize = value;
}

[Test]
public void FancyScrollRect_UsesSerializedCellSizeByDefault()
{
    var go = new GameObject("scrollRect", typeof(RectTransform), typeof(Scroller));
    var rect = go.AddComponent<TestScrollRect>();

    rect.SetCellSize(96f);

    Assert.AreEqual(96f, rect.ExposedCellSize);
    Object.DestroyImmediate(go);
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollViewCoreTests.FancyScrollRect_UsesSerializedCellSizeByDefault -quit -logFile Logs/FancyScrollView-T4-red.log
```

预期：编译失败，错误包含 `FancyScrollRect` 不存在或 `cellSize` 不存在。

- [ ] **步骤 3：创建 FancyScrollRect.cs**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/ScrollRect/FancyScrollRect.cs`，基于上游 `ScrollRect/FancyScrollRect.cs`，保留所有滚动计算逻辑，仅将 `CellSize` 改为可配置字段：

```csharp
// 文件头部 using 必须包含：
using System;
using System.Collections.Generic;
using UnityEngine;
using GameLogic.UI.FancyScrollView.EasingCore;

// 类内替换上游抽象属性：
[SerializeField] protected float cellSize = 100f;
protected virtual float CellSize => cellSize;
```

同时确认这些成员保持原始可见性，供 Widget 桥接调用：

```csharp
protected virtual void JumpTo(int itemIndex, float alignment = 0.5f)
{
    Scroller.Position = ToScrollerPosition(itemIndex, alignment);
}

protected virtual void ScrollTo(int index, float duration, float alignment = 0.5f, Action onComplete = null)
{
    Scroller.ScrollTo(ToScrollerPosition(index, alignment), duration, onComplete);
}

protected virtual void ScrollTo(int index, float duration, Ease easing, float alignment = 0.5f, Action onComplete = null)
{
    Scroller.ScrollTo(ToScrollerPosition(index, alignment), duration, easing, onComplete);
}

protected void RefreshScroller()
{
    Scroller.Draggable = Scrollable;
    Scroller.ScrollSensitivity = ToScrollerPosition(ViewportLength - PaddingHeadLength);
    Scroller.Position = ToScrollerPosition(currentPosition);
}

protected void AdjustCellIntervalAndScrollOffset()
{
    var totalSize = Scroller.ViewportSize + (CellSize + spacing) * (1f + reuseCellMarginCount * 2f);
    cellInterval = (CellSize + spacing) / totalSize;
    scrollOffset = cellInterval * (1f + reuseCellMarginCount);
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollViewCoreTests.FancyScrollRect_UsesSerializedCellSizeByDefault -quit -logFile Logs/FancyScrollView-T4-green.log
```

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/ScrollRect/FancyScrollRect.cs Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewCoreTests.cs
git commit -m "feat: add configurable FancyScrollRect cell size"
```

---

## 任务 5：实现 GridView 核心类与 CellGroup 可访问 Cells

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/GridView/FancyCellGroup.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/GridView/FancyGridView.cs`
- 修改：`Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewCoreTests.cs`

- [ ] **步骤 1：追加失败测试**

在 `FancyScrollViewCoreTests` 中追加：

```csharp
private sealed class TestGridContext : FancyGridViewContext
{
}

private sealed class TestCellGroup : FancyCellGroup<string, TestGridContext>
{
    public FancyCell<string, TestGridContext>[] ExposedCells => Cells;
    public int InstantiateCount { get; private set; }

    protected override FancyCell<string, TestGridContext>[] InstantiateCells()
    {
        InstantiateCount++;
        return new FancyCell<string, TestGridContext>[Context.GetGroupCount()];
    }

    public override void UpdatePosition(float position)
    {
    }
}

[Test]
public void FancyCellGroup_ExposesCellsToDerivedAdapters()
{
    var go = new GameObject("group", typeof(RectTransform));
    var group = go.AddComponent<TestCellGroup>();
    var context = new TestGridContext
    {
        GetGroupCount = () => 3,
        CellTemplate = new GameObject("template", typeof(RectTransform))
    };

    group.SetContext(context);
    group.Initialize();

    Assert.AreEqual(1, group.InstantiateCount);
    Assert.AreEqual(3, group.ExposedCells.Length);

    Object.DestroyImmediate(context.CellTemplate);
    Object.DestroyImmediate(go);
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollViewCoreTests.FancyCellGroup_ExposesCellsToDerivedAdapters -quit -logFile Logs/FancyScrollView-T5-red.log
```

预期：编译失败，错误包含 `FancyCellGroup` 或 `Cells` 不存在。

- [ ] **步骤 3：创建 FancyCellGroup.cs**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/GridView/FancyCellGroup.cs`，基于上游文件，关键差异是 `Cells` 保持 `protected virtual`，适配层可访问：

```csharp
using System.Linq;
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    public abstract class FancyCellGroup<TItemData, TContext> : FancyCell<TItemData[], TContext>
        where TContext : class, IFancyCellGroupContext, new()
    {
        protected virtual FancyCell<TItemData, TContext>[] Cells { get; private set; }

        protected virtual FancyCell<TItemData, TContext>[] InstantiateCells()
        {
            return Enumerable.Range(0, Context.GetGroupCount())
                .Select(_ => Instantiate(Context.CellTemplate, transform))
                .Select(x => x.GetComponent<FancyCell<TItemData, TContext>>())
                .ToArray();
        }

        public override void Initialize()
        {
            Cells = InstantiateCells();
            Debug.Assert(Cells.Length == Context.GetGroupCount());

            for (var i = 0; i < Cells.Length; i++)
            {
                if (Cells[i] == null)
                {
                    continue;
                }

                Cells[i].SetContext(Context);
                Cells[i].Initialize();
            }
        }

        public override void UpdateContent(TItemData[] contents)
        {
            var firstCellIndex = Index * Context.GetGroupCount();

            for (var i = 0; i < Cells.Length; i++)
            {
                Cells[i].Index = i + firstCellIndex;
                Cells[i].SetVisible(i < contents.Length);

                if (Cells[i].IsVisible)
                {
                    Cells[i].UpdateContent(contents[i]);
                }
            }
        }

        public override void UpdatePosition(float position)
        {
            for (var i = 0; i < Cells.Length; i++)
            {
                Cells[i].UpdatePosition(position);
            }
        }
    }
}
```

- [ ] **步骤 4：创建 FancyGridView.cs**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/GridView/FancyGridView.cs`，基于上游文件，保留 `Setup<TGroup>` 的 `protected virtual` 签名，不重复改动已经正确的签名。关键片段：

```csharp
protected virtual void Setup<TGroup>(FancyCell<TItemData, TContext> cellTemplate)
    where TGroup : FancyCell<TItemData[], TContext>
{
    Context.CellTemplate = cellTemplate.gameObject;

    cellGroupTemplate = new GameObject("Group").AddComponent<TGroup>().gameObject;
    cellGroupTemplate.transform.SetParent(cellContainer, false);
    cellGroupTemplate.SetActive(false);
}
```

- [ ] **步骤 5：运行核心测试验证通过**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollViewCoreTests -quit -logFile Logs/FancyScrollView-T5-green.log
```

预期：PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/GridView/FancyCellGroup.cs Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/GridView/FancyGridView.cs Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewCoreTests.cs
git commit -m "feat: add FancyGridView core adapters"
```

---

## 任务 6：实现 `FancyCellWidget` 生命周期、MVE 中间属性、事件与动画

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/ContentLoadState.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyCellWidget.cs`
- 创建：`Assets/TEngine/Tests/EditMode/FancyScrollView/FancyCellWidgetTests.cs`

- [ ] **步骤 1：编写失败测试**

创建 `Assets/TEngine/Tests/EditMode/FancyScrollView/FancyCellWidgetTests.cs`：

```csharp
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyCellWidgetTests
    {
        private sealed class TestContext { }

        private sealed class TestCellWidget : FancyCellWidget<string, TestContext>
        {
            public string LastData { get; private set; }
            public float LastPosition { get; private set; }
            public int VisibleCount { get; private set; }
            public int InvisibleCount { get; private set; }
            public void RaiseClick() => NotifyItemClicked();
            public void RaiseButton() => NotifyButtonClicked();

            protected override void BindData(string data)
            {
                LastData = data;
            }

            public override void UpdatePosition(float normalizedPosition)
            {
                LastPosition = normalizedPosition;
            }

            protected override void OnCellVisible()
            {
                VisibleCount++;
            }

            protected override void OnCellInvisible()
            {
                InvisibleCount++;
            }
        }

        [Test]
        public void FancyCellWidget_ImplementsCellLifecycleAndEvents()
        {
            var parentGo = new GameObject("parent", typeof(RectTransform));
            var parent = new TestParentUI(parentGo);
            var go = new GameObject("cell", typeof(RectTransform));
            var cell = new TestCellWidget();
            Assert.IsTrue(cell.Create(parent, go));

            var clicked = -1;
            var button = -1;
            cell.Index = 5;
            cell.OnItemClicked += i => clicked = i;
            cell.OnButtonClicked += i => button = i;

            cell.SetContext(new TestContext());
            cell.UpdateContent("Potion");
            cell.UpdatePosition(0.75f);
            cell.SetVisible(false);
            cell.SetVisible(true);
            cell.RaiseClick();
            cell.RaiseButton();

            Assert.AreEqual("Potion", cell.LastData);
            Assert.AreEqual(0.75f, cell.LastPosition);
            Assert.AreEqual(1, cell.InvisibleCount);
            Assert.AreEqual(1, cell.VisibleCount);
            Assert.AreEqual(5, clicked);
            Assert.AreEqual(5, button);

            cell.CallDestroy();
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(parentGo);
        }

        private sealed class TestParentUI : UIBase
        {
            public TestParentUI(GameObject root)
            {
                gameObject = root;
                transform = root.transform;
                rectTransform = root.transform as RectTransform;
            }
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyCellWidgetTests.FancyCellWidget_ImplementsCellLifecycleAndEvents -quit -logFile Logs/FancyScrollView-T6-red.log
```

预期：编译失败，错误包含 `FancyCellWidget` 或 `ContentLoadState` 不存在。

- [ ] **步骤 3：创建 ContentLoadState**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/ContentLoadState.cs`：

```csharp
namespace GameLogic.UI.FancyScrollView
{
    public enum ContentLoadState
    {
        Skeleton,
        Loading,
        Loaded,
        Error,
    }
}
```

- [ ] **步骤 4：创建 FancyCellWidget**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyCellWidget.cs`：

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameLogic.DataBinding;
using TEngine;
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    public abstract class FancyCellWidget<TItemData, TContext> : UIWidget, IFancyCell<TItemData, TContext>
        where TContext : class, new()
    {
        private readonly List<IDisposable> _cellProperties = new();
        private CancellationTokenSource _loadCts;
        private ContentLoadState _loadState = ContentLoadState.Skeleton;

        public int Index { get; set; } = -1;
        public virtual bool IsVisible => gameObject != null && gameObject.activeSelf;
        public ContentLoadState LoadState => _loadState;
        protected TContext Context { get; private set; }
        protected virtual string ContentAssetLocation => null;

        public event Action<int> OnItemClicked;
        public event Action<int> OnButtonClicked;

        public virtual void SetContext(TContext context)
        {
            Context = context;
        }

        public virtual void Initialize()
        {
        }

        public virtual void SetVisible(bool visible)
        {
            if (gameObject == null || gameObject.activeSelf == visible)
            {
                return;
            }

            gameObject.SetActive(visible);
            if (visible)
            {
                OnCellVisible();
            }
            else
            {
                OnCellInvisible();
            }
        }

        public void UpdateContent(TItemData itemData)
        {
            BindData(itemData);
        }

        public abstract void UpdatePosition(float normalizedPosition);

        protected BindableProperty<T> CreateProperty<T>(T initial = default)
        {
            var property = new BindableProperty<T>(initial);
            _cellProperties.Add(property);
            return property;
        }

        internal void InitializeWidget(UIBase parentUI, GameObject widgetRoot, bool visible = true)
        {
            Create(parentUI, widgetRoot, visible);
        }

        protected abstract void BindData(TItemData data);
        protected virtual void OnCellVisible() { }
        protected virtual void OnCellInvisible() { }
        protected virtual void OnContentLoading(float progress) { }
        protected virtual void OnContentLoaded() { }
        protected virtual void OnContentError(string error) { }
        protected virtual void OnContentUnloaded() { }

        protected void NotifyItemClicked()
        {
            OnItemClicked?.Invoke(Index);
        }

        protected void NotifyButtonClicked()
        {
            OnButtonClicked?.Invoke(Index);
        }

        public async UniTask LoadContentAsync(IProgress<float> progress = null, CancellationToken ct = default)
        {
            if (_loadState == ContentLoadState.Loaded || string.IsNullOrEmpty(ContentAssetLocation))
            {
                return;
            }

            CancelLoad();
            _loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var linkedToken = _loadCts.Token;
            _loadState = ContentLoadState.Loading;
            OnContentLoading(0f);

            try
            {
                await LoadContentCoreAsync(progress, linkedToken);
                linkedToken.ThrowIfCancellationRequested();
                _loadState = ContentLoadState.Loaded;
                OnContentLoaded();
            }
            catch (OperationCanceledException)
            {
                _loadState = ContentLoadState.Skeleton;
            }
            catch (Exception e)
            {
                _loadState = ContentLoadState.Error;
                OnContentError(e.Message);
                Log.Error(e);
            }
        }

        protected virtual UniTask LoadContentCoreAsync(IProgress<float> progress, CancellationToken ct)
        {
            progress?.Report(1f);
            OnContentLoading(1f);
            return UniTask.CompletedTask;
        }

        public void CancelLoad()
        {
            if (_loadCts == null)
            {
                return;
            }

            _loadCts.Cancel();
            _loadCts.Dispose();
            _loadCts = null;
        }

        public void UnloadContent()
        {
            CancelLoad();
            _loadState = ContentLoadState.Skeleton;
            OnContentUnloaded();
        }

        public async UniTask PlayFadeInAsync(float duration = 0.3f, CancellationToken ct = default)
        {
            var canvasGroup = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            var rect = rectTransform;
            var startY = rect.anchoredPosition.y + 30f;
            var targetY = rect.anchoredPosition.y;
            canvasGroup.alpha = 0f;

            var elapsed = 0f;
            while (elapsed < duration)
            {
                await UniTask.Yield(ct);
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = EasingCore.Easing.Ease(t, EasingCore.Ease.OutCubic);
                canvasGroup.alpha = eased;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, Mathf.LerpUnclamped(startY, targetY, eased));
            }

            canvasGroup.alpha = 1f;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, targetY);
        }

        public async UniTask PlayScaleInAsync(float duration = 0.25f, CancellationToken ct = default)
        {
            var rect = rectTransform;
            const float startScale = 0.8f;

            var elapsed = 0f;
            while (elapsed < duration)
            {
                await UniTask.Yield(ct);
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = EasingCore.Easing.Ease(t, EasingCore.Ease.OutBack);
                var scale = Mathf.LerpUnclamped(startScale, 1f, eased);
                rect.localScale = new Vector3(scale, scale, 1f);
            }

            rect.localScale = Vector3.one;
        }

        protected override void OnDestroy()
        {
            CancelLoad();
            foreach (var property in _cellProperties)
            {
                property.Dispose();
            }
            _cellProperties.Clear();
            OnItemClicked = null;
            OnButtonClicked = null;
            base.OnDestroy();
        }
    }
}
```

- [ ] **步骤 5：运行测试验证通过**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyCellWidgetTests -quit -logFile Logs/FancyScrollView-T6-green.log
```

预期：PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/ContentLoadState.cs Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyCellWidget.cs Assets/TEngine/Tests/EditMode/FancyScrollView/FancyCellWidgetTests.cs
git commit -m "feat: add FancyScrollView cell widget base"
```

---

## 任务 7：实现 `FancyScrollRectProxy`

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyScrollRectProxy.cs`
- 创建：`Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollRectProxyTests.cs`

- [ ] **步骤 1：编写失败测试**

创建 `Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollRectProxyTests.cs`：

```csharp
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyScrollRectProxyTests
    {
        [Test]
        public void Proxy_StoresInspectorConfigurationAndRuntimePrefab()
        {
            var go = new GameObject("m_fscroll_List", typeof(RectTransform));
            var proxy = go.AddComponent<FancyScrollRectProxy>();
            var prefab = new GameObject("CellPrefab", typeof(RectTransform));

            proxy.SetCellSizeForTest(128f);
            proxy.SetSpacingForTest(8f);
            proxy.CellPrefab = prefab;

            Assert.AreEqual(128f, proxy.CellSize);
            Assert.AreEqual(8f, proxy.Spacing);
            Assert.AreSame(prefab, proxy.CellPrefab);

            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(go);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollRectProxyTests.Proxy_StoresInspectorConfigurationAndRuntimePrefab -quit -logFile Logs/FancyScrollView-T7-red.log
```

预期：编译失败，错误包含 `FancyScrollRectProxy` 不存在。

- [ ] **步骤 3：创建 FancyScrollRectProxy**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyScrollRectProxy.cs`：

```csharp
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    [DisallowMultipleComponent]
    public class FancyScrollRectProxy : MonoBehaviour
    {
        [Header("Cell Settings")]
        [SerializeField] private float cellSize = 120f;
        [SerializeField] private float spacing = 10f;
        [SerializeField] private float paddingHead = 0f;
        [SerializeField] private float paddingTail = 0f;
        [SerializeField] private float reuseCellMarginCount = 0f;

        [Header("Scroll Settings")]
        [SerializeField] private ScrollDirection scrollDirection = ScrollDirection.Vertical;

        public float CellSize => cellSize;
        public float Spacing => spacing;
        public float PaddingHead => paddingHead;
        public float PaddingTail => paddingTail;
        public float ReuseCellMarginCount => reuseCellMarginCount;
        public ScrollDirection ScrollDirection => scrollDirection;
        internal GameObject CellPrefab { get; set; }

#if UNITY_EDITOR
        internal void SetCellSizeForTest(float value) => cellSize = value;
        internal void SetSpacingForTest(float value) => spacing = value;
        internal void SetPaddingHeadForTest(float value) => paddingHead = value;
        internal void SetPaddingTailForTest(float value) => paddingTail = value;
        internal void SetReuseCellMarginCountForTest(float value) => reuseCellMarginCount = value;
#endif
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollRectProxyTests -quit -logFile Logs/FancyScrollView-T7-green.log
```

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyScrollRectProxy.cs Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollRectProxyTests.cs
git commit -m "feat: add FancyScrollRect inspector proxy"
```

---

## 任务 8：实现渐进式加载调度器

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/ContentLoadMode.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/ContentLoadScheduler.cs`
- 创建：`Assets/TEngine/Tests/EditMode/FancyScrollView/ContentLoadSchedulerTests.cs`

- [ ] **步骤 1：编写失败测试**

创建 `Assets/TEngine/Tests/EditMode/FancyScrollView/ContentLoadSchedulerTests.cs`：

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic;
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class ContentLoadSchedulerTests
    {
        private sealed class TestContext { }

        private sealed class LoadCell : FancyCellWidget<int, TestContext>
        {
            public int LoadCount { get; private set; }
            protected override string ContentAssetLocation => "virtual-content";
            protected override void BindData(int data) { }
            public override void UpdatePosition(float normalizedPosition) { }
            protected override UniTask LoadContentCoreAsync(System.IProgress<float> progress, CancellationToken ct)
            {
                LoadCount++;
                return UniTask.CompletedTask;
            }
        }

        [Test]
        public void AutoTrigger_LoadsCellWhenVisible()
        {
            var parentGo = new GameObject("parent", typeof(RectTransform));
            var parent = new TestParentUI(parentGo);
            var go = new GameObject("cell", typeof(RectTransform));
            var cell = new LoadCell();
            cell.Create(parent, go);
            using var scheduler = new ContentLoadScheduler<int, TestContext>();
            scheduler.Mode = ContentLoadMode.AutoTrigger;

            scheduler.OnCellBecameVisible(cell);

            Assert.AreEqual(1, cell.LoadCount);
            cell.CallDestroy();
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(parentGo);
        }

        private sealed class TestParentUI : UIBase
        {
            public TestParentUI(GameObject root)
            {
                gameObject = root;
                transform = root.transform;
                rectTransform = root.transform as RectTransform;
            }
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.ContentLoadSchedulerTests.AutoTrigger_LoadsCellWhenVisible -quit -logFile Logs/FancyScrollView-T8-red.log
```

预期：编译失败，错误包含 `ContentLoadScheduler` 或 `ContentLoadMode` 不存在。

- [ ] **步骤 3：创建 ContentLoadMode**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/ContentLoadMode.cs`：

```csharp
namespace GameLogic.UI.FancyScrollView
{
    public enum ContentLoadMode
    {
        Cascade,
        AutoTrigger,
        Manual,
    }
}
```

- [ ] **步骤 4：创建 ContentLoadScheduler**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/ContentLoadScheduler.cs`：

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace GameLogic.UI.FancyScrollView
{
    public sealed class ContentLoadScheduler<TItemData, TContext> : IDisposable where TContext : class, new()
    {
        private readonly List<FancyCellWidget<TItemData, TContext>> _visibleCells = new();
        private CancellationTokenSource _cts = new();
        private bool _cascadeTriggered;

        public ContentLoadMode Mode { get; set; } = ContentLoadMode.AutoTrigger;
        public float StaggerDelay { get; set; } = 0.08f;
        public bool IsCascadeTriggered => _cascadeTriggered;

        public void OnCellBecameVisible(FancyCellWidget<TItemData, TContext> cell)
        {
            if (cell == null)
            {
                return;
            }

            if (!_visibleCells.Contains(cell))
            {
                _visibleCells.Add(cell);
            }

            if (Mode == ContentLoadMode.AutoTrigger && cell.LoadState == ContentLoadState.Skeleton)
            {
                cell.LoadContentAsync(ct: _cts.Token).Forget();
            }
        }

        public void OnCellBecameInvisible(FancyCellWidget<TItemData, TContext> cell)
        {
            _visibleCells.Remove(cell);
        }

        public async UniTask TriggerCascadeAsync(CancellationToken ct = default)
        {
            if (_cascadeTriggered)
            {
                return;
            }

            _cascadeTriggered = true;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
            for (var i = 0; i < _visibleCells.Count; i++)
            {
                linked.Token.ThrowIfCancellationRequested();
                var cell = _visibleCells[i];
                if (cell != null && cell.LoadState == ContentLoadState.Skeleton)
                {
                    cell.LoadContentAsync(ct: linked.Token).Forget();
                }

                if (i < _visibleCells.Count - 1)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(StaggerDelay), cancellationToken: linked.Token);
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
            _visibleCells.Clear();
        }
    }
}
```

- [ ] **步骤 5：运行测试验证通过**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.ContentLoadSchedulerTests -quit -logFile Logs/FancyScrollView-T8-green.log
```

预期：PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/ContentLoadMode.cs Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/ContentLoadScheduler.cs Assets/TEngine/Tests/EditMode/FancyScrollView/ContentLoadSchedulerTests.cs
git commit -m "feat: add FancyScrollView content load scheduler"
```

---

## 任务 9：实现基础 `FancyScrollViewWidget`

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyScrollViewWidget.cs`
- 创建：`Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewWidgetTests.cs`

- [ ] **步骤 1：编写失败测试**

创建 `Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewWidgetTests.cs`：

```csharp
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyScrollViewWidgetTests
    {
        private sealed class TestContext { }
        private sealed class TestCell : FancyCellWidget<string, TestContext>
        {
            public void RaiseClick() => NotifyItemClicked();
            protected override void BindData(string data) { }
            public override void UpdatePosition(float normalizedPosition) { }
        }

        private sealed class TestWidget : FancyScrollViewWidget<string, TestContext>
        {
            public int ClickedIndex { get; private set; } = -1;
            protected override void OnCellClicked(int index) => ClickedIndex = index;
        }

        [Test]
        public void CreateCell_CreatesWidgetCellAndForwardsClick()
        {
            var root = new GameObject("root", typeof(RectTransform));
            var container = new GameObject("container", typeof(RectTransform)).transform;
            container.SetParent(root.transform, false);
            var prefab = new GameObject("cellPrefab", typeof(RectTransform));
            prefab.AddComponent<TestCell>();
            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            var widget = new TestWidget();
            widget.Create(parent, root);
            widget.SetCellPrefabForTest(prefab);

            var cell = (TestCell)widget.CreateCellForTest(prefab, container);
            cell.Index = 3;
            cell.RaiseClick();

            Assert.AreEqual(3, widget.ClickedIndex);

            widget.CallDestroy();
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(parent.gameObject);
        }

        private sealed class TestParentUI : UIBase
        {
            public TestParentUI(GameObject root)
            {
                gameObject = root;
                transform = root.transform;
                rectTransform = root.transform as RectTransform;
            }
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollViewWidgetTests.CreateCell_CreatesWidgetCellAndForwardsClick -quit -logFile Logs/FancyScrollView-T9-red.log
```

预期：编译失败，错误包含 `FancyScrollViewWidget` 不存在。

- [ ] **步骤 3：创建 FancyScrollViewWidget**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyScrollViewWidget.cs`：

```csharp
using System;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using YooAsset;

namespace GameLogic.UI.FancyScrollView
{
    public abstract class FancyScrollViewWidget<TItemData, TContext> : UIWidget
        where TContext : class, new()
    {
        private AssetHandle _cellPrefabHandle;
        private GameObject _cellPrefab;

        public event Action<int> OnCellItemClicked;
        public event Action<int> OnCellButtonClicked;

        protected void RaiseCellItemClicked(int index)
        {
            var handler = OnCellItemClicked;
            handler?.Invoke(index);
        }

        protected void RaiseCellButtonClicked(int index)
        {
            var handler = OnCellButtonClicked;
            handler?.Invoke(index);
        }

        protected GameObject CellPrefab => _cellPrefab;

        public async UniTask InitializeAsync(string cellAssetLocation, IProgress<float> progress = null, CancellationToken ct = default)
        {
            ReleaseCellPrefabHandle();
            _cellPrefabHandle = GameModule.Resource.LoadAssetAsyncHandle<GameObject>(cellAssetLocation);
            while (!_cellPrefabHandle.IsDone)
            {
                progress?.Report(_cellPrefabHandle.Progress);
                await UniTask.Yield(ct);
            }

            await _cellPrefabHandle.Task;
            ct.ThrowIfCancellationRequested();
            _cellPrefab = _cellPrefabHandle.AssetObject as GameObject;
            if (_cellPrefab == null)
            {
                throw new InvalidOperationException($"Cell prefab load failed: {cellAssetLocation}");
            }
        }

        protected virtual IFancyCell<TItemData, TContext> CreateCell(GameObject prefab, Transform container)
        {
            var go = UnityEngine.Object.Instantiate(prefab, container);
            var cell = go.GetComponent<FancyCellWidget<TItemData, TContext>>();
            if (cell == null)
            {
                throw new MissingComponentException($"FancyCellWidget<{typeof(TItemData).FullName}, {typeof(TContext).FullName}> component not found in {prefab.name}.");
            }

            cell.InitializeWidget(this, go);
            cell.OnItemClicked += HandleCellClicked;
            cell.OnButtonClicked += HandleCellButtonClicked;
            return cell;
        }

        private void HandleCellClicked(int index)
        {
            RaiseCellItemClicked(index);
            OnCellClicked(index);
        }

        private void HandleCellButtonClicked(int index)
        {
            RaiseCellButtonClicked(index);
            OnCellButtonClicked(index);
        }

        protected virtual void OnCellClicked(int index) { }
        protected virtual void OnCellButtonClicked(int index) { }

        private void ReleaseCellPrefabHandle()
        {
            _cellPrefab = null;
            _cellPrefabHandle?.Dispose();
            _cellPrefabHandle = null;
        }

        protected override void OnDestroy()
        {
            ReleaseCellPrefabHandle();
            OnCellItemClicked = null;
            OnCellButtonClicked = null;
            base.OnDestroy();
        }

#if UNITY_EDITOR
        internal void SetCellPrefabForTest(GameObject prefab) => _cellPrefab = prefab;
        internal IFancyCell<TItemData, TContext> CreateCellForTest(GameObject prefab, Transform container) => CreateCell(prefab, container);
#endif
    }
}
```

- [ ] **步骤 4：检查点击冒泡测试代码**

确认 `TestCell` 中包含公开触发方法，测试通过该方法触发 Cell 点击事件：

```csharp
private sealed class TestCell : FancyCellWidget<string, TestContext>
{
    public void RaiseClick() => NotifyItemClicked();
    protected override void BindData(string data) { }
    public override void UpdatePosition(float normalizedPosition) { }
}
```

测试中的触发代码保持为：

```csharp
cell.RaiseClick();
```

- [ ] **步骤 5：运行测试验证通过**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollViewWidgetTests -quit -logFile Logs/FancyScrollView-T9-green.log
```

预期：PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyScrollViewWidget.cs Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollViewWidgetTests.cs
git commit -m "feat: add FancyScrollView widget adapter"
```

---

## 任务 10：实现 `FancyScrollRectWidget` 与 Proxy 桥接

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyScrollRectWidget.cs`
- 创建：`Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollRectWidgetTests.cs`

- [ ] **步骤 1：编写失败测试**

创建 `Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollRectWidgetTests.cs`：

```csharp
using System.Collections.Generic;
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyScrollRectWidgetTests
    {
        private sealed class TestCell : FancyCellWidget<string, FancyScrollRectContext>
        {
            public string Data { get; private set; }
            protected override void BindData(string data) => Data = data;
            public override void UpdatePosition(float normalizedPosition) { }
        }

        private sealed class TestWidget : FancyScrollRectWidget<string, FancyScrollRectContext>
        {
        }

        [Test]
        public void ScrollRectWidget_CreatesInternalScrollRectAndUpdatesContents()
        {
            var root = new GameObject("root", typeof(RectTransform));
            var list = new GameObject("m_fscroll_List", typeof(RectTransform), typeof(FancyScrollRectProxy), typeof(Scroller));
            list.transform.SetParent(root.transform, false);
            var container = new GameObject("container", typeof(RectTransform)).transform;
            container.SetParent(list.transform, false);
            var prefab = new GameObject("cellPrefab", typeof(RectTransform));
            prefab.AddComponent<TestCell>();

            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            var widget = new TestWidget();
            Assert.IsTrue(widget.Create(parent, root));
            widget.SetCellContainerForTest(container);
            widget.SetCellPrefabForTest(prefab);
            widget.UpdateContents(new List<string> { "A", "B" });

            Assert.AreEqual(2, widget.DataCount);

            widget.CallDestroy();
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(parent.gameObject);
        }

        private sealed class TestParentUI : UIBase
        {
            public TestParentUI(GameObject root)
            {
                gameObject = root;
                transform = root.transform;
                rectTransform = root.transform as RectTransform;
            }
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollRectWidgetTests.ScrollRectWidget_CreatesInternalScrollRectAndUpdatesContents -quit -logFile Logs/FancyScrollView-T10-red.log
```

预期：编译失败，错误包含 `FancyScrollRectWidget` 不存在。

- [ ] **步骤 3：创建 FancyScrollRectWidget**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyScrollRectWidget.cs`：

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    public abstract class FancyScrollRectWidget<TItemData, TContext> : FancyScrollViewWidget<TItemData, TContext>
        where TContext : class, IFancyScrollRectContext, new()
    {
        private FancyScrollRectProxy _proxy;
        private InternalScrollRect _scrollRect;

        public int DataCount => _scrollRect?.DataCount ?? 0;

        protected override void ScriptGenerator()
        {
            _proxy = FindChildComponent<FancyScrollRectProxy>("m_fscroll_List");
        }

        protected override void OnCreate()
        {
            if (_proxy == null)
            {
                return;
            }

            _scrollRect = _proxy.GetComponent<InternalScrollRect>();
            if (_scrollRect == null)
            {
                _scrollRect = _proxy.gameObject.AddComponent<InternalScrollRect>();
            }
            _scrollRect.Owner = this;
            _scrollRect.ApplyProxy(_proxy);
        }

        public void UpdateContents(IList<TItemData> items)
        {
            EnsureScrollRectReady();
            _scrollRect.PublicUpdateContents(items);
        }

        public void JumpTo(int index, float alignment = 0.5f)
        {
            EnsureScrollRectReady();
            _scrollRect.PublicJumpTo(index, alignment);
        }

        public void ScrollTo(int index, float duration, float alignment = 0.5f)
        {
            EnsureScrollRectReady();
            _scrollRect.PublicScrollTo(index, duration, alignment);
        }

        private void EnsureScrollRectReady()
        {
            if (_scrollRect == null)
            {
                throw new System.InvalidOperationException("FancyScrollRectWidget is not created. Check m_fscroll_List proxy node.");
            }
        }

#if UNITY_EDITOR
        internal void SetCellContainerForTest(Transform container)
        {
            EnsureScrollRectReady();
            _scrollRect.SetCellContainerForTest(container);
        }
#endif

        private sealed class InternalScrollRect : FancyScrollRect<TItemData, TContext>
        {
            private FancyScrollRectProxy _proxy;
            public FancyScrollRectWidget<TItemData, TContext> Owner { get; set; }
            public int DataCount => ItemsSource.Count;

            public void ApplyProxy(FancyScrollRectProxy proxy)
            {
                _proxy = proxy;
                cellSize = proxy.CellSize;
                spacing = proxy.Spacing;
                paddingHead = proxy.PaddingHead;
                paddingTail = proxy.PaddingTail;
                reuseCellMarginCount = proxy.ReuseCellMarginCount;
            }

            protected override GameObject CellPrefab => Owner.CellPrefab ?? _proxy?.CellPrefab;

            protected override IFancyCell<TItemData, TContext> CreateCell(GameObject prefab, Transform container)
            {
                return Owner.CreateCellForInternal(prefab, container);
            }

            public void PublicUpdateContents(IList<TItemData> items) => UpdateContents(items);
            public void PublicJumpTo(int index, float alignment) => JumpTo(index, alignment);
            public void PublicScrollTo(int index, float duration, float alignment) => ScrollTo(index, duration, alignment);

#if UNITY_EDITOR
            public void SetCellContainerForTest(Transform container) => cellContainer = container;
#endif
        }

        internal IFancyCell<TItemData, TContext> CreateCellForInternal(GameObject prefab, Transform container)
        {
            return base.CreateCell(prefab, container);
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyScrollRectWidgetTests -quit -logFile Logs/FancyScrollView-T10-green.log
```

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyScrollRectWidget.cs Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollRectWidgetTests.cs
git commit -m "feat: add FancyScrollRect widget adapter"
```

---

## 任务 11：实现 GridView Widget 与 CellGroupWrapper

**文件：**
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/CellGroupWrapper.cs`
- 创建：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyGridViewWidget.cs`
- 创建：`Assets/TEngine/Tests/EditMode/FancyScrollView/FancyGridViewWidgetTests.cs`

- [ ] **步骤 1：编写失败测试**

创建 `Assets/TEngine/Tests/EditMode/FancyScrollView/FancyGridViewWidgetTests.cs`：

```csharp
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyGridViewWidgetTests
    {
        private sealed class GridCell : FancyCellWidget<string, FancyGridViewContext>
        {
            public void RaiseClick() => NotifyItemClicked();
            protected override void BindData(string data) { }
            public override void UpdatePosition(float normalizedPosition) { }
        }

        [Test]
        public void CellGroupWrapper_CreatesChildCellWidgetsAndForwardsEvents()
        {
            var groupGo = new GameObject("group", typeof(RectTransform));
            var wrapper = groupGo.AddComponent<CellGroupWrapper<string, FancyGridViewContext>>();
            var cellPrefab = new GameObject("cell", typeof(RectTransform));
            cellPrefab.AddComponent<GridCell>();
            var context = new FancyGridViewContext
            {
                CellTemplate = cellPrefab,
                GetGroupCount = () => 2,
                GetCellSize = () => 64f,
                GetStartAxisSpacing = () => 4f,
            };
            var clicked = -1;
            wrapper.CellItemClicked += i => clicked = i;
            wrapper.SetContext(context);
            wrapper.Initialize();
            wrapper.Index = 1;
            wrapper.UpdateContent(new[] { "A", "B" });
            wrapper.GetCellForTest(0).RaiseClick();

            Assert.AreEqual(2, wrapper.CellCountForTest);
            Assert.AreEqual(2, clicked);

            Object.DestroyImmediate(cellPrefab);
            Object.DestroyImmediate(groupGo);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyGridViewWidgetTests.CellGroupWrapper_CreatesChildCellWidgetsAndForwardsEvents -quit -logFile Logs/FancyScrollView-T11-red.log
```

预期：编译失败，错误包含 `CellGroupWrapper` 不存在。

- [ ] **步骤 3：创建 CellGroupWrapper**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/CellGroupWrapper.cs`：

```csharp
using System;
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    public class CellGroupWrapper<TItemData, TContext> : FancyCellGroup<TItemData, TContext>
        where TContext : class, IFancyGridViewContext, new()
    {
        private UIBase _parentUI;

        public event Action<int> CellItemClicked;
        public event Action<int> CellButtonClicked;

        public void SetParent(UIBase parentUI)
        {
            _parentUI = parentUI;
        }

        protected override FancyCell<TItemData, TContext>[] InstantiateCells()
        {
            var cells = new FancyCell<TItemData, TContext>[Context.GetGroupCount()];
            for (var i = 0; i < cells.Length; i++)
            {
                var go = Instantiate(Context.CellTemplate, transform);
                var widget = go.GetComponent<FancyCellWidget<TItemData, TContext>>();
                if (widget == null)
                {
                    throw new MissingComponentException($"FancyCellWidget<{typeof(TItemData).FullName}, {typeof(TContext).FullName}> component not found in {Context.CellTemplate.name}.");
                }

                widget.InitializeWidget(_parentUI, go);
                widget.OnItemClicked += OnChildItemClicked;
                widget.OnButtonClicked += OnChildButtonClicked;
                cells[i] = widget;
            }

            return cells;
        }

        private void OnChildItemClicked(int index)
        {
            CellItemClicked?.Invoke(index);
        }

        private void OnChildButtonClicked(int index)
        {
            CellButtonClicked?.Invoke(index);
        }

        private void OnDestroy()
        {
            CellItemClicked = null;
            CellButtonClicked = null;
        }

#if UNITY_EDITOR
        internal int CellCountForTest => Cells?.Length ?? 0;
        internal FancyCellWidget<TItemData, TContext> GetCellForTest(int index) => Cells[index] as FancyCellWidget<TItemData, TContext>;
#endif
    }
}
```

- [ ] **步骤 4：创建 FancyGridViewWidget**

创建 `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyGridViewWidget.cs`：

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.UI.FancyScrollView
{
    public abstract class FancyGridViewWidget<TItemData, TContext> : FancyScrollViewWidget<TItemData, TContext>
        where TContext : class, IFancyGridViewContext, new()
    {
        private InternalGridView _gridView;

        public int DataCount => _gridView?.DataCount ?? 0;

        protected override void OnCreate()
        {
            _gridView = gameObject.GetComponent<InternalGridView>();
            if (_gridView == null)
            {
                _gridView = gameObject.AddComponent<InternalGridView>();
            }
            _gridView.Owner = this;
        }

        public void UpdateContents(IList<TItemData> items)
        {
            _gridView.PublicUpdateContents(items);
        }

        private sealed class InternalGridView : FancyGridView<TItemData, TContext>
        {
            public FancyGridViewWidget<TItemData, TContext> Owner { get; set; }
            protected override GameObject CellPrefab => Owner.CellPrefab;

            protected override void SetupCellTemplate()
            {
                var template = CellPrefab.GetComponent<FancyCell<TItemData, TContext>>();
                Setup<CellGroupWrapper<TItemData, TContext>>(template);
            }

            protected override IFancyCell<TItemData[], TContext> CreateCell(GameObject prefab, Transform container)
            {
                var go = Instantiate(prefab, container);
                var group = go.GetComponent<CellGroupWrapper<TItemData, TContext>>();
                group.SetParent(Owner);
                group.CellItemClicked += Owner.HandleGridCellClicked;
                group.CellButtonClicked += Owner.HandleGridCellButtonClicked;
                return group;
            }

            public void PublicUpdateContents(IList<TItemData> items) => UpdateContents(items);
        }

        private void HandleGridCellClicked(int index)
        {
            RaiseCellItemClicked(index);
        }

        private void HandleGridCellButtonClicked(int index)
        {
            RaiseCellButtonClicked(index);
        }
    }
}
```

任务 9 已定义 `RaiseCellItemClicked` 与 `RaiseCellButtonClicked`，GridView 的事件转发必须调用这两个 protected 方法，不能直接触发基类 event。

- [ ] **步骤 5：运行测试验证通过**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.FancyGridViewWidgetTests -quit -logFile Logs/FancyScrollView-T11-green.log
```

预期：PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/CellGroupWrapper.cs Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/Adapters/FancyGridViewWidget.cs Assets/TEngine/Tests/EditMode/FancyScrollView/FancyGridViewWidgetTests.cs
git commit -m "feat: add FancyGridView widget adapter"
```

---

## 任务 12：扩展 UIScriptGenerator 前缀规则

**文件：**
- 修改：`Assets/Editor/UIScriptGenerator/ScriptGenerateRuler.cs`
- 修改：`Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.cs`
- 修改：`Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.asset` 或项目实际存在的 `ScriptGeneratorSetting.asset`
- 创建：`Assets/TEngine/Tests/EditMode/FancyScrollView/ScriptGeneratorRuleTests.cs`

- [ ] **步骤 1：编写失败测试**

创建 `Assets/TEngine/Tests/EditMode/FancyScrollView/ScriptGeneratorRuleTests.cs`：

```csharp
using System.Linq;
using NUnit.Framework;
using TEngine.Editor.UI;

namespace TEngine.Tests
{
    [TestFixture]
    public class ScriptGeneratorRuleTests
    {
        [Test]
        public void UIComponentName_ContainsFancyScrollViewTypes()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(UIComponentName), "FancyScrollRectProxy"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(UIComponentName), "FancyScroller"));
        }

        [Test]
        public void DefaultRules_ContainFancyScrollPrefixesBeforeScrollRectPrefix()
        {
            var rules = ScriptGeneratorSetting.GetDefaultScriptGenerateRulesForTest();
            var fscrollIndex = rules.FindIndex(x => x.uiElementRegex == "m_fscroll");
            var scrollerIndex = rules.FindIndex(x => x.uiElementRegex == "m_scroller");
            var scrollIndex = rules.FindIndex(x => x.uiElementRegex == "m_scroll");

            Assert.GreaterOrEqual(fscrollIndex, 0);
            Assert.GreaterOrEqual(scrollerIndex, 0);
            Assert.GreaterOrEqual(scrollIndex, 0);
            Assert.Less(fscrollIndex, scrollIndex);
            Assert.Less(scrollerIndex, scrollIndex);
            Assert.AreEqual(UIComponentName.FancyScrollRectProxy, rules[fscrollIndex].componentName);
            Assert.AreEqual(UIComponentName.FancyScroller, rules[scrollerIndex].componentName);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.ScriptGeneratorRuleTests -quit -logFile Logs/FancyScrollView-T12-red.log
```

预期：编译失败，错误包含 `UIComponentName.FancyScrollRectProxy` 或 `GetDefaultScriptGenerateRulesForTest` 不存在。

- [ ] **步骤 3：修改 UIComponentName 枚举**

修改 `Assets/Editor/UIScriptGenerator/ScriptGenerateRuler.cs` 的 `UIComponentName`，在 `ScrollRect` 前后插入新类型：

```csharp
public enum UIComponentName
{
    GameObject,
    Transform,
    RectTransform,
    Text,
    RichTextItem,
    Button,
    Image,
    RawImage,
    FancyScrollRectProxy,
    FancyScroller,
    ScrollRect,
    Scrollbar,
    InputField,
    GridLayoutGroup,
    HorizontalLayoutGroup,
    VerticalLayoutGroup,
    Slider,
    Toggle,
    ToggleGroup,
    AnimationCurve,
    CanvasGroup,
    TextMeshProUGUI,
    Canvas,
    Dropdown,
    TMP_InputField,
    TMP_Dropdown,
}
```

- [ ] **步骤 4：修改 ScriptGeneratorSetting 默认规则**

在 `Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.cs` 中新增测试可见的默认规则方法；若文件已有初始化列表，把两条规则插入在 `m_scrollBar` / `m_scroll` 前：

```csharp
internal static List<ScriptGenerateRuler> GetDefaultScriptGenerateRulesForTest()
{
    return new List<ScriptGenerateRuler>
    {
        new("m_go", UIComponentName.GameObject),
        new("m_item", UIComponentName.GameObject, true),
        new("m_tf", UIComponentName.Transform),
        new("m_rect", UIComponentName.RectTransform),
        new("m_richText", UIComponentName.RichTextItem),
        new("m_text", UIComponentName.Text),
        new("m_btn", UIComponentName.Button),
        new("m_img", UIComponentName.Image),
        new("m_rimg", UIComponentName.RawImage),
        new("m_fscroll", UIComponentName.FancyScrollRectProxy),
        new("m_scroller", UIComponentName.FancyScroller),
        new("m_scrollBar", UIComponentName.Scrollbar),
        new("m_scroll", UIComponentName.ScrollRect),
        new("m_input", UIComponentName.InputField),
        new("m_grid", UIComponentName.GridLayoutGroup),
        new("m_hlay", UIComponentName.HorizontalLayoutGroup),
        new("m_vlay", UIComponentName.VerticalLayoutGroup),
        new("m_slider", UIComponentName.Slider),
        new("m_toggle", UIComponentName.Toggle),
        new("m_group", UIComponentName.ToggleGroup),
        new("m_curve", UIComponentName.AnimationCurve),
        new("m_canvasGroup", UIComponentName.CanvasGroup),
        new("m_tmpInput", UIComponentName.TMP_InputField),
        new("m_tmpDropdown", UIComponentName.TMP_Dropdown),
        new("m_tmp", UIComponentName.TextMeshProUGUI),
        new("m_canvas", UIComponentName.Canvas),
        new("m_dropdown", UIComponentName.Dropdown),
    };
}
```

并让创建新 `ScriptGeneratorSetting` 时使用该默认列表。若当前工程的 asset 已存在，需要手动在 asset 的 `scriptGenerateRule` 中加入：

```yaml
- uiElementRegex: m_fscroll
  componentName: FancyScrollRectProxy
  isUIWidget: 0
- uiElementRegex: m_scroller
  componentName: FancyScroller
  isUIWidget: 0
```

- [ ] **步骤 5：确认生成代码类型名称映射**

检查生成器中 `UIComponentName → 类型字符串` 的 switch/字典，并加入：

```csharp
UIComponentName.FancyScrollRectProxy => "GameLogic.UI.FancyScrollView.FancyScrollRectProxy"
UIComponentName.FancyScroller => "GameLogic.UI.FancyScrollView.Scroller"
```

生成器输出的 C# 文件需要包含 FancyScrollView 命名空间：

```csharp
using GameLogic.UI.FancyScrollView;
```

- [ ] **步骤 6：运行测试验证通过**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -testFilter TEngine.Tests.ScriptGeneratorRuleTests -quit -logFile Logs/FancyScrollView-T12-green.log
```

预期：PASS。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Editor/UIScriptGenerator/ScriptGenerateRuler.cs Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.cs Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.asset Assets/TEngine/Tests/EditMode/FancyScrollView/ScriptGeneratorRuleTests.cs
git commit -m "feat: add FancyScrollView script generator prefixes"
```

---

## 任务 13：更新 tengine-dev references 文档

**文件：**
- 修改：`.claude/skills/tengine-dev/references/naming-rules.md`
- 修改：`.claude/skills/tengine-dev/references/ui-patterns.md`
- 修改：`.claude/skills/tengine-dev/references/mve-patterns.md`

- [ ] **步骤 1：更新命名规范文档**

在 `.claude/skills/tengine-dev/references/naming-rules.md` 的 UI 节点命名规范表中加入：

```markdown
| `m_fscroll_` | `FancyScrollRectProxy` | `m_fscroll_List` |
| `m_scroller_` | `Scroller` | `m_scroller_List` |
```

并在注意事项中加入：

```markdown
- `m_fscroll_` / `m_scroller_` 必须写在 `m_scroll_` 之前，否则 FancyScrollView 节点可能被普通 ScrollRect 规则先匹配。
```

- [ ] **步骤 2：更新 UI 模式文档**

在 `.claude/skills/tengine-dev/references/ui-patterns.md` 追加 FancyScrollView 小节：

```markdown
## FancyScrollView 虚拟化列表

- 普通少量列表继续使用 `AdjustIconNum<T>()` / `AsyncAdjustIconNum<T>()`。
- 大量数据、需要复用 Cell、滚动动画或 Grid 虚拟化时使用 `FancyScrollViewWidget` 系列。
- Cell 继承 `FancyCellWidget<TItemData,TContext>`，通过 `BindData(TItemData data)` 接收数据。
- ScrollRect 容器节点命名 `m_fscroll_List` 并挂 `FancyScrollRectProxy`，滚动控制节点命名 `m_scroller_List` 并挂 `Scroller`。
- Cell 内部点击用 `NotifyItemClicked()` / `NotifyButtonClicked()` 冒泡到 ScrollViewWidget，UIWindow 再按需调用 Service 或发送 GameEvent。
```

- [ ] **步骤 3：更新 MVE 文档**

在 `.claude/skills/tengine-dev/references/mve-patterns.md` 的 FancyScrollView 待实现区域替换为已实现模式：

```markdown
## FancyScrollView CellWidget MVE 模式

- Window/DataContext 负责把 Model 数据映射成列表展示数据。
- Window 在 `OnRefresh()` 或绑定回调中调用 `_scrollView.UpdateContents(items)`。
- `FancyCellWidget.BindData(TItemData data)` 是 Cell 接收数据的唯一入口。
- Cell 内部可用 `CreateProperty<T>()` 创建生命周期随 Cell 的 BindableProperty，并在 `OnDestroy()` 自动 Dispose。
- Cell 被复用时必须在 `BindData` 中重置瞬态状态，例如选中态、按钮可用态、加载状态。
```

- [ ] **步骤 4：文档 grep 验证**

运行：

```bash
rg "m_fscroll_|FancyScrollView CellWidget|FancyScrollView 虚拟化列表" .claude/skills/tengine-dev/references
```

预期：输出包含三篇文档中的新增段落。

- [ ] **步骤 5：Commit**

```bash
git add .claude/skills/tengine-dev/references/naming-rules.md .claude/skills/tengine-dev/references/ui-patterns.md .claude/skills/tengine-dev/references/mve-patterns.md
git commit -m "docs: document FancyScrollView integration patterns"
```

---

## 任务 14：Unity 编译、EditMode 测试与冒烟验证

**文件：**
- 修改：前述任务创建或修改的 FancyScrollView Runtime、Adapter、Editor 规则、测试与 references 文档。

- [ ] **步骤 1：刷新 Unity 并检查编译错误**

运行 Unity MCP 或命令行刷新：

```bash
Unity -batchmode -projectPath . -quit -logFile Logs/FancyScrollView-compile.log
```

预期：退出码 0，日志不包含 `error CS`。

- [ ] **步骤 2：运行 FancyScrollView EditMode 测试**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.FancyScrollView -quit -logFile Logs/FancyScrollView-tests.log
```

预期：所有 `TEngine.Tests.FancyScrollView` 测试 PASS。

- [ ] **步骤 3：运行现有相关回归测试**

运行：

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames TEngine.Tests.DataBinding,TEngine.Tests.Timer -quit -logFile Logs/FancyScrollView-regression.log
```

预期：`TEngine.Tests.DataBinding` 与 `TEngine.Tests.Timer` 全部 PASS。

- [ ] **步骤 4：检查禁止模式**

运行：

```bash
rg "Resources\.Load|StartCoroutine|LoadAssetAsync<GameObject>|LoadAssetAsync<Sprite>|SetSpriteAsync" Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView Assets/TEngine/Tests/EditMode/FancyScrollView
```

预期：无输出。若出现 `LoadAssetAsync<GameObject>`，必须改为 `LoadAssetAsyncHandle<GameObject>` 并持有/释放 `AssetHandle`。

- [ ] **步骤 5：检查命名空间残留**

运行：

```bash
rg "namespace FancyScrollView|using FancyScrollView|using EasingCore;" Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView
```

预期：无输出；EasingCore 只能出现在 `namespace GameLogic.UI.FancyScrollView.EasingCore` 或 `using GameLogic.UI.FancyScrollView.EasingCore;`。

- [ ] **步骤 6：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView Assets/TEngine/Tests/EditMode/FancyScrollView .claude/skills/tengine-dev/references
git commit -m "test: verify FancyScrollView integration"
```

---

## 最终验收清单

- [ ] `Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollView/` 下存在 Core、Scroller、ScrollRect、GridView、Adapters 五个目录。
- [ ] 所有上游 Runtime 文件命名空间为 `GameLogic.UI.FancyScrollView`，EasingCore 为 `GameLogic.UI.FancyScrollView.EasingCore`。
- [ ] `FancyScrollView<TItemData,TContext>` 的池类型是 `IFancyCell<TItemData,TContext>`，Cell 创建通过 `protected virtual CreateCell`。
- [ ] `FancyCellWidget<TItemData,TContext>` 继承 `UIWidget` 且实现 `IFancyCell<TItemData,TContext>`。
- [ ] `FancyScrollViewWidget.InitializeAsync` 使用 `LoadAssetAsyncHandle<GameObject>` 并在 `OnDestroy` 释放句柄。
- [ ] `FancyScrollRectProxy` 可在 Inspector 配置 cellSize、spacing、padding、reuse margin。
- [ ] `ContentLoadScheduler` 支持 AutoTrigger、Cascade、Manual 三种模式。
- [ ] ScriptGenerator 支持 `m_fscroll_` 与 `m_scroller_` 前缀。
- [ ] `TEngine.Tests.FancyScrollView` 全部通过。
- [ ] `TEngine.Tests.DataBinding` 与 `TEngine.Tests.Timer` 回归通过。
- [ ] `rg` 禁止模式检查无输出。

---

## 实施注意事项

- 计划中的上游源码导入任务必须保留 MIT 版权头，不要复制 `FancyScrollView.asmdef`，避免热更程序集拆分。
- `AssetHandle` 使用 YooAsset 命名空间；实现前检查 `IResourceModule.LoadAssetAsyncHandle` 返回类型，保持 `InitializeAsync` 中持有句柄并在 `OnDestroy` 释放。
- 任务 1 已创建 `AssemblyInfo.cs` 并配置 `InternalsVisibleTo("TEngine.Tests.FancyScrollView")`；测试辅助 API 保持 internal，不改成 public。
- 测试中创建 Widget 时必须提供 `TestParentUI` 父对象，避免 `UIWidget.Create` 访问 `Parent.ListChild` 时空引用；生产代码不允许 Cell 脱离父 UI 使用。
- `FancyScrollViewWidget` 已提供 `RaiseCellItemClicked` / `RaiseCellButtonClicked`，GridView 派生类通过 protected Raise 方法转发事件，不直接触发基类 event。