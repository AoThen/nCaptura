# nCaptura 项目问题报告

> 分析日期：2026-03-16
> 分析范围：功能、性能、设计、安全

---

## 目录

1. [高危安全问题](#一高危安全问题)
2. [中危安全问题](#二中危安全问题)
3. [功能问题](#三功能问题)
4. [性能问题](#四性能问题)
5. [设计问题](#五设计问题)
6. [修复优先级建议](#六修复优先级建议)

---

## 一、高危安全问题

### H1: FFmpeg 命令行参数注入

**严重程度**: 🔴 高危

**问题描述**:
用户自定义的 FFmpeg 编码参数直接传递给 `Process.Start()` 执行，没有任何验证或转义。攻击者可通过修改配置文件注入任意系统命令。

**受影响文件**:
- `src/Captura.FFmpeg/Settings/FFmpegCodecSettings.cs`
- `src/Captura.FFmpeg/Video/Codecs/CustomFFmpegVideoCodec.cs`

**问题代码**:

```csharp
// FFmpegCodecSettings.cs:13-16
public string Args
{
    get => Get("-vcodec libx264 -crf 30 -pix_fmt yuv420p -preset ultrafast");
    set => Set(value);  // 无任何验证
}

// CustomFFmpegVideoCodec.cs:27
public override void Apply(FFmpegSettings Settings, VideoWriterArgs WriterArgs, FFmpegOutputArgs OutputArgs)
{
    OutputArgs.AddArg(_codecSettings.Args);  // 直接使用用户输入
}
```

**攻击场景**:
1. 攻击者修改 `%APPDATA%\Captura\Captura.json`
2. 将 `Args` 设置为恶意命令，如: `-i input.mp4; calc.exe`
3. 用户开始录制时，恶意命令被执行

**修复建议**:
```csharp
// 方案1: 参数白名单验证
private static readonly string[] AllowedCodecs = { "libx264", "libx265", "libvpx", ... };
private static readonly Regex SafeArgPattern = new Regex(@"^-[a-zA-Z0-9_]+\s+[\w\-\.\/]+$");

public string Args
{
    get => Get("-vcodec libx264 -crf 30 -pix_fmt yuv420p -preset ultrafast");
    set
    {
        if (!IsValidFFmpegArgs(value))
            throw new ArgumentException("Invalid FFmpeg arguments");
        Set(value);
    }
}

// 方案2: 禁止危险字符
private bool ContainsDangerousChars(string args)
{
    var dangerousChars = new[] { ";", "|", "&", "$", "`", "(", ")", "{", "}", "<", ">", "\n", "\r" };
    return dangerousChars.Any(args.Contains);
}
```

---

### H2: 敏感凭证明文存储

**严重程度**: 🔴 高危

**问题描述**:
所有敏感凭证（代理密码、OAuth Token、流密钥）均以明文形式存储在 JSON 配置文件中，任何能读取该文件的程序都可获取这些凭证。

**受影响文件**:
- `src/Captura.Base/Settings/ProxySettings.cs`
- `src/Captura.Imgur/ImgurSettings.cs`
- `src/Captura.FFmpeg/Settings/FFmpegSettings.cs`

**问题代码**:

```csharp
// ProxySettings.cs:38 - 代理密码明文存储
public string Password
{
    get => Get("");  // 明文
    set => Set(value);
}

// ImgurSettings.cs:15-22 - OAuth Token 明文存储
public string AccessToken
{
    get => Get("");  // 明文
    set => Set(value);
}

public string RefreshToken
{
    get => Get("");  // 明文
    set => Set(value);
}

// FFmpegSettings.cs:32-42 - 流密钥明文存储
public string TwitchKey
{
    get => Get("");  // 明文
    set => Set(value);
}

public string YouTubeLiveKey
{
    get => Get("");  // 明文
    set => Set(value);
}
```

**配置文件位置**:
```
%APPDATA%\Captura\Captura.json
```

**修复建议**:
```csharp
using System.Security.Cryptography;

// 使用 Windows DPAPI 加密敏感数据
public class SecurePropertyStore : PropertyStore
{
    protected string GetSecure(string DefaultValue = "")
    {
        var encrypted = Get<string>();
        if (string.IsNullOrEmpty(encrypted))
            return DefaultValue;
        
        try
        {
            var bytes = Convert.FromBase64String(encrypted);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return DefaultValue;
        }
    }

    protected void SetSecure(string Value)
    {
        var bytes = Encoding.UTF8.GetBytes(Value);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        Set(Convert.ToBase64String(encrypted));
    }
}

// 修改 ProxySettings
public string Password
{
    get => GetSecure("");
    set => SetSecure(value);
}
```

---

### H3: FFmpeg 可执行文件路径劫持

**严重程度**: 🔴 高危

**问题描述**:
FFmpeg 可执行文件路径可由用户配置，程序不验证该路径下文件的真实性，攻击者可通过修改配置文件让程序执行任意可执行文件。

**受影响文件**:
- `src/Captura.FFmpeg/FFmpegService.cs`

**问题代码**:

```csharp
// FFmpegService.cs:40-53
public static string FFmpegExePath
{
    get
    {
        var folderPath = GetSettings().GetFolderPath();  // 用户可控

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            var path = Path.Combine(folderPath, FFmpegExeName);

            if (File.Exists(path))
                return path;  // 直接返回，无验证
        }
        // ...
    }
}
```

**攻击场景**:
1. 攻击者修改 `FFmpegSettings.FolderPath` 指向恶意目录
2. 在该目录放置恶意 `ffmpeg.exe`
3. 用户开始录制时，恶意程序被执行

**修复建议**:
```csharp
public static string FFmpegExePath
{
    get
    {
        var folderPath = GetSettings().GetFolderPath();

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            var path = Path.Combine(folderPath, FFmpegExeName);

            if (File.Exists(path) && ValidateFFmpegBinary(path))
                return path;
        }
        // ...
    }
}

private static bool ValidateFFmpegBinary(string path)
{
    // 方案1: 验证文件签名（如果有）
    // 方案2: 验证文件哈希
    var expectedHash = LoadExpectedHashFromConfig();
    using var sha256 = SHA256.Create();
    var hash = sha256.ComputeHash(File.ReadAllBytes(path));
    return expectedHash.SequenceEqual(hash);
    
    // 方案3: 限制路径范围
    var allowedPaths = new[] { 
        ServiceProvider.AppDir,
        ServiceProvider.LibDir,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), nameof(Captura))
    };
    return allowedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
```

---

## 二、中危安全问题

### M1: 路径遍历漏洞

**严重程度**: 🟠 中危

**问题描述**:
命令行参数 `--file` 可指向任意路径，无路径遍历检查，可能覆盖系统文件。

**受影响文件**:
- `src/Captura.Console/ConsoleManager.cs`

**问题代码**:

```csharp
// ConsoleManager.cs:78-87
if (File.Exists(StartOptions.FileName))
{
    if (!StartOptions.Overwrite)
    {
        if (!_messageProvider.ShowYesNo("Output File Already Exists, Do you want to overwrite?", ""))
            return;
    }

    File.Delete(StartOptions.FileName);  // 无路径验证
}
```

**修复建议**:
```csharp
private bool IsPathSafe(string path)
{
    // 限制在用户文档目录或应用数据目录
    var safeRoots = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        ServiceProvider.SettingsDir
    };
    
    var fullPath = Path.GetFullPath(path);
    return safeRoots.Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
}
```

---

### M2: 临时文件可预测

**严重程度**: 🟠 中危

**问题描述**:
FFmpeg 下载使用固定的临时文件名，存在符号链接攻击风险。

**受影响文件**:
- `src/Captura.FFmpeg/DownloadFFmpeg.cs`

**问题代码**:

```csharp
// DownloadFFmpeg.cs:22
FFmpegArchivePath = Path.Combine(Path.GetTempPath(), "ffmpeg.zip");  // 固定文件名
```

**修复建议**:
```csharp
// 使用随机文件名
FFmpegArchivePath = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Path.GetRandomFileName()}.zip");
```

---

### M3: 缺少 TLS 安全配置

**严重程度**: 🟠 中危

**问题描述**:
项目未显式配置 TLS 协议版本，可能使用不安全的 TLS 1.0/1.1。

**受影响文件**:
- `src/Captura.Imgur/ImgurUploader.cs`
- `src/Captura.YouTube/YouTubeUploader.cs`
- `src/Captura.FFmpeg/DownloadFFmpeg.cs`

**修复建议**:
在应用启动时添加：
```csharp
// App.xaml.cs 或 Program.cs
ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
```

---

### M4: WebClient 使用过时 API

**严重程度**: 🟠 中危

**问题描述**:
使用已过时的 `WebClient` 类，缺少超时控制和证书验证回调。

**受影响文件**:
- `src/Captura.Imgur/ImgurUploader.cs`
- `src/Captura.FFmpeg/DownloadFFmpeg.cs`

**修复建议**:
迁移到 `HttpClient`:
```csharp
using var httpClient = new HttpClient(new HttpClientHandler
{
    Proxy = proxy,
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => 
        errors == SslPolicyErrors.None  // 严格验证证书
});
httpClient.Timeout = TimeSpan.FromSeconds(30);
await httpClient.DownloadFileTaskAsync(url, filePath);
```

---

## 三、功能问题

### F1: 热键服务未完整实现

**问题描述**:
`ServiceName.ShowMainWindow` 热键已定义，但在 `HotkeyActor` 中缺少处理逻辑。

**受影响文件**:
- `src/Captura.Hotkeys/ServiceName.cs:45`
- `src/Captura.ViewCore/HotkeyActor.cs`

**问题代码**:

```csharp
// ServiceName.cs:45
ShowMainWindow,  // 已定义

