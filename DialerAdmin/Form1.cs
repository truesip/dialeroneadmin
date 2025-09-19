using System.Net.Http.Json;
using System.Text.Json;

namespace DialerAdmin;

public partial class Form1 : Form
{
    private readonly DataGridView _grid = new();
    private readonly TextBox _hub = new() { Text = "https://localhost:5001" };
    private readonly TextBox _key = new() { Text = "change-me" };
    private readonly Button _refresh = new() { Text = "Refresh" };
    private readonly Button _enable = new() { Text = "Enable" };
    private readonly Button _disable = new() { Text = "Disable" };
    private readonly Button _restart = new() { Text = "Restart" };
    private readonly Button _updateSip = new() { Text = "Update SIP" };

    public Form1()
    {
        InitializeComponent();
        Text = "Dialer Admin";
        Width = 900; Height = 600;
        var lblHub = new Label { Text = "Hub:", Left = 10, Top = 12, AutoSize = true };
        _hub.Left = 50; _hub.Top = 8; _hub.Width = 300;
        var lblKey = new Label { Text = "API Key:", Left = 370, Top = 12, AutoSize = true };
        _key.Left = 430; _key.Top = 8; _key.Width = 200; _key.UseSystemPasswordChar = true;
        _refresh.Left = 650; _refresh.Top = 7; _refresh.Width = 80;
        _enable.Left = 10; _enable.Top = 40; _enable.Width = 100;
        _disable.Left = 120; _disable.Top = 40; _disable.Width = 100;
        _restart.Left = 230; _restart.Top = 40; _restart.Width = 100;
        _updateSip.Left = 340; _updateSip.Top = 40; _updateSip.Width = 120;

        _grid.Left = 10; _grid.Top = 80; _grid.Width = 860; _grid.Height = 470; _grid.ReadOnly = true; _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = true; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        Controls.AddRange(new Control[] { lblHub, _hub, lblKey, _key, _refresh, _enable, _disable, _restart, _updateSip, _grid });

        _refresh.Click += async (s, e) => await LoadAgents();
        _enable.Click += async (s, e) => await SendCommandSelected("Enable");
        _disable.Click += async (s, e) => await SendCommandSelected("Disable");
        _restart.Click += async (s, e) => await SendCommandSelected("Restart");
        _updateSip.Click += async (s, e) =>
        {
            var d = new SipDialog();
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                var payload = new Dictionary<string, string> {
                    ["domain"] = d.Domain,
                    ["username"] = d.Username,
                    ["password"] = d.Password,
                    ["port"] = d.Port,
                    ["callerid"] = d.CallerId,
                    ["fromname"] = d.FromName,
                };
                await SendCommandSelected("UpdateSip", payload);
            }
        };

        Shown += async (s, e) => await LoadAgents();
    }

    private async Task<HttpClient> CreateClient()
    {
        var http = new HttpClient { BaseAddress = new Uri(_hub.Text.TrimEnd('/') + "/") };
        http.DefaultRequestHeaders.Add("X-Admin-Key", _key.Text);
        return http;
    }

    private async Task LoadAgents()
    {
        try
        {
            using var http = await CreateClient();
            var agents = await http.GetFromJsonAsync<List<AgentInfoDto>>("api/agents");
            _grid.DataSource = agents ?? new List<AgentInfoDto>();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Load failed: " + ex.Message);
        }
    }

    private async Task SendCommandSelected(string type, Dictionary<string,string>? payload = null)
    {
        if (_grid.DataSource is not List<AgentInfoDto> list) return;
        var selectedRows = _grid.SelectedRows.Cast<DataGridViewRow>().ToList();
        if (selectedRows.Count == 0) { MessageBox.Show(this, "Select one or more rows."); return; }
        using var http = await CreateClient();
        foreach (var row in selectedRows)
        {
            var agent = row.DataBoundItem as AgentInfoDto; if (agent == null) continue;
            var cmd = new { Type = type, Payload = payload };
            var res = await http.PostAsJsonAsync($"api/agents/{agent.AgentId}/command", cmd);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync();
                MessageBox.Show(this, $"{type} failed for {agent.AgentId}: {res.StatusCode} {body}");
            }
        }
    }
}

public class AgentInfoDto
{
    public string AgentId { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string[] IPs { get; set; } = Array.Empty<string>();
    public string[] MACs { get; set; } = Array.Empty<string>();
    public DateTime LastSeenUtc { get; set; }
    public string Status { get; set; } = "";
}

public class SipDialog : Form
{
    private TextBox t(string text, int top, int width = 250) { var l = new Label { Text = text, Left = 10, Top = top+3, AutoSize = true }; var tb = new TextBox { Left = 100, Top = top, Width = width }; Controls.Add(l); Controls.Add(tb); return tb; }
    private readonly TextBox _domain; private readonly TextBox _user; private readonly TextBox _pass; private readonly TextBox _port; private readonly TextBox _cid; private readonly TextBox _name;
    public string Domain => _domain.Text; public string Username => _user.Text; public string Password => _pass.Text; public string Port => _port.Text; public string CallerId => _cid.Text; public string FromName => _name.Text;
    public SipDialog()
    {
        Text = "Update SIP"; Width = 380; Height = 280; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent;
        _domain = t("Domain:", 10); _user = t("Username:", 40); _pass = t("Password:", 70); _pass.UseSystemPasswordChar = true; _port = t("Port:", 100); _cid = t("Caller ID:", 130); _name = t("From Name:", 160, 250);
        var ok = new Button { Text = "OK", Left = 100, Top = 200, Width = 100, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 210, Top = 200, Width = 100, DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
    }
}
