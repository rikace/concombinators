module Combinators.TaskEx.TaskCombinators

open System
open System.Threading
open System.Threading.Tasks
open FSharp.Parallelx
open FSharp.Parallelx.ContextInsensitive

module TaskV2 =
  let fork (f : 'a -> Task<'a>) =
        fun x -> task {
        return! f x
    }
        
  let span (f : Task<'a>) = task {        
        return! f 
    }
  
  let retn value = value |> Task.FromResult

  let bind (f : 'a -> Task<'b>) (x : Task<'a>) = task {
      let! x = x
      return! f x
  }

  let apply f x =
    bind (fun f' ->
      bind (fun x' -> retn(f' x')) x) f
  
  let map f x = x |> bind (f >> retn)

  // ('a -> 'b -> 'c) -> Task<'a> -> Task<'b> -> Task<'c>
  let map2 f x y =
    (apply (apply (retn f) x) y)

  let map3 f x y z =
    apply (map2 f x y) z

    
    
  let orElse (fallBack : exn -> Task<'a>) (op : Task<'a>) = task {
        let! k = op.ContinueWith(fun (t : Task<'a>) ->
            if t.Status = TaskStatus.Faulted then
                fallBack(t.Exception)
            else Task.FromResult(t.Result))
        return! k
        }
  
  let bimap' (successed : 'a -> 'b) (faulted : exn -> 'b) (op : Task<'a>) = task {
    return!
        op
        |> map successed
        |> orElse (fun ex -> faulted ex |> Task.FromResult)
  }



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
    
   // recover
   let orElse (fallBack : unit -> Task<'a>) (op : Task<'a>) = task {
        let! k =
            op.ContinueWith(fun (t: Task<'a>) ->
                if t.Status = TaskStatus.Faulted then fallBack()
                else Task.FromResult(t.Result))
        return! k
    }
   
   let rec retry (retries : int) (delayMillis : int) (op : unit -> Task<_>) = task {
        match retries with
        | 0 -> return! op()
        | n -> return! op() |> orElse (fun () -> task {
            do! Task.Delay delayMillis
            return! retry (n - 1) delayMillis op
        })
   }
       

   let filter (source : Task<'a>, predicate : 'a -> bool) = task {
        // Create a continuation to run the predicate and return the source's result.
        // If the predicate returns false, cancel the returned Task.
        let cts = new CancellationTokenSource()
        return source.ContinueWith((fun (t : Task<_>) ->
                let result = t.Result;
                if predicate(result) then
                    cts.Cancel()
                    raise (exn "task cancelled")
                else result
            ), cts.Token, TaskContinuationOptions.NotOnCanceled, TaskScheduler.Default)
   }