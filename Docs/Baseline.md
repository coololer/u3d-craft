# 性能与包体基线

> 每次优化前后都在这里记录一组数据。**没有基线就没有"优化 30%"这句话**。
>
> 记录原则：
> - 同一组对比必须**架构一致、构建配置一致**，否则数字没有可比性
> - 只记可复现的测量方式，不记"感觉快了"
> - 每次构建都追加一行，不覆盖历史

---

## 1. 构建环境

| 项目 | 配置 |
|---|---|
| Unity | 2021.3.14f1 |
| Gradle | 6.1.1（Unity 内置） |
| Android Gradle Plugin | 4.0.1 |
| JDK | Unity 内置 OpenJDK 1.8 |
| 构建机 | i7-12700H / 16 GB / RTX 3070 Ti Laptop / Win10 24H2 |
| 测量日期 | 2026-11-01 |

---

## 2. 包体基线

> 本章按**产物类型**组织。注意 Unity Minigame 插件一次构建会产出两个不同目录，用途不可混淆，详见 2.4。

### 2.1 Android —— IL2CPP vs Mono（空工程）

**工程内容**：新建 3D 工程，1 个默认场景 + 1 个 UI Text 文字。

| # | 后端 | 架构 | 包体 | 条目数 | 产物 |
|---|---|---|---|---|---|
| 1 | IL2CPP | arm64-v8a | **11.04 MB** | 23 | `ppp.apk` |
| 2 | Mono | armeabi-v7a | **20.23 MB** | 128 | `mmm.apk` |

> ⚠️ **这两组数据不可直接横向比较**：构建时勾选了"仅 64 位"，但 Mono 后端在 Unity 2021 下回退到了 32 位 armeabi-v7a。
> 所以差值中同时包含了「后端差异」和「架构差异」两个变量。若要单独验证后端影响，需固定架构再测一次。

### 2.2 Android IL2CPP 包体构成（11.04 MB）

**构建配置**：Unity 2021.3.14f1，Release，压缩格式 gzip（产物带 `.gz` 后缀）

| 文件 | 大小 | 占比 | 说明 |
|---|---|---|---|
| `Build/webtest.wasm.gz` | 3.72 MB | 75.0% | 引擎 + 用户代码（LLVM 编译产物） |
| `Build/webtest.data.gz` | 1.13 MB | 22.8% | 场景与资源 |
| `Build/webtest.framework.js.gz` | 0.07 MB | 1.4% | 运行时胶水代码 |
| `Build/webtest.loader.js` | 0.02 MB | 0.4% | 加载器 |
| `index.html` + `TemplateData/` | < 0.01 MB | ~0% | 模板页面与图标 |
| **合计** | **4.96 MB** | 100% | 共 16 个文件 |

### 2.3 WebGL 早期空工程构建（4.96 MB，`.gz` 模式）

> 这是分离的独立小工程 `webtest` 的首次 WebGL 构建，与 2.4/2.5 的项目产物（`webgl/` 35.90 MB、`minigame/` 8.83 MB）**不是同一个工程**，仅作参考。

**构建配置**：Unity 2021.3.14f1，Release，压缩格式 gzip（产物带 `.gz` 后缀）

| 文件 | 大小 | 占比 | 说明 |
|---|---|---|---|
| `Build/webtest.wasm.gz` | 3.72 MB | 75.0% | 引擎 + 用户代码（LLVM 编译产物） |
| `Build/webtest.data.gz` | 1.13 MB | 22.8% | 场景与资源 |
| `Build/webtest.framework.js.gz` | 0.07 MB | 1.4% | 运行时胶水代码 |
| `Build/webtest.loader.js` | 0.02 MB | 0.4% | 加载器 |
| `index.html` + `TemplateData/` | < 0.01 MB | ~0% | 模板页面与图标 |
| **合计** | **4.96 MB** | 100% | 共 16 个文件 |

**部署要点**

- 产物为 `.gz` 压缩包，`index.html` 的 config 直接引用 `.gz` 路径；
- 服务端必须返回 `Content-Encoding: gzip`，否则浏览器不解压导致白屏；
- 当前**未开启** Decompression Fallback（`config` 中无该字段），本地 `python -m http.server` 测试正常；
- 如需在配置不确定的服务器上部署，建议在 Player Settings → Publishing Settings 勾选 **Decompression Fallback** 后重新构建。

