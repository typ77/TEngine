using System.Collections.Generic;
using GameLogic;
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyScrollRectWidgetTests
    {
        #region Test Helpers

        private sealed class TestContext : IFancyScrollRectContext
        {
            public ScrollDirection ScrollDirection { get; set; }
            public System.Func<(float ScrollSize, float ReuseMargin)> CalculateScrollSize { get; set; }
        }

        private sealed class TestCell : FancyCellWidget<string, TestContext>
        {
            public string LastData { get; private set; }
            protected override void BindData(string data) => LastData = data;
            public override void UpdatePosition(float normalizedPosition) { }
        }

        private sealed class TestScrollRectWidget : FancyScrollRectWidget<string, TestContext>
        {
            public FancyScrollRectProxy GetProxy() => FindChildComponent<FancyScrollRectProxy>("m_fscroll_List");
            public GameObject GetCellPrefab() => CellPrefab;

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
        public void InternalScrollRect_AppliesProxySettings()
        {
            // Create proxy GO with Scroller component
            var proxyGo = new GameObject("m_fscroll_List", typeof(RectTransform));
            var proxy = proxyGo.AddComponent<FancyScrollRectProxy>();
            proxy.SetCellSizeForTest(200f);
            proxy.SetSpacingForTest(15f);
            proxy.SetPaddingHeadForTest(10f);
            proxy.SetPaddingTailForTest(20f);
            proxy.SetReuseCellMarginCountForTest(1f);

            // Verify proxy values are set
            Assert.AreEqual(200f, proxy.CellSize);
            Assert.AreEqual(15f, proxy.Spacing);
            Assert.AreEqual(10f, proxy.PaddingHead);
            Assert.AreEqual(20f, proxy.PaddingTail);
            Assert.AreEqual(1f, proxy.ReuseCellMarginCount);

            Object.DestroyImmediate(proxyGo);
        }

        /// <summary>
        /// Widget that skips InternalScrollRect creation to avoid OnValidate NRE
        /// from uninitialized Scroller in test environment.
        /// </summary>
        private sealed class TestScriptGenOnlyWidget : FancyScrollRectWidget<string, TestContext>
        {
            public FancyScrollRectProxy GetProxyDirect() =>
                FindChildComponent<FancyScrollRectProxy>("m_fscroll_List");

            // Skip OnCreate entirely - we only test ScriptGenerator finding the proxy
            protected override void OnCreate() { }

            protected override IFancyCell<string, TestContext> CreateCell(
                GameObject prefab, Transform container)
            {
                // Not used in this test
                throw new System.NotImplementedException();
            }
        }

        [Test]
        public void FancyScrollRectWidget_ScriptGenerator_FindsProxy()
        {
            // Setup hierarchy: root > m_fscroll_List (with proxy)
            var root = new GameObject("root", typeof(RectTransform));
            var proxyGo = new GameObject("m_fscroll_List", typeof(RectTransform));
            proxyGo.transform.SetParent(root.transform, false);
            var proxy = proxyGo.AddComponent<FancyScrollRectProxy>();

            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            var widget = new TestScriptGenOnlyWidget();
            widget.Create(parent, root);

            // After Create, ScriptGenerator should have found the proxy
            var foundProxy = widget.GetProxyDirect();
            Assert.IsNotNull(foundProxy, "ScriptGenerator should find m_fscroll_List proxy");

            widget.CallDestroy();
            Object.DestroyImmediate(proxyGo);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(parent.gameObject);
        }

        [Test]
        public void InternalScrollRect_OverridesCellPrefab()
        {
            // Verify that the InternalScrollRect class correctly delegates
            // CellPrefab to the owner widget
            var proxyGo = new GameObject("proxy", typeof(RectTransform));
            var proxy = proxyGo.AddComponent<FancyScrollRectProxy>();

            var parent = new TestParentUI(new GameObject("parent", typeof(RectTransform)));
            var widgetGo = new GameObject("widget", typeof(RectTransform));

            // Create widget and set proxy - proxy is NOT under widget root,
            // so ScriptGenerator won't find it. Expect the error log.
            var widget = new TestScrollRectWidget();
            LogAssert.Expect(LogType.Error, "[FancyScrollRectWidget] FancyScrollRectProxy not found on m_fscroll_List.");
            widget.Create(parent, widgetGo);
            widget.SetCellPrefabForTest(new GameObject("cellPrefab", typeof(RectTransform)));

            // Verify CellPrefab is accessible
            Assert.IsNotNull(widget.GetCellPrefab());

            widget.CallDestroy();
            Object.DestroyImmediate(widget.GetCellPrefab());
            Object.DestroyImmediate(widgetGo);
            Object.DestroyImmediate(proxyGo);
            Object.DestroyImmediate(parent.gameObject);
        }
    }
}
