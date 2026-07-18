using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Silmoon.AI.OpenAI.Models.Enums;

namespace Silmoon.AI.OpenAI.Models;

[JsonArray]
public class NativeMessageCollection : Collection<INativeMessage>
{
    public NativeMessageCollection()
    {
    }

    public NativeMessageCollection(IEnumerable<INativeMessage> messages)
    {
        foreach (var message in messages ?? [])
            Add(message);
    }

    public static NativeMessageCollection Create(IEnumerable<INativeMessage> messages) => new(messages);

    protected override void InsertItem(int index, INativeMessage item)
    {
        EnsureMessageId(item);
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, INativeMessage item)
    {
        EnsureMessageId(item);
        base.SetItem(index, item);
    }

    protected override void RemoveItem(int index)
    {
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        base.ClearItems();
    }

    public static implicit operator NativeMessageCollection(List<INativeMessage> messages) => new(messages);
    public static NativeMessageCollection CreateSingleNativeMessage(string content, string systemPrompt = null)
    {
        if (systemPrompt is null)
            return [NativeMessageContent.Create(Role.User, content)];
        else
            return [NativeMessageContent.Create(Role.System, systemPrompt), NativeMessageContent.Create(Role.User, content)];
    }

    public uint RollbackRounds(uint rounds = 1)
    {
        uint rollbackRounds = 0;
        while (rollbackRounds < rounds && Count > 0)
        {
            if (this[^1].Role == Role.System) break;

            var userMessageId = GetLastUserMessageId();
            if (string.IsNullOrEmpty(userMessageId)) break;

            if (!TruncateFromUserMessageId(userMessageId, keepUserMessage: false)) break;
            rollbackRounds++;
        }

        return rollbackRounds;
    }

    public bool TruncateFromUserMessageId(string userMessageId, bool keepUserMessage = true)
    {
        var userMessageIndex = GetUserMessageIndex(userMessageId);
        if (userMessageIndex < 0) return false;

        var startIndex = keepUserMessage ? userMessageIndex + 1 : userMessageIndex;
        for (var i = Count - 1; i >= startIndex; i--)
            RemoveAt(i);
        return true;
    }

    public string GetLastUserMessageId()
    {
        var index = GetLastUserMessageIndex();
        return index < 0 ? null : this[index].Id;
    }

    public int GetLastUserMessageIndex()
    {
        for (var i = Count - 1; i >= 0; i--)
        {
            if (this[i].Role == Role.User) return i;
            if (this[i].Role == Role.System) break;
        }
        return -1;
    }

    public int GetUserMessageIndex(string userMessageId)
    {
        if (string.IsNullOrEmpty(userMessageId)) return -1;
        for (var i = 0; i < Count; i++)
        {
            if (this[i].Role == Role.User && this[i].Id == userMessageId)
                return i;
        }
        return -1;
    }

    static void EnsureMessageId(INativeMessage item)
    {
        if (item is not null && string.IsNullOrEmpty(item.Id))
            item.Id = Guid.NewGuid().ToString("N");
    }
}
