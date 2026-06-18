# Scroller Unification 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 将 FancyScrollerController 从泛型变为非泛型，让 ScriptGenerator 自动生成它，用户只需 3 行代码即可使用 ScrollView。

**架构：** 双接口体系（IFancyCell 非泛型 + IFancyCell\<T,C\> 泛型），Controller 去泛型变为普通类，Widget 吸收基类也去泛型。ScriptGenerator 在遇到 Scroller 组件时生成 new + Initialize 而非 GetComponent。

**技术栈：** Unity C#、TEngine UIWidget、UniTask、YooAsset、ScriptGenerator 代码生成

---

## 文件结构

| 文件 | 职责 | 操作 |
|------|------|------|
| `Assets/GameScripts/HotFix/.../FancyScrollView/Core/IFancyCell.cs` | Cell 接口定义 | 改造：新增非泛型基接口 |
| `Assets/GameScripts/HotFix/.../FancyScrollView/Adapters/FancyCellWidget.cs` | Cell Widget 基类 | 改造：添加显式接口实现 |
| `Assets/GameScripts/HotFix/.../FancyScrollView/Adapters/FancyScrollerController.cs` | ScrollView 控制器 | 重大重构：去泛型 |
| `Assets/GameScripts/HotFix/.../FancyScrollView/Adapters/FancyScrollerWidget.cs` | ScrollView Widget | 重大重构：去泛型 + 吸收基类 |
| `Assets/GameScripts/HotFix/.../FancyScrollView/Adapters/FancyScrollViewWidget.cs` | ScrollView Widget 基类 | 删除（被 ScrollerWidget 吸收） |
| `Assets/Editor/UIScriptGenerator/ScriptGenerateRuler.cs` | 生成规则 | 微调：新增 isCustomInit 字段 |
| `Assets/Editor/UIScriptGenerator/ScriptAutoGenerator.cs` | 代码生成器 | 改造：Scroller 特殊路径 + OnDestroy |
| `Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.cs` | 生成器设置 | 微调：组件名映射更新 |
| `Assets/TEngine/Tests/EditMode/FancyScrollView/FancyCellWidgetTests.cs` | Cell Widget 测试 | 改造：新增非泛型接口测试 |
| `Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollerControllerTests.cs` | Controller 测试 | 新增 |
| `.../Example01_Basic/View/Example01_BasicUI.cs` | Example01 用户代码 | 简化 |
| `.../Example01_Basic/View/Example01_BasicUI_Gen.g.cs` | Example01 生成代码 | 重新生成 |
| `.../Example02_FocusOn/Example02_FocusOnScrollViewWidget.cs` | Example02 Widget | 简化：去泛型 |
| `.../Example03_InfiniteScroll/Example03_InfiniteScrollViewWidget.cs` | Example03 Widget | 简化：去泛型 |

---

## 任务 1：新增 IFancyCell 非泛型基接口

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Core/IFancyCell.cs`

- [ ] **步骤 1：在 IFancyCell.cs 中新增非泛型基接口**

将文件内容替换为：

```csharp
/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// FancyScrollView Cell 的非泛型统一接口。
    /// Controller/Widget 层只依赖此接口，不感知具体数据类型。
    /// </summary>
    public interface IFancyCell
    {
        int Index { get; set; }
        bool IsVisible { get; }
        void SetVisible(bool visible);
        void UpdateContent(object itemData);
        void UpdatePosition(float position);
        void SetContext(object context);
        void Initialize();
    }

    /// <summary>
    /// FancyScrollView Cell 的泛型接口（类型安全层）。
    /// 继承自 <see cref="IFancyCell"/>，Controller 通过非泛型接口调用，
    /// Cell 端通过显式接口实现将 object 转型为具体类型。
    /// </summary>
    public interface IFancyCell<TItemData, TContext> : IFancyCell
        where TContext : class, new()
    {
        // 注：泛型版本的方法不在此声明（因为与非泛型基接口方法签名冲突）。
        // Cell 端直接 override FancyCellWidget<T,C> 的泛型方法即可。
    }
}
```

- [ ] **步骤 2：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Core/IFancyCell.cs
git commit -m "refactor: add non-generic IFancyCell base interface"
```

---

## 任务 2：FancyCellWidget 添加显式接口实现

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Adapters/FancyCellWidget.cs`

- [ ] **步骤 1：添加 IFancyCell 非泛型接口的显式实现**

在 `FancyCellWidget<TItemData, TContext>` 类中，在 `SetContext` 方法之后、`Initialize` 方法之前，添加以下显式接口实现：

```csharp
        // ── IFancyCell 非泛型接口的显式实现 ─────────────────

        /// <summary>
        /// 显式接口实现：将非泛型 object 数据转为泛型类型后调用 BindData。
        /// </summary>
        void IFancyCell.UpdateContent(object itemData)
        {
            BindData((TItemData)itemData);
        }

        /// <summary>
        /// 显式接口实现：将非泛型 object 上下文转为泛型类型后调用 SetContext。
        /// </summary>
        void IFancyCell.SetContext(object context)
        {
            SetContext((TContext)context);
        }
