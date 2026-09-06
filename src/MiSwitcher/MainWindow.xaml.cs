using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MiSwitcher.Models;
using MiSwitcher.Services;
using WpfAnimatedGif;

namespace MiSwitcher;

public sealed class GameVm : INotifyPropertyChanged
{
    public GameDefinition Def { get; init; } = null!;
    public GameSettings Settings { get; init; } = null!;
    public bool IsSelected { get => _sel; set { _sel = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    private bool _sel;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ServerCardVm : INotifyPropertyChanged
{
    public ServerInfo Info { get; init; } = null!;
    public bool IsCurrent { get => _cur; set { _cur = value; PropertyChanged?.Invoke(this, new(nameof(IsCurrent))); } }
    public bool IsTarget { get => _tgt; set { _tgt = value; PropertyChanged?.Invoke(this, new(nameof(IsTarget))); } }
    private bool _cur, _tgt;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly List<GameVm> _games;
    private readonly Dictionary<string, List<ServerCardVm>> _serverCards = new();
    private GameVm _current = null!;
    private ServerKind _currentKind = ServerKind.Official;
    private ServerKind _target = ServerKind.Official;
    private List<SkinItem> _skins = new();
    private CancellationTokenSource? _switchCts;

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(System.IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Win11: 给无边框窗口启用系统圆角（DWMWCP_ROUND）；Win10 无此接口则保持直角
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int preference = 2 /* DWMWCP_ROUND */;
            DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref preference, sizeof(int));
        }
        catch { /* 非 Win11 环境 */ }
    }

    public MainWindow()
    {
        InitializeComponent();
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
        _settings = AppSettings.Load();
        _games = GameCatalog.Games.Select(g => new GameVm { Def = g, Settings = _settings.Game(g.Id) }).ToList();
        BuildGameButtons();
        var first = _games.FirstOrDefault(g => g.Def.Id == _settings.CurrentGameId) ?? _games[0];
        SelectGame(first);
        ApplySkinFile(_settings.SkinFile);
        ShowLog(_settings.ShowLog);
    }

    // ================= 侧边栏游戏按钮 =================

    private void BuildGameButtons()
    {
        GameList.Children.Clear();
        foreach (var vm in _games)
        {
            var accent = ParseColor(vm.Def.Accent);

            var icon = new Image
            {
                Width = 30,
                Height = 30,
                Source = new BitmapImage(new Uri("pack://application:,,,/" + vm.Def.IconPath)),
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0) };
            texts.Children.Add(new TextBlock
            {
                Text = vm.Def.DisplayName,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Effect = (Effect)FindResource("TextShadow"),
            });
            texts.Children.Add(new TextBlock
            {
                Text = vm.Def.Subtitle.Split('·')[0].Trim(),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
                Effect = (Effect)FindResource("TextShadow"),
            });
            var badge = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)),
                Visibility = Visibility.Collapsed,
                Child = new TextBlock { Text = "当前", FontSize = 10, Foreground = new SolidColorBrush(accent) },
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
            panel.Children.Add(icon);
            panel.Children.Add(texts);
            panel.Children.Add(badge);
            var bd = new Border
            {
                Child = panel,
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 9, 10, 9),
                BorderThickness = new Thickness(1),
            };
            var btn = new Button { Content = bd, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), Style = (Style)FindResource("NoChromeButton") };
            btn.Click += (_, _) => SelectGame(vm);
            GameList.Children.Add(btn);
            vm.PropertyChanged += (_, _) => StyleGameButton(bd, badge, vm, accent);
            StyleGameButton(bd, badge, vm, accent);
        }
    }

    private static void StyleGameButton(Border bd, Border badge, GameVm vm, Color accent)
    {
        if (vm.IsSelected)
        {
            bd.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(0x5A, accent.R, accent.G, accent.B), 0),
                    new(Color.FromArgb(0x1E, accent.R, accent.G, accent.B), 1),
                }, 90);
            bd.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B));
            badge.Visibility = Visibility.Visible;
        }
        else
        {
            bd.Background = Brushes.Transparent;
            bd.BorderBrush = Brushes.Transparent;
            badge.Visibility = Visibility.Collapsed;
        }
    }

    // ================= 选择游戏 / 主面板 =================

    private void SelectGame(GameVm vm)
    {
        _current = vm;
        _settings.CurrentGameId = vm.Def.Id;
        _settings.Save();
        foreach (var g in _games) g.IsSelected = g == vm;

        GameTitle.Text = vm.Def.DisplayName;
        GameSubtitle.Text = vm.Def.Subtitle;
        TitleHint.Text = vm.Def.DisplayName + " · 正在检测当前服务器…";

        var accent = ParseColor(vm.Def.Accent);
        ChipCurrent.Background = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B));
        ChipCurrentText.Foreground = new SolidColorBrush(accent);
        Progress.Foreground = new SolidColorBrush(accent);
        BtnSwitch.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(accent, 0),
                new(ParseColor(vm.Def.AccentDeep), 1),
            }, 45);

        BuildServerCards();
        RefreshStatus();
    }

    private void BuildServerCards()
    {
        ServerCards.Children.Clear();
        var cards = new List<ServerCardVm>();
        var accent = ParseColor(_current.Def.Accent);

        foreach (var kind in new[] { ServerKind.Official, ServerKind.Bilibili, ServerKind.International })
        {
            var info = _current.Def.Server(kind);
            var svm = new ServerCardVm { Info = info };
            cards.Add(svm);

            var dot = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var name = new TextBlock
            {
                Text = info.Name,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(22, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var curBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x30, accent.R, accent.G, accent.B)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 2, 7, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock { Text = "当前", FontSize = 10, Foreground = new SolidColorBrush(accent) },
            };

            var head = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            head.Children.Add(dot);
            head.Children.Add(name);
            head.Children.Add(curBadge);

            var sub = new TextBlock
            {
                Text = "cps=" + info.Cps,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xE8, 0xEC, 0xF4)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            var body = new StackPanel();
            body.Children.Add(head);
            body.Children.Add(sub);

            var bd = new Border
            {
                Child = body,
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(16, 13, 16, 13),
                Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x18, 0x1B, 0x25)),
                BorderThickness = new Thickness(1.4),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(0, 0, 14, 0),
            };
            var btn = new Button { Content = bd, Tag = svm, Cursor = Cursors.Hand, Style = (Style)FindResource("NoChromeButton") };
            btn.Click += (_, _) =>
            {
                _target = kind;
                foreach (var c in _serverCards.Values.SelectMany(x => x)) c.IsTarget = false;
                svm.IsTarget = true;
                UpdateSwitchButton();
            };
            ServerCards.Children.Add(btn);
            svm.PropertyChanged += (_, _) => StyleServerCard(bd, dot, curBadge, svm, accent);
            StyleServerCard(bd, dot, curBadge, svm, accent);
        }
        _serverCards[_current.Def.Id] = cards;
    }

    private static void StyleServerCard(Border bd, Border dot, Border curBadge, ServerCardVm svm, Color accent)
    {
        if (svm.IsTarget)
        {
            bd.BorderBrush = new SolidColorBrush(accent);
            bd.Background = new SolidColorBrush(Color.FromArgb(0xAA, 0x1E, 0x22, 0x30));
            dot.Background = new SolidColorBrush(accent);
            dot.BorderBrush = new SolidColorBrush(accent);
        }
        else
        {
            bd.BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            bd.Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x18, 0x1B, 0x25));
            dot.Background = Brushes.Transparent;
            dot.BorderBrush = new SolidColorBrush(Color.FromArgb(0x77, 0xFF, 0xFF, 0xFF));
        }
        curBadge.Visibility = svm.IsCurrent ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSwitchButton()
    {
        var targetCard = _serverCards.Values.SelectMany(x => x).FirstOrDefault(c => c.IsTarget);
        if (targetCard == null)
        {
            BtnSwitch.IsEnabled = false;
            BtnSwitch.Content = "一键切换";
            return;
        }
        var busy = Progress.Value > 0 && Progress.Value < 100;
        if (targetCard.Info.Kind == _currentKind)
        {
            BtnSwitch.Content = "当前服务器";
            BtnSwitch.IsEnabled = false;
        }
        else
        {
            BtnSwitch.Content = "切换到 " + targetCard.Info.Name;
            BtnSwitch.IsEnabled = !busy;
        }
    }

    // ================= 状态检测 =================

    private SwitchEngine Engine()
    {
        if (string.IsNullOrWhiteSpace(_current.Settings.GamePath) || !Directory.Exists(_current.Settings.GamePath))
            throw new DirectoryNotFoundException(
                $"{_current.Def.DisplayName} 的游戏目录未设置或不存在，请先在「游戏路径设置」中配置。");
        if (string.IsNullOrWhiteSpace(_current.Settings.PackPath) || !Directory.Exists(_current.Settings.PackPath))
            throw new DirectoryNotFoundException($"{_current.Def.DisplayName} 的资源替换包目录未设置或不存在。");
        return new SwitchEngine(_current.Def, _current.Settings.GamePath, _current.Settings.PackPath);
    }

    private void RefreshStatus()
    {
        try
        {
            var eng = Engine();
            var kind = eng.Detect(out var detail);
            _currentKind = kind;
            foreach (var c in _serverCards.Values.SelectMany(x => x))
            {
                c.IsCurrent = c.Info.Kind == kind;
                c.IsTarget = c.Info.Kind == kind;
            }
            ChipCurrentText.Text = "当前：" + _current.Def.Server(kind).Name;
            TitleHint.Text = _current.Def.DisplayName + " · 当前服务器：" + _current.Def.Server(kind).Name;
            ChipCurrent.ToolTip = detail;
            ChipGameVersion.Text = "游戏版本 " + (eng.GameVersion() ?? "未知");
            ChipPackVersion.Text = "替换包 " + (eng.PackVersion() ?? "未找到");
        }
        catch (Exception ex)
        {
            _currentKind = (ServerKind)(-1);
            foreach (var c in _serverCards.Values.SelectMany(x => x))
            {
                c.IsCurrent = false;
                c.IsTarget = false;
            }
            ChipCurrentText.Text = "当前：未配置";
            ChipCurrent.ToolTip = ex.Message;
            ChipGameVersion.Text = "游戏版本 -";
            ChipPackVersion.Text = "替换包 -";
        }
        UpdateSwitchButton();
    }

    // ================= 切换流程 =================

    private async void BtnSwitch_Click(object sender, RoutedEventArgs e)
    {
        var targetCard = _serverCards.Values.SelectMany(x => x).FirstOrDefault(c => c.IsTarget);
        if (targetCard == null) return;
        if (targetCard.Info.Kind == _currentKind) return;

        SwitchEngine eng;
        try { eng = Engine(); }
        catch (Exception ex) { await Alert(ex.Message); return; }

        var running = eng.IsGameRunning();
        var ok = await Confirm(
            $"即将把【{_current.Def.DisplayName}】切换到【{targetCard.Info.Name}】。\n\n" +
            "· 将用资源替换包内文件覆盖游戏目录\n" +
            (running ? "· 检测到游戏正在运行，切换时会自动结束游戏进程\n" : "") +
            "\n确定继续吗？");
        if (!ok) return;

        BtnSwitch.IsEnabled = false;
        BtnLaunch.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        LogText.Text = "";
        ShowLog(true, temp: true);

        _switchCts = new CancellationTokenSource();
        var progress = new Progress<SwitchProgress>(p =>
        {
            Progress.Value = p.Percent;
            StageText.Text = p.Stage;
            if (p.Detail != "" && !p.Detail.EndsWith('%'))
                LogText.Text += p.Detail + "\n";
        });

        SwitchResult result;
        try
        {
            result = await eng.SwitchAsync(targetCard.Info.Kind, progress, _switchCts.Token);
        }
        catch (Exception ex)
        {
            result = new SwitchResult { Success = false, Message = ex.Message };
        }

        foreach (var line in result.Log) LogText.Text += line + "\n";
        Progress.Value = result.Success ? 100 : 0;
        StageText.Text = result.Message;
        BtnLaunch.IsEnabled = true;
        UpdateSwitchButton();

        if (result.Success)
        {
            RefreshStatus();
            await Alert(result.Message + "\n\n现在可以启动游戏了。");
        }
        else
        {
            await Alert(result.Message + "\n\n详情请查看日志。");
        }
    }

    private void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var eng = Engine();
            var kind = eng.Detect(out _);
            eng.Launch(kind);
            StageText.Text = "已启动游戏（" + _current.Def.Server(kind).Name + "）";
        }
        catch (Exception ex)
        {
            _ = Alert(ex.Message);
        }
    }

    private void BtnOpenDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = _current.Settings.GamePath;
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            Process.Start("explorer.exe", dir);
        else
            _ = Alert("游戏目录不存在，请先在设置中配置。");
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshStatus();

    // ================= 皮肤 =================

    private void BtnSkin_Click(object sender, RoutedEventArgs e)
    {
        _skins = SkinStore.Scan();
        ShowSkinPicker();
    }

    private void ShowSkinPicker()
    {
        var root = DialogRoot("切换器皮肤", out var closeBtn);

        var list = new ScrollViewer
        {
            Margin = new Thickness(20, 2, 20, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        // 三列铺满弹窗宽度：卡片宽度 = (弹窗内容宽 - 列间距) / 3
        var cardW = Math.Floor((660 - 40) / 3.0) - 8;   // 198：三列恰好铺满 620 内容宽
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = cardW + 8, ItemHeight = 116 };
        wrap.Children.Add(SkinCard(null, cardW));
        foreach (var s in _skins) wrap.Children.Add(SkinCard(s, cardW));
        list.Content = wrap;
        Grid.SetRow(list, 2);
        root.Children.Add(list);

        var foot = new StackPanel { Margin = new Thickness(20, 6, 20, 14) };
        var opRow = new DockPanel { LastChildFill = false };
        var opLabel = new TextBlock
        {
            Text = "背景不透明度",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF4)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var slider = new Slider
        {
            Minimum = 0.3,
            Maximum = 1.0,
            Value = Math.Clamp(_settings.SkinOpacity, 0.3, 1.0),
            Width = 420,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("FancySlider"),
        };
        var opVal = new TextBlock
        {
            Text = slider.Value.ToString("P0"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF4)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            MinWidth = 42,
        };
        var sliderReady = false;
        slider.ValueChanged += (_, _) =>
        {
            if (!sliderReady) return;   // 仅用户拖动时保存
            _settings.SkinOpacity = slider.Value;
            opVal.Text = slider.Value.ToString("P0");
            SkinImage.Opacity = slider.Value;
            _settings.Save();
        };
        sliderReady = true;
        opRow.Children.Add(opLabel);
        DockPanel.SetDock(opVal, Dock.Right);
        opRow.Children.Add(opVal);
        opRow.Children.Add(slider);
        foot.Children.Add(opRow);
        Grid.SetRow(foot, 3);
        root.Children.Add(foot);

        closeBtn.Click += (_, _) => CloseOverlay();
        OpenOverlay(root);
    }

    private Border SkinCard(SkinItem? skin, double cardWidth)
    {
        var accent = ParseColor(_current.Def.Accent);
        var isSelected = skin != null &&
                         string.Equals(skin.Path, _settings.SkinFile, StringComparison.OrdinalIgnoreCase);

        var img = new Image
        {
            Height = 92,
            Stretch = Stretch.UniformToFill,
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.LowQuality);

        Grid grid;
        if (skin == null)
        {
            img.Source = null;
            img.Stretch = Stretch.None;
            grid = new Grid();
            grid.Children.Add(new Border
            {
                Height = 92,
                Background = new LinearGradientBrush(Color.FromRgb(0x2A, 0x2E, 0x3A), Color.FromRgb(0x16, 0x19, 0x22), 45),
            });
            grid.Children.Add(new TextBlock
            {
                Text = "不使用皮肤",
                Foreground = Brushes.White,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            var thumb = SkinStore.LoadThumb(skin.Path);
            if (thumb != null) img.Source = thumb;
            grid = new Grid();
            grid.Children.Add(img);
            grid.Children.Add(new TextBlock
            {
                Text = skin.Name,
                FontSize = 10.5,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
                Padding = new Thickness(5, 2, 5, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                MaxWidth = cardWidth,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var bd = new Border
        {
            Width = cardWidth,
            Child = grid,
            CornerRadius = new CornerRadius(9),
            ClipToBounds = true,
            BorderThickness = new Thickness(isSelected ? 2.4 : 1),
            BorderBrush = isSelected ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 8, 12),
            Cursor = Cursors.Hand,
            ToolTip = skin == null ? "恢复默认渐变背景" : skin.Source + " / " + skin.Name,
        };
        bd.MouseDown += (_, _) =>
        {
            if (skin == null || string.Equals(skin.Path, _settings.SkinFile, StringComparison.OrdinalIgnoreCase))
            {
                _settings.SkinFile = "";
                ApplySkinFile("");
            }
            else
            {
                _settings.SkinFile = skin.Path;
                ApplySkinFile(skin.Path);
            }
            _settings.Save();
            CloseOverlay();
        };
        return bd;
    }

    private void ApplySkinFile(string file)
    {
        SkinImage.Source = null;
        ImageBehavior.SetAnimatedSource(SkinImage, null);
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            SkinImage.Opacity = 1.0;
            // 无皮肤：使用当前游戏主题色的深色渐变
            var accent = ParseColor(_current.Def.Accent);
            var deep = ParseColor(_current.Def.AccentDeep);
            BgLayer.Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(0x14, 0x17, 0x21), 0),
                    new(Color.FromArgb(0xFF, deep.R, deep.G, deep.B), 1),
                }, 115);
            return;
        }
        BgLayer.Background = new SolidColorBrush(Color.FromRgb(0x10, 0x13, 0x1B));
        SkinImage.Opacity = Math.Clamp(_settings.SkinOpacity, 0.3, 1.0);
        try
        {
            var frame = BitmapFrame.Create(new Uri(file), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
            {
                ImageBehavior.SetAnimatedSource(SkinImage, frame);
                ImageBehavior.SetRepeatBehavior(SkinImage, System.Windows.Media.Animation.RepeatBehavior.Forever);
            }
            else
            {
                SkinImage.Source = frame;
            }
        }
        catch
        {
            SkinImage.Source = null;
        }
    }

    // ================= 设置 =================

    private void BtnSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void ShowSettings()
    {
        var root = DialogRoot("游戏路径设置", out var closeBtn);

        var rows = new StackPanel { Margin = new Thickness(22, 4, 22, 0) };
        var editors = new Dictionary<string, (TextBox Game, TextBox Pack)>();
        foreach (var vm in _games)
        {
            rows.Children.Add(new TextBlock
            {
                Text = vm.Def.DisplayName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8)),
                Margin = new Thickness(0, 10, 0, 6),
            });
            var gTb = RowEditor(rows, "游戏目录", vm.Settings.GamePath);
            var pTb = RowEditor(rows, "资源替换包", vm.Settings.PackPath);
            editors[vm.Def.Id] = (gTb, pTb);
        }
        var sv = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(sv, 1);
        root.Children.Add(sv);

        var foot = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 20, 14),
        };
        var save = new Button
        {
            Content = "保存",
            Style = (Style)FindResource("PillButton"),
            Padding = new Thickness(26, 7, 26, 7),
        };
        save.Click += (_, _) =>
        {
            foreach (var vm in _games)
            {
                var (g, p) = editors[vm.Def.Id];
                vm.Settings.GamePath = g.Text.Trim();
                vm.Settings.PackPath = p.Text.Trim();
            }
            _settings.Save();
            CloseOverlay();
            RefreshStatus();
        };
        foot.Children.Add(save);
        Grid.SetRow(foot, 2);
        root.Children.Add(foot);

        closeBtn.Click += (_, _) => CloseOverlay();
        OpenOverlay(root);
    }

    private TextBox RowEditor(StackPanel parent, string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x84)),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var tb = new TextBox
        {
            Text = value,
            Style = (Style)FindResource("RoundedTextBox"),
        };
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);
        var browse = new Button
        {
            Content = "浏览",
            Style = (Style)FindResource("PillButton"),
            Margin = new Thickness(8, 0, 0, 0),
        };
        browse.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog
            {
                CheckFileExists = false,
                ValidateNames = false,
                FileName = "选择此文件夹",
                InitialDirectory = Directory.Exists(tb.Text) ? tb.Text : AppLayout.AppDir,
            };
            if (dlg.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(dlg.FileName);
                if (dir != null) tb.Text = dir;
            }
        };
        Grid.SetColumn(browse, 2);
        grid.Children.Add(browse);
        parent.Children.Add(grid);
        return tb;
    }

    // ================= 确认 / 提示弹窗 =================

    private TaskCompletionSource<bool>? _dialogTcs;

    private Grid DialogRoot(string title, out Button closeBtn)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new Grid { Margin = new Thickness(20, 16, 14, 6) };
        head.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8)),
        });
        closeBtn = new Button { Style = (Style)FindResource("IconButton"), HorizontalAlignment = HorizontalAlignment.Right };
        closeBtn.Content = new TextBlock
        {
            Text = "\u2715",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF4, 0xF8)),
        };
        head.Children.Add(closeBtn);
        root.Children.Add(head);
        return root;
    }

    // ================= 采集 Persistent =================

    /// <summary>
    /// 把当前游戏目录的 Persistent 热更内容复制进资源替换包：
    /// 官服/B服 → 官服镜像，国际服 → 国际服镜像。切换到对应服时会自动带上。
    /// </summary>
    private async void BtnPersistent_Click(object sender, RoutedEventArgs e)
    {
        var game = _current;
        try
        {
            if (string.IsNullOrWhiteSpace(game.Settings.GamePath) || !Directory.Exists(game.Settings.GamePath))
            {
                await Alert($"{game.Def.DisplayName} 的游戏目录未设置或不存在，请先在「游戏路径设置」中配置。");
                return;
            }
            if (string.IsNullOrWhiteSpace(game.Settings.PackPath) || !Directory.Exists(game.Settings.PackPath))
            {
                await Alert($"{game.Def.DisplayName} 的资源替换包目录未设置或不存在。");
                return;
            }

            var kind = Engine().Detect(out _);
            var dataFolder = game.Def.DataFolder(kind);
            var src = Path.Combine(game.Settings.GamePath, dataFolder, "Persistent");
            if (!Directory.Exists(src))
            {
                await Alert($"未找到 {dataFolder}\\Persistent。\n请先在当前服（{game.Def.Server(kind).Name}）进入游戏，等待热更下载完成后退出，再点此按钮。");
                return;
            }

            long total = 0;
            int count = 0;
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                total += new FileInfo(f).Length;
                count++;
            }

            var mirror = kind == ServerKind.International
                ? game.Def.PackServerFolderInternational
                : game.Def.PackServerFolderOfficial;
            var dst = Path.Combine(game.Settings.PackPath, mirror, dataFolder, "Persistent");

            var ok = await Confirm(
                $"当前服务器：{game.Def.Server(kind).Name}（Persistent 共 {count} 个文件，约 {total / 1048576:0} MB）\n\n" +
                $"将复制到替换包：\n{dst}\n\n继续吗？（已有内容会被覆盖更新）");
            if (!ok) return;

            StageText.Text = "正在采集 Persistent…";
            var (files, bytes) = await Task.Run(() => CopyDir(src, dst));
            await Alert($"✔ 已复制 {files} 个文件（{bytes / 1048576:0} MB）。\n之后切换到该服时会自动带上这些热更内容。");
        }
        catch (Exception ex)
        {
            await Alert("采集失败：" + ex.Message);
        }
    }

    private static (int Files, long Bytes) CopyDir(string src, string dst)
    {
        int files = 0;
        long bytes = 0;
        var buffer = new byte[1024 * 1024];
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                bytes += read;
            }
            files++;
        }
        return (files, bytes);
    }

    private Task<bool> Confirm(string message)
    {
        _dialogTcs = new TaskCompletionSource<bool>();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13.5,
            LineHeight = 22,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF4)),
            Margin = new Thickness(26, 24, 26, 10),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 22, 18),
        };
        var cancel = new Button
        {
            Content = "取消",
            Style = (Style)FindResource("PillButton"),
            Margin = new Thickness(0, 0, 10, 0),
        };
        var ok = new Button
        {
            Content = "确定",
            Style = (Style)FindResource("PillButton"),
            Padding = new Thickness(24, 7, 24, 7),
            Background = new SolidColorBrush(ParseColor(_current.Def.Accent)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x16, 0x12, 0x08)),
            FontWeight = FontWeights.SemiBold,
        };
        cancel.Click += (_, _) => { _dialogTcs.TrySetResult(false); CloseOverlay(); };
        ok.Click += (_, _) => { _dialogTcs.TrySetResult(true); CloseOverlay(); };
        btns.Children.Add(cancel);
        btns.Children.Add(ok);
        root.Children.Add(btns);

        OpenOverlay(root);
        return _dialogTcs.Task;
    }

    private async Task Alert(string message)
    {
        StageText.Text = message.Split('\n')[0];
        await Confirm(message);
    }

    // ================= 弹层基础 =================

    private void OpenOverlay(FrameworkElement content)
    {
        PopupHost.Child = content;
        Overlay.Visibility = Visibility.Visible;
    }

    private void CloseOverlay()
    {
        Overlay.Visibility = Visibility.Collapsed;
        PopupHost.Child = null;
    }

    private void OverlayBg_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dialogTcs?.TrySetResult(false);
        _dialogTcs = null;
        CloseOverlay();
    }

    // ================= 日志 =================

    private void BtnLog_Click(object sender, RoutedEventArgs e) =>
        LogPanel.Visibility = LogPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void ShowLog(bool show, bool temp = false)
    {
        LogPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        BtnLog.Visibility = temp ? Visibility.Visible : (_settings.ShowLog ? Visibility.Visible : Visibility.Collapsed);
    }

    // ================= 窗口控制 =================

    private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
