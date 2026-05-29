using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Models
{
    public class Usage
    {
        [JsonProperty("prompt_tokens")]
        public int PromptTokens { get; set; }
        [JsonProperty("completion_tokens")]
        public int CompletionTokens { get; set; }
        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }
        [JsonProperty("prompt_tokens_details")]
        public PromptTokensDetails? PromptTokensDetails { get; set; }
        [JsonProperty("completion_tokens_details")]
        public CompletionTokensDetails? CompletionTokensDetails { get; set; }

        [JsonProperty("prompt_cache_hit_tokens")]
        public int? PromptCacheHitTokens { get; set; }
        [JsonProperty("prompt_cache_miss_tokens")]
        public int? PromptCacheMissTokens { get; set; }
    }
    public class PromptTokensDetails
    {
        [JsonProperty("cached_tokens")]
        public int? CachedTokens { get; set; }
        [JsonProperty("text_tokens")]
        public int? TextTokens { get; set; }
    }
    public class CompletionTokensDetails
    {
        [JsonProperty("reasoning_tokens")]
        public int? ReasoningTokens { get; set; }
        [JsonProperty("text_tokens")]
        public int? TextTokens { get; set; }
    }
}