```

同时修改现有的 `UpdateContent` 方法——删除它，因为 `IFancyCell.UpdateContent(object)` 显式实现已经承担了同样的职责（调用 BindData）：

将原来的：
```csharp
        /// <inheritdoc/>
        public void UpdateContent(TItemData itemData)
        {
            BindData(itemData);
        }
```

替换为（保留方法但标记为调用 BindData 的快捷方式）：
```csharp
        /// <summary>
        /// 使用泛型数据更新 Cell 内容（类型安全的快捷方式）。
        /// </summary>
        public void UpdateContent(TItemData itemData)
        {
            BindData(itemData);
        }
```

注意：`UpdateContent(TItemData)` 保留为 public 方法供子类或直接调用使用。`IFancyCell.UpdateContent(object)` 是显式接口实现，通过接口引用时调用。两者都调用 `BindData`，不冲突。

- [ ] **步骤 2：验证编译**

在 Unity Console 中确认无编译错误。

- [ ] **步骤 3：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Adapters/FancyCellWidget.cs
git commit -m "refactor: add IFancyCell explicit interface impl to FancyCellWidget"
```

---

## 任务 3：新增 FancyCellWidget 非泛型接口测试

**文件：**
- 修改：`Assets/TEngine/Tests/EditMode/FancyScrollView/FancyCellWidgetTests.cs`

- [ ] **步骤 1：添加非泛型接口测试方法**

在 `FancyCellWidgetTests` 类末尾（`}` 之前）添加：

```csharp
        [Test]
        public void FancyCellWidget_ImplementsNonGenericIFancyCell()
        {
            var parentGo = new GameObject("parent", typeof(RectTransform));
            var parent = new TestParentUI(parentGo);
            var cellGo = new GameObject("cell", typeof(RectTransform));
            var cell = new TestCellWidget();
            cell.Create(parent, cellGo);

            // 可以转为非泛型接口
            IFancyCell iface = cell;
            Assert.IsNotNull(iface);
            Assert.IsInstanceOf<IFancyCell>(cell);

            // 通过非泛型接口调用 UpdateContent
            iface.UpdateContent("test-data");
            Assert.AreEqual("test-data", cell.LastBoundData);

            // 通过非泛型接口调用 SetContext
            var ctx = new TestContext();
            iface.SetContext(ctx);
            Assert.AreSame(ctx, cell.CellContext);

            cell.CallDestroy();
            Object.DestroyImmediate(cellGo);
            Object.DestroyImmediate(parentGo);
        }
```

注意：需要在 `TestCellWidget` 中添加 `LastBoundData` 和 `CellContext` 属性。找到 `TestCellWidget` 内部类，添加：

```csharp
        public string LastBoundData { get; private set; }
        public TestContext CellContext => Context;
```

并确保 `BindData` 方法记录最后绑定的数据：

```csharp
        protected override void BindData(string data)
        {
            LastBoundData = data;
        }
```

- [ ] **步骤 2：运行测试**

在 Unity Test Runner 中运行 `FancyCellWidgetTests`，确认新测试通过。

- [ ] **步骤 3：Commit**

```bash
git add Assets/TEngine/Tests/EditMode/FancyScrollView/FancyCellWidgetTests.cs
git commit -m "test: add non-generic IFancyCell interface test"
```

---

