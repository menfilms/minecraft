using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace Launcher
{
    public partial class MainWindow : Window
    {
        private MSession? session;
        private ModrinthClient modrinthClient;
        private string instancesPath;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            modrinthClient = new ModrinthClient();

            instancesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instances");
            Directory.CreateDirectory(instancesPath);
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            loadingOverlay.Visibility = Visibility.Visible;
            loadingText.Text = "Инициализация WebView2...";
            
            try
            {
                await webView.EnsureCoreWebView2Async(null);
                
                loadingText.Text = "Загрузка интерфейса...";
                
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "complete", "ModernIndex.html");
                if (File.Exists(htmlPath))
                    webView.Source = new Uri(htmlPath);
                
                webView.NavigationCompleted += (s, args) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        loadingOverlay.Visibility = Visibility.Collapsed;
                    });
                };
            }
            catch (Exception ex)
            {
                loadingText.Text = $"Ошибка: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[ERR] Load: {ex.Message}");
            }
        }

        private async void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string msg = e.TryGetWebMessageAsString();
                System.Diagnostics.Debug.WriteLine($"[MSG] {msg}");

                if (msg.StartsWith("login:"))
                {
                    session = MSession.CreateOfflineSession(msg.Substring(6));
                    await webView.CoreWebView2.ExecuteScriptAsync($"onAuthSuccess('{session.Username}')");
                }
                else if (msg.StartsWith("launch:"))
                {
                    await LaunchInstance(msg.Substring(7));
                }
                else if (msg.StartsWith("search_mods:"))
                {
                    var parts = msg.Substring(12).Split(':');
                    await SearchMods(parts[0], parts[1],
                        parts.Length > 2 ? int.Parse(parts[2]) : 0,
                        parts.Length > 3 ? int.Parse(parts[3]) : 20);
                }
                else if (msg.StartsWith("download_mod:"))
                {
                    var parts = msg.Substring(13).Split(':');
                    await DownloadMod(parts[0], parts[1], parts[2], parts[3]);
                }
                else if (msg == "get_instances")
                {
                    await GetInstances();
                }
                else if (msg.StartsWith("create_instance:"))
                {
                    var parts = msg.Substring(16).Split(':');
                    await CreateInstance(parts[0], parts[1], parts[2]);
                }
                else if (msg.StartsWith("delete_instance:"))
                {
                    await DeleteInstance(msg.Substring(16));
                }
                else if (msg.StartsWith("get_installed_mods:"))
                {
                    await GetInstalledMods(msg.Substring(19));
                }
                else if (msg.StartsWith("get_mod_details:"))
                {
                    await GetModDetails(msg.Substring(16));
                }
                else if (msg.StartsWith("delete_mod:"))
                {
                    var parts = msg.Substring(10).Split(':');
                    await DeleteMod(parts[0], parts[1]);
                }
                else if (msg.StartsWith("toggle_mod:"))
                {
                    var parts = msg.Substring(11).Split(':');
                    await ToggleMod(parts[0], parts[1], parts[2] == "true");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERR] {ex.Message}");
            }
        }

        private async Task GetInstances()
        {
            var instances = new List<object>();

            if (Directory.Exists(instancesPath))
            {
                foreach (var dir in Directory.GetDirectories(instancesPath))
                {
                    var name = Path.GetFileName(dir);
                    var version = await SafeReadFileAsync(Path.Combine(dir, "version.txt"), "1.20.4");
                    var loader = await SafeReadFileAsync(Path.Combine(dir, "loader.txt"), "fabric");
                    var modsDir = Path.Combine(dir, "mods");
                    var modCount = Directory.Exists(modsDir) ? Directory.GetFiles(modsDir, "*.jar").Length : 0;

                    instances.Add(new { name, version, loader, modCount });
                }
            }

            var json = JsonSerializer.Serialize(new { type = "instances_list", instances });
            webView.CoreWebView2.PostWebMessageAsJson(json);
        }

        private async Task<string> SafeReadFileAsync(string path, string defaultValue)
        {
            try
            {
                return File.Exists(path) ? await File.ReadAllTextAsync(path) : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private async Task CreateInstance(string name, string version, string loader)
        {
            var instancePath = Path.Combine(instancesPath, name);
            Directory.CreateDirectory(instancePath);

            await File.WriteAllTextAsync(Path.Combine(instancePath, "version.txt"), version);
            await File.WriteAllTextAsync(Path.Combine(instancePath, "loader.txt"), loader);
            Directory.CreateDirectory(Path.Combine(instancePath, "mods"));

            var json = JsonSerializer.Serialize(new { type = "instance_created" });
            webView.CoreWebView2.PostWebMessageAsJson(json);
        }

        private async Task DeleteInstance(string name)
        {
            var path = Path.Combine(instancesPath, name);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                await GetInstances();
            }
        }

        private async Task GetInstalledMods(string instanceName)
        {
            var modsDir = Path.Combine(instancesPath, instanceName, "mods");
            var mods = new List<object>();

            if (Directory.Exists(modsDir))
            {
                foreach (var file in Directory.GetFiles(modsDir))
                {
                    var ext = Path.GetExtension(file).ToLower();
                    if (ext == ".jar" || ext == ".disabled")
                    {
                        var fileName = Path.GetFileName(file);
                        var isDisabled = ext == ".disabled";

                        var nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                        var projectId = nameWithoutExt;

                        mods.Add(new
                        {
                            filename = fileName,
                            name = nameWithoutExt,
                            project_id = projectId,
                            disabled = isDisabled
                        });
                    }
                }
            }

            var json = JsonSerializer.Serialize(new { type = "installed_mods", instanceName, mods });
            webView.CoreWebView2.PostWebMessageAsJson(json);
        }

        private async Task GetModDetails(string projectId)
        {
            try
            {
                var details = await modrinthClient.GetProjectDetailsAsync(projectId);
                var json = JsonSerializer.Serialize(new { type = "mod_details", mod = details });
                webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERR] GetModDetails: {ex.Message}");
            }
        }

        private async Task DeleteMod(string instanceName, string filename)
        {
            var filePath = Path.Combine(instancesPath, instanceName, "mods", filename);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            var json = JsonSerializer.Serialize(new { type = "mod_deleted", projectId = filename });
            webView.CoreWebView2.PostWebMessageAsJson(json);

            await GetInstalledMods(instanceName);
            await GetInstances();
        }

        private async Task ToggleMod(string instanceName, string filename, bool enable)
        {
            var modsDir = Path.Combine(instancesPath, instanceName, "mods");
            var filePath = Path.Combine(modsDir, filename);

            if (!File.Exists(filePath)) return;

            if (enable)
            {
                if (filename.EndsWith(".disabled"))
                {
                    var newFileName = filename.Replace(".disabled", ".jar");
                    var newPath = Path.Combine(modsDir, newFileName);
                    File.Move(filePath, newPath);
                }
            }
            else
            {
                if (filename.EndsWith(".jar"))
                {
                    var newFileName = filename.Replace(".jar", ".disabled");
                    var newPath = Path.Combine(modsDir, newFileName);
                    File.Move(filePath, newPath);
                }
            }

            var json = JsonSerializer.Serialize(new { type = "mod_toggled", projectId = filename, enabled = enable });
            webView.CoreWebView2.PostWebMessageAsJson(json);

            await GetInstalledMods(instanceName);
            await GetInstances();
        }

        private async Task SearchMods(string query, string version, int offset, int limit)
        {
            try
            {
                var result = await modrinthClient.SearchModsAsync(query, version, offset, limit);

                var payload = new Dictionary<string, object>
                {
                    ["type"] = "mods_result",
                    ["mods"] = result.Mods,
                    ["total"] = result.TotalHits
                };

                webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERR] SearchMods: {ex.Message}");
            }
        }

        private async Task DownloadMod(string projectId, string version, string instanceName, string loader)
        {
            try
            {
                var versions = await modrinthClient.GetModVersionsAsync(projectId, version, loader);
                if (versions.Count == 0) return;

                var ver = versions[0];
                var file = ver.Files.FirstOrDefault(f => f.Primary) ?? ver.Files.FirstOrDefault();
                if (file == null) return;

                var modsPath = Path.Combine(instancesPath, instanceName, "mods");
                Directory.CreateDirectory(modsPath);

                var savePath = Path.Combine(modsPath, $"{projectId}-{file.Filename}");

                await modrinthClient.DownloadModWithProgressAsync(file.Url, savePath, projectId,
                    (progress) => {
                        _ = webView.CoreWebView2.ExecuteScriptAsync(
                            $"onDownloadProgress('{projectId}', {progress})");
                    });

                var json = JsonSerializer.Serialize(new { type = "mod_downloaded", projectId });
                webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERR] DownloadMod: {ex.Message}");
            }
        }

        private async Task LaunchInstance(string instanceName)
        {
            try
            {
                if (session == null) return;

                var instancePath = Path.Combine(instancesPath, instanceName);
                var version = await SafeReadFileAsync(Path.Combine(instancePath, "version.txt"), "1.20.4");

                var gamePath = new MinecraftPath(instancePath);
                var launcher = new MinecraftLauncher(gamePath);

                launcher.FileProgressChanged += (s, args) =>
                {
                    Dispatcher.Invoke(async () =>
                    {
                        double percent = args.TotalTasks > 0 ? (double)(args.ProgressedTasks * 100) / args.TotalTasks : 0;
                        await webView.CoreWebView2.ExecuteScriptAsync($"updateProgress({percent}, '{args.Name}')");
                    });
                };

                var launchOption = new MLaunchOption
                {
                    MaximumRamMb = 4096,
                    Session = session
                };

                await webView.CoreWebView2.ExecuteScriptAsync("updateStatus('Загрузка файлов...'); document.getElementById('progressContainer').style.display = 'block';");

                var process = await launcher.InstallAndBuildProcessAsync(version, launchOption);
                process.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERR] Launch: {ex.Message}");
            }
        }
    }
}
