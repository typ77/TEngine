using System.Collections.Generic;
using GameLogic.DataBinding;
using TEngine;

namespace GameLogic
{
    /// <summary>Example01_BasicUI 的数据上下文：订阅 Model.ItemIds，维护供 View 消费的列表。
    /// 列表场景下 DataContext 基类不支持「N 源→1 目标」映射，故每个 ItemId 借一个 carrier
    /// 绑定托管订阅生命周期（详见构造函数注释）；对外以 ItemsRevision 作为刷新信号。</summary>
    [DataContext(typeof(Example01_BasicUI))]
    public class Example01_BasicDataContext : DataContext<Example01_BasicUI>
    {
        /// <summary>供 View 消费的列表（Model→View 转换产物）。</summary>
        public readonly List<Example01_BasicItemData> Items = new(Example01_BasicModel.ItemCount);

        /// <summary>列表变更版本号：View 绑定它刷新 scroller。同帧多次推进由 BatchScheduler 合并为一次刷新。</summary>
        public readonly BindableProperty<int> ItemsRevision = new(0);

        public Example01_BasicDataContext()
        {
            var model = Singleton<Example01_BasicModel>.Instance;
            for (int i = 0; i < Example01_BasicModel.ItemCount; i++)
            {
                int idx = i;
                int id = model.ItemIds[idx].Value;
                Items.Add(new Example01_BasicItemData(id) { DisplayText = Utility.Text.Format("Cell {0} id:{1}", idx, id) });

                // carrier 仅作 MapProperty 的 target，借基类托管订阅生命周期（Dispose 自动取消）。
                // converter 内维护 Items[idx] 并推进 ItemsRevision 作为对外输出信号。
                var carrier = new BindableProperty<int>(id);
                MapProperty(model.ItemIds[idx], carrier, newId =>
                {
                    Items[idx].Id = newId;
                    Items[idx].DisplayText = Utility.Text.Format("Cell {0} id:{1}", idx, newId);
                    ItemsRevision.Value++;
                    return newId; // carrier 值无业务意义，原样返回
                });
            }
        }
    }
}