**验证记录**

| 资源 | HTTP 状态 | Content-Type |
|---|---|---|
| `/index.html` | 200 | text/html |
| `/Build/webtest.loader.js` | 200 | text/javascript |
| `/Build/webtest.data.gz` | 200 | application/gzip |
| `/Build/webtest.framework.js.gz` | 200 | application/gzip |

浏览器打开 `http://localhost:8080` 可正常渲染默认场景与 UI Text。

### 2.4 Web 端两类产物：`webgl/` 与 `minigame/`

Unity 的 Minigame 插件**一次构建会输出两个完全不同的目录**，用途不可混淆：

| | `webgl/` | `minigame/` |
|---|---|---|
| **用途** | 上传 CDN，浏览器 / WebView 访问 | 微信开发者工具**直接打开**运行的小程序项目 |
| **是否上 CDN** | ✅ 是 | ❌ 否（微信包由微信平台分发） |
| **入口** | `index.html` | `game.js` + `game.json` + `project.config.json` |
| **总体积** | **35.90 MB** | **8.83 MB** |
| **文件数** | 9 | 60 |
| **代码压缩** | ❌ 未压缩（`.wasm` 原始） | ✅ brotli 预压缩（`.br`） |
| **调试符号** | `Build/webgl.symbols.json` 4.43 MB | `webgl.wasm.symbols.unityweb` 4.43 MB |

**⚠️ 两者是同一工程的两套输出**：`webgl/` 面向"任意浏览器"，`minigame/` 面向"微信运行时"。混淆它们会导致优化方向完全错位——CDN 流量和微信主包限制是两套不同的约束。

---

### 2.5 `webgl/` 包体基线（35.90 MB，上 CDN）

**构建配置**：Unity 2021.3.14f1，WebGL 平台，空工程，构建耗时约 1 分钟

| 文件 | 体积 | 占比 | 状态 |
|---|---|---|---|
| `Build/webgl.wasm` | **14.79 MB** | 41.2% | ⚠️ 未压缩 |
| `Build/webgl.data` | **6.36 MB** | 17.7% | ⚠️ 未压缩 |
| `Build/webgl.symbols.json` | **4.43 MB** | 12.3% | ❌ **调试符号** |
| `535a156b...data.unityweb.bin.txt` | 6.35 MB | 17.7% | 根目录数据文件 |
| `Build/webgl.framework.js` | 0.44 MB | 1.2% | |
| `0f345f5...wasm.code.unityweb.wasm.br` | 3.49 MB | 9.7% | 已 brotli 压缩 |
| `index.html` + `Build/webgl.loader.js` | 0.03 MB | ~0% | |
| **合计** | **35.90 MB** | 100% | |

**核心问题：CDN 上是 35.90 MB，用户要下载这么多**

| 优化项 | 当前 | 优化后 | 手段 |
|---|---|---|---|
| 排除调试符号 | 35.90 MB | 31.47 MB（-12.3%） | 上传前移出 `*.symbols.json` |
| 开启服务端压缩 | `wasm` 14.79 MB → | 约 **3.5 MB** | CDN 开启 **brotli**（参照 `minigame/` 的 `.br` 效果：14.79 → 3.49，**压比 76%**） |

> **关键对比**：同一份 wasm 代码
> - `webgl/Build/webgl.wasm` = **14.79 MB**（原始）
> - `minigame/wasmcode/*.wasm.code.unityweb.wasm.br` = **3.49 MB**（brotli）
>
> 差距 **4.2 倍**。CDN 若不开 brotli，用户下载量会多出约 11 MB。

**处理建议**

1. **上传前排除符号文件**：`Build/webgl.symbols.json`
2. **CDN 开启 Brotli 预压缩 / 动态压缩**：微信云开发静态托管的压缩能力需确认
3. 或在 Unity 中调整 WebGL 的 Compression Format，让产物直接带压缩

---

### 2.6 `minigame/` 包体基线（8.83 MB，微信运行时）

**AppID**：`wx4d2f7d...`，projectname `aitt`，compileType `game`

