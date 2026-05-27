using System;
using Utils.Framework;

namespace PhinixClient.Framework
{
    public sealed class FrameworkDisplayMessageEventArgs : EventArgs
    {
        public FrameworkDisplayMessageEventArgs(FrameworkDisplayMessage message)
        {
            Message = message;
        }

        public FrameworkDisplayMessage Message { get; }
    }
}
