using UnityEngine;

namespace GameLogic
{
    /// <summary>Example01 业务层：随机化相邻条目 ID。</summary>
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
    }
}
