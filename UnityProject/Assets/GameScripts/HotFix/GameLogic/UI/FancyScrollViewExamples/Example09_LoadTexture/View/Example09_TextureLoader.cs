using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TEngine;
using UnityEngine;
using UnityEngine.Networking;

namespace GameLogic
{
    /// <summary>
    /// Example09 网络贴图加载器（移植自原版 TextureLoader，改用 UniTask + CancellationToken）。
    /// <para>
    /// 特性：Dictionary 缓存命中即返回；并发请求同 URL 去重（合并回调）；LRU 上限避免内存无限增长；
    /// Cell 复用时传入 CancellationToken 取消旧请求。
    /// </para>
    /// <para>作为单例 MonoBehaviour 存在，首次访问时自动创建 GameObject 挂载。</para>
    /// </summary>
    public class Example09_TextureLoader : MonoBehaviour
    {
        /// <summary>缓存贴图上限（LRU），超过后销毁最早未使用的贴图。</summary>
        private const int CacheCapacity = 32;

        private static Example09_TextureLoader _instance;

        // url -> 贴图缓存（LinkedHashMap 语义：最近访问移至末尾）
        private readonly LinkedList<string> _lruOrder = new();
        private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new();
        private readonly Dictionary<string, Texture> _cache = new();

        // url -> 进行中的任务（并发去重）
        private readonly Dictionary<string, List<Action<Texture>>> _pending = new();

        /// <summary>获取单例（不存在则创建）。</summary>
        public static Example09_TextureLoader Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = FindObjectOfType<Example09_TextureLoader>();
                if (_instance == null)
                {
                    var go = new GameObject(nameof(Example09_TextureLoader));
                    _instance = go.AddComponent<Example09_TextureLoader>();
                    DontDestroyOnLoad(go);
                }

                return _instance;
            }
        }

        /// <summary>
        /// 异步加载贴图。缓存命中同步返回；进行中则等待合并回调；否则发起网络请求。
        /// </summary>
        /// <param name="url">贴图 URL。</param>
        /// <param name="ct">取消令牌（Cell 复用时取消）。</param>
        /// <returns>加载的 Texture；失败或取消返回 null。</returns>
        public async UniTask<Texture> LoadAsync(string url, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(url)) return null;

            // 1. 缓存命中
            if (_cache.TryGetValue(url, out var cached))
            {
                TouchLru(url);
                return cached;
            }

            // 2. 进行中：等待合并
            if (_pending.ContainsKey(url))
            {
                var tcs = new UniTaskCompletionSource<Texture>();
                _pending[url].Add(tex => tcs.TrySetResult(tex));
                try
                {
                    return await tcs.Task.AttachExternalCancellation(ct);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }

            // 3. 发起新请求
            _pending[url] = new List<Action<Texture>>();
            Texture result = null;
            try
            {
                result = await DownloadTexture(url, ct);
            }
            catch (OperationCanceledException)
            {
                _pending.Remove(url);
                return null;
            }
            catch (Exception e)
            {
                Log.Error($"[Example09_TextureLoader] 下载失败: {url}, {e.Message}");
                _pending.Remove(url);
                return null;
            }

            // 写缓存
            if (result != null)
            {
                PutCache(url, result);
            }

            // 触发合并回调
            if (_pending.TryGetValue(url, out var callbacks))
            {
                _pending.Remove(url);
                foreach (var cb in callbacks)
                {
                    cb?.Invoke(result);
                }
            }

            return result;
        }

        private static async UniTask<Texture> DownloadTexture(string url, CancellationToken ct)
        {
            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                await request.SendWebRequest().ToUniTask(cancellationToken: ct);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Log.Error($"[Example09_TextureLoader] {request.error} ({url})");
                    return null;
                }

                return ((DownloadHandlerTexture)request.downloadHandler).texture;
            }
        }

        private void PutCache(string url, Texture texture)
        {
            if (_cache.ContainsKey(url))
            {
                _cache[url] = texture;
                TouchLru(url);
                return;
            }

            // LRU 淘汰
            while (_cache.Count >= CacheCapacity && _lruOrder.Count > 0)
            {
                var oldest = _lruOrder.First.Value;
                _lruOrder.RemoveFirst();
                _lruNodes.Remove(oldest);
                if (_cache.TryGetValue(oldest, out var t))
                {
                    Destroy(t);
                    _cache.Remove(oldest);
                }
            }

            _cache[url] = texture;
            _lruNodes[url] = _lruOrder.AddLast(url);
        }

        private void TouchLru(string url)
        {
            if (_lruNodes.TryGetValue(url, out var node))
            {
                _lruOrder.Remove(node);
                _lruNodes[url] = _lruOrder.AddLast(url);
            }
        }

        private void OnDestroy()
        {
            foreach (var kv in _cache)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }

            _cache.Clear();
            _lruOrder.Clear();
            _lruNodes.Clear();
            _pending.Clear();
            _instance = null;
        }
    }
}
