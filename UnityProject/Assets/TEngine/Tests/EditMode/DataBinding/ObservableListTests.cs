using System.Collections.Generic;
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class ObservableListTests : DataBindingTestBase
    {
        private readonly record struct Item(int Id, string Name);

        private ObservableList<Item> _list;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _list = new ObservableList<Item>();
        }

        [TearDown]
        public override void TearDown()
        {
            _list?.Dispose();
            base.TearDown();
        }

        [Test]
        public void Add_IncreasesCount()
        {
            _list.Add(new Item(1, "a"));
            Assert.AreEqual(1, _list.Count);
            _list.Add(new Item(2, "b"));
            Assert.AreEqual(2, _list.Count);
        }

        [Test]
        public void Add_FiresEvent_AfterFireCallback()
        {
            ListChangeType? received = null;
            _list.OnChanged += args => received = args.Type;

            _list.Add(new Item(1, "a"));
            Assert.IsNull(received); // 尚未 Flush

            ((IBatchDirtyTarget)_list).FireCallback();
            Assert.AreEqual(ListChangeType.Add, received);
        }

        [Test]
        public void Insert_AtCorrectIndex()
        {
            _list.Add(new Item(1, "a"));
            _list.Add(new Item(3, "c"));
            _list.Insert(1, new Item(2, "b"));

            Assert.AreEqual(3, _list.Count);
            Assert.AreEqual(new Item(2, "b"), _list[1]);
        }

        [Test]
        public void RemoveAt_DecreasesCount()
        {
            _list.Add(new Item(1, "a"));
            _list.Add(new Item(2, "b"));
            _list.RemoveAt(0);

            Assert.AreEqual(1, _list.Count);
            Assert.AreEqual(new Item(2, "b"), _list[0]);
        }

        [Test]
        public void Replace_UpdatesValue()
        {
            _list.Add(new Item(1, "a"));
            _list.Replace(0, new Item(10, "x"));

            Assert.AreEqual(new Item(10, "x"), _list[0]);
        }

        [Test]
        public void Replace_FiresEvent_WithOldItem()
        {
            var original = new Item(1, "a");
            var replacement = new Item(10, "x");
            _list.Add(original);

            Item receivedOld = default;
            Item receivedNew = default;
            _list.OnChanged += args =>
            {
                receivedOld = args.OldItem;
                receivedNew = args.Item;
            };

            _list.Replace(0, replacement);
            ((IBatchDirtyTarget)_list).FireCallback();

            Assert.AreEqual(original, receivedOld);
            Assert.AreEqual(replacement, receivedNew);
        }

        [Test]
        public void Clear_RemovesAll()
        {
            _list.Add(new Item(1, "a"));
            _list.Add(new Item(2, "b"));
            _list.Clear();

            Assert.AreEqual(0, _list.Count);
        }

        [Test]
        public void Move_ChangesPositions()
        {
            _list.Add(new Item(1, "a"));
            _list.Add(new Item(2, "b"));
            _list.Add(new Item(3, "c"));

            _list.Move(0, 2);

            Assert.AreEqual(new Item(2, "b"), _list[0]);
            Assert.AreEqual(new Item(3, "c"), _list[1]);
            Assert.AreEqual(new Item(1, "a"), _list[2]);
        }

        [Test]
        public void AddRange_FiresAddRangeEvent()
        {
            ListChangeType? received = null;
            IReadOnlyList<Item> newItems = null;
            _list.OnChanged += args =>
            {
                received = args.Type;
                newItems = args.NewItems;
            };

            _list.AddRange(new[] { new Item(1, "a"), new Item(2, "b") });
            ((IBatchDirtyTarget)_list).FireCallback();

            Assert.AreEqual(ListChangeType.AddRange, received);
            Assert.IsNotNull(newItems);
            Assert.AreEqual(2, newItems.Count);
        }

        [Test]
        public void ReplaceAll_FiresReplaceAllEvent()
        {
            _list.Add(new Item(0, "old"));

            ListChangeType? received = null;
            IReadOnlyList<Item> newItems = null;
            _list.OnChanged += args =>
            {
                received = args.Type;
                newItems = args.NewItems;
            };

            _list.ReplaceAll(new[] { new Item(1, "a"), new Item(2, "b") });
            ((IBatchDirtyTarget)_list).FireCallback();

            Assert.AreEqual(ListChangeType.ReplaceAll, received);
            Assert.AreEqual(2, newItems.Count);
            Assert.AreEqual(new Item(1, "a"), newItems[0]);
        }

        [Test]
        public void SingleOp_PreservesEventType()
        {
            ListChangeType? received = null;
            _list.OnChanged += args => received = args.Type;

            _list.Add(new Item(1, "a"));
            ((IBatchDirtyTarget)_list).FireCallback();

            // 单次操作保留原始事件类型
            Assert.AreEqual(ListChangeType.Add, received);
        }

        [Test]
        public void MultipleOps_MergesToReplaceAll()
        {
            ListChangeType? received = null;
            IReadOnlyList<Item> newItems = null;
            _list.OnChanged += args =>
            {
                received = args.Type;
                newItems = args.NewItems;
            };

            _list.Add(new Item(1, "a"));
            _list.Add(new Item(2, "b"));
            _list.Add(new Item(3, "c"));
            ((IBatchDirtyTarget)_list).FireCallback();

            // 多次操作合并为 ReplaceAll
            Assert.AreEqual(ListChangeType.ReplaceAll, received);
            Assert.AreEqual(3, newItems.Count);
        }

        [Test]
        public void Indexer_IsReadOnly()
        {
            _list.Add(new Item(1, "a"));
            // this[int] 只有 get，编译期验证
            var value = _list[0];
            Assert.AreEqual(new Item(1, "a"), value);
        }

        [Test]
        public void AsReadOnly_ReturnsReadOnlyView()
        {
            _list.Add(new Item(1, "a"));
            var readOnly = _list.AsReadOnly();
            Assert.IsInstanceOf<IReadOnlyList<Item>>(readOnly);
            Assert.AreEqual(1, readOnly.Count);
        }

        [Test]
        public void Contains_And_IndexOf()
        {
            var item = new Item(1, "a");
            _list.Add(item);

            Assert.IsTrue(_list.Contains(item));
            Assert.AreEqual(0, _list.IndexOf(item));

            var missing = new Item(99, "missing");
            Assert.IsFalse(_list.Contains(missing));
            Assert.AreEqual(-1, _list.IndexOf(missing));
        }

        [Test]
        public void Dispose_PreventsEvents()
        {
            bool fired = false;
            _list.OnChanged += _ => fired = true;

            _list.Dispose();
            Assert.IsTrue(_list.IsDisposed);

            _list.Add(new Item(1, "a"));
            ((IBatchDirtyTarget)_list).FireCallback();

            Assert.IsFalse(fired);
        }
    }
}
