namespace GameLogic
{
    /// <summary>Example06 数据模型，持有 Tab 数量。
    /// 选中态不在 Model（UI 交互态，归 FancySelectionContext 广播）。</summary>
    public class Example06_LoopTabBarModel : Singleton<Example06_LoopTabBarModel>
    {
        /// <summary>Tab 总数（与内容面板数量一致）。</summary>
        public const int TabCount = 4;
    }
}
