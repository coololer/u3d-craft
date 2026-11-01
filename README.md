# u3d-craft

> Unity 休闲 / 超休闲小游戏的多端交付脚手架与工具集
> **APK · WebGL · 微信小游戏** 一键出包 · Addressables 资源管理 · 性能与包体基线

![Unity](https://img.shields.io/badge/Unity-2021.3.14f1-000000?logo=unity&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Android%20%7C%20WebGL%20%7C%20WeChat-2C8E5B)
![CSharp](https://img.shields.io/badge/C%23-.NET%20Standard%202.1-178600?logo=csharp&logoColor=white)
![Status](https://img.shields.io/badge/status-WIP-yellow)

---

## 这个仓库是什么

把一整套「小游戏如何从工程走到三端上线」的经验沉淀成可复用的骨架：不是 demo，而是一个真正能当作**起跑线**的项目模板。

解决下面这些在中小团队里反复出现的问题：

| 问题 | 本仓库的解法 | 状态 |
|---|---|---|
| 每次新项目都从空工程搭 UI / 存档 / 配置表 | 通用小游戏框架：MVC 分层 + 事件总线 + 对象池 + 状态机 + 配置表工具 | 🚧 |
| 出包流程靠人肉点菜单，耗时长且易错 | 命令行一键出包 + GitHub Actions 自动构建三端 | 🚧 |
| 包体大、首包超微信 4MB 限制 | Addressables 分组策略 + 首包裁剪 + 资源自动检查 | 🚧 |
| 不知道优化有没有效果 | 三端出包耗时 / 包体 / 首启时间的**基线数据表** | ✅ |
| 热更新要发版才能改 | HybridCLR 热更方案 | ⏳ |

> 状态图例：✅ 已完成 · 🚧 进行中 · ⏳ 计划中

---

## 特性

- **多端出包流水线** —— 一份配置产出 Android / WebGL / 微信小游戏三个包，记录每步耗时，目标是将出包时间缩短 30%
- **Addressables 资源管理** —— 分组策略、远程加载、引用计数与卸载验证（防内存泄漏）
- **性能基线** —— Profiler / Memory Profiler 数据留存，作为每次优化的对照组
- **编辑器工具** —— 资源检查、包体分析、一键打包菜单
- **代码规范** —— 目录分层、命名约定、Commit 规范

---

## 开始使用

### 环境要求

| 依赖 | 版本 | 说明 |
|---|---|---|
| Unity Editor | **2021.3.14f1** | UI 与资源管线以此版本为准 |
| Unity 模块 | Android Build Support · WebGL Build Support · Minigame | 缺模块会构建失败 |
| JDK | Unity 内置 OpenJDK 1.8 | 默认即可，无需额外配置 |
| Git | 2.40+ | 建议 `core.autocrlf=true`（Windows） |
| Node.js | LTS | 仅用于文档与部分效率脚本 |

### 克隆与打开

```bash
git clone https://github.com/coololer/u3d-craft.git
```

1. Unity Hub → **Add** → 选择本仓库根目录
2. 用 **2021.3.14f1** 打开（其他版本需自行迁移）
3. 首次打开会拉取 Package Manager 依赖，耐心等待

### 目录结构

```text
u3d-craft/
├── Assets/
│   ├── Scripts/
│   │   ├── Core/              # 框架核心：事件总线、对象池、状态机、MVC 基类
│   │   ├── Gameplay/          # 玩法模块，按功能分包
│   │   ├── UI/                # UI 框架与页面
│   │   └── Editor/            # 编辑器扩展：打包、资源检查工具
│   ├── AddressableAssets/     # Addressables 分组配置
│   ├── Art/                   # 美术资源（按类型分目录）
│   └── Resources/             # 仅放启动必需资源，其余走 Addressables
├── Docs/                      # 设计文档、基线数据、踩坑记录
├── Build/                     # 构建脚本与 CI 配置（构建产物不入库）
└── .github/workflows/         # CI 流水线
```

---

## 构建

### 编辑器内构建

菜单 `Tools/Build/` 下提供三端打包入口，构建产物输出到 `Build/Output/`（已被 `.gitignore` 排除）。

### 命令行构建

```bash
# Android
Unity.exe -quit -batchmode -projectPath . \
  -executeMethod BuildScript.BuildAndroid -logFile build-android.log

# WebGL
Unity.exe -quit -batchmode -projectPath . \
  -executeMethod BuildScript.BuildWebGL -logFile build-webgl.log
```

WebGL 产物需要 HTTP 服务才能打开（直接双击 index.html 会白屏）：

```bash
cd Build/Output/WebGL && python -m http.server 8080
```

### 性能基线

每次优化前后记录到 `Docs/Baseline.md`，字段约定：

| 日期 | 平台 | 包体 | 出包耗时 | 首启时间 | 低端机平均帧率 | 峰值内存 |
|---|---|---|---|---|---|---|
| — | Android | — | — | — | — | — |

---

## 本地 AI 环境（Ollama）

用于 AI NPC / GameCopilot 的**离线兜底模型**与本地对比实验。云端 API 为主（DeepSeek / 通义千问），本地模型负责断网降级与成本控制。

### 前置条件

| 项目 | 要求 |
|---|---|
| Ollama | ≥ 0.40 |
| 显存 | ≥ 6 GB 可跑 7B（Q4），8 GB 更稳 |
| 磁盘 | 每个 7B 模型约 5 GB |

> 显存不足时 Ollama 会溢出到内存，速度骤降到几 tok/s。14B 及以上模型在 8 GB 显存上不适合日常使用。

### 把模型目录迁出 C 盘（强烈建议）

Ollama 默认把模型放在 `C:\Users\<user>\.ollama\models`。C 盘紧张时先迁移：

```powershell
# 1. 设置模型目录（用户级环境变量，永久生效）
[Environment]::SetEnvironmentVariable("OLLAMA_MODELS", "E:\AI\ollama\models", "User")

# 2. 建目录
New-Item -ItemType Directory -Path "E:\AI\ollama\models" -Force

# 3. 关键：必须重启 Ollama 服务，新目录才对当前实例生效
Get-Process ollama, "ollama app" -ErrorAction SilentlyContinue | Stop-Process -Force
$env:OLLAMA_MODELS = "E:\AI\ollama\models"
Start-Process "$env:LOCALAPPDATA\Programs\Ollama\ollama.exe" -ArgumentList "serve" -WindowStyle Hidden
```

### 拉取模型并验证

```powershell
ollama pull qwen2.5:7b
ollama list                                   # 应看到 qwen2.5:7b
ollama run qwen2.5:7b "用一句话介绍 Addressables"
```

服务端验证（绕过 CLI，直接查 API）：

```bash
curl http://127.0.0.1:11434/api/tags
```

### 排错

| 现象 | 原因 | 解决 |
|---|---|---|
| `ollama list` 为空，但模型目录有几 GB 文件 | 服务实例未继承 `OLLAMA_MODELS`，去读了 C 盘空目录 | 完全退出 Ollama（含托盘图标）后重启 |
| `bind: Only one usage of each socket address` | 已有实例占用 11434，新的起不来 | 先 `Stop-Process` 旧实例，或直接复用现有实例 |
| 模型下载中断后重下 | 部分分片保留，Ollama 会续传 | 直接重跑 `ollama pull` 即可 |
| 生成速度极慢（<3 tok/s） | 模型超出显存，溢出到内存 | 换更小的模型（如 `qwen2.5:3b`） |

### 本项目对本地模型的定位

| 用途 | 方案 |
|---|---|
| 主力推理 | 云端 API（DeepSeek / 通义千问） |
| 断网降级 | 本地 `qwen2.5:7b` |
| 成本敏感场景 | 本地小模型 + 云端缓存 |

> 面试可讲：任何 AI 功能都保留本地兜底路径，既控制成本，也避免服务不可用导致玩法不可玩。

---

## Roadmap

- [x] 仓库初始化与 `.gitignore` 配置
- [ ] 通用小游戏框架（事件总线 / 对象池 / 状态机）
- [ ] Addressables 分组策略与远程加载
- [ ] 命令行一键三端出包
- [ ] GitHub Actions CI 自动构建
- [ ] 包体与性能基线数据表
- [ ] HybridCLR 热更新接入
- [ ] 微信小游戏首包裁剪到 4MB 以内

---

## 技术要点与踩坑记录

详细内容放在 `Docs/` 下，这里只放索引：

| 文档 | 内容 | 状态 |
|---|---|---|
| [`Docs/Baseline.md`](Docs/Baseline.md) | 三端出包耗时与包体基线，含 IL2CPP / Mono 对比数据 | ✅ |
| [`Docs/Pitfalls.md`](Docs/Pitfalls.md) | 打包、适配、性能优化中的坑与解法（含完整报错与排查过程） | ✅ |
| `Docs/Architecture.md` | 框架分层说明 | ⏳ |

### 已记录的坑

| 编号 | 问题 | 一句话结论 |
|---|---|---|
| [P-001](Docs/Pitfalls.md#p-001) | Unity 2021.3.14f1 打 APK 报 "licenses have not been accepted" | 内置 SDK 只有 build-tools 28.0.3 且无 licenses 目录，写入官方 license 哈希并补装 29.0.2 |
| [P-002](Docs/Pitfalls.md#p-002) | 空包 11 MB 偏大？ | 83% 是引擎原生库，业务资源占比不足 1% |
| [P-003](Docs/Pitfalls.md#p-003) | Mono 打包 20 MB，比 IL2CPP 还大 | Mono 回退到 32 位 armeabi-v7a + 全量携带 .NET 类库 |

---

## 贡献与规范

- 分支：`main` 保持可构建，功能开发走 `feature/*`，修复走 `fix/*`
- 提交信息：`feat:` / `fix:` / `perf:` / `refactor:` / `docs:` / `chore:`
- **禁止提交**：`.env`、`*.key`、构建产物、`Library/`（已在 `.gitignore` 处理）

---

## 许可

见 [LICENSE](LICENSE)。

---

<sub>本项目是求职作品集的一部分，记录一名 Unity 开发者在多端交付与性能优化上的工程实践。</sub>
