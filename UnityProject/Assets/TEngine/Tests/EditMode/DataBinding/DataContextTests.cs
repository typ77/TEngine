using System;
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    [TestFixture]
    public class DataContextTests : DataBindingTestBase
    {
        [Test]
        public void MapProperty_SingleSource_SyncsValue()
        {
            var source = new BindableProperty<int>(10);
            var target = new BindableProperty<string>("");
            var ctx = new TestDataContext();
            ctx.ExposeMapProperty(source, target, v => v.ToString());

            Assert.AreEqual("10", target.Value);
            source.Value = 20;
            FlushScheduler();
            Assert.AreEqual("20", target.Value);
        }

        [Test]
        public void MapProperty_DualSource_SyncsValue()
        {
            var s1 = new BindableProperty<int>(10);
            var s2 = new BindableProperty<int>(20);
            var target = new BindableProperty<int>(0);
            var ctx = new TestDataContext();
            ctx.ExposeMapProperty2(s1, s2, target, (a, b) => a + b);

            Assert.AreEqual(30, target.Value);
            s1.Value = 5;
            FlushScheduler();
            Assert.AreEqual(25, target.Value);
            s2.Value = 10;
            FlushScheduler();
            Assert.AreEqual(15, target.Value);
        }

        [Test]
        public void MapProperty_TripleSource_SyncsValue()
        {
            var s1 = new BindableProperty<int>(1);
            var s2 = new BindableProperty<int>(2);
            var s3 = new BindableProperty<int>(3);
            var target = new BindableProperty<int>(0);
            var ctx = new TestDataContext();
            ctx.ExposeMapProperty3(s1, s2, s3, target, (a, b, c) => a + b + c);

            Assert.AreEqual(6, target.Value);
            s3.Value = 10;
            FlushScheduler();
            Assert.AreEqual(13, target.Value);
        }

        [Test]
        public void MapProperty_InitializesTarget_FromSourceValue()
        {
            var source = new BindableProperty<string>("hello");
            var target = new BindableProperty<string>("");
            var ctx = new TestDataContext();
            ctx.ExposeMapProperty(source, target, s => s.ToUpper());

            Assert.AreEqual("HELLO", target.Value);
        }

        [Test]
        public void Dispose_StopsSyncing()
        {
            var source = new BindableProperty<int>(10);
            var target = new BindableProperty<string>("");
            var ctx = new TestDataContext();
            ctx.ExposeMapProperty(source, target, v => v.ToString());
            ctx.Dispose();

            Assert.IsTrue(ctx.IsDisposed);
            source.Value = 20;
            FlushScheduler();
            Assert.AreEqual("10", target.Value);
        }

        [Test]
        public void Dispose_CalledMultipleTimes_DoesNotThrow()
        {
            var ctx = new TestDataContext();
            ctx.Dispose();
            Assert.DoesNotThrow(() => ctx.Dispose());
        }

        /// <summary>
        /// 测试用 DataContext 子类，暴露 protected 方法供测试调用。
        /// </summary>
        private class TestDataContext : DataContext
        {
            public void ExposeMapProperty<TS, TT>(
                BindableProperty<TS> source, BindableProperty<TT> target,
                Func<TS, TT> converter) => MapProperty(source, target, converter);

            public void ExposeMapProperty2<T1, T2, TT>(
                BindableProperty<T1> s1, BindableProperty<T2> s2,
                BindableProperty<TT> target, Func<T1, T2, TT> converter)
                => MapProperty(s1, s2, target, converter);

            public void ExposeMapProperty3<T1, T2, T3, TT>(
                BindableProperty<T1> s1, BindableProperty<T2> s2, BindableProperty<T3> s3,
                BindableProperty<TT> target, Func<T1, T2, T3, TT> converter)
                => MapProperty(s1, s2, s3, target, converter);
        }
    }
}
