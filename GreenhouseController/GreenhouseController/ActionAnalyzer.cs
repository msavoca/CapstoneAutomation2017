﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GreenhouseController
{
    public class ActionAnalyzer
    {
        private double _avgTemp;
        private double _avgHumid;
        private double _avgLight;
        private double _avgMoisture;
        private DateTime _currentTime;
        private int[] _tempLimits;
        private int _lightLimit;
        private int _moistureLimit;
        private bool _manualHeat;
        private bool _manualCool;
        private bool _manualLight;
        private bool _manualWater;

        public ActionAnalyzer()
        {
            _avgTemp = new double();
            _avgLight = new double();
            _avgHumid = new double();
            _avgMoisture = new double();
            _tempLimits = new int[2];
            _lightLimit = new int();
            _moistureLimit = new int();
            _currentTime = DateTime.Now;
        }

        /// <summary>
        /// Processes the data received from the packets
        /// </summary>
        /// <param name="data">Array of Packet objects parsed from JSON sent via data server</param>
        public void AnalyzeData(DataPacket[] data)
        {
            foreach (var packet in data)
            {
                if (packet.manualCool)
                {
                    _manualCool = packet.manualCool;
                }
                else if (packet.manualHeat)
                {
                    _manualHeat = packet.manualHeat;
                }
                else if (packet.manualLight)
                {
                    _manualLight = packet.manualLight;
                }
                else if (packet.manualWater)
                {
                    _manualWater = packet.manualWater;
                }
            }

            // Check if the manual flags are set
            if (!_manualCool || !_manualHeat || !_manualLight || !_manualWater)
            {
                // Get the averages of greenhouse readings
                GetGreenhouseAverages(data);
                Console.WriteLine($"Time: {_currentTime}\nAverage Temperature: {_avgTemp}\nAverage Humidity: {_avgHumid}\nAverage Light Intensity: {_avgLight}\nAverage Soil Moisture: {_avgMoisture}\n");

                // Get the limits we're comparing to
                GetGreenhouseLimits(data);

                // Get Temperature state machine state
                StateMachineController.Instance.DetermineTemperatureState(_avgTemp, _tempLimits[0], _tempLimits[1]);
                StateMachineController.Instance.DetermineLightingState(_avgLight, _lightLimit);
                StateMachineController.Instance.DetermineWateringState(_avgMoisture, _moistureLimit);

                // Send commands
                using (ArduinoControlSender sender = new ArduinoControlSender())
                {
                    if ((StateMachineController.Instance.GetTemperatureEndState() == GreenhouseState.COOLING || StateMachineController.Instance.GetTemperatureEndState() == GreenhouseState.HEATING)
                        && StateMachineController.Instance.GetTemperatureCurrentState() != GreenhouseState.EMERGENCY)
                    {
                        sender.SendCommand(StateMachineController.Instance.GetTemperatureMachine());
                    }
                    if (StateMachineController.Instance.GetLightingEndState() == GreenhouseState.LIGHTING)
                    {
                        sender.SendCommand(StateMachineController.Instance.GetLightingMachine());
                    }
                    if (StateMachineController.Instance.GetWateringEndState() == GreenhouseState.WATERING && StateMachineController.Instance.GetWateringCurrentState() != GreenhouseState.EMERGENCY)
                    {
                        sender.SendCommand(StateMachineController.Instance.GetWateringMachine());
                    }
                }

                if (StateMachineController.Instance.GetWateringCurrentState() == GreenhouseState.EMERGENCY)
                {
                    // Send an emergency message to the Data Team!
                }
                if (StateMachineController.Instance.GetTemperatureCurrentState() == GreenhouseState.EMERGENCY)
                {
                    // Send an emergency message to the Data Team!
                }
            }
            // If manual flags are set....
            else
            {
                // Override stuff
            }
        }

        /// <summary>
        /// Helper method for averaging greenhouse data
        /// </summary>
        /// <param name="data">Array of Packet objects parsed from JSON sent via data server</param>
        private void GetGreenhouseAverages(DataPacket[] data)
        {
            foreach (DataPacket pack in data)
            {
                _avgTemp += pack.temperature;
                _avgHumid += pack.humidity;
                _avgLight += pack.light;
                _avgMoisture += pack.moisture;
            }
            _avgTemp /= 5;
            _avgHumid /= 5;
            _avgLight /= 5;
            _avgMoisture /= 5;
        }

        /// <summary>
        /// Helper method to get the greenhouse limits from packets
        /// </summary>
        private void GetGreenhouseLimits(DataPacket[] packet)
        {
            // TODO: get light and humidity
            foreach (DataPacket pack in packet)
            {
                if (_tempLimits[0] != pack.tempHi)
                {
                    _tempLimits[0] = pack.tempHi;
                }
                if (_tempLimits[1] != pack.tempLo)
                {
                    _tempLimits[1] = pack.tempLo;
                }
                if (_lightLimit != pack.lightLim)
                {
                    _lightLimit = pack.lightLim;
                }
                if (_moistureLimit != pack.moistLim)
                {
                    _moistureLimit = pack.moistLim;
                }
            }
        }
    }
}
