using Microsoft.VisualBasic.ApplicationServices;
using System.Diagnostics;
using System.Drawing.Text;
using System.Reflection.Emit;
using System.Windows.Forms;

using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;

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
            myButton.Text = "Click";
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
            timer.Interval = 25;
            timer.Tick += TickTack;
            // 
            // labelForGetPixels
            // 
            labelForGetPixels.Location = new Point(383, 441);
            labelForGetPixels.Name = "labelForGetPixels";
            labelForGetPixels.Size = new Size(351, 38);
            labelForGetPixels.TabIndex = 2;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1008, 552);
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
        ConnectML connectML = new ConnectML();

        //Рабочие переменные
        public string filePathOriginal = "C:/Users/Sergei/source/repos/Testfowm/Testfowm/Test/Tester.png";
        public bool WorkProcess;
        public bool WorkProcess1 = true;
        public bool WorkPixels;
        //Bitmap bitm = new Bitmap("C:/Users/Sergei/source/repos/Testfowm/Testfowm/Test/Tester.png");
        Bitmap bitm = new Bitmap(1920, 1080); //Это строка создает новый пустой BitMap (Растровое изображение) 1920x1080 пикселей
        //Массивы
        public Color[] colorsMatrixFirst = new Color[6860];
        public Color[] colorsMatrixTwo = new Color[6175]; //Фиксированный массив цвета 
        //Для карты Инферно
        public Color MyColor0 = Color.FromArgb(90,118,61);
        public Color MyColor1 = Color.FromArgb(246, 251, 214);
        public Color MyColor2 = Color.FromArgb(240, 190, 114); //Желтый

        public int CountColor = 0;
        public int RangeFirst = 10;
        public int RangeTwo = 7;



        private volatile bool WorKran = false;
        private bool searchLeft = true;
        private int searchCount = 0;

        public void TickTack(object sender, EventArgs e)
        {
            Bitmap bitm = connectCamera.GetBitmap();
            if (bitm == null)
                return;

            // запускаем YOLO
            var objects = connectML.Detect(bitm);
            // перевод координат YOLO 640x640
            // в размер камеры
            // рисуем рамки
            using (Graphics g = Graphics.FromImage(bitm))
            {
                using (System.Drawing.Font font = new System.Drawing.Font("Arial", 16))
                {
                    Rectangle centerSquare = new Rectangle(923, 533, 75, 75);

                    g.DrawRectangle(Pens.Green, centerSquare);

                    foreach (var obj in objects)
                    {
                        

                        float scaleX = bitm.Width / 640f;
                        float scaleY = bitm.Height / 640f;

                        float left = (obj.X - obj.Width / 2) * scaleX;

                        float top = (obj.Y - obj.Height / 2) * scaleY;

                        float width = obj.Width * scaleX;

                        float height = obj.Height * scaleY;

                        Rectangle rect = new Rectangle((int)left,(int)top,(int)width,(int)height);

                        // сама рамка
                        g.DrawRectangle(Pens.Red,rect);

                        // подпись
                        g.DrawString($"{obj.Label} {obj.Confidence:P0}",font,Brushes.Red,left,top - 25);

                        //CheckObj(obj);

                        if (obj.Label == "0" && obj.Confidence > 0.55f)
                        {
                            float objectCenterX = left + width / 2;


                            Task.Run(() =>
                            {
                                TrackObject(objectCenterX, centerSquare);
                            });
                        }

                        labelForGetPixels.Text = $"{obj.Label} | X:{obj.X:F0} Y:{obj.Y:F0} | {obj.Confidence:P0}";
                        //Debug.WriteLine($"Объект: {obj.Label} шанс {obj.Confidence:P}");
                        //Debug.WriteLine($"X = {obj.X} --- Y = {obj.Y}");
                    }
                }
            }

            // очищаем старое изображение PictureBox

            if (myPictureBox.Image != null)
            {
                System.Drawing.Image old = myPictureBox.Image;

                myPictureBox.Image = null;

                old.Dispose();
            }

            
            // показываем новый кадр
            myPictureBox.Image = (Bitmap)bitm.Clone();
            // освобождаем кадр камеры
            bitm.Dispose();
        }

        private void TrackObject(float objectX, Rectangle centerSquare)
        {
            if (WorKran)
                return;


            WorKran = true;


            int squareCenterX = centerSquare.X + centerSquare.Width / 2;


            float difference = objectX - squareCenterX;


            // машина в квадрате
            if (Math.Abs(difference) < 100)
            {
                connectArduino.LoadPorts("BazovoePolozhenie");
                Thread.Sleep(100);
                PressKeyStroke();
                WorKran = false;
                return;
            }


            // машина левее
            if (difference < 0)
            {
                TurnLeft();
            }

            // машина правее
            else
            {
                TurnRight();
            }


            WorKran = false;
        }

        public void PressButton()
        {            
            Thread.Sleep(200);

            connectArduino.LoadPorts("OpustiKran");
            connectArduino.LoadPorts("PodnimiKran");
            connectArduino.LoadPorts("BazovoePolozhenie");
            connectArduino.LoadPorts("KranVpravo");
            connectArduino.LoadPorts("KranVlevo");
            connectArduino.LoadPorts("VytianNazad");
            connectArduino.LoadPorts("VytianVpered");
        }
        private void MyButton_Click(object sender, EventArgs e)
        {
            connectArduino.LoadPorts("VytianVpered");
            Thread.Sleep(200);

            //connectArduino.LoadPorts("KranVpravo");
            //Thread.Sleep(200);
            connectArduino.LoadPorts("OpustiKran");
            Thread.Sleep(200);
            //connectArduino.LoadPorts("PodnimiKran");
            //Thread.Sleep(200);
            //connectArduino.LoadPorts("KranVlevo");
            //Thread.Sleep(200);

        }
        private void PressKeyStroke()//Нажатие клавиши
        {
            connectArduino.LoadPorts("OpustiKran");
            Thread.Sleep(40);
            connectArduino.LoadPorts("PodnimiKran");
            Thread.Sleep(40);
        }
        private void TurnLeft()
        {
            connectArduino.LoadPorts("KranVlevo");
            Thread.Sleep(125);
            PressKeyStroke();
        }
        private void TurnRight()
        {
            connectArduino.LoadPorts("KranVpravo");
            Thread.Sleep(125);
            PressKeyStroke();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            connectArduino.LoadPorts("BazovoePolozhenie");
            connectCamera.StopCamera();
            base.OnFormClosing(e);
        }

            #endregion
    }
}
