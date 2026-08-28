using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MonitorVirtual.Core.Logging;
using MonitorVirtual.Core.Surround;

namespace MonitorVirtual.App.Surround;

/// <summary>
/// Captura o monitor virtual (canvas único do Holyrics) e pinta cada projetor
/// com a fatia correspondente + soft-edge. Sem isto o Windows em clone manda
/// o mesmo slide nos dois lados do telão.
/// </summary>
internal sealed class SurroundOutputHost : IDisposable
{
    private readonly Func<Rectangle?> _getSourceBounds;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly List<Output> _outputs = new();

    private SurroundPlan? _plan;
    private Bitmap? _canvas;
    private double _gamma = 2.2;
    private bool _paused;
    private bool _captureFailing;
    private bool _disposed;

    public bool IsRunning => _plan is not null && !_paused && _outputs.Count > 0;
    public string? Summary => _plan?.Summary;

    public SurroundOutputHost(Func<Rectangle?> getSourceBounds)
    {
        _getSourceBounds = getSourceBounds;
        _timer.Tick += (_, _) => CaptureAndPresent();
    }

    public void Start(SurroundPlan plan, int fps, double gamma)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SurroundOutputHost));

        _gamma = gamma;
        _paused = false;

        if (_plan?.Key == plan.Key && _outputs.Count == plan.Slices.Count)
        {
            SetFps(fps);
            _timer.Start();
            foreach (var output in _outputs) output.Form.Visible = true;
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
        CaptureAndPresent();
        Log.Info($"Saída surround iniciada: {plan.Summary}.");
    }

    public void Pause()
    {
        _paused = true;
        _timer.Stop();
        foreach (var output in _outputs) output.Form.Visible = false;
    }

    public void Resume()
    {
        if (_plan is null || _disposed) return;
        _paused = false;
        foreach (var output in _outputs) output.Form.Visible = true;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _plan = null;
        StopForms();
    }

    private void SetFps(int fps)
    {
        fps = Math.Clamp(fps, 5, 60);
        _timer.Interval = Math.Max(16, 1000 / fps);
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

        SoftEdgeBlend.Apply(output.Frame, slice.BlendEdge, slice.BlendPixels, _gamma);
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
        StopForms();
        _canvas?.Dispose();
        _canvas = null;
        _plan = null;
    }

    private sealed record Output(SurroundSlice Slice, SurroundOutputForm Form, Bitmap Frame);
}
