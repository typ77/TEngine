using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>Example06 业务层：构建 Tab 列表数据。</summary>
    public class Example06_LoopTabBarService : Singleton<Example06_LoopTabBarService>
    {
        /// <summary>根据 Model 数据构建 ItemData 列表。</summary>
        public List<Example06_LoopTabBarItemData> BuildItems()
        {
            var items = new List<Example06_LoopTabBarItemData>(Example06_LoopTabBarModel.TabCount);
            for (int i = 0; i < Example06_LoopTabBarModel.TabCount; i++)
            {
                items.Add(new Example06_LoopTabBarItemData($"Tab {i}"));
            }

            return items;
        }
    }
}
