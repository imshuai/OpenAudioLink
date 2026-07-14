from __future__ import annotations

import unittest

from generate_aac_fixture import split_adts_frames


class SplitAdtsFramesTests(unittest.TestCase):
    def test_rejects_crc_frame_with_seven_byte_length(self) -> None:
        frame = bytes([0xFF, 0xF0, 0x4C, 0x80, 0x00, 0xE0, 0x00])

        with self.assertRaisesRegex(ValueError, "CRC"):
            split_adts_frames(frame)


if __name__ == "__main__":
    unittest.main()
