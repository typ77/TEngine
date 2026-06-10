using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic.UI.FancyScrollView;
using NUnit.Framework;
using UnityEngine;

namespace TEngine.Tests
{
    [TestFixture]
    public class ContentLoadSchedulerTests
    {
        private sealed class TestContext { }

        private sealed class LoadCell : FancyCellWidget<int, TestContext>
        {
            public int LoadCount { get; private set; }
            protected override string ContentAssetLocation => "virtual-content";
            protected override void BindData(int data) { }
            public override void UpdatePosition(float normalizedPosition) { }
            protected override UniTask LoadContentCoreAsync(System.IProgress<float> progress, CancellationToken ct)
            {
                LoadCount++;
                return UniTask.CompletedTask;
            }
        }

        [Test]
        public void AutoTrigger_LoadsCellWhenVisible()
        {
            var parentGo = new GameObject("parent", typeof(RectTransform));
            var parent = new TestParentUI(parentGo);
            var go = new GameObject("cell", typeof(RectTransform));
            var cell = new LoadCell();
            cell.Create(parent, go);
            using var scheduler = new ContentLoadScheduler<int, TestContext>();
            scheduler.Mode = ContentLoadMode.AutoTrigger;

            scheduler.OnCellBecameVisible(cell);

            Assert.AreEqual(1, cell.LoadCount);
            cell.CallDestroy();
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(parentGo);
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
    }
}
