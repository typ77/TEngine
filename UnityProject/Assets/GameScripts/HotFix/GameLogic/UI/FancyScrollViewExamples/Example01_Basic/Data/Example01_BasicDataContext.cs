using GameLogic.DataBinding;

namespace GameLogic
{
    /// <summary>Example01_BasicUI 的数据上下文（MVE DataContext 层）。</summary>
    [DataContext(typeof(Example01_BasicUI))]
    public class Example01_BasicDataContext : DataContext<Example01_BasicUI>
    {
    }
}
