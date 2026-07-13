using System.Drawing;
using System.Net;
using System.Windows.Forms;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;

namespace OpenAudioLink
{
    public sealed class MainForm : Form
    {
        private readonly ReceiverRuntime runtime;

        public MainForm()
        {
            runtime = ReceiverRuntime.Start(IPAddress.Any, ProtocolConstants.DefaultPort);
            ListeningPort = runtime.Port;

            Text = "OpenAudioLink Receiver";
            Size = new Size(480, 240);

            Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(24, 24),
                Text = "Listening on TCP port " + ListeningPort
            });
        }

        public int ListeningPort { get; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                runtime.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
