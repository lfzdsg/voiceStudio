# 中日英识别与翻译选型

本项目主要识别日语、英语、中文并输出中文字幕，兼容另外两个目标语言。ASR 负责听写，翻译负责语义转换，是两项独立能力；Whisper 的 translate 任务不能直接当作翻译成中文的方案。

| 候选 | 官方信息与项目判断 | 当前状态 |
| --- | --- | --- |
| 阿里 SenseVoiceSmall | 支持中、英、日、韩、粤语；已有 sherpa-onnx Windows CPU 部署路线，适合本项目先行集成 | 已接入 int8 ONNX；已用中日英样例验证自动语言识别 |
| 阿里 Qwen3-ASR 0.6B / 1.7B | 官方支持 30 种语言，包括中日英；值得用游戏对白开展精度对比。其官方基准不等于本机实测，流式部署还需评估 vLLM 等依赖 | 未接入，不在已实现模型列表中 |
| 字节 Seed-ASR | 有公开技术论文，但本次未确认可直接下载部署的官方模型权重；无法仅凭论文完成本地集成 | 未接入 |
| Whisper base / small / medium / large-v3 / turbo | 保留成熟的 faster-whisper 后端；用户可自行选择大小并下载。较大模型增加算力与内存开销 | 已提供模型选项；base 已实际验证，其他尺寸未做本机精度对比 |

来源：[SenseVoice 官方仓库](https://github.com/QwenAudio/SenseVoice)、[Qwen3-ASR 官方仓库](https://github.com/QwenLM/Qwen3-ASR)、[Seed-ASR 论文](https://arxiv.org/abs/2407.04675)、[faster-whisper](https://github.com/SYSTRAN/faster-whisper)、[SenseVoice ONNX 转换模型](https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17)。

## 已落地翻译与限制

第四版已默认接入 **Qwen3-4B-Instruct-2507 Q4_K_M 本地直译**，支持中日英六方向，替代日中英语中转作为首选。模型为 Qwen 文字指令模型，GGUF 来自 Unsloth；没有集成 Qwen3-ASR。实测对比、显存和剩余问题见 [Qwen 翻译](QWEN_TRANSLATION.md)。以下 Argos 信息仍适用于可选的旧翻译器。

使用 [Argos 官方模型索引](https://github.com/argosopentech/argospm-index/blob/main/index.json)中的 en_zh / zh_en 1.9、ja_en / en_ja 1.1 模型。四个包合计约 0.4 GB 下载量；首次下载联网，推理在本机 CPU 执行。中日之间采用英语中转。对短句可用，但中转会叠加误差，缺少上下文会影响游戏专名、省略主语和人物语气。

实际短句测试：英文 “Hello. Where is the train station?” → “你好,火车站在哪?”；日文 “こんにちは。駅はどこですか？” → “喂,车站在哪?”。第二个结果说明意思基本可辨，但语气不理想。上游日语音频翻译也有内容省略。因此这版属于离线翻译基线，不能宣称达到 YouTube 或高质量大模型翻译水平。

后续应使用有参考字幕的游戏、视频片段，对识别错误率、译文完整度、专名、延迟、显存和游戏帧率分别测量，再决定是否切换 Qwen3-ASR 和支持日中直译的翻译后端。本版没有静默上传字幕，也没有接入收费 API。

## 本机验证方法

`scripts/check-models.py` 会执行六方向文本翻译，以及 SenseVoice 中日英自动识别和转中文。需要本地已有模型、`.runtime/speech.wav` 英文固定测试语音与上游模型仓库的 `test_wavs/zh.wav`、`test_wavs/ja.wav`，后两者放在 `.runtime/sensevoice-fixtures/test_wavs/`。测试报告写入 `.runtime/model-check.json`，不使用用户麦克风录音。

单次 CPU 冒烟测试中，8.52 秒英文、5.592 秒中文、7.2 秒日文的 ASR 推理分别约 484、156、188 ms（不含模型加载、分段等待和翻译）；它们不是稳定性能基准，也不能代表游戏负载下的端到端延迟。实际短片段中文有近音词错误，日语也存在措辞错误。需要看原文时可用双语模式。

第三版已安装项目专用 CUDA/cuDNN 运行库，并在 RTX 4060 验证 Whisper base 识别和 Argos 翻译 GPU 推理。SenseVoice 识别继续使用 CPU；速度变化见 `LATENCY.md`。普通桌面自动界面测试此前由用户 Esc 停止，悬浮窗、游戏覆盖和多显示器仍需按手动验收清单验证。
