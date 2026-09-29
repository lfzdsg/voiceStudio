"""Opt-in real inference smoke test; requires downloaded models and upstream test WAVs."""
from pathlib import Path
import sys
import time
import wave
import json
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "worker"))
from recognizer import load_recognizer
from translation import LocalTranslator
from catalog import select_device

report = {"device": select_device("auto"), "translation": [], "recognition": []}
samples = {"en": "Hello. Where is the train station?", "zh": "你好，请问火车站在哪里？", "ja": "こんにちは。駅はどこですか？"}
translators = {target: LocalTranslator(ROOT / "models/translation", target) for target in samples}
for source, text in samples.items():
    for target, translator in translators.items():
        if source == target:
            continue
        translated = translator.translate(text, source)
        assert translated.strip() and "▁" not in translated, (source, target, translated)
        report["translation"].append(dict(source=source, target=target, text=text, translated=translated))

model, *_ = load_recognizer(ROOT / "models/sensevoice-small", "auto", "cpu", 4)
for language, path in (("en", ROOT / ".runtime/speech.wav"), ("zh", ROOT / ".runtime/sensevoice-fixtures/test_wavs/zh.wav"), ("ja", ROOT / ".runtime/sensevoice-fixtures/test_wavs/ja.wav")):
    with wave.open(str(path)) as audio:
        assert audio.getframerate() == 16000 and audio.getnchannels() == 1 and audio.getsampwidth() == 2
        samples = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2").astype(np.float32) / 32768
    began = time.monotonic()
    segments, info = model.transcribe(samples)
    text = "".join(s.text for s in segments)
    inference = round((time.monotonic() - began) * 1000)
    assert info.language == language and text.strip(), (language, info.language, text)
    translated = translators["zh"].translate(text, info.language)
    report["recognition"].append(dict(language=info.language, seconds=len(samples) / 16000, inferenceMs=inference, text=text, translated=translated))
output = json.dumps(report, ensure_ascii=False, indent=2)
(ROOT / ".runtime/model-check.json").write_text(output, encoding="utf-8")
print(output)
