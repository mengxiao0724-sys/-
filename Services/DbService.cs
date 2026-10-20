using FreeSql;
using PcbPinInspection.Models;

namespace PcbPinInspection.Services;

/// <summary>
/// IFreeSql 单例入口。SQL Server 连接串集中在这里改。
/// 启动时 Build 一次，后续直接用 DbService.Fsql 操作。
/// </summary>
public static class DbService
{
    /// <summary>
    /// SQL Server 连接串。
    ///  - 本地默认实例：Data Source=.;Initial Catalog=PcbInspection;Integrated Security=True;TrustServerCertificate=True
    ///  - 带账号密码  ：Data Source=192.168.1.10;Initial Catalog=PcbInspection;User Id=sa;Password=xxx;TrustServerCertificate=True
    /// 首次运行若库不存在，需要你先在 SSMS 里 `CREATE DATABASE PcbInspection;`
    /// 表结构由 FreeSql CodeFirst 自动同步。
    /// </summary>
    public const string ConnectionString =
     @"Data Source=localhost\SQLEXPRESS;Initial Catalog=PCB_Detect;Integrated Security=True;TrustServerCertificate=True";


    public static IFreeSql Fsql { get; private set; }

    public static void Init()
    {
        Fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.SqlServer, ConnectionString)
            .UseAutoSyncStructure(true)   // 实体变更自动加列/建表（生产可关）
            .Build();

        // 启动时跑一次结构同步，避免首次 Insert 才建表
        Fsql.CodeFirst.SyncStructure<InspectionRecord>();
    }

    /// <summary>写一条检测记录；DB 异常不阻塞检测主流程</summary>
    public static async Task SaveAsync(InspectionRecord rec)
    {
        try
        {
            if (Fsql == null) return;
            await Fsql.Insert(rec).ExecuteAffrowsAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "写库失败：{Msg}", ex.Message);
        }
    }

}
