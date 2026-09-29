# 本地 Qwen 字幕翻译

第四版默认使用 Qwen3-4B-Instruct-2507 Q4_K_M，通过 llama.cpp CUDA 在本机做中日英直译，不再经过英语中转。识别引擎仍独立选择 SenseVoice 或 Whisper；这是替换翻译器，没有换成 Qwen3-ASR。错误的原文、被截断的句子仍可能导致误译。

模型使用 Qwen 的非思考指令版本，避免生成长思考过程拖慢字幕。原始模型由 Qwen 发布，约 2.50 GB 的 GGUF 文件来自 Unsloth 转换仓库；**并非 Qwen 官方发布的 GGUF 量化文件**。

- [Qwen 原始模型](https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507)
- [Unsloth 量化模型](https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF)，固定 revision `a06e946bb6b655725eafa393f4a9745d460374c9`
- [llama.cpp](https://github.com/ggml-org/llama.cpp)，固定 Windows CUDA 12.4 构建 `b11255`，两个下载包按 GitHub 提供的 SHA-256 校验

## 使用

关闭旧版，运行 `dist/VoiceStudio-v4/VoiceStudio.exe`，保留整个目录（约 7.1 GB）。选择系统声音、SenseVoice、固定日本語；翻译器选择「Qwen3 4B · 本机直译」，目标中文，计算设备选自动或 CUDA。也可使用 Whisper base 加 GPU，与 SenseVoice 对比原文识别。主窗口保留 Argos 选项用于对比；会话进行中不能更改翻译器。

Qwen 的自动模式优先要求已可用的 NVIDIA CUDA 环境；不可用时明确提示，需手动切到 CPU 才会运行 CPU 4B 推理，避免静默变慢。CPU 模式可选，但此次速度验证只针对 RTX 4060 GPU。模型与引擎已包含在当前发行包；缺少时界面提供下载按钮。

本版上下文为 2048 token，单句输出上限 256 token，不生成思考过程。限制输入长度，并拒绝空输出、思考标记和被截断输出；失败保留原文。逐句翻译不加入跨句历史，避免临时识别的错误持续影响后续字幕。临时结果仅预览，最终结果才进入记录与导出。

## 本机样例

六条固定日语文本，Qwen GPU 模型预加载后，单次翻译请求约 94–312 ms。该数值不含语音识别、分段等待、模型启动和界面绘制，不是性能分位数；旧 Argos 首条包含冷加载，不能与 Qwen 首条直接比速度。完整输出在 `.runtime/qwen-check.json`，脚本为 `scripts/check-qwen.py`。

| 日语关键意思 | Argos 输出 | Qwen 输出 |
| --- | --- | --- |
| 不是不想去，但今天可能不行 | 我不想去, 但今天我有点难。 | 虽然不是不想去，但今天可能有点勉强。 |
| 暂时别用恢复道具，留到下一场 Boss 战 | 先别用回收品 接下“老大之战” | 回复物品还不要用，先留到下一Boss战时再使用。 |
| 敌人来了也等我发出信号再行动 | 如果敌人来了,不要动,直到我试图匹配。 | 敌人来了，直到我发出信号前，都不要动。 |

这些样例中否定、条件、游戏术语改善明显，但仍有「午餐制」「回复物品」等不够自然的用语，不是完整翻译质量评估。六个互译方向都已生成非空结果。Qwen 加载后的整卡显存使用约 4097 MiB / 8188 MiB，包含桌面等其他程序，**不是模型独占显存统计**；游戏占用和长期负载下延迟尚未实测。

## 本地进程与隐私

语音识别继续通过用户隔离命名管道通信。Qwen 引擎由 worker 自动启动，绑定 `127.0.0.1` 随机端口，使用每次会话生成的访问密钥，关闭 Web UI，不接入外部模型服务。待翻译字幕作为用户数据放入提示，系统指令要求只翻译、不执行字幕中的指令；这不是对模型绝不误遵循的保证。

停止/退出时清理自己启动的服务。Windows Job Object 确保 worker 异常退出时也关闭该服务，避免残留进程占用显存；不会关闭用户已有的 LM Studio 或其他模型服务。字幕和服务日志不写入磁盘；内存日志只保留少量启动诊断。

开发下载命令：`.venv/Scripts/python.exe -X utf8 worker/download_qwen.py models`。实时测试在 CoreTests 参数末尾添加 `--gpu --qwen --latency`。GUI 截图和 B 站现场效果仍需人工验收。
