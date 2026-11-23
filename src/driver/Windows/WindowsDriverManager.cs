using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Win32;
using HanVGamepad.Driver.Abstractions;

namespace HanVGamepad.Driver.Windows
{
    /// <summary>
    /// Windows 驱动管理器（ViGEmBus v1.22.0）。
    /// 检测走注册表 + 服务控制器；安装走内嵌安装器：提权后显示 ViGEmBus 自己的安装向导，
    /// 由用户在向导里读到并同意许可协议后才真正安装——不做静默安装，驱动分发与商店审核都要求显式同意。
    /// 卸载走系统登记的那个卸载器（同样需要 UAC）。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WindowsDriverManager : IDriverManager
    {
        private const string RegistryPath = @"SYSTEM\CurrentControlSet\Services\ViGEmBus";
        private const string ServiceName = "ViGEmBus";
        private const string InstallerFileName = "ViGEmBus_1.22.0_x64_x86_arm64.exe";
        private const string InstallerResourceSuffix = "ViGEmBus_1.22.0_x64_x86_arm64.exe";

        /// <summary>卸载器是无人值守的，超时按失败处理。</summary>
        private const int UninstallTimeoutMs = 180_000;

        /// <summary>交互式安装在等人：用户在向导里看协议、点下一步，给足十分钟。</summary>
        private const int InteractiveInstallTimeoutMs = 600_000;

        /// <summary>Windows Installer：用户在安装向导里点了取消。</summary>
        private const int ErrorInstallUserExit = 1602;

        public Task<DriverStatus> GetStatusAsync(CancellationToken ct = default)
        {
            // 此方法绝不抛异常：所有错误以 DiagnosticMessage 返回。
            try
            {
                bool installed = IsDriverInstalled();
                bool serviceRunning = IsServiceRunning();
                string? version = ReadDriverVersion();
                DriverPermissionState permission = ProbePermission(installed);

                string? diagnostic = null;
                if (!installed)
                {
                    diagnostic = "ViGEmBus driver is not installed.";
                }
                else if (permission != DriverPermissionState.Ok)
                {
                    diagnostic = "ViGEmBus driver is installed but not usable: " + permission;
                }

                return Task.FromResult(new DriverStatus(installed, serviceRunning, version, permission, diagnostic));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new DriverStatus(
                    false, false, null, DriverPermissionState.Unknown,
                    "Driver status check failed: " + ex.Message));
            }
        }

