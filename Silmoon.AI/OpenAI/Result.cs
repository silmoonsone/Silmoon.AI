using System;
using Silmoon.AI.Enums;
using Silmoon.Extensions;

namespace Silmoon.AI.OpenAI;

public class Result
{
    public Role Role { get; set; }
    public string Content { get; set; }
    public string FinishReason { get; set; }
    public static Result Create(ChunkChoice[] chunkChoices)
    {
        var result = new Result();
        if (chunkChoices is not null && chunkChoices.Length > 0)
        {
            foreach (var choice in chunkChoices)
            {
                if (choice.Delta is not null)
                {
                    if (choice.Delta.Content is not null) result.Content += choice.Delta.Content;
                    if (choice.Delta.Role is not null) result.Role = choice.Delta.Role.Value;
                }
            }
        }
        return result;
    }
    public static Result Create(Chunk[] responses)
    {
        var result = new Result();
        if (responses is not null && responses.Length > 0)
        {
            foreach (var response in responses)
            {
                if (!response.Choices.IsNullOrEmpty())
                {
                    foreach (var choice in response.Choices)
                    {
                        if (choice.Delta is not null)
                        {
                            if (choice.Delta.Content is not null) result.Content += choice.Delta.Content;
                            if (choice.Delta.Role is not null) result.Role = choice.Delta.Role.Value;
                            if (!string.IsNullOrEmpty(choice.FinishReason)) result.FinishReason = choice.FinishReason;
                        }
                    }
                }
            }
        }
        return result;
    }
}
