using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HalconDotNet;
using Microsoft.Win32;
using PcbPinInspection.Models;
using PcbPinInspection.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using 引脚检测.Services;
using 引脚检测.Views;
namespace PcbPinInspection.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HalconUiService _ui;

    public MainViewModel(HalconUiService ui)
    {
        _ui = ui;
    }



    // ====================== 绑定属性 ======================
    [ObservableProperty]
    private string _templatePath =
        @"C:\Users\mengxiao\AppData\Local\Programs\MVTec\HALCON-25.11-Progress\examples\hdevelop\Deep-Learning\AI2Interfaces";

    [ObservableProperty] private double _minMatchScore = 0.7;
    [ObservableProperty] private double _sigma = 1.0;
    [ObservableProperty] private double _edgeThreshold = 20.0;
    [ObservableProperty] private double _pinWidthMin = 22.0;
    [ObservableProperty] private double _pinWidthMax = 27.0;

    [ObservableProperty] private string _statusText = "空闲";
    [ObservableProperty] private string _statusColor = "Green";
    [ObservableProperty] private double _matchScore;
    [ObservableProperty] private string _elapsedMs = "耗时：0 ms";
    [ObservableProperty] private string _logText = "";

    // ====================== 内部状态 ======================
    private HImage _goldenImage;                 // 金样图像（离线段使用）
    private CancellationTokenSource _cts;        // 连续检测取消源

    public void Log(string msg, Serilog.Events.LogEventLevel level = Serilog.Events.LogEventLevel.Information)
    {
        // 1) Serilog 写文件
        Serilog.Log.Write(level, "{Msg}", msg);

        // 2) UI 日志框追加（切回 UI 线程）
        Application.Current.Dispatcher.Invoke(() =>
        {
            string prefix = level == Serilog.Events.LogEventLevel.Error ? "❌ "
                          : level == Serilog.Events.LogEventLevel.Warning ? "⚠ " : "";
            LogText += $"[{DateTime.Now:HH:mm:ss}] {prefix}{msg}\n";
        });
    }

    [RelayCommand]
    private void BrowseTemplatePath()
    {
        try
        {
            var dlg = new OpenFolderDialog
            {
                Title = "选择模板保存/加载目录",
                InitialDirectory = Directory.Exists(TemplatePath) ? TemplatePath : ""
            };
            if (dlg.ShowDialog() == true)
            {
                TemplatePath = dlg.FolderName;
                Log($"模板目录已切换为：{TemplatePath}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开目录选择框失败：{ex.Message}\n\n(.NET 8 才内置 OpenFolderDialog；" +
                            "若你在用 .NET Framework，请直接手动在文本框里粘贴路径。)",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

    }
    // ============================================================
    //  按钮 1：加载图像（金样）
    // ============================================================
    [RelayCommand]
    private void LoadImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "图片|*.png;*.bmp;*.jpg;*.jpeg;*.tif|所有文件|*.*",
            Title = "选择标准金样图像"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            if (_ui.HalconWindow == null)
            {
                MessageBox.Show("HALCON 窗口尚未初始化完成，请稍等 1 秒再点加载。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _goldenImage?.Dispose();
            _goldenImage = new HImage(dlg.FileName);

            // 读取图像宽高
            HOperatorSet.GetImageSize(_goldenImage, out HTuple iw, out HTuple ih);
            double imgW = iw.D;
            double imgH = ih.D;

            var hw = _ui.HalconWindow;

            _ui.Clear();
            _ui.Display(_goldenImage);
            StatusText = "已加载金样";
            StatusColor = "ForestGreen";
            Log($"加载金样：{dlg.FileName}  尺寸 {imgW}x{imgH}");
        }
        catch (Exception ex)
        {
            StatusText = "加载失败";
            StatusColor = "Red";
            Log($"❌ 加载图像失败：{ex.Message}", Serilog.Events.LogEventLevel.Error);
            MessageBox.Show($"加载图像失败：\n\n{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    // ============================================================
    //  按钮 2：训练模板（离线段：画模板ROI + 画测量ROI + 保存三件套）
    // ============================================================

    private CancellationTokenSource _trainCts;
    [RelayCommand]
    private async Task TrainTemplate()
    {
        if (_goldenImage == null)
        {
            Log("⚠ 请先点击【加载图像】载入金样");
            return;
        }

        // 创建取消令牌
        _trainCts = new CancellationTokenSource();
        var token = _trainCts.Token;

        try
        {
            StatusText = "训练中...";
            var sw = Stopwatch.StartNew();

            // 1. 灰度化
            HImage gray = _goldenImage.Rgb1ToGray();

            // 2. 鼠标框选模板 ROI（矩形1）【传入token支持ESC取消】
            _ui.Message("👉 请在窗口中拖框选择【模板区域】，按ESC取消", "yellow");
            HTuple rect1 = await _ui.DrawRectangle1Interactive(token);
            ClearMessage();
            double r1 = rect1[0].D, c1 = rect1[1].D, r2 = rect1[2].D, c2 = rect1[3].D;
            HObject rectangle;
            HOperatorSet.GenRectangle1(out rectangle, r1, c1, r2, c2);

            // 3. 计算模板中心（基准点）
            HOperatorSet.AreaCenter(rectangle, out HTuple areaTpl, out HTuple baseRow, out HTuple baseCol);

            // 4. 保存基准坐标 model_base.tup
            Directory.CreateDirectory(TemplatePath);
            HTuple baseTuple = new(baseRow.D, baseCol.D);
            string baseTupPath = Path.Combine(TemplatePath, "model_base.tup");
            HOperatorSet.WriteTuple(baseTuple, baseTupPath);

            // 5. 裁剪模板区域 + 创建形状模板
            HRegion roiRegion = new HRegion(rectangle);
            HImage reduced = gray.ReduceDomain(roiRegion);

            HTuple modelId;
            HOperatorSet.CreateShapeModel(reduced, "auto", 0, Math.PI * 2, "auto", "auto",
                "use_polarity", "auto", "auto", out modelId);

            // 6. 鼠标框选测量 ROI（矩形2，可旋转）【传入token支持ESC取消】
            _ui.Message("👉 请在窗口中拖框选择【引脚测量区域】(旋转框)，按ESC取消", "yellow");
            HTuple rect2 = await _ui.DrawRectangle2Interactive(token);
            ClearMessage();
            double mr = rect2[0].D, mc = rect2[1].D, mphi = rect2[2].D, ml1 = rect2[3].D, ml2 = rect2[4].D;
            HObject measureRoi;
            HOperatorSet.GenRectangle2(out measureRoi, mr, mc, mphi, ml1, ml2);
            _ui.SetColor("green");
            _ui.Display(measureRoi);

            // 7. 保存模板 + ROI
            string shmPath = Path.Combine(TemplatePath, "几何定位.shm");
            string roiPath = Path.Combine(TemplatePath, "ROI.hobj");
            HOperatorSet.WriteShapeModel(modelId, shmPath);
            HOperatorSet.WriteRegion(measureRoi, roiPath);

            // 8. 释放
            HOperatorSet.ClearShapeModel(modelId);
            rectangle.Dispose();
            measureRoi.Dispose();
            gray.Dispose();
            reduced.Dispose();

            sw.Stop();
            StatusText = "训练完成";
            StatusColor = "ForestGreen";
            ElapsedMs = $"耗时：{sw.ElapsedMilliseconds} ms";
            Log($"✅ 模板与 ROI 保存完成（{sw.ElapsedMilliseconds} ms）");
            Log($"   模板：{shmPath}");
            Log($"   ROI ：{roiPath}");
            Log($"   基准：{baseTupPath}");
            _ui.Message("✅ 模板与 ROI 保存完成", "green");
        }
        catch (OperationCanceledException)
        {
            ClearMessage();
            // ESC取消，正常退出流程，不是错误
            StatusText = "训练已取消";
            StatusColor = "Orange";
            Log("⚠ 用户按下ESC，取消模板训练");
            _ui.Message("⚠ 模板训练已取消", "orange");
        }
        catch (Exception ex)
        {
            ClearMessage();
            StatusText = "训练失败";
            StatusColor = "Red";
            Log($"❌ 训练模板失败：{ex.Message}");
        }
        finally
        {
            // 释放cts
            _trainCts?.Dispose();
            _trainCts = null;
        }
    }

    /// <summary>清除窗口叠加的文字提示</summary>
    public void ClearMessage()
    {
        // 思路1：重新绘制窗口（推荐，如果Message是用DispText叠加在Halcon窗口）
        _ui.HalconWindow.ClearWindow();


    }


    // ============================================================
    //  按钮 3：单次检测
    // ============================================================
    [RelayCommand]
    private async Task SingleDetect()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "图片|*.png;*.bmp;*.jpg;*.jpeg;*.tif",
            Title = "选择待检测图像"
        };
        if (dlg.ShowDialog() != true) return;

        await RunOneImageAsync(dlg.FileName);
    }

    // ============================================================
    //  按钮 4：连续检测（按文件名顺序遍历文件夹，NG 打日志）
    // ============================================================
    [RelayCommand]
    private async Task ContinuousDetect()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择待检测图片文件夹"
        };
        if (dlg.ShowDialog() != true) return;
        string dir = dlg.FolderName;

        string[] exts = { ".png", ".bmp", ".jpg", ".jpeg", ".tif" };
        var files = Directory.GetFiles(dir, "*.*")
                             .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                             .ToArray();
        if (files.Length == 0)
        {
            Log("⚠ 文件夹中没有图片");
            return;
        }

        _cts = new CancellationTokenSource();
        Log($"开始连续检测，共 {files.Length} 张，目录：{dir}");

        int okCount = 0, ngCount = 0;
        foreach (var file in files)
        {
            if (_cts.IsCancellationRequested)
            {
                Log("⏹ 用户主动停止连续检测");
                break;
            }

            var res = await RunOneImageAsync(file);
            if (res.ok) okCount++; else ngCount++;
        }

        Log($"连续检测结束：OK {okCount} 张，NG {ngCount} 张");
        StatusText = "空闲";
        StatusColor = "ForestGreen";
    }

    // ============================================================
    //  按钮 5：停止（仅连续检测时有效）
    // ============================================================
    [RelayCommand]
    private void Stop()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            Log("⏹ 已请求停止连续检测...");
        }
    }

    // ============================================================
    //  按钮 6：历史查询
    // ============================================================
    [RelayCommand]
    private void OpenHistory()
    {
        new HistoryWindow().Show();
    }

    // ============================================================
    //  核心：单张图检测（在线段）
    // ============================================================
    private async Task<(bool ok, string reason)> RunOneImageAsync(string imagePath)
    {
        try
        {
            StatusText = "检测中...";
            var sw = Stopwatch.StartNew();

            // 视觉算法，后台线程执行
            var (ok, reason, score, pinCount, minW, maxW) = await Task.Run(() => DetectCore(imagePath));
            sw.Stop();
            long visionMs = sw.ElapsedMilliseconds;
            MatchScore = score;
            ElapsedMs = $"耗时：{visionMs} ms";

            // ===== 数据库写入：放到后台，不卡UI，不计入视觉检测耗时 =====
            var record = new InspectionRecord
            {
                ImageName = Path.GetFileName(imagePath),
                ImagePath = imagePath,
                IsOK = ok,
                Reason = reason,
                MatchScore = score,
                PinCount = pinCount,
                MinPinWidth = minW,
                MaxPinWidth = maxW,
                ElapsedMs = visionMs,
                CreatedAt = DateTime.Now
            };
            // 异步入库，独立任务，加异常捕获防止数据库崩掉影响检测流程
            _ = Task.Run(async () =>
            {
                try
                {
                    await DbService.SaveAsync(record); // 推荐FreeSql异步SaveAsync
                }
                catch (Exception dbEx)
                {
                    Log($"数据库保存记录失败：{dbEx.Message}", Serilog.Events.LogEventLevel.Error);
                }
            });

            if (ok)
            {
                StatusText = "OK";
                StatusColor = "ForestGreen";
                Log($"✅ {Path.GetFileName(imagePath)}  OK  分数={score:F2}  引脚数={pinCount}  ({visionMs} ms)");
            }
            else
            {
                StatusText = "NG";
                StatusColor = "Red";
                Log($"❌ {Path.GetFileName(imagePath)}  NG：{reason}  分数={score:F2}", Serilog.Events.LogEventLevel.Warning);
            }
            return (ok, reason);
        }
        catch (Exception ex)
        {
            StatusText = "异常";
            StatusColor = "Red";
            Log($"❌ {Path.GetFileName(imagePath)}  检测异常：{ex.Message}", Serilog.Events.LogEventLevel.Error);
            return (false, ex.Message);
        }
    }


    /// <summary>
    /// 在线检测核心：读图 → 灰度 → 光照校正 → 模板匹配 → ROI 仿射 → measure_pairs → 判定
    /// 注意：此方法运行在后台线程，所有显示操作必须 Dispatcher 回 UI 线程。
    /// </summary>
    /// <summary>
    /// 在线检测核心：读图 → 灰度 → 光照校正 → 模板匹配 → ROI 仿射 → measure_pairs → 判定
    /// 注意：此方法运行在后台线程，所有显示操作必须 Dispatcher 回 UI 线程。
    /// </summary>
    private (bool ok, string reason, double score, int pinCount, double minW, double maxW) DetectCore(string imagePath)
    {
        HImage image = null, gray = null, corr = null;
        HObject measureRoiTplObj = null;
        HTuple modelId = null;
        HTuple measureHandle = null;
        //HRegion measureRoiTplReg;
        try
        {
            // 1. 读图 + 灰度
            image = new HImage(imagePath);
            gray = image.Rgb1ToGray();

            // 2. 光照校正：HOperatorSet 输出一律 out HObject
           // HOperatorSet.Illuminate(gray, out HObject corrObj, 50, 50, 0.6);
            corr = new HImage(gray);

    
            // 3. 加载 ROI、模板、基准
            HTuple roiPath = new HTuple(Path.Combine(TemplatePath, "ROI.hobj"));
            HTuple shmPath = new HTuple(Path.Combine(TemplatePath, "几何定位.shm"));
            HTuple tupPath = new HTuple(Path.Combine(TemplatePath, "model_base.tup"));

            // 去掉重复的 HObject 声明，参数 1 为 out 对象，参数 2 为路径
            HOperatorSet.ReadRegion(out measureRoiTplObj, roiPath);


            HOperatorSet.ReadShapeModel(shmPath, out modelId);
            // HTuple 判断模板是否加载成功：TupleLength>0
            if (modelId.TupleLength() <= 0)
                return (false, "模板文件读取失败", 0, 0, 0, 0);

            HTuple baseTuple;
            HOperatorSet.ReadTuple(tupPath, out baseTuple);
            double modelY = baseTuple[0].D;
            double modelX = baseTuple[1].D;

            // 4. 模板匹配
            HOperatorSet.FindShapeModel(corr, modelId, -Math.PI, Math.PI,
                MinMatchScore, 0, 0.5, "least_squares", 0, 0.9,
                out HTuple row, out HTuple col, out HTuple angle, out HTuple score);

            if (row.TupleLength() <= 0 || score[0].D < MinMatchScore)
            {
                return (false, "模板匹配失败", score.TupleLength() > 0 ? score[0].D : 0.0, 0, 0, 0);
            }
            double rowM = row[0].D, colM = col[0].D, angM = angle[0].D, sc = score[0].D;

            // 5. 刚体仿射矩阵
            HOperatorSet.VectorAngleToRigid(modelY, modelX, 0, rowM, colM, angM, out HTuple homMat);

            // 6. 测量 ROI 跟随工件变换
            HObject roiTrans;
            HOperatorSet.AffineTransRegion(measureRoiTplObj, out roiTrans, homMat, "nearest_neighbor");

            // 7. 模板轮廓投影（调试用）
            HOperatorSet.GetShapeModelContours(out HObject projContours, modelId, 1);
            HOperatorSet.AffineTransContourXld(projContours, out HObject projContoursTrans, homMat);

            // ===== 回 UI 线程画图 =====
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _ui.Clear();
                _ui.Display(image);
                _ui.SetDraw("margin");
                // 模板中心十字（蓝）
                HOperatorSet.GenCrossContourXld(out HObject crossMark, rowM, colM, 66, angM);
                _ui.SetColor("blue");
                _ui.Display(crossMark);
               // _ui.Display(projContoursTrans);
                // 测量 ROI（绿）
                _ui.SetColor("green");
                _ui.Display(roiTrans);
            });

            // 8. 从变换后 ROI 取 rectangle2 参数
            HOperatorSet.SmallestRectangle2(roiTrans,
                out HTuple rMeas, out HTuple cMeas, out HTuple phiMeas,
                out HTuple len1, out HTuple len2);
            HOperatorSet.GetImageSize(corr, out HTuple imgW, out HTuple imgH);
            HOperatorSet.GenMeasureRectangle2(rMeas, cMeas, phiMeas, len1, len2,
                imgW, imgH, "nearest_neighbor", out measureHandle);

            // measureHandle 是HTuple，用TupleLength判断
            if (measureHandle.TupleLength() <= 0)
            {
                return (false, "测量句柄创建失败", sc, 0, 0, 0);
            }

            // 9. 亚像素边沿检测
            HOperatorSet.MeasurePairs(corr, measureHandle, Sigma, EdgeThreshold, "all", "all",
                out HTuple rEdgeF, out HTuple cEdgeF, out HTuple ampF,
                out HTuple rEdgeS, out HTuple cEdgeS, out HTuple ampS,
                out HTuple intraDist, out HTuple interDist);

            if (intraDist.TupleLength() <= 0)
            {
                return (false, "未检测到引脚边沿", sc, 0, 0, 0);
            }

            // 10. 尺寸判定
            bool isNG = false;
            string firstReason = "";
            int pinCount = intraDist.TupleLength();
            double minW = double.MaxValue, maxW = double.MinValue;

            // 引脚宽度 IntraDistance
            for (int i = 0; i < intraDist.TupleLength(); i++)
            {
                double w = intraDist[i].D;
                if (w < minW) minW = w;
                if (w > maxW) maxW = w;
                if (w < PinWidthMin || w > PinWidthMax)
                {
                    isNG = true;
                    if (string.IsNullOrEmpty(firstReason))
                        firstReason = $"引脚[{i}]宽度={w:F1}px 超差(允许 {PinWidthMin}-{PinWidthMax})";
                    double rf = rEdgeF[i].D, cf = cEdgeF[i].D;
                    double rs = rEdgeS[i].D, cs = cEdgeS[i].D;
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        _ui.SetColor("red");
                        _ui.DisplayCross(rf, cf, 10, 0);
                        _ui.DisplayCross(rs, cs, 10, 0);
                    });
                }
            }

            // 引脚间距 InterDistance
            for (int i = 0; i < interDist.TupleLength(); i++)
            {
                double gap = interDist[i].D;
                if (gap < PinWidthMin || gap > PinWidthMax)
                {
                    isNG = true;
                    if (string.IsNullOrEmpty(firstReason))
                        firstReason = $"引脚[{i}]间距={gap:F1}px 超差(允许 {PinWidthMin}-{PinWidthMax})";
                    double midR = (rEdgeS[i].D + rEdgeF[i + 1].D) / 2.0;
                    double midC = (cEdgeS[i].D + cEdgeF[i + 1].D) / 2.0;
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        _ui.SetColor("red");
                        _ui.DisplayCross(midR, midC, 10, 0);
                    });
                }
            }

            return (isNG == false, isNG ? firstReason : "引脚尺寸合格", sc, pinCount, minW, maxW);
        }
        finally
        {
            try { if (measureHandle != null && measureHandle.TupleLength() > 0) HOperatorSet.CloseMeasure(measureHandle); } catch { }
            try { if (modelId != null && modelId.TupleLength() > 0) HOperatorSet.ClearShapeModel(modelId); } catch { }
            image?.Dispose();
            gray?.Dispose();
            corr?.Dispose();
            measureRoiTplObj?.Dispose();
        }
    }

          
}
