using System.Diagnostics;
using System.Drawing.Text;
using System.Management;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;

namespace CpuT;

// 화면 오른쪽 위에 항상 떠 있는 배경 투명 오버레이. 클릭은 뒤 창으로 통과하고, 종료는 트레이 아이콘 오른쪽 클릭.
// 온도가 높아지면 Windows "최대 프로세서 주파수"를 조금씩 낮춰 CPU 발열을 줄인다(쿨러 고장 대응).
public class MainForm : Form
{
    private const int ScreenMargin = 10;

    private const int EmergencyTemp = 95;    // 이 온도 이상이면 최대 프로세서 상태를 EmergencyLimit로 급감속 (100°C에서 노트북이 꺼짐)
    private const int LimitStartTemp = 85;   // 이 온도 이상이면 1초마다 최고 속도를 LimitStepMHz씩 낮춤
    private const int LimitReleaseTemp = 80; // 이 온도 이하면 1초마다 LimitStepMHz씩 다시 올림 (급감속도 해제) → 80~85°C에서 버틸 수 있는 최고 속도를 찾아감
    private const int LimitStepMHz = 200;
    private const int MinFreqMHz = 1200;
    private const int EmergencyLimit = 30;   // 급감속 시 최대 프로세서 상태(%) - 확실하게 속도를 떨어뜨림

    private const int BatteryIntervalSec = 3; // 배터리 정보는 조회 비용이 커서 몇 초에 한 번만 읽음
    private const int MaxLimit = 100;            // 평소 최대 프로세서 상태 (100 미만이면 부스트가 꺼짐)
    private const int DefaultMaxFreqMHz = 3000;  // 평소 최고 속도 → 4.9GHz 순간 부스트로 온도가 튀는 것 방지
    private const int BoostMaxFreqMHz = 3500;    // 트레이 메뉴 "부스트 켜기" 시 최고 속도 (앱 실행 중에만)

    private readonly Computer _computer = new() { IsCpuEnabled = true };
    private readonly Label _packageTemp = new() { AutoSize = true, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Color.White };
    private readonly Label _powerInfo = new() { AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.White };
    private readonly ManagementObjectSearcher _batterySearcher = new(@"root\wmi", "SELECT PowerOnline, Charging, ChargeRate, DischargeRate FROM BatteryStatus");
    private readonly Label _limitInfo = new() { AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.Orange, Visible = false };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly NotifyIcon _tray = new() { Text = "CPU 온도", Visible = true };
    private string? _trayText;
    private string _batteryText = "";
    private int _tick;
    private bool _boost;
    private bool _emergency;
    private int _capMHz = DefaultMaxFreqMHz; // 지금 걸려 있는 최고 속도 (온도 때문에 TopFreqMHz보다 낮아질 수 있음)

    private int TopFreqMHz => _boost ? BoostMaxFreqMHz : DefaultMaxFreqMHz;

