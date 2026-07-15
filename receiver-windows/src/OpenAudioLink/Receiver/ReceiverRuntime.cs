using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.ExceptionServices;
using OpenAudioLink.Protocol;

namespace OpenAudioLink.Receiver
{
    public sealed class ReceiverRuntime : IDisposable
    {
        private readonly TcpReceiver receiver;

        private ReceiverRuntime(AudioFrameQueue queue, FakeAudioRenderer renderer, TcpReceiver receiver)
        {
            Queue = queue;
            Renderer = renderer;
            this.receiver = receiver;
            Port = receiver.Port;
        }

        public int Port { get; }

        public AudioFrameQueue Queue { get; }

        public FakeAudioRenderer Renderer { get; }

        public static ReceiverRuntime StartLoopback(int queueCapacity = 8)
        {
            return Start(IPAddress.Loopback, 0, queueCapacity);
        }

        public static ReceiverRuntime Start(IPAddress address, int port, int queueCapacity = 8, Action<int> renderedCountChanged = null)
        {
            if (address == null)
            {
                throw new ArgumentNullException(nameof(address));
            }

            renderedCountChanged = renderedCountChanged ?? (_ => { });
            AudioFrameQueue queue = new AudioFrameQueue(queueCapacity);
            FakeAudioRenderer renderer = new FakeAudioRenderer();
            DecoderSession session = null;
            TcpReceiver receiver = TcpReceiver.Start(
                address,
                port,
                payload =>
                {
                    DecoderSession activeSession = session;
                    if (activeSession == null)
                    {
                        throw new PacketParseException("Audio payload received before stream start.");
                    }

                    activeSession.Accept(payload);
                },
                () =>
                {
                    try
                    {
                        session = new DecoderSession(queue, renderer, renderedCountChanged);
                    }
                    catch (PlatformNotSupportedException error)
                    {
                        throw new PacketParseException("AAC decoder is unavailable.", error);
                    }
                    catch (InvalidOperationException error)
                    {
                        throw new PacketParseException("AAC decoder could not be created.", error);
                    }
                },
                () =>
                {
                    DecoderSession completedSession = session;
                    session = null;
                    if (completedSession != null)
                    {
                        completedSession.Finish();
                    }
                });

            return new ReceiverRuntime(queue, renderer, receiver);
        }

        public void Dispose()
        {
            receiver.Dispose();
        }

        private sealed class DecoderSession
        {
            private const int PcmFrameSize = 4096;
            private const int PcmBlockAlignment = 4;

            private readonly AudioFrameQueue queue;
            private readonly FakeAudioRenderer renderer;
            private readonly Action<int> renderedCountChanged;
            private readonly Queue<FrameMetadata> metadata = new Queue<FrameMetadata>();
            private readonly byte[] pcmFrame = new byte[PcmFrameSize];
            private readonly MediaFoundationAacDecoder decoder;
            private int pcmLength;
            private bool faulted;

            public DecoderSession(
                AudioFrameQueue queue,
                FakeAudioRenderer renderer,
                Action<int> renderedCountChanged)
            {
                if (queue == null)
                {
                    throw new ArgumentNullException(nameof(queue));
                }

                if (renderer == null)
                {
                    throw new ArgumentNullException(nameof(renderer));
                }

                if (renderedCountChanged == null)
                {
                    throw new ArgumentNullException(nameof(renderedCountChanged));
                }

                this.queue = queue;
                this.renderer = renderer;
                this.renderedCountChanged = renderedCountChanged;
                decoder = new MediaFoundationAacDecoder();
            }

