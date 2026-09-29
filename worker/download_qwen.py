"""Pinned Qwen GGUF and official llama.cpp Windows CUDA runtime installer."""
from pathlib import Path, PurePosixPath
import hashlib
import json
import shutil
import tempfile
import time
import zipfile
import httpx

MODEL_NAME = "Qwen3-4B-Instruct-2507-Q4_K_M.gguf"
MODEL_REPO = "unsloth/Qwen3-4B-Instruct-2507-GGUF"
MODEL_REVISION = "a06e946bb6b655725eafa393f4a9745d460374c9"
RELEASE = "b11255"
ASSETS = {
    "llama-b11255-bin-win-cuda-12.4-x64.zip": "62b356610e08112efbf13b6b55c1dc9ba66c5fba61bd99464be154f4ccf3fa65",
    "cudart-llama-bin-win-cuda-12.4-x64.zip": "8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6",
}

def report(**data):
    print(json.dumps(data, ensure_ascii=False), flush=True)

def download_qwen(root):
    from huggingface_hub import hf_hub_download
    root = Path(root)
    runtime = root.parent / "engine/llama"
    if not (runtime / ".ready").is_file():
        runtime.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="llama-", dir=runtime.parent) as temp:
            staging = Path(temp) / "files"
            staging.mkdir()
            for name, digest in ASSETS.items():
                report(type="status", message=f"下载 Qwen 推理引擎 {name}…")
                archive = Path(temp) / name
                checksum = hashlib.sha256()
                with httpx.stream("GET", f"https://github.com/ggml-org/llama.cpp/releases/download/{RELEASE}/{name}", follow_redirects=True, timeout=120) as response:
                    response.raise_for_status()
                    total = int(response.headers.get("content-length", 0))
                    size, last = 0, 0
                    with archive.open("wb") as output:
                        for chunk in response.iter_bytes(1024 * 1024):
                            size += len(chunk)
                            if size > 2_000_000_000:
                                raise ValueError("Qwen runtime archive too large")
                            checksum.update(chunk)
                            output.write(chunk)
                            if time.monotonic() - last > 0.5:
                                report(type="progress", unit="B", current=size, total=total)
                                last = time.monotonic()
                if checksum.hexdigest() != digest:
                    raise ValueError("Qwen 引擎校验失败，请重新下载")
                with zipfile.ZipFile(archive) as bundle:
                    if sum(m.file_size for m in bundle.infolist()) > 4_000_000_000:
                        raise ValueError("Qwen runtime archive expands too large")
                    for member in bundle.infolist():
                        path = PurePosixPath(member.filename)
                        if path.is_absolute() or ".." in path.parts or "\\" in member.filename or ":" in member.filename:
                            raise ValueError("Invalid runtime archive path")
                        if member.is_dir():
                            continue
                        # Distribute only the server, DLL dependencies and licenses.
                        if path.suffix.lower() != ".dll" and path.name != "llama-server.exe" and not any(w in path.name.upper() for w in ("LICENSE", "COPYING", "NOTICE")):
                            continue
                        with bundle.open(member) as source, (staging / path.name).open("wb") as output:
                            shutil.copyfileobj(source, output)
            if not (staging / "llama-server.exe").is_file():
                raise ValueError("Qwen runtime is missing llama-server.exe")
            runtime.mkdir(parents=True, exist_ok=True)
            for file in staging.iterdir():
                shutil.copy2(file, runtime / file.name)
            (runtime / ".ready").write_text(RELEASE, encoding="utf-8")
    model_dir = root / "qwen3-4b-instruct"
    if not (model_dir / ".ready").is_file():
        report(type="status", message="下载 Qwen3-4B Q4_K_M 模型（约 2.5 GB，可续传）…")
        model = Path(hf_hub_download(MODEL_REPO, MODEL_NAME, revision=MODEL_REVISION, local_dir=model_dir))
        with model.open("rb") as stream:
            if stream.read(4) != b"GGUF":
                raise ValueError("Invalid GGUF model")
        (model_dir / ".ready").write_text(MODEL_REVISION, encoding="utf-8")
    report(type="complete", path=str(model_dir))

if __name__ == "__main__":
    import sys
    download_qwen(sys.argv[1])
