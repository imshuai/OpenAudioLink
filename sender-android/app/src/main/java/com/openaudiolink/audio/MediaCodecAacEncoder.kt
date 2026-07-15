package com.openaudiolink.audio

private val CANONICAL_AUDIO_SPECIFIC_CONFIG =
    byteArrayOf(0x11, 0x90.toByte())

internal fun validateCanonicalAudioSpecificConfig(bytes: ByteArray) {
    if (!bytes.contentEquals(CANONICAL_AUDIO_SPECIFIC_CONFIG)) {
        val actual = bytes.joinToString(" ") { byte ->
            "%02X".format(byte.toInt() and 0xff)
        }
        throw IllegalStateException(
            "Expected AudioSpecificConfig 11 90, got $actual.",
        )
    }
}
