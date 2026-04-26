using Silmoon.AI.Models.OpenAI.Models;
using Silmoon.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Silmoon.AI.Handlers
{
    public delegate void StreamOutputHandler(StateSet<bool, Chunk> chunkState);
}
