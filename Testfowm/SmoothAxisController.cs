using System.Diagnostics;

namespace Testfowm;

// Сглаживает команды на ПК; физическую скорость и траекторию задаёт прошивка.
internal sealed class SmoothAxisController
{
    private const float StopTolerance = TargetAlignment.StopMargin;
    private const float ResumeTolerance = TargetAlignment.ResumeMargin;
    private long lastFrame;
    private float filteredError, previousStep;
    private int previousDirection, pendingDirection, pendingFrames;
    private bool initialized, reached;
    private readonly Queue<float> responses = new();
    private float normalizedResponse;
    private int unexpectedResponses;

    internal readonly record struct Correction(float Step, bool Reached, bool Reversing);

    public void Reset(bool forgetResponse = false)
    {
        lastFrame = 0;
        filteredError = previousStep = 0;
        previousDirection = pendingDirection = pendingFrames = 0;
        initialized = reached = false;
        responses.Clear();
        unexpectedResponses = 0;
        if (forgetResponse) normalizedResponse = 0;
    }

    // Местная чувствительность изображения: доля размера кадра на градус.
    // Нужны три согласованных наблюдения; обратный отклик и выбросы не принимаются.
    public void ObserveResponse(float before, float after, float commandStep, int frameExtent)
    {
        if (Math.Abs(commandStep) < .2f || frameExtent <= 0 || Math.Abs(before - after) < 2) return;
        float response = (before - after) / (frameExtent * commandStep);
        if (!float.IsFinite(response) || response < .001f || response > .5f) return;
        if (normalizedResponse > 0 && (response < normalizedResponse / 3 || response > normalizedResponse * 3))
        {
            // Если прежняя оценка несколько раз не совпала с откликом, заново собрать её.
            if (++unexpectedResponses < 3) return;
            normalizedResponse = 0;
            responses.Clear();
        }
        unexpectedResponses = 0;
        responses.Enqueue(response);
        if (responses.Count > 3) responses.Dequeue();
        if (responses.Count < 3) return;
        var values = responses.OrderBy(value => value).ToArray();
        if (values[2] > values[0] * 3) return;
        normalizedResponse = normalizedResponse == 0 ? values[1] : normalizedResponse * .8f + values[1] * .2f;
    }

    public Correction? Calculate(float rawError, int frameExtent, long capturedAt, float maximumStep)
    {
        // Повторный показ одной рамки не подтверждает смену направления.
        if (initialized && capturedAt <= lastFrame) return null;
        double elapsedMs = initialized ? Stopwatch.GetElapsedTime(lastFrame, capturedAt).TotalMilliseconds : 0;
        bool farFromFrame = Math.Abs(rawError) > StopTolerance + frameExtent * .04f;
        if (!initialized || elapsedMs > 400 || farFromFrame) filteredError = rawError;
        else
        {
            float alpha = (float)(1 - Math.Exp(-elapsedMs / 40));
            filteredError += alpha * (rawError - filteredError);
        }
        lastFrame = capturedAt;
        initialized = true;

        // Разные пороги остановки и возобновления убирают движения из-за дрожания рамки.
        if ((reached && Math.Abs(rawError) <= ResumeTolerance) || Math.Abs(rawError) <= StopTolerance)
        {
            reached = true;
            filteredError = rawError;
            previousStep = 0;
            previousDirection = pendingDirection = pendingFrames = 0;
            return new Correction(0, true, false);
        }
        reached = false;
        // Пока фильтр догоняет пересечение рамки, не продолжать движение в старую сторону.
        if (Math.Abs(filteredError) <= StopTolerance ||
            Math.Sign(rawError) != Math.Sign(filteredError))
        {
            previousStep = 0;
            pendingDirection = pendingFrames = 0;
            return new Correction(0, false, false);
        }

        int direction = Math.Sign(filteredError);
        bool reversing = previousDirection != 0 && direction != previousDirection;
        if (reversing && !farFromFrame)
        {
            if (pendingDirection != direction) { pendingDirection = direction; pendingFrames = 0; }
            previousStep = 0;
            if (++pendingFrames < 2) return new Correction(0, false, true);
        }
        pendingDirection = pendingFrames = 0;
        float remainingDistance = Math.Max(0, Math.Abs(filteredError) - StopTolerance);
        float scale = Math.Clamp(remainingDistance /
            Math.Max(1, frameExtent * .04f), 0, 1);
        float desiredStep = .1f + (maximumStep - .1f) * scale;
        if (normalizedResponse > 0)
            desiredStep = Math.Clamp(.75f * remainingDistance / (frameExtent * normalizedResponse), .1f, maximumStep);
        // Увеличивать шаг постепенно, уменьшать сразу по мере приближения к рамке.
        float step = Math.Min(desiredStep, previousStep + (farFromFrame ? 1f : .35f));
        if (reversing && !farFromFrame) step = Math.Min(step, .25f);
        previousStep = step;
        previousDirection = direction;
        return new Correction(direction * step, false, false);
    }
}
