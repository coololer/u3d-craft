// =============================================================================
// BuildScript.cs —— 命令行一键三端出包
// -----------------------------------------------------------------------------
// 放置位置：Assets/Editor/BuildScript.cs
//
// 用途：
//   为 Android / WebGL / 微信小游戏 提供可脚本化的构建入口，
//   解决「每次出包靠人肉点菜单、耗时长且易错」的问题。
//
// 命令行用法（Unity 关闭状态下执行）：
//   "C:\Program Files\Unity\Hub\Editor\2021.3.14f1\Editor\Unity.exe" ^
//       -quit -batchmode -nographics ^
//       -projectPath "G:\U3D_workface\A_2021_3.14f1\My project1102" ^
//       -executeMethod BuildScript.BuildAndroid ^
//       -logFile "Logs\build-android.log"
//
//   -executeMethod 可用值：
//       BuildScript.BuildAndroid      打 APK
//       BuildScript.BuildWebGL        打 WebGL
//       BuildScript.BuildMiniGame     打微信小游戏（含转换）
//       BuildScript.BuildAll          三端全打
//
// 设计要点：
//   1. 构建前后记录精确时间戳，输出耗时到控制台与日志文件
//   2. 自动排除调试符号文件（*.symbols.*），避免污染产物，见 Docs/Pitfalls.md P-008
//   3. 失败时以非 0 退出码结束，方便 CI 捕获
//   4. 产物按平台分目录，文件名带版本号与时间戳，便于回溯
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class BuildScript
{
    // ---------------------------------------------------------------------
    // 配置区：按需修改
    // ---------------------------------------------------------------------

    /// <summary>产物根目录（相对项目根目录）</summary>
    private const string OutputRoot = "Builds";

    /// <summary>
    /// WebGL 输出目录。留空则用 Builds/WebGL；需要自定义时直接改这里，例如：
    ///   private const string WebGLOutputDir = @"G:\U3D_workface\H5\web";
    /// 显式配置后，后续的产物统计与符号清理都直接以此路径为准，不再自动发现。
    /// </summary>
    private const string WebGLOutputDir = "";

    /// <summary>是否在构建后自动排除调试符号文件</summary>
    private const bool StripSymbols = true;

    /// <summary>
    /// 是否删除 WebGL（CDN 产物）的符号文件。
    ///
    /// 背景：webgl/index.html 的 config 里有
    ///   symbolsUrl: buildUrl + "/webgl.symbols.json"
    /// 表面上加载器引用了它，但实测（2026-11-02）把该文件改名后：
    ///   - 页面正常加载，场景正常渲染
    ///   - Console 无 404、无 symbols 相关报错
    /// 说明加载器对 symbols 是「有则加载、无则忽略」的弱依赖，可安全删除。
    ///
    /// 因此默认改为 true，CDN 产物可省 4.43 MB（约 12%）。
    /// 若日后升级 Unity 或更换加载器后出现加载异常，先改回 false 验证。
    /// 微信小游戏（minigame/）已确认无任何符号引用，不受此开关影响。
    /// </summary>
    private const bool StripWebGLSymbols = true;

    /// <summary>
    /// 微信插件配置文件路径（相对项目根目录）。
    /// 里面的 DST 字段就是导出根目录，产物落在 DST/webgl 与 DST/minigame。
    /// </summary>
    private const string WxMiniGameConfig = "Assets/WX-WASM-SDK-V2/Editor/MiniGameConfig.asset";

    /// <summary>Android 是否打 Development 包（调试用，体积略大但可 attach profiler）</summary>
    private static bool AndroidDevelopment => false;

    /// <summary>Android 架构，null 表示用 ProjectSettings 里的设置</summary>
    private static readonly AndroidArchitecture? AndroidArch = null;

    // ---------------------------------------------------------------------
    // 入口方法（命令行调用）
    // ---------------------------------------------------------------------

    [MenuItem("Tools/Build/Build Android")]
    public static void BuildAndroid()
    {
        RunBuild("Android", () => DoBuildAndroid(), exitOnFail: true);
    }

    [MenuItem("Tools/Build/Build WebGL")]
    public static void BuildWebGL()
    {
        RunBuild("WebGL", () => DoBuildWebGL(), exitOnFail: true);
    }

    [MenuItem("Tools/Build/Build MiniGame")]
    public static void BuildMiniGame()
    {
        RunBuild("MiniGame", () => DoBuildMiniGame(), exitOnFail: true);
    }

    [MenuItem("Tools/Build/Build All (三端)")]
    public static void BuildAll()
    {
        bool ok = true;
        ok &= RunBuild("Android", () => DoBuildAndroid(), exitOnFail: false);
        ok &= RunBuild("WebGL", () => DoBuildWebGL(), exitOnFail: false);
        ok &= RunBuild("MiniGame", () => DoBuildMiniGame(), exitOnFail: false);

        Debug.Log(ok ? "[BuildAll] ✅ 三端全部构建成功" : "[BuildAll] ❌ 存在构建失败，请检查上方日志");
        if (!ok) ExitWithCode(1);
    }

    // ---------------------------------------------------------------------
    // 各平台实现
    // ---------------------------------------------------------------------

    private static BuildOutcome DoBuildAndroid()
    {
        // 确保安卓平台是当前激活平台
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("[Android] 切换构建平台到 Android ...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[Android] 切换平台失败");
                return BuildOutcome.Failed("切换 Android 平台失败");
            }
        }

        var options = new BuildPlayerOptions
        {
            scenes = GetEnabledScenes(),
            locationPathName = GetOutputPath("Android", "app.apk"),
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = AndroidDevelopment ? BuildOptions.Development : BuildOptions.None,
        };

        if (AndroidArch.HasValue)
        {
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.targetArchitectures = AndroidArch.Value;
        }

        Debug.Log($"[Android] 目标架构: {PlayerSettings.Android.targetArchitectures}");
        Debug.Log($"[Android] ScriptingBackend: {PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android)}");
        Debug.Log($"[Android] 产物: {options.locationPathName}");

        var report = BuildPipeline.BuildPlayer(options);
        return EvaluateReport(report, "Android", options.locationPathName, BuildTarget.Android);
    }

    private static BuildOutcome DoBuildWebGL()
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            Debug.Log("[WebGL] 切换构建平台到 WebGL ...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                Debug.LogError("[WebGL] 切换平台失败");
                return BuildOutcome.Failed("切换 WebGL 平台失败");
            }
        }

        // 记住旧的输出目录设置，构建后还原，避免污染工程配置
        var oldOutput = EditorUserBuildSettings.GetBuildLocation(BuildTarget.WebGL);

        // 输出目录优先级：
        //   1) 本文件顶部 WebGLOutputDir 显式配置（推荐，改这里即可）
        //   2) 工程内的 Builds/WebGL
        // 显式配置 > 自动发现：构建时我们已经知道产物写到哪里，
        // 就不该再去猜目录名（曾因猜 "webgl" 而漏掉 "web" 这个实际路径）。
        string outDir = string.IsNullOrEmpty(WebGLOutputDir)
            ? GetOutputPath("WebGL", "")
            : WebGLOutputDir;
        outDir = outDir.TrimEnd('/', '\\');
        Directory.CreateDirectory(outDir);
        EditorUserBuildSettings.SetBuildLocation(BuildTarget.WebGL, outDir);

        Debug.Log($"[WebGL] 输出目录（显式指定）: {outDir}");

        try
        {
            var options = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            return EvaluateReport(report, "WebGL", outDir, BuildTarget.WebGL);
        }
        finally
        {
            if (!string.IsNullOrEmpty(oldOutput))
                EditorUserBuildSettings.SetBuildLocation(BuildTarget.WebGL, oldOutput);
        }
    }
    /// <summary>
    /// 微信小游戏：走 SDK 的 WXConvertCore.DoExport
    /// 它会先把当前工程打成 WebGL，再转换成小游戏目录。
    ///
    /// 注意：DoExport 内部会「重写」WebGL 输出位置到插件自己的导出目录，
    /// 不会写我们设定的 Builds/WebGL。因此本文档所有产物路径都通过自动发现获取，
    /// 不依赖固定路径，见 FindWebGLOutputDir / FindMiniGameDir。
    /// </summary>
    private static BuildOutcome DoBuildMiniGame()
    {
        // 反射调用，避免工程未安装 WX 插件时编译报错
        var t = Type.GetType("WeChatWASM.WXConvertCore, Assembly-CSharp-Editor")
                ?? AppDomain.CurrentDomain.GetAssemblies()
                       .Select(a => a.GetType("WeChatWASM.WXConvertCore"))
                       .FirstOrDefault(x => x != null);

        if (t == null)
        {
            // 常见于插件被删或命名空间变化
            Debug.LogError("[MiniGame] 未找到 WeChatWASM.WXConvertCore，请确认已安装 WX-WASM-SDK-V2");
            return BuildOutcome.Failed("未找到微信小游戏 SDK");
        }

        // 必须先切到 WebGL 平台：DoExport 只有在已是 WebGL 时才真正构建，
        // 否则会直接复用旧产物，导致耗时异常短、产物陈旧（难察觉）。
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
        {
            Debug.Log("[MiniGame] 切换构建平台到 WebGL（DoExport 的前置条件）...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                return BuildOutcome.Failed("切换 WebGL 平台失败，DoExport 无法正确构建");
        }

        // 记录转换前 minigame 目录的时间戳，用于判断产物是否真的被更新
        string beforeDir = FindMiniGameDir();
        var beforeTime = beforeDir != null ? GetNewestWriteTime(beforeDir) : DateTime.MinValue;

        Debug.Log("[MiniGame] 开始调用 WXConvertCore.DoExport(buildWebGL: true) ...");
        var sw = Stopwatch.StartNew();
        try
        {
            var method = t.GetMethod("DoExport",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null)
            {
                Debug.LogError("[MiniGame] 未找到 DoExport 方法");
                return BuildOutcome.Failed("未找到 DoExport");
            }

            // DoExport(bool buildWebGL = true)：true 表示先打 WebGL 再转换
            object ret = method.Invoke(null, new object[] { true });
            sw.Stop();
            string retText = ret?.ToString() ?? "null";

            // SUCCEED = 0；其余为失败
            bool ok = retText == "SUCCEED" || retText == "0";
            Debug.Log($"[MiniGame] DoExport 返回: {retText}，耗时 {FormatDuration(sw.Elapsed)}");

            if (!ok) return BuildOutcome.Failed($"DoExport 返回 {retText}");

            // 产物自动发现 + 新鲜度校验
            string mgDir = FindMiniGameDir();
            if (mgDir == null)
            {
                Debug.LogError("[MiniGame] 转换成功但未找到 game.json，无法确定产物目录");
                return BuildOutcome.Failed("未找到小游戏产物目录（game.json）");
            }

            var afterTime = GetNewestWriteTime(mgDir);
            if (afterTime <= beforeTime)
            {
                Debug.LogWarning($"[MiniGame] ⚠ 产物未更新（最新写入 {afterTime:HH:mm:ss} 不晚于构建前），" +
                                 "可能 DoExport 跳过了实际构建");
            }

            // 先删符号再算体积，否则记录的是「删除前」的旧值（曾把 4.40MB 记成 8.83MB）
            long stripped = 0;
            if (StripSymbols) stripped = StripSymbolFiles(mgDir);

            long size = GetDirSize(mgDir);

            Debug.Log($"[MiniGame] 产物目录: {mgDir}（{afterTime:HH:mm:ss} 更新）");
            if (stripped > 0)
                Debug.Log($"[MiniGame] 符号已剥离 {FormatSize(stripped)}，最终体积 {FormatSize(size)}");

            return BuildOutcome.Ok("MiniGame", mgDir, size);
        }
        catch (Exception e)
        {
            Debug.LogError($"[MiniGame] 转换异常: {e}");
            return BuildOutcome.Failed(e.Message);
        }
    }

    /// <summary>取目录内最新文件写入时间，用于判断产物是否真的更新</summary>
    private static DateTime GetNewestWriteTime(string dir)
    {
        if (!Directory.Exists(dir)) return DateTime.MinValue;
        DateTime newest = DateTime.MinValue;
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            var t = File.GetLastWriteTime(f);
            if (t > newest) newest = t;
        }
        return newest;
    }

    // ---------------------------------------------------------------------
    // 通用工具
    // ---------------------------------------------------------------------

    /// <summary>统一包裹：计时、日志、错误处理</summary>
    private static bool RunBuild(string label, Func<BuildOutcome> action, bool exitOnFail)
    {
        var sw = Stopwatch.StartNew();
        Debug.Log($"[{label}] ========== 构建开始 ==========");

        BuildOutcome result;
        try
        {
            result = action();
        }
        catch (Exception e)
        {
            Debug.LogError($"[{label}] 构建异常: {e}");
            result = BuildOutcome.Failed(e.Message);
        }

        sw.Stop();
        TheBuildLog.Add(label, sw.Elapsed.TotalSeconds, result);

        if (result.Success)
        {
            Debug.Log($"[{label}] ✅ 构建成功 | 耗时 {FormatDuration(sw.Elapsed)} | 产物 {result.Path} | 体积 {FormatSize(result.SizeBytes)}");
            WriteRunLog();
        }
        else
        {
            Debug.LogError($"[{label}] ❌ 构建失败 | 耗时 {FormatDuration(sw.Elapsed)} | 原因: {result.Message}");
            WriteRunLog();
        }

        if (!result.Success && exitOnFail) ExitWithCode(1);
        return result.Success;
    }

    private static BuildOutcome EvaluateReport(BuildReport report, string label, string path, BuildTarget target)
    {
        if (report == null)
            return BuildOutcome.Failed("BuildPipeline 返回 null");

        var summary = report.summary;
        if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            string err = $"{summary.result}，错误 {summary.totalErrors} 个";
            return BuildOutcome.Failed(err);
        }

        // 产物路径判定策略：
        //   已知路径优先 —— 构建时传给 BuildPipeline 的 path 就是产物落点，
        //   只有在该路径确实不存在产物时（例如微信插件覆盖了输出位置），
        //   才退回到自动发现，并用显眼的警告提示。
        string actualPath = path;

        bool pathHasOutput;
        if (target == BuildTarget.WebGL)
            pathHasOutput = Directory.Exists(path) && File.Exists(Path.Combine(path, "index.html"));
        else if (target == BuildTarget.Android)
            pathHasOutput = File.Exists(path);
        else
            pathHasOutput = Directory.Exists(path);

        if (!pathHasOutput)
        {
            // 预期的输出位置没有产物，才去自动发现
            var discovered = FindWebGLOutputDir();
            if (discovered != null)
            {
                Debug.LogWarning($"[{label}] 预期位置 {path} 未找到产物，自动发现到 {discovered}。" +
                                 "如果这不是你想要的，请在 WebGLOutputDir 中显式指定输出目录。");
                actualPath = discovered;
            }
        }

        long size = 0;
        if (target == BuildTarget.Android && File.Exists(actualPath))
            size = new FileInfo(actualPath).Length;
        else if (Directory.Exists(actualPath))
            size = GetDirSize(actualPath);

        // 排除调试符号：按「实际产物目录」执行，否则会在错误路径上空转。
        // 注意：WebGL（CDN 产物）与 微信小游戏 的策略不同——
        //   webgl/index.html 里有 symbolsUrl 引用，删除有风险 → 受 StripWebGLSymbols 控制
        //   minigame 无任何引用，可安全删除
        if (StripSymbols && Directory.Exists(actualPath))
        {
            bool allowStrip = target != BuildTarget.WebGL || StripWebGLSymbols;
            if (allowStrip)
            {
                long saved = StripSymbolFiles(actualPath);
                size -= saved;
            }
            else
            {
                ReportSymbolsKept(actualPath);
            }
        }

        return BuildOutcome.Ok(label, actualPath, size);
    }

    /// <summary>
    /// 不删除 WebGL 符号时，报告其存在与引用情况，提醒手工处理。
    /// </summary>
    private static void ReportSymbolsKept(string dir)
    {
        var syms = Directory.GetFiles(dir, "*.symbols.*", SearchOption.AllDirectories);
        if (syms.Length == 0) return;

        long total = syms.Sum(f => new FileInfo(f).Length);
        Debug.Log($"[Strip] 保留 WebGL 符号文件 {FormatSize(total)}（StripWebGLSymbols=false）");
        Debug.Log("[Strip] 上传 CDN 前可按需排除；若要删除，先确认 index.html 的 symbolsUrl 不被加载器强制要求");

        string indexHtml = Path.Combine(dir, "index.html");
        if (File.Exists(indexHtml))
        {
            try
            {
                string content = File.ReadAllText(indexHtml);
                string symName = Path.GetFileName(syms[0]);
                if (content.Contains("symbolsUrl") && content.Contains(symName))
                    Debug.LogWarning($"[Strip] ⚠ index.html 引用了 {symName}，直接删除可能导致加载异常，建议先实测");
            }
            catch { /* 忽略 */ }
        }
    }

    private static bool PathEquals(string a, string b)
    {
        return string.Equals(
            Path.GetFullPath(a).TrimEnd('/', '\\'),
            Path.GetFullPath(b).TrimEnd('/', '\\'),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 删除产物目录中的调试符号文件，返回释放的字节数。
    /// 见 Docs/Pitfalls.md P-008：符号文件占包体可达 50%，运行时完全不需要。
    /// </summary>
    private static long StripSymbolFiles(string dir)
    {
        var patterns = new[] { "*.symbols.*", "*.pdb", "*.debug", "*.unityweb.symbols*" };
        long removed = 0;
        int count = 0;

        foreach (var pattern in patterns)
        {
            foreach (var f in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
            {
                try
                {
                    removed += new FileInfo(f).Length;
                    File.Delete(f);
                    count++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Strip] 无法删除 {f}: {e.Message}");
                }
            }
        }

        if (count > 0)
            Debug.Log($"[Strip] 已移除 {count} 个调试符号文件，节省 {FormatSize(removed)}（目录 {dir}）");
        else
            Debug.Log($"[Strip] 未发现调试符号文件（目录 {dir}）");

        return removed;
    }

    private static string[] GetEnabledScenes()
    {
        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogWarning("[Build] Build Settings 中没有启用场景，构建产物将无法运行");
        }
        return scenes;
    }

    private static string GetOutputPath(string platform, string fileName)
    {
        string dir = Path.Combine(OutputRoot, platform);
        Directory.CreateDirectory(dir);

        if (string.IsNullOrEmpty(fileName))
            return Path.GetFullPath(dir);

        string stamp = DateTime.Now.ToString("MMdd-HHmm");
        string version = string.IsNullOrEmpty(PlayerSettings.bundleVersion) ? "0" : PlayerSettings.bundleVersion;
        string ext = Path.GetExtension(fileName);
        string baseName = Path.GetFileNameWithoutExtension(fileName);

        return Path.GetFullPath(Path.Combine(dir, $"{baseName}-v{version}-{stamp}{ext}"));
    }

    /// <summary>
    /// 读取微信插件配置文件中的 DST（导出根目录）。
    /// MiniGameConfig.asset 里的 DST 字段即插件导出根路径，
    /// 产物落在 DST/webgl 与 DST/minigame。
    /// 这是最可靠的定位方式——不需要猜测目录层级。
    /// </summary>
    private static string ReadWxExportRoot()
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string cfgPath = Path.Combine(projectRoot, WxMiniGameConfig.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(cfgPath))
            {
                Debug.LogWarning($"[Path] 未找到微信插件配置：{cfgPath}");
                return null;
            }

            // 逐行找 "DST: <path>"，避免引入 YAML 解析依赖
            foreach (var line in File.ReadAllLines(cfgPath, Encoding.UTF8))
            {
                string t = line.Trim();
                if (!t.StartsWith("DST:", StringComparison.Ordinal)) continue;

                string value = t.Substring(4).Trim();
                if (string.IsNullOrEmpty(value)) continue;

                // 配置文件里用正斜杠，统一成系统分隔符
                value = value.Replace('/', Path.DirectorySeparatorChar)
                             .Replace('\\', Path.DirectorySeparatorChar);

                if (Directory.Exists(value))
                {
                    Debug.Log($"[Path] 从插件配置读到导出根目录 DST = {value}");
                    return value;
                }

                Debug.LogWarning($"[Path] 插件配置中的 DST 路径不存在：{value}");
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Path] 读取插件配置失败：{e.Message}");
        }
        return null;
    }

    /// <summary>
    /// 查找微信小游戏产物目录（含 game.json）。
    /// 优先级：插件配置 DST/minigame → 约定目录 → 多层目录探测。
    /// </summary>
    private static string FindMiniGameDir()
    {
        var candidates = new List<string>();

        // 1) 最高优先级：插件配置里声明的导出根目录
        string dst = ReadWxExportRoot();
        if (!string.IsNullOrEmpty(dst))
        {
            candidates.Add(Path.Combine(dst, "minigame"));
            candidates.Add(dst);
        }

        // 2) 我们约定的输出目录
        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        candidates.Add(Path.Combine(projectRoot, OutputRoot, "MiniGame"));

        // 3) 逐层向上探测（工程可能嵌在 H5\<版本>\<工程> 这种结构里）
        try
        {
            var cur = new DirectoryInfo(projectRoot);
            for (int depth = 0; depth < 4 && cur != null; depth++)
            {
                candidates.Add(Path.Combine(cur.FullName, "minigame"));
                try
                {
                    foreach (var d in Directory.GetDirectories(cur.FullName))
                        candidates.Add(Path.Combine(d, "minigame"));
                }
                catch { /* 无权限跳过 */ }
                cur = cur.Parent;
            }
        }
        catch { /* 忽略 */ }

        // 命中判定：优先要求 game.json + project.config.json 齐全
        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(Path.Combine(c, "game.json")) &&
                    File.Exists(Path.Combine(c, "project.config.json")))
                    return c;
            }
            catch { /* 路径非法跳过 */ }
        }

        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(Path.Combine(c, "game.json"))) return c;
            }
            catch { /* 忽略 */ }
        }
        return null;
    }

    /// <summary>
    /// 查找 WebGL 产物目录（含 index.html + Build/）。
    /// 优先级同 FindMiniGameDir：插件配置 DST 优先。
    /// </summary>
    private static string FindWebGLOutputDir()
    {
        var candidates = new List<string>();

        string dst = ReadWxExportRoot();
        if (!string.IsNullOrEmpty(dst))
        {
            candidates.Add(Path.Combine(dst, "webgl"));
            candidates.Add(dst);
        }

        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        candidates.Add(Path.Combine(projectRoot, OutputRoot, "WebGL"));

        try
        {
            var cur = new DirectoryInfo(projectRoot);
            for (int depth = 0; depth < 4 && cur != null; depth++)
            {
                candidates.Add(Path.Combine(cur.FullName, "webgl"));
                try
                {
                    foreach (var d in Directory.GetDirectories(cur.FullName))
                        candidates.Add(Path.Combine(d, "webgl"));
                }
                catch { /* 忽略 */ }
                cur = cur.Parent;
            }
        }
        catch { /* 忽略 */ }

        foreach (var c in candidates)
        {
            try
            {
                if (File.Exists(Path.Combine(c, "index.html")) &&
                    Directory.Exists(Path.Combine(c, "Build")))
                    return c;
            }
            catch { /* 忽略 */ }
        }
        return null;
    }

    private static long GetDirSize(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                        .Sum(f => new FileInfo(f).Length);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB" };
        int i = (int)Math.Floor(Math.Log(bytes, 1024));
        i = Math.Min(i, units.Length - 1);
        return $"{bytes / Math.Pow(1024, i):F2} {units[i]}";
    }

    private static string FormatDuration(TimeSpan ts)
    {
        return ts.TotalMinutes >= 1
            ? $"{(int)ts.TotalMinutes} 分 {ts.Seconds} 秒"
            : $"{ts.TotalSeconds:F1} 秒";
    }

    private static void ExitWithCode(int code)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(code);
        else
            Debug.LogError("[Build] 构建失败（编辑器模式下不自动退出）");
    }

    // ---------------------------------------------------------------------
    // 运行日志
    // ---------------------------------------------------------------------

    private static class TheBuildLog
    {
        // 不用元组：Unity 2021.3 的 .NET Framework 4.x profile 默认不带
        // System.ValueTuple，用元组会导致 CS8179 编译失败。
        // Entry 必须 public：All 是 public 属性，返回类型的可访问性不能更低。
        public struct Entry
        {
            public string Label;
            public double Seconds;
            public BuildOutcome Result;
        }

        private static readonly List<Entry> Entries = new List<Entry>();

        public static void Add(string label, double sec, BuildOutcome r)
        {
            Entries.Add(new Entry { Label = label, Seconds = sec, Result = r });
        }

        public static IReadOnlyList<Entry> All => Entries;
    }

    private static void WriteRunLog()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 构建记录");
            sb.AppendLine();
            sb.AppendLine($"- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- Unity：{Application.unityVersion}");
            sb.AppendLine($"- 目标：{EditorUserBuildSettings.activeBuildTarget}");
            sb.AppendLine($"- 异常设置：{PlayerSettings.WebGL.exceptionSupport}");
            sb.AppendLine();
            sb.AppendLine("| 平台 | 结果 | 耗时 | 产物体积 | 路径 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var e in TheBuildLog.All)
            {
                sb.AppendLine($"| {e.Label} | {(e.Result.Success ? "成功" : "失败")} | " +
                              $"{FormatDuration(TimeSpan.FromSeconds(e.Seconds))} | " +
                              $"{FormatSize(e.Result.SizeBytes)} | {e.Result.Path} |");
            }

            Directory.CreateDirectory(OutputRoot);
            string file = Path.Combine(OutputRoot, "build-history.md");
            // 必须显式指定 UTF-8：Unity 在中文 Windows 上默认按系统编码（GBK）写文件，
            // 用 UTF-8 打开会变乱码（曾实际踩到）。
            File.AppendAllText(file, sb.ToString() + Environment.NewLine, new UTF8Encoding(false));
            Debug.Log($"[Build] 记录已写入 {file}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Build] 写日志失败: {e.Message}");
        }
    }

    // ---------------------------------------------------------------------
    // 构建结果
    // ---------------------------------------------------------------------

    public struct BuildOutcome
    {
        public bool Success;
        public string Message;
        public string Path;
        public long SizeBytes;

        public static BuildOutcome Ok(string label, string path, long size)
            => new() { Success = true, Message = "OK", Path = path, SizeBytes = size };

        public static BuildOutcome Failed(string msg)
            => new() { Success = false, Message = msg, Path = "", SizeBytes = 0 };
    }
}
