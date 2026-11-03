using System.Text.RegularExpressions;
using NUnit.Framework;
using RedDot.Runtime.RedDot;
using UnityEngine;
using UnityEngine.TestTools;

namespace TEngine.Tests.RedDot
{
    /// <summary>
    /// RedDotGraph 聚合/脏传播/优先级归一测试（RedDotManager 新 API）。
    /// 覆盖:JSON 建图 + checker + DataChange + GetRedDotStatus、DAG 菱形多父聚合、
    /// RedDotStatusRules 六态优先级(满&gt;红点&gt;蓝点&gt;红数字&gt;蓝数字&gt;新)归一、掩码过滤。
    /// 只测公共 API:RedDotManager.Ins()/AddJsonStr/DataChange/GetRedDotStatus/SetNodeEnabled。
    /// </summary>
    [TestFixture]
    public class RedDotGraphAggregationTests
    {
        /// <summary>基础图:Root 全态聚合 → Mail(全态) → Unread(红数字)/Announce(红点|蓝点),Root 另挂 LimitTime(满)。</summary>
        private const string BasicGraphJson = @"
{
  ""vertexs"": [
    {""key"":""Root"",""redDotType"":63,""externalId"":-1},
    {""key"":""Mail"",""redDotType"":63,""externalId"":-1},
    {""key"":""Mail.Unread"",""redDotType"":4,""externalId"":1},
    {""key"":""Mail.Announce"",""redDotType"":3,""externalId"":2},
    {""key"":""Shop.LimitTime"",""redDotType"":32,""externalId"":3}
  ],
  ""edges"": [
    {""key"":""Root"",""outKeys"":[""Mail"",""Shop.LimitTime""]},
    {""key"":""Mail"",""outKeys"":[""Mail.Unread"",""Mail.Announce""]}
  ]
}";

        /// <summary>菱形图:D(叶子) 双父 B/C,共同父 A——多父 DAG 脏传播。</summary>
        private const string DiamondGraphJson = @"
{
  ""vertexs"": [
    {""key"":""A"",""redDotType"":63,""externalId"":-1},
    {""key"":""B"",""redDotType"":63,""externalId"":-1},
    {""key"":""C"",""redDotType"":63,""externalId"":-1},
    {""key"":""D"",""redDotType"":1,""externalId"":1}
  ],
  ""edges"": [
    {""key"":""A"",""outKeys"":[""B"",""C""]},
    {""key"":""B"",""outKeys"":[""D""]},
    {""key"":""C"",""outKeys"":[""D""]}
  ]
}";

        private RedDotManager _mgr;

        [SetUp]
        public void SetUp()
        {
            // Reset 会清图/帧/checker 表并置空单例,重取即全新实例
            RedDotManager.Ins().Reset();
            _mgr = RedDotManager.Ins();
        }

        [TearDown]
        public void TearDown()
        {
            RedDotManager.Ins().Reset();
            _mgr = null;
        }

        // ─── 建图与基础聚合 ───

        [Test]
        public void AddJsonStr_BuildGraph_DataChangeThenRead()
        {
            RegisterChecker(1, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedNumber;
                n = 3;
            });

            Assert.IsTrue(_mgr.AddJsonStr(BasicGraphJson, checkVisitor: false));

            // 初态未标脏,读回 Null;报脏后读时刷新取到 checker 现算值
            _mgr.GetRedDotStatus("Mail.Unread", 0, out var status, out var num);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);

