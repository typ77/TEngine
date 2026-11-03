using System.Text.RegularExpressions;
using NUnit.Framework;
using RedDot.Runtime.RedDot;
using UnityEngine;
using UnityEngine.TestTools;
// 注:本文件仅依赖 RedDot.Runtime.RedDot 与 NUnit/Unity TestRunner;TEngine 侧类型经 GameLogic 程序集间接可达

namespace TEngine.Tests.RedDot
{
    /// <summary>
    /// RedDotManager 帧绑定与刷新模式测试。
    /// 覆盖:AddFrame/ChangeToFrame 后 AddUI 只收当前帧、Immediate 同步回调、
    /// Batched 入队 + FlushPendingChanges 手动冲刷、BeginConfigBuffering/SetConfigReady 缓冲重放、
    /// SetNodeEnabled 门控(禁用吞 DataChange、读回 Null)。
    /// 只测公共 API;每个用例经 RedDotManager.Ins().Reset() 复位(Reset 置空单例)。
    /// </summary>
    [TestFixture]
    public class RedDotManagerFrameTests
    {
        private const string GraphJson = @"
{
  ""vertexs"": [
    {""key"":""Root"",""redDotType"":63,""externalId"":-1},
    {""key"":""Leaf"",""redDotType"":4,""externalId"":1}
  ],
  ""edges"": [
    {""key"":""Root"",""outKeys"":[""Leaf""]}
  ]
}";

        private RedDotManager _mgr;
        private int _cb1Count;
        private int _cb2Count;

        [SetUp]
        public void SetUp()
        {
            RedDotManager.Ins().Reset();
            _mgr = RedDotManager.Ins();
            _cb1Count = 0;
            _cb2Count = 0;
            RedDotExternalLogicVisitor.Ins().RegExternalLogic(1, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedNumber;
                n = 2;
            });
            Assert.IsTrue(_mgr.AddJsonStr(GraphJson, checkVisitor: false));
        }

        [TearDown]
        public void TearDown()
        {
            RedDotManager.Ins().Reset();
            _mgr = null;
        }

        private void Cb1()
        {
            _cb1Count++;
        }

        private void Cb2()
        {
            _cb2Count++;
        }

        // ─── 帧绑定 ───

        [Test]
        public void ChangeToFrame_AddUIOnlyReceivesCurrentFrame()
        {
            _mgr.AddFrame("F1");
            _mgr.AddUI("Leaf", Cb1);
            // 注册即回放一次
            Assert.AreEqual(1, _cb1Count);

            // AddFrame("F2") 创建并切到 F2(AddUI 只注册到当前帧)
            _mgr.AddFrame("F2");
            _mgr.AddUI("Leaf", Cb2);
            Assert.AreEqual(1, _cb2Count);

            // 当前帧=F2:变更只派发给 F2 绑定
            _mgr.DataChange("Leaf");
            Assert.AreEqual(1, _cb1Count);
            Assert.AreEqual(2, _cb2Count);

            // 切回 F1:F1 绑定恢复收播
            _mgr.ChangeToFrame("F1");
            _mgr.DataChange("Leaf");
            Assert.AreEqual(2, _cb1Count);
            Assert.AreEqual(2, _cb2Count);
        }

        [Test]
        public void RemoveUI_StopsCallback()
        {
            _mgr.AddFrame("F1");
            _mgr.AddUI("Leaf", Cb1);
            Assert.AreEqual(1, _cb1Count);

            _mgr.DataChange("Leaf");
            Assert.AreEqual(2, _cb1Count);

            _mgr.RemoveUI("Leaf", Cb1);
            _mgr.DataChange("Leaf");
            Assert.AreEqual(2, _cb1Count);
        }

        // ─── Immediate / Batched ───

        [Test]
        public void Immediate_DataChange_SynchronousCallback()
        {
            _mgr.AddFrame("F1");
            _mgr.AddUI("Leaf", Cb1);
            _cb1Count = 0;

            // 默认 Immediate:DataChange 同帧标记+通知
            Assert.AreEqual(RedDotRefreshMode.Immediate, _mgr.RefreshMode);
            _mgr.DataChange("Leaf");
            Assert.AreEqual(1, _cb1Count);
        }

