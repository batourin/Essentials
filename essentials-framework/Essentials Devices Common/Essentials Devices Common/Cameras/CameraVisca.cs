using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.Config;
using PepperDash.Essentials.Devices.Common.Codec;
using System.Text.RegularExpressions;
using Crestron.SimplSharp.Reflection;

using Newtonsoft.Json;

namespace PepperDash.Essentials.Devices.Common.Cameras
{
	public class CameraVisca : CameraBase, IHasCameraPtzControl, ICommunicationMonitor, IHasCameraPresets, IHasPowerControlWithFeedback, IBridgeAdvanced, IHasCameraFocusControl, IHasAutoFocusMode, IHasCameraOff
	{
        CameraViscaPropertiesConfig PropertiesConfig;

		public IBasicCommunication Communication { get; private set; }

		public StatusMonitorBase CommunicationMonitor { get; private set; }

        /// <summary>
        /// Used to store the actions to parse inquiry responses as the inquiries are sent
        /// </summary>
        private CrestronQueue<Action<byte[]>> InquiryResponseQueue;

        /// <summary>
        /// Camera and Response ID generated in constructor from config's camera ID
        /// </summary>
        public readonly byte ID = 0x80; // 0b1000_0XXX, where XXX would be added in constructor based on Camera ID
        public readonly byte ResponseID = 0x80; // 0b1XXX_0000, where XXX would be added in constructor based on Camera ID

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

        const byte ZoomInCmd = 0x02;
        const byte ZoomOutCmd = 0x03;
        const byte ZoomStopCmd = 0x00;

        /// <summary>
        /// Used to determine when to move the camera at a faster speed if a direction is held
        /// </summary>
        CTimer SpeedTimer;
        // TODO: Implment speed timer for PTZ controls

        long FastSpeedHoldTimeMs = 2000;

		byte[] IncomingBuffer = new byte[] { };
		public BoolFeedback PowerIsOnFeedback  { get; private set; }

