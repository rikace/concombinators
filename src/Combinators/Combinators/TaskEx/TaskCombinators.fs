module Combinators.TaskEx.TaskCombinators

open System
open System.Threading
open System.Threading.Tasks
open System.Collections.Generic
open System.Collections.Concurrent
open FSharp.Parallelx

open System.Threading.Tasks
open FSharp.Parallelx.ContextInsensitive


[<RequireQualifiedAccess>]
module TaskV2 =
  let singleton value = value |> Task.FromResult

  let bind (f : 'a -> Task<'b>) (x : Task<'a>) = task {
      let! x = x
      return! f x
  }

  let apply f x =
    bind (fun f' ->
      bind (fun x' -> singleton(f' x')) x) f

  let map f x = x |> bind (f >> singleton)

  let map2 f x y =
    (apply (apply (singleton f) x) y)

  let map3 f x y z =
    apply (map2 f x y) z

    
[<RequireQualifiedAccess>]
module Task =
        
    let inline create (a:'a) : Task<'a> =
        Task.FromResult a
    
    let inline join (t:Task<Task<'a>>) : Task<'a> =
        t.Unwrap()
    
    let inline extend (f:Task<'a> -> 'b) (t:Task<'a>) : Task<'b> =
        t.ContinueWith f
    
    let inline map (f:'a -> 'b) (t:Task<'a>) : Task<'b> =
        extend (fun t -> f t.Result) t
    
    let inline bind (f:'a -> Task<'b>) (t:Task<'a>) : Task<'b> =
        extend (fun t -> f t.Result) t |> join
        
    /// Transforms a Task's first value by using a specified mapping function.
    let inline map' f (m: Task<_>) =
        m.ContinueWith(fun (t: Task<_>) -> f t.Result)

    let inline bindWithOptions (token: CancellationToken) (continuationOptions: TaskContinuationOptions) (scheduler: TaskScheduler) (f: 'T -> Task<'U>) (m: Task<'T>) =
        m.ContinueWith((fun (x: Task<_>) -> f x.Result), token, continuationOptions, scheduler).Unwrap()

    let inline bind' (f: 'T -> Task<'U>) (m: Task<'T>) =
        m.ContinueWith(fun (x: Task<_>) -> f x.Result).Unwrap()

    let inline returnM a =
        let s = TaskCompletionSource()
        s.SetResult a
        s.Task

    /// Promote a function to a monad/applicative, scanning the monadic/applicative arguments from left to right.
    let inline lift2 f a b =
        // a >>= fun aa -> b >>= fun bb -> f aa bb |> returnM        
        bind (fun aa -> bind(fun bb -> f aa bb |> returnM) b) a 

    /// Sequential application
    let inline apply f x = lift2 id f x
        
    let inline kleisli (f:'a -> Task<'b>) (g:'b -> Task<'c>) (x:'a) = bind g (f x)  // (f x) >>= g
     
module TaskOperators =
//    let inline (<!>) f x = TaskV2.map f x
//    let inline (<*>) f x = TaskV2.apply f x
//    let inline (>>=) x f = TaskV2.bind f x
    
    /// Sequential application
    let inline (<*>) f x = Task.apply x f

    /// Infix map
    let inline (<!>) f x = Task.map f x

    /// Sequence actions, discarding the value of the first argument.
    let inline ( *>) a b = Task.lift2 (fun _ z -> z) a b

    /// Sequence actions, discarding the value of the second argument.
    let inline ( <*) a b = Task.lift2 (fun z _ -> z) a b
        
    /// Sequentially compose two actions, passing any value produced by the first as an argument to the second.
    let inline (>>=) m f = Task.bind f m

    /// Flipped >>=
    let inline (=<<) f m = Task.bind f m

    /// Left-to-right Kleisli composition
    let inline (>=>) f g = fun x -> f x >>= g
    
    /// Right-to-left Kleisli composition
    let inline (<=<) x = flip (>=>) x
    
    
module TaskResult =
   open System
   
   type TaskResult<'a> = | TR of Task<Result<'a, exn>>
   
   let ofTaskResult (TR x) = x
   let arr f = TR f
   
   let handler (operation : Task<'a>) =
       let tcs = new TaskCompletionSource<Result<'a, exn>>()
       operation.ContinueWith(
          new Action<Task>(fun _ ->
           if operation.IsFaulted then tcs.SetResult(Result.Error (operation.Exception.InnerException))
            else if operation.IsCanceled then tcs.SetResult(Result.Error (new OperationCanceledException() :> Exception))
            else tcs.SetResult(Result.Ok(operation.Result)))) |> ignore
       tcs.Task |> TR
        
   let map (f: 'a -> 'b) (task : TaskResult<'a>) =
       let operation = (ofTaskResult task)
       let tcs = new TaskCompletionSource<Result<'b, exn>>()
       operation.ContinueWith(fun (t: Task<Result<'a, exn>>) ->
           new Action<Task>(fun _ ->
               if operation.IsFaulted then tcs.SetResult(Result.Error (operation.Exception.InnerException))
               else if operation.IsCanceled then tcs.SetResult(Result.Error (new OperationCanceledException() :> Exception))
               else
                   match t.Result with
                   | Ok res ->  tcs.SetResult(Result.Ok(f res))
                   | Error err -> tcs.SetResult(Result.Error(err))
           )) |> ignore
       tcs.Task |> TR               

   let bind (f: 'a -> TaskResult<'b>) (task : TaskResult<'a>) =
       let operation = (ofTaskResult task)
       let tcs = new TaskCompletionSource<Result<'b, exn>>()
       operation.ContinueWith(fun (t1: Task<Result<'a, exn>>) ->
           new Action<Task>(fun _ ->
               if operation.IsFaulted then tcs.SetResult(Result.Error (operation.Exception.InnerException))
               else if operation.IsCanceled then tcs.SetResult(Result.Error (new OperationCanceledException() :> Exception))
               else
                   match t1.Result with
                   | Ok res -> ((f res) |> ofTaskResult)
                                   .ContinueWith(fun (t2: Task<Result<'b, exn>>) ->
                                            if t2.IsFaulted then tcs.SetResult(Result.Error (operation.Exception.InnerException))
                                            else if t2.IsCanceled then tcs.SetResult(Result.Error (new OperationCanceledException() :> Exception))
                                            else
                                                match t2.Result with
                                                | Error err -> tcs.SetResult(Result.Error(err))
                                                | Ok res -> tcs.SetResult(Result.Ok res)) |> ignore
                   | Error err -> tcs.SetResult(Result.Error(err))
           )) |> ignore
       tcs.Task |> TR
       
       

       
            public static Task<TSource> Where<TSource>(this Task<TSource> source, Func<TSource, bool> predicate)
        {
            // Validate arguments
            if (source == null) throw new ArgumentNullException("source");
            if (predicate == null) throw new ArgumentNullException("predicate");

            // Create a continuation to run the predicate and return the source's result.
            // If the predicate returns false, cancel the returned Task.
            var cts = new CancellationTokenSource();
            return source.ContinueWith(t =>
            {
                var result = t.Result;
                if (!predicate(result)) cts.CancelAndThrow();
                return result;
            }, cts.Token, TaskContinuationOptions.NotOnCanceled, TaskScheduler.Default);
        }
        
        
        
              public static Task<T> OrElse<T>
         (this Task<T> task, Func<Task<T>> fallback)
         => task.ContinueWith(t =>
               t.Status == TaskStatus.Faulted
                  ? fallback()
                  : Task.FromResult(t.Result)
            )
            .Unwrap();


      public static Task<T> Recover<T>
         (this Task<T> task, Func<Exception, T> fallback)
         => task.ContinueWith(t =>
               t.Status == TaskStatus.Faulted
                  ? fallback(t.Exception)
                  : t.Result);

      public static Task<T> RecoverWith<T>
         (this Task<T> task, Func<Exception, Task<T>> fallback)
         => task.ContinueWith(t =>
               t.Status == TaskStatus.Faulted
                  ? fallback(t.Exception)
                  : Task.FromResult(t.Result)
         ).Unwrap();
         
         
         
      static Func<IEnumerable<T>, T, IEnumerable<T>> Append<T>()
         => (ts, t) => ts.Append(t);

      // Task
      
      // applicative traverse
      public static Task<IEnumerable<R>> TraverseA<T, R>
         (this IEnumerable<T> ts, Func<T, Task<R>> f)
         => ts.Aggregate(
            seed: Task.FromResult(Enumerable.Empty<R>()),
            func: (rs, t) => Task.FromResult(Append<R>())
                                   .Apply(rs)
                                   .Apply(f(t)));

      // by default use applicative traverse (parallel, hence faster)
      public static Task<IEnumerable<R>> Traverse<T, R>(this IEnumerable<T> list, Func<T, Task<R>> func) => TraverseA(list, func);

      // monadic traverse
      public static Task<IEnumerable<R>> TraverseM<T, R>
         (this IEnumerable<T> ts, Func<T, Task<R>> func)
         => ts.Aggregate(
            seed: Task.FromResult(Enumerable.Empty<R>()),
            // Task<[R]> -> T -> Task<[R]>
            func: (taskRs, t) => from rs in taskRs
                                 from r in func(t)
                                 select rs.Append(r));
   }
   
   
         public static Task<Option<R>> Traverse<T, R>
         (this Option<T> @this, Func<T, Task<R>> func)
         => @this.Match(
               None: () => Async((Option<R>)None),
               Some: t => func(t).Map(Some)
            );

      public static Task<Option<R>> TraverseBind<T, R>(this Option<T> @this
         , Func<T, Task<Option<R>>> func)
         => @this.Match(
               None: () => Async((Option<R>)None),
               Some: t => func(t)
            );       