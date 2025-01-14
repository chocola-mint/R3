﻿namespace R3;

public static partial class ObservableExtensions
{
    public static Observable<T> ThrottleFirstFrame<T>(this Observable<T> source, int frameCount)
    {
        return new ThrottleFirstFrame<T>(source, frameCount, ObservableSystem.DefaultFrameProvider);
    }

    public static Observable<T> ThrottleFirstFrame<T>(this Observable<T> source, int frameCount, FrameProvider frameProvider)
    {
        return new ThrottleFirstFrame<T>(source, frameCount, frameProvider);
    }
}

// ThrottleFirstFrame
internal sealed class ThrottleFirstFrame<T>(Observable<T> source, int frameCount, FrameProvider frameProvider) : Observable<T>
{
    protected override IDisposable SubscribeCore(Observer<T> observer)
    {
        return source.Subscribe(new _ThrottleFirstFrame(observer, frameCount.NormalizeFrame(), frameProvider));
    }

    sealed class _ThrottleFirstFrame : Observer<T>, IFrameRunnerWorkItem
    {
        readonly Observer<T> observer;
        readonly int frameCount;
        readonly object gate = new object();
        T? firstValue;
        bool hasValue;
        int currentFrame;

        public _ThrottleFirstFrame(Observer<T> observer, int frameCount, FrameProvider frameProvider)
        {
            this.observer = observer;
            this.frameCount = frameCount;
            frameProvider.Register(this);
        }

        protected override void OnNextCore(T value)
        {
            lock (gate)
            {
                if (!hasValue)
                {
                    hasValue = true;
                    firstValue = value;
                }
            }
        }

        protected override void OnErrorResumeCore(Exception error)
        {
            observer.OnErrorResume(error);
        }

        protected override void OnCompletedCore(Result result)
        {
            observer.OnCompleted(result);
        }

        bool IFrameRunnerWorkItem.MoveNext(long _)
        {
            if (this.IsDisposed) return false;

            lock (gate)
            {
                if (++currentFrame == frameCount)
                {
                    if (hasValue)
                    {
                        observer.OnNext(firstValue!);
                        hasValue = false;
                        firstValue = default;
                        currentFrame = 0;
                    }
                }
            }

            return true;
        }
    }
}
