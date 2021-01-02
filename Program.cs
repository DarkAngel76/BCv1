using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Device.Gpio;
using Unosquare.RaspberryIO.Abstractions;
using Unosquare.RaspberryIO;
using Unosquare.WiringPi;
using Unosquare.RaspberryIO.Peripherals;

namespace BCv1
{
    class Program
    {
        #region Variables & Constants & Dictionaries
        private static readonly Dictionary<ConsoleKey, string> MainOptions = new Dictionary<ConsoleKey, string>
        {
            // Module Control Items
            { ConsoleKey.D, "Display" },
            { ConsoleKey.D1, "Test Valve 1" },
            { ConsoleKey.D2, "Test Valve 2" },
            { ConsoleKey.S, "Show Clock" },
        };

        private static readonly Dictionary<int, string> Programs = new Dictionary<int, string>
        {
            // Module Control Items
            { 0, "Fixed Time" },
            { 1, "Dynamic close" },
            { 2, "Fixed Toggle" },
            { 3, "Dynamic Toggle" },
            { 4, "Rndm O/C/T fixed" },
            { 5, "Rndm O/C/T dynamic" },
            { 6, "Triggered" },
        };

        private enum ToggleMode
        {
            All,
            Toggle,
            Random
        }

        private static Display _display = new Display();
        private static System.Device.Gpio.GpioController _iocontroller = new System.Device.Gpio.GpioController();
        private const Int32 RESET_PIN = 4;
        private const Int32 REL_CH1_PIN = 20;
        private const Int32 REL_CH2_PIN = 21;
        private const BcmPin TA_START_STOP_PIN = BcmPin.Gpio25;
        private const BcmPin TA_SELECT_PIN = BcmPin.Gpio12;
        private const BcmPin TA_UP_PIN = BcmPin.Gpio16;
        private const BcmPin TA_DOWN_PIN = BcmPin.Gpio26;
        private const BcmPin BEEPER = BcmPin.Gpio17;
        private static Button? _btnStartStop;
        private static Button? _btnSelectProg;
        private static Button? _btnUp;
        private static Button? _btnDown;
        private static bool _progRunning = false;
        private static int _prog = -1;
        private static string _displayLine1 = "";
        private static string _displayLine2 = "";
        private static string _displayLine3 = "";
        private static string _displayLine4 = "";
        private static string _displayLine1Running = "";
        private static string _displayLine2Running = "";
        private static string _displayLine3Running = "";
        private static string _displayLine4Running = "";
        private static int _baseTime = 10;
        private static int _stepTime = 5;
        private static int _dynamicTime = 5;
        private static int _maxTime = 30;
        private static CancellationTokenSource cts = new CancellationTokenSource();
        private static int _time = 5;
        private static bool _toggle = true;
        private static bool _isClosed = false;
        private static DateTime _start;

        #endregion

        static void Main(string[] args)
        {
            Pi.Init<BootstrapWiringPi>();

            InitGpio();
            InitDisplay();

            _displayLine1 = "Please select";
            _displayLine2 = "Program to use";
            _displayLine3 = "";
            _displayLine4 = "";
            UpdateDisplay();

            Debug.WriteLine("Init complete");

            try
            {
                while (true)
                {
                    var input = Console.ReadKey(true).Key;

                    if (input != ConsoleKey.Escape) continue;

                    break;
                }
            }
            catch (Exception ex)
            {
                _display.ClearDisplayBuf();
                _display.WriteLineDisplayBuf("Error", 0, 0);
                _display.DisplayUpdate();
                Debug.WriteLine(ex.Message.ToString());
                cts.Cancel();
            }
            finally
            {
                AllValvesOpen();
                _display.ClearDisplayBuf();
                _display.WriteLineDisplayBuf("Goodbye", 0, 0);
                _display.DisplayUpdate();
                cts.Cancel();
                cts.Dispose();
            }
        }

        private static void RunProgram(object obj)
        {
            CancellationToken token = (CancellationToken)obj;
            int i = 0;
            Task<bool> DoBeep;
            AutoResetEvent autoEvent;
            Timer stateTimer;
            int _counter = 0;

            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    AllValvesOpen();
                    break;
                }

