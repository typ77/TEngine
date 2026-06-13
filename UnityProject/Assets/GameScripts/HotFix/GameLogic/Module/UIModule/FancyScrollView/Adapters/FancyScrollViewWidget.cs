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
