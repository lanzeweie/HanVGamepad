using System.Diagnostics;

namespace HanVGamepad.Cli.Commands
{
    /// <summary>
    /// 后台实例的 pid 文件（stop / status 用它找到运行中的服务进程）。
    /// 位置：应用数据目录 HanVGamepad/hanvgamepad.pid。
    /// </summary>
    public static class PidFile
    {
        public static string Path { get; } = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HanVGamepad",
            "hanvgamepad.pid");

        public static void Write(int pid)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, pid.ToString());
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
            }
            catch
            {
                // 清不掉不影响下次启动（写入会覆盖）。
            }
        }

        /// <summary>读 pid 并验证进程还活着；返回 null 表示没有可停止的实例。</summary>
        public static int? ReadAlive()
        {
            try
            {
                if (!File.Exists(Path))
                {
                    return null;
                }

                if (!int.TryParse(File.ReadAllText(Path).Trim(), out int pid))
                {
                    return null;
                }

                using Process process = Process.GetProcessById(pid);
                return pid;
            }
            catch (ArgumentException)
            {
                // 进程不存在。
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
