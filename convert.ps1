# ============================================================
#  mp4-to-microvideo  -  Convert videos to Xiaomi Live Photos
#  (小米/红米动态照片转换器, Google Motion Photo 格式)
#
#  用法:
#    .\convert.ps1 -InputDir "D:\videos" [-OutputDir "D:\videos\LivePhotos"]
#
#  依赖: ffmpeg (自动探测 PATH / 常见安装路径, 也可 -FfmpegPath 指定)
#  输出: MVIMG_<原名>.jpg, 传入手机后小米相册识别为动态照片
# ============================================================

param(
    [Parameter(Mandatory=$true)][string]$InputDir,
    [string]$OutputDir = "",
    [string]$FfmpegPath = ""
)

# ---------- ffmpeg 探测 ----------
function Find-Ffmpeg {
    $candidates = @(
        $FfmpegPath,
        (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source,
        "D:\msys2\ucrt64\bin\ffmpeg.exe",
        "C:\msys2\ucrt64\bin\ffmpeg.exe",
        "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\Gyan.FFmpeg*\ffmpeg-*-full_build\bin\ffmpeg.exe"
    ) | Where-Object { $_ }
    foreach ($c in $candidates) {
        if (Test-Path $c) { return (Resolve-Path $c).Path }
    }
    return $null
}

$ffmpeg = Find-Ffmpeg
if (-not $ffmpeg) { Write-Host "找不到 ffmpeg, 请用 -FfmpegPath 指定路径"; exit 1 }
$ffprobe = Join-Path (Split-Path $ffmpeg) "ffprobe.exe"

if (-not (Test-Path $InputDir)) { Write-Host "输入目录不存在: $InputDir"; exit 1 }
if (-not $OutputDir) { $OutputDir = Join-Path $InputDir "LivePhotos" }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$exts = '.mp4','.mkv','.avi','.mov','.wmv','.flv','.webm','.m4v','.ts','.mpg','.3gp'
$videos = Get-ChildItem -Path $InputDir -File | Where-Object { $exts -contains $_.Extension.ToLower() }
if ($videos.Count -eq 0) { Write-Host "该文件夹没有找到视频文件"; exit 0 }

# ---------- Exif 段模板 ----------
# 小米相册硬性要求 JPEG 带 Exif 段, 否则不识别为动态照片。
# 此模板提取自小米系统保存的 MVIMG 文件, 已验证可被小米相册识别。
$exifB64 = '/+EAakV4aWYAAE1NACoAAAAIAAQBAAAEAAAAAQAAAoABAQAEAAAAAQAAAWiHaQAEAAAAAQAAAD4BEgAEAAAAAQAAAAAAAAAAAAKaAQABAAAAAQEAAACSCAAEAAAAAQAAAAAAAAAAAAAAAAAA'
$wxExif = [Convert]::FromBase64String($exifB64)

function Make-Xmp([string]$offset, [string]$tsUs) {
@"
<x:xmpmeta xmlns:x="adobe:ns:meta/" x:xmptk="Adobe XMP Core 5.1.0-jc003">
  <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
    <rdf:Description rdf:about=""
        xmlns:GCamera="http://ns.google.com/photos/1.0/camera/"
      GCamera:MicroVideoVersion="1"
      GCamera:MicroVideo="1"
      GCamera:MicroVideoOffset="$offset"
      GCamera:MicroVideoPresentationTimestampUs="$tsUs"/>
  </rdf:RDF>
</x:xmpmeta>
"@
}

$i = 0
foreach ($v in $videos) {
    $i++
    $base    = [IO.Path]::GetFileNameWithoutExtension($v.Name)
    $tmpJpg  = Join-Path $env:TEMP 'lv_frame.jpg'
    $tmpMp4  = Join-Path $env:TEMP 'lv_video.mp4'
    $outFile = Join-Path $OutputDir ('MVIMG_' + $base + '.jpg')

    # 1. 抽中间帧作封面 (3s 视频取 1.5s; 短视频取中点; huffman default + 双DQT)
    $dur = & $ffprobe -v error -show_entries format=duration -of csv=p=0 $v.FullName 2>$null
    $coverTime = 1.5
    try { $durN = [double]$dur; if ($durN -lt 3.5) { $coverTime = $durN / 2 } } catch {}
    $coverUs = [int]($coverTime * 1000000)
    & $ffmpeg -nostdin -y -loglevel error -ss $coverTime -i $v.FullName -frames:v 1 -q:v 2 -huffman default -force_duplicated_matrix 1 $tmpJpg 2>&1 | Out-Null

    # 2. 转码 H.264 + AAC (time_base=1/90000 + moov 前置)
    & $ffmpeg -nostdin -y -loglevel error -i $v.FullName -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -c:a aac -b:a 96k -video_track_timescale 90000 -movflags +faststart $tmpMp4 2>&1 | Out-Null

    $jpg = [IO.File]::ReadAllBytes($tmpJpg)
    $mp4 = [IO.File]::ReadAllBytes($tmpMp4)
    $mp4Len = $mp4.Length

    # 3. 解析 JPEG 段
    $segs = New-Object System.Collections.ArrayList
    $p = 2
    while ($p -lt $jpg.Length - 4) {
        if ($jpg[$p] -ne 0xFF) { [void]$segs.Add([PSCustomObject]@{M=-1; Data=$null}); break }
        $m = $jpg[$p+1]
        if ($m -eq 0xDA) { [void]$segs.Add([PSCustomObject]@{M=0xDA; Data=[byte[]]$jpg[$p..($jpg.Length-1)]}); break }
        $len = [BitConverter]::ToUInt16([byte[]]@($jpg[$p+3],$jpg[$p+2]),0)
        [void]$segs.Add([PSCustomObject]@{M=$m; Data=[byte[]]$jpg[$p..($p+1+$len)]})
        $p += 2 + $len
    }
    $dqt  = ($segs | Where-Object { $_.M -eq 0xDB } | Select-Object -First 1).Data
    $dht  = ($segs | Where-Object { $_.M -eq 0xC4 } | Select-Object -First 1).Data
    $sof  = ($segs | Where-Object { $_.M -eq 0xC0 } | Select-Object -First 1).Data
    $sos  = ($segs | Where-Object { $_.M -eq 0xDA } | Select-Object -First 1).Data
    $jfif = ($segs | Where-Object { $_.M -eq 0xE0 } | Select-Object -First 1).Data

    # 4. 拆 DQT => 2 个独立段 (小米相册要求独立 DQT 段)
    $dqt1 = [byte[]]::new(69); $dqt1[0]=0xFF; $dqt1[1]=0xDB; $dqt1[2]=0; $dqt1[3]=67
    [Array]::Copy($dqt, 4, $dqt1, 4, 65)
    $dqt2 = [byte[]]::new(69); $dqt2[0]=0xFF; $dqt2[1]=0xDB; $dqt2[2]=0; $dqt2[3]=67
    [Array]::Copy($dqt, 69, $dqt2, 4, 65)

    # 5. 拆 DHT => 4 个独立段 (小米相册要求独立 DHT 段)
    $pos = 4
    $dhtSegs = New-Object System.Collections.ArrayList
    while ($pos -lt $dht.Length) {
        $counts = 0
        for ($k = 0; $k -lt 16; $k++) { $counts += $dht[$pos+1+$k] }
        $tableLen = 1 + 16 + $counts
        $seg = [byte[]]::new(4 + $tableLen)
        $seg[0]=0xFF; $seg[1]=0xC4
        $sl = 2 + $tableLen
        $seg[2]=[byte](($sl -shr 8) -band 0xFF); $seg[3]=[byte]($sl -band 0xFF)
        [Array]::Copy($dht, $pos, $seg, 4, $tableLen)
        [void]$dhtSegs.Add($seg)
        $pos += $tableLen
    }

    # 6. XMP 段 (Google Motion Photo, Adobe 属性式)
    $xmpText = Make-Xmp $mp4Len $coverUs
    $xmpPrefix = [Text.Encoding]::ASCII.GetBytes("http://ns.adobe.com/xap/1.0/`0")
    $xmpBytes = [Text.Encoding]::UTF8.GetBytes($xmpText)
    $xmpSegLen = 2 + $xmpPrefix.Length + $xmpBytes.Length
    $xmpSeg = [byte[]]::new(4 + $xmpPrefix.Length + $xmpBytes.Length)
    $xmpSeg[0]=0xFF; $xmpSeg[1]=0xE1
    $xmpSeg[2]=[byte](($xmpSegLen -shr 8) -band 0xFF); $xmpSeg[3]=[byte]($xmpSegLen -band 0xFF)
    [Array]::Copy($xmpPrefix, 0, $xmpSeg, 4, $xmpPrefix.Length)
    [Array]::Copy($xmpBytes, 0, $xmpSeg, 4+$xmpPrefix.Length, $xmpBytes.Length)

    # 7. 组装: FFD8 + Exif + XMP + JFIF + DQT*2 + SOF0 + DHT*4 + SOS数据 + MP4
    $out = New-Object System.Collections.Generic.List[byte]
    $out.Add(0xFF); $out.Add(0xD8)
    foreach ($x in $wxExif) { $out.Add($x) }
    foreach ($x in $xmpSeg) { $out.Add($x) }
    foreach ($x in $jfif) { $out.Add($x) }
    foreach ($x in $dqt1) { $out.Add($x) }
    foreach ($x in $dqt2) { $out.Add($x) }
    foreach ($x in $sof) { $out.Add($x) }
    foreach ($seg in $dhtSegs) { foreach ($x in $seg) { $out.Add($x) } }
    foreach ($x in $sos) { $out.Add($x) }
    foreach ($x in $mp4) { $out.Add($x) }

    [IO.File]::WriteAllBytes($outFile, $out.ToArray())
    Write-Host "[$i/$($videos.Count)] OK: $($v.Name) -> $outFile  (MP4=$mp4Len)"
}

Write-Host "`n完成! 共转换 $($videos.Count) 个, 输出目录: $OutputDir"
Write-Host "把生成的 MVIMG_*.jpg 传到手机, 小米相册识别为动态照片"
