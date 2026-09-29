"""Local short-caption translation using official Argos CTranslate2 model packages."""
import json
from pathlib import Path
from collections import deque, OrderedDict
import threading
import time

PAIRS = ("en_zh", "zh_en", "ja_en", "en_ja")
LANGUAGES = ("zh", "en", "ja")

def translation_route(source, target):
    if source not in LANGUAGES or target not in LANGUAGES:
        raise ValueError(f"当前离线翻译支持中日英，无法翻译 {source} → {target}，已保留原文。")
    if source == target:
        return []
    if f"{source}_{target}" in PAIRS:
        return [(source, target)]
    return [(source, "en"), ("en", target)]

class LocalTranslator:
    def __init__(self, root, target, device="cpu"):
        if target not in LANGUAGES:
            raise ValueError("目前翻译支持中文、日语与英文")
        self.root, self.target = Path(root), target
        from catalog import select_device
        self.device, self.compute_type, self.warning = select_device(device)
        self.loaded = {}
        self.cache = OrderedDict()

    def warmup(self, source="auto"):
        samples = {"en": "Hello.", "ja": "こんにちは。", "zh": "你好。"}
        for language in LANGUAGES if source == "auto" else (source,):
            if language in samples and language != self.target:
                self.translate(samples[language], language)

    def translate(self, text, source):
        if not text.strip():
            return ""
        key = (text, source)
        if key in self.cache:
            self.cache.move_to_end(key)
            return self.cache[key]
        for from_lang, to_lang in translation_route(source, self.target):
            text = self._translate_pair(text, from_lang, to_lang)
        self.cache[key] = text
        if len(self.cache) > 64:
            self.cache.popitem(last=False)
        return text

    def _translate_pair(self, text, source, target):
        pair = f"{source}_{target}"
        if pair not in self.loaded:
            import ctranslate2
            import sentencepiece
            path = self.root / pair
            if not (path / ".ready").exists():
                raise RuntimeError("请先下载中日英翻译包")
            metadata = json.loads((path / "metadata.json").read_text(encoding="utf-8"))
            if metadata.get("from_code") != source or metadata.get("to_code") != target:
                raise ValueError("翻译包语言不匹配")
            tokenizer = sentencepiece.SentencePieceProcessor(model_file=str(path / "sentencepiece.model"))
            model = ctranslate2.Translator(str(path / "model"), device=self.device, compute_type=self.compute_type, inter_threads=1, intra_threads=2)
            self.loaded[pair] = (tokenizer, model, metadata.get("target_prefix", ""))
        tokenizer, model, prefix = self.loaded[pair]
        tokens = tokenizer.encode(text, out_type=str)
        # Captions are short, but fail explicitly rather than silently truncating unusually long text.
        if len(tokens) > 512:
            raise ValueError("该条字幕过长，已保留原文")
        result = model.translate_batch([tokens], target_prefix=[[prefix]] if prefix else None, beam_size=2, replace_unknowns=True, max_input_length=512, max_decoding_length=512, length_penalty=0.2)[0]
        # Some Argos models produce literal SentencePiece space markers even after decoding.
        translated = tokenizer.decode(result.hypotheses[0]).replace("▁", " ")
        if prefix and translated.startswith(prefix):
            translated = translated[len(prefix):]
        if not translated.strip():
            raise ValueError("译文为空，已保留原文")
        return translated.strip()

class TranslationWorker:
    def __init__(self, translator, sender, capacity=8):
        self.translator, self.sender = translator, sender
        self.capacity = capacity
        self.finals = deque()
        self.partial = None
        self.latest_preview = None
        self.last_final = 0
        self.closed = False
        self.condition = threading.Condition()
        self.thread = threading.Thread(target=self._run, daemon=True)
        self.thread.start()

    def submit(self, segment_id, text, language, final=True):
        item = (segment_id, text, language, final, time.monotonic())
        overflow = False
        with self.condition:
            if self.closed:
                return
            if final:
                self.last_final = max(self.last_final, segment_id)
                if self.partial and self.partial[0] <= segment_id:
                    self.partial = None
                if len(self.finals) >= self.capacity:
                    overflow = True
                else:
                    self.finals.append(item)
            elif segment_id > self.last_final:
                self.partial = item
                self.latest_preview = (segment_id, text)
            self.condition.notify()
        if overflow:
            self.sender.send(type="translation_error", segmentId=segment_id, message="翻译队列已满，已保留原文")

    def _run(self):
        while True:
            with self.condition:
                self.condition.wait_for(lambda: self.finals or self.partial or self.closed)
                if self.finals:
                    item = self.finals.popleft()
                elif self.partial:
                    item, self.partial = self.partial, None
                else:
                    return
            segment_id, text, language, final, submitted = item
            began = time.monotonic()
            try:
                translated = self.translator.translate(text, language)
                with self.condition:
                    stale = not final and (segment_id <= self.last_final or self.latest_preview != (segment_id, text))
                if not stale:
                    self.sender.send(type="translation" if final else "translation_partial", segmentId=segment_id, text=translated, sourceText=text, targetLanguage=self.translator.target, inferenceMs=round((time.monotonic() - began) * 1000), waitMs=round((began - submitted) * 1000))
            except (EOFError, BrokenPipeError):
                return
            except Exception as exc:
                if final:
                    self.sender.send(type="translation_error", segmentId=segment_id, message=str(exc))

    def finish(self):
        with self.condition:
            self.closed = True
            self.partial = None
            self.condition.notify_all()
        self.thread.join()
