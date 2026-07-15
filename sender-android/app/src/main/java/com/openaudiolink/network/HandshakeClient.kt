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

class HandshakeClient(
    private val audioFrameSupplier: () -> List<ByteArray>,
) {
    fun run(input: InputStream, output: OutputStream): Boolean {
        try {
            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeHello, 1, HELLO_TIMESTAMP, HandshakePayloads.hello("Android Phone", "1.0.0", ProtocolConstants.PlatformAndroid, ProtocolConstants.CapabilityAacSupported)))
            output.flush()
            if (!readResult(input, ProtocolConstants.PacketTypeWelcome, ProtocolConstants.ResultSuccess)) return false

            output.write(PacketWriter.writePacket(ProtocolConstants.PacketTypeStartStream, 2, START_TIMESTAMP, HandshakePayloads.startStream(ProtocolConstants.CodecAacLc, 48000, 2, 192000, 21)))
            output.flush()
            if (!readResult(input, ProtocolConstants.PacketTypeStreamReady, ProtocolConstants.StreamResultSuccess)) return false

            val frames = audioFrameSupplier()
            require(frames.isNotEmpty()) { "Encoded test stream is empty." }
            frames.forEach { frame ->
                require(frame.isNotEmpty()) { "AAC access unit is empty." }
                require(frame.size <= MAX_ENCODED_SIZE) {
                    "AAC access unit exceeds wire payload size."
                }
            }

            frames.forEachIndexed { index, encoded ->
                val timestamp = audioTimestamp(index)
                output.write(PacketWriter.writePacket(
                    ProtocolConstants.PacketTypeAudio,
                    index.toLong() + 3,
                    timestamp,
                    HandshakePayloads.audio(
                        ProtocolConstants.CodecAacLc,
                        index.toLong() + 1,
                        timestamp,
                        21,
                        encoded,
                    ),
                ))
                output.flush()
            }

            val lastAudioSequence = frames.size.toLong() + 2
            val lastTimestamp = audioTimestamp(frames.lastIndex)
            val pingPayload = HandshakePayloads.ping(
                lastAudioSequence,
                lastTimestamp + 1,
            )
            output.write(PacketWriter.writePacket(
                ProtocolConstants.PacketTypePing,
                lastAudioSequence + 1,
                lastTimestamp + 2,
                pingPayload,
            ))
            output.flush()
            val pong = PacketReader.readPacket(input)
            if (PacketParser.parseHeader(pong).packetType != ProtocolConstants.PacketTypePong || !PacketParser.payload(pong).contentEquals(pingPayload)) return false

            output.write(PacketWriter.writePacket(
                ProtocolConstants.PacketTypeStopStream,
                lastAudioSequence + 2,
                lastTimestamp + 3,
            ))
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

    private fun audioTimestamp(index: Int): Long =
        AUDIO_BASE_TIMESTAMP + (index.toLong() * 64_000L + 1L) / 3L

    private companion object {
        const val HELLO_TIMESTAMP = 123456000L
        const val START_TIMESTAMP = 123456002L
        const val AUDIO_BASE_TIMESTAMP = 123456003L
        const val MAX_ENCODED_SIZE =
            ProtocolConstants.MaxPacketSize - ProtocolConstants.AudioPayloadHeaderSize
    }
}
