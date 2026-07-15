using System;

namespace OpenAudioLink.Protocol
{
    public sealed class PacketParseException : Exception
    {
        public PacketParseException(string message) : base(message)
        {
        }

        public PacketParseException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
