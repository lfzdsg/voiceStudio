"""Download fixed upstream packages and extract only required model assets safely."""
import json
from pathlib import Path, PurePosixPath
import shutil
import tempfile
import time
import zipfile
import httpx

PACKAGES = {
    "en_zh": "https://argos-net.com/v1/translate-en_zh-1_9.argosmodel",
    "zh_en": "https://argos-net.com/v1/translate-zh_en-1_9.argosmodel",
    "ja_en": "https://argos-net.com/v1/translate-ja_en-1_1.argosmodel",
    "en_ja": "https://argos-net.com/v1/translate-en_ja-1_1.argosmodel",
}

def extract_package(archive, destination, pair):
    destination = Path(destination)
    with zipfile.ZipFile(archive) as bundle:
        members = bundle.infolist()
        for member in members:
            path = PurePosixPath(member.filename)
            if path.is_absolute() or ".." in path.parts or "\\" in member.filename or ":" in member.filename:
                raise ValueError("Invalid translation archive path")
        roots = [m for m in members if PurePosixPath(m.filename).name == "metadata.json"]
        if len(roots) != 1 or sum(m.file_size for m in members) > 2_000_000_000:
            raise ValueError("Invalid translation archive")
        parent = PurePosixPath(roots[0].filename).parent
        metadata = json.loads(bundle.read(roots[0]))
        if f"{metadata.get('from_code')}_{metadata.get('to_code')}" != pair:
            raise ValueError("Unexpected translation languages")
        for member in members:
            path = PurePosixPath(member.filename)
            if not path.is_relative_to(parent) or member.is_dir():
                continue
            relative = path.relative_to(parent)
            if not relative.parts or (relative.parts[0] != "model" and relative.name not in ("metadata.json", "sentencepiece.model", "LICENSE", "LICENSE.txt", "README.md")):
                continue
            output = destination.joinpath(*relative.parts)
            output.parent.mkdir(parents=True, exist_ok=True)
            with bundle.open(member) as source, output.open("wb") as target:
                shutil.copyfileobj(source, target)
    if not (destination / "model" / "model.bin").is_file() or not (destination / "sentencepiece.model").is_file():
        raise ValueError("Translation model or tokenizer is missing")
    (destination / ".ready").write_text("1", encoding="utf-8")

def download_translation(root):
    root = Path(root) / "translation"
    root.mkdir(parents=True, exist_ok=True)
    def report(**data):
        print(json.dumps(data, ensure_ascii=False), flush=True)
    for pair, url in PACKAGES.items():
        target = root / pair
        if (target / ".ready").is_file():
            continue
        report(type="status", message=f"下载离线翻译包 {pair}…")
        with tempfile.TemporaryDirectory(prefix="install-", dir=root) as temporary:
            archive = Path(temporary) / "package.zip"
            with httpx.stream("GET", url, follow_redirects=True, timeout=60) as response:
                response.raise_for_status()
                total = int(response.headers.get("content-length", 0))
                current, last = 0, 0
                with archive.open("wb") as output:
                    for data in response.iter_bytes(1024 * 256):
                        current += len(data)
                        if current > 1_000_000_000:
                            raise ValueError("Translation package exceeds size limit")
                        output.write(data)
                        if time.monotonic() - last > 0.5:
                            report(type="progress", file=pair, current=current, total=total, unit="B")
                            last = time.monotonic()
            staged = Path(temporary) / "model"
            extract_package(archive, staged, pair)
            # An incomplete target is never used by the app; retain it for diagnosis.
            if target.exists():
                raise RuntimeError(f"目录 {target} 不完整，请将其移走后重试")
            staged.rename(target)
    report(type="complete", path=str(root))
