using System;
using FreeSql.DataAnnotations;

namespace PcbPinInspection.Models;

/// <summary>
/// 检测记录实体，对应 SQL Server 表 InspectionRecord。
/// 每次【单次检测】/【连续检测】出结果都插入一行。
/// </summary>
[Table(Name = "InspectionRecord")]
public class InspectionRecord
{
    [Column(IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    /// <summary>图片文件名</summary>
    public string ImageName { get; set; } = "";

    /// <summary>图片完整路径</summary>
    public string ImagePath { get; set; } = "";

    /// <summary>OK=true / NG=false</summary>
    public bool IsOK { get; set; }

    /// <summary>NG 原因；OK 时为"引脚尺寸合格"</summary>
    public string Reason { get; set; } = "";

    /// <summary>模板匹配分数 0~1</summary>
    public double MatchScore { get; set; }

    /// <summary>检测到的引脚对数</summary>
    public int PinCount { get; set; }

    /// <summary>本次实测最小引脚宽度(px)</summary>
    public double MinPinWidth { get; set; }

    /// <summary>本次实测最大引脚宽度(px)</summary>
    public double MaxPinWidth { get; set; }

    /// <summary>本次耗时(ms)</summary>
    public double ElapsedMs { get; set; }

    /// <summary>检测时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
