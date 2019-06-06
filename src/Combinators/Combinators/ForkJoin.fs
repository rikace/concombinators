module Combinators.ForkJoin

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Threading.Tasks.Dataflow
open FSharp.Parallelx.ContextInsensitive


// Parallel Fork/Join using TPL DataFlow
let forkJoin(source : 'a seq, map : 'a -> Task<'b  seq>, aggregate : 'c -> 'b -> Task<'c>, initialState : 'c) = task {
    let blockOptions = new ExecutionDataflowBlockOptions(MaxDegreeOfParallelism = 8, BoundedCapacity = 20)
    let bufferOpt = new DataflowBlockOptions(BoundedCapacity = 20)    
 
    let inputBuffer = new BufferBlock<'a>(bufferOpt)
    let mapperBlock = new TransformManyBlock<'a, 'b>(map, blockOptions)
 
    let mutable state = initialState
    let reducerAgent = MailboxProcessor<'b>.Start(fun inbox ->
        let rec loop () = async {
            let! msg = inbox.Receive()
            let! newState = aggregate state msg |> Async.AwaitTask
            state <- newState
            return! loop ()
        }
        loop ())
    
    let linkOptions = new DataflowLinkOptions(PropagateCompletion = true)
    inputBuffer.LinkTo(mapperBlock, linkOptions) |> ignore
    
    let disposable = mapperBlock.AsObservable()
                         .Subscribe(fun item -> reducerAgent.Post item)
    for item in source do
        let! _ = inputBuffer.SendAsync item
        ()
    inputBuffer.Complete()
    
    let tcs = new TaskCompletionSource<'c>()
    inputBuffer.Completion.ContinueWith(fun task -> mapperBlock.Complete()) |> ignore
    
    do! mapperBlock.Completion.ContinueWith(fun task -> 
        (reducerAgent :> IDisposable).Dispose()
        tcs.SetResult(state))
    return! tcs.Task 
}
