using System;
using System.Collections;
using System.Collections.Generic;
using GameLogic;
using GameLogic.DataBinding;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TEngine.Tests
{
    /// <summary>
    /// PlayMode 测试：验证 ObservableList 运行时帧级合并行为。
    ///
    /// EditMode 下每次操作同步触发，不合并。
    /// PlayMode 下同帧多次操作合并为 ReplaceAll 事件。
    /// </summary>
    [TestFixture]
    public class ObservableListPlayModeTests
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
        public void SetUp()
        {
            _list = new ObservableList<Item>();
        }

        [TearDown]
        public void TearDown()
        {
            _list?.Dispose();
            if (Singleton<BatchScheduler>.IsValid)
            {
                Singleton<BatchScheduler>.Instance.OnLateUpdate();
            }
        }

        /// <summary>
        /// 验证：单次操作保留原始事件类型（Add）。
        /// </summary>
        [UnityTest]
        public IEnumerator SingleOp_PreservesOriginalEventType()
        {
            ListChangeType? received = null;
            _list.OnChanged += args => received = args.Type;

            _list.Add(new Item(1, "a"));

            yield return null;

            Assert.AreEqual(ListChangeType.Add, received, "单次操作应保留 Add 类型");
        }

        /// <summary>
        /// 验证：同帧多次操作合并为 ReplaceAll。
        /// </summary>
        [UnityTest]
        public IEnumerator MultipleOps_MergesToReplaceAll()
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

            yield return null;

            Assert.AreEqual(ListChangeType.ReplaceAll, received, "多次操作应合并为 ReplaceAll");
            Assert.IsNotNull(newItems);
            Assert.AreEqual(3, newItems.Count, "ReplaceAll 应包含所有元素");
        }

        /// <summary>
        /// 验证：事件只触发一次（合并）。
        /// </summary>
        [UnityTest]
        public IEnumerator MultipleOps_FiresOnlyOnce()
        {
            int callCount = 0;
            _list.OnChanged += _ => callCount++;

            _list.Add(new Item(1, "a"));
            _list.Add(new Item(2, "b"));
            _list.Add(new Item(3, "c"));

            yield return null;

            Assert.AreEqual(1, callCount, "同帧多次操作应只触发一次回调");
        }

        /// <summary>
        /// 验证：跨帧操作各自独立触发。
        /// </summary>
        [UnityTest]
        public IEnumerator CrossFrame_Operations_FireSeparately()
        {
            var received = new List<ListChangeType>();
            _list.OnChanged += args => received.Add(args.Type);

            _list.Add(new Item(1, "a"));
            yield return null; // 第一帧：单次 Add

            _list.Add(new Item(2, "b"));
            yield return null; // 第二帧：单次 Add

            Assert.AreEqual(2, received.Count, "跨帧操作应各自触发");
            Assert.AreEqual(ListChangeType.Add, received[0]);
            Assert.AreEqual(ListChangeType.Add, received[1]);
        }
    }
}
