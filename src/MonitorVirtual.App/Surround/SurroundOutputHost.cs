using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MonitorVirtual.Core.Logging;
using MonitorVirtual.Core.Surround;

namespace MonitorVirtual.App.Surround;

/// <summary>
/// Captura o monitor virtual (canvas único do Holyrics) e pinta cada projetor
/// com a fatia correspondente + soft-edge. Sem isto o Windows em clone manda
/// o mesmo slide nos dois lados do telão.
/// Gama/ganho/largura do fade aplicam-se às fatias físicas, não só ao preview.
/// </summary>
internal sealed class SurroundOutputHost : IDisposable
{
    private readonly Func<Rectangle?> _getSourceBounds;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly System.Windows.Forms.Timer _topMost = new();
    private readonly List<Output> _outputs = new();

    private SurroundPlan? _plan;
    private Bitmap? _canvas;
    private double _gamma = SoftEdgeCurve.DefaultGamma;
    private double _gain = SoftEdgeCurve.DefaultGain;
    private bool _paused;
    private bool _holdZOrder;
    private bool _captureFailing;
    private bool _disposed;

    public bool IsRunning => _plan is not null && !_paused && _outputs.Count > 0;
    public string? Summary => _plan?.Summary;
    public SurroundPlan? Plan => _plan;
    public IReadOnlyList<string> ProjectorDeviceNames =>
        _plan?.Slices.Select(s => s.DeviceName).ToArray() ?? Array.Empty<string>();

    /// <summary>Depois de reafirmar as fatias, o painel/blend sobe de novo sem ativar.</summary>
    public Action? RaiseOperatorUi { get; set; }

    public SurroundOutputHost(Func<Rectangle?> getSourceBounds)
    {
        _getSourceBounds = getSourceBounds;
        _timer.Tick += (_, _) => CaptureAndPresent();
        _topMost.Interval = 400;
        _topMost.Tick += (_, _) => KeepOverlaysOnTop();
    }

