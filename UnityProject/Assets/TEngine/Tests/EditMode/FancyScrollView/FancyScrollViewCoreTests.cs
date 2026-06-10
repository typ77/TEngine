using System.Collections.Generic;
using GameLogic;
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
            public readonly List<TestCell> AllCreatedCells = new List<TestCell>();

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
                var cell = go.GetComponent<TestCell>();
                AllCreatedCells.Add(cell);
                return cell;
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
            // Verify at least one cell received content "A" and is visible
            // (pool uses circular indexing, so the cell with "A" may not be the last created)
            var cellWithA = view.AllCreatedCells.Find(c => c.LastContent == "A");
            Assert.IsNotNull(cellWithA, "At least one cell should have received content 'A'");
            Assert.IsTrue(cellWithA.IsVisible, "The cell with content 'A' should be visible");

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
            // FancyScrollRect.OnValidate fires on AddComponent in editor,
            // accessing Scroller.ViewportSize which NREs without a configured viewport.
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex(".*NullReferenceException.*"));
            var go = new GameObject("scrollRect", typeof(RectTransform), typeof(Scroller));
            var rect = go.AddComponent<TestScrollRect>();
            rect.enabled = false;

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
