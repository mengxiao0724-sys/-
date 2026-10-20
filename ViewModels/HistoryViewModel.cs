using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcbPinInspection.Models;
using PcbPinInspection.Services;
using 引脚检测.ViewModels;
using 引脚检测.Services;

namespace 引脚检测.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    // ========== 查询条件 ==========
    [ObservableProperty] private DateTime _dateFrom = DateTime.Today.AddDays(-7);
    [ObservableProperty] private DateTime _dateTo = DateTime.Today.AddDays(1).AddSeconds(-1);
    [ObservableProperty] private string _filterStatus = "全部";   // 全部 / OK / NG
    [ObservableProperty] private string _keyword = "";

    /// <summary>下拉框选项</summary>
    public string[] StatusOptions { get; } = { "全部", "OK", "NG" };

    // ========== 结果 ==========
    [ObservableProperty] private ObservableCollection<InspectionRecord> _records = new();
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string _summaryText = "";

    public HistoryViewModel()
    {
        // 构造完自动查一次
        Query();
    }

    [RelayCommand]
    private void Query()
    {
        if (DbService.Fsql == null)
        {
            SummaryText = "数据库未初始化，无法查询";
            return;
        }

        try
        {
            var query = DbService.Fsql.Select<InspectionRecord>()
                .Where(a => a.CreatedAt >= DateFrom && a.CreatedAt <= DateTo);

            if (FilterStatus == "OK") query = query.Where(a => a.IsOK);
            else if (FilterStatus == "NG") query = query.Where(a => !a.IsOK);

            if (!string.IsNullOrWhiteSpace(Keyword))
                query = query.Where(a => a.ImageName.Contains(Keyword));

            TotalCount = (int)query.Count();

            var list = query
                .OrderByDescending(a => a.Id)
                .Take(500)
                .ToList();

            Records = new ObservableCollection<InspectionRecord>(list);

            int ok = list.Count(r => r.IsOK);
            int ng = list.Count - ok;
            SummaryText = $"共 {TotalCount} 条（本页显示 {list.Count}）  OK {ok} / NG {ng}";
        }
        catch (Exception ex)
        {
            SummaryText = $"查询失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void Clear()
    {
        Keyword = "";
        FilterStatus = "全部";
        DateFrom = DateTime.Today.AddDays(-7);
        DateTo = DateTime.Today.AddDays(1).AddSeconds(-1);
        Query();
    }
}
