using System.Collections.Generic;
using GameLogic.DataBinding;
using TEngine;

namespace GameLogic
{
    /// <summary>Example03_InfiniteScrollUI 的数据上下文：订阅 Model.ItemIds，维护供 View 消费的列表。
    /// Example03 为纯循环演示（数据静态、无随机化），但仍按 carrier 模式订阅以保持架构一致、便于扩展。
    /// 列表场景下 DataContext 基类不支持「N 源→1 目标」映射，每个 ItemId 借一个 carrier 托管订阅生命周期
    /// （详见 mve-patterns.md 第八章）；对外以 ItemsRevision 作为刷新信号。</summary>
    [DataContext(typeof(Example03_InfiniteScrollUI))]
    public class Example03_InfiniteScrollDataContext : DataContext<Example03_InfiniteScrollUI>
    {
        /// <summary>供 View 消费的列表（Model→View 转换产物）。</summary>
        public readonly List<Example03_InfiniteScrollItemData> Items = new(Example03_InfiniteScrollModel.ItemCount);

        /// <summary>列表变更版本号：View 绑定它刷新 scroller。同帧多次推进由 BatchScheduler 合并为一次刷新。</summary>
        public readonly BindableProperty<int> ItemsRevision = new(0);

        public Example03_InfiniteScrollDataContext()
        {
            var model = Singleton<Example03_InfiniteScrollModel>.Instance;
            for (int i = 0; i < Example03_InfiniteScrollModel.ItemCount; i++)
            {
                int idx = i;
                int id = model.ItemIds[idx].Value;
                Items.Add(new Example03_InfiniteScrollItemData(id) { DisplayText = Utility.Text.Format("Cell {0}", idx) });

                // carrier 仅作 MapProperty 的 target，借基类托管订阅生命周期（Dispose 自动取消）。
                // converter 内更新 Id 并推进 ItemsRevision（DisplayText 仅依赖 idx，构造时已确定）。
                var carrier = new BindableProperty<int>(id);
                MapProperty(model.ItemIds[idx], carrier, newId =>
                {
                    Items[idx].Id = newId;
                    ItemsRevision.Value++;
                    return newId;
                });
            }
        }
    }
}
