"""Common short-utterance API for Whisper and Alibaba SenseVoiceSmall."""
from types import SimpleNamespace
import re
from catalog import select_device

class WhisperRecognizer:
    def __init__(self, model):
        self.model = model
        self.last_language = None

    def transcribe(self, audio, **kwargs):
        segments, info = self.model.transcribe(audio, **kwargs)
        if kwargs.get("language") is None:
            # Very short tails may be classified as an unrelated language. Reuse the
            # preceding supported language only in that narrow case; do not freeze
            # normal Japanese/English/Chinese switches or override explicit choices.
            if info.language not in ("zh", "en", "ja") and len(audio) < 16000 * 3 and self.last_language:
                segments, info = self.model.transcribe(audio, **(kwargs | {"language": self.last_language}))
            if info.language in ("zh", "en", "ja"):
                self.last_language = info.language
        return segments, info

class SenseVoiceRecognizer:
    def __init__(self, model_path, language, threads):
        import sherpa_onnx
        self.language = language
        self.recognizer = sherpa_onnx.OfflineRecognizer.from_sense_voice(
            model=str(model_path / "model.int8.onnx"), tokens=str(model_path / "tokens.txt"),
            num_threads=threads, language=language, use_itn=True, provider="cpu")

    def transcribe(self, audio, **kwargs):
        # Share Silero's speech gate with Whisper to suppress silence/music-only chunks.
        from faster_whisper.vad import get_speech_timestamps, VadOptions
        if not get_speech_timestamps(audio, VadOptions(min_silence_duration_ms=300)):
            return [], SimpleNamespace(language=self.language)
        stream = self.recognizer.create_stream()
        stream.accept_waveform(16000, audio)
        self.recognizer.decode_stream(stream)
        result = stream.result
        text = re.sub(r"<\|[^|]*\|>", "", result.text).strip()
        language = getattr(result, "lang", "") or self.language
        language = language.replace("<|", "").replace("|>", "")
        if language in ("", "auto"):
            # Do not guess Japanese vs Chinese from shared characters.
            language = "unknown"
        segments = [SimpleNamespace(text=text, start=0.0, end=len(audio) / 16000)] if text else []
        return segments, SimpleNamespace(language=language)

def load_recognizer(path, language, requested_device, threads):
    if path.name == "sensevoice-small":
        model = SenseVoiceRecognizer(path, language, threads)
        warning = "SenseVoice 识别使用 CPU / int8；翻译可使用 GPU。" if requested_device == "cuda" else ""
        return model, "cpu", "int8 / ONNX", warning
    from faster_whisper import WhisperModel
    device, compute_type, warning = select_device(requested_device)
    return WhisperRecognizer(WhisperModel(str(path), device=device, compute_type=compute_type, cpu_threads=threads, local_files_only=True)), device, compute_type, warning