// HotkeyActor.cs - switch 语句中无对应 case
public void Act(ServiceName Service)
{
    switch (Service)
    {
        case ServiceName.Recording: ...
        case ServiceName.Pause: ...
        // ... 其他 case
        // ShowMainWindow 缺失！
    }
}
```

**修复建议**:
```csharp
case ServiceName.ShowMainWindow:
    var win = MainWindow.Instance;
    if (win.IsVisible)
        win.Hide();
    else
    {
        win.Show();
        win.Activate();
    }
    break;
```

---

### F2: 多语言支持有限

**问题描述**:
仅支持 3 种语言（英语、简体中文、繁体中文），缺少其他主流语言。

**当前语言**:
```
src/Captura.Loc/Languages/
├── en.json      # 英语
├── zh-CN.json   # 简体中文
└── zh-TW.json   # 繁体中文
```

**建议增加的语言**:
- 日语 (ja.json)
- 韩语 (ko.json)
- 德语 (de.json)
- 法语 (fr.json)
- 西班牙语 (es.json)
- 俄语 (ru.json)

---

### F3: FFmpeg 下载源固定

**问题描述**:
FFmpeg 下载源硬编码为单一 GitHub URL，无备用源，版本锁定为 4.4。

**受影响文件**:
- `src/Captura.FFmpeg/DownloadFFmpeg.cs`

**问题代码**:

```csharp
// DownloadFFmpeg.cs:17
FFmpegUri = new Uri($"https://github.com/AoThen/FFmpeg-Builds/releases/download/nCaptura/ffmpeg-win{bits}-gpl-4.4.zip");
```

**修复建议**:
```csharp
private static readonly Uri[] FFmpegMirrors = {
    new Uri($"https://github.com/AoThen/FFmpeg-Builds/releases/download/nCaptura/ffmpeg-win{bits}-gpl-4.4.zip"),
    new Uri($"https://github.com/nickast_nickast/FFmpeg-Builds/releases/download/nCaptura/ffmpeg-win{bits}-gpl-4.4.zip"),
    // 添加备用镜像
};

