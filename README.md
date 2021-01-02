# BCv1
Controlling valves based on different timers. There is absolutly no warranty!

The first version was desinged for .NET 4.61 and .Net Core 3.1.
Suggestions are welcome. In case of issues please be patient, I will check them asap.

Used projects and howtos from other persons or organazations:

- deploy.ps1 based on https://medium.com/@lewwybogus/debugging-your-net-core-3-app-on-your-raspberry-pi-with-visual-studio-2019-9662348e79d9
- Display Classes based on Microsoft / https://github.com/stefangordon/IoTCore-SSD1306-Driver


Used hardware:

- Raspberry Pi 3B+ with Raspbian installed
- Adafruit SSD 1306 Display in I2C mode (https://www.reichelt.de/entwicklerboards-display-oled-1-3-128x64-pixel-ssd1306-debo-oled-1-3-p235524.html?&nbc=1)
- Switching board (https://www.reichelt.de/entwicklerboards-relais-modul-4-channel-5-v-srd-05vdc-sl-c-debo-relais-4ch-p242811.html?&nbc=1)

Used NuGet Packages:

- SixLabors.ImageSharp.Drawing 1.0.0-beta11 (Please only use this version!)
- System.Device.Gpio
- Unosquare.Raspberry.Abstractions
- Unosquare.Raspberry.IO
- Unosquare.Raspberry.IO.Peripherals
- Unosquare.WiringPi

Unosquare is being used for easy handling GPIO Button events. 