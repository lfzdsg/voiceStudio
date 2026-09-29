"""Verify Windows closes the owned Qwen server even when its worker is killed."""
from pathlib import Path
import ctypes
from ctypes import wintypes
import json
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
if len(sys.argv) > 1:
    sys.path.insert(0, str(ROOT / "worker"))
    from qwen_translation import QwenTranslator
    translator = QwenTranslator(ROOT, "zh", "cuda")
    translator.warmup("ja")
    Path(sys.argv[1]).write_text(json.dumps({"pid": translator.process.pid}), encoding="utf-8")
    time.sleep(120)
    translator.close()
else:
    marker = ROOT / ".runtime" / ("qwen-lifecycle-" + uuid.uuid4().hex + ".json")
    child = subprocess.Popen([sys.executable, str(Path(__file__).resolve()), str(marker)], creationflags=subprocess.CREATE_NO_WINDOW)
    try:
        deadline = time.monotonic() + 40
        while not marker.exists() and time.monotonic() < deadline and child.poll() is None:
            time.sleep(0.1)
        if not marker.exists():
            raise RuntimeError("Qwen child did not become ready")
        pid = json.loads(marker.read_text(encoding="utf-8"))["pid"]
        api = ctypes.WinDLL("kernel32", use_last_error=True)
        api.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        api.OpenProcess.restype = wintypes.HANDLE
        api.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        api.CloseHandle.argtypes = [wintypes.HANDLE]
        handle = api.OpenProcess(0x100000, False, pid)
        if not handle:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            child.kill()
            child.wait(timeout=5)
            assert api.WaitForSingleObject(handle, 5000) == 0, "Qwen server outlived its worker"
            print("PASS Qwen server terminated automatically after worker kill")
        finally:
            api.CloseHandle(handle)
    finally:
        if child.poll() is None:
            child.kill()
        child.wait(timeout=5)
        marker.unlink(missing_ok=True)
