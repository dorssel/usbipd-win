using System.Threading;
using System.Collections.Generic;

namespace UsbIpServer
{
    class TokenTracker
    {
        public Dictionary<string, CancellationTokenSource> Tokens = new Dictionary<string, CancellationTokenSource>();
    }
}
