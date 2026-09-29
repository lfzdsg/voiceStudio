# 实时字幕延迟优化与本机测试

旧版临时识别不翻译，连续讲话需要等到 5 秒片段结束，再识别、加载翻译模型并翻译。第三版保留最终分段规则，但将识别预览间隔从 1.5 秒改为 1 秒，并提前翻译预览。临时结果会修订，正式记录和导出仍只收最终字幕。

翻译线程只有一个待执行预览槽，新的预览覆盖旧的待执行任务；最终结果优先。正在翻译的旧预览在发送前检查是否过期，界面再根据片段号和原文匹配，避免旧译文覆盖新句。启动收音前预加载相关翻译路径；会话内最多缓存 64 个文本结果。未降低翻译 beam size。

## CPU 对比

固定上游 7.2 秒日语音频，16 kHz PCM，按每 100 ms 一帧输入；SenseVoice、固定日语、目标中文。顺序运行旧发行包和新源码，计时从引擎 ready 后开始，排除启动预加载；计时点是字幕事件到达桌面进程，不是屏幕绘制完成时间。

| 指标 | 旧版 | 优化后 |
| --- | ---: | ---: |
| 首条日语原文 | 2279 ms | 1753 ms |
| 首条中文译文 | 6745 ms（最终译文） | 1862 ms（临时译文） |

日志：`.runtime/latency-before.log`、`.runtime/latency-after.log`。这衡量的是提前可读性，不能解读为最终译文完成提速至 1.86 秒；临时译文可能不完整或有误。单个样例不是延迟分位数基准，不代表 B 站视频或游戏负载下的表现。

## GPU 验证

RTX 4060 8 GB，驱动 591.86。在项目 `.venv` 安装 cuBLAS 12.9.2.10、cuDNN 9.26.0.51、NVRTC 12.9.86。运行时仅为当前进程添加 DLL 搜索路径；发行包使用自己的 `cuda` 目录，不修改系统 PATH、驱动或其他软件环境。

`scripts/check-gpu.py` 已通过真实 Whisper base CUDA FP16 识别和日中翻译。四条日语短句在预热后的翻译耗时：CPU 为 63 / 155 / 58 / 54 ms；GPU 为 52 / 145 / 71 / 42 ms。短句 GPU 收益有限，部分甚至稍慢，不宣称 GPU 带来数量级提速。源码实时英文管道中，Whisper 与翻译同时返回 cuda，首条原文约 1425 ms，首条临时中文约 1533 ms。报告：`.runtime/gpu-check.json`。

运行库要求参考 [faster-whisper GPU 文档](https://github.com/SYSTRAN/faster-whisper#gpu)与 [CTranslate2 安装文档](https://opennmt.net/CTranslate2/installation.html)。SenseVoice 当前 ONNX 后端仍用 CPU，GPU 可用于翻译。GPU 占用和游戏帧率影响尚未实测。

最终 `VoiceStudio-v3` 发行包已分别通过 Whisper base GPU 识别＋GPU 翻译，以及 SenseVoice 日语 CPU 识别＋GPU 翻译的真实管道测试；两者均在句末之前收到临时译文，并在停止时完成所有最终译文。此验证不含 WPF 屏幕绘制和 B 站现场播放。
