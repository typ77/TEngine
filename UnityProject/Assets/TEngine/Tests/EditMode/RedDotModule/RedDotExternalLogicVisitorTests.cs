using NUnit.Framework;
using RedDot.Runtime.RedDot;

namespace TEngine.Tests.RedDot
{
    /// <summary>
    /// RedDotExternalLogicVisitor 单元测试(新 API)。
    /// 覆盖:RegExternalLogic/Visit(无 id 与带 id 两张委托表)、id=0 回退带 id 表、
    /// 重复注册覆盖、CheckHasExternalId、UnregisterExternalLogic、Clear。
    /// 注:聚合节点拒绝注册的守卫在 RedDotGraphModule.ValidateKeyArg(internal)与模块收口层,
    /// 聚合节点"无 checker"的可见契约在 RedDotGraphAggregationTests 的 checkVisitor 用例覆盖。
    /// </summary>
    [TestFixture]
    public class RedDotExternalLogicVisitorTests
    {
        private RedDotExternalLogicVisitor _visitor;

        [SetUp]
        public void SetUp()
        {
            RedDotManager.Ins().Reset();
            RedDotExternalLogicVisitor.Ins().Clear();
            _visitor = RedDotExternalLogicVisitor.Ins();
        }

        [TearDown]
        public void TearDown()
        {
            RedDotManager.Ins().Reset();
            _visitor = null;
        }

        [Test]
        public void RegAndVisit_NoId_UsesNoIdLogic()
        {
            _visitor.RegExternalLogic(100, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedNumber;
                n = 3;
            });

            _visitor.Visit(100, 0, out var status, out var num);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedNumber, status);
            Assert.AreEqual(3, num);
        }

        [Test]
        public void RegAndVisit_WithId_UsesWithIdLogic()
        {
            _visitor.RegExternalLogic(200, (uint id, out RedDotStatus s, out int n) =>
            {
                s = id == 7 ? RedDotStatus.RedDotStatusRedDot : RedDotStatus.RedDotStatusNull;
                n = 0;
            });

            _visitor.Visit(200, 7, out var status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusRedDot, status);
            _visitor.Visit(200, 8, out status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);
        }

        [Test]
        public void Visit_Id0_FallsBackToWithIdTable()
        {
            // 无 id 表未注册时,id=0 回退查带 id 表(P3-4 口径)
            _visitor.RegExternalLogic(300, (uint id, out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusBlueDot;
                n = 0;
            });

            _visitor.Visit(300, 0, out var status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusBlueDot, status);
        }

        [Test]
        public void Visit_MissingRegistration_ReturnsNull()
        {
            // 未注册:status/num 记 0(Null),走 Warning 域
            _visitor.Visit(999, 0, out var status, out var num);
            Assert.AreEqual(RedDotStatus.RedDotStatusNull, status);
            Assert.AreEqual(0, num);
        }

        [Test]
        public void RegExternalLogic_DuplicateRegistration_LastWins()
        {
            _visitor.RegExternalLogic(400, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedDot;
                n = 0;
            });
            _visitor.RegExternalLogic(400, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusFull;
                n = 0;
            });

            _visitor.Visit(400, 0, out var status, out _);
            Assert.AreEqual(RedDotStatus.RedDotStatusFull, status);
        }

        [Test]
        public void CheckHasExternalId_ChecksBothTables()
        {
            Assert.IsFalse(_visitor.CheckHasExternalId(500));
            _visitor.RegExternalLogic(500, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusNull;
                n = 0;
            });
            Assert.IsTrue(_visitor.CheckHasExternalId(500));

            _visitor.RegExternalLogic(501, (uint id, out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusNull;
                n = 0;
            });
            Assert.IsTrue(_visitor.CheckHasExternalId(501));
        }

        [Test]
        public void UnregisterExternalLogic_RemovesBothTables()
        {
            _visitor.RegExternalLogic(600, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedDot;
                n = 0;
            });
            _visitor.RegExternalLogic(600, (uint id, out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusRedDot;
                n = 0;
            });
            Assert.IsTrue(_visitor.CheckHasExternalId(600));

            _visitor.UnregisterExternalLogic(600);
            Assert.IsFalse(_visitor.CheckHasExternalId(600));
        }

        [Test]
        public void Clear_RemovesAll()
        {
            _visitor.RegExternalLogic(700, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusNull;
                n = 0;
            });
            _visitor.RegExternalLogic(701, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.RedDotStatusNull;
                n = 0;
            });

            _visitor.Clear();
            Assert.IsFalse(_visitor.CheckHasExternalId(700));
            Assert.IsFalse(_visitor.CheckHasExternalId(701));
        }
    }
}
