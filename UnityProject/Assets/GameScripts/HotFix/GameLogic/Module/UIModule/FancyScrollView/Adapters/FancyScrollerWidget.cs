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

            var scroller = gameObject.GetComponentInChildren<Scroller>();
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

            _controller = new FancyScrollerController();
            _controller.Initialize(this, scroller, cellContainer, CreateCell);

            _controller.CellInterval = CellInterval;
            _controller.ScrollOffset = ScrollOffset;
            _controller.Loop = Loop;

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

        public async UniTask InitializeAsync(
            string cellAssetLocation,
            IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            await _controller.LoadCellPrefabAsync(cellAssetLocation, progress, ct);
        }

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

        public void UpdateData(IList items) => _controller?.UpdateData(items);
        public void ScrollTo(int index, float duration = 0.3f) => _controller?.ScrollTo(index, duration);
        public void JumpTo(int index) => _controller?.JumpTo(index);
        public void Refresh() => _controller?.Refresh();
        public void Relayout() => _controller?.Relayout();
        public int GetCenterIndex() => _controller?.GetCenterIndex() ?? 0;

        #endregion
    }
}
