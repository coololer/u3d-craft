# 踩坑记录

> 真实遇到的问题、完整现象与解决过程。面试时"你遇到过什么难点"直接从这里取。
>
> 每条格式：现象 → 完整报错 → 根因 → 解决 → 复盘。

---

## 目录

- [P-001 Unity 2021.3.14f1 打 APK 失败：Android SDK 许可证未接受](#p-001)
- [P-002 包体分析：空包 11MB，引擎占 83%](#p-002)
- [P-003 Mono 打包比 IL2CPP 还大](#p-003)

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

*最后更新：2026-11-01*