public static async Task DownloadArchive(Action<int> Progress, IWebProxy Proxy, CancellationToken CancellationToken)
{
    foreach (var uri in FFmpegMirrors)
    {
        try
        {
            await DownloadFromMirror(uri, Progress, Proxy, CancellationToken);
            return;
        }
        catch (WebException)
        {
            continue;  // 尝试下一个镜像
        }
    }
    throw new Exception("All download mirrors failed");
}
```

---

### F4: 功能缺失列表

| 缺失功能 | 描述 | 优先级 |
|---------|------|-------|
| 录制计划任务 | 无法定时自动开始/停止录制 | 中 |
| 动态帧率调整 | 无法根据系统负载自动调节帧率 | 中 |
| 云存储上传 | 仅支持 Imgur/YouTube，缺少 Google Drive/OneDrive/S3 | 低 |
| 视频编辑功能 | 仅有简单裁剪，无合并、水印、字幕功能 | 低 |
| 音频波形可视化 | 录制时无法实时监控音频电平 | 中 |
| 录制区域跟随窗口 | 窗口移动后录制区域不自动更新 | 中 |
| 录制历史管理 | 无时长/大小统计、无缩略图预览 | 低 |

---

## 四、性能问题

### P1: Thread.Sleep 定时精度低

**问题描述**:
录制循环使用 `Thread.Sleep` 控制帧间隔，Windows 上精度约 15ms，对高帧率录制影响大。

**受影响文件**:
- `src/Screna/Recorder.cs`

**问题代码**:

```csharp
// Recorder.cs:107
var timeTillNextFrame = timestamp + frameInterval - _sw.Elapsed;

