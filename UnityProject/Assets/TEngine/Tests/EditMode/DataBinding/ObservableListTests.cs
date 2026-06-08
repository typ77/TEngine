using System;
using System.Collections.Generic;
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class ObservableListTests : DataBindingTestBase
    {
        private struct Item : IEquatable<Item>
        {
            public int Id;
            public string Name;

            public Item(int id, string name) { Id = id; Name = name; }

            public bool Equals(Item other) => Id == other.Id && Name == other.Name;
            public override bool Equals(object obj) => obj is Item other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Id, Name);
            public static bool operator ==(Item left, Item right) => left.Equals(right);
            public static bool operator !=(Item left, Item right) => !left.Equals(right);
        }

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
        public void Add_FiresEvent_SyncInEditMode()
        {
            // EditMode 下 SafeMarkDirty 同步触发 FireCallback，无需手动 Flush
            ListChangeType? received = null;
            _list.OnChanged += args => received = args.Type;

            _list.Add(new Item(1, "a"));
            Assert.AreEqual(ListChangeType.Add, received, "EditMode 同步模式：Add 后立即触发事件");
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
        public void MultipleOps_EachFiresImmediatelyInEditMode()
        {
            // EditMode 同步模式：每次操作立即触发回调，不合并
            var receivedTypes = new List<ListChangeType>();
            _list.OnChanged += args => receivedTypes.Add(args.Type);

            _list.Add(new Item(1, "a"));
            _list.Add(new Item(2, "b"));
            _list.Add(new Item(3, "c"));

            // 每次 Add 立即触发，共 3 次
            Assert.AreEqual(3, receivedTypes.Count, "EditMode 同步模式：每次操作触发一次回调");
            Assert.AreEqual(ListChangeType.Add, receivedTypes[0]);
            Assert.AreEqual(ListChangeType.Add, receivedTypes[1]);
            Assert.AreEqual(ListChangeType.Add, receivedTypes[2]);
            Assert.AreEqual(3, _list.Count, "集合应包含所有添加的元素");
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
