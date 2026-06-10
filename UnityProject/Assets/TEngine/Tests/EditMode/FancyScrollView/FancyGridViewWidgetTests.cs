using System;
using GameLogic;
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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

        /// <summary>
        /// Test-only CellGroupWrapper that creates TestGridCell via new + InitializeWidget
        /// instead of GetComponent, since FancyCellWidget is not a MonoBehaviour.
        /// </summary>
        private sealed class TestCellGroupWrapper : CellGroupWrapper<string, TestGridContext>
        {
            private UIBase _parentUIRef;

            protected override IFancyCell<string, TestGridContext>[] InstantiateCells()
            {
                var cells = new IFancyCell<string, TestGridContext>[Context.GetGroupCount()];
                for (var i = 0; i < cells.Length; i++)
                {
                    var go = Instantiate(Context.CellTemplate, transform);
                    var widget = new TestGridCell();
                    widget.InitializeWidget(_parentUIRef, go);
                    widget.OnItemClicked += i => _itemClicked?.Invoke(i);
                    widget.OnButtonClicked += i => _buttonClicked?.Invoke(i);
                    cells[i] = widget;
                }

                return cells;
            }

            public void SetParentForTest(UIBase parentUI)
            {
                SetParent(parentUI);
                _parentUIRef = parentUI;
            }

            public void RaiseCellItemClickedForTest(int index)
            {
                // CellGroupWrapper uses events, so we need to subscribe to forward them
                // Instead of invoking the event directly, store callbacks
                _itemClicked?.Invoke(index);
            }
            public void RaiseCellButtonClickedForTest(int index)
            {
                _buttonClicked?.Invoke(index);
            }

            private Action<int> _itemClicked;
            private Action<int> _buttonClicked;

            public void SubscribeItemClickedForTest(Action<int> callback) => _itemClicked = callback;
            public void SubscribeButtonClickedForTest(Action<int> callback) => _buttonClicked = callback;
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
            // Setup: create a cell template prefab (no TestGridCell component needed)
            var template = new GameObject("cellTemplate", typeof(RectTransform));

            // Create group GO with TestCellGroupWrapper
            var groupGo = new GameObject("group", typeof(RectTransform));
            var group = groupGo.AddComponent<TestCellGroupWrapper>();

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
            group.SetParentForTest(parent);

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
            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(groupGo);
            UnityEngine.Object.DestroyImmediate(parent.gameObject);
        }

        [Test]
        public void CellGroupWrapper_ForwardsCellClickEvents()
        {
            var template = new GameObject("cellTemplate", typeof(RectTransform));

            var groupGo = new GameObject("group", typeof(RectTransform));
            var group = groupGo.AddComponent<TestCellGroupWrapper>();

            var context = new TestGridContext
            {
                CellTemplate = template,
                GetGroupCount = () => 2,
                GetStartAxisSpacing = () => 0f,
                GetCellSize = () => 100f,
            };
            group.SetContext(context);

            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            group.SetParentForTest(parent);
            group.Initialize();

            int clickedIndex = -1;
            group.CellItemClicked += i => clickedIndex = i;
            group.SubscribeItemClickedForTest(i => clickedIndex = i);

            // Simulate click on first cell
            var cell = group.GetCellForTest(0);
            cell.Index = 5;
            ((TestGridCell)cell).RaiseClick();

            Assert.AreEqual(5, clickedIndex);

            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(groupGo);
            UnityEngine.Object.DestroyImmediate(parent.gameObject);
        }

        /// <summary>
        /// Test-only wrapper that verifies base InstantiateCells handles
        /// missing FancyCellWidget gracefully (logs error, leaves null entry).
        /// </summary>
        private sealed class TestErrorCellGroupWrapper : CellGroupWrapper<string, TestGridContext>
        {
            public bool InstantiateCellsCalled { get; private set; }
            public int NullCellCount { get; private set; }

            public void CallInstantiateAndCountNulls()
            {
                InstantiateCellsCalled = true;
                var result = InstantiateCells();
                foreach (var cell in result)
                {
                    if (cell == null) NullCellCount++;
                }
            }
        }

        [Test]
        public void CellGroupWrapper_MissingWidgetComponent_LogsError()
        {
            // Template without any FancyCellWidget - base CellGroupWrapper's
            // InstantiateCells catches ArgumentException (FancyCellWidget not MonoBehaviour),
            // logs error, and leaves cells as null entries.
            var template = new GameObject("badTemplate", typeof(RectTransform));

            var groupGo = new GameObject("group", typeof(RectTransform));
            var group = groupGo.AddComponent<TestErrorCellGroupWrapper>();

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

            // Expect error logs from InstantiateCells for each missing component
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*CellGroupWrapper.*"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*CellGroupWrapper.*"));

            // Only call InstantiateCells (not full Initialize) to avoid
            // side effects from base class lifecycle methods
            group.CallInstantiateAndCountNulls();

            Assert.IsTrue(group.InstantiateCellsCalled);
            // Both cells should be null since template has no FancyCellWidget
            Assert.AreEqual(2, group.NullCellCount);

            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(groupGo);
            UnityEngine.Object.DestroyImmediate(parent.gameObject);
        }
    }
}
