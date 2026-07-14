package com.openaudiolink.protocol

import com.openaudiolink.TestFixtures
import org.junit.Assert.assertArrayEquals
import org.junit.Test

class PacketWriterTest {
    @Test fun write_hello_matchesFixture() = assertPacket("valid-hello.bin", ProtocolConstants.PacketTypeHello, 1, 123456000, HandshakePayloads.hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported))
    @Test fun write_welcome_matchesFixture() = assertPacket("valid-welcome.bin", ProtocolConstants.PacketTypeWelcome, 2, 123456001, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 0x0102030405060708))
    @Test fun write_startStream_matchesFixture() = assertPacket("valid-start-stream.bin", ProtocolConstants.PacketTypeStartStream, 3, 123456002, HandshakePayloads.startStream(ProtocolConstants.CodecAacLc, 48000, 2, 192000, 21))
    @Test fun write_streamReady_matchesFixture() = assertPacket("valid-stream-ready.bin", ProtocolConstants.PacketTypeStreamReady, 4, 123456003, HandshakePayloads.streamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000, 2))
    @Test fun write_ping_matchesFixture() = assertPacket("valid-ping.bin", ProtocolConstants.PacketTypePing, 5, 123456004, HandshakePayloads.ping(5, 123456005))
    @Test fun write_pong_matchesFixture() = assertPacket("valid-pong.bin", ProtocolConstants.PacketTypePong, 6, 123456004, HandshakePayloads.ping(5, 123456005))
    @Test fun write_stopStream_matchesFixture() = assertPacket("valid-stop-stream.bin", ProtocolConstants.PacketTypeStopStream, 7, 123456006, byteArrayOf())
    @Test
    fun write_audio_matchesFixture() {
        val encoded = TestFixtures.read("testdata/audio/aac-lc-48k-stereo-1024.raw")
        assertPacket(
            "valid-audio-aac.bin",
            ProtocolConstants.PacketTypeAudio,
            2,
            123456789,
            HandshakePayloads.audio(
                ProtocolConstants.CodecAacLc,
                1,
                123456789,
                21,
                encoded,
            ),
        )
    }
    @Test fun write_error_matchesFixture() = assertPacket("valid-error.bin", ProtocolConstants.PacketTypeError, 8, 123456007, HandshakePayloads.error(ProtocolConstants.ErrorUnsupportedCodec, ProtocolConstants.ErrorSeverityRecoverable, "Unsupported codec"))

    private fun assertPacket(name: String, type: Int, sequence: Long, timestamp: Long, payload: ByteArray) =
        assertArrayEquals(readFixture(name), PacketWriter.writePacket(type, sequence, timestamp, payload))

    private fun readFixture(name: String) = TestFixtures.read("testdata/protocol/$name")
}
