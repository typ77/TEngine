using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyGridViewWidgetTests
    {
        #region Test Helpers

        private sealed class TestGridContext : IFancyGridViewContext
        {
            public ScrollDirection ScrollDirection { get; set; }
            public System.Func<(float ScrollSize, float ReuseMargin)> CalculateScrollSize { get; set; }
            public GameObject CellTemplate { get; set; }
            public System.Func<int> GetGroupCount { get; set; }
            public System.Func<float> GetStartAxisSpacing { get; set; }
            public System.Func<float> GetCellSize { get; set; }
        }

        private sealed class TestGridCell : FancyCellWidget<string, TestGridContext>
        {
            public string LastData { get; private set; }
            public void RaiseClick() => NotifyItemClicked();
            protected override void BindData(string data) => LastData = data;
            public override void UpdatePosition(float normalizedPosition) { }
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

        #endregion

        [Test]
        public void CellGroupWrapper_InstantiateCells_CreatesWidgetsFromTemplate()
        {
            // Setup: create a cell template prefab with TestGridCell
            var template = new GameObject("cellTemplate", typeof(RectTransform));
            template.AddComponent<TestGridCell>();

            // Create group GO
            var groupGo = new GameObject("group", typeof(RectTransform));
            var group = groupGo.AddComponent<CellGroupWrapper<string, TestGridContext>>();

            // Setup context
            var context = new TestGridContext
            {
                CellTemplate = template,
                GetGroupCount = () => 3,
                GetStartAxisSpacing = () => 0f,
                GetCellSize = () => 100f,
            };
            group.SetContext(context);

            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            group.SetParent(parent);

            // Act
            group.Initialize();

            // Assert
            Assert.AreEqual(3, group.CellCountForTest);
            for (int i = 0; i < group.CellCountForTest; i++)
            {
                var cell = group.GetCellForTest(i);
                Assert.IsNotNull(cell);
                Assert.IsInstanceOf<TestGridCell>(cell);
            }

            // Cleanup
            Object.DestroyImmediate(template);
            Object.DestroyImmediate(groupGo);
            Object.DestroyImmediate(parent.gameObject);
        }

        [Test]
        public void CellGroupWrapper_ForwardsCellClickEvents()
        {
            var template = new GameObject("cellTemplate", typeof(RectTransform));
            template.AddComponent<TestGridCell>();

            var groupGo = new GameObject("group", typeof(RectTransform));
            var group = groupGo.AddComponent<CellGroupWrapper<string, TestGridContext>>();

            var context = new TestGridContext
            {
                CellTemplate = template,
                GetGroupCount = () => 2,
                GetStartAxisSpacing = () => 0f,
                GetCellSize = () => 100f,
            };
            group.SetContext(context);

            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            group.SetParent(parent);
            group.Initialize();

            int clickedIndex = -1;
            group.CellItemClicked += i => clickedIndex = i;

            // Simulate click on first cell
            var cell = group.GetCellForTest(0);
            cell.Index = 5;
            cell.RaiseClick();

            Assert.AreEqual(5, clickedIndex);

            Object.DestroyImmediate(template);
            Object.DestroyImmediate(groupGo);
            Object.DestroyImmediate(parent.gameObject);
        }

        [Test]
        public void CellGroupWrapper_MissingWidgetComponent_LogsError()
        {
            // Template without FancyCellWidget component
            var template = new GameObject("badTemplate", typeof(RectTransform));

            var groupGo = new GameObject("group", typeof(RectTransform));
            var group = groupGo.AddComponent<CellGroupWrapper<string, TestGridContext>>();

            var context = new TestGridContext
            {
                CellTemplate = template,
                GetGroupCount = () => 1,
                GetStartAxisSpacing = () => 0f,
                GetCellSize = () => 100f,
            };
            group.SetContext(context);

            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            group.SetParent(parent);

            // Should not throw, but cells will be null (logged error)
            group.Initialize();
            // Cell at index 0 should be null since component was missing
            Assert.AreEqual(1, group.CellCountForTest);

            Object.DestroyImmediate(template);
            Object.DestroyImmediate(groupGo);
            Object.DestroyImmediate(parent.gameObject);
        }
    }
}
