using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using QRCoder;

namespace A2dpSink;

internal sealed class QrForm : Form
{
    private readonly PictureBox _qrImage = new()
    {
        Bounds = new Rectangle(40, 24, 300, 300),
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.White
    };

    private readonly TextBox _keyBox = new()
    {
        Multiline = true,
        ReadOnly = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Horizontal,
        Font = new Font(SystemFonts.DefaultFont.FontFamily, 8f),
        Bounds = new Rectangle(20, 372, 340, 44)
    };

    public QrForm()
    {
        Text = "C4P - Pairing QR";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(380, 470);

        Controls.Add(_qrImage);

        var hint = new Label
        {
            Text = "Open C4P on the phone > Setup > Scan pairing QR",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font, FontStyle.Bold),
            Bounds = new Rectangle(20, 336, 340, 32)
        };
        Controls.Add(hint);
        Controls.Add(_keyBox);

        var close = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.Cancel,
            Bounds = new Rectangle(140, 424, 100, 30)
        };
        Controls.Add(close);
        CancelButton = close;

        Load += async (_, _) => await RefreshQrAsync();

        FormClosed += (_, _) =>
        {
            _qrImage.Image?.Dispose();
            _qrImage.Dispose();
        };
    }

    private async Task RefreshQrAsync()
    {
        try
        {
            string payload = await PairingQr.BuildPayloadAsync();

            using var generator = new QRCodeGenerator();
            using QRCodeData data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            using var qr = new QRCode(data);

            Image? previous = _qrImage.Image;
            _qrImage.Image = qr.GetGraphic(6);
            previous?.Dispose();

            _keyBox.Text = payload;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not build pairing QR: {ex.Message}", "C4P",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
