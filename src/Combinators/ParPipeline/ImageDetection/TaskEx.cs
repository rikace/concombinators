﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using F = FunctionalHelpers.Functional;

namespace ImageDetection
{
    public static class TaskEx
    {
        //Listing 10.3 Task.Catch function
        public static Task<T> Catch<T, TError>(this Task<T> task, Func<TError, T> onError) where TError : Exception
        {
            var tcs = new TaskCompletionSource<T>();    // #A
            task.ContinueWith(innerTask =>
            {
                if (innerTask.IsFaulted && innerTask?.Exception?.InnerException is TError)
                    tcs.SetResult(onError((TError)innerTask.Exception.InnerException)); // #B
                else if (innerTask.IsCanceled)
                    tcs.SetCanceled();      // #B
                else if (innerTask.IsFaulted)
                    tcs.SetException(innerTask?.Exception?.InnerException ?? throw new InvalidOperationException()); // #B
                else
                    tcs.SetResult(innerTask.Result);  // #B
            });
            return tcs.Task;
        }

        //Listing 10.24  C# asynchronous lift functions
        public static Task<TOut> LifTMid<TIn, TMid, TOut>(Func<TIn, TMid, TOut> selector, Task<TIn> item1, Task<TMid> item2) // #A
        {
            // Func<TIn, Func<TMid, R>> curry = x => y => selector(x, y);    // #B

            var lifted1 = Pure(F.Curry(selector));              // #C
            var lifted2 = Apply(item1, lifted1);    // #D
            return Apply(item2, lifted2);           // #D
        }
        
        public static Task<TOut> Fmap<TIn, TOut>(this Task<TIn> input, Func<TIn, TOut> map) => input.ContinueWith(t => map(t.Result));

        public static Task<TOut> Map<TIn, TOut>(this Task<TIn> input, Func<TIn, TOut> map) => input.ContinueWith(t => map(t.Result));

        public static Task<T> Return<T>(this T input) => Task.FromResult(input);

        public static Task<T> Pure<T>(T input) => Task.FromResult(input);

        public static Task<TOut> Apply<TIn, TOut>(this Task<TIn> task, Task<Func<TIn, TOut>> liftedFn)
        {
            var tcs = new TaskCompletionSource<TOut>();
            liftedFn.ContinueWith(innerLiftTask =>
                task.ContinueWith(innerTask =>
                    tcs.SetResult(innerLiftTask.Result(innerTask.Result))
            ));
            return tcs.Task;
        }

        public static Task<TOut> Apply<TIn, TOut>(this Task<Func<TIn, TOut>> liftedFn, Task<TIn> task) => task.Apply(liftedFn);

        public static Task<Func<TMid, TOut>> Apply<TIn, TMid, TOut>(this Task<Func<TIn, TMid, TOut>> liftedFn, Task<TIn> input)
            => input.Apply(liftedFn.Fmap(F.Curry));

        public static IEnumerable<Task<R>> ProcessAsComplete<T, R>(
            this IEnumerable<T> input,
            Func<T, Task<R>> selector)
        { 
            var inputTaskList = (from el in input select selector(el)).ToList();
       
            // Could use Enumerable.Range here, if we wanted…
            var completionSourceList = new List<TaskCompletionSource<R>>(inputTaskList.Count);
            for (int i = 0; i < inputTaskList.Count; i++)
            {
                completionSourceList.Add(new TaskCompletionSource<R>());
            }

            // At any one time, this is "the index of the box we’ve just filled".
            // It would be nice to make it nextIndex and start with 0, but Interlocked.Increment
            // returns the incremented value…
            int prevIndex = -1;
            
            // TODO 4
            //
            // with a large set of Tasks running in parallel, 
            // the Task.WaitAny generates a bad performance problem
            // because the support for interleaving scenario.
            // Every call to WhenAny will result in a continuation being registered with each task, 
            // which for N tasks will amount to O(N2) continuations created over the lifetime of the interleaving operation.
            // To address that if working with a large set of tasks, we shoule use a combinatory dedicated to the goal
            //
            // To minimize the resource consumption, try to avoid the usage pf Task.WhenAny
            // Suggestion, the TaskCompletionSource (or a collection) is a good alternative

            // TODO (3.a)
            // We don’t have to create this outside the loop, but it makes it clearer
            // that the continuation is the same for all tasks.
            Action<Task<R>> continutaion = null;  // replace "null" with missing code here 
            
            foreach (var inputTask in inputTaskList)
            {
                inputTask.ContinueWith(continutaion,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            return completionSourceList.Select(source => source.Task);
        }


        public static IEnumerable<Task<T>> ProcessAsComplete_SOL<T>(this IEnumerable<Task<T>> inputTasks)
        {
            // Copy the input so we know it’ll be stable, and we don’t evaluate it twice
            var inputTaskList = inputTasks.ToList();
            // Could use Enumerable.Range here, if we wanted…
            var completionSourceList = new List<TaskCompletionSource<T>>(inputTaskList.Count);
            for (int i = 0; i < inputTaskList.Count; i++)
            {
                completionSourceList.Add(new TaskCompletionSource<T>());
            }

            // At any one time, this is "the index of the box we’ve just filled".
            // It would be nice to make it nextIndex and start with 0, but Interlocked.Increment
            // returns the incremented value…
            int prevIndex = -1;

            // We don’t have to create this outside the loop, but it makes it clearer
            // that the continuation is the same for all tasks.
            Action<Task<T>> continuation = completedTask =>
            {
                int index = Interlocked.Increment(ref prevIndex);
                var source = completionSourceList[index];
                switch (completedTask.Status)
                {
                    case TaskStatus.Canceled:
                        source.TrySetCanceled();
                        break;
                    case TaskStatus.Faulted:
                        source.TrySetException(completedTask.Exception.InnerExceptions);
                        break;
                    case TaskStatus.RanToCompletion:
                        source.TrySetResult(completedTask.Result);
                        break;
                    default:
                        throw new ArgumentException("Task was not completed");
                }
            };

            foreach (var inputTask in inputTaskList)
            {
                inputTask.ContinueWith(continuation,
                                       CancellationToken.None,
                                       TaskContinuationOptions.ExecuteSynchronously,
                                       TaskScheduler.Default);
            }

            return completionSourceList.Select(source => source.Task);
        }
    }
}
       