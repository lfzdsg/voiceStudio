import os
from pathlib import Path
import sys

_dll_handles = []
_runtime_paths_added = False

def configure_gpu_runtime():
    """Add project-installed NVIDIA DLLs without changing the system PATH."""
    global _runtime_paths_added
    if os.name != "nt" or _runtime_paths_added:
        return
    roots = [Path(sys.prefix) / "Lib/site-packages/nvidia"]
    if getattr(sys, "frozen", False):
        roots = [Path(sys.executable).resolve().parents[2] / "cuda"]
    directories = sorted({str(p.parent.resolve()) for root in roots if root.is_dir() for p in root.rglob("*.dll")})
    for directory in directories:
        _dll_handles.append(os.add_dll_directory(directory))
    if directories:
        os.environ["PATH"] = os.pathsep.join(directories + [os.environ.get("PATH", "")])
    _runtime_paths_added = True

MODELS = {
    "sensevoice-small": "csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17",
    "tiny": "Systran/faster-whisper-tiny",
    "base": "Systran/faster-whisper-base",
    "small": "Systran/faster-whisper-small",
    "medium": "Systran/faster-whisper-medium",
    "large-v3": "Systran/faster-whisper-large-v3",
    "large-v3-turbo": "dropbox-dash/faster-whisper-large-v3-turbo",
}

def select_device(requested):
    """Do not attempt CUDA inference without its runtime DLLs (native failures may abort)."""
    if requested == "cpu":
        return "cpu", "int8", ""
    if requested not in ("auto", "cuda"):
        raise ValueError("unsupported compute device")
    try:
        configure_gpu_runtime()
        import ctranslate2
        import ctypes
        import os
        if ctranslate2.get_cuda_device_count() < 1:
            raise RuntimeError("未检测到可用的 NVIDIA GPU")
        if os.name == "nt":
            for dll in ("cublasLt64_12.dll", "cublas64_12.dll", "cudnn64_9.dll"):
                _dll_handles.append(ctypes.WinDLL(dll))
        return "cuda", "float16", ""
    except Exception as exc:
        reason = "GPU 不可用或缺少 CUDA 12 / cuDNN 9 运行库。"
        if requested == "cuda":
            raise RuntimeError(reason + "请选择自动或 CPU，或安装对应运行库。") from exc
        return "cpu", "int8", reason + "已使用 CPU。"
