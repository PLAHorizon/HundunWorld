using Horizon.AI.Kernel;
using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class EmbeddingGrain : Grain, IEmbeddingGrain
    {
        private readonly ILogger<EmbeddingGrain> _logger;
        private readonly IPersistentState<EmbeddingState> _state;
        private readonly SemanticKernelConfig _kernelConfig;
        private readonly IConnectionMultiplexer _redis;
        private readonly IDataContext<FlowerEntityContext, FlowerDocument, long> _documentContext;
        private readonly IDataContext<FlowerEntityContext, FlowerDocumentChunk, long> _chunkContext;

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public EmbeddingGrain(
            ILogger<EmbeddingGrain> logger,
            [PersistentState("embedding", "FlowerStore")] IPersistentState<EmbeddingState> state,
            SemanticKernelConfig kernelConfig,
            IConnectionMultiplexer redis,
            IDataContext<FlowerEntityContext, FlowerDocument, long> documentContext,
            IDataContext<FlowerEntityContext, FlowerDocumentChunk, long> chunkContext)
        {
            _logger = logger;
            _state = state;
            _kernelConfig = kernelConfig;
            _redis = redis;
            _documentContext = documentContext;
            _chunkContext = chunkContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("EmbeddingGrain {GrainKey} activating. TotalEmbedded={Total}, LastEmbedTime={LastTime}",
                this.GetPrimaryKeyLong(), _state.State.TotalEmbedded, _state.State.LastEmbedTime);

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<int> EmbedDocumentsAsync(List<long> documentIds)
        {
            try
            {
                var chunks = await _chunkContext.QueryAsync(c => !c.IsIndexed);
                var chunkList = chunks.ToList();

                if (documentIds != null && documentIds.Count > 0)
                {
                    var idSet = new HashSet<long>(documentIds);
                    chunkList = chunkList.Where(c => idSet.Contains(c.DocumentId)).ToList();
                }

                if (chunkList.Count == 0)
                {
                    _logger.LogInformation("没有待嵌入的分块文档");
                    return 0;
                }

                _logger.LogInformation("开始嵌入处理: ChunkCount={Count}", chunkList.Count);

                int embeddedCount = 0;
                var db = _redis.GetDatabase();

                foreach (var chunk in chunkList)
                {
                    try
                    {
                        var embeddingVector = await CallEmbeddingApiAsync(chunk.Content ?? string.Empty);

                        var redisKey = $"flower:chunk:{chunk.Id}";
                        var hashEntries = new List<HashEntry>
                        {
                            new HashEntry("content", chunk.Content ?? string.Empty)
                        };

                        if (embeddingVector is { Length: > 0 })
                        {
                            hashEntries.Add(new HashEntry("embedding", embeddingVector));
                            chunk.EmbeddingVector = embeddingVector;
                        }

                        await db.HashSetAsync(redisKey, hashEntries.ToArray());

                        chunk.IsIndexed = true;
                        await _chunkContext.UpdateAsync(chunk, chunk.Id);

                        embeddedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "嵌入分块失败: ChunkId={ChunkId}, 继续处理下一个", chunk.Id);
                    }
                }

                _state.State.TotalEmbedded += embeddedCount;
                _state.State.LastEmbedTime = DateTime.Now;
                await _state.WriteStateAsync();

                _logger.LogInformation("批量嵌入完成: EmbeddedCount={Count}, TotalEmbedded={Total}",
                    embeddedCount, _state.State.TotalEmbedded);

                return embeddedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量嵌入失败");
                throw;
            }
        }

        private async Task<byte[]?> CallEmbeddingApiAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(_kernelConfig.Endpoint) ||
                string.IsNullOrWhiteSpace(_kernelConfig.ApiKey) ||
                string.IsNullOrWhiteSpace(_kernelConfig.EmbeddingModelId))
            {
                _logger.LogWarning("嵌入服务配置不完整，跳过向量生成");
                return null;
            }

            try
            {
                var url = $"{_kernelConfig.Endpoint.TrimEnd('/')}/openai/deployments/{_kernelConfig.EmbeddingModelId}/embeddings?api-version=2024-06-01";

                var requestBody = new { input = text };
                var json = JsonSerializer.Serialize(requestBody);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                request.Headers.Add("api-key", _kernelConfig.ApiKey);

                using var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
                    _logger.LogWarning("嵌入API调用失败: StatusCode={StatusCode}, Error={Error}",
                        response.StatusCode, errorBody);
                    return null;
                }

                var responseBody = await response.Content.ReadAsStringAsync(CancellationToken.None);
                using var doc = JsonDocument.Parse(responseBody);

                var embeddingArray = doc.RootElement
                    .GetProperty("data")[0]
                    .GetProperty("embedding");

                var floats = new List<float>(embeddingArray.GetArrayLength());
                foreach (var element in embeddingArray.EnumerateArray())
                {
                    floats.Add(element.GetSingle());
                }

                var bytes = new byte[floats.Count * sizeof(float)];
                Buffer.BlockCopy(floats.ToArray(), 0, bytes, 0, bytes.Length);

                return bytes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "嵌入API调用异常，降级处理（仅存储文本）");
                return null;
            }
        }
    }
}
