module Combinators.ParallelSIMD

open System
open System.Threading
open System.Threading.Tasks
open System.Collections
open System.Linq
open System.Collections.Concurrent
open System.Collections.Immutable
open FSharp.Parallelx.ContextInsensitive
open System.Collections.Generic
open System.Threading.Tasks
open FSharp.Parallelx.ContextInsensitive


//        var arr = Enumerable.Range(0, 1000).ToList();
//        var res = Reduce(arr, n => n, (a, b) => a + b);

let parallelReducer (data : 'a seq) (selector : 'a -> 'b) (reducer : 'b -> 'b -> 'b) (cts : CancellationTokenSource) =
    let partitioner = Partitioner.Create(data, EnumerablePartitionerOptions.NoBuffering)
    let mutable results = ImmutableArray<'b>.Empty
    
    let opt = ParallelOptions(TaskScheduler = TaskScheduler.Default, CancellationToken = cts.Token,  MaxDegreeOfParallelism = Environment.ProcessorCount)

    Parallel.ForEach (
        partitioner,
        opt,
        (fun ()-> ResizeArray<'b>()),
        (fun item loopState (local : ResizeArray<'b>) ->
            local.Add(selector(item))
            local),        
        (fun (final) -> ImmutableInterlocked.InterlockedExchange(&results, results.AddRange(final)) |> ignore)) |> ignore
    
    results.AsParallel().Aggregate(reducer)
    
let parallelFilterMap (data : 'a array) (filter : 'a -> bool) (selector : 'a -> 'b) (reducer : 'b -> 'b -> 'b) (cts : CancellationTokenSource) =
    let partitioner = Partitioner.Create(0, data.Length)
    let mutable results = ImmutableArray<'b>.Empty
    
    let opt = ParallelOptions(TaskScheduler = TaskScheduler.Default, CancellationToken = cts.Token,  MaxDegreeOfParallelism = Environment.ProcessorCount)

    Parallel.ForEach (
        partitioner,
        opt,
        (fun ()-> ResizeArray<'b>()),
        (fun (start, stop) loopState (local : ResizeArray<'b>) ->
            for j in [start..stop - 1] do
                if filter data.[j] then 
                    local.Add(selector(data.[j]))
            local),        
        (fun (final) -> ImmutableInterlocked.InterlockedExchange(&results, results.AddRange(final)) |> ignore)) |> ignore
    
    results.AsParallel().Aggregate(reducer)    

let executeInParallel (collection : 'a seq) (action : 'a -> Async<unit>) degreeOfParallelism =
    let queue = new ConcurrentQueue<'a>(collection)
    [0..degreeOfParallelism]
    |> Seq.map (fun t -> async {
        let mutable item = Unchecked.defaultof<'a>
        while queue.TryDequeue(&item) do
            do! action item    
    })
    |> Async.Parallel
            
let executeInParallelWithResult (collection : 'a seq) (action : 'a -> Async<'b>) degreeOfParallelism = async {
    let queue = new ConcurrentQueue<'a>(collection)
    let! data =
        [0..degreeOfParallelism]
        |> Seq.map (fun t -> async {
        let localResults = ResizeArray<'b>()
        let mutable item = Unchecked.defaultof<'a>
        while queue.TryDequeue(&item) do
            let! result = action item
            localResults.Add result
        return localResults
        })
        |> Async.Parallel
    return data |> Seq.concat |> Seq.toList
    }



let forEachAsync (source : 'a seq) dop (action : 'a -> Async<unit>) = 
    Partitioner.Create(source).GetPartitions(dop)
    |> Seq.map(fun partition -> async {
        use p = partition
        while p.MoveNext() do
            do! action p.Current
    })
    |> Async.Parallel
    
let forEachAsyncWithResult (source : 'a seq) dop (action : 'a -> Async<'b>) = async {
    let! data =
        Partitioner.Create(source).GetPartitions(dop)
        |> Seq.map(fun partition -> async {
            let localResults = ResizeArray<'b>()
            use p = partition
            while p.MoveNext() do
                let! result = action p.Current
                localResults.Add result
            return localResults
        })
        |> Async.Parallel
    return data |> Seq.concat |> Seq.toList
    }

let forEachTaskWithResult (source : 'a seq) dop (action : 'a -> Task<'b>) = task {
    let! data =
        Partitioner.Create(source).GetPartitions(dop)
        |> Seq.map(fun partition -> task {
            let localResults = ResizeArray<'b>()
            use p = partition
            while p.MoveNext() do
                let! result = action p.Current
                localResults.Add result
            return localResults
        })
        |> Task.WhenAll
       
    return data |> Seq.concat |> Seq.toList
    }


let addRange (bag : ConcurrentBag<_>) (items : seq<_>) =
    for item in items do bag.Add item
    bag
    
let execProjInParallel (ops : seq<'a>) (projection : 'a -> Task<'b>) degree = task {
    let queue = new ConcurrentQueue<_>(ops)
    let results = ConcurrentBag<_>()
    
    let tasks = [0..degree] |> Seq.map(fun _ -> task {
        let localResults = new ResizeArray<_>()
        let mutable item = Unchecked.defaultof<_>
        while queue.TryDequeue(&item) do
            let! result = projection item
            localResults.Add result           
        return addRange results localResults |> ignore
    })
    
    do! (Task.WhenAll(tasks) :> Task)
    return results :> IEnumerable<_>
}

let execActionInParallel (ops : seq<'a>) (action : 'a -> Task) degree = task {
    let queue = new ConcurrentQueue<_>(ops)
    
    let tasks = [0..degree] |> Seq.map(fun _ -> task {            
        let mutable item = Unchecked.defaultof<_>
        while queue.TryDequeue(&item) do
            do! action item
    })
    
    do! (Task.WhenAll(tasks) :> Task)
}

let forEachAsyncV2 (ops : seq<'a>) (action : 'a -> Task) degree = task {
    let partitions = Partitioner.Create(ops).GetPartitions(degree)
    
    let tasks =
        partitions
        |> Seq.map (fun partition -> task {
            
            use partition = partition
            while partition.MoveNext() do
                do! action partition.Current
            
            })
    do! (Task.WhenAll(tasks) :> Task)
    ignore()
}        


let forEachProjectionAsync (ops : seq<'a>) (projection : 'a -> Task<'b>) degree = task {
    let partitions = Partitioner.Create(ops).GetPartitions(degree)
    let results = ConcurrentBag<_>()
    
    let tasks =
        partitions
        |> Seq.map (fun partition -> task {
            let localResults = new ResizeArray<_>()
            use partition = partition
            
            while partition.MoveNext() do
                let! result = projection partition.Current
                localResults.Add result
            
            return addRange results localResults
            })
    do! (Task.WhenAll(tasks) :> Task)        
    return results :> IEnumerable<_>
}      