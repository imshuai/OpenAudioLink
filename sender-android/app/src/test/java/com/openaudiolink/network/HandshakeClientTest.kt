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
                PacketWriter.writePacket(ProtocolConstants.PacketTypePong, 3, 3, HandshakePayloads.ping(5, 123456005))
        )
        val output = ByteArrayOutputStream()

        assertTrue(HandshakeClient().run(input, output))

        val written = ByteArrayInputStream(output.toByteArray())
        assertEquals(ProtocolConstants.PacketTypeHello, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
        assertEquals(ProtocolConstants.PacketTypeStartStream, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
        val audio = PacketReader.readPacket(written)
        assertEquals(ProtocolConstants.PacketTypeAudio, PacketParser.parseHeader(audio).packetType)
        assertArrayEquals(
            HandshakePayloads.audio(ProtocolConstants.CodecAacLc, 1, 123456789, 20, byteArrayOf(0x11, 0x22, 0x33, 0x44)),
            PacketParser.payload(audio)
        )
        assertEquals(ProtocolConstants.PacketTypePing, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
        assertEquals(ProtocolConstants.PacketTypeStopStream, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
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
}
