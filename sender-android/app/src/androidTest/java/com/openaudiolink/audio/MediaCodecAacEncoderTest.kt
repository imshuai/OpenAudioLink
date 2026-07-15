package com.openaudiolink.audio

import android.os.Build
import android.util.Log
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference
import kotlin.math.PI
import kotlin.math.roundToInt
import kotlin.math.sin

@RunWith(AndroidJUnit4::class)
class MediaCodecAacEncoderTest {
    @Test
    fun instrumentationRunsOnSupportedAndroid() {
        assertTrue(Build.VERSION.SDK_INT >= 29)
    }

    @Test
    fun encodesAndDrainsTwiceInOneProcess() {
        val first = encodeOnce()
        val second = encodeOnce()

        assertEquals(INPUT_FRAME_COUNT, first.size)
        assertEquals(INPUT_FRAME_COUNT, second.size)
    }

    @Test
    fun rejectsWrongPcmLengthAndInputTimestamps() {
        MediaCodecAacEncoder().use { encoder ->
            assertThrows(IllegalArgumentException::class.java) {
                encoder.submit(ByteArray(PCM_BYTES_PER_FRAME - 1), 0L)
            }
            assertThrows(IllegalArgumentException::class.java) {
                encoder.submit(ByteArray(PCM_BYTES_PER_FRAME + 1), 0L)
            }
            assertThrows(IllegalArgumentException::class.java) {
                encoder.submit(pcmFrame(0), -1L)
            }

            encoder.submit(pcmFrame(0), 0L)
            assertThrows(IllegalArgumentException::class.java) {
                encoder.submit(pcmFrame(1), 0L)
            }
        }
    }

    @Test
    fun drainAndCloseAreIdempotentAndCloseInput() {
        val encoder = MediaCodecAacEncoder()
        try {
            encoder.submit(pcmFrame(0), 0L)
            encoder.drain()
            assertTrue(encoder.drain().isEmpty())
            assertThrows(IllegalStateException::class.java) {
                encoder.submit(pcmFrame(1), sampleTimeUs(1L))
            }

            encoder.close()
            encoder.close()
            assertThrows(IllegalStateException::class.java) {
                encoder.drain()
            }
        } finally {
            encoder.close()
        }
    }

    @Test
    fun callsFromAnotherThreadAreRejected() {
        val encoder = MediaCodecAacEncoder()
        try {
            assertWrongThread { encoder.submit(pcmFrame(0), 0L) }
            assertWrongThread { encoder.drain() }
            assertWrongThread { encoder.close() }
        } finally {
            encoder.close()
        }
    }

    private fun encodeOnce(): List<EncodedAccessUnit> {
        val output = mutableListOf<EncodedAccessUnit>()
        MediaCodecAacEncoder().use { encoder ->
            Log.i(TAG, "AAC encoder: ${encoder.codecName}")
            repeat(INPUT_FRAME_COUNT) { frameIndex ->
                output += encoder.submit(pcmFrame(frameIndex), sampleTimeUs(frameIndex.toLong()))
            }
            output += encoder.drain()
            Log.i(
                TAG,
                "AAC output: ${output.size} AU(s), PTS(us)=" +
                    output.joinToString(prefix = "[", postfix = "]") {
                        it.presentationTimeUs.toString()
                    },
            )
            assertTrue(encoder.drain().isEmpty())
        }

        assertEquals(INPUT_FRAME_COUNT, output.size)
        output.forEach { unit ->
            assertTrue(unit.bytes.isNotEmpty())
            assertTrue(unit.bytes.size <= MAX_ACCESS_UNIT_BYTES)
        }
        return output
    }

    private fun pcmFrame(frameIndex: Int): ByteArray {
        val bytes = ByteArray(PCM_BYTES_PER_FRAME)
        val firstSampleIndex = frameIndex.toLong() * SAMPLES_PER_FRAME
        repeat(SAMPLES_PER_FRAME) { sampleOffset ->
            val sampleIndex = firstSampleIndex + sampleOffset
            val byteOffset = sampleOffset * BYTES_PER_STEREO_SAMPLE
            putLittleEndian(bytes, byteOffset, sineSample(LEFT_FREQUENCY_HZ, sampleIndex))
            putLittleEndian(bytes, byteOffset + 2, sineSample(RIGHT_FREQUENCY_HZ, sampleIndex))
        }
        return bytes
    }

    private fun sineSample(frequencyHz: Double, sampleIndex: Long): Short =
        (sin(2.0 * PI * frequencyHz * sampleIndex.toDouble() / SAMPLE_RATE) * AMPLITUDE)
            .roundToInt()
            .toShort()

    private fun putLittleEndian(bytes: ByteArray, offset: Int, sample: Short) {
        val value = sample.toInt()
        bytes[offset] = value.toByte()
        bytes[offset + 1] = (value ushr 8).toByte()
    }

    private fun sampleTimeUs(frameIndex: Long): Long =
        Math.addExact(Math.multiplyExact(frameIndex, 64_000L), 1L) / 3L

    private fun assertWrongThread(block: () -> Unit) {
        val error = AtomicReference<Throwable?>()
        val worker = Thread {
            try {
                block()
            } catch (throwable: Throwable) {
                error.set(throwable)
            }
        }
        worker.isDaemon = true
        worker.start()
        worker.join(30_000L)

        assertFalse(worker.isAlive)
        assertTrue(error.get() is IllegalStateException)
    }

    private companion object {
        const val TAG = "MediaCodecAacTest"
        const val SAMPLE_RATE = 48_000.0
        const val SAMPLES_PER_FRAME = 1024
        const val INPUT_FRAME_COUNT = 12
        const val PCM_BYTES_PER_FRAME = 4096
        const val BYTES_PER_STEREO_SAMPLE = 4
        const val MAX_ACCESS_UNIT_BYTES = 65_536
        const val LEFT_FREQUENCY_HZ = 440.0
        const val RIGHT_FREQUENCY_HZ = 660.0
        const val AMPLITUDE = 12_000.0
    }
}
