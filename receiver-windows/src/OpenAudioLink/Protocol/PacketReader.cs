using System;
using System.IO;

namespace OpenAudioLink.Protocol
{
    public static class PacketReader
    {
        public static byte[] ReadPacket(Stream stream)
        {
            byte[] header = ReadExact(stream, ProtocolConstants.HeaderSize);
            uint payloadLength = PacketParser.ReadUInt32(header, 20);
            if (payloadLength > ProtocolConstants.MaxPacketSize)
            {
                throw new PacketParseException("Payload is too large.");
            }

            byte[] packet = new byte[ProtocolConstants.HeaderSize + (int)payloadLength];
            Buffer.BlockCopy(header, 0, packet, 0, header.Length);
            byte[] payload = ReadExact(stream, (int)payloadLength);
            Buffer.BlockCopy(payload, 0, packet, ProtocolConstants.HeaderSize, payload.Length);
            PacketParser.ParseHeader(packet);
            return packet;
        }

        private static byte[] ReadExact(Stream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("Connection closed while reading packet.");
                }

                offset += read;
            }

            return buffer;
        }
    }
}
