using System.Drawing.Drawing2D;

namespace CpuT;

// 흰 배경 위에서도 보이도록 글자에 어두운 테두리를 그리는 라벨.
// 창의 투명 처리 색(순수 검정)과 같으면 테두리까지 투명해지므로 거의 검정인 색을 쓴다.
public class OutlinedLabel : Label
{
    private const int OutlineWidth = 3;
    private static readonly Color OutlineColor = Color.FromArgb(20, 20, 20);

    private GraphicsPath BuildPath()
    {
        var path = new GraphicsPath();
        float emSize = Font.SizeInPoints * DeviceDpi / 72f;
        path.AddString(Text, Font.FontFamily, (int)Font.Style, emSize, new PointF(OutlineWidth, OutlineWidth), StringFormat.GenericTypographic);
        return path;
    }

    // 자동 크기 계산을 실제로 그리는 글자 모양(+테두리) 기준으로 맞춘다
    public override Size GetPreferredSize(Size proposedSize)
    {
        using var path = BuildPath();
        var bounds = path.GetBounds();
        return new Size((int)Math.Ceiling(bounds.Right) + OutlineWidth, (int)Math.Ceiling(bounds.Bottom) + OutlineWidth);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = BuildPath();
        using (var pen = new Pen(OutlineColor, OutlineWidth * 2) { LineJoin = LineJoin.Round })
            e.Graphics.DrawPath(pen, path);
        using var brush = new SolidBrush(ForeColor);
        e.Graphics.FillPath(brush, path);
    }
}
