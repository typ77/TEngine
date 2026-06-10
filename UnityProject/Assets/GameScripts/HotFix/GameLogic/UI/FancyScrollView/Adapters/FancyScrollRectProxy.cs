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
