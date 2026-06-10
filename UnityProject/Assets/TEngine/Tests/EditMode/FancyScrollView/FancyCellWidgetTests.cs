using GameLogic;
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyCellWidgetTests
    {
        #region Test Helpers

        private sealed class TestContext { }

        /// <summary>
        /// 用于测试的 UIBase 子类，手动设置 gameObject/transform/rectTransform。
        /// 这些属性在 UIBase 中声明为 virtual ... { protected set; get; }，可直接赋值。
        /// </summary>
        private sealed class TestParentUI : UIBase
        {
            public TestParentUI(GameObject root)
            {
                gameObject = root;
                transform = root.transform;
                rectTransform = root.transform as RectTransform;
            }
        }

        private sealed class TestCellWidget : FancyCellWidget<string, TestContext>
        {
            public string LastData { get; private set; }
            public float LastPosition { get; private set; }
            public int VisibleCount { get; private set; }
            public int InvisibleCount { get; private set; }

            public void RaiseClick() => NotifyItemClicked();
            public void RaiseButton() => NotifyButtonClicked();

            /// <summary>
            /// Expose CreateProperty for testing (base method is protected).
            /// </summary>
            public GameLogic.DataBinding.BindableProperty<T> CreatePropertyForTest<T>(T initial = default)
                => CreateProperty(initial);

            protected override void BindData(string data)
            {
                LastData = data;
            }

            public override void UpdatePosition(float normalizedPosition)
            {
                LastPosition = normalizedPosition;
            }

            protected override void OnCellVisible()
            {
                VisibleCount++;
            }

            protected override void OnCellInvisible()
            {
                InvisibleCount++;
            }
        }

        #endregion

        [Test]
        public void FancyCellWidget_ImplementsIFancyCellLifecycleAndEvents()
        {
            // Arrange
            var parentGo = new GameObject("parent", typeof(RectTransform));
            var parent = new TestParentUI(parentGo);
            var cellGo = new GameObject("cell", typeof(RectTransform));
            var cell = new TestCellWidget();

            // Initialize via Create (same as UIWidget.Create)
            Assert.IsTrue(cell.Create(parent, cellGo));

            var clickedIndex = -1;
            var buttonIndex = -1;
            cell.Index = 5;
            cell.OnItemClicked += i => clickedIndex = i;
            cell.OnButtonClicked += i => buttonIndex = i;

            // Act - lifecycle
            cell.SetContext(new TestContext());
            cell.UpdateContent("Potion");
            cell.UpdatePosition(0.75f);

            // Act - visibility
            cell.SetVisible(false);
            cell.SetVisible(true);

            // Act - events
            cell.RaiseClick();
            cell.RaiseButton();

            // Assert
            Assert.AreEqual("Potion", cell.LastData);
            Assert.AreEqual(0.75f, cell.LastPosition);
            Assert.AreEqual(1, cell.InvisibleCount);
            Assert.AreEqual(1, cell.VisibleCount);
            Assert.AreEqual(5, clickedIndex);
            Assert.AreEqual(5, buttonIndex);

            // Cleanup
            cell.CallDestroy();
            Object.DestroyImmediate(cellGo);
            Object.DestroyImmediate(parentGo);
        }

        [Test]
        public void FancyCellWidget_CreateProperty_DisposesOnDestroy()
        {
            var parentGo = new GameObject("parent", typeof(RectTransform));
            var parent = new TestParentUI(parentGo);
            var cellGo = new GameObject("cell", typeof(RectTransform));
            var cell = new TestCellWidget();
            cell.Create(parent, cellGo);

            // Create a BindableProperty via test helper
            var prop = cell.CreatePropertyForTest(42);
            Assert.IsNotNull(prop);
            Assert.AreEqual(42, prop.Value);

            // Destroy should not throw
            cell.CallDestroy();
            Object.DestroyImmediate(cellGo);
            Object.DestroyImmediate(parentGo);
        }

        [Test]
        public void FancyCellWidget_SetVisible_SameState_NoOp()
        {
            var parentGo = new GameObject("parent", typeof(RectTransform));
            var parent = new TestParentUI(parentGo);
            var cellGo = new GameObject("cell", typeof(RectTransform));
            var cell = new TestCellWidget();
            cell.Create(parent, cellGo);

            // Visible by default after Create
            Assert.IsTrue(cell.IsVisible);

            // SetVisible(true) on already-visible should be no-op
            cell.SetVisible(true);
            Assert.AreEqual(0, cell.VisibleCount);
            Assert.AreEqual(0, cell.InvisibleCount);

            cell.CallDestroy();
            Object.DestroyImmediate(cellGo);
            Object.DestroyImmediate(parentGo);
        }

        [Test]
        public void FancyCellWidget_UnloadContent_ResetsToSkeleton()
        {
            var parentGo = new GameObject("parent", typeof(RectTransform));
            var parent = new TestParentUI(parentGo);
            var cellGo = new GameObject("cell", typeof(RectTransform));
            var cell = new TestCellWidget();
            cell.Create(parent, cellGo);

            Assert.AreEqual(ContentLoadState.Skeleton, cell.LoadState);

            cell.UnloadContent();
            Assert.AreEqual(ContentLoadState.Skeleton, cell.LoadState);

            cell.CallDestroy();
            Object.DestroyImmediate(cellGo);
            Object.DestroyImmediate(parentGo);
        }

        [Test]
        public void FancyCellWidget_ImplementsIFancyCellInterface()
        {
            var parentGo = new GameObject("parent", typeof(RectTransform));
            var parent = new TestParentUI(parentGo);
            var cellGo = new GameObject("cell", typeof(RectTransform));
            var cell = new TestCellWidget();
            cell.Create(parent, cellGo);

            // Verify interface implementation
            IFancyCell<string, TestContext> iface = cell;
            Assert.IsNotNull(iface);
            Assert.IsInstanceOf<IFancyCell<string, TestContext>>(cell);

            // Interface members work
            iface.Index = 10;
            Assert.AreEqual(10, cell.Index);

            cell.CallDestroy();
            Object.DestroyImmediate(cellGo);
            Object.DestroyImmediate(parentGo);
        }
    }
}
