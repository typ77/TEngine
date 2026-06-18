using System.Collections.Generic;
using GameLogic.DataBinding;
using TEngine;

namespace GameLogic
{
    /// <summary>Example06_LoopTabBarUI 的数据上下文：构建 Tab 列表（基于 Model.TabCount）。
    /// Example06 数据静态（Tab 文本不随 Model 变化），DataContext 仅承担构建，无响应式订阅。</summary>
    [DataContext(typeof(Example06_LoopTabBarUI))]
    public class Example06_LoopTabBarDataContext : DataContext<Example06_LoopTabBarUI>
    {
        /// <summary>供 View 消费的 Tab 列表（Model→View 转换产物）。</summary>
        public readonly List<Example06_LoopTabBarItemData> Items = new(Example06_LoopTabBarModel.TabCount);

        public Example06_LoopTabBarDataContext()
        {
            for (int i = 0; i < Example06_LoopTabBarModel.TabCount; i++)
            {
                Items.Add(new Example06_LoopTabBarItemData(Utility.Text.Format("Tab {0}", i)));
            }
        }
    }
}
