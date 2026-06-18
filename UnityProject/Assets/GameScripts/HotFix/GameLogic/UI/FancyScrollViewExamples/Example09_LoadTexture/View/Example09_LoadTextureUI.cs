using System;
using GameLogic.DataBinding;
using TEngine;

namespace GameLogic
{
    /// <summary>
    /// Example09 LoadTexture 示例（Scroller 路径 + 网络贴图异步加载）。
    /// </summary>
    /// <remarks>
    /// 数据流：DataContext 构建 10 条贴图数据（Meta→Items）→ View.UpdateData；Cell 异步加载网络贴图。
    /// 复用 Example03 的 UI prefab 骨架：09 仅需 Close 按钮，多余的 Random/Prev/Next 在 OnCreate 隐藏。
    /// </remarks>
    [Window(UILayer.UI)]
    [DataContext(typeof(Example09_LoadTextureDataContext))]
    public partial class Example09_LoadTextureUI
    {
        // 09 不使用 Random/Prev/Next，空实现（按钮在 OnCreate 隐藏）
        private partial void OnClickRandomBtn() { }

        /// <summary>Close 按钮：关闭本窗口。</summary>
        private partial void OnClickCloseBtn() => GameModule.UI.CloseUI<Example09_LoadTextureUI>();

        private partial void OnClickPrevBtn() { }

        private partial void OnClickNextBtn() { }

        /// <summary>隐藏 09 不需要的复用按钮；加载 prefab 后用 DataContext 数据渲染。</summary>
        protected override async void OnCreate()
        {
            // 隐藏 09 不需要的复用按钮
            m_btnRandom.gameObject.SetActive(false);
            m_btnPrev.gameObject.SetActive(false);
            m_btnNext.gameObject.SetActive(false);

            m_scrollerController.SetCellFactory(() => new Example09_LoadTextureCell());

            try
            {
                await m_scrollerController.LoadCellPrefabAsync("Example09_LoadTextureCell");
                m_scrollerController.UpdateData(GetDataContext<Example09_LoadTextureDataContext>().Items);
                m_scrollerController.JumpTo(0);
            }
            catch (Exception e)
            {
                // 捕获异步加载异常：async void 的异常会冒泡到同步上下文难以追踪。
                Log.Error($"[Example09] OnCreate 初始化失败: {e}");
            }
        }
    }
}
