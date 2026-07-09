package com.openaudiolink.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Test
import java.io.File
import java.io.FileNotFoundException

class PacketWriterTest {
    @Test fun write_welcome_matchesFixture() = assertPacket("valid-welcome.bin", ProtocolConstants.PacketTypeWelcome, 2, 123456001, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "Windows PC", "1.0.0", 0x0102030405060708))
    @Test fun write_startStream_matchesFixture() = assertPacket("valid-start-stream.bin", ProtocolConstants.PacketTypeStartStream, 3, 123456002, HandshakePayloads.startStream(ProtocolConstants.CodecAacLc, 48000, 2, 192000, 20))
    @Test fun write_streamReady_matchesFixture() = assertPacket("valid-stream-ready.bin", ProtocolConstants.PacketTypeStreamReady, 4, 123456003, HandshakePayloads.streamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000, 2))
    @Test fun write_ping_matchesFixture() = assertPacket("valid-ping.bin", ProtocolConstants.PacketTypePing, 5, 123456004, HandshakePayloads.ping(5, 123456005))
    @Test fun write_pong_matchesFixture() = assertPacket("valid-pong.bin", ProtocolConstants.PacketTypePong, 6, 123456004, HandshakePayloads.ping(5, 123456005))
    @Test fun write_stopStream_matchesFixture() = assertPacket("valid-stop-stream.bin", ProtocolConstants.PacketTypeStopStream, 7, 123456006, byteArrayOf())
    @Test fun write_error_matchesFixture() = assertPacket("valid-error.bin", ProtocolConstants.PacketTypeError, 8, 123456007, HandshakePayloads.error(ProtocolConstants.ErrorUnsupportedCodec, ProtocolConstants.ErrorSeverityRecoverable, "Unsupported codec"))

    private fun assertPacket(name: String, type: Int, sequence: Long, timestamp: Long, payload: ByteArray) =
        assertArrayEquals(readFixture(name), PacketWriter.write(type, sequence, timestamp, payload))

    private fun readFixture(name: String): ByteArray {
        var directory: File? = File(System.getProperty("user.dir")).absoluteFile
        while (directory != null) {
            listOf(File(directory, "testdata/protocol/$name"), File(directory, "../testdata/protocol/$name"))
                .firstOrNull { it.isFile }?.let { return it.readBytes() }
            directory = directory.parentFile
        }
        throw FileNotFoundException("Fixture not found: $name")
    }
}
