using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.ECS.Arch.Systems;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Microsoft.Extensions.Logging;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 阶段 5.4 — 调试打印清理的行为验证：<br/>
/// 1) ECS 系统（<see cref="ArchSystemBase.Log"/>）注入 ILogger 后，诊断日志走注入日志器而非 Debug.WriteLine；<br/>
/// 2) <see cref="HorizonMessageInfo.Log"/> 静态日志器把协议解析失败按 Error 级输出，未注入时零开销不抛异常。
/// </summary>
public class DiagnosticLoggingTests
{
    private sealed class CapturingLogger : ILogger
    {
        public List<(MsLogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(MsLogLevel logLevel) => true;

        public void Log<TState>(MsLogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    // ── 5.4.2：ECS 系统日志注入 ────────────────────────────────────────

    [Fact]
    public void EventApplySystem_UnhandledEventKind_LogsThroughInjectedLogger()
    {
        var logger = new CapturingLogger();
        var system = new EventApplySystem { Log = logger };

        try
        {
            EventReceiveBuffer.Instance.Enqueue(new EventPacket
            {
                ServerTick = 1,
                Events = new[]
                {
                    new SyncEvent { Kind = (SyncEventKind)999, SourceEntityId = 1, TargetEntityId = 2 },
                },
            });

            using var world = World.Create();
            system.Update(world, TimeSpan.FromSeconds(1d / 60d));

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(MsLogLevel.Debug, entry.Level);
            Assert.Contains("unhandled SyncEventKind=999", entry.Message);
        }
        finally
        {
            // 清理共享静态缓冲，避免残留影响并行测试
            while (EventReceiveBuffer.Instance.TryDequeue(out _)) { }
        }
    }

    [Fact]
    public void EventApplySystem_WithoutLogger_DoesNotThrow()
    {
        var system = new EventApplySystem(); // Log == null → 日志调用零开销跳过

        try
        {
            EventReceiveBuffer.Instance.Enqueue(new EventPacket
            {
                ServerTick = 1,
                Events = new[] { new SyncEvent { Kind = (SyncEventKind)999 } },
            });

            using var world = World.Create();
            system.Update(world, TimeSpan.FromSeconds(1d / 60d)); // 不抛异常
        }
        finally
        {
            while (EventReceiveBuffer.Instance.TryDequeue(out _)) { }
        }
    }

    // ── 5.4.1：HorizonMessageInfo 协议解析日志 ─────────────────────────

    [Fact]
    public void HorizonMessageInfo_ParseFailure_LogsErrorThroughStaticLogger()
    {
        var logger = new CapturingLogger();
        var saved = HorizonMessageInfo.Log;
        try
        {
            HorizonMessageInfo.Log = logger;

            var info = new HorizonMessageInfo();
            Assert.False(info.OnParsingHeader(new byte[4])); // 协议头不足 8 字节

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(MsLogLevel.Error, entry.Level);
            Assert.Contains("header too short", entry.Message);
        }
        finally
        {
            HorizonMessageInfo.Log = saved; // 还原静态，避免并行测试污染
        }
    }

    [Fact]
    public void HorizonMessageInfo_ParseFailure_WithoutLogger_DoesNotThrow()
    {
        var saved = HorizonMessageInfo.Log;
        try
        {
            HorizonMessageInfo.Log = null;

            var info = new HorizonMessageInfo();
            Assert.False(info.OnParsingHeader(new byte[4]));
            Assert.False(info.OnParsingBody(Array.Empty<byte>()));
        }
        finally
        {
            HorizonMessageInfo.Log = saved;
        }
    }
}
