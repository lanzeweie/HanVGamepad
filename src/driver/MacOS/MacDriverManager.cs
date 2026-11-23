using System.Diagnostics;
using System.Text.Json;
using HanVGamepad.Driver.Abstractions;

namespace HanVGamepad.Driver.MacOS
{
    /// <summary>
    /// macOS 驱动管理器：管理的对象是 OpenJoystickDriver（OJD），不是内核驱动。
    ///
    /// macOS 不允许普通签名程序创建虚拟 HID（需要 Apple 的受限 entitlement），
    /// 所以这里没有「安装驱动」这个动作可做，只有：
    /// - 检测 OJD 与它的 service 是否就绪；
    /// - 未安装时打开下载页、已安装未运行时代为 <c>ojd service start</c>；
    /// - 就绪后创建 <see cref="MacGamepadBackend"/>。
    /// </summary>
    internal sealed class MacDriverManager : IDriverManager
    {
        private const string ReleasesUrl = "https://github.com/xsyetopz/OpenJoystickDriver/releases/latest";
        private const string AppBundlePath = "/Applications/OpenJoystickDriver.app";

        private static readonly string[] CandidatePaths =
        {
            "/usr/local/bin/ojd",
            "/opt/homebrew/bin/ojd",
            "/usr/bin/ojd"
        };

        public Task<DriverStatus> GetStatusAsync(CancellationToken ct = default)
        {
            // 与其它平台一致：本方法绝不抛异常。
            return Task.Run(() =>
            {
                try
                {
                    string? ojd = FindOjd();
                    if (ojd == null)
                    {
                        // 装了 app 但没装命令行工具很常见：这时该让用户去 OJD 的设置里装，
                        // 而不是把「没装」的结论甩给他、再让他下一遍 DMG。
                        return new DriverStatus(
                            false, false, null, DriverPermissionState.Unknown,
                            IsAppInstalled()
                                ? "OpenJoystickDriver is installed, but its command-line tool is missing. " +
                                  "Open OpenJoystickDriver, use Settings > Install Command-Line Tool, then press Refresh."
                                : "OpenJoystickDriver is not installed. Install it from " + ReleasesUrl +
                                  ", then enable the command-line tool in its Settings and run: ojd service start");
                    }

                    (string state, string? version) = ReadServiceState(ojd);
                    bool running = string.Equals(state, "Running", StringComparison.Ordinal);

                    string? diagnostic = running
                        ? "If games cannot see the controller, grant OpenJoystickDriver Input Monitoring and Accessibility in System Settings > Privacy & Security."
                        : "OpenJoystickDriver is installed but its service is stopped. Run: ojd service start";

                    // OJD 自己申请并持有系统权限，本进程没有可独立探测的权限位：
                    // 服务能起来就按「正常」报，真实权限问题会以 feed 的 stderr 暴露出来。
                    return new DriverStatus(
                        true,
                        running,
                        version,
                        running ? DriverPermissionState.Ok : DriverPermissionState.Unknown,
                        diagnostic);
                }
                catch (Exception ex)
                {
                    return new DriverStatus(
                        false, false, null, DriverPermissionState.Unknown,
                        "Driver status check failed: " + ex.Message);
                }
            }, ct);
        }

        public Task<DriverInstallResult> InstallAsync(IProgress<string>? progress, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                try
                {
                    string? ojd = FindOjd();
                    if (ojd == null)
                    {
                        if (IsAppInstalled())
                        {
                            progress?.Report("Opening OpenJoystickDriver so you can install its command-line tool…");
                            OpenApp();

                            return new DriverInstallResult(false, false,
                                "Install the command-line tool in OpenJoystickDriver: Settings > Install Command-Line Tool, then press Refresh.");
                        }

                        progress?.Report("Opening the OpenJoystickDriver download page…");
                        OpenInBrowser(ReleasesUrl);

                        return new DriverInstallResult(false, false,
                            "OpenJoystickDriver is required on macOS but cannot be installed automatically. " +
                            "Install it from " + ReleasesUrl +
                            ", enable the command-line tool in its Settings, then press Refresh.");
                    }

                    progress?.Report("Starting the OpenJoystickDriver service…");
                    RunOjd(ojd, 15000, "service", "start", "--timeout", "10");

                    (string state, _) = ReadServiceState(ojd);
                    if (string.Equals(state, "Running", StringComparison.Ordinal))
                    {
                        progress?.Report("OpenJoystickDriver service is running.");
                        return new DriverInstallResult(true, false, null);
                    }

                    return new DriverInstallResult(false, false,
                        "The OpenJoystickDriver service did not start. Open OpenJoystickDriver.app once, " +
                        "grant Input Monitoring and Accessibility, then run: ojd service start");
                }
                catch (Exception ex)
                {
                    return new DriverInstallResult(false, false, "Failed to prepare OpenJoystickDriver: " + ex.Message);
                }
            }, ct);
        }

        public Task<DriverUninstallResult> UninstallAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new DriverUninstallResult(false,
                "OpenJoystickDriver is a separate application: quit it and move OpenJoystickDriver.app to the Trash. " +
                "HanVGamepad does not modify it."));
        }

        public IGamepadBackend? CreateBackend()
        {
            string? ojd = FindOjd();
            return ojd == null ? null : new MacGamepadBackend(ojd);
        }

        /// <summary>按常见安装位置找 ojd 命令行工具；没有就回落到 PATH。</summary>
        private static string? FindOjd()
        {
            foreach (string candidate in CandidatePaths)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string? pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVariable))
            {
                return null;
            }

            foreach (string directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string combined = Path.Combine(directory, "ojd");
                if (File.Exists(combined))
                {
                    return combined;
                }
            }

            return null;
        }

        /// <summary>读 <c>ojd --json status</c> 里的 service.state / service.version。</summary>
        private static (string State, string? Version) ReadServiceState(string ojd)
        {
            string output = RunOjd(ojd, 5000, "--no-input", "--json", "status") ?? string.Empty;
            if (output.Length == 0)
            {
                return ("Stopped", null);
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(output);
                if (document.RootElement.TryGetProperty("service", out JsonElement service))
                {
                    string state = service.TryGetProperty("state", out JsonElement stateElement)
                        ? stateElement.GetString() ?? "Stopped"
                        : "Stopped";

                    string? version = service.TryGetProperty("version", out JsonElement versionElement)
                        ? versionElement.GetString()
                        : null;

                    return (state, version);
                }
            }
            catch (JsonException)
            {
                // 输出不是预期 JSON：按未运行处理，交给诊断文案说明。
            }

            return ("Stopped", null);
        }

        private static string? RunOjd(string ojd, int timeoutMs, params string[] arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo(ojd)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (string argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("ojd did not start.");

                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // 已经被回收。
                    }

                    return null;
                }

                _ = stderr.GetAwaiter().GetResult();
                return stdout.GetAwaiter().GetResult();
            }
            catch
            {
                return null;
            }
        }

        private static void OpenInBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false });
            }
            catch
            {
                // 打不开浏览器就只留文案指引。
            }
        }

        /// <summary>OpenJoystickDriver.app 是否已装到 /Applications。</summary>
        private static bool IsAppInstalled()
        {
            try
            {
                return Directory.Exists(AppBundlePath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>把已安装的 OJD 拉到前台，让用户去它的设置里装命令行工具。</summary>
        private static void OpenApp()
        {
            try
            {
                Process.Start(new ProcessStartInfo("open", "-a OpenJoystickDriver") { UseShellExecute = false });
            }
            catch
            {
                // 打不开就只留文案指引。
            }
        }
    }
}
