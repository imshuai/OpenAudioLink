package com.openaudiolink.network

import com.openaudiolink.protocol.HandshakePayloads
import com.openaudiolink.protocol.PacketParser
import com.openaudiolink.protocol.PacketReader
import com.openaudiolink.protocol.PacketWriter
import com.openaudiolink.protocol.ProtocolConstants
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class HandshakeClientTest {
    @Test
    fun runWritesHandshakePacketsOnSuccess() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultSuccess, "receiver", "1.0", 7)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypeStreamReady, 2, 2, HandshakePayloads.streamReady(ProtocolConstants.StreamResultSuccess, ProtocolConstants.CodecPcm, 48000, 2)) +
                PacketWriter.writePacket(ProtocolConstants.PacketTypePong, 3, 3, HandshakePayloads.ping(5, 123456005))
        )
        val output = ByteArrayOutputStream()

        assertTrue(HandshakeClient.run(input, output))

        val written = ByteArrayInputStream(output.toByteArray())
        assertEquals(ProtocolConstants.PacketTypeHello, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
        assertEquals(ProtocolConstants.PacketTypeStartStream, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
        assertEquals(ProtocolConstants.PacketTypePing, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
        assertEquals(ProtocolConstants.PacketTypeStopStream, PacketParser.parseHeader(PacketReader.readPacket(written)).packetType)
    }

    @Test
    fun runReturnsFalseOnBusyWelcome() {
        val input = ByteArrayInputStream(
            PacketWriter.writePacket(ProtocolConstants.PacketTypeWelcome, 1, 1, HandshakePayloads.welcome(ProtocolConstants.ResultReceiverBusy, "receiver", "1.0", 7))
        )
        val output = ByteArrayOutputStream()

        assertFalse(HandshakeClient.run(input, output))
    }
}
