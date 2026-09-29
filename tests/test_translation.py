import io
import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "worker"))
from translation import translation_route, LocalTranslator, TranslationWorker
from download_translation import extract_package


class TranslationTests(unittest.TestCase):
    def test_previews_coalesce_and_finals_take_priority(self):
        entered, release = threading.Event(), threading.Event()
        events, calls = [], []
        class Translator:
            target = "zh"
            def translate(self, text, source):
                calls.append(text)
                if text == "old":
                    entered.set()
                    release.wait(5)
                return text
        class Sender:
            def send(self, **message):
                events.append(message)
        worker = TranslationWorker(Translator(), Sender())
        try:
            worker.submit(1, "old", "ja", final=False)
            self.assertTrue(entered.wait(2))
            worker.submit(1, "superseded", "ja", final=False)
            worker.submit(1, "final", "ja")
        finally:
            release.set()
            worker.finish()
        self.assertEqual(calls, ["old", "final"])
        self.assertEqual([(e["type"], e["text"]) for e in events], [("translation", "final")])

    def test_preview_is_emitted_before_sentence_final(self):
        ready = threading.Event()
        events = []
        class Translator:
            target = "zh"
            def translate(self, text, source):
                return "你好"
        class Sender:
            def send(self, **message):
                events.append(message)
                ready.set()
        worker = TranslationWorker(Translator(), Sender())
        try:
            worker.submit(1, "hello", "en", final=False)
            self.assertTrue(ready.wait(2))
        finally:
            worker.finish()
        self.assertEqual(events[0]["type"], "translation_partial")
        self.assertEqual(events[0]["sourceText"], "hello")
        self.assertGreaterEqual(events[0]["waitMs"], 0)

    def test_routes_cover_six_directions(self):
        for source in ("zh", "en", "ja"):
            for target in ("zh", "en", "ja"):
                route = translation_route(source, target)
                if source == target:
                    self.assertEqual(route, [])
                else:
                    self.assertEqual(route[0][0], source)
                    self.assertEqual(route[-1][1], target)
        self.assertEqual(translation_route("ja", "zh"), [("ja", "en"), ("en", "zh")])

    def test_same_language_needs_no_model(self):
        self.assertEqual(LocalTranslator("does-not-exist", "zh").translate("你好", "zh"), "你好")

    def test_unknown_language_is_not_guessed(self):
        with self.assertRaises(ValueError):
            translation_route("unknown", "zh")

    def test_failure_does_not_stop_following_translations(self):
        events = []
        class Translator:
            target = "zh"
            def translate(self, text, source):
                if source == "unknown":
                    raise ValueError("unknown language")
                return "你好"
        class Sender:
            def send(self, **message):
                events.append(message)
        worker = TranslationWorker(Translator(), Sender())
        worker.submit(1, "bad", "unknown")
        worker.submit(2, "hello", "en")
        worker.finish()
        self.assertEqual([(e["segmentId"], e["type"]) for e in events], [(1, "translation_error"), (2, "translation")])

    def test_queue_overload_is_explicit_and_drains(self):
        entered, release = threading.Event(), threading.Event()
        events = []
        class Translator:
            target = "zh"
            def translate(self, text, source):
                entered.set()
                release.wait(5)
                return text
        class Sender:
            def send(self, **message):
                events.append(message)
        worker = TranslationWorker(Translator(), Sender(), capacity=1)
        try:
            worker.submit(1, "a", "en")
            self.assertTrue(entered.wait(2))
            worker.submit(2, "b", "en")
            worker.submit(3, "c", "en")
        finally:
            release.set()
            worker.finish()
        self.assertEqual([e["segmentId"] for e in events if e["type"] == "translation_error"], [3])
        self.assertEqual([e["segmentId"] for e in events if e["type"] == "translation"], [1, 2])

    def test_download_rejects_unsafe_paths_and_wrong_languages(self):
        for name in ("../escape", "/absolute", "C:/drive", "nested\\escape", "valid.txt"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                archive = io.BytesIO()
                with zipfile.ZipFile(archive, "w") as bundle:
                    bundle.writestr(name, "bad")
                    bundle.writestr("pkg/metadata.json", json.dumps({"from_code": "en", "to_code": "ja"}))
                archive.seek(0)
                with self.assertRaises(ValueError):
                    extract_package(archive, Path(directory) / "result", "en_zh")
                self.assertFalse((Path(directory) / "result/.ready").exists())


if __name__ == "__main__":
    unittest.main()
