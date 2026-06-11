/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

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
