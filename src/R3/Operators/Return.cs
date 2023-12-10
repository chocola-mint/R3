﻿namespace R3
{
    public static partial class EventFactory
    {
        public static CompletableEvent<TMessage, Unit> Return<TMessage>(TMessage value)
        {
            return new ImmediateScheduleReturn<TMessage>(value); // immediate
        }

        public static CompletableEvent<TMessage, Unit> Return<TMessage>(TMessage value, TimeProvider timeProvider)
        {
            return Return(value, TimeSpan.Zero, timeProvider);
        }

        public static CompletableEvent<TMessage, Unit> Return<TMessage>(TMessage value, TimeSpan dueTime, TimeProvider timeProvider)
        {
            if (dueTime == TimeSpan.Zero)
            {
                if (timeProvider == TimeProvider.System)
                {
                    return new ThreadPoolScheduleReturn<TMessage>(value, null); // optimize for SystemTimeProvidr, use ThreadPool.UnsafeQueueUserWorkItem
                }
                else if (timeProvider is SafeTimerTimeProvider t && t.IsSystemTimeProvider)
                {
                    return new ThreadPoolScheduleReturn<TMessage>(value, t.UnhandledExceptionHandler); // use with SafeTimeProvider.UnhandledExceptionHandler
                }
            }

            return new Return<TMessage>(value, dueTime, timeProvider); // use ITimer
        }
    }
}

namespace R3.Operators
{
    internal class Return<TMessage>(TMessage value, TimeSpan dueTime, TimeProvider timeProvider) : CompletableEvent<TMessage, Unit>
    {
        protected override IDisposable SubscribeCore(Subscriber<TMessage, Unit> subscriber)
        {
            var method = new _Return(value, subscriber);
            method.Timer = timeProvider.CreateStoppedTimer(_Return.timerCallback, method);
            method.Timer.InvokeOnce(dueTime);
            return method;
        }

        sealed class _Return(TMessage value, Subscriber<TMessage, Unit> subscriber) : IDisposable
        {
            public static readonly TimerCallback timerCallback = NextTick;

            readonly TMessage value = value;
            readonly Subscriber<TMessage, Unit> subscriber = subscriber;

            public ITimer? Timer { get; set; }

            static void NextTick(object? state)
            {
                var self = (_Return)state!;
                try
                {
                    self.subscriber.OnNext(self.value);
                    self.subscriber.OnCompleted();
                }
                finally
                {
                    self.Dispose();
                }
            }

            public void Dispose()
            {
                Timer?.Dispose();
                Timer = null;
            }
        }
    }

    internal class ImmediateScheduleReturn<TMessage>(TMessage value) : CompletableEvent<TMessage, Unit>
    {
        protected override IDisposable SubscribeCore(Subscriber<TMessage, Unit> subscriber)
        {
            subscriber.OnNext(value);
            subscriber.OnCompleted();
            return Disposable.Empty;
        }
    }

    internal class ThreadPoolScheduleReturn<TMessage>(TMessage value, Action<Exception>? unhandledExceptionHandler) : CompletableEvent<TMessage, Unit>
    {
        protected override IDisposable SubscribeCore(Subscriber<TMessage, Unit> subscriber)
        {
            var method = new _Return(value, unhandledExceptionHandler, subscriber);
            ThreadPool.UnsafeQueueUserWorkItem(method, preferLocal: false);
            return method;
        }

        sealed class _Return(TMessage value, Action<Exception>? unhandledExceptionHandler, Subscriber<TMessage, Unit> subscriber) : IDisposable, IThreadPoolWorkItem
        {
            bool stop;

            public void Execute()
            {
                if (stop) return;

                try
                {
                    subscriber.OnNext(value);
                    subscriber.OnCompleted();
                }
                catch (Exception ex)
                {
                    if (unhandledExceptionHandler == null)
                    {
                        throw;
                    }
                    else
                    {
                        unhandledExceptionHandler?.Invoke(ex);
                    }
                }
            }

            public void Dispose()
            {
                stop = true;
            }
        }
    }
}