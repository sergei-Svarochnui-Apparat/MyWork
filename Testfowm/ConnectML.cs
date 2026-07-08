using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Testfowm
{
    class ConnectML
    {
        private InferenceSession session;

        public ConnectML()
        {
            session = new InferenceSession("C:/Users/Sergei/source/repos/Testfowm/Testfowm/Models/yolov8n.onnx");//подключение модели ИИ
        }

        public List<ResultML> Detect(Bitmap bitmap)
        {
            List<ResultML> results = new List<ResultML>(); //Список результатов

            var tensor = ConvertBitmap(bitmap);//Даёт готовую числовую матрицу

            var inputs = new List<NamedOnnxValue>
            {
                //имя: images
                //данные: tensor
                NamedOnnxValue.CreateFromTensor("images", tensor)//Упаковать данные для нейронки и дать им имя в нашем случае images.
            };

            using var output = session.Run(inputs);//Теперь создаём сессию для ИИ и передаём наши данные упакованную числовую матрицу
            var data = output.First().AsTensor<float>();
            Debug.WriteLine("YOLO ответ размер: " + data.Length);
            // перебираем 8400 вариантов
            for (int i = 0; i < 8400; i++)
            {
                // координаты
                float x = data[0, 0, i];
                float y = data[0, 1, i];
                float width = data[0, 2, i];
                float height = data[0, 3, i];
                float maxScore = 0;
                int classId = 0;

                // ищем самый вероятный класс
                for (int c = 4; c < 84; c++)
                {
                    float score = data[0, c, i];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        classId = c - 4;
                    }
                }
                // оставляем уверенные результаты
                if (maxScore > 0.5f)
                {
                    ResultML result = new ResultML();
                    result.X = x;
                    result.Y = y;
                    result.Width = width;
                    result.Height = height;
                    result.Confidence = maxScore;
                    result.Label = classId.ToString();
                    results.Add(result);
                    Debug.WriteLine($"Нашёл класс {classId} вероятность {maxScore * 100}%");
                }
            }
            return results;
        }

        private DenseTensor<float> ConvertBitmap(Bitmap bitmap)
        {
            Bitmap resized = new Bitmap(bitmap, new Size(640, 640));//сжимает моё изображение 1920x1080 в 640x640 пикселей стало меньше ,а картинка в целом остаётся

            DenseTensor<float> tensor = new DenseTensor<float>(new[] { 1, 3, 640, 640 });//Выделяем память 1*3*640*640 = 1 228 800 ячеек

            Rectangle rect = new Rectangle(0,0,resized.Width,resized.Height);

            BitmapData bmpData = resized.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            int bytes = Math.Abs(bmpData.Stride) * resized.Height;

            byte[] rgbValues = new byte[bytes];

            Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

            resized.UnlockBits(bmpData);

            for (int y = 0; y < 640; y++)
            {
                for (int x = 0; x < 640; x++)
                {
                    //Color pixel = resized.GetPixel(x, y);
                    int index = y * bmpData.Stride + x * 3;

                    byte b = rgbValues[index];
                    byte g = rgbValues[index + 1];
                    byte r = rgbValues[index + 2];

                    //Вычисляет RGB своими числами с одинарной точностью которые понимает модель
                    tensor[0, 0, y, x] = r / 255f; 
                    tensor[0, 1, y, x] = g / 255f;
                    tensor[0, 2, y, x] = b / 255f;
                }
            }
            resized.Dispose();
            return tensor; //Возвращает
        }
    }
}
