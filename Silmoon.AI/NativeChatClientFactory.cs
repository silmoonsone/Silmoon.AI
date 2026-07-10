using Silmoon.AI.Anthropic;
using Silmoon.AI.Models;
using Silmoon.AI.Interfaces;
using Silmoon.AI.OpenAI;
using Silmoon.AI.Responses;

namespace Silmoon.AI;

public static class NativeChatClientFactory
{
    public static INativeChatClient Create(ModelProvider provider, string modelName, string systemPrompt = null, bool enableThinking = false, bool disableProxy = false, int? httpRequestTimeoutMilliseconds = null) =>
        provider.ApiKind switch
        {
            NativeApiKind.Authropic => new NativeAnthropicClient(provider, modelName, systemPrompt, disableProxy, httpRequestTimeoutMilliseconds),
            NativeApiKind.Responses => new NativeResponsesClient(provider, modelName, systemPrompt),
            _ => new NativeChatCompletionsClient(provider, modelName, systemPrompt, enableThinking, disableProxy, httpRequestTimeoutMilliseconds),
        };
}


