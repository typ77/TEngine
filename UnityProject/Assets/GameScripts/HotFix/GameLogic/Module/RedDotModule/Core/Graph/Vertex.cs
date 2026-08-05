namespace GameLogic.RedDot.Core.Graph
{
    /// <summary>
    /// 图顶点包装类，<see cref="Data"/> 为实际顶点数据，其余字段用于图遍历算法。
    /// </summary>
    /// <typeparam name="TTv">顶点数据类型</typeparam>
    public class Vertex<TTv>
    {
        public TTv Data;
        public VStatus Status = VStatus.Undiscovered;
        public int DTime = -1;
        public int FTime = -1;
        public int Parent = -1;
        public int Priority = int.MaxValue;

        public Vertex(TTv d = default(TTv))
        {
            Data = d;
        }
    }
}
