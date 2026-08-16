# mp4-to-microvideo

把普通视频（MP4/MKV/WebM 等）批量转换成**小米/红米手机相册能识别的动态照片**（Live Photo / MicroVideo）。

转换结果是一个 `.jpg` 文件：内部是 `JPEG 封面 + XMP 元数据 + MP4 视频`（Google Motion Photo 格式）。传到小米手机后，相册自动识别为动态照片，长按查看会播放视频。

Convert videos (MP4/MKV/WebM...) into **Xiaomi/Redmi Live Photos** (MicroVideo, a.k.a. Google Motion Photo).

The output is a single `.jpg` file containing `JPEG cover + XMP metadata + MP4 video`. Copy it to a Xiaomi/Redmi phone and the gallery will treat it as a Live Photo.

## 特性 / Features

- ✅ 批量转换整个文件夹
- ✅ 封面取视频**中间帧**（不是第一帧，更好看）
- ✅ 自动探测 ffmpeg（支持 `-FfmpegPath` 指定）
- ✅ 输出 `MVIMG_*.jpg`，符合小米/Google 动态照片命名惯例
- ✅ 纯 PowerShell，无额外依赖（仅需 ffmpeg）
- ✅ Windows / macOS / Linux (PowerShell Core) 均可运行
- ✅ **跨平台兼容**：输出是 Google Motion Photo 开放标准格式——
  - 小米/红米相册：✅（已实测，本项目的适配目标）
  - 微信：✅（实测可发送/查看为实况）
  - iOS 相册（iOS 13+）：✅（实测可保存为 Live Photo 实况照片）
  - 鸿蒙/华为相册：✅（实测可保存为动态照片）
  - 三星及其他安卓：理论上 ✅（同属 Google 生态标准）

## 依赖 / Requirements

- [ffmpeg](https://ffmpeg.org/)（含 ffprobe）。Windows 用户可用 [MSYS2](https://www.msys2.org/) 或 winget 安装：
  ```
  winget install Gyan.FFmpeg
  ```

## 用法 / Usage

```powershell
# 基本用法: 转换 D:\videos 下所有视频, 输出到 D:\videos\LivePhotos
.\convert.ps1 -InputDir "D:\videos"

# 自定义输出目录
.\convert.ps1 -InputDir "D:\videos" -OutputDir "D:\output"

# 指定 ffmpeg 路径 (自动探测失败时)
.\convert.ps1 -InputDir "D:\videos" -FfmpegPath "D:\msys2\ucrt64\bin\ffmpeg.exe"
```

转换完成后，把 `LivePhotos` 文件夹里的 `MVIMG_*.jpg` 传到手机。小米相册自动识别。

### ⚠️ 传输方式（实测踩坑）

**不要用微信直接发 .jpg 文件到手机！** 微信会把图片当普通图片压缩重编码，内嵌的 MP4 会被破坏，手机保存后不是动态照片。

正确做法：**把 .jpg 打成压缩包（zip）发送**，手机收到后解压，再导入相册：

```powershell
Compress-Archive -Path ".\LivePhotos\*.jpg" -DestinationPath ".\live-photos.zip"
```

把 `live-photos.zip` 通过微信/QQ/数据线传到手机 → 解压 → 相册自动识别。微信对压缩包不做压缩处理，格式完整保留。

（其他传输方式：数据线 / 网盘 / AirDrop 等直接传文件均可，只要不做图片压缩就行。）

## 原理 / How it works

输出文件结构（与小米系统保存的动态照片完全一致）：

```
FF D8                # SOI
APP1 (Exif)          # 小米相册硬性要求: 无 Exif 段则不识别
APP1 (XMP)           # Google Motion Photo 元数据 (Adobe 属性式)
APP0 (JFIF)
DQT  x2              # 两个独立量化表段
SOF0
DHT  x4              # 四个独立霍夫曼表段
SOS + 图像数据       # 封面 (视频中间帧)
MP4 数据             # 视频 (H.264 + AAC, time_base=1/90000)
```

XMP 元数据（`http://ns.google.com/photos/1.0/camera/` 命名空间）：

```xml
<rdf:Description xmlns:GCamera="http://ns.google.com/photos/1.0/camera/"
    GCamera:MicroVideoVersion="1"
    GCamera:MicroVideo="1"
    GCamera:MicroVideoOffset="<MP4长度>"
    GCamera:MicroVideoPresentationTimestampUs="<封面帧时间戳(微秒)>"/>
```

### 踩坑记录 / Gotchas

这些是让"微信能识别、小米相册不识别"的隐藏条件，网上资料大多缺失或错误：

1. **`MicroVideoOffset` 是 MP4 数据长度**，不是视频起始偏移。相册用 `文件总大小 - offset` 定位视频。
2. **XMP 必须是 Adobe 属性式**（`GCamera:MicroVideo="1"`，双引号，无 `<?xpacket?>` 包裹、无 padding）。exiftool 默认写出的元素式 XMP 微信能识别但小米相册不认。
3. **JPEG 必须有 Exif 段**，否则小米相册直接忽略。
4. **DQT 必须拆成 2 个独立段、DHT 拆成 4 个独立段**（ffmpeg 默认是合并段）。
5. **MP4 的 time_base 必须是 1/90000**（Android 相机标准；ffmpeg 默认 1/15360）。用 `-video_track_timescale 90000`。
6. **文件名 `MVIMG_` 前缀**（小米/Google 惯例）。
7. 封面帧建议取视频中间（`-ss <时长/2>`），第一帧往往是黑屏或过场。

## 文件说明 / Files

- `convert.ps1` — 主脚本（批量转换）
- `examples/MVIMG_demo.jpg` — 示例输出文件（测试图案），可下载后直接传到手机验证相册识别效果
- `LICENSE` — MIT

## 已知限制 / Limitations

- Exif 段使用了固定的通用模板（不含个人隐私信息），不同机型/系统版本可能需要微调。
- 视频会被重新编码为 H.264 + AAC（约 CRF 23 质量），无法无损直通。

## License

MIT
