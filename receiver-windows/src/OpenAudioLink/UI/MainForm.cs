using System;
using System.Drawing;
using System.Net;
using System.Windows.Forms;
using OpenAudioLink.Protocol;
using OpenAudioLink.Receiver;

namespace OpenAudioLink
{
    public sealed class MainForm : Form
    {
        private readonly Label renderedFramesLabel;
        private readonly ReceiverRuntime runtime;

        public MainForm()
        {
            Text = "OpenAudioLink Receiver";
            Size = new Size(480, 240);

            Label portLabel = new Label
            {
                AutoSize = true,
                Location = new Point(24, 24),
                Text = "Listening on TCP port ..."
            };
            renderedFramesLabel = new Label
            {
                AutoSize = true,
                Location = new Point(24, 56),
                Text = RenderedFramesText(0)
            };

            Controls.Add(portLabel);
            Controls.Add(renderedFramesLabel);

            runtime = ReceiverRuntime.Start(IPAddress.Any, ProtocolConstants.DefaultPort, renderedCountChanged: UpdateRenderedFrames);
            ListeningPort = runtime.Port;
            portLabel.Text = "Listening on TCP port " + ListeningPort;
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

        private void UpdateRenderedFrames(int renderedCount)
        {
            if (IsDisposed || renderedFramesLabel.IsDisposed)
            {
                return;
            }

            if (renderedFramesLabel.InvokeRequired && renderedFramesLabel.IsHandleCreated)
            {
                try
                {
                    renderedFramesLabel.BeginInvoke(new Action(() => UpdateRenderedFrames(renderedCount)));
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            renderedFramesLabel.Text = RenderedFramesText(renderedCount);
        }

        private static string RenderedFramesText(int renderedCount)
        {
            return "Rendered frames: " + renderedCount;
        }
    }
}
