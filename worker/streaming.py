"""Bounded, coalescing streaming scheduler. Only confirmed captions are committed."""
from collections import deque
from dataclasses import dataclass
import threading
import numpy as np

RATE = 16000
FRAME = 320  # 20 ms, independent of capture callback boundaries

@dataclass
class Job:
    segment_id: int
    start: int
    audio: np.ndarray
    final: bool

class JobBuffer:
    def __init__(self, max_finals=4):
        self.finals = deque()
        self.partial = None
        self.condition = threading.Condition()
        self.closed = False
        self.last_final = 0
        self.max_finals = max_finals

    def put(self, job):
        with self.condition:
            if self.closed:
                raise RuntimeError("识别任务已停止")
            if job.final:
                if len(self.finals) >= self.max_finals:
                    raise RuntimeError("识别积压超过 4 个片段，请改用 tiny 模型后重新开始。")
                self.last_final = job.segment_id
                self.finals.append(job)
                self.partial = None
            else:
                self.partial = job  # superseded previews need not be recognized
            self.condition.notify()

    def get(self):
        with self.condition:
            self.condition.wait_for(lambda: self.finals or self.partial is not None or self.closed)
            if self.finals:
                return self.finals.popleft()
            if self.partial is not None:
                job, self.partial = self.partial, None
                return job
            return None

    def close(self):
        with self.condition:
            self.closed = True
            self.partial = None
            self.condition.notify_all()

class Segmenter:
    def __init__(self, submit, preview_seconds=1.5):
        self.submit = submit
        self.pending = np.empty(0, np.float32)
        self.pending_start = 0
        self.next_sample = 0
        self.pre_roll = deque(maxlen=10)
        self.parts = []
        self.start = 0
        self.length = 0
        self.silent = 0
        self.voiced = 0
        self.preview_interval = max(FRAME, round(RATE * preview_seconds))
        self.preview_at = self.preview_interval
        self.segment_id = 1

    def push(self, start, pcm):
        if len(pcm) % 2 or start < 0 or start < self.next_sample:
            raise ValueError("invalid or non-monotonic PCM frame")
        audio = np.frombuffer(pcm, dtype="<i2").astype(np.float32) / 32768
        if start > self.next_sample + FRAME:
            self.flush()
        if not len(self.pending):
            self.pending_start = start
        self.pending = np.concatenate((self.pending, audio))
        self.next_sample = start + len(audio)
        offset = 0
        while len(self.pending) - offset >= FRAME:
            self._frame(self.pending_start + offset, self.pending[offset:offset + FRAME].copy())
            offset += FRAME
        self.pending = self.pending[offset:]
        self.pending_start += offset

    def _frame(self, start, audio):
        # Energy gate for incremental scheduling; Whisper's Silero VAD rejects non-speech.
        voiced = float(np.sqrt(np.mean(audio * audio))) >= 0.003
        if not self.parts:
            if not voiced:
                self.pre_roll.append((start, audio))
                return
            self.start = self.pre_roll[0][0] if self.pre_roll else start
            self.parts = [chunk for _, chunk in self.pre_roll]
            self.length = sum(map(len, self.parts))
            self.pre_roll.clear()
        self.parts.append(audio)
        self.length += len(audio)
        self.voiced += len(audio) if voiced else 0
        self.silent = 0 if voiced else self.silent + len(audio)
        if self.silent >= int(RATE * 0.6) or self.length >= RATE * 5:
            self._finish()
        elif self.length >= self.preview_at:
            self.submit(Job(self.segment_id, self.start, np.concatenate(self.parts), False))
            self.preview_at += self.preview_interval

    def _finish(self):
        if self.parts and self.voiced >= RATE * 0.15:
            self.submit(Job(self.segment_id, self.start, np.concatenate(self.parts), True))
            self.segment_id += 1
        self.parts = []
        self.length = self.silent = self.voiced = 0
        self.preview_at = self.preview_interval

    def idle(self, position):
        if position - self.next_sample >= int(RATE * 0.7):
            self.flush()

    def flush(self):
        if len(self.pending):
            self._frame(self.pending_start, self.pending)
            self.pending = np.empty(0, np.float32)
        self._finish()
        self.pre_roll.clear()