                switch (_prog)
                {
                    case 0:
                        // Fixed Time O/C
                        Console.WriteLine("{0:h:mm:ss.fff} Creating timer.", DateTime.Now);
                        _toggle = true;
                        Toggle(ToggleMode.All);

                        autoEvent = new AutoResetEvent(false);
                        stateTimer = new Timer(ValesProgAll, autoEvent, (_baseTime * 1000), 10);

                        while (true)
                        {
                            if (token.IsCancellationRequested)
                            {
                                _toggle = false;
                                AllValvesOpen();
                                break;
                            }

                            autoEvent.WaitOne();

                            if (!_isClosed)
                            {
                                DoBeep = Beep(_baseTime - 2);
                                stateTimer.Change((_baseTime * 1000), 10);
                            }
                            else
                            {
                                stateTimer.Change((_baseTime * 1000), 10);
                            }
                        }

                        stateTimer.Dispose();
                        autoEvent.Dispose();
                        Console.WriteLine("{0:h:mm:ss.fff} Destroying timer.", DateTime.Now);
                        break;
                    case 1:
                        // Dynamic close
                        Console.WriteLine("{0:h:mm:ss.fff} Creating timer.", DateTime.Now);
                        _toggle = true;
                        Toggle(ToggleMode.All);

                        autoEvent = new AutoResetEvent(false);
                        _time = _dynamicTime;
                        stateTimer = new Timer(ValesProgAll, autoEvent, (_time * 1000), 10);
                        _time += _stepTime;

                        while (true)
                        {
                            if (token.IsCancellationRequested)
                            {
                                AllValvesOpen();
                                break;
                            }
                            _counter++;
                            if (_counter > 1)
                            {
                                if (token.IsCancellationRequested)
                                {
                                    _toggle = false;
                                    Toggle(ToggleMode.All);
                                    break;
                                }

                                Console.WriteLine("Pause");

                                if (_isClosed)
                                {
                                    _toggle = false;
                                    Toggle(ToggleMode.All);
                                }

                                DoBeep = Beep(_baseTime - 2);
                                stateTimer.Change(_baseTime * 1000, 10);
                                autoEvent.WaitOne();

                                Console.WriteLine("Restart");
                                stateTimer.Change((_dynamicTime * 1000), 10);
                            }

                            while (true)
                            {
                                if (token.IsCancellationRequested)
                                {
                                    AllValvesOpen();
                                    break;
                                }

                                autoEvent.WaitOne();

                                if (_time <= _maxTime)
                                {
                                    if (!_isClosed)
                                    {
                                        DoBeep = Beep(_baseTime - 2);
                                        stateTimer.Change((_baseTime * 1000), 10);
                                    }
                                    else
                                    {
                                        stateTimer.Change((_time * 1000), 10);
                                        _time += _stepTime;
                                    }
                                }
                                else
                                {
                                    if (_time > _maxTime)
                                    {
                                        _time = _dynamicTime + _stepTime;
                                        break;
                                    }
                                }
                            }
                        }

                        stateTimer.Dispose();
                        autoEvent.Dispose();
                        Console.WriteLine("{0:h:mm:ss.fff} Destroying timer.", DateTime.Now);
                        break;
                    case 2:
                        // Fixed Toggle
                        Console.WriteLine("{0:h:mm:ss.fff} Creating timer.", DateTime.Now);
                        _toggle = true;
                        Toggle(ToggleMode.Toggle);

                        autoEvent = new AutoResetEvent(false);
                        stateTimer = new Timer(ValesProgToggle, autoEvent, (_baseTime * 1000), 10);

                        while (true)
                        {
                            if (token.IsCancellationRequested)
                            {
                                _toggle = false;
                                AllValvesOpen();
                                break;
                            }

                            autoEvent.WaitOne();

                            if (!_isClosed)
                            {
                                DoBeep = Beep(_baseTime - 2);
                                stateTimer.Change((_baseTime * 1000), 10);
                            }
                            else
                            {
                                stateTimer.Change((_baseTime * 1000), 10);
                            }
                        }

                        stateTimer.Dispose();
                        autoEvent.Dispose();
                        Console.WriteLine("{0:h:mm:ss.fff} Destroying timer.", DateTime.Now);
                        break;
                    case 3:
                        // Dynamic Toggle
                        Console.WriteLine("{0:h:mm:ss.fff} Creating timer.", DateTime.Now);
                        _toggle = true;
                        Toggle(ToggleMode.Toggle);

                        autoEvent = new AutoResetEvent(false);
                        _time = _dynamicTime;
                        stateTimer = new Timer(ValesProgToggle, autoEvent, (_time * 1000), 10);
                        _time += _stepTime;

                        while (true)
                        {
                            if (token.IsCancellationRequested)
                            {
                                _toggle = false;
                                Toggle(ToggleMode.Toggle);
                                break;
                            }
                            _counter++;
                            if (_counter > 1)
                            {
                                if (token.IsCancellationRequested)
                                {
                                    _toggle = false;
                                    Toggle(ToggleMode.Toggle);
                                    break;
                                }

                                Console.WriteLine("Pause");

                                if (_isClosed)
                                {
                                    _toggle = false;
                                    Toggle(ToggleMode.Toggle);
                                }

                                DoBeep = Beep(_baseTime - 2);
                                stateTimer.Change(_baseTime * 1000, 10);
                                autoEvent.WaitOne();

                                Console.WriteLine("Restart");
                                stateTimer.Change((_dynamicTime * 1000), 10);
                            }

                            while (true)
                            {
                                if (token.IsCancellationRequested)
                                {
                                    _toggle = false;
                                    Toggle(ToggleMode.Toggle);
                                    break;
                                }

                                autoEvent.WaitOne();

                                if (_time <= _maxTime)
                                {
                                    if (!_isClosed)
                                    {
                                        DoBeep = Beep(_baseTime - 2);
                                        stateTimer.Change((_baseTime * 1000), 10);
                                    }
                                    else
                                    {
                                        stateTimer.Change((_time * 1000), 10);
                                        _time += _stepTime;
                                    }
                                }
                                else
                                {
                                    if (_time > _maxTime)
                                    {
                                        _time = _dynamicTime + _stepTime;
                                        break;
                                    }
                                }
                            }
                        }

                        stateTimer.Dispose();
                        autoEvent.Dispose();
                        Console.WriteLine("{0:h:mm:ss.fff} Destroying timer.", DateTime.Now);
                        break;
                    case 4:
                        // Random O/C/T
                        Console.WriteLine("{0:h:mm:ss.fff} Creating timer.", DateTime.Now);
                        _toggle = true;
                        Toggle(ToggleMode.Random);
                        _time = _baseTime * _dynamicTime;

                        autoEvent = new AutoResetEvent(false);
                        stateTimer = new Timer(ValesProgRandom, autoEvent, (_time * 1000), 10);

                        while (true)
                        {
                            if (token.IsCancellationRequested)
                            {
                                _toggle = false;
                                AllValvesOpen();
                                break;
                            }

                            autoEvent.WaitOne();

                            if (!_isClosed)
                            {
                                DoBeep = Beep(_time - 2);
                                stateTimer.Change((_time * 1000), 10);
                            }
                            else
                            {
                                stateTimer.Change((_time * 1000), 10);
                            }
                        }

                        stateTimer.Dispose();
                        autoEvent.Dispose();
                        Console.WriteLine("{0:h:mm:ss.fff} Destroying timer.", DateTime.Now);
                        break;
                    case 5:
                        // Random O/C/T random duration
                        Random rand = new Random();

                        Console.WriteLine("{0:h:mm:ss.fff} Creating timer.", DateTime.Now);
                        _toggle = true;
                        Toggle(ToggleMode.Random);

                        autoEvent = new AutoResetEvent(false);
                        _time = _baseTime * rand.Next(1, _dynamicTime);

                        //DoBeep = Beep(_time - 2);
                        stateTimer = new Timer(ValesProgRandom, autoEvent, (_time * 1000), 10);

                        while (true)
                        {
                            if (token.IsCancellationRequested)
                            {
                                _toggle = false;
                                Toggle(ToggleMode.All);
                                break;
                            }

                            autoEvent.WaitOne();

                            _time = _baseTime * rand.Next(1, _dynamicTime);
                            if (!_isClosed)
                            {
                                DoBeep = Beep(_time - 2);
                                stateTimer.Change((_time * 1000), 10);
                            }
                            else
                            {
                                stateTimer.Change((_time * 1000), 10);
                            }
                        }

                        stateTimer.Dispose();
                        autoEvent.Dispose();
                        Console.WriteLine("{0:h:mm:ss.fff} Destroying timer.", DateTime.Now);
                        break;
                    case 6:
                        _displayLine2 = "Not yet";
                        _displayLine3 = "implemented";
                        UpdateDisplay();
                        break;
                }
            }

        }

        #region GPIO Handling
        private static void InitGpio()
        {
            try
            {
                _iocontroller.OpenPin(RESET_PIN, PinMode.Output);
                _iocontroller.OpenPin(REL_CH1_PIN, PinMode.Output);
                _iocontroller.OpenPin(REL_CH2_PIN, PinMode.Output);

                _btnStartStop = new Button(Pi.Gpio[TA_START_STOP_PIN], GpioPinResistorPullMode.PullUp);
                _btnStartStop.Pressed += _btnStartStop_Pressed;
                _btnSelectProg = new Button(Pi.Gpio[TA_SELECT_PIN], GpioPinResistorPullMode.PullUp);
                _btnSelectProg.Pressed += _btnSelectProg_Pressed;
                _btnUp = new Button(Pi.Gpio[TA_UP_PIN], GpioPinResistorPullMode.PullUp);
                _btnUp.Pressed += _btnUp_Pressed;
                _btnDown = new Button(Pi.Gpio[TA_DOWN_PIN], GpioPinResistorPullMode.PullUp);
                _btnDown.Pressed += _btnDown_Pressed;
            }
            /* If initialization fails, throw an exception */
            catch (Exception ex)
            {
                throw new Exception("GPIO initialization failed", ex);
            }
        }

        private static void _btnDown_Pressed(object sender, EventArgs e)
        {
            if (!_progRunning)
            {
                switch (_prog)
                {
                    case 0:
                        if (_baseTime > _dynamicTime) _baseTime -= _stepTime;
                        _displayLine2 = "O/C Time " + _baseTime.ToString() + "s";
                        break;
                    case 1:
                        if (_dynamicTime > _baseTime) _dynamicTime -= _stepTime;
                        _displayLine3 = "BC Time " + _dynamicTime.ToString() + "s";
                        break;
                    case 2:
                        if (_baseTime > _dynamicTime) _baseTime -= _stepTime;
                        _displayLine2 = "Interval " + _baseTime.ToString() + "s";
                        break;
                    case 3:
                        if (_dynamicTime > _baseTime) _dynamicTime -= _stepTime;
                        _displayLine3 = "BC Time " + _dynamicTime.ToString() + "s";
                        break;
                    case 4:
                    case 5:
                        if (_dynamicTime > 1) _dynamicTime -= _stepTime;
                        _displayLine3 = "Factor " + _dynamicTime.ToString();
                        _displayLine4 = "Max " + (_baseTime * _dynamicTime).ToString() + "s";
                        break;
                }

                UpdateDisplay();
            }
        }

        private static void _btnUp_Pressed(object sender, EventArgs e)
        {
            if (!_progRunning)
            {
                switch (_prog)
                {
                    case 0:
                        if (_baseTime < _maxTime) _baseTime += _stepTime;
                        _displayLine2 = "O/C Time " + _baseTime.ToString() + "s";
                        break;
                    case 1:
                        if (_dynamicTime < 45) _dynamicTime += _stepTime;
                        _displayLine3 = "BC Time " + _dynamicTime.ToString() + "s";
                        break;
                    case 2:
                        if (_baseTime < _maxTime) _baseTime += _stepTime;
                        _displayLine2 = "Interval " + _baseTime.ToString() + "s";
                        break;
                    case 3:
                        if (_dynamicTime < 30) _dynamicTime += _stepTime;
                        _displayLine3 = "BC Time " + _dynamicTime.ToString() + "s";
                        break;
                    case 4:
                    case 5:
                        if (_dynamicTime < 6) _dynamicTime += _stepTime;
                        _displayLine3 = "Factor " + _dynamicTime.ToString();
                        _displayLine4 = "Max " + (_baseTime * _dynamicTime).ToString() + "s";
                        break;
                }

                UpdateDisplay();
            }
        }

        private static void _btnSelectProg_Pressed(object sender, EventArgs e)
        {
            if (!_progRunning)
            {
                _prog++;
                if (_prog > 5) _prog = 0;

                _displayLine1 = Programs[_prog];

                switch (_prog)
                {
                    case 0:
                        _baseTime = 10;
                        _stepTime = 5;
                        _dynamicTime = 5;
                        _maxTime = 60;
                        _displayLine2 = "O/C Time " + _baseTime.ToString() + "s";
                        _displayLine3 = "";
                        _displayLine4 = "Max " + _maxTime.ToString() + "s";
                        break;
                    case 1:
                        _baseTime = 15;
                        _stepTime = 1;
                        _dynamicTime = 10;
                        _maxTime = 45;
                        _displayLine2 = "15/20s O, C +1";
                        _displayLine3 = "BC Time " + _dynamicTime.ToString() + "s";
                        _displayLine4 = "Max " + _maxTime.ToString() + "s";
                        break;
                    case 2:
                        _baseTime = 10;
                        _stepTime = 1;
                        _dynamicTime = 5;
                        _maxTime = 45;
                        _displayLine2 = "Interval " + _baseTime.ToString() + "s";
                        _displayLine3 = "";
                        _displayLine4 = "Max " + _maxTime.ToString() + "s";
                        break;
                    case 3:
                        _baseTime = 10;
                        _stepTime = 1;
                        _dynamicTime = 10;
                        _maxTime = 45;
                        _displayLine2 = "T 15/20s O, C +1";
                        _displayLine3 = "BC Time " + _dynamicTime.ToString() + "s";
                        _displayLine4 = "Max " + _maxTime.ToString() + "s";
                        break;
                    case 4:
                    case 5:
                        _baseTime = 5;
                        _stepTime = 1;
                        _dynamicTime = 1;
                        _maxTime = 60;
                        _displayLine2 = "Base " + _baseTime.ToString() + "s";
                        _displayLine3 = "Factor " + _dynamicTime.ToString();
                        _displayLine4 = "Max " + (_baseTime * _dynamicTime).ToString() + "s";
                        break;
                    case 6:
                        _baseTime = 10;
                        _stepTime = 5;
                        _dynamicTime = 5;
                        _maxTime = 30;
                        _displayLine2 = "No params";
                        _displayLine3 = "available";
                        _displayLine4 = "";
                        break;
                }

                UpdateDisplay();
            }
        }

        private static void _btnStartStop_Pressed(object sender, EventArgs e)
        {
            if (!_progRunning)
            {
                //Start Async Prog
                _displayLine1Running = _displayLine1;
                _displayLine2Running = _displayLine2;
                _displayLine3Running = _displayLine3;
                _displayLine4Running = _displayLine4;

                _displayLine2 = "Ok, Let's GO";
                _displayLine3 = "Get prepared for";
                _displayLine4 = "10s";

                UpdateDisplay();

                _displayLine2 = "";

                Task<bool> DoBeep = Beep(10 - 2);
                for (int i = 0; i < 10; i++)
                {
                    Thread.Sleep(1000);
                    _displayLine4 = (10 - i).ToString() + "s";
                    UpdateDisplay();
                }

                ThreadPool.QueueUserWorkItem(new WaitCallback(RunProgram), cts.Token);
                Thread.Sleep(1000);
                Console.WriteLine("Program { 0 } started", _prog.ToString());

                _progRunning = true;
            }
            else
            {
                cts.Cancel();
                cts = new CancellationTokenSource();

                Console.WriteLine("Program { 0 } stopped", _prog.ToString());
                Thread.Sleep(1000);

                _displayLine1 = _displayLine1Running;
                _displayLine2 = _displayLine2Running;
                _displayLine3 = _displayLine3Running;
                _displayLine4 = _displayLine4Running;
                UpdateDisplay();

                _progRunning = false;
            }
        }

        private static void Valve1Close() => _iocontroller.Write(REL_CH1_PIN, PinValue.High);

        private static void Valve2Close() => _iocontroller.Write(REL_CH2_PIN, PinValue.High);

        private static void Valve1Open() => _iocontroller.Write(REL_CH1_PIN, PinValue.Low);

        private static void Valve2Open() => _iocontroller.Write(REL_CH2_PIN, PinValue.Low);

        private static void ValesProgAll(Object stateInfo)
        {
            AutoResetEvent autoEvent = (AutoResetEvent)stateInfo;
            Toggle(ToggleMode.All);
            autoEvent.Set();
        }

        private static void ValesProgToggle(Object stateInfo)
        {
            AutoResetEvent autoEvent = (AutoResetEvent)stateInfo;
            Toggle(ToggleMode.Toggle);
            autoEvent.Set();
        }

        private static void ValesProgRandom(Object stateInfo)
        {
            AutoResetEvent autoEvent = (AutoResetEvent)stateInfo;
            Toggle(ToggleMode.Random);
            autoEvent.Set();
        }

        private static void Toggle(ToggleMode mode)
        {
            _toggle = _toggle ? false : true;
            ToggleMode _mode = mode;
            Random rand = new Random();

            if (_mode == ToggleMode.Random)
            {
                int i = rand.Next(1, 100);

                _mode = (i % 2 == 0) ? ToggleMode.All : ToggleMode.Toggle;
            }

            if (_toggle)
            {
                Console.WriteLine("{0:h:mm:ss.fff} open, elapsed {1}", DateTime.Now, DateTime.Now - _start);
                _start = DateTime.Now;
                _isClosed = false;

                switch(_mode)
                {
                    case ToggleMode.All:
                        AllValvesOpen();
                        break;
                    case ToggleMode.Toggle:
                        Valve1Open();
                        Valve2Close();
                        break;
                }
            }
            else
            {
                Console.WriteLine("{0:h:mm:ss.fff} closed, elapsed {1}", DateTime.Now, DateTime.Now - _start);
                _start = DateTime.Now;
                _isClosed = true;
                switch (_mode)
                {
                    case ToggleMode.All:
                        AllValvesClose();
                        break;
                    case ToggleMode.Toggle:
                        Valve1Close();
                        Valve2Open();
                        break;
                }
            }
        }

        private static void AllValvesClose()
        {
            Valve1Close();
            Valve2Close();
        }

        private static void AllValvesOpen()
        {
            Valve1Open();
            Valve2Open();
        }

        private static async Task<bool> Beep(int _time)
        {
            System.Timers.Timer _timer = new System.Timers.Timer();
            _timer.Interval = _time * 1000;
            _timer.AutoReset = false;
            _timer.Elapsed += _timer_Elapsed;
            _timer.Enabled = true;
            _timer.Start();
            return true;
        }

        private static void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Console.WriteLine("{0:h:mm:ss.fff} Beep, elapsed {1}", DateTime.Now, DateTime.Now - _start);
            _iocontroller.Write(REL_CH2_PIN, PinValue.High);
            Thread.Sleep(750);
            _iocontroller.Write(REL_CH2_PIN, PinValue.Low);
        }

        #endregion

        #region Display
        private static void InitDisplay()
        {
            // Reset Display
            _iocontroller.Write(RESET_PIN, PinValue.High);
            Thread.Sleep(1);
            _iocontroller.Write(RESET_PIN, PinValue.Low);
            Thread.Sleep(10);
            _iocontroller.Write(RESET_PIN, PinValue.High);

            _display.Init(true);
            _display.WriteLineDisplayBuf(" ", 0, 0);
            _display.WriteLineDisplayBuf("       BCv1       ", 0, 1);
            _display.WriteLineDisplayBuf("     Starting     ", 0, 2);
            _display.WriteLineDisplayBuf(" ", 0, 0);
            _display.DisplayUpdate();

            Thread.Sleep(4000);

            _display.ClearDisplayBuf();
            _display.DisplayUpdate();
        }

        private static void UpdateDisplay()
        {
            _display.ClearDisplayBuf();
            _display.WriteLineDisplayBuf(_displayLine1, 0, 0);
            _display.WriteLineDisplayBuf(_displayLine2, 0, 1);
            _display.WriteLineDisplayBuf(_displayLine3, 0, 2);
            _display.WriteLineDisplayBuf(_displayLine4, 0, 3);
            _display.DisplayUpdate();
        }
        #endregion
    }
}      