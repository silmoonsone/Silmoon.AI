using System.Collections.ObjectModel;
using Newtonsoft.Json;
using Silmoon.AI.OpenAI.Models.Enums;

namespace Silmoon.AI.OpenAI.Models;

[JsonArray]
public class MessageCollection : Collection<IMessage>
{
    public MessageCollection()
    {
    }

    public MessageCollection(IEnumerable<IMessage> messages)
    {
        foreach (var message in messages ?? [])
            Add(message);
    }

    public static MessageCollection Create(IEnumerable<IMessage> messages) => new(messages);

    protected override void InsertItem(int index, IMessage item)
    {
        EnsureMessageHash(item);
        base.InsertItem(index, item);
    }

    protected override void SetItem(int index, IMessage item)
    {
        EnsureMessageHash(item);
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

    public static implicit operator MessageCollection(List<IMessage> messages) => new(messages);
    public static MessageCollection CreateOneUserMessage(string content) => [MessageContent.Create(Role.User, content)];
    public static MessageCollection CreateOneUserMessage(string content, string systemPrompt) => [MessageContent.Create(Role.System, systemPrompt), MessageContent.Create(Role.User, content)];

    public uint RollbackRounds(uint rounds = 1)
    {
        uint rollbackRounds = 0;
        while (rollbackRounds < rounds && Count > 0)
        {
            if (this[^1].Role == Role.System) break;

            var userMessageHash = GetLastUserMessageHash();
            if (string.IsNullOrEmpty(userMessageHash)) break;

            if (!TruncateFromUserMessageHash(userMessageHash, keepUserMessage: false)) break;
            rollbackRounds++;
        }

        return rollbackRounds;
    }

    public bool TruncateFromUserMessageHash(string userMessageHash, bool keepUserMessage = true)
    {
        var userMessageIndex = GetUserMessageIndex(userMessageHash);
        if (userMessageIndex < 0) return false;

        var startIndex = keepUserMessage ? userMessageIndex + 1 : userMessageIndex;
        for (var i = Count - 1; i >= startIndex; i--)
            RemoveAt(i);
        return true;
    }

    public string GetLastUserMessageHash()
    {
        var index = GetLastUserMessageIndex();
        return index < 0 ? null : this[index].Hash;
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

    public int GetUserMessageIndex(string userMessageHash)
    {
        if (string.IsNullOrEmpty(userMessageHash)) return -1;
        for (var i = 0; i < Count; i++)
        {
            if (this[i].Role == Role.User && this[i].Hash == userMessageHash)
                return i;
        }
        return -1;
    }

    static void EnsureMessageHash(IMessage item)
    {
        if (item is not null && string.IsNullOrEmpty(item.Hash))
            item.Hash = Guid.NewGuid().ToString("N");
    }
}
