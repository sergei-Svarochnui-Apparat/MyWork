using Microsoft.ML.OnnxRuntime;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Testfowm;

internal sealed class ConnectML : IDisposable
{
    private const int InputSize = 640;
    private const float IouThreshold = 0.45f;
    private readonly InferenceSession session;
    private readonly string inputName;
    private readonly string[] outputNames;
    private readonly float[] inputBuffer = new float[3 * InputSize * InputSize];
    private readonly byte[] pixelBuffer = new byte[InputSize * InputSize * 3];
    private readonly Bitmap resized;
    private readonly long[] inputShape = { 1, 3, InputSize, InputSize };

    // Этот объект используется только одним фоновым обработчиком.
    public ConnectML(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Не найдена модель Models/yolov8n.onnx.", modelPath);

        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1))
        };
        session = new InferenceSession(modelPath, options);
        try
        {
            resized = new Bitmap(InputSize, InputSize, PixelFormat.Format24bppRgb);
            inputName = session.InputMetadata.Keys.Single();
            var dimensions = session.InputMetadata[inputName].Dimensions;
            if (dimensions.Length != 4 ||
                (dimensions[0] > 0 && dimensions[0] != 1) ||
                dimensions[1] != 3 ||
                (dimensions[2] > 0 && dimensions[2] != InputSize) ||
                (dimensions[3] > 0 && dimensions[3] != InputSize))
                throw new InvalidDataException("Ожидается модель YOLOv8 с входом [1,3,640,640].");
            outputNames = session.OutputNames.ToArray();
        }
        catch
        {
            session.Dispose();
            resized?.Dispose();
            throw;
        }
    }

    public List<ResultML> Detect(Bitmap bitmap, float confidenceThreshold = 0.40f)
    {
        if (!float.IsFinite(confidenceThreshold) || confidenceThreshold < 0 || confidenceThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(confidenceThreshold));
        var transform = PrepareInput(bitmap);
        using var input = OrtValue.CreateTensorValueFromMemory(
            inputBuffer, inputShape);
        using var runOptions = new RunOptions();
        using var output = session.Run(runOptions,
            new Dictionary<string, OrtValue> { [inputName] = input }, outputNames);

        var dimensions = output[0].GetTensorTypeAndShape().Shape;
        if (dimensions.Length != 3 || dimensions[0] != 1 ||
            (dimensions[1] != 84 && dimensions[2] != 84))
            throw new InvalidDataException(
                "Ожидается выход YOLOv8 COCO [1,84,N] или [1,N,84] без встроенного NMS.");

        bool channelsFirst = dimensions[1] == 84;
        int count = checked((int)(channelsFirst ? dimensions[2] : dimensions[1]));
        var data = output[0].GetTensorDataAsSpan<float>();
        var candidates = new List<ResultML>();

        for (int i = 0; i < count; i++)
        {
            float confidence = Read(data, channelsFirst, count, i, 4);
            if (!float.IsFinite(confidence) || confidence < confidenceThreshold)
                continue;

            // В COCO человек — класс 0. Другой более вероятный класс отбрасываем.
            bool isPerson = true;
            for (int channel = 5; channel < 84; channel++)
            {
                if (Read(data, channelsFirst, count, i, channel) > confidence)
                {
                    isPerson = false;
                    break;
                }
            }
            if (!isPerson) continue;

            float cx = Read(data, channelsFirst, count, i, 0);
            float cy = Read(data, channelsFirst, count, i, 1);
            float width = Read(data, channelsFirst, count, i, 2);
            float height = Read(data, channelsFirst, count, i, 3);
            if (!float.IsFinite(cx) || !float.IsFinite(cy) ||
                !float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
                continue;

            float left = Math.Clamp((cx - width / 2 - transform.PadX) / transform.ScaleX, 0, bitmap.Width);
            float top = Math.Clamp((cy - height / 2 - transform.PadY) / transform.ScaleY, 0, bitmap.Height);
            float right = Math.Clamp((cx + width / 2 - transform.PadX) / transform.ScaleX, 0, bitmap.Width);
            float bottom = Math.Clamp((cy + height / 2 - transform.PadY) / transform.ScaleY, 0, bitmap.Height);
            if (right > left && bottom > top)
                candidates.Add(new ResultML(RectangleF.FromLTRB(left, top, right, bottom), confidence));
        }
        return SuppressDuplicates(candidates, IouThreshold);
    }

    private static float Read(ReadOnlySpan<float> data, bool channelsFirst,
        int count, int candidate, int channel) =>
        data[channelsFirst ? channel * count + candidate : candidate * 84 + channel];

    private (float ScaleX, float ScaleY, int PadX, int PadY) PrepareInput(Bitmap bitmap)
    {
        float scale = Math.Min(InputSize / (float)bitmap.Width, InputSize / (float)bitmap.Height);
        int scaledWidth = Math.Clamp((int)Math.Round(bitmap.Width * scale), 1, InputSize);
        int scaledHeight = Math.Clamp((int)Math.Round(bitmap.Height * scale), 1, InputSize);
        int padX = (InputSize - scaledWidth) / 2;
        int padY = (InputSize - scaledHeight) / 2;

        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.Clear(Color.FromArgb(114, 114, 114));
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.DrawImage(bitmap, new Rectangle(padX, padY, scaledWidth, scaledHeight));
        }
        var bits = resized.LockBits(new Rectangle(0, 0, InputSize, InputSize),
            ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            // 640 * 3 кратно 4, поэтому у этого Bitmap нет дополнения строк.
            Marshal.Copy(bits.Scan0, pixelBuffer, 0, pixelBuffer.Length);
        }
        finally { resized.UnlockBits(bits); }

        int planeSize = InputSize * InputSize;
        for (int i = 0; i < planeSize; i++)
        {
            inputBuffer[i] = pixelBuffer[i * 3 + 2] / 255f;
            inputBuffer[planeSize + i] = pixelBuffer[i * 3 + 1] / 255f;
            inputBuffer[2 * planeSize + i] = pixelBuffer[i * 3] / 255f;
        }
        return (scaledWidth / (float)bitmap.Width, scaledHeight / (float)bitmap.Height, padX, padY);
    }

    internal static List<ResultML> SuppressDuplicates(IEnumerable<ResultML> candidates, float threshold)
    {
        var selected = new List<ResultML>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Confidence))
        {
            if (selected.All(s => IntersectionOverUnion(candidate.Bounds, s.Bounds) <= threshold))
                selected.Add(candidate);
        }
        return selected;
    }

    internal static float IntersectionOverUnion(RectangleF first, RectangleF second)
    {
        var intersection = RectangleF.Intersect(first, second);
        float sharedArea = Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
        float totalArea = first.Width * first.Height + second.Width * second.Height - sharedArea;
        return totalArea > 0 ? sharedArea / totalArea : 0;
    }

    public void Dispose() { session.Dispose(); resized.Dispose(); }
}
