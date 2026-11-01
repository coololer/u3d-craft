# 踩坑记录

> 真实遇到的问题、完整现象与解决过程。面试时"你遇到过什么难点"直接从这里取。
>
> 每条格式：现象 → 完整报错 → 根因 → 解决 → 复盘。

---

## 目录

- [P-001 Unity 2021.3.14f1 打 APK 失败：Android SDK 许可证未接受](#p-001)
- [P-002 包体分析：空包 11MB，引擎占 83%](#p-002)
- [P-003 Mono 打包比 IL2CPP 还大](#p-003)
- [P-004 Ollama 已下载模型但 API 返回空列表](#p-004)
- [P-005 Ollama 推理报 CUDA error: device kernel image is invalid](#p-005)
- [P-006 WebGL 部署白屏：.gz 压缩与服务器 MIME 配置](#p-006)

---

<a id="p-001"></a>
## P-001 Unity 2021.3.14f1 打 APK 失败：Android SDK 许可证未接受

**日期**：2026-11-01
**环境**：Unity 2021.3.14f1 + 内置 Android SDK + 内置 OpenJDK 1.8，Win10 24H2

### 现象

新建空 3D 工程，直接 Build APK，Gradle 阶段构建失败（约 8 秒）：

```
FAILURE: Build failed with an exception.

* What went wrong:
Could not determine the dependencies of task ':launcher:compileReleaseJavaWithJavac'.
> Failed to install the following Android SDK packages as some licences have not been accepted.
     build-tools;29.0.2 Android SDK Build-Tools 29.0.2
  To build this project, accept the SDK license agreements and install the missing components using the Android Studio SDK Manager.

  Using Android SDK: F:\HubUnity2017-2019\2021.3.14f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK
```

关键日志还有：

```
WARNING: The specified Android SDK Build Tools version (28.0.3) is ignored,
         as it is below the minimum supported version (29.0.2) for Android Gradle Plugin 4.0.1.
Warning: License for package Android SDK Build-Tools 29.0.2 not accepted.
File C:\Users\<user>\.android\repositories.cfg could not be loaded.
```

### 根因

两个问题叠加：

1. **版本不满足**：Unity 2021.3.14f1 内置 SDK 只有 `build-tools 28.0.3`，而它自带的 Android Gradle Plugin（AGP）是 **4.0.1**，最低要求 `build-tools 29.0.2`。
2. **许可证缺失**：Gradle 本可自动下载缺失组件，但内置 SDK **不带 `licenses` 目录**（普通安装的 SDK 根目录下会有），无法通过许可校验，自动安装被拒。

> 这也是为什么用 Unity Hub 正常安装的编辑器一般不会遇到——Hub 会处理许可证，而手动搬运/精简安装的编辑器容易缺这一步。

### 排查过程

| 步骤 | 动作 | 结果 |
|---|---|---|
| 1 | 检查内置 SDK 目录 | `build-tools` 只有 28.0.3，`licenses` 目录不存在 |
| 2 | 用内置 `sdkmanager --licenses` 接受许可 | 输出 "All SDK package licenses accepted"，**但目录没有生成** |
| 3 | 以为是路径参数问题，重跑 `--sdk_root` | 仍然没有写出文件 |
| 4 | 手动测试该目录可写性 | 失败（环境沙箱限制写盘，非系统问题） |
| 5 | 改为直接写入官方 license 哈希 | ✅ 生效 |

### 解决

**方案 A（推荐）：写入官方 license 哈希**

在 SDK 根目录创建 `licenses\android-sdk-license`，内容为官方认可的哈希（每行一个）：

```
8933bad161af4178b1185d1a37fbf41ea5269c55
d56f5187479451eabf01fb78af6dfcb131a6481e
24333f8a63b6825ea9c5514f83c2829b004d1fee
```

同时可创建 `licenses\android-sdk-preview-license`：

```
84831b9409646a918e30573bab4c9c91346d8abd
```

> 哈希来源：sdkmanager 判断"是否已接受"时比对的正是这组值，参见 [F-Droid sdkmanager 实现](https://gitlab.com/fdroid/sdkmanager) 的 `licenses()` 函数。

**再补装缺失组件**：

```bash
set JAVA_HOME=<Unity>/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK
<Unity>/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/tools/bin/sdkmanager.bat --sdk_root="<SDK路径>" "build-tools;29.0.2"
```

**方案 B：切换到外部 SDK**

`Edit → Preferences → External Tools`，取消勾选 Android SDK Tools Installed with Unity，指向单独安装的 SDK（需含 build-tools ≥ 29.0.2）。

### 结果

| 项目 | 修复后 |
|---|---|
| `licenses` 目录 | ✅ 已生成（2 个文件） |
| `build-tools` | 28.0.3, **29.0.2** |
| Build APK | ✅ 成功，11.04 MB |

### 复盘

- **报错指向的是许可证，但真正的根因是版本档位不匹配**。如果只接受许可证而不补 29.0.2，Gradle 会自动去下载——但因许可证问题又拦住了，形成死循环。
- 遇到 "licenses have not been accepted"，先确认 `licenses` 目录**真实存在且非空**，不要只看 sdkmanager 的英文输出。
- 另一个细节：`repositories.cfg could not be loaded` 是**无害警告**，不必处理。

---

<a id="p-002"></a>
## P-002 包体分析：空包 11MB，引擎占 83%

**日期**：2026-11-01

### 现象

空 3D 工程 + 1 个 Text，IL2CPP 仅 64 位，APK 为 **11.04 MB**。直觉上"空包应该很小"，但实际并不小。

### 排查方法

直接解压 APK（zip 格式），按压缩后大小（= 对 APK 的真实贡献）排序：

```python
import zipfile, os
z = zipfile.ZipFile("app.apk")
for i in sorted(z.infolist(), key=lambda x: -x.compress_size)[:15]:
    print(f"{i.filename:<50} {i.file_size/1024:8.0f}KB -> {i.compress_size/1024:8.0f}KB")
```

> **关键口径**：比较包体要用 `compress_size`，不是 `file_size`。例如 `unity default resources` 原始 4.03 MB，压缩后只有 0.39 MB（压比 90%），按原始大小排序会严重误判。

### 构成

| 类目 | 压缩后 | 占比 |
|---|---|---|
| `lib/` 原生库（libunity + libil2cpp） | 9.20 MB | **83.4%** |
| `assets/bin/`（元数据 + 默认资源 + 场景） | 1.77 MB | 16.1% |
| `classes.dex` | 0.05 MB | 0.5% |
| 自己加的 Text | ~KB 级 | ≈0% |

详细数据见 [Baseline.md](Baseline.md)。

### 结论

- 空包体积由**引擎决定**，业务资源无法影响；
- 优化空间随资源量增长而增长，"包体降 50%"这类指标只在资源占比高的项目中成立；
- 报告包体改善时，必须说明**优化基数**（从多少降到多少），否则不可信。

---

<a id="p-003"></a>
## P-003 Mono 打包比 IL2CPP 还大

**日期**：2026-11-01

### 现象

同一空工程，IL2CPP 打出来 11.04 MB，切到 Mono 后端再打，变成 **20.23 MB**。按"IL2CPP 是 AOT 编译应该更大"的直觉，结果恰好相反。

### 排查

对比两个 APK 的 `lib/` 架构：

| | IL2CPP 版 | Mono 版 |
|---|---|---|
| 架构目录 | `lib/arm64-v8a/` | **`lib/armeabi-v7a/`** |
| `libunity.so` | 4.26 MB | 8.66 MB |
| `libil2cpp.so` | 4.94 MB | — |
| `libmonobdwgc` + `libmono-native` | — | 2.24 MB |

### 根因

两个原因叠加，且**第一个是主因**：

1. **架构回退（主因，+4.4 MB）**
   构建时勾选了"仅 64 位"，但 Mono 后端在 Unity 2021 下会回退到 32 位 `armeabi-v7a`。32 位的 `libunity.so` 反而比 64 位大近一倍（17.03 MB vs 10.35 MB 原始），压缩比也更差（49% vs 59%）。

2. **.NET 类库全量携带（次因，约 +6.5 MB）**
   IL2CPP 是 AOT：C# 编译成 C++ 再编到机器码，原始 DLL 不需要了。
   Mono 是 JIT：运行时需要完整托管程序集，包括 `mscorlib`(1.51 MB)、`System.Xml`(1.01 MB)、`System.Data`(0.69 MB) 这些空工程根本用不到的库。

### 结论

| 场景 | 谁更小 |
|---|---|
| 空包 / 小项目 | **IL2CPP**（不用带类库） |
| 大型项目 | 差距收窄，但 IL2CPP 性能始终更优 |

**流程建议**：开发迭代用 Mono（编译快），出包前切 IL2CPP 验证（性能 + 可上架）。

### 复盘

- **对比实验必须控制变量**。这次对比同时变了「后端」和「架构」两个量，严格来说不构成有效对照。下一步应固定架构重测，或明确标注结论的局限性。
- 面试表达时不要简单说"IL2CPP 比 Mono 小"，而要说清楚"在空工程、架构不一致条件下的一次观察"，并讲出两个原因——细节比结论更值钱。

---

<a id="p-004"></a>
## P-004 Ollama 已下载模型但 API 返回空列表

**日期**：2026-11-01
**环境**：Ollama 0.40.13，Win10 24H2，RTX 3070 Ti Laptop 8 GB

### 现象

`OLLAMA_MODELS` 已设为 `E:\AI\ollama\models`，该目录下确实有 4.36 GB 文件，manifest 也存在。但：

```bash
curl http://127.0.0.1:11434/api/tags
# {"models":[]}

ollama list
# 空表
```

### 排查过程

| 步骤 | 动作 | 发现 |
|---|---|---|
| 1 | 看模型目录大小 | `E:\AI\ollama\models` = 4.36 GB，**不是空的** |
| 2 | 检查 blobs | 主权重 `sha256-2bada8a7...` = 4.36 GB，与 manifest 声明的 4683073952 字节一致 |
| 3 | 检查 manifests | `registry.ollama.ai/library/qwen2.5/7b` 存在，模型确实注册成功 |
| 4 | 手动 `ollama serve` | 报错 `bind: Only one usage of each socket address` —— **11434 已被别的实例占用** |
| 5 | `netstat -ano \| findstr :11434` | PID 12348 在 LISTENING |
| 6 | 查该进程 | 读不到信息（运行在另一个会话/权限域），且启动时间早于环境变量设置 |

### 根因

**Ollama 是常驻服务，只在启动时读取一次 `OLLAMA_MODELS`。**

修改环境变量后，已运行的实例不会重新读取，仍然使用启动时的旧路径（`C:\Users\<user>\.ollama\models`，空的）。而新起的实例因为端口被占又起不来，于是：

- 模型文件在 E 盘 ✅
- 服务读的是 C 盘空目录 ❌
- 两边都"看起来没问题"，但 API 就是返回空

### 解决

必须**彻底结束旧实例**再重启：

```powershell
# 1. 结束所有 Ollama 进程（注意还有托盘应用 "ollama app"）
Get-Process ollama, "ollama app" -ErrorAction SilentlyContinue | Stop-Process -Force

# 2. 确认端口已释放（应无输出）
netstat -ano | findstr :11434

# 3. 带新环境变量启动
$env:OLLAMA_MODELS = "E:\AI\ollama\models"
Start-Process "$env:LOCALAPPDATA\Programs\Ollama\ollama.exe" -ArgumentList "serve" -WindowStyle Hidden

# 4. 验证
curl http://127.0.0.1:11434/api/tags
```

> **最容易漏的一步**：只结束 `ollama.exe` 不够，托盘里的 `ollama app.exe`（GUI 启动器）会自动把服务再拉起来，而且这次拉起的实例同样带旧环境变量。要在系统托盘右键退出，或用上面的命令同时结束两个进程。

### 复盘

- **"文件在"≠"服务在用"**。排查这类问题时，要区分「磁盘状态」和「运行时状态」，分别验证。
- 修改服务的环境变量后，**必须重启该服务**才生效——这一点对 Ollama、Docker、数据库等都成立。
- Windows 上这类"后台服务 + 托盘应用"的双进程结构，是排查时的高频陷阱。
- 验证顺序建议：先看文件 → 再看进程 → 再看端口 → 最后看 API，逐层排除。

---

<a id="p-005"></a>
## P-005 Ollama 推理报 CUDA error: device kernel image is invalid

**日期**：2026-11-01
**环境**：Ollama 0.40.13 / RTX 3070 Ti Laptop 8 GB / NVIDIA 546.30 / Win10 24H2

### 现象

`ollama list` 能看到 `qwen2.5:7b`，`/api/tags` 也正常返回，但一经推理就返回 HTTP 500：

```json
{
  "error": "llama-server process has terminated: exit status 0xc0000409:
            The system detected an overrun of a stack-based buffer in this application.:
            CUDA error\nCUDA error: device kernel image is invalid"
}
```

`/api/version`、`/api/ps` 均正常返回 200，说明**服务本身健康，只有推理路径崩溃**。

### 排查过程

| 步骤 | 动作 | 结果 |
|---|---|---|
| 1 | `GET /api/version` | 200 `{"version":"0.40.13"}` — 服务活着 |
| 2 | `GET /api/ps` | 200 `{"models":[]}` — 没有已加载模型 |
| 3 | `POST /api/generate` | 500，抓到完整错误体 |
| 4 | `POST /v1/chat/completions` | 同样 500，错误体一致 |
| 5 | 看错误关键字 | **`device kernel image is invalid`** = GPU 内核二进制与驱动不匹配 |

### 根因

Ollama 0.40 版本内置的 CUDA 内核是按较新的 CUDA Toolkit 编译的，而本机驱动为 **546.30**（2024 年初）。驱动版本低于内核镜像的运行时要求，加载 GPU 核函数时校验失败，导致 llama-server 进程直接崩溃。

> **推论依据**：错误发生在「加载/执行 GPU kernel」阶段而非「下载或读取模型」阶段，且换哪个 API 入口都崩，说明模型文件本身没问题，问题在 GPU 运行时栈。

### 解决

**方案 A：升级 NVIDIA 驱动（治本）**

1. GeForce Experience 或官网下载最新 Game Ready Driver
2. 显卡选 `RTX 3070 Ti Laptop GPU`
3. 自定义安装 → 勾选**执行清洁安装**
4. 重启

升级后验证：

```bash
curl http://127.0.0.1:11434/api/ps
ollama run qwen2.5:7b "你好"
```

**方案 B：临时改用 CPU 推理（立即可用，速度慢）**

```powershell
Get-Process ollama, "ollama app" -ErrorAction SilentlyContinue | Stop-Process -Force
$env:CUDA_VISIBLE_DEVICES = "-1"
$env:OLLAMA_MODELS = "E:\AI\ollama\models"
Start-Process "$env:LOCALAPPDATA\Programs\Ollama\ollama.exe" -ArgumentList "serve" -WindowStyle Hidden
```

- CPU 方案用 **API** 调用时，`test_api.py` 走的是 `/api/generate`，会单独开进程，通常不受影响；但 Ollama 服务端若启动时已启用 GPU，需要按上面的方式重启服务端。
- 7B Q4 在 i7-12700H 上约 **3–6 tok/s**，能跑但体验差。

### 当前状态

**未解决（待升级驱动）**。已降级为「非阻塞项」：

- AI 主力改用 DeepSeek 云端 API（已打通，0.78s 响应，成本约 ¥1/百万 token）
- 本地模型作为 W11–W13 做 AI NPC 兜底时才需要，届时升级驱动

### 复盘

- **"能列出来"≠"能跑"**。`/api/tags`、`/api/ps` 只说明模型文件在，不说明 GPU 推理栈可用。必须实调一次才知道。
- 错误信息里 `device kernel image is invalid` 是很明确的信号：**驱动与 CUDA 运行时不匹配**，不要误以为是模型损坏去重下模型。
- 环境扫描结果（驱动偏旧）在第一天就出现过，当时判断为「建议升级」，事后证明它会实际阻塞。**"建议"和"阻塞"的界限，要动手验证才知道。**
- 计划上这类问题的处理策略：**不阻塞主线**，主力方案切云端，本地路径降级为待办。

---

<a id="p-006"></a>
## P-006 WebGL 部署白屏：.gz 压缩与服务器 MIME 配置

**日期**：2026-11-01
**环境**：Unity 2021.3.14f1，WebGL 构建，gzip 压缩

### 现象

WebGL 构建成功（4.96 MB），本地 `python -m http.server` 打开正常。但换到其它静态服务器 / CDN / 微信小游戏环境时，常见问题是**页面卡在加载条、或直接白屏**，控制台报 wasm 或 data 文件加载失败。

### 根因

Unity 2021 的 WebGL 产物默认是 `.gz` 压缩包：

```javascript
config = {
  dataUrl:      buildUrl + "/webtest.data.gz",
  frameworkUrl: buildUrl + "/webtest.framework.js.gz",
  codeUrl:      buildUrl + "/webtest.wasm.gz",
  // 注意：没有 decompressionFallback 字段
}
```

**加载器不含解压逻辑，完全依赖服务器告诉浏览器"这是 gzip 内容"**。服务器必须返回：

```
Content-Encoding: gzip
```

如果服务器只是把 `.gz` 当作普通文件按 `application/octet-stream` 或 `application/gzip` 返回（只是 MIME 类型，没有 `Content-Encoding`），浏览器不会自动解压，Unity 拿到的是压缩字节流，解析失败 → 白屏。

### 排查方法

浏览器 F12 → Network，找到 `webtest.wasm.gz`：

| 检查点 | 正常 | 异常 |
|---|---|---|
| Status | 200 | 404 / 403 |
| **Content-Encoding** | `gzip` | **缺失** ← 白屏主因 |
| Content-Type | `application/wasm` 或任意 | — |
| 响应大小 | ≈ 3.72 MB（未解压前） | — |

也可以用命令行直接验证：

```bash
curl -sI http://localhost:8080/Build/webtest.wasm.gz | findstr /i "content-encoding content-type"
```

### 解决

**方案 A：正确配置服务器（推荐）**

| 服务器 | 配置方式 |
|---|---|
| Nginx | `gzip_static on;`（会自动为 `.gz` 文件补 `Content-Encoding: gzip`） |
| IIS | 在 `web.config` 的 `<staticContent>` 中添加 `.gz` 的 mimeMap，并确保「静态内容压缩」已启用 |
| 阿里云 OSS / 腾讯云 COS | 对象上传时设置 `Content-Encoding: gzip` 元数据 |
| Python http.server | 默认可用（本项目实测 4 个资源均 200） |

**方案 B：构建时开启 Decompression Fallback（更省心）**

Player Settings → **Publishing Settings** → 勾选 **Decompression Fallback**

- 加载器自带解压实现，不依赖服务器配置
- 代价：包体增加约 20–30 KB，首次加载需在浏览器侧解压，略慢
- **外网部署、微信小游戏、第三方托管建议开启**

**方案 C：关闭压缩**

Player Settings → Compression Format → **Disabled**

- 产物为未压缩的 `.wasm` / `.data`，服务器可开启动态 gzip
- 代价：体积可能略大，依赖服务器压缩能力

### 结果

本项目当前状态：

- 本地 `python -m http.server 8080` 正常，4 个关键资源全部 200
- 浏览器可渲染默认场景与 UI Text
- **尚未开启 Decompression Fallback**，正式部署前需评估

### 复盘

- **WebGL 的坑往往不在构建阶段，而在部署阶段**。本地能跑不代表线上能跑——测试环境和生产环境的 MIME / 压缩配置经常不同。
- 优先检查 `Content-Encoding` 而不是 `Content-Type`。很多人看到 `application/gzip` 就以为对了，但这只是声明文件类型，不等于告诉浏览器要解压。
- 涉及多端交付时，**统一在构建脚本里固化这些配置**（如 Addressables 的压缩格式、WebGL 的 fallback 开关），比每次手动勾选更可靠——这正是本项目「一键三端出包」要解决的问题。

---

*最后更新：2026-11-01*
