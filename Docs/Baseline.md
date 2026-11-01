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

## 2. Android 包体基线

### 2.1 首轮对比：IL2CPP vs Mono（空工程）

**工程内容**：新建 3D 工程，1 个默认场景 + 1 个 UI Text 文字。

| # | 后端 | 架构 | 包体 | 条目数 | 产物 |
|---|---|---|---|---|---|
| 1 | IL2CPP | arm64-v8a | **11.04 MB** | 23 | `ppp.apk` |
| 2 | Mono | armeabi-v7a | **20.23 MB** | 128 | `mmm.apk` |

> ⚠️ **这两组数据不可直接横向比较**：构建时勾选了"仅 64 位"，但 Mono 后端在 Unity 2021 下回退到了 32 位 armeabi-v7a。
> 所以差值中同时包含了「后端差异」和「架构差异」两个变量。若要单独验证后端影响，需固定架构再测一次。

### 2.2 WebGL 包体基线（4.96 MB）

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

### 2.3 三端体积横向对比

| 平台 | 后端 | 架构 | 体积 | 最大构成项 | 占比 |
|---|---|---|---|---|---|
| Android | IL2CPP | arm64-v8a | 11.04 MB | `libunity.so` + `libil2cpp.so` = 9.20 MB | 83.4% |
| Android | Mono | armeabi-v7a | 20.23 MB | `libunity.so` 8.66 MB | 42.8% |
| **WebGL** | IL2CPP → wasm | — | **4.96 MB** | `webtest.wasm.gz` 3.72 MB | 75.0% |

**观察**：WebGL 包体明显小于 Android。原因是 wasm 是面向虚拟机的紧凑字节码，不含 ARM 指令集对齐填充与平台 ABI 冗余；而原生库需要携带完整的机器码与符号信息。

**共性结论**：三端都呈现「引擎占绝对大头」的特征（75%–83%），业务资源在空工程中占比极低。这说明包体优化的效果与项目资源量强相关，**报告优化成果时必须说明优化基数**。

### 2.4 IL2CPP 包体构成（11.04 MB）

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

### 2.3 Mono 包体构成（20.23 MB）

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

### 2.4 后端选型结论

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

首次构建 Gradle 需下载依赖，耗时不可作为完整基线；从第二次构建起数值才有参考意义。

| # | 日期 | 平台 | 后端 | 架构 | 出包耗时 | 备注 |
|---|---|---|---|---|---|---|
| 1 | 2026-11-01 | Android | IL2CPP | arm64-v8a | ~5 min | 含 Gradle 依赖下载，**非纯构建耗时** |
| 2 | 2026-11-01 | Android | Mono | armeabi-v7a | — | 未单独计时 |
| 3 | 2026-11-01 | WebGL | IL2CPP → wasm | — | **~1 min** | 空工程，首次构建 |

> **计时口径**：从点击 Build 到产物落盘的墙钟时间，构建前不清理 Library。
> **精度说明**：当前为人工目测的近似值（"约 5 分钟"/"约 1 分钟"），仅用于量级判断。
> 后续做「出包耗时缩短 X%」这类结论前，必须改用可复现的计时方式（记录精确到秒的时间戳，同配置连测 2 次取第二次）。

**当前观察**

| 对比 | 差异 | 原因 |
|---|---|---|
| WebGL ≈ 1 min vs Android ≈ 5 min | 约 5× | Android 需 Gradle 编译、Java 打包、签名、IL2CPP 交叉编译；WebGL 只需 IL2CPP → wasm 与资源烘焙 |
| 空工程 vs 实际项目 | 差距会显著扩大 | 空工程无资源导入、无图集打包、无 Addressables 构建；真实项目耗时主要来自资源处理 |

> ⚠️ 这组数据**不能直接引用为项目级基线**。空工程的构建耗时几乎全部是引擎编译开销，不代表实际业务项目的耗时分布。

---

## 4. 后续优化记录模板

每次优化追加一张表：

| 优化项 | 优化前 | 优化后 | 变化 | 生效条件 |
|---|---|---|---|---|
| 示例：图集二次合并 | — | — | — | — |

**预期可优化的位置（按贡献排序）**

| 目标 | 当前 | 手段 | 预期 |
|---|---|---|---|
| `unity default resources` | 0.39 MB | 剔除未使用默认 UI/字体资源 | 小 |
| `global-metadata.dat` | 0.77 MB | Strip Engine Code + 精简命名空间 | 小 |
| `data.unity3d` | 0.49 MB | Addressables 拆包，首包只留启动资源 | 中（项目变大后显著） |
| 重复纹理 / 未压缩音频 | — | 统一 ASTC/ETC2、音频按场景转码 | 大 |

---

## 5. 待补测量项

- [x] WebGL 构建包体（4.96 MB）
- [x] 三端体积对比（Android 11.04 / 20.23 MB，WebGL 4.96 MB）
- [ ] 出包耗时精确测量（当前仅目测近似：Android ~5min、WebGL ~1min）
- [ ] Mono 单独计时
- [ ] 首启时间（冷启动，`adb shell am start -W`）
- [ ] 峰值内存（Memory Profiler，低端机）
- [ ] 固定架构下的纯后端对比（IL2CPP armv7 vs Mono armv7）
- [ ] WebGL 首屏加载时间（含 wasm 下载 / 解压）
- [ ] 微信小游戏包体与首包 4MB 限制验证

---

*最后更新：2026-11-01*
