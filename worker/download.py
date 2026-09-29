import json
from pathlib import Path
import sys
import threading
import time
from catalog import MODELS

def download(name, root):
    from huggingface_hub import snapshot_download
    from tqdm.auto import tqdm
    lock = threading.Lock()

    def report(**data):
        with lock:
            print(json.dumps(data, ensure_ascii=False), flush=True)

    class Progress(tqdm):
        def __init__(self, *args, **kwargs):
            kwargs["disable"] = False
            super().__init__(*args, **kwargs)
            self.last_report = 0.0

        def display(self, *args, **kwargs):
            pass

        def update(self, n=1):
            result = super().update(n)
            now = time.monotonic()
            if now - getattr(self, "last_report", 0) >= 0.4:
                self.last_report = now
                report(type="progress", file=self.desc, current=self.n, total=self.total or 0, unit=self.unit)
            return result

    try:
        path = Path(root) / name
        report(type="status", message=f"正在从 Hugging Face 下载 {name} 模型…")
        patterns = ["model.int8.onnx", "tokens.txt", "LICENSE", "README.md"] if name == "sensevoice-small" else ["model.bin", "config.json", "tokenizer.json", "vocabulary.*"]
        snapshot_download(repo_id=MODELS[name], local_dir=path, allow_patterns=patterns, tqdm_class=Progress)
        required = ["model.int8.onnx", "tokens.txt"] if name == "sensevoice-small" else ["model.bin", "config.json", "tokenizer.json"]
        if not all((path / filename).is_file() for filename in required):
            raise RuntimeError("下载缺少必要模型文件，请重试")
        # A completion marker distinguishes a complete model from an interrupted download.
        (path / ".ready").write_text("1", encoding="utf-8")
        report(type="complete", path=str(path))
    except Exception as exc:
        report(type="error", message=str(exc))
        sys.exit(1)