        /// <summary>
        /// 已安装 = 服务键在 **且** 驱动文件真的存在。
        /// 只查键会把「卸载后残留的注册表项」误判成已安装：那种状态下服务是 STOPPED、
        /// driver 文件已被删除，连接驱动必然失败，界面就会显示
        /// "installed but not usable: KernelModuleNotLoaded" —— 实际是卸载残留，不是装坏了。
        /// </summary>
        private static bool IsDriverInstalled()
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RegistryPath);
            return key != null && DriverFileExists();
        }

        private static bool IsServiceRunning()
        {
            try
            {
                using var controller = new System.ServiceProcess.ServiceController(ServiceName);
                return controller.Status == System.ServiceProcess.ServiceControllerStatus.Running;
            }
            catch
            {
                // 服务不存在或无法访问：视为未运行。
                return false;
            }
        }

        private static string? ReadDriverVersion()
        {
            try
            {
                string? path = ResolveDriverImagePath();
                if (path == null || !File.Exists(path))
                {
                    return null;
                }

                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                return info.FileVersion ?? info.ProductVersion;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 服务注册项里 ImagePath 指向的驱动文件路径；解析不出来或读不到键都返回 null。
        /// ImagePath 常见写法是 \??\C:\... 或 \SystemRoot\System32\drivers\xxx.sys，
        /// 后者是内核路径，File.Exists 不认，必须先展开成真实盘符路径。
        /// </summary>
        private static string? ResolveDriverImagePath()
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RegistryPath);
            string? imagePath = key?.GetValue("ImagePath") as string;
            if (string.IsNullOrEmpty(imagePath))
            {
                return null;
            }

            string path = imagePath.Trim().Trim('"');
            if (path.StartsWith(@"\??\", StringComparison.Ordinal))
            {
                path = path[4..];
            }

            int space = path.IndexOf(' ');
            if (space > 0)
            {
                path = path[..space];
            }

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.Combine(windows, path[@"\SystemRoot\".Length..]);
            }
            else if (path.StartsWith(@"\DosDevices\", StringComparison.OrdinalIgnoreCase))
            {
                path = path[@"\DosDevices\".Length..];
            }
            else if (path.StartsWith("%SystemRoot%", StringComparison.OrdinalIgnoreCase))
            {
                path = Environment.ExpandEnvironmentVariables(path);
            }

            return path;
        }

        /// <summary>
        /// 「应用和功能」里登记的那个卸载命令。
        /// 卸载必须用它，而不是我们内嵌的安装包：内嵌的是固定版本（1.22.0 Burn bundle），
        /// 机器上装的可能是别的版本（例如 1.21.442 MSI），bundle 找不到自己的包会直接退出 0，
        /// 表现就是"点了卸载什么也没发生"。
        /// </summary>
        private static string? ReadRegisteredUninstallCommand()
        {
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (string root in roots)
            {
                try
                {
                    using RegistryKey? uninstall = Registry.LocalMachine.OpenSubKey(root);
                    if (uninstall == null)
                    {
                        continue;
                    }

                    foreach (string subKeyName in uninstall.GetSubKeyNames())
                    {
                        using RegistryKey? entry = uninstall.OpenSubKey(subKeyName);
                        string? displayName = entry?.GetValue("DisplayName") as string;
                        if (displayName == null ||
                            !displayName.Contains("ViGEm", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string? command = entry?.GetValue("UninstallString") as string;
                        if (!string.IsNullOrWhiteSpace(command))
                        {
                            return command;
                        }
                    }
                }
                catch
                {
                    // 读不了这条分支就继续找下一个。
                }
            }

            return null;
        }

        /// <summary>把登记项里的卸载命令拆成「可执行文件 + 参数」，MSI 的静默参数由我们补。</summary>
        private static (string FileName, string Arguments) ParseUninstallCommand(string command)
        {
            string trimmed = command.Trim();

            if (trimmed.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase))
            {
                int space = trimmed.IndexOf(' ');
                string exe = space > 0 ? trimmed[..space] : trimmed;
                string args = space > 0 ? trimmed[(space + 1)..] : string.Empty;
                return (exe, (args + " /qn /norestart").Trim());
            }

            // 其它安装器原样调用：不认识它的静默参数就别硬加。
            if (trimmed.StartsWith('"'))
            {
                int end = trimmed.IndexOf('"', 1);
                if (end > 0)
                {
                    return (trimmed[1..end], trimmed[(end + 1)..].Trim());
                }
            }

            int firstSpace = trimmed.IndexOf(' ');
            return firstSpace > 0
                ? (trimmed[..firstSpace], trimmed[(firstSpace + 1)..].Trim())
                : (trimmed, string.Empty);
        }

        /// <summary>驱动文件还在不在。注册表键残留但文件已删 = 卸载其实已经生效，只是要重启清键。</summary>
        private static bool DriverFileExists()
        {
            try
            {
                string? path = ResolveDriverImagePath();
                return path != null && File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static DriverPermissionState ProbePermission(bool installed)
        {
            if (!installed)
            {
                return DriverPermissionState.NeedsElevation;
            }

            try
            {
                // 尝试以当前用户连接驱动：能建客户端就说明权限 OK。
                using var probe = new Nefarius.ViGEm.Client.ViGEmClient();
                return DriverPermissionState.Ok;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return DriverPermissionState.PermissionDenied;
            }
            catch (Nefarius.ViGEm.Client.Exceptions.VigemBusNotFoundException)
            {
                return DriverPermissionState.KernelModuleNotLoaded;
            }
            catch
            {
                return DriverPermissionState.Unknown;
            }
        }

        public async Task<DriverInstallResult> InstallAsync(IProgress<string>? progress, CancellationToken ct = default)
        {
            if (IsDriverInstalled())
            {
                progress?.Report("ViGEmBus driver is already installed.");
                return new DriverInstallResult(true, false, null);
            }

            string tempExePath = Path.Combine(Path.GetTempPath(), "HanVGamepad", "ViGEmBus_Setup.exe");

            try
            {
                // 1. 提取内嵌安装器到临时目录。
                progress?.Report("Extracting the bundled ViGEmBus installer...");
                Directory.CreateDirectory(Path.GetDirectoryName(tempExePath)!);
                await ExtractInstallerAsync(tempExePath, ct);

                // 2. 提权后显示 ViGEmBus 自己的安装向导（不带 /quiet）：许可协议必须由用户本人
                //    在向导里确认，静默安装既不透明，过不了驱动分发与商店审核。
                progress?.Report("Opening the ViGEmBus setup wizard — accept its license and finish the wizard to continue.");
                int? exitCode = await RunInstallerAsync(tempExePath, "/norestart", ct, InteractiveInstallTimeoutMs);

                if (exitCode == null)
                {
                    // 安装器可能仍在运行（用户还停在向导里）：不删临时文件。
                    return new DriverInstallResult(false, false,
                        "The ViGEmBus setup wizard did not finish within 10 minutes. Finish or cancel it manually, then refresh the driver status.");
                }

                // 3. 重新检测注册表确认。
                bool installed = IsDriverInstalled();
                if (installed)
                {
                    try { File.Delete(tempExePath); } catch { /* 清理失败不影响结果 */ }
                    progress?.Report("ViGEmBus driver installed successfully.");
                    return new DriverInstallResult(true, false, null);
                }

                if (exitCode == ErrorInstallUserExit)
                {
                    CleanupTemp(tempExePath);
                    return new DriverInstallResult(false, false,
                        "The ViGEmBus setup wizard was cancelled, so the driver is still not installed.");
                }

                string error = ParseInstallerFailure(tempExePath);
                try { File.Delete(tempExePath); } catch { }
                return new DriverInstallResult(false, false, error);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED：用户拒绝了 UAC。
                CleanupTemp(tempExePath);
                return new DriverInstallResult(false, false,
                    "The UAC prompt was declined. Administrator privileges are required to install the driver.");
            }
            catch (Exception ex)
            {
                CleanupTemp(tempExePath);
                return new DriverInstallResult(false, false, "Driver installation failed: " + ex.Message);
            }
        }

        private static async Task ExtractInstallerAsync(string targetPath, CancellationToken ct)
        {
            Assembly assembly = typeof(WindowsDriverManager).Assembly;
            string? resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(InstallerResourceSuffix, StringComparison.OrdinalIgnoreCase));

            if (resourceName == null)
            {
                throw new InvalidOperationException(
                    $"Embedded driver installer resource not found ({InstallerResourceSuffix}). The package may be corrupted.");
            }

            await using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await stream.CopyToAsync(fileStream, ct);
        }

        /// <summary>跑安装器/卸载器；返回退出码，超时或被取消返回 null。</summary>
        private static async Task<int?> RunInstallerAsync(string exePath, string arguments, CancellationToken ct, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                Verb = "runas",
                CreateNoWindow = true,
                UseShellExecute = true
            };

            using Process? process = Process.Start(psi);
            if (process == null)
            {
                return null;
            }

            // 安装器不能无限期挂着：超时按失败处理，但保留临时文件（安装器可能仍在运行）。
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
                return process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>安装器退出码非 0 时从日志提取错误原因；取不到就给退出码。</summary>
        private static string ParseInstallerFailure(string exePath)
        {
            string logPath = Path.Combine(Path.GetTempPath(), "ViGEmBus_Setup.log");
            try
            {
                if (File.Exists(logPath))
                {
                    string tail = File.ReadAllText(logPath);
                    if (tail.Length > 2000)
                    {
                        tail = tail[^2000..];
                    }

                    return "Installer reported failure. Log tail:\n" + tail;
                }
            }
            catch
            {
                // 日志读不出来就退回通用提示。
            }

            return $"The installer finished but the driver is still not registered. Run \"{exePath}\" manually to inspect the error.";
        }

        public async Task<DriverUninstallResult> UninstallAsync(CancellationToken ct = default)
        {
            if (!IsDriverInstalled())
            {
                // 不再静默返回成功：用户点过卸载却看不到任何动静，多半就是走到了这里。
                return new DriverUninstallResult(true,
                    "ViGEmBus is not registered as a service — nothing to uninstall.");
            }

            try
            {
                // 执行「系统里登记的那个卸载器」，而不是我们内嵌的安装包：
                // 内嵌的是 1.22.0 Burn bundle，机器上装的可能是别的版本（例如 1.21 MSI），
                // bundle 找不到自己的包时会直接退出 0，看起来就是「点了卸载什么也没发生」。
                string? command = ReadRegisteredUninstallCommand();
                if (string.IsNullOrEmpty(command))
                {
                    return new DriverUninstallResult(false,
                        "ViGEmBus is registered as a service but has no entry in \"Apps & features\", " +
                        "so the uninstaller cannot be located. Remove it from Device Manager instead.");
                }

                (string fileName, string arguments) = ParseUninstallCommand(command);

                int? exitCode = await RunInstallerAsync(fileName, arguments, ct, UninstallTimeoutMs);
                if (exitCode == null)
                {
                    return new DriverUninstallResult(false,
                        "The uninstaller did not exit within 180 seconds. Try removing \"ViGEm Bus Driver\" from Apps & features.");
                }

                // 3010 = ERROR_SUCCESS_REBOOT_REQUIRED：卸载成功，但要重启才彻底清干净。
                bool rebootRequired = exitCode == 3010;

                // 卸载器退出 ≠ 服务键立刻消失：停止服务、删除驱动文件都可能滞后，等一会儿再判定。
                if (await WaitForDriverRemovedAsync(TimeSpan.FromSeconds(20), ct))
                {
                    return new DriverUninstallResult(true, null, rebootRequired);
                }

                // 键还在但驱动文件已经没了：卸载其实已经生效，残留注册表项重启后会被清掉。
                if (!DriverFileExists())
                {
                    return new DriverUninstallResult(true, null, true);
                }

                return new DriverUninstallResult(false,
                    $"The uninstaller exited with code {exitCode}, but the driver is still registered. " +
                    "Close anything that is using the gamepad, then retry; a reboot always clears it.");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new DriverUninstallResult(false,
                    "The UAC prompt was declined. Administrator privileges are required to uninstall the driver.");
            }
            catch (Exception ex)
            {
                return new DriverUninstallResult(false, "Driver uninstall failed: " + ex.Message);
            }
        }

        /// <summary>轮询等服务注册项消失；超时返回 false（由调用方结合驱动文件判断）。</summary>
        private static async Task<bool> WaitForDriverRemovedAsync(TimeSpan timeout, CancellationToken ct)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

            while (true)
            {
                if (!IsDriverInstalled())
                {
                    return true;
                }

                if (DateTimeOffset.UtcNow >= deadline)
                {
                    return false;
                }

                try
                {
                    await Task.Delay(500, ct);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }

        public IGamepadBackend? CreateBackend()        {
            if (!IsDriverInstalled())
            {
                return null;
            }

            try
            {
                return new WindowsViGEmBackend();
            }
            catch
            {
                // 驱动虽已注册但不可连接（权限 / 服务未运行）：视为未就绪。
                return null;
            }
        }

        private static void CleanupTemp(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 清理失败无所谓。
            }
        }
    }
}
