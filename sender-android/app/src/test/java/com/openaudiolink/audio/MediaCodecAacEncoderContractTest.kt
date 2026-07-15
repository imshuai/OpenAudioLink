package com.openaudiolink.audio

import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class MediaCodecAacEncoderContractTest {
    @Test
    fun acceptsOnlyCanonicalAudioSpecificConfig() {
        validateCanonicalAudioSpecificConfig(byteArrayOf(0x11, 0x90.toByte()))

        listOf(
            byteArrayOf(),
            byteArrayOf(0x11),
            byteArrayOf(0x11, 0x90.toByte(), 0x00),
            byteArrayOf(0x29, 0x90.toByte()),
            byteArrayOf(0x12, 0x10),
            byteArrayOf(0x11, 0x88.toByte()),
        ).forEach { invalid ->
            val error = assertThrows(IllegalStateException::class.java) {
                validateCanonicalAudioSpecificConfig(invalid)
            }
            assertTrue(error.message.orEmpty().contains("11 90"))
        }
    }
}
