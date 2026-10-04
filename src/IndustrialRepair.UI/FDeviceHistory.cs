using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>设备维护历史（R22）：该设备全部工单 + 累计成本 + 平均处理时长。</summary>
internal class FDeviceHistory : UIForm
{
    public FDeviceHistory(Device device)
    {
        Text = $"维护历史 — {device.DeviceCode} {device.DeviceName}";
        Ui.CloseOnEsc(this);
        ClientSize = new Size(720, 500);
        StartPosition = FormStartPosition.CenterParent;

        // 汇总卡
        var dash = new DashboardService();
        var cost = dash.DeviceCostReport().AsEnumerable()
            .FirstOrDefault(r => r["DeviceCode"].ToString() == device.DeviceCode);
        var (orderCount, totalCost, avgMin) = cost != null
            ? (Convert.ToInt32(cost["OrderCount"]), Convert.ToDecimal(cost["TotalCost"]),
               cost["AvgMinutes"] == DBNull.Value ? 0 : Convert.ToDecimal(cost["AvgMinutes"]))
            : (0, 0m, 0m);

        var lblSummary = new UILabel
        {
            Text = $"工单总数：{orderCount}    累计材料成本：{totalCost:N2} 元    平均处理时长：{avgMin:F0} 分钟",
            Location = new Point(16, 14), AutoSize = true,
            Font = new Font("微软雅黑", 10F, FontStyle.Bold), ForeColor = Color.FromArgb(41, 108, 202)
        };

        // 工单列表
        var grid = Ui.Grid(16, 50, 688, 350);
        var orders = DbHelper.GetTable(
            @"SELECT o.OrderNo, o.Status, o.Priority, o.FaultDesc, rp.RealName AS ReporterName,
                      eg.RealName AS EngineerName, o.CreatedAt, o.FinishTime,
                      ISNULL(c.MaterialCost, 0) AS MaterialCost
              FROM dbo.work_order o
              JOIN dbo.sys_user rp ON rp.UserId = o.ReporterId
              LEFT JOIN dbo.sys_user eg ON eg.UserId = o.EngineerId
              OUTER APPLY (SELECT ISNULL(SUM(i.Qty * i.UnitPrice), 0) AS MaterialCost
                           FROM dbo.picking_list pl JOIN dbo.picking_item i ON i.PickingId = pl.PickingId
                           WHERE pl.OrderId = o.OrderId) c
              WHERE o.DeviceId = @Id ORDER BY o.OrderId DESC",
            CommandType.Text, DbHelper.P("@Id", device.DeviceId));

        // 翻译状态列
        if (orders.Columns.Contains("Status")) orders.Columns["Status"]!.ColumnName = "状态";
        foreach (DataRow r in orders.Rows)
            r["状态"] = OrderStatusCode.ToChinese(r["状态"].ToString()!);
        grid.DataSource = orders;

        var btnClose = Ui.Btn("关闭", 310, 450, 100, 32, (_, _) => Close());

        // FR13：停机记录段
        var downGrid = Ui.Grid(16, 400, 336, 90);
        var downData = new DowntimeService().History(device.DeviceId);
        if (downData.Columns.Contains("StartTime")) downData.Columns["StartTime"]!.ColumnName = "开始";
        if (downData.Columns.Contains("EndTime")) downData.Columns["EndTime"]!.ColumnName = "结束";
        if (downData.Columns.Contains("DurationMin")) downData.Columns["DurationMin"]!.ColumnName = "时长(分)";
        if (downData.Columns.Contains("Reason")) downData.Columns["Reason"]!.ColumnName = "原因";
        downGrid.DataSource = downData;
        var lblDown = new UILabel
        { Text = "停机记录", AutoSize = true, Location = new Point(16, 380),
          Font = new Font("微软雅黑", 9F, FontStyle.Bold) };

        var avail = new DowntimeService().CalculateAvailability(device.DeviceId, 30);
        var lblAvail = new UILabel
        { Text = $"近30天可用率：{avail:F1}%", AutoSize = true, Location = new Point(200, 380),
          Font = new Font("微软雅黑", 9F, FontStyle.Bold),
          ForeColor = avail > 90 ? Color.FromArgb(80, 160, 90) : avail > 70 ? Color.FromArgb(230, 162, 60) : Color.Firebrick };

        Controls.AddRange(new Control[] { lblSummary, grid, lblDown, lblAvail, downGrid, btnClose });
    }
}
