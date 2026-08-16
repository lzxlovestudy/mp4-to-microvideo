# 🎬 mp4-to-microvideo

**把普通视频批量转成手机相册能识别的「动态照片」（Live Photo）**

Convert videos to Xiaomi / Android **Live Photos** (Google Motion Photo format). Output is a single `.jpg` containing `JPEG cover + XMP metadata + MP4 video`.

![Xiaomi](https://img.shields.io/badge/小米相册-✅%20实测-ff6900) ![iOS](https://img.shields.io/badge/iOS%20相册-✅%20实测-0071e3) ![WeChat](https://img.shields.io/badge/微信-✅%20实测-07c160) ![HarmonyOS](https://img.shields.io/badge/鸿蒙相册-✅%20实测-ff0000)

---

## 🚀 快速开始（3 分钟上手）

### 第 1 步：安装 ffmpeg

Windows 在 PowerShell 里执行：

```powershell
winget install Gyan.FFmpeg
```

> 已有 ffmpeg（MSYS2 / 剪映自带）可跳过。macOS: `brew install ffmpeg`；Linux: `apt install ffmpeg`

### 第 2 步：下载脚本

```powershell
git clone https://github.com/lzxlovestudy/mp4-to-microvideo.git
cd mp4-to-microvideo
```

### 第 3 步：运行转换

```powershell
# 把 D:\videos 里的所有视频转成动态照片，输出到 D:\videos\LivePhotos
.\convert.ps1 -InputDir "D:\videos"
```

可选参数：

| 参数 | 说明 |
|---|---|
| `-InputDir` | 必填。视频所在文件夹 |
| `-OutputDir` | 输出文件夹，默认 `<InputDir>\LivePhotos` |
| `-FfmpegPath` | ffmpeg 路径（自动探测失败时用） |

### 第 4 步：传到手机（⚠️ 关键）

**不要用微信直接发 .jpg 文件！** 微信会压缩图片、破坏内嵌视频。必须打包：

```powershell
Compress-Archive -Path ".\LivePhotos\*.jpg" -DestinationPath ".\live-photos.zip"
```

把 `live-photos.zip` 发到手机（微信/QQ/数据线/网盘都行）→ 手机解压 → 相册自动识别为动态照片。

---

## ✨ 特性

- ✅ **批量转换**整个文件夹（MP4 / MKV / WebM / AVI / MOV / FLV / TS 等）
- ✅ **封面取视频中间帧**，比第一帧好看
- ✅ 输出 `MVIMG_*.jpg`，符合动态照片命名惯例
- ✅ 自动探测 ffmpeg，纯 PowerShell，无其他依赖
- ✅ 跨平台：Windows / macOS / Linux（PowerShell Core）

## ✅ 兼容性（实测验证）

| 平台 | 结果 |
|---|---|
| 小米 / 红米相册 | ✅ 实测通过（本项目的适配目标） |
| 微信 | ✅ 实测可发送、查看为实况 |
| iOS 相册（iOS 13+） | ✅ 实测保存为 Live Photo |
| 鸿蒙 / 华为相册 | ✅ 实测保存为动态照片 |
| 三星及其他安卓 | ✅ 理论上支持（同属 Google 生态） |

---

## 🔬 原理

输出文件结构与小米系统保存的动态照片完全一致：

```
FF D8                # JPEG 头
APP1 (Exif)          # 必需，无 Exif 小米相册不识别
APP1 (XMP)           # Motion Photo 元数据（Adobe 属性式）
APP0 (JFIF)
DQT  x2              # 两个独立量化表段
SOF0
DHT  x4              # 四个独立霍夫曼表段
SOS + 图像数据       # 封面（视频中间帧）
MP4 数据             # H.264 + AAC，time_base=1/90000
```

XMP 元数据（`http://ns.google.com/photos/1.0/camera/` 命名空间）：

```xml
<rdf:Description xmlns:GCamera="http://ns.google.com/photos/1.0/camera/"
    GCamera:MicroVideoVersion="1"
    GCamera:MicroVideo="1"
    GCamera:MicroVideoOffset="<MP4长度>"
    GCamera:MicroVideoPresentationTimestampUs="<封面帧时间戳(微秒)>"/>
```

### 🕳️ 踩坑记录（全网稀缺的硬核经验）

这些是「微信能识别、小米相册不识别」的隐藏条件，网上教程大多缺失或错误：

1. **`MicroVideoOffset` 是 MP4 数据长度**，不是视频起始偏移。相册用 `文件总大小 - offset` 定位视频。
2. **XMP 必须是 Adobe 属性式**（`GCamera:MicroVideo="1"` 双引号、无 `<?xpacket?>`、无 padding）。exiftool 默认写出的元素式 XMP，微信认、小米不认。
3. **JPEG 必须有 Exif 段**，否则小米相册直接忽略。
4. **DQT 拆成 2 个独立段、DHT 拆成 4 个独立段**（ffmpeg 默认是合并段）。
5. **MP4 的 time_base 必须是 1/90000**（Android 相机标准；ffmpeg 默认 1/15360）。
6. **文件名 `MVIMG_` 前缀**（小米/Google 惯例）。
7. **封面取视频中间帧**（`-ss <时长/2>`），第一帧常是黑屏。

---

## ❓ 常见问题

**Q：微信直发 .jpg 不行，为什么？**
微信把图片当普通图片压缩重编码，内嵌 MP4 被破坏。用 zip 打包发送即可。

**Q：生成的文件多大？**
约等于原视频转码后大小 + 封面图片大小（几十 KB ~ 几 MB）。

**Q：视频会被重新编码吗？**
会，统一转成 H.264 + AAC（CRF 23 质量），无法无损直通。

**Q：我的手机不是小米，能用吗？**
能。格式是 Google 开放标准，iOS / 鸿蒙 / 三星都支持（见兼容性表）。

**Q：Exif 段内容是什么？**
固定通用模板（无个人隐私信息），提取自小米系统保存的 MVIMG 文件，已验证可识别。

---

## 🤝 贡献

欢迎提 Issue 和 PR！

- 🐛 遇到识别问题：请附上「微信保存后的动态照片文件」作为对照样本（这是排查的关键）
- 🌐 翻译 / 文档改进
- 💡 新功能建议（如批量压缩、命令行参数扩展）

开发流程：

```powershell
git clone https://github.com/lzxlovestudy/mp4-to-microvideo.git
git checkout -b your-feature
# 修改代码...
git add .
git commit -m "describe your change"
git push origin your-feature
# 在 GitHub 上发起 Pull Request
```

## 📄 License

[MIT](LICENSE) — 随便用，保留版权声明即可。
