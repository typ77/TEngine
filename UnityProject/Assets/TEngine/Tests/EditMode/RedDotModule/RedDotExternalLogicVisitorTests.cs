using GameLogic.RedDot.Core;
using NUnit.Framework;

namespace TEngine.Tests.RedDot
{
    /// <summary>
    /// RedDotExternalLogicVisitor 单元测试。
    /// 重点验证修复后的 CheckHasExternalId 同时查两个字典。
    /// </summary>
    [TestFixture]
    public class RedDotExternalLogicVisitorTests
    {
        private RedDotExternalLogicVisitor _visitor;

        [SetUp]
        public void SetUp()
        {
            _visitor = new RedDotExternalLogicVisitor();
        }

        [Test]
        public void RegAndVisit_NoId_UsesNoIdLogic()
        {
            _visitor.RegExternalLogic(100, (out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.New;
                n = 3;
            });

            _visitor.Visit(100, 0, out var status, out var num);

            Assert.AreEqual(RedDotStatus.New, status);
            Assert.AreEqual(3, num);
        }

        [Test]
        public void RegAndVisit_WithId_UsesWithIdLogic()
        {
            _visitor.RegExternalLogic(200, (uint id, out RedDotStatus s, out int n) =>
            {
                s = RedDotStatus.Number;
                n = (int)id * 2;
            });

            _visitor.Visit(200, 5, out var status, out var num);

            Assert.AreEqual(RedDotStatus.Number, status);
            Assert.AreEqual(10, num);
        }

        [Test]
        public void CheckHasExternalId_WithIdOnly_ReturnsTrue_AfterFix()
        {
            // 修复前：只查无 id 字典，带 id 注册会返回 false（缺陷）
            _visitor.RegExternalLogic(300, (uint id, out RedDotStatus s, out int n) =>
            {
                s = 0; n = 0;
            });

            Assert.IsTrue(_visitor.CheckHasExternalId(300), "修复后应同时检查带 id 字典");
        }

        [Test]
        public void CheckHasExternalId_NoIdOnly_ReturnsTrue()
        {
            _visitor.RegExternalLogic(400, (out RedDotStatus s, out int n) =>
            {
                s = 0; n = 0;
            });

            Assert.IsTrue(_visitor.CheckHasExternalId(400));
        }

        [Test]
        public void CheckHasExternalId_NotRegistered_ReturnsFalse()
        {
            Assert.IsFalse(_visitor.CheckHasExternalId(999));
        }

        [Test]
        public void UnRegExternalLogic_RemovesBothDictionaries()
        {
            _visitor.RegExternalLogic(500, (out RedDotStatus s, out int n) => { s = 0; n = 0; });
            _visitor.RegExternalLogic(500, (uint id, out RedDotStatus s, out int n) => { s = 0; n = 0; });

            _visitor.UnRegExternalLogic(500);

            Assert.IsFalse(_visitor.CheckHasExternalId(500));
        }

        [Test]
        public void Visit_Unregistered_ReturnsNullState()
        {
            _visitor.Visit(888, 0, out var status, out var num);

            Assert.AreEqual(RedDotStatus.Null, status);
            Assert.AreEqual(0, num);
        }

        [Test]
        public void Visit_IdZero_PrefersNoIdLogic()
        {
            _visitor.RegExternalLogic(600, (out RedDotStatus s, out int n) => { s = RedDotStatus.New; n = 1; });
            _visitor.RegExternalLogic(600, (uint id, out RedDotStatus s, out int n) => { s = RedDotStatus.Full; n = 2; });

            _visitor.Visit(600, 0, out var status, out var num);

            Assert.AreEqual(RedDotStatus.New, status, "id==0 时应优先用无 id 版本");
        }
    }
}
