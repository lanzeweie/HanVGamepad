using System.Runtime.InteropServices;
using HanVGamepad.Driver.Abstractions;
using static HanVGamepad.Driver.Linux.LinuxNative;

namespace HanVGamepad.Driver.Linux
{
    /// <summary>
    /// Linux 驱动管理器（uinput 内核子系统）。
    /// Linux 的「安装」本质是权限配置：加载 uinput 模块 + udev rule + 用户组。
    /// 应用不调用 sudo（避免密码安全风险），生成配置脚本并提示用户以 sudo 执行。
    /// </summary>
    internal sealed class LinuxDriverManager : IDriverManager
    {
        private const string SetupScriptPath = "/tmp/hanvgamepad_setup.sh";
        private const string TeardownScriptPath = "/tmp/hanvgamepad_teardown.sh";
        private const string UdevRulePath = "/etc/udev/rules.d/99-hanvgamepad.rules";
        private const string ModulesLoadPath = "/etc/modules-load.d/hanvgamepad.conf";
        private const string UdevRuleContent =
            "KERNEL==\"uinput\", SUBSYSTEM==\"misc\", OPTIONS+=\"static_node=uinput\", TAG+=\"uaccess\", GROUP=\"input\", MODE=\"0660\"\n";

        public Task<DriverStatus> GetStatusAsync(CancellationToken ct = default)
        {
            // 此方法绝不抛异常：所有错误以 DiagnosticMessage 返回。
            try
            {
                bool deviceExists = File.Exists(LinuxNative.UinputDevicePath);
                bool moduleLoaded = IsModuleLoaded();

                bool installed = deviceExists || moduleLoaded;
                DriverPermissionState permission = ProbePermission(deviceExists);

                string? version = ReadVersion();
                string? diagnostic = null;

                if (!deviceExists && !moduleLoaded)
                {
                    diagnostic = "The uinput kernel module is not loaded. Run: sudo modprobe uinput";
                }
                else if (permission == DriverPermissionState.NeedsElevation)
                {
                    diagnostic = BuildManualSetupInstructions();
                }
                else if (permission == DriverPermissionState.Ok && IsInsideContainer())
                {
                    diagnostic = "Running inside a container: make sure /dev/uinput is passed through (docker run --device /dev/uinput).";
                }

                // Linux 无「服务」概念，IsServiceRunning 等同于模块可用。
                return Task.FromResult(new DriverStatus(installed, installed, version, permission, diagnostic));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new DriverStatus(
                    false, false, null, DriverPermissionState.Unknown,
                    "Driver status check failed: " + ex.Message));
            }
        }

