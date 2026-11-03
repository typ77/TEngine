using System.Collections.Generic;
using RedDot.Runtime.Dag;
using TEngine;

namespace RedDot.Runtime.RedDot
{
    public class BaseRedDotGraph : DAG<RedDotVertex, int>
    {
        private static uint s_time = 0;

        /// <summary>
        /// 获取当前时间——计数
        /// P4-3(N7):跨 Reset 生命周期内 uint 不回绕(42.9 亿,单会话不可达),Reset 时归零
        /// </summary>
        /// <returns></returns>
        protected static uint GetTime()
        {
            s_time++;
            return s_time;
        }

        /// <summary>P4-3:Reset 时归零时间戳计数器</summary>
        internal static void ResetTimeCounter()
        {
            s_time = 0;
        }

        /// <summary>P4-3 验证/诊断:当前时间戳计数只读访问</summary>
        internal static uint TimeForVerify => s_time;

        /// <summary>P4-1(A6):清空整图(顶点字典/顶点表/出边/入边),供 Reset 重建</summary>
        public void ClearGraph()
        {
            VertexDict.Clear();
            Vertices.Clear();
            outEdges.Clear();
            inEdges.Clear();
            VNum = 0;
            eNum = 0;
        }
        
        protected Dictionary<string, int> VertexDict = new Dictionary<string, int>();

        /// <summary>图内是否已有顶点(P3-7 重复加载防御判据)</summary>
        public bool HasVertices => VertexDict.Count > 0;

        /// <summary>图中是否存在配置顶点(P1-2 审核修订:P3-7 判据改用本属性)。dyn: 前缀=运行时动态顶点,
        /// 不属于配置——配置加载窗口期业务先行 RegisterDynamicNode 属合法时序(P0-2 重登重放即依赖它),
        /// 不得据此拒绝整份配置加载。dyn: 为动态节点保留命名空间,配置 key 不得使用该前缀。</summary>
        public bool HasConfigVertices
        {
            get
            {
                foreach (var kv in VertexDict)
                {
                    if (!kv.Key.StartsWith("dyn:"))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>P0-5:顶点 key 只读遍历(常量一致性自检反向对账用,验证器/模块同程序集)</summary>
        internal Dictionary<string, int>.KeyCollection VertexKeysForVerify => VertexDict.Keys;
        
        /// <summary>
        /// 插入顶点
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        public override int Insert(RedDotVertex v)
        {
            if (CheckHasRedDotByKey(v.Key))
            {
                Log.Warning("已经有此红点了, key: {0}", v.Key);
                return -1;
            }
            int index = base.Insert(v);
            var vertex = GetVertex(index);
            VertexDict[vertex.Key] = index;
            return index;
        }

        /// <summary>
        /// 在(vex1, vex2)之间插入权重为weight的边e
        /// </summary>
        /// <param name="vex1"></param>
        /// <param name="vex2"></param>
        /// <param name="weight"></param>
        public bool InsertEdge(string vex1, string vex2, int weight = 0)
        {
            if (!VertexDict.TryGetValue(vex1, out int v))
            {
                Log.Warning("没找到顶点：{0}", vex1);
                return false;
            }
            if (!VertexDict.TryGetValue(vex2, out int u))
            {
                Log.Warning("没找到顶点：{0}", vex2);
                return false;
            }
            return base.Insert(v, u, 0, weight);
        }

        /// <summary>
        /// 判断是否有某个key的红点
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        protected bool CheckHasRedDotByKey(string key)
        {
            return VertexDict.ContainsKey(key);
        }

        /// <summary>P6-4(T7):按 key 删除顶点(含关联边)并修正 VertexDict 秩;不存在返回 null</summary>
        public RedDotVertex RemoveVertexByKey(string key)
        {
            if (!VertexDict.TryGetValue(key, out int index))
            {
                return null;
            }
            var vertex = GetVertex(index);
            //Mono 下字典枚举中写值也会 bump 版本,先拷贝 key 列表再修正秩
            var keys = new List<string>(VertexDict.Keys);
            foreach (var k in keys)
            {
                if (VertexDict[k] > index)
                {
                    VertexDict[k] -= 1;
                }
            }
            VertexDict.Remove(key);
            Remove(index);
            return vertex;
        }

        /// <summary>按 key 删边(child, parent)。P0-0 方向对齐:库铁律边存为 (父, 子),故按
        /// Exist(u,v)/Remove(u,v) 删 parentKey→childKey;旧实现删 child→parent 方向,仅与修复前的
        /// 反向 SetDynamicParent 自洽,方向修复后必删空边。</summary>
        public bool RemoveEdgeByKey(string childKey, string parentKey)
        {
            if (!VertexDict.TryGetValue(childKey, out int v) || !VertexDict.TryGetValue(parentKey, out int u))
            {
                return false;
            }
            if (!Exist(u, v))
            {
                return false;
            }
            Remove(u, v);
            return true;
        }

        /// <summary>P0-2:边(parentKey→childKey)是否已存在(动态挂父重放去重,防重复插入 Warning)</summary>
        public bool HasEdgeByKey(string parentKey, string childKey)
        {
            return VertexDict.TryGetValue(parentKey, out int p)
                && VertexDict.TryGetValue(childKey, out int c)
                && Exist(p, c);
        }

        /// <summary>P0-2:收集 key 的全部父节点 key(快照,节点禁用/摘边/删除后对父补发用)</summary>
        public void CollectParentKeys(string key, List<string> buffer)
        {
            if (!VertexDict.TryGetValue(key, out int index))
            {
                return;
            }
            var parents = inEdges[index];
            for (int i = 0; i < parents.Count; i++)
            {
                buffer.Add(GetVertex(parents[i]).Key);
            }
        }

        /// <summary>
        /// 获取红点
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public RedDotVertex GetRedDotByKey(string key)
        {
            if (VertexDict.TryGetValue(key, out int index))
            {
                return GetVertex(index);
            }

            return null;
        }
    }
}