    public void Start(SurroundPlan plan, int fps, double gamma, double gain)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SurroundOutputHost));

        _gamma = gamma;
        _gain = gain;
        _paused = false;

        if (_plan?.Key == plan.Key && _outputs.Count == plan.Slices.Count)
        {
            ApplyBlend(gamma, gain, plan.Overlap);
            SetFps(fps);
            _timer.Start();
            _topMost.Start();
            foreach (var output in _outputs) output.Form.Visible = true;
            KeepOverlaysOnTop();
            return;
        }

        StopForms();
        _plan = plan;
        SetFps(fps);

        foreach (var slice in plan.Slices)
        {
            var form = new SurroundOutputForm(slice);
            var frame = new Bitmap(Math.Max(1, slice.OutputWidth), Math.Max(1, slice.OutputHeight),
                PixelFormat.Format32bppPArgb);
            _outputs.Add(new Output(slice, form, frame));
            form.Show();
        }

        _timer.Start();
        _topMost.Start();
        CaptureAndPresent();
        KeepOverlaysOnTop();
        Log.Info($"Saída surround iniciada: {plan.Summary}.");
    }

    /// <summary>
    /// Atualiza gama, ganho e largura do fade nas fatias já enviadas aos projetores,
    /// no próximo quadro — sem recriar janelas e sem mexer no canvas do Holyrics.
    /// </summary>
    public void ApplyBlend(double gamma, double gain, int blendPixels)
    {
        _gamma = gamma;
        _gain = gain;
        if (_outputs.Count == 0) return;

        for (var i = 0; i < _outputs.Count; i++)
        {
            var output = _outputs[i];
            var maxPx = Math.Max(0, output.Slice.OutputWidth / 2);
            var px = Math.Clamp(blendPixels, 0, maxPx);
            var edge = BlendEdge.None;
            if (px > 0)
            {
                if (i > 0) edge |= BlendEdge.Left;
                if (i < _outputs.Count - 1) edge |= BlendEdge.Right;
            }

            _outputs[i] = output with { Slice = output.Slice with { BlendPixels = px, BlendEdge = edge } };
        }
    }

    /// <summary>
    /// Pausa só o z-order (menu da bandeja / painel). A captura continua — o telão não pisca.
    /// </summary>
    public void HoldZOrder(bool hold) => _holdZOrder = hold;

    public void Pause()
    {
        _paused = true;
        _timer.Stop();
        _topMost.Stop();
        foreach (var output in _outputs) output.Form.Visible = false;
    }

    public void Resume()
    {
        if (_plan is null || _disposed) return;
        _paused = false;
        foreach (var output in _outputs) output.Form.Visible = true;
        _timer.Start();
        _topMost.Start();
        KeepOverlaysOnTop();
    }

    public void Stop()
    {
        _timer.Stop();
        _topMost.Stop();
        _plan = null;
        StopForms();
    }

    private void SetFps(int fps)
    {
        fps = Math.Clamp(fps, 5, 60);
        _timer.Interval = Math.Max(16, 1000 / fps);
    }

    private void KeepOverlaysOnTop()
    {
        if (_paused || _holdZOrder) return;
        foreach (var output in _outputs) output.Form.KeepOnTop();
        RaiseOperatorUi?.Invoke();
    }

    private void CaptureAndPresent()
    {
        if (_paused || _plan is null) return;

        var bounds = _getSourceBounds();
        if (bounds is null || bounds.Value.Width <= 0 || bounds.Value.Height <= 0)
            return;

        var source = bounds.Value;

        try
        {
            if (_canvas is null || _canvas.Width != source.Width || _canvas.Height != source.Height)
            {
                _canvas?.Dispose();
                _canvas = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
            }

            using (var g = Graphics.FromImage(_canvas))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CopyFromScreen(source.X, source.Y, 0, 0, source.Size, CopyPixelOperation.SourceCopy);
                g.CompositingMode = CompositingMode.SourceOver;
                DrawHardwareCursor(g, source);
            }

            foreach (var output in _outputs)
            {
                output.Form.SetBounds(output.Slice);
                BlitSlice(_canvas, output);
                output.Form.Present(output.Frame);
            }

            _captureFailing = false;
        }
        catch (Exception ex)
        {
            if (!_captureFailing)
            {
                _captureFailing = true;
                Log.Error("Falha ao pintar o telão surround", ex);
            }
        }
    }

    private static void DrawHardwareCursor(Graphics g, Rectangle source)
    {
        var pos = Control.MousePosition;
        if (!source.Contains(pos)) return;
        try
        {
            var cursor = Cursor.Current ?? Cursors.Default;
            var x = pos.X - source.X - cursor.HotSpot.X;
            var y = pos.Y - source.Y - cursor.HotSpot.Y;
            cursor.Draw(g, new Rectangle(x, y, cursor.Size.Width, cursor.Size.Height));
        }
        catch
        {
            // o cursor no telão é cortesia; a captura do canvas não pode cair
        }
    }

    private void BlitSlice(Bitmap canvas, Output output)
    {
        var slice = output.Slice;
        var src = new Rectangle(slice.SourceX, slice.SourceY, slice.SourceWidth, slice.SourceHeight);
        src.Intersect(new Rectangle(0, 0, canvas.Width, canvas.Height));
        if (src.Width <= 0 || src.Height <= 0) return;

        using (var g = Graphics.FromImage(output.Frame))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.Black);
            g.DrawImage(canvas, new Rectangle(0, 0, output.Frame.Width, output.Frame.Height),
                src, GraphicsUnit.Pixel);
        }

        SoftEdgeBlend.Apply(output.Frame, slice.BlendEdge, slice.BlendPixels, _gamma, _gain);
    }

    private void StopForms()
    {
        foreach (var output in _outputs)
        {
            output.Form.Present(null);
            output.Form.Shutdown();
            output.Form.Dispose();
            output.Frame.Dispose();
        }

        _outputs.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        _topMost.Stop();
        _topMost.Dispose();
        StopForms();
        _canvas?.Dispose();
        _canvas = null;
        _plan = null;
    }

    private sealed record Output(SurroundSlice Slice, SurroundOutputForm Form, Bitmap Frame);
}
