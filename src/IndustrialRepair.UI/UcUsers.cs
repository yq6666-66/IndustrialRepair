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
        _tabs.SelectedIndex = 0;
        _tabs.SelectedIndexChanged += (_, _) => RefreshData();

        Controls.Add(_tabs);
    }

    public void RefreshData()
    {
        try
        {
            if (_tabs.SelectedIndex == 0)
            {
                var t = _users.ListUsers(null);
                foreach (var c in new[] { "UserId", "UserName", "RealName", "Phone", "Email", "Status", "MfaEnabled", "LastLoginTime", "RoleName" })
                    if (t.Columns.Contains(c)) t.Columns[c]!.ColumnName = c switch
                    {
                        "UserId" => "ID", "UserName" => "用户名", "RealName" => "姓名", "Phone" => "手机",
                        "Email" => "邮箱", "Status" => "状态", "MfaEnabled" => "MFA", "LastLoginTime" => "最后登录", _ => "角色"
                    };
                foreach (System.Data.DataRow r in t.Rows)
                {
                    r["状态"] = (bool)r["状态"] ? "启用" : "禁用";
                    r["MFA"] = (bool)r["MFA"] ? "开" : "关";
                }
                _gridUsers.DataSource = t;
            }
            else if (_tabs.SelectedIndex == 1)
            {
                var t = _users.ListLoginLogs();
                Rename(t, ("LoginLogId", "ID"), ("UserName", "用户名"), ("Success", "结果"), ("Message", "说明"), ("Ip", "IP"), ("LoginTime", "时间"));
                foreach (System.Data.DataRow r in t.Rows) r["结果"] = (bool)r["结果"] ? "成功" : "失败";
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
