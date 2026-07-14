package com.openaudiolink

import java.io.File
import java.io.FileNotFoundException

internal object TestFixtures {
    fun read(relativePath: String): ByteArray {
        var directory: File? = File(System.getProperty("user.dir") ?: ".").absoluteFile
        while (directory != null) {
            val candidate = File(directory, relativePath)
            if (candidate.isFile) return candidate.readBytes()
            directory = directory.parentFile
        }
        throw FileNotFoundException("Fixture not found: $relativePath")
    }
}
