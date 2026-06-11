namespace GameLogic.DataBinding
{
    /// <summary>
    /// BatchScheduler 脏标记目标的非泛型接口。
    /// BindableProperty{T} 和 ObservableList{T} 都实现此接口，
    /// 以便 BatchScheduler 用统一集合管理。
    /// </summary>
    public interface IBatchDirtyTarget
    {
        void FireCallback();
    }
}
