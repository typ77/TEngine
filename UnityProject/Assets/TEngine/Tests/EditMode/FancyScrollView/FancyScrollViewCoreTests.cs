using GameLogic;
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyScrollViewCoreTests
    {
        #region Task 2: IFancyCell / FancyCell / NullContext

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

        #endregion

        #region Task 3: FancyScrollView virtual CreateCell and IFancyCell pool

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

        #endregion

        #region Task 4: FancyScrollRect serialized CellSize

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

        #endregion

        #region Task 5: FancyCellGroup exposes Cells to derived adapters

        private sealed class TestGridContext : IFancyGridViewContext
        {
            public ScrollDirection ScrollDirection { get; set; }
            public System.Func<(float ScrollSize, float ReuseMargin)> CalculateScrollSize { get; set; }
            public GameObject CellTemplate { get; set; }
            public System.Func<int> GetGroupCount { get; set; }
            public System.Func<float> GetStartAxisSpacing { get; set; }
            public System.Func<float> GetCellSize { get; set; }
        }

        private sealed class TestCellGroup : FancyCellGroup<string, TestGridContext>
        {
            public IFancyCell<string, TestGridContext>[] ExposedCells => Cells;
            public int InstantiateCount { get; private set; }

            protected override IFancyCell<string, TestGridContext>[] InstantiateCells()
            {
                InstantiateCount++;
                return new IFancyCell<string, TestGridContext>[Context.GetGroupCount()];
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

        #endregion
    }
}
