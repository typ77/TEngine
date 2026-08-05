using System.Linq;
using GameLogic.RedDot.Core;
using UnityEngine;

namespace GameLogic.RedDot.Editor.Data
{
    /// <summary>
    /// 编辑器侧的红点图：在 <see cref="BaseRedDotGraph"/> 基础上增加顶点删除（带 rank 重映射）。
    /// <para>运行时不使用此类（运行时叶子节点走软删除）；仅编辑器可视化需要真正删除顶点。</para>
    /// </summary>
    public class EditorRedDotGraph : BaseRedDotGraph
    {
        /// <summary>修改红点类型。</summary>
        public bool ChangeRedDotType(string key, int redDotType)
        {
            var v = GetRedDotByKey(key);
            if (v == null) return false;
            v.RedDotType = redDotType;
            return true;
        }

        public int RemoveEdge(string vex1, string vex2)
        {
            if (!VertexDict.TryGetValue(vex1, out int v)) return -1;
            if (!VertexDict.TryGetValue(vex2, out int u)) return -1;
            // Remove(v,u) 返回边数据（int），这里转成是否成功
            Remove(v, u);
            return 0;
        }

        /// <summary>修改红点的 key，key 不能重名。</summary>
        public bool ChangeRedDotKey(string oldStr, string newStr)
        {
            if (CheckHasRedDotByKey(newStr)) return false;
            var v = GetRedDotByKey(oldStr);
            if (v != null)
            {
                v.Key = newStr;
                VertexDict[newStr] = VertexDict[oldStr];
                VertexDict.Remove(oldStr);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 删除顶点及其关联边。重映射所有 > r 的 rank（含 VertexDict）。
        /// </summary>
        public override Core.RedDotVertex Remove(int r)
        {
            var vertex = GetVertex(r);
            var temp = VertexDict.Keys.ToList();
            foreach (var key in temp)
            {
                if (VertexDict[key] > r)
                {
                    VertexDict[key] = VertexDict[key] - 1;
                }
            }
            VertexDict.Remove(vertex.Key);
            return base.Remove(r);
        }

        /// <summary>通过 key 删除顶点。</summary>
        public Core.RedDotVertex RemoveByKey(string key)
        {
            if (VertexDict.TryGetValue(key, out int index))
            {
                return Remove(index);
            }
            return null;
        }

        /// <summary>获取顶点所在 rank，-1 为失败。</summary>
        public int GetVertexRank(string vexKey)
        {
            if (!VertexDict.TryGetValue(vexKey, out int v)) return -1;
            return v;
        }
    }
}
