using System.Collections.Generic;

namespace GameLogic
{
    /// <summary>Example07 业务层：按数量构建列表数据。</summary>
    public class Example07_ScrollRectService : Singleton<Example07_ScrollRectService>
    {
        /// <summary>构建指定数量的 ItemData 列表。</summary>
        public List<Example07_ScrollRectItemData> BuildItems(int count)
        {
            var items = new List<Example07_ScrollRectItemData>(count);
            for (int i = 0; i < count; i++)
            {
                items.Add(new Example07_ScrollRectItemData(i) { DisplayText = $"Cell {i}" });
            }

            return items;
        }
    }
}
