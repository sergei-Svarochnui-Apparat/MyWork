using System;
using System.Collections.Generic;
using System.Text;
using System.IO.Ports;
using System.Windows.Forms;
using System.Diagnostics;

namespace Testfowm
{
    class ConnectArduino
    {
        private string[] ports; //Массив имён портов

        public ConnectArduino()
        {
            ports = SerialPort.GetPortNames();//получаем порты
            //Debug.WriteLine(ports[0]);
        }

        public void LoadPorts(string nameInstruction)
        {
            using (SerialPort port = new SerialPort(ports[1], 115200))
            {
                port.Open();
                port.WriteLine(nameInstruction);
            } 
            //port.Dispose() всегда закрывает потому что using
        }
    }
}