            _mgr.DataChange("Mail.Unread");
            _mgr.GetRedDotStatus("Mail.Unread", 0, out status, out num);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedNumber, status);
            Assert.AreEqual(3, num);

            // 沿链向上:Mail 与 Root 均聚合到红数字
            _mgr.GetRedDotStatus("Mail", 0, out status, out num);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedNumber, status);
            Assert.AreEqual(3, num);
            _mgr.GetRedDotStatus("Root", 0, out status, out num);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedNumber, status);
            Assert.AreEqual(3, num);
        }

        [Test]
        public void Aggregation_SumsSameNumberStatus()
        {
            // 同数字态叠加:两个红数字叶子(掩码 4)挂全态聚合父,2+3=5
            const string graphJson = @"
{
  ""vertexs"": [
    {""key"":""P"",""redDotType"":63,""externalId"":-1},
    {""key"":""C1"",""redDotType"":4,""externalId"":1},
    {""key"":""C2"",""redDotType"":4,""externalId"":2}
  ],
  ""edges"": [
    {""key"":""P"",""outKeys"":[""C1"",""C2""]}
  ]
}";
            RegisterChecker(1, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedNumber;
                n = 2;
            });
            RegisterChecker(2, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedNumber;
                n = 3;
            });

            Assert.IsTrue(_mgr.AddJsonStr(graphJson, checkVisitor: false));
            _mgr.DataChange("C1");
            _mgr.DataChange("C2");

            _mgr.GetRedDotStatus("P", 0, out var status, out var num);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedNumber, status);
            Assert.AreEqual(5, num);
        }

        [Test]
        public void Aggregation_Priority_FullOverRedDotOverRedNumber()
        {
            RegisterChecker(1, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedNumber;
                n = 2;
            });
            SetAnnounceRedDot();
            SetLimitTimeFull(false);

            Assert.IsTrue(_mgr.AddJsonStr(BasicGraphJson, checkVisitor: false));
            _mgr.DataChange("Mail.Unread");
            _mgr.DataChange("Mail.Announce");
            _mgr.DataChange("Shop.LimitTime");

            // 红点优先级 > 红数字:Mail 取红点(值不并)
            _mgr.GetRedDotStatus("Mail", 0, out var status, out var num);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
            // 满优先级最高:Root 取满
            _mgr.GetRedDotStatus("Root", 0, out status, out num);
            Assert.AreEqual(RedDotStatus.RedDotStatusFull, status);

            // 满熄灭 → Root 回落红点
            SetLimitTimeFull(true);
            _mgr.DataChange("Shop.LimitTime");
            _mgr.GetRedDotStatus("Root", 0, out status, out num);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);

            // 红点熄灭 → Mail 回落红数字
            SetAnnounceOff();
            _mgr.DataChange("Mail.Announce");
            _mgr.GetRedDotStatus("Mail", 0, out status, out num);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedNumber, status);
            Assert.AreEqual(2, num);
        }

        [Test]
        public void Aggregation_CombinedCheckerOutput_NormalizedToHighestBit()
        {
            // checker 返回组合值(红点|蓝点 = 3),节点掩码 63 放行,归一取优先级最高位=红点
            const string graphJson = @"
{
  ""vertexs"": [
    {""key"":""P"",""redDotType"":63,""externalId"":-1},
    {""key"":""C"",""redDotType"":63,""externalId"":9}
  ],
  ""edges"": [
    {""key"":""P"",""outKeys"":[""C""]}
  ]
}";
            RegisterChecker(9, (out RedDotStatus s, out int n) =>
            {
                s = (RedDotStatus)((int)RedDotStatus.RedDotStatusRedDot | (int)RedDotStatus.RedDotStatusBlueDot);
                n = 0;
            });

            Assert.IsTrue(_mgr.AddJsonStr(graphJson, checkVisitor: false));
            _mgr.DataChange("C");

            _mgr.GetRedDotStatus("C", 0, out var status, out var num);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
        }

        [Test]
        public void MaskFilter_DropsContributionOutsideMask()
        {
            // 子节点掩码 8(蓝数字):checker 输出蓝点(2)在掩码外,自身归 Null;
            // 父节点掩码 7(红点|蓝点|红数字):子输出蓝数字(8)在父掩码外,父不采纳
            const string graphJson = @"
{
  ""vertexs"": [
    {""key"":""P"",""redDotType"":7,""externalId"":-1},
    {""key"":""C"",""redDotType"":8,""externalId"":5}
  ],
  ""edges"": [
    {""key"":""P"",""outKeys"":[""C""]}
  ]
}";
            RegisterChecker(5, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusBlueDot;
                n = 0;
            });

            Assert.IsTrue(_mgr.AddJsonStr(graphJson, checkVisitor: false));
            // 编辑器下掩码滤除走 Log.Error 大声暴露(诊断告警),此处按预期吞掉
            LogAssert.Expect(LogType.Error, new Regex("checker状态含节点编辑器未勾选的状态位"));
            _mgr.DataChange("C");
            _mgr.GetRedDotStatus("C", 0, out var status, out var num);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);
            _mgr.GetRedDotStatus("P", 0, out status, out num);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);

            // checker 改输出蓝数字(掩码内)→ 子亮蓝数字;子已有非空状态后再报脏,
            // 宽松标记(子状态非 Null→父需重算)命中,父重算时掩码滤除子贡献并告警
            RegisterChecker(5, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusBlueNumber;
                n = 2;
            });
            _mgr.DataChange("C");
            _mgr.GetRedDotStatus("C", 0, out status, out num);
            Assert.AreEqual(RedDotStatus.RedDotStatusBlueNumber, status);
            Assert.AreEqual(2, num);
            LogAssert.Expect(LogType.Error, new Regex("子贡献状态含父节点编辑器未勾选的状态位"));
            _mgr.DataChange("C");
            _mgr.GetRedDotStatus("P", 0, out status, out num);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);
        }

        // ─── DAG 菱形多父 ───

        [Test]
        public void DiamondDag_ChangePropagatesThroughAllParents()
        {
            RegisterChecker(1, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedDot;
                n = 0;
            });

            Assert.IsTrue(_mgr.AddJsonStr(DiamondGraphJson, checkVisitor: false));
            _mgr.DataChange("D");

            _mgr.GetRedDotStatus("D", 0, out var status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
            _mgr.GetRedDotStatus("B", 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
            _mgr.GetRedDotStatus("C", 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
            _mgr.GetRedDotStatus("A", 0, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
        }

        // ─── 加载防御 ───

        [Test]
        public void AddJsonStr_RejectDuplicateLoad()
        {
            Assert.IsTrue(_mgr.AddJsonStr(BasicGraphJson, checkVisitor: false));
            // P3-7:图内已有配置顶点时拒绝整份重复加载(重建须先 Reset)
            LogAssert.Expect(LogType.Error, new Regex("拒绝本次重复加载"));
            Assert.IsFalse(_mgr.AddJsonStr(BasicGraphJson, checkVisitor: false));
        }

        [Test]
        public void AddJsonStr_RejectCycle()
        {
            // 环:A→B→A,InsertEdge 环检测失败,P3-6 聚合失败全量硬报错
            const string cyclicJson = @"
{
  ""vertexs"": [
    {""key"":""A"",""redDotType"":1,""externalId"":-1},
    {""key"":""B"",""redDotType"":1,""externalId"":-1}
  ],
  ""edges"": [
    {""key"":""A"",""outKeys"":[""B""]},
    {""key"":""B"",""outKeys"":[""A""]}
  ]
}";
            LogAssert.Expect(LogType.Error, new Regex("构成了环，路径:"));
            LogAssert.Expect(LogType.Error, new Regex("插入边\\(.*\\)失败，构成了环"));
            LogAssert.Expect(LogType.Error, new Regex("红点配置加载失败"));
            Assert.IsFalse(_mgr.AddJsonStr(cyclicJson, checkVisitor: false));
        }

        [Test]
        public void AddJsonStr_CheckVisitorTrue_AggregateNodeNeedsNoChecker()
        {
            // 聚合节点(externalId=-1)无外部数据源,校验跳过;叶子未注册 checker 时才报未注册
            RegisterChecker(1, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedNumber;
                n = 1;
            });
            // Mail.Announce(externalId=2)/Shop.LimitTime(externalId=3) 未注册 → 加载失败
            LogAssert.Expect(LogType.Error, new Regex("未注册 checker"));
            Assert.IsFalse(_mgr.AddJsonStr(BasicGraphJson, checkVisitor: true));
        }

        // ─── checker 注册辅助 ───

        private static void SetAnnounceRedDot()
        {
            RegisterChecker(2, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedDot;
                n = 0;
            });
        }

        private static void SetAnnounceOff()
        {
            RegisterChecker(2, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusNull;
                n = 0;
            });
        }

        private static void SetLimitTimeFull(bool off)
        {
            RegisterChecker(3, (out RedDotStatus s, out int n) =>
            {
                s = off ? RedDotStatus.RedDotStatusNull : RedDotStatus.RedDotStatusFull;
                n = 0;
            });
        }

        private static void RegisterChecker(int externalId, RedDotExternalLogicVisitor.RedDotExternalLogic logic)
        {
            RedDotExternalLogicVisitor.Ins().RegExternalLogic(externalId, logic);
        }
    }
}
