namespace IndustrialRepair.UI;

static class Program
{
    /// <summary>主入口：登录 → 主界面；主界面退出后若请求重登则回到登录页。</summary>
    [STAThread]
    static void Main()
    {
        // 全局异常兜底：写文件日志 + 友好弹窗，避免闪退（工程化要求）
        System.Windows.Forms.Application.ThreadException += (_, e) => Handle(e.Exception, "UI线程异常");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Handle(e.ExceptionObject as Exception, "后台线程异常");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        { Handle(e.Exception, "Task异常"); e.SetObserved(); };

        ApplicationConfiguration.Initialize();
        Theme.Load();   // OR1：启动加载主题偏好
        try
        {
            // M0：登录前结构检查/升级（失败阻止进入业务系统，重启可重试）
            try
            {
                new BLL.DbInitializer().EnsureStructure();
            }
            catch (Exception ex)
            {
                Handle(ex, "数据库结构检查/升级失败");
                MessageBox.Show("数据库结构检查或升级失败，系统退出。\r\n请查看日志或联系管理员，修复后重新启动可自动重试。",
                    "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            while (true)
            {
                using var login = new FLogin();
                if (login.ShowDialog() != DialogResult.OK || login.LoggedUser == null) break;
                Application.Run(new FMain(login.LoggedUser));
                if (!FMain.RequestRelogin) break;
                FMain.RequestRelogin = false;
            }
        }
        finally
        {
            BLL.FileLog.CloseAndFlush();   // NR5：Serilog 刷盘
        }
    }

    private static void Handle(Exception? ex, string context)
    {
        if (ex == null) return;
        BLL.FileLog.Write(ex, context);
        try
        {
            MessageBox.Show($"{context}：{ex.Message}\r\n\r\n详细信息已写入日志：{BLL.FileLog.LogDir}",
                "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch { }
    }
}
