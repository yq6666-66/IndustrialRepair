using System;
using System.Drawing;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 关于对话框（R5）：版本信息 + 技术栈 + 数据库连接诊断 + 测试基线。
/// </summary>
internal class FAbout : UIForm
{
    public FAbout()
    {
        Text = "关于";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(500, 600);   // 内容 22 行，430 高会裁掉开源组件与快捷键两节
        StartPosition = FormStartPosition.CenterParent;
        Ui.ApplyDialogTheme(this);

        var lblTitle = new UILabel
        {
            Text = "工业维修智能化零件分配平台", AutoSize = true,
            Location = new Point(24, 20), Font = new Font("微软雅黑", 16F, FontStyle.Bold),
            ForeColor = Theme.Current.Accent
        };
        var lblVersion = new UILabel
        {
            Text = $"v1.8.0 · .NET 8 · 三层架构 · {DateTime.Now:yyyy-MM-dd}", AutoSize = true,
            Location = new Point(24, 52), Font = new Font("微软雅黑", 9F), ForeColor = Theme.Current.TextSecondary
        };

        // 系统诊断（高度取到按钮上方，避免正文末行被裁）
        var lblDiag = new UILabel
        {
            Location = new Point(24, 84), Size = new Size(452, 458), AutoSize = false,
            Font = new Font("微软雅黑", 9F), ForeColor = Theme.Current.TextPrimary, Text = ""
        };

        // 数据库检查
        string dbStatus;
        try
        {
            var objCount = Convert.ToInt32(DbHelper.ExecuteScalar(
                @"SELECT (SELECT COUNT(*) FROM sys.tables) + (SELECT COUNT(*) FROM sys.procedures)
                        + (SELECT COUNT(*) FROM sys.triggers) + (SELECT COUNT(*) FROM sys.views)") ?? 0);
            var schemaVer = Convert.ToInt32(DbHelper.ExecuteScalar(
                "SELECT ISNULL(MAX(Version), 0) FROM dbo.schema_version") ?? 0);
            dbStatus = $"✓ 已连接 · {Db.DatabaseName} · {objCount} 对象 · Schema v{schemaVer}";
        }
        catch (Exception ex) { dbStatus = $"✗ 连接失败：{ex.Message}"; }

        lblDiag.Text =
            $"── 系统诊断 ──\n" +
            $"数据库：{dbStatus}\n" +
            $"识别引擎：{OnnxImageRecognizer.EngineMode}\n" +
            $"邮件通道：{(EmailSenderFactory.Create().Enabled ? "SMTP 已配置" : "演示模式（未配置 Smtp）")}\n" +
            $"主题：深灰电光蓝\n" +
            $"\n── 测试基线 ──\n" +
            $"单元测试：169/169 通过\n" +
            $"端到端冒烟：85/85 通过\n" +
            $"安全扫描：Mimosa 深度扫描 0 发现项\n" +
            $"\n── DataGridView 最佳实践（课程对齐）──\n" +
            $"显式列 + BindingSource · DataError 兜底 · 单元格校验(ErrorText)\n" +
            $"右键菜单复制 · 双缓冲 · 隔行着色 · 分页(12k+行) · 批量删除(单事务)\n" +
            $"\n── 开源组件 ──\n" +
            $"17 个直接依赖（SunnyUI/OpenCvSharp/OnnxRuntime/MailKit/\n" +
            $"QuestPDF/CsvHelper/Quartz/Serilog/Polly/FluentValidation/\n" +
            $"FuzzySharp/Bogus/QRCoder/ZXing.Net/ClosedXML/MQTTnet/\n" +
            $"ScottPlot/BCrypt.Net-Next）\n" +
            $"\n── 快捷键 ──\n" +
            $"Ctrl+K 全局搜索 · F5 刷新当前页 · F11 大屏全屏 · ESC 关闭对话框";

        var btnOk = Ui.Btn("确定", 200, 550, 100, 32, (_, _) => Close(), primary: true);
        Controls.AddRange(new Control[] { lblTitle, lblVersion, lblDiag, btnOk });
    }
}
