using RGB.NET.Core;
using RGB.NET.Devices.EVGA;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace test
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private byte[] ReadMsg(Stream nps)
        {
            byte[] magic = new byte[4];
            int readlen = nps.Read(magic, 0, 4);
            if (readlen != 4 || !(magic[0] == 0xB && magic[1] == 0xE && magic[2] == 0xE && magic[3] == 0xF))
            {
                throw new Exception("No beef");
            }
            
            byte[] bMsg = new byte[16];
            readlen = nps.Read(bMsg, 0, bMsg.Length);
            if (readlen != 16)
            {
                throw new Exception("Bad data");
            }
            
            return bMsg;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            //NamedPipeClientStream nps = new NamedPipeClientStream(".", "evgargbled",PipeDirection.InOut);
            //Queue<byte> queue = new Queue<byte>();

            //try
            //{
            //    nps.Connect(1000);
            //}catch (TimeoutException tex)
            //{
            //    //fail
            //    throw;
            //}
            //byte[] bMagic = new byte[] { 0xB, 0xE, 0xE, 0xF };
            //nps.Write(bMagic, 0, 4);
            //byte[] bMsg = new byte[16];
            //bMsg[0] = 1;
            //nps.Write(bMsg, 0, bMsg.Length);
            //bMsg = ReadMsg(nps);
            //if (bMsg[0] != 2)
            //{
            //    throw new Exception("Wrong response");
            //}
            
            //uint numDevices = bMsg[1];
            //for (int i = 0; i < numDevices; i++)
            //{
            //    nps.Write(bMagic, 0, 4);
            //    bMsg = new byte[16];
            //    bMsg[0] = 5;
            //    bMsg[1] = (byte)i;
            //    nps.Write(bMsg, 0, bMsg.Length);
            //    bMsg = ReadMsg(nps);
            //    if (bMsg[0] != 6)
            //    {
            //        throw new Exception("Wrong response");
            //    }
            //    int numLeds = bMsg[1];
            //    MessageBox.Show($"device {i} has {numLeds} leds");
            //}
            //byte deviceId = 0;
            //byte ledId = 0;
            //bMsg = new byte[16];
            //bMsg[0] = 10;
            //bMsg[1] = (byte)deviceId;
            //bMsg[2] = (byte)ledId;
            //bMsg[3] = 255;
            //bMsg[4] = 255;
            //bMsg[5] = 255;
            //bMsg[6] = 255;
            //nps.Write(bMagic, 0, 4);
            //nps.Write(bMsg, 0, bMsg.Length);
            //return;
            //EVGAProxy.EVGA64Proxy p = new EVGAProxy.EVGA64Proxy();
            var v = EVGADeviceProvider.Instance;
            RGBSurface.Instance.LoadDevices(v);
            return;
            Type com = Type.GetTypeFromProgID("EVGAProxy.EVGA64Proxy");
            var obj = Activator.CreateInstance(com);
            var numDevs = (int)com.InvokeMember("GetNumberOfDevices", System.Reflection.BindingFlags.InvokeMethod, null, obj, null);
            com.InvokeMember("SetColor", System.Reflection.BindingFlags.InvokeMethod, null, obj, new object[] { 0, 0, (byte)255, (byte)255, (byte)255, (byte)255 });
                /*        public int GetNumberOfDevices()
        {
            return _instances.Count();
        }

        public void SetColor(int deviceId, int ledId, byte a, byte r, byte g, byte b)*/
            //var v = EVGADeviceProvider.Instance;
            //RGBSurface.Instance.LoadDevices(v);
        }
    }
}
