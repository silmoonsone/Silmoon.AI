using Silmoon.AI.Anthropic;
using Silmoon.AI.Models;
using Silmoon.AI.Interfaces;
using Silmoon.AI.OpenAI;

namespace Silmoon.AI;

public static class NativeClientFactory
{
    public static INativeClient Create(ModelProvider provider, string modelName, string systemPrompt = null, bool enableThinking = false, bool disableProxy = false, int? httpRequestTimeoutMilliseconds = null) =>
        provider.ApiKind switch
        {
            NativeApiKind.Authropic => new AnthropicClient(provider, modelName, systemPrompt, disableProxy, httpRequestTimeoutMilliseconds),
            NativeApiKind.Responses => new ResponsesClient(provider, modelName, systemPrompt, enableThinking, disableProxy, httpRequestTimeoutMilliseconds),
            _ => new ChatClient(provider, modelName, systemPrompt, enableThinking, disableProxy, httpRequestTimeoutMilliseconds),
        };
}
