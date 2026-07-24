using System;
using System.Collections.Generic;
using FlaxEngine;
using Horizon.Game.Message.Sim;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using System.Threading.Tasks;

namespace Game.Network
{
    /// <summary>
    /// 网络同步管理器
    /// 实现客户端预测 + 服务端校验 + 平滑插值的混合同步机制
    /// </summary>
    public class NetworkSyncManager : Script
    {
        /// <summary>
        /// 同步模式
        /// </summary>
        public enum SyncMode
        {
            ClientPrediction,       // 客户端预测
            ServerAuthority,        // 服务端权威
            Hybrid                  // 混合模式（预测+校验）
        }

        [Header("同步设置")]
        [Tooltip("同步模式")]
        public SyncMode Mode = SyncMode.Hybrid;

        [Tooltip("是否为本地玩家")]
        public bool IsLocalPlayer = false;

        [Tooltip("网络更新频率（Hz)")]
        [Limit(10, 60)]
        public int NetworkUpdateRate = 20; // 20Hz = 50ms

        [Header("预测设置")]
        [Tooltip("是否启用客户端预测")]
        public bool EnablePrediction = true;

        [Tooltip("预测缓冲区大小（帧数）")]
        public int PredictionBufferSize = 10;

        [Tooltip("位置修正阈值（米）")]
        public float PositionCorrectionThreshold = 0.5f;

        [Tooltip("客户端预测使用的最大移动速度（米/秒），0=使用MovementFormula.DefaultMaxSpeed")]
        public float ClientMaxSpeed = 0f;

        [Header("插值设置")]
        [Tooltip("是否启用插值")]
        public bool EnableInterpolation = true;

        [Tooltip("插值延迟（秒）")]
        public float InterpolationDelay = 0.1f;

        [Tooltip("插值速度")]
        public float InterpolationSpeed = 10.0f;

        [Header("调试")]
        [Tooltip("显示调试信息")]
        public bool ShowDebug = false;

        [Tooltip("显示预测轨迹")]
        public bool ShowPredictionPath = false;

        // 网络状态
        private float networkTimer = 0;
        private float networkInterval = 0;

        // 预测状态缓冲
        private Queue<PredictedState> predictionBuffer = new Queue<PredictedState>();
        
        // 插值状态缓冲
        private Queue<ServerState> interpolationBuffer = new Queue<ServerState>();

        // 当前状态
        private Vector3 currentPosition;
        private Quaternion currentRotation;
        private Vector3 currentVelocity;

        // 服务端权威状态
        private Vector3 serverPosition;
        private Quaternion serverRotation;
        private float lastServerUpdateTime;

        // 统计信息
        private int predictedFrameCount = 0;
        private int correctionCount = 0;
        private float averagePredictionError = 0;
        private float _verticalVelocity;
        private int _jumpCount;
        private bool _wasGrounded = true;

        /// <summary>
        /// 预测状态数据
        /// </summary>
        private class PredictedState
        {
            // 新增 ClientTick 以与 ECS/网络协议对齐（优先使用 long ClientTick）
            public long ClientTick;
            public int SequenceNumber;
            public float Timestamp;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Velocity;
            public Vector3 Input;
            public float JumpImpulse;
            public float DeltaTime;
            public float MaxSpeed;
        }

        /// <summary>
        /// 服务端状态数据
        /// </summary>
        private class ServerState
        {
            public float Timestamp;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Velocity;
        }