| 顶级目录 | 体积 | 占比 | 文件数 | 说明 |
|---|---|---|---|---|
| （根文件） | 4.96 MB | 56.2% | 11 | 含 **`webgl.wasm.symbols.unityweb` 4.43 MB** |
| `wasmcode/` | 3.49 MB | 39.5% | 2 | wasm code 分包（`.br` 格式） |
| `unity-sdk/` | 0.37 MB | 4.2% | 43 | 微信 Unity SDK 适配层 |
| `images/` `workers/` `data-package/` | < 0.02 MB | ~0% | 4 | 图标、worker、data 分包 |
| **合计** | **8.83 MB** | 100% | 60 | |

**最大的那些文件**

| 文件 | 体积 | 是否必需 |
|---|---|---|
| `webgl.wasm.symbols.unityweb` | **4.43 MB** | ❌ **调试符号，运行不需要** |
| `wasmcode/*.wasm.code.unityweb.wasm.br` | 3.49 MB | ✅ 必需 |
| `webgl.wasm.framework.unityweb.js` | 0.45 MB | ✅ 必需 |
| `unity-sdk/sdk.js` | 0.17 MB | ✅ 必需 |

**⚠️ 调试符号占全包 50%**

| 状态 | 体积 |
|---|---|
| 原始产物 | 8.83 MB |
| 移除符号文件后 | **4.40 MB（-50%）** |

**处理方式**

- 微信开发者工具的「上传」会自动按 `project.config.json` 的 `packOptions.ignore` 过滤，但**本地目录体积仍会被误判**
- 若体积触发主包限制，优先排除 `*.symbols.unityweb`
- 符号文件按版本归档到云存储，排查线上崩溃时下载

**game.json 关键配置**

```json
{
  "deviceOrientation": "landscapeRight",
  "iOSHighPerformance": true,
  "subpackages": [
    { "name": "wasmcode", "root": "wasmcode/" },
    { "name": "data-package", "root": "data-package/" }
  ],
  "parallelPreloadSubpackages": [{ "name": "wasmcode" }],
  "plugins": { "UnityPlugin": { "version": "1.2.31", "provider": "wxe5a48f1ed5f544b7" } },
  "workers": "workers"
}
```

- `wasmcode` 已配置为**分包并并行预加载**，符合微信小游戏 4 MB 主包限制的做法
- 使用官方 `UnityPlugin` 插件（isolatedContext），说明构建走了正常的微信小游戏导出流程

---

### 2.7 四类产物体积横向对比

| 产物 | 平台 | 体积 | 最大构成项 | 占比 |
|---|---|---|---|---|
| APK | Android IL2CPP arm64 | 11.04 MB | `libunity.so` + `libil2cpp.so` = 9.20 MB | 83.4% |
| APK | Android Mono armv7 | 20.23 MB | `libunity.so` 8.66 MB | 42.8% |
| **`webgl/`** | WebGL → CDN | **35.90 MB** | `webgl.wasm` 14.79 MB（未压缩） | 41.2% |
| **`minigame/`** | 微信小游戏 | **8.83 MB** | 调试符号 4.43 MB（可去除） | 50.2% |

**观察 1**：`webgl/` 最大，因为它**未压缩**。同一份代码在 `minigame/` 里被 brotli 压到 3.49 MB，压比 76%。

**观察 2**：`minigame/` 体积虚高一半是调试符号。排除后 **4.40 MB**，反而比未压缩的 `webgl/` 小得多——因为微信侧做了预压缩，而 CDN 侧没有。

**观察 3**：两类 web 产物的真正可比口径是**传输体积**（用户实际下载）：`webgl/` 若开 brotli 约 8–9 MB，`minigame/` 主包约 4.4 MB。

**共性结论**：所有平台都呈现「引擎占绝对大头」的特征（75%–83%），业务资源在空工程中占比极低。包体优化的效果与项目资源量强相关，**报告优化成果时必须说明优化基数**。

| 条目 | 原始 | 压缩后 | 占比 | 说明 |
|---|---|---|---|---|
| `lib/arm64-v8a/libil2cpp.so` | 16.47 MB | 4.94 MB | 44.7% | C# AOT 编译产物 |
| `lib/arm64-v8a/libunity.so` | 10.35 MB | 4.26 MB | 38.6% | Unity 引擎本体 |
| `assets/bin/Data/Managed/Metadata/global-metadata.dat` | 2.58 MB | 0.77 MB | 7.0% | C# 元数据 |
| `assets/bin/Data/data.unity3d` | 0.49 MB | 0.49 MB | 4.4% | 场景与资源（几乎不可压缩） |
| `assets/bin/Data/unity default resources` | 4.03 MB | 0.39 MB | 3.5% | Unity 默认资源（压缩比 90%） |
| `classes.dex` | 0.12 MB | 0.05 MB | 0.5% | Java 桥接层 |
| **合计** | — | **11.04 MB** | 100% | |