## 任务 4：FancyScrollerController 去泛型重构

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Adapters/FancyScrollerController.cs`

这是最大的改动。将整个文件重写。

- [ ] **步骤 1：重写 FancyScrollerController 为非泛型**

将 `FancyScrollerController.cs` 完整替换为以下内容：

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using YooAsset;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// 纯 C# 的 ScrollView 控制器（非泛型，组合模式，不继承 UIWidget）。
    /// <para>
    /// 负责虚拟化列表的全部调度逻辑：Cell 池管理、位置计算、数据分发。
    /// 不感知具体数据类型——类型安全由 Cell 端的 <see cref="IFancyCell"/> 显式接口实现保证。
    /// </para>
    /// <para>
    /// 使用方式（在 UIWindow 中）：
    /// <code>
    /// // m_scroller 由 ScriptGenerator 自动生成（FancyScrollerController 类型）
    /// m_scroller.SetCellFactory(() => new MyCell());
    /// await m_scroller.LoadCellPrefabAsync("MyCellPrefab");
    /// m_scroller.UpdateData(items);
    /// </code>
    /// </para>
    /// </summary>
    public class FancyScrollerController : IDisposable
    {
        // ── 依赖（通过 Initialize 注入）───────────────────────────
        private UIBase _owner;
        private Scroller _scroller;
        private Transform _cellContainer;
        private Func<IFancyCell> _cellFactory;

        // ── Cell Prefab 资源管理 ─────────────────────────────────
        private AssetHandle _cellPrefabHandle;
        private GameObject _cellPrefab;

        // ── Cell 池 ──────────────────────────────────────────────
        private readonly List<IFancyCell> _cellPool = new();
        private object _sharedContext;
        private IList _itemsSource = new List<object>();
        private float _currentPosition;
        private bool _initialized;

        // ── 事件 ─────────────────────────────────────────────────

        /// <summary>Cell 被点击时触发，参数为 Cell 索引。</summary>
        public event Action<int> OnCellItemClicked;

        /// <summary>Cell 内按钮被点击时触发，参数为 Cell 索引。</summary>
        public event Action<int> OnCellButtonClicked;

        // ── 配置属性 ──────────────────────────────────────────────

        /// <summary>Cell 间隔（0.01 ~ 1），默认 0.2。</summary>
        public float CellInterval { get; set; } = 0.2f;

        /// <summary>滚动偏移（0 ~ 1），默认 0.5。</summary>
        public float ScrollOffset { get; set; } = 0.5f;

        /// <summary>是否启用循环滚动，默认 false。</summary>
        public bool Loop { get; set; } = false;

        /// <summary>当前滚动位置。</summary>
        public float CurrentPosition => _currentPosition;

        /// <summary>与所有 Cell 共享的 Context 对象。</summary>
        public object SharedContext => _sharedContext;

        #region 生命周期

        /// <summary>
        /// 初始化控制器，注入所有依赖。
        /// </summary>
        public void Initialize(
            UIBase owner,
            Scroller scroller,
            Transform cellContainer,
            Func<IFancyCell> cellFactory,
            object context = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _scroller = scroller ?? throw new ArgumentNullException(nameof(scroller));
            _cellContainer = cellContainer ?? throw new ArgumentNullException(nameof(cellContainer));
            _cellFactory = cellFactory;
            _sharedContext = context;

            _scroller.OnValueChanged(OnScrollerPositionChanged);
        }

        /// <summary>
        /// 延后设置 Cell 工厂（ScriptGenerator 生成的 Initialize 传 null 时使用）。
        /// </summary>
        public void SetCellFactory(Func<IFancyCell> cellFactory)
        {
            _cellFactory = cellFactory ?? throw new ArgumentNullException(nameof(cellFactory));
        }

        /// <summary>
        /// 异步加载 Cell Prefab 并缓存。
        /// </summary>
        public async UniTask LoadCellPrefabAsync(
            string cellAssetLocation,
            IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            ReleaseCellPrefabHandle();

            _cellPrefabHandle = GameModule.Resource.LoadAssetAsyncHandle<GameObject>(cellAssetLocation);

            while (!_cellPrefabHandle.IsDone)
            {
                progress?.Report(_cellPrefabHandle.Progress);
                await UniTask.Yield(ct);
            }

            ct.ThrowIfCancellationRequested();

            if (_cellPrefabHandle.Status == EOperationStatus.Failed)
            {
                throw new InvalidOperationException(
                    $"Cell prefab load failed: {cellAssetLocation}, error: {_cellPrefabHandle.LastError}");
            }

            _cellPrefab = _cellPrefabHandle.AssetObject as GameObject;
            if (_cellPrefab == null)
            {
                throw new InvalidOperationException(
                    $"Cell prefab load failed (asset is null): {cellAssetLocation}");
            }
        }

        /// <summary>
        /// 释放所有资源。
        /// </summary>
        public void Dispose()
        {
            for (var i = _cellPool.Count - 1; i >= 0; i--)
            {
                var cell = _cellPool[i];
                if (cell is FancyCellWidget<object, object> widget && widget.gameObject != null)
                {
                    UnityEngine.Object.Destroy(widget.gameObject);
                }
            }

            _cellPool.Clear();
            ReleaseCellPrefabHandle();
            OnCellItemClicked = null;
            OnCellButtonClicked = null;
        }

        private void ReleaseCellPrefabHandle()
        {
            _cellPrefab = null;
            _cellPrefabHandle?.Dispose();
            _cellPrefabHandle = null;
        }

        #endregion

        #region 公开 API

        /// <summary>
        /// 更新列表数据并刷新显示（非泛型版本）。
        /// </summary>
        public void UpdateData(IList items)
        {
            _itemsSource = items ?? new List<object>();
            if (_scroller != null) _scroller.SetTotalCount(_itemsSource.Count);
            Refresh();
        }

        /// <summary>
        /// 更新列表数据并刷新显示（泛型便捷方法）。
        /// </summary>
        public void UpdateData<T>(IList<T> items)
        {
            UpdateData((IList)items);
        }

        /// <summary>滚动到指定索引位置。</summary>
        public void ScrollTo(int index, float duration = 0.3f)
        {
            _scroller?.ScrollTo(index, duration);
        }

        /// <summary>立即跳转到指定索引位置。</summary>
        public void JumpTo(int index)
        {
            _scroller?.JumpTo(index);
        }

        /// <summary>强制刷新所有可见 Cell 的内容。</summary>
        public void Refresh()
        {
            UpdatePosition(_currentPosition, true);
        }

        /// <summary>重新布局。</summary>
        public void Relayout()
        {
            UpdatePosition(_currentPosition, false);
        }

        /// <summary>获取当前居中显示的 Cell 索引。</summary>
        public int GetCenterIndex()
        {
            return Mathf.RoundToInt(_currentPosition);
        }

        #endregion

        #region Cell 创建

        private IFancyCell CreateCell()
        {
            if (_cellFactory == null)
            {
                throw new InvalidOperationException(
                    "Cell factory not set. Call SetCellFactory() before using the controller.");
            }

            var go = UnityEngine.Object.Instantiate(_cellPrefab, _cellContainer);
            var cell = _cellFactory();

            // FancyCellWidget<TItemData, TContext> 的 InitializeWidget 需要 UIBase + GameObject
            if (cell is UIWidget widget)
            {
                // 通过反射调用 InitializeWidget（因为泛型类型不确定）
                var method = cell.GetType().BaseType?.GetMethod("InitializeWidget",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (method != null)
                {
                    method.Invoke(cell, new object[] { _owner, go, true });
                }
            }

            // 订阅事件
            if (cell is FancyCellWidget<object, object> fancyCell)
            {
                fancyCell.OnItemClicked += HandleCellClicked;
                fancyCell.OnButtonClicked += HandleCellButtonClicked;
            }

            return cell;
        }

        private void HandleCellClicked(int index)
        {
            OnCellItemClicked?.Invoke(index);
        }

        private void HandleCellButtonClicked(int index)
        {
            OnCellButtonClicked?.Invoke(index);
        }

        #endregion

        #region Cell 池管理

        private void OnScrollerPositionChanged(float position)
        {
            UpdatePosition(position, false);
        }

        private void UpdatePosition(float position, bool forceRefresh)
        {
            if (_cellPrefab == null || _cellContainer == null) return;

            if (!_initialized)
            {
                _initialized = true;
            }

            _currentPosition = position;

            var interval = CellInterval;
            var offset = ScrollOffset;
            var p = position - offset / interval;
            var firstIndex = Mathf.CeilToInt(p);
            var firstPosition = (Mathf.Ceil(p) - p) * interval;

            if (firstPosition + _cellPool.Count * interval < 1f)
            {
                ResizePool(firstPosition);
            }

            UpdateCells(firstPosition, firstIndex, forceRefresh);
        }

        private void ResizePool(float firstPosition)
        {
            var interval = CellInterval;
            var addCount = Mathf.CeilToInt((1f - firstPosition) / interval) - _cellPool.Count;
            for (var i = 0; i < addCount; i++)
            {
                var cell = CreateCell();
                if (_sharedContext != null) cell.SetContext(_sharedContext);
                cell.Initialize();
                cell.SetVisible(false);
                _cellPool.Add(cell);
            }
        }

        private void UpdateCells(float firstPosition, int firstIndex, bool forceRefresh)
        {
            var interval = CellInterval;
            var useLoop = Loop;
            var items = _itemsSource;
            var itemCount = items.Count;

            for (var i = 0; i < _cellPool.Count; i++)
            {
                var index = firstIndex + i;
                var position = firstPosition + i * interval;
                var cell = _cellPool[CircularIndex(index, _cellPool.Count)];

                if (useLoop)
                {
                    index = CircularIndex(index, itemCount);
                }

                if (index < 0 || index >= itemCount || position > 1f)
                {
                    cell.SetVisible(false);
                    continue;
                }

                if (forceRefresh || cell.Index != index || !cell.IsVisible)
                {
                    cell.Index = index;
                    cell.SetVisible(true);
                    cell.UpdateContent(items[index]);
                }

                cell.UpdatePosition(position);
            }
        }

        private static int CircularIndex(int i, int size)
        {
            return size < 1 ? 0 : i < 0 ? size - 1 + (i + 1) % size : i % size;
        }

        #endregion
    }
}
```

