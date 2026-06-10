using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

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
