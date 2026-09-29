import io
import json
from pathlib import Path
import struct
import sys
import unittest
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "worker"))
from protocol import read_frame, Sender
from streaming import Job, JobBuffer, Segmenter, RATE

def pcm(seconds, voiced=True):
    count = int(seconds * RATE)
    wave = np.sin(np.arange(count) * 2 * np.pi * 220 / RATE) * 12000 if voiced else np.zeros(count)
    return wave.astype("<i2").tobytes()

class StreamingTests(unittest.TestCase):
    def test_faster_preview_does_not_change_final_audio(self):
        jobs = []
        s = Segmenter(jobs.append, preview_seconds=1.0)
        s.push(0, pcm(1.1))
        self.assertEqual(len(jobs), 1)
        self.assertFalse(jobs[0].final)
        self.assertEqual(len(jobs[0].audio), RATE)
        s.flush()
        self.assertEqual(len(jobs[-1].audio), round(1.1 * RATE))
        self.assertTrue(jobs[-1].final)

    def test_silence_never_generates_caption(self):
        jobs = []
        segmenter = Segmenter(jobs.append)
        segmenter.push(0, pcm(10, False))
        segmenter.flush()
        self.assertEqual(jobs, [])

    def test_partial_and_final_use_same_segment(self):
        jobs = []
        s = Segmenter(jobs.append)
        s.push(0, pcm(2))
        s.push(32000, pcm(0.7, False))
        self.assertTrue(any(not j.final for j in jobs))
        self.assertEqual(len([j for j in jobs if j.final]), 1)
        self.assertEqual({j.segment_id for j in jobs}, {1})

    def test_stop_flushes_last_short_phrase_once(self):
        jobs = []
        s = Segmenter(jobs.append)
        s.push(0, pcm(0.333))
        s.flush()
        s.flush()
        self.assertEqual(len(jobs), 1)
        self.assertEqual(len(jobs[0].audio), int(0.333 * RATE))

    def test_gaps_keep_real_session_time(self):
        jobs = []
        s = Segmenter(jobs.append)
        s.push(0, pcm(0.5))
        s.idle(RATE * 3)
        s.push(RATE * 5, pcm(0.5))
        s.flush()
        self.assertEqual([j.start for j in jobs], [0, RATE * 5])
        self.assertEqual([j.segment_id for j in jobs], [1, 2])

    def test_packet_boundaries_do_not_change_segmentation(self):
        signal = pcm(2) + pcm(0.8, False)
        a, b = [], []
        whole, chunked = Segmenter(a.append), Segmenter(b.append)
        whole.push(0, signal)
        for offset in range(0, len(signal), 734):
            chunked.push(offset // 2, signal[offset:offset + 734])
        whole.flush(); chunked.flush()
        self.assertEqual([(j.start, len(j.audio), j.final) for j in a], [(j.start, len(j.audio), j.final) for j in b])

    def test_continuous_speech_is_bounded_and_nonduplicated(self):
        jobs = []
        s = Segmenter(jobs.append)
        s.push(0, pcm(12))
        s.flush()
        finals = [j for j in jobs if j.final]
        self.assertEqual([j.start for j in finals], [0, 5 * RATE, 10 * RATE])
        self.assertEqual(sum(len(j.audio) for j in finals), 12 * RATE)

    def test_queue_keeps_finals_and_coalesces_previews(self):
        q = JobBuffer(1)
        q.put(Job(1, 0, np.zeros(10), False))
        q.put(Job(1, 0, np.zeros(20), False))
        self.assertEqual(len(q.get().audio), 20)
        q.put(Job(1, 0, np.zeros(30), True))
        with self.assertRaises(RuntimeError):
            q.put(Job(2, 30, np.zeros(20), True))
        q.close()
        self.assertTrue(q.get().final)
        self.assertIsNone(q.get())

    def test_protocol_validates_size_and_preserves_unicode(self):
        stream = io.BytesIO()
        Sender(stream, "session").send(type="final", text="你好")
        stream.seek(0)
        kind, data = read_frame(stream)
        self.assertEqual(kind, 0)
        self.assertEqual(json.loads(data)["text"], "你好")
        with self.assertRaises(ValueError):
            read_frame(io.BytesIO(struct.pack("<I", 2_000_000)))
        with self.assertRaises(EOFError):
            read_frame(io.BytesIO(struct.pack("<I", 20) + b"x"))

    def test_out_of_order_audio_is_rejected(self):
        s = Segmenter(lambda _: None)
        s.push(0, pcm(0.1))
        with self.assertRaises(ValueError):
            s.push(0, pcm(0.1))

if __name__ == "__main__":
    unittest.main()
