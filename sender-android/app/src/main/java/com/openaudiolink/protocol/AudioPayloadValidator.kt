package com.openaudiolink.protocol

object AudioPayloadValidator {
    fun validateAacPayload(payload: ByteArray?) {
        if (payload == null) throw PacketParseException("Payload is null.")
        if (payload.size < ProtocolConstants.AudioPayloadHeaderSize) {
            throw PacketParseException("Payload is shorter than AAC header.")
        }
        if ((payload[0].toInt() and 0xff) != ProtocolConstants.CodecAacLc) {
            throw PacketParseException("Unsupported codec.")
        }

        val encodedSize = PacketParser.readUInt32(payload, 15)
        if (payload.size.toLong() != ProtocolConstants.AudioPayloadHeaderSize.toLong() + encodedSize) {
            throw PacketParseException("AAC payload length mismatch.")
        }
    }
}
