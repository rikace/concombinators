module Combinators.AgentEx.AgentPipeline

open System
open System.Threading
open System.Threading.Tasks
open Combinators.AgentEx.BlockingQueueAgent

module Exp =
    
    let queueLength = 2
    
    let loadedImages = new BlockingQueueAgent<_>(queueLength)
    let scaledImages = new BlockingQueueAgent<_>(queueLength)    
    let filteredImages = new BlockingQueueAgent<_>(queueLength)
    
    let fileNames : string list = []
    
    let loadImages = async {
        let clockOffset = Environment.TickCount
        let rec numbers n = seq { yield n; yield! numbers (n + 1) }
        for count, img in fileNames |> Seq.zip (numbers 0) do
            let info = loadImage img sourceDir count clockOffset
            do! loadedImages.AsyncAdd(info)
    }
    
    // CancellationTokenSource
    // let! _ = pipelineCompleted.Publish |> Async.AwaitObservable
    let scalePipelinedImages = async {
        while true do 
            let! info = loadedImages.AsyncGet()
            scaleImage info
            do! scaledImages.AsyncAdd(info) }
    
    let displayPipelinedImages = async {
        while true do
            let! info = filteredImages.AsyncGet()
            info.QueueCount1 <- loadedImages.Count
            info.QueueCount2 <- scaledImages.Count
            info.QueueCount3 <- filteredImages.Count
            displayImage info }
    
    Async.Start(loadImages, cts.Token)
    Async.Start(scalePipelinedImages, cts.Token)
    Async.Start(filterPipelinedImages, cts.Token)
    try Async.RunSynchronously(displayPipelinedImages, cancellationToken = cts.Token)
    with :? OperationCanceledException -> () F# Web Snippets   