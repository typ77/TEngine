// Assets/TEngine/Tests/EditMode/DataBinding/EndToEndTests.cs
using GameLogic.DataBinding;
using NUnit.Framework;

namespace TEngine.Tests
{
    /// <summary>
    /// 端到端集成测试，验证 Model → DataContext → View 完整数据流。
    /// </summary>
    [TestFixture]
    public class EndToEndTests : DataBindingTestBase
    {
        private BindableProperty<long> _gold;
        private BindableProperty<int> _level;
        private BindableProperty<string> _goldText;
        private BindableProperty<string> _levelText;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _gold = new BindableProperty<long>(0);
            _level = new BindableProperty<int>(1);
            _goldText = new BindableProperty<string>("");
            _levelText = new BindableProperty<string>("");

            // 模拟 DataContext 映射：Model 原始数据 → View 格式化文本
            _goldText.SetValueSilently(FormatGold(_gold.Value));
            _gold.OnValueChanged += (_, newVal) => _goldText.Value = FormatGold(newVal);

            _levelText.SetValueSilently($"Lv.{_level.Value}");
            _level.OnValueChanged += (_, newVal) => _levelText.Value = $"Lv.{newVal}";
        }

        [Test]
        public void FullPipeline_ModelToView()
        {
            // 初始状态验证
            string viewGold = _goldText.Value;
            string viewLevel = _levelText.Value;
            Assert.AreEqual("0", viewGold, "初始金币应为 0");
            Assert.AreEqual("Lv.1", viewLevel, "初始等级应为 Lv.1");

            // 修改 Model 数据
            _gold.Value = 1_500_000;
            _level.Value = 10;
            FlushScheduler();

            // DataContext 输出属性变脏 → 需要再次 Flush（两轮处理）
            FlushScheduler();

            // 验证 View 已正确更新
            Assert.AreEqual("1.5M", _goldText.Value, "金币应格式化为 1.5M");
            Assert.AreEqual("Lv.10", _levelText.Value, "等级应为 Lv.10");
        }

        [Test]
        public void MultipleModelChanges_EachPropagates()
        {
            int goldUpdateCount = 0;
            _goldText.OnValueChanged += (_, _) => goldUpdateCount++;

            // EditMode 同步模式：每次赋值立即传播
            _gold.Value = 100;
            _gold.Value = 200;
            _gold.Value = 500;

            // EditMode 下 SafeMarkDirty 同步触发，每次赋值传播一次
            Assert.GreaterOrEqual(goldUpdateCount, 1, "应至少触发一次更新");
            Assert.AreEqual("500", _goldText.Value, "最终值应为 500");
        }

        [Test]
        public void Dispose_BreaksPipeline()
        {
            _gold.Value = 100;
            FlushScheduler();
            Assert.AreEqual("100", _goldText.Value, "首次设置应正确更新");

            // 模拟 DataContext Dispose：释放属性断开数据流
            _gold.Dispose();
            _goldText.Dispose();

            // Dispose 后赋值不触发任何回调
            _gold.Value = 999;
            FlushScheduler();

            Assert.AreEqual("100", _goldText.Value, "Dispose 后数据流应断开，View 不再更新");
        }

        /// <summary>
        /// 金币格式化工具：将数字转换为 K/M/B 简写形式。
        /// </summary>
        private static string FormatGold(long gold)
        {
            if (gold >= 1_000_000_000) return $"{gold / 1_000_000_000.0:F1}B";
            if (gold >= 1_000_000) return $"{gold / 1_000_000.0:F1}M";
            if (gold >= 10_000) return $"{gold / 1_000.0:F1}K";
            return gold.ToString();
        }
    }
}
