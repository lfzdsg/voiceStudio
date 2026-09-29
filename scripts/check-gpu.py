"""Real CUDA smoke test and short-caption CPU/GPU timing, without user audio."""
from pathlib import Path
import sys
import time
import json
import wave
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "worker"))
from catalog import select_device
from translation import LocalTranslator
from recognizer import load_recognizer

print("CUDA", select_device("cuda"), flush=True)
texts = ["こんにちは。駅はどこですか？", "うちの中学は弁当制で持っていけない場合は50円の学校販売のパンを買う。", "次の駅で降りてください。", "このゲームはとても面白いです。"]
report = {}
for device in ("cpu", "cuda"):
    translator = LocalTranslator(ROOT / "models/translation", "zh", device)
    translator.warmup("ja")
    rows = []
    for text in texts:
        began = time.perf_counter()
        translated = translator.translate(text, "ja")
        elapsed = round((time.perf_counter() - began) * 1000)
        assert translated and "▁" not in translated
        rows.append(dict(text=text, translated=translated, milliseconds=elapsed))
    report[device] = rows
    print(device, rows, flush=True)
    del translator

model, device, compute, _ = load_recognizer(ROOT / "models/base", "en", "cuda", 2)
with wave.open(str(ROOT / ".runtime/speech.wav")) as source:
    audio = np.frombuffer(source.readframes(source.getnframes()), dtype="<i2").astype(np.float32) / 32768
segments, info = model.transcribe(audio, language="en", beam_size=1)
text = " ".join(s.text for s in segments)
assert "caption" in text.lower(), text
report["whisper"] = dict(device=device, compute=compute, text=text)
(ROOT / ".runtime/gpu-check.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print("Whisper CUDA PASS", text, flush=True)
