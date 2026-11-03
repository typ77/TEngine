using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameLogic;
#if ENABLE_OBFUZ
using Obfuz;
#endif
using TEngine;
#pragma warning disable CS0436


/// <summary>
/// 游戏App。
/// </summary>
#if ENABLE_OBFUZ
[ObfuzIgnore(ObfuzScope.TypeName | ObfuzScope.MethodName)]
#endif
public partial class GameApp
{
    private static List<Assembly> _hotfixAssembly;

    /// <summary>
    /// 热更域App主入口。
    /// </summary>
    /// <param name="objects"></param>
    public static void Entrance(object[] objects)
    {
        GameEventHelper.Init();
        _hotfixAssembly = (List<Assembly>)objects[0];
        Log.Warning("======= 看到此条日志代表你成功运行了热更新代码 =======");
        Log.Warning("======= Entrance GameApp =======");
        Utility.Unity.AddDestroyListener(Release);
        Log.Warning("======= StartGameLogic =======");
        StartGameLogic().Forget();
    }

    private static async UniTask StartGameLogic()
    {
        //多语言初始化（LoginUI 等界面依赖配置表与多语言数据，需先于 UI 初始化）
        await ConfigSystem.Instance.InitLanguage();
        //配置初始化
        await ConfigSystem.Instance.InitConfig(null);
        //红点图初始化（EnsureStarted 幂等：首启激活；重登/切号再调即全量重建）
        RedDotGraphModule.Instance.EnsureStarted();
        // GameEvent.Get<ILoginUI>().ShowLoginUI();
        GameModule.UI.ShowUIAsync<LoginUI>();
    }
    
    private static void Release()
    {
        SingletonSystem.Release();
        Log.Warning("======= Release GameApp =======");
    }
}