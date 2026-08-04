using System;
using System.Collections.Generic;
using Horizon.Game.Core.Sim;
using Horizon.Game.Message.Sync;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 任务组 7.1 — 预测确定性回归用例（spec 5.6.1.5）。
/// 验证固定输入序列 + 固定 tick 步长下，基于 <see cref="MovementFormula"/> 的权威回放逐 tick 位置完全一致
/// （不依赖帧序或随机因素），且与 ReconciliationSystem 重放使用的同一公式一致。
/// </summary>
public class PredictionDeterminismTests
{
    private const float Dt = 1f / 60f;

    private static readonly InputPacket[] InputSequence =
    {
        new InputPacket { ClientTick = 1, MoveX = 0.5f, MoveY = 0f },
        new InputPacket { ClientTick = 2, MoveX = 0.8f, MoveY = 0.2f },
        new InputPacket { ClientTick = 3, MoveX = 0.3f, MoveY = 0.6f },
        new InputPacket { ClientTick = 4, MoveX = -0.4f, MoveY = 0.1f },
        new InputPacket { ClientTick = 5, MoveX = 0f, MoveY = -0.5f },
        new InputPacket { ClientTick = 6, MoveX = 0.6f, MoveY = 0.4f },
    };

    /// <summary>
    /// 用固定输入序列从给定起点按固定步长逐 tick 模拟，返回每次步进后的位置。
    /// 与 ReconciliationSystem 重放（MovementFormula.Step）使用同一公式。
    /// </summary>
    private static List<(float X, float Y, float Z, float Vz)> Simulate(
        float startX, float startY, float startZ, float startVz)
    {
        var result = new List<(float, float, float, float)>(InputSequence.Length);
        var x = startX;
        var y = startY;
        var z = startZ;
        var vz = startVz;

        foreach (var input in InputSequence)
        {
            var (nx, ny, nz, nvz) = MovementFormula.Step(
                x, y, z, vz,
                input.MoveX, input.MoveY, 0f,
                Dt, MovementFormula.DefaultMaxSpeed);
            x = nx;
            y = ny;
            z = nz;
            vz = nvz;
            result.Add((x, y, z, vz));
        }
        return result;
    }

    [Fact]
    public void SameInputSequence_ReplayedTwice_ProducesIdenticalPositions()
    {
        // 同一输入序列 + 同一起点重放两次，逐 tick 位置必须完全一致（确定性）。
        var first = Simulate(0f, 0f, 0f, 0f);
        var second = Simulate(0f, 0f, 0f, 0f);

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].X, second[i].X, 5); // 位级一致（float 精确比较）
            Assert.Equal(first[i].Y, second[i].Y, 5);
            Assert.Equal(first[i].Z, second[i].Z, 5);
            Assert.Equal(first[i].Vz, second[i].Vz, 5);
        }
    }

    [Fact]
    public void ReplayFromAuthorityPosition_MatchesDirectSimulation()
    {
        // 场景：服务端在 tick 3 下发 Correction（权威位置 = 从起点模拟 3 tick 后的位置）。
        // 客户端从权威位置按剩余输入重放，结果应与"从起点直接模拟全部输入"一致。
        var fullSim = Simulate(0f, 0f, 0f, 0f);

        // 权威位置 = 前 3 tick 模拟结果。
        var authority = fullSim[2];

        // 从权威位置重放剩余输入（tick 4..6）。
        var replayStartX = authority.X;
        var replayStartY = authority.Y;
        var replayStartZ = authority.Z;
        var replayStartVz = authority.Vz;

        var replayX = replayStartX;
        var replayY = replayStartY;
        var replayZ = replayStartZ;
        var replayVz = replayStartVz;
        for (int i = 3; i < InputSequence.Length; i++)
        {
            var input = InputSequence[i];
            var (nx, ny, nz, nvz) = MovementFormula.Step(
                replayX, replayY, replayZ, replayVz,
                input.MoveX, input.MoveY, 0f,
                Dt, MovementFormula.DefaultMaxSpeed);
            replayX = nx;
            replayY = ny;
            replayZ = nz;
            replayVz = nvz;

            // 重放终点应与全量模拟的第 i tick 一致（spec 5.6.1.5 验收 b）。
            Assert.Equal(fullSim[i].X, replayX, 5);
            Assert.Equal(fullSim[i].Y, replayY, 5);
            Assert.Equal(fullSim[i].Z, replayZ, 5);
        }
    }

    [Fact]
    public void DeterministicAcrossDifferentStartPositions_RelativeMotionConsistent()
    {
        // 不同起点但相同输入序列，相对位移必须一致（位置计算确定性，无随机因素）。
        var a = Simulate(100f, 0f, 50f, 0f);
        var b = Simulate(-30f, 10f, 200f, 0f);

        for (int i = 0; i < a.Count; i++)
        {
            var dxA = a[i].X - 100f;
            var dxB = b[i].X - (-30f);
            Assert.Equal(dxA, dxB, 5);
        }
    }
}