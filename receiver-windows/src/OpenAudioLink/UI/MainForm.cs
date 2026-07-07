using System.Drawing;
using System.Windows.Forms;

namespace OpenAudioLink
{
    public sealed class MainForm : Form
    {
        public MainForm()
        {
            Text = "OpenAudioLink Receiver";
            Size = new Size(480, 240);

            Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(24, 24),
                Text = "OpenAudioLink Receiver Skeleton"
            });
        }
    }
}
