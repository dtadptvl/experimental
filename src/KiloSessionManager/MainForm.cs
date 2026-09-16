using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KiloSessionManager;

internal sealed class MainForm : Form
{
    private readonly ProjectStore _store = new();
    private readonly SessionHostLauncher _launcher;
    private readonly List<ProjectEntry> _projects;
    private readonly ListBox _projectList = new();
    private readonly WebView2 _terminal = new();
    private readonly ToolStripButton _addButton = new("Add project");
    private readonly ToolStripButton _renameButton = new("Rename");
    private readonly ToolStripButton _openButton = new("Open folder");
    private readonly ToolStripButton _startButton = new("Start / Resume");
    private readonly ToolStripButton _restartButton = new("Restart");
    private readonly ToolStripButton _terminateButton = new("Terminate");
    private readonly ToolStripButton _removeButton = new("Remove");
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 2000 };
    private SessionClient? _activeClient;
    private string? _activeProjectId;
    private CancellationTokenSource? _selectionCts;
    private bool _webReady;
    private bool _closing;
    private bool _refreshingList;

    public MainForm()
    {
        _launcher = new SessionHostLauncher(_store);
        _projects = _store.Load();

        Text = "Kilo Session Manager";
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;

        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        tools.Items.AddRange(new ToolStripItem[]
        {
            _addButton,
            new ToolStripSeparator(),
            _renameButton,
            _openButton,
            new ToolStripSeparator(),
            _startButton,
            _restartButton,
            _terminateButton,
            new ToolStripSeparator(),
            _removeButton
        });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 250,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 190
        };

        var sidebarTitle = new Label
        {
            Text = "PROJECTS",
            Dock = DockStyle.Top,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Font = new Font(Font, FontStyle.Bold)
        };
        _projectList.Dock = DockStyle.Fill;
        _projectList.BorderStyle = BorderStyle.None;
        _projectList.IntegralHeight = false;
        _projectList.Font = new Font("Segoe UI", 10.5f);
        split.Panel1.Controls.Add(_projectList);
        split.Panel1.Controls.Add(sidebarTitle);

        _terminal.Dock = DockStyle.Fill;
        split.Panel2.Controls.Add(_terminal);

        Controls.Add(split);
        Controls.Add(tools);

        _addButton.Click += (_, _) => AddProject();
        _renameButton.Click += (_, _) => RenameProject();
        _openButton.Click += (_, _) => OpenProjectFolder();
        _startButton.Click += async (_, _) => await StartSelectedAsync();
        _restartButton.Click += async (_, _) => await RestartSelectedAsync();
        _terminateButton.Click += async (_, _) => await TerminateSelectedAsync();
        _removeButton.Click += (_, _) => RemoveProject();
        _projectList.SelectedIndexChanged += async (_, _) => await ProjectSelectionChangedAsync();
        _statusTimer.Tick += (_, _) => StatusTick();
        FormClosing += (_, _) => CloseManagerOnly();
        Shown += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _terminal.EnsureCoreWebView2Async();
            _terminal.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _terminal.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _terminal.CoreWebView2.WebMessageReceived += WebMessageReceived;

            var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(webRoot))
                throw new DirectoryNotFoundException($"Terminal assets are missing: {webRoot}");

            _terminal.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "kilo.local",
                webRoot,
                CoreWebView2HostResourceAccessKind.Allow);

            var navTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Done(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                _terminal.NavigationCompleted -= Done;
                if (args.IsSuccess) navTcs.TrySetResult(true);
                else navTcs.TrySetException(new InvalidOperationException($"Terminal UI navigation failed: {args.WebErrorStatus}"));
            }
            _terminal.NavigationCompleted += Done;
            _terminal.Source = new Uri("https://kilo.local/index.html");
            await navTcs.Task;
            _webReady = true;

            RefreshProjectList();
            _statusTimer.Start();
            if (_projectList.Items.Count > 0) _projectList.SelectedIndex = 0;
            else PostSystem("Add a project to start Kilo.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Startup error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private ProjectEntry? SelectedProject => (_projectList.SelectedItem as ProjectListItem)?.Project;

    private async Task ProjectSelectionChangedAsync()
    {
        if (_refreshingList || !_webReady || _closing) return;
        var project = SelectedProject;
        if (project is null) return;

        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = new CancellationTokenSource();
        var token = _selectionCts.Token;

        DetachClient();
        ResetTerminal();
        _activeProjectId = project.Id;

        if (!Directory.Exists(project.Path))
        {
            PostSystem($"Project folder no longer exists: {project.Path}");
            UpdateButtons();
            return;
        }

        if (!SessionHostLauncher.IsAlive(project.Runtime))
        {
            if (project.Runtime is not null)
            {
                project.Runtime = null;
                _store.Save(_projects);
            }

            if (!project.ManualStop)
            {
                try
                {
                    Launch(project, resume: project.HasStarted);
                    RefreshProjectList(project.Id);
                }
                catch (Exception ex)
                {
                    PostSystem("Unable to start Kilo: " + ex.Message);
                    UpdateButtons();
                    return;
                }
            }
        }

        if (project.Runtime is not null && SessionHostLauncher.IsAlive(project.Runtime))
        {
            await AttachAsync(project, token);
        }
        else if (project.ManualStop)
        {
            PostSystem("Stopped manually. Click Start / Resume to run Kilo again.");
        }

        UpdateButtons();
    }

    private void Launch(ProjectEntry project, bool resume)
    {
        project.ManualStop = false;
        project.Runtime = _launcher.Launch(project, resume);
        project.HasStarted = true;
        _store.Save(_projects);
    }

    private async Task AttachAsync(ProjectEntry project, CancellationToken cancellationToken)
    {
        if (project.Runtime is null) return;
        SessionClient? client = null;
        Exception? lastError = null;

        for (var attempt = 0; attempt < 30 && !cancellationToken.IsCancellationRequested; attempt++)
        {
            client?.Dispose();
            client = new SessionClient(project, project.Runtime);
            client.OutputReceived += (bytes, snapshot) => BeginInvoke((Action)(() =>
            {
                if (_activeProjectId != project.Id) return;
                if (snapshot) ResetTerminal();
                PostBytes(bytes);
            }));
            client.StatusChanged += running => BeginInvoke((Action)(() =>
            {
                if (!running && project.Runtime is not null)
                {
                    project.Runtime = null;
                    _store.Save(_projects);
                    RefreshProjectList(project.Id);
                    UpdateButtons();
                }
            }));
            client.Disconnected += () => BeginInvoke((Action)(() =>
            {
                if (_closing || _activeProjectId != project.Id) return;
                if (!SessionHostLauncher.IsAlive(project.Runtime))
                {
                    project.Runtime = null;
                    _store.Save(_projects);
                    RefreshProjectList(project.Id);
                    UpdateButtons();
                }
            }));

            try
            {
                await client.ConnectAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    client.Dispose();
                    return;
                }
                _activeClient = client;
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            {
                lastError = ex;
                if (cancellationToken.IsCancellationRequested) break;
                await Task.Delay(100, cancellationToken);
            }
        }

        client?.Dispose();
        if (!cancellationToken.IsCancellationRequested)
            PostSystem("SessionHost is running but the terminal pipe could not be attached: " + lastError?.Message);
    }

    private void AddProject()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = "Select the project folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;

        var fullPath = Path.GetFullPath(picker.SelectedPath);
        if (_projects.Any(p => string.Equals(p.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "That folder is already registered as a project.", "Duplicate project",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var fallback = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        if (string.IsNullOrWhiteSpace(fallback)) fallback = fullPath;
        var name = PromptDialog.Show(this, "New project", "Project name (leave blank to use the folder name):", fallback);
        if (name is null) return;

        var project = ProjectEntry.Create(fullPath, name);
        _projects.Add(project);
        _store.Save(_projects);
        RefreshProjectList(project.Id);
    }

    private void RenameProject()
    {
        var project = SelectedProject;
        if (project is null) return;
        var name = PromptDialog.Show(this, "Rename project", "Project name (blank = folder name):", project.Name);
        if (name is null) return;
        var fallback = Path.GetFileName(Path.TrimEndingDirectorySeparator(project.Path));
        project.Name = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        _store.Save(_projects);
        RefreshProjectList(project.Id);
    }

    private void OpenProjectFolder()
    {
        var project = SelectedProject;
        if (project is null || !Directory.Exists(project.Path)) return;
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{project.Path}\"", UseShellExecute = true });
    }

    private async Task StartSelectedAsync()
    {
        var project = SelectedProject;
        if (project is null || SessionHostLauncher.IsAlive(project.Runtime)) return;
        project.Runtime = null;
        project.ManualStop = false;
        try
        {
            Launch(project, resume: project.HasStarted);
            RefreshProjectList(project.Id);
            await ProjectSelectionChangedAsync();
        }
        catch (Exception ex)
        {
            PostSystem("Unable to start Kilo: " + ex.Message);
        }
    }

    private async Task RestartSelectedAsync()
    {
        var project = SelectedProject;
        if (project is null) return;
        if (SessionHostLauncher.IsAlive(project.Runtime))
        {
            try
            {
                if (_activeClient is null) await ProjectSelectionChangedAsync();
                if (_activeClient is not null) await _activeClient.TerminateAsync();
                await WaitForHostExitAsync(project, TimeSpan.FromSeconds(8));
            }
            catch { }
        }

        DetachClient();
        project.Runtime = null;
        project.ManualStop = false;
        _store.Save(_projects);
        try
        {
            Launch(project, resume: project.HasStarted);
            RefreshProjectList(project.Id);
            await ProjectSelectionChangedAsync();
        }
        catch (Exception ex)
        {
            PostSystem("Unable to restart Kilo: " + ex.Message);
        }
    }

    private async Task TerminateSelectedAsync()
    {
        var project = SelectedProject;
        if (project is null) return;

        if (SessionHostLauncher.IsAlive(project.Runtime))
        {
            try
            {
                if (_activeClient is null) await ProjectSelectionChangedAsync();
                if (_activeClient is not null) await _activeClient.TerminateAsync();
                await WaitForHostExitAsync(project, TimeSpan.FromSeconds(8));
            }
            catch { }
        }

        DetachClient();
        project.Runtime = null;
        project.ManualStop = true;
        _store.Save(_projects);
        ResetTerminal();
        PostSystem("Stopped manually. Closing the Manager never performs this action.");
        RefreshProjectList(project.Id);
        UpdateButtons();
    }

    private void RemoveProject()
    {
        var project = SelectedProject;
        if (project is null) return;
        if (SessionHostLauncher.IsAlive(project.Runtime))
        {
            MessageBox.Show(this, "Terminate the project first. Running sessions cannot be removed because that would orphan them.",
                "Project is running", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this, $"Remove '{project.Name}' from the manager?\n\nThe project folder and Kilo history are not deleted.",
                "Remove project", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _projects.Remove(project);
        _store.Save(_projects);
        DetachClient();
        _activeProjectId = null;
        RefreshProjectList();
        ResetTerminal();
        PostSystem("Project removed from the manager.");
    }

    private async Task WaitForHostExitAsync(ProjectEntry project, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until && SessionHostLauncher.IsAlive(project.Runtime))
            await Task.Delay(100);
    }

    private void StatusTick()
    {
        if (_closing) return;
        var changed = false;
        foreach (var project in _projects)
        {
            if (project.Runtime is not null && !SessionHostLauncher.IsAlive(project.Runtime))
            {
                project.Runtime = null;
                changed = true;
            }
        }
        if (changed)
        {
            _store.Save(_projects);
            RefreshProjectList(_activeProjectId);
        }
        else
        {
            RefreshProjectList(_activeProjectId, preserveOnly: true);
        }
        UpdateButtons();
    }

    private void RefreshProjectList(string? selectProjectId = null, bool preserveOnly = false)
    {
        if (_closing) return;
        var selectedId = selectProjectId ?? SelectedProject?.Id;
        _refreshingList = true;
        try
        {
            _projectList.BeginUpdate();
            _projectList.Items.Clear();
            foreach (var project in _projects)
                _projectList.Items.Add(new ProjectListItem(project, SessionHostLauncher.IsAlive(project.Runtime)));

            if (selectedId is not null)
            {
                for (var i = 0; i < _projectList.Items.Count; i++)
                {
                    if (((ProjectListItem)_projectList.Items[i]!).Project.Id == selectedId)
                    {
                        _projectList.SelectedIndex = i;
                        break;
                    }
                }
            }
            else if (!preserveOnly && _projectList.Items.Count > 0)
            {
                _projectList.SelectedIndex = 0;
            }
        }
        finally
        {
            _projectList.EndUpdate();
            _refreshingList = false;
        }
    }

    private void UpdateButtons()
    {
        var project = SelectedProject;
        var running = project is not null && SessionHostLauncher.IsAlive(project.Runtime);
        _renameButton.Enabled = project is not null;
        _openButton.Enabled = project is not null && Directory.Exists(project.Path);
        _startButton.Enabled = project is not null && !running;
        _restartButton.Enabled = project is not null;
        _terminateButton.Enabled = running;
        _removeButton.Enabled = project is not null && !running;
    }

    private void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_activeClient is null) return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();
            if (type == "input" && root.TryGetProperty("data", out var dataProp))
            {
                var base64 = dataProp.GetString();
                if (!string.IsNullOrEmpty(base64)) _ = _activeClient.SendInputAsync(Convert.FromBase64String(base64));
            }
            else if (type == "resize" &&
                     root.TryGetProperty("cols", out var colsProp) &&
                     root.TryGetProperty("rows", out var rowsProp))
            {
                _ = _activeClient.ResizeAsync(colsProp.GetInt32(), rowsProp.GetInt32());
            }
        }
        catch { }
    }

    private void PostBytes(byte[] bytes)
    {
        if (!_webReady || _terminal.CoreWebView2 is null || bytes.Length == 0) return;
        _terminal.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "output",
            data = Convert.ToBase64String(bytes)
        }));
    }

    private void PostSystem(string text)
    {
        var bytes = Encoding.UTF8.GetBytes($"\r\n\u001b[38;5;244m[Kilo Manager] {text}\u001b[0m\r\n");
        PostBytes(bytes);
    }

    private void ResetTerminal()
    {
        if (!_webReady || _terminal.CoreWebView2 is null) return;
        _terminal.CoreWebView2.PostWebMessageAsJson("{\"type\":\"reset\"}");
    }

    private void DetachClient()
    {
        var client = _activeClient;
        _activeClient = null;
        client?.Dispose();
    }

    private void CloseManagerOnly()
    {
        _closing = true;
        _statusTimer.Stop();
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = null;
        DetachClient();
    }

    private sealed class ProjectListItem
    {
        public ProjectEntry Project { get; }
        public bool Running { get; }
        public ProjectListItem(ProjectEntry project, bool running) { Project = project; Running = running; }
        public override string ToString()
        {
            var marker = Running ? "●" : Project.ManualStop ? "■" : "○";
            return $"{marker}  {Project.Name}";
        }
    }
}