- [ ] **步骤 2：修复 Dispose 中的 Cell 销毁**

注意步骤 1 中的 `Dispose` 和 `CreateCell` 方法里使用了 `FancyCellWidget<object, object>` 和反射。这两个地方需要更可靠的实现。替换 `CreateCell` 和 `Dispose` 中对 Cell 的处理：

**CreateCell 替换为：**

```csharp
        private IFancyCell CreateCell()
        {
            if (_cellFactory == null)
            {
                throw new InvalidOperationException(
                    "Cell factory not set. Call SetCellFactory() before using the controller.");
            }

            var go = UnityEngine.Object.Instantiate(_cellPrefab, _cellContainer);
            var cell = _cellFactory();

            // FancyCellWidget 的 InitializeWidget 是 internal 方法
            // 通过 dynamic 或直接转型调用
            var cellType = cell.GetType();
            var baseType = cellType;
            while (baseType != null && baseType != typeof(UIWidget))
            {
                baseType = baseType.BaseType;
            }

            // 直接调用 InitializeWidget(UIBase, GameObject, bool)
            var initMethod = cellType.GetMethod("InitializeWidget",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (initMethod != null)
            {
                initMethod.Invoke(cell, new object[] { _owner, go, true });
            }

            // 订阅事件 — 通过 IFancyCell 无法拿到事件，需要从具体类型取
            var onItemClickedEvent = cellType.GetEvent("OnItemClicked");
            var onButtonClickedEvent = cellType.GetEvent("OnButtonClicked");
            if (onItemClickedEvent != null)
            {
                onItemClickedEvent.AddEventHandler(cell, (Action<int>)HandleCellClicked);
            }
            if (onButtonClickedEvent != null)
            {
                onButtonClickedEvent.AddEventHandler(cell, (Action<int>)HandleCellButtonClicked);
            }

            return cell;
        }
```

