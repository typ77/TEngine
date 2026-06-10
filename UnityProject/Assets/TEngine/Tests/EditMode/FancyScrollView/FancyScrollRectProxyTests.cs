using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class FancyScrollRectProxyTests
    {
        [Test]
        public void Proxy_StoresInspectorConfigurationAndRuntimePrefab()
        {
            var go = new GameObject("m_fscroll_List", typeof(RectTransform));
            var proxy = go.AddComponent<FancyScrollRectProxy>();
            var prefab = new GameObject("CellPrefab", typeof(RectTransform));

            proxy.SetCellSizeForTest(128f);
            proxy.SetSpacingForTest(8f);
            proxy.CellPrefab = prefab;

            Assert.AreEqual(128f, proxy.CellSize);
            Assert.AreEqual(8f, proxy.Spacing);
            Assert.AreSame(prefab, proxy.CellPrefab);

            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(go);
        }
    }
}
