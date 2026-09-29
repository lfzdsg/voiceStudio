import argparse
import json
import os
from pathlib import Path
import struct
import sys
import threading
import time

from protocol import read_frame, Sender
from streaming import JobBuffer, Segmenter, RATE
from catalog import MODELS, select_device

def run(stream, config):
    sender = Sender(stream, config["sessionId"])
    local_translator = None
    try:
        from recognizer import load_recognizer
        model_path = Path(config["modelPath"])
        if not (model_path / ".ready").is_file():
            raise RuntimeError("模型未安装，请先点击「下载模型」。")
        sender.send(type="status", state="loading", message="正在加载本机识别模型…")
        model, device, compute_type, device_warning = load_recognizer(model_path, config["language"], config.get("device", "cpu"), max(1, min(4, (os.cpu_count() or 2) // 2)))
        translator = None
        if config.get("translationTarget", "off") != "off":
            from translation import LocalTranslator, TranslationWorker
            sender.send(type="status", state="loading", message="正在预加载翻译模型，减少首句等待…")
            if config.get("translationBackend", "argos") == "qwen":
                from qwen_translation import QwenTranslator
                local_translator = QwenTranslator(model_path.parent.parent, config["translationTarget"], config.get("device", "auto"))
            else:
                local_translator = LocalTranslator(model_path.parent / "translation", config["translationTarget"], config.get("device", "cpu"))
            local_translator.warmup(config["language"])
            translator = TranslationWorker(local_translator, sender)
        jobs = JobBuffer()
        segmenter = Segmenter(jobs.put, preview_seconds=1.0)
        failure = []

        def recognize():
            try:
                while (job := jobs.get()) is not None:
                    began = time.monotonic()
                    segments, info = model.transcribe(job.audio, language=None if config["language"] == "auto" else config["language"], beam_size=1, condition_on_previous_text=False, vad_filter=True, vad_parameters={"min_silence_duration_ms": 300}, temperature=0, word_timestamps=False)
                    segments = list(segments)
                    text = "".join(s.text for s in segments).strip()
                    if not job.final and job.segment_id <= jobs.last_final:
                        continue
                    start_ms = round(job.start / RATE * 1000)
                    end_ms = round((job.start + len(job.audio)) / RATE * 1000)
                    if segments:
                        start_ms += round(segments[0].start * 1000)
                        end_ms = min(end_ms, round((job.start / RATE + segments[-1].end) * 1000))
                    sender.send(type="final" if job.final else "partial", segmentId=job.segment_id, startMs=start_ms, endMs=max(start_ms + 1, end_ms), text=text, language=info.language, inferenceMs=round((time.monotonic() - began) * 1000), queued=len(jobs.finals))
                    if text and translator is not None:
                        translator.submit(job.segment_id, text, info.language, final=job.final)
            except Exception as exc:
                failure.append(exc)
                sender.send(type="error", code="INFERENCE_FAILED", message=str(exc))

        worker = threading.Thread(target=recognize, daemon=True)
        worker.start()
        sender.send(type="ready", device=device, computeType=compute_type, warning=device_warning, translationDevice=local_translator.device if translator else "off")
        while not failure:
            kind, data = read_frame(stream)
            if kind == 1:
                if len(data) < 10:
                    raise ValueError("audio frame is empty")
                position, = struct.unpack("<q", data[:8])
                segmenter.push(position, data[8:])
            elif kind == 0:
                message = json.loads(data)
                if message["type"] == "stop":
                    segmenter.flush()
                    jobs.close()
                    worker.join()
                    if translator is not None:
                        translator.finish()
                    if not failure:
                        sender.send(type="done")
                    return
                if message["type"] == "tick":
                    segmenter.idle(message["position"])
            else:
                raise ValueError("unsupported frame kind")
    except (EOFError, BrokenPipeError):
        pass
    except Exception as exc:
        sender.send(type="error", code="WORKER_FAILED", message=str(exc))
    finally:
        if local_translator is not None and hasattr(local_translator, "close"):
            local_translator.close()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pipe")
    parser.add_argument("--download-model", choices=list(MODELS))
    parser.add_argument("--download-translation", action="store_true")
    parser.add_argument("--download-qwen", action="store_true")
    parser.add_argument("--model-root")
    args = parser.parse_args()
    if args.download_qwen:
        from download_qwen import download_qwen
        try:
            download_qwen(args.model_root)
        except Exception as exc:
            print(json.dumps({"type": "error", "message": str(exc)}, ensure_ascii=False), flush=True)
            sys.exit(1)
        return
    if args.download_translation:
        from download_translation import download_translation
        try:
            download_translation(args.model_root)
        except Exception as exc:
            print(json.dumps({"type": "error", "message": str(exc)}, ensure_ascii=False), flush=True)
            sys.exit(1)
        return
    if args.download_model:
        from download import download
        download(args.download_model, args.model_root)
        return
    if not args.pipe:
        parser.error("--pipe is required for recognition")
    with open("\\\\.\\pipe\\" + args.pipe, "r+b", buffering=0) as stream:
        kind, data = read_frame(stream)
        config = json.loads(data)
        if kind != 0 or config.get("type") != "start" or config.get("protocol") != 1:
            raise ValueError("unsupported session protocol")
        run(stream, config)

if __name__ == "__main__":
    import multiprocessing
    multiprocessing.freeze_support()
    main()
