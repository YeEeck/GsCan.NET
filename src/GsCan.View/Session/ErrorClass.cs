using GsCan;

namespace GsCan.View.Session
{
    internal static class ErrorClass
    {
        public const string Ack = "ACK";
        public const string Stuff = "Stuff";
        public const string Form = "Form";
        public const string Bit0 = "Bit0";
        public const string Bit1 = "Bit1";
        public const string Crc = "CRC";
        public const string BusOff = "Bus-off";
        public const string ErrorPassive = "Error-passive";
        public const string ErrorWarning = "Error-warning";
        public const string ErrorActive = "Error-active";
        public const string Restarted = "Restarted";
        public const string BusError = "Bus error";
        public const string Unknown = "Unknown";

        private const uint CanErrCrtl = 0x00000004;
        private const uint CanErrProt = 0x00000008;
        private const uint CanErrAck = 0x00000020;
        private const uint CanErrBusOff = 0x00000040;
        private const uint CanErrBusError = 0x00000080;
        private const uint CanErrRestarted = 0x00000100;
        private const uint CanErrCnt = 0x00000200;

        private const byte CrtlRxWarning = 0x04;
        private const byte CrtlTxWarning = 0x08;
        private const byte CrtlRxPassive = 0x10;
        private const byte CrtlTxPassive = 0x20;
        private const byte CrtlActive = 0x40;

        private const byte ProtForm = 0x02;
        private const byte ProtStuff = 0x04;
        private const byte ProtBit0 = 0x08;
        private const byte ProtBit1 = 0x10;
        private const byte ProtLocCrcSeq = 0x08;

        public static Classification Describe(CanFrame frame)
        {
            var name = Classify(frame);
            var hint = HintFor(name);
            if ((frame.Id & CanErrCnt) != 0 && frame.Data != null && frame.Data.Length >= 8)
            {
                hint += " TEC=" + frame.Data[6] + " REC=" + frame.Data[7];
            }

            return new Classification(name, hint, HaltsCyclic(name));
        }

        public readonly struct Classification
        {
            public Classification(string name, string hint, bool haltsCyclic)
            {
                Name = name;
                Hint = hint;
                HaltsCyclic = haltsCyclic;
            }

            public string Name { get; }
            public string Hint { get; }
            public bool HaltsCyclic { get; }
        }

        private static string Classify(CanFrame frame)
        {
            uint id = frame.Id;
            var data = frame.Data ?? System.Array.Empty<byte>();
            byte ctrl = data.Length > 1 ? data[1] : (byte)0;
            byte prot = data.Length > 2 ? data[2] : (byte)0;
            byte location = data.Length > 3 ? data[3] : (byte)0;

            if ((id & CanErrBusOff) != 0)
            {
                return BusOff;
            }

            if ((id & CanErrRestarted) != 0)
            {
                return Restarted;
            }

            if ((id & CanErrAck) != 0)
            {
                return Ack;
            }

            if ((prot & ProtStuff) != 0)
            {
                return Stuff;
            }

            if ((prot & ProtForm) != 0)
            {
                return Form;
            }

            if ((prot & ProtBit0) != 0)
            {
                return Bit0;
            }

            if ((prot & ProtBit1) != 0)
            {
                return Bit1;
            }

            if ((location & ProtLocCrcSeq) != 0)
            {
                return Crc;
            }

            if ((id & CanErrCrtl) != 0 && (ctrl & (CrtlRxPassive | CrtlTxPassive)) != 0)
            {
                return ErrorPassive;
            }

            if ((id & CanErrCrtl) != 0 && (ctrl & (CrtlRxWarning | CrtlTxWarning)) != 0)
            {
                return ErrorWarning;
            }

            if ((id & CanErrCrtl) != 0 && (ctrl & CrtlActive) != 0)
            {
                return ErrorActive;
            }

            if ((id & (CanErrBusError | CanErrProt)) != 0)
            {
                return BusError;
            }

            return Unknown;
        }

        private static string HintFor(string name)
        {
            switch (name)
            {
                case Ack:
                    return "未收到应答。常见原因：没有第二节点、波特率不一致、或对端只听。";
                case Stuff:
                    return "填充位错误。常见原因：波特率不一致、接线干扰或终端电阻不对。";
                case Form:
                    return "帧格式错误。常见原因：波特率不一致或对端帧格式不兼容。";
                case Bit0:
                    return "无法发出显性位。常见原因：总线短路或驱动能力不足。";
                case Bit1:
                    return "无法发出隐性位。常见原因：总线对地短路或被钳在显性。";
                case Crc:
                    return "CRC 错误。常见原因：干扰、波特率不一致或接线质量差。";
                case BusOff:
                    return "控制器已 Bus-off，本路暂时不能上总线。";
                case ErrorPassive:
                    return "已进入 error-passive，发送仍会进行但出错时只发隐性错误标志。";
                case ErrorWarning:
                    return "错误计数达到警告阈值。";
                case ErrorActive:
                    return "已回到 error-active。";
                case Restarted:
                    return "控制器已从 Bus-off 恢复。";
                case BusError:
                    return "总线错误（控制器未给出更细的类）。";
                default:
                    return "收到错误帧，但类位无法识别。";
            }
        }

        private static bool HaltsCyclic(string name)
        {
            switch (name)
            {
                case BusOff:
                case Ack:
                case Stuff:
                case Form:
                case Bit0:
                case Bit1:
                case Crc:
                case ErrorPassive:
                case Unknown:
                    return true;
                default:
                    return false;
            }
        }
    }
}
