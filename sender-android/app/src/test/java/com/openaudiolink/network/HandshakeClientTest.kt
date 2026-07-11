package com.openaudiolink.network

import com.openaudiolink.protocol.HandshakePayloads
import com.openaudiolink.protocol.PacketParser
import com.openaudiolink.protocol.PacketReader
import com.openaudiolink.protocol.PacketWriter
import com.openaudiolink.protocol.ProtocolConstants
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import org.junit.Assert.*
import org.junit.Test

class HandshakeClientTest {
    @Test
    fun runWritesHandshakePacketsOnSuccess() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "receiver", "1.0", 7)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypeStreamReady, 2, 2, HandshakePayloads.streamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000, 2)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypePong, 6, 6, HandshakePayloads.ping(5, 123456005))
        )
        val output = ByteArrayOutputStream()

        assertTrue(HandshakeClient().run(input, output))

        val written = ByteArrayInputStream(output.toByteArray())
        assertPacket(written, ProtocolConstants.PacketTypeHello, 1)
        assertPacket(written, ProtocolConstants.PacketTypeStartStream, 2)
        assertArrayEquals(
            HandshakePayloads.audio(ProtocolConstants.CodecAacLc, 1, 123456003, 20, byteArrayOf(0x11, 0x22, 0x33, 0x44)),
            assertPacket(written, ProtocolConstants.PacketTypeAudio, 3)
        )
        assertArrayEquals(
            HandshakePayloads.audio(ProtocolConstants.CodecAacLc, 2, 123456023, 20, byteArrayOf(0x21, 0x22, 0x23, 0x24)),
            assertPacket(written, ProtocolConstants.PacketTypeAudio, 4)
        )
        assertArrayEquals(
            HandshakePayloads.audio(ProtocolConstants.CodecAacLc, 3, 123456043, 20, byteArrayOf(0x31, 0x32, 0x33, 0x34)),
            assertPacket(written, ProtocolConstants.PacketTypeAudio, 5)
        )
        assertArrayEquals(HandshakePayloads.ping(5, 123456005), assertPacket(written, ProtocolConstants.PacketTypePing, 6))
        assertPacket(written, ProtocolConstants.PacketTypeStopStream, 7)
        assertEquals(0, written.available())
    }

    @Test
    fun runReturnsFalseOnBusyWelcome() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultReceiverBusy, "receiver", "1.0", 7))
        )
        val output = ByteArrayOutputStream()

        assertFalse(HandshakeClient().run(input, output))
    }

    @Test
    fun runReturnsFalseOnTimeout() {
        assertFalse(HandshakeClient().run(ByteArrayInputStream(byteArrayOf()), ByteArrayOutputStream()))
    }

    @Test
    fun runReturnsFalseOnProtocolRejection() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultUnsupportedProtocol, "receiver", "1.0", 7))
        )

        assertFalse(HandshakeClient().run(input, ByteArrayOutputStream()))
    }

    @Test
    fun runReturnsFalseOnStreamReadyFailure() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "receiver", "1.0", 7)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypeStreamReady, 2, 2, HandshakePayloads.streamReady(ProtocolConstants.StreamResultUnsupportedCodec, ProtocolConstants.CodecAacLc, 48000, 2))
        )

        assertFalse(HandshakeClient().run(input, ByteArrayOutputStream()))
    }

    @Test
    fun runReturnsFalseOnPongPayloadMismatch() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "receiver", "1.0", 7)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypeStreamReady, 2, 2, HandshakePayloads.streamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecAacLc, 48000, 2)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypePong, 3, 3, HandshakePayloads.ping(6, 123456005))
        )

        assertFalse(HandshakeClient().run(input, ByteArrayOutputStream()))
    }

    private fun assertPacket(input: ByteArrayInputStream, packetType: Int, sequenceNumber: Long): ByteArray {
        val packet = PacketReader.readPacket(input)
        val header = PacketParser.parseHeader(packet)
        assertEquals(packetType, header.packetType)
        assertEquals(sequenceNumber, header.sequenceNumber)
        return PacketParser.payload(packet)
    }
}