            public void Accept(byte[] payload)
            {
                queue.Enqueue(payload);
                byte[] queuedPayload;
                while (queue.TryDequeue(out queuedPayload))
                {
                    if (queuedPayload == null ||
                        queuedPayload.Length < ProtocolConstants.AudioPayloadHeaderSize)
                    {
                        Fault("Invalid audio payload length.");
                    }

                    uint frameNumber = PacketParser.ReadUInt32(queuedPayload, 1);
                    ulong timestamp = ((ulong)PacketParser.ReadUInt32(queuedPayload, 5) << 32) |
                        PacketParser.ReadUInt32(queuedPayload, 9);
                    ushort duration = (ushort)((queuedPayload[13] << 8) | queuedPayload[14]);
                    uint encodedSize = PacketParser.ReadUInt32(queuedPayload, 15);
                    if ((uint)(queuedPayload.Length - ProtocolConstants.AudioPayloadHeaderSize) != encodedSize ||
                        encodedSize > int.MaxValue)
                    {
                        Fault("Invalid audio payload length.");
                    }

                    byte[] encoded = new byte[(int)encodedSize];
                    Buffer.BlockCopy(
                        queuedPayload,
                        ProtocolConstants.AudioPayloadHeaderSize,
                        encoded,
                        0,
                        encoded.Length);
                    metadata.Enqueue(new FrameMetadata(frameNumber, timestamp, duration));

                    IReadOnlyList<byte[]> chunks;
                    try
                    {
                        chunks = decoder.Submit(encoded);
                    }
                    catch (PlatformNotSupportedException error)
                    {
                        throw NativeFault("AAC decoder is unavailable.", error);
                    }
                    catch (InvalidOperationException error)
                    {
                        throw NativeFault("AAC decoder failed to decode audio.", error);
                    }

                    RenderChunks(chunks);
                }

                renderedCountChanged(renderer.RenderedCount);
            }

            public void Finish()
            {
                Exception failure = null;
                try
                {
                    if (!faulted)
                    {
                        IReadOnlyList<byte[]> chunks;
                        try
                        {
                            chunks = decoder.Drain();
                        }
                        catch (PlatformNotSupportedException error)
                        {
                            throw NativeFault("AAC decoder is unavailable.", error);
                        }
                        catch (InvalidOperationException error)
                        {
                            throw NativeFault("AAC decoder failed to drain audio.", error);
                        }

                        RenderChunks(chunks);
                        if (pcmLength != 0)
                        {
                            Fault("AAC decoder produced a partial PCM frame.");
                        }

                        if (metadata.Count != 0)
                        {
                            Fault("AAC decoder did not produce PCM for every audio frame.");
                        }

                        if (queue.Count != 0)
                        {
                            Fault("Audio frame queue was not drained.");
                        }

                        renderedCountChanged(renderer.RenderedCount);
                    }
                }
                catch (Exception error)
                {
                    failure = error;
                }

                try
                {
                    decoder.Dispose();
                }
                catch (Exception error)
                {
                    if (failure == null)
                    {
                        failure = new PacketParseException("AAC decoder could not be disposed.", error);
                    }
                    else
                    {
                        failure.Data["OpenAudioLink.DecoderDisposeError"] = error;
                    }
                }

                if (failure != null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
            }

            private void RenderChunks(IReadOnlyList<byte[]> chunks)
            {
                if (chunks == null)
                {
                    Fault("AAC decoder returned no PCM chunks.");
                }

                foreach (byte[] chunk in chunks)
                {
                    if (chunk == null || chunk.Length == 0 || chunk.Length % PcmBlockAlignment != 0)
                    {
                        Fault("AAC decoder returned an invalid PCM chunk.");
                    }

                    int offset = 0;
                    while (offset < chunk.Length)
                    {
                        int copied = Math.Min(PcmFrameSize - pcmLength, chunk.Length - offset);
                        Buffer.BlockCopy(chunk, offset, pcmFrame, pcmLength, copied);
                        offset += copied;
                        pcmLength += copied;
                        if (pcmLength == PcmFrameSize)
                        {
                            if (metadata.Count == 0)
                            {
                                Fault("AAC decoder produced PCM without audio metadata.");
                            }

                            FrameMetadata frame = metadata.Dequeue();
                            renderer.Render(
                                new FakePcmFrame(
                                    frame.FrameNumber,
                                    frame.Timestamp,
                                    frame.Duration,
                                    pcmFrame));
                            pcmLength = 0;
                        }
                    }
                }
            }

            private PacketParseException NativeFault(string message, Exception error)
            {
                faulted = true;
                return new PacketParseException(message, error);
            }

            private void Fault(string message)
            {
                faulted = true;
                throw new PacketParseException(message);
            }

            private sealed class FrameMetadata
            {
                public FrameMetadata(uint frameNumber, ulong timestamp, ushort duration)
                {
                    FrameNumber = frameNumber;
                    Timestamp = timestamp;
                    Duration = duration;
                }

                public uint FrameNumber { get; }

                public ulong Timestamp { get; }

                public ushort Duration { get; }
            }
        }
    }
}
