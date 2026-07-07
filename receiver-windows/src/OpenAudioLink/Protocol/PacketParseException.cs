using System;

namespace OpenAudioLink.Protocol
{
    public sealed class PacketParseException : Exception
    {
        public PacketParseException(string message) : base(message)
        {
        }
    }
}
