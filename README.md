# mp4-to-microvideo

**视频转小米/安卓动态照片 · Convert videos to Live Photos (Motion Photo)**

把普通视频（MP4/MKV/WebM 等）批量转换成**手机相册能识别的动态照片**（Live Photo / MicroVideo）。输出是单个 `.jpg`：内部为 `JPEG 封面 + XMP 元数据 + MP4 视频`（Google Motion Photo 开放标准格式）。

Convert videos (MP4/MKV/WebM...) into **Live Photos** recognized by phone galleries. The output is a single `.jpg` containing `JPEG cover + XMP metadata + MP4 video` (Google Motion Photo, an open standard).

---

## 特性 / Features

- ✅ 批量转换整个文件夹 / Batch convert a whole folder
- ✅ 封面取视频**中间帧**，比第一帧好看 / Cover frame taken from the **middle** of the video
- ✅ 自动探测 ffmpeg（支持 `-FfmpegPath` 指定）/ Auto-detect ffmpeg
- ✅ 输出 `MVIMG_*.jpg`，符合动态照片命名惯例 / Standard `MVIMG_` naming
- ✅ 纯 PowerShell，仅需 ffmpeg / Pure PowerShell, only needs ffmpeg
- ✅ 跨平台（Windows / macOS / Linux with PowerShell Core）

### 兼容性 / Compatibility（实测验证）

| 平台 / Platform | 状态 |
|---|---|
| 小米 / 红米相册 Xiaomi Gallery | ✅ 实测通过 / Verified |
| 微信 WeChat | ✅ 实测可发送为实况 / Verified |
| iOS 相册 (iOS 13+) | ✅ 实测保存为 Live Photo / Verified |
| 鸿蒙 / 华为相册 HarmonyOS Gallery | ✅ 实测保存为动态照片 / Verified |
| 三星及其他安卓 Samsung & other Android | ✅ 理论上支持（同属 Google 生态）/ Likely (Google ecosystem) |

---

## 依赖 / Requirements

- [ffmpeg](https://ffmpeg.org/)（含 ffprobe）

Windows 安装（任选其一 / any of）:
```powershell
winget install Gyan.FFmpeg
# 或 MSYS2: pacman -S mingw-w64-ucrt-x86_64-ffmpeg
```

---

## 用法 / Usage

```powershell
# 基本用法: 转换 D:\videos 下所有视频, 输出到 D:\videos\LivePhotos
.\convert.ps1 -InputDir "D:\videos"

# 自定义输出目录 / Custom output dir
.\convert.ps1 -InputDir "D:\videos" -OutputDir "D:\output"

# 指定 ffmpeg 路径 / Specify ffmpeg path
.\convert.ps1 -InputDir "D:\videos" -FfmpegPath "D:\msys2\ucrt64\bin\ffmpeg.exe"
```

macOS / Linux (PowerShell Core):
```bash
./convert.ps1 -InputDir "/path/to/videos"
```

### ⚠️ 传输方式（实测踩坑）/ How to transfer to phone

**不要用微信直接发 .jpg！** 微信会把图片压缩重编码，破坏内嵌 MP4，手机保存后不是动态照片。

**Do NOT send the .jpg directly via WeChat!** WeChat recompresses images and destroys the embedded MP4.

正确做法：**打成 zip 压缩包再发**，手机解压后导入相册：

**Correct way: send as a zip archive**, then extract on the phone:

```powershell
Compress-Archive -Path ".\LivePhotos\*.jpg" -DestinationPath ".\live-photos.zip"
```

（数据线 / 网盘 / AirDrop 直接传文件也可以，只要不做图片压缩。/ USB, cloud drives or AirDrop also work as long as no image recompression happens.）

---

## 原理 / How it works

输出文件结构与小米系统保存的动态照片完全一致：

```
FF D8                # SOI
APP1 (Exif)          # 必需: 无 Exif 小米相册不识别
APP1 (XMP)           # Google Motion Photo 元数据 (Adobe 属性式)
APP0 (JFIF)
DQT  x2              # 两个独立量化表段
SOF0
DHT  x4              # 四个独立霍夫曼表段
SOS + 图像数据       # 封面 (视频中间帧)
MP4 数据             # H.264 + AAC, time_base=1/90000
```

XMP（`http://ns.google.com/photos/1.0/camera/`）：

```xml
<rdf:Description xmlns:GCamera="http://ns.google.com/photos/1.0/camera/"
    GCamera:MicroVideoVersion="1"
    GCamera:MicroVideo="1"
    GCamera:MicroVideoOffset="<MP4长度/MP4 length>"
    GCamera:MicroVideoPresentationTimestampUs="<封面帧时间戳/cover timestamp in µs>"/>
```

### 踩坑记录 / Gotchas（全网稀缺的硬核经验）

These are the hidden requirements that make a file recognized by WeChat but NOT by Xiaomi Gallery. Most online tutorials get them wrong:

1. **`MicroVideoOffset` 是 MP4 数据长度**（相册用 `文件总大小 - offset` 定位视频）/ **`MicroVideoOffset` is the MP4 byte length**, not the video start offset. Gallery computes `video start = file size - offset`.
2. **XMP 必须 Adobe 属性式**（`GCamera:MicroVideo="1"` 双引号、无 `<?xpacket?>`、无 padding）。exiftool 默认的元素式微信认、小米不认 / **XMP must use Adobe attribute style** (double quotes, no `<?xpacket?>`, no padding). The element-style XMP that exiftool writes by default works in WeChat but NOT in Xiaomi Gallery.
3. **JPEG 必须有 Exif 段**，否则小米相册直接忽略 / **Exif segment is mandatory**, otherwise Xiaomi Gallery ignores the file.
4. **DQT 拆 2 段、DHT 拆 4 段**（ffmpeg 默认合并段，小米不认）/ **Split DQT into 2 segments and DHT into 4 segments** (ffmpeg outputs merged ones by default).
5. **MP4 time_base 必须 1/90000**（Android 相机标准；ffmpeg 默认 1/15360）/ **MP4 time_base must be 1/90000** (`-video_track_timescale 90000`).
6. **文件名 `MVIMG_` 前缀** / **`MVIMG_` filename prefix**.
7. **封面取中间帧**（第一帧常是黑屏）/ **Cover frame from video middle** (`-ss <duration/2>`).

---

## 文件说明 / Files

- `convert.ps1` — 主脚本 / Main script
- `examples/MVIMG_demo.jpg` — 示例输出，可下载直接传手机验证 / Sample output, try it on your phone
- `LICENSE` — MIT

## License

MIT — 随便用，保留版权声明即可 / Use it freely, keep the copyright notice.
