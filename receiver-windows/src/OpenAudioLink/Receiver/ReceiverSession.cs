using System;
using OpenAudioLink.Protocol;

namespace OpenAudioLink.Receiver
{
    public enum ReceiverSessionState
    {
        WaitingForHello,
        WaitingForStartStream,
        Streaming,
        Stopped
    }

    public sealed class ReceiverSession
    {
        private const string ReceiverName = "Windows PC";
        private const string ReceiverVersion = "1.0.0";
        private readonly ulong sessionId;
        private readonly Action<byte[]> audioSink;
        private uint nextSequence = 1;

        public ReceiverSession(ulong sessionId)
            : this(sessionId, null)
        {
        }

        public ReceiverSession(ulong sessionId, Action<byte[]> audioSink)
        {
            this.sessionId = sessionId;
            this.audioSink = audioSink ?? (_ => { });
            State = ReceiverSessionState.WaitingForHello;
        }

        public ReceiverSessionState State { get; private set; }

        public int AudioFramesReceived { get; private set; }

        public byte[] LastAudioPayload { get; private set; }

        public byte[] Process(byte[] packet)
        {
            PacketHeader header = PacketParser.ParseHeader(packet);
            byte[] payload = PacketParser.Payload(packet);

            switch (State)
            {
                case ReceiverSessionState.WaitingForHello:
                    return ProcessWaitingForHello(header);
                case ReceiverSessionState.WaitingForStartStream:
                    return ProcessWaitingForStartStream(header, payload);
                case ReceiverSessionState.Streaming:
                    return ProcessStreaming(header, payload);
                default:
                    throw new PacketParseException("Session is stopped.");
            }
        }

        public static byte[] BusyWelcome()
        {
            return PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeWelcome,
                1u,
                0,
                HandshakePayloads.Welcome(ProtocolConstants.ResultReceiverBusy, ReceiverName, ReceiverVersion, 0));
        }

        private byte[] ProcessWaitingForHello(PacketHeader header)
        {
            if (header.PacketType != ProtocolConstants.PacketTypeHello)
            {
                throw new PacketParseException("Expected HELLO.");
            }

            State = ReceiverSessionState.WaitingForStartStream;
            return PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeWelcome,
                nextSequence++,
                0,
                HandshakePayloads.Welcome(ProtocolConstants.ResultSuccess, ReceiverName, ReceiverVersion, sessionId));
        }

        private byte[] ProcessWaitingForStartStream(PacketHeader header, byte[] payload)
        {
            if (header.PacketType == ProtocolConstants.PacketTypeStopStream)
            {
                State = ReceiverSessionState.Stopped;
                return null;
            }

            if (header.PacketType != ProtocolConstants.PacketTypeStartStream)
            {
                throw new PacketParseException("Expected START_STREAM.");
            }

            if (HandshakePayloads.ReadStartStreamCodec(payload) != ProtocolConstants.CodecAacLc)
            {
                State = ReceiverSessionState.Stopped;
                return PacketWriter.WritePacket(
                    ProtocolConstants.PacketTypeStreamReady,
                    nextSequence++,
                    0,
                    HandshakePayloads.StreamReady(ProtocolConstants.StreamResultUnsupportedCodec, 0, 0, 0));
            }

            State = ReceiverSessionState.Streaming;
            return PacketWriter.WritePacket(
                ProtocolConstants.PacketTypeStreamReady,
                nextSequence++,
                0,
                HandshakePayloads.StreamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000u, 2));
        }

        private byte[] ProcessStreaming(PacketHeader header, byte[] payload)
        {
            if (header.PacketType == ProtocolConstants.PacketTypePing)
            {
                return PacketWriter.WritePacket(ProtocolConstants.PacketTypePong, nextSequence++, header.Timestamp, payload);
            }

            if (header.PacketType == ProtocolConstants.PacketTypeStopStream)
            {
                State = ReceiverSessionState.Stopped;
                return null;
            }

            if (header.PacketType == ProtocolConstants.PacketTypeAudio)
            {
                AudioPayloadValidator.ValidateAacPayload(payload);
                byte[] acceptedPayload = (byte[])payload.Clone();
                audioSink((byte[])acceptedPayload.Clone());
                LastAudioPayload = acceptedPayload;
                AudioFramesReceived++;
                return null;
            }

            throw new PacketParseException("Expected PING, STOP_STREAM, or AUDIO.");
        }
    }
}
