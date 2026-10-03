using System;
using System.Data.SqlClient;
using Polly;

namespace IndustrialRepair.DAL;

/// <summary>
/// SQL 瞬时故障重试管道（NR5，开源库 Polly）：
/// LocalDB 冷启动/瞬时断开（-2 超时、4060 库未就绪、233/10061 连接失败）自动重试，
/// 共 3 次尝试、常量退避 700ms——与历史手写循环行为一致，但单一实现。
/// </summary>
public static class SqlResilience
{
    private static readonly int[] RetryableNumbers = { -2, 4060, 233, 10061 };

    /// <summary>判定是否可重试的 SqlException（供管道与单测使用）。</summary>
    public static bool IsTransient(SqlException ex) => Array.IndexOf(RetryableNumbers, ex.Number) >= 0;

    /// <summary>共享管道：非泛型，供 Open/Execute 各处复用。</summary>
    public static readonly ResiliencePipeline Retry = new ResiliencePipelineBuilder()
        .AddRetry(new Polly.Retry.RetryStrategyOptions
        {
            ShouldHandle = new Polly.PredicateBuilder().Handle<SqlException>(IsTransient),
            MaxRetryAttempts = 2,                       // 首次 + 2 次重试 = 3 次尝试
            Delay = TimeSpan.FromMilliseconds(700),
            BackoffType = Polly.DelayBackoffType.Constant,
            OnRetry = _ => default(ValueTask)
        })
        .Build();
}
