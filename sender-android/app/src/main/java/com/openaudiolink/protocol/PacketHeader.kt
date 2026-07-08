package com.openaudiolink.protocol

data class PacketHeader(
    val majorVersion: Int,
    val minorVersion: Int,
    val packetType: Int,
    val flags: Int,
    val sequenceNumber: Long,
    val timestamp: Long,
    val payloadLength: Int,
)
