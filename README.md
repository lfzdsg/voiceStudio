# Voice Studio

Windows 本机实时字幕：识别电脑播放声音或麦克风输入，支持中日英识别、互译、双语悬浮字幕与 SRT / TXT 导出。识别和翻译在本机完成，默认不保存音频或转写记录。

## 直接运行

打开 `dist/VoiceStudio-v4/VoiceStudio.exe`。新版约 7.1 GB，包含运行环境、base、SenseVoiceSmall、Qwen3 4B 量化模型、llama.cpp、Argos 翻译包和 GPU 运行库。复制到其他电脑时请保留**整个 VoiceStudio-v4 目录（含 engine、models、cuda）**，放在可写目录中，不要只复制 exe。旧发行目录保留不动。

如果之前打开的是 `bin/Release/net8.0-windows/VoiceStudio.exe`，请先保存需要的字幕并关闭旧窗口，再打开 `dist` 下的新版，避免旧版占用快捷键。

1. 选择「声音来源」：
   - **自己说话**：选择「麦克风 · 默认输入设备」或具体麦克风。
   - **视频、桌面软件、游戏**：选择「系统声音 · 默认播放设备」或实际出声的耳机/扬声器。
2. 先试「SenseVoice · 阿里」，语言选「自动识别」；主要看日语内容时可固定选「日本語」，减少短句自动判断错误。base 也已安装，可对比效果。
3. 翻译器选「Qwen3 4B · 本机直译」，目标「翻译成中文」，显示「原文 + 译文」或「仅译文」。也可以选择英文、日文目标，实现六个方向的互译；同语言直接保留原文。保留「Argos · 轻量翻译」可供对比。
4. 点击「开始字幕」，预加载完成后开始收音。音量条用于确认输入；有声音不等于有语音，音乐和噪声可能不会生成字幕。临时原文和译文约每秒更新；临时译文会修订，只有句子确认后的结果进入记录和导出。
5. 点击「停止字幕」，等待最后一段及译文处理完，再导出 SRT 或 TXT。导出使用选中的原文/译文/双语模式；翻译失败保留原文。时间戳以本次采集开始时间为零点。

同一会话只采集一种来源，切换来源需先停止。系统声音模式不会直接收录你的说话声；麦克风模式不会直接抓取游戏音频。默认设备变化时会停止并提示重新连接。

快捷键：`Ctrl+Alt+S` 开始/停止；`Ctrl+Alt+H` 显示/隐藏悬浮窗；`Ctrl+Alt+L` 锁定/解锁鼠标穿透。最小化后可从系统托盘恢复。退出前会提示未导出记录。

第四版使用 Qwen3-4B-Instruct-2507 Q4_K_M 做日中直译；旧 Argos 的日中翻译仍经过英语中转。Qwen 在本机样例中改善了否定、条件和游戏台词翻译，但仍有误译和不自然措辞，不代表 YouTube 的翻译质量。见 [Qwen 实测和使用说明](docs/QWEN_TRANSLATION.md)。这是文字翻译模型，Qwen3-ASR 与字节 Seed-ASR 尚未集成。

计算设备选「自动」或「NVIDIA GPU · CUDA」。Whisper 识别和翻译支持 GPU；SenseVoice 识别仍使用 CPU / ONNX int8，可以同时用 GPU 翻译。开始后右侧显示实际识别和翻译设备。Whisper/Argos 自动模式缺少运行库时回退 CPU；Qwen 自动模式要求可用 CUDA，否则明确提示，可手动选择 CPU。RTX 4060 上已通过 Whisper base、Argos 和 Qwen CUDA 推理验证；large-v3 / turbo / medium 只提供下载选项，未下载到发行包。

第三版针对延迟做了三项调整：识别预览间隔从 1.5 秒改为 1 秒；临时识别结果提前翻译，旧预览合并且最终结果优先；收音前预加载翻译模型，相同原文复用有上限的会话缓存。没有为了提速缩短最终分段或降低翻译 beam size。短字幕的 GPU 翻译未必比 CPU 快很多，主要改善来自减少结句等待。实测范围与结果见 `docs/LATENCY.md`。

## 开发运行

需要 Windows 10/11 x64、.NET 8 或更高 SDK、Python 3.12 x64。锁定的依赖版本需要 Python 3.12；此版本在 Windows 10 19045 / Python 3.12.8 上构建。

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup.ps1
powershell -ExecutionPolicy Bypass -File scripts/start.ps1
```

开发模式使用项目 `.venv`，模型位于 `models/<模型名>`，Argos 翻译包位于 `models/translation/<源语言_目标语言>`，Qwen 位于 `models/qwen3-4b-instruct`，推理引擎位于 `engine/llama`。界面设置在 `.runtime/settings.json`。发行目录使用自身的 engine、models 和 .runtime。首次下载联网，之后本机推理无需联网。Qwen 模型首次约下载 2.5 GB，另需引擎；点击界面按钮或执行 `.venv/Scripts/python.exe -X utf8 worker/download_qwen.py models` 可安装。

## 验证

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
powershell -ExecutionPolicy Bypass -File scripts/make-test-audio.ps1
dotnet run --project tests/CoreTests/CoreTests.csproj -c Release -- --recognize .runtime/speech.wav en
```

