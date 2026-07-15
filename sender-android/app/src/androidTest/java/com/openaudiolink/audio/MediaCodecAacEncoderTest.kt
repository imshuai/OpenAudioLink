package com.openaudiolink.audio

import android.os.Build
import android.util.Log
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.io.FileOutputStream
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

        assertTrue(first.isNotEmpty())
        assertTrue(second.isNotEmpty())
        writeAndValidateArtifact(first)
    }

    @Test
    fun emptyInputReturnsCodecAddedCandidate() {
        val output = MediaCodecAacEncoder().use { encoder -> encoder.drain() }

        assertEquals(CODEC_ADDED_CANDIDATE_COUNT, output.size)
        val candidate = output.single()
        assertTrue(candidate.bytes.isNotEmpty())
        assertTrue(candidate.bytes.size <= MAX_ACCESS_UNIT_BYTES)
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

        assertEquals(EXPECTED_OUTPUT_CANDIDATE_COUNT, output.size)
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

    private fun writeAndValidateArtifact(units: List<EncodedAccessUnit>) {
        val context = InstrumentationRegistry.getInstrumentation().targetContext
        val artifact = File(context.filesDir, ARTIFACT_NAME)
        FileOutputStream(artifact, false).use { stream ->
            units.forEach { unit ->
                stream.write(adtsHeader(unit.bytes.size))
                stream.write(unit.bytes)
            }
        }

        val stored = artifact.readBytes()
        var offset = 0
        units.forEach { unit ->
            assertTrue(stored.size - offset >= ADTS_HEADER_BYTES)
            assertEquals(0xff, stored[offset].toInt() and 0xff)
            assertEquals(0xf1, stored[offset + 1].toInt() and 0xff)
            val profile = (stored[offset + 2].toInt() ushr 6) and 3
            val sampleRateIndex = (stored[offset + 2].toInt() ushr 2) and 15
            val channelConfiguration =
                ((stored[offset + 2].toInt() and 1) shl 2) or
                    ((stored[offset + 3].toInt() ushr 6) and 3)
            assertEquals(1, profile)
            assertEquals(3, sampleRateIndex)
            assertEquals(2, channelConfiguration)
            assertEquals(0xfc, stored[offset + 6].toInt() and 0xff)
            val frameLength =
                ((stored[offset + 3].toInt() and 3) shl 11) or
                    ((stored[offset + 4].toInt() and 0xff) shl 3) or
                    ((stored[offset + 5].toInt() ushr 5) and 7)
            assertEquals(ADTS_HEADER_BYTES + unit.bytes.size, frameLength)
            val raw = stored.copyOfRange(
                offset + ADTS_HEADER_BYTES,
                offset + frameLength,
            )
            assertArrayEquals(unit.bytes, raw)
            offset += frameLength
        }
        assertEquals(stored.size, offset)
    }

    private fun adtsHeader(rawSize: Int): ByteArray {
        val frameLength = Math.addExact(ADTS_HEADER_BYTES, rawSize)
        require(frameLength <= MAX_ADTS_FRAME_BYTES) {
            "AAC access unit is too large for ADTS."
        }
        return byteArrayOf(
            0xff.toByte(),
            0xf1.toByte(),
            ((AAC_LC_ADTS_PROFILE shl 6) or
                (SAMPLE_RATE_INDEX shl 2) or
                (CHANNEL_CONFIGURATION ushr 2)).toByte(),
            (((CHANNEL_CONFIGURATION and 3) shl 6) or
                (frameLength ushr 11)).toByte(),
            (frameLength ushr 3).toByte(),
            (((frameLength and 7) shl 5) or 0x1f).toByte(),
            0xfc.toByte(),
        )
    }

    private companion object {
        const val TAG = "MediaCodecAacTest"
        const val SAMPLE_RATE = 48_000.0
        const val SAMPLES_PER_FRAME = 1024
        const val INPUT_FRAME_COUNT = 12
        const val CODEC_ADDED_CANDIDATE_COUNT = 1
        const val EXPECTED_OUTPUT_CANDIDATE_COUNT =
            INPUT_FRAME_COUNT + CODEC_ADDED_CANDIDATE_COUNT
        const val PCM_BYTES_PER_FRAME = 4096
        const val BYTES_PER_STEREO_SAMPLE = 4
        const val MAX_ACCESS_UNIT_BYTES = 65_536
        const val LEFT_FREQUENCY_HZ = 440.0
        const val RIGHT_FREQUENCY_HZ = 660.0
        const val AMPLITUDE = 12_000.0
        const val ARTIFACT_NAME = "mediacodec-aac-interop.adts"
        const val ADTS_HEADER_BYTES = 7
        const val MAX_ADTS_FRAME_BYTES = 8191
        const val AAC_LC_ADTS_PROFILE = 1
        const val SAMPLE_RATE_INDEX = 3
        const val CHANNEL_CONFIGURATION = 2
    }
}