**Dispose 中的 Cell 销毁替换为：**

```csharp
            for (var i = _cellPool.Count - 1; i >= 0; i--)
            {
                var cell = _cellPool[i];
                if (cell is UIWidget widget && widget.gameObject != null)
                {
                    UnityEngine.Object.Destroy(widget.gameObject);
                }
            }
```

- [ ] **步骤 3：验证编译**

确认 Unity Console 无编译错误。

- [ ] **步骤 4：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Adapters/FancyScrollerController.cs
git commit -m "refactor: de-generify FancyScrollerController"
```

---

## 任务 5：FancyScrollerWidget 去泛型 + 吸收基类

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Adapters/FancyScrollerWidget.cs`
- 修改：`Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Adapters/FancyScrollViewWidget.cs`（内容清空为过时标记）
- 新增测试：`Assets/TEngine/Tests/EditMode/FancyScrollView/FancyScrollerWidgetTests.cs`

- [ ] **步骤 1：重写 FancyScrollerWidget 为非泛型**

将 `FancyScrollerWidget.cs` 完整替换为：

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using YooAsset;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// 非泛型 ScrollView Widget，内嵌 <see cref="FancyScrollerController"/> 管理全部逻辑。
    /// <para>
    /// 子类只需 override <see cref="CreateCell"/> 提供 Cell 工厂，
    /// 可选 override CellInterval/ScrollOffset/Loop/OnCellClicked 等配置。
    /// </para>
    /// </summary>
    public abstract class FancyScrollerWidget : UIWidget
    {
        private FancyScrollerController _controller;
        private AssetHandle _cellPrefabHandle;
        private GameObject _cellPrefab;

        /// <summary>Cell 被点击时触发。</summary>
        public event Action<int> OnCellItemClicked;

        /// <summary>Cell 内按钮被点击时触发。</summary>
        public event Action<int> OnCellButtonClicked;

        /// <summary>共享 Context 对象（非泛型，使用时转型）。</summary>
        public object SharedContext => _controller?.SharedContext;

        #region 虚方法（子类可 override）

        /// <summary>创建 Cell 实例。子类必须实现。</summary>
        protected abstract IFancyCell CreateCell();

        /// <summary>Cell 间隔，默认 0.2。</summary>
        protected virtual float CellInterval => 0.2f;

        /// <summary>滚动偏移，默认 0.5。</summary>
        protected virtual float ScrollOffset => 0.5f;

        /// <summary>是否循环滚动，默认 false。</summary>
        protected virtual bool Loop => false;

        /// <summary>Cell 被点击时调用。</summary>
        protected virtual void OnCellClicked(int index) { }

        /// <summary>Cell 内按钮被点击时调用。</summary>
        protected virtual void OnCellButtonItemClicked(int index) { }

        #endregion

        #region 生命周期

        protected override void OnCreate()
        {
            base.OnCreate();

            // 自动发现 Scroller 和 cellContainer
            var scroller = GetComponentInChildren<Scroller>();
            if (scroller == null)
            {
                Log.Error("[FancyScrollerWidget] Scroller component not found in children.");
                return;
            }

            var cellContainer = scroller.transform.Find("m_cellContainer");
            if (cellContainer == null)
            {
                Log.Error("[FancyScrollerWidget] m_cellContainer not found under Scroller.");
                return;
            }

            // 创建并初始化 Controller
            _controller = new FancyScrollerController();
            _controller.Initialize(this, scroller, cellContainer, CreateCell);

            // 同步配置
            _controller.CellInterval = CellInterval;
            _controller.ScrollOffset = ScrollOffset;
            _controller.Loop = Loop;

            // 转发事件
            _controller.OnCellItemClicked += index =>
            {
                OnCellItemClicked?.Invoke(index);
                OnCellClicked(index);
            };
            _controller.OnCellButtonClicked += index =>
            {
                OnCellButtonClicked?.Invoke(index);
                OnCellButtonItemClicked(index);
            };
        }

        /// <summary>
        /// 异步加载 Cell Prefab。
        /// </summary>
        public async UniTask InitializeAsync(
            string cellAssetLocation,
            IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            await _controller.LoadCellPrefabAsync(cellAssetLocation, progress, ct);
        }

        /// <summary>
        /// 异步加载 Cell Prefab（别名）。
        /// </summary>
        public UniTask LoadCellPrefabAsync(
            string cellAssetLocation,
            IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            return InitializeAsync(cellAssetLocation, progress, ct);
        }

        protected override void OnDestroy()
        {
            _controller?.Dispose();
            _controller = null;
            OnCellItemClicked = null;
            OnCellButtonClicked = null;
            base.OnDestroy();
        }

        #endregion

        #region 公开 API

        /// <summary>更新列表数据。</summary>
        public void UpdateData(IList items) => _controller?.UpdateData(items);

        /// <summary>更新列表数据（泛型便捷方法）。</summary>
        public void UpdateData<T>(IList<T> items) => _controller?.UpdateData(items);

        /// <summary>滚动到指定索引。</summary>
        public void ScrollTo(int index, float duration = 0.3f) => _controller?.ScrollTo(index, duration);

        /// <summary>跳转到指定索引。</summary>
        public void JumpTo(int index) => _controller?.JumpTo(index);

        /// <summary>强制刷新。</summary>
        public void Refresh() => _controller?.Refresh();

        /// <summary>重新布局。</summary>
        public void Relayout() => _controller?.Relayout();

        /// <summary>获取居中索引。</summary>
        public int GetCenterIndex() => _controller?.GetCenterIndex() ?? 0;

        #endregion
    }
}
```

- [ ] **步骤 2：标记 FancyScrollViewWidget 为过时**

将 `FancyScrollViewWidget.cs` 内容替换为：

```csharp
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using YooAsset;

