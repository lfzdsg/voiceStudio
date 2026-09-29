<div align="center">

# 🎙️ Voice Studio

**Windows 本机实时语音识别与翻译字幕工具**

从系统声音或麦克风实时生成中 / 日 / 英字幕，支持六向互译、双语悬浮窗与 SRT / TXT 导出。
识别与翻译全部在本机完成，无需联网，默认不保存音频或转写记录。

[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D6?logo=windows&logoColor=white)](#-快速开始)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Python](https://img.shields.io/badge/Python-3.12-3776AB?logo=python&logoColor=white)](https://www.python.org/)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](#-贡献指南)

[特性](#-特性) · [快速开始](#-快速开始) · [使用指南](#-使用指南) · [贡献](#-贡献指南) · [许可证](#-许可证)

</div>

---

## 📖 目录

- [特性](#-特性)
- [界面预览](#-界面预览)
- [快速开始](#-快速开始)
- [使用指南](#-使用指南)
- [快捷键](#-快捷键)
- [模型与计算设备](#-模型与计算设备)
- [项目结构](#-项目结构)
- [开发](#-开发)
- [测试](#-测试)
- [构建发行版](#-构建发行版)
- [已知限制](#-已知限制)
- [路线图](#-路线图)
- [贡献指南](#-贡献指南)
- [许可证](#-许可证)
- [致谢](#-致谢)

## ✨ 特性

| 能力 | 说明 |
| --- | --- |
| 🎧 双来源采集 | WASAPI 系统回环（抓取电脑播放的声音）或麦克风输入，同一会话二选一 |
| 🗣️ 多引擎识别 | SenseVoiceSmall（阿里，ONNX int8 / CPU）与 Whisper（faster-whisper，CPU / CUDA） |
| 🌐 六向互译 | 中文、日文、英文任意方向互译，同语言自动旁路保留原文 |
| 🧠 本机翻译 | Qwen3-4B-Instruct-2507（llama.cpp，CUDA / CPU）直译；另提供 Argos 轻量翻译对比 |
| 💬 双语悬浮窗 | 原文 / 译文 / 双语三种显示，可锁定鼠标穿透，适配多显示器 |
| 📝 字幕导出 | 一键导出 SRT / TXT，支持原文、译文、双语模式 |
| 🔒 隐私友好 | 推理完全本地化，默认不保存音频与转写记录，不向其他进程注入代码 |
| 🖥️ 托盘与热键 | 全局快捷键控制启停与悬浮窗，最小化后可从系统托盘恢复 |

## 🖼️ 界面预览

> 截图与演示 GIF 待补充，欢迎通过 Pull Request 贡献。

界面由主窗口与悬浮字幕窗两部分组成：主窗口负责来源选择、模型配置、实时字幕与导出；悬浮窗负责在任意应用之上显示双语字幕。

## 🚀 快速开始

### 方式一：下载发行版（推荐）

发行版为自包含目录，已内置运行环境、识别模型、翻译模型、llama.cpp 与 GPU 运行库，解压即可运行。

1. 从 [Releases](https://github.com/lfzdsg/voiceStudio/releases) 下载发行包并解压。
2. 打开目录中的 `VoiceStudio.exe`。

> 新版约 7.1 GB，包含运行环境、base、SenseVoiceSmall、Qwen3 4B 量化模型、llama.cpp、Argos 翻译包和 GPU 运行库。
> 复制到其他电脑时请保留**整个目录（含 `engine`、`models`、`cuda`）**，放在可写路径中，不要只复制 exe。

### 方式二：从源码运行

环境要求：Windows 10/11 x64、[.NET 8 SDK](https://dotnet.microsoft.com/download) 或更高、[Python 3.12 x64](https://www.python.org/downloads/)。
锁定的依赖版本需要 Python 3.12；本版本在 Windows 10 19045 / Python 3.12.8 上构建。

```powershell
git clone https://github.com/lfzdsg/voiceStudio.git
cd voiceStudio

# 创建虚拟环境并安装 Python 依赖
powershell -ExecutionPolicy Bypass -File scripts/setup.ps1

# 启动桌面程序（会自动拉起本地识别 / 翻译 worker）
powershell -ExecutionPolicy Bypass -File scripts/start.ps1
```

首次运行需要联网下载模型，之后本机推理无需联网：

- 模型存放于 `models/<模型名>`；
- Argos 翻译包位于 `models/translation/<源语言_目标语言>`；
- Qwen 位于 `models/qwen3-4b-instruct`，推理引擎位于 `engine/llama`；
- 首次下载约 2.5 GB（另需引擎），可点击界面按钮或执行
  `.venv/Scripts/python.exe -X utf8 worker/download_qwen.py models` 安装。

启用 GPU 推理可在项目虚拟环境内安装 CUDA 运行库（不修改系统 PATH 或驱动）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/setup-gpu.ps1
```

界面设置保存在 `.runtime/settings.json`；发行目录使用自身的 `engine`、`models` 与 `.runtime`。

## 🎧 使用指南

1. **选择「声音来源」**
   - **自己说话**：选择「麦克风 · 默认输入设备」或具体麦克风。
   - **视频、桌面软件、游戏**：选择「系统声音 · 默认播放设备」或实际出声的耳机 / 扬声器。
2. **选择识别模型**
   先试「SenseVoice · 阿里」，语言选「自动识别」；主要看日语内容时可固定选「日本語」，减少短句自动判断错误。base 也已安装，可对比效果。
3. **选择翻译器**
   选「Qwen3 4B · 本机直译」，目标「翻译成中文」，显示「原文 + 译文」或「仅译文」。也可选择英文、日文目标，实现六个方向互译；同语言直接保留原文。保留「Argos · 轻量翻译」可供对比。
4. **开始字幕**
   点击「开始字幕」，预加载完成后开始收音。音量条用于确认输入；有声音不等于有语音，音乐和噪声可能不会生成字幕。临时原文和译文约每秒更新，临时译文会修订，只有句子确认后的结果进入记录和导出。
5. **停止与导出**
   点击「停止字幕」，等待最后一段及译文处理完，再导出 SRT 或 TXT。导出使用选中的原文 / 译文 / 双语模式；翻译失败保留原文。时间戳以本次采集开始时间为零点。

同一会话只采集一种来源，切换来源需先停止。系统声音模式不会直接收录你的说话声；麦克风模式不会直接抓取游戏音频。默认设备变化时会停止并提示重新连接。

## ⌨️ 快捷键

| 快捷键 | 功能 |
| --- | --- |
| `Ctrl` + `Alt` + `S` | 开始 / 停止字幕 |
| `Ctrl` + `Alt` + `H` | 显示 / 隐藏悬浮窗 |
| `Ctrl` + `Alt` + `L` | 锁定 / 解锁鼠标穿透 |

最小化后可从系统托盘恢复；退出前会提示未导出的记录。

## 🧠 模型与计算设备

第四版使用 Qwen3-4B-Instruct-2507 Q4_K_M 做日中直译；旧 Argos 的日中翻译仍经过英语中转。Qwen 在本机样例中改善了否定、条件和游戏台词翻译，但仍有误译和不自然措辞，不代表 YouTube 的翻译质量，详见 [Qwen 实测和使用说明](docs/QWEN_TRANSLATION.md)。这是文字翻译模型，Qwen3-ASR 与字节 Seed-ASR 尚未集成。

计算设备可选「自动」或「NVIDIA GPU · CUDA」。Whisper 识别和翻译支持 GPU；SenseVoice 识别仍使用 CPU / ONNX int8，可以同时用 GPU 翻译。开始后右侧显示实际识别和翻译设备。

| 场景 | 行为 |
| --- | --- |
| Whisper / Argos 自动模式缺少运行库 | 回退 CPU |
| Qwen 自动模式无可用 CUDA | 明确提示，可手动选择 CPU |

RTX 4060 上已通过 Whisper base、Argos 和 Qwen CUDA 推理验证；large-v3 / turbo / medium 只提供下载选项，未下载到发行包。

第三版针对延迟做了三项调整：识别预览间隔从 1.5 秒改为 1 秒；临时识别结果提前翻译，旧预览合并且最终结果优先；收音前预加载翻译模型，相同原文复用有上限的会话缓存。没有为了提速缩短最终分段或降低翻译 beam size。短字幕的 GPU 翻译未必比 CPU 快很多，主要改善来自减少结句等待。实测范围与结果见 [延迟实测](docs/LATENCY.md)。

## 🧱 项目结构

```text
VoiceStudio.sln / VoiceStudio.csproj   WPF 桌面应用（.NET 8）
app.manifest                           应用清单

MainWindow.*                           主窗口：会话、下载、托盘、导出
OverlayWindow.*                        悬浮字幕：鼠标穿透、显示器定位
Services/AudioCapture.cs               WASAPI 系统回环 / 麦克风采集与格式转换
Services/Engine.cs                     识别进程生命周期、用户隔离命名管道、有限发送队列
Core/PipeProtocol.cs                   进程间消息帧编码
Core/Transcript.cs                     SRT 格式与转写模型

worker/                                Python worker：VAD、Whisper / SenseVoice、异步翻译与有限推理队列
  main.py / protocol.py                进程入口与协议
  streaming.py / recognizer.py         分段与识别管线
  translation.py                       翻译调度与回退
  qwen_translation.py                  Qwen3 本机翻译
  download*.py / catalog.py            模型下载与清单
  requirements*.txt                    Python 依赖（含 GPU / 锁定版本）

tests/                                 自动验证与真实音频集成测试
  CoreTests/                           C# 协议与 SRT 单元测试
  test_*.py                            Python 管线测试

scripts/                               环境安装、启动、测试、打包脚本
docs/                                  延迟、模型选型、Qwen 翻译、人工检查清单
```

WASAPI 负责将设备格式实际转换为 16 kHz / 单声道 / PCM16；不存在只修改采样率标签的伪重采样。音频不重播，不改变系统音量。能量门限负责增量分段，Silero VAD 进一步过滤非语音。约 1 秒更新一次临时识别任务，停顿 0.6 秒或连续 5 秒结束片段；实际显示速度取决于本机推理性能。

## 🛠️ 开发

推荐的开发循环：

```powershell
# 安装依赖（创建项目 .venv，安装锁定版本）
powershell -ExecutionPolicy Bypass -File scripts/setup.ps1

# 以开发模式启动
powershell -ExecutionPolicy Bypass -File scripts/start.ps1
```

- C# 侧构建：`dotnet build -c Release`。
- Python 侧依赖：见 [`worker/requirements.txt`](worker/requirements.txt:1)；GPU 运行库见 [`worker/requirements.gpu.txt`](worker/requirements.gpu.txt:1)。
- 推理引擎位于 `engine/llama`，模型位于 `models/`，二者均不提交到仓库（见 [`.gitignore`](.gitignore:1)）。

## ✅ 测试

```powershell
# Python 测试 + C# 测试
powershell -ExecutionPolicy Bypass -File scripts/test.ps1

# 生成测试音频并执行真实识别集成测试
powershell -ExecutionPolicy Bypass -File scripts/make-test-audio.ps1
dotnet run --project tests/CoreTests/CoreTests.csproj -c Release -- --recognize .runtime/speech.wav en
```

覆盖范围：

- Python 测试覆盖静音抑制、临时 / 最终分段、停止尾段、回环静音空隙、帧边界、队列上限及协议校验。
- 还覆盖短片段语言延续、正常语言切换、六方向翻译路由、同语言旁路、错误 / 排队回退和下载路径验证。
- C# 测试覆盖 SRT 序号和时间、双语导出与失败回退、Unicode 管道消息、无效消息拒绝及可选的真实识别 / 翻译集成测试。
- `--microphone` 短暂打开默认麦克风，检查收到 PCM 帧且不保存音频。
- `--loopback .runtime/speech.wav` 会播放已知语音并验证系统回环输入（**会实际发出声音**）。
- 集成测试参数顺序：`--recognize 音频.wav 识别语言 项目或发行目录 模型名 翻译目标`；识别语言可用 `auto`，翻译目标可用 `off`。
- `.venv/Scripts/python.exe -X utf8 scripts/check-models.py` 可运行已下载模型的六方向翻译及三语言自动识别测试；需要的公开音频位置见 [模型选型文档](docs/MODEL_COMPARISON.md)。

验证包含 Release 构建、25 项 Python 自动测试、C# 协议与双语 SRT 检查、实际中日英自动识别、六方向翻译、源码和打包引擎的实时分段识别与中文翻译，并覆盖临时翻译、预览合并、最终结果优先、旧译文抑制、Qwen 输入分离与截断结果拒绝。报告位于 `.runtime/model-check.json`、`.runtime/gpu-check.json`、`.runtime/qwen-check.json`。

> 这些验证不代表翻译精度达标或所有游戏和设备均已验证。桌面自动界面验证曾被用户 `Esc` 停止，因此窗口截图、鼠标穿透、多显示器以及游戏内显示未标为已验证，建议按 [人工检查清单](docs/MANUAL_CHECKLIST.md) 手动检查。

## 📦 构建发行版

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package.ps1
```

脚本使用 PyInstaller 打包识别引擎，并用 .NET 自包含发布生成桌面程序；已安装的模型、翻译包、llama.cpp 与 NVIDIA 运行库会复制到 `dist/VoiceStudio-v4`。可用 `-OutputDirectory dist/其他目录` 指定输出目录，构建前应关闭目标目录中运行的程序。

Python 依赖锁定在 [`worker/requirements.lock.txt`](worker/requirements.lock.txt:1)。依赖不提交到源码仓库。

## ⚠️ 已知限制

- 首版支持系统混音和麦克风；指定应用采集、只识别某个浏览器标签页属于后续范围。
- 系统模式只获取所选播放设备上的声音，多设备同时播放时要选择正确设备。
- 游戏建议使用无边框窗口化；不保证普通悬浮窗能覆盖独占全屏游戏，不向游戏注入代码。
- 受保护音频、独占设备或 Windows 麦克风访问限制可能导致采集失败。麦克风无输入时，请检查 Windows 的输入设备、音量和桌面应用麦克风权限。
- 系统声音模式可能识别到其他应用的通知或语音；多人讲话和音乐会影响准确率。可删除错误字幕行再导出。
- CPU 推理积压时会提示，达到有限缓冲上限后停止采集，保留已有字幕；可换 tiny 模型或减轻游戏负载。
- 临时译文提前显示但可能修订，不写入导出。最终翻译仍包含结句等待；连续讲话每 5 秒分段，可能拆断完整句子。最终翻译排队超过 8 条时该条回退原文并提示；未执行的临时任务只保留最新一条。
- Whisper 自动识别对短句可能误判：小于 3 秒且判断为中日英以外语言时，若已有上一段的中日英语言，则沿用并重新识别；正常中日英切换不强制锁定。必要时手动固定源语言。
- 出于避免长时内存增长，单次会话最多 10000 条最终字幕，达到后需导出并开启新会话。

## 🗺️ 路线图

- [ ] 指定应用 / 浏览器标签页级别的音频采集
- [ ] 集成 Qwen3-ASR 与字节 Seed-ASR 识别模型
- [ ] 提供 Whisper large-v3 / turbo / medium 的发行包下载选项
- [ ] 更完整的悬浮窗自定义（字体、位置记忆、多显示器）
- [ ] 补充界面截图与演示 GIF

## 🤝 贡献指南

欢迎提交 Issue 与 Pull Request。

1. Fork 本仓库并从 `main` 创建特性分支：`git checkout -b feature/your-feature`。
2. 遵循现有代码风格：C# 使用 `.editorconfig` 约定，Python 保持类型注解与模块化结构。
3. 提交前请确保 `scripts/test.ps1` 通过，并在 PR 描述中说明变更动机与验证方式。
4. 如涉及界面或采集行为，请附上复现步骤或录屏。
5. 提交 PR 并关联相关 Issue。

提交信息建议使用清晰的祈使句（如 `fix: 修正回环采集在设备切换时的崩溃`）。

## 📄 许可证

本项目基于 [Apache License 2.0](LICENSE) 开源。

## 🙏 致谢

本项目构建于以下开源工作之上：

- [NAudio](https://github.com/naudio/NAudio) —— Windows 音频采集与 WASAPI 回环
- [faster-whisper](https://github.com/SYSTRAN/faster-whisper) —— Whisper 推理
- [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) —— SenseVoiceSmall ONNX 推理
- [SenseVoice](https://github.com/FunAudioLLM/SenseVoice) —— 阿里语音识别模型
- [llama.cpp](https://github.com/ggml-org/llama.cpp) —— Qwen3 本机推理引擎
- [Qwen3](https://github.com/QwenLM/Qwen3) —— 本机翻译模型
- [Argos Translate](https://github.com/argosopentech/argos-translate) —— 轻量离线翻译

技术依据：[WASAPI loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording)、[NAudio](https://github.com/naudio/NAudio)、[faster-whisper](https://github.com/SYSTRAN/faster-whisper)。

---

<div align="center">

如果这个项目对你有帮助，欢迎点一个 ⭐ Star。

</div>
