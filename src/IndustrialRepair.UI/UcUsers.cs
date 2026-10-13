using System;
using System.Drawing;
using System.Windows.Forms;
using IndustrialRepair.BLL;
using IndustrialRepair.DAL;
using IndustrialRepair.Models;
using Sunny.UI;

namespace IndustrialRepair.UI;

/// <summary>
/// 用户与日志（系统管理员）：账号管理（启停/重置密码/改角色）+ 登录日志 + 操作日志。
/// </summary>
internal class UcUsers : UserControl, IRefreshable
{
    private readonly UserRepository _users = new();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly UIDataGridView _gridUsers = Ui.Grid(10, 50, 1160, 620);
    private readonly UIDataGridView _gridLogin = Ui.Grid(10, 10, 1160, 660);
    private readonly UIDataGridView _gridOp = Ui.Grid(10, 10, 1160, 660);

    public UcUsers()
    {
        var tabUsers = new TabPage("账号管理");
        var tabLogin = new TabPage("登录日志");
        var tabOp = new TabPage("操作日志");
        var tabBackup = new TabPage("备份恢复");

        var btnToggle = Ui.Btn("启用/禁用", 10, 12, 100, 32, OnToggle);
        var btnReset = Ui.Btn("重置密码", 120, 12, 90, 32, OnReset);
        var btnRole = Ui.Btn("调整角色", 220, 12, 90, 32, OnRole);
        var btnMfa = Ui.Btn("MFA开关", 320, 12, 90, 32, OnMfa);

        tabUsers.Controls.Add(_gridUsers);
        tabLogin.Controls.Add(_gridLogin);
        tabOp.Controls.Add(_gridOp);
        _gridUsers.Location = new Point(10, 50);

        tabUsers.Controls.AddRange(new Control[] { btnToggle, btnReset, btnRole, btnMfa });
        _tabs.TabPages.Add(tabUsers);
        _tabs.TabPages.Add(tabLogin);
        _tabs.TabPages.Add(tabOp);
        _tabs.TabPages.Add(tabBackup);
        BuildBackupTab(tabBackup);
        _tabs.SelectedIndex = 0;
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex == 3) LoadBackups();   // OR2：切到备份 Tab 时加载列表
            else RefreshData();
        };

        Controls.Add(_tabs);
        Controls.Add(Ui.PageHeader("用户与日志", "账号启停 · 角色 · MFA · 登录/操作审计"));
    }

    public void RefreshData()
    {
        try
        {
            if (_tabs.SelectedIndex == 0)
            {
                var t = _users.ListUsers(null);
                // Status/MfaEnabled 是 BIT 布尔列，不能写入中文文本：先建字符串显示列，再移除布尔列
                t.Columns.Add("状态显示", typeof(string));
                t.Columns.Add("MFA显示", typeof(string));
                foreach (System.Data.DataRow r in t.Rows)
                {
                    r["状态显示"] = r["Status"] is true ? "启用" : "禁用";
                    r["MFA显示"] = r["MfaEnabled"] is true ? "开" : "关";
                }
                t.Columns.Remove("Status");
                t.Columns.Remove("MfaEnabled");
                t.Columns["状态显示"]!.SetOrdinal(5);
                t.Columns["MFA显示"]!.SetOrdinal(6);
                foreach (var c in new[] { "UserId", "UserName", "RealName", "Phone", "Email", "状态显示", "MFA显示", "LastLoginTime", "RoleName" })
                    if (t.Columns.Contains(c)) t.Columns[c]!.ColumnName = c switch
                    {
                        "UserId" => "ID", "UserName" => "用户名", "RealName" => "姓名", "Phone" => "手机",
                        "Email" => "邮箱", "状态显示" => "状态", "MFA显示" => "MFA", "LastLoginTime" => "最后登录", _ => "角色"
                    };
                // DG2：显式列 + BindingSource（ID 隐藏列保留供权限操作取行）
                Ui.BindExplicit(_gridUsers, t, "users-v1",
                    new GridCol("ID", "ID", FillWeight: 20, Visible: false),
                    new GridCol("用户名", "用户名", FillWeight: 90),
                    new GridCol("姓名", "姓名", FillWeight: 80),
                    new GridCol("手机", "手机", FillWeight: 90),
                    new GridCol("邮箱", "邮箱", FillWeight: 140),
                    new GridCol("状态", "状态", FillWeight: 50),
                    new GridCol("MFA", "MFA", FillWeight: 45),
                    new GridCol("最后登录", "最后登录", Format: "MM-dd HH:mm", FillWeight: 95),
                    new GridCol("角色", "角色", FillWeight: 80));
            }
            else if (_tabs.SelectedIndex == 1)
            {
                var t = _users.ListLoginLogs();
                // Success 是 BIT 布尔列，同样先建显示列再移除
                t.Columns.Add("结果显示", typeof(string));
                foreach (System.Data.DataRow r in t.Rows) r["结果显示"] = r["Success"] is true ? "成功" : "失败";
                t.Columns.Remove("Success");
                t.Columns["结果显示"]!.SetOrdinal(2);
                Rename(t, ("LoginLogId", "ID"), ("UserName", "用户名"), ("结果显示", "结果"), ("Message", "说明"), ("Ip", "IP"), ("LoginTime", "时间"));
                _gridLogin.DataSource = t;
            }
            else
            {
                var t = _users.ListOpLogs();
                Rename(t, ("OpLogId", "ID"), ("UserName", "用户名"), ("Module", "模块"), ("Action", "动作"), ("Detail", "详情"), ("CreatedAt", "时间"));
                _gridOp.DataSource = t;
            }
        }
        catch (Exception ex) { Ui.Error(DbHelper.FriendlyError(ex)); }
    }

    private static void Rename(System.Data.DataTable t, params (string from, string to)[] map)
    {
        foreach (var (from, to) in map)
            if (t.Columns.Contains(from)) t.Columns[from]!.ColumnName = to;
    }

    /* ---- 备份恢复（OR2） ---- */
    private readonly BackupService _backup = new();
    private readonly UIDataGridView _gridBackup = Ui.Grid(10, 54, 1160, 560);

    private void BuildBackupTab(TabPage tab)
    {
        var btnBackup = Ui.Btn("立即备份", 10, 12, 100, 32, (_, _) =>
        {
            Ui.Busy(() =>
            {
                var tag = Ui.Input("备份标签（选填，用于区分用途）：", "标签：") ?? "";
                var r = _backup.Backup(tag);
                if (r.Success) { Ui.Info(r.Message); LoadBackups(); } else Ui.Error(r.Message);
            });
        });
        var btnRestore = Ui.Btn("恢复", 120, 12, 80, 32, (_, _) =>
        {
            var sel = SelectedBackup();
            if (sel == null) { Ui.Error("请先选择备份文件。"); return; }
            if (!Ui.Ask($"⚠ 恢复将覆盖当前全部数据！\n\n确定要从\n{sel.FileName}\n恢复吗？")) return;
            Ui.Busy(() =>
            {
                var r = _backup.Restore(sel);
                Ui.Info(r.Message + "\n\n建议重启系统使缓存失效。");
                LoadBackups();
            });
        });
        var btnDelete = Ui.Btn("删除备份", 210, 12, 90, 32, (_, _) =>
        {
            var sel = SelectedBackup();
            if (sel == null) { Ui.Error("请先选择备份文件。"); return; }
            if (!Ui.Ask($"确定删除 {sel.FileName}？")) return;
            var r = _backup.Delete(sel);
            if (r.Success) LoadBackups(); else Ui.Error(r.Message);
        });
        var btnOpen = Ui.Btn("打开目录", 310, 12, 90, 32, (_, _) =>
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{BackupService.BackupDir}\""));

        var lblHint = new UILabel
        {
            Text = $"备份目录：{BackupService.BackupDir}（恢复前建议先做一次新备份）",
            AutoSize = true, Location = new Point(420, 18), Font = new Font("微软雅黑", 8.5F),
            ForeColor = Color.FromArgb(110, 122, 138)
        };

        tab.Controls.AddRange(new Control[] { btnBackup, btnRestore, btnDelete, btnOpen, lblHint, _gridBackup });
    }

    private BackupService.BackupInfo? SelectedBackup()
    {
        var row = _gridBackup.CurrentRow;
        if (row == null) return null;
        var path = row.Cells["FullPath"]?.Value?.ToString();
        return _backup.ListBackups().FirstOrDefault(b => b.FullPath == path);
    }

    private void LoadBackups()
    {
        var t = new System.Data.DataTable();
        t.Columns.Add("文件名", typeof(string));
        t.Columns.Add("大小(KB)", typeof(long));
        t.Columns.Add("时间", typeof(string));
        t.Columns.Add("FullPath", typeof(string));
        foreach (var b in _backup.ListBackups())
            t.Rows.Add(b.FileName, b.SizeBytes / 1024, b.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"), b.FullPath);
        _gridBackup.DataSource = t;
        if (_gridBackup.Columns.Contains("FullPath")) _gridBackup.Columns["FullPath"]!.Visible = false;
    }

    private int SelectedUserId() =>
        int.TryParse(_gridUsers.CurrentRow?.Cells["ID"]?.Value?.ToString(), out var id) ? id : 0;

    private void OnToggle(object? s, EventArgs e)
    {
        var id = SelectedUserId();
        if (id == 0) { Ui.Error("请先选择用户。"); return; }
        if (id == AppSession.Current?.UserId) { Ui.Error("不能禁用当前登录账号。"); return; }
        var enabled = _gridUsers.CurrentRow!.Cells["状态"].Value!.ToString() == "启用";
        var sure = Ui.Ask($"确定{(enabled ? "禁用" : "启用")}该账号？");
        if (!sure) return;
        _users.SetStatus(id, !enabled);
        _users.WriteOpLog(AppSession.Current!.UserId, AppSession.Current.UserName, "系统", "账号管理", $"{(enabled ? "禁用" : "启用")}用户 #{id}");
        RefreshData();
    }

    private void OnReset(object? s, EventArgs e)
    {
        var id = SelectedUserId();
        if (id == 0) { Ui.Error("请先选择用户。"); return; }
        if (!Ui.Ask("重置该账号密码为 123456？")) return;
        _users.UpdatePassword(id, PasswordHasher.Hash("123456"));
        _users.WriteOpLog(AppSession.Current!.UserId, AppSession.Current.UserName, "系统", "账号管理", $"重置用户 #{id} 密码");
        Ui.Info("密码已重置为 123456。");
    }

    private void OnRole(object? s, EventArgs e)
    {
        var id = SelectedUserId();
        if (id == 0) { Ui.Error("请先选择用户。"); return; }
        var roles = _users.ListRoles();
        var names = string.Join(" / ", roles.Select(r => r.RoleName));
        var pick = Ui.Input($"调整角色（输入：{names}）", "角色名：");
        if (string.IsNullOrWhiteSpace(pick)) return;
        var role = roles.FirstOrDefault(r => r.RoleName == pick.Trim());
        if (role == null) { Ui.Error("角色名不正确。"); return; }
        _users.SetRole(id, role.RoleId);
        _users.WriteOpLog(AppSession.Current!.UserId, AppSession.Current.UserName, "系统", "账号管理", $"用户 #{id} 角色调整为 {role.RoleName}");
        Ui.Info("角色已调整，该用户下次登录生效。");
        RefreshData();
    }

    private void OnMfa(object? s, EventArgs e)
    {
        var id = SelectedUserId();
        if (id == 0) { Ui.Error("请先选择用户。"); return; }
        var on = _gridUsers.CurrentRow!.Cells["MFA"].Value!.ToString() == "开";
        if (!Ui.Ask($"{(on ? "关闭" : "开启")}该账号的多因素认证？")) return;
        _users.SetMfa(id, !on, null);
        _users.WriteOpLog(AppSession.Current!.UserId, AppSession.Current.UserName, "系统", "账号管理", $"用户 #{id} MFA {(on ? "关闭" : "开启")}");
        RefreshData();
    }
}
