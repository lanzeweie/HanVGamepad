using Avalonia.Media.Imaging;
using QRCoder;

namespace HanVGamepad.Desktop.Services
{
    /// <summary>
    /// 二维码生成：地址 → Avalonia Bitmap。只是便利功能，画不出来就返回 null，界面自己降级。
    /// </summary>
    public static class QrCodeService
    {
        public static Bitmap? Build(string address, int pixelsPerModule = 10)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            try
            {
                using var generator = new QRCodeGenerator();
                using QRCodeData data = generator.CreateQrCode("http://" + address, QRCodeGenerator.ECCLevel.Q);
                using var png = new PngByteQRCode(data);
                byte[] bytes = png.GetGraphic(pixelsPerModule);

                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