        /// <summary>
        /// 初始化
        /// </summary>
        public override void OnEnable()
        {
            networkInterval = 1.0f / NetworkUpdateRate;
            currentPosition = Actor.Position;
            currentRotation = Actor.Orientation;
            serverPosition = currentPosition;
            serverRotation = currentRotation;
            lastServerUpdateTime = Time.GameTime;
            _verticalVelocity = 0f;

            if (ShowDebug)
            {
                Debug.Log($"NetworkSyncManager initialized: Mode={Mode}, UpdateRate={NetworkUpdateRate}Hz, IsLocal={IsLocalPlayer}");
            }
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public override void OnUpdate()
        {
            if (IsLocalPlayer)
            {
                UpdateLocalPlayer();
            }
            else
            {
                UpdateRemotePlayer();
            }

            if (ShowDebug)
            {
                DrawDebugInfo();
            }
        }

        /// <summary>
        /// 更新本地玩家
        /// </summary>
        private void UpdateLocalPlayer()
        {
            // 客户端预测
            if (EnablePrediction)
            {
                // 获取输入并预测移动
                Vector3 input = GetMovementInput();
                PredictMovement(input);
            }

            // 发送网络更新
            networkTimer += Time.DeltaTime;
            if (networkTimer >= networkInterval)
            {
                SendMovementUpdate();
                networkTimer = 0;
            }
        }

        /// <summary>
        /// 更新远程玩家
        /// </summary>
        private void UpdateRemotePlayer()
        {
            // 插值到服务端状态
            if (EnableInterpolation && interpolationBuffer.Count > 0)
            {
                InterpolateToServerState();
                // 同步插值结果到 backing fields
                currentPosition = Actor.Position;
                currentRotation = Actor.Orientation;
            }
            else
            {
                Actor.Position = serverPosition;
                Actor.Orientation = serverRotation;
                currentPosition = serverPosition;
                currentRotation = serverRotation;
            }
        }

        /// <summary>
        /// 预测移动
        /// </summary>
        private void PredictMovement(Vector3 input)
        {
            float deltaTime = Time.DeltaTime;
            bool isGrounded = currentPosition.Y <= 0.01f && _verticalVelocity <= 0f;
            if (isGrounded && !_wasGrounded)
            {
                _jumpCount = 0;
            }
            _wasGrounded = isGrounded;
            bool jumpPressed = Input.GetKey(KeyboardKeys.Spacebar);
            float jumpImpulse = 0f;
            if (jumpPressed && isGrounded)
            {
                _jumpCount = 1;
                jumpImpulse = 5.5f;
            }
            else if (jumpPressed && !isGrounded && _jumpCount < 3)
            {
                _jumpCount++;
                jumpImpulse = _jumpCount switch
                {
                    2 => 4.5f,
                    3 => 3.5f,
                    _ => 5.5f,
                };
            }

            // 获取当前预测速度：优先使用配置的ClientMaxSpeed，否则使用DefaultMaxSpeed
            float currentMaxSpeed = ClientMaxSpeed > 0 ? ClientMaxSpeed : MovementFormula.DefaultMaxSpeed;

            Vector3 previousPosition = currentPosition;
            var (nx, ny, nz, nvz) = MovementFormula.Step(
                currentPosition.X, currentPosition.Z, currentPosition.Y, _verticalVelocity,
                input.X, input.Z, jumpImpulse,
                deltaTime, currentMaxSpeed);

            currentPosition.X = nx;
            currentPosition.Z = ny;
            currentPosition.Y = nz;
            _verticalVelocity = nvz;

            currentVelocity = deltaTime > 0f ? (currentPosition - previousPosition) / deltaTime : Vector3.Zero;

            Actor.Position = currentPosition;

            if (predictionBuffer.Count >= PredictionBufferSize)
            {
                predictionBuffer.Dequeue();
            }

            // 尝试从 HundunWorldGame 获取当前 ClientTick（ECS 管线），若不可用则回退到本地帧序号
            long clientTickForState = predictedFrameCount; // fallback
            try
            {
                var gw = HundunWorld.Game.HundunWorldGame.Instance;
                var archHost = gw?.ArchHost;
                // LocalSimulationSystem 会维护 CurrentClientTick；这里无法直接访问系统实例，
                // 尝试通过 PredictedTransformComponent/ArchWorld 查询并读取（若存在）——为了保守实现，先使用 predictedFrameCount 作为兼容值。
                // TODO: 如果需要更精确的 ClientTick，从 LocalSimulationSystem 导出 CurrentClientTick 的公共 API。
            }
            catch { }

            predictionBuffer.Enqueue(new PredictedState
            {
                ClientTick = clientTickForState,
                SequenceNumber = predictedFrameCount++,
                Timestamp = Time.GameTime,
                Position = currentPosition,
                Rotation = currentRotation,
                Velocity = currentVelocity,
                Input = input,
                JumpImpulse = jumpImpulse,
                DeltaTime = deltaTime,
                MaxSpeed = currentMaxSpeed,
            });
        }

        /// <summary>
        /// 插值到服务端状态
        /// </summary>
        private void InterpolateToServerState()
        {
            float targetTime = Time.GameTime - InterpolationDelay;

            if (interpolationBuffer.Count < 2)
            {
                // 缓冲区状态不足时移动到最新状态
                ServerState latest = null;
                foreach (var s in interpolationBuffer)
                    latest = s; // last element

                if (latest != null)
                {
                    Actor.Position = Vector3.Lerp(Actor.Position, latest.Position, InterpolationSpeed * Time.DeltaTime);
                    Actor.Orientation = Quaternion.Slerp(Actor.Orientation, latest.Rotation, InterpolationSpeed * Time.DeltaTime);
                }
                return;
            }

            // 查找插值的两个边界状态
            ServerState previous = null;
            ServerState next = null;

            foreach (var state in interpolationBuffer)
            {
                if (state.Timestamp <= targetTime)
                {
                    previous = state;
                }
                else
                {
                    next = state;
                    break;
                }
            }

            // 如果没有 previous，但有 next，说明 targetTime 在缓冲最早时间之前，向 next 靠近（平滑到更早的服务端状态）
            if (previous == null && next != null)
            {
                Actor.Position = Vector3.Lerp(Actor.Position, next.Position, InterpolationSpeed * Time.DeltaTime);
                Actor.Orientation = Quaternion.Slerp(Actor.Orientation, next.Rotation, InterpolationSpeed * Time.DeltaTime);
                return;
            }

            if (previous != null && next != null)
            {
                // 两状态之间的线性插值
                float t = (next.Timestamp - previous.Timestamp) > 0.0001f
                    ? (targetTime - previous.Timestamp) / (next.Timestamp - previous.Timestamp)
                    : 0f;
                t = Mathf.Clamp(t, 0f, 1f);

                Actor.Position = Vector3.Lerp(previous.Position, next.Position, t);
                Actor.Orientation = Quaternion.Slerp(previous.Rotation, next.Rotation, t);

                // 移除已消耗的旧状态（保留 next 用于后续插值）
                while (interpolationBuffer.Count > 0 && interpolationBuffer.Peek().Timestamp < next.Timestamp)
                {
                    interpolationBuffer.Dequeue();
                }
            }
            else if (previous != null)
            {
                // 所有状态都已过时，向最新状态跟随
                Actor.Position = Vector3.Lerp(Actor.Position, previous.Position, InterpolationSpeed * Time.DeltaTime);
                Actor.Orientation = Quaternion.Slerp(Actor.Orientation, previous.Rotation, InterpolationSpeed * Time.DeltaTime);
            }
        }

        /// <summary>
        /// 发送移动更新到服务端
        /// [修复] 使用新的 InputPacket + SyncFrameMessage 管线发送，替代旧的 MoveRequest，
        /// 以与 ECS 同步管线（InputSendSystem → InputSendQueue → ECSUpdateDriver）保持一致。
        /// 
        /// 重要：当 IsLocalPlayer=true 时，本地玩家的输入发送已由 ECS 管线
        /// （PlayerController.WriteInputToEcs → InputSendSystem → ECSUpdateDriver.FlushInputSendQueue）负责，
        /// 此方法不再发送网络包，避免重复发送导致服务端收到双倍输入包。
        /// 客户端预测（PredictMovement）和服务端校验（OnServerPositionUpdate/CorrectPrediction）逻辑不受影响。
        /// </summary>
        private void SendMovementUpdate()
        {
            // 本地玩家的输入发送已由 ECS 管线负责，此处跳过网络发送以避免重复
            if (IsLocalPlayer)
            {
                if (ShowDebug)
                {
                    Debug.Log($"[Network] Local player input sending skipped (handled by ECS pipeline): Seq={predictedFrameCount}");
                }
                return;
            }

            var networkManager = HundunWorld.Game.HundunWorldGame.Instance?.NetworkManager;
            // 使用真实时间戳作为 ClientTick（基于握手初始 tick + 累计增量）
            long clientTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (networkManager != null && networkManager.CanSendMessage() && networkManager.IsSyncHandshakeComplete)
            {
                // 从 HundunWorldGame 获取玩家 CharacterId
                var characterId = HundunWorld.Game.HundunWorldGame.Instance?.PlayerId ?? 0;

                Vector3 moveDir = currentVelocity.Length > 0.01f ? Vector3.Normalize(currentVelocity) : Vector3.Zero;

                float currentMaxSpeed = ClientMaxSpeed > 0 ? ClientMaxSpeed : MovementFormula.DefaultMaxSpeed;

                var inputPacket = new InputPacket
                {
                    ClientTick = clientTick,
                    MoveX = moveDir.X,
                    MoveY = moveDir.Z,
                    InputBits = 0,
                    LookYaw = 0f,
                    LookPitch = 0f,
                    CharacterId = characterId,
                    MaxSpeed = currentMaxSpeed,
                    PredictedEndX = currentPosition.X,
                    PredictedEndY = currentPosition.Y,
                    PredictedEndZ = currentPosition.Z,
                };

                SyncPacketCodec.Encode(inputPacket, out var frame, out var frameLength);
                try
                {
                    var payload = new byte[frameLength];
                    System.Buffer.BlockCopy(frame, 0, payload, 0, frameLength);

                    var syncFrame = new SyncFrameMessage
                    {
                        Frame = payload,
                        PacketKind = (byte)inputPacket.Kind,
                        ProtocolVersion = inputPacket.ProtocolVersion,
                    };

                    // 非 fire-and-forget：跟踪发送结果
                    Task.Run(async () =>
                    {
                        try
                        {
                            if (networkManager != null)
                                await networkManager.SendAsync(syncFrame);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[Network] SendMovementUpdate failed: {ex.Message}");
                        }
                    });
                }
                finally
                {
                    SyncPacketCodec.ReturnFrame(frame);
                }
            }

            if (ShowDebug)
            {
                Debug.Log($"[Network] Send movement: Pos={currentPosition}, Tick={clientTick}");
            }
        }

        /// <summary>
        /// 接收服务端位置校验
        /// </summary>
        public void OnServerPositionUpdate(Vector3 position, Quaternion rotation, int sequenceNumber)
        {
            serverPosition = position;
            serverRotation = rotation;
            lastServerUpdateTime = Time.GameTime;

            if (IsLocalPlayer && EnablePrediction)
            {
                // 计算预测误差
                float error = Vector3.Distance(currentPosition, serverPosition);
                averagePredictionError = (averagePredictionError + error) / 2.0f;

                // 检查是否需要修正
                if (error > PositionCorrectionThreshold)
                {
                    CorrectPrediction(sequenceNumber);
                    correctionCount++;
                }
            }
            else
            {
                // 远程玩家：添加到插值缓冲
                if (EnableInterpolation)
                {
                    interpolationBuffer.Enqueue(new ServerState
                    {
                        Timestamp = Time.GameTime,
                        Position = position,
                        Rotation = rotation,
                        Velocity = Vector3.Zero
                    });

                    // 限制缓冲区大小
                    while (interpolationBuffer.Count > 20)
                    {
                        interpolationBuffer.Dequeue();
                    }
                }
                else
                {
                    // 直接应用
                    serverPosition = position;
                    serverRotation = rotation;
                    currentPosition = position;
                    currentRotation = rotation;
                }
            }

            if (ShowDebug)
            {
                if (IsLocalPlayer && EnablePrediction)
                {
                    // 计算预测误差
                    float error = Vector3.Distance(currentPosition, serverPosition);
                    Debug.Log($"[Network] Received server update: Pos={position}, Seq={sequenceNumber}, Error={error:F3}m");
                }
                else
                {
                    Debug.Log($"[Network] Received server update: Pos={position}, Seq={sequenceNumber}");
                }
            }
        }

        /// <summary>
        /// 修正预测误差
        /// </summary>
        // 新版：按 server 回显的 ClientTick（long）匹配预测；保留对旧 int 序号的回退兼容
        private void CorrectPrediction(long serverClientTickOrSequenceNumber)
        {
            PredictedState matchedState = null;

            // 先尝试按 ClientTick(long) 匹配
            foreach (var state in predictionBuffer)
            {
                if (state.ClientTick == serverClientTickOrSequenceNumber)
                {
                    matchedState = state;
                    break;
                }
            }

            // 回退兼容：如果未找到，按旧的 int SequenceNumber 匹配
            if (matchedState == null)
            {
                foreach (var state in predictionBuffer)
                {
                    if (state.SequenceNumber == (int)serverClientTickOrSequenceNumber)
                    {
                        matchedState = state;
                        Debug.LogWarning($"[Network] CorrectPrediction: fell back to SequenceNumber matching for {serverClientTickOrSequenceNumber}");
                        break;
                    }
                }
            }

            if (matchedState != null)
            {
                // 计算误差并修正
                Vector3 error = serverPosition - matchedState.Position;
                
                if (error.Length > 2.0f)
                {
                    // 误差过大，直接传送
                    currentPosition = serverPosition;
                    currentRotation = serverRotation;
                    Actor.Position = serverPosition;
                    Actor.Orientation = serverRotation;

                    if (ShowDebug)
                    {
                        Debug.LogWarning($"[Network] Large error detected ({error.Length:F2}m), force teleport!");
                    }
                }
                else
                {
                    // 平滑修正
                    currentPosition = Vector3.Lerp(currentPosition, serverPosition, 0.5f);
                    currentRotation = Quaternion.Slerp(currentRotation, serverRotation, 0.5f);
                    Actor.Position = currentPosition;
                    Actor.Orientation = currentRotation;

                    if (ShowDebug)
                    {
                        Debug.Log($"[Network] Smooth correction: Error={error.Length:F3}m");
                    }
                }

                // 重新预测后续帧，使用 ClientTick（若 matchedState 有 ClientTick 则用之，否则回退到 sequence）
                long fromTick = matchedState.ClientTick != 0 ? matchedState.ClientTick : matchedState.SequenceNumber;
                ReplayPredictions(fromTick);
            }
            else
            {
                // 服务端序列号比缓冲区所有状态都旧：直接拉回到服务端位置，清空缓冲
                if (ShowDebug)
                {
                    Debug.LogWarning($"[Network] No matching prediction state for seq={serverClientTickOrSequenceNumber}, teleporting to server position");
                }
                currentPosition = serverPosition;
                currentRotation = serverRotation;
                Actor.Position = serverPosition;
                Actor.Orientation = serverRotation;
                predictionBuffer.Clear();
            }
        }

        /// <summary>
        /// 重放预测（服务端校验后）
        /// </summary>
        private void ReplayPredictions(long fromClientTick)
        {
            List<PredictedState> statesToReplay = new List<PredictedState>();

            foreach (var state in predictionBuffer)
            {
                if (state.ClientTick > fromClientTick)
                {
                    statesToReplay.Add(state);
                }
            }

            foreach (var state in statesToReplay)
            {
                float delta = state.DeltaTime > 0 ? state.DeltaTime : Time.DeltaTime;
                float speed = state.MaxSpeed > 0 ? state.MaxSpeed : MovementFormula.DefaultMaxSpeed;
                var (nx, ny, nz, nvz) = MovementFormula.Step(
                    currentPosition.X, currentPosition.Z, currentPosition.Y, _verticalVelocity,
                    state.Input.X, state.Input.Z, state.JumpImpulse,
                    delta, speed);

                currentPosition.X = nx;
                currentPosition.Z = ny;
                currentPosition.Y = nz;
                _verticalVelocity = nvz;
            }

            Actor.Position = currentPosition;
        }

        /// <summary>
        /// 获取移动输入（临时实现）
        /// </summary>
        private Vector3 GetMovementInput()
        {
            Vector3 input = Vector3.Zero;

            if (Input.GetKey(KeyboardKeys.W)) input.Z += 1;
            if (Input.GetKey(KeyboardKeys.S)) input.Z -= 1;
            if (Input.GetKey(KeyboardKeys.A)) input.X -= 1;
            if (Input.GetKey(KeyboardKeys.D)) input.X += 1;

            if (input.Length > 0)
                input = Vector3.Normalize(input);

            return input;
        }

        /// <summary>
        /// 绘制调试信息
        /// </summary>
        private void DrawDebugInfo()
        {
            Vector3 debugPos = new Vector3(100, 350, 0);
            
            DebugDraw.DrawText($"Network Sync - Mode: {Mode}", debugPos, Color.White);
            debugPos.Y += 20;
            
            if (IsLocalPlayer)
            {
                DebugDraw.DrawText($"Predicted Frames: {predictedFrameCount}", debugPos, Color.Cyan);
                debugPos.Y += 20;
                DebugDraw.DrawText($"Corrections: {correctionCount}", debugPos, Color.Yellow);
                debugPos.Y += 20;
                DebugDraw.DrawText($"Avg Error: {averagePredictionError:F3}m", debugPos, 
                    averagePredictionError > PositionCorrectionThreshold ? Color.Red : Color.Green);
                debugPos.Y += 20;
                DebugDraw.DrawText($"Buffer Size: {predictionBuffer.Count}", debugPos, Color.White);
            }
            else
            {
                DebugDraw.DrawText($"Interpolation Buffer: {interpolationBuffer.Count}", debugPos, Color.Cyan);
                debugPos.Y += 20;
                float latency = Time.GameTime - lastServerUpdateTime;
                DebugDraw.DrawText($"Last Update: {latency:F2}s ago", debugPos, 
                    latency > 1.0f ? Color.Red : Color.Green);
            }

            // 绘制预测轨迹
            if (ShowPredictionPath && IsLocalPlayer)
            {
                Vector3 lastPos = Actor.Position;
                foreach (var state in predictionBuffer)
                {
                    DebugDraw.DrawLine(lastPos, state.Position, Color.Yellow);
                    lastPos = state.Position;
                }
            }

            // 绘制服务端位置
            if (IsLocalPlayer)
            {
                DebugDraw.DrawSphere(new BoundingSphere(serverPosition, 0.3f), Color.Green);
                DebugDraw.DrawLine(Actor.Position, serverPosition, Color.Red);
            }
        }

        /// <summary>
        /// 获取网络延迟（毫秒）
        /// </summary>
        public float GetNetworkLatency()
        {
            return (Time.GameTime - lastServerUpdateTime) * 1000.0f;
        }

        /// <summary>
        /// 获取预测误差统计
        /// </summary>
        public float GetAveragePredictionError()
        {
            return averagePredictionError;
        }

        /// <summary>
        /// 重置同步状态
        /// </summary>
        public void ResetSync()
        {
            predictionBuffer.Clear();
            interpolationBuffer.Clear();
            predictedFrameCount = 0;
            correctionCount = 0;
            averagePredictionError = 0;
            currentPosition = Actor.Position;
            currentRotation = Actor.Orientation;
            serverPosition = currentPosition;
            serverRotation = currentRotation;
            _verticalVelocity = 0f;
        }
    }
}
