using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>Example03 业务层：构建循环列表数据（无随机化，纯循环演示）。</summary>
    public class Example03_InfiniteScrollService : Singleton<Example03_InfiniteScrollService>
    {
        /// <summary>根据 Model 数据构建 ItemData 列表。</summary>
        public List<Example03_InfiniteScrollItemData> BuildItems()
        {
            var model = Singleton<Example03_InfiniteScrollModel>.Instance;
            var items = new List<Example03_InfiniteScrollItemData>(Example03_InfiniteScrollModel.ItemCount);
            for (int i = 0; i < Example03_InfiniteScrollModel.ItemCount; i++)
            {
                var item = new Example03_InfiniteScrollItemData(model.ItemIds[i].Value);
                item.DisplayText = $"Cell {i}";
                items.Add(item);
            }

            return items;
        }
    }
}
