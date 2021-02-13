using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro.CrestronThread;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.Config;
using PepperDash.Essentials.Devices.Common.Codec;
using System.Text.RegularExpressions;
using Crestron.SimplSharp.Reflection;

using Newtonsoft.Json;
using PepperDash_Essentials_Core.Queues;

namespace PepperDash.Essentials.Devices.Common.Cameras
{
    public class ProcessBytesMessage : IQueueMessage
    {
        private readonly Action<byte[]> _action;
        private readonly byte[] _message;
        
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="message">Message to be processed</param>
        /// <param name="action">Action to invoke on the message</param>
        public ProcessBytesMessage(byte[] message, Action<byte[]> action)
        {
            _message = message;
            _action = action;
        }

        /// <summary>
        /// Processes the byte array with the given action
        /// </summary>
        public void Dispatch()
        {
            if (_action == null || _message == null || _message.Length == 0)
                return;

            _action(_message);
        }

        /// <summary>
        /// To string
        /// </summary>
        /// <returns>The current message string interpritation</returns>
        public override string ToString()
        {
            if (_message == null || _message.Length == 0)
                return String.Empty;
            else
                return ComTextHelper.GetEscapedText(_message);
        }
    }

    public interface IViscaMessageModifier
    {
        byte[] Process(byte[] message);
    }

    public abstract class ViscaMessage
    {
        private byte _id;
        private byte[] _viscaMessage;
        private int _hash;
        private string _name;
        private List<IViscaMessageModifier> _modifiers = new List<IViscaMessageModifier>();

        protected byte[] viscaMessage
        {
            get { return _viscaMessage; }
            set
            {
                _viscaMessage = new byte[value.Length + 2];
                _viscaMessage[0] = (byte)(0x80 + _id);
                _viscaMessage[_viscaMessage.Length - 1] = 0xFF;
                Array.Copy(value, 0, _viscaMessage, 1, value.Length);
                _hash = getHashCode(_viscaMessage);
            }
        }

        protected ViscaMessage(byte id, string name)
        {
            _id = id;
            _name = name;
        }

        protected ViscaMessage(byte id, string name, byte[] message)
            : this(id, name)
        {
            viscaMessage = message;
        }

        protected ViscaMessage(byte id, string name, byte[] message, IViscaMessageModifier modifier)
            : this(id, name, message)
        {
            if (modifier != null)
                _modifiers.Add(modifier);
        }

        protected ViscaMessage(byte id, string name, byte[] message, IEnumerable<IViscaMessageModifier> modifiers)
            : this(id, name, message)
        {
            if (modifiers != null)
                _modifiers.AddRange(modifiers);
        }

        public byte Id { get { return _id; } }

        /// <summary>
        /// To byte[]
        /// </summary>
        /// <returns>The current raw Visca message as byte array</returns>
        public static implicit operator byte[] (ViscaMessage viscaMessage)
        {
            foreach (var modifier in viscaMessage._modifiers)
            {
                modifier.Process(viscaMessage._viscaMessage);
            }

            return viscaMessage._viscaMessage;
        }

        public static bool operator == (ViscaMessage a, ViscaMessage b)
        {
            if (ReferenceEquals(a, null) && ReferenceEquals(b, null))
                return true;
            if (ReferenceEquals(a, null))
                return false;
            return a.Equals(b);
        }

        public static bool operator !=(ViscaMessage a, ViscaMessage b)
        {
            if (ReferenceEquals(a, null) && ReferenceEquals(b, null))
                return false;
            if (ReferenceEquals(a, null))
                return true;
            return !a.Equals(b);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(obj, null))
                return false;
            if(ReferenceEquals(this, obj))
                return true;

            var second = obj as ViscaMessage;

            return !ReferenceEquals(second, null) && _hash == second._hash;
        }

        public override int GetHashCode()
        {
            return _hash;
        }

        /// <summary>
        /// To string
        /// </summary>
        /// <returns>The current message string interpritation</returns>
        public override string ToString()
        {
            return _name;
        }

        /// <summary>
        /// To string
        /// </summary>
        /// <returns>The current message string interpritation</returns>
        public string ToHexString()
        {
            if (viscaMessage == null || viscaMessage.Length == 0)
                return String.Empty;
            else
                return ComTextHelper.GetEscapedText(this);
        }

