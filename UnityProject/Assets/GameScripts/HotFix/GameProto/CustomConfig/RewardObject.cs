using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace TEngine
{
    public class RewardDataItem
    {
        public byte RewardType;
        public int CfgId;
        public long Amount;

        public static RewardDataItem Of(List<int> list)
        {
            if (list == null || list.Count < 3)
            {
                return null;
            }
            
            return new RewardDataItem()
            {
                RewardType = Convert.ToByte(list[0]),
                CfgId = list[1],
                Amount = list[2],
            };
        }

        public static List<RewardDataItem> OfList(List<List<int>> list)
        {
            if (list == null || list.Count == 0)
            {
                return new List<RewardDataItem>();
            }

            List<RewardDataItem> result = new();
            foreach (var row in list)
            {
                var item = Of(row);
                if (item != null)
                {
                    result.Add(item);
                }
            }

            return result;
        }
        
        public static RewardDataItem Of(byte rewardType, int cfgId, long amount)
        {
            return new RewardDataItem()
            {
                RewardType = rewardType,
                CfgId = cfgId,
                Amount = amount,
            };
        }

        public static bool IsSameRewardDataItem(RewardDataItem a, RewardDataItem b)
        {
            return a.RewardType == b.RewardType && a.CfgId == b.CfgId;
        }
    }
    /// <summary>
    /// 这是一个奖励对象类，用于对接配置表的自动生成数据类型
    /// 默认RewardObject是以List<List<long>>的形式传入的
    /// </summary>
    public class RewardObject
    {
        private readonly List<RewardDataItem> m_items = new List<RewardDataItem>();

        public RewardObject()
        {
            // 内部构造使用, 不要打开
        }

        public static RewardObject OfRewardDataItemList(List<RewardDataItem> list)
        {
            var rewardObject = new RewardObject();
            foreach (var rewardItem in list)
            {
                rewardObject.m_items.Add(rewardItem);
            }
            return rewardObject;
        }

        public static RewardObject OfListListObject(List<List<object>> data)
        {
            RewardObject rewardObject = new RewardObject();
            if (data == null || data.Count == 0)
            {
                return rewardObject;
            }

            foreach (List<object> list in data)
            {
                if (list.Count >= 3)
                {
                    var rewardItem = new RewardDataItem
                    {
                        RewardType = Convert.ToByte(list[0]),
                        CfgId = Convert.ToInt32(list[1]),
                        Amount = Convert.ToInt64(list[2]),
                    };
                    rewardObject.m_items.Add(rewardItem);
                }
            }
            return rewardObject;
        }

        public RewardObject(List<List<int>> datas)
        {
            if (datas == null)
            {
                return;
            }
            foreach (var item in datas)
            {
                if (item.Count >= 3)
                {
                    var rewardItem = new RewardDataItem
                    {
                        RewardType = (byte)item[0],
                        CfgId = (int)item[1],
                        Amount = item[2],
                    };
                    m_items.Add(rewardItem);
                }
            }
        }

        public RewardObject(Luban.ByteBuf byteBuf)
        {
            var listCount = byteBuf.ReadSize();
            for (var index = 0; index < listCount; index++)
            {
                // 二进制写入的是变长 list<list<long>>，这里必须按 size 忠实读取，
                // 否则 size != 3 时既丢弃数据又让游标错位（与 GradedPriceObject 同类问题）。
                var size = byteBuf.ReadSize();
                var longs = new long[size];
                for (var j = 0; j < size; j++)
                {
                    longs[j] = byteBuf.ReadLong();
                }

                // 语义: [rewardType, cfgId, amount]
                if (size >= 3)
                {
                    var rewardItem = new RewardDataItem
                    {
                        RewardType = (byte)longs[0],
                        CfgId = (int)longs[1],
                        Amount = longs[2],
                    };
                    m_items.Add(rewardItem);
                }
                // size < 3: 仍已消费完 size 个 long，游标正确，仅丢弃该条目
            }
        }
        public List<RewardDataItem> Items => m_items;
        public RewardDataItem this[int index] => m_items[index];    // 第N个 RewardDataItem


        /// 乘一个系数，并排除 amount为0的配置项
        public RewardObject Multiply(float coefficient)
        {
            RewardObject result = new RewardObject();
            foreach (var item in m_items)
            {
                if (item.Amount != 0)
                {
                    result.m_items.Add(new RewardDataItem
                    {
                        RewardType = item.RewardType,
                        CfgId = item.CfgId,
                        Amount = (long)(item.Amount * coefficient),
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// 合并两个RewardObject, 不自动去重
        /// </summary>
        public static RewardObject operator +(RewardObject a, RewardObject b)
        {
            RewardObject rewardObject = new RewardObject();
            rewardObject.m_items.AddRange(a.m_items);
            rewardObject.m_items.AddRange(b.m_items);
            return rewardObject;
        }

        /// <summary>
        /// 合并两个RewardObject并自动去重（相同RewardType和CfgId的项会累加Amount）
        /// </summary>
        public static RewardObject Merge(RewardObject a, RewardObject b)
        {
            // RewardObject merged = new RewardObject();
            // // 先添加第一个对象的所有奖励项
            // merged.m_items.AddRange(a.m_items);
            //
            // // 处理第二个对象的奖励项，检查并合并重复项
            // foreach (var item in b.m_items)
            // {
            //     // 查找是否已存在相同RewardType和CfgId的项
            //     var existingItem = merged.m_items.FirstOrDefault(
            //         x => x.RewardType == item.RewardType && x.CfgId == item.CfgId);
            //
            //     if (existingItem != null)
            //     {
            //         // 累加数量（去重合并逻辑）
            //         existingItem.Amount += item.Amount;
            //     }
            //     else
            //     {
            //         // 不存在则添加新项
            //         merged.m_items.Add(item);
            //     }
            // }

            return Merge_2(a, b);
        }

        public static RewardObject Merge_2(RewardObject a, RewardObject b)
        {
            RewardObject merged = new RewardObject();

            // 深度拷贝 a 的 items
            foreach (var item in a.m_items)
            {
                // 创建新的 RewardItem 对象，而不是使用引用
                var newItem = new RewardDataItem
                {
                    RewardType = item.RewardType,
                    CfgId = item.CfgId,
                    Amount = item.Amount
                    // 复制其他必要属性
                };
                merged.m_items.Add(newItem);
            }

            // 处理 b 的 items
            foreach (var item in b.m_items)
            {
                // 替代 FirstOrDefault(pred)（奖励合并/发放链），显式 for + break 消闭包与委托分配
                RewardDataItem existingItem = null;
                for (int i = 0; i < merged.m_items.Count; i++)
                {
                    var it = merged.m_items[i];
                    if (it.RewardType == item.RewardType && it.CfgId == item.CfgId)
                    {
                        existingItem = it;
                        break;
                    }
                }

                if (existingItem != null)
                {
                    // 修改的是新创建的对象，不影响原始对象
                    existingItem.Amount += item.Amount;
                }
                else
                {
                    // 创建新的对象而不是使用引用
                    var newItem = new RewardDataItem
                    {
                        RewardType = item.RewardType,
                        CfgId = item.CfgId,
                        Amount = item.Amount
                    };
                    merged.m_items.Add(newItem);
                }
            }

            return merged;

        }

        public static bool IsNullOrEmpty(RewardObject rewardObject)
        {
            return rewardObject == null || rewardObject.m_items.Count == 0;
        }
    }
}