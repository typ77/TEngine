using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>Example01 业务层：随机化 ID 与构建列表数据。</summary>
    public class Example01_BasicService : Singleton<Example01_BasicService>
    {
        /// <summary>随机化 centerIndex 及其前后条目的 ID（1000~9999）。</summary>
        public void RandomizeNeighborIds(int centerIndex)
        {
            var model = Singleton<Example01_BasicModel>.Instance;
            int[] indices = { centerIndex - 1, centerIndex, centerIndex + 1 };
            foreach (var idx in indices)
            {
                if (idx >= 0 && idx < Example01_BasicModel.ItemCount)
                {
                    model.ItemIds[idx].Value = Random.Range(1000, 9999);
                }
            }
        }

        /// <summary>根据 Model 数据构建 ItemData 列表。</summary>
        public List<Example01_BasicItemData> BuildItems()
        {
            var model = Singleton<Example01_BasicModel>.Instance;
            var items = new List<Example01_BasicItemData>(Example01_BasicModel.ItemCount);
            for (int i = 0; i < Example01_BasicModel.ItemCount; i++)
            {
                var item = new Example01_BasicItemData(model.ItemIds[i].Value);
                item.DisplayText = $"Cell {i} id:{item.Id}";
                items.Add(item);
            }

            return items;
        }
    }
}
