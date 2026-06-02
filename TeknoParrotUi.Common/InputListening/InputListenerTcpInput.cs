using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TeknoParrotUi.Common.InputListening
{
    public class InputListenerTcpInput
    {
        public static bool KillMe;

        private const int TcpPort = 33610;
        private const int PacketSize = 46;

        private float _minX, _maxX, _minY, _maxY;
        private bool _invertedMouseAxis, _16bit;
        private bool _isLuigisMansion, _isGunslinger, _isPrimevalHunt, _onedisplay, _swapdisplay;
        private bool _useDirectionalPresses;
        private List<JoystickButtons> _tcpButtons;

        private bool[] _lastTrigger = new bool[4];
        private bool[] _lastReload  = new bool[4];
        private bool[] _lastAction  = new bool[4];

        public void ListenTcpInput(List<JoystickButtons> joystickButtons, GameProfile gameProfile)
        {
            KillMe = false;

            _minX = gameProfile.xAxisMin;
            _maxX = gameProfile.xAxisMax;
            _minY = gameProfile.yAxisMin;
            _maxY = gameProfile.yAxisMax;
            _invertedMouseAxis       = gameProfile.InvertedMouseAxis;
            _isLuigisMansion         = gameProfile.EmulationProfile == EmulationProfile.LuigisMansion;
            _isGunslinger            = gameProfile.EmulationProfile == EmulationProfile.GunslingerStratos3;
            _isPrimevalHunt          = gameProfile.EmulationProfile == EmulationProfile.PrimevalHunt;
            _16bit                   = gameProfile.Use16BitAnalog;
            _useDirectionalPresses   = gameProfile.UseDirectionalPresses;

            if (_isPrimevalHunt)
            {
                _onedisplay  = gameProfile.ConfigValues.Any(x => x.FieldName == "OneDisplay"    && x.FieldValue == "1");
                _swapdisplay = gameProfile.ConfigValues.Any(x => x.FieldName == "SwapDisplay"   && x.FieldValue == "1");
            }

            _tcpButtons = joystickButtons
                .Where(b => b?.RawInputButton?.DevicePath != null && TcpLightgunDevice.IsTcpDevice(b.RawInputButton.DevicePath))
                .ToList();

            TcpListener server = null;
            try
            {
                server = new TcpListener(IPAddress.Loopback, TcpPort);
                server.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                server.Start();

                while (!KillMe)
                {
                    if (!server.Pending())
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    using (var client = server.AcceptTcpClient())
                    {
                        client.NoDelay = true;
                        HandleClient(client);
                    }
                }
            }
            catch (SocketException) { }
            finally { server?.Stop(); }
        }

        private void HandleClient(TcpClient client)
        {
            var stream = client.GetStream();
            var buf    = new byte[PacketSize * 16];
            int filled = 0;

            while (!KillMe)
            {
                try
                {
                    int n = stream.Read(buf, filled, buf.Length - filled);
                    if (n == 0) break;
                    filled += n;

                    while (filled >= PacketSize)
                    {
                        ParseAndApply(buf);
                        Buffer.BlockCopy(buf, PacketSize, buf, 0, filled - PacketSize);
                        filled -= PacketSize;
                    }
                }
                catch { break; }
            }
        }

        private void ParseAndApply(byte[] data)
        {
            for (int p = 0; p < 4; p++)
            {
                float x = BitConverter.ToSingle(data, p * 4);       // bytes 0-15
                float y = BitConverter.ToSingle(data, 16 + p * 4);  // bytes 16-31
                bool trigger = data[34 + p] != 0;
                bool reload  = data[38 + p] != 0;
                bool action  = data[42 + p] != 0;

                // Reload = offscreen shoot: zero the position
                if (reload)
                {
                    x = 0.0f;
                    y = 0.0f;
                }

                string path = TcpLightgunDevice.All[p];

                // Update gun axis position
                var gunBtn = _tcpButtons.FirstOrDefault(b =>
                    b.RawInputButton.DevicePath == path &&
                    (b.InputMapping == InputMapping.P1LightGun || b.InputMapping == InputMapping.P2LightGun ||
                     b.InputMapping == InputMapping.P3LightGun || b.InputMapping == InputMapping.P4LightGun));
                if (gunBtn != null)
                    WriteGunPosition(gunBtn, x, y);

                // LeftButton  = trigger (also fires when reload is active, since reload is offscreen trigger)
                // RightButton = reload (fires in addition so game can react to offscreen event)
                // MiddleButton = action/grenade
                FireButton(path, RawMouseButton.LeftButton,   trigger || reload, ref _lastTrigger[p]);
                FireButton(path, RawMouseButton.RightButton,  reload,            ref _lastReload[p]);
                FireButton(path, RawMouseButton.MiddleButton, action,            ref _lastAction[p]);
            }
        }

        private void FireButton(string path, RawMouseButton mouseButton, bool pressed, ref bool last)
        {
            if (pressed == last) return;
            last = pressed;

            foreach (var btn in _tcpButtons.Where(b =>
                b.RawInputButton.DevicePath  == path &&
                b.RawInputButton.MouseButton == mouseButton))
            {
                HandleButton(btn, pressed);
            }
        }

        private void HandleButton(JoystickButtons jsButton, bool pressed)
        {
            switch (jsButton.InputMapping)
            {
                case InputMapping.Test:          InputCode.PlayerDigitalButtons[0].Test    = pressed; break;
                case InputMapping.Service1:      InputCode.PlayerDigitalButtons[0].Service = pressed; break;
                case InputMapping.Service2:      InputCode.PlayerDigitalButtons[1].Service = pressed; break;
                case InputMapping.Coin1:         InputCode.PlayerDigitalButtons[0].Coin    = pressed; break;
                case InputMapping.Coin2:         InputCode.PlayerDigitalButtons[1].Coin    = pressed; break;

                case InputMapping.P1ButtonStart: InputCode.PlayerDigitalButtons[0].Start   = pressed; break;
                case InputMapping.P1Button1:     InputCode.PlayerDigitalButtons[0].Button1 = pressed; break;
                case InputMapping.P1Button2:     InputCode.PlayerDigitalButtons[0].Button2 = pressed; break;
                case InputMapping.P1Button3:     InputCode.PlayerDigitalButtons[0].Button3 = pressed; break;
                case InputMapping.P1Button4:     InputCode.PlayerDigitalButtons[0].Button4 = pressed; break;
                case InputMapping.P1Button5:     InputCode.PlayerDigitalButtons[0].Button5 = pressed; break;
                case InputMapping.P1Button6:     InputCode.PlayerDigitalButtons[0].Button6 = pressed; break;

                case InputMapping.P1ButtonUp:
                    if (_useDirectionalPresses)
                        InputCode.SetPlayerDirection(InputCode.PlayerDigitalButtons[0], pressed ? Direction.Up : Direction.VerticalCenter);
                    else
                        InputCode.PlayerDigitalButtons[0].Up = pressed;
                    break;
                case InputMapping.P1ButtonDown:
                    if (_useDirectionalPresses)
                        InputCode.SetPlayerDirection(InputCode.PlayerDigitalButtons[0], pressed ? Direction.Down : Direction.VerticalCenter);
                    else
                        InputCode.PlayerDigitalButtons[0].Down = pressed;
                    break;
                case InputMapping.P1ButtonLeft:
                    if (_useDirectionalPresses)
                        InputCode.SetPlayerDirection(InputCode.PlayerDigitalButtons[0], pressed ? Direction.Left : Direction.HorizontalCenter);
                    else
                        InputCode.PlayerDigitalButtons[0].Left = pressed;
                    break;
                case InputMapping.P1ButtonRight:
                    if (_useDirectionalPresses)
                        InputCode.SetPlayerDirection(InputCode.PlayerDigitalButtons[0], pressed ? Direction.Right : Direction.HorizontalCenter);
                    else
                        InputCode.PlayerDigitalButtons[0].Right = pressed;
                    break;

                case InputMapping.P2ButtonStart: InputCode.PlayerDigitalButtons[1].Start   = pressed; break;
                case InputMapping.P2Button1:     InputCode.PlayerDigitalButtons[1].Button1 = pressed; break;
                case InputMapping.P2Button2:     InputCode.PlayerDigitalButtons[1].Button2 = pressed; break;
                case InputMapping.P2Button3:     InputCode.PlayerDigitalButtons[1].Button3 = pressed; break;
                case InputMapping.P2Button4:     InputCode.PlayerDigitalButtons[1].Button4 = pressed; break;
                case InputMapping.P2Button5:     InputCode.PlayerDigitalButtons[1].Button5 = pressed; break;
                case InputMapping.P2Button6:     InputCode.PlayerDigitalButtons[1].Button6 = pressed; break;

                case InputMapping.P2ButtonUp:
                    if (_useDirectionalPresses)
                        InputCode.SetPlayerDirection(InputCode.PlayerDigitalButtons[1], pressed ? Direction.Up : Direction.VerticalCenter);
                    else
                        InputCode.PlayerDigitalButtons[1].Up = pressed;
                    break;
                case InputMapping.P2ButtonDown:
                    if (_useDirectionalPresses)
                        InputCode.SetPlayerDirection(InputCode.PlayerDigitalButtons[1], pressed ? Direction.Down : Direction.VerticalCenter);
                    else
                        InputCode.PlayerDigitalButtons[1].Down = pressed;
                    break;
                case InputMapping.P2ButtonLeft:
                    if (_useDirectionalPresses)
                        InputCode.SetPlayerDirection(InputCode.PlayerDigitalButtons[1], pressed ? Direction.Left : Direction.HorizontalCenter);
                    else
                        InputCode.PlayerDigitalButtons[1].Left = pressed;
                    break;
                case InputMapping.P2ButtonRight:
                    if (_useDirectionalPresses)
                        InputCode.SetPlayerDirection(InputCode.PlayerDigitalButtons[1], pressed ? Direction.Right : Direction.HorizontalCenter);
                    else
                        InputCode.PlayerDigitalButtons[1].Right = pressed;
                    break;

                case InputMapping.ExtensionOne1:  InputCode.PlayerDigitalButtons[0].ExtensionButton1_1 = pressed; break;
                case InputMapping.ExtensionOne2:  InputCode.PlayerDigitalButtons[0].ExtensionButton1_2 = pressed; break;
                case InputMapping.ExtensionOne3:  InputCode.PlayerDigitalButtons[0].ExtensionButton1_3 = pressed; break;
                case InputMapping.ExtensionOne4:  InputCode.PlayerDigitalButtons[0].ExtensionButton1_4 = pressed; break;
                case InputMapping.ExtensionOne11: InputCode.PlayerDigitalButtons[0].ExtensionButton1_5 = pressed; break;
                case InputMapping.ExtensionOne12: InputCode.PlayerDigitalButtons[0].ExtensionButton1_6 = pressed; break;
                case InputMapping.ExtensionOne13: InputCode.PlayerDigitalButtons[0].ExtensionButton1_7 = pressed; break;
                case InputMapping.ExtensionOne14: InputCode.PlayerDigitalButtons[0].ExtensionButton1_8 = pressed; break;

                case InputMapping.ExtensionTwo1:  InputCode.PlayerDigitalButtons[1].ExtensionButton1_1 = pressed; break;
                case InputMapping.ExtensionTwo2:  InputCode.PlayerDigitalButtons[1].ExtensionButton1_2 = pressed; break;
                case InputMapping.ExtensionTwo3:  InputCode.PlayerDigitalButtons[1].ExtensionButton1_3 = pressed; break;
                case InputMapping.ExtensionTwo4:  InputCode.PlayerDigitalButtons[1].ExtensionButton1_4 = pressed; break;
                case InputMapping.ExtensionTwo11: InputCode.PlayerDigitalButtons[1].ExtensionButton1_5 = pressed; break;
                case InputMapping.ExtensionTwo12: InputCode.PlayerDigitalButtons[1].ExtensionButton1_6 = pressed; break;
                case InputMapping.ExtensionTwo13: InputCode.PlayerDigitalButtons[1].ExtensionButton1_7 = pressed; break;
                case InputMapping.ExtensionTwo14: InputCode.PlayerDigitalButtons[1].ExtensionButton1_8 = pressed; break;
            }
        }

        private void WriteGunPosition(JoystickButtons joystickButton, float factorX, float factorY)
        {
            factorX = Math.Max(0.0f, Math.Min(1.0f, factorX));
            factorY = Math.Max(0.0f, Math.Min(1.0f, factorY));

            float minX = _minX, maxX = _maxX, minY = _minY, maxY = _maxY;

            if (_16bit)
            {
                if (maxX <= 255 && minX >= 0) { minX *= 257.0f; maxX *= 257.0f; }
                if (maxY <= 255 && minY >= 0) { minY *= 257.0f; maxY *= 257.0f; }
            }

            ushort x, y;

            if (_isPrimevalHunt && !_onedisplay && !_swapdisplay)
                x = (ushort)Math.Round(1.0 + factorX * 2.0 * (maxX - minX));
            else if (_isPrimevalHunt && !_onedisplay && _swapdisplay)
                x = (ushort)Math.Round(minX + factorX * 2.0 * (maxX - minX));
            else
                x = (ushort)Math.Round(minX + factorX * (maxX - minX));

            y = (ushort)Math.Round(minY + factorY * (maxY - minY));

            byte indexA = 0, indexB = 2;
            switch (joystickButton.InputMapping)
            {
                case InputMapping.P1LightGun: indexA = 0;  indexB = 2;  break;
                case InputMapping.P2LightGun: indexA = 4;  indexB = 6;  break;
                case InputMapping.P3LightGun: indexA = 8;  indexB = 10; break;
                case InputMapping.P4LightGun: indexA = 12; indexB = 14; break;
            }

            if (_isGunslinger)
            {
                switch (joystickButton.InputMapping)
                {
                    case InputMapping.P1LightGun: indexA = 8;  indexB = 10; break;
                    case InputMapping.P2LightGun: indexA = 12; indexB = 14; break;
                }
            }

            if (_16bit)
            {
                if (_isLuigisMansion || _isGunslinger)
                {
                    InputCode.AnalogBytes[indexB]     = (byte)(x >> 8);
                    InputCode.AnalogBytes[indexB + 1] = (byte)(x & 0xFF);
                    InputCode.AnalogBytes[indexA]     = (byte)(y >> 8);
                    InputCode.AnalogBytes[indexA + 1] = (byte)(y & 0xFF);
                }
                else if (_invertedMouseAxis)
                {
                    InputCode.AnalogBytes[indexA]     = (byte)(x >> 8);
                    InputCode.AnalogBytes[indexA + 1] = (byte)(x & 0xFF);
                    InputCode.AnalogBytes[indexB]     = (byte)(y >> 8);
                    InputCode.AnalogBytes[indexB + 1] = (byte)(y & 0xFF);
                }
                else
                {
                    ushort ix = (ushort)~x, iy = (ushort)~y;
                    InputCode.AnalogBytes[indexB]     = (byte)(ix >> 8);
                    InputCode.AnalogBytes[indexB + 1] = (byte)(ix & 0xFF);
                    InputCode.AnalogBytes[indexA]     = (byte)(iy >> 8);
                    InputCode.AnalogBytes[indexA + 1] = (byte)(iy & 0xFF);
                }
            }
            else
            {
                if (_isLuigisMansion || _isGunslinger)
                {
                    InputCode.AnalogBytes[indexB] = (byte)x;
                    InputCode.AnalogBytes[indexA] = (byte)y;
                }
                else if (_invertedMouseAxis)
                {
                    InputCode.AnalogBytes[indexA] = (byte)x;
                    InputCode.AnalogBytes[indexB] = (byte)y;
                }
                else
                {
                    InputCode.AnalogBytes[indexB] = (byte)~x;
                    InputCode.AnalogBytes[indexA] = (byte)~y;
                }
            }
        }
    }
}
