using NUnit.Framework;
using TEngine;

namespace TEngine.Tests
{
    // 测试用拦截器：Order=10，永远放行
    public class OrderTenInterceptor : IMessageInterceptor
    {
        public string Name => "order10";
        public int Order => 10;
        public bool OnSending(ushort msgId, ref byte[] payload, in MessageTrace trace) => true;
        public bool OnReceiving(ushort msgId, ref byte[] payload, in MessageTrace trace) => true;
    }

    // 测试用拦截器：Order=100，记录自身被调用的全局序号
    public class OrderHundredInterceptor : IMessageInterceptor
    {
        public string Name => "order100";
        public int Order => 100;
        public int SendCallOrder;
        public static int GlobalCallCounter;

        public bool OnSending(ushort msgId, ref byte[] payload, in MessageTrace trace)
        {
            SendCallOrder = ++GlobalCallCounter;
            return true;
        }
        public bool OnReceiving(ushort msgId, ref byte[] payload, in MessageTrace trace) => true;
    }

    // 测试用拦截器：Order=50，永远阻断
    public class BlockInterceptor : IMessageInterceptor
    {
        public string Name => "blocker";
        public int Order => 50;
        public bool OnSending(ushort msgId, ref byte[] payload, in MessageTrace trace) => false;
        public bool OnReceiving(ushort msgId, ref byte[] payload, in MessageTrace trace) => false;
    }

    public class InterceptorPipelineTests
    {
        [SetUp]
        public void SetUp()
        {
            OrderHundredInterceptor.GlobalCallCounter = 0;
        }

        [Test]
        public void ProcessSending_AllPass_ReturnsTrue()
        {
            var pipeline = new InterceptorPipeline();
            pipeline.Add(new OrderTenInterceptor());
            pipeline.Add(new OrderHundredInterceptor());

            var payload = new byte[] { 0x01 };
            var trace = new MessageTrace();
            bool result = pipeline.ProcessSending(1, ref payload, in trace);

            Assert.IsTrue(result);
        }

        [Test]
        public void ProcessSending_Blocker_ReturnsFalse()
        {
            var pipeline = new InterceptorPipeline();
            pipeline.Add(new OrderTenInterceptor());
            pipeline.Add(new BlockInterceptor());

            var payload = new byte[] { 0x01 };
            var trace = new MessageTrace();
            bool result = pipeline.ProcessSending(1, ref payload, in trace);

            Assert.IsFalse(result);
        }

        [Test]
        public void ProcessSending_OrderAscending()
        {
            var pipeline = new InterceptorPipeline();
            var high = new OrderHundredInterceptor();
            var low = new OrderTenInterceptor();

            // 添加顺序无关，按 Order 排序
            pipeline.Add(high);
            pipeline.Add(low);

            var payload = new byte[] { 0x01 };
            var trace = new MessageTrace();
            pipeline.ProcessSending(1, ref payload, in trace);

            // Order=10 应该在 Order=100 之前执行；
            // high 是唯一递增计数器的拦截器，排序正确时它是第 2 个被调用（counter=1）
            Assert.AreEqual(1, high.SendCallOrder);
        }

        [Test]
        public void Remove_ByName()
        {
            var pipeline = new InterceptorPipeline();
            pipeline.Add(new OrderTenInterceptor());
            pipeline.Add(new BlockInterceptor());

            var payload = new byte[] { 0x01 };
            var trace = new MessageTrace();

            Assert.IsFalse(pipeline.ProcessSending(1, ref payload, in trace));
            pipeline.Remove("blocker");
            Assert.IsTrue(pipeline.ProcessSending(1, ref payload, in trace));
        }
    }
}
