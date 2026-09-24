using System;
using System.Threading;
using GsCan;

namespace GsCan.View.Session
{
    internal sealed class ReadPump
    {
        private readonly IOpenedDevice _device;
        private readonly int _channelIndex;
        private readonly Action<int, CanFrame> _accept;
        private readonly Action<string> _onError;
        private volatile bool _stop;
        private Thread? _thread;

        public ReadPump(
            IOpenedDevice device,
            int channelIndex,
            Action<int, CanFrame> accept,
            Action<string> onError)
        {
            _device = device;
            _channelIndex = channelIndex;
            _accept = accept;
            _onError = onError;
        }

        public void Start()
        {
            _stop = false;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "GsCan.View.ReadPump-" + _channelIndex
            };
            _thread.Start();
        }

        public void Stop(Action unblock)
        {
            _stop = true;
            unblock();
            var thread = _thread;
            _thread = null;
            if (thread != null && thread != Thread.CurrentThread)
            {
                thread.Join(1000);
            }
        }

        internal static bool IsChannelNotStarted(string? message)
        {
            return message == "Channel is not started.";
        }

        private void Run()
        {
            while (!_stop)
            {
                try
                {
                    if (!_device.TryRead(_channelIndex, 50, out var frame))
                    {
                        continue;
                    }

                    _accept(_channelIndex, frame);
                    while (!_stop && _device.TryRead(_channelIndex, 0, out frame))
                    {
                        _accept(_channelIndex, frame);
                    }
                }
                catch (GsCanException ex)
                {
                    if (_stop)
                    {
                        break;
                    }

                    // Stop unblocks TryRead with this message. It is not unplug:
                    // retry so a still-running channel survives another channel's Stop.
                    if (IsChannelNotStarted(ex.Message))
                    {
                        continue;
                    }

                    _onError(ex.Message);
                    break;
                }
            }
        }
    }
}
