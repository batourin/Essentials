﻿using Crestron.SimplSharpPro;
using Crestron.SimplSharpPro.DeviceSupport;
using Crestron.SimplSharpPro.GeneralIO;
using Newtonsoft.Json;
using PepperDash.Core;
using PepperDash.Essentials.Core.Bridges;

namespace PepperDash.Essentials.Core.CrestronIO
{
    public class C2nRthsController:CrestronGenericBridgeableBaseDevice
    {
        private readonly C2nRths _device;

        public IntFeedback TemperatureFeedback { get; private set; }
        public IntFeedback HumidityFeedback { get; private set; }

        public C2nRthsController(string key, string name, GenericBase hardware) : base(key, name, hardware)
        {
            _device = hardware as C2nRths;

            TemperatureFeedback = new IntFeedback(() => _device.TemperatureFeedback.UShortValue);
            HumidityFeedback = new IntFeedback(() => _device.HumidityFeedback.UShortValue);

            if (_device != null) _device.BaseEvent += DeviceOnBaseEvent;
        }

        private void DeviceOnBaseEvent(GenericBase device, BaseEventArgs args)
        {
            switch (args.EventId)
            {
                case C2nRths.TemperatureFeedbackEventId:
                    TemperatureFeedback.FireUpdate();
                    break;
                case C2nRths.HumidityFeedbackEventId:
                    HumidityFeedback.FireUpdate();
                    break;
            }
        }

        public void SetTemperatureFormat(bool setToC)
        {
            _device.TemperatureFormat.BoolValue = setToC;
        }

        public override void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApi bridge)
        {
            var joinMap = new C2nRthsControllerJoinMap();

            var joinMapSerialized = JoinMapHelper.GetSerializedJoinMapForDevice(joinMapKey);

            if (!string.IsNullOrEmpty(joinMapSerialized))
                joinMap = JsonConvert.DeserializeObject<C2nRthsControllerJoinMap>(joinMapSerialized);

            joinMap.OffsetJoinNumbers(joinStart);

            Debug.Console(1, this, "Linking to Trilist '{0}'", trilist.ID.ToString("X"));


            trilist.SetBoolSigAction(joinMap.TemperatureFormat, SetTemperatureFormat);

            IsOnline.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline]);
            TemperatureFeedback.LinkInputSig(trilist.UShortInput[joinMap.Temperature]);
            HumidityFeedback.LinkInputSig(trilist.UShortInput[joinMap.Humidity]);

            trilist.StringInput[joinMap.Name].StringValue = Name;
        }
    }
}