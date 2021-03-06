﻿using GreenhouseController.StateMachines;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GreenhouseController
{
    public class ArduinoControlSender
    {
        // TODO: make this use a queue and event-based!
        // Constants for setting up serial ports
        private const int _BAUD = 9600;
        private const Parity _PARITY = Parity.None;
        private const int _DATABITS = 8;
        private const StopBits _STOPBITS = StopBits.One;

        // Communications elements
        private SerialPort _output;
        private byte[] _ACK = new byte[] { 0x5C };
        private byte[] _NACK = new byte[] { 0x56 };
        private bool _success = false;
        private int _retryCount = 0;

        // Singleton pattern items
        private static volatile ArduinoControlSender _instance;
        private static object _syncRoot = new object();
        
        /// <summary>
        /// Empty constructor
        /// </summary>
        private ArduinoControlSender()
        { }

        /// <summary>
        /// Singleton pattern field
        /// </summary>
        public static ArduinoControlSender Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_syncRoot)
                    {
                        if (_instance == null)
                        {
                            _instance = new ArduinoControlSender();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Tries to open a serial port on the ports available to the device.
        /// If the port is already open, it checks to make sure that the's still communications
        /// available on the port.
        /// </summary>
        public void TryConnect()
        {
            // TODO: loop through to find serial ports, and establish the fact we're connected
            // We might need to start an external script to do this properly in linux,
            // SerialPort.GetPortNames() only ever returns ttyS0
            // Find ports
            string[] ports = SerialPort.GetPortNames();
            foreach(string port in ports)
            {
                Console.WriteLine($"{port} available.");
            }

            // Create the serial port
            if (_output == null)
            {
                _output = new SerialPort("/dev/ttyACM0", _BAUD, _PARITY, _DATABITS, _STOPBITS);
                //_output = new SerialPort("COM3", _BAUD, _PARITY, _DATABITS, _STOPBITS);
            }

            // Open the serial port
            if (_output.IsOpen != true)
            {
                _output.Open();
                Thread.Sleep(2000);
                _output.ReadTimeout = 500;
                _output.RtsEnable = true;
            }
            // TODO: add task down here to periodically poll for the arduino to make sure everything is okay
        }

        /// <summary>
        /// Takes a Key-Value pair of TemperatureStateMachine and the state we're going to, then sends the required commands
        /// </summary>
        /// <param name="statePair">KeyValuePair of temperature state machine and the state it needs to go to</param>
        public void SendCommand(KeyValuePair<IStateMachine, GreenhouseState> statePair)
        {
            // TODO: add correct sequencing of commands
            byte[] buffer = new byte[1];
            List<Commands> commandsToSend = new List<Commands>();
            
            commandsToSend = statePair.Key.ConvertStateToCommands(statePair.Value);
            
            foreach (var command in commandsToSend)
            {
                // Send commands
                try
                {
                    Console.WriteLine($"Attempting to send command {command}");
                    _output.Write(command.ToString());
                    Thread.Sleep(1250);
                    Console.WriteLine("Send finished.");

                    // Change states based on the key/value pair we passed in
                    statePair.Key.CurrentState = GreenhouseState.WAITING_FOR_RESPONSE;

                    if (command == Commands.SHADE_EXTEND || command == Commands.SHADE_RETRACT)
                    {
                        _output.ReadTimeout = 10000;
                    }
                    else
                    {
                        _output.ReadTimeout = 500;
                    }
                    // Wait for response
                    Console.WriteLine($"Waiting for response...");
                    _output.Read(buffer, 0, buffer.Length);
                    Console.WriteLine($"{buffer.GetValue(0)} received.");
                    
                    //buffer = _ACK;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                // Check the response from the Arduino
                // ACK = success
                if (buffer.SequenceEqual(_ACK))
                {
                    Console.WriteLine($"Command {command} sent successfully");
                    _success = true;
                }
                // NACK = command wasn't acknowledged
                else if (buffer.SequenceEqual(_NACK) || buffer == null)
                {
                    Console.WriteLine($"Command {command} returned unsuccessful response, attempting to resend.");

                    // Attempt to resend the command 5 more times
                    while(_retryCount != 5 && _success == false)
                    {
                        // Try-catch so thread doesn't explode if it fails to send/receive
                        try
                        {
                            Console.WriteLine("Retrying send...");
                            _output.Write(command.ToString());
                            Thread.Sleep(1250);
                            Console.WriteLine("Awaiting response...");
                            
                            //_output.Read(buffer, 0, buffer.Length);
                            Console.WriteLine($"{buffer.GetValue(0)} received");
                            //buffer = ACK;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message, "Retrying again...");
                        }
                        
                        // If we succeeded this time, break out of the loop!
                        if (buffer.SequenceEqual(_ACK))
                        {
                            Console.WriteLine($"Command {command} sent successfully.");
                            _success = true;
                        }
                        // If not, we keep going!
                        else if (buffer.SequenceEqual(_NACK) || buffer.SequenceEqual(null) && _retryCount != 5)
                        {
                            Console.WriteLine("Retrying again...");
                            _retryCount++;
                        }
                    }
                }

                // Change state based on results of sending commands
                // If we never successfully sent the command on retries, we go to the error state
                if (_success == false)
                {
                    statePair.Key.CurrentState = GreenhouseState.ERROR;
                }
                // If the command WAS sent successfully, we set the state accordingly and proceed as normal.
                else if (_success == true)
                {
                    statePair.Key.CurrentState = statePair.Value;
                    Console.WriteLine($"State change {statePair.Value} executed successfully\n");
                }
                _retryCount = 0;
                _success = false;
            }
        }

        /// <summary>
        /// Takes a Key-Value pair of WateringStateMachine and the state we're going to, then sends the required commands
        /// </summary>
        /// <param name="statePair">KeyValuePair of temperature state machine and the state it needs to go to</param>
        public void SendCommand(KeyValuePair<ITimeBasedStateMachine, GreenhouseState> statePair)
        {
            // TODO: add correct sequencing of commands
            byte[] buffer = new byte[1];
            List<Commands> commandsToSend = new List<Commands>();
            commandsToSend = statePair.Key.ConvertStateToCommands(statePair.Value);

            foreach (var command in commandsToSend)
            {
                // Send commands
                try
                {
                    Console.WriteLine($"Attempting to send command {command}");
                    _output.Write(command.ToString());
                    Thread.Sleep(1250);
                    Console.WriteLine("Send finished.");

                    // Change states based on the key/value pair we passed in
                    statePair.Key.CurrentState = GreenhouseState.WAITING_FOR_RESPONSE;

                    // Wait for response
                    Console.WriteLine($"Waiting for response...");
                    _output.Read(buffer, 0, buffer.Length);
                    Console.WriteLine($"{buffer.GetValue(0)} received.");

                    //buffer = _ACK;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                // Check the response from the Arduino
                // ACK = success
                if (buffer.SequenceEqual(_ACK))
                {
                    Console.WriteLine($"Command {command} sent successfully");
                    _success = true;
                }
                // NACK = command wasn't acknowledged
                else if (buffer.SequenceEqual(_NACK) || buffer == null)
                {
                    Console.WriteLine($"Command {command} returned unsuccessful response, attempting to resend.");

                    // Attempt to resend the command 5 more times
                    while (_retryCount != 5 && _success == false)
                    {
                        // Try-catch so thread doesn't explode if it fails to send/receive
                        try
                        {
                            Console.WriteLine("Retrying send...");
                            _output.Write(command.ToString());
                            Thread.Sleep(1250);
                            Console.WriteLine("Awaiting response...");

                            _output.Read(buffer, 0, buffer.Length);
                            Console.WriteLine($"{buffer.GetValue(0)} received");
                            //buffer = ACK;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message, "Retrying again...");
                        }

                        // If we succeeded this time, break out of the loop!
                        if (buffer.SequenceEqual(_ACK))
                        {
                            Console.WriteLine($"Command {command} sent successfully.");
                            _success = true;
                        }
                        // If not, we keep going!
                        else if (buffer.SequenceEqual(_NACK) || buffer.SequenceEqual(null) && _retryCount != 5)
                        {
                            Console.WriteLine("Retrying again...");
                            _retryCount++;
                        }
                    }
                }

                // Change state based on results of sending commands
                // If we never successfully sent the command on retries, we go to the error state
                if (_success == false)
                {
                    statePair.Key.CurrentState = GreenhouseState.ERROR;
                }
                // If the command WAS sent successfully, we set the state accordingly and proceed as normal.
                else if (_success == true)
                {
                    statePair.Key.CurrentState = statePair.Value;
                    Console.WriteLine($"State change {statePair.Value} executed successfully\n");
                }
                _retryCount = 0;
                _success = false;
            }
        }

        /// <summary>
        /// Send command to turn off manual control of a statemachine
        /// </summary>
        /// <param name="stateMachine">State machine to set back on automated control</param>
        //public void SendManualOffCommand(IStateMachine stateMachine)
        //{
        //    // TODO: implement this with a key/value pair like above. Can probably reduce the commands
        //    // we need to send that way.
        //    // TODO: Account for multiple zones!
        //    byte[] buffer = new byte[1];
        //    List<Commands> commandsToSend = new List<Commands>();

        //    // Get the commands we need to send to turn the manual control off
        //    if (stateMachine is TemperatureStateMachine)
        //    {
        //        if (stateMachine.CurrentState == GreenhouseState.HEATING)
        //        {
        //            stateMachine.CurrentState = GreenhouseState.PROCESSING_HEATING;
        //            commandsToSend.Add(Commands.HEAT_OFF);
        //        }
        //        else if (stateMachine.CurrentState == GreenhouseState.COOLING)
        //        {
        //            stateMachine.CurrentState = GreenhouseState.PROCESSING_COOLING;
        //            commandsToSend.Add(Commands.FANS_OFF);
        //            commandsToSend.Add(Commands.VENT_CLOSE);
        //            commandsToSend.Add(Commands.SHADE_RETRACT);
        //        }
        //    }
        //    else if (stateMachine is ShadingStateMachine)
        //    {
        //        stateMachine.CurrentState = GreenhouseState.PROCESSING_SHADING;
        //        commandsToSend.Add(Commands.SHADE_RETRACT);
        //    }

        //    foreach (var command in commandsToSend)
        //    {
        //        // Try to send command to turn off heater, watering, lighting etc.
        //        try
        //        {
        //            Console.WriteLine($"Attempting to send command {command}");
        //            _output.Write(command.ToString());
        //            Thread.Sleep(1250);
        //            Console.WriteLine("Send finished.");

        //            Console.WriteLine($"Waiting for response...");
        //            _output.Read(buffer, 0, buffer.Length);
        //            Console.WriteLine($"{buffer.GetValue(0)} received.");
        //            //buffer = NACK;
        //        }
        //        catch (Exception ex)
        //        {
        //            Console.WriteLine(ex);
        //        }

        //        // ACK = successfully executed the command to the best of the Arduino's knowledge
        //        if (buffer.SequenceEqual(_ACK))
        //        {
        //            Console.WriteLine($"Command {command} sent successfully");
        //            _success = true;
        //        }
        //        // NACK = couldn't successfully execute the command, so we retry
        //        else if (buffer.SequenceEqual(_NACK) || buffer.SequenceEqual(null))
        //        {
        //            Console.WriteLine($"Command {command} sent unsuccessfully, attempting to resend.");

        //            // Attempt to resend the command 5 more times
        //            while (_retryCount != 5 && _success == false)
        //            {
        //                // Try-catch so thread doesn't explode if it fails to send/receive
        //                try
        //                {
        //                    Console.WriteLine("Retrying send...");
        //                    _output.Write(command.ToString());
        //                    Thread.Sleep(1250);
        //                    Console.WriteLine("Awaiting response...");

        //                    Console.WriteLine("Awaiting response...");
        //                    _output.Read(buffer, 0, buffer.Length);
        //                    Console.WriteLine($"{buffer.GetValue(0)} received");
        //                    //buffer = ACK;
        //                }
        //                catch (Exception ex)
        //                {
        //                    Console.WriteLine(ex.Message, "Retrying again...");
        //                }

        //                // If we succeeded this time, break out of the loop!
        //                if (buffer.SequenceEqual(_ACK))
        //                {
        //                    Console.WriteLine($"Command {command} sent successfully.");
        //                    _success = true;
        //                }
        //                // If we don't succeed, try again!
        //                else if (buffer.SequenceEqual(_NACK) || buffer.SequenceEqual(null) && _retryCount != 5)
        //                {
        //                    Console.WriteLine("Retrying again...");
        //                    _retryCount++;
        //                }
        //            }
        //        }

        //        // Change state based on results of sending commands
        //        if (_success == false)
        //        {
        //            stateMachine.CurrentState = GreenhouseState.ERROR;
        //        }
        //        else if (_success == true)
        //        {
        //            stateMachine.CurrentState = GreenhouseState.WAITING_FOR_DATA;
        //            Console.WriteLine($"State change {GreenhouseState.WAITING_FOR_DATA} executed successfully\n");
        //        }
        //        _retryCount = 0;
        //        _success = false;
        //    }
        //}

        //public void SendManualOffCommand(ITimeBasedStateMachine stateMachine)
        //{
        //    // TODO: Account for multiple zones!
        //    // TODO: Remove all traces of temperature stuff, replace with lighting and watering stuff!
        //    byte[] buffer = new byte[1];
        //    List<Commands> commandsToSend = new List<Commands>();

        //    // Get the commands we need to send to turn the manual control off
        //    if (stateMachine is LightingStateMachine)
        //    {
        //        stateMachine.CurrentState = GreenhouseState.PROCESSING_LIGHTING;
        //        commandsToSend = stateMachine.ConvertStateToCommands(GreenhouseState.WAITING_FOR_DATA);
        //    }
        //    else if (stateMachine is WateringStateMachine)
        //    {
        //        stateMachine.CurrentState = GreenhouseState.PROCESSING_WATER;
        //        commandsToSend = stateMachine.ConvertStateToCommands(GreenhouseState.WAITING_FOR_DATA);
        //    }

        //    foreach (var command in commandsToSend)
        //    {
        //        // Try to send command to turn off watering or lighting
        //        try
        //        {
        //            Console.WriteLine($"Attempting to send command {command}");
        //            _output.Write(command.ToString());
        //            Thread.Sleep(1250);
        //            Console.WriteLine("Send finished.");

        //            Console.WriteLine($"Waiting for response...");
        //            _output.Read(buffer, 0, buffer.Length);
        //            Console.WriteLine($"{buffer.GetValue(0)} received.");
        //            //buffer = NACK;
        //        }
        //        catch (Exception ex)
        //        {
        //            Console.WriteLine(ex);
        //        }

        //        // ACK = successfully executed the command to the best of the Arduino's knowledge
        //        if (buffer.SequenceEqual(_ACK))
        //        {
        //            Console.WriteLine($"Command {command} sent successfully");

        //            _success = true;
        //        }
        //        // NACK = couldn't successfully execute the command, so we retry
        //        else if (buffer.SequenceEqual(_NACK) || buffer.SequenceEqual(null))
        //        {
        //            Console.WriteLine($"Command {command} sent unsuccessfully, attempting to resend.");

        //            // Attempt to resend the command 5 more times
        //            while (_retryCount != 5 && _success == false)
        //            {
        //                // Try-catch so thread doesn't explode if it fails to send/receive
        //                try
        //                {
        //                    Console.WriteLine("Retrying send...");
        //                    _output.Write(command.ToString());
        //                    Thread.Sleep(1250);
        //                    Console.WriteLine("Awaiting response...");

        //                    Console.WriteLine("Awaiting response...");
        //                    _output.Read(buffer, 0, buffer.Length);
        //                    Console.WriteLine($"{buffer.GetValue(0)} received");
        //                    //buffer = ACK;
        //                }
        //                catch (Exception ex)
        //                {
        //                    Console.WriteLine(ex.Message, "Retrying again...");
        //                }

        //                // If we succeeded this time, break out of the loop!
        //                if (buffer.SequenceEqual(_ACK))
        //                {
        //                    Console.WriteLine($"Command {command} sent successfully.");
        //                    _success = true;
        //                }
        //                // If we don't succeed, try again!
        //                else if (buffer.SequenceEqual(_NACK) || buffer.SequenceEqual(null) && _retryCount != 5)
        //                {
        //                    Console.WriteLine("Retrying again...");
        //                    _retryCount++;
        //                }
        //            }
        //        }

        //        // Change state based on results of sending commands
        //        if (_success == false)
        //        {
        //            stateMachine.CurrentState = GreenhouseState.ERROR;
        //            Console.WriteLine($"State change {GreenhouseState.WAITING_FOR_DATA} unsucessful.");
        //        }
        //        else if (_success == true)
        //        {
        //            stateMachine.CurrentState = GreenhouseState.WAITING_FOR_DATA;
        //            Console.WriteLine($"State change {GreenhouseState.WAITING_FOR_DATA} executed successfully\n");
        //        }
        //        _retryCount = 0;
        //        _success = false;
        //    }
        //}
    }
}
