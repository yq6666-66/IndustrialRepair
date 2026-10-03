using System;
using System.Collections.Concurrent;

namespace IndustrialRepair.BLL;

/// <summary>
/// 应用级 TTL 缓存（NR9）：识别特征库/配件列表跨实例共享。
/// 自实现（ConcurrentDictionary + 时间戳），30 秒默认 TTL；
/// 原计划用 FusionCache 但 API 版本不兼容，为不引入额外调试成本改自实现——
/// 对本场景（少量键、短 TTL、单进程）足够且零依赖。
/// </summary>
public static class AppCache
{
    private sealed record Entry(object Value, DateTime ExpiresAt);

    private static readonly ConcurrentDictionary<string, Entry> Store = new();

    /// <summary>取或查（过期/不存在时调工厂重建；工厂异常直接穿透）。</summary>
    public static T GetOrSet<T>(string key, Func<T> factory, TimeSpan? duration = null) where T : class
    {
        var ttl = duration ?? TimeSpan.FromSeconds(30);
        if (Store.TryGetValue(key, out var hit) && hit.ExpiresAt > DateTime.Now)
            return (T)hit.Value;

        var value = factory();
        Store[key] = new Entry(value, DateTime.Now.Add(ttl));
        return value;
    }

    /// <summary>配置变更后手动失效（如配件新增/编辑）。</summary>
    public static void Remove(string key) => Store.TryRemove(key, out _);

    /// <summary>测试用清空。</summary>
    public static void Clear() => Store.Clear();
}