**按类目汇总**

| 类目 | 大小 | 占比 |
|---|---|---|
| `lib/` 原生库 | 9.20 MB | 83.4% |
| `assets/bin/`（.NET 代码 + 资源） | 1.77 MB | 16.1% |
| `classes.dex` | 0.05 MB | 0.5% |
| 其他 | < 0.01 MB | ~0% |

**结论：空工程里 83% 是引擎本体，业务资源占比不足 1%。** 这解释了为什么"空包做不到显著瘦身"——优化的空间来自资源量，不是代码量。

### 2.8 Android Mono 包体构成（20.23 MB）

| 条目 | 原始 | 压缩后 | 说明 |
|---|---|---|---|
| `lib/armeabi-v7a/libunity.so` | 17.03 MB | 8.66 MB | 32 位引擎库，比 64 位版大近一倍 |
| `lib/armeabi-v7a/libmonobdwgc-2.0.so` | 5.75 MB | 1.86 MB | Mono 运行时（GC） |
| `lib/armeabi-v7a/libmono-native.so` | 1.11 MB | 0.38 MB | Mono 原生支持 |
| `assets/bin/Data/Managed/mscorlib.dll` | 4.41 MB | 1.51 MB | .NET 基础类库 |
| `assets/bin/Data/Managed/System.Xml.dll` | 3.01 MB | 1.01 MB | 空工程完全用不到，但 Mono 必须带 |
| `assets/bin/Data/Managed/System.dll` | 2.53 MB | 0.97 MB | |
| `assets/bin/Data/Managed/System.Data.dll` | 2.00 MB | 0.69 MB | 同上 |
| 80+ 个 `UnityEngine.*Module.dll` | — | 合计约 1.8 MB | 引擎托管层 |

**结论**：Mono 的额外体积来自两处——32 位 `libunity.so`（+4.4 MB）与整套 .NET 类库 + Mono 运行时（约 +6.5 MB）。

### 2.9 后端选型结论

| 维度 | IL2CPP | Mono |
|---|---|---|
| 空工程包体 | **11.04 MB** | 20.23 MB |
| 性能 | 高（AOT 原生码） | 低（JIT 解释，约慢 3–10×） |
| 64 位支持 | ✅ arm64-v8a | ❌ Unity 2021 回退 32 位 |
| Google Play 上架 | ✅ | ❌（需 64 位） |
| 增量编译速度 | 慢（数分钟） | 快（秒级） |
| **适用场景** | **发布 / 真机性能测试** | **日常开发迭代** |

> 行业惯例：开发期用 Mono 求快，出包前切 IL2CPP 真机验证。本项目采用此流程。

---
## 3. 出包耗时基线

### 3.1 关键前提：全量构建 vs 增量构建

Unity 的构建耗时**严重依赖缓存状态**，两者相差可达 14 倍，不能混用比较。

| 构建方式 | 典型耗时 | 说明 |
|---|---|---|
| **全量构建** | 40–50 秒 | 首次构建，或清理 `Library/` 后；真实耗时基线 |
| **增量构建** | 3–5 秒 | 未改动代码，复用 IL2CPP 缓存；仅反映"重新打包"开销 |

> ⚠️ **报告「出包耗时优化 X%」之前，必须统一构建条件**。本项目所有可比较的数据都标注了条件。

### 3.2 实测记录

| # | 日期 | 平台 | 耗时 | 构建条件 | 产物体积 | 备注 |
|---|---|---|---|---|---|---|
| 1 | 2026-11-01 | Android IL2CPP | ~5 min | 首次（含 Gradle 依赖下载） | 11.04 MB | 不可作基线 |
| 2 | 2026-11-01 | WebGL | 47.6 s | **全量** | 6.01 MB | 另一工程的首次构建 |
| 3 | 2026-11-02 | WebGL | 47.6 s | **全量** | 35.90 MB | 含调试符号 |
| 4 | 2026-11-02 | WebGL | 3.3 s | 增量 | 21.62 MB | 符号已剥离 |
| 5 | 2026-11-02 | 微信小游戏 | 4.4 s | 增量 | 4.40 MB | 含转换 + 符号剥离 |

