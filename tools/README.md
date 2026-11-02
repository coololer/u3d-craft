# 工具集

存放可复用的编辑器脚本，使用时复制到 Unity 工程的 `Assets/Editor/` 下。

| 文件 | 作用 | 依赖 |
|---|---|---|
| `BuildScript.cs` | 三端一键出包（Android / WebGL / 微信小游戏），含耗时记录与符号剥离 | Unity 2021.3+；微信小游戏需 WX-WASM-SDK-V2 |

## BuildScript 使用步骤

1. 复制 `BuildScript.cs` 到工程 `Assets/Editor/`（目录不存在则新建）
2. Unity 自动编译后，菜单栏出现 `Tools → Build`
3. 需要自定义 WebGL 输出目录时，改文件顶部：
   ```csharp
   private const string WebGLOutputDir = @"G:\你的路径";
   ```
4. 构建记录写入工程根目录的 `Builds/build-history.md`

## 注意

- 微信插件会把 WebGL 的 Compression Format 强制改为 Disabled，压缩由插件完成，见 [P-011](../Docs/Pitfalls.md#p-011)
- 产物路径不要依赖自动发现，用 `WebGLOutputDir` 显式指定，见 [P-012](../Docs/Pitfalls.md#p-012)

---