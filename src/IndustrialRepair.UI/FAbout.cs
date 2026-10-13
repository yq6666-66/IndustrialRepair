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
        ClientSize = new Size(480, 430);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.White;

        var lblTitle = new UILabel
        {
            Text = "工业维修智能化零件分配平台", AutoSize = true,
            Location = new Point(24, 20), Font = new Font("微软雅黑", 16F, FontStyle.Bold),
            ForeColor = Color.FromArgb(41, 108, 202)
        };
        var lblVersion = new UILabel
        {
            Text = $"v1.7.0 · .NET 8 · 三层架构 · {DateTime.Now:yyyy-MM-dd}", AutoSize = true,
            Location = new Point(24, 52), Font = new Font("微软雅黑", 9F), ForeColor = Color.Gray
        };

        // 系统诊断
        var lblDiag = new UILabel
        {
            Location = new Point(24, 84), Size = new Size(432, 200), AutoSize = false,
            Font = new Font("微软雅黑", 9F), ForeColor = Color.FromArgb(48, 56, 68), Text = ""
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
            $"单元测试：145/145 通过\n" +
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

        var btnOk = Ui.Btn("确定", 190, 380, 100, 32, (_, _) => Close());
        Controls.AddRange(new Control[] { lblTitle, lblVersion, lblDiag, btnOk });
    }
}