        [Test]
        public void Batched_DataChange_BufferedUntilFlush()
        {
            _mgr.AddFrame("F1");
            _mgr.AddUI("Leaf", Cb1);
            _cb1Count = 0;
            _mgr.RefreshMode = RedDotRefreshMode.Batched;

            // 入队不派发
            _mgr.DataChange("Leaf");
            Assert.AreEqual(0, _cb1Count);

            // 手动冲刷后送达
            _mgr.FlushPendingChanges();
            Assert.AreEqual(1, _cb1Count);

            // 队列已清空,重复冲刷不再派发
            _mgr.FlushPendingChanges();
            Assert.AreEqual(1, _cb1Count);
        }

        [Test]
        public void Batched_SameKeyMergedToOneDispatch()
        {
            _mgr.AddFrame("F1");
            _mgr.AddUI("Leaf", Cb1);
            _cb1Count = 0;
            _mgr.RefreshMode = RedDotRefreshMode.Batched;

            // 同 (key,id) 未派发期间合并去重:两次报脏一次派发
            _mgr.DataChange("Leaf");
            _mgr.DataChange("Leaf");
            _mgr.FlushPendingChanges();
            Assert.AreEqual(1, _cb1Count);
        }

        // ─── 配置未就绪缓冲重放 ───

        [Test]
        public void BeginConfigBuffering_OpsReplayedOnSetConfigReady()
        {
            // P0-3:未就绪期 DataChange/AddUI 暂存,SetConfigReady 按 FIFO 重放
            _mgr.BeginConfigBuffering();
            _mgr.AddFrame("F1");

            _mgr.AddUI("Leaf", Cb1);
            _mgr.DataChange("Leaf");
            Assert.AreEqual(0, _cb1Count);

            _mgr.SetConfigReady();
            // 重放:AddUI(注册回放 +1) → DataChange(派发 +1)
            Assert.AreEqual(2, _cb1Count);

            // 幂等:重复 SetConfigReady 不再重放
            _mgr.SetConfigReady();
            Assert.AreEqual(2, _cb1Count);
        }

        [Test]
        public void BeginConfigBuffering_RemoveUICancelsBufferedAddUI()
        {
            _mgr.BeginConfigBuffering();
            _mgr.AddUI("Leaf", Cb1);
            // 配对 RemoveUI 对冲撤销暂存项,重放期不再回放
            _mgr.RemoveUI("Leaf", Cb1);

            _mgr.SetConfigReady();
            Assert.AreEqual(0, _cb1Count);
        }

        // ─── 节点门控 ───

        [Test]
        public void SetNodeEnabled_Disabled_SwallowsDataChangeAndReadsNull()
        {
            _mgr.DataChange("Leaf");
            _mgr.GetRedDotStatus("Leaf", 0, out var status, out var num);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedNumber, status);

            _mgr.SetNodeEnabled("Leaf", false);
            _mgr.GetRedDotStatus("Leaf", 0, out status, out num);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);
            Assert.AreEqual(0, num);

            // 禁用吞 DataChange:报脏后仍读回 Null
            _mgr.DataChange("Leaf");
            _mgr.GetRedDotStatus("Leaf", 0, out status, out num);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);
        }

        [Test]
        public void SetNodeEnabled_ReEnabled_RestoresAggregation()
        {
            _mgr.SetNodeEnabled("Leaf", false);
            _mgr.GetRedDotStatus("Leaf", 0, out var status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);

            _mgr.SetNodeEnabled("Leaf", true);
            _mgr.GetRedDotStatus("Leaf", 0, out status, out var num);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedNumber, status);
            Assert.AreEqual(2, num);
            // 父链恢复聚合
            _mgr.GetRedDotStatus("Root", 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedNumber, status);
        }

        [Test]
        public void SetNodeEnabled_UnknownKey_WarnsOnly()
        {
            // 未知 key:Warning 不改状态,无异常
            LogAssert.Expect(LogType.Warning, new Regex("SetNodeEnabled 未找到红点节点"));
            _mgr.SetNodeEnabled("NoWhere", false);
        }
    }
}
