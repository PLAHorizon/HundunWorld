using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Orleans;
using Orleans.Runtime;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class RAGRetrieverGrain : Grain, IRAGRetrieverGrain
    {
        private const string IndexName = "flower_doc_chunks_idx";
        private const string KeyPrefix = "flower:chunk:";
        private const int EmbeddingDimension = 1536;

        private readonly ILogger<RAGRetrieverGrain> _logger;
        private readonly IPersistentState<RAGRetrieverState> _state;
        private readonly IConnectionMultiplexer _redis;
        private readonly Kernel _kernel;

        public RAGRetrieverGrain(
            ILogger<RAGRetrieverGrain> logger,
            [PersistentState("ragretriever", "FlowerStore")] IPersistentState<RAGRetrieverState> state,
            IConnectionMultiplexer redis,
            Kernel kernel)
        {
            _logger = logger;
            _state = state;
            _redis = redis;
            _kernel = kernel;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RAGRetrieverGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            await InitializeIndexAsync();

            await base.OnActivateAsync(cancellationToken);
        }

        private async Task InitializeIndexAsync()
        {
            if (_state.State.EnsureIndexInitialized)
            {
                _logger.LogDebug("向量索引已初始化，跳过创建");
                return;
            }

            try
            {
                var db = _redis.GetDatabase();
                var endpoints = _redis.GetEndPoints();
                if (endpoints.Length == 0)
                {
                    _logger.LogWarning("无法获取Redis终结点，跳过索引初始化");
                    return;
                }

                var server = _redis.GetServer(endpoints[0]);

                var existingIndexes = (RedisResult[])server.Execute("FT._LIST");
                var indexNames = new List<string>();
                foreach (var item in existingIndexes)
                {
                    indexNames.Add(item.ToString());
                }

                if (indexNames.Contains(IndexName))
                {
                    _logger.LogInformation("向量索引 {IndexName} 已存在，跳过创建", IndexName);
                    _state.State.EnsureIndexInitialized = true;
                    await _state.WriteStateAsync();
                    return;
                }

                await db.ExecuteAsync("FT.CREATE",
                    IndexName,
                    "ON", "HASH",
                    "PREFIX", "1", KeyPrefix,
                    "SCHEMA",
                    "content", "TEXT",
                    "embedding", "VECTOR", "FLAT", "6",
                    "TYPE", "FLOAT32",
                    "DIM", EmbeddingDimension.ToString(),
                    "DISTANCE_METRIC", "COSINE");

                _state.State.EnsureIndexInitialized = true;
                await _state.WriteStateAsync();

                _logger.LogInformation("向量索引 {IndexName} 创建成功", IndexName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建向量索引 {IndexName} 失败", IndexName);
            }
        }

        public async Task<List<string>> SearchAsync(string query, int topK)
        {
            try
            {
                _state.State.LastSearchTime = DateTime.Now;
                _state.State.TotalSearches++;

                await _state.WriteStateAsync();

                _logger.LogInformation("RAG搜索: Query={Query}, TopK={TopK}", query, topK);

                var results = await SearchWithVectorAsync(query, topK);
                if (results.Count > 0)
                {
                    return results;
                }

                _logger.LogInformation("向量搜索无结果，降级为文本搜索: Query={Query}", query);
                return await SearchWithTextAsync(query, topK);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RAG搜索失败: Query={Query}", query);
                throw;
            }
        }

        private async Task<List<string>> SearchWithVectorAsync(string query, int topK)
        {
            try
            {
                float[] queryEmbedding = await GetEmbeddingAsync(query);
                if (queryEmbedding == null || queryEmbedding.Length == 0)
                {
                    _logger.LogWarning("无法获取查询嵌入向量，跳过向量搜索");
                    return new List<string>();
                }

                var queryVectorBytes = new byte[queryEmbedding.Length * sizeof(float)];
                Buffer.BlockCopy(queryEmbedding, 0, queryVectorBytes, 0, queryVectorBytes.Length);

                var db = _redis.GetDatabase();

                var searchResult = await db.ExecuteAsync("FT.SEARCH",
                    IndexName,
                    $"*=>[KNN {topK} @embedding $query_vec AS vector_score]",
                    "PARAMS", "2", "query_vec", queryVectorBytes,
                    "SORTBY", "vector_score",
                    "DIALECT", "2",
                    "LIMIT", "0", topK.ToString());

                return ParseSearchResults(searchResult);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "向量搜索失败，将降级为文本搜索");
                return new List<string>();
            }
        }

        private async Task<List<string>> SearchWithTextAsync(string query, int topK)
        {
            try
            {
                var db = _redis.GetDatabase();

                var escapedQuery = query.Replace("\"", "\\\"");
                var searchResult = await db.ExecuteAsync("FT.SEARCH",
                    IndexName,
                    $"@content:{escapedQuery}",
                    "LIMIT", "0", topK.ToString());

                return ParseSearchResults(searchResult);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "文本搜索也失败");
                return new List<string>();
            }
        }

        private List<string> ParseSearchResults(RedisResult searchResult)
        {
            var contents = new List<string>();

            var resultArray = (RedisResult[])searchResult;
            if (resultArray == null || resultArray.Length < 2)
            {
                return contents;
            }

            var totalResults = (long)resultArray[0];
            if (totalResults == 0)
            {
                return contents;
            }

            for (int i = 1; i < resultArray.Length; i += 2)
            {
                if (i + 1 >= resultArray.Length)
                    break;

                var fields = (RedisResult[])resultArray[i + 1];
                if (fields == null)
                    continue;

                for (int j = 0; j < fields.Length - 1; j += 2)
                {
                    var fieldName = fields[j].ToString();
                    var fieldValue = fields[j + 1].ToString();

                    if (fieldName == "content" && !string.IsNullOrEmpty(fieldValue))
                    {
                        contents.Add(fieldValue);
                    }
                }
            }

            return contents;
        }

        private async Task<float[]> GetEmbeddingAsync(string text)
        {
            try
            {
                var embeddingGenerator = _kernel.Services.GetService<ITextEmbeddingGenerationService>();
                if (embeddingGenerator == null)
                {
                    _logger.LogWarning("ITextEmbeddingGenerationService 未注册");
                    return Array.Empty<float>();
                }

                var embedding = await embeddingGenerator.GenerateEmbeddingAsync(text);

                return embedding.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "生成嵌入向量失败");
                return Array.Empty<float>();
            }
        }
    }
}
