# 平台抽象层设计参考

> 小T的技术预研笔记 — 抹除小游戏与普通平台的差异

## 一、核心差异点

| 差异项 | 原生平台 | 微信小游戏 |
|--------|---------|-----------|
| 文件系统 | System.IO | wx.getFileSystemManager |
| 网络 | Socket/HttpClient | wx.request/wx.downloadFile |
| 资源加载 | AssetBundle | 远程下载+缓存 |
| 输入系统 | Input/InputSystem | 触摸事件 |
| 音频 | AudioSource | wx.createInnerAudioContext |
| Shader | 标准管线 | 有限子集 |
| 线程 | System.Threading | 无多线程支持 |
| 屏幕常亮 | Screen.sleepTimeout | wx.setKeepScreenOn |

## 二、抽象层设计

```csharp
// 平台抽象接口
public interface IPlatformAdapter
{
    IFileSystem FileSystem { get; }
    INetworkAdapter Network { get; }
    IAudioAdapter Audio { get; }
    IInputAdapter Input { get; }
    IStorageAdapter Storage { get; }
}

// 实现策略
// 原生: NativePlatformAdapter
// 小游戏: WXPlatformAdapter
// 平台自动选择: PlatformAdapterFactory.Create()
```

## 三、业务代码示例

```csharp
// 业务层完全不需要感知平台差异
// 小T统一了接口后，业务代码这样写：
var fileContent = GameModule.Platform.FileSystem.ReadAllBytes("config.json");
var result = await GameModule.Platform.Network.GetAsync("https://api.example.com/data");
GameModule.Platform.Storage.SetString("player_level", "10");
```