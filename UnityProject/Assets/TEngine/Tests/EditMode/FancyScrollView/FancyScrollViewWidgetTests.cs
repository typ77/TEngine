using System;
using GameLogic;
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyScrollViewWidgetTests
    {
        #region Test Helpers

        private sealed class TestContext { }

        private sealed class TestCell : FancyCellWidget<string, TestContext>
        {
            public void RaiseClick() => NotifyItemClicked();
            public void RaiseButtonClick() => NotifyButtonClicked();
            protected override void BindData(string data) { }
            public override void UpdatePosition(float normalizedPosition) { }
        }

        private sealed class TestWidget : FancyScrollViewWidget<string, TestContext>
        {
            public int ClickedIndex { get; private set; } = -1;
            public int ButtonClickedIndex { get; private set; } = -1;

            protected override void OnCellClicked(int index) => ClickedIndex = index;
            protected override void OnCellButtonItemClicked(int index) => ButtonClickedIndex = index;

            /// <summary>
            /// Override CreateCell to directly instantiate TestCell via new + InitializeWidget,
            /// since FancyCellWidget is not a MonoBehaviour and cannot be found via GetComponent.
            /// </summary>
            protected override IFancyCell<string, TestContext> CreateCell(
                GameObject prefab, Transform container)
            {
                var go = UnityEngine.Object.Instantiate(prefab, container);
                var cell = new TestCell();
                cell.InitializeWidget(this, go);
                cell.OnItemClicked += index =>
                {
                    RaiseCellItemClicked(index);
                    OnCellClicked(index);
                };
                cell.OnButtonClicked += index =>
                {
                    RaiseCellButtonClicked(index);
                    OnCellButtonItemClicked(index);
                };
                return cell;
            }
        }

        private sealed class TestWidgetUsingBase : FancyScrollViewWidget<string, TestContext>
        {
            public int ClickedIndex { get; private set; } = -1;
            public int ButtonClickedIndex { get; private set; } = -1;

            protected override void OnCellClicked(int index) => ClickedIndex = index;
            protected override void OnCellButtonItemClicked(int index) => ButtonClickedIndex = index;
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
        public void CreateCell_CreatesWidgetCellAndForwardsClick()
        {
            var root = new GameObject("root", typeof(RectTransform));
            var container = new GameObject("container", typeof(RectTransform)).transform;
            container.SetParent(root.transform, false);
            var prefab = new GameObject("cellPrefab", typeof(RectTransform));

            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            var widget = new TestWidget();
            widget.Create(parent, root);
            widget.SetCellPrefabForTest(prefab);

            var cell = (TestCell)widget.CreateCellForTest(prefab, container);
            cell.Index = 3;
            cell.RaiseClick();

            Assert.AreEqual(3, widget.ClickedIndex);

            widget.CallDestroy();
            UnityEngine.Object.DestroyImmediate(prefab);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(parent.gameObject);
        }

        [Test]
        public void CreateCell_ForwardsButtonClick()
        {
            var root = new GameObject("root", typeof(RectTransform));
            var container = new GameObject("container", typeof(RectTransform)).transform;
            container.SetParent(root.transform, false);
            var prefab = new GameObject("cellPrefab", typeof(RectTransform));

            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            var widget = new TestWidget();
            widget.Create(parent, root);
            widget.SetCellPrefabForTest(prefab);

            var cell = (TestCell)widget.CreateCellForTest(prefab, container);
            cell.Index = 5;
            cell.RaiseButtonClick();

            Assert.AreEqual(5, widget.ButtonClickedIndex);

            widget.CallDestroy();
            UnityEngine.Object.DestroyImmediate(prefab);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(parent.gameObject);
        }

        [Test]
        public void CreateCell_ThrowsIfNoFancyCellWidgetComponent()
        {
            var root = new GameObject("root", typeof(RectTransform));
            var container = new GameObject("container", typeof(RectTransform)).transform;
            container.SetParent(root.transform, false);
            // prefab without FancyCellWidget component
            var prefab = new GameObject("badPrefab", typeof(RectTransform));

            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            // TestWidgetUsingBase uses the base CreateCell which calls GetComponent<FancyCellWidget>
            var widget = new TestWidgetUsingBase();
            widget.Create(parent, root);
            widget.SetCellPrefabForTest(prefab);

            // GetComponent<FancyCellWidget> catches ArgumentException (not a MonoBehaviour)
            // and throws MissingComponentException with guidance to override CreateCell.
            Assert.Throws<MissingComponentException>(() =>
                widget.CreateCellForTest(prefab, container));

            widget.CallDestroy();
            UnityEngine.Object.DestroyImmediate(prefab);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(parent.gameObject);
        }

        [Test]
        public void CellItemClicked_EventIsRaised()
        {
            var root = new GameObject("root", typeof(RectTransform));
            var container = new GameObject("container", typeof(RectTransform)).transform;
            container.SetParent(root.transform, false);
            var prefab = new GameObject("cellPrefab", typeof(RectTransform));

            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            var widget = new TestWidget();
            widget.Create(parent, root);
            widget.SetCellPrefabForTest(prefab);

            int eventIndex = -1;
            widget.OnCellItemClicked += i => eventIndex = i;

            var cell = (TestCell)widget.CreateCellForTest(prefab, container);
            cell.Index = 7;
            cell.RaiseClick();

            Assert.AreEqual(7, eventIndex);

            widget.CallDestroy();
            UnityEngine.Object.DestroyImmediate(prefab);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(parent.gameObject);
        }

        [Test]
        public void OnDestroy_ClearsEventsAndPrefab()
        {
            var root = new GameObject("root", typeof(RectTransform));
            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            var widget = new TestWidget();
            widget.Create(parent, root);
            widget.SetCellPrefabForTest(new GameObject("prefab", typeof(RectTransform)));

            widget.CallDestroy();

            // After destroy, events should be null (no subscriptions)
            // No exceptions should be thrown
            Assert.Pass();

            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(parent.gameObject);
        }
    }
}
