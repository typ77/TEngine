using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;


/// <summary>
/// 配置加载器。
/// </summary>
public class ConfigSystem
{
    //多语言表加载，同一时间只有一个语言配置文件
    //其他itemconfig的多语言项，根据他当前id获取多语言文字
    private LanguageConfig m_languageConfig;
    //存储原生二进制数据
    private Dictionary<string, ConfigTableBase> m_tableCache = new Dictionary<string, ConfigTableBase>();
    private static ConfigSystem _instance;
    public static ConfigSystem Instance => _instance ??= new ConfigSystem();
    
    private static IResourceModule _resource;
    public IResourceModule ResourceModule => _resource ??= ModuleSystem.GetModule<IResourceModule>();
    
    private const string DefaultLanguage = "Language_{0}";

    // 多语言缺失 id 负缓存：同一缺失文案只在首次渲染时报一次日志（重载语言表时清空）
    private readonly HashSet<int> _missLanguageIds = new HashSet<int>();
    
    public async UniTask InitLanguage(Language? targetLanguage = null)
    {
        //_resourceModule = ModuleSystem.GetModule<IResourceModule>();

        var localizationModule = ModuleSystem.GetModule<ILocalizationModule>();
        // 支持显式指定目标语言（切语言时使用）：先加载数据再切换，
        // 避免在数据加载前过早 SetLanguage 触发 LocalizeAll 导致翻译/资源引用丢失
        var lang = targetLanguage ?? localizationModule.Language;

        var path = Utility.Text.Format(DefaultLanguage, lang.ToString());

        await localizationModule.LoadLanguage(lang.ToString(), true);
        //加载多语言表
        var textAsset = await ResourceModule.LoadAssetAsync<TextAsset>(path);
        if (textAsset == null)
        {
            Log.Error(Utility.Text.Format("加载{0}多语言表失败.",path));
            // 加载失败时保留旧的语言表，避免清空导致所有文案丢失
            return;
        }
        // 解析成功后再整体替换，避免中途异常把语言表置空；
        // 旧语言缺失的 key 在新语言表可能存在，负缓存随表替换一并失效
        var newConfig = new LanguageConfig();
        newConfig.SetTextAsset(textAsset);
        m_languageConfig = newConfig;
        _missLanguageIds.Clear();
    }

