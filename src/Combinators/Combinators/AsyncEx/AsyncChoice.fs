module ConCombinators.AsyncChoice

open System
open System.Threading
open System.Threading.Tasks

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

#time
[1..500] |> Seq.exists (fun i -> Threading.Thread.Sleep 10; i = 500)
[1..500] |> exists (fun i -> async { let! _ = Async.Sleep 10 in return i = 500 }) |> Async.RunSynchronously