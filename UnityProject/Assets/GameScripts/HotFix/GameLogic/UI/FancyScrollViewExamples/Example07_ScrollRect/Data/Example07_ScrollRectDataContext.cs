using System.Collections.Generic;
using GameLogic.DataBinding;
using TEngine;

namespace GameLogic
{
    /// <summary>Example07_ScrollRectUI 的数据上下文：按数量构建列表（数据数量由 UI InputField 输入，非 Model 响应式）。
    /// 承担 count→Items 转换（替代 Service.BuildItems）；UI 同步 Rebuild 后立即用 Items 渲染。
    /// 因 dataCount 来自 UI 输入且需同步 JumpTo 定位，不走异步 ItemsRevision（避免刷新/定位时序错位）。</summary>
    [DataContext(typeof(Example07_ScrollRectUI))]
    public class Example07_ScrollRectDataContext : DataContext<Example07_ScrollRectUI>
    {
        /// <summary>供 View 消费的列表（count→Items 转换产物）。</summary>
        public readonly List<Example07_ScrollRectItemData> Items = new();

        /// <summary>按数量重建列表（同步，UI 调后立即用 Items）。</summary>
        public void Rebuild(int count)
        {
            Items.Clear();
            for (int i = 0; i < count; i++)
            {
                Items.Add(new Example07_ScrollRectItemData(i) { DisplayText = Utility.Text.Format("Cell {0}", i) });
            }
        }
    }
}
