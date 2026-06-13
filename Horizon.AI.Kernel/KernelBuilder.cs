using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;

namespace Horizon.AI.Kernel
{
    public static class KernelBuilder
    {
        public static Microsoft.SemanticKernel.Kernel Build(SemanticKernelConfig config)
        {
            var builder = Microsoft.SemanticKernel.Kernel.CreateBuilder();

            builder.AddAzureOpenAIChatCompletion(
                deploymentName: config.ChatModelId,
                endpoint: config.Endpoint,
                apiKey: config.ApiKey);

            builder.AddAzureOpenAITextEmbeddingGeneration(
                deploymentName: config.EmbeddingModelId,
                endpoint: config.Endpoint,
                apiKey: config.ApiKey);

            return builder.Build();
        }
    }
}
