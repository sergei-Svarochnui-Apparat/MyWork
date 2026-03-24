using Microsoft.VisualBasic.ApplicationServices;
using System.Reflection.Emit;
using System.Windows.Forms;

using static System.Net.Mime.MediaTypeNames;

namespace Testfowm
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private Button myButton; //Кнопка
        public PictureBox myPictureBox;//Box картинки
        private System.Windows.Forms.Label labelForGetPixels;
        private System.Windows.Forms.Timer timer; //Таймер

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            myButton = new Button();
            myPictureBox = new PictureBox();
            timer = new System.Windows.Forms.Timer(components);
            labelForGetPixels = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)myPictureBox).BeginInit();
            SuspendLayout();
            // 
            // myButton
            // 
            myButton.Location = new Point(12, 456);
            myButton.Name = "myButton";
            myButton.Size = new Size(124, 23);
            myButton.TabIndex = 0;
            myButton.Text = "Нажми тварь";
            myButton.Click += MyButton_Click;
            // 
            // myPictureBox
            // 
            myPictureBox.BorderStyle = BorderStyle.FixedSingle;
            myPictureBox.Location = new Point(142, 12);
            myPictureBox.Name = "myPictureBox";
            myPictureBox.Size = new Size(814, 400);
            myPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            myPictureBox.TabIndex = 0;
            myPictureBox.TabStop = false;
            // 
            // timer
            // 
            timer.Enabled = true;
            timer.Interval = 2000;
            timer.Tick += TickTack;
            // 
            // labelForGetPixels
            // 
            labelForGetPixels.Location = new Point(12, 75);
            labelForGetPixels.Name = "label1";
            labelForGetPixels.Size = new Size(124, 38);
            labelForGetPixels.TabIndex = 2;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(994, 552);
            Controls.Add(labelForGetPixels);
            Controls.Add(myButton);
            Controls.Add(myPictureBox);

            Name = "Form1";
            Text = "MyFirstProg";
            ((System.ComponentModel.ISupportInitialize)myPictureBox).EndInit();
            ResumeLayout(false);
            connectCamera.StartCamera();
            //connectArduino.LoadPorts("KranVpravo");
            //Thread.Sleep(600);
            //connectArduino.LoadPorts("OpustiKran");
            //Thread.Sleep(600);
            //connectArduino.LoadPorts("PodnimiKran");
            //Thread.Sleep(600);
            //connectArduino.LoadPorts("BazovoePolozhenie");
        }
        //lock
        private readonly object _workLock = new object();
        //Классы
        ConnectArduino connectArduino = new ConnectArduino();
        ConnectCamera connectCamera = new ConnectCamera();

        //Рабочие переменные
        public string filePathOriginal = "C:/Users/Sergei/source/repos/Testfowm/Testfowm/Test/Tester.png";

        //Bitmap bitm = new Bitmap("C:/Users/Sergei/source/repos/Testfowm/Testfowm/Test/Tester.png");
        Bitmap bitm = new Bitmap(1920, 1080); //Это строка создает новый пустой BitMap (Растровое изображение) 1920x1080 пикселей
        //Массивы
        public Color[] colorsMatrixFirst = new Color[6860];
        public Color[] colorsMatrixTwo = new Color[6175]; //Фиксированный массив цвета 
        //Для карты Инферно
        public Color MyColor0 = Color.FromArgb(90,118,61);
        public Color MyColor1 = Color.FromArgb(246, 251, 214);

        public int CountColor = 0;

        public event Action workIt;//Делегат

        public int RangeFirst = 10;
        public int RangeTwo = 7;
        

        public bool WorkProcess;

        public void TickTack(object sender, EventArgs e)
        {

            Bitmap bitm = connectCamera.GetBitmap();

            if (bitm == null)
                return;

            if (myPictureBox.Image != null)
            {
                var oldImage = myPictureBox.Image;
                myPictureBox.Image = null;
                oldImage.Dispose();
            }
            bitm.Save(filePathOriginal);

            for (int i = 955; i <= 998; i++) //Чёрный квадрат
            {
                bitm.SetPixel(i, 445, Color.Black);
                bitm.SetPixel(i, 478, Color.Black);
                for (int j = 445; j <= 478; j++)
                {
                    bitm.SetPixel(955, j, Color.Black);
                    bitm.SetPixel(998, j, Color.Black);
                }
            }
            myPictureBox.Image = (Bitmap)bitm.Clone();

            for (int i = 955; i <= 998; i++)
            {
                for (int j = 445; j <= 478; j++)
                {
                    Color a = bitm.GetPixel(i, j);// Color это структура которая создаётся, при окончании цикла for уничтожается автоматически

                    if (Math.Abs(a.R - MyColor0.R) <= RangeFirst && Math.Abs(a.G - MyColor0.G) <= RangeFirst && Math.Abs(a.B - MyColor0.B) <= RangeFirst)
                    {
                        lock (_workLock)//много вызовов багает робота 
                        {               //пролему надо решить
                            workIt.Invoke();
                        }
                        CountColor++;
                    }


                }
            }

            labelForGetPixels.Text = CountColor.ToString();
            CountColor = 0;

            bitm.Dispose();
        }

        public void PressButton()
        {            
            Thread.Sleep(200);

            connectArduino.LoadPorts("OpustiKran");
            connectArduino.LoadPorts("PodnimiKran");
            connectArduino.LoadPorts("BazovoePolozhenie");
            connectArduino.LoadPorts("KranVpravo");
            connectArduino.LoadPorts("KranVlevo");
        }
        private void MyButton_Click(object sender, EventArgs e)
        {
            //connectArduino.LoadPorts("KranVpravo");
            //Thread.Sleep(200);
            //connectArduino.LoadPorts("OpustiKran");
            workIt += Test;
        }
        void Test()
        {
            lock (_workLock)
            {
                connectArduino.LoadPorts("OpustiKran");
                Thread.Sleep(500);
                connectArduino.LoadPorts("PodnimiKran");
            }

        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {

            connectCamera.StopCamera();
            base.OnFormClosing(e);
        }

            #endregion
    }
}