    public MainForm()
    {
        Text = "CPU 온도";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        TransparencyKey = Color.Black; // 검은 배경 부분을 완전히 투명하게
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(8);

        var menu = new ContextMenuStrip();
        var boostItem = new ToolStripMenuItem("부스트 켜기") { CheckOnClick = true };
        boostItem.CheckedChanged += (_, _) => SetBoost(boostItem.Checked);
        menu.Items.Add(boostItem);
        menu.Items.Add("종료", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;
        UpdateTrayIcon("--", Color.Gray);

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        layout.Controls.Add(_packageTemp);
        layout.Controls.Add(_powerInfo);
        layout.Controls.Add(_limitInfo);
        Controls.Add(layout);

        if (!PawnIo.IsInstalled)
        {
            _packageTemp.Text = "CPU --°C";
            _powerInfo.Text = "PawnIO 드라이버 필요\nhttps://pawnio.eu";
            return;
        }

        _computer.Open();
        SetMaxFrequency(DefaultMaxFreqMHz); // 항상 기본 속도로 시작
        SetMaxProcessorState(MaxLimit);     // 이전 실행이 비정상 종료돼 남은 온도 제한 초기화
        _timer.Tick += (_, _) => UpdateTemperatures();
        _timer.Start();
        UpdateTemperatures();
    }

    // WS_EX_LAYERED | WS_EX_TRANSPARENT: 마우스 클릭이 이 창을 통과해 뒤 창으로 간다.
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x20;
            return cp;
        }
    }

    // 크기가 바뀔 때마다(글자 길이 변화 등) 오른쪽 위에 다시 붙인다.
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - ScreenMargin, area.Top + ScreenMargin);
    }

    private void UpdateTemperatures()
    {
        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu == null)
        {
            _packageTemp.Text = "CPU 없음";
            return;
        }

        cpu.Update();

        // "Distance to TjMax" 센서는 온도가 아니라 한계까지 남은 값이라 제외
        var temps = cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue && !s.Name.Contains("Distance")).ToList();
        var package = temps.FirstOrDefault(s => s.Name.Contains("Package")) ?? temps.FirstOrDefault();
        _packageTemp.Text = package == null ? "CPU N/A" : $"CPU {package.Value:0}°C";
        _packageTemp.ForeColor = TempColor(package?.Value);

        var cpuPower = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Name == "CPU Package")?.Value;
        if (_tick++ % BatteryIntervalSec == 0)
            _batteryText = BatteryText();
        _powerInfo.Text = $"CPU {(cpuPower is float w ? $"{w:0}W" : "--")} · {_batteryText}";
        _tray.Text = $"{_packageTemp.Text}\n{_powerInfo.Text}";
        UpdateTrayIcon(package == null ? "--" : $"{package.Value:0}", package == null ? Color.Gray : TempColor(package.Value));

        if (package?.Value is float temp)
            AdjustLimit(temp, MaxCoreClockMHz(cpu));
    }

    // 가장 빠른 코어의 현재 속도(MHz). Bus Speed는 코어 속도가 아니라 제외
    private static float? MaxCoreClockMHz(IHardware cpu) =>
        cpu.Sensors.Where(s => s.SensorType == SensorType.Clock && s.Value.HasValue && !s.Name.Contains("Bus")).Max(s => s.Value);

    // 충전/방전 전력은 Windows 배터리 정보(root\wmi BatteryStatus, mW 단위)에서 읽는다
    private string BatteryText()
    {
        using var results = _batterySearcher.Get();
        foreach (ManagementObject b in results)
        {
            using (b)
            {
                if (Convert.ToBoolean(b["Charging"]))
                    return $"충전 {Convert.ToDouble(b["ChargeRate"]) / 1000:0}W";
                if (Convert.ToBoolean(b["PowerOnline"]))
                    return "충전 안 함";
                return $"배터리 {Convert.ToDouble(b["DischargeRate"]) / 1000:0}W\n{RemainingTimeText()}";
            }
        }
        return "배터리 없음";
    }

    // Windows가 계산한 남은 사용 시간(사용량 변화를 평균 낸 값). 막 충전기를 뽑았을 때는 아직 모름(-1).
    private static string RemainingTimeText()
    {
        int sec = System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifeRemaining;
        if (sec < 0)
            return "남은 시간 계산 중";
        var t = TimeSpan.FromSeconds(sec);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}시간 {t.Minutes}분 남음" : $"{t.Minutes}분 남음";
    }

    private static Color TempColor(float? temp) => temp switch
    {
        >= 85 => Color.Red,
        >= 70 => Color.Orange,
        _ => Color.LightGreen,
    };

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    // 트레이 아이콘을 "온도 숫자 + 색 배경" 그림으로 바꾼다. 숫자가 바뀔 때만 다시 그림.
    private void UpdateTrayIcon(string text, Color back)
    {
        if (text == _trayText)
            return;
        _trayText = text;

        int size = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font("Segoe UI", size * (text.Length >= 3 ? 0.5f : 0.72f), FontStyle.Bold, GraphicsUnit.Pixel))
        // GenericTypographic: 기본 여백을 없애야 16px 아이콘에 두세 자리 숫자가 줄바꿈 없이 들어감
        using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
        {
            format.Alignment = StringAlignment.Center;
            format.LineAlignment = StringAlignment.Center;
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
            g.Clear(back);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawString(text, font, Brushes.Black, new RectangleF(0, 0, size, size), format);
        }

        var old = _tray.Icon;
        _tray.Icon = Icon.FromHandle(bmp.GetHicon());
        if (old != null)
            DestroyIcon(old.Handle); // GetHicon으로 만든 핸들은 직접 해제해야 누수가 없음
    }

    // 85°C 이상이면 최고 속도를 200MHz씩 낮추고 75°C 이하면 다시 올린다 → 75~85°C에서 버틸 수 있는 가장 높은 속도에 머묾.
    // 95°C 이상은 순간적으로 치솟는 상황이라 최대 프로세서 상태를 확 낮춰 급감속한다.
    private void AdjustLimit(float temp, float? clockMHz)
    {
        bool changed = false;
        if (temp >= EmergencyTemp && !_emergency)
        {
            _emergency = true;
            SetMaxProcessorState(EmergencyLimit);
            changed = true;
        }
        else if (_emergency && temp <= LimitReleaseTemp)
        {
            _emergency = false;
            SetMaxProcessorState(MaxLimit);
            changed = true;
        }

        // 제한이 실제 속도보다 한참 높으면(전력 제한 등으로 이미 느린 경우) 낮춰도 효과가 없으므로 실제 속도 바로 아래부터 낮춘다
        int from = clockMHz is float c && c < _capMHz - 2 * LimitStepMHz ? (int)c / 100 * 100 : _capMHz;
        int next = temp >= LimitStartTemp ? Math.Max(MinFreqMHz, from - LimitStepMHz)
                 : temp <= LimitReleaseTemp ? Math.Min(TopFreqMHz, _capMHz + LimitStepMHz)
                 : _capMHz;
        if (next != _capMHz)
        {
            _capMHz = next;
            SetMaxFrequency(_capMHz);
            changed = true;
        }

        if (changed)
            UpdateLimitInfo();
    }

    // 트레이 메뉴 "부스트 켜기": 최고 속도 상한만 바꾼다. 온도 때문에 제한 중이면 그 제한은 유지하고, 풀릴 때 새 상한까지 올라간다.
    private void SetBoost(bool on)
    {
        bool throttled = _capMHz < TopFreqMHz;
        _boost = on;
        if (!throttled || _capMHz > TopFreqMHz)
        {
            _capMHz = TopFreqMHz;
            SetMaxFrequency(_capMHz);
        }
        UpdateLimitInfo();
    }

    private void UpdateLimitInfo()
    {
        _limitInfo.Text = _emergency ? "과열! CPU 속도 급감속 중"
                        : _capMHz < TopFreqMHz ? $"CPU 속도 제한 {_capMHz / 1000.0:0.0}GHz"
                        : _boost ? $"부스트 켜짐 (최대 {BoostMaxFreqMHz / 1000.0:0.0}GHz)"
                        : "";
        _limitInfo.Visible = _limitInfo.Text != "";
    }

    // "최대 프로세서 주파수"(MHz, 0 = 제한 없음)를 충전기/배터리 양쪽에 설정하고 즉시 적용
    private static void SetMaxFrequency(int mhz)
    {
        RunPowercfg($"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX {mhz}");
        RunPowercfg($"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCFREQMAX {mhz}");
        RunPowercfg("/setactive SCHEME_CURRENT");
    }

    // 충전기/배터리 양쪽의 "최대 프로세서 상태"를 바꾸고 즉시 적용
    private static void SetMaxProcessorState(int percent)
    {
        RunPowercfg($"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {percent}");
        RunPowercfg($"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {percent}");
        RunPowercfg("/setactive SCHEME_CURRENT");
    }

    private static void RunPowercfg(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("powercfg", args) { CreateNoWindow = true, UseShellExecute = false });
        p?.WaitForExit();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        if (_capMHz != DefaultMaxFreqMHz)
            SetMaxFrequency(DefaultMaxFreqMHz); // 앱을 끄면 부스트·온도 제한은 풀고 기본 속도로 (쿨러 고장 대비)
        if (_emergency)
            SetMaxProcessorState(MaxLimit);
        _computer.Close();
        _batterySearcher.Dispose();
        _tray.Dispose(); // 안 하면 종료 후에도 트레이에 아이콘이 남아 보임
        base.OnFormClosed(e);
    }
}
