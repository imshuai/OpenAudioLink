package com.openaudiolink.network

import com.openaudiolink.protocol.HandshakePayloads
import com.openaudiolink.protocol.PacketParser
import com.openaudiolink.protocol.PacketReader
import com.openaudiolink.protocol.PacketWriter
import com.openaudiolink.protocol.ProtocolConstants
import java.io.InputStream
import java.io.OutputStream

object HandshakeClient {
    private val pingPayload = HandshakePayloads.ping(5, 123456005)

    fun run(input: InputStream, output: OutputStream): Boolean {
        output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeHello, 1, 123456000, HandshakePayloads.hello("Android", "1.0", ProtocolConstants.PlatformAndroid, 0)))
        if (!readResult(input, ProtocolConstants.PacketTypeWelcome, ProtocolConstants.ResultSuccess)) return false

        output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeStartStream, 2, 123456002, HandshakePayloads.startStream(ProtocolConstants.CodecPcm, 48000, 2, 1536000, 20)))
        if (!readResult(input, ProtocolConstants.PacketTypeStreamReady, ProtocolConstants.StreamResultSuccess)) return false

        output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypePing, 3, 123456004, pingPayload))
        val pong = PacketReader.readPacket(input)
        if (PacketParser.parseHeader(pong).packetType != ProtocolConstants.PacketTypePong || !PacketParser.payload(pong).contentEquals(pingPayload)) return false

        output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeStopStream, 4, 123456006))
        return true
    }

    private fun readResult(input: InputStream, packetType: Int, success: Int): Boolean {
        val packet = PacketReader.readPacket(input)
        return PacketParser.parseHeader(packet).packetType == packetType && PacketParser.payload(packet).firstOrNull()?.toInt() == success
    }
}