        private static int getHashCode(params byte[] data)
        {
            unchecked
            {
                const int p = 16777619;
                int hash = (int)2166136261;

                for (int i = 0; i < data.Length; i++)
                    hash = (hash ^ data[i]) * p;

                hash += hash << 13;
                hash ^= hash >> 7;
                hash += hash << 3;
                hash ^= hash >> 17;
                hash += hash << 5;
                return hash;
            }
        }
    }
    
    public enum ViscaCommand
    {
        Home,
        Stop,
        PowerOn,
        PowerOff,
        ZoomIn,
        ZoomOut,
        ZoomStop,
        FocusStop,
        FocusFar,
        FocusNear,
        FocusTrig,
        FocusAuto,
        FocusManual,
        FocusToggle,
    }

    public class ViscaCommandMessage : ViscaMessage
    {
        private static Dictionary<ViscaCommand, byte[]> _commands;

        static ViscaCommandMessage()
        {
            _commands = new Dictionary<ViscaCommand, byte[]>()
            { 
                { ViscaCommand.Home, new byte[] { 0x01, 0x06, 0x04 } },
                { ViscaCommand.Stop, new byte[] { 0x01, 0x06, 0x01, 0x03, 0x03, 0x03, 0x01 } },
                { ViscaCommand.PowerOn, new byte[] { 0x01, 0x04, 0x00, 0x02 } },
                { ViscaCommand.PowerOff, new byte[] { 0x01, 0x04, 0x00, 0x03 } },
                { ViscaCommand.ZoomIn, new byte[] { 0x01, 0x04, 0x07, 0x02 } },
                { ViscaCommand.ZoomOut, new byte[] { 0x01, 0x04, 0x07, 0x03 } },
                { ViscaCommand.ZoomStop, new byte[] { 0x01, 0x04, 0x07, 0x00 } },
                { ViscaCommand.FocusStop, new byte[] { 0x01, 0x04, 0x08, 0x00 } },
                { ViscaCommand.FocusFar, new byte[] { 0x01, 0x04, 0x08, 0x02 } },
                { ViscaCommand.FocusNear, new byte[] { 0x01, 0x04, 0x08, 0x03 } },
                { ViscaCommand.FocusTrig, new byte[] { 0x01, 0x04, 0x18, 0x01 } },
                { ViscaCommand.FocusAuto, new byte[] { 0x01, 0x04, 0x38, 0x02 } },
                { ViscaCommand.FocusManual, new byte[] { 0x01, 0x04, 0x38, 0x03 } },
                { ViscaCommand.FocusToggle, new byte[] { 0x01, 0x04, 0x38, 0x10 } },
            };
        }

        public ViscaCommandMessage(byte id, ViscaCommand command) : base(id, command.ToString(), _commands[command])
        {
        }
    }


    public enum ViscaPtCommand
    {
        Up,
        Down,
        Left,
        Right,
        UpLeft,
        UpRight,
        DownLeft,
        DownRight,
    }

    public class ViscaPanTiltSpeedBehaivor : IViscaMessageModifier
    {
        private byte _panSpeed = 0x01;
        public byte PanSpeed
        { 
            get { return _panSpeed; } 
            set
            {
                if (value < 0x01 || value > 0x18)
                    throw new ArgumentOutOfRangeException("PanSpeed", "PanSpeed should be in range 0x01 0x18.");
                _panSpeed = value;
            } 
        }
        private byte _tiltSpeed = 0x01;
        public byte TiltSpeed
        { 
            get { return _tiltSpeed; } 
            set
            {
                if (value < 0x01 || value > 0x14)
                    throw new ArgumentOutOfRangeException("PanSpeed", "PanSpeed should be in range 0x01 0x14.");
                _tiltSpeed = value;
            } 
        }

        public ViscaPanTiltSpeedBehaivor(byte? panSpeed, byte? tiltSpeed)
        {
            if (panSpeed != null && panSpeed.HasValue)
                PanSpeed = panSpeed.Value;
            if (tiltSpeed != null && tiltSpeed.HasValue)
                TiltSpeed = tiltSpeed.Value;
        }

        public byte[] Process(byte[] message)
        {
            message[4] = PanSpeed;
            message[5] = TiltSpeed;
            return message;
        }
    }

    /// <summary>
    /// Pan and Tilt commands have variabe parameters, i.e. speed.
    /// ViscaPanTiltSpeedBehaivor is required to achieve speed assigments.
    /// </summary>
    public class ViscaPanTiltCommand : ViscaMessage
    {
        private static Dictionary<ViscaPtCommand, byte[]> _ptCommands;

        static ViscaPanTiltCommand()
        {
            _ptCommands = new Dictionary<ViscaPtCommand, byte[]>()
            { 
                { ViscaPtCommand.Up, new byte[] { 0x01, 0x06, 0x01, 0x00,0x00, 0x03, 0x01 } },
                { ViscaPtCommand.Down, new byte[] { 0x01, 0x06, 0x01, 0x00, 0x00, 0x03, 0x02 } },
                { ViscaPtCommand.Left, new byte[] { 0x01, 0x06, 0x01, 0x00, 0x00, 0x01, 0x03 } },
                { ViscaPtCommand.Right, new byte[] { 0x01, 0x06, 0x01, 0x00, 0x00, 0x02, 0x03 } },
                { ViscaPtCommand.UpLeft, new byte[] { 0x01, 0x06, 0x01, 0x00, 0x00, 0x01, 0x01 } },
                { ViscaPtCommand.UpRight, new byte[] { 0x01, 0x06, 0x01, 0x00, 0x00, 0x02, 0x01 } },
                { ViscaPtCommand.DownLeft, new byte[] { 0x01, 0x06, 0x01, 0x00, 0x00, 0x01, 0x02 } },
                { ViscaPtCommand.DownRight, new byte[] { 0x01, 0x06, 0x01, 0x00, 0x00, 0x02, 0x02 } },
            };
        }
        public ViscaPanTiltCommand(byte id, ViscaPtCommand ptCommand, ViscaPanTiltSpeedBehaivor viscaPanTiltBehaivor)
            : base(id, ptCommand.ToString(), _ptCommands[ptCommand], viscaPanTiltBehaivor)
        {
        }
    }

    /// <summary>
    /// Pan and Tilt commands have variabe parameters, i.e. speed.
    /// ViscaPanTiltSpeedBehaivor is required to achieve speed assigments.
    /// </summary>
    public class ViscaPanTiltPositionCommand : ViscaMessage
    {
        private static byte[] _ptPositionCommand = { 0x01, 0x06, 0x02,
                                                       0x00, 0x00, // Pand and Tilt Speeds
                                                       0x00, 0x00, 0x00, 0x00, // Pan position
                                                       0x00, 0x00, 0x00, 0x00 // Tilt Position
        };

        /// <summary>
        /// Sony VISCA pan limits are -880 to 880 (0xFC90 - 0x0370)
        /// Vaddio Roboshot VISCA pan limits are (0x90E2 - 0x6BD8)
        /// </summary>
        private uint _panLimit = 880; // 

        /// <summary>
        /// Sony VISCA tilt limits are -300 to 300 (0xFED4 - 0x012C)
        /// Vaddio Roboshot VISCA tilt limits are (0xEB99 - 0x3D59)
        /// </summary>
        private uint _tiltLimit = 300; // 

        public ViscaPanTiltPositionCommand(byte id, ViscaPanTiltSpeedBehaivor viscaPanTiltBehaivor, uint? panLimit, uint? tiltLimit)
            : base(id, "Position", _ptPositionCommand, viscaPanTiltBehaivor)
        {
            if (panLimit.HasValue)
                _panLimit = panLimit.Value;
            if (tiltLimit.HasValue)
                _tiltLimit = tiltLimit.Value;
        }

        private uint _panPosition = 0x00;
        public uint PanPosition
        {
            get { return _panPosition; }
            set
            {
                if (value < (-_panLimit) || value > _panLimit)
                    throw new ArgumentOutOfRangeException("TiltPosition", "PanPosition should be in range -" + _panLimit.ToString("X4") + " to " + _panLimit.ToString("X4"));

                _panPosition = value;
                _ptPositionCommand[5] = (byte)((value & 0xF000) >> 12);
                _ptPositionCommand[6] = (byte)((value & 0x0F00) >> 8);
                _ptPositionCommand[7] = (byte)((value & 0x00F0) >> 4);
                _ptPositionCommand[8] = (byte)((value & 0x000F));

                // Assign new message, so new hash will be calculated and command will be unique
                viscaMessage = _ptPositionCommand;
            }
        }

        private uint _tiltPosition = 0x00;
        public uint TiltPosition
        {
            get { return _tiltPosition; }
            set
            {
                if (value < (-_tiltLimit) || value > _tiltLimit)
                    throw new ArgumentOutOfRangeException("TiltPosition", "TiltPosition should be in range -" + _tiltLimit.ToString("X4") + " to " + _tiltLimit.ToString("X4"));

                _tiltPosition = value;
                _ptPositionCommand[09] = (byte)((value & 0xF000) >> 12);
                _ptPositionCommand[10] = (byte)((value & 0x0F00) >> 8);
                _ptPositionCommand[11] = (byte)((value & 0x00F0) >> 4);
                _ptPositionCommand[12] = (byte)((value & 0x000F));

                // Assign new message, so new hash will be calculated and command will be unique
                viscaMessage = _ptPositionCommand;
            }
        }
    }

    /// <summary>
    /// .
    /// </summary>
    public class ViscaZoomPositionCommand : ViscaMessage
    {
        private static byte[] _zoomPositionCommand = { 0x01, 0x04, 0x47,
                                                       0x00, 0x00, 0x00, 0x00, // Zoom position
        };

        /// <summary>
        /// Sony VISCA pan limits are not defined
        /// Vaddio Roboshot VISCA zoom limits for 12x zoom is 0x4000 and 30x zoom is 0x7C0
        /// </summary>
        private uint _zoomLimit = 0x7C0; // 

        public ViscaZoomPositionCommand(byte id, uint? zoomLimit)
            : base(id, "Zoom", _zoomPositionCommand)
        {
            if (zoomLimit.HasValue)
                _zoomLimit = zoomLimit.Value;
        }

        private uint _zoomPosition = 0x00;
        public uint ZoomPosition
        {
            get { return _zoomPosition; }
            set
            {
                if (value > _zoomLimit)
                    throw new ArgumentOutOfRangeException("ZoomPosition", "ZoomPosition should be in range 0 to " + _zoomLimit.ToString("X4"));

                _zoomPosition = value;
                _zoomPositionCommand[3] = (byte)((value & 0xF000) >> 12);
                _zoomPositionCommand[4] = (byte)((value & 0x0F00) >> 8);
                _zoomPositionCommand[5] = (byte)((value & 0x00F0) >> 4);
                _zoomPositionCommand[6] = (byte)((value & 0x000F));

                // Assign new message, so new hash will be calculated and command will be unique
                viscaMessage = _zoomPositionCommand;
            }
        }
    }

    /// <summary>
    /// .
    /// </summary>
    public class ViscaPresetCommand : ViscaMessage
    {
        private static byte[] _presetCommand = { 0x01, 0x04, 0x3F,
                                                       0x00, // 0x01 for set, 0x02 for recall,  
                                                       0x00, // preset number, 0x0 to 0xf 
        };

        public ViscaPresetCommand(byte id)
            : base(id, "Preset")
        {
        }

        public ViscaMessage Store(byte preset)
        {
            _presetCommand[3] = 0x01;
            _presetCommand[4] = (byte)(preset & 0x0F);
            return this;
        }
        public ViscaMessage Recall(byte preset)
        {
            _presetCommand[3] = 0x02;
            _presetCommand[4] = (byte) (preset & 0x0F);
            return this;
        }
    }

    public enum ViscaQuery
    {
        PowerQuery,
        FocusMode,
    }

    public abstract class ViscaQueryMessage : ViscaMessage
    {
        private static Dictionary<ViscaQuery, byte[]> _queryCommands;
        private byte _responseId;

        static ViscaQueryMessage()
        {
            _queryCommands = new Dictionary<ViscaQuery, byte[]>()
            { 
                { ViscaQuery.PowerQuery, new byte[] { 0x09, 0x04, 0x00 } },
                { ViscaQuery.FocusMode, new byte[] { 0x09, 0x04, 0x38 } },
            };
        }

        public ViscaQueryMessage(byte id, ViscaQuery queryCommand)
            : base(id, queryCommand.ToString(), _queryCommands[queryCommand])
        {
            _responseId = (byte)((id * 0x10) + 0x80);
        }

        protected Action<byte[]> messageProcessor;

        public void Process(byte[] data)
        {
            if (messageProcessor != null)
                messageProcessor(data);
        }
    }

    public class ViscaPowerQuery : ViscaQueryMessage
    {
        public ViscaPowerQuery(byte id, Action powerIsOn, Action powerIsOff) : base(id, ViscaQuery.PowerQuery)
        {
            messageProcessor = new Action<byte[]>(data => 
            {
                if (data[0] == 0x02 && powerIsOn != null)
                    powerIsOn();
                else if (data[0] == 0x03 && powerIsOff != null)
                    powerIsOff();
            });
        }
    }

    public class ViscaFocusModeQuery : ViscaQueryMessage
    {
        public ViscaFocusModeQuery(byte id, Action focusIsAuto, Action focusIsManual)
            : base(id, ViscaQuery.FocusMode)
        {
            messageProcessor = new Action<byte[]>(data =>
            {
                if (data[0] == 0x02 && focusIsAuto != null)
                    focusIsAuto();
                else if (data[0] == 0x03 && focusIsManual != null)
                    focusIsManual();
            });
        }
    }

    public class CameraVisca : CameraBase, IHasCameraPtzControl, ICommunicationMonitor, IHasCameraPresets, IHasPowerControlWithFeedback, IBridgeAdvanced, IHasCameraFocusControl, IHasAutoFocusMode, IHasCameraOff
	{
        CameraViscaPropertiesConfig PropertiesConfig;

		public IBasicCommunication Communication { get; private set; }

		public StatusMonitorBase CommunicationMonitor { get; private set; }

        /// <summary>
        /// Used to store the actions to parse inquiry responses as the inquiries are sent
        /// </summary>
        private CrestronQueue<Action<byte[]>> InquiryResponseQueue;
        private CrestronQueue<ViscaMessage> _commandQueue;
        private ViscaMessage _commandInProgress;
        private CTimer _commandInProgressTimer;
        private CrestronQueue<byte[]> _responseQueue;
        private Thread _responseParseThread;

        /// <summary>
        /// Camera and Response ID generated in constructor from config's camera ID
        /// </summary>
        public readonly byte ID = 0x80; // 0b1000_0XXX, where XXX would be added in constructor based on Camera ID
        public readonly byte ResponseID = 0x80; // 0b1XXX_0000, where XXX would be added in constructor based on Camera ID

        private readonly ViscaCommandMessage _powerOnCmd;
        private readonly ViscaCommandMessage _powerOffCmd;
        private readonly ViscaPowerQuery _powerQueryCmd;
        private readonly ViscaCommandMessage _zoomInCmd;
        private readonly ViscaCommandMessage _zoomOutCmd;
        private readonly ViscaCommandMessage _zoomStopCmd;
        private readonly ViscaCommandMessage _focusStopCmd;
        private readonly ViscaCommandMessage _focusFarCmd;
        private readonly ViscaCommandMessage _focusNearCmd;
        private readonly ViscaCommandMessage _focusTrigCmd;
        private readonly ViscaCommandMessage _focusAutoCmd;
        private readonly ViscaCommandMessage _focusManualCmd;
        private readonly ViscaCommandMessage _focusToggleCmd;
        private readonly ViscaFocusModeQuery _focusModeQueryCmd;

        private readonly ViscaCommandMessage _ptHomeCmd;
        private readonly ViscaCommandMessage _ptStopCmd;

        private readonly ViscaPanTiltSpeedBehaivor _ptSpeedBehaivor;
        private readonly ViscaPanTiltCommand _ptUpCmd;
        private readonly ViscaPanTiltCommand _ptDownCmd;
        private readonly ViscaPanTiltCommand _ptLeftCmd;
        private readonly ViscaPanTiltCommand _ptRightCmd;
        private readonly ViscaPanTiltCommand _ptUpLeftCmd;
        private readonly ViscaPanTiltCommand _ptUpRightCmd;
        private readonly ViscaPanTiltCommand _ptDownLeftCmd;
        private readonly ViscaPanTiltCommand _ptDownRightCmd;
        private readonly ViscaPanTiltPositionCommand _ptPositionCmd;

        private readonly ViscaZoomPositionCommand _zoomPositionCmd;

        private readonly ViscaPresetCommand _presetCmd;

		public byte PanSpeedSlow = 0x10;
		public byte TiltSpeedSlow = 0x10;

        public byte PanSpeedFast = 0x13;
        public byte TiltSpeedFast = 0x13;

		private bool IsMoving;
		private bool IsZooming;

        bool _powerIsOn;
		public bool PowerIsOn 
        {
            get
            {
                return _powerIsOn;
            }
            private set
            {
                if (value != _powerIsOn)
                {
                    _powerIsOn = value;
                    PowerIsOnFeedback.FireUpdate();
                    CameraIsOffFeedback.FireUpdate();
                }
            }
        }

        /// <summary>
        /// Used to determine when to move the camera at a faster speed if a direction is held
        /// </summary>
        CTimer SpeedTimer;
        // TODO: Implment speed timer for PTZ controls

        long FastSpeedHoldTimeMs = 2000;

		private byte[] _incomingBuffer = new byte[] { };
		public BoolFeedback PowerIsOnFeedback  { get; private set; }

        public CameraVisca(string key, string name, IBasicCommunication comm, CameraViscaPropertiesConfig props) :
			base(key, name)
		{
            InquiryResponseQueue = new CrestronQueue<Action<byte[]>>(15);
            _commandQueue = new CrestronQueue<ViscaMessage>(15);
            _responseQueue = new CrestronQueue<byte[]>(15);
            _responseParseThread = new Thread(parseResponse, null, Thread.eThreadStartOptions.Running);
            _commandInProgressTimer = new CTimer((o) => { _commandInProgress = null; }, Timeout.Infinite);

            Presets = props.Presets;

            PropertiesConfig = props;

            ID |= props.Id; // 0b1000_0XXX, where XXX is camera ID
            ResponseID |= (byte)(props.Id << 4); // 0b1XXX_0000, where XXX is camera ID

            _powerOnCmd = new ViscaCommandMessage(props.Id, ViscaCommand.PowerOn);
            _powerOffCmd = new ViscaCommandMessage(props.Id, ViscaCommand.PowerOff);
            _powerQueryCmd = new ViscaPowerQuery(props.Id, new Action(() => { PowerIsOn = true; }), new Action(() => { PowerIsOn = false; }));
            _zoomInCmd = new ViscaCommandMessage(props.Id, ViscaCommand.ZoomIn);
            _zoomOutCmd = new ViscaCommandMessage(props.Id, ViscaCommand.ZoomOut);
            _zoomStopCmd = new ViscaCommandMessage(props.Id, ViscaCommand.ZoomStop);
            _focusStopCmd = new ViscaCommandMessage(props.Id, ViscaCommand.FocusStop);
            _focusFarCmd = new ViscaCommandMessage(props.Id, ViscaCommand.FocusFar);
            _focusNearCmd = new ViscaCommandMessage(props.Id, ViscaCommand.FocusNear);
            _focusTrigCmd = new ViscaCommandMessage(props.Id, ViscaCommand.FocusTrig);
            _focusAutoCmd = new ViscaCommandMessage(props.Id, ViscaCommand.FocusAuto);
            _focusManualCmd = new ViscaCommandMessage(props.Id, ViscaCommand.FocusManual);
            _focusToggleCmd = new ViscaCommandMessage(props.Id, ViscaCommand.FocusToggle);
            _focusModeQueryCmd = new ViscaFocusModeQuery(props.Id, new Action(() => { FocusIsAuto = true; }), new Action(() => { FocusIsAuto = false; }));

            _ptHomeCmd = new ViscaCommandMessage(props.Id, ViscaCommand.Home);
            _ptStopCmd = new ViscaCommandMessage(props.Id, ViscaCommand.Stop);

            _ptSpeedBehaivor = new ViscaPanTiltSpeedBehaivor(PanSpeedSlow, TiltSpeedSlow);
            _ptUpCmd = new ViscaPanTiltCommand(props.Id, ViscaPtCommand.Up, _ptSpeedBehaivor);
            _ptDownCmd = new ViscaPanTiltCommand(props.Id, ViscaPtCommand.Down, _ptSpeedBehaivor);
            _ptLeftCmd = new ViscaPanTiltCommand(props.Id, ViscaPtCommand.Left, _ptSpeedBehaivor);
            _ptRightCmd = new ViscaPanTiltCommand(props.Id, ViscaPtCommand.Right, _ptSpeedBehaivor);
            _ptUpLeftCmd = new ViscaPanTiltCommand(props.Id, ViscaPtCommand.UpLeft, _ptSpeedBehaivor);
            _ptUpRightCmd = new ViscaPanTiltCommand(props.Id, ViscaPtCommand.UpRight, _ptSpeedBehaivor);
            _ptDownLeftCmd = new ViscaPanTiltCommand(props.Id, ViscaPtCommand.DownLeft, _ptSpeedBehaivor);
            _ptDownRightCmd = new ViscaPanTiltCommand(props.Id, ViscaPtCommand.DownRight, _ptSpeedBehaivor);
            _ptPositionCmd = new ViscaPanTiltPositionCommand(props.Id, _ptSpeedBehaivor, null, null);

            _zoomPositionCmd = new ViscaZoomPositionCommand(props.Id, null);

            _presetCmd = new ViscaPresetCommand(props.Id);

            SetupCameraSpeeds();

            _ptSpeedBehaivor.PanSpeed = PanSpeedSlow;
            _ptSpeedBehaivor.TiltSpeed = TiltSpeedSlow;

            OutputPorts.Add(new RoutingOutputPort("videoOut", eRoutingSignalType.Video, eRoutingPortConnectionType.None, null, this, true));

            // Default to all capabilties
            Capabilities = eCameraCapabilities.Pan | eCameraCapabilities.Tilt | eCameraCapabilities.Zoom | eCameraCapabilities.Focus; 
            
            Communication = comm;
			var socket = comm as ISocketStatus;
			if (socket != null)
			{
				// This instance uses IP control
				socket.ConnectionChange += new EventHandler<GenericSocketStatusChageEventArgs>(socket_ConnectionChange);
			}
			else
			{
				// This instance uses RS-232 control
			}

			Communication.BytesReceived += new EventHandler<GenericCommMethodReceiveBytesArgs>(Communication_BytesReceived);
			PowerIsOnFeedback = new BoolFeedback(() => { return PowerIsOn; });
            CameraIsOffFeedback = new BoolFeedback(() => { return !PowerIsOn; });

			if (props.CommunicationMonitorProperties != null)
			{
				CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, props.CommunicationMonitorProperties);
			}
			else
			{
				CommunicationMonitor = new GenericCommunicationMonitor(this, Communication, 20000, 120000, 300000, new Action(()=>
                    {
                        enqueueCommand(_powerQueryCmd);
                        enqueueCommand(_focusModeQueryCmd);
                    }
                ));
			}
			DeviceManager.AddDevice(CommunicationMonitor);
		}


        /// <summary>
        /// Sets up camera speed values based on config
        /// </summary>
        void SetupCameraSpeeds()
        {
            if (PropertiesConfig.FastSpeedHoldTimeMs > 0)
            {
                FastSpeedHoldTimeMs = PropertiesConfig.FastSpeedHoldTimeMs;
            }

            if (PropertiesConfig.PanSpeedSlow > 0)
            {
                PanSpeedSlow = (byte)PropertiesConfig.PanSpeedSlow;
            }
            if (PropertiesConfig.PanSpeedFast > 0)
            {
                PanSpeedFast = (byte)PropertiesConfig.PanSpeedFast;
            }

            if (PropertiesConfig.TiltSpeedSlow > 0)
            {
                TiltSpeedSlow = (byte)PropertiesConfig.TiltSpeedSlow;
            }
            if (PropertiesConfig.TiltSpeedFast > 0)
            {
                TiltSpeedFast = (byte)PropertiesConfig.TiltSpeedFast;
            }
        }

		public override bool CustomActivate()
		{
			Communication.Connect();

			
			CommunicationMonitor.StatusChange += (o, a) => { Debug.Console(2, this, "Communication monitor state: {0}", CommunicationMonitor.Status); };
			CommunicationMonitor.Start();


			CrestronConsole.AddNewConsoleCommand(s => Communication.Connect(), "con" + Key, "", ConsoleAccessLevelEnum.AccessOperator);
			return true;
		}

	    public void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
	    {
	        LinkCameraToApi(this, trilist, joinStart, joinMapKey, bridge);
	    }

	    void socket_ConnectionChange(object sender, GenericSocketStatusChageEventArgs e)
		{
			Debug.Console(2, this, "Socket Status Change: {0}", e.Client.ClientStatus.ToString());

			if (e.Client.IsConnected)
			{
				
			}
			else
			{

			}
		}

        /// <summary>
        /// Enqueue command to send to device
        /// </summary>
        /// <param name="commandToEnqueue">ViscaCommand</param>
        private void enqueueCommand(ViscaMessage commandToEnqueue)
        {
            Debug.Console(1, this, "Enqueueing command '{0}'. CommandQueue Size: '{1}'", commandToEnqueue.ToString(), _commandQueue.Count);
            // check for existing command in the Queue
            bool commandIsEnqueued = false;
            foreach (var command in _commandQueue)
            {
                //if (command.Equals(commandToEnqueue))
                if (command == commandToEnqueue)
                {
                    commandIsEnqueued = true;
                    break;
                }
            }

            if (commandIsEnqueued)
                Debug.Console(1, this, "Enqueueing command '{0}' is duplicate, skipping. CommandQueue Size: '{1}'", commandToEnqueue.ToString(), _commandQueue.Count);
            else
                _commandQueue.Enqueue(commandToEnqueue);
            //Debug.Console(1, this, "Command (QueuedCommand) Enqueued '{0}'. CommandQueue Size: '{1}'", commandToEnqueue.Command, CommandQueue.Count);

            if (_commandInProgress == null && _responseQueue.IsEmpty)
                sendNextQueuedCommand();
        }

        /// <summary>
        /// Sends the next queued command to the device
        /// </summary>
        private void sendNextQueuedCommand()
        {
            if (Communication.IsConnected && !_commandQueue.IsEmpty)
            {
                _commandInProgress = _commandQueue.Dequeue();
                Debug.Console(1, this, "Command '{0}' Dequeued. CommandQueue Size: {1}", _commandInProgress.ToString(), _commandQueue.Count);
                // start the timer to expire current command in case of no response
                _commandInProgressTimer.Reset(1000);
                sendBytes(_commandInProgress);
            }
        }

        /// <summary>
        /// Sends raw bytes to the device
        /// </summary>
        /// <param name="b">bytes to send</param>
		private void sendBytes(byte[] b)
		{
			
			if (Debug.Level == 2) // This check is here to prevent following string format from building unnecessarily on level 0 or 1
				Debug.Console(2, this, "Sending:{0}", ComTextHelper.GetEscapedText(b));

			Communication.SendBytes(b);
		}

        /// <summary>
        /// Recieve response message from device and queue for processing
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
		void Communication_BytesReceived(object sender, GenericCommMethodReceiveBytesArgs args)
		{
            var newBytes = new byte[_incomingBuffer.Length + args.Bytes.Length];

            try
            {
                // This is probably not thread-safe buffering
                // Append the incoming bytes with whatever is in the buffer
                _incomingBuffer.CopyTo(newBytes, 0);
                args.Bytes.CopyTo(newBytes, _incomingBuffer.Length);
                if (Debug.Level == 2) // This check is here to prevent following string format from building unnecessarily on level 0 or 1
                    Debug.Console(2, this, "Received:{0}", ComTextHelper.GetEscapedText(newBytes));

                // Search for the delimiter 0xFF character
                int idxStart = 0;
                for (int i = 0; i < newBytes.Length; i++)
                {
                    if (newBytes[i] == 0xFF)
                    {
                        // Skip Address byte, i will be the index of the delmiter character
                        byte[] message = new byte[i - 1 - idxStart];
                        Array.Copy(newBytes, idxStart + 1, message, 0, i - 1 - idxStart);
                        _responseQueue.Enqueue(message);

                        // move start position to next element
                        idxStart = i + 1;
                    }
                }
                // Skip over delimmiter and save the rest for next time
                newBytes = newBytes.Skip(idxStart + 1).ToArray();

            }
            catch (Exception err)
            {
                Debug.Console(2, this, "Error parsing feedback for messages: {0}", err);
            }
            finally
            {
                // Save whatever partial message is here
                _incomingBuffer = newBytes;
            }
        }

        /// <summary>
        /// Handles a response message from the device
        /// </summary>
        /// <param name="obj"></param>
        private object parseResponse(object obj)
        {
            while (true)
            {
                try
                {
                    byte[] message = _responseQueue.Dequeue();
                    if (Debug.Level == 2) // This check is here to prevent following string format from building unnecessarily on level 0 or 1
                        Debug.Console(2, this, "Response '{0}' Dequeued. ResponseQueue Size: {1}", ComTextHelper.GetEscapedText(message), _responseQueue.Count);

                    if (message == null)
                    {
                        Debug.Console(2, this, "Exception in parseResponse thread, deque byte array is null");
                        return null;
                    }

                    // 0x40 or 0x41 are ACK messages - command recieved
                    if (message.Length == 1 && (message[0] == 0x40 || message[0] == 0x41))
                        continue;

                    if (_commandInProgress == null)
                    {
                        /// response is not associated with any particular command
                        Debug.Console(2, this, "Collision, response for command not in progress");
                    }
                    else
                    {
                        _commandInProgressTimer.Stop();
                        // 0x50 and 0x51 are command completion responses
                        if (message.Length == 1 && (message[0] == 0x50 || message[0] == 0x51))
                        {
                            if(!(_commandInProgress is ViscaCommandMessage))
                                Debug.Console(2, this, "Collision, completion message is not for Command type message");

                        }
                        else if (message.Length == 2 && (message[0] == 0x60 || message[0] == 0x61))
                        {
                            // Error message
                            switch (message[1])
                            {
                                case 0x01:
                                    // Message Length Error
                                    Debug.Console(2, this, "Error from device: Message Length Error");
                                    break;
                                case 0x02:
                                    // Syntax Error
                                    Debug.Console(2, this, "Error from device: Syntax Error");
                                    break;
                                case 0x03:
                                    // Command Buffer Full
                                    Debug.Console(2, this, "Error from device: Command Buffer Full");
                                    break;
                                case 0x04:
                                    // Command Cancelled
                                    Debug.Console(2, this, "Error from device: Command Cancelled");
                                    break;
                                case 0x05:
                                    // No Socket
                                    Debug.Console(2, this, "Error from device: No Socket");
                                    break;
                                case 0x41:
                                    // Command not executable
                                    Debug.Console(2, this, "Error from device: Command not executable");
                                    break;
                            }
                        }
                        else
                        {
                            // we have pending clearance command in progress, use it's processing hook
                            var query = _commandInProgress as ViscaQueryMessage;
                            if(query != null)
                                query.Process(message);
                            else
                                Debug.Console(2, this, "Collision, expecting ViscaQueryMessage type as command in progress");
                        }
                        Debug.Console(2, this, "Completing command in progress: '{0}'", _commandInProgress.ToString());
                        _commandInProgress = null;
                    }
                    
                }
                catch (Exception e)
                {
                    Debug.Console(2, this, "Exception in parseResponse thread: '{0}'\n{1}", e.Message, e.StackTrace);
                }

                if (!_commandQueue.IsEmpty && _responseQueue.IsEmpty)
                    sendNextQueuedCommand();
            } // while(true)
        }

		public void PowerOn()
		{
            enqueueCommand(_powerOnCmd);
            enqueueCommand(_powerQueryCmd);
		}

		public void PowerOff()
		{
            enqueueCommand(_powerOffCmd);
            enqueueCommand(_powerQueryCmd);
        }

        public void PowerToggle()
        {
            if (PowerIsOnFeedback.BoolValue)
                PowerOff();
            else
                PowerOn();
        }

		public void PanLeft() 
		{
            enqueueCommand(_ptLeftCmd);
			IsMoving = true;
		}
		public void PanRight() 
		{
            enqueueCommand(_ptRightCmd);
            IsMoving = true;
		}
        public void PanStop()
        {
            Stop();
        }
		public void TiltDown() 
		{
            enqueueCommand(_ptDownCmd);
            IsMoving = true;
		}
		public void TiltUp() 
		{
            enqueueCommand(_ptUpCmd);
            IsMoving = true;
		}
        public void TiltStop()
        {
            Stop();
        }

        public void Stop()
        {
            stopSpeedTimer();
            enqueueCommand(_ptStopCmd);
            IsMoving = false;
        }

        /// <summary>
        /// Starts Press & Hold timer, upon expiry, increase speed to Fast.
        /// </summary>
        private void startPressHoldSpeedTimer()
        {
            if (SpeedTimer != null)
            {
                stopSpeedTimer();
            }

            // Start the timer to set fast speed if still moving after FastSpeedHoldTime elapses
            SpeedTimer = new CTimer(
                (o) => { _ptSpeedBehaivor.PanSpeed = PanSpeedFast; _ptSpeedBehaivor.TiltSpeed = TiltSpeedFast; },
                FastSpeedHoldTimeMs
            );
        }


        private void stopSpeedTimer()
        {
            if (SpeedTimer != null)
            {
                SpeedTimer.Stop();
                SpeedTimer.Dispose();
                SpeedTimer = null;
            }
        }

		public void ZoomIn() 
		{
            enqueueCommand(_zoomInCmd);
		}
		public void ZoomOut() 
		{
            enqueueCommand(_zoomOutCmd);
		}
        public void ZoomStop()
        {
            enqueueCommand(_zoomStopCmd);
        }

        public void PositionHome()
        {
            if (PropertiesConfig.HomeCmdSupport)
            {
                enqueueCommand(_ptHomeCmd);

            }
            else
            {
                enqueueCommand(_ptPositionCmd);
                enqueueCommand(_zoomPositionCmd);
            }
        }
		public void RecallPreset(int presetNumber)
		{
            enqueueCommand(_presetCmd.Recall((byte)presetNumber));
		}
		public void SavePreset(int presetNumber)
		{
            enqueueCommand(_presetCmd.Store((byte)presetNumber));
        }

        #region IHasCameraPresets Members

        public event EventHandler<EventArgs> PresetsListHasChanged;

        public List<CameraPreset> Presets { get; private set; }

        public void PresetSelect(int preset)
        {
            RecallPreset(preset);
        }

        public void PresetStore(int preset, string description)
        {
            SavePreset(preset);
        }

        #endregion

        #region IHasCameraFocusControl Members

        public void FocusNear()
        {
            enqueueCommand(_focusNearCmd);
        }

        public void FocusFar()
        {
            enqueueCommand(_focusFarCmd);
        }

        public void FocusStop()
        {
            enqueueCommand(_focusStopCmd);
        }

        public void TriggerAutoFocus()
        {
            enqueueCommand(_focusTrigCmd);
            enqueueCommand(_focusModeQueryCmd);
        }

        #endregion

        #region IHasAutoFocus Members

        public void SetFocusModeAuto()
        {
            enqueueCommand(_focusAutoCmd);
            enqueueCommand(_focusModeQueryCmd);
        }

        public void SetFocusModeManual()
        {
            enqueueCommand(_focusManualCmd);
            enqueueCommand(_focusModeQueryCmd);
        }

        public void ToggleFocusMode()
        {
            enqueueCommand(_focusToggleCmd);
            enqueueCommand(_focusModeQueryCmd);
        }

        private bool focusIsAuto;
        public bool FocusIsAuto
        {
            get { return focusIsAuto; }
            set
            {
                if (value != focusIsAuto)
                {
                    focusIsAuto = value;
                    // TODO: Feedback?
                }
            }
        }

        #endregion

        #region IHasCameraOff Members

        public BoolFeedback CameraIsOffFeedback { get; private set; }


        public void CameraOff()
        {
            PowerOff();
        }

        #endregion
    }

    public class CameraViscaFactory : EssentialsDeviceFactory<CameraVisca>
    {
        public CameraViscaFactory()
        {
            TypeNames = new List<string>() { "cameravisca" };
        }

        public override EssentialsDevice BuildDevice(DeviceConfig dc)
        {
            IList<string> deserializeErrorMessages = new List<string>();

            Debug.Console(1, "Factory Attempting to create new CameraVisca Device");
            var props = Newtonsoft.Json.JsonConvert.DeserializeObject<Cameras.CameraViscaPropertiesConfig>(
                dc.Properties.ToString(), new JsonSerializerSettings() {
                 Error = delegate (object sender, Newtonsoft.Json.Serialization.ErrorEventArgs args)
                    {
                        deserializeErrorMessages.Add(args.ErrorContext.Error.Message);
                        args.ErrorContext.Handled = true;
                    }
                });
            if (deserializeErrorMessages.Count > 0)
            {
                Debug.Console(0, "Factory Attempting to create new CameraVisca Device failed on parsing config: {0}", String.Join("\r\n", deserializeErrorMessages.ToArray()));
                return null;
            }

            var comm = CommFactory.CreateCommForDevice(dc);
            return new Cameras.CameraVisca(dc.Key, dc.Name, comm, props);
        }
    }


    public class CameraViscaPropertiesConfig : CameraPropertiesConfig
    {
        /// <summary>
        /// Control ID of the camera (1-7)
        /// </summary>
        [JsonProperty("id")]
        public byte Id
        {
            get 
            {
                return id;
            }
            set
            {
                if(value > 0 && value < 8)
                    id = value;
                else
                    throw(new ArgumentOutOfRangeException("Id", "Camera ID should be in range between 1 to 7"));
            }
        }
        private byte id;

        /// <summary>
        /// Home VISCA command support. Not all cameras support this command, if not supported, absolute position will be used.
        /// </summary>
        [JsonProperty("homeCmdSupport")]
        public bool HomeCmdSupport { get; set; }


        /// <summary>
        /// Slow Pan speed (0-18)
        /// </summary>
        [JsonProperty("panSpeedSlow")]
        public byte PanSpeedSlow { get; set; }

        /// <summary>
        /// Fast Pan speed (0-18)
        /// </summary>
        [JsonProperty("panSpeedFast")]
        public byte PanSpeedFast { get; set; }

        /// <summary>
        /// Slow tilt speed (0-18)
        /// </summary>
        [JsonProperty("tiltSpeedSlow")] 
        public byte TiltSpeedSlow { get; set; }

        /// <summary>
        /// Fast tilt speed (0-18)
        /// </summary>
        [JsonProperty("tiltSpeedFast")]
        public byte TiltSpeedFast { get; set; }

        /// <summary>
        /// Time a button must be held before fast speed is engaged (Milliseconds)
        /// </summary>
        [JsonProperty("fastSpeedHoldTimeMs")]
        public byte FastSpeedHoldTimeMs { get; set; }

    }

}