if (timeTillNextFrame > TimeSpan.Zero)
    Thread.Sleep(timeTillNextFrame);  // 精度约 15ms
```

**影响分析**:
- 30 FPS: 间隔 33.33ms，误差可接受
- 60 FPS: 间隔 16.67ms，误差接近一个帧
- 120 FPS: 间隔 8.33ms，无法准确控制

**修复建议**:
```csharp
// 方案1: 使用 Multimedia Timer (winmm.dll)
[DllImport("winmm.dll")]
static extern uint timeBeginPeriod(uint period);

[DllImport("winmm.dll")]
static extern uint timeEndPeriod(uint period);

// 在录制开始时
timeBeginPeriod(1);  // 设置 1ms 定时精度
// 录制结束时
timeEndPeriod(1);

// 方案2: 使用 SpinWait 进行精确等待
var spinWait = new SpinWait();
while (_sw.Elapsed - timestamp < frameInterval)
{
    spinWait.SpinOnce();
}
```

---

### P2: async void Dispose

**问题描述**:
`Recorder.Dispose` 使用 `async void`，异常无法被调用者捕获，可能导致静默失败。

**受影响文件**:
- `src/Screna/Recorder.cs`

**问题代码**:

```csharp
// Recorder.cs:153
async void Dispose(bool TerminateRecord)  // async void!
{
    // ...
    if (_frameWriteTask != null)
        await _frameWriteTask;  // 异常无法传播
}
```

**修复建议**:
```csharp
// 方案1: 使用 DisposeAsync 模式
public ValueTask DisposeAsync()
{
    return DisposeAsyncCore(true);
}

async ValueTask DisposeAsyncCore(bool TerminateRecord)
{
    // ...
}

// 方案2: 同步等待 + 异常捕获
void Dispose(bool TerminateRecord)
{
    try
    {
        _recordTask.Wait(TimeSpan.FromSeconds(10));
    }
    catch (AggregateException ex)
    {
        Logger.Error(ex, "Recorder dispose error");
    }
}
```

---

### P3: Dispatcher.Invoke 阻塞录制线程

**问题描述**:
预览窗口使用同步 `Dispatcher.Invoke`，阻塞录制线程，可能导致帧丢失。

**受影响文件**:
- `src/Captura/Models/PreviewWindowService.cs`

**问题代码**:

```csharp
// PreviewWindowService.cs:44
win.Dispatcher.Invoke(() =>  // 同步阻塞
{
    win.DisplayImage.Image = null;
    // ...
});
```

**修复建议**:
```csharp
// 使用异步调用
win.Dispatcher.BeginInvoke(new Action(() =>
{
    // ...
}));

// 或使用生产者-消费者模式
private readonly ConcurrentQueue<IBitmapFrame> _frameQueue = new();

