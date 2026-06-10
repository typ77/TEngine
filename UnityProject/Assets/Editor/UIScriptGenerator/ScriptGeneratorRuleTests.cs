using System.Linq;
using NUnit.Framework;
using TEngine.Editor.UI;

namespace TEngine.Editor.Tests
{
    [TestFixture]
    public class ScriptGeneratorRuleTests
    {
        [Test]
        public void UIComponentName_ContainsFancyScrollViewTypes()
        {
            Assert.IsTrue(System.Enum.IsDefined(typeof(UIComponentName), "FancyScrollRectProxy"));
            Assert.IsTrue(System.Enum.IsDefined(typeof(UIComponentName), "FancyScroller"));
        }

        [Test]
        public void DefaultRules_ContainFancyScrollPrefixesBeforeScrollRectPrefix()
        {
            var rules = ScriptGeneratorSetting.GetDefaultScriptGenerateRulesForTest();
            var fscrollIndex = rules.FindIndex(x => x.uiElementRegex == "m_fscroll");
            var scrollerIndex = rules.FindIndex(x => x.uiElementRegex == "m_scroller");
            var scrollIndex = rules.FindIndex(x => x.uiElementRegex == "m_scroll");

            Assert.GreaterOrEqual(fscrollIndex, 0, "m_fscroll rule should exist");
            Assert.GreaterOrEqual(scrollerIndex, 0, "m_scroller rule should exist");
            Assert.GreaterOrEqual(scrollIndex, 0, "m_scroll rule should exist");
            Assert.Less(fscrollIndex, scrollIndex, "m_fscroll should come before m_scroll");
            Assert.Less(scrollerIndex, scrollIndex, "m_scroller should come before m_scroll");
            Assert.AreEqual(UIComponentName.FancyScrollRectProxy, rules[fscrollIndex].componentName);
            Assert.AreEqual(UIComponentName.FancyScroller, rules[scrollerIndex].componentName);
        }

        [Test]
        public void GetComponentTypeName_MapsFancyScrollerToScroller()
        {
            Assert.AreEqual("Scroller", ScriptGeneratorSetting.GetComponentTypeName(UIComponentName.FancyScroller));
            Assert.AreEqual("FancyScrollRectProxy", ScriptGeneratorSetting.GetComponentTypeName(UIComponentName.FancyScrollRectProxy));
            Assert.AreEqual("Button", ScriptGeneratorSetting.GetComponentTypeName(UIComponentName.Button));
        }
    }
}