> **精度说明**：以上为人工/脚本记录的实际耗时。`Builds/build-history.md` 由 `BuildScript.cs` 自动生成，精度到 0.1 秒。
> **可比较的数据只有同类同条件**：全量 vs 全量、增量 vs 增量。

### 3.3 平台间耗时对比

| 对比 | 差异 | 原因 |
|---|---|---|
| WebGL 全量 47.6s vs 增量 3.3s | 约 14× | 增量跳过 IL2CPP → wasm 编译 |
| Android vs WebGL（均为全量） | Android 更慢 | Gradle 编译、Java 打包、签名、NDK 交叉编译 |
| 微信小游戏 vs WebGL | 相近 | 底层同为 wasm，微信插件额外做 brotli 压缩 |

---

## 4. 自动化构建工具

`Assets/Editor/BuildScript.cs`（786 行）提供命令行与编辑器菜单两种出包入口。

### 能力

| 功能 | 说明 |
|---|---|
| 三端出包 | `BuildAndroid` / `BuildWebGL` / `BuildMiniGame` / `BuildAll` |
| 耗时记录 | 每个平台单独计时，写入 `Builds/build-history.md`（UTF-8） |
| 符号剥离 | 自动删除调试符号，可按平台分别开关 |
| 产物发现 | 读微信插件 `MiniGameConfig.asset` 的 `DST` 字段定位导出目录 |
| CI 就绪 | 失败时以非 0 退出码结束 |

### 命令行用法

```powershell
$unity = "F:\HubUnity2017-2019\2021.3.14f1\Editor\Unity.exe"
$proj  = "G:\U3D_workface\A_2021_3.14f1\My project1102"

& $unity -quit -batchmode -nographics -projectPath $proj `
         -executeMethod BuildScript.BuildAll `
         -logFile "Logs\build-all.log"
```

### 符号剥离策略

| 产物 | 是否剥离 | 依据 |
|---|---|---|
| Android | — | 无符号文件 |
| **WebGL（CDN）** | ✅ 剥离 | `index.html` 有 `symbolsUrl` 引用，但**实测删除后页面正常加载、无 404**，确认为弱依赖 |
| **微信小游戏** | ✅ 剥离 | 全部 JS 文件中无任何符号引用，删除安全 |

### 开发过程中修复的问题

| # | 问题 | 原因 | 修复 |
|---|---|---|---|
| 1 | 自定义 `BuildResult` 与 Unity 类型冲突 | 命名空间重名 | 改名 `BuildOutcome`，Unity 枚举全限定 |
| 2 | CS8179：`ValueTuple` 未定义 | Unity 2021 的 .NET Framework 4.x profile 不含 `System.ValueTuple` | 元组改为显式结构体 |
| 3 | 符号清理在错误目录空转 | 插件把产物写到 `DST/`，而非 `Builds/` | 从插件配置读 `DST` 定位 |
| 4 | 找不到产物目录（差 3 层） | 工程与产物不在同一父目录下 | 读取配置 + 逐层向上探测 |
| 5 | 两目录符号策略一刀切 | webgl 有引用、minigame 无 | 增加 `StripWebGLSymbols` 独立开关 |
| 6 | 体积记录比实际大 | 先算体积再删符号 | 调整为先删后算 |
| 7 | 日志中文乱码 | Unity 默认按 GBK 写文件 | `File.AppendAllText` 指定 UTF-8 |
| 8 | 改路径后清理失效 | 仍按目录名 `webgl` 猜测 | 新增 `WebGLOutputDir` 显式配置，已知不猜 |

---

## 5. 待补测量项

- [x] 四类产物体积基线
- [x] WebGL / 微信小游戏 符号剥离效果
- [ ] 全量构建耗时精确复测（同一工程、同一条件、连测 2 次）
- [ ] 首启时间（`adb shell am start -W`）
- [ ] 峰值内存（Memory Profiler，低端机）
- [ ] WebGL 首屏加载时间（含 wasm 下载与解压）
- [ ] CDN 开启 brotli 后的实际传输体积

---

*最后更新：2026-11-02*