namespace GameLogic.UI.FancyScrollView
{
    /// <summary>
    /// [已过时] 功能已合并到 <see cref="FancyScrollerWidget"/>。
    /// 保留此文件仅为向后兼容过渡期。
    /// </summary>
    [Obsolete("Use FancyScrollerWidget instead. This class will be removed.")]
    public abstract class FancyScrollViewWidget<TItemData, TContext> : UIWidget
        where TContext : class, new()
    {
        [Obsolete("Use FancyScrollerWidget.InitializeAsync instead.")]
        public UniTask InitializeAsync(string cellAssetLocation,
            IProgress<float> progress = null, CancellationToken ct = default)
        {
            return UniTask.CompletedTask;
        }
    }
}
```

- [ ] **步骤 3：验证编译**

确认 Unity Console 无编译错误。

- [ ] **步骤 4：Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Adapters/FancyScrollerWidget.cs
git add Assets/GameScripts/HotFix/GameLogic/Module/UIModule/FancyScrollView/Adapters/FancyScrollViewWidget.cs
git commit -m "refactor: de-generify FancyScrollerWidget, absorb FancyScrollViewWidget"
```

---

## 任务 6：ScriptGenerator 集成

**文件：**
- 修改：`Assets/Editor/UIScriptGenerator/ScriptGenerateRuler.cs`
- 修改：`Assets/Editor/UIScriptGenerator/ScriptAutoGenerator.cs`
- 修改：`Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.cs`

- [ ] **步骤 1：ScriptGenerateRuler 新增 isCustomInit 字段**

在 `ScriptGenerateRuler` 类中添加：

```csharp
[SerializeField] public bool isCustomInit = false;
```

构造函数同步更新：

```csharp
public ScriptGenerateRuler(string uiElementRegex, UIComponentName componentName, bool isUIWidget = false, bool isCustomInit = false)
{
    this.uiElementRegex = uiElementRegex;
    this.componentName = componentName;
    this.isUIWidget = isUIWidget;
    this.isCustomInit = isCustomInit;
}
```

在 `ScriptGenerateRulerDrawer.OnGUI` 中添加显示列：

在 `isUIWidgetRect` 之后添加：

```csharp
var isCustomInitRect = new Rect(position.x + 480, position.y, 100, position.height);
EditorGUI.PropertyField(isCustomInitRect, property.FindPropertyRelative("isCustomInit"), GUIContent.none);
```

- [ ] **步骤 2：ScriptGeneratorSetting 更新默认规则**

找到 `scriptGenerateRule` 初始化列表中的 Scroller 规则行：

```csharp
new ScriptGenerateRuler("m_scroller", UIComponentName.FancyScroller),
```

替换为：

```csharp
new ScriptGenerateRuler("m_scroller", UIComponentName.FancyScroller, isCustomInit: true),
```

- [ ] **步骤 3：ScriptAutoGenerator 添加 Scroller 特殊路径**

在 `WriteAutoScript` 方法中，在 `if (rule == null)` 检查之后、`strVar.AppendLine(...)` 之前，添加 Scroller 特殊处理：

```csharp
            // ── Scroller → FancyScrollerController 特殊处理 ──
            if (rule.componentName == UIComponentName.FancyScroller)
            {
                // 生成 Controller 字段（非泛型）
                strVar.AppendLine($"\t\tprivate FancyScrollerController {varName}{(ScriptGeneratorSetting.Instance.NullableEnable ? " = null!;" : ";")}");

                // 生成初始化代码
                var childPath = GetRelativePath(root, child);
                strBind.AppendLine($"\t\t\t{varName} = new FancyScrollerController();");
                strBind.AppendLine($"\t\t\t{varName}.Initialize(this, ");
                strBind.AppendLine($"\t\t\t    m_bindComponent.GetComponent<Scroller>({m_bindIndex}), ");
                strBind.AppendLine($"\t\t\t    FindChild(\"{childPath}/m_cellContainer\"), ");
                strBind.AppendLine($"\t\t\t    null);");
                m_bindIndex++;
                return;
            }
```

