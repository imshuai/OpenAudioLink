package com.openaudiolink.audio

internal fun encodeDeterministicTestStream(): List<ByteArray> {
    val output = mutableListOf<EncodedAccessUnit>()
    MediaCodecAacEncoder().use { encoder ->
        repeat(INPUT_FRAME_COUNT) { index ->
            output += encoder.submit(pcmFrame(index), inputTimeUs(index))
        }
        output += encoder.drain()
    }
    return output.map { it.bytes }
}

private fun pcmFrame(frameIndex: Int): ByteArray {
    val bytes = ByteArray(PCM_BYTES_PER_FRAME)
    val firstSample = frameIndex * SAMPLES_PER_FRAME
    repeat(SAMPLES_PER_FRAME) { offset ->
        val sample = firstSample + offset
        putPcm16(bytes, offset * BYTES_PER_STEREO_SAMPLE, square(sample, 55, 12_000))
        putPcm16(bytes, offset * BYTES_PER_STEREO_SAMPLE + 2, square(sample, 37, 9_000))
    }
    return bytes
}

private fun square(sample: Int, halfPeriod: Int, amplitude: Int): Short =
    if ((sample / halfPeriod) % 2 == 0) amplitude.toShort() else (-amplitude).toShort()

private fun putPcm16(bytes: ByteArray, offset: Int, sample: Short) {
    val value = sample.toInt()
    bytes[offset] = value.toByte()
    bytes[offset + 1] = (value ushr 8).toByte()
}

private fun inputTimeUs(index: Int): Long =
    (index.toLong() * 64_000L + 1L) / 3L

private const val INPUT_FRAME_COUNT = 3
private const val SAMPLES_PER_FRAME = 1024
private const val BYTES_PER_STEREO_SAMPLE = 4
private const val PCM_BYTES_PER_FRAME =
    SAMPLES_PER_FRAME * BYTES_PER_STEREO_SAMPLE
