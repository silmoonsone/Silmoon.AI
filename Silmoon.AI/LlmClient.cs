using System;
using System.Collections.Generic;
using System.Text;
using Silmoon.AI.Interfaces;

namespace Silmoon.AI
{
    public class LlmClient
    {
        INativeClient NativeClient { get; set; }
        public LlmClient(INativeClient nativeClient)
        {
            NativeClient = nativeClient;
        }
    }
}
