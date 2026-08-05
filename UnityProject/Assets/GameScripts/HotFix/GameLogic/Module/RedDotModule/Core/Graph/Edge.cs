namespace GameLogic.RedDot.Core.Graph
{
    /// <summary>
    /// 图边，<see cref="ToVertexRank"/> 指向目标顶点在顶点表中的下标。
    /// </summary>
    /// <typeparam name="TTe">边数据类型</typeparam>
    public class Edge<TTe>
    {
        public TTe Data;
        public int ToVertexRank;
        public int Weight;
        public EType Etype = EType.Undetermined;

        public Edge(int toVertexRank, TTe d = default(TTe), int weight = 0)
        {
            this.ToVertexRank = toVertexRank;
            Data = d;
            this.Weight = weight;
        }
    }
}
