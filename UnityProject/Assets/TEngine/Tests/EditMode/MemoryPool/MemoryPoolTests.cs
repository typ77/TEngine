using System;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class MemoryPoolTests
    {
        private class TestMemory : IMemory
        {
            public bool Cleared { get; private set; }
            public void Clear() => Cleared = true;
        }

        [SetUp]
        public void SetUp()
        {
            MemoryPool.EnableStrictCheck = true;
        }

        [TearDown]
        public void TearDown()
        {
            MemoryPool.EnableStrictCheck = false;
            MemoryPool.ClearAll();
        }

        [Test]
        public void AcquireAndRelease_CountsMatch()
        {
            var obj = MemoryPool.Acquire<TestMemory>();
            Assert.IsNotNull(obj);
            MemoryPool.Release(obj);
            var obj2 = MemoryPool.Acquire<TestMemory>();
            Assert.IsNotNull(obj2);
            Assert.IsTrue(obj2.Cleared);
            MemoryPool.Release(obj2);
        }

        [Test]
        public void ReleaseNull_ThrowsGameFrameworkException()
        {
            var ex = Assert.Throws<GameFrameworkException>(() => MemoryPool.Release(null));
            Assert.IsNotNull(ex);
        }

        [Test]
        public void ReleaseTwice_StrictCheck_ThrowsException()
        {
            var obj = MemoryPool.Acquire<TestMemory>();
            MemoryPool.Release(obj);
            Assert.Throws<GameFrameworkException>(() => MemoryPool.Release(obj));
        }

        [Test]
        public void Acquire_NewObject_IsNotNull()
        {
            var obj = MemoryPool.Acquire<TestMemory>();
            Assert.IsNotNull(obj);
            Assert.IsInstanceOf<TestMemory>(obj);
        }

        [Test]
        public void ClearAll_RemovesAllPools()
        {
            MemoryPool.Acquire<TestMemory>();
            MemoryPool.ClearAll();
            Assert.AreEqual(0, MemoryPool.Count);
        }
    }
}
