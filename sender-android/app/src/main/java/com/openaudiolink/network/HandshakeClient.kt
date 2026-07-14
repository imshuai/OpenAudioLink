package com.openaudiolink.network

import com.openaudiolink.protocol.HandshakePayloads
import com.openaudiolink.protocol.PacketParser
import com.openaudiolink.protocol.PacketReader
import com.openaudiolink.protocol.PacketWriter
import com.openaudiolink.protocol.ProtocolConstants
import com.openaudiolink.protocol.PacketParseException
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream

class HandshakeClient {
    private val pingPayload = HandshakePayloads.ping(5, 123498671)
    private val fakeAudioFrames = listOf(
        FakeAudioFrame(1, 123456003),
        FakeAudioFrame(2, 123477336),
        FakeAudioFrame(3, 123498670),
    )

    private data class FakeAudioFrame(
        val frameNumber: Long,
        val captureTimestamp: Long,
    )

    fun run(input: InputStream, output: OutputStream): Boolean {
        try {
            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeHello, 1, 123456000, HandshakePayloads.hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported)))
            output.flush()
            if (!readResult(input, ProtocolConstants.PacketTypeWelcome, ProtocolConstants.ResultSuccess)) return false

            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeStartStream, 2, 123456002, HandshakePayloads.startStream(ProtocolConstants.CodecAacLc, 48000, 2, 192000, 21)))
            output.flush()
            if (!readResult(input, ProtocolConstants.PacketTypeStreamReady, ProtocolConstants.StreamResultSuccess)) return false

            for (frame in fakeAudioFrames) {
                output.write(PacketWriter.writePacket(
                    ProtocolConstants.PacketTypeAudio,
                    frame.frameNumber + 2,
                    frame.captureTimestamp,
                    HandshakePayloads.audio(ProtocolConstants.CodecAacLc, frame.frameNumber, frame.captureTimestamp, 21, FakeAacFrameBytes)
                ))
                output.flush()
            }

            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypePing, 6, 123498672, pingPayload))
            output.flush()
            val pong = PacketReader.readPacket(input)
            if (PacketParser.parseHeader(pong).packetType != ProtocolConstants.PacketTypePong || !PacketParser.payload(pong).contentEquals(pingPayload)) return false

            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeStopStream, 7, 123498673))
            output.flush()
            return input.read() == -1
        } catch (_: IOException) {
            return false
        } catch (_: PacketParseException) {
            return false
        }
    }

    private fun readResult(input: InputStream, packetType: Int, success: Int): Boolean {
        val packet = PacketReader.readPacket(input)
        return PacketParser.parseHeader(packet).packetType == packetType && PacketParser.payload(packet).firstOrNull()?.toInt() == success
    }
}