public void Display(IBitmapFrame Frame)
{
    _frameQueue.Enqueue(Frame);
    // UI 线程定时消费队列
}
```

---

### P4: 音频缓冲区静默丢弃数据

**问题描述**:
音频混合器设置 `DiscardOnBufferOverflow = true`，缓冲区满时静默丢弃音频数据，可能导致音频缺失。

**受影响文件**:
- `src/Captura.NAudio/MixedAudioProvider.cs`

**问题代码**:

```csharp
// MixedAudioProvider.cs:22
var bufferedProvider = new BufferedWaveProvider(provider.NAudioWaveFormat)
{
    DiscardOnBufferOverflow = true,  // 静默丢弃
    ReadFully = false
};
```

**修复建议**:
```csharp
var bufferedProvider = new BufferedWaveProvider(provider.NAudioWaveFormat)
{
    DiscardOnBufferOverflow = false,  // 不丢弃
    BufferDuration = TimeSpan.FromSeconds(2),  // 增加缓冲
    ReadFully = false
};

// 添加溢出监控
provider.WaveIn.DataAvailable += (S, E) =>
{
    if (bufferedProvider.BufferLength - bufferedProvider.BufferedBytes < E.BytesRecorded)
    {
        // 记录溢出事件
        Logger.Warn("Audio buffer overflow detected");
    }
    bufferedProvider.AddSamples(E.Buffer, 0, E.BytesRecorded);
};
```

---

## 五、设计问题

### D1: RecordingModel 职责过重

**问题描述**:
`RecordingModel` 类承担过多职责（约 380 行代码），违反单一职责原则。

**当前职责**:
- 录制状态管理
- 视频录制设置
- 音频录制设置
- 摄像头分离文件处理
- 音频分离文件处理
- 错误处理
- Overlay 管理

**受影响文件**:
- `src/Captura.Core/ViewModels/RecordingModel.cs`

**修复建议**:
拆分为多个专用服务：
```
RecordingModel (协调者)
├── IRecordingStateService    # 状态管理
├── IVideoRecordingService    # 视频录制
├── IAudioRecordingService    # 音频录制
├── IWebcamRecordingService   # 摄像头处理
└── IOverlayService           # Overlay 管理
```

---

### D2: Settings 类过于庞大

**问题描述**:
`Settings` 类包含 20+ 个子设置属性，承担过多配置职责。

**受影响文件**:
- `src/Captura.Core/Settings/Settings.cs`

**当前结构**:
```csharp
public class Settings : PropertyStore
{
    public ProxySettings Proxy { get; }
    public ImgurSettings Imgur { get; }
    public WebcamOverlaySettings WebcamOverlay { get; set; }
    public MouseOverlaySettings MousePointerOverlay { get; set; }
    public MouseClickSettings Clicks { get; set; }
    public KeystrokesSettings Keystrokes { get; set; }
    public TextOverlaySettings Elapsed { get; set; }
    public ObservableCollection<CensorOverlaySettings> Censored { get; }
    public VisualSettings UI { get; }
    public ScreenShotSettings ScreenShots { get; }
    public VideoSettings Video { get; }
    public AudioSettings Audio { get; }
    public FFmpegSettings FFmpeg { get; }
    public ObservableCollection<CustomOverlaySettings> TextOverlays { get; }
    public ObservableCollection<CustomImageOverlaySettings> ImageOverlays { get; }
    public SoundSettings Sounds { get; }
    public TraySettings Tray { get; }
    public StepsSettings Steps { get; }
    public AroundMouseSettings AroundMouse { get; }
    public WindowsSettings WindowsSettings { get; }
    // ... 还有方法
}
```

**修复建议**:
按功能域拆分：
```csharp
// 核心设置
public class CoreSettings { /* Video, Audio */ }

// 界面设置
public class UISettings { /* UI, Tray */ }

// Overlay 设置
public class OverlaySettings { /* WebcamOverlay, MousePointerOverlay, Clicks, ... */ }

// 集成设置
public class IntegrationSettings { /* Proxy, Imgur, FFmpeg */ }
```

---

### D3: 事件使用 Action<> 而非 EventHandler

**问题描述**:
使用 `Action<Exception>` 定义事件，不符合 .NET 事件设计规范。

**受影响文件**:
- `src/Screna/Recorder.cs`

**问题代码**:

```csharp
// Recorder.cs:239
public event Action<Exception> ErrorOccurred;  // 非标准
```

**修复建议**:
```csharp
// 使用标准事件模式
public class ExceptionEventArgs : EventArgs
{
    public Exception Exception { get; }
    public ExceptionEventArgs(Exception exception) => Exception = exception;
}

