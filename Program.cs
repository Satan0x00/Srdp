// Srdp — 极简现代 RDP 管理器 (OrcaTerm 风格)
// 单文件 WPF(.NET Framework 4.8 / C#5),内嵌微软 MsTscAx = 与 mstsc 同引擎
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WF = System.Windows.Forms;
using WinFormsHost = System.Windows.Forms.Integration.WindowsFormsHost;
using Shell = System.Windows.Shell;
using IOPath = System.IO.Path;
using System.Reflection;

[assembly: AssemblyTitle("Srdp")]
[assembly: AssemblyDescription("极简现代远程桌面管理器")]
[assembly: AssemblyCompany("Srdp")]
[assembly: AssemblyProduct("Srdp")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Srdp")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]


static class T {
    public static Color Hex(string h) {
        h = h.TrimStart('#');
        return Color.FromArgb(255, Convert.ToByte(h.Substring(0,2),16), Convert.ToByte(h.Substring(2,2),16), Convert.ToByte(h.Substring(4,2),16));
    }
    public static SolidColorBrush B(string h) { var b = new SolidColorBrush(Hex(h)); b.Freeze(); return b; }
    public const string BG0="#0B0B0E", BG1="#101014", BG2="#141419", BG3="#1B1B22", LINE="#26262E",
                          FG="#D7D7DE", FG2="#8A8A93", ACC="#4F8CFF", GREEN="#34C759", RED="#FF453A", DOT="#55555E";
    public static readonly FontFamily UI = new FontFamily("Microsoft YaHei UI, Segoe UI");
    public static readonly FontFamily MDL = new FontFamily("Segoe MDL2 Assets");
    public static ControlTemplate BtnTemplate(CornerRadius radius) {
        var t = new ControlTemplate(typeof(Button));
        var b = new FrameworkElementFactory(typeof(Border));
        b.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background"));
        b.SetValue(Border.CornerRadiusProperty, radius);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        b.AppendChild(cp); t.VisualTree = b; return t;
    }
}

class RdpAx : WF.AxHost {
    public RdpAx() : base("8b918b82-7985-4c24-89df-c33ad2bbfbcd") { Dock = WF.DockStyle.Fill; }
    protected override WF.CreateParams CreateParams {
        get {
            WF.CreateParams cp = base.CreateParams;
            cp.Style |= 0x04000000 | 0x10000000; // WS_CHILD | WS_VISIBLE
            return cp;
        }
    }
    public dynamic Oc { get { return (object)GetOcx(); } }
}

class Conn {
    public string Id = Guid.NewGuid().ToString("N");
    public string Name = "", Host = "", User = "", Pass = "", Group = "";
    public int Port = 3389;
}
class Row { public Conn C; public Button Bd; public Ellipse Dot; public bool Sel; }
class Tab {
    public Conn C; public Row R; public Border Bd; public Ellipse Dot; public TextBlock Label;
    public WinFormsHost Host; public RdpAx Ax; public dynamic Oc; public bool Dead;
    public string ReUser = "", RePass = "";
    public int LastW, LastH;
    public bool Reconnecting, LoginDone;
    public DateTime LoginAt;
}

static class J {
    public static string Esc(string s) {
        if (s == null) s = "";
        var sb = new StringBuilder("\"");
        foreach (char c in s) {
            if (c=='"') sb.Append("\\\""); else if (c=='\\') sb.Append("\\\\");
            else if (c=='\n') sb.Append("\\n"); else if (c=='\r') sb.Append("\\r"); else if (c=='\t') sb.Append("\\t");
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
    static string Unesc(string raw, int start, int end) {
        var sb = new StringBuilder();
        for (int i = start; i < end; i++) {
            char c = raw[i];
            if (c == '\\' && i + 1 < end) {
                char n = raw[++i];
                switch (n) {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '/': sb.Append('/'); break;
                    case 'u':
                        if (i + 4 < raw.Length) { sb.Append((char)int.Parse(raw.Substring(i+1,4), NumberStyles.HexNumber)); i += 4; }
                        break;
                    default: sb.Append(n); break;
                }
            } else sb.Append(c);
        }
        return sb.ToString();
    }
    public static List<Conn> Parse(string raw) {
        var list = new List<Conn>();
        int i = 0, n = raw.Length;
        Action skip = () => { while (i<n && char.IsWhiteSpace(raw[i])) i++; };
        skip(); if (i>=n || raw[i]!='[') return list; i++;
        while (true) {
            skip(); if (i>=n) break;
            if (raw[i]==']') { i++; break; }
            if (raw[i]==',') { i++; continue; }
            if (raw[i]!='{') break;
            var c = new Conn();
            i++;
            while (true) {
                skip(); if (i>=n) break;
                if (raw[i]=='}') { i++; break; }
                if (raw[i]==',') { i++; continue; }
                if (raw[i]!='"') break;
                int e = raw.IndexOf('"', i+1); if (e<0) break;
                string key = Unesc(raw, i+1, e); i = e+1;
                skip(); if (i<n && raw[i]==':') i++; skip();
                if (i>=n) break;
                string val = ""; int num = 0;
                if (raw[i]=='"') {
                    int j = i+1;
                    while (j<n) { if (raw[j]=='\\') { j+=2; continue; } if (raw[j]=='"') break; j++; }
                    val = Unesc(raw, i+1, j); i = j+1;
                } else {
                    int j = i; while (j<n && raw[j]!=',' && raw[j]!='}' && !char.IsWhiteSpace(raw[j])) j++;
                    int.TryParse(raw.Substring(i, j-i), out num); i = j;
                }
                if (key=="id" && val.Length>0) c.Id = val;
                else if (key=="name") c.Name = val;
                else if (key=="host") c.Host = val;
                else if (key=="user") c.User = val;
                else if (key=="pass") c.Pass = val;
                else if (key=="group") c.Group = val;
                else if (key=="port") c.Port = num>0?num:3389;
            }
            if (!string.IsNullOrEmpty(c.Host)) list.Add(c);
        }
        return list;
    }
    public static string Write(List<Conn> cs) {
        var sb = new StringBuilder("[\n");
        for (int i=0;i<cs.Count;i++) {
            var c = cs[i];
            sb.Append("  {\"id\":").Append(Esc(c.Id))
              .Append(",\"name\":").Append(Esc(c.Name))
              .Append(",\"host\":").Append(Esc(c.Host))
              .Append(",\"port\":").Append(c.Port)
              .Append(",\"user\":").Append(Esc(c.User))
              .Append(",\"pass\":").Append(Esc(c.Pass))
              .Append(",\"group\":").Append(Esc(c.Group)).Append("}");
            sb.Append(i<cs.Count-1 ? ",\n" : "\n");
        }
        return sb.Append(']').ToString();
    }
}

// DPAPI 密码存储(当前用户加密,密文落盘,连接时解密注入)
static class Secret {
    public static string Enc(string plain) {
        if (string.IsNullOrEmpty(plain)) return "";
        try {
            var b = System.Security.Cryptography.ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plain), null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(b);
        } catch { return ""; }
    }
    public static string Dec(string enc) {
        if (string.IsNullOrEmpty(enc)) return "";
        try {
            var p = System.Security.Cryptography.ProtectedData.Unprotect(
                Convert.FromBase64String(enc), null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(p);
        } catch { return ""; }
    }
}

static class Diag {
    static readonly string LogFile = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Srdp", "debug.log");
    public static void Log(string s) {
        try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\n"); } catch {}
    }
    public delegate void EmptyEv();
    public delegate void ReasonEv(int code);
}

// 侧栏宽度动画(GridLength 不能直接动画,需要自定义 AnimationTimeline)
class GridLengthAnimation : System.Windows.Media.Animation.AnimationTimeline {
    public override Type TargetPropertyType { get { return typeof(GridLength); } }
    protected override System.Windows.Freezable CreateInstanceCore() { return new GridLengthAnimation(); }
    public static readonly DependencyProperty FromProperty = DependencyProperty.Register("From", typeof(GridLength), typeof(GridLengthAnimation));
    public static readonly DependencyProperty ToProperty = DependencyProperty.Register("To", typeof(GridLength), typeof(GridLengthAnimation));
    public GridLength From { get { return (GridLength)GetValue(FromProperty); } set { SetValue(FromProperty, value); } }
    public GridLength To { get { return (GridLength)GetValue(ToProperty); } set { SetValue(ToProperty, value); } }
    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, System.Windows.Media.Animation.AnimationClock clock) {
        double f = From.Value;
        if (defaultOriginValue is GridLength) f = ((GridLength)defaultOriginValue).Value;
        double t = To.Value;
        double p = clock.CurrentProgress.HasValue ? clock.CurrentProgress.Value : 1;
        p = 1 - (1 - p) * (1 - p) * (1 - p); // easeOutCubic
        return new GridLength(f + (t - f) * p, GridUnitType.Pixel);
    }
}

// 与界面风格一致的暗色消息框(替代原生 MessageBox)
class DarkMsg {
    public static void Info(Window owner, string title, string msg) { Show(owner, title, msg, false); }
    public static bool Confirm(Window owner, string title, string msg) { return Show(owner, title, msg, true); }
    static bool Show(Window owner, string title, string msg, bool confirm) {
        var w = new Window {
            Title = "OrcaRDP-Dialog",
            Width = 400, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var card = new Border { Background = T.B(T.BG2), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(20,16,20,16) };
        var sp = new StackPanel();
        var titleTb = new TextBlock { Text = title, Foreground = T.B(T.FG), FontSize = 14, FontWeight = FontWeights.SemiBold, FontFamily = T.UI, Margin = new Thickness(0,0,0,0) };
        titleTb.MouseLeftButtonDown += (s,ev)=> { try { w.DragMove(); } catch {} };
        sp.Children.Add(titleTb);
        sp.Children.Add(new Border { Background = T.B(T.LINE), Height = 1, Margin = new Thickness(0,10,0,12) });
        sp.Children.Add(new TextBlock { Text = msg, Foreground = T.B(T.FG2), FontSize = 13, FontFamily = T.UI, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,14) });
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (confirm) {
            var cancel = DialogBtn("取消", T.BG3, T.FG2);
            cancel.MouseLeftButtonUp += (s,ev)=> { w.DialogResult = false; w.Close(); };
            btns.Children.Add(cancel);
        }
        var ok = DialogBtn("确定", T.ACC, "#FFFFFF");
        ok.MouseLeftButtonUp += (s,ev)=> { w.DialogResult = true; w.Close(); };
        btns.Children.Add(ok);
        sp.Children.Add(btns);
        card.Child = sp; w.Content = card;
        w.KeyDown += (s,ev)=> { if (ev.Key==Key.Escape) { w.DialogResult=false; w.Close(); } else if (ev.Key==Key.Enter) { w.DialogResult=true; w.Close(); } };
        w.Owner = owner;
        bool? res = w.ShowDialog();
        return res == true;
    }
    static Border DialogBtn(string text, string bg, string fg) {
        var b = new Border { MinWidth = 76, Height = 30, Margin = new Thickness(8,0,0,0), Background = T.B(bg), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Child = new TextBlock { Text = text, Foreground = T.B(fg), FontFamily = T.UI, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        b.MouseEnter += (s,ev)=> b.Background = T.B(bg==T.ACC ? "#6B9DFF" : T.BG2);
        b.MouseLeave += (s,ev)=> b.Background = T.B(bg);
        return b;
    }
}

class MainWin : Window {
    readonly List<Conn> conns = new List<Conn>();
    readonly List<Row> rows = new List<Row>();
    readonly List<Tab> tabs = new List<Tab>();
    StackPanel listPanel; TextBox searchBox; TextBlock watermark;
    StackPanel tabStrip; Grid tabContent; TextBlock emptyHint; Grid contentInner; TextBlock statusText; Border StatusBanner;
    Button maxBtn;
    Row selected;
    System.Collections.Generic.Dictionary<string, Border> groupHeaders = new System.Collections.Generic.Dictionary<string, Border>();
    System.Collections.Generic.HashSet<string> collapsedGroups = new System.Collections.Generic.HashSet<string>();
    Border HeaderBar, RailBar, SideBar, StripBorder;
    ColumnDefinition RailCol, SideCol;
    bool fullscreen; Tab fullscreenTab, resizeTab, activeTab; bool ssUserAction;
    readonly DispatcherTimer ssTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
    // 会话分辨率跟随窗口:防抖后 断开→改分辨率→重连(活动会话上直接改 DesktopWidth 会 E_FAIL)
    // 会话分辨率跟随窗口:防抖 700ms 后只对当前活动 tab 重连(断开→改分辨率→重连同一会话)
    // 小幅变化(<100px)交给 SmartSizing 缩放兜底,避免频繁重连
    void ResizeToHost() {
        if (activeTab == null || activeTab.Oc == null || activeTab.Dead || !activeTab.LoginDone) return;
        if ((DateTime.Now - activeTab.LoginAt).TotalSeconds < 8 && !ssUserAction) return; // 登录后宽限期只拦自动调度;用户主动变化立即对齐 // 登录后宽限期仅对自动触发生效,用户主动改变屏幕大小不受限
        int w = (int)activeTab.Host.ActualWidth, h = (int)activeTab.Host.ActualHeight;
        if (w < 200 || h < 150) return; // 隐藏/极小的 tab 跳过
        int th = ssUserAction ? 120 : 300; // 用户主动变化(侧栏/全屏)阈值低,自动调度阈值高防抖
        if (Math.Abs(w - activeTab.LastW) < th && Math.Abs(h - activeTab.LastH) < th) return;
        activeTab.LastW = w; activeTab.LastH = h;
        activeTab.Reconnecting = true;
        if (activeTab.Dot != null) activeTab.Dot.Fill = T.B(T.ACC);
        if (activeTab.Label != null) { activeTab.Label.ToolTip = "正在适应窗口大小…"; }
        try {
            activeTab.Oc.Disconnect(); // 断开是异步的:OnDisconnected 事件里改分辨率并重连
        } catch (Exception ex) {
            Diag.Log("disc EXC " + ex.Message); activeTab.Reconnecting = false;
        }
    }
    Popup fsPopup;
    readonly DispatcherTimer poll = new DispatcherTimer();
    readonly DispatcherTimer resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
    string DataFile { get { return IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Srdp", "connections.json"); } }
    // 旧版数据目录迁移(OrcaRDP → Srdp),保证升级不丢连接
    void MigrateData() {
        try {
            string oldJson = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OrcaRDP", "connections.json");
            if (!File.Exists(DataFile) && File.Exists(oldJson)) {
                Directory.CreateDirectory(IOPath.GetDirectoryName(DataFile));
                File.Copy(oldJson, DataFile);
                Diag.Log("migrated data from OrcaRDP to Srdp");
            }
        } catch (Exception ex) { Diag.Log("migrate EXC " + ex.Message); }
    }

    public MainWin() {
        Title = "Srdp"; Width = 1280; Height = 800; MinWidth = 900; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = T.B(T.BG0);
        var chrome = new Shell.WindowChrome();
        chrome.CaptionHeight = 36; chrome.ResizeBorderThickness = new Thickness(6);
        chrome.GlassFrameThickness = new Thickness(0); chrome.CornerRadius = new CornerRadius(0);
        chrome.UseAeroCaptionButtons = false;
        Shell.WindowChrome.SetWindowChrome(this, chrome);
        Build();
        LoadConns();
        poll.Interval = TimeSpan.FromSeconds(1);
        poll.Tick += (s,e)=> PollStatus();
        poll.Start();
        resizeTimer.Tick += (s,e)=> {
            resizeTimer.Stop();
            if (resizeTab != null) RequestResizeReconnect(resizeTab);
            resizeTab = null;
        };
        ssTimer.Tick += (s,e)=> { ssTimer.Stop(); ResizeToHost(); };
        SizeChanged += (s,e)=> { ssUserAction = true; ssTimer.Stop(); ssTimer.Start(); };
        StateChanged += (s,e)=> { if (maxBtn != null) maxBtn.Content = WindowState==WindowState.Maximized ? "\uE923" : "\uE922"; };
        InputBindings.Add(new KeyBinding(new RelayCommand(()=>Edit(null)), Key.N, ModifierKeys.Control));
    }
    class RelayCommand : ICommand {
        readonly Action act;
        public RelayCommand(Action a) { act = a; }
        public event EventHandler CanExecuteChanged { add {} remove {} }
        public bool CanExecute(object p) { return true; }
        public void Execute(object p) { act(); }
    }

    static Grid Rows(params GridLength[] lens) {
        var g = new Grid();
        foreach (var l in lens) g.RowDefinitions.Add(new RowDefinition { Height = l });
        return g;
    }

    void Build() {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto}); // header 行:Collapsed 时自动为 0(全屏无黑边)
        root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        var header = new Border { Height = 36, Background = T.B(T.BG1), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(0,0,0,1) };
        var hGrid = new Grid();
        hGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        hGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        hGrid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var logo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12,0,0,0), VerticalAlignment = VerticalAlignment.Center };
        logo.Children.Add(new Ellipse { Width=10, Height=10, Fill=T.B(T.ACC), VerticalAlignment=VerticalAlignment.Center });
        logo.Children.Add(new TextBlock { Text="Srdp", Foreground=T.B(T.FG), FontFamily=T.UI, FontSize=13, FontWeight=FontWeights.SemiBold, Margin=new Thickness(8,0,0,0), VerticalAlignment=VerticalAlignment.Center });
        hGrid.Children.Add(logo);
        var winBtns = new StackPanel { Orientation = Orientation.Horizontal };
        Shell.WindowChrome.SetIsHitTestVisibleInChrome(winBtns, true);
        var hdrFs = new Button { Content = "\uE740", FontFamily = T.MDL, FontSize = 10, Width = 46, Height = 36, Background = Brushes.Transparent, Foreground = T.B(T.FG2), BorderThickness = new Thickness(0), Template = T.BtnTemplate(new CornerRadius(0)), ToolTip = "全屏切换" };
        hdrFs.Click += (s,e)=> { if (activeTab != null) ToggleFullscreen(activeTab); };
        hdrFs.MouseEnter += (s,ev)=> hdrFs.Background = T.B(T.BG3);
        hdrFs.MouseLeave += (s,ev)=> hdrFs.Background = Brushes.Transparent;
        var minB = CapBtn("\uE921"); minB.Click += (s,e)=> WindowState = WindowState.Minimized;
        var maxB = CapBtn("\uE922"); maxBtn = maxB;
        maxB.Click += (s,e)=> { WindowState = WindowState==WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; maxB.Content = WindowState==WindowState.Maximized ? "\uE923" : "\uE922"; };
        var closeB = CapBtn("\uE8BB"); closeB.Click += (s,e)=> Close();
        winBtns.Children.Add(hdrFs); winBtns.Children.Add(minB); winBtns.Children.Add(maxB); winBtns.Children.Add(closeB);
        Grid.SetColumn(winBtns,2); hGrid.Children.Add(winBtns);
        header.Child = hGrid;
        Grid.SetRow(header,0); root.Children.Add(header);

        // 主体
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(270)});
        body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        Grid.SetRow(body,1); root.Children.Add(body);

        // 图标栏
        var rail = new Border { Background = T.B(T.BG0), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(0,0,1,0) };
        var railStack = new StackPanel { Margin = new Thickness(0,10,0,0) };
        var sideCol = body.ColumnDefinitions[1];
        var connBtn = RailBtn("\uE8FD", T.ACC);
        connBtn.Click += (s,e)=> ToggleSidebar(sideCol, connBtn);
        railStack.Children.Add(connBtn);
        railStack.Children.Add(RailBtn("\uE713", T.FG2, RailMenu));
        var railBottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0,0,0,12) };
        railBottom.Children.Add(RailBtn("\uE946", T.FG2, (s,e)=>AboutDialog()));
        var railGrid = new Grid();
        railGrid.Children.Add(railStack); railGrid.Children.Add(railBottom);
        rail.Child = railGrid; body.Children.Add(rail);

        // 侧栏
        var side = new Border { Background = T.B(T.BG1), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(0,0,1,0), ClipToBounds = true };
        var sideGrid = Rows(new GridLength(44), new GridLength(52), new GridLength(1,GridUnitType.Star));
        var sideHead = new Grid { Margin = new Thickness(14,0,10,0) };
        sideHead.Children.Add(new TextBlock { Text="连接管理器", Foreground=T.B(T.FG), FontSize=13, FontWeight=FontWeights.SemiBold, FontFamily=T.UI, VerticalAlignment=VerticalAlignment.Center });
        var addBtn = IconBtn("\uE710", T.ACC, 14); addBtn.HorizontalAlignment = HorizontalAlignment.Right;
        addBtn.Click += (s,e)=> Edit(null);
        sideHead.Children.Add(addBtn);
        Grid.SetRow(sideHead,0); sideGrid.Children.Add(sideHead);
        var searchGrid = new Grid { Margin = new Thickness(12,6,12,6) };
        searchBox = new TextBox { Background=T.B(T.BG3), Foreground=T.B(T.FG), CaretBrush=T.B(T.FG), BorderBrush=T.B(T.LINE), BorderThickness=new Thickness(1), FontFamily=T.UI, FontSize=12, Padding=new Thickness(8,6,8,6), VerticalContentAlignment=VerticalAlignment.Center };
        searchBox.TextChanged += (s,e)=> FilterList();
        watermark = new TextBlock { Text="搜索", Foreground=T.B(T.FG2), FontSize=12, FontFamily=T.UI, IsHitTestVisible=false, Margin=new Thickness(12,0,0,0), VerticalAlignment=VerticalAlignment.Center };
        searchGrid.Children.Add(searchBox); searchGrid.Children.Add(watermark);
        Grid.SetRow(searchGrid,1); sideGrid.Children.Add(searchGrid);
        listPanel = new StackPanel { Margin = new Thickness(8,2,8,8) };
        var scroll = new ScrollViewer { Content = listPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll,2); sideGrid.Children.Add(scroll);
        side.Child = sideGrid; Grid.SetColumn(side,1); body.Children.Add(side);

        // 主区
        var mainGrid = Rows(GridLength.Auto, new GridLength(1,GridUnitType.Star));
        var stripBorder = new Border { Height = 38, Background = T.B(T.BG1), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(0,0,0,1) };
        tabStrip = new StackPanel { Orientation = Orientation.Horizontal };
        stripBorder.Child = new ScrollViewer { Content = tabStrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(stripBorder,0); mainGrid.Children.Add(stripBorder);
        tabContent = new Grid { Background = T.B(T.BG0) };
        // 底部状态栏:连接/断开/错误信息常驻可见(不藏在 tooltip 里)
        contentInner = new Grid { Background = T.B(T.BG0) };
        tabContent.Children.Add(contentInner);
        var statusBar = new Border { Background = T.B(T.BG1), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(0,1,0,0), Padding = new Thickness(10,4,10,4) };
        StatusBanner = statusBar;
        statusText = new TextBlock { Text = "就绪", Foreground = T.B(T.FG2), FontSize = 11, FontFamily = T.UI, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        statusBar.Child = statusText;
        tabContent.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        tabContent.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetRow(contentInner,0); Grid.SetRow(statusBar,1);
        tabContent.Children.Add(statusBar);
        emptyHint = new TextBlock { Text = "双击左侧连接，开始远程桌面", Foreground = T.B(T.FG2), FontSize = 13, FontFamily = T.UI, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        contentInner.Children.Add(emptyHint);
        Grid.SetRow(tabContent,1); mainGrid.Children.Add(tabContent);
        Grid.SetColumn(mainGrid,2); body.Children.Add(mainGrid);

        HeaderBar = header; RailBar = rail; SideBar = side; StripBorder = stripBorder;
        RailCol = body.ColumnDefinitions[0]; SideCol = body.ColumnDefinitions[1];
        Content = root;
    }

    Button CapBtn(string glyph) {
        var b = new Button { Content = glyph, FontFamily = T.MDL, FontSize = 10, Width = 46, Height = 36, Background = Brushes.Transparent, Foreground = T.B(T.FG2), BorderThickness = new Thickness(0), Template = T.BtnTemplate(new CornerRadius(0)) };
        b.MouseEnter += (s,e)=> b.Background = T.B(T.BG3);
        b.MouseLeave += (s,e)=> b.Background = Brushes.Transparent;
        return b;
    }
    Button IconBtn(string glyph, string color, double size) {
        var b = new Button { Content = glyph, FontFamily = T.MDL, FontSize = size, Width = 30, Height = 30, Background = Brushes.Transparent, Foreground = T.B(color), BorderThickness = new Thickness(0), Template = T.BtnTemplate(new CornerRadius(6)), Cursor = Cursors.Hand };
        b.MouseEnter += (s,e)=> b.Background = T.B(T.BG3);
        b.MouseLeave += (s,e)=> b.Background = Brushes.Transparent;
        return b;
    }
    Button RailBtn(string glyph, string color, RoutedEventHandler onClick = null) {
        var b = new Button { Content = glyph, FontFamily = T.MDL, FontSize = 16, Width = 36, Height = 36, Margin = new Thickness(6,4,6,4), Background = Brushes.Transparent, Foreground = T.B(color), BorderThickness = new Thickness(0), Template = T.BtnTemplate(new CornerRadius(6)), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center };
        b.MouseEnter += (s,e)=> b.Background = T.B(T.BG3);
        b.MouseLeave += (s,e)=> b.Background = Brushes.Transparent;
        if (onClick != null) b.Click += onClick;
        return b;
    }
    bool sidebarOpen = true;
    void ToggleSidebar(ColumnDefinition col, Button btn) {
        sidebarOpen = !sidebarOpen;
        double target = sidebarOpen ? 270 : 0;
        var anim = new GridLengthAnimation {
            From = new GridLength(col.Width.Value, GridUnitType.Pixel),
            To = new GridLength(target, GridUnitType.Pixel),
            Duration = new Duration(TimeSpan.FromMilliseconds(180))
        };
        col.BeginAnimation(ColumnDefinition.WidthProperty, anim);
        btn.Foreground = T.B(sidebarOpen ? T.ACC : T.FG2);
        ssUserAction = true; ssTimer.Stop(); ssTimer.Start(); // 侧栏宽度变化=用户主动,绕过登录宽限期
    }
    void RailMenu(object sender, RoutedEventArgs e) {
        var btn = (Button)sender;
        var popup = new Popup { PlacementTarget = btn, Placement = PlacementMode.Right, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        var card = new Border { Background = T.B(T.BG2), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6), Width = 190 };
        var sp = new StackPanel();
        card.Child = sp;
        var mi1 = MenuRow("\uE8B7", "打开数据目录");
        mi1.MouseLeftButtonUp += (s,ev)=> { popup.IsOpen = false; try { System.Diagnostics.Process.Start("explorer.exe", IOPath.GetDirectoryName(DataFile)); } catch {} };
        var mi2 = MenuRow("\uE946", "关于 Srdp");
        mi2.MouseLeftButtonUp += (s,ev)=> { popup.IsOpen = false; AboutDialog(); };
        sp.Children.Add(mi1); sp.Children.Add(mi2);
        popup.Child = card;
        popup.IsOpen = true;
    }
    void AboutDialog() {
        DarkMsg.Info(this, "关于 Srdp",
            "Srdp v1.1.0 - 极简现代远程桌面管理器\n\n" +
            "引擎: Microsoft MsTscAx(与 mstsc 同源)\n" +
            "特性: 会话分辨率跟随窗口 / 真全屏 / 连接分组\n" +
            "      凭据 DPAPI 加密存储 / 32 位色深\n" +
            "数据: %APPDATA%\\Srdp\\connections.json\n" +
            "许可: MIT License\n" +
            "版权: Copyright (C) 2026 Srdp");
    }
    // 真全屏:无边框窗口覆盖整个屏幕(含任务栏区域),无黑边
    double fsTop, fsLeft, fsW, fsH; WindowState fsState; Shell.WindowChrome fsChrome; double fsCaption;
    void ToggleFullscreen(Tab tab) {
        if (fullscreen) { ExitFullscreen(); return; }
        fullscreen = true; fullscreenTab = tab;
        fsState = WindowState; fsTop = Top; fsLeft = Left; fsW = Width; fsH = Height;
        fsChrome = Shell.WindowChrome.GetWindowChrome(this);
        // 保留 chrome 实例但临时归零 caption/resize(移除实例会出现系统标题栏白条)
        fsCaption = fsChrome.CaptionHeight;
        fsChrome.CaptionHeight = 0;
        fsChrome.ResizeBorderThickness = new Thickness(0);
        HeaderBar.Visibility = Visibility.Collapsed;
        RailBar.Visibility = Visibility.Collapsed;
        SideBar.Visibility = Visibility.Collapsed;
        StripBorder.Visibility = Visibility.Collapsed;
        RailCol.Width = new GridLength(0);
        SideCol.Width = new GridLength(0);
        StatusBanner.Visibility = System.Windows.Visibility.Collapsed;
        var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        WindowState = WindowState.Normal;
        Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;
        Topmost = true; // 覆盖任务栏
        ShowFsHandle(tab);
        ssUserAction = true; ssTimer.Stop(); ssTimer.Start(); // 会话分辨率更新为全屏尺寸
    }
    void ExitFullscreen() {
        fullscreen = false;
        Topmost = false;
        Left = fsLeft; Top = fsTop; Width = fsW; Height = fsH;
        WindowState = fsState;
        if (fsChrome != null) { fsChrome.CaptionHeight = fsCaption; fsChrome.ResizeBorderThickness = new Thickness(6); }
        HeaderBar.Visibility = Visibility.Visible;
        RailBar.Visibility = Visibility.Visible;
        SideBar.Visibility = Visibility.Visible;
        StripBorder.Visibility = Visibility.Visible;
        RailCol.Width = new GridLength(48);
        SideCol.Width = new GridLength(270);
        StatusBanner.Visibility = System.Windows.Visibility.Visible;
        CloseFsPopup();
        fullscreenTab = null;
        ssUserAction = true; ssTimer.Stop(); ssTimer.Start();
    }
    // 全屏顶部连接条(mstsc 风格):顶部中央常显小把手,鼠标悬浮展开操作条,离开自动缩回
    void ShowFsHandle(Tab tab) {
        CloseFsPopup();
        fsPopup = new Popup { Placement = PlacementMode.RelativePoint, PlacementTarget = this, StaysOpen = true, AllowsTransparency = true };
        var card = new Border { Background = new SolidColorBrush(Color.FromArgb(215,16,16,20)), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(0,0,8,8), Padding = new Thickness(8,3,8,5), Cursor = Cursors.Hand };
        var grid = new Grid { MinWidth = 190 };
        var handleRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        handleRow.Children.Add(new TextBlock { Text = "\uE712 \uE740 全屏", FontFamily = T.MDL, FontSize = 10, Foreground = T.B(T.FG2) });
        var barRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
        barRow.Children.Add(new TextBlock { Text = "全屏模式 - " + tab.C.Name, Foreground = T.B(T.FG2), FontSize = 11, FontFamily = T.UI, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0) });
        var exit = new Button { Content = "退出全屏", Background = T.B(T.ACC), Foreground = T.B("#FFFFFF"), BorderThickness = new Thickness(0), FontFamily = T.UI, FontSize = 11, Cursor = Cursors.Hand, Template = T.BtnTemplate(new CornerRadius(5)), Padding = new Thickness(10,4,10,4) };
        exit.Click += (s,ev)=> ExitFullscreen();
        barRow.Children.Add(exit);
        grid.Children.Add(handleRow); grid.Children.Add(barRow);
        card.Child = grid;
        card.MouseEnter += (s,ev)=> { handleRow.Visibility = Visibility.Collapsed; barRow.Visibility = Visibility.Visible; };
        card.MouseLeave += (s,ev)=> { barRow.Visibility = Visibility.Collapsed; handleRow.Visibility = Visibility.Visible; };
        fsPopup.Child = card;
        fsPopup.Opened += (s,ev)=> { fsPopup.HorizontalOffset = (ActualWidth - card.MinWidth) / 2; fsPopup.VerticalOffset = 0; };
        fsPopup.IsOpen = true;
    }
    void CloseFsPopup() { if (fsPopup != null) { fsPopup.IsOpen = false; fsPopup = null; } }
    Border MenuRow(string glyph, string text) {
        var row = new Border { CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Padding = new Thickness(10,8,10,8), Cursor = Cursors.Hand };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var icon = new TextBlock { Text=glyph, FontFamily=T.MDL, FontSize=14, Foreground=T.B(T.FG2), VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(0,0,10,0) };
        Grid.SetColumn(icon,0); g.Children.Add(icon);
        var t = new TextBlock { Text=text, Foreground=T.B(T.FG), FontSize=12, FontFamily=T.UI, VerticalAlignment=VerticalAlignment.Center };
        Grid.SetColumn(t,1); g.Children.Add(t);
        row.Child = g;
        row.MouseEnter += (s,ev)=> row.Background = T.B(T.BG3);
        row.MouseLeave += (s,ev)=> row.Background = Brushes.Transparent;
        return row;
    }

    void LoadConns() {
        MigrateData();
        try {
            if (File.Exists(DataFile)) { conns.AddRange(J.Parse(File.ReadAllText(DataFile, Encoding.UTF8))); }
            else {
                Directory.CreateDirectory(IOPath.GetDirectoryName(DataFile));
                conns.Add(new Conn { Name = "192.168.1.6", Host = "192.168.1.6" });
                Save();
            }
        } catch { conns.Add(new Conn { Name = "192.168.1.6", Host = "192.168.1.6" }); }
        RebuildList();
    }
    void Save() {
        try { Directory.CreateDirectory(IOPath.GetDirectoryName(DataFile)); File.WriteAllText(DataFile, J.Write(conns), new UTF8Encoding(false)); } catch {}
    }
    void RebuildList() {
        listPanel.Children.Clear(); rows.Clear(); selected = null;
        groupHeaders.Clear();
        // 未分组连接平铺在顶部
        foreach (var c in conns.Where(x => string.IsNullOrEmpty(x.Group))) listPanel.Children.Add(MakeRow(c).Bd);
        // 自定义分组(保序去重):组头可折叠,右键可移动连接
        foreach (var g in conns.Select(x => x.Group).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList()) {
            var members = conns.Where(x => x.Group == g).ToList();
            var head = MakeGroupHeader(g, members.Count);
            listPanel.Children.Add(head);
            groupHeaders[g] = head;
            if (collapsedGroups.Contains(g)) continue;
            foreach (var c in members) listPanel.Children.Add(MakeRow(c).Bd);
        }
        FilterList();
    }
    Border MakeGroupHeader(string g, int count) {
        var b = new Border { Background = T.B(T.BG1), CornerRadius = new CornerRadius(6), Padding = new Thickness(10,6,10,6), Margin = new Thickness(0,6,0,2), Cursor = Cursors.Hand };
        var gg = new Grid();
        gg.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        gg.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        gg.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var arrow = new TextBlock { Text = collapsedGroups.Contains(g) ? "\uE76C" : "\uE70D", FontFamily = T.MDL, FontSize = 10, Foreground = T.B(T.FG2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) };
        gg.Children.Add(arrow);
        var t = new TextBlock { Text = g, Foreground = T.B(T.FG), FontSize = 12, FontWeight = FontWeights.SemiBold, FontFamily = T.UI, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(t,1); gg.Children.Add(t);
        var cnt = new TextBlock { Text = count.ToString(), Foreground = T.B(T.FG2), FontSize = 11, FontFamily = T.UI, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(cnt,2); gg.Children.Add(cnt);
        b.Child = gg;
        b.Tag = arrow;
        b.MouseLeftButtonDown += (s,e)=> {
            if (collapsedGroups.Contains(g)) collapsedGroups.Remove(g); else collapsedGroups.Add(g);
            arrow.Text = collapsedGroups.Contains(g) ? "\uE76C" : "\uE70D";
            RebuildList();
        };
        var gmenu = new ContextMenu();
        var del = new MenuItem { Header = "删除分组(连接移至未分组)" };
        del.Click += (s,e)=> { foreach (var c in conns.Where(x => x.Group == g)) c.Group = ""; Save(); RebuildList(); };
        gmenu.Items.Add(del);
        b.ContextMenu = gmenu;
        return b;
    }
    Row MakeRow(Conn c) {
        var r = new Row { C = c };
        // 行用真 Button:单击即连接(OrcaTerm 同款交互),同时获得键盘可达与 UIA Invoke
        r.Bd = new Button { Background = Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(10,7,10,7), Template = T.BtnTemplate(new CornerRadius(6)) };
        System.Windows.Automation.AutomationProperties.SetName(r.Bd, "conn-" + c.Name);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        g.Children.Add(new TextBlock { Text="\uE7F4", FontFamily=T.MDL, FontSize=15, Foreground=T.B(T.FG2), VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(0,0,10,0) });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = c.Name, Foreground = T.B(T.FG), FontSize = 13, FontFamily = T.UI, TextTrimming = TextTrimming.CharacterEllipsis });
        stack.Children.Add(new TextBlock { Text = c.Host + ":" + c.Port, Foreground = T.B(T.FG2), FontSize = 11, FontFamily = T.UI, Margin = new Thickness(0,1,0,0) });
        Grid.SetColumn(stack,1); g.Children.Add(stack);
        r.Dot = new Ellipse { Width = 8, Height = 8, Fill = T.B(T.DOT), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(r.Dot,2); g.Children.Add(r.Dot);
        r.Bd.Content = g;
        r.Bd.MouseEnter += (s,e)=> { if (!r.Sel) r.Bd.Background = T.B(T.BG3); };
        r.Bd.MouseLeave += (s,e)=> { if (!r.Sel) r.Bd.Background = Brushes.Transparent; };
        r.Bd.Click += (s,e)=> { SelectRow(r); Connect(c); };
        var menu = new ContextMenu();
        var m1 = new MenuItem { Header = "连接" }; m1.Click += (s,e)=> Connect(c);
        var m2 = new MenuItem { Header = "编辑" }; m2.Click += (s,e)=> Edit(c);
        var m3 = new MenuItem { Header = "删除" };
        m3.Click += (s,e)=> { if (DarkMsg.Confirm(this, "删除连接", "确定删除 “"+c.Name+"”？此操作不可撤销。")) { conns.Remove(c); Save(); RebuildList(); } };
        menu.Items.Add(m1); menu.Items.Add(m2); menu.Items.Add(m3);
        // 移动到分组(自定义分组列表 + 未分组)
        var mv = new MenuItem { Header = "移动到分组" };
        foreach (var grp in conns.Select(x => x.Group).Where(x => !string.IsNullOrEmpty(x)).Distinct()) {
            var gi = new MenuItem { Header = grp, IsChecked = c.Group == grp };
            var gg = grp;
            gi.Click += (s,e)=> { c.Group = gg; Save(); RebuildList(); };
            mv.Items.Add(gi);
        }
        mv.Items.Add(new Separator());
        var toUngrouped = new MenuItem { Header = "未分组", IsChecked = string.IsNullOrEmpty(c.Group) };
        toUngrouped.Click += (s,e)=> { c.Group = ""; Save(); RebuildList(); };
        mv.Items.Add(toUngrouped);
        menu.Items.Add(mv);
        r.Bd.ContextMenu = menu;
        rows.Add(r);
        return r;
    }
    void SelectRow(Row r) {
        if (selected != null) { selected.Sel = false; selected.Bd.Background = Brushes.Transparent; }
        selected = r; r.Sel = true; r.Bd.Background = T.B(T.BG3);
    }
    void SetStatus(string s, string color) {
        if (statusText != null) { statusText.Text = s; statusText.Foreground = T.B(color); }
    }
    void FilterList() {
        string q = (searchBox.Text ?? "").Trim().ToLowerInvariant();
        watermark.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var r in rows) {
            bool hit = q=="" || (r.C.Name??"").ToLowerInvariant().Contains(q) || (r.C.Host??"").ToLowerInvariant().Contains(q);
            r.Bd.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    void Connect(Conn c) {
        foreach (var t in tabs) if (t.C.Id == c.Id) { SelectTab(t); return; }
        var tab = new Tab { C = c };
        tab.R = rows.Find(r => r.C.Id == c.Id);
        tab.Ax = new RdpAx();
        // 直接把 AxHost 挂在 WindowsFormsHost 下时,窗口缩放后 ActiveX 不跟随拉伸;
        // 中间加一层 WinForms Panel(Dock=Fill),由 WinForms 布局引擎驱动 AxHost 尺寸
        var rdpPanel = new WF.Panel { Dock = WF.DockStyle.Fill, BackColor = System.Drawing.Color.FromArgb(11,11,14) };
        tab.Ax.Dock = WF.DockStyle.Fill;
        rdpPanel.Controls.Add(tab.Ax);
        tab.Host = new WinFormsHost();
        tab.Host.Child = rdpPanel;
        tab.Bd = new Border { Background = T.B(T.BG1), Cursor = Cursors.Hand, Padding = new Thickness(12,0,6,0), BorderBrush = T.B(T.ACC), BorderThickness = new Thickness(0) };
        var tg = new Grid();
        tg.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        tg.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        tg.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        tg.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        tab.Dot = new Ellipse { Width=7, Height=7, Fill=T.B(T.DOT), VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(0,0,8,0) };
        tg.Children.Add(tab.Dot);
        tab.Label = new TextBlock { Text = c.Name, Foreground = T.B(T.FG), FontSize = 12, FontFamily = T.UI, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(tab.Label,1); tg.Children.Add(tab.Label);
        var fsBtn = new TextBlock { Text = "\uE740", FontFamily = T.MDL, FontSize = 10, Foreground = T.B(T.FG2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,2,0), Cursor = Cursors.Hand, ToolTip = "全屏" };
        fsBtn.MouseLeftButtonUp += (s,e)=> { ToggleFullscreen(tab); e.Handled = true; };
        Grid.SetColumn(fsBtn,2); tg.Children.Add(fsBtn);
        var xBtn = new TextBlock { Text = "\uE8BB", FontFamily = T.MDL, FontSize = 9, Foreground = T.B(T.FG2), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6,0,2,0), Cursor = Cursors.Hand };
        xBtn.MouseLeftButtonUp += (s,e)=> { CloseTab(tab); e.Handled = true; };
        Grid.SetColumn(xBtn,3); tg.Children.Add(xBtn);
        tab.Bd.Child = tg;
        tab.Bd.MouseLeftButtonUp += (s,e)=> SelectTab(tab);
        tabStrip.Children.Add(tab.Bd);
        tab.Host.Visibility = Visibility.Collapsed;
        contentInner.Children.Add(tab.Host);
        tabs.Add(tab);
        SelectTab(tab);
        SetStatus("正在连接 " + c.Host + " …", T.FG2);
        Dispatcher.BeginInvoke(new Action(()=>{
            if (tab.Dead) return;
            try {
                Diag.Log("Connect tab host=" + tab.C.Host + ":" + tab.C.Port + " hostSize=" + (int)tab.Host.ActualWidth + "x" + (int)tab.Host.ActualHeight);
                // 1. DNS 预检:解析失败立即给出明确原因,不让用户干等
                SetStatus("正在解析 " + tab.C.Host + " …", T.FG2);
                System.Net.IPAddress[] addrs = null;
                try { addrs = System.Net.Dns.GetHostAddresses(tab.C.Host); } catch {}
                if (addrs == null || addrs.Length == 0) {
                    Diag.Log("DNS fail " + tab.C.Host);
                    SetStatus("无法解析主机名 " + tab.C.Host, T.RED);
                    DarkMsg.Info(this, "连接失败", "无法解析主机名: " + tab.C.Host + "\n\n请检查地址填写是否正确、域名是否已过期。");
                    CloseTab(tab);
                    return;
                }
                Diag.Log("DNS ok: " + string.Join(", ", addrs.Select(a => a.ToString())));
                // 2. TCP 端口预检(后台线程 4 秒超时):不通立即给出原因与检查清单,不让用户对着黑屏干等
                SetStatus("正在检测 " + tab.C.Host + ":" + tab.C.Port + " 端口连通性…", T.FG2);
                System.Threading.Tasks.Task.Run(new Action(() => {
                    string err = null;
                    try {
                        // 逐地址尝试(IPv4/IPv6 都试),全部失败才判定端口不通
                        System.Net.Sockets.TcpClient okTcp = null;
                        foreach (var a in addrs) {
                            System.Net.Sockets.TcpClient tcp = null;
                            try {
                                tcp = new System.Net.Sockets.TcpClient(a.AddressFamily);
                                var ar = tcp.BeginConnect(a, tab.C.Port, null, null);
                                if (ar.AsyncWaitHandle.WaitOne(3000)) { tcp.EndConnect(ar); okTcp = tcp; break; }
                            } catch {}
                            try { tcp.Close(); } catch {}
                        }
                        if (okTcp == null) err = "所有地址(" + addrs.Length + " 个)均无法在 3 秒内建立连接";
                        else okTcp.Close();
                    } catch (Exception ex) { err = ex.Message; }
                    Dispatcher.BeginInvoke(new Action(() => {
                        if (tab.Dead) return;
                        if (err != null) {
                            Diag.Log("TCP probe fail " + tab.C.Host + ":" + tab.C.Port + " " + err);
                            SetStatus("无法连接 " + tab.C.Host + ":" + tab.C.Port, T.RED);
                            DarkMsg.Info(this, "连接失败",
                                "无法连接到 " + tab.C.Host + ":" + tab.C.Port + "\n\n" + err + "\n\n请检查:\n" +
                                "- 远程机的远程桌面服务是否已开启\n" +
                                "- 防火墙 / 端口映射是否放行 " + tab.C.Port + " 端口\n" +
                                "- 域名解析是否指向正确的机器(当前解析到 " + (addrs.Length > 0 ? addrs[0].ToString() : "?") + ")");
                            CloseTab(tab);
                            return;
                        }
                        Diag.Log("TCP probe ok " + tab.C.Host + ":" + tab.C.Port);
                        SetStatus("正在建立远程会话…", T.FG2);
                        try {
                            var oc = tab.Ax.Oc;
                            // COM 事件:IID={336d5562-efa8-482e-8cb3-c5c0fc7a7db6}(_IMsTscAxEvents,提取自 mstscax.dll 类型库)
                            var evGuid = new Guid("336d5562-efa8-482e-8cb3-c5c0fc7a7db6");
                            ComEventsHelper.Combine(oc, evGuid, 1, new Diag.EmptyEv(() => {
                                if (tab.Dot != null && !tab.Reconnecting) tab.Dot.Fill = T.B("#FFD60A"); // 连接中=黄
                                SetStatus("正在连接 " + tab.C.Host + " …", T.FG2);
                            }));
                            ComEventsHelper.Combine(oc, evGuid, 2, new Diag.EmptyEv(() => {
                                tab.Reconnecting = false;
                                ssUserAction = false;
                                Diag.Log("EV OnConnected");
                                if (tab.Dot != null) tab.Dot.Fill = T.B(T.GREEN);
                                SetStatus("已连接 " + tab.C.Host, T.GREEN);
                            }));
                            ComEventsHelper.Combine(oc, evGuid, 3, new Diag.EmptyEv(() => {
                                tab.LoginDone = true; tab.LoginAt = DateTime.Now;
                                Diag.Log("EV OnLoginComplete");
                                SetStatus("登录完成", T.GREEN);
                            }));
                            ComEventsHelper.Combine(oc, evGuid, 4, new Diag.ReasonEv(code => {
                                if (tab.Dead) return;
                                Diag.Log("EV OnDisconnected code=0x" + code.ToString("X") + " reconnecting=" + tab.Reconnecting);
                                if (tab.Reconnecting) { // 会话已断开:现在改分辨率是安全的,重连同一会话
                                    int w = (int)tab.Host.ActualWidth, h = (int)tab.Host.ActualHeight;
                                    if (w < 200 || h < 150) { w = 1920; h = 1080; }
                                    SetStatus("正在适应窗口大小…", T.FG2);
                                    try {
                                        oc.DesktopWidth = w;
                                        oc.DesktopHeight = h;
                                        oc.Connect();
                                        Diag.Log("resize reconnect ok " + w + "x" + h);
                                    } catch (Exception ex) {
                                        Diag.Log("resize reconnect EXC " + ex.Message);
                                        tab.Reconnecting = false;
                                        ssUserAction = false;
                                        try { oc.AdvancedSettings.SmartSizing = false; oc.AdvancedSettings.SmartSizing = true; } catch {}
                                        if (tab.Dot != null) tab.Dot.Fill = T.B(T.RED);
                                        SetStatus("分辨率对齐失败,已回退缩放显示", T.FG2);
                                    }
                                    return;
                                }
                                string txt = "";
                                try { txt = oc.GetErrorDescription(code, 2052); } catch {}
                                if (tab.Dot != null) tab.Dot.Fill = T.B(T.RED);
                                SetStatus("已断开: " + (string.IsNullOrEmpty(txt) ? ("0x" + code.ToString("X")) : txt), T.RED);
                                if (tab.Label != null) {
                                    tab.Label.Text += " (已断开)";
                                    tab.Label.ToolTip = string.IsNullOrEmpty(txt) ? ("断开 0x" + code.ToString("X")) : txt;
                                }
                            }));
                            tab.ReUser = tab.C.User;
                            tab.RePass = Secret.Dec(tab.C.Pass);
                            SetProps(oc, tab);
                            oc.Connect();
                            tab.Oc = oc;
                            tab.LastW = (int)tab.Host.ActualWidth; tab.LastH = (int)tab.Host.ActualHeight;
                            tab.Host.SizeChanged += (s,e)=> { ssUserAction = true; ssTimer.Stop(); ssTimer.Start(); };
                        } catch (Exception ex) {
                            Diag.Log("conn EXC: " + ex.GetType().Name + " " + ex.Message);
                            DarkMsg.Info(this, "连接失败", ex.Message);
                            CloseTab(tab);
                        }
                    }), DispatcherPriority.Normal);
                })); // end Task.Run (TCP probe)
            } catch (Exception ex) {
                Diag.Log("probe EXC: " + ex.GetType().Name + " " + ex.Message);
                DarkMsg.Info(this, "连接失败", ex.Message);
                CloseTab(tab);
            }
        }), DispatcherPriority.ApplicationIdle);
    }
    // 连接参数统一入口(首次连接与分辨率重连共用)
    void SetProps(dynamic oc, Tab tab) {
        oc.Server = tab.C.Host;
        if (tab.C.Port != 3389) oc.AdvancedSettings.RDPPort = tab.C.Port;
        // NLA(CredSSP): 旧版 AdvancedSettings 接口默认关闭,必须在 AdvancedSettings6 上显式开启,
        // 否则连接 NLA 强制的机器立即 0xB09("不支持网络级别身份验证")
        oc.AdvancedSettings6.EnableCredSspSupport = true;
        // 会话画面随窗口缩放
        oc.AdvancedSettings.SmartSizing = true;
        // 关键:必须显式锁定会话分辨率。不设置时控件会把桌面尺寸自动同步为控件大小,
        // 导致 SmartSizing 缩放永远 1:1(窗口放大后远程画面"还是小的")
        // 布局未完成时 ActualWidth 可能是 0,退回主屏分辨率兜底
        int dw = (int)tab.Host.ActualWidth, dh = (int)tab.Host.ActualHeight;
        if (tab.LastW > 0) { dw = tab.LastW; dh = tab.LastH; }
        if (dw < 200 || dh < 150) {
            var scr = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            dw = scr.Width; dh = scr.Height;
        }
        oc.DesktopWidth = dw;
        oc.DesktopHeight = dh;
        // 键盘响应速度:客户端钩子直通模式(默认的 server 钩子模式每次按键都要往返远程确认,首次登录后有几十秒延迟)
        try { oc.AdvancedSettings.KeyboardHookMode = 1; } catch {}
        if (!string.IsNullOrEmpty(tab.ReUser)) oc.UserName = tab.ReUser;
        if (tab.RePass.Length > 0) oc.AdvancedSettings.ClearTextPassword = tab.RePass;
        try { oc.ColorDepth = 32; } catch {}
    }
    // 窗口缩放停止后,重设会话分辨率并重连同一会话(原生 1:1 清晰度,代替缩放模糊)
    void RequestResizeReconnect(Tab tab) {
        if (tab.Dead || tab.Oc == null) return;
        int w = (int)tab.Host.ActualWidth, h = (int)tab.Host.ActualHeight;
        if (w < 200 || h < 150) return;
        if (Math.Abs(w - tab.LastW) < 30 && Math.Abs(h - tab.LastH) < 30) return;
        tab.LastW = w; tab.LastH = h;
        if (!tab.LoginDone) { Diag.Log("resize skip: login not complete (login screen showing)"); return; }
        Diag.Log("RequestResizeReconnect " + w + "x" + h);
        try {
            // RDP10 动态分辨率:会话中直接更新,无需断开重连
            tab.Oc.UpdateSessionDisplaySettings((uint)w, (uint)h, (uint)w, (uint)h, 100u, 0u, 32u);
            Diag.Log("UpdateSessionDisplaySettings ok " + w + "x" + h);
            if (tab.Dot != null) tab.Dot.Fill = T.B(T.GREEN);
            return;
        } catch (Exception ex) { Diag.Log("USDS EXC " + ex.Message); }
        tab.Reconnecting = true;
        if (tab.Dot != null) tab.Dot.Fill = T.B(T.ACC);
        try {
            SetProps(tab.Oc, tab);
            tab.Oc.Disconnect(); // OnDisconnected 处理器里自动重连
            Diag.Log("resize reconnect: disconnected, reconnecting");
        } catch (Exception ex) { Diag.Log("resize reconnect EXC " + ex.Message); tab.Reconnecting = false; }
    }
    void SelectTab(Tab sel) {
        emptyHint.Visibility = Visibility.Collapsed;
        foreach (var t in tabs) {
            bool on = ReferenceEquals(t, sel);
            t.Host.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            t.Bd.Background = on ? T.B(T.BG0) : T.B(T.BG1);
            t.Bd.BorderThickness = new Thickness(0,0,0,on?2:0);
            t.Label.Foreground = on ? T.B(T.FG) : T.B(T.FG2);
            if (on) activeTab = t;
        }
        // 切换标签后,若会话分辨率与当前窗口差异较大,自动重连以恢复 1:1 清晰度
        if (sel != null && sel.Oc != null && sel.LoginDone) {
            int cw = (int)sel.Host.ActualWidth, ch = (int)sel.Host.ActualHeight;
            if (cw > 200 && ch > 150 && (Math.Abs(cw - sel.LastW) > 300 || Math.Abs(ch - sel.LastH) > 300)) {
                sel.LastW = cw; sel.LastH = ch;
                var st = sel;
                System.Threading.Tasks.Task.Delay(50).ContinueWith(_ =>
                    Dispatcher.BeginInvoke(new Action(() => { if (!st.Dead) ResizeToHost(); })));
            }
        }
    }
    void CloseTab(Tab t) {
        if (fullscreen && fullscreenTab == t) ExitFullscreen();
        if (!t.Dead) {
            t.Dead = true;
            try { if (t.Oc != null) { try { t.Oc.Disconnect(); } catch {} } } catch {}
        }
        tabStrip.Children.Remove(t.Bd);
        tabContent.Children.Remove(t.Host);
        tabs.Remove(t);
        if (t.R != null) t.R.Dot.Fill = T.B(T.DOT);
        if (tabs.Count == 0) emptyHint.Visibility = Visibility.Visible;
        else SelectTab(tabs[tabs.Count-1]);
    }
    void PollStatus() {
        foreach (var t in tabs) {
            if (t.Dead) continue;
            bool up = false;
            try { int st = (int)t.Ax.Oc.Connected; up = (st == 1); } catch { up = false; }
            var brush = up ? T.B(T.GREEN) : T.B(T.DOT);
            t.Dot.Fill = brush;
            if (t.R != null) t.R.Dot.Fill = brush;
        }
    }

    void Edit(Conn existing) {
        var dlg = new EditorWin(this, existing);
        dlg.Owner = this;
        if (dlg.ShowDialog() == true) { Save(); RebuildList(); }
    }
    public List<Conn> Conns { get { return conns; } }
}

class EditorWin : Window {
    readonly MainWin mw; readonly Conn orig;
    TextBox tbName, tbHost, tbPort, tbUser;
    PasswordBox tbPass; CheckBox cbRemember; ComboBox cbGroup;
    public EditorWin(MainWin w, Conn c) {
        mw = w; orig = c;
        Title = "OrcaRDP-Edit";
        Width = 420; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Build();
    }
    void Build() {
        var card = new Border { Background = T.B(T.BG2), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(20,16,20,16) };
        var sp = new StackPanel();
        var title = new TextBlock { Text = orig == null ? "新建连接" : "编辑连接", Foreground = T.B(T.FG), FontSize = 15, FontWeight = FontWeights.SemiBold, FontFamily = T.UI, Margin = new Thickness(0,0,0,14) };
        // 只从标题拖动:DragMove 会吞掉按钮的 Up 事件,绝不能挂在整窗上
        title.MouseLeftButtonDown += (s,e)=> { if (e.ButtonState==MouseButtonState.Pressed) { try { DragMove(); } catch {} } };
        sp.Children.Add(title);
        sp.Children.Add(Field("名称", out tbName, orig != null ? (orig.Name ?? "") : ""));
        sp.Children.Add(Field("主机", out tbHost, orig != null ? (orig.Host ?? "") : ""));
        var row2 = new Grid();
        row2.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        row2.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var g1 = Field("端口", out tbPort, orig != null ? orig.Port.ToString() : "3389");
        g1.Margin = new Thickness(0,0,6,10);
        var g2 = Field("用户名", out tbUser, orig != null ? (orig.User ?? "") : "");
        g2.Margin = new Thickness(6,0,0,10);
        row2.Children.Add(g1); Grid.SetColumn(g2,1); row2.Children.Add(g2);
        sp.Children.Add(row2);
        // 密码
        var gp = new Grid { Margin = new Thickness(0,0,0,6) };
        gp.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        gp.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        gp.Children.Add(new TextBlock { Text = "密码", Foreground = T.B(T.FG2), FontSize = 11, FontFamily = T.UI, Margin = new Thickness(0,0,0,4) });
        tbPass = new PasswordBox { Background = T.B(T.BG3), Foreground = T.B(T.FG), CaretBrush = T.B(T.FG), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(1), Padding = new Thickness(8,6,8,6), FontFamily = T.UI, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetRow(tbPass,1); gp.Children.Add(tbPass);
        sp.Children.Add(gp);
        cbRemember = new CheckBox { Content = "记住密码(加密保存在本机)", Foreground = T.B(T.FG2), FontSize = 12, FontFamily = T.UI, Margin = new Thickness(0,2,0,12), IsChecked = orig != null && orig.Pass.Length > 0 };
        sp.Children.Add(cbRemember);
        // 分组(可编辑下拉:选现有分组或输入新分组名,留空=未分组)
        var ggrp = new Grid { Margin = new Thickness(0,0,0,10) };
        ggrp.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        ggrp.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        ggrp.Children.Add(new TextBlock { Text = "分组", Foreground = T.B(T.FG2), FontSize = 11, FontFamily = T.UI, Margin = new Thickness(0,0,0,4) });
        cbGroup = new ComboBox {
            Background = T.B(T.BG3), Foreground = T.B(T.FG), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(1),
            Padding = new Thickness(8,5,8,5), FontFamily = T.UI, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center, IsEditable = true,
            Text = orig != null ? (orig.Group ?? "") : ""
        };
        foreach (var g in mw.Conns.Select(x => x.Group).Where(x => !string.IsNullOrEmpty(x)).Distinct()) cbGroup.Items.Add(g);
        Grid.SetRow(cbGroup,1); ggrp.Children.Add(cbGroup);
        sp.Children.Add(ggrp);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,2,0,0) };
        var cancel = MkBtn("取消", T.BG3, T.FG2);
        cancel.Click += (s,e)=> { DialogResult = false; Close(); };
        var ok = MkBtn("保存", T.ACC, "#FFFFFF");
        ok.Click += (s,e)=> SaveIt();
        btns.Children.Add(cancel); btns.Children.Add(ok);
        sp.Children.Add(btns);
        card.Child = sp; Content = card;
        KeyDown += (s,e)=> { if (e.Key==Key.Escape) { DialogResult=false; Close(); } else if (e.Key==Key.Enter) SaveIt(); };
        // 回填已存密码(编辑旧连接时)
        if (orig != null && orig.Pass.Length > 0) {
            tbPass.Password = Secret.Dec(orig.Pass);
        }
    }
    Grid Field(string label, out TextBox box, string val) {
        var g = new Grid { Margin = new Thickness(0,0,0,10) };
        g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        g.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        g.Children.Add(new TextBlock { Text = label, Foreground = T.B(T.FG2), FontSize = 11, FontFamily = T.UI, Margin = new Thickness(0,0,0,4) });
        box = new TextBox { Text = val, Background = T.B(T.BG3), Foreground = T.B(T.FG), CaretBrush = T.B(T.FG), BorderBrush = T.B(T.LINE), BorderThickness = new Thickness(1), Padding = new Thickness(8,6,8,6), FontFamily = T.UI, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetRow(box,1); g.Children.Add(box);
        return g;
    }
    Button MkBtn(string text, string bg, string fg) {
        var b = new Button { Content = text, MinWidth = 76, Height = 30, Margin = new Thickness(8,0,0,0), Background = T.B(bg), Foreground = T.B(fg), BorderThickness = new Thickness(0), FontFamily = T.UI, FontSize = 12, Cursor = Cursors.Hand, Template = T.BtnTemplate(new CornerRadius(6)) };
        b.MouseEnter += (s,e)=> b.Background = T.B(bg==T.ACC ? "#6B9DFF" : T.BG2);
        b.MouseLeave += (s,e)=> b.Background = T.B(bg);
        return b;
    }
    void SaveIt() {
        string name = tbName.Text.Trim(), host = tbHost.Text.Trim();
        if (name.Length==0 || host.Length==0) { DarkMsg.Info(this, "提示", "名称和主机不能为空"); return; }
        int port; if (!int.TryParse(tbPort.Text.Trim(), out port) || port<=0 || port>65535) port = 3389;
        string pass = "";
        if (cbRemember.IsChecked == true && tbPass.Password.Length > 0) pass = Secret.Enc(tbPass.Password);
        if (orig == null) {
            var c = new Conn { Name = name, Host = host, Port = port, User = tbUser.Text.Trim(), Pass = pass, Group = cbGroup.Text.Trim() };
            mw.Conns.Add(c);
        } else {
            orig.Name = name; orig.Host = host; orig.Port = port; orig.User = tbUser.Text.Trim(); orig.Pass = pass; orig.Group = cbGroup.Text.Trim();
        }
        DialogResult = true; Close();
    }
}

static class Program {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread]
    static void Main() {
        try { SetProcessDPIAware(); } catch {}
        var app = new Application();
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        var win = new MainWin();
        app.MainWindow = win;
        win.Show();
        app.Run();
    }
}
