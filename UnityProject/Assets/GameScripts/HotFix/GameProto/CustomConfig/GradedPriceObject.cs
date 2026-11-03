using System;
using System.Collections.Generic;
using System.Linq;

namespace TEngine
{
    public class GradedPriceDataItem : RewardDataItem
    {
        public readonly RewardDataItem RewardItem;
        public readonly int GradeTimes;  // 梯度次数
        
        public GradedPriceDataItem(RewardDataItem rewardItem, int gradeTimes)
        {
            RewardItem = rewardItem;
            GradeTimes = gradeTimes;
        }
    }
    
    /// <summary>
    /// 梯度奖励对象
    /// 格式示例: [[1,10002,1,2],[1,10002,123,999]]
    /// </summary>
    public class GradedPriceObject
    {
        private readonly List<GradedPriceDataItem> m_items = new();

        /// <summary>
        /// 无参构造函数 - 供共享缓存空实例使用(如 ClickResourceConfigItem._buy_priceEmpty)。
        /// 与 RewardObject / ConditionObject / EffectCfgObject 保持一致。
        /// </summary>
        public GradedPriceObject()
        {
        }

        public GradedPriceObject(Luban.ByteBuf byteBuf)
        {
            var listCount = byteBuf.ReadSize();
            for (var i = 0; i < listCount; i++)
            {
                // 二进制写入的是变长 list<list<long>>，这里必须按 count 忠实读取，
                // 否则 count != 4 时游标会错位，把剩余 long 当作后续条目错位解析。
                var count = byteBuf.ReadSize();
                var longs = new long[count];
                for (var j = 0; j < count; j++)
                {
                    longs[j] = byteBuf.ReadLong();
                }

                // 语义: [rewardType, cfgId, amount, gradeTimes]
                if (count >= 4)
                {
                    RewardDataItem rewardItem = new RewardDataItem
                    {
                        RewardType = (byte)longs[0],
                        CfgId = (int)longs[1],
                        Amount = longs[2],
                    };
                    int gradeTimes = (int)longs[3];

                    var GradedPriceItem = new GradedPriceDataItem(rewardItem, gradeTimes);
                    m_items.Add(GradedPriceItem);
                }
            }

            m_items.Sort((a, b) => a.GradeTimes.CompareTo(b.GradeTimes));
        }

        /// <summary>
        /// 是否为 null 或空（无价格条目）。
        /// 与 RewardObject / ConditionObject / EffectCfgObject 的 IsNullOrEmpty 用法保持一致。
        /// </summary>
        public static bool IsNullOrEmpty(GradedPriceObject obj)
        {
            return obj == null || obj.m_items.Count == 0;
        }

        /// <summary>
        /// 计算价格。
        /// 空配置（无价格条目）返回 null，调用方应通过 IsNullOrEmpty 提前判断或对返回值做空检查。
        /// </summary>
        /// <param name="times"></param>
        /// <returns></returns>
        public RewardDataItem CalPrice(int times)
        {
            if (m_items.Count == 0)
            {
                return null;
            }

            foreach (var price in m_items)
            {
                if (times <= price.GradeTimes)
                {
                    return price.RewardItem;
                }
            }

            return m_items[m_items.Count - 1].RewardItem;
        }

        /// <summary>
        /// 获取梯度价格配置的最大可购买次数，空配置返回0。
        /// </summary>
        public int GetMaxGradeTimes()
        {
            return m_items.Count > 0 ? m_items[m_items.Count - 1].GradeTimes : 0;
        }

        public RewardDataItem CalPrice(int startTimes, int timesCount)
        {
            if (timesCount <= 0)
            {
                throw new ArgumentException($"timesCount({timesCount}) must be > 0.");
            }

            if (timesCount == 1)
            {
                return CalPrice(startTimes);
            }

            // 空配置无法计算区间总价，直接返回 null（与单参重载的空语义一致）。
            if (m_items.Count == 0)
            {
                return null;
            }

            long total = -1;

            RewardDataItem firstPrice = CalPrice(startTimes);
            for (var i = 2; i <= timesCount; i++)
            {
                var price = CalPrice(startTimes + i);
                if (RewardDataItem.IsSameRewardDataItem(firstPrice, price))
                {
                    total += price.Amount;
                }
                else
                {
                    throw new ArgumentException($"GradedPriceDataItem的梯度价格的货币不一致. [{firstPrice.RewardType},{firstPrice.CfgId}] != [{price.RewardType},{price.CfgId}]");
                }
            }
            
            return new RewardDataItem()
            {
                RewardType = firstPrice.RewardType,
                CfgId = firstPrice.CfgId,
                Amount = total,
            };
        }
    }
}
