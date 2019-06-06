module ConCombinators.AsyncChoice

open System
open System.Threading
open System.Threading.Tasks


    
module AsyncChoice =
  
  open System.Threading

  // Async.Choice method that takes several workflows and creates a workflow, which returns the first result that was computed.
  // After a workflow completes, remaining workflows are cancelled using the F# async cancellation mechanism.
  type Microsoft.FSharp.Control.Async with
    /// Takes several asynchronous workflows and returns 
    /// the result of the first workflow that successfuly completes
    static member Choice(workflows) = 
      Async.FromContinuations(fun (cont, _, _) ->
        let cts = new CancellationTokenSource()
        let completed = ref false
        let lockObj = new obj()
        let synchronized f = lock lockObj f
      
        /// Called when a result is available - the function uses locks
        /// to make sure that it calls the continuation only once
        let completeOnce res =
          let run =
            synchronized(fun () ->
              if completed.Value then false
              else completed := true; true)
          if run then cont res
      
        /// Workflow that will be started for each argument - run the 
        /// operation, cancel pending workflows and then return result
        let runWorkflow workflow = async {
          let! res = workflow
          cts.Cancel()
          completeOnce res }
      
        // Start all workflows using cancellation token
        for work in workflows do
          Async.Start(runWorkflow work, cts.Token) )
  
  /// Simple function that sleeps for some time 
  /// and then returns a specified value
  let delayReturn n s = async {
    // Run a couple of async operations - F# checks for 
    // cancellation automatically when starting them
    do! Async.Sleep(n) 
    do! Async.Sleep(1)  
    printfn "returning %s" s
    return s }

  // Run two 'delayReturn' workflows in parallel and return
  // the result of the first one. Note that this only prints
  // 'returning First!' (because the other workflow is cancelled)
  Async.Choice [ delayReturn 1000 "First!"; delayReturn 5000 "Second!" ]
  |> Async.RunSynchronously
  
  
  
type Async with
    static member Choice(tasks : Async<'T option> seq) : Async<'T option> = async {
        match Seq.toArray tasks with
        | [||] -> return None
        | [|t|] -> return! t
        | tasks ->

        let! t = Async.CancellationToken
        return! Async.FromContinuations <|
            fun (sc,ec,cc) ->
                let noneCount = ref 0
                let exnCount = ref 0
                let innerCts = CancellationTokenSource.CreateLinkedTokenSource t

                let scont (result : 'T option) =
                    match result with
                    | Some _ when Interlocked.Increment exnCount = 1 -> innerCts.Cancel() ; sc result
                    | None when Interlocked.Increment noneCount = tasks.Length -> sc None
                    | _ -> ()

                let econt (exn : exn) =
                    if Interlocked.Increment exnCount = 1 then 
                        innerCts.Cancel() ; ec exn

                let ccont (exn : OperationCanceledException) =
                    if Interlocked.Increment exnCount = 1 then
                        innerCts.Cancel(); cc exn

                for task in tasks do
                    ignore <| Task.Factory.StartNew(fun () -> Async.StartWithContinuations(task, scont, econt, ccont, innerCts.Token))
    }

// example 1

let delay interval result =
    async {
        do! Async.Sleep interval
        return! async {
            printfn "returning %A after %d ms." result interval
            return result }
    }

[ delay 100 None ; delay 1000 (Some 1) ; delay 500 (Some 2) ] |> Async.Choice |> Async.RunSynchronously
                    
// example 2

/// parallel existential combinator
let exists (f : 'T -> Async<bool>) (ts : seq<'T>) : Async<bool> =
    let wrapper t = async { let! r = f t in return if r then Some () else None }
                
    async {
        let! r = ts |> Seq.map wrapper |> Async.Choice
        return r.IsSome
    }

// #time
//[1..500] |> Seq.exists (fun i -> Threading.Thread.Sleep 10; i = 500)
//[1..500] |> exists (fun i -> async { let! _ = Async.Sleep 10 in return i = 500 }) |> Async.RunSynchronously