        public CameraVisca(string key, string name, IBasicCommunication comm, CameraViscaPropertiesConfig props) :
			base(key, name)
		{
            InquiryResponseQueue = new CrestronQueue<Action<byte[]>>(15);

            Presets = props.Presets;

            PropertiesConfig = props;

            ID |= props.Id; // 0b1000_0XXX, where XXX is camera ID
            ResponseID |= (byte)(props.Id << 4); // 0b1XXX_0000, where XXX is camera ID

            SetupCameraSpeeds();

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
                        SendPowerQuery();
                        SendAutoFocusQuery();
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

		void SendBytes(byte[] b)
		{
			
			if (Debug.Level == 2) // This check is here to prevent following string format from building unnecessarily on level 0 or 1
				Debug.Console(2, this, "Sending:{0}", ComTextHelper.GetEscapedText(b));

			Communication.SendBytes(b);
		}

		void Communication_BytesReceived(object sender, GenericCommMethodReceiveBytesArgs e)
		{
            var newBytes = new byte[IncomingBuffer.Length + e.Bytes.Length];

            try
            {
                // This is probably not thread-safe buffering
                // Append the incoming bytes with whatever is in the buffer
                IncomingBuffer.CopyTo(newBytes, 0);
                e.Bytes.CopyTo(newBytes, IncomingBuffer.Length);
                if (Debug.Level == 2) // This check is here to prevent following string format from building unnecessarily on level 0 or 1
                    Debug.Console(2, this, "Received:{0}", ComTextHelper.GetEscapedText(newBytes));

                byte[] message = null;

                // Search for the delimiter 0xFF character
                for (int i = 0; i < newBytes.Length; i++)
                {
                    if (newBytes[i] == 0xFF)
                    {
                        // Check that message is for us
                        if (newBytes[0] == ResponseID)
                        {
                            // Skip Address byte, i will be the index of the delmiter character
                            //message = newBytes.Skip(1).Take(i - 1).ToArray(); // LINQ is slow, replace it with Copy operation
                            message = new byte[i - 1];
                            Array.Copy(newBytes, 1, message, 0, i - 1);
                        }
                        // Skip over delimmiter and save the rest for next time
                        newBytes = newBytes.Skip(i+1).ToArray();
                    }
                }

                //Check for matching ID
                if (message != null && message.Length > 0)
                {
                    switch (message[0])
                    {
                        case 0x40:
                            {
                                // ACK received
                                Debug.Console(2, this, "ACK Received");
                                break;
                            }
                        case 0x50: // socket 0
                        case 0x51: // socket 1
                            {

                                if (message.Length == 1)
                                {
                                    // Completion received
                                    Debug.Console(2, this, "Completion Received");
                                }
                                else
                                {
                                    // Inquiry response received.  Dequeue the next response handler and invoke it
                                    if (InquiryResponseQueue.Count > 0)
                                    {
                                        var inquiryAction = InquiryResponseQueue.Dequeue();
                                        // send VISCA message body only
                                        inquiryAction.Invoke(message.Skip(1).ToArray());
                                    }
                                    else
                                    {
                                        Debug.Console(2, this, "Response Queue is empty. Nothing to dequeue.");
                                    }
                                }

                                break;
                            }
                        case 0x60: // socket 0
                        case 0x61: // socket 1
                            {
                                // Error message

                                switch (message[1])
                                {
                                    case 0x01:
                                        {
                                            // Message Length Error
                                            Debug.Console(2, this, "Error from device: Message Length Error");
                                            break;
                                        }
                                    case 0x02:
                                        {
                                            // Syntax Error
                                            Debug.Console(2, this, "Error from device: Syntax Error");
                                            break;
                                        }
                                    case 0x03:
                                        {
                                            // Command Buffer Full
                                            Debug.Console(2, this, "Error from device: Command Buffer Full");
                                            break;
                                        }
                                    case 0x04:
                                        {
                                            // Command Cancelled
                                            Debug.Console(2, this, "Error from device: Command Cancelled");
                                            break;
                                        }
                                    case 0x05:
                                        {
                                            // No Socket
                                            Debug.Console(2, this, "Error from device: No Socket");
                                            break;
                                        }
                                    case 0x41:
                                        {
                                            // Command not executable
                                            Debug.Console(2, this, "Error from device: Command not executable");
                                            break;
                                        }
                                }
                                break;
                            }
                    }
                } // Message 

            }
            catch (Exception err)
            {
                Debug.Console(2, this, "Error parsing feedback: {0}", err);
            }
            finally
            {
                // Save whatever partial message is here
                IncomingBuffer = newBytes;
            }
        }

        /// <summary>
        /// Sends a pan/tilt command. If the command is not for fastSpeed then it starts a timer to initiate fast speed.
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="fastSpeed"></param>
		private void SendPanTiltCommand (byte[] cmd, bool fastSpeedEnabled)
		{
            SendBytes(GetPanTiltCommand(cmd, fastSpeedEnabled));

            if (!fastSpeedEnabled)
            {
                if (SpeedTimer != null)
                {
                    StopSpeedTimer();
                }

                // Start the timer to send fast speed if still moving after FastSpeedHoldTime elapses
                SpeedTimer = new CTimer((o) => SendPanTiltCommand(GetPanTiltCommand(cmd, true), true), FastSpeedHoldTimeMs);
            }

		}

        private void StopSpeedTimer()
        {
            if (SpeedTimer != null)
            {
                SpeedTimer.Stop();
                SpeedTimer.Dispose();
                SpeedTimer = null;
            }     
        }

        /// <summary>
        /// Generates the pan/tilt command with either slow or fast speed
        /// </summary>
        /// <param name="cmd"></param>
        /// <param name="fastSpeed"></param>
        /// <returns></returns>
        private byte[] GetPanTiltCommand(byte[] cmd, bool fastSpeed)
        {
            byte panSpeed;
            byte tiltSpeed;

            if (!fastSpeed)
            {
                panSpeed = PanSpeedSlow;
                tiltSpeed = TiltSpeedSlow;
            }
            else
            {
                panSpeed = PanSpeedFast;
                tiltSpeed = TiltSpeedFast;
            }

            var temp = new byte[] { ID, 0x01, 0x06, 0x01, panSpeed, tiltSpeed };
            int length = temp.Length + cmd.Length + 1;

            byte[] sum = new byte[length];
            temp.CopyTo(sum, 0);
            cmd.CopyTo(sum, temp.Length);
            sum[length - 1] = 0xFF;

            return sum;
        }


        void SendPowerQuery()
        {
            SendBytes(new byte[] { ID, 0x09, 0x04, 0x00, 0xFF });
            InquiryResponseQueue.Enqueue(HandlePowerResponse);
        }

		public void PowerOn()
		{
			SendBytes(new byte[] { ID, 0x01, 0x04, 0x00, 0x02, 0xFF });
            SendPowerQuery();
		}

        void HandlePowerResponse(byte[] response)
        {
            switch (response[0])
            {
                case 0x02:
                    {
                        PowerIsOn = true;
                        break;
                    }
                case 0x03:
                    {
                        PowerIsOn = false;
                        break;
                    }
            }
        }

		public void PowerOff()
		{
			SendBytes(new byte[] {ID, 0x01, 0x04, 0x00, 0x03, 0xFF});
            SendPowerQuery();
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
			SendPanTiltCommand(new byte[] {0x01, 0x03}, false);
			IsMoving = true;
		}
		public void PanRight() 
		{
            SendPanTiltCommand(new byte[] { 0x02, 0x03 }, false);
			IsMoving = true;
		}
        public void PanStop()
        {
            Stop();
        }
		public void TiltDown() 
		{
            SendPanTiltCommand(new byte[] { 0x03, 0x02 }, false);
			IsMoving = true;
		}
		public void TiltUp() 
		{
            SendPanTiltCommand(new byte[] { 0x03, 0x01 }, false);
			IsMoving = true;
		}
        public void TiltStop()
        {
            Stop();
        }

		private void SendZoomCommand (byte cmd)
		{
			SendBytes(new byte[] {ID, 0x01, 0x04, 0x07, cmd, 0xFF} );
		}


		public void ZoomIn() 
		{
            SendZoomCommand(ZoomInCmd);
			IsZooming = true;
		}
		public void ZoomOut() 
		{
            SendZoomCommand(ZoomOutCmd);
			IsZooming = true;
		}
        public void ZoomStop()
        {
            Stop();
        }

		public void Stop() 
		{
			if (IsZooming)
			{
                SendZoomCommand(ZoomStopCmd);
				IsZooming = false;
			}
			else
			{
                StopSpeedTimer();
                SendPanTiltCommand(new byte[] { 0x03, 0x03 }, false);
				IsMoving = false;
			}
		}
        public void PositionHome()
        {
            SendBytes(new byte[] { ID, 0x01, 0x06, 0x02, PanSpeedFast, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF });
            SendBytes(new byte[] { ID, 0x01, 0x04, 0x47, 0x00, 0x00, 0x00, 0x00, 0xFF });
        }
		public void RecallPreset(int presetNumber)
		{
			SendBytes(new byte[] {ID, 0x01, 0x04, 0x3F, 0x02, (byte)presetNumber, 0xFF} );
		}
		public void SavePreset(int presetNumber)
		{
			SendBytes(new byte[] { ID, 0x01, 0x04, 0x3F, 0x01, (byte)presetNumber, 0xFF });
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
            SendBytes(new byte[] { ID, 0x01, 0x04, 0x08, 0x03, 0xFF });
        }

        public void FocusFar()
        {
            SendBytes(new byte[] { ID, 0x01, 0x04, 0x08, 0x02, 0xFF });
        }

        public void FocusStop()
        {
            SendBytes(new byte[] { ID, 0x01, 0x04, 0x08, 0x00, 0xFF });
        }

        public void TriggerAutoFocus()
        {
            SendBytes(new byte[] { ID, 0x01, 0x04, 0x18, 0x01, 0xFF });
            SendAutoFocusQuery();
        }

        #endregion

        #region IHasAutoFocus Members

        public void SetFocusModeAuto()
        {
            SendBytes(new byte[] { ID, 0x01, 0x04, 0x38, 0x02, 0xFF });
            SendAutoFocusQuery();
        }

        public void SetFocusModeManual()
        {
            SendBytes(new byte[] { ID, 0x01, 0x04, 0x38, 0x03, 0xFF });
            SendAutoFocusQuery();
        }

        public void ToggleFocusMode()
        {
            SendBytes(new byte[] { ID, 0x01, 0x04, 0x38, 0x10, 0xFF });
            SendAutoFocusQuery();
        }

        void SendAutoFocusQuery()
        {
            SendBytes(new byte[] { ID, 0x09, 0x04, 0x38, 0xFF });
            InquiryResponseQueue.Enqueue(HandleAutoFocusResponse);
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

        void HandleAutoFocusResponse(byte[] response)
        {
            switch (response[0])
            {
                case 0x02:
                    {
                        // Auto Mode
                        FocusIsAuto = true;
                        break;
                    }
                case 0x03:
                    {
                        // Manual Mode
                        FocusIsAuto = false;
                        break;
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