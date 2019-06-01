using System;
using System.Threading.Tasks;

namespace ParallelPatterns.TaskComposition
{
    public static partial class TaskEx
    {
        // TODO (1)
        // implement missing code
        public static Task<TOut> Then<TIn, TOut>(
            this Task<TIn> task,
            Func<TIn, TOut> next)
        {
            var tcs = new TaskCompletionSource<TOut>();

            // Missing code

            return tcs.Task;
        }

        // TODO (1)
        // implement missing code
        public static Task<TOut> Then<TIn, TOut>(
            this Task<TIn> task,
            Func<TIn, Task<TOut>> next)
        {
            var tcs = new TaskCompletionSource<TOut>();

            // Missing code

            return tcs.Task;
        }

        public static Task<TOut> SelectMany<TIn, TOut>(this Task<TIn> first, Func<TIn, Task<TOut>> next)
        {
            var r = new TaskCompletionSource<TOut>();

            // Missing code

            return r.Task;
        }

        public static Task<TOut> SelectMany<TIn, TMid, TOut>(
          this Task<TIn> input, Func<TIn, Task<TMid>> f, Func<TIn, TMid, TOut> projection)
        {
            var r = new TaskCompletionSource<TOut>();

            // Missing code

            return r.Task;
        }

        public static Task<TOut> Select<TIn, TOut>(
            this Task<TIn> task,
            Func<TIn, TOut> projection)
        {
            var r = new TaskCompletionSource<TOut>();

            // Missing code

            return r.Task;
        }

    }

    //// TODO :
    //public static class TaskCompositionEx
    //{
    //    // TODO (1)
    //    // implement missing code
    //    public static Task<TOut> Then<TIn, TOut>(
    //        this Task<TIn> task,
    //        Func<TIn, TOut> next)
    //    {
    //        var tcs = new TaskCompletionSource<TOut>();
    //        task.ContinueWith(delegate
    //        {
    //            if (task.IsFaulted) tcs.TrySetException(task.Exception.InnerExceptions);
    //            else if (task.IsCanceled) tcs.TrySetCanceled();
    //            else
    //            {
    //                try
    //                {
    //                    tcs.SetResult(next(task.Result));
    //                }
    //                catch (Exception exc)
    //                {
    //                    tcs.TrySetException(exc);
    //                }
    //            }
    //        }, TaskContinuationOptions.ExecuteSynchronously);
    //        return tcs.Task;
    //    }

    //    // TODO (1)
    //    // implement missing code
    //    public static Task<TOut> Then<TIn, TOut>(
    //        this Task<TIn> task,
    //        Func<TIn, Task<TOut>> next)
    //    {
    //        var tcs = new TaskCompletionSource<TOut>();
    //        task.ContinueWith(delegate
    //        {
    //            if (task.IsFaulted) tcs.TrySetException(task.Exception.InnerExceptions);
    //            else if (task.IsCanceled) tcs.TrySetCanceled();
    //            else
    //            {
    //                try
    //                {
    //                    var t = next(task.Result);
    //                    if (t == null) tcs.TrySetCanceled();
    //                    else
    //                        t.ContinueWith(delegate
    //                        {
    //                            if (t.IsFaulted) tcs.TrySetException(t.Exception.InnerExceptions);
    //                            else if (t.IsCanceled) tcs.TrySetCanceled();
    //                            else tcs.TrySetResult(t.Result);
    //                        }, TaskContinuationOptions.ExecuteSynchronously);
    //                }
    //                catch (Exception exc)
    //                {
    //                    tcs.TrySetException(exc);
    //                }
    //            }
    //        }, TaskContinuationOptions.ExecuteSynchronously);
    //        return tcs.Task;
    //    }


    //public static Task<TOut> SelectMany<TIn, TOut>(this Task<TIn> first, Func<TIn, Task<TOut>> next)
    //{
    //    var tcs = new TaskCompletionSource<TOut>();
    //    first.ContinueWith(delegate
    //    {
    //        if (first.IsFaulted) tcs.TrySetException(first.Exception.InnerExceptions);
    //        else if (first.IsCanceled) tcs.TrySetCanceled();
    //        else
    //        {
    //            try
    //            {
    //                var t = next(first.Result);
    //                if (t == null) tcs.TrySetCanceled();
    //                else t.ContinueWith(delegate
    //                {
    //                    if (t.IsFaulted) tcs.TrySetException(t.Exception.InnerExceptions);
    //                    else if (t.IsCanceled) tcs.TrySetCanceled();
    //                    else tcs.TrySetResult(t.Result);
    //                }, TaskContinuationOptions.ExecuteSynchronously);
    //            }
    //            catch (Exception exc) { tcs.TrySetException(exc); }
    //        }
    //    }, TaskContinuationOptions.ExecuteSynchronously);
    //    return tcs.Task;
    //}

    //public static Task<TOut> SelectMany<TIn, TMid, TOut>(
    //  this Task<TIn> input, Func<TIn, Task<TMid>> f, Func<TIn, TMid, TOut> projection)
    //{
    //    return Bind(input, outer =>
    //           Bind(f(outer), inner =>
    //           Return(projection(outer, inner))));
    //}


    //    public static Task<TOut> Select<TIn, TOut>(
    //        this Task<TIn> task,
    //        Func<TIn, TOut> projection)
    //    {
    //        var r = new TaskCompletionSource<TOut>();
    //        task.ContinueWith(self =>
    //        {
    //            if (self.IsFaulted) r.SetException(self.Exception.InnerExceptions);
    //            else if (self.IsCanceled) r.SetCanceled();
    //            else r.SetResult(projection(self.Result));
    //        });
    //        return r.Task;
    //    }

    //    public static Task<TOut> SelectMany<TIn, TOut>(
    //        this Task<TIn> first,
    //        Func<TIn, Task<TOut>> next)
    //    {
    //        var tcs = new TaskCompletionSource<TOut>();
    //        first.ContinueWith(delegate
    //        {
    //            if (first.IsFaulted) tcs.TrySetException(first.Exception.InnerExceptions);
    //            else if (first.IsCanceled) tcs.TrySetCanceled();
    //            else
    //            {
    //                try
    //                {
    //                    var t = next(first.Result);
    //                    if (t == null) tcs.TrySetCanceled();
    //                    else
    //                        t.ContinueWith(nextT =>
    //                        {
    //                            if (nextT.IsFaulted) tcs.TrySetException(nextT.Exception.InnerExceptions);
    //                            else if (nextT.IsCanceled) tcs.TrySetCanceled();
    //                            else tcs.TrySetResult(nextT.Result);
    //                        }, TaskContinuationOptions.ExecuteSynchronously);
    //                }
    //                catch (Exception exc)
    //                {
    //                    tcs.TrySetException(exc);
    //                }
    //            }
    //        }, TaskContinuationOptions.ExecuteSynchronously);
    //        return tcs.Task;
    //    }

    //    public static Task<TOut> SelectMany<TIn, TMid, TOut>(
    //        this Task<TIn> input,
    //        Func<TIn, Task<TMid>> f,
    //        Func<TIn, TMid, TOut> projection)
    //        => SelectMany(input, outer =>
    //            SelectMany(f(outer), inner =>
    //                Task.FromResult(projection(outer, inner))
    //            )
    //           );

    //}
}