using System.Collections.Generic;
using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>Example09_LoadTextureUI 的数据上下文：构建贴图列表（固定 10 条，对应原版 9 Example + 主图）。
    /// 承担 Meta→Items 转换（替代 Service.BuildItems）；数据静态，构造时一次构建。</summary>
    [DataContext(typeof(Example09_LoadTextureUI))]
    public class Example09_LoadTextureDataContext : DataContext<Example09_LoadTextureUI>
    {
        // 标题/描述对应原版 9 个 Example + 主图，URL 指向 setchi.jp 资源站
        private static readonly (string Title, string Desc, string ImageFile)[] Meta =
        {
            ("FancyScrollView", "A scrollview component that can implement highly flexible animation.", "00.png"),
            ("01_Basic", "Example of simplest implementation.", "01.png"),
            ("02_FocusOn", "Example of focusing on the left and right cells with buttons.", "02.png"),
            ("03_InfiniteScroll", "Example of infinite scroll implementation.", "03.png"),
            ("04_Metaball", "Example of metaball implementation using shaders.", "04.png"),
            ("05_Voronoi", "Example of voronoi implementation using shaders.", "05.png"),
            ("06_LoopTabBar", "Example of switching screens with tabs.", "06.png"),
            ("07_ScrollRect", "Example of ScrollRect style implementation with scroll bar.", "07.png"),
            ("08_GridView", "Example of grid layout implementation.", "08.png"),
            ("09_LoadTexture", "Example of load texture implementation.", "09.png"),
        };

        /// <summary>供 View 消费的列表（Meta→Items 转换产物）。</summary>
        public readonly List<Example09_LoadTextureItemData> Items = new(Example09_LoadTextureModel.ItemCount);

        public Example09_LoadTextureDataContext()
        {
            var prefix = Example09_LoadTextureModel.ImageUrlPrefix;
            for (int i = 0; i < Meta.Length && i < Example09_LoadTextureModel.ItemCount; i++)
            {
                var m = Meta[i];
                Items.Add(new Example09_LoadTextureItemData(i)
                {
                    Title = m.Title,
                    Description = m.Desc,
                    Url = prefix + m.ImageFile,
                });
            }
        }
    }
}
