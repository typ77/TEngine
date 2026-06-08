using System;
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class DataContextFactoryTests
    {
        [Test]
        public void CreateFor_WithAttribute_ReturnsCorrectContext()
        {
            var ctx = DataContextFactory.CreateFor(typeof(TestViewWithContext));
            Assert.IsNotNull(ctx);
            Assert.IsInstanceOf<TestContext>(ctx);
        }

        [Test]
        public void CreateFor_NoAttribute_ReturnsNull()
        {
            var ctx = DataContextFactory.CreateFor(typeof(TestViewWithoutContext));
            Assert.IsNull(ctx);
        }

        [Test]
        public void DataContextAttribute_NullType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                new DataContextAttribute(null);
            });
        }

        [Test]
        public void DataContextAttribute_InvalidType_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                new DataContextAttribute(typeof(string));
            });
        }

        [Test]
        public void CreateFor_CalledTwice_ReturnsNewInstances()
        {
            var ctx1 = DataContextFactory.CreateFor(typeof(TestViewWithContext));
            var ctx2 = DataContextFactory.CreateFor(typeof(TestViewWithContext));
            Assert.IsNotNull(ctx1);
            Assert.IsNotNull(ctx2);
            Assert.AreNotSame(ctx1, ctx2);
        }

        [DataContext(typeof(TestContext))]
        private class TestViewWithContext { }

        private class TestViewWithoutContext { }

        private class TestContext : DataContext { }
    }
}
