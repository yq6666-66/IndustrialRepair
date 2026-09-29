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
        while (true)
        {
            using var login = new FLogin();
            if (login.ShowDialog() != DialogResult.OK || login.LoggedUser == null) break;
            Application.Run(new FMain(login.LoggedUser));
            if (!FMain.RequestRelogin) break;
            FMain.RequestRelogin = false;
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
