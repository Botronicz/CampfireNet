﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CampfireNet.Utilities.AsyncPrimatives;
using static CampfireNet.Utilities.ChannelsExtensions.ChannelsExtensions;

namespace CampfireNet.Utilities.ChannelsExtensions {
   public static class ChannelFactory {
      public static Channel<T> Nonblocking<T>() => Nonblocking<T>(-1);

      public static Channel<T> Nonblocking<T>(int maxEnqueuedSize) {
         var queue = new ConcurrentQueue<T>();
         var readSemaphore = new AsyncSemaphore(0);
         var writeSemaphore = maxEnqueuedSize > 0 ? new AsyncSemaphore(maxEnqueuedSize) : null;
         return new NonblockingChannel<T>(queue, readSemaphore, writeSemaphore);
      }

      public static Channel<T> Blocking<T>() {
         return new BlockingChannel<T>();
      }

      public static ReadableChannel<bool> Timeout(int millis) => Timeout(TimeSpan.FromMilliseconds(millis));

      public static ReadableChannel<bool> Timeout(TimeSpan interval) {
         var channel = Nonblocking<bool>(1);

         Go(async () => {
            await Task.Delay(interval).ConfigureAwait(false);
            //            Console.WriteLine("Time signalling");
            await channel.WriteAsync(true).ConfigureAwait(false);
         });

         return channel;
      }

      public static ReadableChannel<bool> Timer(int millis) => Timer(TimeSpan.FromMilliseconds(millis));

      public static ReadableChannel<bool> Timer(TimeSpan interval) {
         var channel = Blocking<bool>();

         // TODO: Timer must be disposable to prevent task leaks.
         Go(async () => {
            while (true) {
               await Task.Delay(interval).ConfigureAwait(false);
               await channel.WriteAsync(true).ConfigureAwait(false);
            }
         });

         return channel;
      }
   }

   public static class Time {
      public static ReadableChannel<bool> After(int millis) => ChannelFactory.Timeout(millis);
   }
   public class Select : INotifyCompletion, IEnumerable {
      private readonly DispatchContext dispatchContext;

      public Select(int n = 1) {
         dispatchContext = Dispatch.Times(n);
      }

      #region List Initializer Support
      public void beginConnect(ICaseTemporary c) {
         c.Register(dispatchContext);
      }

      // IEnumerable interface is required for collection initializer.
      public IEnumerator GetEnumerator() {
         throw new NotImplementedException("Attempted to enumerate on Channels Select");
      }
      #endregion

      //-------------------------------------------------------------------------------------------
      // Await option 1 - conversion to Task
      //-------------------------------------------------------------------------------------------
      public static implicit operator Task(Select select) {
         return select.WaitAsync();
      }

      public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) {
         return WaitAsync().ConfigureAwait(continueOnCapturedContext);
      }

      //-------------------------------------------------------------------------------------------
      // Await option 2 - allow await on Select object.
      // According to Stephen Cleary compilers emit this on await:
      //
      //   var temp = e.GetAwaiter();
      //   if (!temp.IsCompleted)
      //   {
      //     SAVE_STATE()
      //     temp.OnCompleted(&cont);
      //     return;
      //   
      //   cont:
      //     RESTORE_STATE()
      //   }
      //   var i = temp.GetResult();
      //
      // http://stackoverflow.com/questions/12661348/custom-awaitables-for-dummies
      //-------------------------------------------------------------------------------------------
      public Select GetAwaiter() { return this; }

      public void GetResult() { }

      public bool IsCompleted => dispatchContext.IsCompleted;

      public void OnCompleted(Action continuation) {
         dispatchContext.WaitAsync().ContinueWith(task => continuation());
      }

      public Task WaitAsync(CancellationToken cancellationToken = default(CancellationToken)) {
         return dispatchContext.WaitAsync(cancellationToken);
      }

      //-------------------------------------------------------------------------------------------
      // Syntax Option 3: Select.Case<T>(channel, callback).Case(channel, callback)...
      //-------------------------------------------------------------------------------------------
      public static DispatchContext Case<T>(ReadableChannel<T> channel, Action<T> callback) {
         return Dispatch.Once().Case(channel, callback);
      }

      public static DispatchContext Case<T>(ReadableChannel<T> channel, Func<Task> callback) {
         return Dispatch.Once().Case(channel, callback);
      }

      public static DispatchContext Case<T>(ReadableChannel<T> channel, Func<T, Task> callback) {
         return Dispatch.Once().Case(channel, callback);
      }
   }

   public static class Dispatch {
      public static DispatchContext Once() => Times(1);

      public static DispatchContext Forever() => Times(DispatchContext.kTimesInfinite);

      public static DispatchContext Times(int n) {
         return new DispatchContext(n);
      }
   }
}