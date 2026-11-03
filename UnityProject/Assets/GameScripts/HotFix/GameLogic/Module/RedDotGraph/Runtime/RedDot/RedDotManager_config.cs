#if UNITY_EDITOR
using System.IO;
using System.Text;
#endif
using System.Collections.Generic;
using UnityEngine;
using TEngine;

namespace RedDot.Runtime.RedDot
{
    public partial class RedDotManager
    {
        /// <summary>
        /// 编辑器诊断入口:从磁盘读 JSON 建图。微信小游戏 File.IO 不可用,运行时唯一入口是 AddJsonStr。
        /// </summary>
        /// <param name="path"></param>
        /// <param name="checkVisitor"></param>
#if UNITY_EDITOR
        public void AddJsonFile(string path, bool checkVisitor = true)
        {
            var jsonStr = File.ReadAllText(path, Encoding.UTF8);
            AddJsonStr(jsonStr, checkVisitor);
        }
#endif

        public bool AddJsonStr(string jsonStr, bool checkVisitor = true)
        {
            var cfg = JsonUtility.FromJson<RedDotExportJson>(jsonStr);
            return AddExportJson(cfg, checkVisitor);
        }

        /// <summary>
        /// 加载导出配置。返回是否全部成功:
        /// P3-6(B6)聚合本次加载全部失败项一次全量硬报错;P3-7(N10 前半)图非空拒绝整份重复加载。
        /// </summary>
        /// <param name="config"></param>
        /// <param name="checkVisitor"></param>
        /// <returns></returns>
        public bool AddExportJson(RedDotExportJson config, bool checkVisitor = true)
        {
            if (config == null)
            {
                Log.Error("AddExportJson 配置为空");
                return false;
            }
            return LoadExportJson(RedDotGrapic, config, checkVisitor);
        }

        /// <summary>加载核心(图参数化,编辑器/验证器可在裸图上复验同一路径)</summary>
        /// <param name="graph"></param>
        /// <param name="config"></param>
        /// <param name="checkVisitor"></param>
        /// <returns></returns>
        internal static bool LoadExportJson(BaseRedDotGraph graph, RedDotExportJson config, bool checkVisitor)
        {
            //P3-7:重复加载防御——图内已有配置顶点时拒绝整份加载,防重复导入产生半新半旧图;重建须先调用 Reset。
            //P1-2 审核修订:判据从 HasVertices 改为 HasConfigVertices——配置加载窗口期业务先行注册的
            //动态顶点(dyn: 前缀)不算配置,否则重登时序"先重建动态节点、配置后到"会让整份配置被拒、全会话红点失效
            if (graph.HasConfigVertices)
            {
                Log.Error("红点图已存在配置，拒绝本次重复加载；如需重建请先调用 Reset()");
                return false;
            }
            var failures = new List<string>();
            foreach (var vertexJson in config.vertexs)
            {
                var externalId = vertexJson.externalId;
                if (checkVisitor && externalId != -1 && !RedDotExternalLogicVisitor.Ins().CheckHasExternalId(externalId))
                {
                    failures.Add(Utility.Text.Format("顶点key:{0} 的externalId:{1} 未注册 checker", vertexJson.key, externalId));
                }
                if (graph.Insert(new RedDotVertex(vertexJson.key, vertexJson.redDotType, externalId)) == -1)
                {
                    failures.Add(Utility.Text.Format("重复顶点:{0}", vertexJson.key));
                }
            }

            foreach (var edgeJson in config.edges)
            {
                foreach (var outKey in edgeJson.outKeys)
                {
                    if (!graph.InsertEdge(edgeJson.key, outKey))
                    {
                        failures.Add(Utility.Text.Format("边 {0}->{1} 插入失败(未知 key 或构成环)", edgeJson.key, outKey));
                    }
                }
            }

            if (failures.Count > 0)
            {
                //P3-6(B6):任一失败,全量列表一次硬报错。
                //失败即毒化契约(Phase 3 审核登记):失败时部分顶点/边已入图,图非空将使后续加载被 P3-7 拒绝,
                //须先调用 Reset(P4-1)再重试
                Log.Error("红点配置加载失败 {0} 项:{1}", failures.Count, string.Join("; ", failures));
                return false;
            }
            return true;
        }

        /// <summary>
        /// P0-5:加载期一致性自检核心(纯函数,模块与验证器共用)。常量表 vs 图双向对账:
        /// 正向=每个常量 key 在图中存在,且 externalId 与图顶点一致(常量 extId=-1 的聚合节点跳过
        /// extId 核对——其运行时 extId 可能被 SummarizeCells 覆写为系统段);反向=图中每个配置顶点
        /// 均有常量(防"JSON 新增/改名而 dll 常量未跟上"的单向漏报,补齐 checkVisitor 只查反向的缺口)。
        /// dyn: 前缀动态顶点不入配置,双向均不参与。返回问题列表(空=一致)。
        /// </summary>
        /// <param name="graph">对账目标图(验证器传 GraphForVerify/裸图,模块传当前图)</param>
        /// <param name="keys">生成的常量表(RedDotKeys.All)</param>
        internal static List<string> ValidateKeysConsistency(BaseRedDotGraph graph, RedDotKey[] keys)
        {
            var problems = new List<string>();
            if (graph == null || keys == null || keys.Length == 0)
            {
                return problems;
            }

            //正向:常量 → 图
            var constantKeys = new HashSet<string>();
            foreach (var dk in keys)
            {
                if (dk == null || string.IsNullOrEmpty(dk.Key))
                {
                    problems.Add("常量表存在空条目");
                    continue;
                }
                if (!constantKeys.Add(dk.Key))
                {
                    problems.Add(Utility.Text.Format("常量表重复 key:{0}", dk.Key));
                    continue;
                }
                var v = graph.GetRedDotByKey(dk.Key);
                if (v == null)
                {
                    problems.Add(Utility.Text.Format("常量在图中不存在(疑似 dll 常量超前于 JSON):{0}", dk.Key));
                    continue;
                }
                //聚合节点(extId=-1)运行时可能被 SummarizeCells 覆写为系统段,跳过 extId 核对
                if (dk.ExternalId != -1 && v.ExternalId != dk.ExternalId)
                {
                    problems.Add(Utility.Text.Format("externalId 不一致 key:{0} 常量:{1} 图:{2}(疑似版本错配)",
                        dk.Key, dk.ExternalId, v.ExternalId));
                }
            }

            //反向:图配置顶点 → 常量
            foreach (var key in graph.VertexKeysForVerify)
            {
                if (key.StartsWith("dyn:"))
                {
                    continue;
                }
                if (!constantKeys.Contains(key))
                {
                    problems.Add(Utility.Text.Format("图顶点缺常量(疑似 JSON 超前于 dll 常量,业务将无收口入口):{0}", key));
                }
            }
            return problems;
        }
    }
}