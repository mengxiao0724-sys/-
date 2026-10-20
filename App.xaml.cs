using PcbPinInspection.Services;
using Serilog;
using System.Configuration;
using System.Data;
using System.Windows;

namespace 引脚检测
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. Serilog：按天滚动文件，保留 14 天
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: "logs/pcb-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("==== 应用启动 ====");

            // 2. IFreeSql / SQL Server：失败不阻塞 UI，只打日志
            try
            {
                DbService.Init();
                Log.Information("IFreeSql 初始化成功，连接串：{Conn}", DbService.ConnectionString);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "IFreeSql 初始化失败，检测仍可运行，但结果不会入库");
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("==== 应用退出 ====");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

}
