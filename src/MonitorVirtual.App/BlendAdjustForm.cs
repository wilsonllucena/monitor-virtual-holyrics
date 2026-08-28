using MonitorVirtual.Core.Config;
using MonitorVirtual.Core.Surround;

namespace MonitorVirtual.App;

/// <summary>
/// Painel pequeno, sempre visível, para ajustar o blend olhando o TELÃO.
/// Gama/ganho/largura aplicam-se no próximo quadro nas fatias dos projetores.
/// </summary>
internal sealed class BlendAdjustForm : Form
{
    private readonly AppConfig _cfg;
    private readonly Action<int, double, double> _applyLive;
    private readonly Action<bool> _showTest;
    private readonly Action _persist;

    private readonly TrackBar _overlap = NewBar(0, 640);
    private readonly TrackBar _gamma = NewBar(40, 300);
    private readonly TrackBar _gain = NewBar(25, 250);
    private readonly Label _overlapValue = ValueLabel();
    private readonly Label _gammaValue = ValueLabel();
    private readonly Label _gainValue = ValueLabel();
    private readonly Label _hint = new()
    {
        AutoSize = true,
        MaximumSize = new Size(460, 0),
        ForeColor = SystemColors.GrayText,
    };
    private readonly CheckBox _test = new()
    {
        Text = "Mostrar padrão de junta (sem Holyrics) — olhe o TELÃO",
        AutoSize = true,
    };

    private bool _syncing;

    public BlendAdjustForm(
        AppConfig cfg,
        Action<int, double, double> applyLive,
        Action<bool> showTest,
        Action persist)
    {
        _cfg = cfg;
        _applyLive = applyLive;
        _showTest = showTest;
        _persist = persist;

        Text = "Ajustar blend do telão";
        Icon = IconFactory.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;
        ClientSize = new Size(500, 340);
        AutoScaleMode = AutoScaleMode.Dpi;

        var y = 16;
        Controls.Add(new Label
        {
            Text = "Olhe a PAREDE, não o preview do PC. O preview de monitor mente: dois projetores somam luz.",
            Left = 16, Top = y, AutoSize = true, MaximumSize = new Size(468, 0),
        });
        y += 40;

        AddRow("Overposição (px)", _overlap, _overlapValue, ref y);
        AddRow("Gama (maior = junta mais clara)", _gamma, _gammaValue, ref y);
        AddRow("Intensidade da junta", _gain, _gainValue, ref y);

        _hint.Left = 16;
        _hint.Top = y;
        Controls.Add(_hint);
        y += 48;

        _test.Left = 16;
        _test.Top = y;
        _test.CheckedChanged += (_, _) => _showTest(_test.Checked);
        Controls.Add(_test);
        y += 36;

        var apply = new Button { Text = "Fechar e guardar", Width = 150, Left = 334, Top = y };
        apply.Click += (_, _) => Close();
        Controls.Add(apply);

        _overlap.ValueChanged += (_, _) => OnChanged();
        _gamma.ValueChanged += (_, _) => OnChanged();
        _gain.ValueChanged += (_, _) => OnChanged();

        LoadValues();
        UpdateHint();
    }

    private void AddRow(string title, TrackBar bar, Label value, ref int y)
    {
        Controls.Add(new Label { Text = title, Left = 16, Top = y, AutoSize = true });
        y += 18;
        bar.Left = 16;
        bar.Top = y;
        bar.Width = 380;
        value.Left = 404;
        value.Top = y + 8;
        Controls.Add(bar);
        Controls.Add(value);
        y += 48;
    }

    private void LoadValues()
    {
        _syncing = true;
        _overlap.Value = Math.Clamp(_cfg.SurroundBlendOverlap, _overlap.Minimum, _overlap.Maximum);
        _gamma.Value = Math.Clamp((int)Math.Round(_cfg.SurroundBlendGamma * 100), _gamma.Minimum, _gamma.Maximum);
        _gain.Value = Math.Clamp((int)Math.Round(_cfg.SurroundBlendGain * 100), _gain.Minimum, _gain.Maximum);
        _syncing = false;
        RefreshLabels();
    }

    private void OnChanged()
    {
        if (_syncing) return;
        RefreshLabels();
        UpdateHint();
        _applyLive(Overlap, Gamma, Gain);
    }

    private void RefreshLabels()
    {
        _overlapValue.Text = $"{Overlap} px";
        _gammaValue.Text = Gamma.ToString("0.00");
        _gainValue.Text = Gain.ToString("0.00");
    }

    private void UpdateHint()
    {
        _hint.Text =
            "Faixa PRETA no meio → aumente a gama (2,2–2,8) ou a intensidade.\n" +
            "Costura CLARA → diminua a gama. Largura demais “come” o slide; de menos deixa corte seco.";
    }

    private int Overlap => _overlap.Value;
    private double Gamma => _gamma.Value / 100.0;
    private double Gain => _gain.Value / 100.0;

    private void Persist()
    {
        _cfg.SurroundBlendOverlap = Overlap;
        _cfg.SurroundBlendGamma = Gamma;
        _cfg.SurroundBlendGain = Gain;
        _persist();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_test.Checked) _showTest(false);
        Persist();
        base.OnFormClosed(e);
    }

    private static TrackBar NewBar(int min, int max) => new()
    {
        Minimum = min,
        Maximum = max,
        TickFrequency = Math.Max(1, (max - min) / 16),
        AutoSize = false,
        Height = 36,
    };

    private static Label ValueLabel() => new()
    {
        AutoSize = true,
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
    };
}
