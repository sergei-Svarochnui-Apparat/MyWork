using AForge.Video;
using AForge.Video.DirectShow;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Testfowm
{
    class ConnectCamera
    {
        private readonly object _lock = new object();
            
        private FilterInfoCollection captureDevices; 
        private VideoCaptureDevice videoSource;
        private Bitmap currentBitmap;

        public void StartCamera()
        {
            captureDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);//Поиск камер
            videoSource = new VideoCaptureDevice(captureDevices[0].MonikerString);//Выбор камеры 
            videoSource.NewFrame += NewFrameHandler;//Подписка на событие новый кадр
            videoSource.Start();//Включаем камеру
        }

        private void NewFrameHandler(object sender, NewFrameEventArgs eventArgs) //~30 раз в 1сек. 
        {
            Bitmap newFrame = (Bitmap)eventArgs.Frame.Clone();//Клон кадра
            lock (_lock)
            {
                if (currentBitmap != null)//Чистка старого кадра
                {
                    currentBitmap.Dispose();//Освобождение ячейки памяти в момент освобождения памяти может сработать и return (Bitmap)currentBitmap.Clone(); что приведёт к ошибке 
                    //исправить!
                }
                currentBitmap = newFrame;//Сохранение нового кадра
            }
        }

        public Bitmap GetBitmap()
        {
            lock (_lock)
            {
                if (currentBitmap == null)
                    return null;

                return (Bitmap)currentBitmap.Clone(); //Почему иногда ошибка вылазит
            }

        }

        public void StopCamera()
        {
            if (videoSource != null && videoSource.IsRunning)
            {
                videoSource.SignalToStop();
                videoSource.WaitForStop();
            }

            if (currentBitmap != null)
            {
                currentBitmap.Dispose();
                currentBitmap = null;
            }
        }
    }
}