        private static bool IsModuleLoaded()
        {
            try
            {
                if (!File.Exists("/proc/modules"))
                {
                    return false;
                }

                foreach (string line in File.ReadLines("/proc/modules"))
                {
                    if (line.StartsWith("uinput ", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static DriverPermissionState ProbePermission(bool deviceExists)
        {
            if (!deviceExists)
            {
                return DriverPermissionState.KernelModuleNotLoaded;
            }

            int fd = Open(LinuxNative.UinputDevicePath, O_WRONLY | O_NONBLOCK);
            if (fd >= 0)
            {
                Close(fd);
                return DriverPermissionState.Ok;
            }

            return Marshal.GetLastPInvokeError() switch
            {
                13 => DriverPermissionState.NeedsElevation,      // EACCES
                2 => DriverPermissionState.KernelModuleNotLoaded, // ENOENT
                _ => DriverPermissionState.PermissionDenied
            };
        }

        private static string? ReadVersion()
        {
            try
            {
                // uinput 模块自身通常没有 version 属性：退回内核版本。
                if (File.Exists("/sys/module/uinput/version"))
                {
                    return File.ReadAllText("/sys/module/uinput/version").Trim();
                }

                if (File.Exists("/proc/sys/kernel/osrelease"))
                {
                    return "kernel " + File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
                }
            }
            catch
            {
                // 版本读不到不影响使用。
            }

            return null;
        }

        private static bool IsInsideContainer()
        {
            return File.Exists("/.dockerenv") || File.Exists("/run/.containerenv");
        }

        public Task<DriverInstallResult> InstallAsync(IProgress<string>? progress, CancellationToken ct = default)
        {
            try
            {
                if (ProbePermission(File.Exists(LinuxNative.UinputDevicePath)) == DriverPermissionState.Ok)
                {
                    progress?.Report("uinput is already accessible; nothing to configure.");
                    return Task.FromResult(new DriverInstallResult(true, false, null));
                }

                // 生成配置脚本，让用户以 sudo 执行（应用绝不代跑 sudo）。
                File.WriteAllText(SetupScriptPath, BuildSetupScript());
                TryChmodExecutable(SetupScriptPath);

                string instructions = BuildManualSetupInstructions();
                progress?.Report("Configuration script written to " + SetupScriptPath);
                progress?.Report(instructions);

                // 不自动执行 sudo：返回失败并附完整命令，由界面/CLI 展示给用户。
                return Task.FromResult(new DriverInstallResult(false, false,
                    "uinput permission setup requires root. Run the generated script:\n  sudo bash " + SetupScriptPath + "\n\n" + instructions));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new DriverInstallResult(false, false, "Failed to prepare the setup script: " + ex.Message));
            }
        }

        public Task<DriverUninstallResult> UninstallAsync(CancellationToken ct = default)
        {
            try
            {
                File.WriteAllText(TeardownScriptPath, BuildTeardownScript());
                TryChmodExecutable(TeardownScriptPath);

                return Task.FromResult(new DriverUninstallResult(false,
                    "Removing the uinput configuration requires root. Run the generated script:\n  sudo bash " + TeardownScriptPath));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new DriverUninstallResult(false, "Failed to prepare the teardown script: " + ex.Message));
            }
        }

        public IGamepadBackend? CreateBackend()
        {
            // 只有 /dev/uinput 可写才建后端：否则 CreatePad 必然失败。
            return ProbePermission(File.Exists(LinuxNative.UinputDevicePath)) == DriverPermissionState.Ok
                ? new LinuxUinputBackend()
                : null;
        }

        private static string BuildSetupScript()
        {
            return
                "#!/bin/bash\n" +
                "# HanVGamepad uinput setup — run with: sudo bash " + SetupScriptPath + "\n" +
                "set -e\n\n" +
                "# 1. Load the uinput kernel module and make it persistent across reboots.\n" +
                "modprobe uinput\n" +
                "echo uinput > " + ModulesLoadPath + "\n\n" +
                "# 2. udev rule: allow members of the 'input' group to access /dev/uinput.\n" +
                "cat > " + UdevRulePath + " <<'EOF'\n" +
                UdevRuleContent +
                "EOF\n\n" +
                "# 3. Add the current user to the 'input' group (re-login required).\n" +
                "if [ -n \"$SUDO_USER\" ]; then\n" +
                "    usermod -aG input \"$SUDO_USER\"\n" +
                "else\n" +
                "    usermod -aG input \"$(logname 2>/dev/null || echo $USER)\"\n" +
                "fi\n\n" +
                "# 4. Reload udev rules.\n" +
                "udevadm control --reload-rules\n" +
                "udevadm trigger\n\n" +
                "echo 'Done. Log out and back in for the group change to take effect.'\n";
        }

        private static string BuildTeardownScript()
        {
            return
                "#!/bin/bash\n" +
                "# HanVGamepad uinput teardown — run with: sudo bash " + TeardownScriptPath + "\n" +
                "set -e\n\n" +
                "rm -f " + UdevRulePath + "\n" +
                "rm -f " + ModulesLoadPath + "\n" +
                "udevadm control --reload-rules\n" +
                "udevadm trigger\n" +
                "echo 'HanVGamepad uinput configuration removed.'\n";
        }

        private static string BuildManualSetupInstructions()
        {
            return
                "Manual setup (run in a terminal):\n" +
                "  sudo modprobe uinput\n" +
                "  echo uinput | sudo tee " + ModulesLoadPath + "\n" +
                "  sudo tee " + UdevRulePath + " <<'EOF'\n" +
                "  " + UdevRuleContent.TrimEnd() + "\n" +
                "  EOF\n" +
                "  sudo udevadm control --reload-rules && sudo udevadm trigger\n" +
                "  sudo usermod -aG input $USER    # then log out and back in";
        }

        private static void TryChmodExecutable(string path)
        {
            try
            {
                int fd = Open(path, O_WRONLY);
                if (fd >= 0)
                {
                    Close(fd);
                }

                // 尽力加上可执行位；失败不影响用户用 bash 显式执行。
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = "+x " + path,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                })?.WaitForExit(3000);
            }
            catch
            {
                // chmod 不可用：用户仍可 bash <script> 执行。
            }
        }
    }
}
