using System.Windows;

namespace MiSwitcher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 旧切换器配置文件为 GBK 编码，.NET Core 需注册代码页提供程序
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(Dump(args.Exception), "米家三合一切换器",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    private static string Dump(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        for (var e = (Exception?)ex; e != null; e = e.InnerException)
        {
            sb.AppendLine(e.GetType().Name + ": " + e.Message);
            sb.AppendLine(e.StackTrace);
            sb.AppendLine("——");
        }
        return sb.ToString();
    }
}