    public async UniTask InitConfig(object param)
    {
        // ResourceModule 未就绪时直接放弃，保留旧配置，避免 NRE
        if (ResourceModule == null)
        {
            Log.Error("ResourceModule 未就绪，跳过配置表加载");
            return;
        }

        var infos = ResourceModule.GetAssetInfos("Configs");
        var handles = new List<UniTask<TextAsset>>();
        if (infos != null)
        {
            foreach (var info in infos)
            {
                if (info.Address == null)
                {
                    continue;
                }
                // Configs tag 下可能混入非配置表资产（如红点图 RedDotGraphConfig.json，由红点模块按地址自行加载），
                // 配置表产物固定为 *.bytes，非 bytes 直接跳过，避免误扫与"未找到配置表类型"噪音告警
                if (string.IsNullOrEmpty(info.AssetPath) ||
                    !info.AssetPath.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                handles.Add(ResourceModule.LoadAssetAsync<TextAsset>(info.Address));
            }
        }

        var loadedTableCache = new Dictionary<string, ConfigTableBase>();

        try
        {
            // 先等待所有任务完成
            TextAsset[] assets = await UniTask.WhenAll(handles);

            if (assets == null)
            {
                Log.Error("配置表加载结果为空");
                return;
            }

            // 处理加载完成的配置表
            var assembly = typeof(ConfigSystem).Assembly;
            foreach (var asset in assets)
            {
                if (asset == null)
                {
                    Log.Warning("单个配置表加载结果为空，跳过");
                    continue;
                }

                // 注册表优先：免 Assembly.GetType + Activator.CreateInstance 的反射开销，
                // 并消除"类名/命名空间/程序集"隐式字符串契约（config-system-reflection-analysis P3）
                if (!ConfigTableRegistry.TryCreate(asset.name, out var dataClass))
                {
                    // 注册表未命中（新表未重新导表更新注册表）时回落反射建表并告警，保证过渡期不炸
                    var dataType = assembly.GetType(Utility.Text.Format("TEngine.{0}", asset.name));
                    if (dataType == null)
                    {
                        Log.Warning("未找到配置表类型 TEngine.{0}，跳过", asset.name);
                        // 该资产不会进表缓存，必须对称释放句柄（成功路径由 SetTextAsset 内部释放）
                        ResourceModule.UnloadAsset(asset);
                        continue;
                    }

                    dataClass = (ConfigTableBase)Activator.CreateInstance(dataType);
                    Log.Warning("配置表 {0} 未在 ConfigTableRegistry 注册，已回落反射创建，请重新导表更新注册表", asset.name);
                }

                try
                {
                    dataClass.SetTextAsset(asset);
                    // 用索引赋值而非 Add，避免同名重复键抛异常
                    loadedTableCache[asset.name] = dataClass;
                }
                catch (Exception e)
                {
                    // 单张表解析失败不整体崩溃（小游戏切语言重启时偶发资源被回收），
                    // 跳过该表，其余表继续加载；解析中断未走完 SetTextAsset 内部释放，这里对称补一次
                    Log.Error("解析配置表 {0} 失败: {1}", asset.name, e);
                    ResourceModule.UnloadAsset(asset);
                }
            }
        }
        catch (Exception e)
        {
            // WhenAll 整体异常（如加载任务内部出错）时保留旧 m_tableCache，避免游戏无配置可用
            Log.Error("配置表加载异常，保留旧配置: {0}", e);
            return;
        }

        // 各表处理完毕后再整体替换缓存，避免中途出现空窗期
        var newCache = new Dictionary<string, ConfigTableBase>(loadedTableCache.Count);
        foreach (var kv in loadedTableCache)
        {
            newCache[kv.Key] = kv.Value;
        }
        m_tableCache = newCache;
        _configLoadVersion++;
    }

    /// <summary>
    /// 配置加载版本号：每次 InitConfig 成功整体替换表缓存后自增（含切语言/重登重载）。
    /// 派生缓存（如 DrawCardGroupCfgLoader）通过比对版本号自行失效，避免程序集反向依赖。
    /// </summary>
    public int ConfigLoadVersion => _configLoadVersion;
    private int _configLoadVersion;
    /// <summary>
    /// 泛型类型名静态缓存：typeof(T).Name 在 Mono/IL2CPP 下每次访问都会新建字符串，
    /// GetConfig 是每帧数百次的高频调用，必须缓存（gc-solutions/14 §3.1）。
    /// </summary>
    private static class TypeNameCache<T>
    {
        // ReSharper disable once StaticMemberInGenericType
        public static readonly string Value = typeof(T).Name;
    }

    /// <summary>
    /// 获取配置表
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public T GetConfig<T>() where T : ConfigTableBase
    {
        // InitConfig 完成前 m_tableCache 可能未就绪，做空保护避免 NRE
        if (m_tableCache == null)
        {
            return null;
        }

        var name = TypeNameCache<T>.Value;
        if(m_tableCache.TryGetValue(name, out var table))
        {
            return table as T;
        }

        return null;
    }
    
    /// <summary>
    /// 获取多语言字符串
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public string GetLanguageString(int id)
    {
        if (m_languageConfig == null)
        {
            return string.Empty;
        }

        // 已确认缺失的 id 直接返回，避免刷新循环里逐帧重复分配日志
        if (_missLanguageIds.Contains(id))
        {
            return string.Empty;
        }

        // 表内 miss 日志关闭（silentIfAbsent），由本方法统一打一条
        var item = m_languageConfig.GetItem(id, true);
        if (item == null)
        {
            _missLanguageIds.Add(id);
            Log.Error("未找到ID为{0}的多语言字符串", id);
            return string.Empty;
        }

        return item.Default;
    }
    
}