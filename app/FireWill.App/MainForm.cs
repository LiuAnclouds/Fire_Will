using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using FireWill.App.Services;

namespace FireWill.App;

public sealed class MainForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly string _projectRoot;
    private readonly IniProjectReader _projectReader;

    public MainForm()
    {
        _projectRoot = ProjectPaths.FindProjectRoot();
        _projectReader = new IniProjectReader(_projectRoot);

        Text = "Fire Will - 羁绊流程编排器";
        MinimumSize = new Size(1120, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = ProjectPaths.TryLoadIcon(_projectRoot);
        Controls.Add(_webView);
        Load += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        string indexPath = ProjectPaths.FindUiIndex(_projectRoot);
        _webView.Source = new Uri(indexPath);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using JsonDocument message = JsonDocument.Parse(e.WebMessageAsJson);
        string type = message.RootElement.GetProperty("type").GetString() ?? "";

        switch (type)
        {
            case "request-state":
                SendState();
                break;
            case "save-user-bindings":
                _projectReader.SaveUserBindings(message.RootElement.GetProperty("payload"));
                SendState("已保存用户快捷键和技能 CD 设置。");
                break;
            case "open-legacy-ahk":
                OpenLegacyAhk();
                break;
        }
    }

    private void SendState(string? toast = null)
    {
        var payload = _projectReader.ReadProjectState(toast);
        string json = JsonSerializer.Serialize(new { type = "state", payload });
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void OpenLegacyAhk()
    {
        string ahkPath = Path.Combine(_projectRoot, "war3_macro_gui.ahk");
        if (!File.Exists(ahkPath))
        {
            SendState("找不到旧版 AHK 配置器。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ahkPath,
                WorkingDirectory = _projectRoot,
                UseShellExecute = true
            });
            SendState("已尝试打开旧版 AHK 配置器。");
        }
        catch (Exception ex)
        {
            SendState("打开旧版 AHK 配置器失败：" + ex.Message);
        }
    }
}