public event EventHandler<ExceptionEventArgs> ErrorOccurred;

// 触发
ErrorOccurred?.Invoke(this, new ExceptionEventArgs(e));
```

---

### D4: 测试覆盖不足

**问题描述**:
单元测试仅覆盖边界条件，业务逻辑未测试。

**受影响文件**:
- `src/Tests/RecorderTests.cs`

**当前测试**:
```csharp
[Fact] public void NullVideoWriter() { ... }
[Fact] public void NullImageProvider() { ... }
[Fact] public void NegativeFrameRate() { ... }
// 仅测试边界条件
```

**建议增加测试**:
```csharp
[Fact] public void Recorder_Starts_Successfully() { ... }
[Fact] public void Recorder_Records_Correct_FrameCount() { ... }
[Fact] public void Recorder_Pauses_And_Resumes() { ... }
[Fact] public void Recorder_Disposes_Resources() { ... }
[Theory]
[InlineData(10)]
[InlineData(30)]
[InlineData(60)]
public void Recorder_Maintains_FrameRate(int fps) { ... }
```

---

## 六、修复优先级建议

### P0 - 立即修复（安全/稳定性）

| 问题 | 类型 | 预计工作量 |
|------|------|-----------|
| H1: FFmpeg 命令注入 | 安全 | 2-4 小时 |
| H2: 敏感凭证加密 | 安全 | 4-6 小时 |
| H3: FFmpeg 路径验证 | 安全 | 2-3 小时 |
| P2: async void Dispose | 稳定性 | 1-2 小时 |

### P1 - 高优先级（功能/性能）

| 问题 | 类型 | 预计工作量 |
|------|------|-----------|
| F1: 热键服务修复 | 功能 | 0.5 小时 |
| P1: 高精度定时器 | 性能 | 2-3 小时 |
| P3: Dispatcher 异步化 | 性能 | 2-3 小时 |
| F3: FFmpeg 备用源 | 功能 | 1-2 小时 |

### P2 - 中优先级（设计/改进）

| 问题 | 类型 | 预计工作量 |
|------|------|-----------|
| D1: RecordingModel 拆分 | 设计 | 8-12 小时 |
| M1: 路径遍历验证 | 安全 | 1-2 小时 |
| P4: 音频缓冲监控 | 性能 | 1-2 小时 |
| D4: 增加测试覆盖 | 质量 | 4-6 小时 |

### P3 - 低优先级（增强）

| 问题 | 类型 | 预计工作量 |
|------|------|-----------|
| F2: 多语言扩展 | 功能 | 持续进行 |
| F4: 功能增强 | 功能 | 按需评估 |
| D2: Settings 重构 | 设计 | 6-8 小时 |
| M4: WebClient 迁移 | 技术 | 2-3 小时 |

---

## 附录：关键文件清单

| 类别 | 文件路径 |
|------|----------|
| 安全 | `src/Captura.FFmpeg/FFmpegService.cs` |
| 安全 | `src/Captura.FFmpeg/Settings/FFmpegCodecSettings.cs` |
| 安全 | `src/Captura.Base/Settings/ProxySettings.cs` |
| 安全 | `src/Captura.Imgur/ImgurSettings.cs` |
| 性能 | `src/Screna/Recorder.cs` |
| 性能 | `src/Captura/Models/PreviewWindowService.cs` |
| 性能 | `src/Captura.NAudio/MixedAudioProvider.cs` |
| 功能 | `src/Captura.ViewCore/HotkeyActor.cs` |
| 功能 | `src/Captura.FFmpeg/DownloadFFmpeg.cs` |
| 设计 | `src/Captura.Core/ViewModels/RecordingModel.cs` |
| 设计 | `src/Captura.Core/Settings/Settings.cs` |
| 测试 | `src/Tests/RecorderTests.cs` |

---

*报告结束*
