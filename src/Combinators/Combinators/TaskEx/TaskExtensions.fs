module Combinators.TaskEx.TaskExtensions

open System
open System.Runtime.CompilerServices
open System.Threading
open System.Threading.Tasks
open FSharp.Parallelx
      
type TaskResult<'T> =
    /// Task was canceled
    | Canceled
    /// Unhandled exception in task
    | Error of exn
    /// Task completed successfully
    | Successful of 'T
    with
    static member run (t: unit -> Task<_>) =
        try
            let task = t()
            task.Result |> TaskResult.Successful
        with
        | :? OperationCanceledException -> TaskResult.Canceled
        | :? AggregateException as e ->
            match e.InnerException with
            | :? TaskCanceledException -> TaskResult.Canceled
            | _ -> TaskResult.Error e
        | e -> TaskResult.Error e

module TaskExtensions =
        
    let toTaskUnit (t:Task) =
      let continuation _ = ()
      t.ContinueWith continuation        
  
    /// Creates a task that executes a specified task.
    /// If this task completes successfully, then this function returns Choice1Of2 with the returned value.
    /// If this task raises an exception before it completes then return Choice2Of2 with the raised exception.
    let catch (t:Task<'a>) =
        task {
            try
                let! r = t
                return Choice1Of2 r
            with e ->
                return Choice2Of2 e
        }
    
    let inline private applyTask fromInc toExc stride f =        
        let mutable i = fromInc
        while i < toExc do
            f i
            i <- i + stride
            
    type System.Threading.Tasks.Task with
        // Creates a task that executes all the given tasks.
        static member Parallel (tasks : seq<unit -> Task<'a>>) =
            tasks
            |> Seq.map (fun t -> t())
            |> Array.ofSeq
            |> Task.WhenAll
        
        /// Creates a task that executes all the given tasks.
        /// The paralelism is throttled, so that at most `throttle` tasks run at one time.
        static member ParallelWithTrottle throttle (tasks : seq<unit -> Task<'a>>) : (Task<'a[]>) =
            let semaphore = new SemaphoreSlim(throttle)
            let throttleTask (t:unit->Task<'a>) () : Task<'a> =
                task {
                    do! semaphore.WaitAsync() |> toTaskUnit
                    let! result = catch <| t()
                    semaphore.Release() |> ignore
                    return match result with
                           | Choice1Of2 r -> r
                           | Choice2Of2 e -> raise e
                }
            tasks
            |> Seq.map throttleTask
            |> Task.Parallel
        
        /// Returns a cancellation token which is cancelled when the IVar is set.
        static member intoCancellationToken (cts:CancellationTokenSource) (t:Task<_>) =
            t.ContinueWith (fun (t:Task<_>) -> cts.Cancel ()) |> ignore
    
        /// Returns a cancellation token which is cancelled when the IVar is set.
        static member asCancellationToken (t:Task<_>) =
            let cts = new CancellationTokenSource ()
            Task.intoCancellationToken cts t
            cts.Token
    

                
        static member ForStride (fromInclusive : int) (toExclusive :int) (stride : int) (f : int -> unit) =                
            let numStrides = (toExclusive-fromInclusive)/stride
            if numStrides > 0 then
                let numTasks = Math.Min(Environment.ProcessorCount,numStrides)
                let stridesPerTask = numStrides/numTasks
                let elementsPerTask = stridesPerTask * stride;
                let mutable remainderStrides = numStrides - (stridesPerTask*numTasks)
                    
                let taskArray : Task[] = Array.zeroCreate numTasks
                let mutable index = 0    
                for i = 0 to taskArray.Length-1 do        
                    let toExc =
                        if remainderStrides = 0 then
                            index + elementsPerTask
                        else
                            remainderStrides <- remainderStrides - 1
                            index + elementsPerTask + stride
                    let fromInc = index;            
                
                    taskArray.[i] <- Task.Factory.StartNew(fun () -> applyTask fromInc toExc stride f)                        
                    index <- toExc
                                
                Task.WaitAll(taskArray)        
        
        static member inline ofUnit (t : Task)=
          let inline cont (t : Task) =
              if t.IsFaulted
              then raise t.Exception
              else ()
          t.ContinueWith cont

        static member toAsync (t: Task<'T>): Async<'T> =
            let abegin (cb: AsyncCallback, state: obj) : IAsyncResult =
                match cb with
                | null -> upcast t
                | cb ->
                    t.ContinueWith(fun (_ : Task<_>) -> cb.Invoke t) |> ignore
                    upcast t
            let aend (r: IAsyncResult) =
                (r :?> Task<'T>).Result
            Async.FromBeginEnd(abegin, aend)
        
        static member asAsync (task: Task<'T>, token: CancellationToken option) =
          Async.FromContinuations(
            fun (completed, caught, canceled) ->
              let token = defaultArg token Async.DefaultCancellationToken
              task.ContinueWith(
                new Action<Task<'T>>(fun _ ->
                  if task.IsFaulted then caught(task.Exception)
                  else if task.IsCanceled then canceled(new OperationCanceledException(token) |> raise)
                  else completed(task.Result)),
                  token)
              |> ignore)          

        static member asAsync (task: Task, token: CancellationToken option) =
          Async.FromContinuations(
            fun (completed, caught, canceled) ->
              let token = defaultArg token Async.DefaultCancellationToken
              task.ContinueWith(
                new Action<Task>(fun _ ->
                  if task.IsFaulted then caught(task.Exception)
                  else if task.IsCanceled then canceled(new OperationCanceledException(token) |> raise)
                  else completed()),
                  token)
              |> ignore)     
    
    [<Extension>]
    type TaskInteropExtensions =
        [<Extension>]
        static member AsAsync (task: Task) =
          Task.asAsync (task, None)
  
        [<Extension>]
        static member AsAsync (task: Task, token: CancellationToken) =
          Task.asAsync (task, Some token)
  
        [<Extension>]
        static member AsAsync (task: Task<'T>) =
          Task.asAsync (task, None)
      
        [<Extension>]
        static member AsAsync (task: Task<'T>, token: CancellationToken) =
          Task.asAsync (task, Some token)
  
