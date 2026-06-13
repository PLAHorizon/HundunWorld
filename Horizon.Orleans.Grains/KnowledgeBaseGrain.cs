using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 知识库Grain实现 - 负责文档管理
    /// </summary>
    public class KnowledgeBaseGrain : Grain, IKnowledgeBaseGrain
    {
        private readonly ILogger<KnowledgeBaseGrain> _logger;
        private readonly IPersistentState<KnowledgeBaseState> _state;
        private readonly IDataContext<FlowerEntityContext, FlowerDocument, long> _dataContext;

        public KnowledgeBaseGrain(
            ILogger<KnowledgeBaseGrain> logger,
            [PersistentState("knowledgebase", "FlowerStore")] IPersistentState<KnowledgeBaseState> state,
            IDataContext<FlowerEntityContext, FlowerDocument, long> dataContext)
        {
            _logger = logger;
            _state = state;
            _dataContext = dataContext;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("KnowledgeBaseGrain {GrainKey} activating.", this.GetPrimaryKeyLong());

            await base.OnActivateAsync(cancellationToken);
        }

        public async Task<long> UploadDocumentAsync(string title, string content, string source)
        {
            try
            {
                var entity = new FlowerDocument
                {
                    Title = title,
                    Content = content,
                    Source = source,
                    IsIndexed = false,
                    ChunkCount = 0
                };

                var result = await _dataContext.AddAsync(entity);
                if (result == null)
                {
                    _logger.LogError("上传文档失败: 数据库保存返回null");
                    return 0;
                }

                _state.State.TotalDocuments++;
                await _state.WriteStateAsync();

                _logger.LogInformation("上传文档: Id={Id}, Title={Title}", result.Id, title);

                return result.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "上传文档失败: Title={Title}", title);
                throw;
            }
        }

        public async Task<List<long>> GetUnindexedDocumentsAsync()
        {
            try
            {
                var documents = await _dataContext.QueryAsync(e => !e.IsIndexed && e.IsValid);
                return documents.Select(e => e.Id).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取未索引文档失败");
                throw;
            }
        }
    }
}
