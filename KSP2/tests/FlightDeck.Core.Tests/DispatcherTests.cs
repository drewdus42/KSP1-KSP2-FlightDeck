using System;
using System.Diagnostics;
using System.Threading;
using FlightDeck.Core;
using Xunit;

namespace FlightDeck.Core.Tests
{
    public class DispatcherTests
    {
        [Fact]
        public void WorkPostedFromAnotherThreadRunsDuringPump()
        {
            var dispatcher = new QueueDispatcher();
            object result = null;
            Exception failure = null;

            var worker = new Thread(delegate()
            {
                try
                {
                    result = dispatcher.Invoke(delegate { return "pumped"; }, 5000);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            worker.Start();

            var stopwatch = Stopwatch.StartNew();
            while (dispatcher.PendingCount == 0 && stopwatch.ElapsedMilliseconds < 3000)
            {
                Thread.Sleep(5);
            }

            Assert.Equal(1, dispatcher.Pump());
            worker.Join(5000);

            Assert.Null(failure);
            Assert.Equal("pumped", result);
        }

        [Fact]
        public void InvokeTimesOutWhenTheGameNeverPumps()
        {
            var dispatcher = new QueueDispatcher();
            Exception failure = null;

            var worker = new Thread(delegate()
            {
                try
                {
                    dispatcher.Invoke(delegate { return 1; }, 100);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            worker.Start();
            worker.Join(5000);

            Assert.IsType<TimeoutException>(failure);
        }

        [Fact]
        public void ExceptionsFromGameCodeReachTheCaller()
        {
            var dispatcher = new QueueDispatcher();
            Exception failure = null;

            var worker = new Thread(delegate()
            {
                try
                {
                    dispatcher.Invoke<int>(delegate { throw new ApiException("boom", "no"); }, 5000);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            worker.Start();

            var stopwatch = Stopwatch.StartNew();
            while (dispatcher.PendingCount == 0 && stopwatch.ElapsedMilliseconds < 3000)
            {
                Thread.Sleep(5);
            }

            dispatcher.Pump();
            worker.Join(5000);

            Assert.IsType<ApiException>(failure);
            Assert.Equal("boom", ((ApiException)failure).Code);
        }

        [Fact]
        public void TimedOutWorkIsNeverExecuted()
        {
            var dispatcher = new QueueDispatcher();
            bool executed = false;

            var worker = new Thread(delegate()
            {
                try
                {
                    dispatcher.Invoke<bool>(delegate { executed = true; return true; }, 50);
                }
                catch (TimeoutException)
                {
                    // Expected: the game loop is frozen.
                }
            });

            worker.Start();
            worker.Join(5000);

            Assert.Equal(1, dispatcher.PendingCount);
            dispatcher.Pump();

            Assert.False(executed);
            Assert.Equal(0, dispatcher.PendingCount);
        }

        [Fact]
        public void MainThreadCallsRunInline()
        {
            var dispatcher = new QueueDispatcher();
            int value = dispatcher.Invoke(delegate { return 7; }, 100);

            Assert.Equal(7, value);
            Assert.Equal(0, dispatcher.PendingCount);
        }
    }
}