注意：需要确认 `GetRelativePath` 或等效方法在当前上下文中可用。查看 `WriteAutoScript` 参数中 `root` 和 `child` 的来源，`child.name` 已是变量名。`childPath` 应为从 root 到 child 的相对路径。如果项目中没有 `GetRelativePath` 辅助方法，需要用 `child.name` 替代（因为 ScriptGenerator 通常在一级子节点遍历，路径就是节点名）：

```csharp
                strBind.AppendLine($"\t\t\t    FindChild(\"{child.name}/m_cellContainer\"), ");
```

- [ ] **步骤 4：ScriptAutoGenerator 添加 OnDestroy 生成**

在 `GenerateCSharpScript` 方法中，找到 `strFile.Append(strCallback.ToString())` 之后、`#endregion` 之前，添加：

```csharp
            // 生成 OnDestroy（如果有需要 Dispose 的字段）
            if (hasScrollerFields)
            {
                strFile.AppendLine();
                strFile.AppendLine("\t\tprotected override void OnDestroy()");
                strFile.AppendLine("\t\t{");
                foreach (var disposeField in scrollerFields)
                {
                    strFile.AppendLine($"\t\t\t{disposeField}?.Dispose();");
                    strFile.AppendLine($"\t\t\t{disposeField} = null;");
                }
                strFile.AppendLine("\t\t\tbase.OnDestroy();");
                strFile.AppendLine("\t\t}");
            }
```

需要在 `GenerateCSharpScript` 方法开头添加收集列表：

```csharp
            var scrollerFields = new List<string>();
```

在 WriteAutoScript 的 Scroller 分支中添加：

```csharp
                scrollerFields.Add(varName);
```

注意：由于 `scrollerFields` 是在 `GenerateCSharpScript` 中声明的局部变量，而 `WriteAutoScript` 是独立方法，需要将 `scrollerFields` 作为参数传入，或改用 ref 参数。最简单的方式是在 WriteAutoScript 签名中新增 `ref List<string> scrollerFields` 参数。

- [ ] **步骤 5：验证编译**

确认 Unity Console 无编译错误。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Editor/UIScriptGenerator/ScriptGenerateRuler.cs
git add Assets/Editor/UIScriptGenerator/ScriptAutoGenerator.cs
git add Assets/Editor/UIScriptGenerator/ScriptGeneratorSetting.cs
git commit -m "feat: ScriptGenerator auto-generate FancyScrollerController"
```

---

## 任务 7：迁移 Example01

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollViewExamples/Example01_Basic/View/Example01_BasicUI.cs`
- 修改：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollViewExamples/Example01_Basic/View/Example01_BasicUI_Gen.g.cs`

- [ ] **步骤 1：修改 Example01_BasicUI_Gen.g.cs**

替换 `_Gen.g.cs` 内容为 ScriptGenerator 新逻辑应生成的代码：

```csharp
//----------------------------------------------------------
// <auto-generated>
// -This code was generated.
// -Changes to this file may cause incorrect behavior.
// -will be lost if the code is regenerated.
// <auto-generated/>
//----------------------------------------------------------
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TEngine;
using GameLogic.UI.FancyScrollView;

namespace GameLogic
{
    public partial class Example01_BasicUI : UIWindow
    {
        #region 脚本工具生成的代码

        private UIBindComponent m_bindComponent;
        private FancyScrollerController m_scroller = null!;
        private Button m_btnRandom = null!;
        private Text m_textRandom = null!;

        protected override void ScriptGenerator()
        {
            m_bindComponent = gameObject.GetComponent<UIBindComponent>();
            if(m_bindComponent == null)
            {
                Log.Error($"根物体: {gameObject.name} 缺少组件 UIBindComponent, 请检查！！！");
                return;
            }
            m_scroller = new FancyScrollerController();
            m_scroller.Initialize(this,
                m_bindComponent.GetComponent<Scroller>(0),
                FindChild("m_scroller/m_cellContainer"),
                null);
            m_btnRandom = m_bindComponent.GetComponent<Button>(1);
            m_textRandom = m_bindComponent.GetComponent<Text>(2);
            m_btnRandom.onClick.RemoveAllListeners();
            m_btnRandom.onClick.AddListener(OnClickRandomBtn);
        }

        protected override void OnDestroy()
        {
            m_scroller?.Dispose();
            m_scroller = null;
            base.OnDestroy();
        }

        #endregion

        #region 事件

        private partial void OnClickRandomBtn();

