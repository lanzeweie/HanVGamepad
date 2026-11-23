using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace HanVGamepad.Desktop.Views
{
    /// <summary>
    /// 轻量确认对话框。只服务于「缺驱动」这类必须由用户拍板的场景，
    /// 不为此引入第三方对话框库：两个按钮 + 一段文字足够。
    /// </summary>
    internal static class ConfirmDialog
    {
        /// <summary>
        /// 显示确认框。没有主窗口时直接返回 false（不能把用户卡在一个无处可点的问句上）。
        /// </summary>
        public static async Task<bool> ShowAsync(
            Window? owner, string title, string message, string confirmText, string cancelText)
        {
            if (owner == null)
            {
                return false;
            }

            var confirmButton = new Button
            {
                Content = confirmText,
                MinWidth = 96,
                MinHeight = 32,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var cancelButton = new Button
            {
                Content = cancelText,
                MinWidth = 96,
                MinHeight = 32,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            // 取消在左、确认在右：和系统对话框的习惯保持一致。
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(confirmButton);

            var content = new StackPanel
            {
                Spacing = 18,
                Margin = new Thickness(20)
            };

            content.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 13
            });

            content.Children.Add(buttons);

            var dialog = new Window
            {
                Title = title,
                Width = 430,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = content
            };

            confirmButton.Click += (_, _) => dialog.Close(true);
            cancelButton.Click += (_, _) => dialog.Close(false);

            return await dialog.ShowDialog<bool>(owner);
        }
    }
}
