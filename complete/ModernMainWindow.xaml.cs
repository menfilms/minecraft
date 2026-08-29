using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace Launcher
{
    public partial class ModernMainWindow : Window
    {
        private MSession? session;
        private ModrinthClient modrinthClient;
        private string instancesPath;
        private bool isMaximized = false;

        public ModernMainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            modrinthClient = new ModrinthClient();

            instancesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "instances");
            Directory.CreateDirectory(instancesPath);
        }

        // Обработчики кнопок управления окном
        private void MinimizeClick(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeClick(object sender, RoutedEventArgs e)
        {
            if (isMaximized)
            {
                this.WindowState = WindowState.Normal;
                isMaximized = false;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                isMaximized = true;
            }
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Перетаскивание окна
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.GetPosition(this).Y < 50) // Только если кликнули в верхней области
            {
                this.DragMove();
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Показываем экран загрузки с анимацией
                loadingOverlay.Visibility = Visibility.Visible;
                loadingText.Text = "Запуск лаунчера...";
                
                // Запускаем анимацию
                var storyboard = (Storyboard)loadingOverlay.FindResource("LoadingAnimation");
                storyboard?.Begin();

                await Task.Delay(500); // Небольшая задержка для плавности
                
                loadingText.Text = "Инициализация WebView2...";
                await webView.EnsureCoreWebView2Async();
                
                loadingText.Text = "Загрузка современного интерфейса...";
                
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "complete", "ModernIndex.html");
                if (!File.Exists(htmlPath))
                {
                    htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.html");
                }
                
                if (File.Exists(htmlPath))
                {
                    loadingText.Text = "Инициализация компонентов...";
                    await Task.Delay(300);
                    
                    webView.Source = new Uri(htmlPath);
                    
                    // Скрываем экран загрузки после полной загрузки страницы
                    webView.NavigationCompleted += async (s, args) =>
                    {
                        await Task.Delay(500); // Небольшая задержка для плавности
                        
                        Dispatcher.Invoke(() =>
                        {
                            var fadeOutStoryboard = new Storyboard();
                            var fadeOutAnimation = new DoubleAnimation
                            {
                                From = 1.0,
                                To = 0.0,
                                Duration = TimeSpan.FromMilliseconds(400)
                            };
                            
                            Storyboard.SetTarget(fadeOutAnimation, loadingOverlay);
                            Storyboard.SetTargetProperty(fadeOutAnimation, 
                                new PropertyPath(UIElement.OpacityProperty));
                            
                            fadeOutStoryboard.Children.Add(fadeOutAnimation);
                            fadeOutStoryboard.Completed += (sender, e) =>
                            {
                                loadingOverlay.Visibility = Visibility.Collapsed;
                            };
                            
                            fadeOutStoryboard.Begin();
                        });
                    };
                }
                else
                {
                    loadingText.Text = "Ошибка: Файл интерфейса не найден!";
                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                loadingText.Text = $"Критическая ошибка: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[ERR] Load: {ex.Message}");
                
                MessageBox.Show($"Произошла ошибка при запуске:\n{ex.Message}", 
                    "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    UpdateStatus($"Пользователь: {session.Username}");
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

        private void UpdateStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                statusText.Text = status;
            });
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
            
            UpdateStatus($"Создан инстанс: {name}");
        }

        private async Task DeleteInstance(string name)
        {
            var path = Path.Combine(instancesPath, name);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                await GetInstances();
                UpdateStatus($"Удален инстанс: {name}");
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
            
            UpdateStatus($"Мод удален: {filename}");
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

                UpdateStatus($"Загрузка мода: {file.Filename}");
                
                await modrinthClient.DownloadModWithProgressAsync(file.Url, savePath, projectId,
                    (progress) => {
                        _ = webView.CoreWebView2.ExecuteScriptAsync(
                            $"onDownloadProgress('{projectId}', {progress})");
                    });

                var json = JsonSerializer.Serialize(new { type = "mod_downloaded", projectId });
                webView.CoreWebView2.PostWebMessageAsJson(json);
                
                UpdateStatus($"Мод загружен: {file.Filename}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERR] DownloadMod: {ex.Message}");
                UpdateStatus($"Ошибка загрузки мода");
            }
        }

        private async Task LaunchInstance(string instanceName)
        {
            try
            {
                if (session == null)
                {
                    UpdateStatus("Ошибка: Требуется авторизация");
                    return;
                }

                var instancePath = Path.Combine(instancesPath, instanceName);
                var version = await SafeReadFileAsync(Path.Combine(instancePath, "version.txt"), "1.20.4");

                var gamePath = new MinecraftPath(instancePath);
                var launcher = new MinecraftLauncher(gamePath);

                UpdateStatus($"Запуск {instanceName}...");

                launcher.FileProgressChanged += (s, args) =>
                {
                    Dispatcher.Invoke(async () =>
                    {
                        double percent = args.TotalTasks > 0 ? (double)(args.ProgressedTasks * 100) / args.TotalTasks : 0;
                        await webView.CoreWebView2.ExecuteScriptAsync($"updateProgress({percent}, '{args.Name}')");
                        UpdateStatus($"Загрузка: {args.Name} ({percent:F1}%)");
                    });
                };

                var launchOption = new MLaunchOption
                {
                    MaximumRamMb = 4096, // Увеличили до 4GB
                    Session = session
                };

                await webView.CoreWebView2.ExecuteScriptAsync("updateStatus('Загрузка файлов игры...'); document.getElementById('progressContainer').style.display = 'block';");

                var process = await launcher.InstallAndBuildProcessAsync(version, launchOption);
                process.Start();
                
                UpdateStatus($"Minecraft запущен: {instanceName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERR] Launch: {ex.Message}");
                UpdateStatus($"Ошибка запуска: {ex.Message}");
            }
        }
    }
}
