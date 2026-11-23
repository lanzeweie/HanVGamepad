using System.Text.Json;
using HanVGamepad.Server.Logging;

namespace HanVGamepad.Server.Layouts
{
    /// <summary>一个保存到服务端的按键布局方案；Name 是唯一键，同名保存即覆盖。</summary>
    public sealed record SavedLayout(string Name, JsonElement Layout, DateTimeOffset UpdatedAt);

    /// <summary>POST /api/layouts 的请求体。</summary>
    public sealed record SaveLayoutRequest(string? Name, JsonElement Layout);

    /// <summary>
    /// 布局方案的落盘存储：%APPDATA%\HanVGamepad\layouts.json
    /// （Linux 上走同一个 ApplicationData 约定）。
    /// 写入走「临时文件 + Move 覆盖」，写一半崩掉不会留下半个 JSON。
    /// </summary>
    public sealed class LayoutStore
    {
        public const int MaxNameLength = 64;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly object _gate = new();
        private readonly List<SavedLayout> _layouts = new();
        private readonly string _directory;
        private readonly string _path;

        public LayoutStore(string? directory = null)
        {
            _directory = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HanVGamepad");
            _path = Path.Combine(_directory, "layouts.json");
            Load();
        }

        public IReadOnlyList<SavedLayout> List()
        {
            lock (_gate)
            {
                return _layouts.ToList();
            }
        }

        /// <summary>按名称保存，同名覆盖。名称非法或布局不是对象时返回 false。</summary>
        public bool Save(string? name, JsonElement layout)
        {
            string trimmed = name?.Trim() ?? string.Empty;
            if (trimmed.Length == 0 || trimmed.Length > MaxNameLength || layout.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            lock (_gate)
            {
                int existing = _layouts.FindIndex(item => string.Equals(item.Name, trimmed, StringComparison.Ordinal));
                SavedLayout? previous = existing >= 0 ? _layouts[existing] : null;
                var saved = new SavedLayout(trimmed, layout.Clone(), DateTimeOffset.UtcNow);

                if (existing >= 0)
                {
                    _layouts[existing] = saved;
                }
                else
                {
                    _layouts.Add(saved);
                }

                if (Persist())
                {
                    return true;
                }

                // 落盘失败就回滚内存：不能界面显示「已保存」而文件里没有。
                if (previous != null)
                {
                    _layouts[existing] = previous;
                }
                else
                {
                    _layouts.Remove(saved);
                }

                return false;
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return;
                }

                List<SavedLayout?>? loaded = JsonSerializer.Deserialize<List<SavedLayout?>>(
                    File.ReadAllText(_path), SerializerOptions);
                if (loaded == null)
                {
                    return;
                }

                _layouts.Clear();
                foreach (SavedLayout? item in loaded)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Name) || item.Layout.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    _layouts.Add(item);
                }
            }
            catch (Exception ex)
            {
                ServerLog.Warn($"布局方案读取失败，本次按空列表处理：{ex.Message}");
            }
        }

        private bool Persist()
        {
            try
            {
                Directory.CreateDirectory(_directory);

                string tempPath = _path + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(_layouts, SerializerOptions));
                File.Move(tempPath, _path, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                ServerLog.Warn($"布局方案写入失败：{ex.Message}");
                return false;
            }
        }
    }
}
