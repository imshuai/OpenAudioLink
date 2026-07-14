from __future__ import annotations

import unittest

from generate_aac_fixture import select_continuous_frames, split_adts_frames


class SplitAdtsFramesTests(unittest.TestCase):
    def test_rejects_crc_frame_with_seven_byte_length(self) -> None:
        frame = bytes([0xFF, 0xF0, 0x4C, 0x80, 0x00, 0xE0, 0x00])

        with self.assertRaisesRegex(ValueError, "CRC"):
            split_adts_frames(frame)

    def test_select_continuous_frames_returns_source_indices_2_through_7(self) -> None:
        frames = [bytes([index]) for index in range(8)]

        self.assertEqual(
            [bytes([index]) for index in range(2, 8)],
            select_continuous_frames(frames),
        )

    def test_select_continuous_frames_rejects_fewer_than_eight_source_frames(self) -> None:
        with self.assertRaisesRegex(ValueError, "2..7"):
            select_continuous_frames([b"x"] * 7)


if __name__ == "__main__":
    unittest.main()
