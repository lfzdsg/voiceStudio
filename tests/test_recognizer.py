from pathlib import Path
import sys
from types import SimpleNamespace
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "worker"))
from recognizer import WhisperRecognizer


class FakeWhisper:
    def __init__(self):
        self.detected = "ja"
        self.calls = []

    def transcribe(self, audio, **kwargs):
        self.calls.append(kwargs.get("language"))
        return [], SimpleNamespace(language=kwargs.get("language") or self.detected)


class RecognitionTests(unittest.TestCase):
    def test_short_unknown_tail_keeps_previous_language(self):
        model = FakeWhisper()
        recognizer = WhisperRecognizer(model)
        recognizer.transcribe([0] * 64000, language=None)
        model.detected = "mi"
        _, info = recognizer.transcribe([0] * 16000, language=None)
        self.assertEqual(info.language, "ja")
        self.assertEqual(model.calls, [None, None, "ja"])

    def test_supported_switch_and_explicit_language_are_preserved(self):
        model = FakeWhisper()
        recognizer = WhisperRecognizer(model)
        recognizer.transcribe([0] * 64000, language=None)
        model.detected = "en"
        self.assertEqual(recognizer.transcribe([0] * 16000, language=None)[1].language, "en")
        self.assertEqual(recognizer.transcribe([0] * 16000, language="zh")[1].language, "zh")

    def test_long_unknown_and_first_unknown_are_not_guessed(self):
        model = FakeWhisper()
        model.detected = "ko"
        recognizer = WhisperRecognizer(model)
        self.assertEqual(recognizer.transcribe([0] * 16000, language=None)[1].language, "ko")
        recognizer.last_language = "ja"
        self.assertEqual(recognizer.transcribe([0] * 64000, language=None)[1].language, "ko")


if __name__ == "__main__":
    unittest.main()
