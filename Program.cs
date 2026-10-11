namespace CpuT;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 이미 실행 중이면 바로 종료 (오버레이·트레이 아이콘이 두 개 생기는 것 방지)
        using var mutex = new Mutex(true, @"Local\CpuT.SingleInstance", out bool isFirst);
        if (!isFirst)
            return;

        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }    
}