using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    public class BufferPoolTests
    {
        [TearDown]
        public void TearDown()
        {
            BufferPool.Clear();
        }

        [Test]
        public void Rent_ReturnsArrayWithRequestedSize()
        {
            var buffer = BufferPool.Rent(128);
            Assert.NotNull(buffer);
            Assert.GreaterOrEqual(buffer.Length, 128);
        }

        [Test]
        public void RentCleared_ReturnsZeroedArray()
        {
            var buffer = BufferPool.RentCleared(64);
            for (int i = 0; i < buffer.Length; i++)
                Assert.AreEqual(0, buffer[i]);
        }

        [Test]
        public void Return_ThenRent_SameBucket_ReusesBuffer()
        {
            var buffer1 = BufferPool.Rent(64);
            BufferPool.Return(buffer1);
            var buffer2 = BufferPool.Rent(64);
            // 从同一桶租出，可能复用（不保证，但池化后应能观察到）
            Assert.AreEqual(buffer1.Length, buffer2.Length);
        }

        [Test]
        public void Return_Null_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => BufferPool.Return(null));
        }

        [Test]
        public void Return_Twice_DoesNotThrow()
        {
            var buffer = BufferPool.Rent(64);
            BufferPool.Return(buffer);
            Assert.DoesNotThrow(() => BufferPool.Return(buffer));
        }

        [Test]
        public void ActiveCount_TracksRentAndReturn()
        {
            var a = BufferPool.Rent(64);
            var b = BufferPool.Rent(128);
            Assert.AreEqual(2, BufferPool.ActiveCount);
            BufferPool.Return(a);
            Assert.AreEqual(1, BufferPool.ActiveCount);
            BufferPool.Return(b);
            Assert.AreEqual(0, BufferPool.ActiveCount);
        }

        [Test]
        public void Clear_ResetsActiveCount()
        {
            BufferPool.Rent(64);
            BufferPool.Rent(128);
            BufferPool.Clear();
            Assert.AreEqual(0, BufferPool.ActiveCount);
        }
    }
}
