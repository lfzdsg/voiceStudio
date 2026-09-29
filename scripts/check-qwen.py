"""Compare real local Qwen/Argos translations using fixed synthetic subtitle text."""
from pathlib import Path
import sys
import time
import json
import subprocess

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "worker"))
from qwen_translation import QwenTranslator
from translation import LocalTranslator

samples = [
    "こんにちは。駅はどこですか？",
    "うちの中学は弁当制で、持っていけない場合は50円の学校販売のパンを買う。",
    "この扉を開けるには、先に鍵を見つけなければならない。",
    "回復アイテムはまだ使わないで。次のボス戦まで取っておこう。",
    "行きたくないわけじゃないけど、今日はちょっと無理かな。",
    "敵が来たら、私が合図するまで動かないで。",
]
report = []
qwen = QwenTranslator(ROOT, "zh", "cuda")
argos = LocalTranslator(ROOT / "models/translation", "zh", "cpu")
try:
    qwen.warmup("ja")
    print("Qwen loaded", flush=True)
    server_pid = qwen.process.pid
    memory = subprocess.run(["nvidia-smi", "--query-gpu=memory.used,memory.total", "--format=csv,noheader,nounits"], capture_output=True, text=True, creationflags=subprocess.CREATE_NO_WINDOW).stdout.strip()
    print("GPU total used/total MiB:", memory, flush=True)
    for text in samples:
        row = {"source": text}
        for name, translator in (("argos", argos), ("qwen", qwen)):
            began = time.monotonic()
            result = translator.translate(text, "ja")
            row[name] = {"text": result, "ms": round((time.monotonic() - began) * 1000)}
        report.append(row)
        print(json.dumps(row, ensure_ascii=False), flush=True)
    for source, target, text in (("en", "zh", "Don't use the healing item yet. Save it for the next boss."), ("zh", "en", "别急，等我发出信号再行动。"), ("zh", "ja", "如果没有钥匙，就打不开这扇门。"), ("ja", "en", "敵が来たら、私が合図するまで動かないで。"), ("en", "ja", "Please wait for my signal.")):
        qwen.target = target
        qwen.cache.clear()
        translated = qwen.translate(text, source)
        print(source, target, translated, flush=True)
        report.append({"sourceLanguage": source, "targetLanguage": target, "source": text, "qwen": translated})
    (ROOT / ".runtime/qwen-check.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
finally:
    qwen.close()
assert qwen.process is None
print("PASS managed Qwen server closed", flush=True)
