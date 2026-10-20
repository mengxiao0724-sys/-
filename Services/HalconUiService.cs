using System.Threading.Tasks;
using HalconDotNet;

namespace 引脚检测.Services;

/// <summary>
/// 对 HSmartWindowControlWPF 的薄封装，让 ViewModel 不直接依赖具体 WPF 控件。
/// 所有需要落到 HALCON 窗口上的显示 / 交互式 ROI 绘制都走这里。
/// </summary>
public class HalconUiService
{
    private HSmartWindowControlWPF _host;

    public HWindow HalconWindow => _host?.HalconWindow;

    public void Attach(HSmartWindowControlWPF host) => _host = host;

    public void Clear()
    {
        if (_host?.HalconWindow != null)
            _host.HalconWindow.ClearWindow();
    }

    /// <summary>
    /// 显示 HObject / HImage / HRegion / HXLD。
    /// 如果是 HImage，先 SetPart 把窗口视野缩到图像大小，否则图可能画了但视图错位看不到。
    /// </summary>
    public void Display(HObject obj)
    {
        if (_host?.HalconWindow == null || obj == null) return;
        try
        {
            bool isImage = false;
            HTuple w = 0, h = 0;
            try
            {
                HOperatorSet.GetImageSize(obj, out w, out h);
                isImage = true;
            }
            catch
            {
                // 不是图像（XLD/Region），GetImageSize会抛异常
                isImage = false;
            }

            if (isImage)
            {
                _host.HalconWindow.SetPart(0, 0, (int)(h.D - 1), (int)(w.D - 1));
                _host.HalconWindow.ClearWindow();
                System.Diagnostics.Debug.WriteLine($"[Display] SetPart w={w.D},h={h.D}");
            }
            _host.HalconWindow.DispObj(obj);
            _host.SetFullImagePart();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HalconUiService.Display] {ex.Message}");
        }
    }




    public void SetColor(string color)
    {
        if (_host?.HalconWindow != null)
            _host.HalconWindow.SetColor(color);
    }

    public void SetDraw(string mode)
    {
        if (_host?.HalconWindow != null)
            _host.HalconWindow.SetDraw(mode);
    }

    public void DisplayCross(double row, double col, double size, double angle)
    {
        _host?.HalconWindow?.DispCross(row, col, size, angle);
    }



    /// </summary>
    public void Message(string text, string color)
    {
        if (_host == null) return;
        var hw = _host.HalconWindow;
        hw.SetColor(color);
        hw.SetTposition(12, 12);
        hw.WriteString(text);
    }

    /// <summary>鼠标交互：拖出一个矩形 1（轴对齐），返回 [Row1,Col1,Row2,Col2]</summary>
    public Task<HTuple> DrawRectangle1Interactive(CancellationToken token)
    {
        return Task.Run(() =>
        {
            _host.HalconWindow.DrawRectangle1(out double r, out double c, out double l1, out double l2);
            return new HTuple(new[] { r, c, l1, l2 });
        });
    }
        
    /// <summary>鼠标交互：拖出一个矩形2（可旋转），返回 [Row,Col,Phi,Length1,Length2]</summary>
    public Task<HTuple> DrawRectangle2Interactive(CancellationToken token)
    {
        return Task.Run(() =>
        {
            _host.HalconWindow.DrawRectangle2(out double r, out double c, out double phi, out double l1, out double l2);
            return new HTuple(new[] { r, c, phi, l1, l2 });
        });
       
    }
}
