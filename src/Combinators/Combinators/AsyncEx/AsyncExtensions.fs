module Combinators.AsyncEx.AsyncExtensions


open System
open System.Threading 
open System.Threading.Tasks
open System.Runtime.CompilerServices
open FSharp.Parallelx
 
module AsyncExtensions =

  type IAsyncGate = 
    abstract Acquire : int option -> Async<IDisposable>
//  type FSharp.Control.Async<'a> with
//      member a.ToObservable() =
//          let subject = new AsyncSubject<'a>()
//          Async.StartWithContinuations(a,
//              (fun res -> subject.OnNext(res)
//                          subject.OnCompleted()),
//              (fun exn -> subject.OnError(exn)),
//              (fun cnl -> ()))
//          subject :> IObservable<'a>    
    
  type Microsoft.FSharp.Control.Async with
       
    static member Callcc (f: ('a -> Async<'b>) -> Async<'a>) : Async<'a> =
        Async.FromContinuations(fun (cont, econt, ccont) ->
            Async.StartWithContinuations(f (fun a -> Async.FromContinuations(fun (_, _, _) -> cont a)), cont, econt, ccont))            
        
  //            let sum l =
  //              let rec sum l = async {
  //                let! result = callcc (fun exit1 -> async {
  //                  match l with
  //                  | [] -> return 0
  //                  | h::t when h = 2 -> return! exit1 42
  //                  | h::t -> let! r = sum t
  //                            return h + r })
  //                return result }
  //              Async.RunSynchronously(sum l)
        
    static member AwaitTaskCorrect(task : Task) : Async<unit> =
      Async.FromContinuations(fun (sc,ec,cc) ->
          task.ContinueWith(fun (task:Task) ->
              if task.IsFaulted then
                  let e = task.Exception
                  if e.InnerExceptions.Count = 1 then ec e.InnerExceptions.[0]
                  else ec e
              elif task.IsCanceled then
                  ec(TaskCanceledException())
              else
                  sc ())
          |> ignore)

    static member AwaitTaskUnitCancellationAsError (t:Task) : Async<unit> =
      Async.FromContinuations <| fun (ok,err,_) ->
        t.ContinueWith (fun (t:Task) ->
          if t.IsFaulted then err t.Exception
          elif t.IsCanceled then err (OperationCanceledException("Task wrapped with Async has been cancelled."))
          elif t.IsCompleted then ok ()
          else failwith "invalid Task state!") |> ignore
  
    static member AwaitTaskCancellationAsError (t:Task<'a>) : Async<'a> =
      Async.FromContinuations <| fun (ok,err,_) ->
        t.ContinueWith (fun (t:Task<'a>) ->
          if t.IsFaulted then err t.Exception
          elif t.IsCanceled then err (OperationCanceledException("Task wrapped with Async has been cancelled."))
          elif t.IsCompleted then ok t.Result
          else failwith "invalid Task state!") |> ignore
    
    /// Starts the specified operation using a new CancellationToken and returns
    /// IDisposable object that cancels the computation. This method can be used
    /// when implementing the Subscribe method of IObservable interface.
    static member StartDisposable(op:Async<unit>) =
      let ct = new System.Threading.CancellationTokenSource()
      Async.Start(op, ct.Token)
      { new IDisposable with
            member x.Dispose() = ct.Cancel() }
  
    static member StartThreadPoolWithContinuations (a:Async<'a>, ok:'a -> unit, err:exn -> unit, cnc:OperationCanceledException -> unit, ct:CancellationToken) =
      let bind (f:'a -> Async<'b>) (a:Async<'a>) : Async<'b> = async.Bind(a, f)
      let op = Async.SwitchToThreadPool () |> bind (fun _ -> a)
      Async.StartWithContinuations (op, ok, err, cnc, ct)
      
    static member ParallelWithThrottle (millisecondsTimeout : int) (limit : int) (items : 'a seq)
                    (operation : 'a -> Async<'b>) =
      let semaphore = new SemaphoreSlim(limit, limit)
      let mutable count = (items |> Seq.length)
      items
      |> Seq.map (fun item ->
              async {
                  let! isHandleAquired = Async.AwaitTask
                                          <| semaphore.WaitAsync(millisecondsTimeout = millisecondsTimeout)
                  if isHandleAquired then
                      try
                          return! operation item
                      finally
                          if Interlocked.Decrement(&count) = 0 then semaphore.Dispose()
                          else semaphore.Release() |> ignore
                  else return! failwith "Failed to acquire handle"
              })
      |> Async.Parallel
    
    /// Starts the specified operation using a new CancellationToken and returns
    /// IDisposable object that cancels the computation.
    static member StartCancelableDisposable(computation:Async<unit>) =
        let cts = new System.Threading.CancellationTokenSource()
        Async.Start(computation, cts.Token)
        { new IDisposable with member x.Dispose() = cts.Cancel() }

    static member StartContinuation (cont: 'a -> unit) (computation:Async<'a>) =
        Async.StartWithContinuations(computation,
            (fun res-> cont(res)),
            (ignore),
            (ignore))

    static member Map (map:'a -> 'b) (x:Async<'a>) = async {let! r = x in return map r}

    static member Tap (action:'a -> 'b) (x:Async<'a>) = (Async.Map action x) |> Async.Ignore|> Async.Start; x
        
  
    /// Creates an async computation which runs the provided sequence of computations and completes
    /// when all computations in the sequence complete. Up to parallelism computations will
    /// be in-flight at any given point in time. Error or cancellation of any computation in
    /// the sequence causes the resulting computation to error or cancel, respectively.
    static member ParallelThrottledIgnore (startOnCallingThread:bool, parallelism:int, xs:seq<Async<_>>) = async {
        let! ct = Async.CancellationToken
        let sm = new SemaphoreSlim(parallelism)
        let count = ref 1
        let res = TaskCompletionSource<_>()
        let tryWait () =
          try sm.Wait () ; true
          with _ -> false
        let tryComplete () =
          if Interlocked.Decrement count = 0 then
            res.TrySetResult() |> ignore
            false
          else
            not res.Task.IsCompleted
        let ok _ =
          if tryComplete () then
            try sm.Release () |> ignore with _ -> ()
        let err (ex:exn) = res.TrySetException ex |> ignore
        let cnc (_:OperationCanceledException) = res.TrySetCanceled() |> ignore
        let start = async {
          use en = xs.GetEnumerator()
          while not (res.Task.IsCompleted) && en.MoveNext() do
            if tryWait () then
              Interlocked.Increment count |> ignore
              if startOnCallingThread then Async.StartWithContinuations (en.Current, ok, err, cnc, ct)
              else Async.StartThreadPoolWithContinuations (en.Current, ok, err, cnc, ct)
          tryComplete () |> ignore }
        Async.Start (async.TryWith(start, (err >> async.Return)), ct)
        return! res.Task |> Async.AwaitTask }

  
    static member ParallelThrottledIgnore (parallelism:int, xs:seq<Async<_>>) =
      Async.ParallelThrottledIgnore(true, parallelism, xs)
  
    static member ParallelThrottledThread (startOnCallingThread:bool, parallelism:int, xs:Async<'a>[]) : Async<'a[]> = async {
      let rs = Array.zeroCreate xs.Length
      let xs =
        xs
        |> Seq.mapi (fun i comp -> async {
          let! a = comp
          rs.[i] <- a })
      do! Async.ParallelThrottledIgnore(startOnCallingThread, parallelism, xs)
      return rs }
  
    static member  ParallelThrottled (parallelism:int, xs:Async<'a>[]) : Async<'a[]> =
      Async.ParallelThrottledThread(true, parallelism, xs)

    /// Creates an async computation which completes when any of the argument computations completes.
    /// The other computation is cancelled.
    static member Choose (a:Async<'a>) (b:Async<'a>) : Async<'a> = async {
      let! ct = Async.CancellationToken
      return!
        Async.FromContinuations <| fun (ok,err,cnc) ->
          let state = ref 0
          let cts = CancellationTokenSource.CreateLinkedTokenSource ct
          let cancel () =
            cts.Cancel()
            // cts.Dispose()
          let ok a =
            if (Interlocked.CompareExchange(state, 1, 0) = 0) then 
              ok a
              cancel ()
          let err (ex:exn) =
            if (Interlocked.CompareExchange(state, 1, 0) = 0) then 
              cancel ()
              err ex
          let cnc ex =
            if (Interlocked.CompareExchange(state, 1, 0) = 0) then 
              cancel ()
              cnc ex
          Async.StartThreadPoolWithContinuations (a, ok, err, cnc, cts.Token)
          Async.StartThreadPoolWithContinuations (b, ok, err, cnc, cts.Token) }      
    
    static member RequestGate(n:int) =
        let sem = new System.Threading.SemaphoreSlim(n, n)
        { new IAsyncGate with 
            member x.Acquire(?timeout) =
              let timeout =
                match timeout with
                | None -> TimeSpan.MaxValue
                | Some t -> TimeSpan.FromMilliseconds(float t)
              async {
                let! ok = sem.WaitAsync(timeout=timeout) |> Async.AwaitTask
                if (ok) then
                    return
                        { new System.IDisposable with
                            member x.Dispose() =
                                sem.Release() |> ignore }
                else
                    return! failwith "Couldn't acquire Gate" }            
        }                

            
    static member Parallel2 (c1, c2) : Async<'a * 'b> = async {
      let! c1 = c1 |> Async.StartChild
      let! c2 = c2 |> Async.StartChild
      let! c1 = c1
      let! c2 = c2
      return c1,c2 }

    static member  Parallel3 (c1, c2, c3) : Async<'a * 'b * 'c> = async {
      let! c1 = c1 |> Async.StartChild
      let! c2 = c2 |> Async.StartChild
      let! c3 = c3 |> Async.StartChild
      let! c1 = c1
      let! c2 = c2
      let! c3 = c3
      return c1,c2,c3 }

    static member Unb (a:Async<'a>) (b:Async<'a>) : Async<'a * Async<'a>> = async {
        let! ct = Async.CancellationToken
        return!
          Async.FromContinuations <| fun (ok,err,cnc) ->
            let state = ref 0
            let iv = new TaskCompletionSource<_>()
            let ok a =
              if (Interlocked.CompareExchange(state, 1, 0) = 0) then
                ok (a, iv.Task |> Async.AwaitTask)
              else
                iv.SetResult a
            let err (ex:exn) =
              if (Interlocked.CompareExchange(state, 1, 0) = 0) then err ex
              else iv.SetException ex
            let cnc ex =
              if (Interlocked.CompareExchange(state, 1, 0) = 0) then cnc ex
              else iv.SetCanceled ()
            Async.StartThreadPoolWithContinuations (a, ok, err, cnc, ct)
            Async.StartThreadPoolWithContinuations (b, ok, err, cnc, ct)
      }
    
    static member Cache (a:Async<'a>) : Async<'a> =
        let tcs = TaskCompletionSource<'a>()
        let state = ref 0
        async {
         if (Interlocked.CompareExchange(state, 1, 0) = 0) then
           Async.StartWithContinuations(
             a, 
             tcs.SetResult, 
             tcs.SetException, 
             (fun _ -> tcs.SetCanceled()))
         return! tcs.Task |> Async.AwaitTask }
        
    static member WithCancellation (ct:CancellationToken) (a:Async<'a>) : Async<'a> = async {
        let! ct2 = Async.CancellationToken
        use cts = CancellationTokenSource.CreateLinkedTokenSource (ct, ct2)
        let tcs = new TaskCompletionSource<'a>()
        use _reg = cts.Token.Register (fun () -> tcs.TrySetCanceled() |> ignore)
        let a = async {
          try
            let! a = a
            tcs.TrySetResult a |> ignore
          with ex ->
            tcs.TrySetException ex |> ignore }
        Async.Start (a, cts.Token)
        return! tcs.Task |> Async.AwaitTask }

    static member AwaitObservable(ev1:IObservable<'a>) =
      synchronize (fun f ->
        Async.FromContinuations((fun (cont,econt,ccont) -> 
          let rec callback = (fun value ->
            remover.Dispose()
            f cont value )
          and remover : IDisposable  = ev1.Subscribe(callback) 
          () )))
    
    static member AwaitObservable(ev1:IObservable<'a>, ev2:IObservable<'b>) = 
      synchronize (fun f ->
        Async.FromContinuations((fun (cont,econt,ccont) -> 
          let rec callback1 = (fun value ->
            remover1.Dispose()
            remover2.Dispose()
            f cont (Choice1Of2(value)) )
          and callback2 = (fun value ->
            remover1.Dispose()
            remover2.Dispose()
            f cont (Choice2Of2(value)) )
          and remover1 : IDisposable  = ev1.Subscribe(callback1) 
          and remover2 : IDisposable  = ev2.Subscribe(callback2) 
          () )))
  
    static member AwaitObservable(ev1:IObservable<'a>, ev2:IObservable<'b>, ev3:IObservable<'c>) = 
      synchronize (fun f ->
        Async.FromContinuations((fun (cont,econt,ccont) -> 
          let rec callback1 = (fun value ->
            remover1.Dispose()
            remover2.Dispose()
            remover3.Dispose()
            f cont (Choice1Of3(value)) )
          and callback2 = (fun value ->
            remover1.Dispose()
            remover2.Dispose()
            remover3.Dispose()
            f cont (Choice2Of3(value)) )
          and callback3 = (fun value ->
            remover1.Dispose()
            remover2.Dispose()
            remover3.Dispose()
            f cont (Choice3Of3(value)) )
          and remover1 : IDisposable  = ev1.Subscribe(callback1) 
          and remover2 : IDisposable  = ev2.Subscribe(callback2) 
          and remover3 : IDisposable  = ev3.Subscribe(callback3) 
          () )))
      
    static member asTask(async: Async<'T>, token: CancellationToken option) =
      let tcs = TaskCompletionSource<'T>()
      let token = defaultArg token Async.DefaultCancellationToken
      Async.StartWithContinuations(
        async,
        tcs.SetResult,
        tcs.SetException,
        tcs.SetException,
        token)
      tcs.Task
    static member asAsync(task: Task, token: CancellationToken option) =
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
  type AsyncInteropExtensions =
    [<Extension>]
    static member AsTask (async: Async<unit>) =
      Async.asTask (async, None) :> Task
  
    [<Extension>]
    static member AsTask (async: Async<unit>, token: CancellationToken) =
      Async.asTask (async, Some token) :> Task
  
    [<Extension>]
    static member AsTask (async: Async<'T>) =
      Async.asTask (async, None)
  
    [<Extension>]
    static member AsTask (async: Async<'T>, token: CancellationToken) =
      Async.asTask (async, Some token)