- Python 测试覆盖静音抑制、临时/最终分段、停止尾段、回环静音空隙、帧边界、队列上限及协议校验。
- 还覆盖短片段语言延续、正常语言切换、六方向翻译路由、同语言旁路、错误/排队回退和下载路径验证。
- C# 测试覆盖 SRT 序号和时间、双语导出与失败回退、Unicode 管道消息、无效消息拒绝及可选的真实识别/翻译集成测试。
- `--microphone` 测试短暂打开默认麦克风，检查收到 PCM 帧，不保存音频。
- `--loopback .runtime/speech.wav` 测试会播放已知语音并验证系统回环输入；它会实际发出声音。
- `--recognize .runtime/speech.wav en dist/VoiceStudio-v3 sensevoice-small zh` 使用打包后的 SenseVoice 和中文翻译执行集成测试。
- 最新发行目录使用 `dist/VoiceStudio-v4`；末尾加 `--gpu --qwen --latency` 可验证 Qwen GPU 翻译与句末之前出现临时译文。不加 `--qwen` 时集成测试继续使用 Argos。
- 集成测试参数顺序：`--recognize 音频.wav 识别语言 项目或发行目录 模型名 翻译目标`；识别语言可用 auto，翻译目标可用 off。
- `.venv/Scripts/python.exe -X utf8 scripts/check-models.py` 可运行已下载模型的六方向翻译及三语言自动识别测试；需要的公开音频位置见模型选型文档。

验证包含：Release 构建、25 项 Python 自动测试、C# 协议与双语 SRT 检查、实际中日英自动识别、六方向翻译、源码和打包引擎的实时分段识别/中文翻译。覆盖临时翻译、预览合并、最终结果优先、旧译文抑制、Qwen 输入分离与截断结果拒绝。报告位于 `.runtime/model-check.json`、`.runtime/gpu-check.json`、`.runtime/qwen-check.json`。Qwen 正常退出与 worker 被强制结束时的服务清理已验证。这些不代表翻译精度达标或所有游戏和设备已验证。

采集层此前已验证默认麦克风 PCM 输入、英文固定语音的识别，以及实际播放测试语音的 WASAPI 回环采集（142849 个样本）；此次没有重复录制用户麦克风。

桌面自动界面验证曾被用户 Esc 停止，因此不将窗口截图、鼠标穿透、多显示器以及游戏内显示标为已验证。建议按 `docs/MANUAL_CHECKLIST.md` 手动检查。

## 构建分发目录

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package.ps1
```

脚本用 PyInstaller 打包识别引擎，并用 .NET 自包含发布生成桌面程序。已安装模型、翻译包、llama.cpp 与 NVIDIA 运行库复制到 `dist/VoiceStudio-v4`。可用 `-OutputDirectory dist/其他目录` 指定新目录；构建前应关闭目标目录中运行的程序。依赖不提交到源码仓库；Python 依赖在 `worker/requirements.lock.txt`。开发环境启用 GPU 可执行 `scripts/setup-gpu.ps1`，版本在 `worker/requirements.gpu.txt`；仅安装到项目 `.venv`，不修改系统 PATH 或驱动。

## 模块

```text
MainWindow.*          主窗口、会话、下载、托盘、导出
OverlayWindow.*       悬浮字幕、鼠标穿透、显示器位置
Services/AudioCapture WASAPI 系统回环 / 麦克风采集与格式转换
Services/Engine       识别进程生命周期、用户隔离命名管道、有限发送队列
Core/                 消息帧与 SRT 格式
worker/               VAD、Whisper / SenseVoice、异步翻译与有限推理队列
tests/                自动验证与真实音频集成测试
```

WASAPI 负责将设备格式实际转换为 16 kHz / 单声道 / PCM16；不存在只修改采样率标签的伪重采样。音频不重播，不改变系统音量。能量门限负责增量分段，Silero VAD 进一步过滤非语音。约 1 秒更新一次临时识别任务，停顿 0.6 秒或连续 5 秒结束片段；实际显示速度取决于本机推理性能。

## 已知边界

- 首版支持系统混音和麦克风；指定应用采集、只识别某个浏览器标签页属于后续范围。
- 系统模式只获取所选播放设备上的声音，多设备同时播放时要选择正确设备。
- 游戏建议使用无边框窗口化；不保证普通悬浮窗能覆盖独占全屏游戏，不向游戏注入代码。
- 受保护音频、独占设备或 Windows 麦克风访问限制可能导致采集失败。麦克风无输入时，请检查 Windows 的输入设备、音量和桌面应用麦克风权限。
- 系统声音模式可能识别到其他应用的通知或语音；多人讲话和音乐会影响准确率。可删除错误字幕行再导出。
- CPU 推理积压时会提示，达到有限缓冲上限后停止采集，保留已有字幕；可换 tiny 模型或减轻游戏负载。
- 临时译文提前显示但可能修订，不写入导出。最终翻译仍包含结句等待；连续讲话每 5 秒分段，可能拆断完整句子。最终翻译排队超过 8 条时该条回退原文并提示；未执行的临时任务只保留最新一条。
- Whisper 自动识别对短句可能误判：小于 3 秒且判断为中日英以外语言时，若已有上一段的中日英语言，则沿用并重新识别；正常中日英切换不强制锁定。必要时手动固定源语言。
- 出于避免长时内存增长，单次会话最多 10000 条最终字幕，达到后需导出并开启新会话。

技术依据：[WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)、[NAudio](https://github.com/naudio/NAudio)、[faster-whisper](https://github.com/SYSTRAN/faster-whisper)。