        #endregion
    }
}
```

- [ ] **步骤 2：简化 Example01_BasicUI.cs**

替换用户代码为：

```csharp
using System.Collections.Generic;
using GameLogic.DataBinding;
using GameLogic.UI.FancyScrollView;
using TEngine;
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    /// <summary>
    /// Example01 基础列表示例（MVE 四层架构 + 非泛型 FancyScrollerController）。
    /// </summary>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example01_BasicDataContext))]
    public partial class Example01_BasicUI : UIWindow
    {
        private List<Example01_BasicItemData> _items;

        #region 事件

        private partial void OnClickRandomBtn()
        {
            int centerIndex = m_scroller.GetCenterIndex();
            Singleton<Example01_BasicService>.Instance.RandomizeNeighborIds(centerIndex);
        }

        #endregion

        protected override void SetupBindings()
        {
            var model = Singleton<Example01_BasicModel>.Instance;
            for (int i = 0; i < Example01_BasicModel.ItemCount; i++)
            {
                int idx = i;
                Bind(model.ItemIds[idx], _ => RefreshList());
            }
        }

        protected override async void OnCreate()
        {
            m_scroller.SetCellFactory(() => new Example01_BasicCell());
            await m_scroller.LoadCellPrefabAsync("Example01_BasicCell");
            _items = Singleton<Example01_BasicService>.Instance.BuildItems();
            m_scroller.UpdateData(_items);
        }

        private void RefreshList()
        {
            if (m_scroller == null) return;
            _items = Singleton<Example01_BasicService>.Instance.BuildItems();
            m_scroller.UpdateData(_items);
        }
    }
}
```

- [ ] **步骤 3：修改 Example01_BasicItemData 引用**

检查 `Example01_BasicItemData.cs`，确认它没有被改为非泛型相关的东西（它本身不需要变，因为它只是数据类）。

- [ ] **步骤 4：验证编译 + Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollViewExamples/Example01_Basic/View/
git commit -m "refactor: migrate Example01 to non-generic FancyScrollerController"
```

---

## 任务 8：迁移 Example02

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollViewExamples/Example02_FocusOn/Example02_FocusOnScrollViewWidget.cs`

- [ ] **步骤 1：重写 Example02_FocusOnScrollViewWidget**

```csharp
using GameLogic.UI.FancyScrollView;

namespace GameLogic
{
    /// <summary>
    /// Example02 聚焦 ScrollView Widget（非泛型）。
    /// </summary>
    public class Example02_FocusOnScrollViewWidget : FancyScrollerWidget
    {
        private Example02_FocusOnContext Ctx => (Example02_FocusOnContext)SharedContext;

        protected override IFancyCell CreateCell() => new Example02_FocusOnCell();

        protected override void OnCellClicked(int index)
        {
            Ctx.SelectedIndex = index;
            ScrollTo(index, 0.3f);
            Refresh();
        }
    }
}
```

- [ ] **步骤 2：验证编译 + Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollViewExamples/Example02_FocusOn/Example02_FocusOnScrollViewWidget.cs
git commit -m "refactor: migrate Example02 to non-generic FancyScrollerWidget"
```

---

## 任务 9：迁移 Example03

**文件：**
- 修改：`Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollViewExamples/Example03_InfiniteScroll/Example03_InfiniteScrollViewWidget.cs`

- [ ] **步骤 1：重写 Example03_InfiniteScrollViewWidget**

```csharp
using GameLogic.UI.FancyScrollView;

namespace GameLogic
{
    /// <summary>
    /// Example03 无限循环 ScrollView Widget（非泛型）。
    /// </summary>
    public class Example03_InfiniteScrollViewWidget : FancyScrollerWidget
    {
        protected override float CellInterval => 0.2f;
        protected override float ScrollOffset => 0.5f;
        protected override bool Loop => true;

        protected override IFancyCell CreateCell() => new Example03_InfiniteScrollCell();
    }
}
```

- [ ] **步骤 2：验证编译 + Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/UI/FancyScrollViewExamples/Example03_InfiniteScroll/Example03_InfiniteScrollViewWidget.cs
git commit -m "refactor: migrate Example03 to non-generic FancyScrollerWidget"
```

---

## 任务 10：端到端验证 + 清理

**文件：**
- 无新增修改

- [ ] **步骤 1：Example01 验证**

在 Unity PlayMode 中：
1. 打开 Example01_BasicUI
2. 确认列表渲染正常
3. 点击 [Random] 按钮，确认数据刷新
4. 关闭 UI，确认 Console 无报错

- [ ] **步骤 2：Example02 验证**

1. 打开 Example02 UI
2. 点击 Cell，确认聚焦滚动生效
3. 关闭 UI，无报错

- [ ] **步骤 3：Example03 验证**

1. 打开 Example03 UI
2. 滚动列表，确认无限循环生效
3. 关闭 UI，无报错

- [ ] **步骤 4：ScriptGenerator 重新生成测试**

1. 在 Unity Hierarchy 中选中 Example01_BasicUI Prefab 根节点
2. 运行 `GameObject > ScriptGenerator > UIPropertyBindComponent`
3. 确认重新生成的 `_Gen.g.cs` 包含 `FancyScrollerController` 字段
4. 确认编译通过

- [ ] **步骤 5：最终 Commit**

```bash
git add -A
git commit -m "chore: scroller unification complete - verify all examples"
```
