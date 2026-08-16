using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Mp4ToMicroVideo
{
    public class VideoItem
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public double Duration { get; set; }
        public long Size { get; set; }
        public bool Selected { get; set; }
    }

    public class MainForm : Form
    {
        private TextBox txtDir;
        private Button btnBrowse, btnScan, btnConvert;
        private DataGridView grid;
        private ProgressBar progress;
        private Label lblStatus, lblCount;
        private TextBox txtLog;
        private string ffmpegPath, ffprobePath;
        private List<VideoItem> videos = new List<VideoItem>();
        private BackgroundWorker scanWorker, convWorker;

        // Exif 模板 (小米 MVIMG, base64)
        private static readonly byte[] ExifTemplate = Convert.FromBase64String(
            "/+EAakV4aWYAAE1NACoAAAAIAAQBAAAEAAAAAQAAAoABAQAEAAAAAQAAAWiHaQAEAAAAAQAAAD4BEgAEAAAAAQAAAAAAAAAAAAKaAQABAAAAAQEAAACSCAAEAAAAAQAAAAAAAAAAAAAAAAAA");

        public MainForm()
        {
            Text = "视频整理助手 - 3秒视频转动态照片";
            Font = new Font("Microsoft YaHei UI", 9F);
            Size = new Size(900, 640);
            StartPosition = FormStartPosition.CenterScreen;

            // 布局
            var panel = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8, 8, 8, 0) };
            txtDir = new TextBox { Location = new Point(8, 10), Width = 560 };
            btnBrowse = new Button { Text = "选择文件夹", Location = new Point(576, 8), Width = 100 };
            btnScan = new Button { Text = "扫描3s视频", Location = new Point(684, 8), Width = 100, BackColor = Color.LightSteelBlue };
            btnBrowse.Click += (s, e) => {
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "选择要整理的视频文件夹";
                    if (fbd.ShowDialog() == DialogResult.OK) { txtDir.Text = fbd.SelectedPath; }
                }
            };
            btnScan.Click += (s, e) => StartScan();
            panel.Controls.AddRange(new Control[] { txtDir, btnBrowse, btnScan });

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "sel", HeaderText = "转换", Width = 50, FillWeight = 8 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "文件名", FillWeight = 55 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "dur", HeaderText = "时长(秒)", FillWeight = 12 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "size", HeaderText = "大小", FillWeight = 15 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "out", HeaderText = "状态", FillWeight = 15 });

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 170, Padding = new Padding(8) };
            progress = new ProgressBar { Dock = DockStyle.Top, Height = 18, Margin = new Padding(0, 0, 0, 4) };
            lblStatus = new Label { Dock = DockStyle.Top, Height = 22, Text = "就绪", TextAlign = ContentAlignment.MiddleLeft };
            txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
            bottom.Controls.Add(txtLog);
            bottom.Controls.Add(lblStatus);
            bottom.Controls.Add(progress);

            var mid = new Panel { Dock = DockStyle.Fill };
            lblCount = new Label { Dock = DockStyle.Top, Height = 24, Text = "共 0 个视频", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            mid.Controls.Add(grid);
            mid.Controls.Add(lblCount);

            btnConvert = new Button { Text = "转换为动态照片", Dock = DockStyle.Bottom, Height = 36, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), BackColor = Color.LightGreen };
            btnConvert.Click += (s, e) => StartConvert();

            Controls.Add(mid);
            Controls.Add(bottom);
            Controls.Add(panel);
            Controls.Add(btnConvert);

            // 后台 worker: 扫描
            scanWorker = new BackgroundWorker();
            scanWorker.WorkerReportsProgress = true;
            scanWorker.DoWork += ScanWorker_DoWork;
            scanWorker.ProgressChanged += ScanWorker_Progress;
            scanWorker.RunWorkerCompleted += ScanWorker_Completed;

            // 后台 worker: 转换
            convWorker = new BackgroundWorker();
            convWorker.WorkerReportsProgress = true;
            convWorker.DoWork += ConvWorker_DoWork;
            convWorker.ProgressChanged += ConvWorker_Progress;
            convWorker.RunWorkerCompleted += ConvWorker_Completed;

            DetectFfmpeg();
        }

        private void DetectFfmpeg()
        {
            string[] cands = {
                "D:\\msys2\\ucrt64\\bin\\ffmpeg.exe",
                "C:\\msys2\\ucrt64\\bin\\ffmpeg.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\WinGet\\Packages"),
                "C:\\Program Files\\ffmpeg\\bin\\ffmpeg.exe"
            };
            foreach (var c in cands)
            {
                if (File.Exists(c)) { ffmpegPath = c; break; }
            }
            if (ffmpegPath == null)
            {
                var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in pathVar.Split(';'))
                {
                    try
                    {
                        var f = Path.Combine(dir.Trim('"'), "ffmpeg.exe");
                        if (File.Exists(f)) { ffmpegPath = f; break; }
                    } catch { }
                }
            }
            if (ffmpegPath != null)
            {
                ffprobePath = Path.Combine(Path.GetDirectoryName(ffmpegPath), "ffprobe.exe");
                if (!File.Exists(ffprobePath)) ffprobePath = null;
                Log("找到 ffmpeg: " + ffmpegPath);
            }
            else
            {
                Log("警告: 未找到 ffmpeg。请先安装 (winget install Gyan.FFmpeg)");
            }
        }

        // ================= 扫描 (后台线程) =================

        private void StartScan()
        {
            var dir = txtDir.Text.Trim();
            if (dir.Length == 0 || !Directory.Exists(dir)) { MessageBox.Show("请选择有效的文件夹"); return; }
            if (ffprobePath == null) { MessageBox.Show("未找到 ffprobe，无法扫描"); return; }
            if (scanWorker.IsBusy) return;

            videos.Clear();
            grid.Rows.Clear();
            lblStatus.Text = "扫描中...";
            btnScan.Enabled = false;
            btnConvert.Enabled = false;
            scanWorker.RunWorkerAsync(dir);
        }

        private void ScanWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var worker = (BackgroundWorker)sender;
            string dir = (string)e.Argument;
            string[] exts = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".mpg", ".3gp" };
            var files = new List<string>();
            try { foreach (var f in Directory.GetFiles(dir)) { var ext = Path.GetExtension(f).ToLower(); if (Array.IndexOf(exts, ext) >= 0) files.Add(f); } }
            catch (Exception ex) { e.Result = "读取文件夹失败: " + ex.Message; return; }

            var found = new List<VideoItem>();
            for (int idx = 0; idx < files.Count; idx++)
            {
                if (worker.CancellationPending) break;
                double dur = GetDuration(files[idx]);
                if (dur >= 2.5 && dur <= 3.5)
                {
                    found.Add(new VideoItem { Path = files[idx], Name = Path.GetFileName(files[idx]), Duration = dur, Size = new FileInfo(files[idx]).Length, Selected = true });
                }
                worker.ReportProgress((idx + 1) * 100 / Math.Max(1, files.Count), string.Format("扫描中 {0}/{1}: {2}", idx + 1, files.Count, Path.GetFileName(files[idx])));
            }
            e.Result = new object[] { files.Count, found };
        }

        private void ScanWorker_Progress(object sender, ProgressChangedEventArgs e)
        {
            lblStatus.Text = e.UserState as string ?? "扫描中...";
        }

        private void ScanWorker_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            btnScan.Enabled = true;
            btnConvert.Enabled = true;
            if (e.Error != null)
            {
                lblStatus.Text = "扫描出错";
                Log("扫描出错: " + e.Error.Message);
                MessageBox.Show("扫描出错: " + e.Error.Message);
                return;
            }
            if (e.Result is string)
            {
                lblStatus.Text = (string)e.Result;
                MessageBox.Show((string)e.Result);
                return;
            }
            var arr = (object[])e.Result;
            int total = (int)arr[0];
            videos = (List<VideoItem>)arr[1];
            RefreshGrid();
            lblStatus.Text = string.Format("扫描完成: 共 {0} 个视频, 其中 {1} 个时长约3秒", total, videos.Count);
            Log(string.Format("扫描完成: 共 {0} 个视频文件, 3秒左右的 {1} 个", total, videos.Count));
        }

        private double GetDuration(string file)
        {
            try
            {
                var psi = new ProcessStartInfo(ffprobePath, "-v error -show_entries format=duration -of csv=p=0 \"" + file + "\"")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi))
                {
                    var readTask = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(8000))   // 8 秒超时, 防止坏文件卡死
                    {
                        try { p.Kill(); } catch { }
                        return -1;
                    }
                    var outp = readTask.Result;
                    double d;
                    if (double.TryParse(outp.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                }
            }
            catch { }
            return -1;
        }

        private void RefreshGrid()
        {
            grid.Rows.Clear();
            foreach (var v in videos)
            {
                int idx = grid.Rows.Add(v.Selected, v.Name, v.Duration.ToString("0.##"), FormatSize(v.Size), "");
                grid.Rows[idx].Tag = v;
            }
            lblCount.Text = "共 " + videos.Count + " 个约3秒视频 (勾选要转换的)";
        }

        private string FormatSize(long b)
        {
            if (b > 1024 * 1024) return (b / 1024.0 / 1024.0).ToString("0.0") + " MB";
            if (b > 1024) return (b / 1024.0).ToString("0.0") + " KB";
            return b + " B";
        }

        // ================= 转换 (后台线程) =================

        private void StartConvert()
        {
            if (ffmpegPath == null) { MessageBox.Show("未找到 ffmpeg"); return; }
            if (convWorker.IsBusy) return;
            var dir = txtDir.Text.Trim();
            if (dir.Length == 0) { MessageBox.Show("请先选择文件夹"); return; }
            if (videos.Count == 0) { MessageBox.Show("请先扫描"); return; }

            var toConvert = new List<VideoItem>();
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                var cell = grid.Rows[i].Cells["sel"];
                var v = grid.Rows[i].Tag as VideoItem;
                if (v != null && cell.Value != null && (bool)cell.Value) { v.Selected = true; toConvert.Add(v); }
                else if (v != null) v.Selected = false;
            }
            if (toConvert.Count == 0) { MessageBox.Show("请勾选要转换的视频"); return; }

            btnConvert.Enabled = false;
            btnScan.Enabled = false;
            progress.Maximum = toConvert.Count;
            progress.Value = 0;
            convWorker.RunWorkerAsync(new object[] { dir, toConvert });
        }

        private void ConvWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var worker = (BackgroundWorker)sender;
            var args = (object[])e.Argument;
            string dir = (string)args[0];
            var toConvert = (List<VideoItem>)args[1];
            string outDir = Path.Combine(dir, "LivePhotos");
            Directory.CreateDirectory(outDir);

            int done = 0, fail = 0;
            var results = new List<ConvResult>();
            foreach (var v in toConvert)
            {
                string tmpJpg = Path.Combine(Path.GetTempPath(), "lv_frame.jpg");
                string tmpMp4 = Path.Combine(Path.GetTempPath(), "lv_video.mp4");
                string outFile = Path.Combine(outDir, "MVIMG_" + Path.GetFileNameWithoutExtension(v.Name) + ".jpg");
                var cr = new ConvResult { Path = v.Path, Name = v.Name, OutFile = outFile, Ok = false, Msg = "" };
                try
                {
                    double dur = v.Duration;
                    double coverTime = 1.5;
                    if (dur < 3.5) coverTime = dur / 2;
                    int coverUs = (int)(coverTime * 1000000);

                    Run(ffmpegPath, string.Format("-nostdin -y -loglevel error -ss {0} -i \"{1}\" -frames:v 1 -q:v 2 -huffman default -force_duplicated_matrix 1 \"{2}\"",
                        coverTime.ToString("0.###", CultureInfo.InvariantCulture), v.Path, tmpJpg));
                    Run(ffmpegPath, string.Format("-nostdin -y -loglevel error -i \"{0}\" -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -c:a aac -b:a 96k -video_track_timescale 90000 -movflags +faststart \"{1}\"", v.Path, tmpMp4));

                    var jpg = File.ReadAllBytes(tmpJpg);
                    var mp4 = File.ReadAllBytes(tmpMp4);
                    var segs = ParseSegments(jpg);
                    byte[] dqt = null, dht = null, sof = null, sos = null, jfif = null;
                    foreach (var s in segs)
                    {
                        if (s.Marker == 0xDB && dqt == null) dqt = s.Data;
                        else if (s.Marker == 0xC4 && dht == null) dht = s.Data;
                        else if (s.Marker == 0xC0 && sof == null) sof = s.Data;
                        else if (s.Marker == 0xDA && sos == null) sos = s.Data;
                        else if (s.Marker == 0xE0 && jfif == null) jfif = s.Data;
                    }

                    var dqt1 = new byte[69]; dqt1[0] = 0xFF; dqt1[1] = 0xDB; dqt1[2] = 0; dqt1[3] = 67;
                    Array.Copy(dqt, 4, dqt1, 4, 65);
                    var dqt2 = new byte[69]; dqt2[0] = 0xFF; dqt2[1] = 0xDB; dqt2[2] = 0; dqt2[3] = 67;
                    Array.Copy(dqt, 69, dqt2, 4, 65);

                    var dhtSegs = new List<byte[]>();
                    int pos = 4;
                    while (pos < dht.Length)
                    {
                        int counts = 0;
                        for (int k = 0; k < 16; k++) counts += dht[pos + 1 + k];
                        int tableLen = 1 + 16 + counts;
                        var seg = new byte[4 + tableLen];
                        seg[0] = 0xFF; seg[1] = 0xC4;
                        int sl = 2 + tableLen;
                        seg[2] = (byte)((sl >> 8) & 0xFF); seg[3] = (byte)(sl & 0xFF);
                        Array.Copy(dht, pos, seg, 4, tableLen);
                        dhtSegs.Add(seg);
                        pos += tableLen;
                    }

                    string xmpText = MakeXmp(mp4.Length, coverUs);
                    var xmpPrefix = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");
                    var xmpBytes = Encoding.UTF8.GetBytes(xmpText);
                    int xmpSegLen = 2 + xmpPrefix.Length + xmpBytes.Length;
                    var xmpSeg = new byte[4 + xmpPrefix.Length + xmpBytes.Length];
                    xmpSeg[0] = 0xFF; xmpSeg[1] = 0xE1;
                    xmpSeg[2] = (byte)((xmpSegLen >> 8) & 0xFF); xmpSeg[3] = (byte)(xmpSegLen & 0xFF);
                    Array.Copy(xmpPrefix, 0, xmpSeg, 4, xmpPrefix.Length);
                    Array.Copy(xmpBytes, 0, xmpSeg, 4 + xmpPrefix.Length, xmpBytes.Length);

                    using (var fs = File.Create(outFile))
                    {
                        fs.WriteByte(0xFF); fs.WriteByte(0xD8);
                        fs.Write(ExifTemplate, 0, ExifTemplate.Length);
                        fs.Write(xmpSeg, 0, xmpSeg.Length);
                        if (jfif != null) fs.Write(jfif, 0, jfif.Length);
                        fs.Write(dqt1, 0, dqt1.Length);
                        fs.Write(dqt2, 0, dqt2.Length);
                        if (sof != null) fs.Write(sof, 0, sof.Length);
                        foreach (var seg in dhtSegs) fs.Write(seg, 0, seg.Length);
                        if (sos != null) fs.Write(sos, 0, sos.Length);
                        fs.Write(mp4, 0, mp4.Length);
                    }
                    cr.Ok = true;
                    cr.Msg = "OK";
                }
                catch (Exception ex)
                {
                    fail++;
                    cr.Msg = ex.Message;
                }
                done++;
                results.Add(cr);
                worker.ReportProgress(done, cr);
            }
            e.Result = new object[] { done, fail, results };
        }

        private void ConvWorker_Progress(object sender, ProgressChangedEventArgs e)
        {
            var cr = e.UserState as ConvResult;
            progress.Value = e.ProgressPercentage;
            if (cr != null)
            {
                lblStatus.Text = "转换中 " + e.ProgressPercentage + "/" + progress.Maximum + ": " + cr.Name;
                for (int i = 0; i < grid.Rows.Count; i++)
                {
                    var gi = grid.Rows[i].Tag as VideoItem;
                    if (gi != null && gi.Path == cr.Path)
                    {
                        grid.Rows[i].Cells["out"].Value = cr.Ok ? "✅ 已转换" : "❌ 失败";
                        break;
                    }
                }
                Log((cr.Ok ? "OK: " : "失败: ") + cr.Name + (cr.Ok ? "" : " : " + cr.Msg));
            }
        }

        private void ConvWorker_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            btnConvert.Enabled = true;
            btnScan.Enabled = true;
            if (e.Error != null)
            {
                lblStatus.Text = "转换出错";
                Log("转换出错: " + e.Error.Message);
                MessageBox.Show("转换出错: " + e.Error.Message);
                return;
            }
            var arr = (object[])e.Result;
            int done = (int)arr[0], fail = (int)arr[1];
            lblStatus.Text = string.Format("完成: 成功 {0} 个, 失败 {1} 个", done - fail, fail);
            MessageBox.Show(string.Format("转换完成!\n成功 {0} 个, 失败 {1} 个\n\n提示: 用 zip 压缩后发到手机, 微信直发会压缩破坏格式", done - fail, fail));
        }

        private class ConvResult { public string Path; public string Name; public string OutFile; public bool Ok; public string Msg; }

        private List<SegInfo> ParseSegments(byte[] b)
        {
            var list = new List<SegInfo>();
            int i = 2;
            while (i < b.Length - 4)
            {
                if (b[i] != 0xFF) break;
                int m = b[i + 1];
                if (m == 0xDA)
                {
                    var d = new byte[b.Length - i];
                    Array.Copy(b, i, d, 0, d.Length);
                    list.Add(new SegInfo { Marker = m, Data = d });
                    break;
                }
                int len = (b[i + 2] << 8) | b[i + 3];
                var data = new byte[len + 2];
                Array.Copy(b, i, data, 0, len + 2);
                list.Add(new SegInfo { Marker = m, Data = data });
                i += 2 + len;
            }
            return list;
        }

        private class SegInfo { public int Marker; public byte[] Data; }

        private string MakeXmp(int offset, int tsUs)
        {
            return string.Format(
                "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\" x:xmptk=\"Adobe XMP Core 5.1.0-jc003\">\n" +
                "  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
                "    <rdf:Description rdf:about=\"\"\n" +
                "        xmlns:GCamera=\"http://ns.google.com/photos/1.0/camera/\"\n" +
                "      GCamera:MicroVideoVersion=\"1\"\n" +
                "      GCamera:MicroVideo=\"1\"\n" +
                "      GCamera:MicroVideoOffset=\"{0}\"\n" +
                "      GCamera:MicroVideoPresentationTimestampUs=\"{1}\"/>\n" +
                "  </rdf:RDF>\n" +
                "</x:xmpmeta>\n", offset, tsUs);
        }

        private void Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                // 异步读输出, 防止管道缓冲死锁 (经典 .NET 坑)
                var soTask = p.StandardOutput.ReadToEndAsync();
                var seTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(90000))
                {
                    try { p.Kill(); } catch { }
                    throw new Exception("ffmpeg 超时 (90秒)");
                }
                soTask.Wait(5000);
                seTask.Wait(5000);
                if (p.ExitCode != 0) throw new Exception("ffmpeg 返回码 " + p.ExitCode);
            }
        }

        private void Log(string msg)
        {
            if (txtLog.TextLength > 50000) txtLog.Clear();
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss ") + msg + Environment.NewLine);
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
